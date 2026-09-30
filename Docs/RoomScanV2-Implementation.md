# Room Scan v2 — what was built, and where it differs from the plan

State of 2026-09-30, branch `feature/room-scan-v2`. The plan is `RoomScanV2-Plan.md` (Turkish), the
specification `RoomScanV2-Design.md`. This file lists what is in the code now, what was decided
differently while building it, and what is still open.

## State

| Phase | Content | State |
|---|---|---|
| P0 | Shaders in the build, settings, diagnostics | done, committed (`ae4a886`) |
| P1 | Floor-first model | done, committed (`ae4a886`) |
| P2 | Map space and anchors | done, committed (`d0d6eb9`) |
| P3 | Steady depth on the whole floor, contact protection | done, committed (`3f8a7d5`) |
| P4 | Obstacles | done, not committed |
| P5 | Clean geometry | done, not committed |
| P6 | Walls | done, not committed |
| P7 | Performance (pacing, heat, whole depth image, budgets) | done without the Burst jobs, not committed |
| P8 | End-to-end tests, README, this file | done, not committed |

Tests in the Editor (Unity 6000.6.0f1): **522 Edit Mode, 47 Play Mode, all pass.**
Nothing was measured on a phone yet: the device protocol and the checklist of the plan are open.

## Decided differently

### P1 — floor

- The floor is picked at the start of `RoomSurfaceMap.Integrate` (`UpdateFloor`), before the points are
  classified. With a depth camera that measures 3 % too far, the level was moved by depth before the
  floor was known, and the plane could no longer hold it.
- `FloorModel.PlanesNeedDepth`: below a floor that was trusted once, a plane alone makes no new floor;
  depth has to see a surface there. A plane of ARCore that drifted down made the floor state switch
  between Trusted and Candidate otherwise.
- `FloorModel.IsContradicted` looks at the heights measured below the floor only, not at planes.

### P2 — map space

- The scan takes nothing in further than **10 m** from where it started (the plan said 8 m).
- `ScanVerifier` keeps a second histogram of heights that no level explains: a surface where the model
  has none means that the model lies somewhere else (verdict `Moved`).

### P4 — obstacles

- Obstacles are always **boxes**. The plan drew a prism along the outline for things that fill their
  box badly. Instead such a cluster is cut where the boxes of the parts fit best (`ObstacleClusterer.TryCut`,
  at most 3 cuts): an L-shaped row of desks becomes two boxes.
- The floor under a table is not drawn (`SurfaceLevel.MarkCovered`), and holes there are not filled:
  an object cannot be placed under a table.
- A seat refuses objects although it is a surface (`RoomSurfaceMap.IsBehindObstacle`): what is put on
  a chair is sat on.

### P5 — outlines

- The outline of a surface holds still (`SurfaceMeshBuilder.Clean.cs`): it is drawn anew when it moved
  by more than 5 cm for 3 redraws, at once when it moved by more than 15 cm, and a tidier outline (fewer
  corners, along the room) wins over one that is as good. Without this the edges of the floor moved with
  every redraw on noisy depth.
- The direction of the room (`RoomAxes`) is the circular mean of four times the edge angles, not the
  center of a histogram bin: a room along the axes of the session was drawn half a degree off.
- `PolygonSnapper.DropDents`: a side that is broken by a short slanted piece (≤ 10 cm) is one side.
- `SurfaceMeshBuilder.Build` with `SmoothEdges` (the way of version 1.0) builds its field from the
  mask, which made the test `Build_SmoothEdges_StraightenNoisyEdges` pass that had failed before.

### P6 — walls

- Floor behind a wall is not drawn when depth never confirmed it, up to **3 m** behind the wall, seen
  from where the camera was (`WallModel.MarkBehind`, `NoteCamera`). The plan removed pieces of less than
  1 m² that were not joined to the main floor; planes of ARCore reach through walls in one piece, so
  that rule did not catch them.
- A wall that depth saw stays for 6 redraws when it is not found again (`WallModel.KeepRedraws`):
  what stands in the room is sorted into boxes anew at every redraw, and walls flickered.
- A ray that ends where nothing is drawn still says why (`SurfaceHit.Blocked`, `BehindWall`), so the
  user gets "That is behind a wall" instead of nothing.

### P7 — performance

- **No Burst jobs.** `DepthUnprojectJob`, the Burst/Collections/Mathematics references of the asmdefs
  and the entry in `DependencyInstaller` were left out. The managed code keeps to a budget instead, and
  whether that is enough is for the measurements on phones to say (trigger of the plan: integration and
  obstacles above 4 ms, or a redraw above 8 ms, on a low-end phone).
- The whole depth image is sampled, also what lies beside the screen (`DepthImageMapping.TryGetViewport`,
  `TryGetImageBounds`, `DepthScanSource.LayOutGrid`): up to half a screen on each side, at most 6400
  points per frame. Points closer than 2 cm to their neighbors in the grid are left out.
- At most 3 surfaces are redrawn at a time, those that waited longest first; the floor always
  (`RoomModelBuilder.MaxRedraws`, `Waiting`). When surfaces wait, the next redraw comes after 0.15 s.
- `ScanPacing`: 8 frames a second; 4 when a frame of the scan costs more than 6 ms or the app needs more
  than 40 ms for a frame, until all was well for 5 s. Android thermal status MODERATE: 4 frames a second
  and a message; SEVERE: the scan is finished.
- Profiler markers: `Erasmus.RoomScan.CopyDepth`, `SampleDepth`, `Integrate`, `Rebuild`.

### P8 — tests

- `RoomScanEndToEndPlayModeTests`: a walk through a simulated classroom (shiny floor, desk, chair,
  walls) through the `RoomScanner` component; a jump of the anchor by 3 cm; a relocalisation by 12 cm.
- `RoomSurfaceMap.NoteMapMoved` was added for the jump test: after the anchor moved by more than 2 cm or
  0.5°, the heights measured before count a quarter, and the drawn heights follow without waiting for
  three redraws. The floor is back where depth sees it after about 2.5 s (7 s before).
- Not written: the performance test assembly (`Tests/Performance`), because numbers from the Editor say
  little about phones; `AnchorRemoved_IsRecreated`, because AR anchors cannot be simulated in the
  Editor (`MapFrame.OnPlaneRemoved` is covered by reading the code only); the upgrade of a copy of a
  1.0.0 project, which is a manual step.

## Open

1. Build the APK, run the device protocol of the plan (section "Cihazda ölçüm protokolü"), and send the
   `Scan:` lines of Logcat. The constants of `DepthNoise`, the clamps of the floor and the thresholds of
   `ScanPacing` are defaults until then.
2. The checklist of the plan on a phone with depth, one without, Vulkan and GLES3.
3. Decide about the Burst jobs with the timings of step 1 (`int=`, `reb=`, `cap=` in the `Scan:` line).
4. Measure what vertical plane detection costs (`Detect Walls` on and off).
