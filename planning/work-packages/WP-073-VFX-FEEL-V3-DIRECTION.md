# WP-073 · VFX & Game Feel v3 — Yön belgesi (Faz A)

Sahip S-07 · 2026-09-24 · Fable 5.1 konseyi: art-director (A1–A3), animation-specialist (A4); derleme ve Faz B planı S-07 (Opus 5.5).
Kaynaklar: `WP-073-VFX-FEEL-V3.md` (paket), M4-R1 §0/§4, DEC-092 (60 fps; Yüksek ort ≤ 16,6 ms / p99 ≤ 20 ms), DEC-093 (release ≤ 5 GB), DEC-094 (iki özgün Special: Raiju zincir yıldırım, Namekuji kaygan jel), WP-069 DIRECTION (7cb81ff; §2 art-director bağlayıcı taban, §5 ADR-069-1 olay yüzeyi), WP-070 (kamera modları; B1/B2 main'de), WP-071 A (olay sözleşmesi v3), mevcut durum envanteri `wp-019r2/evidence/wp073/current-state-inventory.md`.
Sayı çelişkisinde görsel/his tarafında bu belge WP-069 §2'yi günceller ("Δ" işaretli); oyun sayıları (hız, hasar, RPM, recoil deseni) S-02'nindir.

## Arayüzler (tüketilen / yayımlanan)
- **Tüketilen:** S-02 `ProjectileSystem` (ADR-069-1: `LiveCount`/`Get(i)`, `ProjectileEvents.Spawned/Impact/Expired`, `WeaponEvents.FireStarted/FireStopped`, `SpecialUsageEvents`), S-01 `ICameraReactionSink` (`AddRecoil/AddKick/SetBob/FovPunch`, mod olayları, FP görüntü modeli katmanı, `ViewmodelToWorld`, dünya namlu noktası), S-06 anim olayları (`eject`, `bolt_back`, adım fazı), S-05 `_DD_ShaftMask` + WP-075 kademe sözleşmesi, S-09 yönlü hasar UI.
- **Yayımlanan:** FeelRuntime `CameraModesFeel` (değerler S-07; uygulayıcı S-01), `VfxTier` (Sinematik/Yüksek/Performans; `UiSettings.Quality` ile), kademe başına ms raporu.

## Faz B dilimleri (her biri ayrı commit + C-00 teslimi; değişen süit; üç kamera modunda kare + Fable AD)
- **B1 · Omurga + ateş:** `VfxTier`, `DD/VFX Ribbon` + `TracerRibbonRenderer` (S-02 B1 gelene dek mevcut hitscan izine bağlı adaptör), `MuzzleSystem` sınıf profilleri + süreklilik modeli + FP katman devri, `CasingSystem` (havuz 48, LMG link).
- **B2 · Darbe:** yüzey × kalibre matrisi, `DecalPool` (4 atlas), gore v3, sekme; ıslak dünya kuralı.
- **B3 · Launcher:** roket mesh/egzoz/duman izi, `ExplosionDirector` 8 katman, ışık, mesafe sönümlü sarsıntı (S-02 B3 ile).
- **B4 · Juice v3:** `CameraModesFeel` FeelConfig bölümü + S-01 sink bağlaması, hit-stop tablosu, öldürme beat'i, yönlü hasar, erişilebilirlik (RM kamerada, Konfor ön ayarı).
- **B5 · Atmosfer v3:** 6 alan, curl benzeri Noise + rüzgâr, `DD/VFX Atmo` huzme parıltısı, göz hizası yakın alanı.
- **B6 · Special VFX:** Raiju (`ArcSystem`) + Namekuji (`GelSystem`, `DD/VFX Puddle`); eski Kagura/Ofuda/Tide VFX'inin S-02 B4 ile birlikte kaldırılması.
Perf: DEC-092 içinde; VFX toplamı Yüksek ≤ 3,0 ms (§5), 0 B/kare (WP-059D burst/kontrol kalıbı).

---

Kaynaklar: M4-R1 §0 (N1/N8/N10/N16/N18), §4.7–4.8, DEC-092 (60 fps: Yüksek ort ≤ 16,6 ms / p99 ≤ 20 ms, 1080p, referans Mac) · WP-069 DIRECTION §1 (sayılar), **§2 art-director (K1–K5, palet 3.1, namlu §4, kovan §5, darbe §6, launcher §7, Special §8, kademe §9 — bağlayıcı taban)**, §5 ADR-069-1 (olay yüzeyi) · WP-070 (FP görüntü modeli katmanı) · `current-state-inventory.md` · hedef görseller `DD_StyleRef_DEC076_{overview,gameplay}_2k.png` (Islak Arduvaz: koyu gri-mavi taş #3E434A, sis #D0D7DE, sakura #F2C4D0, yosun-yeşil #6E7F55; R1: parçalı bulut + huzme + hafif bölgesel sis + daha canlı renk).
Öncelik: sayı çelişkisinde **bu belge** WP-069 §2'yi günceller (aynı rol, daha yeni); farklar "Δ" ile işaretli. Oyun sayıları (hız, hasar, RPM) S-02'nindir, burada yalnız okunur.
Sert kısıtlar: VFX Graph / Shader Graph yok; her şey kod sürümlü havuzlu mesh/quad/LineRenderer/ParticleSystem + `DD/VFX Flipbook` ailesi + yeni HLSL; **0 B/kare**; her efekt kademeli (§5); bu pakete Higgsfield kredisi yok (doku = prosedürel / Blender / AP-010 · AP-013C yeniden kullanım; §6 istek listesi); dünya VFX'i dünya kamerasında, FP'de namlu flaşı + izin ilk 1,2 m'si + FP kovanı görüntü modeli katmanında (S-01 ile anlaşıldı); gore yüksek ama okunur.

## 0 · Teknik omurga (Faz B'nin kuracağı sistemler — hepsi havuzlu, önceden boyutlu)
| Sistem | Ne çizer | Nasıl | Ölçek (Yüksek) |
|---|---|---|---|
| `TracerRibbonRenderer` | mermi izleri (çekirdek + parıltı) | `ProjectileSystem.LiveCount` + `Get(i)` kare başına okunur; `Graphics.DrawMeshInstanced` view-aligned quad, MPB dizileri önceden ayrılmış (≤ 1023/çağrı); yeni shader **`DD/VFX Ribbon`** (kamera yönlü şerit, baş/kuyruk rengi, üstel sönüm, ekran-uzayı min kalınlık K2 2 px, FP yakın-kamera HDR rampası) | 2 draw (ek + parıltı), ≤ 256 iz |
| `MuzzleSystem` | flaş sayfası + duman/pus + ısı + ışık | `FlipbookEffect` (mevcut `fb_muzzle` 8×8 + AP-010 varyantlar) + `SmokeHaze` tek ParticleSystem (namlu başına emitter değil) + `VfxLightPool` | flaş 6 canlı, ışık tavanı 4 |
| `CasingSystem` | kovan + LMG link | mevcut `CasingEffect` fizik modeli (bir zemin ışını, tek sekme) + `CasingLibrary` AP-013C; instanced çizim | havuz 48 |
| `ImpactSystem` | yüzey × kalibre darbe | `AtlasBurstEffect` (ParticleSystem texture-sheet; kıvılcım/kırıntı/serpinti/kıymık 4 atlas) + `FlipbookEffect` + `DecalPool` (quad, 4 yüzey atlası) | darbe 40 eşzamanlı, decal 128 |
| `ExplosionDirector` | 7 katmanlı patlama (§1.5) | sıralı zamanlayıcı, mevcut `fb_explosion` / `fb_shockwave` / `decal_scorch` + kor `AtlasBurst` + buhar puf | 2 eşzamanlı tam patlama, 3. "hafif" |
| `ArcSystem` | Raiju küre + zincir arkları + çarpılma | `LineRenderer` havuzu (8 ana + 24 dal), kodla prosedürel titreşim (§2.1), `DD/VFX Ribbon` additive; deri rim emissive MPB anahtarı | 8 ana ark |
| `GelSystem` | Namekuji damla/şap/birikinti/iz | damla mesh (vertex wobble, yeni **`DD/VFX Gel`**), birikinti **`DD/VFX Puddle`** (yeni HLSL: parlak animasyonlu yüzey, seviye/kuruma), kayma izi ribbon | birikinti 12 (GD tavanı) |
| `AtmosphereSystem` | bölgesel tane alanları + göz-hizası yakın alan | mevcut `WorldAtmosphere/AtmosphereDrift` genişler; ParticleSystem Noise modülü (curl benzeri) + WindZone; yeni **`DD/VFX Atmo`** (huzme parıltısı) | 1 800 canlı |
| `VfxTier` | kademe | `UiSettings.Quality` → Sinematik/Yüksek/Performans; her sistem `ApplyTier(tier)`; ProfilerRecorder ile ms raporu | §5 |
Ortak kural: "her atışta flaş, her isabette darbe, her ateşte ışık" üç kademede korunur; kesilen şey yoğunluk/ömür/havuz/gölge. Olay yüzeyi: `ProjectileEvents.Spawned/Impact/Expired/FlightStarted/FlightEnded`, `WeaponEvents.FireStarted/FireStopped`, anim `eject/bolt_back`, `SpecialUsageEvents`.

## 1 · A1 — Silah sınıfı başına profil
### 1.1 · Namlu (WP-069 §4 tabanı; buradaki eklemeler: kademe, FP katmanı, süreklilik modeli)
| Sınıf (yuva) | Flaş biçimi · boy×çap m | Sayfa/süre | Işık renk / yoğunluk / menzil / süre | Duman | Isı dalgası |
|---|---|---|---|---|---|
| AR (ar_1, ar_2) | 4 loblu yıldız + koni · 0,55×0,35 (ar_2 %10 küçük, daha beyaz) | 6 kare @60 = 0,10 s + 0,3 s tel-kor | #FFC28A / 6 / 4 m / 2 kare + 80 ms sönüm | puf Ø 0,30→0,60, 0,8 s, α 0,35→0, +0,4 m/s | namlu ucu 0,25 m haze kartı, yalnız Sinematik |
| SMG (smg_1, smg_2) | yuvarlak kısa çiçek · 0,35×0,30 (smg_2 %15 büyük, kısa) | 5 kare = 0,08 s | #FFC28A / 5 / 4 m / aynı | Ø 0,25→0,45, 0,6 s | — |
| Shotgun (sg_1, sg_2) | çan: geniş çiçek + **duman halkası** (vortex quad, 0,4→0,9 m, 1,0 s) · 0,50×0,60; sg_2 iki ağızdan 2 flaş 0,3 s arayla | 7 kare = 0,12 s | #FFB878 / 12 / 7 m / 3 kare + 120 ms | halka α 0,40 + pompa dumanı | — |
| LMG (lmg_1, lmg_2) | haç (kafes) + koni · 0,60×0,40; lmg_2 daha kısa ve sarı-beyaz | 6 kare, auto varyant | #FFC28A / 9 / 6 m / **sürekli model** | pus: 1 m yarıçapta birikir, K4 tavanı α 0,25, kesilince 1,2 s dağılır | Sinematik |
| Sniper (sr_1) | uzun mızrak + yan taç yaprakları · 1,20×0,40 | 8 kare = 0,14 s | #FFD0A0 / 18 / 10 m / 4 kare + 200 ms | Ø 0,45→1,00, 1,4 s, α 0,40 | Sinematik + Yüksek (tek atış, ucuz) |
| Launcher (rl_1) | ön ateş çiçeği 0,80×0,80 + **arka geri tepme konisi** 2,5 m (duman + su serpintisi #B8B0A6 α 0,45, 0,6 s) | 9 kare = 0,15 s | ön #FFB070 / 24 / 12 m; arka #FFA060 / 12 / 6 m | arka koni ıslak zemini koyulaştırır (decal 1,2 m, 4 s) | — |
Süreklilik modeli (auto ≥ 600 RPM): `fb_muzzle` parlak karelerinden (f0–7) her atış 4 karelik alt dizi, her 4. atış tam sayfa; ışık sönmez, 0,6–1,0 arasında 10 Hz titrer; erişilebilirlik ReduceFlashes → ışık 0,8 sabit + sayfa α ×0,4 (flaş ≤ 3/s hissi). Serpantin koru: her atışta 0,2 s #FF6A2A ×3 (FP'de okunur).
**FP katmanı:** flaş sayfası ve ışık `fp_socket`'ten görüntü modeli katmanında; ışık **dünya** kamerasında da geçerli (Light nesnesi tek, layer mask ikisini kapsar). Flaş FP'de 0,85 ölçek (viewmodel FOV farkı için) ve kameraya doğru 12° eğik (namlu ağzı okunur, beyaz leke değil). Duman/pus dünya kamerasında, namlu ucundan 0,3 m ileriden doğar (viewmodel içinden geçmez).

### 1.2 · Mermi / iz (`TracerRibbonRenderer`)
Formül **Δ**: L = clamp(v × 0,020 s, 1,2 m, 8,0 m); sr_1 = clamp(v × 0,025, —, 10 m). (WP-069'un 0,035 katsayısı 170 m/s üstünde her izi 6 m'ye sabitliyor, hız farkı okunmuyordu.) İz her kare `ProjectileState`'ten çizilir: baş = mermi konumu, kuyruk = konum − yön × min(L, katedilen). Baş boncuğu 0,20 m (heavy 0,30) tam yoğunluk, kuyruk üstel, son %40 α ≤ 0,15. **Parıltı (glint)** her mermide: 0,02 m soğuk-beyaz #E8EEF2 α 0,35 (Performans'ta kapalı). Ekran kalınlığı K2: w_px = max(w_m × px/m, 2 px).
| Yuva | Hız m/s → L m | Rol (renk 3.1) | Çekirdek w / parıltı w (m) | İz her N | FP yakın-kamera |
|---|---|---|---|---|---|
| ar_1 | 260 → 5,2 | Kor-light #FFF1DC ×5 → #FF9A4A ×2 | 0,035 / 0,10 | 3 | ilk 1,2 m viewmodel katmanı; α 0→1 rampası 0–0,4 m; HDR ×0,3 @ < 1,5 m → ×1 @ 3 m |
| ar_2 | 320 → 6,4 | Kor-medium ×6 → #FF8A3A ×2,5 | 0,045 / 0,12 | 2 (burst'ün 1. ve 3.) | aynı |
| smg_1 | 170 → 3,4 | Kor-light ×5 | 0,030 / 0,08 | 3 | aynı |
| smg_2 | 200 → 4,0 | Kor-light ×4 | 0,030 / 0,08 | 4 | aynı |
| sg_1 | 120 → pellet 0,5 m parıltı çubuğu ×2 (#FFD9B0), 6 m sonra söner | Pellet | 0,025 / — | 8/8 parıltı, iz 0 | çubuklar 0,6 m'den başlar (yüzde leke yok) |
| sg_2 | 130 → 8 parıltı + merkez pellet Kor-medium iz 2,6 m | Pellet + Kor-medium | 0,025 · 0,045 | merkez 1/1 | aynı |
| lmg_1 | 250 → 5,0 | Kor-medium ×6 | 0,045 / 0,12 | 4 | aynı |
| lmg_2 | 300 → 6,0 | Kor-heavy #FFF8F0 ×7 → #FF6E2E ×3 | 0,060 / 0,16 | 5 | aynı |
| sr_1 | 420 → 10 | Soğuk-sniper #F4FAFF ×8 → #9FC8E8 ×2 | 0,070 / 0,20 | 1 | + **buhar izi**: 0,05 m şerit (ribbon, 12 örnek noktası), 0,5 s (Sin 0,7 / Perf 0,3), α 0,35→0, rüzgârla 0,3 m/s; ADS'de belirgin; mermi ışığı #CFE6FF / 2 / 3 m |
| rl_1 | 24→46 | mesh (§1.5) | — | — | roket FP'de tüpten 0,12 m görünür, ateşte tam mesh dünya katmanına devredilir |
Katman devri: viewmodel iz segmenti [0; 1,2 m] `fp_socket`'ten; dünya izi mermi katedilen ≥ 1,2 m olunca başlar, [1,2; 1,6 m] α rampası; çift çizim yok (aynı kare iki katmanda toplam α ≤ 1). Tepeden: baş boncuğu ≥ 6 px nokta, parıltı 1 px'e inince α 0,5. Mermi mesh yok (roket/Special hariç): iz = mermi.

### 1.3 · Kovan (`CasingSystem`, AP-013C; WP-069 §5 malzeme: bakır taban #6B4A3A + balmumlu kâğıt #8C7A66)
| Sınıf | Tür · boy | Fırlatma yön / hız / dönüş | Olay | Yerde Sin / Yüksek / Perf (s) | Havuz |
|---|---|---|---|---|---|
| AR | rifle 0,055 | +X 35° yukarı 10° geri / 3,0 m/s / 12 dev/s | eject | 8 / 5 / 3 | 96 / 48 / 24 (ortak) |
| SMG | pistol 0,035 | +X 40° / 2,5 m/s / 15 dev/s | eject | 8 / 5 / 3 | |
| Shotgun | shell 0,065 Ø 0,020 (hurma kâğıt) | sg_1 +X 45° / 2,0 m/s / 6 dev/s; sg_2 kırmada 2 fişek arkaya 1,5 m/s | pompa / eject | 10 / 6 / 3 | |
| LMG | rifle + demir link 0,03 | kovan +X 30° / 3,5 m/s; link −Y serbest düşüş | eject | 8 / 5 / 3 | link ayrı 32 |
| Sniper | heavy 0,085 Ø 0,014 | bolt_back'te +X 25° / 1,8 m/s / 4 dev/s | bolt_back | 12 / 8 / 4 | |
| Launcher | yok | — | — | — | — |
Fizik: g 9,81, tek zemin ışını, tek sekme (restitution 0,3, Perf'te sekme yok), `CasingLanded{type, SurfaceKind, bounce}` sekmede fırlatılır (S-08). FP: kovan viewmodel katmanında doğar, kameradan ≥ 0,25 m (küçük itme), 0,25 s sonra dünya katmanına devredilir (ilk sekmeden önce). Islak taşta metalik 0,9 / smoothness 0,55 bakır parıltısı.

### 1.4 · Darbe matrisi (`ImpactSystem`; parçacık sayıları Yüksek kademe ×1,0; Sin ×1,3 tavan, Perf ×0,4)
Kalibre: light (smg_1/smg_2/pellet) ×0,7 · medium (ar, lmg_1) ×1,0 · heavy (lmg_2, sr_1) ×1,6 · explosive = §1.5. Islak dünya (K3): "toz" yok → su serpintisi #D9E1E6 α 0,5 0,15 s + koyu ıslak parça; kuru toz yalnız kapalı alan (WP-074 `dryInterior` bayrağı). Decal çapı light 0,06 / medium 0,09 / heavy 0,14 m, 12 s, α ≤ 0,85, +0,03 m ofset, 4 yüzey atlası (stone/wood/metal-armor/dirt-default), havuz 256 / 128 / 64, RecycleOldest. Darbe ışığı #FFE0B0 / 3 / 2 m / 2 kare: Metal/Armor + heavy (Yüksek: yalnız heavy).
| SurfaceKind | medium darbe (parçacık) | Decal | Sekme (Ricochet dalı) |
|---|---|---|---|
| Default | gri puf 0,3 m + 4 parça | delik 0,09 | kısa kor iz 0,6 m ×3 0,08 s |
| Stone (ıslak arduvaz) | serpinti 8 damla + 4 taş kırıntı (0,01–0,03 m, g) + 2 kıvılcım; heavy'de `fb_impact_stone` | koyu ıslak halkalı delik + ıslak leke 0,2 m 4 s | evet + 1 kıvılcım çizgisi |
| Wood (lake) | 8 kıymık (0,02–0,06 m, dönen quad) + 2 lake pulu + `fb_impact_wood` | açık damar delik 0,08 | yok |
| Foliage | 6 yaprak (AP-010 leaf) + 4 su damlası + 0,4 s salınım (`DD_Foliage_Wind` itki) | yok | yok |
| Water | sütun 0,4 m + halka 0,9 m (mevcut) + 6 damla | yok | yok |
| Metal | 10 kıvılcım #FFE0B0 ×4 (0,3 s, g, sekmeli) + darbe ışığı | çukur 0,06 normal-only | evet (kor iz + 3 kıvılcım) |
| Dirt (ıslak toprak) | 8 çamur damlası + koyu tokmak + `fb_impact_earth` | 0,10 koyu | yok |
| Paper (shōji) | yırtık 0,15 + 4 kâğıt parçası (0,4 s uçuş) | yırtık decal (atlas) | yok |
| Flesh (**gore yüksek**) | `splash_04..06` + 10 damlacık (GPU instanced) + **çıkış spreyi** (penetrasyonda: koni 0,8 m, 12 damla, arka yüzeye leke ≤ 0,5 m); heavy: `heavy_01..02` + 16 damla + yer izi; **kafa öldürme**: `fb_zombiedeath` + kırmızı sis pufu Ø 0,5 m α 0,5 0,4 s + 3 parça (§6 gib seti) | kan işareti (mevcut ≤ 128, ıslak→kuru 30 s) | yok |
| Armor (lake) | `fb_impact_armor` + 3 kıvılcım + 1 lake kıymığı; heavy ×1,6 + darbe ışığı + çınlama; kırılma: mevcut sayfa + parçalar + lake decal 8 s | lake çatlak 0,06 | evet (zırh sekmesi: kor iz ×3 + 6 kıvılcım) |
Gore okunurluk tavanı: eşzamanlı kırmızı sis ≤ 2, sprey damlası toplam ≤ 96 canlı, kan lekesi oyuncu silüetine ve HUD güvenli alanına düşmez (ekran-uzayı kan yok — yönlü hasar efekti A4'ün işi, kan değil vinyet). Explosive sütunu §1.5.

### 1.5 · Launcher rl_1 (`ExplosionDirector`; WP-069 §7 tabanı)
| Katman | Karar |
|---|---|
| Roket mesh | bōhiya ok-roket 0,55 m, ≤ 600 tri (AP-021 P-07 teslimi; gelene kadar Blender gri kutu §6); demir ogive #4A4C50, kâğıt gövde #8C7A66, 3 lake kanatçık; uçuşta yalpa 0,3°/0,1 s (S-02) mesh'e de uygulanır; 25 m sonra billboard (Yüksek) |
| Egzoz | koni 0,45 m × Ø 0,12 (8 karelik flipbook döngü, AP-010 `fb_muzzle` kor kareleri yeniden kullanım) #FFC070 ×6 + #FF7A3C ×2; kıvılcım damlası 6/s 0,3 s g; roket ışığı #FFB070 / 8 / 5 m (Sin/Yüksek), alçak uçuşta ıslak zemin parlar |
| Duman izi | tek ParticleSystem ribbon-benzeri puf dizisi 20 puf/s, Ø 0,15→0,90 m 2,5 s; #B8B0A6 (ilk 0,5 s alev aydınlatması) → #8E949A; α 0,45→0; kalma 3,5 / 2,0 / 1,0 s; rüzgâr vektörü `_DD_WindVector` + 0,3 m/s yükseliş |
| Patlama sırası (t) | 0 ms flaş beyaz-turuncu ×12 Ø 2 m 1 kare → 0 ms ateş topu `fb_explosion` 3,6 m 1,6 s → 0 ms şok halkası zemin 0,3 s (mevcut, Ø 1→7 m) → 16 ms kor çizgileri ×24 (0,8 s, g, ıslak zeminde sönerken 4 mini serpinti) → 33 ms toz/su sütunu Ø 1,5→3 m 1,2 s (serpinti + koyu parça) → 50 ms yüzey kalıntısı (Stone: 12 taş parçası 0,05–0,20 m; Wood: 6 kalas; Dirt: 10 kesek + krater decal 1,8 m; Water: kubbe 2,5 m + halka 4 m + 1,5 s damla yağmuru; Foliage: 30 yaprak) → 200 ms **buhar** tutamları ×6 α 0,25 1,2 s (Sinematik) → scorch 2,4 m / 12 s + ıslak koyu halka 3,0 m / 6 s |
| Işık | #FFB070 / 40 / 9 m; 0,08 s tepe → 0,35 s sönüm; Sinematik'te gölge açık |
| Gore (≤ 1,8 m'deki zombi) | 6 gib parçası (§6 seti, ≤ 300 tri, havuz 24, 6 s kalır, ıslak taşta kayar) + `heavy_02` + kan pufu; 1,8–5 m: ragdoll itkisi (WP-076) + `splash_06` |
| Kamera / his (A4 ile) | FP 0,25 s / TP 0,20 s / tepeden 0,15 s Rumble 7 Hz; genlik ≤ 3 m 0,15 m (GameFeelSettings tavanı) → 12 m'de 0,04 m doğrusal; rumble 0,8 / 100 ms; yakın (< 4 m) 0,3 s ekran kenarı su/çamur lekesi α 0,35 (ReduceFlashes'ta kapalı) |

## 2 · A2 — Special VFX (DEC-094; Look B içinde: rezervin sahibi Special'lardır — cyan/kehribar/magenta yalnız burada)
### 2.1 · Raiju (sw_arc, DG-2 benzeri) — retro-tech: kiraz lake, pirinç ≤ %25, fener camı, kumihimo bakır bobin
Palet: çekirdek #EAF6FF ×10 (200°) · dal #7FD3FF ×4 · hale #A08CFF ×1,5 · depolanan yük kehribar #FFC65A ×2 (HUD Gold400 ailesi = "dolu Special") · yer arkı #BFE9FF ×6.
| Öğe | Şekil / sayım / zamanlama |
|---|---|
| Şarj (0,25 s) | bobinlerde 1 → 6 Hz titreme rampası, hücreler kehribar → beyaz-cyan geçişi son 80 ms; ağız elektrotu çevresinde 3 kısa ark (0,15 m, 30 ms yenileme); ışık #7FD3FF 0→8 / 3 m |
| Namlu deşarjı | halka deşarj Ø 0,50 (3 elektrot arası 3 ark, 0,20 s) + 0,5 s art-titreme; ozon pusu α 0,15 Ø 0,4 m 0,6 s; ışık #7FD3FF / 20 / 9 m / 0,25 s |
| Küre mermisi (14 m/s, 3 s) | mesh: Ø 0,25 çekirdek küre (emissive ×10, `DD/VFX Gel` wobble ×0,3) + Ø 0,45 plazma kabuğu (fresnel additive, 4 Hz dalgalanma) + 4–6 tel uç (LineRenderer 0,6 m, 0,1 s ömür, 10 Hz yenileme); ışık #7FD3FF / 8 / 6 m; **ıslak zeminde yer arkı**: zemin ışını ≤ 2,5 m ise 0,5 m halkada 3 kısa ark / 0,1 s + zeminde parlak nokta; FP'de küre kameradan 0,6 m uzakta doğar, ilk 0,3 s HDR ×0,4 |
| Zincir arkı (hedef başına) | ana ark LineRenderer 16 nokta; her 33 ms yeniden örneklenir: orta noktalar normal yönde ±(0,08 × mesafe/6 m) m titreşim + 2 oktav; çekirdek 0,06 m ×8, hale 0,25 m ×1,5; segment başına 2–4 dal (Yüksek 3, Perf 2), dal uzunluğu ana ark'ın %25–40'ı, 0,02 m, 2 nokta, 40 ms ömür; sıçrama 0,12 s görünür (GD 0,08 s gecikme + 40 ms bindirme), hedefler arası 40 ms kademe; ark ışığı hedef başına yok — küre ışığı sıçrama noktasına 0,05 s'de atlar (tek Light, 8 hedefte 8 atlama) |
| Çarpılma (1,2 s titreme) | deri materyali MPB `_RimEmissive` cyan ×3 20 Hz kare-dalga titreme (0,5 s tam, 0,7 s sönen), gözler beyaz ×6; iç arklar 5 nokta (baş–omurga–leğen–2 el; `HitBone` transformları) 0,02 m, 60 ms yenileme; uç kıvılcım 12 (0,2 s); **baştan duman**: 0,6 s sonra kafa soketinden gri-mavi duman 4 puf/s 1,5 s α 0,3 + 2 kızıl kor; ayaklarda 0,4 m ark halkası (ıslak taş); sonra WP-071 ölüm başlangıcı (elektrik seğirmesi S-06) |
| Zemin yanığı | her çarpılan hedefin altında dallı yanık decal 0,6 m (Lichtenberg deseni, 3 varyant, kod-üretimli DLA, §6) #2A2420 α 0,6, 10 s; küre sönerken 0,3 s "cız": 0,4 m decal + 6 kıvılcım |
| Kendine çarpma (≤ 2,5 m) | oyuncu ekranı: 0,4 s cyan rim vinyet + `ArcSystem` 2 ark oyuncu gövdesine; FP'de kol rim ×2 |
| Reload cell_out/in | kaset kayarken hücreler kararır (×2 → ×0, is lekesi α 0,3), yeni kaset 0,4 s'de kehribara ısınır, bobin hum-parıltı senkron (S-08) |
Eşzamanlı ana ark tavanı 8 / 8 / 6; dal 24 / 24 / 12; deri rim ≤ 8 zombi.

### 2.2 · Namekuji (sw_gel, Sliquifier benzeri) — canlı parlak jel; sake şişesi dilinde demir bantlı cam hazne
Palet (🔴 WP-069 §10-1 sadakat önerisi geçerli): jel #FF5FD2 ×3 (313°) · kenar #B040FF ×1,5 · kuru #6E3560 mat · sıçrama damlası #FF8AE0 ×2. Look B içinde gerekçe: sahnenin soğuk gri-yeşiline tam tamamlayıcı, Foliage ile karışmaz, beacon rezervi zaten Special'ın.
| Öğe | Şekil / sayım / zamanlama |
|---|---|
| Nozul | ateşte damla perdesi 6 damlacık (0,02 m, 0,3 s, g) + Ø 0,30 sıçrama 0,15 s; ışık #FF5FD2 / 6 / 4 m / 0,20 s; hazne seviyesi −1/8 çalkalanarak (0,5 Hz, 0,4 s) |
| Damla mermisi (22 m/s, g 12) | 0,18 m uzamış damla mesh (`DD/VFX Gel`: vertex wobble 6 Hz genlik 0,015 m, hız yönünde uzama ×1,4, smoothness 0,95, emissive ×3, fresnel kenar mor); kuyrukta 4 damlacık (0,3 s, ribbon değil, ayrı küçük mesh); ışık #FF5FD2 / 3 / 3 m; FP'de 0,5 m'den doğar |
| Şap (gel_impact) | yüzey normaline 8 damla yelpazesi (0,4 m, 0,25 s, g) + normal-hizalı splat flipbook 0,6 m 0,2 s (AP-010 `fb_impact_earth` kor kareleri magenta MPB ile; yeni sayfa istenmez) + 0,5 m ıslak parlama; duvar/tavan: 3 akıntı ribbon 0,4 m 0,8 s aşağı, zeminde birikinti doğar |
| Birikinti (`DD/VFX Puddle`, r 1,6 m / ölüm 0,9 m, 25 s, tavan 12) | düzensiz splat decal 3 varyant + döndürme; shader: smoothness 0,92 (ıslak taş 0,60'dan parlak), emissive ×0,6 + 0,3 Hz nabız, kabarcık 3/s (UV-animasyonlu nokta katmanı, parçacık yok), yüzey normal kaydırma 2 katman 0,15 Hz; yaşam T: %0–10 yayılma 0,6 → tam (ease-out), %10–80 canlı, %80–100 **kuruma** (emissive → 0, renk → #6E3560, kenar çatlak maskesi), son 2 s solma; eşzamanlı 12 → 2 draw (instanced) |
| Kayma izleri | zombi: ayak altında 2 magenta sürtme ribbon 0,04 m 0,6 s + 4 damlacık; oyuncu: 2 paralel iz 1,0 s + ayaktan damlacık 3/s; FP'de alt kenar 0,3 s magenta parıltı α 0,25; TP/tepeden kamera hafif yatış (A4) |
| Zombide jel | isabet/kayma sonrası deri MPB `_GelMask` (üst gövde/bacak 2 bölge) 0,8 parlak magenta film 6 s → mat; damla 2/s 3 s (0,02 m, g, yere 0,1 m nokta) |
| Sıvılaşma (liquefy, Standard/Runner) | dissolve kenar magenta ×4 0,8 s (deri shader `_Dissolve` — DD_TierLit'e parametre, Sin/Yüksek), gövde 0,3 s'de çöker, geride 0,6 m jel lekesi birikintiye katılır; Perf: anlık ragdoll + splat |
| Reload canister_out/in | boş hazne mat film kalıntısı çıkar, dolu hazne çalkalanır (0,5 Hz ×2 genlik), seviye 0,3 s'de yükselir |

## 3 · A3 — Atmosfer taneleri (N10; `AtmosphereSystem`)
Hedef: overview görselindeki nemli, katmanlı hava — sisin içinde asılı taneler, sakura yağışı, su üstü buğu; R1'de huzmeye giren tane **parlar** (sinematik). Hareket: ParticleSystem **Noise** modülü (strength 0,35 m/s, frequency 0,18, 2 oktav, scroll 0,12, quality High → curl benzeri girdap) + External Forces (WindZone; `_DD_WindVector`, gust 0,4 / 9 s) — rüzgâr esince tüm alanlar aynı yöne "solur"; sakura ve yaprakta 3D dönüş (tumble 0,6 dev/s) + hafif salınım (sin 1,2 Hz, 0,04 m).
Shader **`DD/VFX Atmo`** (alpha-blend, soft particle 0,3 m, kamera içinde 0,35 m'de α→0): vertex'te `MainLightRealtimeShadow` (gölgede sönük) × huzme maskesi `_DD_ShaftMask` (S-05 WP-075 global param; yoksa 1) × ileri saçılma pow(saturate(dot(viewDir, −sunDir)), 8) × 4 → **huzmede parıltı**: taban α 0,35 + aydınlıkta ×(1 + saçılma); tane başına faz (custom vertex stream) ile 3–7 Hz kıpırtı; boyut ekran tabanı ≥ 1,5 px, tavan 24 px (tepeden 28 px/m'de 0,03 m tane 0,84 px → taban ile görünür).
| Bölge (WP-074 etiketi) | Tür · boyut m · renk | Yüksek canlı | Sin / Perf | Hareket |
|---|---|---|---|---|
| Avlu (Courtyard) | toz-polen 0,02–0,05 · #E6DCC8 α 0,30 (huzmede ×2) | 500 | 800 / 180 | yükseliş 0,05 m/s, noise ×1,0 |
| Doğu bahçe / sakura | taç yaprağı 0,08–0,18 · #F2C4D0 → #E8A9BF (yaş: koyu uç) | 220 | 360 / 90 | düşüş 0,35 m/s, tumble, rüzgâr ×1,2, yere değince 6 s kalır (mevcut petal decal yok → parçacık yerde durur) |
| Su (havuz / dere kıyısı 6 m) | buğu tutamı 0,6–1,2 m · #D0D7DE α 0,06–0,12 | 120 | 200 / 40 | 0,1 m/s sürüklenme, noise ×0,5, yüzeye 0,2–0,8 m |
| Bambu | yaprak kıymığı 0,10×0,03 · #9DB36A / #B8A86A | 160 | 260 / 60 | flutter: rotasyon 3D 1,5 dev/s + noise ×1,6 |
| Kapalı alan / koridor (`dryInterior`) | toz 0,01–0,03 · #C8BFB0 α 0,25, huzmede ×3 | 300 | 500 / 100 | çok yavaş (0,02 m/s), noise ×0,6 |
| Yakın alan (TP/FP, kamerayı izler, 10×5×10 m, 3 m önde) | bölgeye göre karışım + ince "mote" 0,01–0,02 (yalnız aydınlıkta görünür) | 500 | 900 / 130 | bölge değişiminde 1 s harman; tepeden modda kapalı |
Toplam canlı: **Sinematik ≈ 3 000 / Yüksek ≈ 1 800 / Performans ≈ 600** (bugün ≈ 440). Draw: bölge başına 1 ParticleSystem, tek materyal (`DD/VFX Atmo`), ≤ 6 draw; alanlar oyuncuya ≤ 60 m'de aktif (uzak alan ateşleme durur, canlılar söner). Tepeden: dünya alanları (mevcut 86×5×86 kutuları bölgelere bölünür) + boyut tabanı; TP/FP: yakın alan + dünya alanı. Renk-canlılık N19: taneler asla doygun değil (s ≤ 0,25); canlılık huzmedeki parıltıdan gelir.

## 4 · Okunurluk ("reads" — kaosta neyin okunur kalacağı)
1. **Oyuncu silüeti** hiçbir VFX ile örtülmez: namlu dumanı/pus oyuncu kapsülünden 0,3 m dışarıda doğar; patlama sütunu ve Special birikintisi oyuncu üst gövdesine ekran-uzayında binerse α ×0,5 (ImpactSystem/GelSystem "player guard" testi: kamera ışını). Kan ve gib oyuncu üstüne yapışmaz.
2. **Tier okunurluğu** (WP-036 renk kodu Special/Elite/Ultra) korunur: jel filmi ve cyan rim tier işaretini (baş/omuz bandı) kapatmaz — maskeler alt gövde + kol; rim emissive ≤ 0,5 s tam yoğunluk.
3. **Hasar okunur**: her isabette darbe; kan sisi ≤ 2; ekran-uzayı kan yok.
4. **Ateş yönü okunur**: iz her N'de ama parıltı her mermide; auto silahta ışık sürekli; tepeden baş boncuğu ≥ 6 px.
5. **Tehlike okunur**: birikinti kenarı her zaman görünür (kuruma evresinde bile kenar çatlağı); roket dumanı ve küre ışığı ıslak zeminde parlar.
6. **Atmosfer örtmez**: tane α ≤ 0,35 (buğu ≤ 0,12), kamera 0,35 m içinde α 0; HUD güvenli alanına parçacık boyutu ≤ 24 px.
7. **Flaş güvenliği**: ReduceFlashes → flaş sayfası ×0,4, ışık sürekli model, patlama flaşı 1 kare → 0; sarsıntı kaydırıcısı ve ReducedMotion A4'te korunur.

## 5 · Kalite kademeleri ve ms bütçesi (DEC-092; tam yük: 40 zombi + 256 mermi + LMG sürekli + 1 patlama + 1 Special + atmosfer)
Toplam VFX (GPU+CPU, 1080p, referans Mac): **Sinematik ≤ 5,5 ms · Yüksek ≤ 3,0 ms · Performans ≤ 1,2 ms.** Yüksek dökümü: arsenal (iz+namlu+kovan+darbe+decal+patlama) 1,5 · Special 0,5 · atmosfer 0,6 · kan/gib 0,2 · ışık havuzu 0,2 = 3,0 (WP-069 §9'daki 2,2 ms "arsenal + Special" payı korunur; atmosfer üstüne eklenir). Aşan öğe bir alt kademe değerine iner, kimlik kesilmez. S-07 her satırı ProfilerRecorder ile ölçer, `evidence/wp073/tier-ms.md`'ye yazar.
| Öğe | Sinematik | Yüksek (varsayılan) | Performans | Yüksek ms hedefi |
|---|---|---|---|---|
| İz (ribbon) | 256, bloom tam, parıltı açık | 256, aynı | 256, bloom ×0,5, parıltı kapalı | 0,25 |
| Namlu flaş / duman / ısı | her atış + gölge; 0,8 s + pus; haze açık (opaque texture yalnız Sin) | her atış gölgesiz; 0,5 s + pus; haze kapalı | her 2. atış ışık; 0,25 s, pus yok | 0,20 |
| VFX ışık havuzu (eşzamanlı) | 8 | 4 | 2 | 0,20 |
| Kovan | 96, 8–12 s, sekme | 48, 5–8 s, sekme | 24, 3–4 s, sekme yok | 0,05 |
| Darbe parçacık / darbe eşzamanlı | ×1,3 / 48 | ×1,0 / 40 | ×0,4 / 24 | 0,35 |
| Decal (delik/scorch/yanık) | 256, 12 s | 128, 12 s | 64, 8 s | 0,05 |
| Kan / gore (damla / sis / gib) | 128 / 2 / 24 | 96 / 2 / 24 | 48 / 1 / 8 | 0,20 |
| Patlama | 8 katman + buhar + gölge, 2 tam | 7 katman, 2 tam + 1 hafif | 5 katman, kor ×12, 1 tam | 0,30 (tepe kare 0,6) |
| Roket duman izi | 3,5 s | 2,0 s | 1,0 s | 0,05 |
| Raiju ark / dal / rim | 8 / 32 / rim + eklem + yer halkası | 8 / 24 / rim + eklem | 6 / 12 / yalnız rim | 0,25 |
| Namekuji birikinti / erime | 12, kabarcık + nabız + erime | 12, kabarcık + nabız | 12, düz emissive, erime yok | 0,25 |
| Sniper buhar izi | 0,7 s | 0,5 s | 0,3 s | 0,02 |
| Projectile mesh (roket/küre/damla) | her mesafede | ≤ 25 m, sonra billboard | ≤ 15 m | 0,03 |
| Atmosfer canlı / draw | 3 000 / 6 | 1 800 / 6 | 600 / 4 | 0,60 |
| Toplam | ≤ 5,5 | **≤ 3,0** | ≤ 1,2 | |
Ayırma: tüm sistemler önceden boyutlu havuz, MPB dizileri statik, NonAlloc sorgu; ayırma testi WP-059D kalıbı (tek karede N olay + kontrol + pozitif kontrol), 0 B hedef.
**Δ DEC-100 (kullanıcı 25.09; QUALITY-TIERS-V2 r2):** varsayılan kademe Sinematik, 30 fps (ort ≤ 33,3 / p99 ≤ 40 ms), Sinematik kapılı; VFX ≤ 5,5 ms. Sinematik sütunu cömertleşti: darbe parçacık ×1,6 / eşzamanlı 64, enkaz 800 + kıvılcım 400 canlı, decal 384 · 20 s, kovan 128, namlu dumanı ömrü ×1,3 (160 canlı); ışık tavanı 8 ve kan sisi ≤ 2 (okunurluk) aynen; atmosfer B5'te (DEC-102). Yüksek / Performans sütunları değişmedi.

## 6 · Varlık istekleri (bu pakette kredi yok; hepsi Blender / prosedürel / yeniden kullanım)
| # | Varlık | Kaynak | Boyut | Kullanım |
|---|---|---|---|---|
| 1 | Gib seti: 8 parça (kafatası parçası ×2, kaburga, kol/bacak kesiti ×3, et yığını ×2), ıslak et materyali | P-06 Blender (AP-007 zombi mesh kesiti), ≤ 300 tri/parça, 1K atlas | 1 FBX + 1K BaseColor/Normal | §1.4 kafa öldürme, §1.5 patlama |
| 2 | Bōhiya roket mesh + LOD | P-07 AP-021 (zaten kapsamda) | ≤ 600 tri, 1K | §1.5; gelene kadar Blender gri kutu |
| 3 | Kıymık / taş kırıntı / lake pulu / kâğıt parçası atlas (4×4, 512², normal-only + albedo) | kod-üretimli (Blender procedural render, kredi 0) | 512² ×2 | §1.4 darbe |
| 4 | Lichtenberg yanık decal 3 varyant | kod-üretimli DLA (Faz B'de C# çalışma zamanı değil, editör aracı) | 512² α | §2.1 zemin yanığı |
| 5 | Jel splat 3 varyant + kuruma çatlak maskesi | Blender procedural (Voronoi + noise), kredi 0 | 1024² RGBA (splat) + 512² (çatlak) | §2.2 birikinti |
| 6 | Atmosfer tane atlası (toz noktası, polen, sakura ×3 yaş varyant, bambu yaprak ×2, buğu) | AP-010 pollen/petal yeniden kullanım + Blender 2 yeni yaprak | 512² 4×2 | §3 |
| 7 | Buhar/duman puf 4×4 (mevcut smoke pufu yeniden kullanım; yeni istek yok) | AP-010 | — | §1.1, §1.5 |
Sanat oturumu (Higgsfield) gerektiren sayfa: **yok**.

## 7 · Açık kararlar (C-00 / kullanıcı)
1. 🔴 İz katsayısı Δ 0,020 s (WP-069 0,035 yerine) — S-02'ye bilgi; oyun etkisi yok.
2. 🔴 Jel rengi magenta sadakati (WP-069 §10-1 ile aynı soru).
3. Sinematik'te URP opaque texture (ısı dalgası, ≈ 0,3–0,5 ms) — kademe anahtarı ile açılır; Yüksek'te kapalı.
4. `_DD_ShaftMask` sözleşmesi S-05 WP-075'ten; gelene kadar `DD/VFX Atmo` ana ışık gölgesi + ileri saçılma ile çalışır.
5. Gib seti (§6-1) P-06 kapasitesine bağlı; gelene kadar kafa öldürme = sis + `fb_zombiedeath`.

---

Sahip: S-07 (değerler, `FeelRuntime.CameraModesFeel`). Uygulayıcı: S-01 `ICameraReactionSink` (`AddRecoil`, `AddKick`, `SetBob`, `FovPunch`) + `CameraShakeService` mod ölçeği. Kaynaklar: WP-069 §4b (oyun recoil, S-02), §4–§5.5 (görsel/kamera, bu konsey), WP-070 §6 + D-15/D-16/D-17/D-20, FeelConfig v1 alanları, WP-071 K3 kadans bandı. Yazarlık 60 fps (DEC-092); "kare" = 1/60 s.

## A4.0 · Kanal sözleşmesi (bağlayıcı)
| Kanal | Sink | Nişan ışını | Kaynak | Sahibi |
|---|---|---|---|---|
| Recoil (pitch/yaw °) | `AddRecoil` | **oynatır** | WP-069 §4b silah deseni (kick °/atış, yatay desen, birikim 4×kick, toparlanma s) — mod bağımsız, TP=FP | S-02 değer · S-07 geri dönüş yayı |
| Kick (çeviri m + roll °) | `AddKick` | oynatmaz | bu belge, mod × silah | S-07 |
| Shake (çekirdekler) | `CameraShakeService` | oynatmaz | mevcut m-tabanlı olaylar × mod ölçeği (FP: yalnız dönüş, D-15) | S-07 |
| FOV punch (°, s) | `FovPunch` | oynatmaz | bu belge; **hep dışa** (+), tek istisna Raiju şarj sıkışması | S-07 |
| Bob | `SetBob(phase01, amp)` | oynatmaz | phase01 = adım olayı fazı (StrideCalibrator `foot_*`), amp = hız bandı × ağırlık × ADS × HeadBobScale; **şekil profilde** (§1) | S-07 |
| Görüntü modeli kick/sway/titreşim | FP viewmodel Animator/rig (S-06/S-01) | oynatmaz | WP-069 §4 tablosu (bu belge §2'de özet) | S-07 |
Tepeden: recoil oyun etkisi 0 (S-02 kural 2), tüm his mevcut `WeaponFeelRow`/`ImpactFeelRow` ile; değişiklikler yalnız §4. WP-069 §5.2 "TP pitch" sütunu `AddRecoil`'e devredildi; TP kozmetik kanal = çeviri + roll (çift pitch yok).

## A4.1 · FP hareket: bob, sway, iniş, ADS, mekanizma dürtmeleri
| Durum | Kamera dikey / yanal cm (adım başına) | Roll ± ° | Adım bandı | Görüntü modeli (kamera ×) | Not |
|---|---|---|---|---|---|
| Duruş | 0 / 0 | 0 | — | 0,3 cm @ 0,3 Hz | nefes: kamera pitch 0,12° @ 0,3 Hz |
| Yürüyüş ≈ 3,0 m/s | 1,4 / 0,9 | 0,35 | 1,8–2,0 Hz | ×1,5, 90° faz | yanal işaret = temas eden ayak tarafı |
| Koşu/sprint 6,2 m/s | 3,0 / 1,7 | 0,70 | 2,7–3,4 Hz | ×1,6, silah %15 indirilmiş | FOV +4° (ramp 0,25 s, ease-out); > 3 adım/s → genlik ×0,8 |
| Eğilme (varsa) | ×0,5 | ×0,5 | ×0,8 | ×0,5 | göz yüksekliği geçişi 0,18 s smoothstep |
| ADS | ×0,30 | 0 | aynı | ×0,20 | sway ×0,35, nefes 0,06° @ 0,25 Hz (sr_1 0,10°) |
| Ağırlık çarpanı | W1 ×0,85 · W2 ×1,0 · W3 ×1,25 · W4 ×1,4 | aynı | — | aynı | W = WP-069 §3 sınıfı |
- **BobStep eğrisi** (sinüs değil): temas = çukur, 3 karede ease-in quad; kalkış = tepe, ease-in-out sine; phase01 ayak olayına kilitli → kayma ("ayaklar hızdan yavaş") olsa bile kamera ayağı izler. Hız 0,35 m/s altı: amp 0 (0,12 s'de söner).
- **İniş (LandDip):** koşu uçuş fazı 1,5 cm / 0,12 s; düşüş ≥ 0,6 m → 5,0 cm + pitch 0,6° / 0,18 s (tavan). Cubic-out çöküş + yay dönüş (8 Hz, ζ 0,6).
- **Sway (SwayLag):** görüntü modeli bakışın tersine gecikir: konum 1,2 cm @ 100°/s, dönüş tavanı 2,0°; yay 8 Hz ζ 0,9. W1 10 Hz ×0,8 · W3/W4 6 Hz ζ 0,85 ×1,4. Koşuda ×1,2. ADS ×0,35, 12 Hz.
- **ADS:** süre = S-02 ADS s (0,18–0,55), ease-out cubic (0,22, 1, 0,36, 1). FP dünya FOV ×0,85 (AR/SMG/LMG), ×0,90 (pompalı), ×0,45 (sr_1 dürbün; Faz 1'de görüntü modeli FOV kilitli → dürbün overlay S-09), ×0,80 (rl_1). TP: FOV 60 → 52, omuz kamerası 0,30 m içeri, 0,25 s.
- **Mekanizma dürtmeleri (FP; TP ×0,5):** `mag_in`/`rocket_seat`/`chamber` → pitch −0,30° dip; `pump`/`bolt_*`/`charge` → roll 0,25° el tarafına; `switch_in` → pitch +0,40°; hepsi 0,10–0,15 s, yay 10 Hz ζ 0,8. Reload'da bob ×0,8, sway ×1,2.

## A4.2 · FP ateş — silah başına (pitch/yaw = `AddRecoil` S-02 §4b; roll/FOV/mikro-sarsıntı kozmetik; görüntü modeli WP-069 §4)
| Yuva | Recoil pitch ° (S-02) / yaw deseni | Toparlanma s | Kamera roll ± ° | FOV + ° | Mikro-sarsıntı Hz / ° / s | VM geri / yukarı cm · pitch / yaw / roll ° | VM top. s |
|---|---|---|---|---|---|---|---|
| ar_1 | 0,55 / desen ±0,25 | 0,35 | 0,15 | 1,2 | 20 / 0,06 / 0,08 | 3,0 / 0,6 · 2,2 / 0,5 / 0,7 | 0,11 |
| ar_2 | 0,70 ×3 burst / +0,15 | 0,30 | 0,20 | 1,4 | 20 / 0,07 / 0,10 | 3,5 / 0,8 · 2,6 / 0,6 / 0,8 (3 kick 55 ms, 1,0/1,15/1,3) | 0,14 |
| smg_1 | 0,30 / rastgele ±0,45 | 0,25 | 0,12 | 0,8 | 24 / 0,05 / 0,06 | 2,0 / 0,4 · 1,4 / 0,8 / 0,5 | 0,09 |
| smg_2 | 0,75 / zikzak ±0,40 | 0,30 | 0,15 | 0,9 | 24 / 0,05 / 0,06 | 2,2 / 0,5 · 1,6 / 0,9 / 0,6 (roll sola) | 0,09 |
| sg_1 | 2,4 / ±0,5 | 0,55 | 0,60 | 3,5 | 14 / 0,25 / 0,14 | 8,0 / 2,0 · 6,5 / 0,8 / 1,4 | 0,30 |
| sg_2 | 3,0 (2. atış 4,0) / ±0,6 | 0,60 | 0,70 | 4,0 | 14 / 0,28 / 0,16 | 9,0 / 2,4 · 7,5 / 1,0 / 1,6 (çift ×1,6) | 0,32 |
| lmg_1 | 0,45 / ±0,5 | 0,50 | 0,25 | 1,2 (sürdürme +0,4) | 8 / 0,12 / sürekli | 3,5 / 0,7 · 2,6 / 1,0 / 1,2 | 0,12 |
| lmg_2 | 0,90 (oturma) / ±0,35 | 0,60 | 0,22 | 1,1 (sürdürme +0,4) | 8 / 0,11 / sürekli | 3,2 / 0,6 · 2,4 / 0,9 / 1,0 | 0,12 |
| sr_1 | 4,5 / ±0,3 | 0,90 | 0,80 | 4,5 (ADS 2,5) | 12 / 0,20 / 0,12 | 9,0 / 2,6 · 7,5 / 0,5 / 1,2 | 0,34 |
| rl_1 | 6,0 / — | 1,00 | 1,20 | 6,0 | 9 / 0,40 / 0,30 | 12 / 3,0 · 9,0 / 1,5 / 1,8 | 0,55 |
| sw_arc Raiju | 1,2 / ±0,6 | 0,40 | 0,40 | 2,5 (şarj −1,0) | 28 / 0,15 / 0,35 | 5,0 / 1,2 · 4,0 / 1,0 / 0,8 | 0,40 |
| sw_gel Namekuji | 0,5 / ±0,3 | 0,20 | 0,20 | 0,8 | 6 / 0,10 / 0,20 | 3,0 / 0,8 · 2,0 / 0,6 / 0,5 (wobble ζ 0,3) | 0,20 |
Kurallar: (1) `AddRecoil` geri dönüşü **kritik sönümlü yay**, toparlanma s = %95 yerleşme; birikim tavanı S-02 4×kick, mutlak FP 8°/TP 6°; oyuncu fareyle bastırır (girdi yayı iptal etmez, üstüne biner). (2) Kamera roll/FOV/mikro-sarsıntı **1 kare gecikmeli** (silah önce, kafa sonra = ağırlık). (3) Auto: kamera kanalları **yığılmaz**; VM kick yığılır (kick_n = kick_1 × min(1 + 0,13(n−1), 1,8), bırakınca 0,25 s sıfır). FOV: tek seferlik tepe + ateş sürerken sabit +0,4°. Sürekli mikro-sarsıntı ramp-in 0,25 / ramp-out 0,20 s. (4) ADS: VM kick ×0,6 (geri ×0,4, pitch korunur), kamera roll ×0,5, FOV punch ×0,6, recoil değişmez (oyun). (5) FP'de shake **çeviri yok**: metre → derece 12°/m (ar 0,018 m → 0,22°, sg 0,055 → 0,66°), toplu tavan 1,8°.

## A4.3 · TP — aynı set, oranlar
| Kanal | TP değeri | FP'ye oran |
|---|---|---|
| Recoil | S-02 §4b aynen (oyun), tavan 6° | 1,0 (oyun kuralı) |
| Kick çeviri (`AddKick`) | geri/aşağı m = VM geri cm × 0,005 (ar 1,5 cm, sg 4 cm, sr 4,5 cm, rl 6 cm), tavan 0,12 m; yay silah sınıfı | — |
| Kick roll | FP roll ×0,6, tavan 1,0° | 0,6 |
| FOV punch | ×0,65 (ar +0,8, sg +2,5, sr +3,0, rl +4,5), sprint +2° | 0,65 |
| Mikro-sarsıntı | 0 (omuz kamerası); lmg/Raiju için Rumble 7 Hz 0,08° sürekli | — |
| Shake servisi | çeviri ×0,55 (tavan 0,12 m) + roll 1,0°/m·0,55 | 0,55 |
| Bob | FP × 0,25 (kamera sway; omuz ofseti sabit) | 0,25 |
| Sıkışma | ateş sürerken geri mesafe −0,10 m (ramp 0,20 s) + ağır atışta −0,04 m yay | — |
| Hasar / iniş / dürtmeler | roll ±0,8° · 0,03 m dip · ×0,5 | ≈ 0,6 |

## A4.4 · Tepeden — yalnız değişiklikler
1. WP-069 §5.1 satırları `WeaponFeelRow`'a girer (10 silah + 2 Special; Kagura/Ofuda/Tide satırları silinir). 2. lmg sürdürme Rumble 0,030 m "ateş başladı/bitti" olayı yeni. 3. rl_1 patlama mesafe sönümü §5.4. 4. Öldürme onayı Bump 0,008 m (§5.2) yeni. 5. Ölüm beat'i push-in 0,12 aynen. Başka değişiklik yok.

## A4.5 · Tüm modlar
### 5.1 Mikro hit-stop (`HitStopService`, grup-yerel; sunum ölçeği 0,06; min aralık öldürmede 0,20 s, diğerlerinde 0,25 s)
| Olay | Tepeden ms | TP ms | FP ms (tavan 35) | Not |
|---|---|---|---|---|
| Normal öldürme | 18 | 18 | 15 | tek hedef; 120 ms içinde en fazla 1 |
| Kafa vuruşu öldürme | 28 | 28 | 25 | + §5.2 headshot beat |
| ArmorBreak | 35 | 35 | 30 | mevcut 0,035 |
| HeavyDeath (Guard/Elite) | 40 | 40 | 35 | mevcut 0,04 |
| Ultra öldürme | 40 | 40 | 35 | + Rumble 0,3 s |
| Special zincir/kayma öldürme | 22 | 22 | 20 | zincir başına 1 kez, halka başına değil |
| Oyuncu ağır hasar (≥ %25 HP) | 20 | 20 | 15 | push-in yok (hiçbir modda yeni push-in eklenmez) |
### 5.2 Öldürme onayı (his beat'i; görseller A1/S-09, ses S-08)
t = 0: hit-stop (§5.1) + reticle hit-marker (S-09) + `kill_confirm` ses (S-08) + rumble 0,25 / 40 ms. Kamera: tepeden/TP Bump 0,008 m 0,06 s; FP roll bump 0,10° 0,06 s (yay 12 Hz ζ 0,7). Kafa vuruşu: hit-stop 28 + FOV +0,5° 0,06 s + rumble 0,40 / 60 ms + ses varyantı. Çoklu öldürme: 80 ms birleştirme, beat ≥ 120 ms aralık, 3+ → tek "büyük" beat (hit-stop ×1,3 tavan içinde, Bump 0,012 m).
### 5.3 Yönlü hasar (kamera tarafı; HUD yön göstergesi + vinyet S-09)
| Kanal | FP | TP | Tepeden |
|---|---|---|---|
| Roll (darbe tarafına) | ±1,2° | ±0,8° | 0 |
| Pitch | ön −0,6° (baş geri) / arka +0,6° | ×0,5 | 0 |
| Kick çeviri (kaynaktan uzağa) | 0 (D-15) | 0,03 m | mevcut PlayerDamage shake |
| Süre / yay | 0,20 s, 8 Hz ζ 0,6 | aynı | mevcut |
Ağır hasar (≥ %25 HP) ×1,6 + §5.1; yön = darbe kaynağı − oyuncu, kamera-yerel'e çevrilir; 0,25 s içinde ikinci darbe yığılmaz (büyük olan kalır).
### 5.4 Patlama / launcher — mesafe sönümü
A(d) = A0 · (1 − sat((d − 3,2) / (12 − 3,2)))^1,5. A0: tepeden 0,160 m Rumble 0,45 s (mevcut 0,12 → yükselir; tavan 0,22 içinde), TP 0,10 m + roll 2,0°, FP dönüş 1,8° (tavan) + FOV +3,0° (d ≤ 5 m, 12 m'de 0) 0,12 s. Rumble 7 Hz; hit-stop yok. Kendine hasar → §5.3 ağır. rl_1 ateşi: §2 satırı + TP sıkışma −0,06 m. Cluster (WP-069) bombacıkları: her biri ×0,35, 100 ms birleştirme.
### 5.5 Special his
- **Raiju (DG-2 benzeri):** şarj 0,20–0,25 s → FOV −1,0° sıkışma (ease-in quad; tek "içe" istisna) + Rattle 28 Hz 0,08° ramp; bırakış → §2 satırı; küre ilk hedefe çarpınca Rattle 0,045 m 0,30 s (tepeden), mesafe sönümü 2 → 10 m, FP 0,55°; zincir halkaları kameraya dokunmaz; öldürme beat'i §5.1 zincir satırı. Şarj sırasında bob ×0,6.
- **Namekuji (Sliquifier benzeri):** ateş §2; jel isabeti ≤ 4 m ise Bump 0,015 m / FP 0,18°; birikinti doğması kameraya dokunmaz. **Oyuncu kayması:** bob 0 (fade 0,10 s), kamera kayma yönüne lean roll 1,5° (FP) / 0,9° (TP), sway ×1,5, FOV +1° / (m/s > 3,0) tavan +4° (ramp 0,15 s), Rumble 7 Hz 0,06° sürekli; kayma sonu iniş bump 1,2 cm / 0,12 s + roll 0. Zombi kaymaları/erimeleri kameraya dokunmaz.

## A4.6 · Eğriler, yığılma, tavanlar
| Ad | Tanım | Parametre |
|---|---|---|
| KickSpring | kick tepe 1 kare (W1–W2) / 2 kare (W3–W4), kritik-altı yay dönüş | W1 14 Hz ζ 0,70 · W2 11 Hz ζ 0,65 · W3 8 Hz ζ 0,55 · W4 6 Hz ζ 0,50 (+ gövde kuyruğu 0,5 s Rumble) |
| RecoilReturn | `AddRecoil` geri dönüşü, kritik sönümlü | ζ 1,0; ω = 4,6 / toparlanma s (%95 yerleşme) |
| FovPunch | tepe ≤ 0,08 s ease-out cubic (0,22, 1, 0,36, 1); dönüş yay | 6 Hz ζ 0,8; sürdürme +0,4° ramp 0,25 s |
| BobStep | §1 | temas 3 kare ease-in quad, kalkış ease-in-out sine |
| LandDip | cubic-out çöküş + yay | 8 Hz ζ 0,6 |
| SwayLag | bakış hızına orantılı gecikme yayı | 8 Hz ζ 0,9 (W1 10 Hz, W3/W4 6 Hz ζ 0,85) |
| DamageNudge | roll/pitch dürtme + yay | 8 Hz ζ 0,6, 0,20 s |
Yığılma/tavan (mutlak, kaydırıcıdan bağımsız): recoil birikimi FP 8° / TP 6°; kozmetik roll toplamı (ateş + hasar + bob + kayma) ≤ 1,5° FP / 1,0° TP; FOV punch toplamı ≤ +6°; FP shake toplu ≤ 1,8°, tek olay ≤ 0,5° (sarsıntı) / 1,2° (rl); sürekli sarsıntı ≤ 9 Hz, > 12 Hz yalnız ≤ 0,4 s (flaş/nöbet standardı); kozmetik kanalların toplam açısal hızı ≤ 60°/s (yumuşak sınırlayıcı: aşınca tüm kozmetik kanallar oransal kısılır). Öncelik `FeelEventRouter`: patlama 80 > oyuncu hasar 70 > Special 60 > silah ateşi 40 > öldürme beat 30 > bob/sway 0; aynı kanalda düşük öncelik yüksek olanın altına eklenir, tavana vurunca yeni olan kısılır (eski kesilmez). Full-auto LMG 5 s testi: recoil ≤ 4×0,45 = 1,8°, roll ≤ 0,25°, FOV sabit +0,4°, mikro 8 Hz 0,12° → hiçbir kanal tavanın %50'sini aşmaz.

## A4.7 · Erişilebilirlik / hareket hastalığı
| Ayar | Ne ölçekler | Ne ölçeklemez |
|---|---|---|
| Sarsıntı kaydırıcısı 0–2 (varsayılan 1; > 1 tavan içinde büyütür) | shake, `AddKick` çeviri + roll, FOV punch, mikro-sarsıntı, öldürme/hasar dürtmeleri, patlama; **≤ 0,5 → roll 0** | `AddRecoil` (oyun), görüntü modeli kick/titreşim, bob (ayrı `HeadBobScale` 0–1, S-01) |
| Azaltılmış hareket (artık kamera da) | bob 0, FOV punch 0, roll 0, çeviri kick 0, mikro-sarsıntı 0, iniş dip 0, sway ×0,35, shake ×0,3, TP sıkışma 0, sprint FOV 0, mod geçişi anlık, Raiju şarj sıkışması 0; FP ölüm: baş düşüşü yok → 0,2 s solma + sabit TP kadraj (yörünge dönmez) | `AddRecoil` (D-10) — geri dönüş ζ 1,0 aynen; ADS FOV değişimi (0,25 s'ye uzar) |
| FP FOV kaydırıcısı 70–100 yatay | açısal kozmetik kanallar (roll, FOV punch, mikro-sarsıntı, hasar dürtme) × clamp(fov/85, 0,85, 1,15) — dar FOV'da aynı açı daha büyük görünür; cm cinsinden bob/sway sabit | recoil (oyun) |
| "Konfor" ön ayarı (tek tık, Ayarlar > Erişilebilirlik) | sarsıntı 0,6 · HeadBobScale 0,4 · roll 0 · FOV punch ×0,5 (tavan +3°) · mikro-sarsıntı 0 · sürekli sarsıntı 0 · sprint FOV +2° · statik merkez noktası açık · FP FOV 90 · geçiş 0,30 s | — |
Test kancası: `-ddFeelPreset comfort|reduced|default` bot koşusu; FP kanıt karesi + 5 s kanal günlüğü (roll/FOV/recoil zaman serisi) C-00'a.

## A4.8 · Veri şekli — `CameraModesFeelData` (FeelConfig v2 bölümü; S-01 sink bunu okur)
```
enum WeightClass { W1, W2, W3, W4 }   enum HitStopEventId { Kill, HeadshotKill, ArmorBreak, HeavyDeath, UltraKill, SpecialChainKill, PlayerHeavyHit }
struct Spring { float hz; float zeta; }   struct Nudge { float pitchDeg; float rollDeg; float seconds; Spring spring; }
class BobFeel { float verticalCm; float lateralCm; float rollDeg; float minHz, maxHz; float highCadenceScale = 0.8f; float highCadenceHz = 3f; }
class SwayFeel { float cmPer100DegPerSec = 1.2f; float maxRotDeg = 2f; Spring spring; float w1Scale = 0.8f, w3Scale = 1.4f; float runScale = 1.2f; float adsScale = 0.35f; }
class BreathFeel { float pitchDeg; float hz; float vmCm; }
class ModeShakeFeel { float translationScale; bool rotationOnly; float degreesPerMeter; float aggregateCapMeters; float aggregateCapDegrees; float singleEventCapDegrees; float continuousMaxHz = 9f; }
class DeathCameraFeel { float pullBackMeters; float orbitSeconds; float orbitDeg; float headDropMeters = -1.2f; float headRollDeg = 17f; float headFovDelta = -10f; float headDropSeconds = 0.6f; float pushInFraction; }
class CameraModeFeelProfile {
  ModeShakeFeel shake;
  Spring recoilReturn = (0, 1.0);            // hz per weapon: 4.6 / recoverySeconds
  float recoilAccumulateCapDeg;              // fp 8, tp 6, td 0
  float kickTranslationScale, kickTranslationCapMeters, kickRollScale, kickRollCapDeg; Spring[] kickSpringByWeight; // 4
  float fovPunchScale, fovPunchCapDeg, fovSustainAutoDeg, sprintFovDeg, sprintFovRampSeconds, adsFovPunchScale;
  BobFeel bobWalk, bobRun, bobCrouch; float bobAdsScale, bobReloadScale, bobTpScale; int bobContactFrames = 3;
  float landRunDipCm, landRunSeconds, landFallDipCm, landFallPitchDeg, landFallSeconds; Spring landSpring;
  SwayFeel sway; BreathFeel breathHip, breathAds, breathAdsSniper;
  float adsFovScaleRifle = 0.85f, adsFovScaleShotgun = 0.9f, adsFovScaleSniper = 0.45f, adsFovScaleLauncher = 0.8f; float adsShoulderInMeters; float adsTpFovDeg;
  Nudge nudgeMagIn, nudgeMechanism, nudgeSwitch; float nudgeScale;   // fp 1, tp 0.5, td 0
  float damageRollDeg, damagePitchDeg, damageKickMeters, damageSeconds, heavyDamageScale = 1.6f, damageStackWindow = 0.25f; Spring damageSpring;
  float killBumpMeters, killBumpRollDeg, killBumpSeconds, headshotFovDeg, killMergeWindow = 0.08f, killMinInterval = 0.12f;
  float explosionA0Meters, explosionA0RollDeg, explosionA0Deg, explosionFovDeg, explosionFovRangeMeters = 5f, explosionFullRange = 3.2f, explosionZeroRange = 12f, explosionFalloffPower = 1.5f;
  float squeezeSustainedMeters, squeezeShotMeters, squeezeRampSeconds;   // tp only
  float slideLeanRollDeg, slideFovPerMps, slideFovCapDeg, slideRumbleDeg, slideEndBumpCm;
  float raijuChargeFovDeg = -1f, raijuChargeRattleDeg = 0.08f, raijuImpactShakeMeters, raijuImpactDeg;
  float cosmeticAngularVelocityCapDegPerSec = 60f; float fovAngularScaleMin = 0.85f, fovAngularScaleMax = 1.15f, fovReferenceDeg = 85f;
  DeathCameraFeel death;
}
class CameraWeaponFeelRow {                // 12 satır (weaponId, WeaponDefinition v3 id); tepeden değerleri mevcut WeaponFeelRow'da kalır
  string weaponId; WeightClass weight; float recoveryOverrideSeconds;   // 0 = S-02 toparlanma
  float fpRollDeg, fpFovPunchDeg, fpFovPunchAdsDeg, fpMicroHz, fpMicroDeg, fpMicroSeconds /*0 = sürekli*/;
  float vmBackCm, vmUpCm, vmPitchDeg, vmYawDeg, vmRollDeg, vmRecoverySeconds, vmAutoStackStep = 0.13f, vmAutoStackCap = 1.8f, vmAdsScale = 0.6f, vmVibrationHz, vmVibrationDeg;
  float tpBackMeters, tpDownMeters, tpRollDeg, tpFovPunchDeg, tpSqueezeMeters, tpRecoverySeconds;
  float rumbleFraction; int rumbleDurationMs; bool sustainedRumble;
}
class HitStopEventRow { HitStopEventId id; float topDownSeconds, tpSeconds, fpSeconds; }   // fp ≤ 0.035 (GameFeelSettings.MaxHitStopSecondsFp)
class ComfortPresetFeel { float shakeScale = 0.6f, headBobScale = 0.4f, fovPunchScale = 0.5f, fovPunchCapDeg = 3f, sprintFovDeg = 2f; bool rollOff = true, microShakeOff = true, sustainedOff = true, centerDot = true; float fpFovDeg = 90f, transitionSeconds = 0.3f; }
class CameraModesFeelData { CameraModeFeelProfile topDown, tp, fp; CameraWeaponFeelRow[] weapons; HitStopEventRow[] hitStop; float killMinIntervalSeconds = 0.2f; ComfortPresetFeel comfort; ReducedMotionFeel reducedMotion; }
class ReducedMotionFeel { float shakeScale = 0.3f, swayScale = 0.35f, adsSeconds = 0.25f; bool bobOff = true, fovPunchOff = true, rollOff = true, kickTranslationOff = true, microOff = true, landDipOff = true, squeezeOff = true, sprintFovOff = true, instantTransitions = true, fpDeathHeadDropOff = true; }
```
Sink eşlemesi: `AddRecoil(p, y)` ← S-02 desen (WeaponDefinition v3 `recoil*`), geri dönüş `recoilReturn`; `AddKick(localPos, roll)` ← `tpBack/Down` + `fpRollDeg`·1 kare gecikme; `FovPunch(d, s)` ← `*FovPunchDeg`, s = 0,08; `SetBob(phase01, amp)` ← StrideCalibrator + `bob*`; shake ← `ImpactFeelRow` × `ModeShakeFeel`. Klampler `GameFeelSettings`'e eklenir: `MaxHitStopSecondsFp 0,035`, `MaxFpShakeDegrees 1,8`, `MaxRollDegrees 1,5`, `MaxFovPunchDegrees 6`, `MaxRecoilAccumulateDegrees 8`.

## A4.9 · Kabul (A4)
| # | Ölçüt | Eşik |
|---|---|---|
| J1 | Nişan sözleşmesi: bob/kick/punch/shake aktifken `TryGetAimRay.Direction` sabit; `AddRecoil(1,0)` → 1° | EditMode, 3 mod |
| J2 | lmg_1 5 s full-auto FP: roll ≤ 0,25°, FOV ≤ +1,6°, recoil ≤ 1,8°, açısal hız ≤ 60°/s | kanal günlüğü |
| J3 | Hit-stop FP ≤ 35 ms, push-in 0; öldürme beat aralığı ≥ 120 ms | PlayMode |
| J4 | Azaltılmış hareket: bob/punch/roll/çeviri 0; recoil aynen | 3 mod |
| J5 | Bob faz kilidi: kamera çukuru `foot_*` olayından ≤ 1 kare | koşu 6,2 m/s + strafe kayma |
| J6 | Sürekli sarsıntı ≤ 9 Hz; > 12 Hz ≤ 0,4 s | tarama |
| J7 | Her satır `CameraWeaponFeelRow`'da; yeni silah = veri, kod değil | 12/12 |
