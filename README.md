# Shapes Game AR (Kinect)

A tabletop take on the Shapes game for PC, using a Kinect v2 as a depth camera. Click a table, a
desk or the floor in the Kinect's view, and the game puts its own board down on it; roll a ball
around the board to collect shapes. Anything real you put on the board, like a cup, a book or a
box, becomes an obstacle the ball has to go around, live.

It grew out of [Shapes-Game-AR](https://github.com/tadnorman2/Shapes-Game-AR), an Android ARCore
prototype. Phone depth couldn't find table edges or objects reliably; the Kinect measures depth
directly, so this version can.

## How a round plays

1. Point the Kinect at a table or other flat surface. Move the mouse over it: the flat surface
   under the mouse is outlined in blue. **Click** it.
2. The game's board (60 x 40 cm, with low glowing walls) appears on the surface, slid and turned
   to sit on it near where you clicked, and made smaller if the surface is too small. Adjust it:
   **WASD, arrows or the stick** slide it, **Q/E** (or the shoulder buttons) turn it, **+/-** (or
   the d-pad) resize it. It turns orange if it hangs off the surface. Click elsewhere to start
   over somewhere else.
3. Press **Space** (or **A** on a gamepad) to put the board down, keep it clear for a moment while
   the game looks at it, then press **Space** again to start a three-minute round.
4. Steer the ball with the **left stick, WASD or arrow keys**, or press **Tab** (gamepad **X**)
   for **hand control**: hold a hand 15-60 cm above the board and the ball rolls toward the spot
   under it.
5. For the first 15 seconds every shape is worth **+10**. Then the game calls out one shape at a
   time ("Collect: ▲ Triangle"), and the called shape pulses. Only it scores +10; any other costs
   5. A new shape is called every 20 seconds.
6. Put things on the board: they get a red ring around their base, block the ball, and shapes
   under them disappear. Only things touching the board count (1.5-15 cm up), so your hand and
   arm above it don't get in the way.

**R** (gamepad **Y**) goes back to picking a surface. **K** switches between the
Kinect and the simulated table. The screen shows the Kinect's color camera like a
normal video, with the ball, shapes and outlines drawn on top, lined up with the real table: the
game works out exactly where the color camera is and how its lens sees from the Kinect's own
calibration. **C** switches to the depth view (a 3D surface of what the depth camera sees: board
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

There's no set way to place it: anywhere it sees a good piece of flat surface works, from looking
almost straight across a table to looking straight down. Things to know:

- It can't see closer than 50 cm, and its depth gets rougher past 2 m.
- Only level surfaces can be picked; walls and the sides of things can't.
- The board lies on the plane of the surface right where you clicked. From low down, the Kinect
  reads the far part of a table as rising, so it only counts the nearer, flat-looking part as the
  surface; the board may come out smaller there. Looking down more steeply gives more room and
  steadier object detection.

Press **P** (or **F12**) at any time to save what the Kinect sees (a `.depth` file and a `.jpg`
photo) and a screenshot to a `Captures` folder, keeping the last ten. A `.txt` file beside them
records where the game thinks the color camera, the surface and the board are, so the overlay can be checked
against the photo. On Windows the folder is
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\<project folder name>\Captures`, and the game
shows it on screen.

## How it's built

- **`Assets/Scripts/Core`**: everything that isn't drawing, in plain C# with no Unity dependency:
  - `SurfacePicker` finds the flat surface at a clicked point: the plane of the depth right
    around it, then everything joined to it across the depth picture on that plane, without
    jumps in depth.
  - `Board` is the game's own arena on that surface, fitted on it and moved and turned by the
    player; `BoardTracker` learns how the surface under it looks when it's put down, then keeps
    `ObstacleMap`, the live map of things that rise from it.
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
