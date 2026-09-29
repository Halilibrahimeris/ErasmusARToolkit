# Plan: Telefonda Quest-benzeri zemin / yüzey / engel / duvar tarama (Room Scan v2)

## Context

Erasmus AR Toolkit'in (Unity 6000.6.0f1, AR Foundation 6.6.2, ARCore 6.6.2, URP 17.6) "Room Scan"
sistemi parlak fayanslı sınıfta şu belirtileri veriyor:

| # | Belirti |
|---|---|
| S1 | Zemin ızgarası gerçek zeminin altına düşüyor / yanlış yükseklikte |
| S2 | Yerleştirilen kutunun altı yırtık (gerçek dünya derinliği kesiyor) |
| S3 | Zemin kaplaması yamalı, delikli, kenarlar düzensiz |
| S4 | Masa hizası yüzeyler sandalyelere taşıyor; ızgara sandalye ayaklarının üstüne çiziliyor |
| S5 | Engel tanıma yok |

Hedef: sıradan ARCore telefonlarında (çoğunda ToF yok) VR gözlüğü oda kurulumu gibi kararlı, temiz
zemin + yüzey + engel + duvar modeli.

**Kullanıcı kararları:** kapsam = tümü, aşamalı · engeller = yarı saydam kenarlı kutular ·
ARCore Depth = Optional · duvarlar = dahil (oda sınırı).

**Dış kaynak sonucu:** ARCore + AR Foundation 6.6.2 telefonda mesh / bounding box / düzlem sınıflandırması
vermiyor; `ARShaderOcclusion` yalnızca gözlüklerde. Niantic NSDK bu Unity sürümüyle uyumsuz ve ücretli;
ARCore Scene Semantics yalnızca dış mekân. Dolayısıyla mevcut 2.5D seviye/hücre mimarisi **evrilecek**
(TSDF ile değiştirilmeyecek). Algoritma referansları: ARCore Depth Lab (Apache-2.0), ANYbotics
elevation_mapping (BSD), OctoMap log-odds, OpenChisel serbest-alan oyma, Quest Scene modeli.

**Yöntem:** 18 ajanlı kod denetimi + araştırma (her iddia karşıt doğrulandı), ardından mimar taslağı
4 eleştirmen merceğiyle (kod gerçekliği, ARCore API, telefon performansı, test/göç) sınandı:
5 engelleyici + 38 önemli sorun bulunup plana işlendi.

## Doğrulanmış kök nedenler

| # | Kök neden | Kanıt | Belirti |
|---|---|---|---|
| K1 | `Surface Depth` shader'ı Android build'ine girmiyor (temada `surfaceDepthMaterial` yok, `.mat` yok, `Shader.Find` player'da null) → derinlik düzeltme telefonda hiç çalışmıyor | `Settings/ARToolkitTheme.asset`, `RoomScanner.cs:465-486` | S2 |
| K2 | Zeminin altında ölçülen noktalar (fayans yansıması, uzak menzil gürültüsü) gerçek zemin hücrelerini oyuyor; oyma için normal/yüksek güven gerekmiyor | `RoomSurfaceMap.cs:359-384` | S3 |
| K3 | Zemin altı seviyeler keşfedilebiliyor, oyulamıyor; `FloorHeight` en alçağı seçiyor; `Raycast` delikten alt seviyeye düşüyor | `RoomSurfaceMap.cs:173-227, 250-256` | S1, S2 |
| K4 | Yükseklik kestirimi ağırlıksız, unutmuyor; ARCore düzlem yüksekliği yalnızca 20 sahte örnek | `SurfaceLevel.cs:86-137` | S1 |
| K5 | Model dünya uzayında sabit, anchor yok, `FinishScan` sonrası güncellenmiyor | `RoomSurfaceView.cs:99-110` | S1, S2 |
| K6 | Izgara shader'ı derinlik testi için kendini kameraya %5–30 çekiyor → öndeki nesneler örtemiyor | `ARToolkitSurface.shader:97-102` | S4 |
| K7 | ARCore dışbükey zarfları derinlik teyidi olmadan gösteriliyor (`PriorWeight 0.5 > 0.3`) | `SurfaceEvidence.cs:34-39` | S4 |
| K8 | Zemin engellerce oyulamıyor; yatay olmayan noktalar atılıyor; ≤ 0.25 m² delikler doluyor; engel katmanı yok | `RoomSurfaceMap.cs:350-377`, `SurfaceRegionFilter.cs:85-105` | S4, S5 |
| K9 | Derinlik eşleme öz-denetimi düz zeminde sonsuza dek kararsız kalabiliyor → hiç kare işlenmiyor | `DepthScanSource.cs:165-169, 367-383` | S3, S4 |
| K10 | Yalnızca 40×~87 örnek; poz, derinlik zaman damgasıyla eşlenmiyor; yumuşatılmış derinlik ham güvenle eşleniyor | `DepthScanSource.cs:141-245` | S1, S3 |
| K11 | `DepthGeometry.IsHorizontal` aşağı bakan normalleri de kabul ediyor (masa altları "yukarı bakan" sayılıyor) | `DepthImageMapping.cs:100` | S1 |
| K12 | Izgara UV'si dünya XZ × 4, oturum yönüne hizalı; kenarlar normalize edilmemiş skor alanından, sadeleştirme yok | `ARToolkitSurface.shader:104`, `SurfaceMeshBuilder.cs:168-216` | S3 |
| K13 | Ayarlar: `CameraYOffset 1.1176` (ekrandaki Y = −0.09'un nedeni), düzlem eşiği `Limited`, Depth `Required`, build index 0 demo sahne, uyku engeli yok | sahneler, prefab, `ARCoreSettings.asset` | dolaylı |

## Temel tasarım ilkeleri

1. **Zemin kimliği "en alçak seviye" değildir.** Durumlar: `Unknown → Candidate → Trusted`. Yıkıcı korumalar yalnızca `Trusted` iken; güven hiçbir şeyi silmez (bastırır, geri alınabilir).
2. **Ayna kuralı = "ışını güvenilir zeminde kes".** Yalnızca zemin seviyesine uygulanır. Yükseltilmiş yüzeyler normal oyulmaya devam eder (çekilen sandalye silinebilsin).
3. **"Kanıt yok" ≠ "aleyhte kanıt".** Parlak/dokusuz zeminde derinlik zayıfsa zemin ARCore düzleminden gösterilir.
4. **Kanıt, bağımsız görüşlerden gelir.** Anahtar kare kapısı; eşikler nokta sayısıyla değil farklı hücre sayısıyla.
5. **Çizilen = vurulan.** `Kept` (kanıt) ve `Drawn` (temizlenmiş poligon) maskeleri ayrı; raycast `Drawn` kullanır.
6. **Genel API imzaları korunur.** Yeni davranış overload/opsiyonel parametreyle; kaldırılan sabitler `[Obsolete]` takma adı. `ToolkitPaths.Version = 1.1.0`.

## Aşama özeti

Her aşama sonunda proje derlenir; değişen testler aynı commit'te güncellenir. Yol kısaltması: `R` = `Assets/ErasmusARToolkit`.

| Aşama | Amaç | Belirti |
|---|---|---|
| **P0** | Shader build'e girsin, ayarlar düzelsin, cihaz ne yaptığını raporlasın | S2 |
| **P1** | Zemin-öncelikli model: doğru, kilitli, deliksiz zemin | S1, S3, S4 |
| **P2** | Harita uzayı + anchor; nesneler yüzeyine bağlı | S1, S2 |
| **P3** | Derinlik düzeltme tüm zemini kapsasın; küçük bias; temas koruması | S2, S4 |
| **P4** | Engel katmanı → yarı saydam kutular; engellenen hücreler | S4, S5 |
| **P5** | Temiz geometri: morfoloji, düz kenarlı poligon, dikdörtgen masalar, ızgara hizası | S3 |
| **P6** | Duvarlar: oda sınırı panelleri | S5 |
| **P7** | Performans: Burst, uyarlamalı hız, ısı yönetimi | — |
| **P8** | Aşamalar arası testler, benchmark, yükseltme denetimi | — |

Çalışma düzeni: `feature/room-scan-v2` dalı; aşama başına ayrı commit (onayınızla). İlk adımda ayrıntılı
tasarım belgesi repoya `Docs/RoomScanV2-Design.md` olarak kopyalanır (kaynak: aşağıdaki "Kaynaklar").

---

## P0 — Build/ayar düzeltmeleri ve tanılama

**Değiştir**

| Dosya | Değişiklik |
|---|---|
| `R/Runtime/Core/ToolkitTheme.cs` | `[SerializeField] Shader occluderShader, surfaceDepthShader` + getter; `public bool HasRoomScanAssets`; `internal SetRoomScanShaders` |
| `R/Editor/Services/ToolkitAssets.cs` | `HasRoomScanAssets()` (yalnızca okur); `EnsureRoomScanMaterials` → `public static bool EnsureRoomScanAssets(ToolkitTheme)` (shader referanslarını da atar); `ConfigurePlaneObject` düzlem eşiğini `TrackingState.Tracking` yapar |
| `R/Runtime/Scanning/RoomScanner.cs` | `Initialize(Camera, ToolkitTheme)`; `CreateFallbackMaterial` önce serileştirilmiş shader, sonra `Shader.Find`; cihazda `LogError`; `public ScanDiagnostics Diagnostics`; taramada 2 sn'de bir tek satır log (stacktrace'siz) |
| `R/Runtime/Scanning/SurfaceDepthMaterials.cs` | `PullHeight 0.15 → 0.06`, `MaxPull 0.35 → 0.20` (K1 düzelince 15 cm "derinlik silici" devreye girmesin diye **aynı commit'te**) |
| `R/Runtime/Scanning/DepthScanSource.cs` | `IScanSourceDiagnostics` uygular; `args.timestampNs` saklanır; gecikme ve güvenli piksel oranı ölçülür |
| `R/Runtime/Core/ARToolkitManager.cs` | `EnsureOriginSettings()`: `arOriginObject.GetComponent<XROrigin>()` (null kontrollü) `.CameraYOffset = 0`; `EnsureBackgroundMode()` → `BeforeOpaques`; uyku engeli yalnızca AR'da tarama sırasında veya son dokunuştan 180 sn içinde; `ARSession.stateChanged` `OnEnable/OnDisable`; tema hatası yalnızca cihazda AR modunda `LogError` |
| `R/Editor/Services/SceneBuilder.cs` | `CreateXROrigin`: `CameraYOffset = 0`, arka plan modu `BeforeOpaques` |
| `R/Runtime/Placement/ARPlaneSurfaces.cs` | Eklenen düzlemde eşik `Tracking` değilse ayarla; `internal bool IsSuppressed` |
| `R/Editor/Services/BuildRunner.cs` | `GetBlockingProblem` yan etkisiz (yalnızca Android, `HasRoomScanAssets()`); `Build` derlemeden önce `EnsureRoomScanAssets` çağırır |
| `R/Editor/Services/ToolkitSetup.cs` | `RunAll`: `SceneUpgrader.UpgradeOpenScenes()`, `UrpRendererConfigurator.MakeStencilSafe()`, `ProjectConfigurator.SetDepthOptional()` (kullanıcı kararı) |
| `R/Editor/Services/UrpRendererConfigurator.cs` | `IsStencilSafe` / `MakeStencilSafe()` (Forward/Forward+, depth priming kapalı, stencil'li derinlik) |
| `R/Editor/Services/ProjectConfigurator.cs` | `IsDepthOptional` / `SetDepthOptional()` (`SerializedObject` ile `m_Depth = 1`); `IsPhoneRenderingTuned` / `TunePhoneRendering()` (HDR kapalı, `androidUseSwappy = 1`) — ayrı satır |
| `R/Editor/Tabs/SetupTab.cs`, `R/Editor/UI/SetupTab.uxml` | Satırlar: `row-scan-assets`, `row-scene-upgrade`, `row-urp-stencil`, `row-depth-optional`, `row-phone-rendering` |
| `R/Editor/Tabs/BuildTab.cs` | İlk sahneyi "(telefonda açılır)" diye işaretle; demo sahne ilk sıradaysa uyar |
| `R/Runtime/Core/ARToolkitConfig.cs` | `bool showScanDiagnostics` |
| `R/Runtime/UI/ScanPanel.cs`, `R/UI/ARToolkitHUD.uxml/.uss` | Opsiyonel `Label name="scan-debug"` (`Require` değil `Q<Label>`; eski UXML bozulmaz) |

**Oluştur**

| Dosya | İçerik |
|---|---|
| `R/Runtime/Scanning/ScanDiagnostics.cs` | `public enum MappingState { Checking, Verified, Assumed, Unverifiable }`, `public enum FloorState { Unknown, Candidate, Trusted }`, `public struct ScanDiagnostics` (+ `ToLine()`, invariant culture), `internal interface IScanSourceDiagnostics` |
| `R/Editor/Services/ToolkitBuildChecks.cs` | `ToolkitPreBuild : IPreprocessBuildWithReport` (`callbackOrder -200`, yalnızca Android): malzemeleri onarır, hâlâ eksikse `BuildFailedException` |
| `R/Editor/Services/SceneUpgrader.cs` | `NeedsUpgrade`, `Apply`, `UpgradeOpenScenes` |
| `R/Materials/ARToolkitSurfaceDepth.mat` | Run Setup üretir |
| `R/Tests/Editor/ToolkitBuildChecksTests.cs`, `R/Tests/Runtime/ManagerStartupPlayModeTests.cs` | salt-okunur denetimler; `Awake_OnASceneBuilderRig_DoesNotThrow_InDesktopMode` |

**P0 ile commit edilen asset'ler:** `ARToolkitTheme.asset`, `ARToolkitSurfaceDepth.mat`, `ARToolkitPlane.prefab`,
yeniden üretilmiş `ARToolkitDemo.unity`, yükseltilmiş `ARScene.unity`, `Assets/XR/Settings/ARCoreSettings.asset` (`m_Depth: 1`).

**Yeniden kullanılan:** `ReferenceLibraryPreBuild` kalıbı (`ReferenceLibraryBuilder.cs:124`), `ToolkitAssets.GetOrCreateShaderMaterial`, `RealWorldOcclusion.Configure`, `SceneQueries.FindAll`.

> **P0 sonrası sizden beklenen:** APK'yı kurup "Cihazda ölçüm protokolü"nü çalıştırmak ve logcat tanılama
> satırlarını paylaşmak. P1 sabitleri bu ölçümlerle kalibre edilir (varsayılanlarla ilerleme sürer).

---

## P1 — Zemin-öncelikli model

**Sabitler**

| Sabit | Değer | Gerekçe |
|---|---|---|
| `DepthNoise` σ(d) | `0.010 + 0.005·d²` m | ARCore hatası mesafeyle karesel artar |
| Güven: isabet / yükseklik / oyma | 128 / 160 / 180 | Google önerisi; yıkıcı işlemler daha katı |
| Menzil: isabet / yükseklik / oyma | 0.4–3.5 / 0.5–3.0 / 0.3–4.5 m | |
| `HitTolerance(d)` | `clamp(2σ, 0.03, 0.06)` | `0.03 + 0.01·d` yerine |
| `MissMargin(d)` | `max(0.06, 3σ)` (+0.15 teyitsizken) | `0.04 + 0.02·d` yerine |
| `Band(d)` (zemin altı) | `max(0.04, 2.5σ)` | |
| `LevelMergeDistance` | 0.08 (oluşturma ve birleştirme için tek eşik) | 4 cm / 10 cm uyumsuzluğu gider |
| Prior ağırlığı | zemin 0.5; yükseltilmiş+hassas 0.25; seyrek 0.5 | zarf hücresi 1 derinlik isabeti ister |
| Göster / gizle | 0.30 / 0.12 | histerezis |
| Yükseklik histogramı | 1 cm × 512, kare başına 0.97 sönüm, kare başına (hücre, 2 cm) tekil örnek | örnek yoğunluğundan bağımsız |
| Zemin kelepçesi (düzleme göre) | −0.02 / +0.03, tepe keskinse +0.08 | bilinen sapmalar derinliği aşağı iter |
| Yükseklik kilidi | ölü bant 1.5 cm, 3 yeniden çizim, 2 cm/sn, aşağı sınır: son düzlem-onaylı değerin 3 cm altı | batma için ARCore onayı gerekir |
| Zemin uygunluğu | `camP80 − h` ∈ 0.8–1.9 m ve (düzlem ≥ 1 m² veya teyitli ≥ 1 m²) | 1.4 m'deki telefonun altındaki masa aday olamaz |
| Güven (Trusted) | 12 kare aynı seçim ve destek ≥ 2.0 m² | tek masa zarfı güvenilir olamaz |
| Anahtar kare | ≥ 5 cm veya ≥ 4° hareket; aksi halde 0.5 sn'de bir yarım ağırlık; ilk 6 kare daima | |
| Örnekleme | 56 sütun, ≤ 104 satır (≤ 6000 nokta) | yönetilen kod bütçesi |
| Eşleme denetimi | 12 nokta; 24 denemede `Assumed`; isabetsiz 16'da `Unverifiable` | takılamaz |
| Seviye sınırı | 12; zayıf seviyeler budanır | |

**Oluştur** (`R/Runtime/Scanning/`)

| Dosya | API |
|---|---|
| `DepthNoise.cs` | `Sigma`, `HitTolerance`, `MissMargin`, `Band`, `HeightWeight` |
| `HeightHistogram.cs` | `Add`, `Decay`, `TryNearestMode(center, radius, minCount, out height, out sharp)`, `CountAround`, `Clear` |
| `FrameCellSet.cs` | açık adresli küme, `Add(x, z, bin)`, `NextFrame()` |
| `HeightLock.cs` | `struct` (alan olarak saklanır, property değil): `Update(target, dt, plane, limitDown)` |
| `FloorModel.cs` | `State`, `LevelId`, `Height`, `DrawnHeight`, `FromPlane`, `CameraP80`; `NoteCamera`, `Update`, `NoteBelow`, `ForceTrust`, `OnMerged`, `Reset`; `event StateChanged` |
| `MappingSelfCheck.cs` | saf durum makinesi (test edilebilir) |
| `CameraPoseBuffer.cs` | 32'lik halka; `Push`, `TrySample` |
| `DepthSnapshot.cs` | kalıcı `NativeArray<ushort> depth`, `NativeArray<byte> confidence`; poz, projeksiyon, `WorldToMap` |
| `RoomModelBuilder.cs` | `Rebuild(map, dt, all, sink)`: kopyala → filtrele → mesh → `StoreShown`. Hem `RoomScanner` hem test yardımcısı `ShowAll` kullanır |

**Değiştir**

- `ScanFrame.cs` (eklemeli): `Add(point, facesUp, confidence)`, `ConfidenceAt`, `Weight`, `Time`, `AllowCarving`, `Transform(in Matrix4x4)`.
- `DepthImageMapping.cs`: `DepthGeometry.IsUpFacing(tA, tB, toCamera, minCosine)` — normali kameraya çevirir, `n.y ≥ minCosine·|n|` ister (K11).
- `DepthScanSource.cs`: yumuşatılmış derinlik yedeği kaldırılır; yakalama `RenderPipelineManager.beginCameraRendering` içinde ve yalnızca istek varken; `readsWithoutDepth` yalnızca `TryReadFrame`'de artar; `Assumed` iken `AllowCarving = false`.
- `SurfaceEvidence.cs`: `PrecisePriorWeight 0.25`, `HideThreshold 0.12`, `ConfirmedLogOdds 0.4`, `ReflectionHitWeight 0.1`; ağırlık alan overload'lar.
- `SurfaceGrid.cs`: `Reset(..., priorWeight)`, `LogOddsAt`, `WasKept/SetKept`.
- `SurfaceRegionFilter.cs`: `Apply(grid, in SurfaceFilterSettings)`; `Apply(grid)` = `Legacy` (mevcut testler dokunulmaz).
- `SurfaceLevel.cs`: seviye başı histogram kaldırılır; `PlaneHeight`, `PlaneArea`, `ConfirmedArea`, `IsFloor`, `IsSuppressed`, `IsGhost`, `DrawnHeight => heightLock.HasValue ? heightLock.Value : Height`; `BeginPriors/EndPriors` (yalnızca değişen chunk kirli); `Absorb`: `logOdds = max(...)`.
- `RoomSurfaceMap.cs`: `PriorsNeedConfirmation`; `AddPrior` imzası aynı (alan, rasterlenen hücreden); `Raycast(ray, max, out pose, out levelId)`; `LevelMerged` / `LevelRemoved` olayları; birleştirmede zemin hayatta kalır.
- `ArcorePlanePriors.cs`: `trackingState != Tracking` düzlemler atlanır.
- `RoomScanner.cs`: `PriorsNeedConfirmation = source.IsPrecise`; `UpdatePlaneSuppression()` (ilk derinlik karesine kadar ARCore düzlemleri görünür kalır); anahtar kare kapısı; `Rebuild` → `RoomModelBuilder`; `public void SetFloorAt(Vector2)`; `ConfigureForTests(..., precise, sparse, ...)`.
- `RoomScanProgress.cs`: yeni metinler `FindingFloor`, `DepthRecalibrated`, `DepthUnusable`, `ShinyFloor`, `SetFloorButton`.
- `StatusMessageResolver.cs`: tarama metni "önce telefonu ayağınızın yanındaki zemine tutun" ile başlar.
- `ScanPanel.cs` + HUD: opsiyonel `scan-set-floor` düğmesi.

**Çekirdek algoritma**

```
Integrate(frame):
  ClassifyPoints(); RefineHeights(); DiscoverLevels(); MergeCloseLevels(); PruneLevels()
  floor.Update(levels); CollectVotes(); ApplyVotes(W)

ClassifyPoints:                       // nokta başına bir kez; sonraki tüm geçişler bunu kullanır
  if d < 0.3 || d > 4.5 || camP80 - p.y > 1.9: skip
  if floor.State == Trusted && p.y < F - Band(d):          // ışını zeminde kes
      q = ray ∩ plane(F); c = CellOf(q)
      kind = floorLevel.LogOdds(c) >= 0.4 ? Reflection : floorLevel.HasPrior(c) ? Suspect : Below
      if kind == Below && conf >= 160 && upFacing: floor.NoteBelow(p.y, c, W)   // gerçek alt zemin kanıtı
      p = q; clipped = true; upFacing = false
  canHit    = conf >= 128 && 0.4 <= d <= 3.5 && (!HasNormals || upFacing)
  canHeight = conf >= 160 && 0.5 <= d <= 3.0 && (!HasNormals || upFacing) && !clipped
  canCarve  = conf >= 180 && frame.AllowCarving && |v.y| >= 0.15 * d

Fuse(L):                              // yükseklik birleştirme
  found = heights.TryNearestMode(L.Height, 0.15, FusionCells, out hD, out sharp); hA = L.PlaneHeight
  if found && agree(hD, hA ∈ [-0.06, +0.08]): L.Height = hA + clamp(hD - hA, -down, up)
  else if found && L.IsFloor && hA: L.Height = hA     // zeminde düzlem yetkili; 40 kare keskin tepe varsa hD
  else if found: L.Height = hD
  else if !L.HasDepthHeight && hA: L.Height = hA
  // aksi halde son birleştirilmiş yükseklik korunur

FloorModel.Update:
  eligible = !IsGhost && 0.8 <= camP80 - Height <= 1.9 && (PlaneArea >= 1 || ConfirmedArea >= 1)
  pick = lowest eligible; 0.20 m içinde desteği 2 katı olan üst seviye varsa o seçilir
  Candidate -> Trusted: 12 kare aynı seçim && max(PlaneArea, ConfirmedArea) >= 2.0
  Trusted olunca: prior'lu ve logOdds < 0 zemin hücreleri 0'a çekilir; F - 0.06 altı seviyeler bastırılır
  Trusted -> Candidate: alt kümede >= 200 hücre-kare (±3 cm) veya F - 0.06 altında >= 1 m² izlenen düzlem

CollectVotes:
  clipped && Reflection -> zemin hücresine zayıf isabet
  canHit  -> en yakın seviyeye isabet
  canCarve-> ışının geçtiği her seviyeye kaçırma; (zemin && Trusted) atlanır
```

**Değişen testler:** `EnsureLevel_HeightsWithinTenCentimeters_...` yeniden adlandırılır;
`AddPrior_MarksCellsInsideThePlane_AsSurface` ikiye bölünür; `Integrate_PriorSeenThroughOnce_...` ve
`Raycast_HitsTheNearestShownSurface_...` `PriorsNeedConfirmation = false` kullanır; `ShowAll` → `RoomModelBuilder`.

**Yeni testler:** `FloorModelTests`, `HeightHistogramTests`, `HeightLockTests`, `DepthNoiseTests`,
`MappingSelfCheckTests`, `CameraPoseBufferTests`; harita testleri (`...SeatSeenThroughAfterChairMoved_IsCarved`,
`...SameSceneAt3480And19200Points_GivesTheSameLevels...`); benzetim (`SimulationOptions`):
`GlossyFloor_ReflectionsMakeNoHolesAndNoLevelBelow`, `LowConfidenceFloor_CoverageAtLeast90Percent...`,
`DeskPlaneFirst_FloorEndsAtZero`, `PoseLag_DoesNotCarveTheFloor`, `StartOnAPodium_...`, `UserStandsStill_...`.

---

## P2 — Harita uzayı ve anchor

- **Oluştur** `R/Runtime/Scanning/MapFrame.cs`: `Begin(origin, anchors, seedWorld)`, `TryAttachFloor(plane)`,
  `OnPlaneRemoved`, `Refresh(out moved, out turned)`, `AddHeightOffset`, `UseTransformForTests`, `WorldToMap/MapToWorld`.
  - Poz (XZ + yalnızca yaw) **serbest anchor**'dan (`ARToolkitManager.CreatePlacementParent` kalıbı).
  - Zemin `Trusted` olunca `AttachAnchor(floorPlane, pose)` — **yalnızca yükseklik farkı** kullanılır
    (düzlem anchor'ının yaw'ı poligon büyürken dönebilir). Çağrı korumalı + `try/catch`.
  - Kök `"Room Surfaces (scanned)"` artık `XROrigin.TrackablesParent` altında.
- `RoomScanner.cs`: `LateUpdate → mapFrame.Refresh`; sıçrama > 2 cm / 0.5° → 0.5 sn duraklat; > 10 cm / 3° → `Reverify()`;
  anchor izlenmiyorken bütünleştirme durur; harita kökünden > 8 m noktalar atlanır;
  `TryGetHit(screen, out SurfaceHit)`, `CreateContentHolder`, `Bind/Unbind/MoveBound`; `OnApplicationPause(false)` → `Reverify()`.
- `ArcorePlanePriors.Apply(..., Matrix4x4 worldToMap)`.
- `ARToolkitSurface.shader`: `_GridFrame` (ızgara kökeni xz, cos, sin); varsayılan bugünkü çıktıyı verir.
- `ARToolkitManager.CreatePlacementParent`: model varsa tutucu `CreateContentHolder`'dan; izleme kaybı > 2 sn → `Reverify()`.
- `ToolkitObject`, `ObjectManipulator`, `InteractionController`, `EditPanel`, `TransformFormatter`:
  nesne seviyesine bağlanır; `Raise` yüzeyin altına inemez; panel "Yüzeyden yükseklik" gösterir.
- `StatusMessageResolver`: izleme yokken taramada `TrackingLostWhileScanning`.

**Değişen test:** `StatusMessageResolverTests.AR_Scanning_ExplainsTheRoomScan_OnceTracking` (ikinci beklenti).
**Yeni:** `MapFrameTests`, `TransformFormatterTests`; play mode `RootFollowsTheMapFrame`,
`PlacedObject_FollowsItsLevelHeight`, `PlacedObject_StaysBoundWhenItsLevelMerges`, `DeletedObject_LeavesNoHolder`.

---

## P3 — Çizim ve örtme (occlusion)

**Render kuyrukları** (`R/Runtime/Core/RenderOrder.cs`)

| Sabit | Kuyruk | İçerik |
|---|---|---|
| `ContactDepth` | 1940 | 4 yuva × 3 (yeni) |
| `SurfaceDepth` | 1952 | 8 yuva × 3 (eskiden 1960 × 2) |
| `RoomOccluder` / `PlaneFill` / `PlaneOutline` | 1988 / 1990 / 1991 | değişmez |
| `ObstacleFill` / `ObstacleOutline` | 1992 / 1993 | yeni (P4, P6) |
| `SurfaceMark` | 1995 | değişmez |

**Geçiş durumları**

| Geçiş | ZTest | ZWrite | Stencil (bit 128) | Vertex |
|---|---|---|---|---|
| Depth Mark | LEqual | Off | Always, Replace | kameraya `f(d)` kadar çekilir |
| Depth Unmark (yeni) | LEqual | Off | Equal, Zero | `f(d)` kadar itilir |
| Depth Write | Always | On | Equal, Zero | ölçek `1.004` |
| Occluder | LEqual | On | — | üst `height − 0.015` |
| Taranmış dolgu/kenar (düzeltmeli) | LEqual | Off | — | sabit oran 0.004 |
| ARCore düzlemi, halkalar | LEqual | Off | düzlem bitleri | bugünkü çekme (tarama öncesi tek gürültü koruması) |
| Engel/duvar dolgu-kenar | LEqual | Off | — | metrik çekme 0.10 m |

`pull(d) = clamp(0.02 + 0.01·d², 0.03, 0.10)` m; `f(d) = min(pull / H, 0.25)`. Unmark, hayalet bir yüzeyin
arkasındaki gerçek derinliği korur. `PlaneGrouping.StencilBitCount` 8 kalır (bit 128 kuyruk 1980'den önce temizlenir).

**Hide Planes:** dolgu, kenar, engel ve duvar gizlenir; Depth Mark/Unmark/Write, occluder ve temas koruması **kalır**.

**Değiştir**
- `ARToolkitSurfaceDepth.shader`: `_NoiseBase`, `_NoiseK`, `_MinPull`, `_MaxPullHeight`, `_MaxPull`, `_Direction`, `_PushBack`.
- `SurfaceDepthMaterials.cs`: `(Material, firstQueue, slots)` + mevcut tek parametreli overload; `Mark/Unmark/Write`.
- `SurfaceMeshBuilder.cs`: `BuildSteady` (`dilate(Shown ∪ Confirmed, 1)`), `BuildOccluder` (yalnızca teyitli hücreler),
  `BuildFloorWindow` (kamera merkezli 12 × 12 m). Yeni mesh'ler yerel y = 0'da. Mevcut `Build(...)` değişmez.
- `RoomSurfaceView.cs`: `SetLevelHeight` seviye transform'unu taşır (yükseklik kayması yeniden mesh gerektirmez);
  `AssignDepthSlots()` — zemin önce, sonra alan sırası; boş seviyeler yuva tutmaz; `Trusted` zemin pencere mesh'i kullanır.

**Oluştur** `R/Runtime/Placement/ContactDepthView.cs`: tarama öncesi veya düzeltme kapsamı dışındaki nesnelerin
tabanına Mark/Unmark/Write dörtgenleri. Yalnızca `Mode == AR` ve occlusion açıkken (Desktop'ta asla).
`PlacedObjectRegistry.Objects` eklenir.

**Değişen testler:** `SurfaceDepthMaterialsTests` (3 kuyruk/yuva, `_Direction`, Unmark),
`RoomScannerPlayModeTests.ScannedFloor_GetsADepthCorrection_...` (`AreSame` yerine kapsama denetimi).
**Yeni:** `BuildSteady_CoversEveryShownCell`, `BuildOccluder_UsesConfirmedCellsOnly`,
`TrustedFloor_GetsTheWholeFloorSteadyMesh`, `NinthLevel_GetsNoSlot_FloorKeepsIts`, `DesktopMode_HasNoContactDepthRenderer`.

---

## P4 — Engel katmanı

**Sabitler**

| Sabit | Değer |
|---|---|
| İsabet bandı | zeminden `max(0.08, 3σ(d))` … 2.0 m; 0.30 m altı yalnızca d ≤ 2.5 m |
| Gövde dışlama | `cam.xz − 0.2·forward.xz` noktasına yatay uzaklık < 0.45 m (kullanıcının kendi bacakları) |
| İsabet / kaçırma, kelepçe | 0.25 / 0.15, [−1.0, 2.0] |
| Engelli açık | `logOdds > 0.5` **ve** en az 1.5 sn arayla isabet (geçen kişi mobilya değildir) |
| Engelli kapalı | `logOdds < 0.2` veya son isabetten > 0.5 sn sonra hücrede zemin görüldü |
| Tepe | mutlak harita Y'si olarak saklanır |
| Asgari bileşen | 8 hücre |
| Kademe (podyum) | alan ≥ 1.5 m² ve F + 0.40 altı → engel değil, yüzey |
| Kutu | doluluk ≥ 0.6, değilse kontur prizması; dolgu 2.5 cm; 5 cm / 5° üstü harekette yeniden çizim |
| Yerleştirme reddi | hacim yaşı ≥ 3 sn, güven ≥ 0.5, `t_enter < t_hit − 0.02`, vuruş kutunun kendi üst yüzeyi değil |
| Türler | Table: üst 0.55–1.2 m, ≥ 0.3 m² · Seat: 0.30–0.55 m, 0.08–0.5 m² · Wall: yükseklik ≥ 1.5 m, kalınlık ≤ 0.25 m, uzunluk ≥ 1.0 m · Other |

**Oluştur** (`R/Runtime/Scanning/`): `ObstacleVolume.cs` (`enum ObstacleKind`, `readonly struct ObstacleVolume` — `XRBoundingBox` benzeri, harita uzayında),
`ObstacleMap.cs`, `ObstacleGrid.cs`, `GridRay.cs` (Amanatides–Woo DDA), `ObstacleClusterer.cs`,
`MinAreaRectangle.cs` (dönen kumpas), `ObstacleMeshBuilder.cs`; `R/Shaders/ARToolkitObstacle.shader`.

```
Yalnızca floor.State == Trusted iken:
  her sınıflanmış, kesilmemiş nokta (conf >= 128, 0.4 <= d <= 3.5), gövde kapsülü dışında:
      h = p.y - F
      bant içindeyse AddHit(cell, p.y); |h| <= HitTolerance ise AddFloorSeen(cell)
  zeminde biten her ışın (conf >= 180): XZ DDA; ışın yüksekliği [F+0.08, Top-0.05) ise AddMiss(cell)
Kümeleme (her yeniden çizim):
  mask = IsBlocked || (kademe olmayan, F+0.15 üstü seviyede gösterilen hücre)
  3x3 kapama; 8-bağlı etiketleme; < 8 hücre at
  dışbükey zarf -> en küçük alanlı dikdörtgen (oda yaw'ına yakınsa ona hizala) -> kutu; tepe = %90'lık
```

**Değiştir:** `RoomSurfaceMap` (engel haritası + hacimler, `Raycast` reddi), `SurfaceGrid` (`Blocked`, `Covered`),
`SurfaceRegionFilter` (engelli hücre asla gösterilmez; Blocked/Carved/Covered delik doldurulmaz),
`RoomModelBuilder`, `RoomScanner` (`Obstacles`, `ObstaclesChanged`), `RoomSurfaceView` ("Obstacle Fill/Outline"),
`ToolkitTheme` + `ToolkitAssets` (`obstacleMaterial`, `obstacleEdgeMaterial`, `obstacleShader`; dolgu = vurgu rengi α 0.18),
`RoomScanProgress` ("· N obstacles", `ObstacleThere`), `InteractionController`.

**Örtme:** kutular derinlik **yazmaz** (kaba kutu sanal nesneyi kesmesin). Masa/oturaklar P3 seviye occluder'ını korur;
diğer engeller canlı ARCore derinliğine dayanır.

**Yeni testler:** `ObstacleMapTests` (1.5 sn kuralı, kullanıcının ayakları yok sayılır, 3.5 m gürültüsü engel üretmez),
`GridRayTests`, `MinAreaRectangleTests`, `ObstacleClustererTests`, `Raycast_BehindAnObstacle_IsRefused`;
benzetim `ThinLegChair_BlocksTheFloorUnderIt`, `MovedChair_FreesTheFloor`, `PersonWalkingThrough_LeavesNoObstacle`.

---

## P5 — Temiz geometri

| Sabit | Değer |
|---|---|
| Kapama + açma | 3 × 3 (zemin ve yükseltilmiş) |
| Bileşenler | zemin: en büyük + ≥ 0.25 m²; yükseltilmiş ≥ 0.10 m² |
| Delik doldurma | zemin ≤ 0.5 m², yükseltilmiş ≤ 0.25 m²; Blocked/Carved/Covered asla |
| Douglas–Peucker ε | 0.04 m zemin, 0.03 m yükseltilmiş |
| Oda yaw'ı | kenar açılarının mod 90° histogramı (uzunluk ağırlıklı), yavaş güncelleme |
| Hizalama kapısı | ±10°; alan > %15 veya köşe > 10 cm değişirse reddet |
| Dikdörtgen kabulü | poligon / dikdörtgen alanı ≥ 0.85 |
| Izgara aralığı | 0.25 m varsayılan, config 0.10–1.00 m |

**Oluştur:** `MaskMorphology.cs`, `ContourTracer.cs`, `PolygonSimplifier.cs`, `RoomAxes.cs`, `PolygonSnapper.cs`,
`PolygonFill.cs`, `PolygonRaster.cs` (`RoomSurfaceMap.AddPrior` tarama çizgisi dolgusundan çıkarılır).

```
Seviye başına: mask = Kept && !blocked -> Close, Open -> bileşenler, delik doldurma
  alan = mask ? +1 : -1 -> bulanıklaştır -> marching squares -> parçalar
  ContourTracer.Chain -> Simplify -> RoomAxes.AddEdge -> hizala -> (yükseltilmişse) dikdörtgen uydur
  dolgu = PolygonFill.Trapezoids; kenar = gönyeli şeritler
  Drawn = PolygonRaster(loops) && !blocked && state != Carved;  level.StoreShown(Drawn)
```

**Değiştir:** `SurfaceMeshBuilder.BuildClean(grid, in SurfaceBuildSettings, output)` (eski `Build` kalır),
`SurfaceRegionFilter` (`Floor`/`Raised` ayarları), `RoomModelBuilder` (sıra: filtre → `BuildClean` → `StoreShown(Drawn)`),
`RoomScanner.FinishScan` (tam yeniden çizim, `RoomAxes.Freeze()`, kutular ve kilitler dondurulur),
`RoomSurfaceView` (`_GridFrame` = harita yaw + oda yaw; tiling `1 / gridCellSize`), `ARToolkitConfig.gridCellSize`.

**Kabul kapıları:** mevcut `DeskArea_MatchesTheRealDesk` ve `DeskPlane_NoLongerReachesOverTheChairAndFloor` düzenlenmeden geçmeli.
**Yeni:** her yeni sınıf için test + `BuildClean_DrawnMaskNeverCoversCarvedOrBlockedCells`, `Outline_DoesNotCreepOverTenRebuilds`.

---

## P6 — Duvarlar (oda sınırı)

Kullanıcı kararıyla tam aşama; `ARToolkitConfig.detectWalls` varsayılan **true**.

**Dikey düzlem algılamayı güvenli açmak için zorunlu korumalar**

| Dosya | Değişiklik |
|---|---|
| `SceneBuilder.cs`, `SceneUpgrader.cs`, `ARToolkitManager.EnsurePlaneSurfaces` | `requestedDetectionMode = detectWalls ? Horizontal \| Vertical : Horizontal` |
| `R/Runtime/Placement/ARPlacementSurface.cs` | `Hits[0]` yerine düzlemi `alignment.IsHorizontal()` olan ilk vuruş (yoksa duvara nesne yerleşir) |
| `R/Runtime/Placement/ARPlaneSurfaces.cs` | `ApplyVisibility`: yatay olmayan düzlemlerin ham görseli daima kapalı (dünya-XZ UV'si duvarda çizgi çizgi görünür) |
| `ARToolkitManager.PlaneCount` | yalnızca yatay düzlemler sayılır |

**Oluştur**

| Dosya | API |
|---|---|
| `R/Runtime/Scanning/WallModel.cs` | `public readonly struct WallSegment { Vector2 A, B; float Bottom, Top; float Confidence; bool FromPlane; }`; `internal sealed class WallModel`: `BeginPlanes()`, `AddPlane(polygonWorld, worldToMap)`, `EndPlanes()`, `SetObstacleWalls(volumes)`, `Rebuild(floorY, roomYaw)`, `Segments`, `Blocks(cell)`, `Clear()` |
| `R/Runtime/Scanning/WallMeshBuilder.cs` | zeminden `min(Top, F + 2.4)`'e panel + taban şeridi + üst kenar çizgisi |
| `R/Runtime/Scanning/ArcoreWallPriors.cs` | izlenen, yutulmamış dikey düzlemleri `WallModel`'e verir (`ArcorePlanePriors` kalıbı) |

**Kaynaklar ve kurallar**

| Kural | Değer |
|---|---|
| Kaynak A | ARCore dikey düzlemi: dikey uzanım ≥ 1.0 m veya üst ≥ F + 1.5 m; uzunluk ≥ 0.6 m |
| Kaynak B | P4 engel kümeleri, tür `Wall` (dokusuz duvarlarda ARCore düzlem bulamaz; derinlik sütunları yedek) |
| Birleştirme | yön farkı ≤ 10°, dik uzaklık ≤ 0.15 m, boşluk ≤ 0.5 m → tek segment (uzunluk ağırlıklı) |
| Hizalama | oda yaw'ına ±10° içinde hizalanır; duvarlar `RoomAxes`'e 3× ağırlıkla oy verir |
| Köşe | birbirine dik iki segmentin uçları 0.4 m içindeyse kesişime uzatılır |
| Zemin etkisi | duvar altındaki hücreler (kalınlık 0.10 m) Blocked; ana zemine bağlı olmayan, duvar arkası < 1.0 m² bileşenler atılır |
| Yerleştirme | vuruştan önce duvar panelini kesen ışın reddedilir (`RoomScanText.WallThere`) |
| Örtme | duvarlar derinlik yazmaz |

**Çizim:** `ARToolkitObstacle.shader`'a `_FadeHeight` eklenir (tabanda güçlü, yukarı doğru sönen alfa).
Kuyruklar 1992 / 1993. "Hide Planes" ile gizlenir. İlerleme metni "· N walls".

**Testler:** `WallModelTests` (dikey poligondan segment, eşdoğrusal birleştirme, yaw'a hizalama, köşe kapama, hücre engelleme),
`RoomSurfaceMapTests.Raycast_ThroughAWall_IsRefused`, benzetim `RoomWithWalls_FloorEndsAtTheWalls`,
play mode `PlacementSurface_WithAVerticalPlane_IgnoresIt`, `HidePlanes_HidesWalls`.

**Risk:** dikey düzlem algılama ARCore işlem yükünü artırır → cihaz protokolünde açık/kapalı kare süresi ölçülür; config'den kapatılabilir.

---

## P7 — Performans

**7a (her zaman)**

| Öğe | Değişiklik |
|---|---|
| asmdef'ler | `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics` referansları + sürüm tanımları (6000.6'da bunlar "shim" paket; önce tanımların çözüldüğü doğrulanır) |
| `DependencyInstaller.cs` | üç paket `RequiredPackages`'a eklenir |
| `DepthImageMapping.TryGetViewport` | `TryGetPixel`'in tersi; ekran dışı derinlik pikselleri kullanılır olur |
| `DepthUnprojectJob.cs` (yeni) | `[BurstCompile] IJobParallelFor`; satır başı adım `max(1, floor(0.05 / (d·pixelAngle)))`, 6000 nokta bütçesi |
| Zamanlama | `Stopwatch.GetTimestamp` (release'de de çalışır) + `ProfilerMarker` |
| Uyarlamalı hız | tarama CPU > 6 ms veya `deltaTime > 40 ms` → 4 Hz bütünleştirme; toparlanma `≤ 36 ms` ve < 3 ms, 5 sn |
| Isı | 10 sn'de bir `PowerManager.getCurrentThermalStatus`; MODERATE → düşük hız; SEVERE → `FinishScan()` + uyarı |
| Tarama tamam ipucu | 20 sn kapsama artmazsa "Tarama tamam görünüyor" |

**7b (yalnızca ölçüm gerektirirse):** hücre depolaması `NativeParallelHashMap` + kalıcı havuzlara; tek Burst `ScanIntegrateJob`
(oylama + tepe testi + DDA). Tetik: düşük segment telefonda bütünleştirme + engeller > 4 ms veya yeniden çizim > 8 ms.

---

## P8 — Aşamalar arası testler ve yükseltme denetimi

| Öğe | İçerik |
|---|---|
| `R/Tests/Support/` (yeni asmdef) | `RoomSimulator`, `SimulatedRoomSource : IScanFrameSource` (iki test derlemesi de kullanır) |
| Uçtan uca | `SimulatedClassroom_EndToEnd`: masa + sandalye + parlak zemin + duvar `RoomScanner` üzerinden |
| Kayma / yeniden konumlanma | `DriftJump_FloorConvergesWithinThreeSeconds...`, `RelocalisationTenCentimetersDown_DemotesAndRecovers`, `AnchorRemoved_IsRecreated` |
| `R/Tests/Performance/` (yeni asmdef) | `Integrate_19200Points_8Levels`, `Rebuild_20x15mRoom`, `Integrate_AllocatesNothing` |
| Yükseltme | 1.0.0 proje kopyasına yeni paket → Run Setup → build → çalıştır |
| Desktop | her aşamadan sonra `DemoScenePlayModeTests`; Windows build |
| README | tarama bölümü, sınırlamalar, sorun giderme, yükseltme adımları |

---

## Editor ve göç güvenceleri

| Yol | Güvence |
|---|---|
| Run Setup | tema malzemeleri + shader referansları, düzlem prefab'ı, stencil-güvenli URP, açık sahneler, Depth Optional |
| Toolkit penceresini açmak | yalnızca okur |
| Toolkit'ten / batch build | `BuildRunner.Build` onarır, sonra derler |
| Unity'nin kendi Build diyaloğu | `ToolkitPreBuild` onarır; hâlâ eksikse "Run Setup" ile build'i durdurur |
| Eski sahneler (çalışma anında) | `ARToolkitManager.Awake` eksik bileşenleri ekler, ofseti ve arka plan modunu ayarlar |
| Eski HUD UXML | yeni öğeler opsiyonel |

## Riskler

| Risk | Önlem |
|---|---|
| ARCore zemin düzlemi yanlış | 40 kare keskin tepe varsa derinlik yukarı yönde düzeltebilir; fark loglanır; "Set Floor" düğmesi |
| Masa zemin sanılır | kamera penceresi 0.8–1.9 m; güven 2.0 m² ister; alt düzlem/küme ile düşürme; silme yerine bastırma |
| Parlak fayansta güvenli derinlik az | zemin düzlem prior'ından gösterilir; güven düzlem alanından; kullanıcıya ipucu |
| Yanlış eşleme varsayıldı | `Assumed` iken oyma yok; yeniden denetim; değişimde harita temizlenir |
| Hayalet engeller | gövde dışlama, zaman ve güncellik kuralları, gürültüye duyarlı bant, 8 hücre asgari |
| İnce sandalye ayakları derinlikte görünmez | oturak seviyesi ayak izini engeller; kutu oturak + sırtlıktan gelir |
| Yuvarlak masalar kareleşir | 0.85 alan testi + ±10° kapısı |
| Isı ve pil | anahtar kare kapısı, uyarlamalı hız, ısı durumu, uyku engeli yalnızca kullanımda |
| Vulkan ve GLES3 farkı | yalnızca ZTest, stencil, vertex ölçekleme (fragment derinliği yazılmaz); ikisi de denetim listesinde |
| Benzetim testleri elle izlendi | ilk çalıştırma karar verir; başarısızlıkta test değil boru hattı düzeltilir |

## Cihazda ölçüm protokolü (P0 build'i ile)

1. Eşleme: `Verified`'a kadar süre, dört adayın hatası, `Assumed` sıklığı.
2. Derinlik gecikmesi `frameNs·1e-9 − depth.timestamp`: işaret ve büyüklük.
3. Poz politikası A/B: düz zeminde ~30°/sn kaydırma; zemin artık eğimi karşılaştırılır.
4. Derinlik çözünürlüğü ve parlak fayansta güvenli piksel oranı.
5. Zemin artığı − menzil → `DepthNoise.Base`, `K`.
6. `h_D − h_A` işareti/büyüklüğü → zemin kelepçesi.
7. Yer gerçeği: telefon şerit metreyle 1.00 m'de; `cameraY − FloorHeight` = 1.00 ± 0.02.
8. Yansıma sayısı; zeminin altındaki seviye/düzlem yükseklikleri.
9. 5 dakikalık yürüyüşte anchor sıçramaları.
10. 10 dakikada tarama CPU süreleri, kare süresi, ısı durumu; dikey düzlem algılama açık/kapalı farkı.
11. Vulkan, sonra GLES3.

## Doğrulama

**Editor (her aşamadan sonra):** Unity MCP ile derleme + konsol denetimi (`Unity_ReadConsole`), ardından
`Tools > Erasmus AR Toolkit > Developer > Run Edit Mode Tests` ve `Run Play Mode Tests`
(`DemoScenePlayModeTests` dahil). Editor kapalıysa: `Unity.exe -batchmode -runTests`.

**Cihaz denetim listesi** (orta + düşük segment derinlikli telefon, bir derinliksiz telefon)

| # | Adım | Beklenen | Belirti |
|---|---|---|---|
| 1 | Kur, logcat aç | "Shader ... is missing" yok; 2 sn'de bir tanılama satırı | S2 |
| 2 | Taramayı başlat, ayağının yanındaki zemine bak | ilk derinlik karesine kadar ARCore düzlemleri kalır; ~5 sn'de zemin `Trusted` | S3 |
| 3 | Telefon ölçülü 1.00 m'de | `cameraY − FloorHeight` = 1.00 ± 0.02 | S1 |
| 4 | Bir fayans köşesini işaretle; 1 m ve 3 m'den bak | ızgara köşede kalır; yürürken batma yok | S1 |
| 5 | Masaların altına/yanına bak | altta ikinci ızgara yok; parlak zeminde delik açılmaz | S1, S3 |
| 6 | Taramadan önce zemine kutu koy | alt kenar düz | S2 |
| 7 | Taranmış zeminde kenara/deliğe yakın kutu koy | alt kenar düz; "Yüzeyden yükseklik 0.00" | S2 |
| 8 | "Lower"a tekrar tekrar bas | kutu yüzeyde durur | S2 |
| 9 | Sandalyeleri itilmiş masaları tara | masa kenarı masada biter; sandalye üstünde masa ızgarası yok | S4 |
| 10 | Zemindeki sandalye ayaklarına bak | 2 m içinde ~5 cm üstünde ayakta ızgara yok; sandalye izinde zemin ızgarası yok | S4 |
| 11 | Kendi ayaklarına bak, yürü | durduğun yerde engel/delik yok | S5 |
| 12 | Kutulanmış sandalyeyi çek | kutu ve engelli hücreler birkaç saniyede temizlenir | S5 |
| 13 | Duvarlara bak (dokulu ve düz) | oda yönüne hizalı duvar panelleri; zemin poligonu duvarda biter | S5 |
| 14 | Taramayı bitir | zemin düz kenarlı tek poligon; masalar dikdörtgen; ızgara oda eksenlerinde | S3 |
| 15 | Sandalyeye, sonra masa üstüne dokun | sandalye mesajla reddeder; masa üstü kabul eder | S5 |
| 16 | Hide Planes | ızgara, kutu, duvar kaybolur; kutu yine gerçek masanın arkasında gizlenir | S2, S4 |
| 17 | Kamerayı 5 sn kapat; uygulamayı duraklat/sürdür | tarama yeniden doğrulanır veya yeniden tarama istenir | S1 |
| 18 | 5 dk tarama; 40 dk ders | %95 kare süresi ≤ 40 ms; ısı ≤ MODERATE | — |
| 19 | Derinliksiz telefon | izleme noktalarıyla tarar, "daha az hassas" mesajı | — |
| 20 | 4, 7, 10'u GLES3'te tekrarla | aynı sonuç | tümü |

## Bilinen sınırlamalar ve kapsam dışı

- Taramadan **önce** ham ARCore zarfları bugünkü gibi görünür (S4 yalnızca "Scan Room" sonrası düzelir) — README'de belgelenir.
- Görsel hedef (image target) içeriği temas koruması almaz.
- Gelecek: TSDF mesh; Inference Engine ile monoküler derinlik / anlamsal bölütleme; keyfi malzemeler için
  yumuşak örtme shader'ı; elle engel düzenleme; büyük salonlar için bölgesel anchor; oturumlar arası kalıcılık.

## Kaynaklar

Ayrıntılı İngilizce şartname ve kanıtlar (uygulamanın ilk adımında `Docs/RoomScanV2-Design.md` olarak repoya kopyalanır):

- Revize tasarım (950 satır, tüm sabitler, sözde kod, eleştiri çözüm tablosu):
  `C:\Users\Halil\AppData\Local\Temp\claude\C--Unity-ErasmusARToolkit\92dc87bf-0742-4395-90a0-10710d50c5de\scratchpad\revised_plan.md`
- Aynı klasörde: `verified.txt` (doğrulanmış hatalar), `unverified.txt`, `research.txt` (ARCore API + algoritmalar), `critiques.txt`.

**Kritik dosyalar:** `R/Runtime/Scanning/RoomSurfaceMap.cs`, `RoomScanner.cs`, `DepthScanSource.cs`,
`RoomSurfaceView.cs`, `SurfaceLevel.cs`, `SurfaceMeshBuilder.cs`; `R/Shaders/ARToolkitSurface*.shader`;
`R/Runtime/Core/ARToolkitManager.cs`; `R/Editor/Services/ToolkitAssets.cs`, `BuildRunner.cs`.
