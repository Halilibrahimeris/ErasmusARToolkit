# Proje modülleri: uygulama kaydı

Plan: `Docs/HackathonFeatures-Plan.md`. Yol kısaltmaları: `R` = `Assets/ErasmusARToolkit`,
`C` = `Assets/ARToolkitContent`.

## Durum (2026-10-01)

| Adım | Durum |
|---|---|
| M0 Ortak altyapı, Projects sekmesi | Bitti |
| M1 Tasarım gezintisi | Bitti |
| M2 Canlanan poster | Bitti |
| M3 Duvar sergisi | Bitti |
| M4 Okul rotası | Bitti |
| M5 Farklı gözlerle bak | Bitti |
| M6 Odayı dolduran simülasyon | Bitti |
| M7 Portal | Bitti |
| M8 NPC hikâyesi | Bitti |
| M9 Örnekler ve belgeler | Bitti |

Testler (M9 sonu, projenin kopyasında batch modda): 636 Edit Mode testinin hepsi geçiyor. Play Mode'da
63 testten 56'sı geçiyor, 1'i atlanıyor (webcam açıkken `SimulatedImage_InView_IsFound`). Düşen 6 test
modül çalışmasından önceki commit'te (`5c7f306`) de aynı şekilde düşüyor; nedenleri batch ortamı:

- `Screenshot_WithHud_IsCaptured`, `SelectingAnObject_OpensEditPanel_ThatEditsIt`: batch modda
  `WaitForEndOfFrame` gelmiyor.
- `TrustedFloor_GetsTheWholeFloorSteadyMesh`: "Could not start graph" (webcam açık editörde meşgul).
- `DriftJump_WhileScanning_TheFloorComesBack`, `Relocalisation_WhileScanning_IsCheckedAndPutRight`,
  `HidePlanes_KeepsDepthPartsAndHidesObstacles`: batch modda tarama zamanlaması.

M5'te açık editörde bu testlerin hepsi geçiyordu. Editör açılınca Play Mode testlerini editörden bir kez
daha koşun.

## Yapılanlar

- **M0:** `ToolkitModule` taban sınıfı, `ModulePlacement` (At App Start / Tap To Place, anchor'ı takip
  eder, ona bağlanmaz), `FloorFinder`, `InfoCard`, `CameraImageSwitch` (`manager.CameraImage`),
  `ITapHandler` yığını, HUD modül şeridi, bilgi satırı ve altyazı, salt okunur düzenleme paneli,
  masaüstü yürüme modu, Projects sekmesi (`SceneProjectSection<T>`, `ProjectSection`).
- **M1:** `DesignSpace`, `DesignStartPoint`, oda kabuğu, AR / Virtual düğmesi (basılı tutunca oda
  görünür), Scene görünümünde ölçü ızgarası, üçgen sayısı uyarısı.
- **M2:** `PosterMedia` (video, ses), `VideoSurface`, `PosterPlayer`, panelde Pause / Play, MP4 uyarısı.
- **M3:** `PlaceOn.Wall`, duvar pozu (taranmış duvar, ARCore dikey düzlem, masaüstünde sanal duvar),
  duvar boyunca sürükleme ve duvar normali etrafında döndürme, sergi prefab üretici (`C/Gallery`).
- **M4:** `RouteStop`, `ImageTargetEvents.Found`, `RouteTracker`, 3D ok ve "Next" kartı, sıralanabilir
  durak listesi, işaret üretimi (`C/Route`).
- **M5:** `ARToolkitVision.shader` (tam ekran, opaque texture), `VisionSimulator` (7 filtre, göz
  yükseklikleri), tema malzemeleri.
- **M6:** `RoomFill` (su, duman, nesneler; Start / Pause, kaydırıcı, bilgi satırı), `FillLevel`,
  `FillArea` (taranmış zemin ya da dikdörtgen), `ObjectPile` (katman katman, engellerin içi boş, en çok
  1500), `ObjectPileRenderer` (GPU instancing), `RoomScanner.TryGetFloorOutline`.
- **M7:** `PortalController`, `PortalCrossing` (Outside / Inside / Behind), `PortalMeshes` (kapısı açık
  kabuk, kapı, gök), `ARToolkitDepthClear.shader`, `RenderOrder.PortalMask = 1989`, örnek iç dünya
  (çayır, ağaçlar, kaya, bilgi kartlı tabela).
- **M8:** `StoryRunner` (saf adım makinesi), `StoryStep` (Say, Walk To, Turn To User, Play Animation,
  Wait, Show, Hide; Auto, Tap, User Near), `StoryCharacter` (NavMeshAgent ya da düz yürüme, Animator
  `Speed` / `Talk`), `StoryPoint`, `StoryNavMesh` (zemin kutusu, engel ve duvar kutuları),
  `StoryStage` (baloncuk, altyazı, Next / Restart / Pause, dokunuş devam ettirir), Projects sekmesinde
  Add Story, Add NPC To Story, Add Point To Story.
- **M9:** README'de "Project modules" bölümü, `Docs/ProjectModules-Guide-tr.md`, geliştirici menüsünde
  **Generate Module Demos** (`Samples/Scenes/Modules`, Build Settings değişmez).

## Plandan sapmalar

- **Video ve AI yerleşik modülleri artık gerekli.** Bu projede Video modülü kapalıydı. `manifest.json`'a
  `com.unity.modules.video` ve `com.unity.modules.ai` eklendi, `DependencyInstaller` bunları kurar, toolkit
  assembly'leri `ERASMUS_HAS_VIDEO` ve `ERASMUS_HAS_AI` olmadan derlenmez (ARF ve Input System gibi).
- Runtime assembly URP'ye başvuruyor (`ERASMUS_HAS_URP`).
- At App Start modüller masaüstünde yerinden oynamaz; kamera başlangıç noktasından yürür.
- Göz yüksekliği, tasarımın `ModulePlacement.HeightOffset` değeriyle uygulanır; yalnız Sanal görünümde.
- Görme filtresi kuyruk 2900'de çizilir: kartlar (3000) filtrelenmez ve okunur kalır.
- Opaque texture tam çözünürlük kontrolü ayrı satır yerine Setup'taki "Phone rendering tuned" satırına
  eklendi.
- Rota okları kartta yazıyla gösterilir (TMP yazı tipinde ok işaretleri olmayabilir).
- Modül örnek malzemeleri `C/Modules/Materials` altına yazılır.
- **Portal stencil kullanmıyor.** Stencil bitlerinin hepsi dolu. Dışarıdayken kabuk bir örtücüdür
  (kuyruk 1989), kapı deliği derinliği uzak düzleme siler; içerideyken kapı deliği örtücü olur ve gerçek
  dünya örtmesi askıya alınır.
- Hikâye adımlarının özel Inspector çizicisi (`StoryStepDrawer`) yapılmadı; alanların tooltip'i hangi
  eylemde kullanıldığını söyler.
- Konuşma balonu mevcut `InfoLabel` ile, ses karakterin `AudioSource`'u ile; ayrı `SpeechBubble` sınıfı yok.
- Doldurma yüzeyi mevcut `PolygonFill` ile üçgenlenir; ayrı bir fill shader'ı yerine M5'te eklenen
  `ARToolkitFill` malzemesi kullanılır.
- M6–M9 sırasında açık editör bir pencerede kilitli kaldı (bilgisayar kullanımı izni verilmedi).
  Derleme ve testler projenin bir kopyasında batch modda koşuldu; üretilen `.meta` dosyaları, Depth Clear
  malzemesi ve tema değişikliği projeye geri kopyalandı.

## Telefonda denenecekler

- M1: Sanal görünümde `ARCameraBackground` kapatma, projeksiyon, bir karelik geçiş.
- M2: Android'de H.264 MP4 oynatma, izleme kaybında durup sürme.
- M3: Tarama olmadan dikey düzlem bulunması, resmin duvarda kalması.
- M4: Gerçek işaretlerle sıra ve "Route complete".
- M5: Kare hızı ve opaque texture'ın AR kamera görüntüsünü içermesi.
- M6: Taranmış zeminde su yüzeyi, masaların kuru kalması, 1500 nesnede kare hızı.
- M7: Kapıdan geçiş, içeride kapıdan gerçek odanın görünmesi, derinlik silme shader'ının Adreno/Mali'de
  doğru çalışması.
- M8: Taranmış odada NavMesh kurulması, karakterlerin eşyaların etrafından yürümesi, User Near mesafesi.
