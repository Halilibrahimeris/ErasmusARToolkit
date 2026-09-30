# Erasmus AR Toolkit

A small, non-commercial toolkit for building simple AR apps in Unity without writing code.
Everything is done from one window: **Tools > Erasmus AR Toolkit > Open Toolkit**.

- **Android:** real AR with AR Foundation + Google ARCore (plane detection, tap-to-place, image tracking).
- **PC / Editor:** *Desktop mode* — the same scene runs with mouse and keyboard on a virtual ground grid,
  with the PC webcam as background. No extra packages are needed for this.

### Which camera is used

| Situation | Camera | Floor | Image targets |
|---|---|---|---|
| Android phone with ARCore | The AR camera (tracked by ARCore) | Real floors / tables detected | Recognised by the camera |
| Android phone without ARCore | The phone's back camera as background | Virtual floor | Shown on the virtual floor (simulated) |
| PC (Windows build and Editor) | The first webcam as background | Virtual floor | **Recognised in the webcam picture** (OpenCV); with the webcam off they lie on the virtual floor |

The **Camera** button in the HUD switches to the next camera and, after the last one, turns the camera
off. Turn the camera off completely with *Use Device Camera* in the Content tab, or pick a specific
webcam with *Preferred Camera Name* (the Console lists the camera names at start-up).
On PC the webcam is only a background: real surface or image tracking needs AR, which Unity provides on
phones only.

Features:
- **Surface detection:** detected floors and tables get a grid texture, and a ring in the middle of the
  view shows where an object will land. When the phone detects the same floor more than once, the
  overlapping planes are drawn as one surface and objects placed on it sit at one shared height.
  Planes also have colliders, ready for physics.
- **Room scan:** like the space setup of a VR headset. Tap **Scan Room**, walk slowly and look at the
  floor, every table top and the walls, then tap **Finish Scan**. The scan finds the **floor** first and
  holds it at one height, then the **surfaces** above it (tables, seats, a podium), what **stands in the
  way** (chairs, bags, cupboards: translucent boxes) and the **walls** of the room (panels that fade
  toward the top). The grid has straight edges, lies along the walls of the room and is never drawn
  under a chair or behind a wall. Objects can only be placed on free scanned surfaces. See
  [Room scan](#room-scan). Phones without ARCore Depth scan with tracking points (slower, less exact).
- **Real-world occlusion:** on phones with ARCore Depth, real objects (furniture, people, hands) hide
  virtual objects and surface grids behind them. Turn it off with *Real World Occlusion* in the
  Behaviour settings; phones without depth support simply show everything.
- **Tap-to-place:** placed objects are fixed with AR anchors, so they stay put when you look away and back.
- **Image targets:** when the camera sees a configured image, its 3D content appears on top with a
  frame around the image.
- **Edit panel:** tap any object to see its info and live position / rotation / scale, and to scale
  (slider), rotate, raise / lower, reset or delete it. Pinch, twist and drag work too.
- Info cards (TextMeshPro), an on-screen HUD (UI Toolkit: UXML + USS), screenshots, one-click builds and export.

---

## Requirements

| Item | Version |
|---|---|
| Unity | 6000.0 or newer (tested on 6000.6) |
| Render pipeline | Universal Render Pipeline (URP) |
| Unity Hub modules | Android Build Support (OpenJDK, SDK & NDK), Windows Build Support |
| Packages (installed by the toolkit) | AR Foundation, Google ARCore XR Plugin, Input System |
| Phone | An [ARCore supported device](https://developers.google.com/ar/devices), Android 10+ |

---

## Install

1. **Assets > Import Package > Custom Package...** and pick `ErasmusARToolkit_vX.Y.Z.unitypackage`.
2. A **Setup** window opens. Click **Install Required Packages** and wait for Unity to compile.
3. Open **Tools > Erasmus AR Toolkit > Open Toolkit**, go to the **Setup** tab and click
   **Run All Setup Steps**. Every row should turn green.

`Run All Setup Steps` creates your config, imports the TextMeshPro essentials, enables ARCore for
Android, adds the AR camera features to your URP renderers and applies Unity's XR Project
Validation fixes. For the room scan it also puts the scan materials and shaders into the theme (so
they are part of the build), makes the URP renderers suit AR occlusion, makes ARCore Depth optional
(phones without depth can install the app) and brings the open scenes up to date.

### Updating from version 1.0

1. Import the new package over the old one. `Assets/ARToolkitContent` is not touched.
2. Open **Tools > Erasmus AR Toolkit > Open Toolkit > Setup** and click **Run All Setup Steps**.
   The new rows (*Room scan materials and shaders in the theme*, *Open scenes use the current AR rig
   settings*, *URP renderers on Android suit AR occlusion*, *ARCore Depth is optional*) turn green.
3. Open each of your AR scenes once and click **Upgrade Scenes** in the Setup tab (or create the
   scene again from the Scene tab). Scenes that were not upgraded are put right when the app starts.
4. Build again. A build that lacks the scan shaders is stopped with a message that says so.

---

## Quick start

1. **Content tab:** click **Add Sample Content** (Box, Globe, EU Flag and a sample image target),
   or drag your own prefabs / images onto the drop area.
2. **Scene tab:** click **Create New AR Scene**. The scene is saved to `Assets/ARToolkitContent/Scenes`
   and added to Build Settings.
3. Press **Play**. The Editor runs in Desktop mode, so you can test everything with the mouse.
4. **Build tab:** choose *Android (APK)* or *Windows (EXE)* and click **Build**
   (or **Build And Run On Phone** with a phone connected over USB).

A ready-made demo is in `Samples/Scenes/ARToolkitDemo.unity`.

---

## Controls

| Action | Phone (AR) | PC (Desktop mode) |
|---|---|---|
| Choose object | Tap a button in the bottom bar | Click a button in the bottom bar |
| Place | Tap a highlighted surface | Left-click the grid |
| Move | Drag the object | Left-drag the object |
| Rotate | Twist with two fingers | Q / E |
| Scale | Pinch | Shift + mouse wheel |
| Edit (info, scale, rotate, raise, reset, delete) | Tap the object → edit panel | Click the object → edit panel |
| Close the edit panel | Tap empty space or **Close** | Click empty space or **Close** |
| Delete | Edit panel → **Delete** | Edit panel → **Delete**, or the **Delete** key |
| Switch camera / off | **Camera** button (phones without ARCore) | **Camera** button |
| Scan the room (accurate surfaces) | **Scan Room** → walk around → **Finish Scan** (**Reset** starts over) | — |
| Say which surface is the floor | While scanning: aim the middle of the screen at the floor → **Set Floor** | — |
| Hide surfaces | **Hide Planes** | **Hide Grid** |
| Show / hide info | Tap the object | Click the object |
| Camera | Move the phone | Right-drag orbit, middle-drag pan, wheel zoom, WASD move, F reset |

---

## Room scan

ARCore detects planes, and its planes are rough: they reach past table edges, lie a few centimeters
too high or too low and know nothing of chairs. The room scan measures the room with the phone's depth
images instead and builds a model of it, as the room setup of a VR headset does.

**How to scan**

1. Tap **Scan Room** and point the phone at the floor near your feet, and take a step or two. The
   status line asks for the floor until it was found, which takes a few seconds.
2. Walk slowly through the room. Look at the floor next to and behind every table, at the table tops,
   at the chairs from two or three sides and at the walls up to head height.
3. The status line counts what was found (area, surfaces, obstacles, walls). When nothing new was found
   for 20 seconds the app says so. Tap **Finish Scan**.

**What you see**

| Shown as | What it is | Can objects be placed there? |
|---|---|---|
| Grid on the floor | The floor. One height for the whole room, straight edges, along the walls | Yes |
| Grid above the floor | Table tops, a podium, shelves you look at from above | Yes |
| Translucent box with edges | Something that stands in the room: a chair, a bag, a cupboard | No: *"Something stands there"* |
| Panel that fades toward the top | A wall of the room | No: *"That is behind a wall"* |

**Hide Planes** hides the grid, the boxes and the walls. Real things still hide virtual objects that
stand behind them, and objects still stand on the scanned surfaces.

**What the scan takes care of**

- *Shiny floors.* A shiny floor mirrors the room, and depth then measures points below the floor. Once
  the floor is known, nothing below it is drawn or used, and the mirror images make no holes.
- *One floor height.* The floor is drawn at a height that holds still. It follows the measured height
  slowly, and does not sink below the plane ARCore detected.
- *ARCore corrects its map.* The scan hangs from AR anchors, so it moves with the room when ARCore
  corrects itself. After tracking was lost, or the app was paused, the scan is checked against what the
  camera sees: a scan that is a few centimeters off is moved, one that no longer matches asks to be
  scanned again.
- *Objects on scanned surfaces.* An object belongs to the surface it was placed on. When the scan finds
  out that the table is 2 cm higher, the object goes with it, and **Lower** never pushes it into the
  surface. The edit panel shows the height above the surface.
- *Things that move.* A chair that is taken away frees the floor under it after a few seconds of looking
  at the spot. A person who walks through the view leaves nothing behind.
- *The phone.* The scan takes 8 camera frames a second, 4 when the phone is busy or warm. A phone that
  gets hot finishes the scan by itself and says so.

**Settings** (Content tab, *Room Scan*)

| Setting | Meaning |
|---|---|
| Grid Cell Size | Distance between the grid lines, 0.10 to 1.00 m (0.25 m by default) |
| Detect Walls | Looks for the walls of the room. ARCore then detects vertical planes, too, which costs a little speed. Objects are never placed on walls |
| Show Scan Diagnostics | Shows what the scan measures on the HUD. The same line is written to the log every 2 seconds, starting with `Scan:` |

---

## Content

All content lives in one asset: `Assets/ARToolkitContent/ARToolkitConfig.asset`, edited in the
**Content** tab.

- **Placeable objects:** a prefab, a display name, an optional icon and a scale. Put the prefab's
  pivot at the bottom so it sits on the surface. Sizes are in meters (a 15 cm cube = scale 0.15).
- **Image targets:** a PNG/JPG image, its **printed width in meters** and a prefab and/or info label.
  Use **Check Image Quality** — Google recommends a score of 75 or more. Detailed, high-contrast
  images without repeating patterns work best. Print the image in the exact width you entered.
  The image library is rebuilt automatically before every build. The content can be scaled, rotated
  and raised from the edit panel but always stays on its image. An image target without a prefab
  shows its info card as the content.
- **Info labels:** a title and body text shown above an object (TextMeshPro). Tick *Show On Start*
  to show them immediately; otherwise users tap the object.
- **Behaviour settings:** maximum placed objects, scale limits, planes visible on start, mode
  (Auto / Force AR / Force Desktop), device camera on/off, preferred camera name and image target
  simulation in Desktop mode.

Your content folder `Assets/ARToolkitContent` is never overwritten when you update the toolkit.

---

## Project modules

The **Projects** tab builds ready-made projects in the open scene. Each project type is one section:
fill in a few fields, press **Add**, and a working example appears in the scene. Change it in the
Inspector, press Play to try it on the PC, then build for the phone. The list under each section shows
what is already in the scene, with **Select** and **Remove**.

Nothing is added to a scene by itself. A module you never add changes nothing, and modules work
together in one scene, so a team can mix them into a project of its own. Ready scenes to open and
play are in `Samples/Scenes/Modules` (Design with the vision simulator, Room Fill, Portal, Story).

| Section | What it builds | Try it on the PC |
|---|---|---|
| Design Walkthrough | A designed space (classroom, garden, museum) that stands around the user. **View: AR / Virtual** hides the real room; hold the button to peek at it | WASD walks from the Start Camera, right-drag looks around |
| Living Poster | An image target that plays a video or a sound on the printed poster | Image target simulation shows the poster on the ground |
| Wall Gallery | A framed picture or video (a prefab in `ARToolkitContent/Gallery`) that is hung on walls | A virtual wall stands in front of the start view |
| School Route | Numbered image targets. Each found stop shows an arrow and "Next: ..."; the HUD counts the stops | Simulated stops count when they come into view |
| See Through Other Eyes | A **Vision** button: colour blindness, low vision, tunnel vision, cataract. With a Design, an **Eye** button shows it from a wheelchair or a child's eye height | Same buttons in the HUD |
| Room Fill | Water, smoke or objects that rise in the room to a level, with **Start / Pause** and a slider | The fill covers a rectangle on the virtual ground |
| Portal | A door to another world. Walk through it to be inside; the room shows through the door behind you | WASD walks through the door |
| NPC Story | Characters that say lines, walk to points, turn to the user and wait for a tap or for the user to come close | Click to go on, WASD to come close |
| Info Cards | A card (title and text) on any object; tapping it opens a read-only panel | Click the object |

### Placing a module

Every module root has a **Module Placement** component:

- **At App Start:** the module appears where the app starts. The root's position in the scene is where
  the phone is at start (for a Design, its Start Camera is). It sits on the floor as soon as the floor
  is found.
- **Tap To Place:** the HUD asks the user to tap the floor. A **Move** button places it again.

On the phone a module follows an AR anchor, so it stays put while the user walks. After a **Room scan**
modules use the scanned floor; Room Fill covers the scanned floor, and NPCs walk around the scanned
furniture and walls.

### Editing the examples

- **Design:** put your models under the Design root. Objects under **Virtual Only** are shown only in
  the Virtual view (walls and floor of the designed room). Add Info Cards to single objects, not to a
  group root (the card's collider would cover the children).
- **Room Fill:** the level, time, colour and the object prefab are on the **Room Fill** component. Up to
  1500 objects are drawn with GPU instancing, from the first mesh and material of the object prefab.
- **Portal:** replace the example **Inside World** with your own prefab; keep its content inside the
  portal's **World Size** (the blue box shown when the portal is selected).
- **Story:** the steps are a list on the **Story Stage** component. Each step has a character, an action
  (Say, Walk To, Turn To User, Play Animation, Wait, Show, Hide) and when it goes on (Auto, Tap, User
  Near). **Add NPC To Story** and **Add Point To Story** add to the selected story. A character model
  with an Animator gets its `Speed` (float) and `Talk` (bool) parameters.

### Limits of the modules

- Videos must be H.264 MP4 files to play on Android.
- The vision filters read the URP opaque texture. The camera makes it only while a filter is on, and
  **Run Setup** keeps it at full resolution. Transparent effects (Room Fill water and smoke) and info
  cards are not filtered.
- The Virtual view and the Portal pause the real-world occlusion while they need to; a running room
  scan keeps it on.
- Characters walk on a NavMesh the story builds at run time: the scanned floor, else a square of
  **Area Size** around the story. Without a scan they do not know the real furniture.

---

## Customizing the look

- **HUD (UI Toolkit):** `UI/ARToolkitHUD.uxml` (layout) and `UI/ARToolkitHUD.uss` (styles).
  Change the variables at the top of the USS (`--primary-color`, `--accent-color`, `--font-size`...)
  to re-color the whole HUD. Keep the element `name`s used by the code.
- **Info labels and 3D visuals:** `Settings/ARToolkitTheme.asset` — label font (any TextMeshPro font
  asset), label colors, grid colors and materials.
- **Toolkit window:** `Editor/UI/*.uxml` and `Editor/UI/ARToolkitWindow.uss`.

---

## Folder structure

```
ErasmusARToolkit/
  Editor/            Toolkit window (UXML/USS), setup, scene builder, build and export services
  Editor/Bootstrap/  Package installer (compiles even before AR Foundation is installed)
  Runtime/           Mode selection, placement, interaction, image tracking, HUD, info labels
  OpenCV/            Webcam image recognition (Editor + Windows x64 only)
  Plugins/OpenCvSharp/  OpenCvSharp binaries and THIRD-PARTY-NOTICES.md
  UI/                HUD UXML, USS, panel settings and runtime theme
  Settings/          Theme asset       Materials/  Toolkit materials       Prefabs/  AR plane prefab
  Samples/           Demo scene, demo config, sample prefabs and SampleMarker.png
```

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Camera image is black on the phone | Setup tab → *AR camera image enabled on URP renderers* → **Add Features** |
| No webcam image on PC | Check *Use Device Camera* in the Content tab; close other apps using the webcam; on Windows allow *Settings > Privacy > Camera > Let desktop apps access your camera* |
| Wrong webcam is used | Press **Camera** to cycle, or set *Preferred Camera Name* to a name from the Console |
| Phone without ARCore shows no camera | Allow the camera permission when asked (or in Android settings > Apps) |
| App says "AR is not supported on this device" | The phone is not ARCore compatible; the app falls back to Desktop mode |
| Info label text is missing | Setup tab → *TextMeshPro Essential Resources* → **Import** |
| Nothing is placed when tapping | Move the phone slowly until planes appear; tap on the highlighted area |
| Surfaces reach past table edges or look patchy | Tap **Scan Room** and look at the floor next to and behind each table from a few sides |
| After a scan, a spot cannot be used | Only scanned surfaces take objects: tap **Scan Room** again and look at that spot |
| The scan does not find the floor | Point the phone at the floor near your feet and take a step to each side. If it still does not: aim at the floor and tap **Set Floor** |
| The floor grid lies too low or too high | Tap **Reset** and start the scan with the phone pointed at the floor near your feet. With *Show Scan Diagnostics* the line shows the heights of the phone (`cam=`) and of the floor (`floor=`): the difference should be the height you hold the phone at |
| *"Shiny or plain floor"* | The phone measures such floors poorly. Move slowly, turn on more light; the floor is then taken from the plane ARCore detected |
| A box stays where nothing stands | Look at the spot from two sides for a few seconds. **Reset** clears everything |
| A chair has no box | Thin legs are hard to see for a phone. Look at the chair from above and from the side, from less than 2.5 m |
| *"The scan no longer matches the room"* | Tracking was lost and found again somewhere else. Tap **Scan Room**, then **Reset**, and scan again |
| *"The phone is hot"* | Let the phone cool down; take it out of its case and out of the sun. Switching off *Detect Walls* makes the scan lighter |
| The box under a placed object looks torn | Run **Run All Setup Steps** and build again: the build lacked the depth shader. Logcat then shows no `Shader ... is missing` |
| The app cannot be installed on a phone without depth | Setup tab → *ARCore Depth is optional* → **Make Optional** |
| Image target is not detected | Check the quality score, print at the configured width, use good lighting |
| Pink materials | The project is not using URP |
| Where are screenshots? | Android: `Android/data/<package>/files/Screenshots`. PC: `Screenshots` next to the game |

---

## Known limitations

- Real AR works on Android only; PC and the Editor use Desktop mode.
- With Vulkan and ARCore set to *Required*, Unity raises the minimum Android version to 10 (API 29).
- Real floor detection needs ARCore; a single PC webcam cannot detect floors, so PC and phones without
  ARCore use a virtual floor.
- Webcam image recognition on PC uses OpenCV (`Plugins/OpenCvSharp`, Apache-2.0, Windows x64 only). Keep
  the Windows build on the Mono scripting backend (the default). Images need a quality score of about
  75+ and must be held fairly flat and well lit. Phones without ARCore show image targets simulated
  on the floor.
- OpenCvSharp is not officially supported on Unity by its authors; it is tested here in the Unity 6.3
  Editor on Windows. If it cannot load, the toolkit falls back to simulated image targets.
- Screenshots are saved to the app folder, not the phone's gallery.
- The room scan is kept for the current session only, and reaches 10 m from where it was started.
  On the first scan frames the phone checks how its depth image lines up with the screen; the result
  is part of the `Scan:` line in the log (`map=Verified`).
- Before the first scan the planes are shown as ARCore detects them: they may reach past table edges
  and over chairs. The scan puts that right.
- What stands in the room is shown as boxes, not in its real shape. A round table gets a grid with
  corners when its outline is close to a rectangle. Walls are found where the camera looked at them up
  to about 1.5 m; glass walls and mirrors are not found.
- Phones measure depth up to about 3.5 m well enough for the scan, and thin things (chair legs, cables)
  only from close by.
- The content of image targets does not take part in the room scan.

---

## Command line (CI / scripted builds)

Every step can run without opening the Editor (`Unity.exe -batchmode -projectPath <project> -executeMethod <method>`).
Each method exits with code 0 on success and 1 on failure.

| Step | Method |
|---|---|
| Install AR Foundation, ARCore, Input System | `Erasmus.ARToolkit.Editor.BootstrapBatch.InstallDependencies` |
| Run all setup steps | `Erasmus.ARToolkit.Editor.BatchCommands.Setup` |
| Add sample content and create an AR scene | `Erasmus.ARToolkit.Editor.BatchCommands.CreateSampleScene` |
| Build the Android APK | `Erasmus.ARToolkit.Editor.BatchCommands.BuildAndroid` |
| Build the Windows player | `Erasmus.ARToolkit.Editor.BatchCommands.BuildWindows` |

Builds are written to the `Builds` folder of the project.

---

## For developers

Tests live in `Tests/` (not included in the exported package). Run them from
**Tools > Erasmus AR Toolkit > Developer > Run Edit Mode Tests / Run Play Mode Tests** or the Test Runner.
**Developer > Generate Demo Scene** rebuilds the demo config and scene.

The room scan is tested without a phone: `Tests/Support/RoomSimulator` is a room with a floor, boxes
and walls that is seen by a simulated depth camera with the mistakes phones make (noise that grows with
the distance, a shiny floor, unsure pixels, a pose that lags). `RoomScanEndToEndPlayModeTests` walks
through a simulated classroom. The design of the scan, its constants and what was measured are in
`Docs/RoomScanV2-Design.md` and `Docs/RoomScanV2-Plan.md` of the project.

The scan runs in managed code on the main thread, within a budget (at most 6400 depth points per
frame, 3 surfaces redrawn at a time). The Profiler shows it as `Erasmus.RoomScan.CopyDepth`,
`SampleDepth`, `Integrate` and `Rebuild`. Moving it into Burst jobs is prepared in the design but was
not done: it should be decided by measurements on phones.

Non-commercial educational project.
