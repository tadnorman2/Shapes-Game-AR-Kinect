using System.Collections.Generic;
using System.Linq;
using ShapesKinect.Core;
using ShapesKinect.Kinect;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace ShapesKinect.Game
{
    /// <summary>
    /// Runs the whole game. Builds its own scene when Play is pressed, so the project needs no
    /// scene files: open it in Unity, press Play.
    ///
    /// The flow: point the Kinect at a clear table and press Space (or A) to find it; then press
    /// Space to start a three-minute round, steering the ball with a stick or keys, or with a hand
    /// held over the table (Tab switches). Real objects put on the table block the ball. Without
    /// a Kinect, a simulated table stands in, with number keys to put objects on it.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        private enum State { FindTable, Ready, Playing, Results }
        private enum Steering { Stick, Hand }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<GameManager>() == null) new GameObject("Shapes Kinect").AddComponent<GameManager>();
        }

        private IDepthSource source;
        private string sourceNote;
        private float waitingForKinectSince = -1f;
        private long lastFrameNumber = -1;
        private float depthRate;
        private int framesThisSecond;
        private float rateTimer;

        private readonly TableTracker tracker = new TableTracker();
        private TableView tableView;
        private LineLayer edgeLines;
        private LineLayer objectLines;
        private ShapeVisuals visuals;
        private Camera viewCamera;
        private int drawnObstacleVersion = -1;

        private State state = State.FindTable;
        private Steering steering = Steering.Stick;
        private GameRound round;
        private string message;
        private float messageUntil;
        private string popupText;
        private Color popupColor;
        private float popupStart = -10f;
        private float targetPulseStart = -10f;
        private int lastScore;
        private bool newBest;

        private readonly List<NVector3> hands = new List<NVector3>();

        /// <summary>Where the simulated hand is held, in table coordinates: 30 cm above the table.</summary>
        private const float SimulatedHandHeight = 0.3f;

        /// <summary>How long to wait for the Kinect's first image before falling back to the simulator.</summary>
        private const float KinectTimeout = 5f;

        private void Start()
        {
            Application.targetFrameRate = 60;
            BuildScene();
            UseKinect();
        }

        private void BuildScene()
        {
            viewCamera = Camera.main;
            if (viewCamera == null)
            {
                viewCamera = new GameObject("Camera").AddComponent<Camera>();
                viewCamera.tag = "MainCamera";
            }
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = new Color(0.07f, 0.08f, 0.12f);
            viewCamera.nearClipPlane = 0.05f;
            viewCamera.farClipPlane = 20f;
            // The Kinect's depth camera sees about 60 degrees top to bottom.
            viewCamera.fieldOfView = 60f;
            ViewFromKinect();

            if (FindFirstObjectByType<Light>() == null)
            {
                var light = new GameObject("Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(60f, -30f, 0f);
            }

            tableView = new GameObject("Kinect view").AddComponent<TableView>();
            edgeLines = new GameObject("Table edge").AddComponent<LineLayer>();
            edgeLines.SetColor(new Color(0.4f, 0.9f, 1f));
            objectLines = new GameObject("Objects").AddComponent<LineLayer>();
            objectLines.SetColor(new Color(1f, 0.3f, 0.3f));
            visuals = new GameObject("Round").AddComponent<ShapeVisuals>();
        }

        private void UseKinect()
        {
#if KINECT_V2
            var kinect = KinectDepthSource.TryOpen();
            if (kinect != null)
            {
                SetSource(kinect, null);
                waitingForKinectSince = Time.unscaledTime;
                return;
            }
            UseSimulator("The Kinect runtime isn't installed, so a simulated table stands in.");
#else
            UseSimulator("Kinect support is off until the Kinect v2 Unity plugin is imported (see the README), so a simulated table stands in.");
#endif
        }

        private void UseSimulator(string why)
        {
            SetSource(new SimulatedDepthSource(), why);
        }

        private void SetSource(IDepthSource newSource, string note)
        {
            source?.Dispose();
            source = newSource;
            sourceNote = note;
            waitingForKinectSince = -1f;
            lastFrameNumber = -1;
            ViewFromKinect();
            // A different camera means a different view of the table: find it again.
            tracker.Reset();
            EndRound();
            state = State.FindTable;
            drawnObstacleVersion = -1;
            edgeLines.Clear();
            objectLines.Clear();
        }

        private void OnDestroy()
        {
            source?.Dispose();
        }

        private void Update()
        {
            source.Tick();
            if (waitingForKinectSince >= 0f && source.Latest == null &&
                Time.unscaledTime - waitingForKinectSince > KinectTimeout)
            {
                UseSimulator("No Kinect picture arrived. Check it's plugged in and powered, then press K to try again.");
            }
            if (Controls.SwitchSource)
            {
                if (source is SimulatedDepthSource) UseKinect();
                else UseSimulator("Switched to the simulated table. Press K to go back to the Kinect.");
            }

            HandleInput();

            var frame = source.Latest;
            var newFrame = frame != null && frame.Number != lastFrameNumber;
            if (newFrame)
            {
                lastFrameNumber = frame.Number;
                framesThisSecond++;
                if (tracker.IsCalibrated) tracker.Update(frame, frame.Width > 300 ? 2 : 1);
                tableView.Show(frame, tracker);
            }
            rateTimer += Time.unscaledDeltaTime;
            if (rateTimer >= 1f)
            {
                depthRate = framesThisSecond / rateTimer;
                framesThisSecond = 0;
                rateTimer = 0f;
            }

            if (tracker.IsCalibrated && tracker.Obstacles.Version != drawnObstacleVersion)
            {
                drawnObstacleVersion = tracker.Obstacles.Version;
                objectLines.Begin();
                tracker.Obstacles.TraceOutlines(objectLines.Add);
                objectLines.Rebuild();
            }

            var hand = SteeringHand();
            if (state == State.Playing && round != null)
            {
                var move = Vector2.zero;
                if (steering == Steering.Stick)
                {
                    move = StickOnTable(Controls.Move);
                }
                else if (hand != null)
                {
                    var steer = HandSteering.Steer(round.PlayerX, round.PlayerZ, new NVector3(hand.Value.x, hand.Value.y, hand.Value.z));
                    move = new Vector2(steer.X, steer.Y);
                }
                foreach (var e in round.Update(Time.deltaTime, move.x, move.y)) OnRoundEvent(e);
            }
            if (round != null) visuals.Show(round, steering == Steering.Hand ? hand : null);
        }

        private void HandleInput()
        {
            if (source is SimulatedDepthSource simulated)
            {
                var key = Controls.NumberKey;
                if (key >= 0 && key < SimulatedDepthSource.Props.Length) simulated.ToggleProp(key);
                simulated.SetHand(steering == Steering.Hand ? PointerHand() : null);
            }

            if (Controls.SwitchControl)
            {
                steering = steering == Steering.Stick ? Steering.Hand : Steering.Stick;
                Say(steering == Steering.Hand
                    ? "Hand control: hold a hand 15-60 cm above the table and the ball follows it."
                    : "Stick control: left stick, WASD or arrow keys.");
            }

            if (Controls.FindTableAgain && state != State.Playing)
            {
                EndRound();
                state = State.FindTable;
                Say("Clear the table, then press Space (or A) to find it again.");
                return;
            }

            if (!Controls.Confirm) return;
            switch (state)
            {
                case State.FindTable:
                    FindTable();
                    break;
                case State.Ready:
                case State.Results:
                    StartRound();
                    break;
            }
        }

        private void FindTable()
        {
            var frame = source.Latest;
            if (frame == null)
            {
                Say("Waiting for the first picture from the Kinect.");
                return;
            }
            if (!tracker.Calibrate(frame))
            {
                Say("Couldn't find a table. Make sure most of it is in view, it's clear, and the Kinect is at least 50 cm from it.");
                return;
            }
            edgeLines.Begin();
            tracker.Surface.TraceEdge(edgeLines.Add);
            edgeLines.Rebuild();
            drawnObstacleVersion = -1;
            PlaceCamera();
            state = State.Ready;
            Say($"Found the table: {tracker.Surface.Area:0.00} square meters.");
        }

        /// <summary>
        /// Before the table is found, the view shows depth as the Kinect sees it, from the Kinect's
        /// own position: put the game's camera there.
        /// </summary>
        private void ViewFromKinect()
        {
            viewCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        /// <summary>Puts the game's camera where the Kinect is, so the screen shows the table as the Kinect sees it.</summary>
        private void PlaceCamera()
        {
            var frame = tracker.Surface.Frame;
            var position = frame.CameraPosition;
            var look = frame.CameraLook;
            var up = frame.CameraUp;
            viewCamera.transform.position = new Vector3(position.X, position.Y, position.Z);
            viewCamera.transform.rotation = Quaternion.LookRotation(new Vector3(look.X, look.Y, look.Z), new Vector3(up.X, up.Y, up.Z));
        }

        private void StartRound()
        {
            round = new GameRound(tracker.PlayArea());
            state = State.Playing;
            newBest = false;
        }

        private void EndRound()
        {
            round = null;
            visuals?.Hide();
        }

        private void OnRoundEvent(RoundEvent e)
        {
            switch (e)
            {
                case RoundEvent.Collected collected:
                    popupText = collected.Points > 0 ? $"+{collected.Points}" : collected.Points.ToString();
                    popupColor = collected.Points > 0 ? new Color(0.42f, 0.94f, 0.54f) : new Color(1f, 0.35f, 0.35f);
                    popupStart = Time.time;
                    break;
                case RoundEvent.TargetChanged _:
                    targetPulseStart = Time.time;
                    break;
                case RoundEvent.Finished finished:
                    lastScore = finished.Score;
                    var best = PlayerPrefs.GetInt("best", 0);
                    newBest = finished.Score > best;
                    if (newBest)
                    {
                        PlayerPrefs.SetInt("best", finished.Score);
                        PlayerPrefs.Save();
                    }
                    state = State.Results;
                    break;
            }
        }

        /// <summary>
        /// Turns the stick into a direction on the table relative to the view: up is away from the
        /// camera, right is to the screen's right.
        /// </summary>
        private Vector2 StickOnTable(Vector2 stick)
        {
            var forward = viewCamera.transform.forward;
            var flat = new Vector2(forward.x, forward.z);
            if (flat.sqrMagnitude < 1e-6f) flat = new Vector2(viewCamera.transform.up.x, viewCamera.transform.up.z);
            flat.Normalize();
            var right = new Vector2(flat.y, -flat.x);
            return right * stick.x + flat * stick.y;
        }

        /// <summary>The hand nearest the ball that's held in steering position over the table, in table coordinates.</summary>
        private Vector3? SteeringHand()
        {
            if (!tracker.IsCalibrated) return null;
            hands.Clear();
            source.GetHands(hands);
            Vector3? best = null;
            var bestDistance = float.MaxValue;
            foreach (var cameraHand in hands)
            {
                var t = tracker.ToTable(cameraHand);
                if (!HandSteering.IsSteering(t)) continue;
                var distance = round == null ? 0f : new Vector2(t.X - round.PlayerX, t.Z - round.PlayerZ).magnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = new Vector3(t.X, t.Y, t.Z);
                }
            }
            return best;
        }

        /// <summary>For the simulator: where the mouse points, as a hand held over the table, in camera space.</summary>
        private NVector3? PointerHand()
        {
            var pointer = Controls.Pointer;
            if (!tracker.IsCalibrated || pointer == null) return null;
            var ray = viewCamera.ScreenPointToRay(pointer.Value);
            var plane = new UnityEngine.Plane(Vector3.up, new Vector3(0f, SimulatedHandHeight, 0f));
            if (!plane.Raycast(ray, out var distance)) return null;
            var p = ray.GetPoint(distance);
            if (!tracker.Surface.Contains(p.x, p.z)) return null;
            return tracker.Surface.Frame.ToCamera(new NVector3(p.x, p.y, p.z));
        }

        private void Say(string text, float seconds = 4f)
        {
            message = text;
            messageUntil = Time.unscaledTime + seconds;
        }

        // ---- On-screen text ----

        private GUIStyle big;
        private GUIStyle medium;
        private GUIStyle small;
        private GUIStyle panel;

        private void OnGUI()
        {
            var unit = Screen.height / 40f;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
                medium = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerLeft, wordWrap = true };
                panel = new GUIStyle(GUI.skin.box);
            }
            big.fontSize = Mathf.RoundToInt(unit * 1.6f);
            medium.fontSize = Mathf.RoundToInt(unit * 0.9f);
            small.fontSize = Mathf.RoundToInt(unit * 0.6f);
            big.normal.textColor = Color.white;
            medium.normal.textColor = Color.white;
            small.normal.textColor = new Color(0.75f, 0.8f, 0.9f);

            var width = Screen.width;
            var height = Screen.height;

            if (state == State.Playing && round != null)
            {
                var seconds = Mathf.CeilToInt(round.TimeLeft);
                medium.alignment = TextAnchor.MiddleLeft;
                GUI.Label(new Rect(unit, unit * 0.5f, width / 3f, unit * 2f), $"Score {round.Score}", medium);
                medium.alignment = TextAnchor.MiddleRight;
                GUI.Label(new Rect(width - width / 3f - unit, unit * 0.5f, width / 3f, unit * 2f), $"{seconds / 60}:{seconds % 60:00}", medium);
                medium.alignment = TextAnchor.MiddleCenter;
                var target = round.Target;
                var pulse = 1f + 0.4f * Mathf.Clamp01(1f - (Time.time - targetPulseStart) / 0.4f);
                big.fontSize = Mathf.RoundToInt(unit * 1.6f * pulse);
                big.normal.textColor = target == null ? Color.white : ShapeVisuals.ToColor(target.Value.Color());
                GUI.Label(new Rect(0, unit * 2f, width, unit * 3f),
                    target == null ? "Collect any shape!" : $"Collect: {target.Value.Symbol()} {target.Value.Label()}", big);
                big.normal.textColor = Color.white;

                var age = Time.time - popupStart;
                if (age < 0.7f)
                {
                    var color = popupColor;
                    color.a = 1f - age / 0.7f;
                    big.normal.textColor = color;
                    GUI.Label(new Rect(0, height / 2f - unit * 4f - age * unit * 3f, width, unit * 3f), popupText, big);
                    big.normal.textColor = Color.white;
                }
                if (steering == Steering.Hand && SteeringHand() == null)
                {
                    GUI.Label(new Rect(0, height - unit * 6f, width, unit * 2f), "Hold a hand 15-60 cm above the table to steer", medium);
                }
            }
            else
            {
                string title = null;
                string body = null;
                switch (state)
                {
                    case State.FindTable:
                        title = "Find the table";
                        body = "Point the Kinect across the table from its edge, clear the table, and press Space (or A on a gamepad).";
                        break;
                    case State.Ready:
                        title = "Ready";
                        body = "Press Space (or A) to start a round. Put real things on the table and the ball has to go around them.";
                        break;
                    case State.Results:
                        title = "Time's up!";
                        body = $"Score: {lastScore}   {(newBest ? "New best!" : $"Best: {PlayerPrefs.GetInt("best", 0)}")}\nPress Space (or A) to play again.";
                        break;
                }
                GUI.Box(new Rect(width * 0.2f, height * 0.12f, width * 0.6f, unit * 7f), GUIContent.none, panel);
                GUI.Label(new Rect(width * 0.2f, height * 0.12f + unit * 0.5f, width * 0.6f, unit * 2.5f), title, big);
                GUI.Label(new Rect(width * 0.22f, height * 0.12f + unit * 3f, width * 0.56f, unit * 3.5f), body, medium);
            }

            if (message != null && Time.unscaledTime < messageUntil)
            {
                GUI.Box(new Rect(width * 0.15f, height * 0.72f, width * 0.7f, unit * 2.6f), GUIContent.none, panel);
                GUI.Label(new Rect(width * 0.16f, height * 0.72f, width * 0.68f, unit * 2.6f), message, medium);
            }

            var status = $"{source.Name} · {depthRate:0} depth frames/s · {(steering == Steering.Stick ? "Stick" : "Hand")} control (Tab)";
            if (state != State.Playing) status += " · R finds the table again · K switches Kinect/simulator";
            if (source is SimulatedDepthSource simulated)
            {
                var props = SimulatedDepthSource.Props.Select((p, i) => $"{i + 1}: {p.name}{(simulated.IsPropOn(i) ? " (on)" : "")}");
                status += "\nSimulated table. Keys " + string.Join(", ", props) + (steering == Steering.Hand ? " · mouse moves the hand" : "");
            }
            if (sourceNote != null) status += "\n" + sourceNote;
            GUI.Label(new Rect(unit * 0.5f, height - unit * 4.5f, width - unit, unit * 4.2f), status, small);
        }
    }
}
