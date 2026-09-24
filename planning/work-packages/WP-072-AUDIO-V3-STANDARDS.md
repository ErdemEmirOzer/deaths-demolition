# WP-072 Ses v3 — Teknik Standartlar
Model: Fable 5.1 · sound-engineer · 2026-09-24 · Girdi: WP-072 promptu, M4-R1 §0/§4, `_Prototype/Audio/README.md`, `_Art/Audio/README.md`, `AudioMixPlan/AudioVoicePool/AmbienceDirector`.
Kapsam: sound-designer DIRECTION belgesinin sayısal tabanı. Korunan standartlar: klip SFX −18 LUFS-I / TP ≤ −1,5 dBTP (ölçülen Vorbis ≤ −0,5), UI −26…−30, ambiyans −24…−26, müzik −16; master limiter −8 dB / 10 ms / 90 ms; master çıkış ≤ −1 dBFS. Sapmalar §2 ve §7'de gerekçeli. Perf/boyut kararı gelene kadar §9 550 MB varsayımıyla yazıldı.

## 1 · Silah katman modeli (her kimlik 5 katman; dosya = katmanların ön-karışımı, tail ayrı dosya)

| Katman | Bant (Hz) | Süre | Göreli seviye (dB, gövde=0) | Kanal | Değişken mi |
|---|---|---|---|---|---|
| Mekanik/tetik (horoz, sürgü, yay) | 1 500–9 000 (HPF 1 k) | 12–40 ms | −10 (Tepeden) · −7 (TP) · −3 (FP) | mono | perspektif (§4) |
| Gövde/punch (namlu basıncı) | 80–450 | 40–120 ms (zarf: atak ≤ 2 ms, düşüş 60–100 ms) | 0 | mono | perspektifte mesafe/HPF |
| Metalik mid/ring (mekanizma rezonansı, "gerçekçi metalik") | 1 200–6 000, ana rezonans 2–4 k, Q 4–8 | 60–250 ms | −6 (Tepeden/TP) · −4 (FP) | mono; FP'de 30 % genişlik | perspektif |
| Tail (mekân) | 200–8 000, HF sönüm §5 | 0,25–1,9 s (§5) | −14 açık · −10 koridor · −8 salon | stereo | mekân (§5) |
| Sub | 35–90 (HPF 30, LPF 110) | 60–150 ms | −8 | mono, spatialBlend 0 | yalnız oyuncu silahı + Launcher patlaması |

Sub yönetimi: (a) Weapon bus'ına kalıcı HPF 30 Hz; (b) yalnız oyuncu silahında ve ≤ 25 m patlamada sub katmanı var, dünya kaynaklarında yok; (c) World havuzunda aktif ses ≥ 20 ise sub −6 dB, ≥ 28 ise sub kapalı (AudioMixPlan gain, yumuşatma 120 ms) — limiter'ı sub değil orta bant sürer; (d) klipte sub tepe payı: sub'sız ön-karışım TP ≤ −3 dBTP, sub eklendikten sonra ≤ −1,5 dBTP; (e) ≥ 40 düşman yığınında (WP-065 dense-40 kaydı) LUFS-M max ≤ −13, limiter kazanç azaltımı ≤ 4 dB (ölçüm: kayıt-öncesi/sonrası fark).

## 2 · Sınıf hedefleri ve bus (klip = −18 LUFS-I standardı korunur; "punch" farkı crest ve LUFS-M ile, seviye farkı AudioMixPlan kazancıyla)

| Sınıf | Klip LUFS-I | LUFS-M max | Crest (dBTP − RMS) | TP | Mix kazancı (AR=0 dB) | MaxInstances | Bus |
|---|---|---|---|---|---|---|---|
| AR ×2 | −18 | −13 | 14–16 | −1,5 | 0 | 3 | Weapon → SFX-Player |
| SMG ×2 | −18 | −14 | 12–14 | −1,5 | −1,0 | 2 tetik + 1 döngü (§3) | Weapon |
| Shotgun ×2 | −18 | −11 | 17–19 | −1,5 | +1,5 | 2 | Weapon |
| LMG ×2 | −18 | −13 | 14–16 | −1,5 | +0,5 | 2 tetik + 1 döngü | Weapon |
| Sniper | −20 (sapma: 60 ms tek darbe, tepe-sınırlı; README kuralı "peak-limited below target") | −10 | 20–22 | −1,5 | +2,0 | 1 | Weapon |
| Launcher ateş / patlama | −18 / −18 | −12 / −11 | 16 / 18 | −1,5 / −1,5 | +1,0 / +3,0 (patlama Target bus, mevcut Explosion duck 0,30/0,60/0,55 s korunur) | 1 / 2 | Weapon / Target → SFX-World |
| Special zincir-yıldırım: şarj döngüsü / zap / ark | −24 / −18 / −20 | −20 / −12 / −15 | — / 16 / 12 | −3 / −1,5 / −1,5 | −6 / +1,5 / −3 | 1 / 1 / 4 | Weapon / Weapon / Impact |
| Special jel: atış / şap / kayma / fokurtu döngüsü | −18 / −20 / −22 / −26 | −13 / −16 / −18 / −22 | 14 / 12 / 8 / 6 | −1,5 / −1,5 / −3 / −3 | 0 / −4 / −8 / −10 | 2 / 4 / 2 / 2 (Special Field havuzu) | Weapon / Impact / Impact / Impact |

Sınıf içi iki silah aynı sınıf hedefinde kalır; ayrım spektral (§10 ölçüt A1), seviyeyle değil. Enemy tarafı ateş etmez; "diğer" = darbe, zombi, kırılma (Target/Impact, mevcut kazançlar).

## 3 · Yüksek RPM ve ses bütçesi

| Kural | Değer |
|---|---|
| Karar eşiği | atış aralığı ≥ 100 ms (≤ 600 RPM) → tek-atış klip; < 100 ms (> 600 RPM: SMG ~900 = 67 ms, LMG 700–800 = 75–86 ms, AR > 600 dahil) → **hibrit döngü**: atış başına tetik/ring transienti (≤ 70 ms) + RPM'e kilitli gövde+sub döngüsü (tam 8 atış/döngü, sample-doğru dikiş) + bırakışta tail |
| Örtüşme tavanı | aynı silahın tek-atış klipleri ≤ 3 üst üste (AR 3, Shotgun 2, Sniper 1); hibritte transient 2 + döngü 1 + tail 1 = 4 ses |
| MinRetrigger | 0,5 × atış aralığı: SMG 33 ms, LMG 40 ms, AR 45 ms, Shotgun 120 ms, Sniper 300 ms, Launcher 400 ms; transient için 0,45 × aralık |
| Voice stealing | oyuncu silahı çalınmaz (öncelik 95–100); tail'ler önce çalınır (öncelik 40), sonra darbe (55), kovan (20) |
| Eşzamanlı tavan | oyuncu silahı 4 · darbe 8 · kovan 2 · Special alan 3 · zombi/kırılma/foley 14 · tail 2 → World soft 16 → **24**, hard 32 korunur (bütçe 8,0 ms kararına kadar 20 soft, kalibrasyon ölçümüyle) |
| Yeni havuz | **PlayerWeapon 6** (4 ateş + 2 foley; asla çalınmaz, listener pause'a uyar, Signal gibi ayrı; World soft bütçesinden düşülmez); Signal 8 korunur |
| En uzun tail | kapalı salon 1,9 s (Sniper/Shotgun/Launcher); ≥ 0,8 s tail'ler CompressedInMemory (§9); yeni atış gelirken çalan tail −6 dB'ye 80 ms'de iner (mevcut duck mekanizması) |
| Döngü kalitesi | döngü dosyası ≥ 8 atış, RPM sapması ≤ 0,5 %; RPM değişirse (upgrade) pitch ile değil yeni döngü dosyasıyla (≤ 3 RPM kademesi/silah) |

## 4 · Perspektif mix (WP-070 CameraMode olayı → snapshot + placement delta)

| Parametre | Tepeden (mevcut) | TP | FP |
|---|---|---|---|
| Dinleyici | oyuncu başı, kamera yaw (ADR-021-2) | oyuncu başı, kamera yaw + pitch | kamera konumu/yönü (baş ≈ kamera) |
| Oyuncu silahı spatialBlend / spread | 0,35 / 0° | 0,25 / 60° | 0,0 (2D) mekanik+ring, 0,15 / 120° gövde |
| Oyuncu silahı stereo genişlik (korelasyon hedefi) | ≥ 0,9 (mono) | 0,6–0,8 | 0,3–0,6 (tail stereo, gövde ortada) |
| Dünya rolloff (linear, min–max m) | 8–60 | 6–55 | 3–45 |
| Oyuncu silahı HPF / LPF | 60 Hz / 14 kHz | 45 Hz / 16 kHz | 30 Hz / yok |
| Yakın-alan kazancı (gövde+sub) | 0 dB | +1 dB | +2 dB (sub +2 ayrıca, §1 sınırları içinde) |
| Mekanik katman | −10 dB | −7 dB | −3 dB (öne çıkar) |
| Snapshot delta (Music/Amb/World/Player/UI, Run v2.3'e göre) | 0/0/0/0/0 | −1/−1/−1/+1/0 | −2/−2/−2/+2/0 |
| Tail ıslak oran | §5 taban | +1 dB | +2 dB |
| Kovan duyulurluk (§6) | ≤ 3 m, −18 dB | ≤ 5 m, −12 dB | ≤ 6 m, −8 dB |

Geçiş: Pause'dan çıkışta snapshot `TransitionTo` 250 ms; placement parametreleri (spatialBlend, spread, rolloff, filtre) 200 ms lineer; çalan döngü/tail kesilmez. Mod başına ayrı klip yok — aynı dosya, farklı placement (bellek ×1). Reload foley FP'de 2D, TP/Tepeden'de spatialBlend 0,2.

## 5 · Mekân tail'leri

| Mekân | RT60 (500 Hz–1 k) | Ön yansıma gecikmesi | HF sönüm (RT60 4 k / RT60 1 k) | Islak/kuru (tail seviyesi) | Karakter |
|---|---|---|---|---|---|
| Açık avlu | 0,7–0,9 s (erken yansıma ağır, kuyruk zayıf) | 25–35 ms | 0,35 | −14 dB | tek duvar/kapı yansıması, "slap" 40–60 ms |
| Koridor | 1,1–1,4 s | 8–14 ms | 0,55 | −10 dB | flutter 6–10 ms, dar stereo (genişlik 0,4) |
| Kapalı salon | 1,6–1,9 s | 18–24 ms | 0,50 | −8 dB | yoğun difüzyon, geniş stereo (0,8), ahşap/kağıt sönüm |

Uygulama kararı: **kayıtlı tail katmanı** (silah sınıfı × 3 mekân = 21 stereo dosya; Special/Launcher kendi tail'i) oyuncu silahı için + **mevcut AudioReverbZone** (5 bölge) dünya 3D kaynakları için; mixer SFX Reverb send **yok** (reflection ile efekt ekleme WP-060'ta ertelendi; Unity SFX Reverb ~0,3–0,5 ms/örnek, iki kanal ~1 ms — 8,0 ms bütçede yok). Maliyet: kayıtlı tail = 1 ses/atış (~0,02 ms), reverb zone karışımı ≈ 0,15 ms/aktif bölge (mevcut). Seçim: WP-074 bölge verisine `Enclosure {Open, Corridor, Hall}` alanı eklenir (S-05 ile sözleşme); yoksa mevcut AudioRegion → Open. Geçiş: tail seçimi bir sonraki atışta değişir (çalan tail bitene kadar kalır), kapı eşiğinde 1 m histerezis; reverb zone preset'i 400 ms crossfade (mevcut 2,5 s bed geçişi ayrı kalır). Koridor preset'i Unity `Hallway` (decay 1,49 s → 1,3 s), salon `Stoneroom` decay 1,8 s, avlu `Plain`/mevcut.

## 6 · Darbe ve kovan

| Yüzey | Bant (Hz) | Süre | Klip LUFS-I | Mix kazancı (dB, taş=0) | Not |
|---|---|---|---|---|---|
| Taş/arduvaz | 800–6 000 çatlak + 150–300 gövde | 120–250 ms | −18 | 0 | ıslak Look B: kısa HF "sıçrama" 4–8 k |
| Ahşap | 200–2 500 | 150–300 ms | −18 | −1 | rezonans 300–600 |
| Sıva | 500–4 000 + toz 2–6 k | 150–350 ms | −18 | −1 | döküntü 200 ms |
| Toprak/çakıl | 100–1 500 | 100–200 ms | −18 | −3 | boğuk |
| Su | 300–5 000 + 60–120 "plop" | 200–400 ms | −18 | −2 | sıçrama tail 300 ms |
| Yaprak | 1 000–8 000 hışırtı | 150–300 ms | −20 | −6 | |
| Et | 80–600 + 1–3 k ıslak | 80–160 ms | −18 | 0 | FleshHit mevcut kimlik güncellenir |
| Zırh metali | 1 500–7 000 ring + 200–400 | 150–400 ms | −18 | +1 | ArmorImpact mevcut kimlik; Ricochet ayrı |

Kovan: yüzeye göre 3 varyant (taş, ahşap, toprak; diğer yüzeyler taşa düşer), bant 2–9 kHz, süre 150–400 ms, klip −24 LUFS-I (UI/foley setiyle aynı taban), mix kazancı −14 dB, mesafe sınırı ve mod seviyesi §4 tablosunda (Tepeden'de −18 dB / ≤ 3 m: fiilen fısıltı, kapatmak yerine kısıldı — FP/TP paritesi bir dosyayla). Kovan gecikmesi atıştan 250–600 ms sonra (WP-073 fizik olayı; yoksa sabit 400 ms). Eşzamanlılık: darbe 8 (MinRetrigger 20 ms, mevcut Impact), kovan 2 (MinRetrigger 40 ms), zırh ring 3. Projectile (WP-069): darbe konumu mermi çarpma noktası; ≥ 35 m darbelerde LPF 3 kHz (mesafe filtresi placement'ta).

## 7 · Launcher

| Öğe | Standart |
|---|---|
| Fırlatma | tek-atış, §2; ek "back-blast" katmanı 200–800 Hz 200 ms, −4 dB |
| Uçuş döngüsü | 3D loop 1,0 s dikişsiz, 150–2 500 Hz, −22 LUFS-I, spatialBlend 1, rolloff 4–45 m; en fazla 2 roket sesi |
| Doppler | `dopplerLevel 0` korunur (havuz geneli); yerine FP/TP'de pişirilmiş pitch ramp: uçuş döngüsüne mesafeye göre −2 st (0 → 45 m) placement pitch eğrisi; Tepeden'de 0 st (kamera üstte, geçiş yok) |
| Patlama katmanları | yakın ≤ 12 m: crack 1–6 k + gövde 60–200 + sub 35–80, 1,2 s; orta 12–35 m: gövde + yankı, LPF 6 k, 1,6 s; uzak > 35 m: LPF 2 k rumble, 2,0 s — 3 ayrı dosya, mesafeye göre seçim (crossfade yok, atış anında tek seçim) |
| Limiter koruması | patlama klibi TP ≤ −3 dBTP (sapma: ön-limiter tepe payı; LUFS-I −18 korunur); mevcut Explosion duck (Amb 0,30 / Combat 0,60 / 0,55 s) + oyuncu silahı hariç tüm Weapon −3 dB 300 ms; sub yalnız yakın katmanda; MaxInstances 2, MinRetrigger 60 ms; iki patlama üst üste → ikincisi −4 dB |

## 8 · Tekrar önleme

| Kimlik sınıfı | Min varyant | Pitch (st) | Filtre rastgeleliği | Round-robin |
|---|---|---|---|---|
| Ateş tek-atış (AR/Shotgun/Sniper/Launcher) | 4 | ±0,5 (Sniper ±0,25) | LPF 12–18 kHz rastgele | torba karıştırma (bag): 4'ü bitmeden tekrar yok |
| Hibrit transient (SMG/LMG) | 6 | ±0,35 | HPF 900–1 400 Hz | bag |
| Gövde döngüsü | 1 (RPM kademesi başına) | 0 | yok | — |
| Tail | 2 × 3 mekân | ±0,3 | yok | ardışık tekrar yok (mevcut) |
| Reload foley (olay başına) | 2 | ±0,4 | yok | ardışık tekrar yok |
| Darbe (yüzey başına) | 4 | ±1,5 | LPF 9–16 kHz + HPF 80–160 Hz | bag |
| Kovan (yüzey başına) | 3 | ±2,0 | LPF 8–14 kHz | bag |
| Special | 3 (zap/şap), döngüler 1 | ±0,5 | yok | bag |

Mevcut `AudioClipLibrary` ardışık-tekrar-yok kuralı bag'e yükseltilir (N ≥ 4 için); pitch jitter `AudioPlacement.PitchSpread` ile (ratio: ±0,5 st ≈ 0,029).

## 9 · Dosya ve bellek bütçesi (48 kHz mono 16-bit kaynak, Vorbis)

| Grup | Kimlik × varyant | Ort. süre | Toplam s | Vorbis q0,8 (~14 KB/s) | RAM (DecompressOnLoad 96 KB/s) |
|---|---|---|---|---|---|
| Ateş tek-atış 6 silah × 4 | 24 | 0,6 s | 14 | 0,20 MB | 1,4 MB |
| Hibrit transient 4 × 6 + gövde döngüsü 4 × 2 kademe | 32 | 0,07 / 1,0 s | 10 | 0,14 | — |
| Tail 7 sınıf × 3 mekân × 2 (stereo) | 42 | 1,4 s | 59 (×2 kanal) | 1,6 | — (CompressedInMemory) |
| Reload/handling foley 10 × 8 olay × 2 | 160 | 0,5 s | 80 | 1,1 | 7,7 |
| Darbe 8 × 4 + kovan 3 × 3 | 41 | 0,35 s | 14 | 0,20 | 1,4 |
| Launcher 1 + 1 döngü + 3 patlama + Special 2 × ~8 kimlik × 3 | ~55 | 0,9 s | 50 | 0,70 | 2,0 (döngüler hariç) |
| **Toplam** | **~354 dosya** | | **~230 s** | **≈ 3,9 MB** | **≈ 12 MB** |

Kurallar: Vorbis q0,8 ateş/darbe/Special (metalik HF için q0,6 yetersiz — sapma gerekçesi: 4–8 k ring bandı), q0,7 döngü/tail, foley q0,6; ≤ 1,0 s → DecompressOnLoad; 1,0–8,0 s → CompressedInMemory; ≥ 8 s → Streaming (mevcut). Eski 3 Special'ın 12 kimliği çıkar (−0,4 MB). Build'e eklenen tavan: **≤ 4,5 MB** (ses toplamı ≈ 14 MB / 550 MB); 2 GB kararı gelirse q0,9 ve stereo tail 3 varyant ile ≤ 8 MB. LFS kaynak WAV ≈ 22 MB.

## 10 · Kabul ölçümleri

| # | Ölçüt | Yöntem | Eşik |
|---|---|---|---|
| A1 | Sınıf içi ayırt edilebilirlik | iki silahın 4 varyant ortalaması: spektral centroid farkı ve zarf farkı (atak süresi, −20 dB düşüş süresi) | centroid ≥ 12 % veya ≥ 250 Hz **ve** düşüş süresi farkı ≥ 20 % |
| A2 | Sınıflar arası ayrım | tüm 10 silah çifti üzerinde A1 | 45/45 çift geçer |
| A3 | Klip seviyeleri | EditMode manifest yeniden ölçümü (mevcut) | §2 tablosu ±1,5 LU; TP ≤ −1,5 (Vorbis ölçülen ≤ −0,5); tepe-sınırlı sapmalar manifestte işaretli |
| A4 | Dense-40 karışım | `-ddAudioCapture` + `audio_capture_report.py --require-alive 40` | LUFS-I −19 ± 1 · LUFS-M max ≤ −13 · TP ≤ −1,0 dBTP · drop 0 · steal (PlayerWeapon) 0 |
| A5 | Ayırma | mevcut PlayMode GC 0 testi ateş sıcak yoluna genişletilir (hibrit döngü dahil) | 0 B |
| A6 | Perspektif | aynı atış 3 modda kayıt: Player bus delta ve stereo korelasyon | FP − Tepeden = +2 ± 0,5 dB; korelasyon Tepeden ≥ 0,9, FP ≤ 0,6; snapshot geçişi 250 ± 30 ms |
| A7 | Mekân | Sniper tek atış 3 mekânda; Schroeder geri-integrasyonla RT60 | §5 hedefi ±20 %; ön yansıma ±5 ms; tail seçimi bir sonraki atışta doğru (bölge testi) |
| A8 | RPM döngüsü | SMG 3 s sürekli ateş kaydı; atış aralığı otokorelasyonu | RPM sapması ≤ 0,5 %; dikiş tıkı yok (tıklama dedektörü > 6 dB/ms atlama 0) |
| A9 | Limiter | en yüksek yığın (Shotgun + Launcher patlama + 40 düşman) | kazanç azaltımı ≤ 4 dB; master ≤ −1 dBFS |
| A10 | Kapsam | EVENT-COVERAGE-MATRIX satırları | her yeni kimlik: manifest + recipe fallback + satır + test (README kural 3) |

A/B dinleme kiti (kullanıcı): `asset-production/AP-011/v016/review/ab/<sınıf>_<silahId>_A-eski_B-yeni.wav`, 48 kHz stereo; sıra: 3 tek atış (1 s ara) → 1,5 s sürekli/seri ateş → 2 s boşluk → tam reload → 1 s boşluk; A ve B −18 LUFS-I'ye eşitlenir, aralarına 1 kHz −30 dBFS 200 ms işaret; her dosyanın yanında `.json` (LUFS-I, LUFS-M max, TP, centroid, mekân, mod). Kit FP modunda (mekanik katman en belirgin) + Tepeden karşılaştırma dosyası (`_topdown`).
