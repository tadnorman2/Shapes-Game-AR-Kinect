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
    /// The flow: put a printed Shapes card on the table, and the game builds its arena around it,
    /// following the card as it's moved; each card brings up a different arena. Press Space (or
    /// A) to start a three-minute round, steering the ball with a stick or keys, or with a hand
    /// held over the board (Tab switches). Real things put on the board block the ball. Without a
    /// Kinect, a simulated table stands in, with number keys to put objects and cards on it.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        private enum State { Look, Ready, Playing, Results }
        private enum Steering { Stick, Hand }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<GameManager>() == null) new GameObject("Shapes Kinect").AddComponent<GameManager>();
        }

        private IDepthSource source;
        /// <summary>
        /// The Kinect, once opened. It stays open until the game stops, even while the simulator is
        /// in use, since closing and reopening the Kinect can hang Unity.
        /// </summary>
        private IDepthSource kinect;
        private SimulatedDepthSource simulator;
        private bool showColor = true;
        private string sourceNote;
        private float waitingForKinectSince = -1f;
        private long lastFrameNumber = -1;
        private float depthRate;
        private int framesThisSecond;
        private float rateTimer;

        private readonly CardTracker cardTracker = new CardTracker();
        private System.IO.StreamWriter cardRecording;
        private string cardRecordingPath;
        private int cardRecordingLeft;
        /// <summary>How many color pictures a card recording covers: about three seconds.</summary>
        private const int CardRecordingPictures = 90;
        private Board board;
        private BoardTracker boardTracker;

        private TableView tableView;
        private BackgroundView background;
        /// <summary>Holds what's drawn in the board's coordinates: the board, the ball and shapes, object rings.</summary>
        private Transform boardRoot;
        private BoardView boardView;
        private LineLayer objectLines;
        private ShapeVisuals visuals;
        private Camera viewCamera;
        private int drawnObstacleVersion = -1;

        private State state = State.Look;
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

        // What the game logs, to find out where time goes and where it stops if Unity hangs.
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        private double slowestTick;
        private double slowestTracking;
        private double slowestCard;
        /// <summary>How far the arena moved, at most, from one Unity frame to the next, in meters, since the last log line.</summary>
        private float cardWobble;
        private NVector3? lastCardOrigin;
        private double slowestView;
        private float slowestFrame;
        private float nextLog;
        private bool loggedDepth;
        private bool loggedColor;

        /// <summary>How often the game writes a line about how it's running to Unity's log.</summary>
        private const float LogInterval = 2f;

        /// <summary>Where the simulated hand is held: 30 cm above the board.</summary>
        private const float SimulatedHandHeight = 0.3f;

        /// <summary>
        /// How long to wait for the Kinect's first image before falling back to the simulator. The
        /// Kinect can take several seconds to start streaming after it's opened.
        /// </summary>
        private const float KinectTimeout = 15f;

        private void Start()
        {
            Application.targetFrameRate = 60;
            Debug.Log($"[Shapes] Starting in Unity {Application.unityVersion}.");
            Watchdog.Start();
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
            viewCamera.cullingMask = ~(1 << BackgroundView.Layer);

            if (FindFirstObjectByType<Light>() == null)
            {
                var light = new GameObject("Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(60f, -30f, 0f);
            }

            tableView = new GameObject("Kinect view").AddComponent<TableView>();
            background = new GameObject("Background").AddComponent<BackgroundView>();

            boardRoot = new GameObject("Board").transform;
            boardView = new GameObject("Board walls").AddComponent<BoardView>();
            boardView.transform.SetParent(boardRoot, false);
            boardView.Hide();
            objectLines = new GameObject("Objects").AddComponent<LineLayer>();
            objectLines.SetColor(new Color(1f, 0.3f, 0.3f));
            objectLines.transform.SetParent(boardRoot, false);
            visuals = new GameObject("Round").AddComponent<ShapeVisuals>();
            visuals.transform.SetParent(boardRoot, false);
        }

        private void UseKinect()
        {
#if KINECT_V2
            if (kinect == null)
            {
                Debug.Log("[Shapes] Opening the Kinect.");
                kinect = KinectDepthSource.TryOpen();
                Debug.Log(kinect == null ? "[Shapes] No Kinect runtime." : "[Shapes] Kinect opened, waiting for pictures.");
            }
            if (kinect != null)
            {
                SetSource(kinect, null);
                if (kinect.Latest == null) waitingForKinectSince = Time.unscaledTime;
                return;
            }
            UseSimulator("The Kinect runtime isn't installed, so a simulated table stands in.");
#else
            UseSimulator("Kinect support is off until the Kinect v2 Unity plugin is imported (see the README), so a simulated table stands in.");
#endif
        }

        private void UseSimulator(string why)
        {
            if (simulator == null) simulator = new SimulatedDepthSource();
            SetSource(simulator, why);
        }

        private void SetSource(IDepthSource newSource, string note)
        {
            source = newSource;
            sourceNote = note;
            waitingForKinectSince = -1f;
            lastFrameNumber = -1;
            Debug.Log($"[Shapes] Using {newSource.Name}.");
            // A different camera means a different view: look for the card afresh.
            cardTracker.Reset();
            LoseBoard();
        }

        private void OnDestroy()
        {
            cardRecording?.Dispose();
            Watchdog.Stop();
            kinect?.Dispose();
            simulator?.Dispose();
        }

        private void Update()
        {
            watch.Restart();
            Watchdog.Mark("reading the Kinect");
            source.Tick();
            slowestTick = System.Math.Max(slowestTick, watch.Elapsed.TotalMilliseconds);
            if (!loggedDepth && source.Latest != null)
            {
                loggedDepth = true;
                Debug.Log($"[Shapes] First depth picture: {source.Latest.Width} x {source.Latest.Height}.");
            }
            if (!loggedColor && source.ColorTexture != null)
            {
                loggedColor = true;
                Debug.Log($"[Shapes] First color picture: {source.ColorTexture.width} x {source.ColorTexture.height}.");
            }
            if (waitingForKinectSince >= 0f && source.Latest == null &&
                Time.unscaledTime - waitingForKinectSince > KinectTimeout)
            {
                UseSimulator("No picture arrived from the Kinect. Check it's plugged in and powered (its light is on), then press K to try again.");
            }
            if (Controls.SwitchSource)
            {
                if (source is SimulatedDepthSource) UseKinect();
                else UseSimulator("Switched to the simulated table. Press K to go back to the Kinect.");
            }

            var frame = source.Latest;
            HandleInput(frame);

            watch.Restart();
            Watchdog.Mark("looking for the card");
            if (cardTracker.Update(source.ColorGray, source.ColorCamera, frame, Time.unscaledTime))
            {
                slowestCard = System.Math.Max(slowestCard, watch.Elapsed.TotalMilliseconds);
                RecordCardPicture();
            }
            FollowCard();
            var cardOrigin = cardTracker.Current?.Frame.Origin;
            if (cardOrigin != null && lastCardOrigin != null) cardWobble = Mathf.Max(cardWobble, NVector3.Distance(cardOrigin.Value, lastCardOrigin.Value));
            lastCardOrigin = cardOrigin;

            var newFrame = frame != null && frame.Number != lastFrameNumber;
            if (newFrame)
            {
                lastFrameNumber = frame.Number;
                framesThisSecond++;
                watch.Restart();
                Watchdog.Mark("watching the board");
                boardTracker?.Update(frame, frame.Width > 300 ? 2 : 1);
                slowestTracking = System.Math.Max(slowestTracking, watch.Elapsed.TotalMilliseconds);
            }
            watch.Restart();
            Watchdog.Mark("updating the view");
            UpdateView(frame, newFrame);
            slowestView = System.Math.Max(slowestView, watch.Elapsed.TotalMilliseconds);
            Watchdog.Mark("game logic");
            LogProgress();
            rateTimer += Time.unscaledDeltaTime;
            if (rateTimer >= 1f)
            {
                depthRate = framesThisSecond / rateTimer;
                framesThisSecond = 0;
                rateTimer = 0f;
            }

            var obstacleVersion = boardTracker != null && boardTracker.IsReady ? boardTracker.Obstacles.Version : -1;
            if (obstacleVersion != drawnObstacleVersion)
            {
                drawnObstacleVersion = obstacleVersion;
                objectLines.Begin();
                if (obstacleVersion >= 0) boardTracker.Obstacles.TraceOutlines(objectLines.Add);
                objectLines.Rebuild();
            }

            var hand = SteeringHand();
            if (state == State.Playing && round != null)
            {
                var move = Vector2.zero;
                if (steering == Steering.Stick)
                {
                    move = StickOnBoard(Controls.Move);
                }
                else if (hand != null)
                {
                    var steer = HandSteering.Steer(round.PlayerX, round.PlayerZ, new NVector3(hand.Value.x, hand.Value.y, hand.Value.z));
                    move = new Vector2(steer.X, steer.Y);
                }
                foreach (var e in round.Update(Time.deltaTime, move.x, move.y)) OnRoundEvent(e);
            }
            if (round != null) visuals.Show(round, steering == Steering.Hand ? hand : null);
            // Until the next Update, Unity itself has the main thread: rendering, the editor, other scripts.
            Watchdog.Mark("Unity, between game updates (rendering, editor)");
        }

        private void HandleInput(DepthFrame frame)
        {
            // In a built game; the editor ignores it.
            if (Controls.Quit) Application.Quit();

            if (source is SimulatedDepthSource simulated)
            {
                var key = Controls.NumberKey;
                if (key >= 0 && key < SimulatedDepthSource.Props.Length) simulated.ToggleProp(key);
                var card = key - SimulatedDepthSource.Props.Length;
                if (card >= 0 && card < SimulatedDepthSource.Cards.Length) simulated.ToggleCard(card);
                simulated.SetHand(steering == Steering.Hand ? PointerHand() : null);
            }

            if (Controls.SwitchView)
            {
                showColor = !showColor;
                Say(showColor ? "Color view." : "Depth view: board grey, objects orange, hands purple.");
            }

            if (Controls.Screenshot && frame != null)
            {
                var saved = SaveCapture(frame, screenshot: true);
                if (saved != null) Say($"Saved a screenshot and what the Kinect saw in {saved}.", 6f);
            }

            if (Controls.RecordCard && cardRecording == null) StartCardRecording();

            if (Controls.SwitchControl)
            {
                steering = steering == Steering.Stick ? Steering.Hand : Steering.Stick;
                Say(steering == Steering.Hand
                    ? "Hand control: hold a hand 15-60 cm above the board and the ball follows it."
                    : "Stick control: left stick, WASD or arrow keys.");
            }

            if (Controls.Confirm && (state == State.Ready || state == State.Results)) StartRound();
        }

        /// <summary>
        /// Builds the arena around the card on the table, or moves it to follow the card. A new
        /// card brings up its own arena, except during a round. Without a card for a while, the
        /// arena goes, except during a round, when it stays where the card last was.
        /// </summary>
        private void FollowCard()
        {
            var card = cardTracker.Current;
            if (card == null)
            {
                if (state != State.Playing && board != null) LoseBoard();
                return;
            }
            var layout = ArenaLayout.ForCard(card.Id);
            if (board == null || (board.Layout != layout && state != State.Playing))
            {
                board = new Board(layout, card.Frame);
                boardTracker = new BoardTracker(board);
                drawnObstacleVersion = -1;
                EndRound();
                state = State.Ready;
                Say($"{layout.Name}! Press Space (or A) to play.");
                Debug.Log($"[Shapes] Found card {card.Id + 1} ({layout.Name}), {card.Size * 100:0.0} cm, {(card.FromDepth ? "placed by depth" : "placed by the picture alone")}.");
                return;
            }
            board.MoveTo(card.Frame);
        }

        /// <summary>
        /// Starts writing, for the next few seconds of color pictures, how the card was seen in
        /// each: whether it was, its corners in the picture, where that picture alone put it, and
        /// where the smoothed arena is. Shows where a wobble comes from.
        /// </summary>
        private void StartCardRecording()
        {
            try
            {
                var folder = System.IO.Path.Combine(Application.persistentDataPath, "Captures");
                System.IO.Directory.CreateDirectory(folder);
                cardRecordingPath = System.IO.Path.Combine(folder, "card-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                cardRecording = new System.IO.StreamWriter(cardRecordingPath);
                cardRecording.WriteLine("time,seen,card,c0x,c0y,c1x,c1y,c2x,c2y,c3x,c3y,depth,measured x,measured y,measured z,measured up x,measured up y,measured up z,measured forward x,measured forward y,measured forward z,arena x,arena y,arena z,arena up x,arena up y,arena up z,arena forward x,arena forward y,arena forward z");
                cardRecordingLeft = CardRecordingPictures;
                Say("Recording the card for a few seconds: keep it still.", 4f);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Shapes] Couldn't record the card: {e.Message}");
                cardRecording = null;
            }
        }

        private void RecordCardPicture()
        {
            if (cardRecording == null) return;
            var seen = cardTracker.IsVisible;
            var s = seen ? cardTracker.LastSighting : null;
            var m = seen ? cardTracker.LastMeasured : null;
            var a = cardTracker.Current;
            string V(NVector3 v) => System.FormattableString.Invariant($"{v.X:0.00000},{v.Y:0.00000},{v.Z:0.00000}");
            var line = System.FormattableString.Invariant($"{Time.unscaledTime:0.000},{(seen ? 1 : 0)},{(a != null ? a.Id + 1 : 0)},");
            line += s == null ? ",,,,,,,," : string.Join(",", s.Corners.Select(c => System.FormattableString.Invariant($"{c.X:0.00},{c.Y:0.00}"))) + ",";
            line += m == null ? ",,,,,,,,,," : $"{(m.FromDepth ? 1 : 0)},{V(m.Frame.Origin)},{V(m.Frame.Up)},{V(m.Frame.Forward)},";
            line += a == null ? ",,,,,,,," : $"{V(a.Frame.Origin)},{V(a.Frame.Up)},{V(a.Frame.Forward)}";
            cardRecording.WriteLine(line);
            if (--cardRecordingLeft > 0) return;
            cardRecording.Dispose();
            cardRecording = null;
            Debug.Log($"[Shapes] Recorded the card to {cardRecordingPath}.");
            Say($"Recorded the card to {cardRecordingPath}.", 8f);
        }

        private void LoseBoard()
        {
            EndRound();
            board = null;
            boardTracker = null;
            state = State.Look;
            drawnObstacleVersion = -1;
            objectLines?.Clear();
            boardView?.Hide();
        }

        /// <summary>
        /// Saves the depth picture and the color picture to the Captures folder, keeping the last
        /// few, to look into how the game saw things. With <paramref name="screenshot"/>, also
        /// what's on screen. A text file beside them has the color camera and the arena as the
        /// game sees them, so the overlay can be redrawn on the photo. Returns the folder, or null
        /// if saving failed.
        /// </summary>
        private string SaveCapture(DepthFrame frame, bool screenshot = false)
        {
            try
            {
                var folder = System.IO.Path.Combine(Application.persistentDataPath, "Captures");
                System.IO.Directory.CreateDirectory(folder);
                var name = System.IO.Path.Combine(folder, "table-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                using (var file = System.IO.File.Create(name + ".depth")) DepthCapture.Save(frame, file);
                if (source.ColorTexture != null) System.IO.File.WriteAllBytes(name + ".jpg", UprightJpg(source.ColorTexture));
                System.IO.File.WriteAllText(name + ".txt", Describe());
                if (screenshot) ScreenCapture.CaptureScreenshot(name + "-screen.png");
                var old = System.IO.Directory.GetFiles(folder, "table-*.depth").OrderByDescending(f => f).Skip(10);
                foreach (var depthFile in old)
                {
                    System.IO.File.Delete(depthFile);
                    System.IO.File.Delete(System.IO.Path.ChangeExtension(depthFile, ".jpg"));
                    System.IO.File.Delete(System.IO.Path.ChangeExtension(depthFile, ".txt"));
                    System.IO.File.Delete(System.IO.Path.ChangeExtension(depthFile, null) + "-screen.png");
                }
                Debug.Log($"[Shapes] Saved what the Kinect saw to {name}.depth.");
                return folder;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Shapes] Couldn't save what the Kinect saw: {e.Message}");
                return null;
            }
        }

        /// <summary>The color camera and the board, in Kinect camera space, as plain text.</summary>
        private string Describe()
        {
            var text = new System.Text.StringBuilder();
            var c = source.ColorCamera;
            text.AppendLine(c == null
                ? "color camera: none"
                : System.FormattableString.Invariant($"color camera: position {c.Position} right {c.Right} down {c.Down} forward {c.Forward} fx {c.Fx} fy {c.Fy} cx {c.Cx} cy {c.Cy} skew {c.Skew} size {c.Width}x{c.Height}"));
            var card = cardTracker.Current;
            if (card != null)
            {
                text.AppendLine(System.FormattableString.Invariant($"card: {card.Id + 1}, {card.Size} m, {(cardTracker.IsVisible ? "seen" : "held")}, {(card.FromDepth ? "by depth" : "by picture")}"));
                var corners = cardTracker.LastSighting?.Corners;
                if (corners != null) text.AppendLine(System.FormattableString.Invariant($"card corners in the color picture: {corners[0]} {corners[1]} {corners[2]} {corners[3]}"));
            }
            if (board != null)
            {
                var f = board.Frame;
                text.AppendLine(System.FormattableString.Invariant($"board: {board.Layout.Name}, {board.Width} x {board.Length} m, origin {f.Origin} up {f.Up} right {f.Right} forward {f.Forward}"));
                // Each side of the board as two points in camera space.
                board.TraceEdge((x1, z1, x2, z2) =>
                    text.AppendLine(System.FormattableString.Invariant($"board edge {f.ToCamera(new NVector3(x1, 0f, z1))} {f.ToCamera(new NVector3(x2, 0f, z2))}")));
                // Where it thinks real things are on the board, in its own coordinates, and the ball.
                if (boardTracker != null && boardTracker.IsReady)
                {
                    var grid = boardTracker.Obstacles.Grid;
                    var blocked = new List<string>();
                    for (var cell = 0; cell < grid.CellCount; cell++)
                    {
                        if (boardTracker.Obstacles.IsBlocked(cell)) blocked.Add(System.FormattableString.Invariant($"({grid.CenterX(cell):0.00},{grid.CenterZ(cell):0.00})"));
                    }
                    text.AppendLine($"objects on the board ({blocked.Count} cells of {grid.CellSize * 100:0} cm): {string.Join(" ", blocked)}");
                }
                if (round != null) text.AppendLine(System.FormattableString.Invariant($"ball at ({round.PlayerX:0.000},{round.PlayerZ:0.000})"));
            }
            else
            {
                text.AppendLine("board: none");
            }
            text.AppendLine($"screen {Screen.width}x{Screen.height}, view {viewCamera.rect}");
            return text.ToString();
        }

        /// <summary>
        /// The color picture as a JPEG, the right way up: it's stored with its top row first,
        /// where textures expect the bottom one.
        /// </summary>
        private static byte[] UprightJpg(Texture2D picture)
        {
            var pixels = picture.GetPixels32();
            int width = picture.width, height = picture.height;
            var flipped = new Color32[pixels.Length];
            for (var y = 0; y < height; y++) System.Array.Copy(pixels, y * width, flipped, (height - 1 - y) * width, width);
            var upright = new Texture2D(width, height, TextureFormat.RGBA32, false);
            upright.SetPixels32(flipped);
            var jpg = upright.EncodeToJPG(85);
            Destroy(upright);
            return jpg;
        }

        /// <summary>
        /// Sets up what the screen shows this frame. Normally that's the Kinect's color picture,
        /// with the game's camera placed exactly where the color camera is, so the board, ball and
        /// shapes line up with the real surface. In the depth view (C), or without a color camera,
        /// it's a 3D surface of what the depth camera sees, from the depth camera's position.
        ///
        /// The game's world is the depth camera's view, unmirrored; the board is placed in it
        /// where its card is.
        /// </summary>
        private void UpdateView(DepthFrame frame, bool newFrame)
        {
            if (board != null)
            {
                SetFrame(boardRoot, board.Frame);
                boardView.Show(board.Layout);
            }

            var picture = source.ColorTexture;
            var colorCamera = source.ColorCamera;
            if (showColor && picture != null && colorCamera != null)
            {
                var camera = colorCamera.Transformed(ToView, ToView);
                viewCamera.transform.SetPositionAndRotation(ToUnity(camera.Position),
                    Quaternion.LookRotation(ToUnity(camera.Forward), -ToUnity(camera.Down)));
                // The game's world isn't mirrored; if the picture is, relative to it, flip it to match.
                var flip = Vector3.Dot(viewCamera.transform.right, ToUnity(camera.Right)) < 0f;
                viewCamera.rect = background.Show(picture, flip);
                // Exactly the color camera's lens, including its center being off the picture's middle.
                var m = camera.ProjectionMatrix(viewCamera.nearClipPlane, viewCamera.farClipPlane, flip);
                var projection = new Matrix4x4();
                for (var i = 0; i < 16; i++) projection[i / 4, i % 4] = m[i];
                viewCamera.projectionMatrix = projection;
                viewCamera.clearFlags = CameraClearFlags.Depth;
                tableView.gameObject.SetActive(false);
                return;
            }

            background.Hide();
            viewCamera.ResetProjectionMatrix();
            viewCamera.rect = new Rect(0f, 0f, 1f, 1f);
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            // The Kinect's depth camera sees about 60 degrees top to bottom.
            viewCamera.fieldOfView = 60f;
            viewCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            tableView.gameObject.SetActive(true);
            if (newFrame) tableView.Show(frame, board, boardTracker != null && boardTracker.IsReady ? boardTracker.Obstacles : null);
        }

        /// <summary>
        /// Converts a camera-space point or direction to the game world: the camera's own view,
        /// with X flipped, since camera space has X toward the sensor's left.
        /// </summary>
        private static NVector3 ToView(NVector3 v) => new NVector3(-v.X, v.Y, v.Z);

        private static Vector3 ToUnity(NVector3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>Places <paramref name="t"/> so its own coordinates are <paramref name="frame"/>'s.</summary>
        private static void SetFrame(Transform t, TableFrame frame)
        {
            t.SetPositionAndRotation(ToUnity(ToView(frame.Origin)),
                Quaternion.LookRotation(ToUnity(ToView(frame.Forward)), ToUnity(ToView(frame.Up))));
        }

        /// <summary>Writes a line to Unity's log every couple of seconds about how the game is running.</summary>
        private void LogProgress()
        {
            slowestFrame = Mathf.Max(slowestFrame, Time.unscaledDeltaTime * 1000f);
            if (Time.realtimeSinceStartup < nextLog) return;
            nextLog = Time.realtimeSinceStartup + LogInterval;
            Debug.Log($"[Shapes] {Time.realtimeSinceStartup:0.0} s · {source.Name} · {state} · {depthRate:0} depth pictures/s · " +
                $"slowest in the last {LogInterval:0} s: frame {slowestFrame:0} ms, Kinect {slowestTick:0} ms, " +
                $"card {slowestCard:0} ms (moved up to {cardWobble * 1000:0.0} mm a frame), objects {slowestTracking:0} ms, view {slowestView:0} ms · memory {System.GC.GetTotalMemory(false) / 1048576} MB");
            slowestFrame = 0f;
            slowestTick = 0;
            slowestTracking = 0;
            slowestCard = 0;
            cardWobble = 0f;
            slowestView = 0;
        }

        private void StartRound()
        {
            if (boardTracker == null || !boardTracker.IsReady)
            {
                Say("Just a moment: the game is looking at the board. Keep it clear.");
                return;
            }
            Debug.Log("[Shapes] Starting a round.");
            visuals.Hide();
            round = new GameRound(boardTracker.PlayArea());
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

        /// <summary>The stick as a move on the board, relative to the view: up is away from the camera, right is to the screen's right.</summary>
        private Vector2 StickOnBoard(Vector2 stick) => StickOn(board.Frame, stick);

        /// <summary>
        /// Turns the stick into a move in <paramref name="frame"/>'s coordinates relative to the
        /// view: up is away from the camera, right is to the screen's right.
        /// </summary>
        private Vector2 StickOn(TableFrame frame, Vector2 stick)
        {
            var forward = ToTableDirection(frame, viewCamera.transform.forward);
            var flat = new Vector2(forward.X, forward.Z);
            if (flat.sqrMagnitude < 1e-6f)
            {
                var up = ToTableDirection(frame, viewCamera.transform.up);
                flat = new Vector2(up.X, up.Z);
            }
            flat.Normalize();
            var right = new Vector2(flat.y, -flat.x);
            return right * stick.x + flat * stick.y;
        }

        /// <summary>A direction in the game world, in <paramref name="frame"/>'s coordinates.</summary>
        private static NVector3 ToTableDirection(TableFrame frame, Vector3 world) =>
            frame.DirectionToTable(ToView(new NVector3(world.x, world.y, world.z)));

        /// <summary>
        /// The hand nearest the ball that's held in steering position over the board, in the
        /// board's coordinates, with its height being how far it is above the board.
        /// </summary>
        private Vector3? SteeringHand()
        {
            if (board == null) return null;
            hands.Clear();
            source.GetHands(hands);
            Vector3? best = null;
            var bestDistance = float.MaxValue;
            foreach (var cameraHand in hands)
            {
                var t = board.Frame.ToTable(cameraHand);
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

        /// <summary>For the simulator: where the mouse points, as a hand held over the board, in camera space.</summary>
        private NVector3? PointerHand()
        {
            var pointer = Controls.Pointer;
            if (board == null || pointer == null) return null;
            var ray = viewCamera.ScreenPointToRay(pointer.Value);
            var origin = boardRoot.InverseTransformPoint(ray.origin);
            var direction = boardRoot.InverseTransformDirection(ray.direction);
            var plane = new UnityEngine.Plane(Vector3.up, new Vector3(0f, SimulatedHandHeight, 0f));
            if (!plane.Raycast(new Ray(origin, direction), out var distance)) return null;
            var p = origin + direction * distance;
            if (!board.Contains(p.x, p.z)) return null;
            return board.Frame.ToCamera(new NVector3(p.x, p.y, p.z));
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
            Watchdog.Mark("drawing on-screen text");
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
                    GUI.Label(new Rect(0, height - unit * 6f, width, unit * 2f), "Hold a hand 15-60 cm above the board to steer", medium);
                }
            }
            else
            {
                string title = null;
                string body = null;
                switch (state)
                {
                    case State.Look:
                        title = "Put a Shapes card down";
                        body = "Lay a printed Shapes card flat on the table where the Kinect can see it. Its arena appears around it.";
                        break;
                    case State.Ready:
                        title = boardTracker.IsReady ? board.Layout.Name : "Keep the board clear...";
                        body = "Press Space (or A) to start a round. Move the card and the arena moves with it. Put real things on the board and the ball has to go around them.";
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

            var status = $"{source.Name} · {depthRate:0} depth frames/s · {1000f * Time.smoothDeltaTime:0} ms per frame · " +
                $"{(steering == Steering.Stick ? "Stick" : "Hand")} control (Tab) · {(showColor ? "Color" : "Depth")} view (C)";
            status += cardTracker.Current == null ? " · No card seen" : $" · Card {cardTracker.Current.Id + 1}{(cardTracker.IsVisible ? "" : " (hidden)")}";
            if (state != State.Playing) status += " · P saves a screenshot · J records the card · K switches Kinect/simulator";
            if (source is SimulatedDepthSource simulated)
            {
                var props = SimulatedDepthSource.Props.Select((p, i) => $"{i + 1}: {p.name}{(simulated.IsPropOn(i) ? " (on)" : "")}");
                var cards = SimulatedDepthSource.Cards.Select((c, i) => $"{i + 1 + SimulatedDepthSource.Props.Length}: {c.name}{(simulated.CardOn == i ? " (on)" : "")}");
                status += "\nSimulated table. Keys " + string.Join(", ", props.Concat(cards)) + (steering == Steering.Hand ? " · mouse moves the hand" : "");
            }
            if (sourceNote != null) status += "\n" + sourceNote;
            GUI.Label(new Rect(unit * 0.5f, height - unit * 4.5f, width - unit, unit * 4.2f), status, small);
            Watchdog.Mark("Unity, between game updates (rendering, editor)");
        }
    }
}
