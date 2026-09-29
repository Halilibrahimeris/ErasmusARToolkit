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
  floor and every table top, then tap **Finish Scan**. The phone's depth images are fused over many
  frames: surfaces are confirmed by repeated observations, and wherever the camera sees through a
  detected plane (ARCore planes are convex hulls that reach past table edges) that part is removed.
  Duplicate floors merge, table heights are measured, small fragments disappear and surfaces ARCore
  missed (chair seats) are added. After the scan the grid follows the real edges, objects can only be
  placed on scanned surfaces, and scanned tables hide the floor grid and virtual objects behind or
  under them. Phones without ARCore Depth scan with tracking points (slower, less exact).
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
| Unity | 6000.0 or newer (tested on 6000.3) |
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
Validation fixes.

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
| Hide surfaces | **Hide Planes** | **Hide Grid** |
| Show / hide info | Tap the object | Click the object |
| Camera | Move the phone | Right-drag orbit, middle-drag pan, wheel zoom, WASD move, F reset |

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
- The room scan covers horizontal surfaces (floors, tables, seats), not walls. It is kept for the
  current session only. On the first scan frames the phone checks how its depth image lines up with
  the screen; the result is logged as `Depth mapping self-check` (Android Logcat).

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

Non-commercial educational project.
