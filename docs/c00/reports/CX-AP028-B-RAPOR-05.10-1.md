# CX-AP028-B RAPOR 1 — 05.10.2026
## ÖZET (≤ 10 satır)
Durum: **TESLİM — tasarım/varlık üretimi tamam; kanonik alım BLOCKED.**
Üretim: 58 semantik ikon + SDF/derinlik/motion, 20 ekran taslağı, 5 yükleme masterı, 5 çekim/60 s sinematik previz.
Teslim: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002` — 6.651 dosya / 179.401.865 bayt.
Manifest: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/manifest/AP028-manifest-v002.json`; SHA: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/manifest/SHA256SUMS-v002`.
Yükleme: L01/L02/L04/L05/L08, bağlayıcı ana-repo text-safe **5/5 PASS**; q90 toplam **3.606.314 B**.
Kalite kontrol: tek dd_validator içerik bütünlüğü PASS; kanonik araç 793 desteklenmeyen tür hatasıyla FAIL.
Çözünürlük sınırı: image_gen kaynakları 1672×941; final PNG 2560×1440'e büyütüldü, sRGB gömülü; native 4K/2560 iddiası yok.
🔴 / sorular: 1 kanonik şema uyumu; `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/c-00/codex/requests/CX-AP028-B-ISTEK-1.md`.
Sonraki adım: S-09 tür desteğini ekleyip alımı tekrar doğrulasın; ardından Unity entegrasyonu ve runtime ölçümleri.

## 1. Yapılanlar

CODEX-SISTEM-R2, KADRO-CODEX-AJANLARI ve CX-AP028-B baştan sona okundu. Çalışma sırasında iletilen C-00 güncellemesi üzerine §3 yeniden okundu. O1 Fırça Mürekkebi, L01/L02/L04/L05/L08 ve başlangıçta kapalı Profil kararı uygulandı; Profil rotası korundu. L07 teslim beşlisine alınmadı.

Üretim üç uzmanla paralel yürütüldü: `dd_graphic_designer` ikon/motion ve manifest üretimi; `dd_ui_ux_designer` ekranlar; `dd_concept_artist` sinematik. Lider yükleme düzenlemelerini, lisans birleşimini ve görsel kontrolünü yaptı. HIZ MODU 7 uyarınca ayrı AD/oyun tasarımcısı inceleme ajanı açılmadı. Teslim sonunda **tek** `dd_validator` salt-okuma mekanik turu çalıştı.

İkon ailesi Faz A'nın 35 işlevini ve 23 ortak gezinme/durum/HUD işlevini kapsar. 58 semantik ID içinde 7 geometrik alias açıktır; 58 farklı siluet iddiası yoktur. SVG, beyaz PNG ve SDF aynı semantik kaynaktan türedi. B atlası S-09'un mevcut G1 atlasına alınacak taşıyıcıdır, ikinci runtime sayfa kararı değildir.

Sinematikte 5 çekim ve 60 saniyelik dikişsiz zaman çizelgesi üretildi. `HAZIR.md` diğer işler bitmeden bırakıldı ve C-00 kullanıcıya göstereceğini bildirdi. Hedef kareler Higgsfield üretimidir; videolar bu kareler üzerindeki 2B kadraj hareketidir. Kamera/landmark bilgisi S-05 plakası, AP-005 yerleşimi ve V6 oda verisinden çıkarıldı; bu kaynakların farklı sahneleri temsil edebildiği açıkça ayrıldı.

Yükleme sanatı yerleşik **imagegen becerisi / image_gen aracıyla** düzenlendi. Merkezdeki ışık, fener ve zemin parlamaları ölçülüp gölgeye alındı; L04 silahları AP-021 referanslarıyla değiştirildi. Ressamca ışık düzenlemesi yapıldı; Python ile karartma maskesi, kutu veya vinyet eklenmedi. Beş sanat da **ilk tam PASS aldığı turda bırakıldı**: L01 r4, L02 r5, L04 r5, L05 r3, L08 r5. Sonradan yalnız teknik boyut/ICC ve inceleme türevleri üretildi. L02 son düzeltmesi genel −1 EV, L08 son düzeltmesi genel −0,5 EV istemi içerir; bu yüzden önce/sonra çiftleri merkez dışındaki pozlama farkını da gösterir.

Repo/main'e, Unity projesine veya başka üretim paketine yazılmadı. Unity/Blender/Player çalıştırılmadı; kurulum, login, satın alma, push veya git değişikliği yapılmadı. Başlangıç disk kontrolü 106 GiB boştu. V001 manifestindeki 463 varlığın SHA'ları bağımsız doğrulamada korundu. Ortak lisans/ledger kayıtları genişletildi; önceki hallerinin kopyaları saklandı.

## 2. Üretilen dosyalar

| Dosya / indeks | Özellik | SHA256 kısa |
|---|---|---|
| [Manifest](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/manifest/AP028-manifest-v002.json>) | 6.651 item, yalnız exports/v002 | 23f1b27c902e |
| [SHA256SUMS](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/manifest/SHA256SUMS-v002>) | Sürüme özel; eski SHA dosyası korunur | e61940cb9d44 |
| [İkon eşleme](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/Icons/atlas.json>) | 58 semantik ID / 7 alias | 1cdb669de0ee |
| [Motion JSON](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/Icons/motion.json>) | Durum anahtar kareleri ve reduced motion | 43f184cb2c5a |
| [Sinematik kamera](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/MenuCinematic/shot-list.json>) | 5 çekim / 60 s / kaynaklı pozlar | 0372ad0a4a1f |
| [Loading ölçüm](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/Loading/loading-metrics.json>) | 5 master / PASS | bafd7f368f71 |
| [Harf sayımları](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/Screens/letter-counts.json>) | 20 ekran / EN-TR / U1 projeksiyonu | 9a54041fb699 |

- [İkon teslimi](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/Icons/README.md>): 58 SVG; 174 PNG (64/128/256); 58 RGBA SDF + 58 ham L SDF; 116 height/normal; 290 durum renderı; 5.336 motion PNG. PNG dizilerinin zamanları `sequence.json` ile tanımlı, MP4 zorunlu olmayan ikon teslimi için PNG dizisi seçildi.
- [Sinematik HAZIR](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/MenuCinematic/HAZIR.md>) · [60 saniye oynatıcı](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/MenuCinematic/preview.html>) · [Storyboard](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/MenuCinematic/Storyboard_ContactSheet.png>): 5 stil karesi, 5×6 s MP4, kamera JSON'u, SVG harita ve uygulama notu.
- [Yükleme önce/sonra galerisi](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/Loading/review.html>): 5 final master, her sanat için ayrı önce/sonra PNG (10), ayrıca 5 karşılaştırma sayfası. İnceleme türevleri runtime'a alınmaz.
- [Ekran galerisi](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/Screens/index.html>) · [20 ekran kontakt sayfası](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/Screens/contact-sheet.png>): ana menü EN/TR ve Profil açık karşılaştırması; seçim EN/TR; 8 ayar sekmesi; pause ve kayıp onayı EN/TR; yükseltme, sonuç ve başarımlar. Her mockup 1920×1080, eş JSON ve harf sayımı vardır.

Bütün dosyaların tam listesi ve hash'leri manifesttedir; tablo temsili giriş noktalarıdır. Manifest item yollarında exports dışı kaynak/kanıt dosyası yoktur.

## 3. Kalite ve doğrulama

### Yükleme — bağlayıcı ölçüm

Ana repo yolundan `python3 tools/wallpaper/wallpaper_intake.py --src <master> --name loading_LNN --master-ref <master> --text-safe --dry-run` çalıştırıldı. Araç değiştirilmedi. Dört ekran, cover-fit, 1,0/1,05 itiş ve JPEG q90 dekode görüntü ölçümü esas alındı. Bağımsız dd_validator aynı koşuyu beşinde tekrarladı; bütün çıkış kodları 0 ve sayılar eşleşti. Pillow sürüm etiketi farklıydı, sayısal sonuç değişmedi.

| Master | Halka min ≥3 | Etiket min ≥4,5 | İpucu min ≥7 | PSNR dB | q90 bayt | SHA256 kısa |
|---|---:|---:|---:|---:|---:|---|
| L01 | 3.34 | 6.05 | 7.19 | 44.78 | 860,709 | cfe4aee5d6f3 |
| L02 | 6.03 | 5.09 | 8.25 | 46.06 | 696,143 | 64675f34e841 |
| L04 | 4.43 | 6.66 | 7.26 | 46.48 | 603,493 | 687921ab75ec |
| L05 | 5.39 | 6.23 | 8.52 | 45.88 | 684,302 | 9a10aaebe715 |
| L08 | 6.41 | 5.72 | 10.03 | 45.04 | 761,667 | ee9384ec2498 |

Toplam 3.606.314 bayt, 10 MB bütçe içinde. Diğer wallpaperlar için kalan 6.393.686 bayt; bütün oyun wallpaper envanterinin toplamı bu işte yeniden ölçülmedi. Ham stdout ve önceki başarısız turlar [ölçüm özeti](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/validation/v002/loading/loading-summary.json>) üzerinden izlenebilir. PASS sonrası sanat değişmedi.

Beş master RGB opak ve gömülü sRGB ICC taşır. Kaynak üretim çözünürlüğü 1672×941 olduğundan teknik büyütmenin ayrıntı sınırı vardır. Bu sınır gizlenmedi; native 2560/4K kalite vaadi yapılmaz. L04 rafı kontrast düzeltmesi sonrası daha gölgelidir. [L04 kimlik kararı](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/Loading/L04-identity.md>) gerçek Special referanslarını, GLB parça/hash kontrolünü ve birebir mesh renderı yapılmadığını açıklar. Yanlış yatay cam tüp/küresel hazne tasarımları çıkarılmıştır.

### İkon / hareket

Tek 64 birim SVG kaynağı, nominal 24 px'te 2 px çizgi, 64² SDF spread6 ve beyaz RGB/mesafe alfa taşıyıcısı vardır. Alpha8 içe alımı RGBA taşıyıcının A kanalından yapılmalıdır; ham gri PNG'nin otomatik alfa olduğu varsayılmaz. Height/normal doğrusal ve OpenGL Y+ sözleşmeli, shader ışığı/sheen/rim/ıslak mürekkep parametreleri README'dedir.

290 durum ölçümü bağımsız tekrarlandı: minimum 9,7943:1, aynı durumdaki düz glife fark ≥0. Bu kanıt hedef 24 px shader renk alt sınırını uygular; ham 128 px renkli renderı doğrudan küçültmek aynı sonuç garantisi değildir. Unity Linear shader, bütün tamsayı grid snap noktaları, her dar aralığın geometrik ispatı ve insan kör tanıması ölçülmedi. İki easing + lineer token, hover/focus/press/select/disabled/unlock/notification/idle ve anlık reduced motion tanımlı. Stroke draw yönlü mürekkep cephesidir; gerçek SVG path-length çizimi olarak sunulmaz. Kanıt: [ikon ölçümleri](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/validation/v002/icons/icon-metrics.json>), [24 px sayfası](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/validation/v002/icons/24px-test.png>).

### Ekranlar / sinematik

20/20 statik ekran kontrolü PASS, minimum metin kontrastı 5,921:1. Kapalı ana menü EN/TR 22/22 harf; karşılaştırma için açık Profil 126/124; seçim 158/161. Aynı içerik kapsamındaki açık Profil+seçim karşılaştırması EN %80,278 / TR %80,056 azalma: hedef %40 ve 150/300 sınırları karşılanır. Bu **statik projeksiyondur**, runtime U1 kapısı değildir. Ayar ad/değerleri, ekonomi, önce→sonra ve kayıp açıklaması korunur. Fixture değerler canlı oyuncu verisi değildir. [Ekran kontrolü](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/validation/v002/screens/independent-screen-check.json>).

Sinematik: 5 çekim/60 s; 5 MP4 1280×720,24 fps,144 kare/6 s. Tarayıcıda 0/60 s canvas byte eşliği ve reduced motion PASS, JS hata0. Sol üçte bir sakinlik 5/5 PASS. S03 ham glif zarfında 3,73:1; ana menünün mevcut alpha0,55 scrim'iyle konservatif lineer hesap6,39:1, tarayıcıda 60 zaman örneğinde min8,84:1. Bu menü kuralı loading'e aktarılmaz. Gerçek geometri/çarpışma/oyun içi kamera/GPU/CPU ölçülmedi; performans rakamları tahmin olarak etiketli. [Uygulama ve kaynak ayrımı](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/exports/v002/MenuCinematic/IMPLEMENTATION.md>).

### Manifest / bağımsız doğrulayıcı

[Tek dd_validator tutanağı](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/validation/v002/mechanical-validator-receipt.md>): 6.651 benzersiz dosya, iki SHA256SUMS kontrolü exit0; 6.084 bitmap decode,60 SVG,489 JSON; bütün kapsam ve lisanslar PASS. Beş MP4 yapısal kontrol gördü, tam video decode yapılmadı.

**Kanonik alım halen FAIL/BLOCKED.** Main `0040c85fda16` kaynakları `git show main:` ile okunup byte eşliği doğrulandı ([araç kimliği](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/validation/v002/canonical-tool-provenance.json>)). `codex_manifest_check.py` yalnız beş eski türü tanıyor. 793 hatanın tamamı ek türlerden:236 data_texture,504 document,5 wallpaper_opaque,43 preview_opaque,5 video.5.336 uyarı motion kare isimlerinden. Yol/SHA/alfa/lisans hatası yok. Opak duvar kâğıdı şeffaf UI diye yeniden etiketlenmedi; dosyalar saklanmadı. [Kök neden özeti](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/validation/v002/canonical-compatibility-summary.json>) · [Değişmemiş araç ham sonucu](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/validation/v002/canonical-manifest-check.json>). Tür duyarlı doğrulayıcının PASS sonucu **kanonik alım onayı yerine geçmez**.

## 4. Kaynaklar ve lisanslar

[LICENSES.csv](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/03-sources/LICENSES.csv>) 66 eksiksiz kayıt içerir. Proje AP-005 plaka, AP-021 Special referansları, mevcut AP-012 portreleri/upgrade sanatı ve OFL fontları kaydedildi. Yeni dış ikon kütüphanesi kullanılmadı.

Higgsfield: Faz B'de 8 görüntü üretimi, preflight toplam22 kredi; gerçek tahsilat araçtan dönmedi. [Ortak ledger](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/03-higgsfield/ledger.csv>) önceki8 + yeni8 kaydı tutar. Ticari çıktı kullanımı [Higgsfield Terms §4.4](https://higgsfield.ai/terms-of-use-agreement) kapsamında kayıtlıdır. Videolar mevcut Higgsfield sandbox ffmpeg ile üretildi, kurulum yapılmadı.

Yerleşik image_gen:22 çağrı; [ayrı üretim kaydı](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/03-sources/loading-v002/imagegen-ledger.json>) ve [istem/seçim reçetesi](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-028/01-design/AP028-B-loading-recipe.json>). Kredi tutarı dönmedi. Çıktı sahipliği [OpenAI Terms, Content](https://openai.com/policies/terms-of-use/) kapsamında, özgün kullanıcı projesi referanslarıyla. Adobe kullanılmadı. İki servis için hesap açılmadı veya giriş yapılmadı.

## 5. Entegrasyon notları

S-09: `Icons/README.md` sözleşmesiyle mevcut G1'e birleştir; SDF sRGB kapalı/Alpha8, height/normal doğrusal, shader tint ve 24 px renk alt sınırı. Motion/reduced motion ve okuyucu/tooltip/focus davranışını uygula. Önizleme/state PNG'lerini runtime'a alma. Ana menüde Profil kapalı başlasın. Ekran fixture sayıları veriden bağlanmalı; U1 ve gerçek TextScale/dar oran testleri entegrasyonda koşmalı.

S-05/S-07: gerçek World V6 üzerinde landmark ve kamera yolu; 0,35 m sphere cast/clip, LOD, gölge, iki döngü CPU/GPU P95 ve bellek ölçümü. Stil karesi veya 2B MP4 gerçek harita yerine geçmez; runtime video oynatma hedefi yok. Kaynak sahne uyuşmazlığında landmark esas alınır, uydurma geometri eklenmez.

S-09 loading: yalnız beş `AP028_loading_LNN_2560x1440.png` masterını aynı intake ile q90 JPEG'e çevir. Sonrasında Unity LoadingKeyArtTests çalıştır. Ortada kutu/scrim yok; ana menünün scrim'iyle karıştırma. Önce/sonra ve review sayfaları oyun varlığı değildir.

## 6. C-00'A SORULAR / 🔴 KARARLAR

**1 — Kanonik tür desteği / alım engeli.** Soru: S-09 kanonik denetleyiciye görevde gerekli opak/veri/belge/video türlerini ekleyerek v002'yi tekrar doğrulasın mı? Seçenekler: A) açık tür desteği ve mevcut hashlerle yeniden alım; B) ayrı kapsam manifestleri için yeni alım sözleşmesi. Öneri:A. Bekleyen iş: kanonik alım; sanat üretimi tamamlandı. İstek dosyası: [CX-AP028-B-ISTEK-1.md](</Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/c-00/codex/requests/CX-AP028-B-ISTEK-1.md>). Bu rapor kullanıcıya doğrudan yeni tasarım kararı sormaz.

## 7. Araç / Higgsfield istekleri

Yeni araç veya kurulum isteği yok. Mevcut Python/Pillow/numpy, yerleşik image_gen, Higgsfield GPT Image2.5 ve sandbox ffmpeg kullanıldı. Kaynak çözünürlük sınırı raporlu; onaysız başka model/API veya satın alma yapılmadı. Kanonik araç değişikliği yalnız C-00/S-09 isteğidir, ana repo değiştirilmedi.

## 8. Sonraki önerilen çalışma

Önce kanonik alım engelini kapat; sonra S-09/S-05/S-07 Unity entegrasyonu, U1, gerçek kameralar ve performans kapıları. Loading PASS sonrasında ek karartma veya görsel üretim önerilmiyor. Kullanıcıya `Loading/review.html` ve sinematik `preview.html` gösterilebilir. Mevcut teslimler bu raporla donduruldu.
