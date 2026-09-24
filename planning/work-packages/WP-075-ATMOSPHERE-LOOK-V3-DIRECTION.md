# WP-075 — Atmosfer ve Görünüm v3: yön belgesi (Faz A)

Sahip: S-05 · Koordinasyon: C-00 · Tarih: 2026-09-24 · Dal `wp-075-look-v3` · Konsey (Fable 5.1): art-director · Paydaşlar: P-05 AP-023 (gök v2 plakaları), S-07 WP-073 (parçacık × cookie, darbe post), S-01 WP-070 (mod profili, FP DoF odağı), S-09 WP-068 R1 (grain/vinyet UI dışı, HUD kontrast).
Çerçeve: DEC-092 (60 fps), DEC-093 (5 GB; ADR-015-1 v3 — `WP-074-WORLD-V5-DIRECTION.md` §4), kullanıcı maddeleri N9, N13, N15 (Lightroom sınıfı), N19 (renk). Kalite kademeleri v2 sözleşmesi: `planning/work-packages/QUALITY-TIERS-V2.md` (bu dalda, ilk teslimin parçası).

Bu belge = §0 S-05 kararları + §1 Faz B dilim planı + Ek A (AD konsey metni, birebir). Çelişkide §0 geçerlidir.

## 0 · S-05 kararları (bağlayıcı)

| # | Konu | Karar |
|---|---|---|
| L1 | Görünüm tezi | Ek A §0 "Yağmur sonrası açılma": bulut örtüsü %60 ± 5, zemin %55–65 bulut gölgesinde, güneş aksan; hedefin yoğunluk/malzeme/sis derinliği korunur (M4-R1 §7). |
| L2 | Işık | Ek A §1.1 değerleri `WorldAtmosphere` sabitlerine (tek kaynak): güneş Euler (33, 52, 0), 4900 K #FFE2C6, yoğunluk 2,0 (cookie ort. 0,62); ambient trilight #B3C0CE / #8B9088 / #474C43; yansıma 0,70; sis rengi #D0D7DE değişmez. WP-074 AD'nin 215°/38° önerisi bunun yerine geçmez (WP-074 §0 K7). |
| L3 | Gök doku sınıfı | Ek A'nın istediği Sky 1024 → 2048 ADR-015-1 v3 ile çözüldü (Sky 4096); bant 2048×1024 lat-long üst yarı, bulut levhaları 1024² (Vfx 2048 tavanı içinde). |
| L4 | Huzme | Dünya-uzayı kartlar tüm modlarda (cookie ile yanar/söner) + ekran-uzayı radyal huzme yalnız TP/FP (yarım çözünürlük renderer feature). Okunabilirlik yasakları Ek A §3 bağlayıcı (meydan merkezinde kart yok, zemin +0,12 V, kare +0,08 V tavanı). |
| L5 | Sis | "Çok hafif" = yerel sis toplam opaklığı ≤ %12, savaş bandında ≤ %3 (Ek A §5). Tepeden mesafe sisi 52 → 210 m değişmez (REQ-104); TP/FP 45 → 235 m + yükseklik sisi; cull/sis sonu pop yok (fade-out). |
| L6 | LUT v2 | Ek A §6 B5 reçetesi Adobe MCP Lightroom sınıfı araçlarla (M4-R1 §4.6) 15 referans karede; şeride yalnız global işlemler (Texture/Clarity/Dehaze 0); ΔE kapısı ort ≤ 6, hero kare ≤ 4 (ADR-029-1 zinciri). |
| L7 | Post | Tonemap Neutral (REQ-105), bloom eşik 1,10, grain kademe başına, vinyet, CA oyun içinde yok, DoF/motion blur yalnız TP/FP Sinematik (Ek A §7). |
| L8 | Yansıma | 10 baked prob (gök v2 ile yeniden bake), koi havuzlarına tek planar kamera (Yüksek 256 / Sinematik 512 + karakter). |
| L9 | Kademeler | `QUALITY-TIERS-V2.md` bağlayıcı; Ek A §8 "hiçbir kademede kaybedilmez" listesi sözleşmeye taşındı. |
| L10 | Higgsfield | Gök v2 (bant + 2 bulut levhası + siluet plakaları) ≤ 18 kr, AP-023'ün 70 kr tavanı içinde (P-05 üretir); hedef v3 derecelendirmesi Adobe (0 kr). |

## 1 · Faz B dilim planı (her dilim ayrı commit)

| Dilim | İçerik | Kapı |
|---|---|---|
| C1 Kademe çekirdeği | `QualityTiers` API + QualitySettings 6 → 3 seviye + 3 URP varlığı + akış bütçeleri (ADR-015-1 v3) + `CameraModeService.CurrentProfile` birleşimi (sözleşme §3) + UiSettings eşlemesi (S-09 ekran adları) | EditMode API/eşleme testleri; mevcut görünüm Yüksek'te ΔE 0 |
| C2 Işık v3 + bulut gölgesi | `WorldAtmosphere` v3 sabitleri, `T_cloud_cookie` projeksiyonu (240 m, 1,3 m/s KD), DEC-077 4 faz ölçümü, `LookReferenceTests` 15 kare | K7, K12 |
| C3 Gök v2 | gök bandı + 2 bulut düzlemi (500 / 900 m, parallaks), ufuk plakaları v2 içeriği (AP-019 sözleşmesi korunur), ufuk dikişi | K6, WP-074 E-3 |
| C4 Huzme | dünya-uzayı kart shader'ı (cookie örnekli) + 15 kart yerleşimi; TP/FP ekran-uzayı radyal huzme renderer feature | K8, K9 |
| C5 Bölgesel sis | yer sisi levha shader'ı (gürültü + cookie) 7 levha; TP/FP yükseklik sisi feature; FP 3 m solma | K5, K10, K11 |
| C6 LUT v2 | B5 reçetesi (Adobe) → `DD_Look_v2.png` + recipe.json; hedef v3 görselleri | K13 |
| C7 Post + yansıma | bloom/grain/vinyet/DoF/motion blur kademe tabloları; 10 prob; havuz planar | K14 |
| C8 Kanıt | yan yana Y1–Y6 (üç mod), metrics.json K1–K16, kademe × mod perf raporu (sözleşme §5) → Fable AD turu | K1–K16, DEC-092 |

Perf bütçesi (öneri, sözleşme §5): atmosfer v3 ≤ +2,0 ms (Yüksek, tepeden); TP/FP ekran-uzayı huzme + yükseklik sisi ayrıca raporlanır.


## Ek A — AD konsey metni (Fable 5.1, birebir; başlıklar bir düzey alta, mutlak yollar ~/)

## WP-075 — Atmosfer & Görünüm v3: Sanat Yönü Belgesi

**Sahip:** art-director (Fable 5.1) · **Uygulayıcı:** S-05 (unity-specialist) · **Varlık:** P-05 AP-023 (gök v2 plakaları) · **Tarih:** 2026-09-24
**Kaynak:** `m4-r1/M4-R1-REVIZYON-TURU.md` §0 N9/N13/N19, §4.7, §7 · `wp-074/COUNCIL-CONTEXT.md` · hedef `wp-015/higgsfield/selected/DD_StyleRef_DEC076_{overview,gameplay}_2k.png` · Look B `wp-029/derived/lightroom/B/` · güncel kareler `wp-074/captures-current/world-v3/` · canlı değerler `WorldAtmosphere.cs`, `DD_Overcast_v1.asset`, `World_AP005.unity`, stil kılavuzu v1 §2/§5.
**Tek kaynak kuralı:** bu belgedeki sayılar `WorldAtmosphere` sabitlerine ve `DD_Look_v2.recipe.json`'a yazılır; sahnede elle değer kalmaz. Bugünkü değerlerden sapmalar "v2 → v3" biçiminde verilir.

---

### 0 · Gerilim ve çözüm: "Yağmur sonrası açılma"

Hedef görsel (DEC-076) kapalı, puslu-gri ve yağmur-ıslak; kullanıcı parçalı bulut, güneş huzmesi ve daha canlı renk istiyor. İkisi aynı sahnede aynı anda doğrudur: **yağmur yeni dinmiş, bulut örtüsü güney-batıdan yırtılıyor; alçalmış öğleden sonra güneşi yarıklardan giriyor; taş hâlâ ıslak, pus henüz kalkmamış, sadece alçak bölgelere ve uzağa çekilmiş.** Bu an fotoğrafçılıkta en doygun renklerin göründüğü andır: ıslak yüzeyler koyulaşıp doygunlaşır, dağınık gök ışığı gölgeleri soğuk tutar, güneş sıcak ve yönlüdür.

Korunan (hedefin kimliği): dressing yoğunluğu, ıslak arduvaz değeri ve parlaması, sis derinlik katmanları (90 m'de %24 rampası), sıva > sakura > taş > yeşil > ahşap değer sırası, sakura pastel, koi havuzu yansıması, çok katmanlı ufuk.
Değişen: gök içeriği (düz gri → parçalı), anahtar ışık (yumuşak/renksiz → alçak, sıcak, yönlü, bulut gölgesiyle modüle), kontrast (+1 kademe yerel), doygunluk (vibrance +, her malzeme kendi bandında), ışık huzmeleri, bölgesel yer sisi.
Kural: **Bulut gölgesi baskın, güneş aksan.** Zemin alanının her an ≈%55–65'i bulut gölgesinde (= hedefin kapalı hava okuması), %35–45'i güneşte. Güneş tamamen açık bir sahne hiçbir zaman olmaz.

---

### 1 · Görünüm tezi ve palet

**Tez.** "Islak Arduvaz — Açılma": bahar öğleden sonrası (≈15:30, sakura mevsimi), yağmur 20 dakika önce dinmiş. Güney-batıdan yırtılan stratokümülüs örtüsünün aralıklarından 33° yükseklikte sıcak (4900 K) güneş, soğuk (7200 K) gök dolgusunun üstüne yamalar düşürür; ıslak arduvaz meydanı bu yamaları aynalar, sıva panelleri güneşte fildişi-sıcak, gölgede serin okunur. Uzak ufuk hedefin pusunda kalır; pus yakında yalnız havuz üstü, bambu tabanı ve saray arkasında çok hafif, hareketli levhalar hâlinde yaşar. Batı bambusundan ve doğu sakura tacından giren huzmeler soğuk sisi ısıtır. Renk canlılığı doygunluk düğmesinden değil ışık/malzeme fiziğinden gelir: vibrance ile bastırılmış tonlar açılır, rezerv aksanlar (altın, vermilion) DEC-077 ile aynı kalır.

#### 1.1 Işık kurulumu (WorldAtmosphere v2 → v3)

| Öğe | v2 (bugün) | v3 | Not |
|---|---|---|---|
| Güneş yönü | Euler (42, 25, 0) — 42° yükseklik, pusuladan 205° | **Euler (33, 52, 0)** — **33° yükseklik, pusuladan 232° (BGB)**, ışık KD'ye (52°) gider | 1,8 m figür gölgesi 2,8 m, KD'ye düşer = tepeden kamerada sağ-üste, figürü örtmez; saray (kuzey) gölgesi meydan dışına |
| Güneş rengi | #FFF8EC (≈6200 K) | **#FFE2C6, 4900 K** | Alçak güneş, yağmur sonrası temiz hava |
| Güneş yoğunluğu | 1,15 | **2,0** (cookie ort. 0,62 ile etkin ≈1,25) | Güneş yaması bulut gölgesine göre +0,8 EV |
| Gölge gücü | 0,5 | **0,75**, yumuşak gölge açık, 4 kaskat 60 m (TP/FP 80 m) | Bulut gölgesinde cookie zaten kısar; sert öğle gölgesi yasağı (S3) korunur: penumbra ≥ 6 cm/m |
| Ambient sky (trilight) | #9DA7B3 | **#B3C0CE (7200 K)** | Gölgeler serin ve %15 daha açık |
| Ambient equator | #72786B | **#8B9088** | Zeytin kaymasını azaltır (nötr-gri) |
| Ambient ground | #373B31 | **#474C43** | |
| Yansıma yoğunluğu | 0,55 | **0,70** | Islak yüzey gök yansıması |
| Sis rengi | #D0D7DE | **#D0D7DE (aynı)** | Hedef pusu; güneş tarafı sıcaklığı bant/plakalarda (§2), sis renginde değil |
| Post exposure | 0 | **0** | Parlaklık ışıkta ve LUT'ta; çift uygulama yok |

#### 1.2 Renk paleti (sRGB)

| Rol | Hex | HSV | K | Kullanım |
|---|---|---|---|---|
| Anahtar (güneş) | #FFE2C6 | 29°, 0,22, 1,00 | 4900 | Directional light color |
| Dolgu (gök) | #B3C0CE | 211°, 0,13, 0,81 | 7200 | Ambient sky; gölge rengi bundan türer |
| Gök aralığı, zenit | #8FA8C0 | 209°, 0,25, 0,75 | ≈8000 | Bant üst; yağmur sonrası soluk mavi, asla doygun mavi |
| Gök aralığı, alçak | #A6BBD0 | 210°, 0,20, 0,82 | — | 10–30° yükseklik aralıkları |
| Bulut aydınlık kenar (güneş tarafı) | #F2EBE0 | 37°, 0,07, 0,95 | 5200 | Bantta en açık değer; sıvadan (0,90) açık olan tek şey |
| Bulut gövde | #D8DBDE | 210°, 0,03, 0,87 | — | |
| Bulut alt gölge | #9AA4B0 | 213°, 0,13, 0,69 | — | |
| Karşı taraf (KD) gök | #C4CDD6 | 210°, 0,08, 0,84 | — | Tepeden kamerada arduvaz bunu yansıtır → V ≥ 0,70 zorunlu |
| Ufuk / mesafe sisi | #D0D7DE | 210°, 0,06, 0,87 | — | DEC-076, değişmez |
| Güneş tarafı ufuk tonu | #DBD9D3 | 45°, 0,04, 0,86 | — | Bant ufuk satırında 232° ± 40° |
| Yer sisi, gölgede | #D3D9DF | 210°, 0,05, 0,87 | — | Levha taban rengi |
| Yer sisi, güneşte | #E9E2D5 | 39°, 0,09, 0,91 | — | Levha × cookie |
| Huzme tonu | #FFE7C4 | 36°, 0,23, 1,00 | 4900 | Kart ve ekran-uzayı |
| Bulut gölgesinde arduvaz | #4B545C | 208°, 0,18, 0,36 | — | Ölçüm referansı |
| Sahne siyah tabanı | #0E1114 | — | — | Hiçbir kare bunun altına inmez |

Rezerv aksanlar değişmez: altın orb #FFC74D, vermilion #C4362B, sakura aksanı #E8A0B8, LanternGlow #F7C766 (DEC-077 rezervi s > 0,55 & v > 0,80 çevrede yasak).

#### 1.3 Pozlama niyeti (ölçülebilir, post + LUT sonrası sRGB V)

| Kare | Ölçüm | v3 hedef | Bugün (yaklaşık) |
|---|---|---|---|
| gp-centre, güneş yaması | Islak arduvaz ortalama V | **0,46 ± 0,05**; spekular tepe 0,70–0,80 | 0,31, spekular yok |
| gp-centre, bulut gölgesi | Islak arduvaz ortalama V | **0,34 ± 0,04** | — |
| gp-centre | Kare ortalama luma | **0,36–0,42** | ≈0,20 |
| ov-south-high | Kare ortalama luma / gök bandı | **0,45–0,52 / 0,72–0,80** | ≈0,30 / 0,74 (sarı-bej) |
| Tümü | Kırpılan beyaz (V > 0,98) | **≤ %0,5** (yalnız spekular/bloom) | — |
| Tümü | Çöken siyah (V < 0,03) | **≤ %2** | — |
| tp/fp sıva paneli | güneşte / gölgede V | **0,86 ± 0,04 / 0,68 ± 0,04**, hue 40–50° | 0,55, hue 60° (zeytin) |

#### 1.4 Malzeme başına canlı-ama-doğal doygunluk hedefleri (post sonrası, güneş yaması / bulut gölgesi)

| Malzeme | Hue | S (güneş / gölge) | V (güneş / gölge) | Kim taşır | Kural |
|---|---|---|---|---|---|
| Yosun | 85–95° | 0,50–0,58 / 0,42–0,48 | 0,40–0,48 / 0,30–0,36 | L (green +14) + I | Hedef yosunu stil kılavuzu 0,41'den doygun; s > 0,60 "neon" ret |
| Sakura | 335–345° | 0,30–0,36 / 0,26–0,30 | 0,84–0,90 / 0,74–0,80 | L (magenta +10) | Pastel kalır; hue 330° altına (mor) kaymaz |
| Ahşap (ıslak kereste) | 22–30° | 0,32–0,40 / 0,28–0,34 | 0,30–0,40 / 0,22–0,28 | M (ıslak koyulaşma) + L (orange +8) | Islakken 0,06 daha koyu, 0,05 daha doygun |
| Sıva | 40–50° | 0,05–0,09 | 0,84–0,90 / 0,66–0,72 | L (yellow −10 sat, +8 lum) + I equator | **Her zaman en açık opak yüzey** (kılavuz §2 sırası); bulut kenarı hariç |
| Arduvaz (ıslak) | 205–215° | 0,10–0,16 / 0,14–0,20 | 0,42–0,52 / 0,30–0,38 | M (AP-018 smoothness ≤ 0,60; birikinti maskesinde 0,72) + I | Ayna değil; spekular alanı kare başına ≤ %8 |
| Su (koi) | 195–205° | 0,25–0,35 | yansıma baskın; taban 0,30–0,45 | yansıma (§7) | Gök aralığı yansırken bulut kenarı bloom edebilir |
| Kiremit | 212–220° | 0,12–0,18 | 0,30–0,38 | M | Zeminden ≥ 0,08 V farklı kalır (WP-020 MUST-2) |
| Zombi ten (Flesh #7E8674) | 85–90° | 0,12–0,16 | 0,50–0,56 | değişmez | Yosunla aynı hue: zombi asla yosun üstünde tek başına ölçülmez; ΔV zemine ≥ 0,08 her iki cookie fazında |
| Zombi kumaş (koyu küme) | — | — | 0,14–0,30 | L (shadows +12, blacks −8) | Küme açılır ama 0,12 tabanı korunur; tabanla arduvaz farkı ≥ 0,12 |
| Oyuncu fildişi / kızıl | 44° / 358° | 0,10 / 0,80 | 0,90 / 0,64 | değişmez | Güneş yamasında fildişi ≤ 0,95 (kırpılmaz); kızıl bant s ≥ 0,70 kalır |

---

### 2 · Gök

#### 2.1 Kubbe
- **Bulut örtüsü %60 ± 5** (kubbenin gök aralığı payı %35–45). Tip: kırılan stratokümülüs deck + kümülüs parçaları; yatay bantlı stratus yalnız ufukta (0–6°).
- **Hareket yönü KD (pusuladan 52°'ye doğru)** — güneşin ışık yönüyle aynı, rüzgâr sözleşmesi `DD_Foliage_Wind` aynı yöne çekilir (S-07/S-05).
- İki bulut düzlemi (far-plane derinlik hilesi, AP-019 plaka tekniği): **yakın kümülüs y 500 m, 3,5 m/s (≈0,40°/s)**; **uzak deck y 900 m, 5,0 m/s (≈0,32°/s)**. Parallaks: yakın daha hızlı. Döşeme 2 km × 2 km, 4× / 6× tekrar; tekrar dikişi zenitte görünmez (doku kenarları kesintisiz).
- Bulut ölçeği: yakın parçalar açısal 8–20°, uzak deck hücreleri 25–45°. Aralıklar ("mavi pencereler") 6–18° çapında, yumuşak kenar.
- Işıklandırma doku içine pişer: güneş tarafı (B/GB) kenarlar #F2EBE0, gövde #D8DBDE, alt #9AA4B0; karşı taraf (KD) gövde 0,05 V daha koyu. Güneş diski **yok**; 232°/33° etrafında 25° yarıçaplı yumuşak parlama (tepe #F3ECE0) bantta.
- **Tepeden kamera kuralı:** arduvazın yansıttığı KD gök çeyreği V ≥ 0,70 (yansıma probu/skybox örneği ölçülür).

#### 2.2 Ufuk (AP-019 sözleşmesi korunur, içerik v2)
Yarıçaplar 260/330/420 m, 8/6/4 tekrar, taban −1 m, `DD/SkyPlate` aynen.
- **Yakın plaka (260 m, 51 m):** çam/sedir hattı; B-GB (200–260°) kesiminde güneş rim'i (kenar #E9DFCB, %6 alan); KB (300–340°) sedir kümesi 1,3× yüksek (NW_ServiceGate çıkışını kapatır).
- **Orta plaka (330 m, 86 m):** tepeler + **siluetler**: beş katlı pagoda KD 35° (yükseklik 9° ufuk üstü), tapınak kapı/çatı kümesi DGD 120°, tek torii GB 250° (huzme yönünde, karşı ışık siluet). Ton: sis rengine %55 karışmış.
- **Uzak plaka (420 m, 178 m):** dağ sırtı 2–4°, ton sis rengine %80 karışmış, güneş tarafında #DBD9D3, karşıda #CBD3DB.
- Sis dikişi: plaka altı = sis rengi; plakadan gök bandına geçişte luma adımı ≤ 0,04 V / 10 px (ov-south-high'daki sert bant yasak).

#### 2.3 P-05 AP-023 gök v2 plaka spesifikasyonu

| Varlık | Boyut | Sınıf | İçerik |
|---|---|---|---|
| `T_skyband_v2_BaseColor` | 2048×1024 lat-long üst yarı (Sky sınıfı cap 1024→2048 gerekiyorsa ADR-015-1 v6, C-00) — fallback 2048×512 | Sky | gradyan #D0D7DE ufuk → #A6BBD0 → #8FA8C0 zenit; güneş parlaması; ufuk stratusu; **bulut yok** (bulutlar düzlemlerde) |
| `T_cloud_near_Sheet` | 1024×1024 tileable RGBA (RGB ışıklı bulut, A örtü) | Vfx | kümülüs parçaları, örtü %35, kenar yumuşaklığı 6–10 px |
| `T_cloud_far_Sheet` | 1024×1024 tileable RGBA | Vfx | stratokümülüs deck, örtü %45, ince yırtıklar |
| `T_horizon_{near,mid,far}_Sheet` v2 | 2048×512 (aynı) | Vfx | §2.2 içerik; alfa; yeniden derecelendirilmiş |
| `T_cloud_cookie` | 1024×1024 R8 tileable | Lighting | §4; bulut düzlemlerinin örtü maskesinden türetilir (aynı desen ailesi) |

Higgsfield: gök bandı + 2 bulut levhası + siluet plakaları = AP-023 tavanı içinde (≤ 18 kr önerisi); bulut levhaları alfa çıkarımı Adobe (0 kr).

#### 2.4 Kamera moduna göre okuma
- **Tepeden:** gök hiç görünmez; gök üç yoldan var olur — bulut gölgesi (§4), huzme kartları (§3), havuz/arduvaz yansıması (§7). Bu üçü aynı yön/hız ailesindedir.
- **Üçüncü şahıs (2,2 m, vfov 50):** ufuk plakaları + 0–22° gök; kadrajın üst %25'i gök/bulut. Bulut düzlemi parallaksı burada okunur.
- **Birinci şahıs (1,6 m, vfov 60):** yukarı bakışta 70°'ye kadar kubbe; bulut düzlemlerinin döşeme dikişi ve ufuk plakası üst kenarı (23°) görünmez olmalı (alfa gradyanı ≥ 4°).

---

### 3 · Güneş huzmeleri

İki mekanizma, tek görünüm:

**A. Dünya-uzayı huzme kartları (tüm kameralar).** Yumuşak additive şerit/koni mesh'leri, güneş yönüne (Euler 33/52) hizalı, derinlik solması, düşük frekanslı kaydırmalı gürültü. **Şiddet = kart şeffaflığı × cookie örneği (kartın tepe noktasında, §4 ile aynı projeksiyon)** → huzme yalnız bulut aralığı geçerken yanar, gölgeyle söner (0,6–1,2 s yumuşatma).

| Bölge | Kart | Uzunluk × genişlik | Tepe alfa | Not |
|---|---|---|---|---|
| Batı bambu (−30, 0) | 4 | 10–14 m × 1,5–2,5 m | 0,26 | İmza huzme; TP/FP batıya bakışta ana kadraj |
| Batı koridor (kairō) iç | 3 | 6–8 m × 1,2 m | 0,22 | Açıklıklardan giren; iç mekân kadrajı |
| Doğu sakura tacı (30, 0) | 3 | 8–12 m × 2–3 m | 0,20 | Taç altı, petal parçacıkları huzmede parlar (S-07: parçacık × cookie) |
| Saray verandası / saçak (0, 30) | 2 | 6–9 m × 1,5 m | 0,18 | Kafes gölgesi ritmi ile birlikte |
| Güney köprü / havuz | 2 | 8 m × 2 m | 0,14 | Havuz sisi üstünde |
| Orta pavyon | 1 | 6 m × 1,2 m | 0,14 | Meydan merkezi kuralına bakınız |

Ton #FFE7C4; kenar yumuşaklığı ≥ 0,4 m; zemin temasında alfa → 0 (son 1,5 m).

**B. Ekran-uzayı radyal huzme (yalnız TP/FP).** Derinlik tabanlı güneş maskesi + radyal bulanıklık (S-05 renderer feature, yarım çözünürlük). Güneş ekran konumu frustum + %25 marj içindeyken aktif; marj dışına çıkınca 0,5 s'de söner. Parametre: 32 örnek (Sinematik) / 20 (Yüksek), yoğunluk 0,90, ağırlık 0,45, sönüm 0,94, tepe katkı **0,35 (Sinematik) / 0,22 (Yüksek)**, ton #FFE7C4. Çatı, ağaç, bambu, kafes doğal olarak örter (derinlik). Cookie ile çarpılır: bulut kapalıyken ekran huzmesi de söner.

**Yasaklar (okunabilirlik):**
- Tepeden kamerada meydan merkezi (0,0) ± 10 m üstünde kart yok; oyuncunun 6 m yarıçapı içinde kart alfa ≤ 0,10.
- Düşmanların yürüdüğü zemin temasında huzme zemini +0,12 V'den fazla açmaz; ekran-uzayı huzme kare ortalama lumasını +0,08 V'den fazla yükseltemez.
- FP'de nişangâh çevresi 6° konide huzme katkısı ≤ 0,25; hasar vinyeti aktifken ekran huzmesi ×0,5.
- DEC-077 kapıları huzme "açık" fazında da geçer; geçmezse önce alfa düşer, kart sayısı değil.
- Huzme hiçbir zaman bloom eşiğini aşan değer üretmez (kart HDR < 1,0).

---

### 4 · Bulut gölgesi (ana ışık cookie)

| Parametre | Değer |
|---|---|
| Doku | `T_cloud_cookie` 1024² R8 tileable, bulut düzlemi örtü maskesinin bulanık/tersi |
| Projeksiyon | 240 × 240 m dünya (harita 90 m'de desenin %14'ü görünür → tekrar hissi yok) |
| Hız / yön | **1,3 m/s, KD (52°)** — bulut düzlemleriyle aynı yön; hız kasıtlı olarak fiziksel eşleşmeden düşük (40 m yama bir noktayı ≈30 s'de geçer) |
| Kontrast | aydınlık 1,00 / gölge **0,40** / ortalama 0,62; aydınlık pay (≥ 0,8) **%35–45** |
| Özellik ölçeği | en küçük yama **≥ 14 m**, en büyük 60 m; kenar geçişi **5–9 m** (asla keskin) |
| Faz senkronu | huzme kartları ve yer sisi levhaları aynı dokuyu aynı projeksiyonla örnekler |

**Tepeden okunabilirlikle ilişki:** cookie'nin uzamsal frekansı düşman siluet ölçeğinin (≈1 m) çok üstünde; 5 m'de luma adımı ≤ 0,20 V → bulut kenarı bir nesne gölgesiyle karışmaz. Bulut gölgesindeki zemin (≈#4B545C) hedefin kapalı hava okumasına eşittir; güneş yaması +0,8 EV. DEC-077 ölçümleri **4 fazda** (0 / 0,25 / 0,5 / 0,75 kaydırma) koşar; her fazda oyuncu/zombi luma ≥ 0,08, kapsül Michelson ≥ 0,06, altın orb doygunluk ≥ 0,60 / ayrışma ≥ 0,41, zırh siluet ≥ %30. Bulut kenarı figürü ikiye bölerse kabul (doğal), ama figürün gölgedeki yarısı 0,12 V tabanının altına inmez.

---

### 5 · Bölgesel çok hafif sis

**İlke:** "çok hafif" = herhangi bir pikselde yerel sis toplam opaklığı **≤ %12**; savaş bandında (tepeden, oyuncu 6 m) **≤ %3**.

#### 5.1 Yer sisi levhaları (yatay, gürültülü, cookie ile aydınlanan)

| Bölge | Levha | Yükseklik (y) | Kalınlık | Tepe alfa | Gürültü ölçeği / hız |
|---|---|---|---|---|---|
| Güney koi havuzları (20, −30; köprü) | 3 × 24×16 m | 0,4 m | 0–1,2 m | **0,12** | 20 m + 6 m ikincil, 0,4 m/s KD |
| Batı bambu tabanı (−30, 0) | 2 × 20×26 m | 0,6 m | 0–1,8 m | **0,08** | 18 m, 0,35 m/s |
| Saray arkası / kuzey veranda (0, 30 → 42) | 1 × 40×6 m | 0,5 m | 0–1,5 m | **0,06** | 24 m, 0,3 m/s |
| Doğu sakura altı | 1 × 18×14 m | 0,5 m | 0–1,0 m | **0,04** | 20 m, 0,4 m/s |
| Merkez meydan | yok | — | — | — | Yalnız huzme sonrası sürüklenme parçacığı (S-07), ≤ %3 |

Renk: gölgede #D3D9DF, cookie aydınlığında #E9E2D5'e karışır (levha shader'ı cookie örnekler). Kenarlar 3 m yumuşak; levha yerleşimi NavMesh yürüme alanını kapatmaz (görsel, çarpışma yok).

#### 5.2 Yükseklik sisi (yalnız TP/FP; ekran-uzayı, S-05 renderer feature)
Taban y −0,2 m, yoğunluk **0,004/m**, her 5 m'de yarıya iner; toplam katkı tavanı **%12**. 30 m'de ≈%9, 45 m'de ≈%12 (tavan). Renk #D3D9DF; güneş yönüne bakan piksellerde %20 #E9E2D5'e karışım (in-scatter hissi, ucuz).

#### 5.3 Mesafe sisi (lineer, kamera-bağıl)
- **Tepeden:** 52 → 210 m, #D0D7DE — **değişmez** (REQ-104: ≤ 45 m sis yok).
- **TP/FP:** **45 → 235 m** (90 m'de %23,7 = hedef 0,003/m ile aynı; 120 m %39; 235 m %100). Yükseklik sisiyle birlikte 90 m zemin seviyesinde toplam **%26–32**.
- LOD cull ↔ sis sonu notu (kılavuz §9 #E) TP/FP'de gerçek olur: cull 138 m'de nesne %49 sisli → S-05 cull'u 235'e bağlar ya da 138'de fade-out yazar (teknik tercih, AD şartı: pop yok).

#### 5.4 Kamera moduna göre
- **Tepeden:** levhalar ×0,6; yükseklik sisi kapalı; huzme kartları aktif.
- **TP:** tam; yükseklik sisi aktif.
- **FP:** tam; **kameraya 3 m içindeki levha/kart solar** (fp-horizon-north'taki düz levha artefaktı yasak); nişan çizgisi üstünde levha alfa ≤ 0,06.

---

### 6 · LUT v2 reçetesi — "Islak Arduvaz, Açılma" (B5)

Zincir korunur: motor (ışık v3 + §7 post) → referans kareler → Adobe Lightroom sınıfı reçete → aynı reçete `DD_Neutral_32.png` şeridine → `LUT/DD_Look_v2.png` + `DD_Look_v2.recipe.json` (ADR-029-1). Yalnız global ton/renk işlemleri şeride taşınır; **Texture / Clarity / Dehaze şeritte 0** (yerel/uyarlanır işlemler LUT'a taşınmaz — README), niyetleri motorda karşılanır: Clarity → SSAO 1,2→1,5 + gölge gücü; Dehaze → sis rampası/plaka tonu. Sunum stilleri (key art, yükleme plakası) için aynı sliderlar ayrı bir "hero" reçetesinde kullanılır.

#### 6.1 Slider değerleri (B4'ten evrim; parantez B4)

| Grup | Slider | B5 | Gerekçe |
|---|---|---|---|
| Taban preset | Creative – Cool Shadows & Warm Highlights | **aynı** | "soğuk sis, sıcak huzme" tam bu split-tone |
| Beyaz dengesi | Temp | **+4** (−8) | Zeytin/teal kastını kırar; sıcaklık güneşten gelir, LUT nötr-ılık |
| | Tint | **+3** (0) | Yeşil-sarı sıva kastına karşı hafif magenta |
| Ton | Exposure | **+0,20** (0) | Işık v3 ana artışı yapar; LUT küçük kaldırma |
| | Contrast | **+14** (+16) | Güneş kontrast getirdi, S-eğrisi hafifler |
| | Highlights | **−18** (−6) | Güneşte sıva/fildişi/bulut kenarı korunur |
| | Shadows | **+12** (+6) | Koyu küme 0,12–0,29 → okunur; taban korunur |
| | Whites | **+6** | Bulut kenarı ve spekular canlı |
| | Blacks | **−8** | Ahşap/kiremit derinliği, hedefin zengin siyahları |
| Varlık | Texture / Clarity / Dehaze | **0 / 0 / 0** (hero: +6 / +8 / +6) | Şerit kuralı |
| Renk | Vibrance | **+18** (+6) | Canlılık ana kaynağı |
| | Saturation | **+4** (0) | Global küçük |
| HSL Hue | Red / Orange / Yellow / Green / Aqua / Blue / Purple / Magenta | 0 / +2 / **−6** / **−3** / 0 / −2 / 0 / +2 | Sarı → turuncuya (zeytin ölür), yeşil hafif sıcak (yosun), sakura pembede kalır |
| HSL Sat | Red / Orange / Yellow / Green / Aqua / Blue / Purple / Magenta | +6 / **+8** / **−10** / **+14** / +6 / **+8** / +4 / **+10** | Ahşap, yosun, gök aralığı, sakura açılır; sarı bastırılır (B4 +12 → −10: sıva zeytin kayması kapanır; altın orb rezervi ölçülür) |
| HSL Lum | Red / Orange / Yellow / Green / Aqua / Blue / Purple / Magenta | 0 / +2 / **+8** / **−4** / 0 / **−6** / 0 / +4 | Sıva açık, yosun derin, gök aralığı derin |
| Color Grading | Shadows | hue **215°**, sat **10**, lum **−4** | Soğuk gölge |
| | Midtones | hue **42°**, sat **6**, lum 0 | Sıva/toprak ılık nötr |
| | Highlights | hue **38°**, sat **12**, lum **+4** | Huzme/bulut kenarı sıcak |
| | Blending / Balance | 60 / **+10** | Highlights'a yatık |
| Tone Curve (in→out, 0–255) | (0→4), (32→26), (64→58), (128→136), (192→204), (255→250) | | Lift'li toe, korunmuş tepe, hafif S |

Adobe MCP eşlemesi (`image_apply_adjustments`, WP-029 alan adları): Temp → `tempA` +4; Exposure/Contrast/Highlights/Shadows/Vibrance/Saturation birebir; Whites/Blacks → `lights`/`darks` (+6/−8) + ton eğrisi; HSL sat → `channelTarget`/`channelSaturation` sırayla (green, magenta, blue, orange, red, aqua, purple, yellow — bu sıra json'a yazılır); API'de olmayan slider (HSL hue/lum, Color Grading) preset + eğri ile yaklaşılır ve `unsupported` işaretiyle kaydedilir; **ΔE kapısı yargılar** (≤ 6 ort, hero kare ≤ 4).

#### 6.2 Derecelendirilecek referans kareler (`LookReferenceTests` genişletilir, 1920×1080, raw + post)
1. WP-029 sekizlisi: center, north, east, south, west, presentation, crowd40, player-closeup — **center iki cookie fazında** (aydınlık / gölge).
2. Yeni TP: tp-north-palace, tp-west-bamboo, tp-east-sakura.
3. Yeni FP: fp-plaza-palace, fp-south-pond, fp-horizon-north.
Toplam 15 kare. Hero grade önce **gp-centre aydınlık faz** ve **presentation** üstünde kurulur, diğerleri aynı reçeteyle doğrulanır. Şerit tek kez işlenir.

#### 6.3 Hedef v3 görseli (0 kr, Adobe)
DEC-076 overview ve gameplay kareleri aynı B5 reçetesiyle derecelendirilir, gök alanı AP-023 bulut kompoziti ile değiştirilir → `wp-075/target/DD_Target_v3_{overview,gameplay}.png`. Yan yana kabul (§9) hem orijinal DEC-076'ya (yoğunluk/malzeme/sis) hem v3 hedefine (gök/ışık/renk) bakar.

---

### 7 · Sinematik post

| Öğe | v2 | v3 | Kural |
|---|---|---|---|
| Tonemap | Neutral | **Neutral** | ACES yok (REQ-105) |
| Bloom | eşik 1,25 / yoğunluk 0,5 / scatter 0,62 / 4 iter | **eşik 1,10 / yoğunluk 0,55 / scatter 0,68 / ton #FFF1DC / 4 iter, HQ kapalı** | Yalnız HDR > 1,1: altın, beacon, güneş speküları (su/arduvaz), bulut kenarı yansıması; dünya albedosu bloom etmez |
| Film grain | yok | **Thin1, yoğunluk 0,18 (Sinematik) / 0,10 (Yüksek) / 0, response 0,85** | HUD'a asla (UI post sonrası katman, S-09); tepeden ince çizgi okunabilirliği: derz/ince hatlarda ölçülebilir kayıp yok |
| Vinyet | 0,20 / 0,45 | **0,24 / 0,40 (tepeden), 0,22 / 0,42 (TP/FP)**, rounded kapalı | Hasar vinyeti (S-07) ile toplam ≤ 0,55 |
| Kromatik aberasyon | yok | **Hayır** (oyun içi) | Yalnız S-07 darbe/patlama anı ≤ 0,12, ≤ 120 ms; tepeden asla |
| Alan derinliği | yok | Tepeden **kapalı**; TP Sinematik: Gaussian uzak 70→220 m, yarıçap 1,2 px; FP Sinematik: Bokeh f/8, 28 mm, odak = nişan ışını (S-01 API) 0,25 s yumuşatma; Yüksek/Performans kapalı | Nişangâh düzlemi asla bulanık değil; pause arka planı S-09'un ayrı bulanıklığı |
| Hareket bulanıklığı | yok | Tepeden **kapalı**; TP/FP Sinematik yalnız kamera, yoğunluk 0,12, clamp 0,03; Yüksek kapalı | Nesne hareket bulanıklığı yok |
| SSAO | 1,2 / 0,30 m | **1,5 / 0,35 m (Sinematik), 1,2 / 0,30 (Yüksek), kapalı (Performans)** | Clarity niyeti burada |
| Yansıma probları | 6 baked, 128 | **10 baked** (5 bölge + havuz + batı koridor iç + saray veranda + pavyon + köprü), 256 (Sinematik) / 128 | Gök v2 ile yeniden bake |
| Planar yansıma | yok | **Koi havuzları (3) tek planar kamera**: Sinematik 512 px + karakterler; Yüksek 256 px, karakter yok; Performans kapalı (prob) | Yalnız su; arduvaz birikintileri prob + smoothness maskesi (AP-018 v2: birikintide 0,72, diğer 0,55) |
| Gölge | 0,5, 60 m/4 kaskat | **0,75, yumuşak, 60 m (tepeden) / 80 m (TP/FP Sinematik)** | |
| LUT | DD_Look_v1 (B4) | **DD_Look_v2 (B5)**, katkı 1 | |
| Parçacık (S-07 sözleşmesi) | — | Sürüklenen tane/petal parlaklığı × cookie örneği; huzme içinde 1,6×, gölgede 0,7× | Huzmede "toz ışığı" okuması |

---

### 8 · Kalite kademeleri v2 — görsel niyet (ms sözleşmesi S-05)

| Özellik | Sinematik | Yüksek | Performans |
|---|---|---|---|
| Gök bandı v2 + 2 bulut düzlemi | tam, parallaks | tam | bant + **tek** bulut düzlemi (uzak), yavaş |
| Ufuk plakaları v2 | 3 | 3 | 3 |
| Bulut gölgesi cookie | tam | tam | **tam** (tek örnek, ucuz) |
| Huzme kartları | 15 | 10 (bambu 4, sakura 3, koridor 2, saray 1) | **6** (bambu 3, sakura 2, koridor 1), alfa ×0,7 |
| Ekran-uzayı huzme (TP/FP) | 32 örnek, 0,35 | 20 örnek, 0,22 | kapalı |
| Yer sisi levhaları | 7 | 7, alfa ×0,8 | 4 (havuz 2, bambu 2), alfa ×0,6 |
| Yükseklik sisi (TP/FP) | açık | açık | kapalı |
| Mesafe sisi | açık | açık | açık |
| LUT v2 | açık | açık | **açık** |
| Bloom | 4 iter | 4 iter | 3 iter, scatter 0,6 |
| Film grain | 0,18 | 0,10 | kapalı |
| Vinyet | açık | açık | açık |
| DoF / motion blur | §7 | kapalı | kapalı |
| SSAO | 1,5 / 0,35 | 1,2 / 0,30 | kapalı |
| Yansıma probları | 10 × 256 | 10 × 128 | 6 × 128 |
| Planar (havuz) | 512 + karakter | 256 | kapalı |
| Gölge | yumuşak, 4 kaskat, 80 m TP/FP | yumuşak, 4 kaskat, 60 m | sert, 2 kaskat, 45 m, güç 0,65 |
| Parçacık × cookie (S-07) | tam yoğunluk | ×0,7 | ×0,4 |

**Hiçbir kademede kaybedilmez (görünümün omurgası):**
1. Islak arduvaz değeri ve parlaması (malzeme; maliyetsiz).
2. Bulut gölgesi cookie — parçalı bulutun tepeden okunmasının tek yolu.
3. LUT v2 + sis rengi #D0D7DE + mesafe rampası (90 m %24) — hedefin pusu.
4. Ufuk plakaları + gök bandı (siluetler, ufuk dikişi yok).
5. İmza huzmeler: batı bambu ≥ 3 kart, doğu sakura ≥ 2 kart.
6. Değer sırası (sıva > sakura > taş > yeşil > ahşap) ve DEC-077 eşikleri.

---

### 9 · AD kabul ölçütleri — yan yana (hedef ↔ motor)

#### 9.1 Kadraj çiftleri (1920×1080, post + LUT açık, cookie fazı belirtilir)

| # | Motor karesi | Hedef | Mod |
|---|---|---|---|
| Y1 | ov-south-high | DEC-076 overview + DD_Target_v3_overview | genel |
| Y2 | gp-centre, aydınlık faz | DEC-076 gameplay + DD_Target_v3_gameplay | tepeden |
| Y3 | gp-centre, gölge faz | DEC-076 gameplay (orijinal) | tepeden |
| Y4 | tp-north-palace, tp-west-bamboo | DD_Target_v3_overview (kırpım) | TP |
| Y5 | fp-plaza-palace, fp-south-pond, fp-horizon-north | DD_Target_v3_overview (kırpım) | FP |
| Y6 | presentation (WP-029 kadrajı) | DEC-076 overview | sunum |

#### 9.2 Ölçülebilir ölçütler

| # | Ölçüt | Yöntem | Eşik |
|---|---|---|---|
| K1 | Malzeme yaması ΔE (CIE76) hedefe: arduvaz, sıva, yosun, sakura, ahşap, su, gök | 6 yama × Y1/Y2, 64×64 px ortalama | her yama **≤ 12**, ortalama **≤ 9** |
| K2 | Yama HSV aralıkları (§1.4) | aynı yamalar, iki cookie fazı | %100 aralık içinde |
| K3 | Pozlama (§1.3) | kare ortalama luma, kırpma yüzdeleri | tablo değerleri |
| K4 | Değer sırası | sıva > sakura > taş > yeşil > ahşap (V) | her karede korunur; bulut kenarı hariç sıvadan açık opak yüzey yok |
| K5 | Sis rampası | 90 m'de sis payı (tepeden / TP zemin) | **%20–28 / %26–32** |
| K6 | Ufuk dikişi | plaka-bant geçişinde luma gradyanı | ≤ 0,04 V / 10 px; sert bant yok |
| K7 | Bulut gölgesi | aydınlık pay, min yama, kenar geçişi (cookie analizi) | %35–45, ≥ 14 m, 5–9 m |
| K8 | Huzme görünürlüğü | TP tur (5 bölge): ≥ 2 bölgede kartlı huzme; tepeden 30 s pencerede ≥ 1 huzme (bambu/sakura kenarı), meydan merkezinde 0 | geçer |
| K9 | Huzme okunabilirlik tavanı | zemin temasında +V, kare ortalama +V | ≤ 0,12 / ≤ 0,08 |
| K10 | Yerel sis tavanı | herhangi piksel yerel sis toplamı; oyuncu 6 m (tepeden) | ≤ %12 / ≤ %3 |
| K11 | FP artefakt | kameraya < 3 m levha/kart | 0 |
| K12 | DEC-077 kapıları, 4 cookie fazı × huzme açık | oyuncu/zombi luma ≥ 0,08; kapsül Michelson ≥ 0,06; altın orb sat ≥ 0,60 / ayrışma ≥ 0,41; zırh siluet ≥ %30; koyu küme tabanı ≥ 0,12 | 4/4 faz |
| K13 | LUT sadakati | Lightroom ↔ Unity ΔE, 15 kare | ort ≤ 6; gp-centre aydınlık ve presentation ≤ 4 |
| K14 | Yansıma | tepeden arduvaz yansıttığı KD gök çeyreği V; havuzda bulut kenarı görünür | ≥ 0,70; evet |
| K15 | Kademeler arası kimlik | Performans karesi ↔ Sinematik karesi yama ΔE (K1 yamaları) | ≤ 6 (omurga korunur) |
| K16 | Mod tutarlılığı | aynı konumda tepeden/TP/FP kare üçlüsü: sis rengi, gök tonu, güneş yönü tutarlı | görsel yargı + K1 yamaları ≤ 6 arası |

#### 9.3 Yargı rubriği
Hedef sadakati (yoğunluk, malzeme, sis) %35 · okunabilirlik (K9–K12) %30 · sinematik işaretler (huzme, bulut hareketi, yansıma, grain/vinyet ölçüsü) %20 · modlar arası tutarlılık %15. **KABUL ≥ %85 ve MUST 0.** MUST: K4, K12, K10, K11 ihlali. SHOULD: K1 tek yama 12–16, K8 tek bölge eksik.

#### 9.4 Kanıt
`wp-075/evidence/side-by-side/Y1..Y6-{engine,target}.png` + 4-up kontak · `wp-075/evidence/metrics.json` (K1–K16) · `LUT/DD_Look_v2.recipe.json` · `WorldAtmosphere.cs` sabit farkı · kademe başına aynı 6 kadraj (`tier-{cinematic,high,performance}/`).

---

### Ek · Bağımlılık ve devirler
- **P-05 AP-023:** §2.3 varlıkları; AP-018 v2 birikinti smoothness maskesi (0,72 birikinti / 0,55 gövde).
- **S-05:** §1.1 sabitler, cookie, huzme kart shader'ı (cookie örnekleme), yer sisi levha shader'ı, yükseklik sisi + ekran huzme renderer feature'ları, planar yansıma, prob bake, kademe sözleşmesi (ms), `LookReferenceTests` 15 kare, K1–K16 ölçüm aracı.
- **S-07 (WP-073):** parçacık × cookie parlaklığı, kromatik aberasyon darbe kuralı, hasar vinyeti tavanı.
- **S-01 (WP-070):** FP DoF odak noktası için nişan ışını; kamera modu olayında sis/post profil geçişi (tepeden ↔ TP/FP değerleri §5.3–5.4, §7).
- **S-09 (WP-068 R1):** grain/vinyet UI'ya sızmaz; LUT v2 sonrası HUD kontrast yeniden ölçümü.
- **C-00 kararı gereken:** Sky sınıfı doku cap 1024 → 2048 (ADR-015-1 v6) — reddedilirse bant 2048×512 kalır, bulut düzlemleri niyeti taşır.

