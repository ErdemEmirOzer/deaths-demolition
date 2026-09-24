# WP-072 Ses v3 — Tasarım yönü (DIRECTION)
Model: Fable 5.1 · sound-designer (lider) · sound-engineer standartları STANDARDS.md
Tarih 2026-09-24 · Girdi: WP-072 promptu (10 madde), `WP-072-AUDIO-V3-STANDARDS.md` (bağlayıcı sayısal taban), M4-R1 §0/§4/§5 (tavan 35 kr), WP-069 promptu (S-02 kimlik sayfası henüz yok → yuva adları), `_Prototype/Audio/README.md`, manifest v015 (201 kimlik / 457 dosya), AP-011 README v005–v015.
Kullanıcı cümlesi (N17): "her silahın ve fonksiyonlarının sesleri de farklı ve çok daha gerçekçi metalik olacak." Bu belgede "farklı" = STANDARDS §10 A1/A2 ile ölçülür; "gerçekçi metalik" = katman 1 (mekanik) ve katman 3 (ring) gerçek mekanizma kayıtlarından türetilir, sentez yalnız sub/gürültü/ton desteğinde.
Yuva adları: AR1 AR2 SMG1 SMG2 SG1 SG2 LMG1 LMG2 SNP LNC (10 silah, LNC dahil) + SW_CHAIN (DG-2 benzeri) + SW_GEL (Sliquifier benzeri). S-02 ad verince §10'a eşleme sütunu eklenir; AudioSoundId'ler yuva adıyla kalır (kod tarafı ada bağımlı olmaz).
STANDARDS'tan sapmalar bu belgede yalnız §3 (kovan aile sayısı) ve §2 (foley kimlik sayısı, dosya sayısı korunur) — gerekçeleri yerinde.

## 1 · Ses kimliği ilkesi — 12 karakter, 5 katman

İlke: **iki merdiven.** Kuru ön-karışımın (tail hariç) spektral merkezi ve −20 dB düşüş süresi 10 silahı iki ayrı merdivene dizer; ardışık basamaklar merkezde ≥ 13 %, düşüşte ≥ 21 % ayrık → 45 çiftin hepsi A1'i geçer (A2 tasarımdan sağlanır, ölçümle doğrulanır). Sınıf içi çift her zaman "hafif/hızlı/tiz" (kısa strok, kapalı sürgü, gaz, 5,56) vs "ağır/yavaş/tok" (uzun strok, açık sürgü, pompalı, 7,62) olarak ayrılır — oyuncu sesi görmeden silahı tanır.

| Yuva | Referans mekanizma | K1 mekanik/tetik (bant · süre · zamanlama) | K2 gövde/punch (merkez Hz · süre) | K3 metalik ring (ana rezonans · Q · süre · ikincil) | K4 tail | K5 sub | Merkez / −20 dB düşüş (kuru) |
|---|---|---|---|---|---|---|---|
| AR1 | 5,56 kısa strok piston, alüminyum alt-üst gövde, hafif taşıyıcı | 5–8 k · 15 ms · t=0 (horoz + taşıyıcı ileri "tık") | 170 Hz · 70 ms | 3,3 kHz · Q6 · 90 ms · ikincil 5,2 k namlu ağzı | AR sınıfı ×3 mekân | 55 Hz · 80 ms | 2,85 kHz / 85 ms |
| AR2 | 7,62 uzun strok piston, preslenmiş çelik gövde, ağır taşıyıcı | 2–4 k · 30 ms · t=0 "klank" + t+45 ms taşıyıcı geri çarpma | 120 Hz · 95 ms | 2,0 kHz · Q5 · 150 ms · ikincil 1,2 k gövde takırtısı | AR ×3 | 45 Hz · 110 ms | 1,70 kHz / 195 ms |
| SMG1 | 9 mm kapalı sürgü, makaralı gecikme, sıkı çelik | 6–9 k · 12 ms · t=0 (kuru "tik") | 230 Hz · 50 ms | 3,9 kHz · Q8 · 60 ms · ikincil 6 k | SMG ×3 | 60 Hz · 60 ms | 3,30 kHz / 65 ms |
| SMG2 | .45 açık sürgü, ağır sürgü kütlesi, pirinç ayna | 1,5–3 k · 35 ms · **t=−20 ms** (sürgü ileri çarpar, sonra atış) | 150 Hz · 80 ms | 1,7 kHz · Q4 · 110 ms · ikincil 0,8 k sürgü toku | SMG ×3 | 48 Hz · 90 ms | 1,45 kHz / 160 ms |
| SG1 | 12 ga pompalı, uzun namlu, çelik hazne borusu | 2–4 k · 20 ms · t=0 horoz; pompa fire'da yok (§2) | 85 Hz · 120 ms | 1,5 kHz · Q4 · 220 ms · ikincil 0,6 k namlu gövdesi | SG ×3 | 40 Hz · 150 ms | 1,05 kHz / 290 ms |
| SG2 | 12 ga yarı-oto gazlı, kısa namlu, alüminyum alıcı | 3–6 k · 25 ms · t=0 horoz + **t+40 ms gaz pistonu "klak"** | 110 Hz · 95 ms | 2,5 kHz · Q6 · 140 ms · ikincil 4 k | SG ×3 | 42 Hz · 120 ms | 2,20 kHz / 130 ms |
| LMG1 | 5,56 kayış beslemeli, hafif alıcı, açık sürgü | 5–8 k · 15 ms · t=−10 ms + kayış halkası şıngırtısı 6–9 k | 160 Hz · 65 ms | 3,0 kHz · Q6 · 100 ms · ikincil 5 k | LMG ×3 | 50 Hz · 80 ms | 2,50 kHz / 105 ms |
| LMG2 | 7,62 ağır, uzun alıcı, çift yaylı | 1,5–3 k · 35 ms · t=−15 ms + alıcı tokmağı 300–600 Hz | 100 Hz · 110 ms | 1,4 kHz · Q4 · 180 ms · ikincil 0,45 k tok | LMG ×3 | 40 Hz · 120 ms | 1,25 kHz / 240 ms |
| SNP | .338 sürgülü, namlu freni, ağır kundak | 4–7 k · 20 ms · t=0 tetik/iğne; fren patlaması 300–900 Hz t+5 ms | 70 Hz · 150 ms (+ fren 300–900) | 2,8 kHz · Q5 · 300 ms · ikincil 1,1 k namlu | SNP ×3 | 38 Hz · 150 ms | 1,95 kHz / 350 ms |
| LNC | RPG benzeri, omuzdan, tüp + ateşleyici | 3–5 k · 25 ms · t=0 ateşleyici "çat"; **back-blast 200–800 Hz · 200 ms · t+30 ms (−4 dB)** | 90 Hz · 180 ms ("pop" + tıslama) | 0,9 kHz · Q3 · 120 ms tüp rezonansı (metal ring yok) | LNC ×3 | 45 Hz · 150 ms | 0,85 kHz / 430 ms |
| SW_CHAIN | Tesla bobini + kıvılcım aralığı, cam tüp, pirinç | pirinç şalter 2–4 k · 20 ms | boşalma çatlağı 1–6 k | cam tüp tınlaması 2,1 k + 4,4 k · Q10 · 400 ms | kendi (elektrik kuyruğu) | 40 Hz yalnız zap | zap 2,4 kHz / 180 ms |
| SW_GEL | Basınçlı cam hazne, pirinç meme, canlı jel | pirinç meme "psst" 3–6 k · 40 ms | jel fışkırma 120–400 Hz · 150 ms | cam hazne 1,8 k · Q6 · 250 ms (kısa) | kendi | yok | atış 1,1 kHz / 260 ms |

Sınıf içi çiftler (A1: merkez ≥ 12 % veya ≥ 250 Hz **ve** düşüş ≥ 20 %): AR 2,85→1,70 k (−40 %) · 85→195 ms (+129 %) — fark K3 (3,3 k vs 2,0 k + 1,2 k takırtı) ve K1 zamanlaması (tek tık vs klank + 45 ms geri çarpma). SMG 3,30→1,45 k (−56 %) · 65→160 ms — fark K1 (kapalı sürgü tık vs 20 ms erken sürgü çarpması) ve K3 (3,9 k vs 1,7 k + 0,8 k). SG 2,20→1,05 k (−52 %) · 130→290 ms — fark K3 (2,5 k kısa vs 1,5 k uzun) ve K1 (gaz pistonu +40 ms yalnız SG2). LMG 2,50→1,25 k (−50 %) · 105→240 ms — fark K3 + K1 (kayış şıngırtısı LMG1, alıcı tokmağı LMG2). Sınıflar arası en dar basamaklar: SG2→SNP 2,20/1,95 (13 %) ve SMG2→LMG2 1,45/1,25 (16 %); üretimde ilk ölçülen bunlar.
Seviye ile ayrım yapılmaz (STANDARDS §2). Hibrit döngü (SMG1 900, SMG2 650, LMG1 800, LMG2 650 RPM): transient dosyası = K1 + K3 (≤ 70 ms), döngü = K2 + K5; AR1/AR2 ≤ 600 RPM tek-atış (S-02'ye kısıt: AR RPM > 600 olursa hibrit sınıfına geçer, +2 kimlik). Hibrit sınıfların tail dosyası ilk 60 ms'de gövde sönümünü taşır (bırakış = son transient + tail; ayrı "release" kimliği yok).

## 2 · Reload / handling foley olay seti (olay adları mevcut `anim.*` sözleşmesini genişletir — S-06 WP-071 klip olaylarıyla birebir)

Mevcut: `anim.mag_out · anim.mag_in · anim.chamber · anim.eject · anim.switch · anim.lowered · anim.raised` (WP-032C). Yeni: `anim.bolt_back · anim.bolt_release · anim.pump_back · anim.pump_forward · anim.shell_insert · anim.belt_open · anim.belt_lay · anim.belt_close · anim.bolt_open · anim.bolt_close · anim.rocket_load · anim.rocket_seat · anim.sight_flip`. ADS: animasyon olayı değil, nişan durumu (`WeaponAimState` → S-02/S-01 sözleşmesi) `ads_in / ads_out`. Dry fire: mevcut `ClassicWeaponDryFire` olayına silah parametresi. Kural: her olay tek sahip; `anim.eject` (kovan atma) yalnız SNP/SG1 elle çevrimde, otomatiklerde ateş dosyasında.

| Yuva | Reload zinciri (sıra, tipik ms — S-06 klip süresi belirler) | Handling | Karakter notu |
|---|---|---|---|
| AR1 | mag_out 0 → mag_in 900 → bolt_release 1400 (boş) | switch raised lowered ads_in ads_out dry_fire | alüminyum şarjör kuyusu "şak", hafif taşıyıcı ileri; ADS = kayış + kundak sürtme 60 ms |
| AR2 | mag_out (kaya-çıkar, 2 adım) → mag_in (kaya-tak "klank") → bolt_back + bolt_release (çelik kol) | aynı 6 | çelik-çelik, ağır; tüm olaylar AR1'den −4 st daha tok ve 30–50 ms daha uzun |
| SMG1 | mag_out → mag_in → bolt_back (kol) → bolt_release ("HK slap": en sert foley) | aynı 6 | sıkı, kuru, kısa; ring 3,9 k çekirdeği foley'de de duyulur |
| SMG2 | mag_out → mag_in → bolt_back (açık sürgüde kalır; release yok — boş reload'da `bolt_back`, taktikte yalnız mag) | aynı 6 | ağır sürgü, yağlı çelik; mag_in pirinç ayna toku |
| SG1 | shell_insert ×N (her fişek ayrı olay, 250 ms aralık) → pump_back/pump_forward (yalnız boş) | switch raised lowered ads_in ads_out dry_fire | plastik fişek + çelik boru; pompa ileri "şak-klak" oyunun imza foley'i; ateşler arası pump çifti ateş kimliğine katılmaz (ayrı olay) |
| SG2 | shell_insert ×N → bolt_release (ilk fişekte sürgü düşer; boşta bolt_back kilit) | aynı 6 | daha hafif, alüminyum; bolt_release SG1 pompasından kısa ve tiz |
| LMG1 | mag_out (kutu) → belt_open → belt_lay (halka şıngırtısı) → belt_close (kapak çarpar) → mag_in (kutu) → bolt_back | aynı 6 | en uzun zincir; belt_close 200–600 Hz + 3–6 k çift vuruş |
| LMG2 | aynı 6 olay, her biri −5 st / +40 ms | aynı 6 | ağır kapak, çift yay gıcırtısı bolt_back'te |
| SNP | bolt_open (kaldır+çek, eject dahil "şıng") → bolt_close (it+indir "klak") her atışta; boş reload: mag_out → mag_in → bolt_open/bolt_close | aynı 6 | en okunur tek mekanizma; bolt_open 2 parça (kaldırma tık + çekme sürtme) |
| LNC | rocket_load (tüpe sürme, sürtme 400 ms) → rocket_seat (pirinç klik) → sight_flip (yalnız ilk kaldırışta) | switch raised lowered ads_in ads_out dry_fire | fiberglas tüp + pirinç; dry_fire = ateşleyici boşa "çat" |

Varyant: reload olayları 2 (STANDARDS §8), handling (raised/lowered/ads_in/ads_out/switch) 1 + pitch jitter ±0,4 st, dry_fire 2. Toplam foley 103 kimlik / ~166 dosya (STANDARDS §9 80 × 2 = 160 dosya ile eş bellek; kimlik sayısı yüksek çünkü "her fonksiyon farklı" — dosya sayısı korunarak). FP'de 2D, TP/Tepeden spatialBlend 0,2 (§4).

## 3 · Kovan düşüşleri — yüzey × kovan ailesi

STANDARDS §6 tek kovan sesi × 3 yüzey der; **sapma:** 12 ga plastik fişek (tok, 0,8–2 kHz, sekmesiz) ile pirinç tüfek kovanı (2–9 kHz çınlama, 2–3 sekme) aynı dosyayla pitch'le ayrılmaz — plastik/pirinç malzeme farkı. Üç aile + kayış halkası: `CasingRifle` (5,56/7,62/.338; 7,62 −2 st, .338 −4 st placement pitch) · `CasingPistol` (9 mm/.45; .45 −2 st) · `CasingShell` (12 ga plastik) × {Stone, Wood, Dirt} = 9 kimlik + `CasingLink` (LMG kayış halkası, taş; diğer yüzey taşa düşer) = 10 kimlik × 3 varyant = 30 dosya (+21 dosya ≈ +0,15 MB Vorbis, §9 tavanı içinde). Diğer yüzeyler taşa düşer; su/yaprak kovan sessiz (fısıltı bile duyulmaz, ses yok). Seviye/mesafe/gecikme STANDARDS §4/§6. Mevcut kovan kimliği yok → hepsi yeni. Kaynak: R1/R2/R3 render'larındaki şarjör/fişek düşme dilimleri + `ArmorPieceDrop` + `ImpactStone/Wood/Dirt` düşük seviyeli katman.

## 4 · Darbe seti yüzey başına (projectile çarpma noktası, WP-069)

| Yüzey | Kimlik | Durum | Tasarım (STANDARDS §6 bandı içinde) |
|---|---|---|---|
| Taş/arduvaz | `ImpactStone` | yenilenir (1 → 4 var) | çatlak 800–6 k + kısa 150–300 tok; ıslak Look B: 4–8 k sıçrama 30 ms üstte; kaynak `impact_stone` + `footstep_stone` transient dilimleri + `Ricochet` −14 dB |
| Ahşap | `ImpactWood` | yenilenir (1 → 4) | 300–600 rezonans belirgin; `impact_wood` + `amb_wood_creak` kısa dilim |
| Sıva | `ImpactPlaster` | **yeni** (4) | 500–4 k + toz 2–6 k 200 ms döküntü; `impact_stone` −3 st LPF 4 k + `noise:pink` 2–6 k zarf 200 ms + `ImpactPaper` −12 dB; `ImpactPaper` (shoji) ayrı kalır |
| Toprak/çakıl | `ImpactDirt` | yenilenir (1 → 4) | boğuk 100–1,5 k; `impact_dirt` + `FootstepGravel` kısa |
| Su | `ImpactWater` | yenilenir (1 → 4) | 60–120 "plop" + 300–5 k sıçrama 300 ms; `impact_water` + `footstep_water` + R8 jel render'ından **kullanılmaz** (jel ıslaklığı farklı, karışmasın) |
| Yaprak | `ImpactFoliage` | yenilenir (3 → 4, −20 LUFS) | 1–8 k hışırtı; mevcut türetme + `footstep_foliage` |
| Et | `FleshHit` | yenilenir (5, yeniden EQ + zarf) | 80–600 gövde + 1–3 k ıslak; `flesh_hit` + `AttackImpactBody` −8 dB; **isim yasağı:** render yok (NSFW filtresi), yalnız türetme |
| Zırh metali | `ArmorImpact` | yenilenir (3 → 4) | 1,5–7 k ring + 200–400; `armor_impact_c2` + R2 çelik gövde dilimi ring; `Ricochet` (5) korunur, sekme olasılığı S-02 verisi |
| (prop metal) | `ImpactMetal` | korunur (1 → 3) | mevcut + R2 dilimi |

Kalibre farkı dosyayla değil placement ile: SNP/7,62 darbeleri −2 st +2 dB, 9 mm/.45 +1 st −1 dB, 12 ga saçma = 3 darbe 15 ms yayılım (ADR-034-1 pellet). ≥ 35 m LPF 3 k (STANDARDS §6).

## 5 · Launcher

Fırlatma `WeaponLncFire` (4 var): ateşleyici çat → 90 Hz "pop" + 200–800 back-blast −4 dB t+30 ms → 300 ms azalan tıslama (motor ateşlenmesi, `noise:pink` BPF 400–2 k sweep aşağı). Uçuş `RocketFlightLoop` (1, 1,0 s dikişsiz, 150–2,5 k, −22 LUFS, 3D; `noise:pink` BPF + 6 Hz tremolo %15 + hafif 700 Hz ton; pitch rampası placement −2 st 0→45 m, STANDARDS §7). Patlama 3 dosya mesafeye göre tek seçim: `ExplosionNear` (≤ 12 m: 1–6 k çatlak + 60–200 gövde + 35–80 sub, 1,2 s, TP ≤ −3), `ExplosionMid` (12–35 m: gövde + yankı, LPF 6 k, 1,6 s), `ExplosionFar` (> 35 m: LPF 2 k gürleme 2,0 s) × 3 var. Kaynak: `explosion` + `explosion_tail` + `heavy_fire_burst` gövde + sub sentez; mevcut `Explosion` (2) çıkar, Explosion duck korunur. Patlama tail'i §8 mekân setinde (`ExplosionTail{Open,Corridor,Hall}`). Kendine hasar/yakın patlama: oyuncu ≤ 4 m ise `ExplosionNear` + 300 ms HF kısılması (LPF 2 k → 16 k, "kulak çınlaması" yok — retro değil, yorucu). R9 roket render'ı yalnız kredi artarsa (§9).

## 6 · Special'lar — retro-teknik palet (cam, bobin, pirinç, ahşap) / canlı jel

Sanat yönü bağlayıcı (WP-069 A2): SW_CHAIN'de metal ring **pirinç ve cam**, çelik değil; elektrik "temiz" değil, kıvılcım aralığı gibi çatırtılı. SW_GEL'de jel canlı ve parlak: fokurtu ince ve hızlı, çamur değil.

| Kimlik | Olay (`SpecialUsage` / yeni) | Tasarım | Süre · LUFS |
|---|---|---|---|
| `SwChainChargeLoop` | tetik basılı / şarj (Fired öncesi, WP-069 mekanik) | yükselen bobin vınlaması 180 → 900 Hz 1,2 s + cam tüp 2,1 k ışıltısı, döngü son 300 ms'de tutar; pitch = şarj oranı (placement) | 1,2 s loop · −24 |
| `SwChainFire` | Fired | pirinç şalter 20 ms → boşalma çatlağı 1–6 k 60 ms → küre uçuş "zzzt" 250 ms azalan | 0,4 s · −18 |
| `SwChainArc` | Hit (hedef başına sıçrama; mevcut `ChainArc` yenilenir) | kısa ark çatırtısı 2–8 k 120 ms + cam çınlaması 4,4 k −12 dB; hedef indeksine göre +0/+1/+2 st (zincir yükselir) | 0,2 s · −20 · 4 var |
| `SwChainShockLoop` | zombi elektrik çarpması (S-03 beat `sw_shock`) | 50/100 Hz brum + 3–6 k çatırtı yoğunluğu rastgele, 3D, hedef başına 1 | 0,6 s loop · −24 |
| `SwChainEmpty` | boş tetik | pirinç şalter + sönük bobin "vuu" düşen 400 → 120 Hz | 0,5 s · −26 |
| `SwChainReady` | şarj tamam (döngü biter) | cam tüp tek "ping" 2,1 k + kısa ark tık | 0,3 s · −24 |
| `SwChainCellOut / CellIn` | `anim.mag_out / mag_in` (pil/tüp değişimi) | cam tüp pirinç yuvadan çekilir (cam-metal sürtme 400 ms + bobin sönümü) / oturur (pirinç klik + kısa şarj ipucu) | 0,5 · −22 |
| `SwChainRaised / Lowered` | `anim.raised / lowered` | ahşap kundak + pirinç, bobin uyanış/sönüş 200 ms | 0,4 · −24 |
| `SwGelFire` | Fired | pirinç meme "psst" 40 ms → jel fışkırma 120–400 Hz 150 ms → cam hazne 1,8 k kısa | 0,35 s · −18 · 3 var |
| `SwGelSplat` | Hit (doğrudan isabet şap) | ağır ıslak şap 100–2 k 120 ms + ince damla 3–6 k saçılma | 0,3 s · −20 · 3 var |
| `SwGelKnockdown` | Hit (devirme; S-03 beat) | şap + gövde düşme toku 80–300 (mevcut `AttackImpactBody` −6 dB) | 0,45 · −20 · 2 var |
| `SwGelSlip` | S-03 beat `sw_slip` (birikintiye basan zombi) | kısa ıslak kayma "vıjjt" 300–3 k 200 ms yükselen + düşme toku | 0,4 · −22 · 3 var |
| `SwGelPuddleLoop` | FieldSpawned (birikinti, 3D, Special Field havuzu 2) | ince hızlı fokurtu 400–3 k, rastgele kabarcık 8–14/s, hafif 1,8 k cam tınısı yok (dünyada) | 2,0 s loop · −26 |
| `SwGelPuddleEnd` | FieldEnded | son 3 kabarcık + kuruma "çıt" | 0,6 · −24 |
| `SwGelPlayerSlide` | oyuncu birikintide kayma (WP-069 mekanik) | sürekli ıslak kayma, hız = pitch/kazanç, 2D | 1,0 s loop · −24 |
| `SwGelEmpty` | boş tetik | meme "psst" boş + cam hazne tıkırtı | 0,4 · −26 |
| `SwGelTankOut / TankIn` | `anim.mag_out / mag_in` | cam hazne pirinç kelepçeden (klik + jel çalkalanma) / oturur (klik + basınç "tss") | 0,5 · −22 |
| `SwGelRaised / Lowered` | `anim.raised / lowered` | jel çalkalanma + pirinç | 0,4 · −24 |
| `ZombieReactShock / ZombieReactSlip` | S-03 beat (vokal) | mevcut `ZombieHit*` −3 st tremolo 30 Hz / kısa şaşkın nefes; türetme | 0,6 · −18 · 2 var (SHOULD) |

Çıkış listesi (S-02 B4 ile birlikte, manifest ve matris satırları silinir): `SwFireKagura SwFireOfuda SwFireTide · SwFieldKagura SwFieldOfuda SwFieldTide · SwFieldEndKagura SwFieldEndOfuda SwFieldEndTide · SwHitKagura SwHitOfuda SwHitTide` (12). Koşullu: `ZombieReactPush/Web/Pull` — S-03 WP-076 `sw_push/web/pull` beat'lerini kaldırırsa çıkar, yerine `sw_shock/sw_slip`. Korunur: `SwSpawnGong SwPickup SwBeaconLoop SwExpired SwFinalSeconds SwGroundExpired` (WP-007 yaşam döngüsü), `StingerSpecialPickup`.

## 7 · Perspektif — tasarım dili (STANDARDS §4 sayıları bağlayıcı)

- **FP:** silah kulağın 40 cm önünde. K1 mekanik ve K3 ring öne (−3 / −4 dB), 2D ve %30 genişlik; gövde ortada, tail stereo. Reload foley 2D, her tık kafanın içinde. "Gerçekçi metalik" en çok burada okunur — A/B kiti FP'de.
- **TP:** silah omuzda; mekanik −7 dB, ring %20 genişlik, gövde hafif sağ/sol (omuz tarafı, spread 60°). Foley spatialBlend 0,2.
- **Tepeden (mevcut):** okunurluk perspektifi: mekanik −10 dB (fısıltı), ring ve gövde mono; kimliği ring taşır (K3 en az 3 dB gövdeden az değil). Kovan neredeyse yok.
- Mod başına dosya yok; tek dosya, placement farkı. Geçiş yalnız Pause'dan (250 ms snapshot + 200 ms placement) — çalan döngü/tail kesilmez.

## 8 · Mekân — tasarım dili (STANDARDS §5)

- **Açık avlu:** kuyruk yok gibi; tek duvar/kapı "slap" 40–60 ms, sonra hızla sönen 0,7–0,9 s. Silah kurudur, dünya geniştir. Tail −14 dB.
- **Koridor:** dar ve sert; flutter 6–10 ms (tahta/kağıt duvar arası), stereo dar (0,4), 1,1–1,4 s. Tail −10 dB. SMG/LMG serisinde flutter ritmi RPM'le çakışmasın: flutter 6–10 ms, atış aralığı ≥ 67 ms → çakışma yok.
- **Kapalı salon:** yoğun, yumuşak (ahşap/kağıt sönüm HF 0,50), geniş (0,8), 1,6–1,9 s. Tail −8 dB. Sniper/Shotgun/Launcher burada en uzun.
- Tail seçimi bölge `Enclosure {Open, Corridor, Hall}` (S-05 WP-074 sözleşmesi); dünya 3D kaynakları mevcut AudioReverbZone. Tail kimlikleri sınıf başına: `WeaponTail{Ar,Smg,Sg,Lmg,Snp,Lnc}{Open,Corridor,Hall}` + `ExplosionTail{…}` = 21 × 2 var stereo.

## 9 · Üretim planı

**(a) Seed Audio render listesi — 8 render, tavan 35 kr (hesap 4,3 kr/render = 34,4; gerçek 3,6–4,5).** Her render tek malzeme, olay sayısı + süre, dersler: sessizlik yok sayılabilir → olaylar onset dedektörüyle kesilir (`envelopes.txt` yöntemi), sıra prompta uymayabilir → dilimler tınıya göre atanır; "blood/gore/kill/shoot/gun" kelimeleri yok ("firearm" yerine "rifle action", "bolt" kabul — v006 `carbine_fire` geçti). Önce `balance`, her render sonrası `transactions`; ilk render (R1) ölçülür, birim maliyet 4,4'ü aşarsa R6 düşer (R6 en düşük öncelik).

| R | Anahtar | Prompt (İngilizce) | Kaynak olduğu katman/kimlikler |
|---|---|---|---|
| R1 | `mech_light_rifle_action` | A modern lightweight 5.56 rifle with an aluminium receiver handled on a wooden bench, six separate mechanical events with 1 second of silence between each: the charging handle pulled back and released with a fast bolt slap, an empty aluminium magazine released and dropped into the hand, a loaded magazine inserted and seated with a firm click, the bolt release paddle slapped so the bolt runs forward, a fire selector clicked twice, and a single dry hammer click. Crisp, tight, precise metal-on-metal, small parts, no loose rattle. Dry, close-miked recording, no reverb, no room tone, no music, no voice-over, no speech. | AR1 K1/K3 · AR1 SMG1 SG2 LMG1 foley (mag/bolt/switch/dry) · CasingRifle katmanı · AdsIn/Out sürtme |
| R2 | `mech_heavy_stamped_action` | A heavy 7.62 rifle with a stamped steel receiver and a long-stroke bolt carrier handled on a wooden bench, six separate mechanical events with 1 second of silence between each: the steel charging lever pulled back slowly and let go so the heavy carrier slams forward with a loud clank and a short receiver rattle, a steel magazine rocked out and dropped onto wood, a steel magazine rocked in and locked with a hard clunk, the selector lever swept down with a loud click, the receiver cover tapped twice, and a single dry hammer strike. Heavy, loose, oily steel-on-steel with a ringing rattle. Dry, close-miked recording, no reverb, no room tone, no music, no voice-over, no speech. | AR2 K1/K3 · AR2 SMG2 LMG2 foley · ArmorImpact/ImpactMetal ring · CasingRifle 7,62 |
| R3 | `mech_pump_shotgun_shells` | A 12 gauge pump-action shotgun with a steel tube magazine handled on a wooden bench, separate mechanical events with 1 second of silence between each: three plastic shotgun shells pushed one by one into the loading gate with a spring click each, the pump racked back with a metallic shuck, the pump slammed forward with a solid clack, a single empty plastic shell dropped onto a stone floor and bouncing once, and a single dry hammer click. Bright hollow plastic, steel tube spring, solid wood-and-steel action. Dry, close-miked recording, no reverb, no room tone, no music, no voice-over, no speech. | SG1/SG2 foley (shell_insert, pump, bolt_release) · SG1 K1 · CasingShell ×3 yüzey |
| R4 | `mech_bolt_action_sniper` | A large heavy bolt-action precision rifle on a wooden bench, separate mechanical events with 1 second of silence between each: the bolt handle lifted with a sharp click, the bolt drawn back with a smooth steel glide and a brass case flicked out with a short ring, the bolt pushed forward and the handle turned down with a solid locking clack, a steel box magazine removed and clicked back in, the safety lever flicked twice, and a dry firing-pin strike. Precise, heavy, smooth machined steel. Dry, close-miked recording, no reverb, no room tone, no music, no voice-over, no speech. | SNP foley (bolt_open/close, mag, dry) · SNP K1 · CasingRifle .338 |
| R5 | `mech_belt_feed_machine_gun` | A belt-fed machine gun with a steel top cover on a wooden bench, separate mechanical events with 1 second of silence between each: the feed tray cover unlatched and swung open with a steel clank, a belt of linked cartridges laid onto the feed tray with a jingle of metal links, the cover slammed shut with a heavy double clunk, the charging handle racked back and forward, a metal ammunition box unclipped and dropped onto wood, and a handful of empty steel belt links dropped onto stone. Heavy steel, chains of small metal links, no loose gravel. Dry, close-miked recording, no reverb, no room tone, no music, no voice-over, no speech. | LMG1/LMG2 foley (belt_*, mag box, bolt_back) · LMG1 kayış şıngırtısı K1 · CasingLink |
| R6 | `report_heavy_calibre_brake` | A very large calibre bolt-action rifle with a muzzle brake fired outdoors, three single shots with 2 seconds of silence between each, each shot a short extremely sharp crack with a hard low chest-thumping pressure wave and a brief metallic muzzle ring, no echo, no long decay. Dry, close-miked recording, no reverb, no room tone, no music, no voice-over, no speech. | SNP K2/K3 · AR2/LMG2 K2 (−3 st) · fren 300–900 katmanı; mevcut `rifle_fire`/`carbine_fire`/`shotgun_fire`/`heavy_fire_burst` diğer gövdeler |
| R7 | `electric_tesla_spark_gap` | A large Tesla coil with a brass spark gap and glass tubes, in one continuous take: a rising electrical whine building for 3 seconds, then one very loud sharp discharge crack, then 3 seconds of sustained crackling electric arc with a low buzz, then two short separate arc snaps. Raw electrical crackle and hum, no thunder, no rain, no synthesizer. Dry, close-miked recording, no reverb, no room tone, no music, no voice-over, no speech. | SW_CHAIN tamamı (ChargeLoop, Fire, Arc, ShockLoop, Ready, Empty) · mevcut `chain_arc` destek |
| R8 | `gel_viscous_slime_glass` | Thick glossy viscous slime in a glass jar, in one continuous take: a fast pressurised squirt of slime for half a second, a heavy wet splat as it lands on stone, 3 seconds of slow thick bubbling with small fast pops, then a short wet slide-squelch across the stone. Dense, sticky, wet, no water stream, no rain. Dry, close-miked recording, no reverb, no room tone, no music, no voice-over, no speech. | SW_GEL tamamı (Fire jel katmanı, Splat, PuddleLoop, Slip, PlayerSlide, TankIn/Out çalkalanma) |
| R9 (yalnız artan kredi) | `rocket_launch_tube` | A shoulder-fired rocket leaving a fibreglass tube outdoors: a sharp igniter pop, a hard rushing back-blast and a hissing rocket motor fading away over 2 seconds; repeat 3 times with 2 seconds of silence between. Dry, close-miked recording, no reverb, no room tone, no music, no voice-over, no speech. | LNC K2 + back-blast, RocketFlightLoop; yoksa (b) türetme |

**(b) Kredisiz türetme/sentez (`ap011_derive.py` şeması: `layers[] {src,start,length,pitch_st,stretch,reverse,hp,lp,low_shelf,high_shelf,gain_db,delay_ms,attack/hold/decay_ms,drive,tremolo_hz,var_*}` + `tone:` inharmonik partial + `noise:pink/white`):**
- Ateş ön-karışımları (12): K2 gövde = mevcut `rifle_fire` (AR1), `rifle_fire` −3 st + R6 (AR2), `carbine_fire_c2` (SMG1), `carbine_fire_c1` −4 st + drive 0,15 (SMG2), `shotgun_fire` (SG1), `shotgun_fire` +2 st kısa zarf (SG2), `heavy_fire_burst` dilim (LMG1), `heavy_fire_burst` −3 st (LMG2), R6 (SNP), `explosion` kısa + `heavy_fire_burst` + `noise:pink` sweep (LNC). K1 = R1–R5 dilimleri (HPF 1 k, 12–40 ms). K3 = ring için `tone:` inharmonik partial (ana rezonans + 1,6×/2,4× overtone, Q'ya göre decay) **+** R1/R2/R4 metal dilimlerinin 1,2–6 k bantlı, stretch 1,3–2,0 uzatılmış tınlaması (gerçek metal; sentez tek başına yasak). K5 = `tone:` 35–90 Hz sinüs zarf (HPF 30/LPF 110), yalnız oyuncu silahı dosyalarında.
- Hibrit döngüler (4 × 2 RPM kademesi): K2+K5 tek atışı RPM aralığına tam 8 kez dizerek dikişsiz döngü (sample-doğru; tıklama dedektörü A8), transient ayrı 6 varyant (`var_start_s` ile R-dilim kaydırma).
- Foley 103: R1–R5 dilimleri, yuva karakterine göre pitch (AR2/SMG2/LMG2 −4…−5 st, SMG1 +2 st, SG2 +3 st) + zarf; ADS = `noise:pink` 60 ms HPF 2 k sürtme + ilgili render'ın en küçük tıkı −12 dB; raised/lowered = kayış sürtme + tık. Mevcut `WeaponA/B/C*` 21 kimlik: AR1/SMG1/SG1'in v3 karşılıklarına eşlenir, eski kimlikler manifestten çıkar (S-02 göçüyle).
- Darbe 40 + kovan 30: §3/§4 reçeteleri. Special: R7/R8 dilimleri + `tone:` cam (2,1 k/4,4 k Q10) + `noise:` çatırtı yoğunluk modülasyonu; jel fokurtu döngüsü R8 3 s bölümünden 2,0 s eşit-güç dikişle.
- Ölçüm: `ap011_ab.py` centroid/onset/crest her kimlikte; A1 merdiveni ölçümle doğrulanır, basamak dışına düşen kimlik K3 rezonansı ±200 Hz ya da K2 zarfı ±20 ms kaydırılarak düzeltilir (yeniden render değil).

**(c) Mekân tail'leri (kredisiz, ofline):** yeni araç `tools/ap011_tail.py` — sentetik IR: STANDARDS §5 parametreleriyle 4 bantlı (250/1 k/4 k/8 k) üstel sönümlü gürültü IR (RT60 bant başına: 4 k = RT60×HF-sönüm), erken yansımalar tap deseni (avlu: tek slap 40–60 ms −6 dB; koridor: 6–10 ms flutter tarağı 8 tap; salon: 18–24 ms ön gecikme + 40 yoğun tap 20–80 ms), stereo = iki bağımsız gürültü dizisi korelasyon hedefli (0,9/0,4/0,8 karışım). Her silah sınıfının kuru ön-karışımı IR ile konvolve edilir, kuru sinyal çıkarılır → yalnız kuyruk, sınıf başına 2 varyant (farklı IR tohumu), −14/−10/−8 dB göreli seviye dosyaya değil AudioMixPlan kazancına yazılır (dosya −20 LUFS tabanı, TP ≤ −3). Doğrulama A7 (Schroeder geri-integrasyon) aynı araçla. Explosion tail'i ExplosionNear kuru katmanından.

## 10 · Kimlik envanteri (özet; tam satırlar `olay-haritasi` ekinde v016 manifestinde)

| Grup | AudioSoundId öneri | Olay | Adet (kimlik) | Var/kimlik | Süre | LUFS | Kaynak | Öncelik |
|---|---|---|---|---|---|---|---|---|
| Ateş tek-atış | `Weapon{Ar1,Ar2,Sg1,Sg2,Snp,Lnc}Fire` | `ClassicWeaponFire(weapon)` | 6 | 4 | 0,4–0,9 s | −18 (SNP −20 tepe-sınırlı) | R1/R2/R4/R6 + mevcut gövdeler (b) | MUST |
| Ateş hibrit | `Weapon{Smg1,Smg2,Lmg1,Lmg2}FireTransient` · `…FireLoop` | aynı; döngü tetik basılı | 8 | 6 / 2 kademe | 0,07 / 1,0 s | −18 | (b) | MUST |
| Tail | `WeaponTail{Ar,Smg,Sg,Lmg,Snp,Lnc}{Open,Corridor,Hall}` · `ExplosionTail{…}` | ateş sonrası, Enclosure | 21 | 2 stereo | 0,8–1,9 s | −20 | (c) | MUST (Hall/Corridor S-05'e bağlı; Open ilk dilim) |
| Foley | `Weapon{Yuva}{MagOut,MagIn,BoltBack,BoltRelease,Pump*,ShellInsert,Belt*,BoltOpen/Close,RocketLoad/Seat,SightFlip,Switch,Raised,Lowered,AdsIn,AdsOut,DryFire}` | `anim.*`, `ads_*`, `ClassicWeaponDryFire` | 103 | 2 (handling 1) | 0,1–0,6 s | −22 (dry −26) | R1–R5 (b) | MUST (AdsIn/Out, SightFlip SHOULD) |
| Kovan | `Casing{Rifle,Pistol,Shell}{Stone,Wood,Dirt}` · `CasingLink` | WP-073 fizik ya da +400 ms | 10 | 3 | 0,15–0,4 s | −24 | R1/R3/R5 + mevcut (b) | SHOULD (Stone MUST) |
| Darbe | `Impact{Stone,Wood,Plaster,Dirt,Water,Foliage,Metal}` · `FleshHit` · `ArmorImpact` · `Ricochet` | projectile çarpma (yüzey) | 10 | 4 (Ricochet 5, Metal 3) | 0,1–0,4 s | −18 (Foliage −20) | mevcut + R2 (b) | MUST (Plaster S-05 yüzeyi gelince) |
| Launcher | `RocketFlightLoop` · `Explosion{Near,Mid,Far}` | uçuş / patlama mesafe | 4 | 1 / 3 | 1,0 / 1,2–2,0 s | −22 / −18 | (b), R9 opsiyon | MUST |
| SW_CHAIN | §6 tablosu | `SpecialUsage`, `anim.*`, `sw_shock` | 10 | 1–4 | 0,2–1,2 s | −18…−26 | R7 (b) | MUST (CellOut/In, Ready SHOULD) |
| SW_GEL | §6 tablosu | `SpecialUsage`, `anim.*`, `sw_slip` | 12 | 1–3 | 0,3–2,0 s | −18…−26 | R8 (b) | MUST (PlayerSlide, Tank* SHOULD) |
| Zombi tepki | `ZombieReact{Shock,Slip}` | S-03 beat | 2 | 2 | 0,6 s | −18 | türetme | SHOULD |
| **Toplam yeni/yenilenen** | | | **186 kimlik ≈ 390 dosya** | | | | **8 render ≈ 34,4 kr** | |
| Çıkan | Kagura/Ofuda/Tide 12 · `WeaponA/B/C*` 21 (eşlenip kalkar) · `Explosion` 1 · `RifleFire ShotgunFire SmgFire HeavyFire HeavyMotorLoop HeavySpinDown ReloadStart/Insert/End DryFire` 10 (v3 karşılığına eşlenir) | | 44 | | | | | S-02 B2/B4 ile |

Bellek: ~390 dosya, ≈ 245 s → Vorbis ≈ 4,2 MB (STANDARDS §9 tavanı 4,5), RAM ≈ 12,5 MB; kovan +0,15 MB ve foley kimlik artışı dosya sayısını değiştirmez. Manifest v016 (ateş/foley/darbe), v017 (Special + tail), v018 (kovan + Plaster + Hall/Corridor) — dilim başına ayrı commit.

## 11 · Dinleme kiti ve açık sorular

Dinleme kiti (STANDARDS §10 formatı): `asset-production/AP-011/v016/review/ab/<sınıf>_<yuva>_A-eski_B-yeni.wav` — 10 silah + 2 Special = 12 dosya + `_topdown` çiftleri; A = mevcut kimlik (AR1←RifleFire, SMG1←SmgFire, SG1←ShotgunFire, LMG1←HeavyFire; A'sı olmayan yuvalarda A = sınıf komşusu, dosya adında `A-yok`), B = v3 FP ön-karışımı + Open tail; dizi: 3 tek atış → 1,5 s seri → 2 s → tam reload → 1 s. Ek: `ladder_all10.wav` — 10 silah merdiven sırasıyla (SMG1 → … → LNC) tek atış 1,5 s arayla; kullanıcı "hepsi farklı mı" sorusunu tek dosyada cevaplar. Her dosya yanında `.json` (LUFS-I, LUFS-M, TP, centroid, −20 dB düşüş, mekân, mod).

| # | Açık soru | Kime | Varsayılan (cevap gelmezse) |
|---|---|---|---|
| Q1 | Yuva ↔ özgün ad ve RPM/şarjör (AR RPM ≤ 600?) ; SMG2 açık sürgü kabul mü; SNP tek atışta bolt otomatik mi | S-02 WP-069 | §1 tablosu; AR tek-atış; bolt her atışta anim olayı |
| Q2 | `WeaponAimState` (ads_in/out) ve `ClassicWeaponDryFire(weapon)` parametreli olay; `SpecialUsage` kind seti (Charge/Fired/Hit/FieldSpawned/FieldEnded/Empty/Ready/Slide) | S-02 | mevcut Usage kind'ları + Charge/Empty/Ready/Slide eklenir |
| Q3 | `CameraMode` olay adı ve Pause geçiş zamanlaması; dinleyici FP'de kamera mı baş mı | S-01 WP-070 | STANDARDS §4 |
| Q4 | `Enclosure {Open, Corridor, Hall}` alanı bölge verisinde; kapı eşiği histerezisi 1 m | S-05 WP-074 | yoksa Open |
| Q5 | Klip olay adları §2 listesi birebir; temas kareleri (mag_in oturma, pump ileri, bolt_close) 35 ms onset payı ile | S-06 WP-071 | §2 adları; olay yoksa foley kimliği rezerve |
| Q6 | Kovan fizik olayı (`CasingLanded(surface)`) ve projectile çarpma yüzey etiketi (`Plaster` yeni) | S-07 WP-073 / S-02 | +400 ms sabit; Plaster → Stone |
| Q7 | `sw_shock / sw_slip / sw_knockdown` beat'leri; `sw_push/web/pull` kaldırılıyor mu | S-03 WP-076 | ZombieReactPush/Web/Pull kalır, yeni ikisi eklenir |
| Q8 | ~~550 MB / 2 GB kararı~~ **KAPANDI 24.09: DEC-092 60 fps, DEC-093 5 GB** → STANDARDS §9 geniş dalı: q0,9 ateş/darbe/Special, 3 stereo tail varyantı, ses eklentisi ≤ 8 MB | C-00 / kullanıcı | — |

> **Durum (2026-09-24, B1):** yuva adları S-02 WP-069 §0 ile eşlendi: SNP→SR1, LNC→RL1, SW_CHAIN→SW_ARC; döngü sınıfı AR1/SMG1/LMG1 (600/900/720 RPM), SMG2/LMG2 tek-atış. C-00 kararı (a): seed_audio SFX'e kullanılmaz; R6/R9 türetildi, kalan 8,4 kr rezerve döndü. B1 = AP-011 v016 (34 kimlik / 91 dosya, A1 45/45).
