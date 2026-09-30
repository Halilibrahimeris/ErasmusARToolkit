# Plan: Proje modülleri (Toolkit 1.2)

Hackathonun dört teması için toolkit'e **sekiz proje türü** eklenir. Her tür bir modüldür: toolkit
penceresinde bir bölüm, birkaç alan ve bir **Add** düğmesi (Add Design, Add Portal, Add NPC…). Ekip
değerleri girer, düğmeye basar, sahnede çalışan bir örnek oluşur; gerisini Inspector'da ya da
Unity'nin Scene görünümünde kendi içeriğiyle değiştirir.

**Nasıl kullanılacak:** ekipler projelerini yalnızca Unity içinde üretir, build alır, kendi
telefonlarında dener. Jüri uygulamayı telefonda dener, puanı kâğıda yazar. Sunum, problem tanımı,
jüri gibi kısımlar fizikseldir; toolkit'e girmez.

Yol kısaltmaları: `R` = `Assets/ErasmusARToolkit`, `C` = `Assets/ARToolkitContent`.

---

## 1. Kapsam

| Modül | Ne yapar | Temalar |
|---|---|---|
| **M1 Tasarım gezintisi** | Unity'de tasarlanan mekânın içinde telefonla yürünür; AR ↔ Sanal; nesnelerde kart | 4 (ve 2, 3) |
| **M2 Canlanan poster** | Basılı poster kameraya girince üstünde video, ses ya da 3D model | 2, 3 |
| **M3 Duvar sergisi** | Resim, video, 3D eser duvarlara asılır; her eserin kartı | 1, 4 |
| **M4 Okul rotası** | Okulun her yerine işaret; durak durak içerik, "3 / 6", sonraki durağın yönü | 3, 2 |
| **M5 Farklı gözlerle bak** | Renk körlüğü, az görme, tünel görüş filtreleri; tasarımda tekerlekli sandalye göz yüksekliği | 3 |
| **M6 Odayı dolduran simülasyon** | Odada su, duman ya da nesne seviyesi yükselir; gerçek eşyalar içinde kalmış görünür | 2 |
| **M7 Portal** | Odada bir kapı; içinden sanal bir dünya görünür, kapıdan geçen o dünyanın içinde kalır | 2, 3 |
| **M8 NPC hikâyesi** | Taranan odada karakterler yürür, konuşur; hikâye dokunarak ya da yaklaşınca ilerler | 1, 3, 4 |

**Kapsam dışı (kararlaştırıldı):** yapay zekâ entegrasyonu; oylama, iğne, geri bildirim, sonuç
dosyaları, sunucu, telefonlar arası paylaşım (uygulama veri toplamaz); quiz, ders anlatımı, ölçüm ve
hesap; erişilebilirlik ayar paneli; kartta kaynak satırı; masaüstünde simüle oda.

**Karar verilmedi, şimdilik yok:** çok dil (HUD ve kartların dil düğmesi), sesli kart (metni
okuma). Ekipler kart metinlerini istedikleri dilde yazabilir; HUD İngilizce kalır.

---

## 2. Toolkit penceresi: Projects sekmesi

Pencereye Content'ten sonra yeni bir sekme gelir. Her modül bir katlanır bölümdür: iki satırlık
açıklama, gerekli alanlar, **Add** düğmesi ve sahnede / config'de var olanların listesi (Select, Remove).

```
 Setup | Scene | Content | Projects | Build | Export
 ─────────────────────────────────────────────────────────────────
 ▼ Design Walkthrough
   Design a space in Unity and walk through it with the phone.
   Room size  [ 8.0 ] x [ 6.0 ] x [ 3.0 ] m    [x] Create room shell
   Start height [ 1.5 ] m     Sky color [■]
   [ Add Design ]   [ Add Info Card To Selection ]   [ Look Through Start Camera ]
 ▶ Living Poster
 ▶ Wall Gallery
 ▶ School Route
 ▶ Vision Simulator
 ▶ Room Fill
 ▶ Portal
 ▶ NPC Story
```

- Sahneye eklenenler (tasarım, portal, simülasyon, filtre, hikâye) açık toolkit sahnesinde bir
  nesne olur ve Add'den sonra seçili gelir; bütün ayrıntılı alanlar Inspector'dadır.
- Görsele ya da yerleştirmeye dayananlar (poster, rota durağı, sergi eseri) config'e bir kayıt
  ekler; Content sekmesinde de görünür ve düzenlenir. Görsel kütüphanesi bunlarla kendiliğinden
  yeniden kurulur (bugünkü `ReferenceLibraryBuilder`, değişiklik gerekmez).
- Sahnede toolkit yoksa bölüm "önce Scene sekmesinden AR sahnesi oluşturun" der.
- Setup sekmesine modüllerin ihtiyaç duyduğu iki satır eklenir (M5, M8).

---

## 3. Ortak altyapı (M0)

Her modülün kullandığı parçalar önce kurulur.

| Parça | Ne |
|---|---|
| `ToolkitModule` (taban bileşen) | Sahnedeki `ARToolkitManager`'ı bulur; mod (AR / Desktop) belli olunca `OnModeReady` çağırır; HUD'a düğme ve satır ekler; yok edilince temizler |
| `ModulePlacement` | Bir modülün gerçek dünyada nereye konacağı: **At App Start** (uygulamanın başlatıldığı yer ve yön; tasarım gezintisi gibi) ya da **Tap To Place** (kullanıcı zemine dokunur, modül kullanıcıya dönük orada belirir). AR'da bir `ARAnchor`'a asılır; oda taranmışsa taranan zemine oturur. **Move Here** düğmesi yeniden koyar |
| `FloorFinder` | Zeminin yüksekliği: taranmış zemin, yoksa telefonun altındaki en alçak ARCore düzlemi, Desktop'ta sanal zemin |
| HUD genişletmesi | UXML'e isteğe bağlı üç parça: modül düğmeleri şeridi (nesne çubuğunun üstünde), altyazı satırı (M8), bilgi satırı (durum satırının altında: rota, filtre adı, seviye). `ARToolkitManager.Hud` ile erişilir. Eski UXML'de yoksa modüller düğmesiz çalışır ve bunu bir kez loglar |
| Dokunma yönlendirme | `InteractionController`'a `ITapHandler` yığını: yerleştirme bekleyen modül, hikâyede "devam" dokunuşu önce sorulur; kimse almazsa bugünkü davranış (seç / yerleştir) |
| `InfoCard` | Sahnedeki herhangi bir nesneye kart (başlık, metin). Çalışma anında nesne `ToolkitObject` olur: dokununca 3D kart ve HUD'da **salt okunur** panel (ölçek / döndür / sil satırları gizli). Tasarım, portal dünyası ve NPC'ler kullanır |
| `CameraImageSwitch` | Kamera görüntüsünü kapatıp açar (`ARCameraBackground`, izleme sürer); kapalıyken arka plan gökyüzü ya da renk, gerçek dünya örtmesi kapalı. Desktop'ta webcam arka planı. M1 Sanal ve M7 içi kullanır |
| Projects sekmesi iskeleti | `ProjectsTab.cs` + `ProjectsTab.uxml`, modül başına bir bölüm sınıfı (`IProjectSection`) |
| Sürüm | `ToolkitPaths.Version = "1.2.0"`; yeni içerik klasörleri `C/Gallery`, `C/Route` |

Geriye uyum: yeni bileşenler eski sahnelerde yoktur ve hiçbir şeyi değiştirmez; yeni config alanları
varsayılanlarla yüklenir; `StatusMessages`, `RoomScanText` ve genel API'ler korunur.

**Testler:** `TapRoutingTests`, `ModulePlacementTests` (uygulama başı ve dokunmayla poz, yalnız yaw,
zemin yüksekliği), `InfoCardTests`; Play Mode (Desktop): modül düğmesi HUD'a eklenir, dokunmayla
yerleştirme sanal zeminde çalışır, eski sahne değişmeden açılır.

---

## M1 — Tasarım gezintisi

**Projects → Design Walkthrough:** oda ölçüsü (en × boy × yükseklik), oda kabuğu oluştur (açık /
kapalı), başlangıç yüksekliği (1,5 m), gökyüzü rengi → **Add Design**. Sahnede tasarım varsa düğme
**Select Design** olur. Ayrıca **Add Info Card To Selection** ve **Look Through Start Camera**.

**Oluşan:** `Design` (`DesignSpace`) ve altında:
- `Start Camera` (`DesignStartPoint`): Scene görünümünde kamera gibi çizilir, render etmez. Kamera
  tasarımın neresine konursa telefon orada başlar, baktığı yön telefonun ilk yönü olur.
- `Virtual Only`: yalnız Sanal görünümde görünenler (oda kabuğu: zemin, dört duvar, tavan).
- Scene görünümünde 1 m'lik ızgara ve tasarımın ölçüsü ("8,0 × 6,0 × 3,0 m"): yanlış ölçekli
  modeller hemen görülür.

**Telefonda:**
- Tasarım, uygulamanın başlatıldığı yere oturur: başlangıç kamerası telefonun yerine gelir (yalnız
  yatay konum ve yön). Gerçek duvarlara hizalama yok; boş bir alan yeter.
- Zemin: başta telefon yüksekliği − başlangıç yüksekliği; ARCore zemini bulunca ona oturur.
- HUD: **AR / Virtual** (Sanal: kamera kapalı, `Virtual Only` açık; düğmeye basılı tutunca kamera
  geçici olarak görünür; ilk geçişte "etrafınızı göremezsiniz, yavaş yürüyün" uyarısı) ve **Move Here**.
- Nesneye dokununca kart.

**PC'de:** Play'de kamera başlangıç kamerasından başlar, **yürüme modu** (WASD, sağ sürükleme ile
bakış, F başa döner); AR / Virtual düğmesi webcam arka planını açar / kapar.

**Dosyalar:** `R/Runtime/Modules/Design/{DesignSpace, DesignStartPoint, DesignTour, DesignPlacementMath}.cs`,
`R/Editor/Modules/{DesignSection, DesignSpaceEditor, RoomShellBuilder}.cs`,
`R/Runtime/Desktop/DesktopCameraController.cs` (yürüme modu).
**Testler:** `DesignPlacementMathTests`, `DesignTourTests` (Virtual Only, arka plan); Play Mode:
kamera başlangıçta, tıklayınca kart.
**Kabul:** başlangıç kamerası odanın ortasında → telefonda bir alanın ortasında başlatılınca tasarım
gerçek boyutta çevrede; AR ↔ Sanal; basılı tutunca kamera; kart açılır; büyük boş alanda da aynı.

---

## M2 — Canlanan poster

**Projects → Living Poster:** görsel (PNG / JPG), basılı genişlik (m), video (isteğe bağlı), ses
(isteğe bağlı), 3D model (isteğe bağlı), döngü, kart başlığı / metni → **Add Poster**.

**Oluşan:** config'de bir görsel hedef kaydı; yeni alt alan `media { video, audio, loop, volume }`.

**Telefonda:** poster görülünce video posterin tam üstünde (görselin ölçüsü ve oranında) oynar, ses
başlar; poster kaybolunca durur, tekrar görülünce kaldığı yerden sürer. Varsa 3D model de çıkar.
Posterin paneline **Pause / Play** eklenir.

**PC'de:** webcam ile tanınan posterde ve sanal zemine serilen posterlerde aynı.

**Dosyalar:** `R/Runtime/Core/ImageTargetItem.cs` (`PosterMedia`), `R/Runtime/Modules/Poster/PosterPlayer.cs`
(VideoPlayer + AudioSource), `ToolkitObjectFactory.SpawnImageContent` (medya varsa ekler),
`R/Editor/Modules/PosterSection.cs`.
**Testler:** `PosterLayoutTests` (video karesinin ölçüsü ve oranı), `ConfigValidatorTests` (videosuz,
modelsiz ve kartsız poster uyarısı); Play Mode: simüle posterde video oynar, içerik gizlenince durur.
**Kabul:** poster görülünce video ve ses; kaybolunca durur; Android'de H.264 MP4 oynar.

---

## M3 — Duvar sergisi

**Projects → Wall Gallery:** resim ya da video, genişlik (m), çerçeve (açık / kapalı, renk), başlık,
metin → **Add Exhibit**. Ayrıca herhangi bir yerleştirilebilir nesne Content sekmesinde **Place On:
Wall** yapılabilir (3D eserler için).

**Oluşan:** `C/Gallery/<başlık>.prefab` (çerçeve + resim ya da video yüzeyi; pivot arkada, duvara
değen yüzde) ve config'de `placeOn = Wall` olan bir yerleştirilebilir.

**Telefonda:** alt çubuktan eser seçilir, duvara dokunulur, eser oraya asılır (duvara bakan, dik).
Duvar kaynağı: taranmış duvarlar; tarama yoksa ARCore'un dikey düzlemleri (duvar eseri varken dikey
düzlem algılama kendiliğinden açılır). Sürükleyince duvar boyunca kayar; Raise / Lower yukarı aşağı;
döndürme duvarın normali etrafında. Zemine dokununca "Duvara dokunun".

**PC'de:** duvar eseri varken sanal zeminin arkasında sanal bir duvar belirir; eserler oraya asılır.

**Dosyalar:** `PlaceableItem.placeOn`, `InteractionController` (duvar yolu),
`ARPlacementSurface.TryGetWallPose`, `RoomScanner.TryGetWallHit`, `DesktopPlacementSurface` (sanal
duvar), `ObjectManipulator` (döndürme ekseni), `R/Editor/Modules/{GallerySection, ExhibitPrefabBuilder}.cs`.
**Testler:** `WallPoseTests` (normal, dik duruş, en düşük yükseklik), `ExhibitPrefabBuilderTests`
(oran, pivot); Play Mode: sanal duvara asma, zemine dokununca ret.
**Kabul:** resim / video eser duvara dokunulan yerde asılı, kartı açılır, taşınır, silinir.

---

## M4 — Okul rotası

**Projects → School Route:** rota adı; durak alanları: görsel (ya da **Generate Marker**: toolkit
benzersiz bir işaret üretir, `C/Route/` altına), basılı genişlik, başlık, metin, sonraki durağın
tarifi ("Kütüphane, üst kat"), ok (Sol / Sağ / Düz / Yukarı / Aşağı / Arka), isteğe bağlı 3D model →
**Add Stop**. Durak listesi sıralanabilir (↑ ↓), Remove.

**Oluşan:** config'de bir görsel hedef kaydı; yeni alt alan `routeStop { order, nextHint, arrow }`.

**Telefonda:** bilgi satırı "Route · 2 / 6". Durak görülünce kartı açılır, görselin yanında sonraki
yönü gösteren 3D ok ve "Sonraki: Kütüphane, üst kat" yazısı; bilgi satırı da oku ve tarifi gösterir.
Duraklar sırasız da bulunabilir; hepsi bulununca "Route complete". İlerleme yalnız uygulama açıkken
tutulur.

**PC'de:** simüle ve webcam ile tanınan duraklarda aynı.

**Dosyalar:** `ImageTargetItem` (`RouteStop`), `R/Runtime/Modules/Route/{RouteTracker, RouteProgress,
RouteArrow}.cs`, `R/Editor/Modules/RouteSection.cs` (`MarkerGenerator` ile işaret üretimi).
**Testler:** `RouteProgressTests` (sıra, sırasız bulma, tamamlama), `ConfigValidatorTests` (aynı sıra,
boş tarif); Play Mode: simüle durak bulununca satır ve ok.
**Kabul:** 3 duraklı rota; her durakta içerik, ok ve "n / 3"; sonunda tamamlandı mesajı.

---

## M5 — Farklı gözlerle bak

**Projects → Vision Simulator:** sunulacak filtreler (işaret kutuları): kırmızı körlüğü (protanopi),
yeşil körlüğü (döteranopi), mavi körlüğü (tritanopi), renksiz görme, az görme (bulanık), tünel görüş,
katarakt (puslu); her filtre için isteğe bağlı kısa açıklama; tasarım gezintisi için göz
yükseklikleri (tekerlekli sandalye 1,2 m, çocuk 1,0 m) → **Add Vision Simulator**.

**Telefonda:** HUD düğmesi **Vision: Normal** sıradaki filtreye geçer; bilgi satırında filtrenin adı
ve açıklaması. Filtre kamera görüntüsüne ve 3D nesnelere uygulanır; kartlar ve HUD okunur kalır.
Tasarım gezintisinin Sanal görünümünde **Eye: Standing / Wheelchair / Child** düğmesi sanal dünyayı
seçilen göz yüksekliğine göre kaydırır (AR'da gerçekle kayacağı için yalnız Sanal'da).

**Nasıl:** kameranın önünde tam ekran bir yüzey, kameranın o anki görüntüsünü (URP opaque texture)
filtreleyerek çizer; opaque texture yalnız filtre açıkken o kamerada istenir. Renk körlüğü için
Machado (2009) matrisleri, bulanıklık ve tünel görüş aynı shader'da.

**Setup:** yeni satır "Opaque texture at full resolution" (Android URP varlıklarında opaque
downsampling = None; yoksa filtreli görüntü yarı çözünürlükte bulanık olur). Runtime asmdef URP'ye
başvurur (`ERASMUS_HAS_URP` sürüm tanımıyla).

**Dosyalar:** `R/Runtime/Modules/Vision/{VisionSimulator, VisionFilter}.cs`, `R/Shaders/ARToolkitVision.shader`,
`R/Materials/ARToolkitVision.mat` (temaya; build'e girsin diye), `R/Editor/Modules/VisionSection.cs`,
`UrpRendererConfigurator` (satır).
**Testler:** `VisionFilterTests` (matrislerin satır toplamları, sıra ve döngü), `EyeHeightTests`;
Play Mode: düğme filtreyi değiştirir, "Normal"de yüzey ve opaque texture kapalı.
**Kabul:** telefonda her filtre kamera görüntüsünde görünür, kare hızı kabul edilir; göz yüksekliği
Sanal'da değişir.

---

## M6 — Odayı dolduran simülasyon

**Projects → Room Fill:** tür (Water / Smoke / Objects), en yüksek seviye (m), süre (s) ya da elle
kaydırıcı, renk, nesne prefab'ı (Objects için; ör. şişe), alan (taranmış zemin ya da en × boy),
seviye dolunca gösterilecek yazı ("Bir yılın plastiği"), yerleştirme (Tap / At App Start) →
**Add Room Fill**.

**Telefonda:** HUD: **Start / Pause**, 0–100 % kaydırıcı, bilgi satırında seviye ("45 cm").
- Water: yarı saydam, hafif dalgalı yüzey. Alan oda taranmışsa taranan zeminin sınırı, değilse
  dikdörtgen. Gerçek dünya örtmesiyle, yüzeyden yüksek masalar görünür kalır, altında kalanlar su
  içinde kalmış görünür.
- Smoke: üst üste yarı saydam, gürültülü katmanlar.
- Objects: prefab kopyaları katman katman seviyeye kadar dizilir (hafif rastgele); oda taranmışsa
  engellerin içine konmaz. En çok 1500 kopya, GPU instancing.

**PC'de:** sanal zeminde dikdörtgen alan.

**Dosyalar:** `R/Runtime/Modules/Fill/{RoomFill, FillArea, FillSurface, ObjectPile}.cs`,
`R/Shaders/ARToolkitFill.shader`, `R/Editor/Modules/FillSection.cs`, `RoomScanner.TryGetFloorOutlines`
(taranmış zeminin sınırı, dünya uzayında).
**Testler:** `FillLevelTests` (süre, kaydırıcı, sınırlar), `ObjectPileTests` (katmanlar, engel hücreleri,
kopya sınırı), `FillAreaTests` (taranmış sınır, dikdörtgen yedek); Play Mode: seviye yükselir, durur.
**Kabul:** su seviyesi zamanla yükselir, gerçek masalar örtme ile doğru görünür; nesneler zemini
katman katman doldurur.

---

## M7 — Portal

**Projects → Portal:** kapı genişliği ve yüksekliği, çerçeve rengi, iç dünya ölçüsü (en × yükseklik ×
derinlik), gökyüzü rengi, iç dünya prefab'ı (isteğe bağlı; yoksa örnek: zemin, gökyüzü, birkaç
nesne), yerleştirme (Tap / At App Start) → **Add Portal**.

**Oluşan:** `Portal` ve altında `Frame`, `Mask`, `Inside World` (ekip içeriğini buraya koyar,
nesnelere kart eklenebilir), `Sky`.

**Telefonda:**
- Dışarıdan: iç dünya yalnızca kapının içinden görünür.
- Kapıdan geçince: dünya her yanda; geriye dönünce kapıdan gerçek oda görünür. İçerideyken gerçek
  dünya örtmesi kapanır (yoksa gerçek duvarlar sanal dünyayı keserdi), kapıdan çıkınca açılır.
- Kapıdan geçmeden arkasına dolaşılırsa dünya gizlenir, yalnız çerçeve görünür.

**Nasıl:** iç dünyanın çevresinde, önünde kapı biçiminde deliği olan görünmez bir derinlik kabuğu
dünyadan önce çizilir (bugünkü occluder shader'ı, ayrı render sırası). Stencil kullanılmaz: sekiz
stencil bitinin hepsi düzlem ızgaraları ve tarama derinlik düzeltmesi tarafından kullanılıyor.

**PC'de:** masaüstü kamerasıyla kapıya yürünüp girilir.

**Dosyalar:** `R/Runtime/Modules/Portal/{PortalController, PortalCrossing, PortalMaskBuilder}.cs`,
`R/Editor/Modules/PortalSection.cs`, `RenderOrder.PortalMask`.
**Testler:** `PortalMaskBuilderTests` (delik, kabuk yüzleri), `PortalCrossingTests` (önden giriş,
arkadan dolaşma, geri çıkış); Play Mode: kamera kapıdan geçince durum ve örtme değişir.
**Kabul:** dışarıdan kapıdan bakınca dünya; içeri girince her yanda; geri dönünce gerçek oda;
arkadan dolaşınca yalnız çerçeve.

---

## M8 — NPC hikâyesi

**Projects → NPC Story:**
- **Add Story**: sahneye `Story` kökü (yerleştirme: Tap / At App Start; "oda taraması önerilir"
  notu).
- **Add NPC**: ad, karakter modeli (isteğe bağlı; yoksa toolkit'in basit karakteri), renk, yürüme
  hızı → seçili hikâyenin altına.
- **Add Point**: hikâyenin altına adlandırılmış bir nokta ("Kapı", "Masa yanı"); Scene görünümünde
  işaret olarak çizilir.
- Adımlar `Story` Inspector'ında sıralanabilir bir listedir. Adım: karakter · eylem (Say, Walk To,
  Turn To User, Play Animation, Wait, Show, Hide) · metin · ses · nokta · animasyon tetikleyici ·
  süre · devam (Auto, Tap, User Near n m).

**Telefonda:**
- Hikâye yerleşince oda taranmışsa taranan zeminden, engeller ve duvarlar çıkarılarak bir yürüme
  alanı (NavMesh) kurulur. Tarama yoksa ARCore zemin düzlemi kullanılır (engelsiz). Noktalar en
  yakın yürünebilir yere oturur.
- Karakterler sandalye ve masaların etrafından yürür; gerçek bir masanın arkasına geçince gerçek
  dünya örtmesi onları gizler.
- Konuşma: başın üstünde balon, HUD'da altyazı ("Ayşe: …"), varsa ses dosyası.
- HUD: **Next** (adım dokunma bekliyorsa), **Restart**, **Pause**. Karaktere dokununca kartı açılır.
- Animasyon: toolkit'in basit karakteri yürürken sallanır. Ekibin Animator'lü modelinde `Speed`
  (float) ve `Talk` (bool) parametreleri varsa kullanılır, Play Animation bir tetikleyici çalar.

**PC'de:** sanal zeminde aynı akış (engelsiz).

**Setup:** Unity'nin yerleşik AI modülü (`com.unity.modules.ai`, NavMesh) gerekli paketlere eklenir.
AI Navigation paketi gerekmez.

**Dosyalar:** `R/Runtime/Modules/Story/{StoryStage, StoryCharacter, StoryPoint, StoryStep, StoryRunner,
StoryNavMesh, SpeechBubble, SimpleCharacterMotion}.cs`, `R/Editor/Modules/{StorySection, StoryStageEditor,
SimpleCharacterBuilder}.cs`, `RoomScanner` (NavMesh için zemin, engel ve duvar kaynakları),
`DependencyInstaller` (AI modülü).
**Testler:** `StoryRunnerTests` (sıra, Auto / Tap / User Near, Restart, eksik karakter),
`StoryNavMeshTests` (engel ve duvarın kesilmesi, nokta oturtma); Play Mode: iki karakterli, dört
adımlı hikâye sanal zeminde sonuna kadar oynar.
**Kabul:** taranan odada karakter kapıdan masaya engellerin etrafından yürür, konuşur, altyazı çıkar;
dokununca ilerler; masanın arkasında gizlenir.

---

## M9 — Örnekler ve belgeler

- Her Add düğmesi tek başına çalışan bir örnek kurar (ilkel geometriden içerik), ekip onu kendi
  içeriğiyle değiştirir. Ayrı şablon sistemi yoktur.
- `Samples/Scenes/`: her modül için küçük bir örnek sahne (`DemoSceneGenerator`'a "Generate Module
  Demos"). Bir sahnede birden çok modül olabilir; iki modül aynı anda **At App Start** ise Projects
  sekmesi uyarır.
- README (İngilizce): modül başına bölüm (ne yapar, alanlar, telefonda nasıl denenir, kısıtlar).
  `Docs/ProjectModules-Guide-tr.md`: ekipler için kısa Türkçe kılavuz (modül → Add → alanlar →
  build → telefonda deneme).
- `Docs/ProjectModules-Implementation.md`: ne yapıldı, plandan sapmalar, açık maddeler.

---

## 4. Sıra ve boyut

| Adım | İçerik | Boyut |
|---|---|---|
| M0 | Ortak altyapı, Projects sekmesi | M |
| M1 | Tasarım gezintisi | M |
| M2 | Canlanan poster | S |
| M3 | Duvar sergisi | M |
| M4 | Okul rotası | S–M |
| M5 | Farklı gözlerle bak | S–M |
| M6 | Odayı dolduran simülasyon | M |
| M7 | Portal | M |
| M8 | NPC hikâyesi | L |
| M9 | Örnekler ve belgeler | M |

S yarım gün, M 1–2 gün, L 3+ gün. Her adımın sonunda proje derlenir, bütün testler geçer (bugün
522 Edit Mode + 47 Play Mode). Commit'ler sizin.

---

## 5. Riskler

| Risk | Etki | Karşılık |
|---|---|---|
| Boş, düz renkli alanlarda ARCore izlemesi zayıf | M1, M7, M8: içerik kayar | İyi ışık, yavaş yürüme, zemine desenli işaret; Move Here düğmesi |
| Sanal görünümde gerçek engeller görünmez | M1, M7: çarpma | Basılı tutunca kamera; uyarı; kılavuzda "yanınızda biri olsun" |
| `ARCameraBackground` kapatılınca URP'de davranış | M1 Sanal, M7 içi | M1'in ilk işi telefonda denemek; olmazsa kameranın önüne tam ekran sanal arka plan |
| Oda taraması telefonda ölçülmedi (RoomScanV2 açık madde) | M3, M6, M8 taranmış veriyle | Tarama yokken de çalışan yedek yol (ARCore düzlemleri); taramalı yol telefonda denenir |
| Android'de video biçimi | M2, M3 video oynamaz | H.264 MP4; Content'te uyarı; README'de biçim notu |
| Opaque texture maliyeti | M5 kare hızı | Yalnız filtre açıkken istenir; tam çözünürlük Setup satırıyla |
| Derinlik kabuğu sanal nesneleri gizler | M7: portal arkasındaki yerleştirilmiş nesne görünmez | Kabuk yalnız iç dünya kadar büyük; README notu |
| NavMesh kurma süresi | M8 telefonda takılma | Yalnız yerleşirken ve tarama bitince; alan 10 m ile sınırlı |
| Ağır içe aktarılmış modeller | M1, M7, M8 kare hızı | Tasarım istatistiği (üçgen sayısı) ve uyarı |
| HUD'da yer | Çok modül bir sahnede | Modül düğmeleri ayrı şeritte; yalnız sahnedeki modüller düğme ekler |

---

## 6. Onay

Plan onaylanınca M0 ile başlanır, sırayla M9'a kadar gidilir. Sırada değiştirmek istediğiniz ya da
çıkarmak istediğiniz modül varsa söyleyin.
