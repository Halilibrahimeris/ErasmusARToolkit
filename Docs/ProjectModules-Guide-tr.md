# Proje modülleri: ekipler için kısa kılavuz

Toolkit penceresini açın (**Tools > Erasmus AR Toolkit**), sahneyi **Scene** sekmesinde kurun, sonra
**Projects** sekmesine geçin. Her proje türü bir bölümdür: alanları doldurun, **Add** düğmesine basın.
Sahneye çalışan bir örnek eklenir. Örneği Inspector'da değiştirin, PC'de Play ile deneyin, sonra
telefon için build alın.

Modüller birbirinden bağımsızdır. Eklemediğiniz modül hiçbir şeyi değiştirmez. Aynı sahnede birkaç
modülü birlikte kullanıp kendi projenizi kurabilirsiniz: örneğin bir Tasarım, içinde bir Hikâye ve
bir Görme simülatörü. Açıp hemen Play'e basılabilecek örnek sahneler
`Assets/ErasmusARToolkit/Samples/Scenes/Modules` klasöründedir.

## Bölümler

| Bölüm | Ne kurar | Hangi tema için |
|---|---|---|
| Design Walkthrough | Unity'de tasarlanan bir mekân (sınıf, bahçe, müze); telefonla içinde yürünür. **View** düğmesi gerçek odayı gizler, basılı tutunca oda görünür | Kapsayıcı okul, sürdürülebilir kampüs |
| Living Poster | Basılı posterin üstünde video ya da ses çalar | Tanıtım, sergi |
| Wall Gallery | Duvara asılan çerçeveli resim ya da video | Sanal müze |
| School Route | Numaralı işaretler; her durakta ok ve "Next: ..." yazısı, HUD'da sayaç | Okul içi rota, yönlendirme |
| See Through Other Eyes | **Vision** düğmesi: renk körlüğü, az görme, tünel görüş, katarakt. Tasarım varsa **Eye** düğmesi tekerlekli sandalye ve çocuk göz yüksekliği | Erişilebilirlik, empati |
| Room Fill | Odada yükselen su, duman ya da nesneler; **Start / Pause** ve kaydırıcı | Sel, yangın, bir yılın plastik şişeleri |
| Portal | Başka bir dünyaya açılan kapı; içinden geçince o dünyanın içindesiniz | Geçmiş, gelecek, başka bir yer |
| NPC Story | Konuşan, yürüyen, kullanıcıya dönen karakterler; dokunuşla ya da yaklaşınca devam eder | Hikâye anlatımı, rehber |
| Info Cards | Herhangi bir nesneye başlık ve metin; dokununca okunur | Hepsi |

## Yerleştirme

Her modülün kökünde **Module Placement** bileşeni vardır.

- **At App Start:** modül uygulamanın başladığı yerde belirir. Kökün sahnedeki konumu, telefonun
  başlangıçtaki yeridir. Zemin bulununca zemine oturur.
- **Tap To Place:** HUD kullanıcıdan zemine dokunmasını ister. **Move** düğmesi yeniden yerleştirir.

Önce **Room scan** yapılırsa modüller taranmış zemini kullanır. Room Fill yalnız taranmış zemini
doldurur, gerçek masalar kuru kalır. Karakterler taranmış eşyaların ve duvarların etrafından yürür.

## Örnekleri değiştirmek

- **Design:** modellerinizi Design kökünün altına koyun. **Virtual Only** altındakiler yalnız Sanal
  görünümde görünür. Info Card'ı tek nesnelere ekleyin, grup köküne değil.
- **Room Fill:** seviye, süre, renk ve nesne prefab'ı **Room Fill** bileşenindedir. En çok 1500 nesne
  çizilir; prefab'ın ilk mesh'i ve malzemesi kullanılır.
- **Portal:** örnek **Inside World** yerine kendi prefab'ınızı koyun. İçerik portalın **World Size**
  kutusunun içinde kalmalı (portal seçiliyken mavi kutu).
- **Story:** adımlar **Story Stage** bileşenindeki listededir. Her adım: karakter, eylem (Say, Walk To,
  Turn To User, Play Animation, Wait, Show, Hide) ve ne zaman devam edeceği (Auto, Tap, User Near).
  **Add NPC To Story** ve **Add Point To Story** seçili hikâyeye ekler. Animator'lü bir karakter
  modelinin `Speed` (float) ve `Talk` (bool) parametreleri ayarlanır.

## PC'de deneme

- WASD ile yürünür, sağ sürükleme ile etrafa bakılır, F başlangıca döner (Design, Portal).
- Tap To Place modüllerde sanal zemine tıklayın.
- Hikâyede tıklama ya da **Next** devam ettirir; yaklaşmak için WASD.

## Dikkat

- Videolar Android'de oynaması için H.264 MP4 olmalı.
- Görme filtreleri saydam efektleri (su, duman) ve bilgi kartlarını filtrelemez.
- Tarama yapılmadıysa karakterler gerçek eşyaları bilmez, **Area Size** kadar bir karede yürür.
- Telefonda deneyin: kare hızı, video ve portal geçişi PC'de tam görünmez.
