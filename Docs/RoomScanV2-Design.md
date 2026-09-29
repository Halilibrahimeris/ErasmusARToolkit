# Implementation Plan (revised) — Quest-like floor / surface / obstacle scanning on ARCore phones

Path alias: `R` = `C:\Unity\ErasmusARToolkit\Assets\ErasmusARToolkit`. New runtime types use `namespace Erasmus.ARToolkit`, `internal sealed`, reused buffers, XML doc comments and `const` thresholds.

Nothing here was compiled or run. Statements that an existing test still passes come from tracing the test input through the new rules by hand. Every millisecond figure is an estimate until the P0 diagnostics are read on a phone.

## 0. Corrections that shape the plan (checked against the code)

1. **Pose policy.** Raw depth between new estimates is a reprojection to the current frame, and only the timestamp marks a new estimate. The default is "pose of the ARCore frame in which the image was acquired, read at render time". A pose buffer exists as a switchable alternative, decided by a device A/B.
2. **Floor identity is not "lowest level".** It comes from ARCore plane support, a camera-height window and support area, with states `Unknown → Candidate → Trusted`. Destructive protections apply only when `Trusted`, and trust never deletes anything.
3. **The mirror rule is "clip the ray at the trusted floor".** It applies to the floor level only. Raised levels keep normal see-through carving, which is the only mechanism that removes hull overreach (`RoomSurfaceMap.cs:374-383`).
4. **Fixing F1 activates the latent 15 cm depth eraser**, so P0 lowers `PullHeight` in the same commit.
5. **A per-vertex range-aware pull is used only in passes without texture coordinates** (depth-only, obstacle boxes). The textured grid keeps a constant fraction.
6. **Burst 2.0 and Mathematics 1.4 are shim packages in 6000.6** (`Library\PackageCache\com.unity.burst@626e66ab421b\package.json`, `"type": "shim"`). The README supports 6000.0+, where they are real packages. Both resolve the asmdef names `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`.
7. **`DepthGeometry.IsHorizontal` accepts down-facing normals** (`DepthImageMapping.cs:100`, `Mathf.Abs(normal.y)`).
8. **`ARToolkitManager` sits on the scene root.** `XROrigin` is on the child held in `arOriginObject` (`SceneBuilder.cs:105,143`; `ARToolkitManager.cs:26,171`).
9. **`checkedCells` is level-independent** (`RoomSurfaceMap.cs:409`), so it cannot be used to decide whether a floor cell was seen as floor.
10. **Public API keeps its signatures.** `RoomSurfaceMap`, `SurfaceLevel`, `SurfaceGrid`, `SurfaceRegionFilter`, `SurfaceMeshBuilder`, `ScanFrame`, `IScanFrameSource` and `RoomScanProgress` get new behaviour through overloads and optional parameters. Removed public constants stay as `[Obsolete]` aliases. `ToolkitPaths.Version` becomes `1.1.0`.

## 1. Symptom → phase map

| Symptom | Fixed by |
|---|---|
| S1 floor below the real floor | P1 (floor model, plane authority, clipping, lock), P2 (anchor, re-verification) |
| S2 torn box bottom | P0 (shader ships), P3 (steady region larger than the fill, whole floor when trusted, contact protection), P2 (object bound to its level) |
| S3 patchy coverage | P1 (no noise carving, floor prior shown, hysteresis, denser sampling), P5 (morphology, polygons), P6 |
| S4 bleed over chairs, grid on legs | P1 (raised priors need confirmation), P3 (small bias on steadied levels), P4 (blocked cells), P5 (strip removal). Fixed after Scan Room only; raw ARCore hulls before a scan keep today's look (documented in the README) |
| S5 no obstacles | P4 |

---

## P0 — Ship and config fixes, diagnostics

### Modify

| File | Change |
|---|---|
| `R\Runtime\Core\ToolkitTheme.cs` | Add `[SerializeField] Shader occluderShader, surfaceDepthShader;` with getters, `public bool HasRoomScanAssets` (both materials and both shaders non-null), `internal void SetRoomScanShaders(Shader, Shader)`. |
| `R\Editor\Services\ToolkitAssets.cs` | `public static bool HasRoomScanAssets()` loads the theme with `AssetDatabase.LoadAssetAtPath` and checks it, with no writes. `EnsureRoomScanMaterials` becomes `public static bool EnsureRoomScanAssets(ToolkitTheme)`: keeps a theme material whose shader is already right, else uses `GetOrCreateShaderMaterial`, and assigns both shaders. `internal static bool ConfigurePlaneObject` also sets `ARPlaneMeshVisualizer.trackingStateVisibilityThreshold = TrackingState.Tracking` when it differs. |
| `R\Runtime\Scanning\RoomScanner.cs` | `internal void Initialize(Camera, ToolkitTheme)`. `CreateFallbackMaterial(Shader serialized, string name, int queue)` uses the serialized shader, then `Shader.Find`; logs `LogError` when `!Application.isEditor`, else `LogWarning`. `public ScanDiagnostics Diagnostics`. One line every 2 s while scanning via `Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, ...)`. |
| `R\Runtime\Scanning\SurfaceDepthMaterials.cs` | `PullHeight = 0.06f`, `MaxPull = 0.20f` (2σ at 2 m; interim until P3). |
| `R\Runtime\Scanning\DepthScanSource.cs` | Implements `IScanSourceDiagnostics`. `OnCameraFrame` stores `args.timestampNs` when it has a value. Lag = `frameNs * 1e-9 − depthImage.timestamp`. `ConfidentRatio` = sampled pixels at or above `MinConfidence` / sampled. |
| `R\Runtime\Core\ARToolkitManager.cs` | `EnsureOriginSettings()` in `Awake`: `var xr = arOriginObject != null ? arOriginObject.GetComponent<XROrigin>() : null; if (xr != null) xr.CameraYOffset = 0f;`. `EnsureBackgroundMode()` sets `ARCameraManager.requestedBackgroundRenderingMode = BeforeOpaques`. Keep-awake: `NeverSleep` only while `Mode == AR && (IsScanning || now − lastActivity < 180 s)`, else `SystemSetting`; restored in `OnDisable`. `internal void NoteUserActivity()` is called by `InteractionController` on any press. `ARSession.stateChanged` is subscribed in `OnEnable` and removed in `OnDisable`. Theme check logs `LogError` only when `!Application.isEditor && Mode == AR`, else `LogWarning` in AR mode and nothing in Desktop. |
| `R\Editor\Services\SceneBuilder.cs` | `CreateXROrigin`: `CameraYOffset = 0f` and `requestedBackgroundRenderingMode = BeforeOpaques`. |
| `R\Runtime\Placement\ARPlaneSurfaces.cs` | For added planes, set the visualizer threshold only when it is not already `Tracking`. The setter logs unconditionally (`ARPlaneMeshVisualizer.cs:52-60`), so this runs for old prefabs only. `internal bool IsSuppressed`. |
| `R\Editor\Services\BuildRunner.cs` | `GetBlockingProblem` stays side-effect free: Android only, `!ToolkitAssets.HasRoomScanAssets()` → "Room scan materials are missing. Press Run All Setup Steps." `Build` calls `ToolkitAssets.EnsureRoomScanAssets(GetOrCreateTheme())` before `BuildPipeline.BuildPlayer`. |
| `R\Editor\Services\ToolkitSetup.cs` | `RunAll` adds `SceneUpgrader.UpgradeOpenScenes()` and `UrpRendererConfigurator.MakeStencilSafe()`. |
| `R\Editor\Services\UrpRendererConfigurator.cs` | `IsStencilSafe`: every renderer used by an Android quality level is Forward or Forward+, depth priming Disabled, depth attachment Default or with S8. `MakeStencilSafe()` sets these. |
| `R\Editor\Services\ProjectConfigurator.cs` | Explicit rows, not part of `RunAll`: `IsDepthOptional` / `SetDepthOptional()` (`SerializedObject` on the `UnityEditor.XR.ARCore.ARCoreSettings` config object, `m_Depth = 1`, `ApplyModifiedPropertiesWithoutUndo`, `SetDirty`, `SaveAssets`); `IsPhoneRenderingTuned` / `TunePhoneRendering()` (HDR off on Android URP assets, `androidUseSwappy = 1`). |
| `R\Editor\Tabs\SetupTab.cs`, `R\Editor\UI\SetupTab.uxml` | Rows `row-scan-assets` (read-only state), `row-scene-upgrade`, `row-urp-stencil`, `row-depth-optional` (with an explanation), `row-phone-rendering`. |
| `R\Editor\Tabs\BuildTab.cs` | Mark the first scene "(starts on the phone)". Warn when it lies under `ToolkitPaths.SamplesFolder` while a scene under `ToolkitPaths.ScenesFolder` exists. |
| `R\Runtime\Core\ARToolkitConfig.cs` | `[SerializeField] bool showScanDiagnostics;` |
| `R\Runtime\UI\ScanPanel.cs`, `R\UI\ARToolkitHUD.uxml`, `.uss` | Optional `Label name="scan-debug"` looked up with `root.Q<Label>` (not `Require`). Shown when `available && IsScanning && (showScanDiagnostics || (Debug.isDebugBuild && !Application.isEditor))`. Updated at most 2 Hz and only when the string changed. |

### Create

| File | API |
|---|---|
| `R\Runtime\Scanning\ScanDiagnostics.cs` | `public enum MappingState { Checking, Verified, Assumed, Unverifiable }`; `public enum FloorState { Unknown, Candidate, Trusted }`; `public struct ScanDiagnostics` with `Mapping, DepthLagMs, DepthWidth, DepthHeight, ConfidentRatio, CaptureMs, IntegrateMs, RebuildMs, FloorHeight, PlaneFloorHeight, Floor, FloorFromPlane, LevelCount, DepthSlotsUsed, Reflections, Implausible, FramesIntegrated, BackgroundMode, ThermalStatus` and `string ToLine()` (invariant culture). `internal interface IScanSourceDiagnostics { MappingState Mapping; float DepthLagMs; int DepthWidth, DepthHeight; float ConfidentRatio; float CaptureMs; event Action MappingChanged; }`. Fields owned by later phases stay at their defaults. |
| `R\Editor\Services\ToolkitBuildChecks.cs` | `sealed class ToolkitPreBuild : IPreprocessBuildWithReport`, `callbackOrder => -200`, Android only. Calls `EnsureRoomScanAssets` (material and shader references only, the same kind of work `ReferenceLibraryPreBuild` already does) and throws `BuildFailedException` when assets are still missing. It does not touch the plane prefab. |
| `R\Editor\Services\SceneUpgrader.cs` | `NeedsUpgrade(ARToolkitManager)`, `Apply(ARToolkitManager, ToolkitTheme)`, `UpgradeOpenScenes()`. Uses `SceneQueries.FindAll` (includes inactive). Sets the theme through `SerializedObject` on `"theme"`, reads `arOriginObject` the same way, sets `CameraYOffset = 0` and the background mode, and adds missing `ARAnchorManager`, `ARPlaneSurfaces`, `RoomScanner`, disabled `ARPointCloudManager` and `AROcclusionManager`. |
| `R\Materials\ARToolkitSurfaceDepth.mat` | Generated by Run Setup. |
| `R\Tests\Editor\ToolkitBuildChecksTests.cs` | Read-only: `Theme_HasRoomScanAssets`, `PlanePrefab_ShowsOnlyTrackedPlanes`, `ConfigurePlaneObject_SetsTheThreshold` (in-memory GameObject), `Diagnostics_ToLine_UsesDotDecimals`. |
| `R\Tests\Runtime\ManagerStartupPlayModeTests.cs` | `Awake_OnASceneBuilderRig_DoesNotThrow_InDesktopMode`. |

There is no `IProcessSceneWithReport`. It would also run in Play Mode, and runtime migration already covers old scenes.

**Assets committed with P0:** `ARToolkitTheme.asset`, `ARToolkitSurfaceDepth.mat`, `ARToolkitPlane.prefab` (threshold 2), regenerated `Samples\Scenes\ARToolkitDemo.unity` (via `DemoSceneGenerator`), upgraded `ARScene.unity`.

**Reused:** `ReferenceLibraryPreBuild` pattern; `ToolkitAssets.GetOrCreateShaderMaterial`; `RealWorldOcclusion.Configure`; `SceneQueries.FindAll`.

**Tests:** `RealWorldOcclusionTests.ToolkitMaterials_UseTheirRenderOrder("ARToolkitSurfaceDepth")` fails today and passes once the asset is committed. `SurfaceDepthMaterialsTests` read the constants and need no edit.

---

## P1 — Floor-first model

### Constants

| Constant | Value | Reason |
|---|---|---|
| `DepthNoise.Base`, `K` | 0.010 m, 0.005 m⁻¹ | σ(d) = 1.5 cm at 1 m, 5.5 cm at 3 m; calibrate from device residuals |
| `MinConfidence` / `HeightConfidence` / `CarveConfidence` | 128 / 160 / 180 | Google's threshold; stricter for heights and for carving |
| Hit / height / carve range | 0.4–3.5 / 0.5–3.0 / 0.3–4.5 m | 2σ exceeds 14 cm beyond 3.5 m |
| `HitTolerance(d)` | `clamp(2σ, 0.03, 0.06)` | replaces `0.03 + 0.01·d` |
| `MissMargin(d)` | `max(0.06, 3σ)`, +0.15 while unconfirmed | replaces `0.04 + 0.02·d` |
| `Band(d)` | `max(0.04, 2.5σ)` | below-floor test |
| `LevelMergeDistance` | 0.08 for creation and merge | one threshold; the nearest-level rule prevents double hits |
| Prior weight | floor level 0.5; raised, precise 0.25; sparse 0.5 | the floor may show from the ARCore plane, raised hulls need one depth hit |
| Show / hide | 0.30 / 0.12 | hysteresis |
| `ConfirmedLogOdds` | 0.4 | two hit frames |
| `ReflectionHitWeight` | 0.1 | half a hit, confirmed cells only |
| Height histogram | 1 cm bins × 512, decay 0.97 per frame, one sample per distinct (cell, 2 cm bin) per frame | independent of sample density |
| `FusionCells` | 30 cell-frames in a 5-bin window | 0.075 m² seen once |
| Mode search radius | 0.15 m, nearest peak first | a level keeps its own peak |
| Plane agreement window | −0.06 … +0.08 m | outside it the plane and depth disagree |
| Floor clamp around the plane | −0.02 / +0.03, +0.08 when sharp | known biases push depth down |
| Sharp | mass within ±1.5 cm ≥ 60 % of mass within ±5 cm | |
| Raised clamp | ±0.05 | desk planes are less exact |
| Lock | dead band 0.015 m, 3 rebuilds, slew 0.02 m/s, settle 0.005 m | no swimming |
| Down limit | 0.03 m below the last plane-agreed value | sinking needs ARCore to agree |
| Camera height | 80th percentile over 20 s of integrated frames (`camP80`) | replaces the decaying maximum |
| Floor eligibility | `camP80 − h` in 0.8–1.9 m; plane area ≥ 1.0 m² or confirmed area ≥ 1.0 m² | a desk under a 1.4 m phone is never eligible |
| Promotion | an eligible level within 0.20 m above the lowest with support ≥ 2× wins | drifted duplicate planes |
| Trust | same pick for 12 integrated frames and support ≥ 2.0 m² | one desk hull (1.75 m²) cannot be trusted |
| Ghost | ≥ 40 cell-frames and through-ratio ≥ 0.5 | levels below the pick only |
| Demotion | below-cluster ≥ 200 cell-frames within ±3 cm, or a tracked plane ≥ 1.0 m² more than 0.06 m below F | 0.5 m² of coherent floor |
| Implausible depth | `camP80 − y > 1.9 m` | deep reflections, dropped |
| Suppress below | `Height < F − 0.06` | hidden, not deleted |
| Raycast floor guard | `F_drawn − 0.02` | |
| Discovery | distinct cells, cap 60 per bin per frame, decay 0.7, threshold 105 | at least 3 frames and about 0.08 m² |
| Level cap / prune | 12; confirmed < 0.1 m², no prior, age ≥ 80 frames | |
| Speed gates | 60°/s, 1.5 m/s | walking pace |
| Keyframe | moved ≥ 0.05 m or turned ≥ 4°; otherwise every 0.5 s at weight 0.5; first 6 frames always | evidence from independent views |
| Sampling | 56 columns, up to 104 rows (≤ 6000 points) | managed budget |
| Flying pixel | neighbour depth differs by more than `max(0.05, 0.08·d)` | |
| Mapping check | 12 points; Verified: count ≥ 6, error ≤ 0.12, lead ≥ 0.04; Assumed after 24 attempts with hits; Unverifiable after 16 without hits or error > 0.12 | cannot hang |

### Create

| File | API |
|---|---|
| `R\Runtime\Scanning\DepthNoise.cs` | `public static class DepthNoise { Sigma(d); HitTolerance(d); MissMargin(d); Band(d); HeightWeight(d) /* min(1, (0.015/σ)²) */ }` |
| `R\Runtime\Scanning\HeightHistogram.cs` | `internal sealed class HeightHistogram`. Origin set lazily to `firstSample − 2.56 m`. `Add(y, weight, count)`, `Decay(f)`, `bool TryNearestMode(center, radius, minCount, out height, out sharp)` (5-bin window, 3 mean-shift steps with h = 1.5 cm), `CountAround(y, r)`, `Clear()`. |
| `R\Runtime\Scanning\FrameCellSet.cs` | Open-addressing set with 16384 slots and a stamp array. `bool Add(int x, int z, int bin)`, `NextFrame()`. |
| `R\Runtime\Scanning\HeightLock.cs` | `internal struct HeightLock { bool HasValue; float Value; bool Update(float target, float dt, float? plane, bool limitDown); void Reset(); }`. Stored as a field, never a property. |
| `R\Runtime\Scanning\FloorModel.cs` | `internal sealed class FloorModel`: `State`, `LevelId`, `Height`, `DrawnHeight`, `FromPlane`, `CameraP80`; `NoteCamera(y, time)`, `Update(levels, planes)`, `NoteBelow(y, cell, weight)`, `ForceTrust(levelId)`, `OnMerged(from, to)`, `Reset()`; `event Action<FloorState> StateChanged`. |
| `R\Runtime\Scanning\MappingSelfCheck.cs` | Pure state machine: `BeginAttempt()`, `AddSample(candidate, error)`, `EndAttempt(anyHit)`, `State`, `Best`, `bool Recheck(out int switchedTo)`. |
| `R\Runtime\Scanning\CameraPoseBuffer.cs` | Ring of 32 `{ double time; Vector3 position; Quaternion rotation; }`. `Push`, `bool TrySample(time, out Pose)` (nearest within 8 ms, else interpolate when the gap is ≤ 100 ms). |
| `R\Runtime\Scanning\DepthSnapshot.cs` | Persistent `NativeArray<ushort> depth`, `NativeArray<byte> confidence`; `CopyFrom(XRCpuImage, XRCpuImage)` row by row using `rowStride` and `pixelStride`. Holds `Pose`, `Projection`, `Display`, `WorldToMap`, `DepthTime`, `FrameTime`, `Ready`. |
| `R\Runtime\Scanning\RoomModelBuilder.cs` | `internal sealed class RoomModelBuilder`: `Rebuild(RoomSurfaceMap map, float dt, bool all, IRoomModelSink sink)`. Per level: copy, filter, mesh, then `StoreShown`. Used by `RoomScanner.Rebuild` and by the test helper `ShowAll`. |

### Modify

**`ScanFrame.cs`** (public, additive): `Add(point, facesUp, confidence)`; `Add(point, facesUp)` stores confidence 255; `ConfidenceAt(i)`; `Weight` (default 1); `Time` (seconds, 0 = unset); `AllowCarving` (default true); `internal void Transform(in Matrix4x4 m)`.

**`DepthImageMapping.cs`**: `DepthGeometry.IsUpFacing(tangentA, tangentB, toCamera, minCosine)` orients the normal toward the camera and requires `n.y ≥ minCosine·|n|`.

**`DepthPixels.cs`**: `ConfidenceAt(x, y)`; factory `FromSnapshot(DepthSnapshot, minConfidence)`.

**`DepthScanSource.cs`**
- Remove the smoothed-depth fallback. Frames without a confidence image are skipped.
- Capture runs in `RenderPipelineManager.beginCameraRendering` for the AR camera only, and only when `captureRequested` is set. `TryReadFrame` returns the ready snapshot and sets `captureRequested` again.
- `readsWithoutDepth` increases only inside `TryReadFrame` when the last capture found no image.
- The mapping check runs in the capture callback through `MappingSelfCheck`. While `Assumed`, frames carry `AllowCarving = false` and the check repeats every 4th image. A switch raises `MappingChanged`.

```
Capture(cam):
  if cam != arCamera || !captureRequested || snapshot.Ready: return
  if frameTimeNs.HasValue: poseBuffer.Push(frameTimeNs * 1e-9, cameraPose)
  if turnSpeed > 60 || moveSpeed > 1.5: return
  if !TryAcquireRaw(out depth): lastCaptureHadNoImage = true; return
  if depth.timestamp == lastTimestamp: return                     // reprojection
  if !TryAcquireConfidence(out conf): return
  lag = frameTime - depth.timestamp
  policy = (lag < 0 || lag > 0.5) ? CurrentFrame : PosePolicy
  snapshot.CopyFrom(depth, conf)
  snapshot.Pose = policy == CurrentFrame ? cameraPose : poseBuffer.Sample(depth.timestamp) ?? cameraPose
  snapshot.WorldToMap = mapFrame.WorldToMap                       // identity until P2
  if mapping is Checking or Assumed: RunMappingCheck(snapshot)
  snapshot.Ready = mapping is Verified or Assumed
  captureRequested = false
```

**`SurfaceEvidence.cs`**: add `PrecisePriorWeight = 0.25f`, `HideThreshold = 0.12f`, `ConfirmedLogOdds = 0.4f`, `ReflectionHitWeight = 0.1f`; overloads `Score`, `Classify`, `AddHit`, `AddMiss` that take the prior weight and frame weight. Existing overloads are unchanged.

**`SurfaceGrid.cs`**: `Reset(..., float priorWeight = SurfaceEvidence.PriorWeight)`; `LogOddsAt(i)`; `WasKept(i)` / `SetKept(i)`.

**`SurfaceRegionFilter.cs`**: `Apply(SurfaceGrid, in SurfaceFilterSettings)`. `Apply(grid)` uses `SurfaceFilterSettings.Legacy` (today's numbers, no hysteresis), so `SurfaceMeshingTests` are untouched. With hysteresis: `kept = score > 0.30 || (wasKept && logOdds > 0 && score > 0.12)`.

**`SurfaceLevel.cs`**
- Remove the per-level bins. `HeightWindow` and `ConfirmSamples` stay as constants; `SampleCount` returns the confirmed cell count.
- `SurfaceChunk` gains `bool[] Kept` and `bool[] PriorNext`.
- Internal members: `PlaneHeight` (largest-area tracked plane sample within 0.05 m of `Height`), `PlaneArea`, `ConfirmedArea` (counted in `ApplyVote` on crossing 0.4), `IsFloor`, `IsSuppressed`, `IsGhost`, `HasDepthHeight`, `PriorWeight`, `DrawnHeight => heightLock.HasValue ? heightLock.Value : Height`, `ThroughCells`, `SeenCells`, `CreatedFrame`.
- `IsConfirmed` latches when a mode with at least `FusionCells` was found.
- `BeginPriors()` / `EndPriors()` compare per chunk and mark a level dirty only when a chunk changed.
- `Absorb`: `logOdds = max(mine, theirs)`, priors and `Kept` are OR'd, plane samples are concatenated, `ConfirmedArea` is recounted, through/seen counters are added, and the survivor keeps its `HeightLock`.

**`RoomSurfaceMap.cs`**
- `public bool PriorsNeedConfirmation { get; set; } = true;` Changing it marks all levels dirty.
- `AddPrior(height, polygon)` keeps its signature. Plane area comes from the rasterised cell count. A plane within 0.15 m of the floor pick joins the floor level's cells without creating a level.
- `FloorHeight(minArea)` returns `floor.DrawnHeight` when the state is not `Unknown`.
- `Raycast(ray, max, out pose)` stays; new `internal bool Raycast(ray, max, out Pose pose, out int levelId)` uses `DrawnHeight`, skips suppressed levels and levels below the floor guard.
- `internal event Action<int,int> LevelMerged; internal event Action<int> LevelRemoved;`
- Merge keep rule: the floor survives; otherwise the larger `ConfirmedArea`; on a tie the lower `Id`.
- `Vote` gains `bool Weak`.

```
Integrate(frame):
  clock = frame.Time > clock ? frame.Time : clock + 0.125
  floor.NoteCamera(origin.y, clock); W = frame.Weight
  ClassifyPoints(); RefineHeights(); DiscoverLevels(); MergeCloseLevels(); PruneLevels()
  floor.Update(levels); CollectVotes(); ApplyVotes(W)

ClassifyPoints:                       // once per point, reused by every later pass
  v = p - origin; d = |v|
  if d < 0.3 || d > 4.5: skip
  if camP80 - p.y > 1.9: skip; Implausible++
  if floor.State == Trusted && p.y < F - Band(d):
      q = origin + v * (origin.y - F) / (origin.y - p.y); c = CellOf(q)
      kind = floorLevel.LogOdds(c) >= 0.4 ? Reflection
           : floorLevel.HasPrior(c)      ? Suspect
           :                               Below
      if kind == Below && conf >= 160 && upFacing: floor.NoteBelow(p.y, c, W)
      p = q; clipped = true; upFacing = false
  canHit   = conf >= 128 && 0.4 <= d <= 3.5 && (!HasNormals || upFacing)
  canHeight= conf >= 160 && 0.5 <= d <= 3.0 && (!HasNormals || upFacing) && !clipped
  canCarve = conf >= 180 && frame.AllowCarving && |v.y| >= 0.15 * d

RefineHeights:
  heights.Decay(0.97 ^ W)
  for points with canHeight:
      if frameCells.Add(cell.x, cell.z, floor(p.y / 0.02)):
          heights.Add(p.y, HeightWeight(d) * W * (HasNormals ? 1 : 0.5), count: W)
          if HasNormals && p.y < origin.y - 0.2 && NearestLevel(p.y, 0.08) < 0
             && (floor.State != Trusted || p.y >= F + 0.05):
                 discoveryThisFrame[bin] += W                     // capped at 60 per bin
  for each level L: Fuse(L)

Fuse(L):
  found = heights.TryNearestMode(L.Height, 0.15, FusionCells, out hD, out sharp)
  hA = L.PlaneHeight
  if found:
      L.HasDepthHeight = true; L.Confirm()
      agree = hA.HasValue && -0.06 <= hD - hA <= 0.08
      if agree:
          (down, up) = L.IsFloor ? (0.02, sharp ? 0.08 : 0.03) : (0.05, 0.05)
          L.Height = hA + clamp(hD - hA, -down, up)
      else if L.IsFloor && hA.HasValue:
          L.Height = hA; PlaneDepthDisagree++                     // the plane has authority on the floor
          if hD > hA + 0.08 && sharp && disagreement lasted >= 40 frames
             && heights.CountAround(hD, 0.025) >= 300: L.Height = hD
      else L.Height = hD                                          // duplicate planes move onto the real surface
  else if !L.HasDepthHeight && hA.HasValue: L.Height = hA
  // otherwise keep the last fused height

FloorModel.Update:
  eligible = levels where !IsGhost && 0.8 <= camP80 - Height <= 1.9
             && (PlaneArea >= 1.0 || ConfirmedArea >= 1.0)
  pick = lowest eligible
  S(L) = ConfirmedArea + 0.5 * PlaneArea
  for E in eligible with 0 < E.Height - pick.Height <= 0.20: if S(E) >= 2 * S(pick): pick = E
  if pick == null: State = Unknown; RestoreSuppressed(); return
  if pick.Id != LevelId: LevelId = pick.Id; stable = 0; State = Candidate; RestoreSuppressed()
  else stable++
  if State == Candidate && stable >= 12 && max(PlaneArea, ConfirmedArea) >= 2.0:
      State = Trusted; FromPlane = pick.ConfirmedArea < 0.5
      for floor cells with logOdds < 0 and a prior: logOdds = 0   // forgive carving done before trust
      suppress levels with Height < F - 0.06
  if State == Trusted && (belowCluster >= 200 cell-frames within ±3 cm
                          || tracked plane >= 1.0 m² lower than F - 0.06 inside the camera window):
      State = Candidate; stable = 0; RestoreSuppressed(); EnsureLevel(cluster height)

CollectVotes:
  for each classified point:
      if clipped:
          if kind == Reflection: AddVote(floor, c, weak: true); Reflections++
      else if canHit:
          L = nearest level with |p.y - L.Height| <= HitTolerance(d)
          if L exists:
              AddVote(L, CellOf(p), hit)
              if L is below the floor pick:                          // ghost statistics
                  L.SeenCells++; if Solid(pick, crossing cell): L.ThroughCells++
      if canCarve:
          for each level U whose height the ray crosses (end point p, possibly clipped):
              if U.IsFloor && floor.State == Trusted: continue
              if |p.y - U.Height| > MissMargin(d) + (U.IsConfirmed ? 0 : 0.15):
                  AddVote(U, crossing cell, miss)
  Solid(C, cell) = C.LogOdds(cell) >= 0.4
                || (C.HasPrior(cell) && C.PlaneArea >= 1.0 && C.LogOdds(cell) >= 0 && L.PlaneArea < 0.5)
  ghost: SeenCells >= 40 && ThroughCells / SeenCells >= 0.5 -> IsGhost, hidden, not eligible; counters decay 0.97

ApplyVotes(W): per cell
  Hits > 0 && Hits >= Misses -> AddHit(HitWeight * W)
  else Misses > 0            -> AddMiss(MissWeight * W)
  else Weak                  -> AddHit(ReflectionHitWeight * W)

HeightLock.Update(target, dt, plane, limitDown):
  if !HasValue: Value = lastAgreed = target; HasValue = true; return true
  if plane.HasValue && |Value - plane| <= 0.02: lastAgreed = Value
  if limitDown && target < lastAgreed - 0.03 && !(plane.HasValue && plane <= target + 0.02):
      target = lastAgreed - 0.03
  delta = target - Value
  if !moving:
      if |delta| <= 0.015: pending = 0; return false
      if ++pending < 3: return false
      moving = true
  Value += clamp(delta, -0.02 * dt, 0.02 * dt)
  if |target - Value| <= 0.005: moving = false; pending = 0
  return true
```

**`ArcorePlanePriors.cs`**: skip planes whose `trackingState != Tracking`; wrap the loop in `BeginPriors` / `EndPriors`.

**`RoomScanner.cs`**
- `map.PriorsNeedConfirmation = source.IsPrecise` in `StartScan` and `SwitchToSparseIfNeeded`.
- `UpdatePlaneSuppression()`: `suppress = HasModel || (IsScanning && (map.FramesIntegrated > 0 || !source.IsPrecise))`. Called from `StartScan`, `ResetScan`, `Integrate` and `FinishScan`.
- `Integrate` applies the keyframe gate and sets `frame.Weight`.
- `Rebuild` delegates to `RoomModelBuilder`.
- `public void SetFloorAt(Vector2 screenPosition)`: the level under the pointer becomes the floor through `floor.ForceTrust`.
- `internal void ConfigureForTests(Camera, IScanFrameSource precise, IScanFrameSource sparse, Func<bool>, ...)`; the existing overload stays.
- On `MappingChanged`: `map.Clear()`, `Notify(RoomScanText.DepthRecalibrated)`.
- A hint `RoomScanText.ShinyFloor` when `ConfidentRatio < 0.15` for 5 s while the camera pitch is below −30°.

**`RoomScanProgress.cs`**: optional constructor parameter `bool floorFound = true`. New texts: `FindingFloor`, `DepthRecalibrated`, `DepthUnusable` (mapping unverifiable, separate from `LessPrecise`), `ShinyFloor`, `SetFloorButton`.

**`StatusMessageResolver.cs`**: `StatusMessages.RoomScan` now begins with "first point the phone at the floor near your feet".

**`ScanPanel.cs`, HUD UXML**: optional button `scan-set-floor` (looked up without `Require`) that calls `SetFloorAt(screen centre)`.

### Tests that change in P1

| Test | Change | Why |
|---|---|---|
| `RoomSurfaceMapTests.EnsureLevel_HeightsWithinTenCentimeters_ShareOneLevel` | rename to `..._HeightsCloseTogether_...` | threshold 0.08; assertions hold |
| `...AddPrior_MarksCellsInsideThePlane_AsSurface` | split: `PriorsNeedConfirmation = false` keeps the old assertion; default gives `Unknown`, then `Occupied` after one hit frame | raised priors need confirmation |
| `...Integrate_PriorSeenThroughOnce_StaysUntilSeenThroughAgain` | set `PriorsNeedConfirmation = false` | sparse behaviour |
| `...Raycast_HitsTheNearestShownSurface_AtItsHeight` | set `PriorsNeedConfirmation = false` | it builds surfaces from priors only |
| `RoomSurfaceMapTests.ShowAll` | calls `RoomModelBuilder.Rebuild(map, 10f, true, null)` | tests see what the phone draws |

Traced by hand and expected to pass unchanged: the other `RoomSurfaceMapTests`, all six `RoomScanSimulationTests`, all `SurfaceMeshingTests` (legacy overloads), and the play-mode tests (the six bootstrap frames bring the floor up before the first rebuild after discovery).

### New tests in P1

- `FloorModelTests`: desk under a 1.4 m camera is never eligible; desk under a 1.7 m camera is demoted when a floor plane appears; drifted plane 12 cm below is not picked; promotion rule; suppress and restore; merge keeps trust.
- `HeightHistogramTests`, `HeightLockTests`, `DepthNoiseTests`, `FrameCellSetTests`, `CameraPoseBufferTests`.
- `MappingSelfCheckTests`: Verified; Assumed within 24 attempts on ties; Unverifiable with zero hits; recheck switch.
- `RoomSurfaceMapTests`:
  - `Integrate_ConfirmedPrior_IsHiddenAfterThreeSeeThroughFrames`
  - `Integrate_ConfirmedSeatSeenThroughAfterChairMoved_IsCarved`
  - `Integrate_FloorSeenPastConfirmedDeskEdge_CarvesTheOverhang`
  - `Integrate_DeskOutOfViewFor30Frames_KeepsItsHeight`
  - `Integrate_SameSceneAt3480And19200Points_GivesTheSameLevelsAndFloorState`
  - sparse variants of the duplicate-floor and desk-height tests
- Simulation harness: `SimulationOptions { noiseBase, noiseK, scaleBias, planeBias, glossyFloor, lowConfidenceShare, poseLagDegrees, driftJump, driftAtFrame, thinLegChairs, cameraHeight }`. Defaults reproduce today's input.
  - `RangeNoise_FloorHeightStaysWithinOneCentimeter`
  - `ScaleBiasAndPlaneBias_FloorErrorAgainstGroundTruthWithinTwoCentimeters` (depth × 1.03, plane +0.02)
  - `GlossyFloor_ReflectionsMakeNoHolesAndNoLevelBelow`
  - `LowConfidenceFloor_CoverageAtLeast90PercentOfTheHull_OneComponent` (40 % of floor pixels below 128)
  - `PoseLag_DoesNotCarveTheFloor`
  - `DeskPlaneFirst_FloorEndsAtZero` (camera 1.4 m, and 1.7 m with floor frames after 20 desk frames)
  - `StartOnAPodium_FloorMovesDown_PodiumStaysASurface`
  - `UserStandsStill_EvidenceGrowsAtHalfRate`
- Play mode: `MappingNeverVerified_FallsBackToTrackingPoints` (two sources), `ArcorePlanes_StayVisibleUntilTheFirstDepthFrame` (`ARPlaneSurfaces.IsSuppressed`), `ResetScan_WhileScanning_ShowsArcorePlanesAgain`.

---

## P2 — Anchoring (map space)

### Create

`R\Runtime\Scanning\MapFrame.cs`

```csharp
internal sealed class MapFrame
{
    public bool IsAnchored { get; }
    public bool IsTracking { get; }
    public Transform Root { get; }               // "Room Surfaces (scanned)", child of XROrigin.TrackablesParent
    public Matrix4x4 WorldToMap { get; }
    public Matrix4x4 MapToWorld { get; }
    public float HeightOffset { get; }
    public bool Begin(XROrigin origin, ARAnchorManager anchors, Vector3 seedWorld);
    public bool TryAttachFloor(ARPlane floorPlane);        // after trust, height only
    public void OnPlaneRemoved(ARPlane plane);             // re-attach to subsumedBy
    public bool Refresh(out float movedMeters, out float turnedDegrees);
    public void AddHeightOffset(float delta);
    internal void UseTransformForTests(Transform pose);
    public void Dispose();
}
```

```
Begin: free anchor (GameObject under TrackablesParent with AddComponent<ARAnchor>(), the pattern of
       ARToolkitManager.CreatePlacementParent) at (camera.x, floorGuess, camera.z), identity rotation.
       floorGuess = lowest tracked HorizontalUp plane >= 1 m² inside the camera window, else camera.y - 1.2.
TryAttachFloor: only if anchors.isActiveAndEnabled && anchors.subsystem != null
       && anchors.descriptor.supportsTrackableAttachments && plane.trackingState == Tracking,
       inside try/catch (AttachAnchor throws, ARAnchorManager.cs:131-146).
       floorDelta0 = floorAnchor.localY - poseAnchor.localY
Refresh:
  if poseAnchor was destroyed or removed: re-create it at the last local pose
  a = poseAnchor.transform                                 // local to TrackablesParent
  f = a.localRotation * forward; f.y = 0; if |f|² < 0.01: f = cross(a.localRotation * right, up)
  yaw = atan2(f.x, f.z)
  y = a.localPosition.y
  if floorAnchor is tracking: y += (floorAnchor.localY - a.localPosition.y) - floorDelta0
  Root.localPosition = (a.localPosition.x, y + verifyOffset, a.localPosition.z)
  Root.localRotation = Euler(0, yaw, 0)
  MapToWorld = Root.localToWorldMatrix; WorldToMap = Root.worldToLocalMatrix
p_map = WorldToMap.MultiplyPoint3x4(p_world); dir_map = WorldToMap.MultiplyVector(dir_world)
```

Without an `XROrigin` (tests, Editor) the frame is the identity and `Root` is a scene root.

### Modify

| File | Change |
|---|---|
| `RoomScanner.cs` | `StartScan` creates the frame once per session and clears the map and histogram when it does. `LateUpdate` calls `mapFrame.Refresh`. After a jump above 0.02 m or 0.5° the root follows and integration pauses 0.5 s; above 0.10 m or 3° it runs `Reverify()`. Integration also pauses while the anchor is not tracking. Points farther than 8 m from the map origin are skipped, with one `RoomScanText.TooFar` toast. On `floor.StateChanged(Trusted)` it calls `TryAttachFloor(largest tracked plane of the floor level)`. It subscribes to `planeManager.trackablesChanged` for removed planes. New API: `public bool TryGetHit(Vector2 screen, out SurfaceHit hit)`, `internal Transform CreateContentHolder(Pose worldPose, int levelId)`, `internal void Bind/Unbind/MoveBound(ToolkitObject, SurfaceHit)`. `ResetScan` keeps the anchor and unbinds objects. `FinishScan` adopts objects whose base lies within 0.10 m above a level. `OnApplicationPause(false)` runs `Reverify()`. |
| `DepthScanSource.cs` | Capture stores `mapFrame.WorldToMap` in the snapshot, and `TryReadFrame` transforms the frame with it. |
| `ArcorePlanePriors.cs` | `Apply(planeManager, map, buffer, Matrix4x4 worldToMap)`. |
| `RoomSurfaceView.cs` | Takes the root from `MapFrame`. Owns four material copies (fill and outline, steadied and plain) and destroys them in `Dispose`. Sets `_GridFrame` on them when the frame changes. |
| `R\Shaders\ARToolkitSurface.shader` | `_GridFrame("Grid origin xz, cos, sin", Vector) = (0,0,1,0)`. UV: `p = positionWS.xz − _GridFrame.xy; uv = float2(c·p.x − s·p.y, s·p.x + c·p.y) · _BaseMap_ST.xy + _BaseMap_ST.zw`. The defaults reproduce today's output, so shared ARCore plane materials are unaffected. |
| `ARToolkitManager.cs` | `CreatePlacementParent`: when `roomScanner.HasModel`, the holder comes from `CreateContentHolder` and is returned through `out anchorHolder`, so it is destroyed with the object. Tracking loss: a timer starts on `SessionInitializing` and clears on `SessionTracking`; above 2 s it calls `roomScanner.Reverify()`. |
| `InteractionController.cs` | `TryGetSurface(position, out pose, out levelId, out blocked)` asks `RoomScanner.TryGetHit` when a model exists. `TryPlace` binds. Dragging a bound object calls `MoveBound`. |
| `ToolkitObject.cs` | `internal Transform SurfaceHolder`, `internal int SurfaceLevelId` (−1 = none), `public bool HasSurface`, `public float HeightAboveSurface`. |
| `ObjectManipulator.cs` | `internal float? MinLocalHeight`. `Raise` clamps local y when it is set. `MoveTo` and `ResetPose` are unchanged; a bound drag moves the holder, so reset returns the object onto its surface. |
| `EditPanel.cs`, `TransformFormatter.cs` | New overload `Format(position, heightAboveSurface, yaw, scale)`. Bound objects show "Height above surface"; unbound AR objects show Y relative to the floor when it is known. |
| `StatusMessageResolver.cs` | `ResolveAR`: `if (!IsTracking) return IsScanning ? TrackingLostWhileScanning : MovePhone;` |
| `RoomScanProgress.cs` | `RoomScanText.ScanMoved`, `TooFar`. |

`public readonly struct SurfaceHit { Pose Pose; int LevelId; bool Blocked; }`

```
Reverify():                           // also works after FinishScan: the source is started for the check
  source = PickSource(); source.Begin(); pause integration 1 s
  for at most 3 s: feed frames into a scratch HeightHistogram and count raised-level agreement
  dF = scratch floor mode - floor.Height
  agree = up-facing points within HitTolerance of a raised level that land on its shown cells / all such points
  |dF| <= 0.02 and (agree >= 0.6 or fewer than 100 cells) -> nothing
  0.02 < |dF| <= 0.06 and agree >= 0.6                     -> mapFrame.AddHeightOffset(dF)
  otherwise                                                 -> Notify(RoomScanText.ScanMoved)
  if !IsScanning: source.End()
```

On `LevelMerged(from, to)` holders move to the survivor. On `LevelRemoved` objects are unbound and keep their world pose. A height change moves the holder's local y.

### Tests

| Test | Change |
|---|---|
| `StatusMessageResolverTests.AR_Scanning_ExplainsTheRoomScan_OnceTracking` | the second assertion expects `TrackingLostWhileScanning` |

New:
- `MapFrameTests`: round trip; yaw only with a tilted anchor; identity without an anchor.
- `TransformFormatterTests` for the overload.
- Play mode: `RootFollowsTheMapFrame` (`UseTransformForTests`), `PlacedObject_FollowsItsLevelHeight`, `PlacedObject_StaysBoundWhenItsLevelMerges`, `DeletedObject_LeavesNoHolder`, `ResetScan_KeepsPlacedObjects`.

---

## P3 — Rendering and occlusion

### Render queues (`R\Runtime\Core\RenderOrder.cs`)

| Constant | Queue | Content |
|---|---|---|
| `ContactDepth` | 1940 | 4 slots × 3 queues (1940–1951) |
| `SurfaceDepth` | 1952 | 8 slots × 3 queues (1952–1975); was 1960 × 2 |
| `CameraImage` | 1980 | unchanged |
| `VirtualGround` | 1985 | unchanged |
| `RoomOccluder` | 1988 | unchanged |
| `PlaneFill` / `PlaneOutline` | 1990 / 1991 | unchanged |
| `ObstacleFill` / `ObstacleOutline` | 1992 / 1993 | new in P4 |
| `SurfaceMark` | 1995 | unchanged |

New constants: `SurfaceDepthQueuesPerSlot = 3`, `ContactDepthSlots = 4`.

`PlaneGrouping.StencilBitCount` stays 8. Every Mark → Unmark → Write sequence leaves bit 128 cleared before queue 1980, so the eighth plane group at 1990 never sees it set.

### Pass states

| Pass | Queue | ZTest | ZWrite | Color | Stencil | Vertex |
|---|---|---|---|---|---|---|
| Depth Mark | base + 0 | LEqual | Off | 0 | Ref 128, Comp Always, Pass Replace | pulled toward the camera by `f(d)` |
| Depth Unmark | base + 1 | LEqual | Off | 0 | Ref 128, Comp Equal, Pass Zero | pushed away by `f(d)` |
| Depth Write | base + 2 | Always | On | 0 | Ref 128, Comp Equal, Pass Zero | scale `1 + 0.004` |
| Occluder | 1988 | LEqual | On | 0 | none | top at `height − 0.015` |
| Scanned fill / outline, steadied | 1990 / 1991 | LEqual | Off | blend | none | constant fraction 0.004 |
| Scanned fill / outline, no slot | 1990 / 1991 | LEqual | Off | blend | none | today's pull (0.12 / 0.05 / 0.30) |
| ARCore plane fill / outline, rings | 1990 / 1991 / 1995 | LEqual | Off | blend | plane bits 1..128, NotEqual | today's pull, unchanged |
| Obstacle fill | 1992 | LEqual | Off | blend, Cull Back | none | metric pull 0.10 m |
| Obstacle outline | 1993 | LEqual | Off | blend, Cull Off | none | metric pull 0.10 m |

`pull(d) = clamp(0.02 + 0.01·d², 0.03, 0.10)` metres of height above the surface. `f(d) = min(pull / H, 0.25)` with `H = max(|cam.y − p.y|, 0.05)`. Unmark clears marks where the real depth lies behind the pushed surface, so a phantom surface never overwrites what is seen through it. Unity flips the ZTest for reversed Z; no pass writes fragment depth.

The shared plane and ring materials keep their pull, because before a scan that pull is their only noise protection.

### Hide Planes

| Part | Hidden by Hide Planes |
|---|---|
| Scanned fill, outline | yes |
| Obstacle fill, outline | yes |
| ARCore plane fill, outline | yes |
| Depth Mark / Unmark / Write | no |
| Occluders | no |
| Contact protection | no |

Depth-only renderers are named `"Depth ..."` or `"Occluder"`, which is what `SetVisible_HidesTheGrid_ButKeepsTheModel` filters on.

### Modify

| File | Change |
|---|---|
| `R\Shaders\ARToolkitSurfaceDepth.shader` | Properties `_NoiseBase 0.02`, `_NoiseK 0.01`, `_MinPull 0.03`, `_MaxPullHeight 0.10`, `_MaxPull 0.25`, `_Direction` (+1, −1, 0), `_PushBack`. Vertex: `scale = 1 − _Direction·f + _PushBack`. `_PullHeight` is removed. |
| `SurfaceDepthMaterials.cs` | Constructor `(Material source, int firstQueue, int slots)`; `(Material)` forwards to `RenderOrder.SurfaceDepth` and `SurfaceDepthSlots`. Arrays are sized from `slots`. `Mark`, `Unmark`, `Write`. |
| `SurfaceMeshBuilder.cs` | `SurfaceMeshData.SteadyVertices/Triangles`. `BuildSteady(grid, mask, output)`: row-merged full-cell quads over `dilate(Shown ∪ Confirmed, 1)`. `BuildOccluder(grid, confirmedMask, output)`: a second marching-squares pass with its own field buffer; top at y = 0, walls from 0 to −1. `BuildFloorWindow(center, output)`: 0.5 m quads over 12 × 12 m, centred on the camera snapped to 1 m, rebuilt when the camera is more than 3 m from the centre. New meshes are built at local y = 0. `Build(grid, height, occluderBottom, output)` is unchanged. |
| `RoomSurfaceView.cs` | `SetLevelHeight(id, drawnHeight, floorDrawn)` moves the level transform. The occluder child has `localPosition.y = −0.015` and `localScale.y = height − floor − 0.015`, so a height slew never re-meshes. `Part.SetMaterial`; `LevelView.SetDepthSlot` releases the old slot. `AssignDepthSlots()` runs after each rebuild: floor first, then `ShownArea` descending; levels with an empty steady mesh hold no slot. `SetSteadied(bool)` switches the fill and outline between the small-bias and plain copies. A Candidate floor uses the cell-based steady mesh; a Trusted floor uses the window mesh. |
| `RoomModelBuilder.cs` | Emits steady and occluder meshes. At most one level per frame (round-robin, floor first), except when `all`. |

### Create

`R\Runtime\Placement\ContactDepthView.cs`: `internal sealed class ContactDepthView`.
- Created by `ARToolkitManager` only when `Mode == AR` and the occlusion manager is enabled. Never in Desktop mode, where the webcam quad at queue 1980 would fail the depth test behind the quads.
- `Refresh(IReadOnlyList<ToolkitObject>, Func<ToolkitObject,bool> coveredByScan)`. Objects bound to a level that holds a depth slot are skipped.
- The rest are grouped by support height (quantised to 0.01 m, at most 4 groups). One mesh of quads per group: renderer footprint plus 0.15 m, at least 0.3 × 0.3 m.
- Bounds are cached until the transform changes. It uses `GetComponentsInChildren(List<Renderer>)` with a reused list.
- Refreshed on `Registry.Changed` and every 0.25 s.

`PlacedObjectRegistry` gains `public IReadOnlyList<ToolkitObject> Objects`.

Image-target content gets no contact protection in this version (it is not in the registry). This is a documented limitation.

### Tests that change in P3

| Test | Change |
|---|---|
| `SurfaceDepthMaterialsTests.MarkAndWrite_OfASlot_RunBackToBack_InTheirOwnQueues` | expects +6, +7, +8 for slot 2 |
| `...Mark_TestsDepthWithAPull_Write_ReplacesDepthOnMarkedPixels` | asserts `_Direction` and `_MinPull`; adds Unmark assertions |
| `...RenderOrder_DepthCorrectionRunsBeforeOccludersAndGrids` | uses `SurfaceDepthQueuesPerSlot` |
| `RoomScannerPlayModeTests.ScannedFloor_GetsADepthCorrection_...` | queues base and base + 2; every shown cell lies under the steady mesh (replaces `AreSame`) |

`ARToolkitSurfaceDepth.mat` is re-saved with queue 1952 and committed, so `RealWorldOcclusionTests` follows the constant.

New:
- `SurfaceMeshingTests.BuildSteady_CoversEveryShownCell`, `BuildOccluder_UsesConfirmedCellsOnly`.
- Play mode: `TrustedFloor_GetsTheWholeFloorSteadyMesh` (the test moves the camera 6 cm per frame to produce keyframes), `NinthLevel_GetsNoSlot_FloorKeepsIts`, `DesktopMode_HasNoContactDepthRenderer`.

---

## P4 — Obstacle layer

### Constants

| Constant | Value | Reason |
|---|---|---|
| Hit band | `max(0.08, 3σ(d))` … 2.0 m above F; below 0.30 m only when d ≤ 2.5 m | noise at range must not create low obstacles |
| Range, confidence | 0.4–3.5 m, ≥ 128 | same as hits |
| Body exclusion | horizontal distance to `cam.xz − 0.2·forward.xz` below 0.45 m | the user's own legs and feet |
| Hit / miss weight, clamp | 0.25 / 0.15, [−1.0, 2.0] | |
| Blocked on | `logOdds > 0.5` and hits at least 1.5 s apart | a passer-by is not furniture |
| Blocked off | `logOdds < 0.2`, or floor seen in the cell more than 0.5 s after the last hit | newer evidence wins |
| Weak decay | cells with `0 < logOdds ≤ 0.5` untouched since the last rebuild lose 0.08 | |
| Top | stored as absolute map Y; a higher value is accepted after 3 frames within 0.10 m; otherwise `max(frameTop, top − 0.02)` | |
| DDA | rays that end on floor hits, confidence ≥ 180, at most 90 steps, chunks without obstacles skipped | managed budget before P6 |
| Minimum component | 8 cells | |
| Floor dilation | 1 cell | |
| Tier | level area ≥ 1.5 m² and lower than F + 0.40 | podium or stage: a surface, not an obstacle |
| Box fill ratio | ≥ 0.6, else prism from the contour | |
| Box padding | 0.025 m | |
| Box redraw | moved more than 0.05 m or 5° | |
| Raycast refusal | volume age ≥ 3 s, confidence ≥ 0.5, `t_enter < t_hit − 0.02`, and the hit is not on the box's own top level | |
| Kinds | Table: level top 0.55–1.2 m, ≥ 0.3 m². Seat: 0.30–0.55 m, 0.08–0.5 m². Wall: height ≥ 1.5 m, thickness ≤ 0.25 m, length ≥ 1.0 m. Other: the rest | heuristics |

### Create

| File | API |
|---|---|
| `ObstacleVolume.cs` | `public enum ObstacleKind { Other, Table, Seat, Wall }`; `public readonly struct ObstacleVolume { int Id; Pose Pose; Vector3 Size; ObstacleKind Kind; float Confidence; float Age; }`. Stored in map space; the public API converts with `MapToWorld`. |
| `ObstacleMap.cs` | 32 × 32 chunks: `float[] LogOdds, Top, TopCandidate, FirstHit, LastHit, LastFloorSeen; byte[] TopCount; bool AnyObstacle`. `BeginFrame(floorY, time, weight)`, `AddHit(cell, y)`, `AddFloorSeen(cell)`, `AddMiss(cell)`, `EndFrame()`, `IsBlocked(cell)`, `TopOf(cell)`, `CopyTo(ObstacleGrid)`, `Clear()`. |
| `ObstacleGrid.cs` | Dense copy with a one-cell border: `Reset`, `IsBlocked(i)`, `TopAt(i)`. |
| `GridRay.cs` | `internal struct GridRay`: Amanatides–Woo over 5 cm XZ cells; `Start(origin, end)`, `bool Next(out Vector2Int cell, out float t)`. Caches the current chunk. |
| `ObstacleClusterer.cs` | `Cluster(ObstacleGrid, IReadOnlyList<SurfaceLevel>, floorY, yaw, List<ObstacleVolume> output)`. |
| `MinAreaRectangle.cs` | `public static bool Fit(IReadOnlyList<Vector2> points, float preferredYaw, out Vector2 center, out Vector2 size, out float yaw)`. |
| `ObstacleMeshBuilder.cs` | Five faces per box, twelve edge ribbons of 0.01 m. |
| `R\Shaders\ARToolkitObstacle.shader` | `"Erasmus AR Toolkit/Obstacle"`: `_BaseColor`, `_PullMeters 0.10`, `_MinPull 0.02`, `_MaxPull 0.25`, `_Cull`. |

```
After CollectVotes, only while floor.State == Trusted (the map is cleared when the floor id changes
without a merge):
  obstacles.BeginFrame(F, clock, W)
  for each classified point, not clipped, conf >= 128, 0.4 <= d <= 3.5:
      if inside the body capsule: continue
      h = p.y - F
      if h >= max(0.08, 3σ(d)) && h <= 2.0 && (h >= 0.30 || d <= 2.5): AddHit(cell, p.y)
      else if |h| <= HitTolerance(d) && (!HasNormals || upFacing): AddFloorSeen(cell)
  for each ray that ended on a floor hit or was clipped at a confirmed floor cell, conf >= 180:
      ray.Start(origin.xz, end.xz)
      while ray.Next(out cell, out t) && cell != endCell:
          if !chunk.AnyObstacle: skip to the chunk border
          y = lerp(origin.y, end.y, t)
          if Top(cell) is set && y >= F + 0.08 && y < Top(cell) - 0.05: AddMiss(cell)
  EndFrame: per touched cell
      hits > 0 && hits >= misses -> logOdds += 0.25 * W; LastHit = clock; FirstHit set on the first hit
      else misses > 0 || floorSeen -> logOdds -= 0.15 * W
      floorSeen && clock > LastHit + 0.5 -> logOdds = min(logOdds, 0)

Cluster (each rebuild):
  mask = IsBlocked(cell) || cell shown on a raised level that is not a tier and has DrawnHeight >= F + 0.15
  close 3x3; label 8-connected (two-pass union-find); drop components < 8 cells
  per component:
      hull = monotone-chain convex hull of the cell corners
      rectMin = rotating calipers over the hull edges
      rectYaw = bounding box in the frame rotated by yaw
      rect = area(rectYaw) <= 1.15 * area(rectMin) ? rectYaw : rectMin; pad 0.025
      top = 90th percentile of Top; if >= 50 % of the cells lie on one level: top = that level's DrawnHeight
      kind from the table; if cells * 0.0025 / area(rect) < 0.6: prism from the component outline
      match to the previous volumes (centre within 0.3 m): keep Id and Age; keep the old box unless it
      moved more than 0.05 m or 5 degrees
```

In P4 `yaw` is 0 (map axes). P5 passes `RoomAxes.Yaw`.

### Modify

| File | Change |
|---|---|
| `RoomSurfaceMap.cs` | Owns `internal ObstacleMap Obstacles` and the volume list in map space. `Raycast(..., out levelId)` applies the refusal rule and reports `blocked`. |
| `SurfaceGrid.cs` | `SetBlocked/IsBlocked`, `SetCovered/IsCovered`. |
| `SurfaceRegionFilter.cs` | Blocked cells are never kept. Floor hole filling skips cells that are Blocked, Carved or Covered (a raised level is shown or confirmed above). Floor components that touch blocked cells are kept from 0.10 m². |
| `RoomModelBuilder.cs` | Marks blocked (dilated by one) and covered cells on the floor grid before filtering; runs the clusterer; emits obstacle meshes. |
| `RoomScanner.cs` | `public IReadOnlyList<ObstacleVolume> Obstacles` (world poses), `public event Action ObstaclesChanged`. |
| `RoomSurfaceView.cs` | Parts `"Obstacle Fill"` (1992) and `"Obstacle Outline"` (1993). |
| `ToolkitTheme.cs`, `ToolkitAssets.cs` | `obstacleMaterial`, `obstacleEdgeMaterial`, `obstacleShader`, created in `EnsureRoomScanAssets` and part of `HasRoomScanAssets`. Fill: accent colour with alpha 0.18. Theme and `.mat` files are committed with P4. |
| `RoomScanProgress.cs` | Optional last constructor parameter `int obstacleCount = 0`. `Progress` appends "· N obstacles" when N > 0. `RoomScanText.ObstacleThere`. |
| `InteractionController.cs` | `NoSurfaceMessage` returns `ObstacleThere` when the hit was blocked. |

**Occlusion:** boxes write no depth. Tables and seats keep the level occluder from P3. Other obstacles rely on live ARCore depth.

**Optional walls** (`ARToolkitConfig.detectWalls`, default false): `PlaneDetectionMode.Horizontal | Vertical`. Vertical planes become `Wall` volumes 0.05 m thick. Required with it:
- `ARPlacementSurface.TryGetPose` takes the first hit whose plane `alignment.IsHorizontal()`.
- `ARPlaneSurfaces.ApplyVisibility` forces non-horizontal planes off.
- `ARToolkitManager.PlaneCount` counts horizontal planes only.

### Tests

Existing: `RoomScanTextTests` are unchanged (optional parameter). `RealWorldOcclusionTests` gets cases `ARToolkitObstacle` (1992) and `ARToolkitObstacleEdge` (1993).

New:
- `ObstacleMapTests`: blocked only after hits 1.5 s apart; floor seen later clears; the user's feet are ignored; noise at 3.5 m creates nothing; top stored as absolute Y survives a floor slew.
- `GridRayTests`, `MinAreaRectangleTests`.
- `ObstacleClustererTests`: L-shape becomes a prism; podium is a tier.
- `RoomSurfaceMapTests.Raycast_BehindAnObstacle_IsRefused`, `Raycast_OnATableTop_IsNotRefused`.
- Simulation: `ThinLegChair_BlocksTheFloorUnderIt`, `MovedChair_FreesTheFloor`, `PersonWalkingThrough_LeavesNoObstacle`, `BagOnTheFloor_IsAnOtherVolume`, `ChairUnderDesk_FloorIsNotFilled`.
- Play mode: `HidePlanes_KeepsDepthPartsAndHidesObstacles`, `PlacementSurface_WithAVerticalPlane_IgnoresIt`.

---

## P5 — Clean geometry

### Constants

| Constant | Value | Reason |
|---|---|---|
| Closing, opening | 3 × 3; both on the floor and on raised levels | bridges sampling gaps; removes strips under 2 cells (backrest tops) |
| Components | floor: largest, plus ≥ 0.25 m², plus ≥ 0.10 m² when touching blocked cells; raised: ≥ 0.10 m² | |
| Hole filling | floor ≤ 0.5 m², raised ≤ 0.25 m²; never Blocked, Carved or Covered | |
| Douglas–Peucker ε | 0.04 m floor, 0.03 m raised | below one cell |
| Yaw histogram | 1° bins modulo 90°, smoothed ±3 bins, edges ≥ 0.30 m, total ≥ 2.0 m | |
| Yaw update | `θ += 0.2 · wrap90(θ_new − θ)` per full rebuild | |
| Snap gate | ±10° | |
| Minimum edge | 0.05 m | |
| Snap rejection | area change above 15 % or a vertex moved more than 0.10 m | |
| Rectangle accept | polygon area / rectangle area ≥ 0.85 | |
| Polygon redraw | a vertex moved more than 0.03 m | |
| Full pipeline rate | at most 1 Hz per level while scanning, always at `FinishScan` | |
| Grid pitch | 0.25 m default, config 0.10–1.00 m | |

### Create

| File | API |
|---|---|
| `MaskMorphology.cs` | `Close(bool[] mask, int w, int h, bool[] locked, bool[] scratch)`, `Open(...)`. Dilation never turns on locked (carved, blocked) cells. |
| `ContourTracer.cs` | `Chain(List<Vector3> segments, List<List<Vector2>> loops)`; endpoints keyed at 0.5 mm. |
| `PolygonSimplifier.cs` | `Simplify(List<Vector2> loop, float epsilon, List<Vector2> output)`. |
| `RoomAxes.cs` | `AddEdge(a, b)`, `bool TryEstimate(out float yawDegrees)`, `Yaw`, `Freeze()`. |
| `PolygonSnapper.cs` | `bool Snap(List<Vector2> loop, float yaw, List<Vector2> output)`. |
| `PolygonFill.cs` | `Trapezoids(List<List<Vector2>> loops, float yaw, List<Vector3> vertices, List<int> triangles)`. |
| `PolygonRaster.cs` | `FillRows<T>(IReadOnlyList<Vector2> polygon, float cellSize, ref T sink) where T : struct, IRowSink`. Extracted from `RoomSurfaceMap.AddPrior`. A struct sink avoids closures. |

```
Per level (full pipeline):
  mask = Kept && !blocked
  Close, then Open
  components (8-connected), area rules, hole filling
  field = mask ? +1 : -1; 1-2-1 blur; marching squares -> segments
  loops = ContourTracer.Chain(segments)
  for each loop: Simplify(eps); RoomAxes.AddEdge for edges >= 0.30 m
  yaw = RoomAxes.Yaw
  for each loop:
      for every edge within 10 deg of yaw or yaw + 90: replace it by the line with that direction
          through its midpoint
      merge consecutive parallel lines closer than 0.05 m (length weighted)
      vertices = intersections of consecutive lines; drop edges < 0.05 m
      reject the snap when the area changes > 15 % or a vertex moves > 0.10 m
      raised levels: MinAreaRectangle.Fit(loop, yaw); if area ratio >= 0.85: loop = rectangle
  keep the previous polygons when no vertex moved > 0.03 m
  fill = PolygonFill.Trapezoids(loops) in the yaw frame (slabs between sorted vertex z, even-odd spans)
  outline = ribbons along the loops with mitred corners
  Drawn = PolygonRaster(loops) && !blocked && state != Carved
  level.StoreShown(Drawn)                  // Kept stays the evidence state and feeds the hysteresis
```

### Modify

| File | Change |
|---|---|
| `SurfaceMeshBuilder.cs` | `BuildClean(SurfaceGrid, in SurfaceBuildSettings, SurfaceMeshData)` with `SurfaceBuildSettings { bool IsFloor; float RoomYaw; bool FullPipeline; }`. `Build(...)` stays the old path. |
| `SurfaceRegionFilter.cs` | `SurfaceFilterSettings.Floor` and `.Raised` with the values above, 8-connected regions, 4-connected holes. |
| `RoomModelBuilder.cs` | Order: filter → `BuildClean` → `StoreShown(Drawn)`. Passes `RoomAxes.Yaw` to the obstacle clusterer. |
| `RoomSurfaceMap.cs` | `AddPrior` uses `PolygonRaster.FillRows`. |
| `RoomScanner.cs` | `FinishScan`: full rebuild, `RoomAxes.Freeze()`, obstacle volumes and height locks frozen. |
| `RoomSurfaceView.cs` | `_GridFrame` gets map yaw + room yaw; tiling `1 / gridCellSize`. |
| `ARPlaneSurfaces.cs` | When `gridCellSize != 0.25`, a `MaterialPropertyBlock` with `_BaseMap_ST` is set on plane mesh renderers. |
| `ARToolkitConfig.cs` | `[SerializeField, Range(0.1f, 1f)] float gridCellSize = 0.25f;` |

### Tests

No existing test is edited. The simulation tests now run through the polygon pipeline. `DeskArea_MatchesTheRealDesk` and `DeskPlane_NoLongerReachesOverTheChairAndFloor` are the acceptance gates: if they fail, the pipeline is wrong, not the test.

New: `MaskMorphologyTests`, `ContourTracerTests`, `PolygonSimplifierTests`, `RoomAxesTests`, `PolygonSnapperTests`, `PolygonFillTests` (area equals polygon area, holes stay open), `PolygonRasterTests`, `SurfaceMeshingTests.BuildClean_DrawnMaskNeverCoversCarvedOrBlockedCells`, `Outline_DoesNotCreepOverTenRebuilds`, and simulation tests `BackrestTopAtDeskHeight_DoesNotJoinDeskAndChair` and `FragmentedFloorBetweenDesks_KeepsItsAisles`.

---

## P6 — Performance

**6a (always):**

| Item | Change |
|---|---|
| Runtime, Editor and both test asmdefs | References `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`. Version defines `ERASMUS_HAS_BURST` (≥ 1.8), `ERASMUS_HAS_COLLECTIONS` (≥ 2.1), `ERASMUS_HAS_MATHEMATICS` (≥ 1.2) are added to `defineConstraints`, the same pattern as `ERASMUS_HAS_ARF`. No managed runtime fallback. First check that the defines resolve for the built-in shim packages in 6000.6; if one does not, drop that constraint. |
| `R\Editor\Bootstrap\DependencyInstaller.cs` | `RequiredPackages` gains the three package names. |
| `DepthImageMapping.cs` | `bool TryGetViewport(int x, int y, Matrix4x4 display, int width, int height, out Vector2 viewport)`, the inverse of `TryGetPixel`. Off-screen depth pixels become usable through the verified mapping. |
| `DepthUnprojectJob.cs` (new) | `[BurstCompile(CompileSynchronously = true)] struct DepthUnprojectJob : IJobParallelFor` over rows. Output: points in map space, flags (valid, up-facing), confidence. Per-row stride `max(1, floor(0.05 / (d_row · pixelAngle)))` and an output budget of 6000 points. Scheduled in the capture callback followed by `JobHandle.ScheduleBatchedJobs()`. Completed in `TryReadFrame`, `End`, `OnDisable` and before every capture. |
| `RoomScanner.cs` | `Stopwatch.GetTimestamp` around capture, integrate and rebuild, with a 2 s moving average in the diagnostics; `ProfilerMarker`s for development builds. |
| Adaptive rates | Degrade when scan CPU averages above 6 ms per frame or `deltaTime > 40 ms`: integrate at 4 Hz, rebuild 1.0 s, stride + 1. Recover when `deltaTime ≤ 36 ms` and scan CPU below 3 ms for 5 s. Both scenes cap at 30 fps through `m_MatchFrameRate: 1`. |
| Thermal | Every 10 s, `PowerManager.getCurrentThermalStatus` through `AndroidJavaObject`, guarded by `Build.VERSION.SDK_INT >= 29`. MODERATE: integrate at 4 Hz, obstacle rays every 4th frame, no full polygon pipeline while scanning. SEVERE: `FinishScan()` and `RoomScanText.ScanTooHot`. |
| Auto-finish hint | No coverage growth for 20 s → toast "Scan looks complete. Tap Finish Scan." |
| Checked area | A level-independent chunked bitset in `RoomSurfaceMap` replaces `HashSet<long>`. |
| Loops | `for` loops over `IReadOnlyList`; no capturing lambdas in per-frame code. |

**6b (only if the device shows managed integrate + obstacles above 4 ms on the low-end phone, or rebuild spikes above 8 ms):**

| Item | Change |
|---|---|
| Cell storage | `NativeParallelHashMap<int2,int>` chunk → slot, with persistent pools per level and for obstacle columns. |
| `ScanIntegrateJob` (new) | One Burst `IJob`: vote accumulation, top test and DDA. Output is per-touched-cell aggregates only. Per-frame counters are `ushort` with saturating add. |
| Rebuild | Morphology and marching squares over the bounding rectangle of dirty chunks plus a 2-cell halo; components on the whole mask. |

Targets on a mid-range phone, to be replaced by measurements: capture ≤ 0.5 ms, integrate ≤ 3 ms per integrated frame, rebuild ≤ 6 ms per level, no hitch above 50 ms. The URP depth prepass requested by `ARBackgroundRendererFeature` is profiled next to these.

**Tests:** `DepthImageMappingTests.TryGetViewport_IsTheInverseOfTryGetPixel`; `DepthUnprojectJobTests.MatchesTheManagedSampler` (the managed reference lives in the test assembly); `JobLifecycleTests.DisposeWhileScheduled_DoesNotThrow`.

---

## P7 — Cross-cutting tests, benchmarks, device protocol

Tests are written in the phase that introduces the behaviour. P7 adds what spans phases.

| Item | Content |
|---|---|
| `R\Tests\Support\` (new asmdef `Erasmus.ARToolkit.Tests.Support`, constraint `UNITY_INCLUDE_TESTS`) | `RoomSimulator` (moved from `RoomScanSimulationTests.Capture/Cast`, plus `SimulationOptions`) and `SimulatedRoomSource : IScanFrameSource`. Referenced by both test assemblies. |
| `RoomScannerPlayModeTests` | `SimulatedClassroom_EndToEnd`: desk, chair and glossy floor through `RoomScanner`; asserts floor state, obstacle count, and a bound object following the floor. |
| Drift and relocalisation | `DriftJump_FloorConvergesWithinThreeSeconds_NoGhostNoHole`; `RelocalisationTenCentimetersDown_DemotesAndRecovers`; `AnchorRemoved_IsRecreated`; `RelocalisationWithXzOffset_AsksForARescan`. |
| Limits | `MoreThanTwelveLevels_PrunesTheWeakest`. |
| `R\Tests\Performance\` (new asmdef, `Unity.PerformanceTesting`, constrained by a version define on `com.unity.test-framework.performance`) | `Integrate_19200Points_8Levels`, `Rebuild_20x15mRoom`, `Integrate_AllocatesNothing` (GC.Alloc = 0). |
| Upgrade check | Import the new package into a copy of a 1.0.0 project (theme without `surfaceDepthMaterial`, offset 1.1176, Limited prefab, locally generated `ARToolkitSurfaceDepth.mat`). Run Setup, build, run. No toolkit file is deleted or renamed by this plan. The README adds "delete `Assets/ErasmusARToolkit` before importing a new version". |
| Desktop check | `DemoScenePlayModeTests` after every phase; a Windows build; camera-view mode shows no patches under placed objects. |

---

## 8. Editor side and migration summary

| Path | Guarantee |
|---|---|
| Run Setup | Creates theme materials and shader references, upgrades the plane prefab, makes URP renderers stencil-safe, upgrades open scenes. Depth Optional and phone rendering are separate rows the teacher presses. |
| Opening the toolkit window | Status rows only read. `HasRoomScanAssets()` writes nothing. |
| Build from the toolkit window or batch | `BuildRunner.Build` repairs room-scan assets, then builds. |
| Build from Unity's own dialog | `ToolkitPreBuild` repairs materials and fails the build with "Run Setup" when they are still missing. |
| Old scenes at runtime | `ARToolkitManager.Awake` sets the origin offset and background mode and adds `ARAnchorManager`, `ARPlaneSurfaces`, `AROcclusionManager`, `RoomScanner`. `ARPlaneSurfaces` fixes the visibility threshold of old prefabs. |
| Old HUD UXML | New elements are optional. |
| Package export | New materials and shaders lie under `R`, so `PackageExporter` includes them. After P0, P3 and P4 the regenerated theme and `.mat` files are committed. |
| README | Scan section (floor first, walk around furniture, obstacles, walls optional), limitations (S4 before a scan, image-target content, thin legs), troubleshooting (diagnostics line, shiny floors), upgrade steps, MSAA / render scale as a quality-versus-heat choice. |

## 9. Risks and mitigations

| Risk | Mitigation |
|---|---|
| ARCore floor plane is wrong | Depth may override upward after 40 frames of a sharp, strong peak; `h_D − h_A` is logged; Set Floor |
| A desk or seat is taken as the floor | Camera window 0.8–1.9 m; trust needs 2.0 m²; demotion by a lower plane or a 0.5 m² cluster; suppression instead of deletion; Set Floor |
| A real lower floor is hidden | Clipping only when Trusted; points outside known floor cells feed demotion |
| Ghost plane created by ARCore below the floor | Promotion rule; ghost ratio; device check logs planes below the floor |
| Low confidence on glossy tiles | The floor shows from the plane prior; trust from plane area; hint to the user |
| Wrong mapping assumed | No carving while Assumed; recheck; map cleared on a switch |
| Pose policy wrong | Switch plus A/B; automatic fallback when the lag is implausible |
| Plane-attached anchor yaw unstable | Only its height is used; XZ and yaw come from a free anchor |
| Anchor lost or plane subsumed | Re-create or re-attach; integration paused while not tracking |
| Room larger than 8 m | Integration radius and a toast; per-region anchors are future work |
| Phantom obstacles | Body exclusion, time rule, recency rule, noise-aware band, 8-cell minimum |
| Thin legs invisible | Seat level blocks the footprint |
| Coarse boxes cut virtual objects | Boxes write no depth |
| Rectangle fitting squares round tables | 0.85 test and ±10° gate |
| Outline creep | Separate Kept and Drawn masks |
| Rebuild hitches | Height through the transform, prior diff, one level per frame, full pipeline at 1 Hz, 6b if measured |
| Heat and battery | Keyframe gate, adaptive rates, thermal status, keep-awake only while in use |
| Vulkan and GLES3 | Only ZTest, stencil and vertex scaling; both on the checklist |
| P6b storage port changes core data late | Gated on measurement; behaviour pinned by the P1–P5 tests |
| Version defines for shim packages | Checked first in P6 |
| Simulation tests after P1 and P5 | Traced by hand only; the first run decides |

## 10. Measure on the device before tuning (P0 build)

1. Mapping: time to Verified, the four candidate errors, how often Assumed occurs.
2. Depth lag `frameNs · 1e-9 − depth.timestamp` for new estimates: sign and size.
3. Pose policy A/B: pan at about 30°/s over a flat floor; compare floor residual tilt.
4. Depth resolution and confident-pixel ratio on the glossy tiles.
5. Floor residual against range → `DepthNoise.Base`, `K`.
6. Sign and size of `h_D − h_A` → floor clamp.
7. Ground truth: phone at a tape-measured 1.00 m; `cameraY − FloorHeight` must be 1.00 ± 0.02.
8. Reflection and implausible counts per frame; heights of any level or ARCore plane below the floor.
9. Anchor jumps during a five-minute walk; yaw of a plane-attached anchor while the floor polygon grows.
10. Scan CPU times, frame time and thermal status over ten minutes.
11. `currentRenderingMode` is BeforeOpaques.
12. Vulkan, then GLES3.

## 11. Verification

### Edit mode

Run `Tools > Erasmus AR Toolkit > Developer > Run Edit Mode Tests` after every phase. Tests listed as changed are edited in the same commit as the code. All others must pass without edits.

### Play mode

Run Play Mode Tests after every phase, `DemoScenePlayModeTests` included.

### On-device checklist

Devices: one mid-range and one low-end ARCore depth phone (3–4 GB RAM), one phone without the Depth API.

| # | Step | Expected | Symptom |
|---|---|---|---|
| 1 | Install, open logcat | no "Shader ... is missing"; diagnostics line every 2 s | S2 |
| 2 | Before scanning, look at desks with chairs | raw ARCore hulls as before (known limitation) | S4 |
| 3 | Start a scan, point at the floor by your feet | ARCore planes stay until the first depth frame; mapping Verified or Assumed within 5 s; floor Trusted within about 5 s of moving | S3 |
| 4 | Phone at a measured 1.00 m | `cameraY − FloorHeight` = 1.00 ± 0.02 | S1 |
| 5 | Mark a tile corner; view from 1 m and 3 m | the grid stays on the corner; no sinking while walking | S1 |
| 6 | Look at the floor under and beside desks | no second grid below; `Reflections` > 0 on glossy tiles; no holes appear | S1, S3 |
| 7 | Place a box on the floor before any scan | straight bottom edge | S2 |
| 8 | Place a box on the scanned floor near an edge or hole | straight bottom edge; "Height above surface 0.00" | S2 |
| 9 | Press Lower repeatedly | the box stops at the surface | S2 |
| 10 | Scan desks with chairs pushed in | the desk outline ends at the desk edge; no desk-height grid over chairs or backrests | S4 |
| 11 | Look at chair legs on the floor | within 2 m no grid on legs above about 5 cm, at 3 m above about 10 cm; no floor grid inside chair footprints | S4 |
| 12 | Look down at your own feet, then walk on | no obstacle and no floor hole where you stood | S5 |
| 13 | Move a chair after it was boxed | the box and the blocked cells clear within a few seconds of looking there | S5 |
| 14 | Finish the scan | the floor is one polygon with straight edges; desks are rectangles; grid lines follow the room axes | S3 |
| 15 | Tap a chair, then a desk top | the chair refuses placement with a message; the desk top accepts | S5 |
| 16 | Hide Planes | grids and boxes disappear; a placed box is still hidden by a real desk and keeps a straight bottom | S2, S4 |
| 17 | Cover the camera 5 s; then pause and resume the app | the scan is re-verified; the grid returns or the user is asked to scan again | S1 |
| 18 | Scan for 5 minutes | 95th-percentile frame time ≤ 40 ms; no hitch above 50 ms | — |
| 19 | 40-minute lesson soak | thermal status ≤ MODERATE; battery per 10 min noted; the screen sleeps when idle outside a scan | — |
| 20 | Phone without Depth | the scan runs on tracking points with the "less exact" message | — |
| 21 | Repeat 5, 8, 11 on GLES3 | same results | all |

## 12. Out of scope (future)

TSDF mesh; monocular depth or semantic segmentation through the Inference Engine; a soft-occlusion shader for arbitrary user materials; steadying for raw ARCore plane groups before a scan; contact protection for image-target content; manual obstacle editing; per-region anchors for halls; persistence between sessions.

---

## Critique resolution

| Issue | Decision | What changed |
|---|---|---|
| `GetComponent<XROrigin>()` on the manager (3 lenses) | accepted | `arOriginObject.GetComponent<XROrigin>()` with null check; `SceneUpgrader` reads the serialized field; startup play-mode test |
| `FloorState` used in P0 | accepted | both enums declared in `ScanDiagnostics.cs` in P0 |
| `DrawnHeight` only advanced by Rebuild | accepted | falls back to `Height`; `HeightLock` is a field; `ShowAll` uses `RoomModelBuilder` |
| `AddPrior` area parameter, undefined median | accepted | no parameter; area from rasterised cells; `PlaneHeight` = largest agreeing plane; `Absorb` specified |
| Mirror rule on all levels | accepted | clipping at the trusted floor only; raised levels carve normally; test expects 3 frames; two new carve tests |
| `StencilBitCount = 7` breaks a test | accepted (alternative) | constant stays 8 |
| `readsWithoutDepth` in the render callback | accepted | counted only in `TryReadFrame`; capture only on request |
| Diagnostics path through `IScanFrameSource` | accepted | separate internal `IScanSourceDiagnostics` |
| No level id for binding | accepted | `Raycast(..., out levelId)`, `TryGetHit`, `SurfaceHit`, `MoveBound` |
| Candidate floor has no steady mesh | accepted | steady = `dilate(Shown ∪ Confirmed, 1)`; window mesh when Trusted; per-cell coverage test |
| Merge keep rule and floor id | accepted | floor survives, else larger confirmed area; `FloorModel.OnMerged`; merge and remove events |
| `ObstacleGrid` undefined; volumes in world space | accepted | `ObstacleGrid.cs`; volumes in map space |
| `StoreShown` before `Build`; polygons over carved cells | accepted | order changed in `RoomModelBuilder`; Drawn mask excludes blocked and carved |
| `GetBlockingProblem` writes assets | accepted | read-only; repair in `BuildRunner.Build` |
| Pre-build does prefab work | accepted in part | light material repair plus validation, Android only, no prefab or texture work |
| `UseSurfaceShader` bias reset; lower shared tolerances | accepted | shared materials unchanged; small bias only on the scan's own steadied copies |
| One-argument `SurfaceDepthMaterials` constructor | accepted | overload kept; arrays sized from `slots` |
| Slot reassignment impossible | accepted | `Part.SetMaterial`, `LevelView.SetDepthSlot` |
| `.mat` queue stale after P3 | accepted | assets re-saved and committed in P0, P3, P4 |
| `cameraTop` multiplicative decay | accepted | 80th percentile over 20 s |
| Sparse frames never refine heights | accepted | `(!HasNormals || upFacing)`, half weight, sparse tests |
| Fallback test not writable | accepted | two-source `ConfigureForTests`, `IsSuppressed`, `UseTransformForTests`, `SimulatedRoomSource` |
| `RoomScanProgress` constructor break | accepted | optional parameters |
| Checked area per level | accepted | level-independent bitset |
| Job reads arrays during capture | accepted | capture only after the snapshot was consumed; `Complete()` at every boundary |
| Content holder leak | accepted | holder returned as `anchorHolder` |
| Theme setter missing | accepted | `SerializedObject` on `"theme"` |
| Test asmdefs lack references | accepted | references and defines added |
| P4 needs the room yaw from P5 | accepted | yaw 0 in P4; `BuildClean` has `IsFloor` |
| Reason for merge distance wrong | accepted | reason corrected |
| Weak vote storage; byte counters | accepted | `Vote.Weak`; `ushort` saturating counters in 6b |
| Prior weight flip mid-scan | accepted | per-level weight; levels marked dirty |
| Histogram origin | accepted | lazy origin; cleared when the map frame begins |
| `ResetScan` while scanning keeps planes hidden | accepted | `UpdatePlaneSuppression()` |
| `ResetPose` undoes binding | accepted | binding moves the holder |
| `AttachAnchor` at scan start, throws, yaw | accepted | free anchor for pose; guarded plane anchor for height after trust |
| Anchor change above 2 cm: freeze the root | rejected in part | the root follows the anchor, which is ARCore's corrected estimate; integration pauses; large jumps trigger `Reverify` with an XZ agreement test |
| Walls: consumers do not filter | accepted | three fixes listed; default off |
| Visualizer setter logs | accepted | assigned only when different |
| Background mode assumption | accepted | BeforeOpaques set in builder, upgrader and runtime; logged |
| Obstacle boxes torn by depth | accepted | metric pull 0.10 m |
| Snapshot integrated with a later map pose; root lags a frame | accepted | `WorldToMap` stored at capture; root under `TrackablesParent` |
| Timestamp units and null | accepted | pushes at render time keyed by frame seconds; policy fallback |
| `stateChanged` never unsubscribed | accepted | `OnEnable` / `OnDisable` |
| Scene processor runs in Play Mode | accepted | scene processor removed |
| Depth Optional fixes no symptom | accepted | explicit Setup row |
| Burst fallback is dead weight | accepted | single path; managed oracle in tests only |
| `_GridFrame` unused until P5 | accepted | own material copies and `_GridFrame` in P2 |
| Gates depend on sample density | accepted | distinct-cell counting; density regression test |
| Managed integration cost | accepted | one classification pass, chunk caching, 6000-point budget, DDA only for floor-ending rays |
| Native storage for Burst voting | accepted, gated | P6b, only if the device measurement requires it |
| Rebuild cost | accepted | height via transform, prior diff, one level per frame, 1 Hz pipeline; tiling in 6b |
| Adaptive rates never recover; no thermal handling | accepted | Stopwatch timing, thresholds for a 30 fps cap, thermal status |
| Floor can lock onto a desk; trust is destructive | accepted | new eligibility, promotion, trust and demotion rules; suppression; tiers; Set Floor |
| Low-confidence glossy floor never shows | accepted | floor prior weight 0.5; trust from plane area; hint |
| Height snaps back to the plane; clamp; down-step no-op | accepted | last fused height kept; asymmetric clamp; limit against the last plane-agreed value |
| Phantom obstacles | accepted | body exclusion, band, time and recency rules, refusal conditions |
| Tops relative to a moving floor | accepted | absolute Y; fed only when Trusted |
| No keyframe gating | accepted | keyframe gate in `RoomScanner`; half-weight static frames; six bootstrap frames |
| Podium becomes an obstacle | accepted | tier rule; refusal uses `t_enter < t_hit − 0.02` |
| Keep-awake all lesson | accepted | only while scanning or within 3 min of a touch |
| Logging and GC | accepted | no-stacktrace logs, 2 Hz HUD, cached lists, struct sinks |
| Outline creep through `wasShown` | accepted | Kept and Drawn masks |
| Level cap | accepted | 12 levels, pruning |
| Opening on raised levels | accepted | 3 × 3 opening on all levels |
| Steady window position | accepted | centred on the camera |
| 1.0 m/s speed gate | accepted | 1.5 m/s |
| HDR and frame pacing | accepted | explicit Setup row |
| Error log fails Desktop tests | accepted | error only on the device in AR mode |
| Tests creating assets | accepted | read-only tests |
| Public struct with internal enum; removed public constants | accepted | public enums; obsolete aliases; version 1.1.0 |
| `CameraYOffset` listed as an S1 fix | accepted | removed from the S1 row; panel shows height above surface or floor |
| Missing texts | accepted | new `RoomScanText` entries with tests; README |
| Debug label in the Editor | accepted | device-only condition |
| Tautological P7 tests | accepted | plane bias added; assertions against ground truth; relocalisation case |
| Hole filling "observed or prior" | changed | rule is Blocked / Carved / Covered, because `checkedCells` is level-independent (`RoomSurfaceMap.cs:409`) and a seat hit would mark the floor cell as observed |
| Mapping check not testable; map cleared mid-scan | accepted | `MappingSelfCheck`; no carving while Assumed |
| S4 without a scan | documented | README and checklist step 2; steadying of raw plane groups is future work |
| Image-target contact protection | deferred | listed as a limitation |

### Critical Files for Implementation

- C:\Unity\ErasmusARToolkit\Assets\ErasmusARToolkit\Runtime\Scanning\RoomSurfaceMap.cs
- C:\Unity\ErasmusARToolkit\Assets\ErasmusARToolkit\Runtime\Scanning\RoomScanner.cs
- C:\Unity\ErasmusARToolkit\Assets\ErasmusARToolkit\Runtime\Scanning\DepthScanSource.cs
- C:\Unity\ErasmusARToolkit\Assets\ErasmusARToolkit\Runtime\Scanning\RoomSurfaceView.cs
- C:\Unity\ErasmusARToolkit\Assets\ErasmusARToolkit\Editor\Services\ToolkitAssets.cs