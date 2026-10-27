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
the Kinect and the simulated table. The screen shows the Kinect's color camera like a
normal video, with the ball, shapes and outlines drawn on top, lined up with the real table: the
game works out exactly where the color camera is and how its lens sees from the Kinect's own
calibration. **C** switches to the depth view (a 3D surface of what the depth camera sees: table
grey, objects orange, hands purple), which is handy for seeing what the game counts as an object.

The game writes a line to Unity's log every two seconds saying how it's running (lines start
with `[Shapes]`), which helps track down slowdowns. In the editor the log is the Console; the full
file is `%LOCALAPPDATA%\Unity\Editor\Editor.log`.

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

### If the Kinect keeps restarting

If the Kinect's light keeps going off and on (and the log shows `Kinect disconnected` warnings),
the Kinect is dropping off USB. The game never closes the Kinect while it runs, so this is the PC's
USB connection or power. Each drop also makes the Unity editor rescan USB devices, which can freeze
it. Things to try, in order:

1. Run **Kinect Configuration Verifier** (in *SDK Browser v2.0*, installed with the Kinect SDK). It
   checks the USB controller and bandwidth. Leave it running for a minute to see whether the
   sensor stays connected.
2. Plug the Kinect adapter into a **USB 3.0 port on the back of the PC**, straight into the
   motherboard: no hub, no front-panel port, no extension cable. Nothing else heavy (webcams,
   other cameras, external drives) should share that controller.
3. Stop Windows from powering USB down: **Control Panel → Power Options → Change plan settings →
   Change advanced power settings → USB settings → USB selective suspend setting: Disabled**. In
   **Device Manager**, open each *USB Root Hub* and *USB eXtensible Host Controller*, and on the
   **Power Management** tab untick *Allow the computer to turn off this device to save power*.
4. Check the adapter's power brick is in the wall and every plug along the cable is pushed fully
   in.
5. The Kinect v2 is fussy about USB 3.0 controllers: Intel and Renesas work; some AMD/Ryzen and
   ASMedia ones don't. If none of the above helps, a PCIe USB 3.0 card with a Renesas (NEC) chip
   is the usual fix.

### If the editor freezes a few seconds into Play

With a Kinect streaming, the Unity editor can get stuck in its own USB device scanning: the game
stops, and the log fills with `Scanning for USB devices : 2000ms`. A built game doesn't scan, so
play it as a Windows program instead: choose **Shapes Kinect → Build and Run** from Unity's menu
bar. It builds the game into the `Builds` folder and starts it (**Escape** quits). Next time you
can start `Builds/ShapesKinect/ShapesKinect.exe` directly, until the code changes.

The project also turns off Unity Remote (**Edit → Project Settings → Editor → Unity Remote →
Device: None**), which scans USB for phones.

### Placing the Kinect

- At one edge of the table, tilted down across it, so the whole tabletop is in view.
- At least 50 cm from the nearest part of the table (the Kinect can't see closer), and ideally
  within 2 m of the far edge.
- The game screen shows the Kinect's view live before you press Space, which helps aim it.
- High enough to look down on the table: 50-80 cm above it, tilted 30-60 degrees down, works
  best. From just above the table, looking across it, the Kinect sees only a thin strip of the
  tabletop, and the game says so when it finds the table.
- Anywhere from looking almost straight across the table to looking straight down at it works.
  The table is taken to be the highest level surface in view that's at least 30 x 30 cm, so
  keep higher things (shelves, a bed, a chair seat) out of the picture or below the table.

Each time you press Space to find the table, the game saves what the Kinect saw (a `.depth` file
and a `.jpg` photo) to a `Captures` folder, keeping the last ten, and writes what it found to the
log. On Windows the folder is
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\<project folder name>\Captures`; the game
shows it on screen when it can't find the table. Those files are what's needed to see why the
table wasn't found. Press **P** (or **F12**) at any time to save them along with a screenshot of
the game; a `.txt` file beside them records where the game thinks the color camera and the table
are, so the overlay can be checked against the photo.

## How it's built

- **`Assets/Scripts/Core`**: everything that isn't drawing, in plain C# with no Unity dependency:
  - `TableFinder` and `TableSurface` find the table: they work out which way is up from the
    direction most flat patches in the picture face (helped by the Kinect's tilt sensor), sort
    those patches by height, and take the highest level surface big enough to play on (so the
    floor is skipped, and walls never count), then the biggest connected piece of it on a 2 cm
    grid.
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
