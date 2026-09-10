# MANUEL TEST KİTİ · `G-deaths-demolition-10`

**Kim koşar:** kullanıcı (T4 katı — otomasyonun göremediği tek şey insanın deneyimidir)
**Ne kadar sürer:** ~15 dakika, kesintisiz
**Neden var:** S5 gereği **öncelik 1-5'in his imzası sende.** Sen bu kiti koşana kadar HI1–HI5 raporda `doğrulanmadı` kalır; hiçbir koşulda `kapandı` yazılmaz.

---

## 0 · KOŞMADAN ÖNCE (2 dk, süre sayacına dahil değil)

| | Yapılacak | Neden |
|---|---|---|
| 0.1 | Otomatik kapı **yeşil** mi? (`OTOMATIK-KAPI.md` koşulmuş ve `KAPI: YEŞİL` yazıyor mu) | Kapı kırmızıyken alınan his imzası kanıt değildir — kırık bir yapıda "hissiz" demek adaletsizdir |
| 0.2 | Unity Console → **`Clear on Play` KAPALI**, `Error Pause` KAPALI | Açıksa TS-410 ölçümü silinir |
| 0.3 | Oyun penceresi → **1920×1080**, `Maximize on Play` **AÇIK**, VSync kapalı | Ortam yazılmayan ölçüm kanıt değildir |
| 0.4 | Elinin altında bir not defteri / bu dosyanın kopyası | Her adımda `geçti` / `KALDI` + serbest not yazacaksın |
| 0.5 | Kayıt dosyasını **silme.** İlk koşu soğuk açılış olacak | Adım 1 zaten soğuk açılışı sınıyor |

**Puanlama:** her his imzası adımında **1-5** puan ver.
`1` = yok · `2` = zayıf · `3` = idare eder · `4` = iyi · `5` = tam
**`≤2` verdiğin her puan otomatik `KALDI` sayılır ve `BULGU`ya çevrilir.**

---

## ADIM 1 · SOĞUK AÇILIŞ VE MENÜ · 0:00–1:00

**Ne yapacaksın**
1. Editör'de Play'e bas. (İkinci Play olsun — ilk Play'de shader derlenir, süre ölçümü bozulur.)
2. Açılan ilk ekranı gör. Hiçbir tuşa basmadan 5 saniye bak.
3. Main Menu'de **Play · Profile · Statistics · Achievements** bölümlerinin dördünü de gör; Profile ve Statistics'e girip çık.

**Beklenen**
- Uygulama doğrudan **Main Menu** ile açılır. Boş sahne, siyah ekran veya "önce şu sahneyi aç" gerekmez.
- Dört bölüm de var ve açılıyor.
- Yazılar İngilizce.

**KALDI koşulu**
- Main Menu dışında bir şeyle açılıyorsa → `K01 KALDI`
- Dört bölümden biri yoksa veya tıklandığında boş ekran geliyorsa → `K01 KALDI`
- Menü "debug butonu yığını" gibi duruyorsa (tipografi tutarsız, hizasız, boşluk sistemi yok) → `TS-408 KALDI`

**Not alanı:** `_______________________`

---

## ADIM 2 · SEÇİM EKRANI · 1:00–1:45

**Ne yapacaksın**
1. `Play` → Skin & Weapon Selection ekranına gir.
2. Üç skini de sırayla seç, karakterin renk değiştirdiğini gör: Ivory/Crimson · Indigo/Black · Moss/Cream.
3. Silah listesine bak. **Bu ilk koşunda yalnız Assault Rifle açık olmalı**, SMG ve Shotgun kilitli görünmeli.
4. Assault Rifle'ı seç, koşuyu başlat.

**Beklenen**
- Üçü de baştan açık, yalnız görsel fark var.
- Kilitli silahlar **kilitli olduğu belli** olacak şekilde görünüyor (gizlenmiyor).

**KALDI koşulu**
- Bir skin seçilemiyorsa veya seçim karaktere yansımıyorsa → `K03 KALDI`
- İlk koşuda SMG/Shotgun **açık** geliyorsa → `K03 KALDI` (unlock kuralı çalışmıyor, `TS-209` ile çelişir)
- Kilitli silah listede hiç görünmüyorsa → `K03 kısmi` (bulgu: kilitli içerik gizleniyor, ilerleme okunmuyor)

> ⚠ **Bu adımı yalnız ilk koşunda görebilirsin.** `niyet §9a` gereği unlock merdiveni tek koşuda tükeniyor; ikinci koşudan sonra kilitli silah kalmıyor. Kapsam boşluğu `G-05`.

**Not alanı:** `_______________________`

---

## ADIM 3 · HI1 · KONTROL TEPKİSELLİĞİ · 1:45–3:30 · **öncelik 1**

**Ne yapacaksın** — bu adımda **düşman öldürmeye çalışma**, sadece kontrolü hisset.
1. Merkez avluda WASD ile sekiz yöne de yürü. Yön değiştir, dur, tekrar başla.
2. Fareyi karakterin çevresinde tam tur çevir. Karakterin dönüşünü izle.
3. **Aynı anda:** kuzeye yürürken güneye ateş et. Sonra doğuya yürürken batıya ateş et.
4. `R` ile reload et, reload sırasında hareket etmeye devam et.
5. Bir zombinin sana vurmasına izin ver. Vurulduğun anda **hareket ve ateş kontrolünü kaybediyor musun?**
6. Dash/dodge tuşu ara — `Shift`, `Space`, `Ctrl` dene. **Hiçbiri bir kaçış hareketi yapmamalı.**

**Beklenen**
- Tuşa bastığında karakter gecikmesiz hareket ediyor; durduğunda kayarak devam etmiyor.
- Nişan fareyi **geriden gelmeden** takip ediyor.
- Hareket ve nişan tamamen bağımsız; ateş yönü hareket yönünü çekmiyor.
- Hasar aldığında ekran kenarı/ses/küçük kamera tepkisi var ama **kontrol kesilmiyor**.
- Dash/dodge yok.

**KALDI koşulu**
- Ateş ederken karakterin hareket yönü değişiyorsa → `K05a KALDI` (`TS-222` çürütme koşulu tutmuş)
- Hasar aldığında bir an hareket edemiyorsan → `K05a KALDI` (REQ-045)
- Bir tuş kaçış hareketi yapıyorsa → `REQ-042 KALDI`

**HI1 PUAN (1-5):** `___`
`≤2` ise: **öncelik 1 KALDI** — bu, S1 gereği ucuz kriterlerden önce gelir.

**Serbest not — ne "yanlış" hissettirdi:** `_______________________`

---

## ADIM 4 · HI2 · VURUŞ VE İSABET HİSSİ · 3:30–5:00 · **öncelik 2**

**Ne yapacaksın**
1. Round 1'i normal oyna. Zombileri öldür.
2. Özellikle şu dördünü **ayırt etmeye çalış**:
   - açık ete vuruş
   - zırha vuruş (kabuto/dō/haidate görünen parça)
   - zırhın kırıldığı an
   - ölüm
3. Kafaya nişan al, bacağa nişan al. Farkı görüyor musun?
4. Bir zombinin **zırhını kıran** vuruşu izle: kırılma anında düşmanın canı düşüyor mu? (Düşmemeli — artan hasar boşa gitmeli.)
5. **F1** panelini aç, `Gold Mode` seçeneğini gör ama şimdilik dokunma, kapat.

**Beklenen**
- Ateş → namlu parlaması → tracer → yüzey darbesi → düşman tepkisi → ses zinciri kesintisiz.
- Zırh vuruşu et vuruşundan **hem sesle hem görüntüyle** ayrılıyor.
- Zırh kırılması bir **olay** gibi: çatlama + parça kopması + ayrı ses + üstteki kumaşın görünmesi.
- Hafif vuruşlar zombiyi her mermide tamamen durdurmuyor; kesintisiz stun-lock yok.

**KALDI koşulu**
- Zırh vuruşu ile et vuruşu ayırt edilemiyorsa → `K08 KALDI` + `HI2 düşer`
- Zırh kırıldığı an fark edilemiyorsa → `K08 KALDI`
- Her mermide düşman tamamen duruyorsa (stun-lock) → `REQ-123/124 KALDI`
- Hasar sayılarını (damage number) kapatınca hiçbir şey anlaşılmıyorsa → `TS-406 KALDI`

**HI2 PUAN (1-5):** `___`

**Serbest not:** `_______________________`

---

## ADIM 5 · ROUND DÖNGÜSÜ VE GOLD · 5:00–6:00

**Ne yapacaksın**
1. Round 1'i bitir. Son zombi ölünce ne oluyor, izle.
2. Round End Upgrade ekranında: 3 kart var mı? Gold bakiyen görünüyor mu? Bir kart al veya geç.
3. Geri sayımı izle, Round 2 otomatik başlasın.
4. Round 2'de HUD'a bak: **Health · Round · Enemies Remaining (havuzda kalan + yaşayan) · Gold · Weapon · Ammo** okunuyor mu?

**Beklenen**
- Havuz bittiğinde spawn duruyor; **son düşman ölünce** round tamamlanıyor.
- Round bitince simülasyon **donuyor** (donmuş bir kare değil — düşmanlar gerçekten duruyor).
- Upgrade ekranı → geri sayım (3 sn) → Round 2 otomatik.
- Reroll düğmesi prototipte **yok** (DEC-050/REQ-079 bir kez ücretli yenileme ister; uygulanmamış — kod-karar boşluğu, sonraki paket). Düğmenin yokluğu KALDI üretmez; varlığı da bu kitin kapsamında değildir.
- Harcamadığın gold Round 2'ye taşınıyor.

**KALDI koşulu**
- Upgrade ekranı açıkken arkada düşman hareket ediyorsa → `K15 KALDI`
- Geri sayım bitmeden veya hiç olmadan round başlıyorsa → `K16 KALDI`
- Gold round geçişinde sıfırlanıyorsa → `K10 KALDI`
- HUD'da "Enemies Remaining" iki sayıyı (havuz + yaşayan) ayrı göstermiyorsa → `TS-107 kısmi`

**Not alanı:** `_______________________`

---

## ADIM 6 · F1 · R3 SIÇRAMA · RUNNER VE SPECIAL WEAPON · 6:00–8:00

> **F1 panelini kullan.** `Round Jump → 3` ön ayarına bas.
> Gerekçe: `niyet §9c` — Runner Round 3'te garantili çıkar; doğal ilerleme 15 dk bütçesini yer.

**Ne yapacaksın**
1. **F1 → Round Jump → 3.** Paneli kapat.
2. Kırmızı/koyu sash'li, öne eğik siluetli **Runner**'ı bul. Lunge saldırısının hazırlığını (windup) gör.
3. Special Weapon çıkmasını bekle. Çıkmazsa **F1 → Trigger Special Weapon** ile zorla.
4. Işık sütununu ve HUD işaretini gör. **Sayı arama — yok.** Yerdeki silah yanıp sönüyor; sönme hızlandıkça süre azalıyor demek. Konuma git, al.
5. Üç Special Weapon'dan hangisi çıktığına bak — **Kagura Bell** (çan ağacı, imleç okumaz, çevrene basınç dalgası salar), **Ofuda Loom** (kâğıt tılsım mühürler; ≥3 mühür birikince mühürlüye ateş et, ağ tek seferde çöker) ya da **Tide Jewel** (kuyu açar, kalabalığı içeri çeker, sonra patlar). Üçünde de mermi sayacı ve reload yok.
6. Efektin standart silahlardan **belirgin biçimde daha güçlü** olduğunu gör. Son saniyelerde uyarı geliyor mu? Süre bitince **otomatik** Assault Rifle'a dönüyor musun?

**Beklenen**
- R3'te en az bir Runner **kesin** görülüyor ve Standard'dan siluetiyle ayırt ediliyor.
- SW spawn'ı ses + ışık sütunu + HUD işareti ile **birlikte** bildiriliyor.
- SW noktası ulaşılabilir ama seni güvenli rotandan çıkarıyor. Yerde **22 sn** duruyor (ölçüm: en uzak köşe hâlâ ulaşılamaz, kasıtlı).
- Yerdeki silahta **sayı yok**; yanıp sönme zamanla hızlanıyor ve HUD işareti ile **aynı vuruşta**.
- Aktif süre HUD'da **sayıyla** (bu bilerek kaldı: 3 sn ile 6 sn arasındaki fark yanıp sönmeyle ayırt edilemez); son saniyelerde görsel **ve** işitsel uyarı; süre bitince klasik silah geri geliyor.

**KALDI koşulu**
- R3'te Runner hiç görülmüyorsa → `K06/TS-219 KALDI`
- SW alındığında klasik silah kaybolup **geri gelmiyorsa** → `K13 KALDI`
- Aynı anda yerde iki SW görürsen → `K12 KALDI`
- Süre HUD'da yoksa veya son saniye uyarısı yoksa → `K13 kısmi`

**Not alanı:** `_______________________`

---

## ADIM 7 · F1 · R5 SIÇRAMA · GUARD, ZIRH VE PATLAMA · 8:00–9:30

> **F1 → Round Jump → 5.** Ayrıca upgrade ekranında **Explosion** dalını al (gold yetmezse `F1 → Give Gold`).

**Ne yapacaksın**
1. Geniş siluetli, koyu zırhlı **Guard**'ı bul. İki kollu sweep saldırısının telegraph'ını gör — vurulmadan kaçabiliyor musun?
2. Tam zırhlı (miğfer + gövde + bacak) bir zombi bul. **Patlamayla** vur.
3. Patlamadan sonra düşmanın **canı düştü mü?** (Üç zırh da tamsa düşmemeli — bir parça kırılana kadar patlama can hasarı yapmaz.)
4. Bacağına, gövdesine, kafasına ayrı ayrı vur. Açık kafaya vuruş belirgin şekilde daha güçlü mü?

**Beklenen**
- Guard'ın telegraph'ı **okunabilir**: windup → active → recovery ayrımı görülüyor.
- Tam zırhlı düşmanda ilk patlama yalnız zırhları yıpratıyor.
- Zırh kırıldıktan sonra o bölgeye vuruş cana gidiyor.

**KALDI koşulu**
- Guard'ın saldırısı önceden okunamıyorsa (kaçınılamıyorsa) → `HI4 düşer` + bulgu
- Tam zırhlı düşman ilk patlamada can kaybediyorsa → `K09 KALDI` (`TS-206` çürütmesi tutmuş)
- Zırh kırıldıktan sonra da o bölge hasar almıyorsa → `K08 KALDI`

**Not alanı:** `_______________________`

---

## ADIM 8 · F1 · R8 + 40 DÜŞMAN ZORLAMA · HI3 + HI4 · 9:30–11:30 · **öncelik 3 ve 4**

> **F1 → Round Jump → 8** · sonra **F1 → Force Enemy Count → 40**
> Gerekçe: `niyet §9b/§9c` — `min(7+2R,32)` tavanı 32; **40 yükü yalnız panelden zorlanarak oluşur.** Executioner R8'de gelir.

**Ne yapacaksın**
1. R8'e sıçra. Yavaş, büyük, kolay tanınan **Executioner**'ı bul. Overhead smash'ini bir kez ye, bir kez kaç.
2. **Force Enemy Count → 40.** Paneli kapat (panelin kendi çizim yükü ölçümü bozar).
3. Halka rotada koş: **kuzey taş yolu → doğu sakura → güney köprü → batı bambu koridoru.** Sürüyü peşine tak.
4. **Köprüde** dur ve sürünün üstüne gelmesini bekle. Sıkışıp kalıyor musun, yoksa çevrelenme bir tehdit ama çözülebilir mi?
5. Bambu koridorunda ve saray duvarının arkasında: kamera ile senin aranda kalan nesne **saydamlaşıyor mu?**
6. Bu kaosun ortasında: kendi vuruşunun geri bildirimini hâlâ görebiliyor musun? Gold orb'ları çevreden ayrılıyor mu?

**Beklenen**
- 40 düşman sahnedeyken oyun akıcı kalıyor (belirgin takılma yok).
- Sürü akıyor, üst üste binmiyor, birbirini hafifçe itiyor.
- Çevrelenmek **gerçek tehdit** ama trafik kilidi yok.
- Görüş engelleri saydamlaşıyor; kamera aniden yaklaşmıyor; sarsıntı nişanı bozmuyor.

**KALDI koşulu**
- Köprüde 3 saniyeden uzun süre **hiç hareket edemiyorsan** → `TS-510 KALDI` (REQ-046/047 çelişkisi)
- 40 düşmanda gözle görülür takılma varsa → `TS-302 KALDI` (ölçülen fps rapora yazılır)
- Kalabalıkta kendi vuruşunun efekti kayboluyorsa → `TS-504 KALDI` + `HI2 düşer`
- Kamera engeli saydamlaşmıyorsa veya sarsıntı nişanı kaydırıyorsa → `HI4 KALDI`

**HI3 PUAN · sürü baskısı (1-5):** `___`
**HI4 PUAN · kamera ve okunabilirlik (1-5):** `___`

**Serbest not — baskı gerçek miydi, yoksa sadece kalabalık mıydı:** `_______________________`

---

## ADIM 9 · HI5 · ATMOSFER + PAUSE + GOLD MODU · 11:30–13:00 · **öncelik 5**

**Ne yapacaksın**
1. **F1 → Force Enemy Count → 0** (veya `Clear Enemies`). Panel kapat.
2. Dört bölgeyi yavaşça gez: saray önü taş yolu · sakura bahçesi · koi göleti ve köprü · bambu koridoru. Her bölgede 10 sn dur ve bak.
3. Sonra **F1 → Force Enemy Count → 25** yap ve aynı yerlerde savaşırken bak: sis/yaprak/polen **düşmanı veya nişanı kapatıyor mu?**
4. **Escape** ile Pause. 10 saniye bekle. Ekranda hiçbir şeyin ilerlemediğini doğrula: düşmanlar, mermiler, Special Weapon sayacı, can yenilenmesi.
5. Pause'tan çık — kaldığın yerden devam ediyor mu?
6. **F1 → Gold Mode** `ProximityMagnet` (onaylı ürün kararı, 2026-09-10) ile birkaç zombi öldür. `ContactPickup` / `DirectCredit` yalnız geliştirici karşılaştırma modudur; denenmesi isteğe bağlı, ürün kabulü üretmez.

**Beklenen**
- "Güzel ama tehditkâr" oluşuyor: sakin ışık, uzak sis, seyrek yaprak — ama boşluk hissi rahatsız edici.
- Bölgeler landmark'larıyla ayırt ediliyor.
- Partiküller savaşta **okunabilirliği bozmuyor**.
- Pause **her şeyi** donduruyor; 10 saniye sonra SW sayacı aynı değerde.
- `ProximityMagnet` ile gold yakınlıkta çekiliyor ve toplanıyor (ürün kabulü). Diğer modlar: yalnız debug kontrolü; KALDI/GEÇTİ üretmez.

**KALDI koşulu**
- Pause'ta bir sayaç ilerliyorsa (özellikle SW süresi veya regen gecikmesi) → `K26 KALDI` — **bu tur-kırandır**, ölçüm ortamının kendisi bozulur
- Sis/yaprak savaşta düşmanı gizliyorsa → `HI5 KALDI` **ve** `HI4 KALDI`
- Bir gold modu geçişte çöküyorsa veya gold vermiyorsa → `K10 KALDI`

**HI5 PUAN · atmosfer (1-5):** `___`

**Serbest not:** `_______________________`

---

## ADIM 10 · ÖLÜM → SONUÇ → İKİNCİ KOŞU · 13:00–15:00 + SERBEST NOT

**Ne yapacaksın**
1. Ölmeye çalış — düşmanların ortasında dur. **Revive teklif ediliyor mu?** (Edilmemeli.)
2. **Run Results** ekranını okumadan önce ekran görüntüsü al.
3. On iki alanı say: Reached Round · Total Kills · Standard · Special · Elite · Ultra · Completed Round XP · Kill XP · Total XP Earned · Profile Level Progress · Newly Unlocked Content · Achievements Earned.
4. **Kaydırma gerekiyor mu?** (Gerekmemeli.) Yeni açılan içerik göze çarpıyor mu? **Claim düğmesi var mı?** (Olmamalı.)
5. Main Menu'ye dön. `Statistics` ve `Achievements` bölümlerine gir — Run Results'taki sayılar buraya işlenmiş mi?
6. Tekrar `Play` → Seçim ekranı: **son kullandığın skin ve silah hazır seçili mi?** Yeni açılan silah listede mi?
7. Play'i durdur, tekrar başlat. Seçim ekranına gir: **varsayılan skin/silaha dönmüş mü?**
8. Son olarak Console penceresine bak: **kaç kırmızı, kaç sarı?**

**Beklenen**
- Revive yok, koşu doğrudan bitiyor.
- 12 alan tek ekranda, kaydırmasız, okunacak büyüklükte.
- Yeni içerik otomatik açılmış, Claim gerekmiyor.
- İstatistikler ve başarımlar profile işlenmiş.
- Aynı oturumda son seçim hazır; yeniden başlatınca varsayılana dönmüş.
- Console: **0 kırmızı**.

**KALDI koşulu**
- 12 alandan biri eksikse veya kaydırma gerekiyorsa → `K19 KALDI`
- Claim düğmesi varsa → `K19 KALDI`
- Yeniden başlatınca son seçim hâlâ geliyorsa → `K24 KALDI`
- Console'da **tek bir kırmızı** varsa → `K27 KALDI` — **tur-kırandır**, otomatik kapı yeniden koşulur

**Serbest not — bu 15 dakikada seni en çok rahatsız eden şey neydi:**
`_______________________________________________`
`_______________________________________________`

**Serbest not — bu 15 dakikada beklemediğin biçimde iyi olan şey neydi:**
`_______________________________________________`

---

## GAMEPAD EKİ · **KOŞULAMAZ** · `⊘ G-01`

`niyet §9d` — makinede gamepad donanımı yok. Aşağıdaki adımlar **rapora `koşulamadı (donanım)` olarak** girer; `geçti` veya `kaldı` yazılmaz. Kullanıcı bir gamepad takarsa koşulur.

| # | Adım | Beklenen |
|---|---|---|
| GP-1 | Sol analog ile hareket, sağ analog ile nişan — aynı anda ters yönler | Bağımsız çalışır, karakter nişan yönüne döner |
| GP-2 | Right Trigger ile otomatik ateş, standart tuşla reload | Hold-to-fire çalışır |
| GP-3 | Sağ analog ile sınırlı kamera kadraj kayması | ~2.5 m sınırı aşılmıyor, kamera döndürülemiyor |
| GP-4 | Pause/Menu tuşu | Simülasyon donuyor |

**K05 rapor biçimi (bağlayıcı):**
```
K05a  klavye + fare  ·  <geçti|KALDI>
K05b  gamepad        ·  koşulamadı (donanım yok — niyet §9d)
```

---

## SONUÇ KARTI — rapora aynen kopyalanır

```
MANUEL KİT · G-deaths-demolition-10
ortam    : Unity 2022.3.22f1 · URP 14.0.10 · macOS · 1920×1080 · Editör Play Mode
tarih    : ____________   süre: ____ dk

HIS İMZASI (S5 — yalnız kullanıcı doldurur)
  HI1 kontrol tepkiselliği ...... ___/5   → doğrulandı / KALDI
  HI2 vuruş ve isabet hissi ..... ___/5   → doğrulandı / KALDI
  HI3 sürü baskısı .............. ___/5   → doğrulandı / KALDI
  HI4 kamera ve okunabilirlik ... ___/5   → doğrulandı / KALDI
  HI5 atmosfer .................. ___/5   → doğrulandı / KALDI
  (doldurulmayan satır "doğrulanmadı" kalır — "kapandı" yazılmaz)

ADIM SONUÇLARI
  1 ___  2 ___  3 ___  4 ___  5 ___  6 ___  7 ___  8 ___  9 ___  10 ___

KALDI sayısı: ___     → her biri BULGU'ya çevrilir
KOŞULAMADI  : GP-1..GP-4 (gamepad donanımı yok)
```
