# Shapes Game AR (Kinect)

A tabletop take on the Shapes game for PC, using a Kinect v2. Lay a printed Shapes card on the
table, like the Nintendo 3DS's AR cards, and the game builds its arena around it; roll a ball around
the arena to collect shapes. Move the card and the arena moves with it. Each card brings up a
different arena, and anything real you put on the arena, like a cup, a book or a box, becomes an
obstacle the ball has to go around, live.

It grew out of [Shapes-Game-AR](https://github.com/tadnorman2/Shapes-Game-AR), an Android ARCore
prototype. Phone depth couldn't find table edges or objects reliably; the Kinect measures depth
directly, so this version can.

## How a round plays

1. Print the cards: [`Cards/ShapesCards.pdf`](Cards/ShapesCards.pdf), one per page. Print at
   **actual size** (100%, not "fit to page") on matte paper or card.
2. Lay a card flat on the table where the Kinect can see it, with its white border showing. Its
   arena appears around it within a moment: 60 x 40 cm with low glowing walls, its far side toward
   the card's top (the label is at the bottom).
   - **Card 1, Open field**: an open arena. **Card 2, Pillars**: four pillars to steer round.
     **Card 3, Zigzag**: two walls to weave between.
   - Slide or turn the card and the arena follows. A hand passing over it doesn't lose it.
3. Keep the arena clear for a moment while the game looks at it, then press **Space** (or **A**
   on a gamepad) to start a three-minute round. The ball starts on the card.
4. Steer the ball with the **left stick, WASD or arrow keys**, or press **Tab** (gamepad **X**)
   for **hand control**: hold a hand 15-60 cm above the board and the ball rolls toward the spot
   under it.
5. For the first 15 seconds every shape is worth **+10**. Then the game calls out one shape at a
   time ("Collect: ▲ Triangle"), and the called shape pulses. Only it scores +10; any other costs
   5. A new shape is called every 20 seconds.
6. Put things on the board: they get a red ring around their base, block the ball, and shapes
   under them disappear. Only things touching the board count (1.5-15 cm up), so your hand and
   arm above it don't get in the way.

During a round the arena keeps following the card; if the card can't be seen, it stays where the
card last was. **K** switches between the Kinect and the simulated table. The screen shows the Kinect's color camera like a
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
instead, so it can be tried anywhere. Number keys **1-3** put a cup, a book and a box on it,
**4-6** swap which card is on it (or take it off), and in hand control the mouse is the hand.

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

### Placing the Kinect and the card

There's no set way to place the Kinect: anywhere its color camera sees the card clearly works.
Things to know:

- The card is read from the color picture, so it needs to look big enough: from about 0.45 m to
  1.2 m away when the Kinect looks across the table, further when it looks down more steeply.
- Light matters more than for depth: a lamp over the table helps in a dim room. Avoid glare on
  glossy paper.
- The depth camera can't see closer than 50 cm. Where it sees the card, it sets exactly how the
  card lies; nearer than that, or where there's no depth, the game goes by the picture alone,
  which is a little less steady.
- Real objects are found with the depth camera, so they count from about 50 cm out.

Press **P** (or **F12**) at any time to save what the Kinect sees (a `.depth` file and a `.jpg`
photo) and a screenshot to a `Captures` folder, keeping the last ten. A `.txt` file beside them
records where the game thinks the color camera, the card and the arena are, so the overlay can
be checked against the photo. **J** records three seconds of how the card is seen, picture by
picture, to a `card-....csv` file in the same folder: where its corners were found, where each
picture alone put it, and where the arena was, which shows where any wobble comes from. On Windows the folder is
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\<project folder name>\Captures`, and the game
shows it on screen.

## How it's built

- **`Assets/Scripts/Core`**: everything that isn't drawing, in plain C# with no Unity dependency:
  - `CardDetector` finds the cards in a grey version of the color picture (`GrayImage`): dark
    blobs with four-cornered outlines are read cell by cell against the patterns in `CardCodes`,
    which survive two misread cells and a mirrored camera. `CardLocator` works out where a card
    lies from where the color camera's lines of sight through its corners meet the plane the
    depth camera measures under it, and `CardTracker` follows it from picture to picture.
  - `Board` is the arena built around a card, laid out by `ArenaLayout`; `BoardTracker` learns
    how the surface under it looks whenever it settles, then keeps `ObstacleMap`, the live map of
    things that rise from it.
  - `GameRound` holds the rules, ported from the Android prototype; `HandSteering` does hand
    control.
  - `SimulatedTable` renders Kinect-like depth of a table scene, and a grey color picture of it
    with a card on it, for tests and for playing without a Kinect.
- **`Assets/Scripts/Kinect`**: where depth comes from: the real Kinect (`KinectDepthSource`,
  compiled once the plugin is imported) or the simulator.
- **`Assets/Scripts/Game`**: the Unity side: the live 3D view of what the Kinect sees, the
  outlines, the ball and shapes, controls, and the on-screen text. `GameManager` runs it all.
- **`Cards/make_cards.py`** makes the printable cards from the patterns and arena names in the
  code (`python Cards/make_cards.py`, with Pillow installed).
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
- The arenas (sizes, walls): `ArenaLayout.All` in `Assets/Scripts/Core/ArenaLayout.cs`. Adding
  one there and running `Cards/make_cards.py` gives it a card.
