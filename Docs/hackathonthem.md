# Hackathon Temaları ve Erasmus AR Toolkit İçin Özellik Fikirleri

Kaynak: `DigiCollab_01_Project_and_Challenge_Reveal.pptx` ve `DigiCollab_02_Thematic_Presentations.pptx`
(Balıkesir, 30 Eylül – 3 Ekim 2026). Bu belge temaları özetler ve her tema için toolkit'e eklenebilecek
özellikleri listeler. Karar verilmiş bir plan değildir; seçim yapmak için bir fikir havuzudur.

---

## 1. Hackathon özeti

**Görev:** Topluluğunuzda öğrenmeyi, çalışmayı ya da yaşamayı **daha kapsayıcı, daha sürdürülebilir ya da
dijital olarak daha yetkin** yapan dijital bir çözüm tasarlayın, prototipini yapın ve **çalışır hâlde
gösterin**.

**Dört şart**

1. Gerçek bir sorundan çıkmalı: sorunun kimde olduğu ve neden önemli olduğu söylenebilmeli.
2. Dört temadan en az birine bağlanmalı (en güçlü projeler ikisine bağlanır).
3. Anlatılmakla kalmamalı, yapılmalı: web sayfası, uygulama, oyun, **VR/AR sahnesi**, cihaz ya da video.
4. Karma ekip: her ekipte en az iki ülkeden öğrenci.

**Takvim**

| Gün | Saat | Ne var |
|---|---|---|
| Cuma 2 Ekim | 10:00 | Problem tanımı (Empathy + Define) |
| | 11:00 | Konsept geliştirme |
| | 12:00 – 15:30 | Mentorlarla prototip |
| Cumartesi 3 Ekim | 10:00 | Son çalışma |
| | **12:00** | **Teslim (kesin)** |
| | 12:15 | Jüri sunumları (15 dk + 5 dk soru) |
| | 14:30 | Ödüller |

Toplam çalışma süresi yaklaşık 11 saat. Sunumdaki öğüt: *"Çalışan en küçük sürümü yapın."*

**Teslim edilecekler:** tek cümlelik problem tanımı, canlı gösterilen prototip, bir sayfalık özet,
5 slaytlık sunum.

**Puanlama (100 puan)**

| Ölçüt | Puan |
|---|---|
| Yenilikçilik ve yaratıcılık | 25 |
| STEAM ve yapay zekâ kullanımı | 25 |
| Uygulanabilirlik | 20 |
| Sosyal etki | 20 |
| Sunum | 10 |

**Problem tanımı kalıbı:** *How might we help [KİM] to [NE YAPSIN] so that [NEDEN ÖNEMLİ]?*

---

## 2. Dört tema

| # | Tema | Özü | Sunumdaki uyarı |
|---|---|---|---|
| 1 | Dijital okuryazarlık ve 21. yüzyıl becerileri | Aracı doğru seçmek, ürettiğini sorgulamak, onunla bir şey yapmak (DigComp: bilgi, iletişim, içerik, güvenlik, problem çözme) | Yapay zekâ kullanın ama yazdığı her satırı açıklayabilin; jüri soracak |
| 2 | Sürdürülebilirlik ve çevre bilinci | Enerji, malzeme, ulaşım, yemek ve atık, dijital ayak izi | Yeşil badana yok: bir sayı gösterin (kWh, litre, kg, km) |
| 3 | Kapsayıcılık ve sosyal eşitlik | Genelde dışarıda kalanlar: engelliler, dil öğrenenler, cihazı olmayanlar, yaşlı çalışanlar, kırsaldakiler | Küçük yazı, tek dil, yeni telefon varsayımı dışlar. Ekip dışından bir kişiyle deneyin |
| 4 | Demokratik katılım ve vatandaşlık | Okulda ya da iş yerinde insanlara söz hakkı | Asıl mesele oy düğmesi değil, döngü: topla → yayınla → karar ver → geri bildir. Kişisel veriyi az toplayın, anonimliğe izin verin |

---

## 3. Toolkit bugün ne yapabiliyor

Fikirler bu yapı taşlarının üzerine kurulur, sıfırdan başlamaz:

| Yapı taşı | Nerede |
|---|---|
| Yüzey algılama, dokunarak yerleştirme, AR anchor | `Runtime/Placement` |
| Oda taraması: zemin, yüzeyler, engeller (kutular), duvarlar | `Runtime/Scanning` |
| Gerçek dünya örtmesi (occlusion, ARCore Depth) | `Core/RealWorldOcclusion.cs` |
| Görsel hedef (image target) tanıma, PC'de OpenCV ile webcam'de | `Runtime/ImageTracking`, `Runtime/CameraFeed`, `OpenCV` |
| Bilgi kartları (TextMeshPro) | `Runtime/InfoLabels` |
| Düzenleme paneli, taşıma / döndürme / ölçekleme | `Runtime/Interaction`, `UI/EditPanel.cs` |
| HUD (UI Toolkit), ekran görüntüsü | `Runtime/UI` |
| PC'de masaüstü modu (fare + klavye, sanal zemin) | `Runtime/Desktop` |
| Kod yazmadan içerik ekleme penceresi, tek tıkla build | `Editor` |
| Projede yüklü: Unity Inference Engine (cihaz üzerinde model çalıştırma) | `Packages` |

Toolkit **kod yazmadan AR uygulaması yapma aracı**. Bu yüzden iki yol var:

- **A. Toolkit'in kendisini geliştirmek:** öğrenci ekipleri hackathonda toolkit'i kullanarak prototip
  yapar. Eklenen her özellik onlara yeni bir "hazır parça" verir.
- **B. Toolkit ile hazır bir tema uygulaması yapmak:** aşağıdaki fikirlerden biri örnek sahne olarak
  eklenir (ör. `Samples/Scenes/`), ekipler onu çoğaltıp kendi sorunlarına uyarlar.

Aşağıdaki her fikir iki yola da uyar.

**Zorluk:** S = yarım gün, M = 1–2 gün, L = 3+ gün.

---

## 4. Tema 1: Dijital okuryazarlık

### 1.1 AR ders kartı / adım adım anlatım  *(M)*
Nesneye dokununca tek bir bilgi kartı yerine **adım adım** ilerleyen bir anlatım açılır (İleri / Geri,
"Adım 3 / 7"). Örnek: bir motorun parçaları, bir devre kartı, bir tezgâh.
- Temel: `InfoLabelData`, `EditPanel`.
- Yeni: `LessonStep` listesi (başlık, metin, vurgulanacak alt nesne, isteğe bağlı ses).
- Jüri: STEAM, uygulanabilirlik. Mesleki eğitime doğrudan uyar.

### 1.2 Parça vurgulama ve patlatılmış görünüm  *(M)*
Bir modelin alt parçaları dokununca renklenir ve adını gösterir. "Patlat" düğmesi parçaları dışarı
açar, "Birleştir" geri toplar.
- Temel: `ToolkitObject`, `SelectionIndicator`, `InfoLabel`.
- Yeni: alt parça seçimi ve basit bir patlatma animasyonu (her parça merkezden dışa doğru).

### 1.3 AR quiz modu  *(S–M)*
Sahnede işaretler belirir, soru ekranda: "Emniyet valfine dokunun." Doğru / yanlış, puan, süre.
Sonunda sonuç ekranı ve ekran görüntüsü.
- Temel: `InteractionController` (dokunma), `ScreenshotService`.
- Jüri: **ölçülebilir öğrenme** (sunumdaki DigComp ön-test / son-test fikrine uyar).

### 1.4 Yapay zekâ ile nesne tanıma → bilgi kartı  *(L)*
Kamera bir aleti görür (tornavida, multimetre), küçük bir modelle tanır, üstüne bilgi kartı koyar.
- Temel: Inference Engine (yüklü), `CameraFeed/TexturePixelReader.cs`.
- Risk: model bulmak ve telefonda hız. Hackathondan önce hazır değilse **görsel hedefle (image target)
  taklit edin**: aletin üstüne küçük bir etiket yapıştırılır.
- Jüri: "STEAM ve yapay zekâ" ölçütü (25 puan) için en güçlü fikir.

### 1.5 "Bunu nasıl doğruladık" kartı  *(S)*
Yapay zekânın ürettiği her içerik kartında küçük bir "Kaynak / nasıl kontrol ettik" bölümü.
Sunumdaki "yapay zekânın yazdığı her satırı açıklayın" uyarısına doğrudan cevap.
- Temel: `InfoLabelData` alanına `source` ve `verifiedBy` eklemek.

---

## 5. Tema 2: Sürdürülebilirlik

Sunumun en önemli cümlesi: *bir sayı gösterin.* Bu yüzden buradaki fikirler ölçüm yapar.

### 2.1 Oda taramasıyla alan ve malzeme hesabı  *(M)*
Oda taraması zaten zemin alanını ve duvarları biliyor. Bunu bir hesaba çevirmek:
"Zemin 42 m², duvarlar 96 m². Boya: 9,6 litre. Kesilecek fazla parça: %8."
- Temel: `RoomSurfaceMap`, `FloorModel`, `WallModel`, `RoomScanProgress` (alan sayımı zaten var).
- Yeni: "Hesapla" paneli (malzeme türü, birim tüketim, fire oranı → litre / kg / adet / TL).
- Neden güçlü: toolkit'in **en özgün parçasını** (oda taraması) kullanır, sonuç bir sayıdır.
  İnşaat, boya, döşeme, mobilya atölyelerine uyar.

### 2.2 Boşta çalışan makine / enerji kartı  *(S–M)*
Atölyedeki makinenin üstünde (görsel hedefle) bir kart: güç (kW), bugün kaç saat boşta çalıştı,
kaç kWh ve kaç TL. Veri elle girilir ya da basit bir JSON dosyasından / web adresinden okunur.
- Temel: `ImageTargetSpawner`, `InfoLabel`.
- Sunumdaki örnekle aynı ("sensör + panel, kWh ve para olarak").
- Gerçek sensör yoksa: elle girilen değer + "sensör bağlanınca buraya gelecek" notu jüriye yeter.

### 2.3 Tamir mi, değiştirme mi: AR tamir kılavuzu  *(M)*
Bozuk bir cihazın üstünde adım adım tamir anlatımı (1.1'in üstüne kurulur). Sonunda
"Bu tamirle 3,2 kg elektronik atık ve ~X TL kurtardınız."
- İki temaya bağlanır: dijital okuryazarlık + sürdürülebilirlik.

### 2.4 Geri dönüşüm kutusu yerleştirme  *(S)*
Oda taraması sonrası sınıfa sanal geri dönüşüm kutuları yerleştirilir. Her kutuya dokununca "buraya ne
atılır" kartı. Taranan engeller sayesinde kutular sandalyenin içine değil boş zemine konur.
- Temel: mevcut yerleştirme + oda taraması. Neredeyse hiç yeni kod gerekmez; **örnek sahne** olarak iyi.

### 2.5 Ayak izi karşılaştırma nesnesi  *(S)*
"1 kg plastik bu kadar yer kaplar" gibi soyut sayıları gerçek boyutta AR küpüne çevirmek.
Kullanıcı sayıyı girer, gerçek ölçekte küp / yığın masanın üstünde belirir.
- Temel: `ToolkitObjectFactory`, gerçek ölçek (AR'da 1 birim = 1 metre).

---

## 6. Tema 3: Kapsayıcılık

Sunumdaki "iki günde yapabilecekleriniz" listesi doğrudan özellik listesidir: ekip dışından biriyle
deneyin, ikinci dil ya da sade dil ekleyin, eski telefonda çalıştırın, altyazı / kontrast / klavye
erişimi ekleyin.

### 3.1 Çok dilli içerik  *(M)*
Bilgi kartları, HUD ve tarama mesajları seçilebilir dilde: Türkçe, İngilizce, Almanca, İtalyanca
(üç ortak ülkenin dilleri). HUD'da dil düğmesi.
- Temel: `InfoLabelData`, `StatusMessageResolver`, `HudView`. Metinler şu an İngilizce ve kod içinde.
- Yeni: basit bir çeviri tablosu (ScriptableObject ya da CSV), Unity Localization paketi gerekmez.
- Neden güçlü: karma uluslararası ekip şartına ve sunumdaki "Türkçe okuyamayan birinci sınıf atölye
  öğrencisi" örneğine birebir uyar.

### 3.2 Erişilebilirlik ayarları  *(S)*
HUD'da tek panel: büyük yazı, yüksek kontrast, daha büyük düğmeler, animasyonları azalt.
- Temel: UI Toolkit USS (tema değişkenleri), `ToolkitTheme`.
- Kolay, görünür, jüriye canlı gösterilebilir.

### 3.3 Sesli anlatım  *(S–M)*
Her bilgi kartında "Dinle" düğmesi. Android'in yerleşik metin okuma (TTS) özelliği ya da önceden
kaydedilmiş ses dosyası.
- Okuma güçlüğü çekenler, dil öğrenenler ve görme zorluğu olanlar için.

### 3.4 AR / VR güvenlik brifingi  *(M)*
Sunumun kendi örneği: makine güvenlik brifingi, altyazılı, üç dilde, yapılarak öğrenilen bir gezinti.
Makinenin etrafında AR işaretleri: "Bu çizginin içine girmeyin", "Acil durdurma düğmesi burada".
Oda taraması ile güvenlik bölgesi zemine çizilir.
- Temel: oda taraması (zemin), görsel hedef (makine), 1.1 adım adım anlatım, 3.1 çok dil.
- **Üç temaya birden bağlanır** (kapsayıcılık + dijital okuryazarlık + iş güvenliği).

### 3.5 Eski telefon / ARCore'suz mod  *(S)*
Toolkit ARCore olmayan telefonda zaten sanal zeminle çalışıyor. Bunu öne çıkarmak: "Hafif mod"
seçeneği (occlusion kapalı, tarama kapalı, düşük çözünürlük) ve bunu gösteren bir ayar.
- Sunumdaki "eski bir telefonda da çalışsın" maddesi.

### 3.6 Erişilebilirlik denetimi (oda taramasıyla)  *(M–L)*
Oda taraması engelleri ve boş zemini biliyor. Bundan bir **tekerlekli sandalye geçiş kontrolü**
çıkar: engeller arası boşluk 90 cm'den darsa kırmızı işaret, "geçiş yok" uyarısı.
- Temel: `ObstacleMap`, `ObstacleGrid`, `SurfaceGrid`.
- Neden güçlü: çok özgün, gerçek bir sorun, sonuç bir sayı (cm). Toolkit'in taramasını en iyi
  sergileyen fikir. Tarama doğruluğu telefonda henüz ölçülmedi; bu bir risk.

---

## 7. Tema 4: Demokratik katılım

Sunumdaki döngü: **topla → yayınla → karar ver → geri bildir.**

### 4.1 AR öneri iğnesi  *(M)*
Okulda bir yere (görsel hedef ya da taranmış oda) sanal bir iğne bırakılır: "Buraya bir bank konsun",
"Bu lamba bozuk". Diğerleri aynı yeri kameradan görünce iğneyi ve durumunu görür:
*Alındı → Konuşuldu → Yapıldı.*
- Temel: görsel hedef ile konum, `InfoLabel`, `PlacedObjectRegistry`.
- Sunumdaki "QR tabanlı bildir ve takip et panosu" örneğinin AR hâli.
- Paylaşım için basit bir çözüm: iğneler bir JSON dosyasında ya da ücretsiz bir web servisinde.
  Hackathon için tek telefon + "sunucu gelince paylaşılır" notu da yeter.

### 4.2 Mekân düzenleme oylaması  *(M)*
Sınıf / kantin / bahçe için 2–3 sanal yerleşim hazırlanır (masaların yeri, bitkiler, bank). Öğrenciler
AR'da her birini gerçek odanın içinde görür ve oy verir. Sonuç ekranı.
- Temel: oda taraması + yerleştirme, "yerleşimi kaydet / yükle".
- Yeni: **sahne düzenini kaydetme ve geri yükleme** (toolkit'te şu an yok; başka fikirlere de yarar).

### 4.3 Anonim geri bildirim kartı  *(S)*
Her nesnenin düzenleme panelinde 👍 / 👎 ve kısa yorum. İsim sorulmaz.
- Sunumdaki veri uyarısına uyar: yalnız gerekeni toplayın, anonim bırakın.

---

## 8. Temalar arası ortak altyapı

Birkaç fikrin ihtiyaç duyduğu parçalar. Hangi fikir seçilirse seçilsin önce bunlar gelir:

| Parça | Hangi fikirler kullanır | Zorluk |
|---|---|---|
| **Adım adım anlatım (LessonStep)** | 1.1, 2.3, 3.4 | M |
| **Çok dilli metin tablosu** | 3.1, 3.4, bütün kartlar | M |
| **Sahne düzenini kaydet / yükle** | 4.1, 4.2, 2.4 | M |
| **Oda taramasından ölçüm (alan, mesafe, boşluk)** | 2.1, 3.6, 2.4 | M |
| **Bilgi kartına ses ve kaynak alanı** | 1.5, 3.3 | S |

---

## 9. Öneri: iki gün için en güçlü paket

Puanlama ve toolkit'in güçlü yanlarına bakınca en iyi dönüşü şu üçü verir:

1. **3.4 Çok dilli AR güvenlik brifingi.** 1.1 (adım adım) + 3.1 (çok dil) + görsel hedef + oda
   taramasıyla zemine güvenlik çizgisi. Üç temaya bağlanır, sunumun kendi örneği, VET'e birebir uyar.
2. **2.1 Oda taramasıyla malzeme hesabı.** Sayı gösterir (sürdürülebilirlik), toolkit'in en özgün
   parçasını sergiler.
3. **3.2 Erişilebilirlik ayarları.** Yarım gün, canlı gösterimi kolay, jüride "kapsayıcılık" sorusuna
   hazır cevap.

"STEAM ve yapay zekâ" ölçütü (25 puan) için: **1.4 nesne tanıma** hazır yetişmezse, sunumda yapay
zekânın içerik üretiminde (çeviriler, anlatım metinleri) nasıl kullanıldığını ve **1.5** ile nasıl
doğrulandığını gösterin.

**Sıralama önerisi**

1. Ortak altyapı: çok dilli metin tablosu, adım adım anlatım.
2. 3.2 erişilebilirlik ayarları.
3. 3.4 güvenlik brifingi örnek sahnesi.
4. Zaman kalırsa 2.1 malzeme hesabı, sonra 3.6 erişilebilirlik denetimi.

---

## 10. Riskler ve dikkat

- **Telefonda ölçülmedi:** oda taramasının doğruluğu gerçek cihazda henüz ölçülmedi
  (bkz. `RoomScanV2-Implementation.md`). 2.1 ve 3.6 taramanın sayılarına dayanıyor; önce telefonda
  deneyin.
- **Canlı gösterim:** jüri prototipi canlı görmek istiyor. Sahne ışığı ve parlak zemin taramayı
  bozabilir. Yedek olarak PC masaüstü modu ve kısa bir video hazır olsun.
- **Kişisel veri:** 4.x fikirleri görüş topluyor. İsim ve konum gerekmiyorsa sormayın.
- **Kapsam:** her fikri "çalışan en küçük sürüm" olarak tanımlayın. Cuma öğlen yapılmayacakları
  yazın (sunumdaki "scope creep" uyarısı).
