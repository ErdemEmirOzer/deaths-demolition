# CX-AP027-B RAPOR 1 — 05.10.2026
## ÖZET (≤10 satır)
Durum: **KISMİ — 10 silahın teknik kaynak seti tamamlandı; geçerli oyun içi G4 kiti bekleniyor.**
Üretildi: 1118 WAV / 153.903.883 bayt; Faz A’nın 12 kusuru kapandı; 34 yeni gerçek kayıt edinildi.
ACİL uygulandı: eski oyun kitleri ve türevleri GEÇERSİZ; teslimden çıkarılıp silmeden karantinaya alındı.
Birleştirici: normalizasyon öncesi ≥6 dB silah-var kapısı; 22 sentetik sınır testi ve A/B–ABC tam hat PASS.
Dosya kapıları: biçim/tepe/trim1118/1118; Fire60, kovan324, link72, OpenTail60 PASS; 9 yeni stil crest sapması açık.
Kuru otomatik LF: minimum %33,183; SMG1 %43,779. G4, Unity, dense40 ve öznel AAA kabulü verilmedi.
Teslim: /Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/exports/v002/; manifest/AP027-manifest-v002.json; manifest/v002/SHA256SUMS.
Doğrulama: tek dd_validator kaynak setini denetledi; acil değişiklik sonrası lider SHA1273/1273, eski ses92/92 değişmedi.
Bekleyen: S-08 b4/kit-v2/HAZIR.md + C-00 bildirimi; final miks ve fiziksel kaynak açıkları. İstek: CX-AP027-B-ISTEK-1.md.

## 1. Yapılanlar

CODEX-SISTEM-R2, kadro HIZ MODU ve CX-AP027-B okundu. Üretim dd_sound_engineer (birleştirici), dd_researcher (kaynak edinimi) ve dd_sound_designer (ses seti) arasında bölündü. Kaliteyi lider ölçtü; ayrı estetik/AD inceleme yapılmadı. Tek mekanik dd_validator çalıştırıldı.

Görev sürerken C-00/kullanıcı acil güncellemesi geldi: `b4/kit` ve `kit-r1-ab` kayıtlarında silah sesi yok; bus tabanlarının −80 dB’de donduğu ve silah stem tepesinin −76 dBFS olduğu bildirildi. Bu bildirim bağlayıcı alındı. **Eski girdilerden daha önce üretilen önizlemeler, A/B çalışma çıktıları ve bütün kit ses ölçümleri iptal edildi.** Güncellemeden sonra bu kayıtlar yeniden okunmadı, ölçülmedi veya render edilmedi. Eski B5 kopyaları da reddedildi. Dosyalar silinmeden `validation/invalid-game-kit-v002/` altına taşındı; geçerli manifest ve checksum kapsamından çıkarıldı. Bildirilen −76/−80 değerleri C-00 kanıtıdır; burada yeni ölçülmüş gibi sunulmaz.

Birleştiriciye ham silah steminde atış/pre-shot tepe farkı ≥6 dB kapısı eklendi. Bilinen geçersiz dizinler WAV okunmadan reddedilir. Gerçek kayıt yerine sentetik girdilerle eşik, ret ve tam A/B–ABC hattı sınandı. `exports/v002-kit/` içinde **0 WAV ve HAZIR.md yok**; yalnız bekleme durumu vardır. S-08’in yeni kaydını C-00 bildirmeden işleme alınmayacak.

34 yeni Sonniss WAV indirildi. Büyük arşivler kopyalanmadı; bireysel WAV ve seçili ZIP üyeleri alındı. SMG1 öncelikli, ardından ar2→lmg1→sr1→smg2→lmg2→sg2→rl1 kaynak üretimi tamamlandı. Faz A’nın Mechanical04 trim, dört kovan loudness ve yedi Fire crest kusuru v002’de kapandı. Liderin farklı EDC yönteminde sınır dışı kalan bir SR1 açık kuyruğu da yalnız o dosyaya uygulanan ölçülü düzeltmeyle iki yöntemde PASS oldu: [validation/production-v002-tail06-repair.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/production-v002-tail06-repair.json).

## 2. Üretilen dosyalar

[Ses seti](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/exports/v002): 10 silah klasörü, 1118 PCM24 WAV. Dağılım: Fire60; Body/Sub/MuzzleTransient/Mechanical240; üç mekân kuyruğu180; DistanceMid/Far120; FireTP/FP120; kovan324; LMG link72; Cock/Pump2. RL1 kovan/link üretmez. Her ana katmanda6 çıktı vardır; bağımsız ham olay sayısı ayrıca reçetede yazılıdır.

|Dosya|Tür / özellik|SHA256 kısa|
|---|---|---|
|[manifest/AP027-manifest-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/manifest/AP027-manifest-v002.json)|1118 kaynak WAV; kaynak/lisans/özellik eşlemesi|7b9dbd0024ad|
|[manifest/AP027-manifest-v002-kit.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/manifest/AP027-manifest-v002-kit.json)|0 geçerli WAV; WAITING_VALID_RECORDINGS|1dba88d97057|
|[manifest/v002/SHA256SUMS](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/manifest/v002/SHA256SUMS)|1273 kararlı dosyanın hash listesi|16537c6a3246|
|[03-sources/LICENSES-v002.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/03-sources/LICENSES-v002.csv)|47 ham kaynağın sabit lisans kaydı|8921b606afed|
|[03-sources/source-index-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/03-sources/source-index-v002.json)|34 yeni kaynak; URL, format ve indirme kanıtı|3596d9fa5436|
|[04-tools/render_listening_kit.py](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/04-tools/render_listening_kit.py)|Ham silah-var kapısı, A/B/C, bip ve kör sıra|5d59181cb910|
|[validation/kit-presence-v002/results.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/kit-presence-v002/results.json)|22 sınır testi ve sentetik A/B/ABC tam hat|b7b142eb9a21|
|[validation/production-v002-recipes.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/production-v002-recipes.json)|1118 çıktının kesim, işlem ve SHA reçetesi|b7faa0a9f5ca|
|[validation/leader-quality-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/leader-quality-v002.json)|Final PCM ve kuru kadans kalite ölçümü|3cd48c666328|
|[validation/leader-pcm-measurements-v002.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/leader-pcm-measurements-v002.csv)|1118 dosyanın ayrı ölçüm satırı|df8970f70cfc|
|[validation/firetrim-v002.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/firetrim-v002.csv)|10 silah için kazanç ön hesabı|24535d54d47a|
|[validation/mechanical-validation-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/mechanical-validation-v002.json)|Tek mekanik denetimin geçerli ve iptal edilen kapsamı|472b9f201f6b|
|[validation/post-urgent-integrity.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/post-urgent-integrity.json)|Acil değişiklik sonrası lider bütünlük kontrolü|726a45c7a7f7|

Tam hash doğrulaması AP-027 kökünden `shasum -a 256 -c manifest/v002/SHA256SUMS` ile yapılır. Export klasörlerindeki yerel SHA256SUMS kendi klasörüne göre bağıl yol kullanır. `post-urgent-integrity.json`, dondurulan listenin sonradan yazılmış denetim sonucudur; listenin kendi içine döngüsel olarak alınmadı.

Önceki `exports/v001-prototip/` 92 WAV değişmedi. V001 manifest ve tarihsel SHA listesi korunur; o tarihin README/LICENSES kopyaları `validation/phase-a-frozen-v002/` altındadır. Güncel metadata için eski kök hash listesi yerine v002 listesini kullanın.

## 3. Kalite ve doğrulama

Lider ölçümü nihai yazılmış PCM üzerindedir: [validation/leader-quality-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/leader-quality-v002.json) ve [validation/leader-pcm-measurements-v002.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/leader-pcm-measurements-v002.csv). Temel dosya kapılarının geçmesi, algısal gerçekçilik veya oyun içi kabul anlamına gelmez.

|Kontrol|Sonuç|
|---|---|
|48 kHz / PCM24|1118/1118 PASS|
|Tepe ≤−1 dBTP|1118/1118 PASS; en yüksek −1,060002 dBTP tahmini|
|Uç sessizlik ≤5 ms|1118/1118 PASS; ön/arka en fazla0,5 ms, aşağıdaki eşikle|
|Fire −18±1,5 LUFS|60/60 PASS; −19,499404…−18,000001|
|Kovan −24±1,5 LUFS|324/324 PASS; −24,999894…−24,000001|
|LMG Link −30±1,5 LUFS|72/72 PASS; yaklaşık−30; −6 dB fark dosyaya işlenmiş|
|OpenTail RT60 0,64–0,96 s|60/60 PASS; lider0,650203…0,854412 s|
|Faz A açık kusurları|12/12 kapalı; [validation/production-v002-defect-closure.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/production-v002-defect-closure.json)|
|Yeni Fire stil crest|51/60 hedefte;9 sapma açık|
|Geçerli oyun kiti / G4 / dense40|YOK / BEKLİYOR / ÖLÇÜLMEDİ|

LUFS: K-weight,400 ms blok/100 ms hop,−70 LUFS mutlak ve−10 LU göreli kapı. Kısa klip yalnız analizde400 ms’ye sıfırla uzatılır; WAV’a pad eklenmez. dBTP4×polifaz tahminidir. Trim eşiği Faz A ile aynı `max(−120 dBFS, tepeye göre−80 dB)`; mutlak−60 dBFS tanısı CSV’de ayrıca vardır. OpenTail:500–1000 Hz bant,4.derece filtre/kanal güç toplamı ve Schroeder T20→RT60; ölçülmüş kale IR’si değildir. Açık tail loudness aralığı−26,289394…−20,000001’dir; tail−20±1,5 hedefinin tüm dosyalarda geçtiği iddia edilmez.

### Kuru otomatik düşük bant

Main verisi salt okunur kaydedildi: [validation/main-cadence-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/main-cadence-v002.json). AR1/SMG1/SMG2/LMG1/LMG2 için600/900/540/720/420 RPM; AR2 için50/50/260 ms üçlü dizi kullanıldı. İlk atışın tam katkısı dışındaki pencere ve30–400 Hz /30–16000 Hz Welch oranı ölçüldü; crowd veya ayrı kuyruk oranı şişirmek için eklenmedi.

|Silah|Kuru hold en düşük LF %|
|---|---:|
|ar_1|35.082|
|ar_2|81.992|
|lmg_1|33.183|
|lmg_2|52.253|
|smg_1|43.779|
|smg_2|44.852|

36 lider satırı,6 silah×3 kamera×sınırsız/mevcut ses sınırı referansıdır. Kamera kazancı tek başına LF oranını değiştirmez; bu tablo oyun EQ/oda etkisi kanıtı değildir. Üretimin farklı jitter koşulunda minimum%34,1965, lider koşulunda%33,183279 ölçüldü. SMG1’in eski B1 %9,8 değerine karşı yeni çevrimdışı kaynak %43,779’dur; eş koşullu Unity A/B ölçümü değildir. Özellikle AR2’nin yaklaşık%82 LF oranı, yalnız alt sınırı geçtiği için tını kabulü sayılmaz.

### Güncel birleştirici testi

[validation/kit-presence-v002/results.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/kit-presence-v002/results.json) ve [validation/kit-presence-v002/summary.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/kit-presence-v002/summary.md):22/22 sentetik sınır testi PASS. Ham stemde ilk atışa göre−200…−10 ms temiz ön pencere; her atışta0…150 ms pencere kullanılır. Her atışın tepe farkı aynı temiz baseline’a göre en az6 dB olmalıdır. Dijital sıfır için24-bit1LSB tabanı kullanılır; tam sessizlik, eksik pencere, NaN ve hatalı marker/frame/rate reddedilir. Kapı gain/normalizasyon öncesindedir; gerçek akustik onset tespiti iddia edilmez.

Sentetik tam CLI A/B ve ABC koşuları PASS:10 kör parça birebir, decoded loudness farkı en fazla7,382e−8 LU, bip1000 Hz/200 ms. Silahsız B steminde işlem normalizasyondan önce exit1 ile durdu; çıktı oluşturulmadı. Bütün test çıktıları **TEST_ONLY** ve validation altındadır; gerçek kayıt okuma0, exports yazımı0. `--test-only` girdi/çıktı yollarını test alanına kilitler ve HAZIR yazımını yasaklar. Göreli+6 dB kapı tek başına mutlak duyulurluk veya AAA onayı değildir.

### Açık kaynak ve stil sınırları

Yeni crest sapmaları: SR1 Fire02/05≈18,37 dB (hedef20–22),SG2 Fire02≈16,19 (17–19),RL1 altılı≈12,86–12,93 (yaklaşık16 hedef). Faz A’nın yedi Fire crest kusuru kapanmıştır. Kaynak kalite ve işleme ayrıntıları: [validation/production-v002-source-quality.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/production-v002-source-quality.json), [validation/production-v002-notes.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/production-v002-notes.md).

RL1, tek3,658 s RPG firing+impact kaydının0,309–0,530 s ilk transientinden6 DSP türevidir; transientin gerçekten launch olduğu doğrulanmadı.220–222 ms Fire adayları final430–600 ms kimlik hedefinin kabulü değildir. Dokuz diğer silah6 benzersiz çekirdek segment kullanır; mikrofon/kalibre/model kimliğinin birebirliği ayrıca sınırlıdır. LMG1 HK416→belt-fed,LMG2 MG42 7,92→7,62,SR1 K98k→ağır tüfek,SG2 SPAS/pompalı→çift namlu ve bazı mekanikler benzetimdir. SR1 açık kuyruklarında MP5 ortam kaydı kullanılmıştır. Gerçek su/çakıl/metal ile yapılan kompozitler tam kalibre×yüzey fiziksel foley kaydı değildir; Link gerçek mühimmat bağlantısı kaydı değildir. Bu sınırlar manifest/reçetede işaretlidir.

### Mekanik denetim kapsamı

Tek dd_validator,1118 sesin başlık/SHA/reçete kapsamını,47 ham lisans kaynağını ve önceki92 WAV’ın değişmediğini bağımsız denetledi. Ses metriklerini SHA-eşleşen lider tablosundan kontrol etti. Bu tur acil bildirimden önce tamamlandı; eski kit hakkındaki denetim kapsamı **iptal edildi**. Kaynak seti sonrasında değişmedi. Acil güncellemenin kod/metadata/test değişikliklerini lider doğruladı: kanonik1273/1273, kaynak export1118/1118, boş kit durum dosyası1/1 ve araştırma destek23/23 SHA PASS. Kanıt [validation/mechanical-validation-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/mechanical-validation-v002.json) ve [validation/post-urgent-integrity.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/validation/post-urgent-integrity.json). İkinci estetik veya bağımsız doğrulayıcı açılmadı.

## 4. Kaynaklar ve lisanslar

[01-design/AP027-B-sources.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/01-design/AP027-B-sources.md) ve [03-sources/LICENSES-v002.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/03-sources/LICENSES-v002.csv): önceki13+yeni34=47 kayıt; üretimde37 ham kaynak kullanıldı. Yeni kaynaklar349.650.382 bayt; SHA/başlık/boyut/resmî tracklist34/34,2024 seçili ZIP CRC6/6 PASS. Ham dosyalar `/Users/Erdem/DD-Sources/sonniss/` altındadır. İndirme sonrası kaydedilen boş alan106.159.570.944 bayt (~98,87 GiB),15 GB sınırının üstündedir.

[Sonniss resmî lisansı](https://sonniss.com/gdc-bundle-license/) ticari proje ve düzenleme kullanımına izin verir; kaynak/türevler bağımsız ses kütüphanesi olarak dağıtılamaz. Lisans dökümü ve dört makbuz kaynak klasöründedir. Yayıncıdan ayrı WAV SHA listesi bulunmadı; yerel SHA indirilen kopyanın kimliğidir. Yeni SFX’te AI/Seed Audio kullanılmadı. Test sinyalleri ve bipler teknik sınama içindir. Geçersiz eski oyun kayıtları, yeni Sonniss setine veya geçerli teslim girdilerine dahil değildir.

## 5. Entegrasyon ve kazanç önerisi

[01-design/AP027-B-integration.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/01-design/AP027-B-integration.md) ve [01-design/AP027-B-gain-recommendations.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-027/01-design/AP027-B-gain-recommendations.md) kullanılır. Fire ile Body/Sub/Muzzle stemlerini birim kazançta birlikte çalmak çiftler. FireTP/FP alternatif dinleme dosyalarıdır; B3 ayrı enum seçmez. Runtime+1/+2 dB nearfield dosyalara işlenmedi. Mechanical−10/−7/−3 dB ve tail30/11/21 ms olay sözleşmesidir. **Link−6 dB farkı dosyaya işlenmiştir; ikinci kez uygulanmaz.** RL kovan0;SG2 iki kabuk100–130 ms; kovan tetiklemesi ejection/landing olayına bağlıdır.

Kazanç tablosu final mixer kararı değildir. Silah başına24 kuru durum:6 başlangıç varyantı×nominal/±%3 pitch×sınırsız/mevcut ses sınırı. Formül `ceiling=−1,01−worstDryTP−20log10(0,85)−2`; öneri1 dB ek paylı ve pozitif trim içermez. RL’nin mevcut−4 dB patlama ilişkisi üst sınır olarak korunur. Bütün silahlara ortak trim istenirse denenen durumlar içindeki en küçük öneri−7,204 dB’dir; tüm olası rastgele mikslerin garantisi değildir.

|Silah|Main trim dB|Denenen kuru TP dB|Öneri trim dB|Aktif PCM sonu kesimi, en fazla|
|---|---:|---:|---:|---:|
|ar_1|-0.90|4.606|-7.204|0|
|ar_2|0.00|1.441|-4.039|0|
|smg_1|-2.60|4.137|-6.736|25|
|smg_2|-0.40|3.265|-5.863|0|
|lmg_1|-1.60|4.138|-6.737|8|
|lmg_2|0.00|3.235|-5.833|0|
|sg_1|0.00|-1.060|-1.538|0|
|sg_2|0.00|-0.511|-2.088|0|
|sr_1|0.00|-1.054|-1.545|0|
|rl_1|-4.00|-1.060|-4.000|0|

SMG1 Fire264,771–280 ms;LMG1 Fire240,771–339,062 ms. Mevcut4 ses/50 ms rezerv modelinde tabloda görülen son kesimleri oluşur; bunlar Unity’de ölçülmüş olay veya duyulur kusur sayısı değildir. `ceil((maxDuration/0,97+0,05)/period)` hesabı SMG1 için6,LMG1 için5 ses verir. AR2’de4 rezerv geri kullanımı,0 aktif PCM kesimi vardır. S-08 kit-only6/5 ses denemesi ve gerçek recycle/steal telemetrisi istenir; FireRefused0 tek başına kesimsiz çalma kanıtı değildir.

Dense40; tail/mechanical/crowd/casing birikimi; launcher patlaması; limiter, codec sonrası tepe ve bellek ölçülmedi. S-08 gerçek oyunda TP≤−1 dBTP, limiter GR≤4 dB ve Fire duyulurluğu/drop/recycle kapılarını denetlemelidir. Unity’ye içe alım yapılmadı.

## 6. C-00’A SORULAR / 🔴 KARARLAR

1. **Kararı verilmiş kayıt bekleme noktası:** S-08 bus sorununu düzeltip `b4/kit-v2/HAZIR.md` üretecek, C-00 bildirecek. Codex bu bildirimi bekler; eski girdileri yeniden kullanmaz. İstek: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/c-00/codex/requests/CX-AP027-B-ISTEK-1.md`.
2. **G4 ve final miks:** Kullanıcı yönü gelmedi. Öneri: geçerli kayıtla kaynak adaylarını dinletmek, SMG1/LMG1 kapasitesi ve kazançlarını kit-only deneyip dense40 ile doğrulamak. Bekleyen: final kazanç ve Unity kabulü.
3. **Fiziksel kaynak açıkları:** RL launch kimliği, gerçek belt-link/kalibre-yüzey ve bazı mekanikler için benzetimlerin kabulü veya ek gerçek kayıt gerekiyor. Öneri: final AAA iddiasından önce hedefli foley/ayrılmış launch kaydı; RL adayını onaysız final oyuna almamak. Dokuz yeni stil crest sapması da açık kalır.

## 7. Araç / Higgsfield istekleri

Mevcut Python3,numpy,scipy,wave/hashlib/csv/json ve HTTP/ZIP araçları kullanıldı. Kurulum, satın alma, hesap açma veya giriş yapılmadı; Higgsfield/Adobe SFX çağrısı yok. Blender, Unity ve Player başlatılmadı; kullanıcı verisi koşusu yapılmadı. Repo/main, başka paket ve önceki teslim sesi değiştirilmedi. S-08’e sohbet mesajı gönderilmedi; istek dosyası manuel aktarım içindir. Otomatik izleme kurulmadı.

## 8. Sonraki önerilen çalışma

C-00’ın yeni kayıt bildiriminden sonra raw silah-var kapısı ve diğer kit kontrolleri; ardından G4, final miks, hedefli kaynak/crest revizyonu ve S-08 guarded dense40/import doğrulaması. **1118 WAV teknik kaynak seti teslim edildi; geçerli G4 kiti ve final ses kabulü tamamlanmış sayılmadı.** Bu raporla çalışma durdu; arka planda devam eden iş yok.
