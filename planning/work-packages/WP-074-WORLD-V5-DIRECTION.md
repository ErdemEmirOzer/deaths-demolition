# WP-074 — Dünya v5: yön belgesi (Faz A)

Sahip: S-05 · Koordinasyon: C-00 · Tarih: 2026-09-24 · Dal `wp-074-world-v5` · Tasarım ekibi: art-director (sanat yönü, boşluk analizi), game-designer (seviye tasarımı) · Paydaşlar: S-03 WP-076 (spawn/NavMesh), S-01 WP-070 (kamera modları; `f23f38e7` §8/§9/§11), S-08 WP-072 (akustik alan), P-05 AP-023 (varlıklar).
Çerçeve kararları: DEC-092 (60 fps — Yüksek ort ≤ 16,6 ms, p99 ≤ 20 ms, 1080p; + Performans), DEC-093 (build ≤ 5 GB; 5 GB'ye kadar doku/mesh kalitesi boyut için kısılmaz). Kullanıcı maddeleri: N3, N4, N13 (M4-R1 §0).
Kanıt: güncel dünya 19 kare `~/Desktop/KADRO/context/projects/deaths-demolition/wp-074/captures-current/world-v3/` (ov genel / gp tepeden / tp 2,2 m / fp 1,6 m; araç `WorldV3CaptureTests` + `DD_ART_CAPTURE_POSES`, commit `91b52cd`); hedef `wp-015` DEC-076 stil referansı kareleri (genel bakış + oynanış, depo dışı).

Bu belge = §0 S-05 kararları (konsey çelişkilerini bağlayıcı biçimde çözer) + §1–§5 S-05 eklemeleri + Ek A (AD konsey metni, birebir) + Ek B (seviye tasarımı konsey metni, birebir). Çelişkide §0 geçerlidir.

## 0 · S-05 kararları (bağlayıcı)

| # | Konu | AD önerisi | Seviye tasarımı önerisi | **Karar** | Gerekçe |
|---|---|---|---|---|---|
| K1 | Büyütme | oynanır ±52 + vista 52–85 | ±55 (yedek ±50), ±60 ret | **Oynanır ±55 m (110×110), vista kuşağı 55–85 m yürünmez gerçek geometri (duvar, çatı, ağaç, pagoda); yedek ±50 (Ek B §1.3 tetikleyicileri)** | Plan v6 yapı ayak izi ≈ 3 400 m² merkez arenaya dokunmadan ±55'te sığar; Standard ilk temas +3,6 s (≈ %3 koşu süresi); AD'nin vista kuşağı ufuk bandını (B1) kapatır. Halka ±55, NavMesh ±54,5 (WP-038R kuralı). |
| K2 | Saray salonu | 27×15, sırt 11,5, 1,2 m kaide + 6 basamak, irimoya 2 kademe, orta 12×8 girilebilir | 36×12 (x −18…18, z 38…50), 0,35 zemin, 6 giriş, tamamı girilebilir, iç nef halkaları | **Ayak izi Ek B (36×12 → 36×13, 6 giriş, 3 nef, dais) + görünüm Ek A (kaide 1,2 m, irimoya 2 kademe, sırt 11,5 m, engawa + kōran, kōshi bandı, orta 3 bölme açık)**. Kaideye 5 merdiven: ön x −10 / 0 / +10 (3 / 6 / 3 m), D ve B 2,4 m, K 3 m; basamak 6 × 0,20 m (NavMesh tırmanma 0,30 altında, rampa olarak pişer). | Savaş akışı (≥ 2 çıkış, iç halka) seviye tasarımından; siluet/ölçek hedef görselden. |
| K3 | Kapalı koridor | kairō iç 3,0 m, ahşap döşeme +0,30, direk 2,4 m, ≥ 96/150 m, 1–2 tam kapalı | K1…K6 ≈ 175 m, iç 4,0–4,5 m, K1 kapalı | **Hat ve genişlik Ek B (K1…K6, iç net 4,0–4,5 m, ≈ 175 m, K1 tam kapalı 36 m) + yapı Ek A (kiremit çatı 28°, kōran korkuluk, renji-mado kafesli sıva duvar, asılı fener 4,8 m aralık)**. Direkler yalnız dış çizgilerde (net genişliğe girmez), aralık 2,4 m. Döşeme **+0,25 m** (0,30 tırmanma sınırında voksel yuvarlama riski — WP-038N dersi). | 40 düşman akışı 3 m'de tıkanır; +0,30 döşeme tam sınırda. |
| K4 | Girilebilir mekân | salon orta 12×8, çay evi 3,6×3,6, kura 6×4, azumaya 4×4, tsuridono 5×4 | R1 salon, R2 köşk 10×8, R3 ambar 10×8, R4 çay evi 8×8, R5 azumaya, R6 kapı evi, R7 sakura avlusu | **Ek B R1–R7 ölçüleri** (her oda ≥ 2 çıkış ≥ 6 m ayrık); AD'nin tsuridono su köşkü dekoratif P2 (savaş dışı, havuz kuzey kıyısı). | 3,6×3,6 çay evinde 2 ayrık çıkış kurulamaz; oda-çıkış grafı bot/AI için şart. |
| K5 | Kapılar | karamon (0,−16) meydan güneyi, 2 yakuimon, 2 duvar kapısı, 1 torii (doğu hokora) | R6 chūmon kapı evi (0,18…23) | **İkisi de**: güney eksende karamon (0,−16, açıklık 6 m) → meydan → chūmon (0,18…23) → ön avlu → salon; yakuimon avlu D/B kapıları (26,30)/(−26,30); torii doğu hokora önü (tek vermilyon vurgu). | Eksen hedef görselin kompozisyon çizgisi; 6 m açıklık darboğaz değil. |
| K6 | Yoğunluk | ≥ 8 000 (±45) / ≥ 13 000 (büyük, vista dahil); okunur ≥ 35/100 m² | ≤ 4 900 örnek (8,0 ms varsayımıyla) | **Okunur örnek ≥ 35/100 m² (±55 oynanır ≈ 4 200), yapı modülü ≥ 7,5/100 m², toplam render örneği ≤ 12 000 (Yüksek) — zemin örtüsü/yosun/çalı/bambu GPU instancing, uzak ağaç impostor; Performans kademesinde bitki yoğunluğu ×0,6.** | DEC-092 (16,6 ms) seviye tasarımının 8,0 ms tavanını kaldırdı; görünür renderer tavanı (TP ≤ 900) ölçümle korunur. |
| K7 | Güneş/ışık | AD-dünya: azimut 215°, 38° | — | **WP-075 değerleri bağlayıcı** (Euler 33/52 — 33° yükseklik, pusula 232°, 4900 K; bulut gölgesi baskın) | Işık/atmosfer sahibi WP-075; dünya materyal kuralları (Ek A §5) aynen. |
| K8 | Göz hizası | Ek A §3 | Ek B §5 | **S-01 E-1…E-20 (WP-070 §9) bağlayıcı liste**; Ek A §3 ölçütleri bu maddelerin eşikleridir (§2 eşleme). | Tek liste, iki kaynak. |
| K9 | Doku bütçesi | Sky cap 1024 → 2048 isteniyor | — | **ADR-015-1 v3 (§4)**: Sky 4096, Ground/Hero 4096, Structure/Foliage 2048 … | DEC-093. |
| K10 | Spawn | — | 18 kapı (12 kenar + 6 orta), bant 22–45 m, MaxUseful 75 | **Öneri olarak S-03'e (WP-076)**; eski 12 ad korunur; S-03 kararı bağlayıcı. | Spawn sahibi S-03. |

## 1 · Faz B dilim planı (her dilim ayrı commit, dal `wp-074-world-v5`)

| Dilim | İçerik | Tüketici / kapı |
|---|---|---|
| B1 Greybox v6 | Ek B §2 yerleşimi collider + basit mesh ile (±55, vista kuşağı yer tutucu); halka ±55 + NavMesh v5 (tırmanma 0,30, halka 0,5 m içeride, FallGuard, adacık bağlayıcı gövde yürüyüşü — WP-038M/N/P/Q/R kuralları aynen); `spawn-gates-v6.json` (18 kapı öneri, eski 12 ad korunur), `regions-v6` poligonları, `acoustic-spaces-v6.json` (§3) | S-03 spawn simülasyonu, S-10 bot soak (B9), S-01 TP çarpışma turu, S-08 AcousticSpace |
| B2 Doku/varlık sözleşmesi | ADR-015-1 v3 (§4): `ArtImportContract` sınıf tavanları + Hero sınıfı + sevk bütçe sabitleri + kademe başına akış bütçesi (WP-075 kademe dilimiyle aynı anda) | P-05 AP-023 teslim biçimi |
| B3 Occluder sınıfları | CameraOccluder nesnelerine Roof / Wall etiketi (E-20); Ek B §5 occluder grupları | S-01 `CameraOcclusionFader` (TP'de yalnız Wall) |
| B4 Yerleşim v6 dressing | mevcut 70 aile + ara aileler ile yoğunluk (K6), eksen/ritim kuralları (Ek A §2), yapı modülleri geldikçe değişim; AP-005 delta v007 | AD kabul A2/A3 |
| B5 AP-023 entegrasyonu | P0 sırasıyla: saray kiti + iç, kairō, tsuiji, karamon/yakuimon, tōrō, ağaçlar (sakura/çam/bambu), havuz kıyı + taiko köprü, bordür; sonra P1 | Ek A §6 |
| B6 Göz hizası temizliği | otomatik tarama (1,6 / 2,2 m ızgara × 8 yön: ufuk altı gök pikseli, siyah düzlem, arka yüz) + düzeltmeler (E-1, E-3, E-6, E-7, E-11) | S-01 §9 kanıt kareleri |
| B7 LOD / impostor / culling | yoğun bitki GPU instancing, uzak ağaç oktahedral impostor, vista impostor, occlusion culling bake (E-14), katman cull mesafeleri (küçük prop 45 m, bitki 60 m — S-01 §8) | B11/B12 perf |
| B8 Işık v5 | lightmap + probe yeniden bake (iç mekân/koridor probe sıklaştırma, ≥ 10 yansıma probu — WP-075 §7), UV2 dikiş kuralları (E-9) | E-9, E-13 |
| B9 Perf raporu | mod × kademe matrisi (9 hücre) ölçümü — `tools/qa/wp064_run.py perf` + `-ddCameraMode`; A15/B10/B11 | DEC-092 |

## 2 · Göz hizası gereksinimleri (S-01 WP-070 §9, bağlayıcı) → dilim ve eşik

| E | Madde (özet) | Dilim | Eşik / kanıt |
|---|---|---|---|
| E-1 | Arka yüz kapalı / çift taraflı | B5, B6 | Ek A §6 A9 = 0; tarama |
| E-2 | İç mekân kapalı ya da asgari döşeli | B5 | R1–R6 döşeli (Ek A §4 iç kit); cephe-only yapılarda ≥ 2,5 m derinlik illüzyonu |
| E-3 | Ufuk 360° boşluksuz, uzak plan 450 m | B4 (vista), WP-075 | Ek A A7 (ΔL ≤ %3, sarı bant ≤ %0,5); bugün krem sis bandı (fp-horizon-east) |
| E-4 | Zemin texel 0,5 m'de bulanmaz | B2, B5 | ≥ 1024 px/m LOD0 (Ek A A13); ADR-015-1 v3 Ground 4096 |
| E-5 | Collider = görsel; halka görünür engel | B1, B4 | halka ±55 = tsuiji duvarı + vista (görünmez duvar yok) |
| E-6 | Çatı altı/saçak modelli | B5 | mertek + kaplama; A8 siyah düzlem ≤ %2 |
| E-7 | Prop arka yüzleri kapalı | B5, B6 | tarama |
| E-8 | LOD geçişleri göz hizasında (LOD1 ≥ 25 m, LOD2 ≥ 50 m) | B7 | pop ≤ 2 px |
| E-9 | Işık sızması yok | B8 | lightmap dikiş kontrolü |
| E-10 | Z-fighting yok | B4, B6 | tarama |
| E-11 | Havada/gömülü prop yok | B6 | zemin raycast denetimi ≤ 2 cm |
| E-12 | Sis yatay bakışa kalibre, plaka ile uyumlu | WP-075 | WP-075 K5/K6 |
| E-13 | ≥ 4 bölge yansıma probu | B8 / WP-075 | 10 prob |
| E-14 | Occlusion culling bake + mod başına draw call | B7, B9 | rapor |
| E-15 | Siper yükseklikleri (0,9–1,1 / ≥ 1,8 m) | B4 | Ek B §3.7 siper = h ≥ 1,1 m → alçak siper 0,9–1,1 ayrıca etiketlenir |
| E-16 | VFX yönelimi (S-07) | — | WP-073 |
| E-17 | Merdiven/rampa: collider basamak ≤ 0,3 m | B1, B5 | saray merdiveni 0,20; kairō döşemesi 0,25 |
| E-18 | Bitki 0,5 m yakın alfa kesimi | B5 | AP-015P doğrulaması |
| E-19 | Dünya-uzay HUD (S-09) | — | WP-068 R1 |
| E-20 | Occluder Roof / Wall etiketi | B3 | S-01'e liste |

## 3 · Akustik alanlar (S-08, WP-072)

Kaynak tablo Ek B §4 (23 alan). S-08 biçimine eşleme: açık avlu → `Enclosure.Open`, koridor → `Enclosure.Corridor`, kapalı salon → `Enclosure.Hall`; tablo dışı her yer Open. Kapı/eşik konumları = Ek B §2 giriş listeleri (R1 6 giriş, R2 3, R3 2, R4 2, R6 2 + 2 yan, K1 5 ağız …). Veri: B1 diliminde `acoustic-spaces-v6.json` (poligon x/z, tavan m, taban m², eşik noktaları) — S-08 AcousticSpace kaydı bunu okur; yerleşim B4/B5'te kayarsa json aynı dilimde güncellenir.

## 4 · ADR-015-1 v3 — doku ve varlık bütçesi (DEC-093, 5 GB) · öneri

Bağlam: DEC-093 release tavanını 550 MB → **5 GB** yaptı; 5 GB'ye kadar doku/mesh kalitesi boyut için kısılmaz. v2 (dünya ≤ 120 MB, karakter ≤ 80 MB, toplam ≤ 200 MB; sınıf tavanları Ground 2048 / Structure 1024 / Foliage 1024 / Prop 512 / Breakable 512 / Sky 1024 / Character 2048 / Vfx 1024 / Weapon 1024; akış bütçesi 160 MB tek değer) göz hizası (TP/FP) ve hedef görsel yoğunluğu için dar. Asıl sınır artık build değil **çalışma belleği** (yerleşik doku) — bu yüzden bütçe iki eksenli: sevk (disk) ve yerleşik (kademe başına).

**1 · Sınıf tavanları (import azami boyutu)**
| Sınıf | v2 | v3 | Not |
|---|---|---|---|
| Ground (zemin/döşeme/su) | 2048 | **4096** | FP ayak altı arduvaz, gameplay hedef karesi (derz, ıslaklık) |
| Hero (yeni: saray cephesi, ana kapı, köprü, fener seti) | — | **4096** | kahraman parçalar, sayıca ≤ 24 doku seti |
| Structure | 1024 | **2048** | duvar, kairō, çatı kiremidi, ahşap |
| Foliage | 1024 | **2048** | çam/akçaağaç/sakura/bambu atlası; impostor atlası 2048 |
| Prop | 512 | **1024** | (hero prop → Hero) |
| Breakable | 512 | **1024** | kırılma parçaları aynı atlas |
| Sky | 1024 | **4096** | gök kubbesi lat-long 4096×2048, ufuk plakaları 4096×1024 |
| Character | 2048 | 2048 | FP kolu/eller 4096'ya Weapon sınıfıyla |
| Weapon | 1024 | **2048** (FP görünüm modeli 4096) | AP-021 FP kalitesi |
| Vfx | 1024 | **2048** | flipbook / huzme / sis gürültüsü |

**2 · Sevk bütçesi (release build, LZ4HC, Editor raporu)** — toplam ≤ **4,6 GB**, 0,4 GB pay: dünya dokusu ≤ 1,6 GB · karakter ≤ 0,4 GB · silah ≤ 0,4 GB · VFX ≤ 0,2 GB · UI ≤ 0,2 GB (S-09) · mesh ≤ 1,0 GB · ses ≤ 0,5 GB (S-08) · kod/shader/sahne ≤ 0,3 GB. Kapı `ArtImportContract.Shipped*BudgetMb` sabitleri + `tools/build-report.sh --cap 5000`; kategori satırları raporlanır, toplam kapı.

**3 · Yerleşik bellek (kademe başına, `QualitySettings.streamingMipmapsMemoryBudget`)** — Performans **512 MB** · Yüksek **1024 MB** · Sinematik **2048 MB** (v2: her seviyede 160). Mip akışı Sky / Vfx / UI dışında açık. Performans kademesinde `globalTextureMipmapLimit` 1 (yarım çözünürlük) izinli.

**4 · Sıkıştırma** — albedo/paketli BC7 (yüksek kalite), normal BC5, tek kanal maske BC4, crunch kapalı; NPOT yalnız UI (mip'siz, WP-068 dersi); her sınıfta mip zinciri (UI hariç).

**5 · Uygulama** — WP-074 Faz B ilk teknik dilimi: `ArtImportContract.MaxSizeOf` + Hero sınıfı + bütçe sabitleri + kalite seviyelerine akış bütçesi (kademe v2 ile aynı dilimde); mevcut 95+ doku yeniden import edilir (kaynak PNG'ler değişmez). P-05 AP-023 yeni varlıkları v3 tavanlarında teslim eder.

## 5 · Perf tabanı ve R1 bütçe paylaşımı (öneri — C-00 hakemliğinde)

Ölçülmüş taban (WP-064 r3 kapanışı, `wp-064r3/closure-96e3c17`, tepeden, 40 düşman, 1080p, referans Mac): ort 7,82–7,90 ms, temiz p99 8,62–8,69, GPU 5,5–5,7 ms, draw ≈ 1 480, SetPass ≈ 142 — kare **CPU (ana iş parçacığı) bağlı**. DEC-092 Yüksek 16,6 ms → R1 için ≈ 8,7 ms pay. Öneri (tepeden, Yüksek): dünya v5 (yoğunluk, yapılar, vista) ≤ +3,5 ms · atmosfer v3 (WP-075) ≤ +2,0 ms · VFX/animasyon/AI/UI/ses (WP-069…073, 076, 068) ≤ +3,0 ms · 0,2 ms pay. TP/FP: dünya ek ≤ +1,5 ms (occlusion culling + LOD ile; Ek B §1.2 tahmini). Her dilim kendi Δms'sini WP-075 kademe sözleşmesi §5 kuralıyla raporlar; aşan dilim kendi kademesinde kısar.

## 6 · Devirler ve tarihler

- **P-05 AP-023:** eksik varlık listesi = Ek A §4 (P0/P1/P2) ∪ Ek B §2.7 (≈ 16 yeni aile); ölçüler §0 kararlarına göre (salon 36×13 + 1,2 m kaide, kairō net 4,0–4,5 m, odalar Ek B R1–R7); doku tavanları ADR-015-1 v3; dış kaynak ≤ 70 kr (Ek A §4 dağılımı; gök v2 plakaları WP-075 §2.3 ile ortak). İlk teslim sırası: saray kiti → kairō → tsuiji/kapılar → ağaç atlasları → havuz kıyı + köprü.
- **S-03 WP-076:** Ek B §3.4 kapı tablosu, §3.5 halkalar, §2 oda-çıkış grafı, §5 ihtiyaç listesi; B1 greybox + `spawn-gates-v6.json` + `regions-v6` (hedef 25.09–27.09).
- **S-01 WP-070:** §2 eşleme; B3 occluder Roof/Wall listesi; greybox'ta TP çarpışma turu (K1, R1, R6, köprü); kalite modülü ↔ `CameraModeService.CurrentProfile` tek-yazar kuralı (WP-075 kademe sözleşmesi).
- **S-08 WP-072:** §3 json yolu B1 teslimiyle.

## 7 · Kabul

Ek A §6 (A1–A16; A15 DEC-092 ile: Performans ≤ 8,0 / Yüksek ≤ 16,6 ms, p99 ≤ 20) + Ek B §6 (B1–B17; B10 DEC-092 ile, B12 örnek tavanı §0 K6 ile ≤ 12 000) + §2 E-listesi kanıt kareleri (FP, `-ddCameraMode fp`) + hedef görselle yan yana (genel + oynanış kadrajı, üç kamera modu) → AD turu.


## Ek A — Sanat yönü metni (art-director, birebir; başlıklar iki düzey alta, mutlak yollar ~/)

### WP-074 Dünya v5 — Sanat Yönü Belgesi (AD, Faz A)

Kaynak: hedef `wp-015` DEC-076 stil referansı kareleri (genel bakış + oynanış, depo dışı); güncel kareler `wp-074/captures-current/world-v3/` (ov-south-high, ov-southeast, ov-west, gp-centre, gp-north-plaza, gp-west, gp-south, tp-north-palace, tp-west-bamboo, tp-south-bridge, tp-east-sakura, fp-plaza-palace, fp-south-pond, fp-horizon-north, fp-horizon-east); `placement-families.txt` (3168 örnek / 70 aile); `worldlayout-excerpt.txt`; M4-R1 brief §0/§7. Tüm ölçüler metre, tüm renkler sRGB hex, "okunur örnek" = herhangi bir eksende ≥ 0,5 m olan ve {creeping_groundcover, greenery_cluster, moss_patch, leaf_pile} dışındaki aile örneği.

#### 0 · Altı ana bulgu (kareye bağlı)

| # | Bulgu | Kanıt karesi |
|---|---|---|
| B1 | Harita bir "ışık kutusu": dört yanı düz, tepesiz, kaidesiz krem sıva düzlemi; tepesinde sarı-krem ufuk bandı, üstünde alt kenarı sert kesilmiş ağaç plakası. Hedefte kenar yok — çatı sırtları, ağaç kümeleri ve sisle eriyen tepeler var. | ov-south-high, fp-horizon-north (kare %100 düz sıva), fp-south-pond, fp-horizon-east |
| B2 | Saray tek katlı, düz cepheli, 12 özdeş shoji bölmeli uzun kutu: kaide yok, merdiven yok, korkuluk yok, açık orta bölme yok, iç mekân yok, kanat yok, kırma-beşik (irimoya) çatı yok. Hedefte 2 kademeli çatı, taş kaide, 6 basamak, engawa + korkuluk, karanlık iç boşluk, sol/sağ kanatlar. | tp-north-palace, fp-plaza-palace, ov-west |
| B3 | Merkez meydan boş: gp kadrajında oyuncu dışında 0 okunur örnek; hedef gameplay karesinde aynı alanda duvar + kafes pencere + granit bordür + çiçek yatağı + fener + yaprak kümeleri var. | gp-centre, gp-north-plaza |
| B4 | Kapalı koridor yok: batı "koridor" açık dikme + düz çatı; direkler göz hizasında ~1 m kalınlığında, çatı altı siyah düzlem; kapı çatısı FP'de siyah pano olarak kameranın önüne düşüyor. | tp-west-bamboo, gp-west, fp-plaza-palace |
| B5 | Bitki örtüsü şematik: sakura kanopisi mor-gri bloklar (#6E5A6E ≈ hedef #F0C4CF değil), bambu seyrek ince çubuk, kanopi yok; çam/momiji/geniş yapraklı hiç yok; kayalar geodezik "mücevher" biçimli. | tp-east-sakura, gp-west, tp-south-bridge, gp-south |
| B6 | Koi havuzu 3 ayrı yuvarlak leğen, özdeş taş blok halkası, düz kalas köprü; hedefte tek gövdeli havuz, kaya-çakıl-yosun kıyı geçişi, kavisli taiko köprü, kamış, nilüfer, koi. | gp-south, tp-south-bridge, fp-south-pond |

Genel yargı: yerleşim (S-05) hedefe kompozisyon olarak yakın (kuzey saray, batı bambu, doğu sakura, güney havuz+köprü ekseni doğru), ama **yapı sınıfı varlıklar** (P-05) ve **dikey/derinlik katmanı** (S-05 yerleşim + AP-023) eksik. Renk/pus/ıslaklık farkı WP-075'e; bu belge dünyaya ait olanı kapsar.

#### 1 · Boşluk analizi (hedef ↔ güncel)

Yoğunluk tabanı: güncel 3168 / 8100 m² = 39/100 m² toplam; okunur ≈ 1460 → 18/100 m²; yapı modülü ≈ 230 → 2,8/100 m² (bunun 74'ü çevre duvar düzlemi).

| Eksen | Güncel | Hedef (±45 m, 8100 m²) | Hedef (büyütülmüş: oynanır ±52 m = 10 816 m² + vista kuşağı 52–85 m, yürünmez) | Boşluk | Aksiyon |
|---|---|---|---|---|---|
| Yoğunluk — toplam örnek | 3168 (39/100 m²) | ≥ 8000 (≈100/100 m²) | ≥ 13 000 (oynanır ≥ 10 000 + vista ≥ 3000) | ×2,5–4 | S-05 yerleşim v5 (AP-005 delta v007) |
| Yoğunluk — okunur örnek | ≈1460 (18/100 m²) | ≥ 2800 (35/100 m²) | ≥ 4200 | ×2 | S-05 + P-05 yeni aileler |
| Yoğunluk — yapı modülü | ≈230 (2,8/100 m²) | ≥ 600 (7,5/100 m²) | ≥ 900 | ×2,6 | P-05 modüler kit (§4) |
| Yoğunluk — gp kadrajı (≈28×16 m zemin, 450 m²) | gp-centre: 0 okunur, 0 dikey, 0 gölge düşüren kanopi/saçak | 6 m ızgarada örneklenen yürünür noktaların ≥ %90'ında: ≥ 20 okunur örnek + ≥ 1 dikey öge (duvar/direk/gövde) + ≥ 1 gölge düşüren kanopi/saçak kenarı | aynı | mutlak | S-05 (ölçüm betiği: kamera örnekleyici + frustum sayımı) |
| Katmanlar — ön (0–8 m kamera) | yaprak tek tek, dairesel "hero_earth" lekeleri | zemin dekalleri: su birikintisi (%8–12 alan), yaprak kümeleri (12–25 yaprak/küme, kenarlara sürüklenmiş), yosun kenar bandı, çatlak, çakıl; hero_earth daireleri kaldırılır → yatak kenarı granit bordürle biter | aynı | mutlak | S-05 dekal + P-05 bordür/yosun |
| Katmanlar — orta (0,5–3 m) | fener 13+12, kaya 131 (geodezik), çalı 364 (tek tür) | fener 28 (3 tür), kaya 20 büyük + 120 basamak taşı, çalı 6 tür, çit 3 tür, bank/kova/fıçı/araba | ×1,5 | P-05 §4 |
| Katmanlar — arka (≥ 6 m) | 1 saray kutusu, 18 sakura blok, düz duvar, plaka | saray irimoya + 2 kanat, karamon, 3 kairō hattı, 4 köşk, 8 hero sakura, 20 çam, dev bambu gövdesi, vista kuşağı çatıları | mutlak | P-05 + S-05 |
| Kompozisyon — kadraj çizgileri | eksen yalnız köprü; kapı = 2 dikme + kiriş; meydan kenarı yok | K-G ekseni: karamon (0,−16) → meydan → 6 basamak → salon; sol kairō diyagonali, sağ sakura perdesi; fener çiftleri eksen üstünde 6 m aralık | ekseni ±52'de karamon (0,−20)'ye uzat | mutlak | S-05 |
| Kompozisyon — odak hiyerarşisi | her şey aynı yükseklik (≤ 5 m), odak yok | 1. saray sırtı 11,5 m; 2. karamon 6,5 m; 3. hero sakura 9–10 m; 4. kairō çatısı 4,2 m; 5. fenerler 2,1 m | + NE pagoda silueti 28 m (vista, 70 m uzakta) | mutlak | P-05 boyut sözleşmesi |
| Kompozisyon — derinlik katmanı | 2 katman (zemin + plaka) | 5 katman: havuz/köprü → meydan → salon/kanat → arka çatılar + ağaç kümeleri → sisli tepe (hedefte aynen) | 6 (vista kuşağı ek) | +3 | S-05 vista + AP-019 plakaları alta gömme |
| Saray — ölçek | ≈36×8 m, tek kat, sırt ≈5,5 m | ana salon 27×15 m, sırt 11,5 m, 2 kademe (ana çatı + 3,6 m'de mokoshi/engawa çatısı); 2 kanat 15×5 m (L), arka kanat cephe | aynı + arka avlu 12×10 | mutlak | P-05 kit + S-05 |
| Saray — kaide/basamak | veranda 0,35 m, basamak yok | taş kaide (kidan) 1,2 m; orta merdiven 6 m genişlik 6 basamak (rıht 0,20 m → NavMesh tırmanma 0,30 altında, rampa olarak pişer); 2 yan merdiven 1,8 m | aynı | mutlak | P-05; S-03 NavMesh doğrulama |
| Saray — çatı silueti | düz beşik, saçak kalkışı yok, sırt süsü yok | irimoya: eğim 30°, saçak kalkışı (sori) 0,35 m uçlarda, sırt kiremidi + onigawara 2 uç, kara-hafu yok (karamon'da var) | aynı | mutlak | P-05 |
| Saray — engawa/shoji/kafes | 12 özdeş kapalı bölme | engawa 1,8 m derin + kōran korkuluk 0,85 m; orta 3 bölme açık (karanlık iç, 6 m derinlik görünür), 6 bölme shoji kapalı, 2 bölme yarı açık; üst bant kōshi kafes (hedef gameplay'deki gibi) | aynı | mutlak | P-05 (3 panel durumu) |
| Saray — iç mekân | yok | girilebilir orta salon 12×8 m: tatami dokusu, 6 direk, fusuma duvarları, tokonoma nişi, 2 asılı fener, kılıç sehpası; tavan 3,2 m kiriş | aynı | mutlak | P-05 iç kit + S-05 |
| Kapalı koridor (kairō) | 6 modül açık dikme, düz çatı, yükseltilmiş döşeme yok, ≈18 m | yükseltilmiş 0,30 m ahşap döşeme, kiremit çatı (eğim 28°) + sırt, direk 0,27 m kare / 2,4 m aralık, bir yan kōran korkuluk, diğer yan renji-mado kafesli sıva duvar; hatlar: batı 36 m, kuzey 2×18 m (kanat → yan kapı), doğu 24 m; **1 tam kapalı** 18 m (iki yan duvarlı, 4 kafes pencere); toplam ≥ 96 m; iç genişlik 3,0 m | toplam ≥ 150 m + 2 tam kapalı | +78 m / +132 m | P-05 kairō kiti; S-05 hat; S-03 çıkmaz yok kuralı |
| Duvarlı avlu (tsuiji) | 74 düz sıva düzlemi, kapak yok, kaide yok, band yok | tsuijibei: taş kaide 0,4 m + sıva gövde + 5 yatay ahşap band + kiremit kapak, h 2,4 m, t 0,6 m; 2 alt avlu: çay bahçesi 14×12 m (GB), depo avlusu 12×10 m (KB); uzunluk ≥ 120 m iç duvar; çevre halkası tsuiji'ye dönüşür (arkasında vista) | + arka saray avlusu; ≥ 180 m | mutlak | P-05 duvar kiti (düz 3 m, köşe, kapı boşluğu, kırık parça) |
| Kapılar | 5 gate_transition (dikme+kiriş, çatısız) | 1 karamon (6 m açıklık, 6,5 m yükseklik, kara-hafu kavisli alınlık, 4 direk, kiremit) meydan güneyi; 2 yakuimon (3,6 m, kırma kiremit çatı) avlu girişleri; 2 sade çatılı duvar kapısı (2,4 m) kairō uçları; 1 ahşap torii (4 m, koyu vermilyon #A63A2E) doğu koru küçük tapınak (hokora) önü; toplam 6 | + 1 nagayamon (uzun kapı binası, KB servis) | mutlak | P-05; S-05 çatı soldurma listesi |
| Girilebilir köşk/salon | 1 shed (cephe), saray girilmez | girilebilir: saray orta salonu 12×8; çay evi (chashitsu) 3,6×3,6 + 1,2 m engawa (GB avlu); kura depo 6×4 (sıva, tek kapı, içeride fıçı/çuval) KB; azumaya açık köşk 4×4 (doğu koru); tsuridono su üstü köşkü 5×4 (havuz kuzey kıyısı, direkler suda) → 5 iç mekân, ≥ 190 m² iç zemin; cephe-only: kanat arkaları, vista yapıları | + 1 shōrō çan kulesi (girilebilir taban 3×3) | mutlak | P-05 + S-05 + S-03 (spawn erişimi) |
| Dekor — fenerler | 13 stone_lantern + 12 kırılabilir taş + 5 kâğıt, tek tip | tōrō 3 tip: kasuga 2,1 m ×14 (eksen + kapılar), yukimi 1,0 m ×8 (havuz kıyısı), oribe 1,4 m ×6 (çay bahçesi, patika); asılı bronz tsuri-dōrō 0,45 m ×24 (kairō saçak altı 4,8 m aralık); kâğıt chōchin 0,5 m ×12 (kapılar, çay evi, köşkler) → 64 fener; hepsi emissive (§5) | ×1,3 | +34 taş / +36 asılı | P-05 (varyant Blender); S-05 ışık bütçesi |
| Dekor — taş/bank/temizlik | 2 tsukubai, 29 bank/duvar kapağı | tsukubai 4 + hishaku, 1 büyük chōzubachi (meydan orta, 0,9 m), basamak taşı (tobi-ishi) 120, jizō/taş buda 4, komainu çifti 1, ema tahtası 1, bağış kutusu 1, kayalar 1,5–3 m ×20 (erozyon, yosun kuzey yüzü) | +30 % | mutlak | P-05 (kaya: Blender displacement; heykel: dış kaynak model) |
| Dekor — köprüler | 5 segment düz kalas | taiko köprü 12 m açıklık, 1,6 m kavis, korkuluk + giboshi topuzları; 2 taş plaka köprü 3 m; 1 yatsuhashi zikzak kalas 8 m (kamışlık üstü) | aynı | mutlak | P-05 |
| Dekor — çitler | bambu çit 35 + kırık 10 | yotsume-gaki (açık kafes) 60 m, kenninji-gaki (kapalı panel) 40 m, sode-gaki kol çiti 1,8 m ×6, takegaki kapı 2 | +40 % | mutlak | P-05 (modüler 1,8 m) |
| Dekor — araç/fıçı/tapınak | 4 araba, 4 fıçı, 8 saman, 9 ofuda, 8 furin | el arabası 6 (2 devrik), sake fıçısı (komodaru, hasır) 20 + kırık 6, pirinç çuvalı 20, sandık 10, nobori bayrak 3 m ×10 (siyah/çivit/beyaz + 2 vermilyon arma), bonshō çan + shōrō, tütsü ocağı, shimenawa ip 4, kılıç rafı 2, silah sandığı 12 (silah noktaları için) | aynı | mutlak | P-05 (kumaş: Blender cloth) |
| Ağaçlar — sakura | 18 (mor-gri bloklar) | hero 9–10 m ×8 (kanopi #F3CBD5 ışık / #E4A6B8 gölge / #C98CA0 iç), orta 6 m ×10, kanopi kartlar 3 katman + gövde LOD | + vista 8 | mutlak | P-05 (kanopi atlası dış kaynak, gövde Blender) |
| Ağaçlar — çam (niwaki) | 0 | budanmış bulut çam 3,5 m ×10, 6 m ×6; büyük kara çam 9 m ×4 (meydan köşeleri, havuz kıyısı hedefte 2) | + vista 6 | mutlak | P-05 |
| Ağaçlar — momiji | 0 | 5 m ×8 (yaz yeşili #7BA24B, %20 kızıl uç); GB çay bahçesi + havuz doğu | + 4 | mutlak | P-05 |
| Ağaçlar — bambu | 143 gövde + 93 küme, yaprak seyrek, kanopi yok | batı koru 25×30 m çekirdek ≥ 1,2 gövde/m² (≈ 700 gövde, h 8–12 m, çap 0,08–0,12), kanopi kartları 7–12 m'de %85 kapatma, kenar 0,4 gövde/m²; ışık huzmesi geçirgen | aynı + kuzeybatı ikinci koru 12×15 | ×5 | P-05 (gövde GPU instancing, kanopi atlası dış kaynak) |
| Ağaçlar — vista/arka plan | ağaç plakası (AP-019), alt kenar sert | 52–85 m: sugi 15–20 m ×24 (impostor), kafur/geniş yapraklı 10–14 m ×30 (impostor), söğüt 2 havuz güneyi; plaka ayakları vista kuşağının arkasına düşer | aynı | mutlak | P-05 impostor; S-05 |
| Yosun | 93 düz yama | 400 yama (3 shader varyantı: kadife/yastık/ıslak) + her taş varlıkta üst yön yosun maskesi (triplanar, 0,25–0,45 karışım) + fener kaideleri, kaya kuzey yüzleri, ağaç kök boğazı; ağaç altı toprakta ≥ %30 örtü | aynı | ×4 | P-05 materyal + S-05 |
| Çalı | 364 tek tür (koyu, düz) | satsuki kesme kubbe 0,6–1,2 m ×120 (3 boy), tamamono grup ×30, eğrelti ×200, hosta ×60, süs otu ×300, kamış/iris ×150 (havuz), kuru çalı (hedef ön plan) ×40 | aynı | ×2,5 (tür ×7) | P-05 |
| Çakıl / tırmıklı kum | quiet_paving 1 + shore_path 11 (altıgen taşlı plaka) | 1 karesansui avlusu 12×9 m (tırmık dalga normal haritası döşenebilir, 3 kaya grubu, yosun ada); havuz çakıl kıyı bandı 1,5 m; çakıl patika + tobi-ishi | + 1 küçük | mutlak | P-05 doku + S-05 |
| Koi havuzu | 3 ayrı leğen, özdeş blok halka, düz köprü, kamış yok | tek gövde 26×16 m düzensiz (mevcut 3 dikdörtgen birleşimi bağlanır), kıyı 3 tip: kaya (%45, 0,4–1,5 m karışık), çakıl plaj (%30), yosun/ot şevi (%25); kamış kümesi 40, nilüfer 30, tobi-ishi 8, koi 16 (iz dalgası), su: SSR/planar + kırılma + derinlik sisi + ıslak kıyı bandı 0,3 m | + güney gölet 10×8 | mutlak | P-05 kıyı kiti; S-05 su |

#### 2 · Kompozisyon ve bölge programı

Kamera gerçekleri: gp rig (0,15,−13) vfov 42 → zemin izi ≈ 28×16 m; tp 2,2 m vfov 50; fp 1,6 m vfov 60. "Hero kare" = her bölgede sabitlenecek 1 gp + 1 göz hizası referans kadrajı; Faz B'de aynı kadrajlar yeniden çekilir (§6).

| Bölge | gp hero (tepeden) — karede olması gerekenler | Göz hizası hero (tp/fp) — karede olması gerekenler |
|---|---|---|
| Merkez meydan (0,0) r 14 | Islak arduvaz büyük plakalar (1,2×2,0 m karışık, derz 12 mm), ≥ 1 granit bordürlü yatak (0,4 m) + yosun + satsuki, 1 kasuga fener, yaprak kümeleri bordür diplerinde, su birikintisi 2–3, ortada chōzubachi taş havuz + 2 fener (dais 0,3 m taş platform olarak kalır); dört köşede kara çam; kadrajda ≥ 1 duvar/direk dikeyi | Kuzeye bakış (0,−10) → karamon arkası, meydan, 6 basamak, açık orta bölme karanlık, sırt 11,5 m ve saçak kalkışı; sol kairō diyagonal, sağ sakura perdesi; fenerler eksen üstünde perspektif çizgisi kurar; gökte parçalı bulut |
| Kuzey saray (0,30) | Kaide + merdiven + engawa + korkuluk çatı soldurmasıyla görünür kalır (soldurma sınırı: saçak çizgisi); engawa'da fıçı/kılıç rafı; kaide dibi yosun bandı; 2 kasuga fener merdiven dibinde; kanat köşeleri kadraja girer | Cephe (0,18)'den: 2 kademeli çatı, kōshi kafes bandı, orta 3 bölme açık ve içeride 2 asılı fener sıcak; sol kanat + tsuiji duvar + yakuimon; sağ kanat + kairō; arkasında vista ağaçları ve kuzeydoğu pagoda silueti; ufukta sert band yok |
| Doğu sakura (30,0) | Yaprak halısı (%25 örtü, pembe), tobi-ishi patika 6 sakura + 3 çam + 4 kaya arasından kıvrılır, oribe fener, hokora + torii (vermilyon tek vurgu), azumaya çatısı, bambu yotsume çit | Kanopi kamera üstünde 4–9 m (tp'de kare üstü %30 pembe), düşen yaprak parçacığı, torii kadraj sağ üçte bir; arkada tsuiji + vista çatı + tepe plakası |
| Güney koi/köprü (20,−30) | Tek gövde havuz, kaya-çakıl-yosun kıyı geçişi, taiko köprü kavisi gölgesiyle, koi 6+ karede, nilüfer, kamış, yukimi fener 2, tsuridono köşk çatısı | **Ana hero:** köprü tepesinden (4, 2,5, −28) kuzeye bakış = hedef overview'ün oyun içi karşılığı: havuz → meydan → karamon → salon → kanatlar → vista → sis; 5 katman sayılabilir olmalı |
| Batı bambu (−30,0) | Gövde yoğunluğu ≥ 1,2/m², kanopi gölge gobosu zeminde, kairō kiremit çatısı sırtıyla, yosun zemin, oribe fener patikası, kura depo çatısı KB'de | Kairō içi (−31, 2,2, 15) güneye 36 m koridor: asılı fenerler 4,8 m aralık perspektif dizisi, sağda kafesli sıva duvar, solda korkuluk ardında bambu; koru içi: 3–5 ışık huzmesi (WP-075), gövde arası sis 0–3 m |

Eksen ve ritim kuralları (S-05): fener çiftleri eksen üstünde 6 m; kairō direk ritmi 2,4 m; tsuiji band yüksekliği 0,5/0,9/1,3/1,7/2,1 m; hiçbir yürünür 12 m çizgi üstünde ≥ 1 dikey öge olmadan kalmaz; her bölgeden ≥ 2 komşu bölgenin siluet ögesi görünür.

Büyütme kararı (AD önerisi, S-05/S-03 doğrular): oynanır alan ±45 → **±52 m** (spawn→oyuncu mesafesi +7 m ≈ +5 s tempo; S-03 spawn noktalarını dışa kaydırır), yeni alan kuzeyde saray kanat/arka avlu, güneyde havuz güneyi köşk/gölet, GB çay bahçesi, KB depo avlusu; **vista kuşağı 52–85 m** yürünmez gerçek geometri (duvar, çatı, ağaç, pagoda). Halka (WP-038M) ±52'ye taşınır; NavMesh 0,5 m içeride biter.

#### 3 · Göz hizası hazırlığı (üçüncü/birinci şahıs)

Karelerde görülen hatalar → gereksinim:

| Alan | Güncel hata (kare) | Gereksinim (ölçülebilir) |
|---|---|---|
| Arka yüzler | Çevre duvarı tek yüzlü düzlem, kapak yok (fp-horizon-north: karenin %100'ü düz sıva); çatı altı siyah (tp-west-bamboo) | Tüm duvar/çatı varlıkları çift taraflı kapalı hacim; çatı altında mertek + tahta kaplama (min 2 kiriş/m); "backface" görünürlüğü 1,6 m'den 0 |
| Direk ölçeği | Kairō direkleri ≈1 m (tp-west-bamboo) | direk 0,27 m kare, saray direği 0,42 m yuvarlak, karamon 0,45 m; insan 1,75 m ölçek objesiyle doğrulanır |
| İç mekân | Yok; shoji arkası düz sıva rengi | 5 girilebilir mekân (§1); cephe-only yapılarda açık pencere/kapı boşluklarında ≥ 2,5 m derinlik illüzyonu (iç kutu + parallax interior veya gerçek 1 oda) |
| Ufuk/siluet | Sarı-krem band (L≈0,78) duvar üstü ile plaka arasında; plaka alt kenarı sert; gök plakası dikey ek yeri görünür (fp-horizon-east orta) | Ufuk parlaklık gradyanı sürekli: komşu %1 yükseklik bantları arasında ΔL ≤ %3; plaka ayakları vista geometrisinin ≥ 4 m arkasında/altında; plaka dikişi 0; uzak siluet: pagoda 28 m @ 70 m, shōrō, 3 çatı sırtı, sugi hattı |
| Gök görünürlüğü | fp'de kare üst %45 düz gri | parçalı bulut plakası + güneş yönü sabit (azimut 215°, yükseklik 38°) gölgeyle tutarlı; gök plakası 4096×1024 eşdeğeri, ufukta sis rengiyle aynı değer (#C3C8CB ±2) |
| Yakın kamera detay | Sıva düz, kiremit çizgi, yaprak tek tek, kaya faset | Texel yoğunluğu LOD0: zemin/duvar/fener ≥ 1024 px/m (FP 1 m), orta 512, uzak 256; dekal seti: su birikintisi 6, yosun kenar 4, çatlak 4, sıva akıntısı 3, kiremit liken 2, yaprak kümesi 4; ıslaklık: alt 0,5 m sıva koyulaşma |
| Çatı soldurma | Kapı çatısı FP'de siyah pano (fp-plaza-palace) | FP/TP'de soldurma kapalı, tepeden yalnız kamera–oyuncu arası; soldurulan parça alfa değil dither + iç yüz görünür |
| Sis derinlik katmanı | Tek düzlem sis, 40 m'de sert geçiş | Yükseklik sisi 0–6 m (zeminde yoğun, hedef arka planda görünür), mesafe sisi 35 m başlangıç, 200 m'de %85; bölgesel hafif sis hacimleri (WP-075) bambu ve havuz üstü |
| Silüet okunabilirliği | Ağaçlar blob, çatı düz | Çatı eğimi 28–32°, saçak kalkışı ≥ 0,3 m, sırt kiremidi; sakura kanopi 3 kat, çam bulut katmanları 4–6; siluet kontrastı sisli göğe karşı ΔL ≥ 0,25 |
| Kenar ötesi | Halka ötesi boş düzlem | Vista kuşağında hiçbir yönde 1,6 m'den ≥ 25 m boş düzlem görünmez |

#### 4 · Eksik varlık listesi — AP-023 (P-05)

Dış kaynak 70 kr planı: gök v2 plakaları 12 · saray salonu konsept (cephe + köşe) 8 · karamon/yakuimon 4 · kairō 4 · çay evi/kura/azumaya/tsuridono 8 · tōrō + dekor levhası 6 · ağaç atlasları (sakura/çam/momiji) 9 · bambu kanopi atlası 3 · havuz kıyı/kamış 3 · nobori desen 2 · rezerv 11 = 70. Modüler yapılar Blender prosedürel (kiremit dizisi, sıva band, direk ritmi, çit); organik/heykel dış kaynak model; ağaç gövde Blender + kanopi kart atlası dış kaynak.

| P | Varlık | Adet (±45 / büyük) | Boyut | LOD / impostor | Materyal | Hedef ref | Üretim |
|---|---|---|---|---|---|---|---|
| P0 | Saray salonu kiti: kaide blok, basamak (6+2), direk, engawa döşeme, kōran korkuluk, shoji panel ×3 durum, kōshi üst bant, irimoya çatı (sırt/köşe/saçak/onigawara), mokoshi çatı | 1 salon + 2 kanat (≈140 modül) | 27×15×11,5; kanat 15×5×6 | LOD0 ≤ 60k tris toplam, LOD1 20k, LOD2 6k; kanat arkası impostor | kiremit #474B4F ıslak, sıva #DED6C4, ahşap #4B3B2E, kaide granit | overview merkez üst | dış kaynak 8 kr + Blender |
| P0 | Saray iç kiti: tatami 0,9×1,8, fusuma, tokonoma, iç direk, tavan kirişi, asılı fener | 1 salon (12×8) | — | LOD0/1 | tatami #B8A97A, fusuma #E3DCCB altın çizgi | — | Blender |
| P0 | Kairō modülü 2,4 m: döşeme 0,3 m, direk 0,27, korkuluk, kafesli sıva duvar, kiremit çatı, köşe, uç | 40 / 63 | 2,4×3,0×4,2 | LOD0 4k, LOD1 1,2k, LOD2 300 | ahşap silverleşme üst yüz, kiremit yosun oluk | overview sol | dış kaynak 4 + Blender |
| P0 | Tsuijibei duvar kiti: düz 3 m, köşe, kapı boşluğu, kırık, kapak sırtı | 60 / 90 | 3×0,6×2,4 | LOD0 1,5k, LOD1 400 | sıva + 5 band, taş kaide, kiremit kapak | gameplay sol, overview sağ uç | Blender |
| P0 | Karamon | 1 | 6×3×6,5 | LOD0 20k / 6k / 1,5k | kara-hafu bakır sırt, ahşap, kiremit | — (dönem ref.) | dış kaynak 4 (yakuimon ile) + Blender |
| P0 | Yakuimon + sade duvar kapısı | 2 + 2 / 3 + 2 | 3,6×2×4,2 ; 2,4×1×3 | 8k/2k/600 | kiremit kırma | overview sağ orta yapı | Blender |
| P0 | Tōrō kasuga / yukimi / oribe | 14/8/6 → 18/10/8 | 2,1 / 1,0 / 1,4 | 3k/800/200 | granit yosunlu, emissive ateş kutusu | overview fenerler, gameplay sağ | dış kaynak 6 (dekor levhası) + Blender varyant |
| P0 | Sakura hero + orta | 8+10 / 12+14 | 9–10 m; 6 m | LOD0 25k, LOD1 8k, LOD2 2k, oktahedral impostor | kanopi 3 ton pembe, gövde koyu #3E3330 yosun | overview sağ | dış kaynak atlas 3 + Blender |
| P0 | Çam niwaki 3,5 / 6 / kara çam 9 | 10/6/4 → 14/8/6 | — | 18k/6k/1,5k + impostor | iğne #3F5A3A, gövde kızıl-kahve | overview sol-orta, sağ alt | dış kaynak atlas 3 + Blender |
| P0 | Bambu gövde ×3 çap + kanopi kartı + devrik | 700 gövde / 1000 | 8–12 m | gövde GPU instancing 300 tris; kanopi kart | culm #8FA84C→#6E8A3C, düğüm koyu | overview sol | dış kaynak atlas 3 + Blender |
| P0 | Havuz kıyı kiti: kaya 5 boy, çakıl plaj şerit, yosun şev, tobi-ishi, ıslak band | 1 havuz (≈220 parça) | kaya 0,4–1,5 | 2k/600/150 | erozyon kaya, ıslak koyulaşma | overview ön | Blender displacement |
| P0 | Taiko köprü | 1 | 12×3, kavis 1,6 | 12k/4k/1k | ahşap ıslak, giboshi bronz | overview ön | Blender |
| P0 | Gök v2 plakaları: parçalı bulut gök bandı + güneş boşluğu, 2 bulut katmanı | 1 set | 4096×1024 eşdeğeri | — | HDR-ish, ufuk #C3C8CB | overview gök + R1 N9 | dış kaynak 12 |
| P0 | Granit bordür + yatak kiti (düz/köşe/kırık) | 80 / 120 | 1,0×0,3×0,4 | 300/100 | granit ıslak, yosun derz | gameplay sağ | Blender |
| P1 | Çay evi (chashitsu) + engawa + nijiriguchi | 1 | 4,8×4,8×3,8 | 15k/5k/1,2k | saz çatı (kayabuki) veya kiremit, ahşap | — | dış kaynak 8 (4 köşk) + Blender |
| P1 | Kura depo | 1 / 2 | 6×4×5 | 10k/3k/800 | kalın sıva #E8E2D3, kara namako duvar, çift kapı | — | Blender |
| P1 | Azumaya köşk | 1 / 2 | 4×4×3,6 | 6k/2k/500 | kiremit, bank | — | Blender |
| P1 | Tsuridono su köşkü | 1 | 5×4×4 | 8k/2,5k/600 | direkler suda, ıslak | — | Blender |
| P1 | Asılı fener tsuri-dōrō (bronz) + chōchin kâğıt | 24 + 12 / 36 + 16 | 0,45 / 0,5 | 1,2k/300 | bronz yeşil patina; kâğıt SSS emissive | — | Blender |
| P1 | Momiji 5 m | 8 / 12 | — | 15k/5k/1,2k + impostor | yeşil-lime, kızıl uç | — | dış kaynak atlas 3 + Blender |
| P1 | Çit kiti: yotsume, kenninji, sode-gaki, takegaki kapı | 33+22+6+2 modül | 1,8 m | 800/200 | bambu eskimiş #9A8C5C, siyah ip bağ | — | Blender |
| P1 | Nobori bayrak + direk | 10 / 14 | 3×0,6 | 600 | kumaş sim rüzgâr, arma | — | dış kaynak desen 2 + Blender |
| P1 | Karesansui avlu dokusu + tırmık normal + 3 kaya grubu | 1 | 12×9 | doku 2048 | çakıl #B9B6AC | — | Blender |
| P1 | Kamış/iris kümesi, nilüfer, su otu | 40/30/60 | 0,8–1,4 | kart | SSS yeşil | overview ön sol | Blender |
| P1 | Satsuki kubbe 3 boy, tamamono, eğrelti, hosta, süs otu | 120/30/200/60/300 | 0,3–1,2 | kart/2k | — | overview yeşil kümeler | Blender |
| P1 | Taş plaka köprü, yatsuhashi | 2 + 1 | 3 m; 8 m | 1k | granit / kalas | — | Blender |
| P1 | Jizō/taş buda, komainu çifti, bonshō çan + shōrō, tütsü ocağı, ema | 4/1/1/1/1 | 0,8–1,2; shōrō 3×3×5 | 5k/1,5k | granit yosun, bronz | — | dış kaynak model (heykel) + Blender |
| P1 | Sake fıçı komodaru + kırık, pirinç çuvalı, sandık, silah sandığı | 20+6/20/10/12 | 0,5–0,8 | 800/200 | hasır, ahşap | — | Blender |
| P1 | Kaya 1,5–3 m (5 varyant) | 20 / 28 | — | 4k/1,2k/300 | erozyon, yosun üst maske | overview sağ alt | Blender |
| P1 | Dekal seti (birikinti, yosun kenar, çatlak, akıntı, liken, yaprak kümesi) | 23 dekal | — | — | — | gameplay yaprak kümeleri | Blender/Substance |
| P2 | Vista impostor seti: sugi, kafur, söğüt, arka çatı kartı, pagoda 5 kat | 24/30/2/12/1 | 15–28 m | impostor | sis rengine karışan | overview arka | Blender |
| P2 | Nagayamon uzun kapı binası (büyütme) | 0 / 1 | 12×4×5 | 12k/4k | — | — | Blender |
| P2 | Kuyu çatısı, kova, hishaku rafı v2, kagura sehpası cilası | mevcut yükseltme | — | — | — | — | Blender |

#### 5 · Işık / materyal notları (dünyaya ait; LUT değil)

| Yüzey | Kural |
|---|---|
| Islak arduvaz | albedo kuru #5A6065 → ıslak ×0,62 = #3F4448; smoothness taban 0,55, birikinti maskesinde 0,82; birikinti maskesi yükseklik + vertex alfa, alan %8–12; mikro normal 2 katman (plaka 0,5 m, derz 12 mm çukur); yağmur halkası yok (hedef durgun) |
| Yosun | albedo #4F6B3A / #7F934A, roughness 0,85, hafif SSS 0,15; üst yön triplanar maske (normal.y > 0,6) tüm taş varlıkta 0,25–0,45; kaya kuzey yüzü +0,2 |
| Kiremit (ibushi) | #474B4F, anizotropik parıltı 0,3, ıslak smoothness 0,60; oluk dipleri yosun/liken dekal; sırt kiremidi ayrı materyal; onigawara koyu |
| Sıva | #DED6C4 (mevcut cephe #B9B48A sarıya kaymış → düzelt), roughness 0,9; saçak altı 0,3 m kir akıntısı, taban 0,5 m ıslak koyulaşma ×0,8; band ahşabı #4B3B2E |
| Ahşap | sedir eskimiş #4B3B2E, açık yüzey silverleşme #8A8478 (üst kenarlar), roughness 0,7 → ıslak 0,5; damar normal 1024/m |
| Emissive fener | renk #FFC078 (≈2500 K), emissive 4 nit (HDR), taş fener alev titremesi 0,5 Hz ±%10; gerçek zamanlı point ışık bütçesi ≤ 12 (eksen kasuga 6 + salon 2 + kairō 4), kalan yalnız emissive + bake; kâğıt fener SSS yayılma |
| Su | #4C6B72 derin, kıyıda kırılma; SSR (Yüksek/Sinematik) / planar (Sinematik); derinlik sisi 1,2 m; ıslak kıyı bandı 0,3 m smoothness 0,75 |
| Granit | #7C7F7B, kırık kenar aşınma, derz yosun |
| Bambu | culm #8FA84C→#6E8A3C, düğüm #3E4A2A, ıslak smoothness 0,45 |
| Toprak patika | #6E6152 ıslak, tekerlek izi dekal, çakıl karışım |
| Işık yönü (S-05 ile ortak) | güneş azimut 215°, yükseklik 38°, renk 5200 K, yoğunluk parçalı bulut altı; gölge 2 kaskat → 3 (Yüksek); ambient gök #B7BEC4 |

#### 6 · AD kabul ölçütleri — Faz B

| # | Ölçüt | Ölçüm | Eşik |
|---|---|---|---|
| A1 | Yan yana genel benzerlik | Sabit kadraj K1 (ov-south-high yerine hedef overview'e hizalanmış: kamera (0, 24, −40), pitch 26°, vfov 42) ile hedef; 3×3 bölge ortalama L ve doygunluk farkı; 12 maddelik AD listesi (5 katman, eksen, kairō, sakura, çam, havuz, köprü, fener, kaide/merdiven, kanat, vista, sis) | bölge ΔL ≤ %12, ΔS ≤ %10; liste ≥ 11/12 |
| A2 | Yoğunluk | manifest sayımı (okunur örnek/100 m², yapı modülü/100 m²) | ≥ 35 ve ≥ 7,5 (±45) ; ≥ 39 ve ≥ 8,3 (±52) |
| A3 | gp kadraj doluluğu | 6 m ızgara örnekleyici, frustum içi sayım | ≥ %90 noktada ≥ 20 okunur + ≥ 1 dikey + ≥ 1 gölge kanopi/saçak |
| A4 | Kapalı koridor | kairō hat uzunluğu, tam kapalı adet | ≥ 96 m / ≥ 1 (±45); ≥ 150 m / ≥ 2 (±52) |
| A5 | Girilebilir iç mekân | NavMesh üstünde erişilir iç zemin | ≥ 5 mekân, ≥ 190 m², çıkmaz 0 |
| A6 | Saray silueti | K1'de yapı maskesi: sırt yüksekliği, saçak kalkışı, kademe sayısı | 11,5 m ±0,5; kalkış ≥ 0,3; 2 kademe; irimoya |
| A7 | Ufuk bandı | fp-horizon-N/E/S/W 1,6 m 4 yön: dikey L profili | komşu %1 bant ΔL ≤ %3; sarı band (H 45–60°, S > 0,15) piksel ≤ %0,5 |
| A8 | Siyah düzlem | tp/fp 16 sabit kadraj | L < 0,03 bağlantılı alan ≤ %2 kare |
| A9 | Arka yüz | tp/fp tarama, backface görünürlüğü | 0 |
| A10 | Ağaç sayımı ve rengi | manifest; sakura kanopi ortalama renk | sakura ≥ 18, çam ≥ 20, momiji ≥ 8, bambu ≥ 700; kanopi H 335–350°, S 0,20–0,35, L ≥ 0,70 |
| A11 | Fener | manifest 3 tür + asılı | ≥ 28 taş (3 tür) + ≥ 36 asılı; emissive %100 |
| A12 | Havuz | tek bağlı su gövdesi; kıyı tipi oranı; köprü kavisi | 1 gövde; kaya/çakıl/yosun 45/30/25 ±10; kavis ≥ 1,4 m |
| A13 | Texel yoğunluğu | LOD0 fp 1 m ölçüm | zemin/duvar/fener ≥ 1024 px/m |
| A14 | Tutarlılık sapması | 60 rastgele varlık örneklemesi, §5 materyal kuralı | sapma ≤ %5 |
| A15 | Teknik tavan | kademe başına ms (WP-075 sözleşmesi) | 🔴 karar öncesi 8,0 ms Performans; Yüksek ≤ 16,6 / p99 ≤ 20 |
| A16 | NavMesh/oyun | S-03 12 spawn erişim, 40 düşman soak | çıkmaz 0, adacık 0 |

Açık kararlar (C-00'a): (1) büyütme ±52 + vista 85 (S-03 tempo onayı); (2) perf 60 fps ve build 2 GB (§6 brief); (3) torii vermilyon tek vurgu (renk canlılığı N19 ile uyumlu, palet dışı tek renk).

Kaynak dosyalar: `~/Desktop/KADRO/context/projects/deaths-demolition/wp-074/COUNCIL-CONTEXT.md`, `~/Desktop/KADRO/context/projects/deaths-demolition/wp-074/placement-families.txt`, `~/Desktop/KADRO/context/projects/deaths-demolition/m4-r1/M4-R1-REVIZYON-TURU.md`, DEC-076 stil referansı kareleri (genel bakış + oynanış, depo dışı), `~/Desktop/KADRO/context/projects/deaths-demolition/wp-074/captures-current/world-v3/`.


## Ek B — Seviye tasarımı metni (game-designer, birebir; başlıklar iki düzey alta)

### WP-074 Dünya v5 — Seviye tasarımı yönü (game-designer, Faz A, 2026-09-24)

Girdi olarak kullanılan sayılar (kod/config'den okundu, varsayım değil): harita ±45 m, NavMesh halkanın 0,5 m içinde biter; oyuncu hızı 6,2 m/s; düşman hızları Standard 2,7 / Runner 4,6 / Guard 2,2 / Executioner 1,7 m/s; spawn: MinSpawnDistance 12 m, tercih bandı 18–35 m, MaxUsefulDistance 62 m, OnScreenRejectDistance 20 m, PointCooldown 4 s, LocalDensityRadius 9 m; NavMesh ajanı r 0,5 / h 2,0; tırmanma kuralı 0,30 (WP-038); tepeden rig (0,15,−13) vfov 42 → yer penceresi ≈ 27–43 m genişlik × 23 m derinlik (oyuncunun 7,5 m arkası, 15 m önü); 3168 örnek / 70 aile; NavMesh varlığı 251 KB; perf 7,8 / 8,0 ms. Yol katsayısı (yürüme yolu / kuş uçuşu) tüm hesaplarda **1,25**.

---

#### 1 · Büyütme kararı

**Karar: ±55 m (110 × 110 m, kare, orijin merkezli). Yedek: ±50 m. ±60 reddedildi.** Büyüme simetrik kalır; WP-038M halkası ve RingRegionMap (CenterRadius 14) orijinde durur, yalnız yarıçaplar değişir.

##### 1.1 Neden büyüme gerekli, neden ±55
Kullanıcı isteği (koridor + kapalı alan + dekor + ağaç) ±45 içinde yalnız merkezi daraltarak karşılanır; bugün 5 bölge içinde yapı olarak tek uzun salon + 24 m koridor var. Plan v6 ≈ 175 m örtülü koridor + 5 kapalı salon + 4 duvarlı avlu ekliyor; bunun brüt ayak izi ≈ 3 400 m². ±45'te bu, merkez arenasını (r 14) ve dolaşım halkasını yer; ±55'te (+4 000 m²) arenaya dokunmadan sığar.

##### 1.2 Sayısal etki (başlangıç noktası (−7,5, 0) referans, yol ×1,25)

| Ölçüt | Bugün ±45 (12 kapı) | v6 ±55 (12 kenar + 6 orta) | ±60 (kenar) | ±50 (kenar) |
|---|---|---|---|---|
| Kapı → başlangıç yolu min/medyan/max | 35 / 42 / 53 m | 34 / 67 / 85 m (kenar tek: 60 / 75 / 85) | 66 / 81 / 92 m | 54 / 68 / 78 m |
| Ağırlıklı beklenen ilk temas yolu* | ≈ 42 m | **≈ 53 m** | ≈ 70 m | ≈ 60 m |
| İlk temas Standard (2,7) | 16 s | **19,6 s (+3,6 s)** | 26 s | 22 s |
| İlk temas Runner (4,6) | 9 s | **11,5 s** | 15 s | 13 s |
| Executioner en uzak kapıdan | 31 s | 50 s → tier kapısı ile ≤ 35 s | 54 s | 46 s |
| Brüt alan / yürünebilir tahmini | 8 100 / 5 000 m² | 12 100 / 6 800 m² | 14 400 / 7 800 m² | 10 000 / 5 800 m² |
| m² / aktif zombi (40 tavan) | 126 | **169** | 194 | 145 |
| NavMesh hücre/bake/varlık boyutu | 1,0× (251 KB) | ≈ 1,5× (≈ 380 KB, bake +50 %) | 1,8× | 1,25× |
| Örnek sayısı hedefi (aynı m² yoğunluğu + yapılar) | 3 168 | ≤ 4 900 | ≈ 5 800 | ≈ 4 200 |

\* Spawn ağırlığı bandı (22–45 m tam ağırlık, 62 m'de 0,15 tabanı) ile ağırlıklandırılmış beklenti; S-03 simülasyonu doğrular.

**Tempo:** 12 turda ilk temas gecikmesi toplam ≈ +45 s (≈ 25 dk koşuda +3 %). Havuz damlaması (PointCooldown 4 s) 18 kapıyla 4,5 spawn/s tavanına çıkar (bugün 3/s); damlama darboğaz olmaz. ±60'ta Standard medyan 26 s ve son-zombi avı (Executioner 54 s) tur sonlarını ölü zamana çevirir; reddin nedeni budur.

**Kiting:** halka uzunluğu L için oyuncu turu L/6,2 s; bir turda Runner L·0,26, Standard L·0,56 geride kalır. 22 m halka → Runner'a 5,7 m (1,2 s ateş penceresi) — bu **asgari**; birincil halkalar 40–60 m (10–15 m, 2,2–3,4 s) olmalı. ±55, 8 adet ≥ 40 m halkaya yer verir (§3.5).

**QA botu:** daha çok alan = daha kolay kaçış; risk yeni kapalı alanlar. Kural: her oda ≥ 2 çıkış, çıkışlar ≥ 6 m ayrık ve aynı duvarda değil; koridorlarda en yakın açıklık ≤ 9 m. Bot için oda-çıkış grafı S-03'e verilir (§5).

**Perf:** tepeden pencere sabit (≈ 30 × 23 m), ekrandaki örnek sayısı harita boyutuna değil m² yoğunluğuna bağlıdır → tepeden maliyet tahmini +0,3–0,5 ms (yoğunluk 0,39 → 0,45 örnek/m²). TP/FP'de derinlik 55 m'ye kadar görünür → statik occlusion culling + LOD bias 2,0 ile hedef ≤ 900 görünür renderer; tahmin +1,0–1,5 ms → 8,0 ms'ye sığmaz, **60 fps kararı (16,6 ms) veya Performans kademesi (yeşillik −40 %) gerekir**. Duvar/koridorlar TP/FP'de doğal occluder'dır; ±55'te yapıların çoğu kenar bandında olduğu için merkezden bakışta ufuk plakaları (r 260/330/420) yerinde kalır.

**Kamera/occluder:** tepeden 7 çatı soldurma grubu + güney avlu duvarları; TP boom (≈ 3,5 m) koridor iç genişliği 4,0–4,5 m ile çarpışmaz; kapı yüksekliği ≥ 2,4 m.

##### 1.3 Yedek ±50
Faz B'de (a) TP/FP perf Performans kademesinde bile > 16,6 ms, (b) bot 2 tohumda 12/12 vermezse, (c) AP-023 70 kr yapı ailelerine yetmezse → ±50: koridor K4/K5 kısalır (33 → 24 m, 22 → 16 m), R5 azumaya ve NE çam köşesi düşer, kapı sayısı 16; yerleşim koordinatları 55/50 ölçeğinde yalnız kenar bandı kayar, merkez ve saray bileşkesi değişmez.

---

#### 2 · Yerleşim planı v6

5 bölge kimliği korunur; oyuncu başlangıcı (−7,5, 0, 0) ve merkez arena (r 14, çakıl, köşe yuvarlatma 3,5) değişmez.

##### 2.1 Bölge haritası (5 m hücre, şematik; Unity x sağa, z yukarı)

```
      -55   -45   -35   -25   -15    -5     5    15    25    35    45    55
z +55                       G  =  =  =  =  =  =  =  G
z +50     :  :  :  :  :     =  =  =  =  g  =  =  =  =
z +45     :  G  #  #  :     #  #  #  #  W  #  #  #  #        p  p  G  p  p
z +40     :  #  #  #  :     -  -  -  -  -  -  -  -  -        p  .  p  p  p
z +35     :  #  W  #  :  #  =  -  -  -  -  -  -  -  =  #     .  .  p  p  p
z +30  b  :  :  :  g  :  +  =  :  :  :  W  :  :  :  =  +  #  W  #  p  p  p
z +25  b  b  .  .     =  #  g  :  :  +  +  +  :  :  =  #  =  #  #  p  p  p
z +20  b  G  .  b  =  =  #  =  .  .  +  +  +  .  .  =  #  W  =  =  =  p  p
z +15  b  .  .  b  =  =        :  :  :  :  :  :  :     s  s  s  s  s  G  s
z +10  b  b  .  b  =  =        .  .  .  .  .  .  .     #  #  +  #  #  .  s
z  +5  b  b  .  b  =  =        :  :  :  :  :  :  :     #  s  s  s  #  s  s
z  +0  b  b  W  .  W  =  .  .  .  :  @  :  :  :  .  .  +  s  W  s  +  .  s
z  -5  b  b  .  b  =  =        :  :  :  :  :  :  :     #  s  s  s  #  s  s
z -10  b  b  .  b  =  =        :  :  :  :  :  W  :     #  #  g  #  #  .  s
z -15  b  .  .  b  g  =     .  .  .  .  .  W  .  .  .  .  .  .  s  .  G  s
z -20  b  G  .  .  .  .  .     .  o  ~  ~  |  :  .  W  :  .  s  s  .  .  s
z -25  b  b  .  .        .     .  o  ~  ~  |  ~  .  :  :  .  s  s  .  s  s
z -30  b  b  b  .  #  #  .     .  o  ~  ~  |  ~  .  g  :  A  A  .  .  s  s
z -35              #  W  .     .  o  ~  ~  |  ~  .     s  A  W  .  s  s  s
z -40           .  #  #  .  .  .  ~  ~  ~  |  :  .  :  :  .  .  G  s  s  s
z -45        G  .        :  =  =  W  =  =  :  :  :  :  :
z -50                          G           G
z -55
```
`#` duvar/yapı · `=` örtülü koridor (kairō) · `-` veranda · `+` kapı/geçit · `:` çakıl/taş avlu · `.` yol · `~` su · `|` köprü · `o` taş atlama geçidi · `A` azumaya (direkli çatı) · `b` bambu · `s` sakura/akçaağaç · `p` çam+kaya · `G` kenar spawn · `g` orta spawn · `W` silah noktası · `@` oyuncu başlangıcı

##### 2.2 KUZEY — Saray bileşkesi (x −26…26, z 18…54)

| Öğe | Kutu / ölçü | Girişler | Notlar |
|---|---|---|---|
| **R1 Ana salon** (girilebilir) | x −18…18, z 38…50 (36 × 12 m), tavan 6,5 m, mahya 8 m | G (−10,38) 3 m · G (0,38) 3 m · G (10,38) 3 m · D (18,44) 2,4 m · B (−18,44) 2,4 m · K (0,50) 3 m → **6** | Zemin 0,35 (veranda ile aynı), arka dais x −6…6, z 46…50 +0,30 (tek basamak). İçeride 2 shoji bölme x ±6, z 42…46 (4 m, iki ucu açık) → 3 nef, 2 iç halka ≈ 40 m. Mevcut 11–12 saray modülü (roof_bay/screen/veranda/clerestory/rafters) burada yeniden kullanılır, +6 m ve arka duvar/kapı eklenir. |
| Veranda (engawa) | x −20…20, z 36…38, yükseklik 0,35 | açık | mevcut palace_veranda |
| **K6 Arka kairō** | x −22…22, z 50…54, 4 m iç, çatı 3,2, açık yanlı | iki uç flank yollarına | 44 m; kuzey kenar spawn'ları buradan gelir |
| **Ön avlu** (duvarlı, açık hava) | duvar x ±26, z 20…36; iç açık alan x −20…20, z 24…36 (40 × 12) | Kapı evi (güney) · D (26,30) 4 m · B (−26,30) 4 m | çakıl + taş; mevcut 14 m ön avlu bandı buraya taşınır |
| **K3 Avlu kairō ×2** | x −24…−20 ve 20…24, z 22…36; 4 m iç, çatı 3,4 | veranda + kapı evi yanları | 14 m × 2 |
| **R6 Kapı evi (chūmon)** | x −4…4, z 18…23 (8 × 5), tavan 5,0 | G açıklık 5 m · K açıklık 5 m · 2 yan kapı 2,0 m (kairō'ya) | geçiş yapısı; darboğaz değil (5 m) |
| Ön meydan | x −16…16, z 12…20, taş | açık | mevcut 16 m saray yolu 8 m'ye iner, iki yanına fener sırası |
| Flank yolları | x ±22, z 20…50, 4 m | — | bileşkeyi dıştan dolaşan halka L4 |

##### 2.3 BATI — Bambu korusu, kairō, hizmet avlusu (x −54…−26, z −46…48)

| Öğe | Kutu / ölçü | Girişler | Notlar |
|---|---|---|---|
| **K1 Batı kairō** (kapalı, mevcut uzatılır) | duvar x −36…−30, iç 4,5 m, z −16…20 (36 m), çatı 3,4 | G ağız (−33,−16) 4 m · K ağız (−33,20) 4 m · D (−30,−7) 4 m · D (−30,7) 4 m · B (−36,0) 3 m → **5** | mevcut west_corridor_module ×6 → ×9; tek orta kapı yerine 2 doğu kapısı; en uzak açıklık 9 m |
| **K2 Kuzeybatı bağlantı** | (−33,20)→(−28,27)→(−26,30), 4 m, çatı 3,4, açık yanlı | K1 kuzey ağzı ↔ batı avlu kapısı | 12 m; mevcut open_corridor ×2 burada |
| Bambu şeritleri | dış şerit x −45, z −26…26 (52 m); bağlantı (−36,0)→(−45,0); çaprazlar (−33,−16)→(−45,−26), (−33,20)→(−45,26); kenar (−52,±18)→(−45,±14) | — | hepsi 3,0 m; bambu 236 → ≈ 380 örnek |
| **Hizmet avlusu** (çitli, açık hava) | x −48…−28, z 30…48 | G (−36,30) 4 m · D (−28,32) K2'ye 4 m · KB kapısı (−46,46) | kuyu, el arabaları, kurutma rafı, saman balyası buraya yoğunlaşır |
| **R3 Ambar (kura)** | x −44…−34, z 36…44 (10 × 8), tavan 4,0 | D (−34,40) 2,2 m · G (−39,36) 2,2 m → **2** | kalın sıva duvar; içi yarı boş (fıçı/yığın örtü) |
| **R4 Güneybatı çay evi** | x −36…−28, z −40…−32 (8 × 8), tavan 3,6 | K (−32,−32) 2,2 m · D (−28,−36) 2,2 m → **2** | mevcut pagoda spur döngüsünün yerine; spur yolu korunur |

##### 2.4 DOĞU — Sakura bahçesi, büyük sakura avlusu, köşk (x 26…54, z −40…44)

| Öğe | Kutu / ölçü | Girişler | Notlar |
|---|---|---|---|
| **R7 Büyük sakura avlusu** (duvarlı, açık hava) | duvar x 27…45, z −8…10 (18 × 18), duvar h 1,8 m | B (27,1) 4 m (doğu radyali) · K (36,10) 4 m · G (36,−8) 4 m · D (45,1) 3,5 m → **4** | mevcut büyük sakura çatalı (30…39, −7…8) içeride kalır; ada halkası 40 m, duvar dibi halkası 60 m; tsukubai, 3 fener |
| **R2 Doğu köşk** (çay pavyonu, girilebilir) | x 30…40, z 24…32 (10 × 8), tavan 3,8 | B (30,28) 2,4 m · G (35,24) 2,4 m · D (40,28) 2,4 m → **3** | shoji + veranda; TP/FP'de iç mekân vitrin |
| **K4 Doğu kairō** | (26,30)→(30,22)→(44,22)→(50,14), 4 m iç, çatı 3,4, açık yanlı | avlu doğu kapısı ↔ E_GardenNorth | 33 m; batı kairō'nun aynası |
| Sakura/akçaağaç bahçesi | x 26…52, z −40…20 | — | sakura 18 → 34, akçaağaç +12, fener 13 → 30; iç patikalar 3,5 m: (36,−8)→(30,−18)→(28,−30); (45,−8)→(48,−20)→(40,−34)→(28,−41) |
| Çam + kaya köşesi (KD) | x 36…54, z 20…44 | (44,44)→(40,40)→(36,34) 3,5 m | çam +10, kaya grupları; NE_Corner spawn |
| **R5 Azumaya** (4 direk + çatı) | x 30…36, z −36…−30, çatı 3,2 | her yan açık | kıyı köşkü; dekoratif örtü, yarı kapalı akustik |

##### 2.5 GÜNEY — Koi havuzu, köprü, karesansui, kıyı (x −30…30, z −54…−14)

| Öğe | Kutu / ölçü | Girişler | Notlar |
|---|---|---|---|
| Havuz | mevcut 3 dikdörtgen aynen (−11…6/−33…−20; 2…12/−36…−24; −9…1/−38…−30) | — | 3 koi_pond_basin kalır; büyütülmez (köprü uzamasın) |
| Köprü | x 4, z −18…−39,5, 3,0 m (mevcut 5 segment) | — | REQ-110 yumuşak darboğaz; tek istisna olarak 21,5 m koşu |
| **Taş atlama geçidi** (yeni) | x −8, z −20…−33, 2,0 m; orta adacık 3 × 3 m (z −27) | — | batı lobunu ikinci kez keser → havuz çevresinde 2 halka; iki 6 m koşu |
| **Karesansui** (açık hava, alçak duvar) | x 12…28, z −30…−16 (16 × 14), duvar 0,9 m K ve D kenarda, 2 boşluk | B (12,−22) · G (20,−30) · K (18,−16) · D (28,−22) | taranmış çakıl yürünebilir; 5 kaya grubu siper; I_ShoreEast kapısı doğu boşluğunun arkasında |
| **K5 Güney kıyı çardağı** | (−22,−46)→(0,−46), 3,5 m iç, çatı 3,2, açık yanlı | iki uç kıyı yoluna | 22 m; köprü güney ucuna varır |
| Çakıl kıyı | x −24…24, z −41…−46 | — | referans ön planı; kıyı yolu z −41 → −47'ye kayar |
| Kıyı yolları | x ±15/16, z −17…−47 (mevcut, uzar) 2,5 m → 3,0 m | — | |

##### 2.6 Mevcut öğeler: kalır / taşınır / kalkar

| Öğe | Karar |
|---|---|
| Merkez avlu (r 14), oyuncu başlangıcı, 4 planter adası | **kalır** aynen |
| Saray salonu z 34…44 (11–12 modül) | **taşınır** z 38…50; +6 m, arka duvar + 6 kapı, iç bölme, dais |
| Batı koridor x −36…−30, z −12…12 (6 modül) | **kalır + uzar** z −16…20, 2. doğu kapısı |
| open_corridor ×2 | **taşınır** K2'ye |
| Köprü 5 segment, havuz 3 basen, koi | **kalır** aynen |
| Sakura 18, fener 13, bambu 236 | **kalır** + ekleme (34 / 30 / ≈ 380) |
| Halka yolları (25–40 m) | **ölçeklenir** 30–48 m; çakıl kıyı ve doğu dış patikası eklenir |
| Pagoda spur (GB) | yol **kalır**, ucuna R4 eklenir |
| 12 spawn adı | adlar **korunur**, konum değişir (eşleme §3.4); +6 yeni |
| 12 silah noktası | **14**'e çıkar (§3.6) |

##### 2.7 P-05 (AP-023) için yeni aile ihtiyacı
Açık yanlı kairō modülü (4 m bay, direk + çatı) · kapalı kairō modülü (mevcut +3) · avlu duvarı 4 m modül + köşe + kapı (2,4 m yüksek) · kapı evi · köşk 10 × 8 (shoji, veranda) · ambar kura 10 × 8 · çay evi 8 × 8 · azumaya · salon iç shoji bölmesi · arka duvar/kapı modülü · taş atlama adası · akçaağaç (2 varyant) · çam (2 varyant) · kaya grubu (3) · alçak taş duvar 0,9 m · kuyu/kurutma rafı zaten var. Toplam ≈ 16 yeni aile; hedef örnek ≤ 4 900.

---

#### 3 · Savaş akışı kuralları

##### 3.1 Genişlikler
- Kapalı koridor iç ≥ 4,5 m (K1); açık yanlı kairō iç 4,0 m (K5 3,5 m), direkler 3 m aralık, r 0,2.
- Bahçe patikaları 3,0–3,5 m; bambu şeritleri 3,0 m; ana radyaller 6–8 m; ön meydan 32 m.
- Kapılar: genişlik ≥ 2,2 m (2 zombi yan yana; ajan r 0,5), yükseklik ≥ 2,4 m; ana salon ön kapıları 3 m.
- Tavanlar: salon 6,5 · kapı evi 5,0 · ambar 4,0 · köşk 3,8 · çay evi 3,6 · kairō 3,4 · K5/K6/azumaya 3,2 (TP boom için ≥ 3,2 şart).

##### 3.2 Döngü ve çıkmaz
- Çıkmaz 0: her yol parçasının iki ucu başka bir parçayla paylaşılır (ValidateConnectivity sürer).
- Her kapalı oda ≥ 2 çıkış; çıkışlar ≥ 6 m ayrık, aynı duvarda değil (R1 6, R2 3, R3 2, R4 2, R6 2+2).
- Koridorlarda ardışık açıklıklar ≤ 9 m (K1: −16, −7, 0, 7, 20).
- Oda derinliği ≤ 12 m: tepeden çatı soldurma tüm odayı tek pencerede gösterir.

##### 3.3 Darboğaz politikası
Tanımlı darboğaz = iç genişlik ≤ 2,5 m. Sayı **8**: C1 taş atlama (2,0 m, 2 × 6 m koşu) · C2 ambar 2 kapı (2,2) · C3 çay evi 2 kapı (2,2) · C4 salon yan kapılar (2,4) · C5 salon iç bölme uçları (2,4) · C6 köşk kapıları (2,4) · C7 kapı evi yan kapıları (2,0) · C8 köprü (3,0 m yumuşak, 21,5 m koşu, tek istisna). Kural: her darboğazın 15 m yol içinde ≥ 3,5 m alternatifi olur; darboğaz koşusu ≤ 8 m (köprü hariç); iki darboğaz arka arkaya gelmez.

##### 3.4 Spawn kapıları (18 = 12 kenar + 6 orta; hepsi başlangıçtan ≥ 34 m yol)

| # | Ad (eski ad → yeni) | x, z | Başlangıca yol | Not |
|---|---|---|---|---|
| 1 | N_PalaceGateWest → N_RearWest | −20, 53 | 68 m | K6 batı ucu |
| 2 | N_PalaceGateEast → N_RearEast | 20, 53 | 75 m | K6 doğu ucu |
| 3 | NE_StonePathCorner → NE_Corner | 44, 44 | 85 m | çam köşesi |
| 4 | E_SakuraGroveNorth → E_GardenNorth | 52, 16 | 77 m | K4 ucu |
| 5 | E_SakuraGroveSouth → E_GardenSouth | 52, −14 | 76 m | |
| 6 | SE_PondApproach → SE_Shore | 42, −42 | 81 m | |
| 7 | S_BridgeSouthEnd → S_BridgeSouth | 4, −52 | 67 m | köprü/K5 ucu |
| 8 | S_ShorePathWest → S_ShoreWest | −16, −52 | 66 m | |
| 9 | SW_BambooEntry → SW_TeaHouse | −44, −44 | 72 m | |
| 10 | W_BambooCorridorSouth → W_BambooSouth | −52, −18 | 60 m | |
| 11 | W_BambooCorridorNorth → W_BambooNorth | −52, 18 | 60 m | |
| 12 | NW_ServiceGate | −46, 46 | 75 m | |
| 13 | I_WestKairoSouth (yeni) | −33, −16 | 38 m | K1 güney ağzı arkası |
| 14 | I_CloisterWest (yeni) | −22, 14 | set 3 | K3 batı kairō içi (C-00 30.09 set 3, 92232235) |
| 15 | I_R7SouthWest (eski I_ShoreEast) | 24, −8 | set 3 | R7 avlusunun güneybatısı, karesansui kuzeyi (set 3; ad konumdan, M-27) |
| 16 | I_R7NorthWest (eski I_GreatSakuraSouth) | 24, 14 | set 3 | R7 avlusunun kuzeybatısı, I_CloisterEast'e 10,2 m (set 3; ad konumdan, M-27) |
| 17 | I_ServiceYardSouth (yeni) | −36, 29 | 51 m | hizmet avlusu güney çiti |
| 18 | I_HallRear (yeni) | 0, 49 | 62 m | salon arka kapısı içi |

Kurallar: kenar kapılar NavMesh sınırının ≥ 2 m içinde; orta kapılar görüş engelinin (duvar/çit/çatı) arkasında, çıkış patikası ≥ 3 m; hiçbir kapı tek çıkışlı hacimde değil; kapı ile silah noktası ≥ 12 m; kapı çevresinde 6 m siper yok. Tier kuralı (S-03): Elite/Ultra yalnız oyuncuya yol ≤ 55 m kapılardan → Executioner ilk temas ≤ 35 s.

##### 3.5 Kiting halkaları (bot kullanabilir, iç genişlik ≥ 3 m)

| Halka | Uzunluk | Halka | Uzunluk |
|---|---|---|---|
| L1 Merkez planter halkası | ≈ 75 m | L7 Havuz doğu lobu (köprü + doğu kıyı + güney yol) | ≈ 64 m |
| L2 Ön avlu kairō halkası | ≈ 104 m | L8 K1 + bambu bypass | ≈ 92 m (iç mini 30 m) |
| L3 Salon iç nef halkaları ×2 | ≈ 40 m | L9 Hizmet avlusu (ambar çevresi) | ≈ 44 m |
| L4 Saray dış halkası (flank + K6) | ≈ 124 m | L10 Çay evi spur | ≈ 36 m |
| L5 Büyük sakura ada/duvar | 40 / 60 m | L11 Köşk + K4 | ≈ 50 m |
| L6 Havuz batı lobu (taş atlama + köprü) | ≈ 60 m | L12 Karesansui kaya grupları | ≈ 48 m |

12 halka ≥ 22 m, 8'i ≥ 40 m; her bölgede ≥ 2 halka.

##### 3.6 Silah noktaları (14)
(0,30) ön avlu · (0,47) salon dais · (35,28) köşk · (36,1) büyük sakura · (20,−22) karesansui · (4,−16) köprü kuzey · (−32,−36) çay evi · (−33,2) K1 orta · (−38,34) hizmet avlusu · (−45,0) bambu şeridi · (−10,−44) K5 · (33,−33) azumaya · (10,−8) merkez GD · (30,20) K4 başı. Kural: her bölge ≥ 2, kapalı alan içi ≥ 4 (oda girmeye teşvik), kapıya ≥ 12 m.

##### 3.7 Siper yoğunluğu
Siper = h ≥ 1,1 m, w ≥ 1,5 m, NavMesh'i kesen nesne (fener, kaya, planter, fıçı yığını, alçak duvar parçası). Açık alan 1 / 60 m² (merkez, ön meydan, ön avlu); bahçe/avlu 1 / 25 m²; koridor içi 0 (sadece direk); kapı çevresi 6 m boş; hiçbir siper 3 m'den dar geçit yaratmaz.

##### 3.8 Görüş hatları
- Tepeden: pencere 30 × 23 m, önde 15 m. Kural: oyuncunun 15 m önünde her zaman ≥ 1 çıkış görünür; oyuncunun 7,5 m güneyindeki duvar/çatı soldurulur (S-01 occluder grubu); duvar yüksekliği ön avluda 2,4 m, R7'de 1,8 m (tepeden okunur).
- TP/FP: ≥ 6 uzun görüş hattı ≥ 25 m: K1 (36 m), K6 (44 m), doğu radyali (13→45, 32 m), ön meydan→salon (12→38, 26 m), güney radyal→köprü (−13→−40, 27 m), bambu dış şeridi (52 m). Sis 45–70 m; ufuk plakaları ±55'te r 260 ile değişmez.

---

#### 4 · Akustik alan sınıfları (S-08, WP-072)

| Alan | Sınıf | Hacim (x/z köşeler) | Tavan | Taban m² |
|---|---|---|---|---|
| Merkez meydan | açık avlu | daire r 14 @ (0,0) | — | 615 |
| Ön meydan | açık avlu | (−16,12)(16,12)(16,20)(−16,20) | — | 256 |
| Ön avlu (duvarlı) | açık avlu | (−20,24)(20,24)(20,36)(−20,36) | — | 480 |
| Hizmet avlusu | açık avlu (çitli) | (−48,30)(−28,30)(−28,48)(−48,48) | — | 360 |
| Büyük sakura avlusu | açık avlu (duvarlı) | (27,−8)(45,−8)(45,10)(27,10) | — | 324 |
| Karesansui | açık avlu | (12,−30)(28,−30)(28,−16)(12,−16) | — | 224 |
| Çakıl kıyı + havuz kıyısı | açık avlu (su) | (−24,−46)(24,−46)(24,−17)(−24,−17) | — | 1 390 |
| Sakura/akçaağaç bahçesi | açık avlu (orman) | (26,−40)(54,−40)(54,20)(26,20) | — | 1 680 |
| Bambu korusu | açık avlu (orman, yoğun) | (−54,−30)(−38,−30)(−38,30)(−54,30) | — | 960 |
| K1 Batı kairō | koridor (kapalı) | (−36,−16)(−30,−16)(−30,20)(−36,20) | 3,4 | 216 |
| K2 KB bağlantı | koridor (açık yanlı) | (−35,18)(−31,22)(−24,30)(−28,32) | 3,4 | 48 |
| K3 Batı kairō | koridor (açık yanlı) | (−24,22)(−20,22)(−20,36)(−24,36) | 3,4 | 56 |
| K3 Doğu kairō | koridor (açık yanlı) | (20,22)(24,22)(24,36)(20,36) | 3,4 | 56 |
| K4 Doğu kairō | koridor (açık yanlı) | (26,30)→(30,22)→(44,22)→(50,14), yarı genişlik 2 | 3,4 | 132 |
| K5 Güney kıyı çardağı | koridor (açık yanlı) | (−22,−48)(0,−48)(0,−44)(−22,−44) | 3,2 | 77 |
| K6 Arka kairō | koridor (açık yanlı) | (−22,50)(22,50)(22,54)(−22,54) | 3,2 | 176 |
| Köprü | koridor (açık, su üstü) | (2.5,−18)(5.5,−18)(5.5,−39.5)(2.5,−39.5) | — | 65 |
| R5 Azumaya | koridor (açık yanlı) | (30,−36)(36,−36)(36,−30)(30,−30) | 3,2 | 36 |
| R6 Kapı evi | koridor (kapalı geçit) | (−4,18)(4,18)(4,23)(−4,23) | 5,0 | 40 |
| R1 Ana salon | kapalı salon (büyük, ahşap) | (−18,38)(18,38)(18,50)(−18,50) | 6,5 | 432 |
| R2 Doğu köşk | kapalı salon (küçük, shoji) | (30,24)(40,24)(40,32)(30,32) | 3,8 | 80 |
| R3 Ambar | kapalı salon (küçük, sıva/taş) | (−44,36)(−34,36)(−34,44)(−44,44) | 4,0 | 80 |
| R4 GB çay evi | kapalı salon (küçük, ahşap) | (−36,−40)(−28,−40)(−28,−32)(−36,−32) | 3,6 | 64 |

Geçiş bantları: kapı eşiğinden ±1,5 m harman; veranda (z 36…38) ön avlu ↔ salon arası yarı-açık ara katman.

---

#### 5 · S-03 (WP-076) ve S-01 (WP-070) devir

**S-03 — AI v3 / spawn / NavMesh / tehdit rotaları**
- D0 (24.09): bu belge; kapı tablosu §3.4, halka listesi §3.5, oda-çıkış grafı §2.
- D+1 (25.09): S-05'ten `spawn-gates-v6.json` (18 kapı, ad eşlemesi eski→yeni; mevcut 12 ad korunur ki test/konfig kırılmasın) + `regions-v6` poligonları: merkez r 14; kuzey x −28…28 / z 18…55; doğu x 26…55 / z −40…20 + KD çam köşesi; güney x −30…30 / z −55…−14; batı x −55…−26 / z −46…30 + hizmet avlusu (x −55…−28, z 30…55). Halka yarıçapları 30–48 m.
- İhtiyaç: (a) spawn bandı yeniden ayarı önerisi: tercih 18–35 → **22–45 m**, MaxUsefulDistance 62 → **75 m**, MinSpawnDistance 12 kalır; (b) TP/FP için "ekranda" testi mesafe yerine frustum + LOS raycast (duvar arkası orta kapılar 20 m içinde de geçerli) — S-01 CameraMode API'sine bağlı; (c) tier mesafe kapısı (Elite/Ultra ≤ 55 m yol); (d) tehdit rotaları = 12 halka düğüm listesi + 8 darboğaz; (e) NavMesh: tek parça, halka ±55 (NavMesh 54,5), FallGuard, adacık bağlayıcı; yükseltilmiş zeminler 0,35 (veranda/salon) ve dais +0,30 → tırmanma 0,30 kuralı korunur, off-mesh link yok; kapılar NavMesh'te ≥ 2,2 m açık; (f) bot oda kuralı: 2 çıkışlı oda = halka düğümü; köprü yasağı: iki iniş 15 m içinde ≥ 6 zombi ise.
- D+3 (27.09): greybox sahne (collider + NavMesh bake) → S-03 spawn simülasyonu (2 m ızgara) + S-10 bot soak.
- Riskler: spawn ad değişikliği testleri kırar (adlar korunuyor); kapalı alanlarda bot takılması; köprü darboğazının 21,5 m koşusu.

**S-01 — Kamera modları**
- D0: occluder listesi: 7 çatı grubu (R1, R2, R3, R4, R6, K1, K3×2 tek grup) + K4/K5/K6 hafif çatılar + ön avlu güney duvarı + R7 duvarları; soldurma tetiği: oyuncunun 7,5 m güneyindeki (kamera tarafı) her duvar/çatı.
- İhtiyaç: TP boom ≤ 3,5 m ve çarpışma katmanı "Wall" (duvar/direk); koridor iç genişliği 4,0–4,5 m, kapı yüksekliği ≥ 2,4 m, tavan ≥ 3,2 m sözleşmesi; FP 1,6 m göz için iç mekân LOD/detay mesafesi 0–12 m (oda derinliği); ufuk plakası r 260 değişmez; sis 45–70 m; TP/FP'de gök görünür → AP-023 bulut plakaları.
- D+3: greybox'ta TP/FP kamera çarpışma turu (K1, R1, R6, köprü).
- Riskler: TP boom'un kapalı kairō'da geri çekilmesi (4,5 m iç); tepeden soldurma ile TP occlusion'ın aynı bileşende çakışması.

---

#### 6 · Faz B kabul ölçütleri (ölçülebilir)

| # | Ölçüt | Ölçüm | Eşik |
|---|---|---|---|
| B1 | NavMesh tek parça | ada sayısı (≤ 2 m² parçalar hariç); NavMesh sınırı | 1 ada; sınır 54,5 ± 0,2 m |
| B2 | Yürüme testi | başlangıç → 18 kapı NavMesh yolu; yol/kuş uçuşu | 18/18 yol var; oran ≤ 1,45 |
| B3 | Spawn mesafesi | her kapı → başlangıç yolu | ≥ 30 m; en yakın 34 ± 3 m |
| B4 | Kapı kapsaması | 2 m ızgara, her yürünebilir noktadan yol bandı | ≥ 4 kapı 22–45 m, ≥ 8 kapı ≤ 75 m; ihlal ≤ %3 nokta |
| B5 | İlk temas | S-03 sim, 12 tur, medyan (Standard) / Runner / Executioner max | ≤ 20 s / ≤ 12 s / ≤ 35 s |
| B6 | Halka sayısı | graf analizi, iç genişlik ≥ 3 m | ≥ 12 halka ≥ 22 m; ≥ 8 halka ≥ 40 m |
| B7 | Çıkmaz | parça uç derecesi; oda çıkış sayısı ve ayrıklığı | 0 çıkmaz; her oda ≥ 2 çıkış ≥ 6 m ayrık |
| B8 | Darboğaz | ≤ 2,5 m geçit sayısı, koşu uzunluğu, alternatif | ≤ 8; koşu ≤ 8 m (köprü hariç); alternatif ≤ 15 m |
| B9 | Bot kararlılığı | S-10 soak, 40 aktif, tohum 26 + 61 | 12/12 × 2; oda içi takılma > 5 s = 0 |
| B10 | Perf tepeden | DEC-088 ölçümü | ≤ 8,0 ms (veya kullanıcı kararı ≤ 16,6 / p99 20) |
| B11 | Perf TP/FP | 4 kadraj (K1 içi, R1 içi, merkez→kuzey, köprü) | kademe başına ms raporu; Yüksek ≤ 16,6 ms |
| B12 | Örnek/renderer | placement manifesti; TP görünür renderer | ≤ 4 900 örnek; ≤ 900 görünür |
| B13 | Kamera | TP boom çarpışma turu; kapı/tavan ölçüleri | koridor/oda içinde clipping 0; kapı ≥ 2,2 × 2,4; tavan ≥ 3,2 |
| B14 | Görüş hattı | TP/FP ≥ 25 m hat sayısı; tepeden 15 m önde çıkış | ≥ 6; ihlal 0 (2 m ızgara) |
| B15 | Siper yoğunluğu | bölge başına siper / m²; kapı 6 m çevresi | açık 1/60, bahçe 1/25 (±%20); kapı çevresi 0 |
| B16 | Akustik hacimler | sahnedeki trigger hacimleri ↔ §4 tablosu | 23/23, köşe sapması ≤ 1 m |
| B17 | Kimlik | 5 bölge poligonu RingRegionMap'te; oyuncu başlangıcı | 5/5; (−7,5, 0, 0) değişmez |

---

#### 7 · Açık kararlar ve riskler
1. 🔴 60 fps kararı: TP/FP +1,0–1,5 ms tahmini 8,0 ms'ye sığmaz; karar gelmezse Performans kademesi zorunlu (yeşillik −40 %, çam/akçaağaç LOD1 başlangıç).
2. AP-023 70 kr: ≈ 16 yeni aile; kredi yetmezse öncelik sırası kairō açık modül → avlu duvarı/kapı → köşk → ambar → çay evi → azumaya (sonuncular Blender kit-bash).
3. Köprü 21,5 m tek darboğaz istisnası; bot ölçümünde sorun çıkarsa taş atlama geçidi 2,0 → 3,0 m'ye çıkarılır (darboğaz sayısı 7'ye iner).
4. Mevcut 12 spawn adı korunuyor; S-03 eşleme tablosunu config'de tek yerden değiştirir (O5: sabit kod 0).
5. Yedek ±50 tetikleyicileri §1.3; tetiklenirse bu belge yalnız kenar bandı koordinatlarında revize edilir, §2–§4 yapı listesi aynı kalır.

