# Shapes Game AR (Kinect)

A tabletop take on the Shapes game for PC, using a Kinect v2 as a depth camera. The Kinect
watches a real table from its edge; the game finds the table, and you roll a ball around on it to
collect shapes. Anything real you put on the table, like a cup, a book or a box, becomes an
obstacle the ball has to go around, live.

It grew out of [Shapes-Game-AR](https://github.com/tadnorman2/Shapes-Game-AR), an Android ARCore
prototype. Phone depth couldn't find table edges or objects reliably; the Kinect measures depth
directly, so this version can.

## How a round plays

1. Point the Kinect across the table, clear the table, and press **Space** (or **A** on a
   gamepad). The game finds the table and outlines it in blue.
2. Press **Space** to start a three-minute round.
3. Steer the ball with the **left stick, WASD or arrow keys**, or press **Tab** (gamepad **X**)
   for **hand control**: hold a hand 15-60 cm above the table and the ball rolls toward the spot
   under it.
4. For the first 15 seconds every shape is worth **+10**. Then the game calls out one shape at a
   time ("Collect: ▲ Triangle"), and the called shape pulses. Only it scores +10; any other costs
   5. A new shape is called every 20 seconds.
5. Put things on the table at any time: they get a red ring around their base, block the ball,
   and shapes under them disappear. Only things touching the table count (1.5-15 cm up), so your
   hand and arm above it don't get in the way.

**R** (gamepad **Y**) finds the table again, e.g. after moving the Kinect. **K** switches between
the Kinect and the simulated table. **C** switches between the color view (the Kinect's color
camera painted onto what its depth camera sees) and the depth view (table grey, objects orange,
hands purple), which is handy for seeing what the game counts as an object.

## Setting up

You need Windows, a Kinect v2 with its PC adapter, and:

1. **Kinect for Windows SDK 2.0**, from Microsoft (you have this if `Kinect-Test-1` runs).
2. **Unity 2022.3.22f1** through [Unity Hub](https://unity.com/download) (**Installs → Install
   Editor → Archive** for older versions). Other 2022.3 versions should work too; Hub offers to
   open the project with one you have.
3. **Microsoft's Kinect v2 Unity plugin**: download *Kinect for Windows Unity Pro packages*
   (`KinectForWindows_UnityPro_2.0.1410.zip`) from Microsoft's Kinect for Windows downloads.

Then:

1. Clone this repository (GitHub Desktop works well) and, in Unity Hub, choose **Add → Add project
   from disk** and pick the folder. The first open takes a few minutes while Unity sets up.
2. In Unity, choose **Assets → Import Package → Custom Package…** and import
   `Kinect.2.0.1410.19000.unitypackage` from the zip. Import everything it lists.
3. The project notices the plugin and turns Kinect support on by itself; the Console says
   "found the Kinect v2 plugin".
4. Press **Play**. The game builds its own scene, so there's nothing else to open.

Without the plugin, or without a Kinect plugged in, the game runs on a **simulated table**
instead, so it can be tried anywhere. Number keys **1-3** put a cup, a book and a box on it, and
in hand control the mouse is the hand.

### Placing the Kinect

- At one edge of the table, tilted down across it, so the whole tabletop is in view.
- At least 50 cm from the nearest part of the table (the Kinect can't see closer), and ideally
  within 2 m of the far edge.
- The game screen shows the Kinect's view live before you press Space, which helps aim it.

## How it's built

- **`Assets/Scripts/Core`**: everything that isn't drawing, in plain C# with no Unity dependency:
  - `TableSurface` and `PlaneFit` find the table: RANSAC for flat surfaces whose
    camera-facing side points up (so walls are skipped), the highest sizable one (so the floor
    is skipped), then the biggest connected piece of it on a 2 cm grid.
  - `ObstacleMap` keeps the live map of things on the table; `TableTracker` ties it together.
  - `GameRound` holds the rules, ported from the Android prototype; `HandSteering` does hand
    control.
  - `SimulatedTable` renders Kinect-like depth of a table scene, for tests and for playing
    without a Kinect.
- **`Assets/Scripts/Kinect`**: where depth comes from: the real Kinect (`KinectDepthSource`,
  compiled once the plugin is imported) or the simulator.
- **`Assets/Scripts/Game`**: the Unity side: the live 3D view of what the Kinect sees, the
  outlines, the ball and shapes, controls, and the on-screen text. `GameManager` runs it all.
- **`Assets/Editor/KinectSetup.cs`** turns the `KINECT_V2` scripting symbol on when the plugin is
  present.

The core is covered by tests in `Tests/Core.Tests`, which run on every push (see the **Core
tests** workflow) with plain .NET, no Unity or Kinect needed. To run them locally:
`dotnet test Tests/Core.Tests`.

## Tuning

- Game rules (round length, ball speed, spawning, points): `RoundRules` in
  `Assets/Scripts/Core/GameRound.cs`.
- What counts as an object (heights, how quickly it appears and clears): the constants at the top
  of `Assets/Scripts/Core/ObstacleMap.cs`.
- Hand control (heights, dead zone): `Assets/Scripts/Core/HandSteering.cs`.
