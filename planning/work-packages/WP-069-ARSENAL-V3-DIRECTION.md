# WP-069 Arsenal v3 — Yön belgesi (DIRECTION)

Paket: WP-069 (S-02, M4-R1 N1/N8/N12/N16). Tarih 2026-09-24. **Faz A çıktısı**: A1 kimlik sayfası (§1–§4), A2 iki Special (§1 §7 + §2 §8 + §3 §6 + §4 §7), A3 ADR-069-1 (§5), A4 WeaponDefinition v3 veri sözleşmesi (§6). Tasarım ekibi: **game-designer (lider)** · art-director · sound-designer · animation-specialist; mühendislik bölümleri (A3/A4) S-02. Görüş alınan oturumlar: S-06 (animasyon kimlikleri/olaylar/FP soketleri), S-08 (ses bloğu ve olaylar), S-04 (kilit sırası, coreBranches/keystone), S-01 (WP-070 nişan ışını, paralaks, geçiş kapısı), P-07 (soket/parça adları, blockout seti v001), S-07/S-09 (sorular gönderildi; yanıt geldikçe §7'ye işlenir).

**Kullanıcı kararları (24.09, M4-R1 §6):** DEC-092 60 fps (Yüksek kademe ort ≤ 16,6 ms / p99 ≤ 20 ms) · DEC-093 build tavanı 5 GB · DEC-094 Special'lar özgün ad + kendi modelimiz, mekanik/his/sanat yönü DG-2 / Sliquifier'e çok yakın — **tek ad sütunu (özgün); orijinal ad yalnız "referans" notu** (alt bölümlerde kalan "orijinal referans" ibareleri yalnız referans notudur) · DEC-095 gelişim ağacı iki katman (silah kilitleri kalıcı meta ağaçta). **Tüm sayılar PROTOTYPE-ONLY konsey önerisidir** → C-00 onayıyla **DEC-096 (Arsenal denge v3)** olur.

## §0 · Arsenal özeti ve konsey uzlaştırması

| Yuva | Ad | Sınıf | Ateş | Hasar × pellet | RPM | Şarjör / yedek | Reload tac / boş s | Mermi m/s | Silüet | audioId |
|---|---|---|---|---|---|---|---|---|---|---|
| ar_1 | Hinawa | AR | auto | 12 | 600 | 30 / 210 | 1,6 / 2,1 | 260 | Long | AR1 |
| ar_2 | Sanren | AR | burst 3 | 16 | 500 ef. | 24 / 168 | 1,8 / 2,3 | 320 | Long | AR2 |
| smg_1 | Shippu | SMG | auto | 8 | 900 | 42 / 294 | 1,5 / 2,0 | 170 | Short-thick | SMG1 |
| smg_2 | Tetsui | SMG | auto | 14 | 540 | 24 / 192 | 1,7 / 2,2 | 200 | Medium | SMG2 |
| sg_1 | Rappa | Pompalı | pompa | 9 × 8 | 72 | 7 / 42 | fişek fişek (kesilebilir) | 120 | Short-thick | SG1 |
| sg_2 | Nito | Pompalı | çift namlu | 11 × 10 | 200 tavan | 2 / 40 | 1,7 / 1,7 | 130 | Medium | SG2 |
| lmg_1 | Mukade | LMG | auto | 11 | 720 | 100 / 300 | 4,4 / 5,2 | 250 | Heavy | LMG1 |
| lmg_2 | Kurogane | LMG | auto ("oturma") | 24 | 420 | 60 / 180 | 5,0 / 5,8 | 300 | Heavy | LMG2 |
| sr_1 | Tomi | Keskin nişancı | sürgülü | 55 | 48 | 5 / 40 | 2,6 / 3,2 | 420 | LongX | SR1 |
| rl_1 | Bohiya | Launcher | tek | 110 + 160 patlama | 20 | 1 / 8 | 2,8 / 2,8 | 24 → 46 | Tube | RL1 |
| sw_arc | Raiju | Special | şarj + tek | zincir 8 hedef | ≈ 52 | 3 / 9 | 3,0 / 3,6 | 14 | Special | SW_ARC |
| sw_gel | Namekuji | Special | tek | jel + birikinti | ≈ 133 | 8 / 16 | 2,4 / 2,8 | 22 (yay) | Special | SW_GEL |

**Uzlaştırma (konsey bölümleri paralel yazıldı; çelişkiler burada bağlayıcı olarak çözülür):**
1. **rl_1 reload 2,6 → 2,8 s** — animasyonun okunur alt sınırı 2,8 s (§4 §3.2); game-design §3a/§5 güncellendi (efektif ≈ 20 RPM).
2. **Launcher `audioId` = `RL1`** (türetme kuralı: `weaponId` büyük harf; S-08 örneğindeki "LAUNCHER" yerine).
3. **Döngülü ateş sesi** (`fireLoop`) kuralı: Auto ∧ RPM ≥ 600 → **AR1, SMG1, LMG1**; SMG2 (540) tek tek, AR2 burst, LMG2 (420) tek tek (ses §2a'daki "SMG2 döngü" varsayımı sayılarla düzeldi).
4. **smg_2 besleme** = yan yatay şarjör 24 (animasyon §2); ses önerisindeki çift tambur alınmadı.
5. **Silüet sınıfları**: Long · LongX (sr_1) · Medium · ShortThick · Heavy (LMG) · Tube (rl_1) — art-director §1.1 son söz; sözleşme enum'u buna göre.
6. **Kapasite**: ar_2 24, smg_2 24, lmg_2 60, sw_arc 3, sw_gel 8 (game-design sayıları geçerli; animasyon tablosundaki aralıklar tahmindi).
7. **sg_1 reload** = başla 0,40 + fişek başına 0,55 + bitir 0,35 (boş: +0,5 chamber/pompa); animasyon alt sınırları (0,35/0,40/0,25; boş 0,45/0,40/0,45) sağlanır.
8. **Special görsel kararları (AD 🔴)** varsayılanı **referansa sadakat**: sw_gel jeli canlı magenta/mor; Special'larda pirinç/altın ≤ %25 alan (altın rezervi zaten Special'a ayrılı). Kullanıcı DEC-094 "sanat yönü çok benzer" dediği için sadakat varsayılandır; itiraz C-00'a.
9. **His sayıları** (görsel kick, mod başına kamera tepkisi, FP bob/sway, rumble) — animasyon §4–§5 değerleri **FeelConfig v2** satırlarına `weaponId` ile girer (S-07 WP-073 uygular); oyun etkili recoil (nişan kayması) WeaponDefinition'dadır (game-design §4b). Oyun recoil'i TP/FP'de `CameraReactions.AddRecoil(pitch, yaw)` (S-01 WP-070) üzerinden nişan ışınına dahil olur.


## §1 · A1 Oyun tasarımı ve sayılar (game-designer, konsey lideri)
Tarih 2026-09-24 · Girdi: `00-BRIEF.md`, M4-R1 §0 birebir notlar, S-04 WP-077 ek girdisi (kilit sırası / coreBranches / keystone). Kullanıcı kararları (24.09): **DEC-092** perf hedefi 60 fps (Yüksek kademe ort ≤ 16,6 ms) · **DEC-094** Special'lar özgün ad + kendi modelimiz, mekanik/his/sanat yönü DG-2 / Sliquifier'e çok yakın · **DEC-095** gelişim ağacı iki katman (koşu içi dallanan ağaç + kalıcı meta ağaç; silah kilitleri meta ağaçta). Tüm sayılar **PROTOTYPE-ONLY öneri**; veride tutulur (`WeaponDefinition v3`), sabit kod yok; C-00 onayıyla DEC-09x olur. Görsel profil → art-director, ses kimliği → sound-designer, animasyon/kamera tepkisi → animation-specialist; burada yalnız **oyun etkisi** tanımlanır.

---

### §1 · Arsenal felsefesi

1. **On silah = on ayrı oyun sorusu.** Aynı sınıfın iki silahı aynı soruya iki cevap değil, iki farklı soruya birer cevaptır: AR'de *sürekli baskı* (ar_1) ↔ *hassas patlama/kafa* (ar_2); SMG'de *hacim + hareket* (smg_1) ↔ *durdurucu güç* (smg_2); pompalıda *tüp + koni + tek tek doldurma* (sg_1) ↔ *iki namlu, iki atış, hepsini ver* (sg_2); LMG'de *hat tutma hacmi* (lmg_1) ↔ *ağır kalibre, hat delme, oturdukça sıkılaşan namlu* (lmg_2).
2. **Rol haritası:** ar_1 varsayılan omurga (4–30 m) · ar_2 orta-uzun hassasiyet ve zırh · smg_1 4–14 m kalabalık eritme, en hareketli · smg_2 Runner durdurucu (yüksek stagger) · sg_1 4–8 m koni temizliği · sg_2 köşe/ kapı patlaması (en yüksek anlık hasar) · lmg_1 koridor bastırma, 100'lük kutu · lmg_2 Guard/Executioner kırıcı · sr_1 hat delici (4 gövde), tek kafa · rl_1 kümeye alan hasarı, kendine risk.
3. **İlerleme eğrisi:** erken (R1–5) her şey Standard'ı 1–5 mermide düşürür; orta (R6–10) zırh + Guard yükselince zırh çarpanı ve penetrasyon ayrışır (SMG ×0,7 zırhta zorlanır, ar_2/lmg_2/sr_1 öne çıkar); geç (R11+) Executioner ×5 ve 40 zombi tavanı → alan (rl_1, sg_2 burst) ve delme (sr_1, lmg_2) belirleyici, ateş hacmi tek başına yetmez.
4. **Dünya dili:** hepsi "fantastik erken dönem" — fitil kilidi, serpantin, demir tava, kenevir kord, kutu/tüp/kutu-şerit şarjör; modern gerçek silah adı yok. Adlar Japonca esintili, kısa, Latin harfle okunur.
5. **Oyun-ölçekli mermi hızı:** tepeden kamerada 1 m ≈ 28 px; gerçek 900 m/s = 15 m/kare (60 fps, DEC-092) → 45 m harita 3 karede biter, iz görünmez ve silahlar arasında fark hissedilmez. Bant **110–420 m/s** balistik, roket 24→46 m/s, Special küresi 14 m/s, jel 22 m/s yay. 20 m'de uçuş: pompalı 167 ms (10 kare) ↔ keskin nişancı 48 ms (3 kare); SMG mermisi Runner'a 0,5 m öndeleme ister, sniper istemez — hız farkı **oynanışa** girer, sadece görsele değil. İz uzunluğu hız × 2 kare olarak S-07'ye önerilir (görsel karar onların).
6. **Yerçekimi** balistikte kozmetik (9,81; 45 m'de ≤ 0,2 m düşüş), roket ve jelde oyun etkisi (yay çizer, hedefe kadar düşer).
7. **Hitscan yok:** tüm mermiler projectile, havuzlu struct, eşzamanlı tavan 256 (pompalı 10 pellet × 2 + LMG 12/s birikimi tavanı zorlamaz: en kötü ~60 canlı mermi).

---

### §2 · Kimlik tablosu

| Yuva | Görünen ad (anlam) | Sınıf | Rol | His (1 cümle) | Ateş modu | Kovan | Kalibre | Silüet | audioId | Göç notu |
|---|---|---|---|---|---|---|---|---|---|---|
| ar_1 | **Hinawa** (fitil) | AR | omurga, 4–30 m | Güvenilir, ritmik, her tetikte küçük sarsıntı; "evim gibi". | auto 600 RPM | rifle | medium | Long | AR1 | ← A AssaultRifle (AP-013 yer tutucu) |
| ar_2 | **Sanren** (üçlü) | AR | hassasiyet, kafa, zırh | Üç mermilik keskin tokat; disiplinli oyuncuya kafa ödülü. | burst 3 · 0,05 s ara · 0,26 s burst arası (≈500 RPM efektif) | rifle | medium | Long (ince, uzun serpantin) | AR2 | yeni |
| smg_1 | **Shippu** (fırtına) | SMG | 4–14 m kalabalık, hareket | Kesintisiz vızıltı, kalçadan koşarak; mermi yağmuru. | auto 900 RPM | pistol | light | Short-thick | SMG1 | ← B AutoSmg |
| smg_2 | **Tetsui** (demir çekiç) | SMG | Runner durdurucu | Ağır tabanca mermisi, her vuruş zombiyi geri iter. | auto 540 RPM | pistol | medium | Medium (kalın kısa) | SMG2 | yeni |
| sg_1 | **Rappa** (boru/çan) | Pompalı | 4–8 m koni | Çan ağızdan dolu bir avuç; pompalayınca dünya sarsılır. | single 72 RPM (pompa) | shell | heavy | Short-thick | SG1 | ← C CombatShotgun |
| sg_2 | **Nito** (iki kılıç) | Pompalı | köşe patlaması | İki namlu, iki gürleme, sonra kırıp doldur; kumar hissi. | single 200 RPM tavan, mag 2 | shell | heavy | Medium (çift namlu, kırma) | SG2 | yeni |
| lmg_1 | **Mukade** (kırkayak; şerit) | LMG | koridor bastırma | Bitmeyen kutu-şerit, ağır ama sürekli; yer ayağı gibi. | auto 720 RPM | rifle | medium | Heavy (yeni sınıf: kutu + uzun namlu) | LMG1 | yeni |
| lmg_2 | **Kurogane** (kara demir) | LMG | zırh/Guard kırıcı, hat delme | İlk atışlar dağınık, namlu oturunca çelik gibi; her mermi iki gövde. | auto 420 RPM | rifle | heavy | Heavy (kısa kalın namlu, büyük kutu) | LMG2 | yeni |
| sr_1 | **Tomi** (uzağı gören) | Keskin nişancı | hat delici, kafa | Bir nefes, bir sürgü, dört gövde; sessizlik sonra tokat. | single 48 RPM (sürgü 1,25 s) | rifle | heavy | Long (en uzun, dürbün yerine demir gez-arpacık + "uzak gözü" kılıfı) | SR1 | yeni |
| rl_1 | **Bohiya** (ateş oku) | Launcher | alan hasarı, küme | Tüpten ateş oku fırlar, hızlanır, duman bırakır; patlama yer sarsar. | single, mag 1 | none | explosive | Tube (yeni sınıf: omuz borusu) | RL1 | yeni |
| sw_arc | **Raiju** (yıldırım canavarı) | Special | zincir yıldırım | Küre hedefe çarpar, kalabalık titreyerek çöker. | single, şarj 0,25 s | none | explosive (ses sınıfı) | Special-Heavy | SW_ARC | Kagura/Ofuda/Tide kalkar |
| sw_gel | **Namekuji** (sümüklü) | Special | kaydırıcı jel | Mor jel yay çizer, yer kayar, zombiler devrilir. | single | none | light (ıslak) | Special-Tube | SW_GEL | — |

Silüet sınıfı önerisi: mevcut Long / Medium / Short-thick + **Heavy** (LMG ikilisi) + **Tube** (rl_1) — 32 px'te beş sınıf ayrışır; art-director son sözü söyler.

---

### §3 · Sayı tablosu (PROTOTYPE-ONLY)

#### 3a · Hasar, kadans, cephane, reload

| Yuva | Hasar (pellet) | RPM (efektif) | Pellet · desen | Şarjör / yedek (toplam) | Reload taktik / boş s | Kafa ek çarpanı (×2,0 üstüne) | Zırh çarpanı | Stagger 0–1 |
|---|---|---|---|---|---|---|---|---|
| ar_1 | 12 | 600 | 1 | 30 / 210 (240) | 1,6 / 2,1 | ×1,0 | ×1,0 | 0,25 |
| ar_2 | 16 | 500 (3'lü) | 1 | 24 / 168 (192) | 1,8 / 2,3 | ×1,15 (toplam ×2,3) | ×1,2 | 0,30 |
| smg_1 | 8 | 900 | 1 | 42 / 294 (336) | 1,5 / 2,0 | ×1,0 | ×0,7 | 0,12 |
| smg_2 | 14 | 540 | 1 | 24 / 192 (216) | 1,7 / 2,2 | ×1,0 | ×0,9 | 0,45 |
| sg_1 | 9 | 72 | 8 · merkez 1 + iç halka 3 @ %40 + dış halka 4 @ %100 | 7 / 42 (49) | 0,4 başla + 0,55/fişek + 0,35 bitir (7 fişek 4,6 s; kesilebilir) / boş +0,5 (chamber) | ×1,0 | ×0,8 | 0,60 (atış toplamı) |
| sg_2 | 11 | 200 tavan (0,3 s) | 10 · serbest (gauss σ = %45) | 2 / 40 (42) | 1,7 (iki fişek birlikte) / 1,7 | ×1,0 | ×0,8 | 0,70 |
| lmg_1 | 11 | 720 | 1 | 100 / 300 (400) | 4,4 / 5,2 | ×1,0 | ×1,0 | 0,20 |
| lmg_2 | 24 | 420 | 1 | 60 / 180 (240) | 5,0 / 5,8 | ×1,0 | ×1,3 | 0,55 |
| sr_1 | 55 | 48 | 1 | 5 / 40 (45) | 2,6 / 3,2 (şeritli klip) | ×1,25 (toplam ×2,5) | ×1,6 | 0,80 |
| rl_1 | 110 doğrudan + 160 patlama | 20 (0,2 s + 2,8 s reload) | 1 | 1 / 8 (9) | 2,8 / 2,8 (§0 uzlaştırma) | ×1,0 (patlama bölgesel) | patlama kuralı §5 | 1,0 |

Reload notu: "taktik" şarjörde mermi varken (chamber adımı atlanır), "boş" chamber/pompa/sürgü dâhil. S-06 olay adları aynen (mag_out/mag_in/chamber, shell_in[i], box_open/belt_seat/box_close, bolt_*, rocket_in/rocket_seat).

#### 3b · Balistik

| Yuva | Mermi hızı m/s | Yerçekimi m/s² | Azami menzil m (ömür) | Tam hasar ≤ m | En az % @ m | Penetrasyon gövde / kayıp | 20 m uçuş ms |
|---|---|---|---|---|---|---|---|
| ar_1 | 260 | 9,81 | 60 | 30 | %70 @ 60 | 1 / −%40 | 77 |
| ar_2 | 320 | 9,81 | 70 | 40 | %75 @ 70 | 1 / −%35 | 62 |
| smg_1 | 170 | 9,81 | 40 | 14 | %55 @ 40 | 0 | 118 |
| smg_2 | 200 | 9,81 | 42 | 18 | %60 @ 42 | 1 / −%50 | 100 |
| sg_1 | 120 | 9,81 | 24 | 6 | %35 @ 24 | 0 | 167 |
| sg_2 | 130 | 9,81 | 20 | 8 | %40 @ 20 | 0 | 154 |
| lmg_1 | 250 | 9,81 | 55 | 28 | %65 @ 55 | 1 / −%40 | 80 |
| lmg_2 | 300 | 9,81 | 60 | 32 | %70 @ 60 | 2 / −%30 her gövde | 67 |
| sr_1 | 420 | 9,81 | 90 | 60 | %85 @ 90 | 4 / −%20 her gövde | 48 |
| rl_1 | 24 → 46 (§5) | 3,0 itkide / 6,0 sonra | 70 (2,2 s kendini patlatır) | — | — | 0 (temasta patlar) | ≈500 |

Menzil düşüşü doğrusal (tam hasar sınırından azami menzile). Penetrasyon: her geçilen gövde sonraki hasarı belirtilen oranda düşürür; zırhlı bölge geçişte zırhı eritir ama gövde sayar.

#### 3c · Sağlama (Upper gövde, zırhsız; Standard R1 34 HP · Standard R10 70,7 · Guard R10 155,6 · Executioner R10 353,6)

| Yuva | DPS (tüm pellet) | Standard R1 atış | Standard R10 | Guard R10 gövde / kafa | Executioner R10 | Guard R10 TTK s | Şarjör hasarı | Okuma |
|---|---|---|---|---|---|---|---|---|
| ar_1 | 120 | 3 | 6 | 13 / 7 | 30 | 1,20 | 360 | omurga: her tier'e "yeter", hiçbirinde en iyi değil |
| ar_2 | 133 | 3 (1 burst) | 5 (2 burst) | 10 / 5 | 23 | 1,08 | 384 | kafa vurabilen için ar_1'den %30 hızlı |
| smg_1 | 120 | 5 | 9 | 20 / 10 | 45 | 1,27 | 336 | zırhsızda ar_1'e eşit, zırhlıda ×0,7 ile geride — bilinçli |
| smg_2 | 126 | 3 | 6 | 12 / 6 | 26 | 1,22 | 336 | smg_1'in cephane verimliliği ×1,75; stagger 0,45 Runner'ı 2 vuruşta durdurur |
| sg_1 | 86 | 1 (≥ 4 pellet) | 1 (≥ 8 @ ≤ 6 m) | 3 / 2 | 5 | 1,67 | 504 | 6 m dışında hızla erir; Guard'a değil kalabalığa |
| sg_2 | 367 anlık / 110 sürekli | 1 | 1 | 2 (iki namlu 0,3 s) / 1 | 4 | 0,30 | 220 | en yüksek anlık; 2 atış sonra 1,7 s savunmasız |
| lmg_1 | 132 | 4 | 7 | 15 / 8 | 33 | 1,17 | 1100 | tek şarjörle R10'da 7 Guard |
| lmg_2 | 168 | 2 | 3 | 7 / 4 | 15 | 0,86 | 1440 | en yüksek sürekli DPS; bedeli hareket ×0,70 ve 5 s reload |
| sr_1 | 44 tek hedef | 1 | 2 (kafa 1) | 3 / 2 | 7 (kafa 3) | 2,50 | 275 | tek hedefte zayıf, hatta 4 gövde → efektif 176 DPS |
| rl_1 | 96 tek / alan | 1 | 1 | 1 doğrudan (blast tek başına 160 ≥ 155,6) | 2 | — | 270 | 5 m çapta R10 Standard'ları siler (3,5 m'de 75 ≥ 70,7) |

---

### §4 · Dağılım ve nişan

#### 4a · Koni yarı açısı (derece) — üç kamera modu

Tepeden modda nişan düzlemsel; **"odak" tuşu** (sağ fare / sol tetik basılı) ADS eşdeğeridir: koni odak değerine iner, hareket hızı ×0,75, zoom yok (reticle S-09). TP/FP'de gerçek ADS.

| Yuva | Tepeden kalça / odak | TP kalça / ADS | FP kalça / ADS | Pellet deseni notu |
|---|---|---|---|---|
| ar_1 | 1,4 / 0,7 | 1,8 / 0,5 | 1,6 / 0,35 | — |
| ar_2 | 1,0 / 0,45 | 1,3 / 0,3 | 1,15 / 0,2 | burst içi tırmanış §4b |
| smg_1 | 2,8 / 1,6 | 3,5 / 1,1 | 3,2 / 0,8 | — |
| smg_2 | 2,2 / 1,2 | 2,8 / 0,85 | 2,5 / 0,6 | — |
| sg_1 | 7,5 / 5,5 | 8,5 / 4,0 | 8,0 / 3,5 | merkez 0° + halka 3 @ %40 yarıçap + halka 4 @ %100 |
| sg_2 | 5,0 / 3,8 | 6,0 / 2,7 | 5,5 / 2,4 | gauss σ = %45 koni |
| lmg_1 | 2,4 / 1,4 | 3,0 / 1,0 | 2,8 / 0,7 | — |
| lmg_2 | 3,5→1,2 / 2,2→0,8 | 4,4→1,5 / 1,5→0,55 | 4,0→1,4 / 1,1→0,4 | "oturma": 6 atışta taban değere iner (§4b) |
| sr_1 | 3,0 / 0,15 | 3,8 / 0,10 | 3,5 / 0,08 | kalçadan neredeyse işe yaramaz — niyet |
| rl_1 | 1,5 / 0,6 | 1,9 / 0,4 | 1,7 / 0,3 | — |

#### 4b · Bloom, hareket, ADS süresi, recoil (oyun etkisi)

| Yuva | Bloom +°/atış | Tavan ° | Sönüm °/s | Koşarken yayılma × / hız × (ateşte) | ADS s | Dikey kick °/atış (nişan gerçekten kayar) | Yatay desen | İlk N atış | Toparlanma s |
|---|---|---|---|---|---|---|---|---|---|
| ar_1 | +0,40 | 3,5 | 6 | ×1,5 / ×0,92 | 0,25 | 0,55 | desenli: 6 atış sağ-sağ-sol-sol, ±0,25 | 6 | 0,35 |
| ar_2 | +0,30 (burst +0,9) | 2,8 | 7 | ×1,6 / ×0,92 | 0,28 | 0,70 (burst toplam 2,1) | desenli: hafif sağ +0,15 | 3 | 0,30 |
| smg_1 | +0,55 | 6,5 | 9 | ×1,15 / ×1,00 | 0,18 | 0,30 | rastgele ±0,45 | — | 0,25 |
| smg_2 | +0,65 | 5,5 | 8 | ×1,3 / ×0,96 | 0,20 | 0,75 | desenli zikzak ±0,40 | 8 | 0,30 |
| sg_1 | +1,0 | 10 | 10 | ×1,2 / ×0,90 | 0,30 | 2,4 | rastgele ±0,5 | — | 0,55 |
| sg_2 | +1,5 | 8 | 10 | ×1,2 / ×0,92 | 0,28 | 3,0 (2. atış 4,0) | rastgele ±0,6 | 2 | 0,60 |
| lmg_1 | +0,25 | 4,5 | 5 | ×1,9 / ×0,80 | 0,45 | 0,45 | rastgele ±0,5 | — | 0,50 |
| lmg_2 | **−0,40** (oturma; taban 1,2) | 3,5 | tetik bırakınca 4°/s geri açılır | ×2,2 / ×0,70 | 0,55 | 0,90 | desenli sol-sağ ±0,35 | 6 | 0,60 |
| sr_1 | +2,0 | 6 | 8 | ×2,5 / ×0,85 | 0,40 | 4,5 | rastgele ±0,3 | 1 | 0,90 |
| rl_1 | 0 | — | — | ×1,4 / ×0,78 | 0,45 | 6,0 | — | 1 | 1,00 |

Kurallar: (1) TP/FP'de dikey kick nişan ışınını gerçekten yukarı kaydırır; birikim tavanı = 4 × kick, toparlanma süresi sonunda başlangıca döner (oyuncu fareyle bastırabilir). (2) **Tepeden modda** dikey kick'in oyun etkisi 0 (düzlemsel nişan); yatay desen 2B sapma olarak ×0,6 uygulanır; kamera sarsıntısı yalnız görsel (animation-specialist). (3) Bloom kalça ve ADS'de aynı miktarda eklenir, sönüm ateş kesildikten 0,1 s sonra başlar. (4) Hareket çarpanı koşma hızına göre doğrusal (duruş ×1,0 → tam koşu belirtilen ×). (5) ADS geçişinde koni doğrusal interpolasyon; ADS s içinde ateş edilirse ara değer geçerli.

---

### §5 · Launcher rl_1 Bohiya — ek kurallar

| Parametre | Değer | Not |
|---|---|---|
| Roket ilk hız / ivme / azami / itki süresi | 24 m/s / 60 m/s² / 46 m/s / 0,6 s | 0,25 s'de 7,9 m (39 m/s), 0,6 s'de 23,7 m; sonra sabit 46 m/s, yerçekimi 6,0 |
| Yerçekimi | 3,0 itkide / 6,0 sonra | 40 m'de ~1,4 m düşüş: uzak hedefe hafif yukarı nişan gerekir |
| Sapma | rastgele yürüyüş 0,3°/0,1 s (yalpa) | görsel titreşim; 30 m'de ≤ 0,5 m |
| İz dumanı | 1,8 s kalır, oyun etkisi yok | S-07 |
| Patlama yarıçapı iç / dış | 1,8 m tam / 5,0 m sıfır, düşüş üssü 1 (doğrusal) | 2,5 m → 125, 3,0 m → 100, 3,5 m → 75, 4,0 m → 50 |
| Doğrudan isabet + patlama | 110 (isabet alan bölge, çarpanlar geçerli) + 160 patlama | doğrudan hedef 270 alır |
| Bölgesel zırh payı | mevcut kural korunur: patlama %25 / %45 / %30 (Head/Upper/Lower), zırhlı bölge payını zırh yutar | Guard'da kask 22 + gövde 38 + bacak 28 = 88; R10 Guard tam patlamada 160 − 88 = 72 kalır → doğrudan isabet şart, niyet |
| Kendine hasar | patlama hasarının **%45'i** (tavan 72), aynı düşüş eğrisi; oyuncu 1,8 m'de 72, 3,0 m'de 45, 4,0 m'de 22, **≥ 5,0 m sıfır** | zırh yok; ölümcül değil ama bedelli |
| Kurma mesafesi (arming) | 4,0 m: altında roket **kurulmaz** — 30 doğrudan hasar, patlama yok, roket yere düşüp 1,0 s sonra "cız" ile söner | yakın panik atışını cezalandırır ama intihar ettirmez |
| Şarjör / yedek / reload | 1 / 8 / 2,8 s (`rocket_in` t 0,56 ≈ 1,57 s → `rocket_seat` t 0,70 ≈ 1,96 s → hazır; §0 uzlaştırma: animasyon alt sınırı 2,8 s) | boş ile taktik aynı |
| Stagger | 1,0 yarıçap içinde herkese (ragdoll itkisi S-03/S-06) | |
| Kendini patlatma | 70 m veya 2,2 s ömür | havada patlar, alan hasarı geçerli |

---

### §6 · Dükkân, kilit ve dallar (S-04 WP-077 ile birlikte kararlaşır)

**Başlangıç ve kilit sırası (DEC-095: kalıcı meta ağaç, profil XP → meta puan; koşu içi ağaç altınla dal/keystone açar, silah kilidi açmaz):** ar_1 açık. Önerim S-04'ten iki yerde ayrılır: **ar_1 → smg_1 → sg_1 → ar_2 → lmg_1 → sr_1 → smg_2 → sg_2 → lmg_2 → rl_1.** Gerekçe: ilk beş kilit her sınıfın *ilk* sorusunu birer kez öğretir (auto / hacim / koni / burst-kafa / hat tutma); sr_1 yeni bir beceri (hat dizme, öndeleme yok) getirdiği için sınıf *ikinci* cevaplarından (smg_2, sg_2) önce gelir; lmg_2 ve rl_1 güç fantezisi olarak sona kalır. S-04 sırası (sr_1'i 8.'ye, smg_2'yi 6.'ya koyan) de kabul edilebilir — fark yalnız 6–7. sıra.

| Sıra | Yuva | Meta puan bandı | Koşu içi fiyat bandı (altın; WP-077 dükkân silah satarsa) | coreBranches (3) | Keystone (silaha özgü, 1) |
|---|---|---|---|---|---|
| 0 | ar_1 | açık | 0 (başlangıç) | Piercing · FireRate · ReloadSpeed | **Kōdō** (düzen): 6 atış sonrası bloom −%50 |
| 1 | smg_1 | 1 | 500–700 | Ricochet · FireRate · Chain | **Kaze** (rüzgâr): ateş ederken hareket ×1,10 |
| 2 | sg_1 | 2 | 700–900 | Piercing · Explosion · ReloadSpeed | **Manin** (dolu): pellet 8 → 10 |
| 3 | ar_2 | 3 | 900–1100 | Piercing · Ricochet · FireRate | **Kubi** (boyun): kafa ek çarpanı ×1,15 → ×1,5 |
| 4 | lmg_1 | 4 | 1100–1300 | Piercing · Chain · ReloadSpeed | **Nagaya** (uzun ev): şarjör 100 → 150 |
| 5 | sr_1 | 5 | 1300–1500 | Piercing · Ricochet · **ArmorBreak** (yeni) | **Kabuto-wari** (miğfer yaran): kafa isabeti zırhı yok sayar |
| 6 | smg_2 | 6 | 1200–1400 | **Impact** (yeni) · Chain · FireRate | **Tsuchi** (tokmak): stagger 0,45 → 0,7, Runner düşer |
| 7 | sg_2 | 7 | 1400–1600 | Explosion · **Impact** · ReloadSpeed | **Sōhatsu** (çift atış): iki namlu tek tetikte (20 pellet, kick 5°) |
| 8 | lmg_2 | 8 | 1700–2000 | Piercing · **ArmorBreak** · ReloadSpeed | **Ishizue** (temel): oturma 6 → 3 atış, taban 1,2° → 0,9° |
| 9 | rl_1 | 10 | 2200–2500 | Explosion (anlam: yarıçap/hasar, §altı) · **Cluster** (yeni) · ReloadSpeed | **Hibana** (kıvılcım): patlama alanında 3 s yanan zemin (4 HP/s) |

**Dal sinerjisi (ADR-034-1 pellet sözleşmesi korunur):**
- Piercing pellet başına: sg_1/sg_2'de her pellet 1 gövde deler → koni derinlik kazanır; sr_1/lmg_2'de zaten var olan penetrasyona +1 gövde ekler (üst üste biner, kayıp oranı değişmez).
- Explosion pellet-normalize: sg_1'de şans/8, sg_2'de şans/10 — pompalıda zayıf, niyet (pompalı koni zaten alan). **rl_1'de Explosion'un "şans" anlamı anlamsız** (zaten patlıyor) → rl_1 için Explosion dalı **"Blast" anlamına yeniden yazılır**: her seviye iç yarıçap +0,3 m, dış +0,5 m, patlama hasarı +%12. Veri: `branchSemantics.explosion = chance | blast` alanı WeaponDefinition v3'te; kod tek dal enum'u tutar. 🔴 WP-077 ile onay.
- Chain / Ricochet tetik başına (pellet 0): pompalıda çekirdek olarak verilmez (Validate reddeder, aynen). sr_1'de Ricochet doğal (tek mermi, duvardan seken sniper hattı); sr_1'de Chain **verilmez** (penetrasyon zaten hattır). rl_1'de Chain/Ricochet **verilmez**; `Validate` rl_1 için Chain/Ricochet'i reddeder (yeni satır).
- Yeni dal türleri (üç): **ArmorBreak** (zırh çarpanı +%25/seviye; sr_1, lmg_2), **Impact** (stagger +0,15/seviye; smg_2, sg_2), **Cluster** (rl_1: patlamada 2/3/4 küçük bombacık, her biri 40 hasar r 1,5 m, oyuncuya %45 kuralı geçerli). 🔴 S-04 ağaç v2 bu üçünü tanımlarsa `coreBranches` doğrulaması genişler; tanımlamazsa yerine ReloadSpeed/FireRate ile doldurulur (tablodaki keystone'lar kalır).
- FireRate/ReloadSpeed/oyuncu dalları pellet'e dokunmaz (aynen). ar_2'de FireRate burst arası gecikmeyi kısaltır (burst içi 0,05 sabit); sg_1'de ReloadSpeed fişek başına süreyi kısaltır; lmg'lerde kutu süresini.
- Keystone her silahta tek ve koşu içi (altın), ağacın 3. kademesinde açılır (S-04 yerleştirir).

---

### §7 · İki Special (A2) — DEC-094

Özgün ad + kendi modelimiz; mekanik, mantık, his ve sanat yönü referansa **çok yakın** (DEC-094). Referans yalnız kısa not olarak geçer; sayılar bizimdir.

#### 7a · sw_arc **Raiju** — zincir yıldırım

*Referans notu:* Wunderwaffe DG-2 (WaW/BO Zombies): yavaş görünür küre, ilk hedef + 9 sıçrama = 10 zombi, hasar azalmaz, 3/15 cephane, yavaş yarı otomatik, ampul/hücre reload ≈ 6 s (3 s'de iptal), yakın zombiye ateş oyuncuyu çarpar. Bizim sapmalar: 8 hedef, 3/9, reload 3,0 s, Executioner iki küre.

| Parametre | Değer |
|---|---|
| Mermi | elektrik küresi, **14 m/s**, yerçekimi 0, ömür 3 s (42 m), çarpışma yarıçapı 0,35 m; hedefe çarpmazsa yerde 0,3 s "cız" ile söner (zincir yok) |
| İlk hedef | çarptığı zombi ölür (Standard/Runner/Guard kesin, her round) |
| Zincir | en fazla **8 hedef** (1 + 7 sıçrama), sıçrama yarıçapı **6 m**, hedef seçimi **en yakın vurulmamış**, sıçrama gecikmesi 0,08 s (zincir 0,56 s'de tamamlanır), hasar zincir boyunca azalmaz |
| Tier direnci | Standard/Runner/Guard: ölür. **Executioner**: azami HP'nin %60'ı + 2,5 s sersemleme, ikinci küre öldürür; zincir Executioner'dan devam eder |
| Çarpılma tepkisi | 1,2 s elektrik titremesi (ışıklı iskelet), sonra ragdoll düşüş; ölüm/skor/altın titreme sonunda sayılır; titreyen hedef hareket etmez, ısırmaz |
| Şarjör / yedek | **3 / 9** (12 atış) |
| Atış hızı | single; şarj 0,25 s + çevrim 0,9 s (≈ 52 RPM tavan); şarj sırasında tetik bırakılırsa atış iptal, cephane harcanmaz |
| Yakın mesafe riski | zincirdeki herhangi bir hedef oyuncuya **≤ 2,5 m** ise oyuncu **25 HP** alır (atış başına bir kez) + 0,4 s yavaşlama + ekran çarpılma; ilk hedef ≤ 1,5 m ise **40 HP** |
| Reload | taktik 3,0 s / boş 3,6 s — `cell_out` 1,2 s → `cell_in` 2,4 s (ampul/hücre değişimi, S-06); ateş tuşu reload'u kesmez |
| Zırh | bölgesel zırhı yok sayar (elektrik) |
| Ses/VFX kancaları (S-08/S-07) | charge_start, fire, flight_loop start/end, chain_hop (hedef başına), electrocute_start/end, self_shock |

#### 7b · sw_gel **Namekuji** — kaydırıcı jel

*Referans notu:* Sliquifier (BO2 Die Rise): mor jel yay çizer; doğrudan isabet sıradan zombiyi sıvılaştırır (geç roundlarda zayıflar), yere uzun süreli kaygan birikinti bırakır; birikintiye basan zombi kayar ve ölür (her round), ölen zombi yeni birikinti bırakır (zincir); oyuncu birikintide hızla kayar; yarı otomatik, hazne reload ≈ 3 s; cephane ≈ 8–10 / 32–40 (kaynaklar çelişkili). Bizim sapmalar: 8/16, birikinti 25 s ve tavan 12, Guard/Executioner kademesi.

| Parametre | Değer |
|---|---|
| Mermi | jel topu, **22 m/s**, yerçekimi **12 m/s²** (yay; 12° yükselişte ≈ 20 m), çarpışma yarıçapı 0,25 m; tepeden modda imleç uzaklığına otomatik yay (≤ 20 m); duvara/tavana çarparsa aşağı akıp yerde birikinti |
| Doğrudan isabet | Standard/Runner: ölür (sıvılaşma); Guard: R ≤ 10 ölür, R > 10 azami HP'nin %60'ı + devrilme 1,5 s; Executioner: %25 + devrilme 1,5 s |
| Birikinti | isabet noktasında r **1,6 m** (zombi ölümünden r 0,9 m), süre **25 s**; birbirine değen birikintiler birleşmez, süre en uzun olana eşitlenir; eşzamanlı tavan **12** (en eski söner, 0,5 s solma) |
| Zombi birikintide | kayar → 0,9 s düşüş (ragdoll) → **ölür** (Standard/Runner/Guard, her round); Executioner düşer, %20 HP, kalktıktan sonra 3 s bağışık; her kayma ölümü yeni r 0,9 m birikinti (+8 s) → zincirleme |
| Oyuncu birikintide | hız **×1,8**, dönüş tepkisi ×0,35 (kontrol azalır, düşme yok), çıkışta 0,3 s'de normale; hasar yok; ateş edebilir |
| Şarjör / yedek | **8 / 16** (24 atış) |
| Atış hızı | single, çevrim 0,45 s (≈ 133 RPM tavan) |
| Reload | taktik 2,4 s / boş 2,8 s — `canister_out` 0,9 s → `canister_in` 1,9 s |
| Zırh | kayma ölümü ve doğrudan isabet zırhı yok sayar |
| Ses/VFX kancaları (S-08/S-07) | fire, gel_impact (SurfaceKind), puddle_spawn/puddle_end, slide_start/slide_end (zombi ve oyuncu ayrı), liquefy |

#### 7c · Yaşam döngüsü — WP-007 / DEC-024..026 revizyon önerisi

| Kural | Bugün | Öneri | Gerekçe / etki |
|---|---|---|---|
| Ortaya çıkış | round 4'ten şans 0,35 → 0,65, round ortasında, ≥ 22 m | **korunur** | tempo doğru |
| Yerde bekleme | 22 s beacon | **korunur** | |
| Aktif süre | 11 s sabit, sonra kaybolur | **cephane sınırı + azami süre tavanı**: Raiju 3/9, tavan **40 s**; Namekuji 8/16, tavan **45 s**; ikisi de biterse Special düşer, klasik silah döner | referans silahlar mermi sınırlı; 11 s'de reload'lı bir silah anlamsız. Cephane 12 / 24 atış, süre tavanı sürüklemeyi önler |
| Klasik silah askıda | evet | **korunur** (tek eldeki silah) | |
| Aynı anda tek Special | evet | **korunur**; ikincisi düşerse ilk düşer (cephanesi silinir) | |
| Denge tavanı | — | Raiju azami 12 × 8 = 96 öldürme (40 tavanla pratikte ≤ 40 + yeniden doğanlar); Namekuji birikinti zinciri ile ≈ 30–50; ikisi de bir roundun çoğunu bitirir → referanstaki "wonder" hissi; R11+ Executioner'a karşı tam çözüm değil | 30 günlük sim değil, WP-041 sim tablosuna Special sütunu eklenir (S-04 ile) |
| HUD | 11 s halka | halkanın yerine **şarjör / yedek** sayacı + ikincil süre şeridi (Gold400 rezervi aynen) | S-09'a satır |
| DEC | DEC-024..026 | yeni DEC satırı (numara C-00'dan; DEC-092/094/095 alındı → sıradaki) "Special cephane + süre tavanı" — DEC-026 (11 s) yerine geçer | |

---

### §8 · Açık sorular ve riskler

1. 🔴 **Explosion dalının rl_1'de "Blast" anlamı + üç yeni dal (ArmorBreak / Impact / Cluster)** — S-04 ağaç v2 kabul ederse `coreBranches` doğrulaması ve ADR-034-1 satırı genişler; etmezse tabloya göre yedek doldurma (§6).
2. 🔴 **Special yaşam döngüsü:** 11 s sabit → cephane + tavan (§7c). HUD (S-09), DEC satırı ve WP-041 sim etkilenir.
3. 🔴 **Executioner'a karşı Special direnci** (Raiju %60 + sersemleme, Namekuji devirme) — referans "her şeyi öldürür"; tier ağırlığı için bilinçli sapma, DEC-094 "çok yakın" dediği için onay gerekir.
4. 🔴 **Kilit sırası** — S-04 ile fark 6–7. sırada (sr_1 ↔ smg_2); WP-077 (DEC-095 meta ağaç) karar verir.
5. 🔴 **Kendine hasar ve kurma mesafesi rl_1** (%45, tavan 72, 4,0 m kurma) — tepeden modda oyuncu roketin yakınına daha kolay atar; ilk oynanış testinde ölüm oranı > %5 çıkarsa tavan 50'ye iner.
6. Mermi hızı bandı (110–420) DEC-092 60 fps'e göre seçildi (kapandı); Performans kademesi daha düşük kareye inerse kare başına mesafe artar — sayı değişmez, S-07 iz uzunluğu kare değil saniye tabanlı olmalı.
7. lmg_2 "negatif bloom" (oturma) yeni bir eğri türü: `bloomPerShot` negatif + `settleFloor` alanı; kod tarafı tek formülle çözer (clamp), test satırı gerekir.
8. sg_1 fişek fişek reload kesilebilirlik (ateş tuşu reload'u keser, dolan fişekler kalır) — WP-034'te yoktu; S-06 `shell_in[i]` olayı buna izin veriyor, oynanış kuralı burada.
9. Tepeden "odak" tuşu gamepad'de sol tetik: WP-024 haritasında sol tetik boş mu doğrulanmalı (S-09/S-01).
10. Penetrasyon + bölgesel zırh etkileşimi (zırh eritilir, gövde sayar) WP-006 testlerinde yok; test-engineer'a yeni satır: "sr_1 dört gövde, üçüncüsü kasklı".

---

#### WeaponDefinition v3 alan özeti (bu belgeden türeyen veri anahtarları; sözleşme sahibi S-02 uygulama)

`id, displayName, class, role, audioId, fireMode{single|burst|auto}, burstCount, burstInterval, burstDelay, rpm, damage, pellets, pelletPattern{single|centerRing|gauss}, patternParams, magazine, reserve, reloadTactical, reloadEmpty, reloadPerShell, caseType{rifle|pistol|shell|none}, caliberClass{light|medium|heavy|explosive}, silhouetteClass, projectileSpeed, gravity, maxRange, fullDamageRange, minDamagePct, penetrationBodies, penetrationLoss, stagger, headExtraMult, armorMult, spread{topHip, topFocus, tpHip, tpAds, fpHip, fpAds}, bloomPerShot, bloomCap, bloomDecay, settleFloor, moveSpreadMult, moveSpeedMult, adsTime, kickVertical, kickPattern, kickFirstN, recoilRecovery, launcher{v0, accel, vMax, thrustTime, gThrust, gCoast, wobble, blastInner, blastOuter, blastDamage, directDamage, selfDamagePct, selfDamageCap, armingDistance, lifetime}, special{ammoMag, ammoReserve, activeCap, chainMax, chainRadius, chainDelay, selfShockRadius, selfShockDamage, puddleRadius, puddleDuration, puddleCap, slideSpeedMult, slideSteerMult}, coreBranches[3], keystone, branchSemantics, unlockOrder, metaCost, shopPriceBand`.

## §2 · A1 Görsel profiller (art-director)
Girdi: `00-BRIEF.md` · AP-013 konsept v001 (silüet sınıfları, palet, rezerv denetimi) · M4-R1 §0/§7 · stil kılavuzu v1 (Look B: sis #D0D7DE 52→210 m, ıslak taş smoothness ≤ 0,60, Gold400 #D9A93C, Vermilion600 #C4362B) · AP-010 VFX manifest v004 (mevcut `fb_muzzle` 24 f@60, `fb_impact_{armor,stone,wood,earth}`, `fb_explosion` 3,6 m + scorch 2,4 m/12 s, kan A1–A7, beacon sütun/halka). Kullanıcı kararları 24.09 (C-00): **DEC-092** 60 fps hedefi (Yüksek kademe ort ≤ 16,6 ms, p99 ≤ 20 ms, 1080p) · **DEC-093** build tavanı 5 GB · **DEC-094** Special'lar özgün ad + kendi modelimiz, mekanik/his/sanat yönü DG-2 / Sliquifier'e çok yakın. Denge sayıları game-designer'ındır; buradaki her sayı **görsel ölçüdür** (m, px, s, hex, HDR ×). Yuva kimlikleri brief'teki gibi; ad çağrışımı tek kelime, bağlayıcı değil.

### 0 · Beş üst kural (tüm bölümler buna bağlı)
| # | Kural | Ölçüm / eşik |
|---|---|---|
| K1 Rezerv | **Kalıcı** öğe (ekranda ≥ 0,5 s: silah gövdesi, kovan, decal, birikinti, pickup) rezerv denetimine girer: altın bandı (h 35–65°, s > 0,30) ve beacon (s > 0,55 & v > 0,80) **yalnız Special**. **Geçici** VFX (< 0,5 s: flaş, iz, kıvılcım) denetim dışı; ama klasik izler h ≤ 33° (kor) ya da h ≥ 190° (soğuk) tutulur, "altın" okunmaz | piksel sayımı: klasik silah + kovan + decal'de rezerv = 0; iz/flaş hue aralığı dışı |
| K2 Okunurluk | İz kalınlığı ekranda **≥ 2 px**: w_ekran = max(w_dünya × px/m, 2 px). Silüet sınıfı 32 px'te ayrılır; aynı sınıfın iki silahı ≤ 8 m'de (TP/FP) ayrılır | siyah silüet testi 32 px; TP kadraj yan yana testi |
| K3 Islak dünya | Toz yerine **su serpintisi + koyu ıslak parça**; her VFX ışığı (namlu, patlama, ark, jel) ıslak zeminde spekülar parlama üretmeli → ışık zorunlu (URP'de SSR yok, yansımayı ışık taşır) | her ateş/patlama/Special olayında ≥ 1 nokta ışığı (kademe §9) |
| K4 Kara barut | Her atış duman bırakır (erken dönem dünyası); duman α ≤ 0,35, yükselir, oyun alanını örtmez | namlu 1 m çevresi kümülatif α ≤ 0,25 |
| K5 Canlılık (N19) | Klasik silahta **gövde değil olay** canlıdır (iz/flaş HDR ×5–×8, kord tonu, lake tonu çeşidi); Special'da **gövde de** canlıdır (pirinç, cam, jel, ark) | klasik silah ort. v ≤ 0,34; Special gövde altın bandı alanı ≤ %25 |

**Silah çerçevesi — kanonik (T4, 25.09, ölçüldü: `WeaponModelSliceTests.HerSilah_Ejection_AticininSaginda_KovanSagaFirlar`):** Blender'da kök = kabza (tetik), namlu **−Y**, üst **+Z**, atıcının sağı / ejection portu **−X** (sol yan şarjör +X); dışa aktarım FBX `axis_forward −Z`, `axis_up Y`, `FBX_SCALE_UNITS`, Unity'de eksen pişirme kapalı; Unity'de kök = kabza, **+Z namlu, +Y üst, +X = atıcının SAĞI** (ejection portu; kovan soketin +X'ine fırlar).

### §1 · Arsenal görsel dili
1. AP-013 "fantastik erken dönem" (fitil kilidi, serpantin, esmer demir, koyu lake, kord, kutu şarjör) 10 silaha **aynı üç malzeme ailesiyle** genişler; yeni sınıflar yeni malzeme getirmez, yeni **mekanizma parçası** getirir: LMG'de lake ahşap cephane sandığı + demir soğutma kanatları, sniper'da uzun demir dürbün tüpü + sürgü topuzu, launcher'da demir "hiya-zutsu" tüpü + kâğıt/kord sarılı roket.
2. Aynı sınıftaki iki silah **en az 3 eksende** ayrılır: (a) 19,9 m'de ≥ 2 px'lik bir çıkıntı farkı (kafes/şarjör/tepsi/çan), (b) lake tonu Δv ≥ 0,10, (c) kord ailesi (kenevir / çivit / hurma), (d) iz rengi-oranı. Silah 1 = AP-013 mirası (kahve lake + kenevir), silah 2 = daha koyu ya da daha sıcak lake + farklı kord.
3. Lake tonları (hepsi v ≤ 0,34 → açık kimonoya Δv ≥ 0,54): kahve #52443B (24°, 0,28, 0,32) · roiro siyah #26222A (285°, 0,19, 0,16) · tamenuri kehribar #573B24 (27°, 0,59, 0,34) · ao-urushi yeşil-mavi #2E3F3C (170°, 0,27, 0,25). Kord: kenevir #5E5549 · çivit #2F3A5C (224°, 0,49, 0,36) · hurma #8C5A3C (20°, 0,57, 0,55) · kırmızı #803F39 yalnız ince bağ, ≤ %3.
4. Pirinç/altın klasik silahta **yok** (AP-013 kuralı korunur); soluk #8A7F66 ≤ %2 yalnız sr_1 dürbün halkasında AD onayıyla. Rezervin tamamı sw_arc/sw_gel'e.
5. Canlılık nerede: iz başı HDR ×5–×8 ve kuyruk kor turuncusu; namlu flaşı sıcak beyaz; kord/lake tonu çeşidi; ıslak zeminde ışık parlaması; kovanlarda bakır parıltısı. **Emissive yok** klasik silahta (fitil koru hariç: serpantin ucunda 2 mm kor #FF6A2A ×2, yalnız FP'de okunur).
6. Silüet: Long / Medium / Short-thick korunur; **üç yeni sınıf**: Long-X (sniper), Heavy (LMG), Tube (launcher). 32 px kuralı: her sınıf en az bir eksende ayrılır — uzunluk Δ ≥ %15 **ya da** kalınlık Δ ≥ %30 **ya da** ≥ 3 px benzersiz çıkıntı.
7. FP kalitesi (P-07 AP-021; DEC-093 5 GB ile doku/mesh bütçesi rahat): LOD0_FP ≤ 20 000 tri (Special ≤ 26 000: cam/bobin/hazne), **4K** BaseColor + Normal (yalnız elde tutulan silah 4K'da yerleşik, diğerleri mip streaming ile 1K'da), MRA 2K, Emissive yalnız Special 2K; LOD1_TP ≤ 6 000 (2K); LOD2_tepeden ≤ 1 800 (1K). Disk tahmini 12 silah ≈ 0,6 GB (BC7), VRAM'de aynı anda ≤ 2 silah tam çözünürlük. Detay normal: lake çatlağı (craquelure) 3 cm tile, kord örgüsü 1 cm tile. Aşınma: çelik kenar ≤ %8 alan (mevcut), lake dökülmesi ≤ %6, tutuş bölgesinde parlatılmış lake (smoothness +0,15). Su boncuğu: yalnız üst yüzeylerde, smoothness maskesi, geometri yok, FP'de okunur.
8. Gravür: yalnız demirde, normal-only derinlik, motif seigaiha / asanoha / kikkō bantları, silah başına ≤ 2 bant, **yazı/marka/lore yok**. Special'da pirinçte tomoe motifi serbest.
9. Değer kontrastı: silah gövdesi ort. v 0,26–0,34; oyuncu haori v ≈ 0,90 → Δv ≥ 0,55 üç skinde de (AP-013 hükmü sürer). Skin 2 alt kimono (v 0,38) riski flaş + iz ile kapanır.
10. Kamera modları: tepeden = sınıf okunur; TP = silah 2 ayrımı okunur; FP = malzeme/aşınma/mekanizma okunur. Konsept görseli üç ölçekte de denetlenir (§10 devir).

#### 1.1 · Silüet sınıfları (kabza kökü 0; 19,9 m'de 28 px/m)
| Sınıf | Yuva | Boy / gövde kalınlığı (L:T) | 19,9 m'de görünüm | Ayırıcı kural |
|---|---|---|---|---|
| Long | ar_1, ar_2 | 1,20 / 0,07 · 1,10 / 0,07 (≈ 16:1) | 34×2 px / 31×2 px ince çizgi | omuz hizasında en uzun ince koyu çizgi (sr_1 hariç) |
| Long-X | sr_1 | 1,45 / 0,07 (≈ 20:1) + üstte dürbün tüpü 2 px | 41×2 px + üst çizgi | ar'dan ≥ %20 uzun **ve** üstte ikinci ince çizgi |
| Medium | smg_1, smg_2 | 0,85 / 0,09 · 0,72 / 0,09 (≈ 9:1) | 24×3 "L" / 20×3 "T" | göğse yakın kompakt blok; kırık profil |
| Short-thick | sg_1, sg_2 | 0,78 / 0,13 · 0,92 / 0,14 (≈ 6:1) | 22×4 + çan / 26×5 düz kalın | kısa-kalın leke; sg_1 ucu genişler, sg_2 çift ağız düz |
| Heavy | lmg_1, lmg_2 | 1,30 / 0,13 · 1,20 / 0,13 (≈ 9:1) | 36×4 + altta sandık 5 px / 34×4 + üstte tepsi 6 px | uzun **ve** kalın; sandık altta / tepsi üstte |
| Tube | rl_1 | 1,10 / 0,10 tekdüze + baş Ø 0,09 + arka çan Ø 0,14 | 31×3 düz boru + önde 3 px yumru | kundak çizgisi yok; boru + roket başı |
| Special | sw_arc, sw_gel | 1,05 / 0,14 · 0,95 / 0,16 | 29×4 + 3 parlak hücre / 27×5 + parlak hazne | tek parlayan (emissive) silahlar |

### §2 · Silah başına görsel kimlik
| Yuva | Çağrışım | Sınıf · boy | 19,9 m'de ayırıcı öğe | Malzeme dağılımı (lake / demir / kord) | Aksan | P-07 konsept yönü (tek cümle) |
|---|---|---|---|---|---|---|
| ar_1 | Sekizgen | Long · 1,20 m | en uzun ince çizgi + kutu şarjör | kahve lake %40 / esmer demir %50 / kenevir %10 | kırmızı bağ ≤ %3 | AP-013 A'nın FP kalitesinde yeniden üretimi: sekizgen namlu, serpantin, omuz kundağı, kutu şarjör. |
| ar_2 | Kafes | Long · 1,10 m | ön üçte birde delikli demir kılıf (3 px) + muz şarjör "C" | roiro siyah %35 / demir %55 / çivit kord %10 | çivit kord | ar_1'in daha kısa, daha ağır kardeşi: delikli dövme namlu kılıfı, kavisli derin şarjör, demir dipçik plakası, siyah lake. |
| smg_1 | Kırık | Medium · 0,85 m | aşağı kırık yanak kundağı "L" | kahve lake %35 / demir %50 / kenevir %15 | kırmızı bağ ≤ %3 | AP-013 B'nin FP kalitesinde yeniden üretimi. |
| smg_2 | Tepe | Medium · 0,72 m | üstten beslenen yatık şarjör "T", kundaksız (katlı demir çubuk) | tamenuri kehribar %25 / demir %60 / hurma kord %15 | hurma kord | daha kısa, daha hızlı hissi: üst şarjör, katlanır demir omuzluk, kalın kord sargılı çift kabza. |
| sg_1 | Çan | Short-thick · 0,78 m | çan ağız (Ø 0,09) + tüp şarjör | kahve lake %40 / demir %50 / kenevir %10 | kırmızı bağ ≤ %3 | AP-013 C'nin FP kalitesinde yeniden üretimi. |
| sg_2 | Çifte | Short-thick · 0,92 m | üst üste iki düz kalın ağız + menteşe | ao-urushi lake %35 / demir %55 / çivit kord %10 | çivit kord | kırma çifte: iki kalın yuvarlak namlu, demir menteşe bloğu, kısa kalın kundak, yeşil-mavi lake. |
| lmg_1 | Sandık | Heavy · 1,30 m | altta lake cephane sandığı (5 px) + katlı çatal ayak | kahve lake %30 / demir %60 / kenevir %10 | kırmızı bağ ≤ %3 | demir kanatlı ağır namlu, ahşap sandıkta kayış (link), taşıma kulpu, çatal ayak; en "makine" silah. |
| lmg_2 | Tepsi | Heavy · 1,20 m | üstte demir tepsi şarjör (Ø 0,22, 6 px) + kalın soğutma gömleği | roiro siyah %25 / demir %65 / hurma kord %10 | hurma kord | daha kompakt: üst tepsi, delikli soğutma gömleği, ön dikey demir kabza, çatal ayak yok. |
| sr_1 | Mızrak | Long-X · 1,45 m | en uzun çizgi + üst dürbün tüpü + sürgü topuzu | kahve lake %35 / demir %55 / kenevir %10 | soluk #8A7F66 dürbün halkası ≤ %2 (AD onayı) | çok uzun sekizgen namlu, demir dürbün tüpü (lake kapaklı), yanak yükseltisi, top başlı sürgü kolu. |
| rl_1 | Bōhiya | Tube · 1,10 m | düz boru + önde roket başı yumrusu + arka çan | lake omuz eyeri %15 / demir tüp %70 / kord %15 | kord sargı bantları | "hiya-zutsu" ok-roket tüpü: bantlı demir boru, arka venturi çanı, lake omuz eyeri, önden yüklenen roket görünür. |

### §3 · Projectile görsel profili (en kritik)
#### 3.1 · İz paleti (sRGB hex · HDR yoğunluk = doğrusal çarpan; bloom eşiği 1,0 kabul)
| Rol | Baş (çekirdek) | Kuyruk | Ölü kuyruk | Not |
|---|---|---|---|---|
| Kor-light | #FFF1DC ×5 (33°, 0,14) | #FF9A4A ×2 (29°) | #6E2A10 ×0 | ar_1, smg_1, smg_2 |
| Kor-medium | #FFF4E6 ×6 | #FF8A3A ×2,5 (29°) | #6E2A10 ×0 | ar_2, lmg_1 |
| Kor-heavy | #FFF8F0 ×7 | #FF6E2E ×3 (22°) | #5A2410 ×0 | lmg_2 |
| Soğuk-sniper | #F4FAFF ×8 (205°) | #9FC8E8 ×2 | #6C8CA6 ×0 | sr_1; buhar izi #C9D3DB α 0,35 |
| Pellet parıltısı | #FFD9B0 ×2 | — | — | sg_1, sg_2; kısa parıltı, iz değil |
| Roket | alev #FFC070 ×6 | duman §7 | — | rl_1 mesh |
| Ark | #EAF6FF ×10 (200°) | dal #7FD3FF ×4 | hale #A08CFF ×1,5 | sw_arc |
| Jel | #FF5FD2 ×3 (313°, 0,63, 1,0) | kenar #B040FF ×1,5 | kuru #6E3560 mat | sw_gel; beacon rezervinde, Special olduğu için serbest |

#### 3.2 · Silah başına iz
Formüller: iz uzunluğu L = v_mermi × 0,035 s, sınır [1,2 m; 6,0 m] (sr_1: × 0,05 s, ≤ 9 m). Baş boncuğu 0,20 m (heavy 0,30 m) tam yoğunluk; kuyruk üstel sönüm, son %40'ta α ≤ 0,15. Kalınlık dünya ölçüsü, ekranda K2 ile ≥ 2 px. **Parıltı (glint):** her mermi, ince (0,02 m) soğuk-beyaz #E8EEF2 α 0,35, HDR ×1; **iz (tracer):** her N'de bir, 3.1 renkleri.
| Yuva | Renk rolü | Kalınlık (m) | Oran (iz : mermi) | Baş/kuyruk | Mesh | Not |
|---|---|---|---|---|---|---|
| ar_1 | Kor-light | 0,035 | 1 : 3 | 0,20 m baş ×5 → üstel | yok | referans iz |
| ar_2 | Kor-medium | 0,045 | 1 : 2 | 0,20 m baş ×6 | yok | ar_1'den sıcak ve sık: "ağır kardeş" |
| smg_1 | Kor-light | 0,030 | 1 : 3 | 0,15 m baş ×5 | yok | kısa iz (L sınırı 1,2 m'e yakın) |
| smg_2 | Kor-light | 0,030 | 1 : 4 | 0,15 m baş ×4 | yok | yüksek atış hızında lazer görünmesin diye seyrek; parıltı her mermide |
| sg_1 | Pellet | 0,025 | 8/8 parıltı, iz 0 | 0,5 m parıltı çubuğu, 6 m sonra söner | yok | yelpaze deseni okunur; ağır namlu flaşı taşır |
| sg_2 | Pellet + Kor-medium | 0,025 / 0,045 | 8/8 parıltı + merkez pellet iz | merkez 0,20 m baş ×6 | yok | sg_1'den ayrım: tek sıcak iz ortada |
| lmg_1 | Kor-medium | 0,045 | 1 : 4 | 0,20 m baş ×6 | yok | kayış izi ritmi (gerçek kayış 1:5 dili) |
| lmg_2 | Kor-heavy | 0,060 | 1 : 5 | 0,30 m baş ×7 | yok | daha seyrek ama daha kalın/sıcak |
| sr_1 | Soğuk-sniper | 0,070 | 1 : 1 | 0,30 m baş ×8, kuyruk 9 m | yok | + buhar izi: 0,05 m şerit, 0,7 s kalır, α 0,35 → 0, rüzgârla 0,3 m/s sürüklenir |
| rl_1 | Roket | mesh Ø 0,06 | — | itki alevi 0,45 m | **evet** (§7) | duman izi §7 |
| sw_arc | Ark | küre Ø 0,25 | — | — | **evet** (§8) | plazma kabuğu + yer arkları |
| sw_gel | Jel | damla 0,18 m | — | 3–4 damlacık kuyruğu | **evet** (§8) | yerçekimi yayı, ıslak parlaklık |

#### 3.3 · Üç kamera modunda okunurluk
| Mod | Ölçek | Kural |
|---|---|---|
| Tepeden | 28 px/m @ 19,9 m | K2 → tüm izler 2 px; ayrım **uzunluk + renk + oran + baş parlaklığı** ile; baş boncuğu 6 px nokta; parıltı 1 px'e düşerse α 0,5'e çıkar (min görünürlük) |
| TP | ≈ 4× tepeden | dünya kalınlıkları geçerli; kalınlık farkları okunur (sr_1 0,07 vs smg 0,03) |
| FP | göze yakın | iz **namlu ucundan** doğar (fp_socket), kameradan değil; ilk 0,4 m'de α 0 → 1 rampası; kameraya < 1,5 m'de HDR ×0,3, 3 m'de ×1 (yüzde beyaz leke yok); kalınlık dünya ölçüsüyle sınırlı (2 px kuralı FP'de zaten aşılır); sniper ADS'de buhar izi belirgin |
| Islak zemin | tüm modlar | iz ışığı yok (maliyet), yansımayı namlu ışığı + darbe ışığı taşır; sr_1/rl_1/Special mermileri nokta ışığı taşır (§9) |

### §4 · Namlu (muzzle) profili
| Sınıf | Flaş biçimi | Boy × çap (m) | Süre | Duman pufu | Işık (renk / yoğunluk / menzil / süre) |
|---|---|---|---|---|---|
| Long (ar) | 4 loblu yıldız + merkez koni | 0,55 × 0,35 | 6 kare @60 (0,10 s) + 0,3 s tel | Ø 0,30 → 0,60, 0,8 s, α 0,35 → 0, 0,4 m/s yükselir | #FFC28A / 6 / 4 m / 2 kare + 80 ms sönüm |
| Medium (smg) | yuvarlak kısa çiçek | 0,35 × 0,30 | 5 kare (0,08 s) | Ø 0,25 → 0,45, 0,6 s | #FFC28A / 5 / 4 m / aynı |
| Short-thick (sg) | çan: geniş kısa çiçek + **duman halkası** (vortex) | 0,50 × 0,60 | 7 kare (0,12 s) | halka Ø 0,40 → 0,90, 1,0 s, α 0,40 | #FFB878 / 12 / 7 m / 3 kare + 120 ms |
| Heavy (lmg) | haç (flaş gizleyici kafes) + koni | 0,60 × 0,40 | 6 kare, auto varyant | Ø 0,35 → 0,70, 0,8 s; auto'da namlu ucunda pus | #FFC28A / 9 / 6 m / sürekli varyant |
| Long-X (sr) | uzun mızrak + yan taç yaprakları | 1,20 × 0,40 | 8 kare (0,14 s) | Ø 0,45 → 1,00, 1,4 s, α 0,40 | #FFD0A0 / 18 / 10 m / 4 kare + 200 ms |
| Tube (rl) | ön ateş çiçeği + **arka geri tepme konisi** | ön 0,80 × 0,80; arka 2,5 m koni | 9 kare (0,15 s); arka toz 0,6 s | arka: duman + ıslak toz #B8B0A6 α 0,45 | ön #FFB070 / 24 / 12 m; arka #FFA060 / 12 / 6 m |
| sw_arc | halka deşarj (3 uçlu elektrot arası) | Ø 0,50 | 0,20 s + 0,5 s titreme | duman yok, ozon pusu α 0,15 | #7FD3FF / 20 / 9 m / 0,25 s |
| sw_gel | nozul sıçraması + damla perdesi | Ø 0,30 | 0,15 s | duman yok, 6 damlacık | #FF5FD2 / 6 / 4 m / 0,20 s |

Otomatik ateşte (≥ ~600 RPM): `fb_muzzle` parlak yaşam (f0–7) üçe bölünür, her atış 4 karelik alt dizi seçer, her 4. atış "büyük" (tam sayfa); ışık atışlar arasında tamamen sönmez (0,6–1,0 titreme); duman tek tek puf değil, namlu ucunda 1 m yarıçaplı pus birikir (K4 tavanı α 0,25), ateş kesilince 1,2 s'de dağılır. Serpantin fitili her atışta 0,2 s parlar (#FF6A2A ×3, FP'de okunur). Işık ıslak zeminde spekülar parlama verir — Look B'de "ateş ettim" okumasının yarısı bu parlamadır; ışıksız atış yok (Performans'ta bile her 2. atış).

### §5 · Kovan profili
Dünya kararı: pirinç yok → **esmer bakır tabanlı balmumlu kâğıt kartuş**: kısa bakır taban #6B4A3A (18°, 0,45, 0,42) + kavrulmuş kâğıt gömlek #8C7A66 (30°, 0,27, 0,55), ağız ucu is #3A2E28. Rezerv denetimi: bakır 18° altın bandı dışı; kâğıt s 0,27 < 0,30. Pompalı fişek: hurma kâğıt gövde #8C5A3C + bakır taban. LMG: kovanla birlikte dövme demir **link** (#2E3034) düşer. Metalik 0,9 / smoothness 0,55 → ıslak zeminde küçük bakır parıltısı (K5 canlılık).
| Yuva | Tür | Boy (m) | Fırlatma (yön / hız / dönüş) | Olay | Yerde kalma (Sinematik/Yüksek/Perf) |
|---|---|---|---|---|---|
| ar_1, ar_2 | rifle | 0,055 | +X 35° yukarı, 10° geri / 3,0 m/s / 12 dev/s | eject | 8 / 5 / 3 s |
| smg_1, smg_2 | pistol | 0,035 | +X 40° yukarı / 2,5 m/s / 15 dev/s | eject | 8 / 5 / 3 s |
| sg_1 | shell (kâğıt fişek) | 0,065 Ø 0,020 | +X 45° yukarı / 2,0 m/s / 6 dev/s | pompa (eject) | 10 / 6 / 3 s |
| sg_2 | shell ×2 | 0,065 | +Y açılınca arkaya 1,5 m/s (kırma) | eject (reload'da) | 10 / 6 / 3 s |
| lmg_1, lmg_2 | rifle + link | 0,055 + link 0,03 | kovan +X 30° / 3,5 m/s; link −Y düşer | eject | 8 / 5 / 3 s (havuz büyük) |
| sr_1 | heavy (uzun bakır) | 0,085 Ø 0,014 | bolt_back'te +X 25° / 1,8 m/s / 4 dev/s | bolt_back | 12 / 8 / 4 s |
| rl_1 | none | — | — | — | — |
| sw_arc | none (hücre kaseti değişir) | — | — | cell_out/in | — |
| sw_gel | none (hazne değişir) | — | — | canister_out/in | — |
Fizik: tek sekme (restitution 0,3), ıslak taşta sekme sesi (S-08), sonra durur; havuz 96 / 48 / 24. FP'de kovan kameraya ≥ 0,25 m yaklaşmaz (küçük itme).

### §6 · Darbe (impact) profili — yüzey × kalibre
Kalibre ölçekleri: light ×0,7 · medium ×1,0 · heavy ×1,6 · explosive = §7 patlama katmanları (yüzey burada yalnız kalıntı türünü seçer). Decal (bullet hole) çapı: light 0,06 m / medium 0,09 / heavy 0,14; kalma 12 s, havuz §9; α ≤ 0,85 (kan kuralıyla aynı). **Islak dünya kuralı:** toz yerine su serpintisi (#D9E1E6 α 0,5, 0,15 s) + koyu ıslak parça; kuru toz yalnız kapalı alan/Dirt iç mekânda (WP-074 yüzey bayrağı). Darbe ışığı: yalnız Metal/Armor kıvılcımı ve heavy/explosive'de (#FFE0B0 / 3 / 2 m / 2 kare).
| Yüzey | light | medium | heavy | explosive |
|---|---|---|---|---|
| Default | gri puf 0,2 m + 2 parça; delik 0,06 | puf 0,3 + 4 parça; delik 0,09 | puf 0,45 + 6 parça; delik 0,14 | kalıntı: gri parça + scorch |
| Stone (ıslak arduvaz) | su serpintisi + 2 taş kırıntı + 1 kıvılcım; delik koyu ıslak halkalı 0,06 | serpinti + 4 kırıntı + 2 kıvılcım; 0,09 | serpinti + 7 kırıntı + 4 kıvılcım + `fb_impact_stone`; 0,14 | taş parçaları 0,05–0,20 m ×12 + scorch 2,4 m |
| Wood (lake ahşap) | 4 kıymık + 1 lake pulu; delik 0,05 (açık damar) | 8 kıymık + 2 lake pulu, `fb_impact_wood`; 0,08 | 12 kıymık + 3 pul + kırık kenar; 0,12 | kalas parçası ×6 + yanık kenar decal |
| Foliage | 3 yaprak titremesi + yeşil-gri kırıntı; decal yok | 6 yaprak + su damlası (ıslak yaprak) | 10 yaprak + dal kırıntısı | yaprak patlaması ×30 + 1,2 s salınım |
| Water | sütun 0,25 m + halka 0,6 m; decal yok | sütun 0,4 + halka 0,9 | sütun 0,7 + halka 1,2 + damla yağmuru | kubbe 2,5 m + halka 4 m + 1,5 s damla yağmuru |
| Metal | 6 kıvılcım #FFE0B0 ×4 + çınlama (S-08); çukur decal 0,04 normal-only | 10 kıvılcım + sekme çizgisi (ricochet izi); 0,06 | 16 kıvılcım + darbe ışığı; 0,08 | kıvılcım fıskiyesi ×40 + is decal |
| Dirt (ıslak toprak) | çamur damlası ×4 + koyu tokmak; iz 0,07 | çamur ×8 + `fb_impact_earth`; 0,10 | çamur ×14 + su birikintisi sıçraması; 0,15 | çamur kraterı decal 1,8 m + kesek ×10 |
| Paper (shōji) | yırtık delik 0,10 + 2 kâğıt parçası uçuşur | yırtık 0,15 + 4 parça | yırtık 0,22 + 6 parça | panel parçalanır (WP-074 kırılabilir bayrağı) |
| Flesh | kan A1 splash_01..03 + 6 damlacık (WP-019) | splash_04..06 + 10 damlacık | heavy_01..02 + 16 damlacık + yer izi | death pool + heavy (mevcut sistem; ek gore yok) |
| Armor (lake zırh) | `fb_impact_armor` ×0,7: 1 lake kıymığı + 3 kıvılcım | tam sayfa + darbe ışığı | ×1,6 + 2 ek kıymık + çınlama | zırh kırılma sayfası (WP-006 olayı) + kıvılcım |
Ricochet dalı: Metal/Stone'da sekme çizgisi = kısa kor iz (0,6 m, ×3, 0,08 s). Piercing: çıkış yüzeyinde aynı darbe ×0,6. Et darbesi izi zombide kalır (mevcut kan), silah darbesi decal'ları statik yüzeylerde.

### §7 · Launcher rl_1
| Katman | Tanım |
|---|---|
| Roket mesh | "bōhiya" ok-roket: dökme demir ogive baş Ø 0,09 (kord sarılı boyun), kâğıt+kord sarılı barut gövdesi Ø 0,06 × 0,40 m (#8C7A66 kâğıt, kord bantları), kuyrukta 3 lake ahşap kanatçık (kahve lake), arkada fitil koru. Toplam 0,55 m; ≤ 600 tri; FP'de yüklü roket tüpün ağzından 0,12 m görünür. |
| İtki alevi | koni 0,45 m × Ø 0,12, #FFC070 ×6 çekirdek + #FF7A3C ×2 kenar, 8 karelik döngü + kıvılcım damlaları (6/s, 0,3 s, yerçekimli) |
| İz dumanı | başlangıç Ø 0,15 → 0,90 m (2,5 s); renk sıcak gri #B8B0A6 (ilk 0,5 s, alev aydınlatması) → soğuk #8E949A; α 0,45 → 0; kalma 3,5 / 2,0 / 1,0 s (§9); rüzgâr vektörüyle sürüklenir (WP-075 atmosfer rüzgârı) + 0,3 m/s yükselir; ıslak zeminde alçak geçişte zemin yansımasını roket ışığı taşır |
| Roket ışığı | #FFB070 / 8 / 5 m, uçuş boyunca (Sinematik/Yüksek) |
| Patlama katmanları (sıra, süre) | 1 flaş 1 kare beyaz-turuncu ×12 Ø 2 m → 2 ateş topu `fb_explosion` 3,6 m (1,6 s) → 3 şok halkası zemin 0,3 s (mevcut) → 4 kor çizgileri ×24 (0,8 s, yerçekimli, ıslak zeminde sönerken cızırtı) → 5 toz/su sütunu Ø 1,5 → 3 m (1,2 s, ıslak: su serpintisi + koyu parça) → 6 kalıntı (yüzeye göre §6 explosive) → 7 **buhar** (Look B'ye özgü: ıslak taştan 1,2 s beyaz buhar tutamları α 0,25) → 8 zemin yanığı scorch 2,4 m / 12 s (mevcut) + ıslak halka koyu 3,0 m / 6 s |
| Patlama ışığı | #FFB070 / 40 / 9 m; 0,08 s tepe → 0,35 s sönüm; Sinematik'te gölge açık |
| Kamera / his | S-07: FP'de 0,25 s sarsıntı, tepeden 0,15 s; yakın patlamada 0,3 s çamur/su lekesi ekran kenarı (kısa) |

### §8 · İki Special sanat yönü (A2, DEC-094: özgün ad + kendi modelimiz; his/sanat yönü orijinale çok yakın)
Ortak: Special'lar rezervin sahibi — pirinç #B08A3C (42°, 0,66, 0,69) gövde alanı ≤ %25; koyu kiraz lake kundak #4A2A24 (7°, 0,52, 0,29); cam: smoothness 0,95, kalın kenar kırılması; her ikisi de **emissive taşıyan tek silahlar**. Pickup: mevcut beacon sütunu Gold400 kalır (Special = altın HUD kodu), **zemin halkası tür rengini** alır (ark cyan / jel magenta); silah yerde parlar. Reload'da hücre/hazne değişimi = cephane görselleştirme (cam içindeki yük azalır).

#### 8.1 · sw_arc (zincir yıldırım; referans DG-2)
Orijinal referans notu: ahşap kundak + pirinç gövde + üstte üç cam tüp + bobinler + üç uçlu emitör; mavi elektrik topu, hedeften hedefe yıldırım, zombi kasılıp düşer. Bizim farkımız: Edo malzeme dili (kiraz lake, fener camı, kumihimo sarılı bakır, mitsudomoe halka), **iki renkli yük mantığı** (depolanan yük kehribar = HUD Special altını; deşarj cyan) ve ıslak zemin iletkenliği (yer arkları).
| Öğe | Görsel karar |
|---|---|
| Silah (1,05 m) | kiraz lake kundak + pirinç gövde plakaları; üstte **3 cam hücre** (fener camı, demir kapaklı, Ø 0,06 × 0,16 m) sıra halinde; namlu çevresinde kumihimo sarılı bakır bobinler; ağızda **mitsudomoe demir halka** içinde cam küre elektrot (Ø 0,08) |
| Hazır durum | hücreler **kehribar** (#FFC65A ×2, HUD Gold400 ile aynı aile = "dolu Special"), bobinlerde 1 Hz zayıf cyan titreme; FP'de hum ile senkron parıltı |
| Ateş / küre | Ø 0,25 çekirdek #EAF6FF ×10 + Ø 0,45 plazma kabuğu #7FD3FF ×4 dalgalanır + 0,6 m tel uçlar (4–6, 0,1 s ömür); küre ışığı #7FD3FF / 8 / 6 m; **ıslak zeminde altında yer arkları** (0,5 m halka, 3 kısa ark / 0,1 s) |
| Zincir yayı | çekirdek 0,06 m ×8 + hale 0,25 m ×1,5 (#A08CFF kenar); segment başına 2–4 dal (%25–40 uzunluk, 0,02 m); sıçrama başına 0,12 s, hedefler arası 40 ms kademe, 3 yenileme titremesi; uç noktada 12 kıvılcım |
| Çarpma tepkisi (zombi) | deri materyalinde **rim emissive cyan ×3** 0,5 s titreme, gözler beyaz ×6; baş–omurga–leğen–eller arasında 5 noktalı iç arklar (0,02 m) — "iskelet parıltısı" hissini rim + eklem arkları verir, ayrı iskelet mesh'i yok; ayaklarda ıslak zemine 0,4 m ark halkası; sonra WP-076 ölüm (ragdoll seğirmesi S-06) |
| Reload (cell_out/in) | 3 hücre **lake kaset** olarak yana kayar (sönük, is lekeli), yeni kaset oturur, hücreler 0,4 s'de kehribara ısınır (×0 → ×2) + hum yükselir |
| Cephane görseli | her hücre = cephanenin üçte biri; boşalan hücre kararır, camda is |
| Pickup / beacon | beacon sütunu Gold400 + zemin halkası cyan; yerde hücreler kehribar, 1 Hz'de zemine 0,3 m kısa ark (ıslak taş iletken) + çıtırtı (S-08) |

#### 8.2 · sw_gel (kaydırıcı jel; referans Sliquifier)
Orijinal referans notu: derme çatma tank + hortum + pompa + pirinç bağlantılar; canlı pembe/mor sıvı topu, parlak süreli birikinti, zombiler kayar/düşer/sıvılaşır, oyuncu da kayar. Bizim farkımız: sake şişesi dilinde demir bantlı cam hazne, **hazne seviyesi = cephane**, ıslak zeminden daha parlak birikinti (smoothness 0,92 > 0,60) ve süre sonunda görünür **kuruma** evresi.
| Öğe | Görsel karar |
|---|---|
| Silah (0,95 m) | kiraz lake kundak + demir gövde; üstte/yanda **cam hazne** (silindir Ø 0,16 × 0,32, demir bantlı, pirinç vana), el pompası kolu, nozul = geniş pirinç çan + damlalık; hortum kord sarılı |
| Jel rengi | **karar: DEC-103** (kullanıcı 30.09, sadakat) — **sadakat önerisi:** canlı magenta #FF5FD2 (313°) + mor kenar #B040FF; gerekçe: beacon rezervi zaten Special'ın, Look B'nin soğuk gri-yeşilinde tam tamamlayıcı okunur, DEC-094 "sanat yönü çok yakın"; alternatif dünya uyumu = cıva-yeşil #7CFF9A (Foliage ile karışır, önermem) |
| Hazne | jel seviyesi = cephane (shader sıvı seviyesi, 0,5 Hz çalkantı, kabarcık 2/s), emissive ×1,5, cam kenarında magenta kırılma; ışık #FF5FD2 / 2 / 1,5 m sürekli (FP kolda parlar) |
| Damla mermisi | 0,18 m uzamış damla mesh (vertex wobble), smoothness 0,95, emissive ×3, 3–4 damlacık kuyruğu (0,3 s), yerçekimi yayı; ışık #FF5FD2 / 3 / 3 m |
| Birikinti | düzensiz splat decal 1,6–2,2 m (3 varyant + döndürme), smoothness 0,92 (ıslak taşın 0,60'ından yüksek → "ıslaktan ıslak" okunur), emissive ×0,6 + 0,3 Hz nabız, kabarcık 3/s; yaşam T (GD): %0–10 yayılma 0,6 → tam, %10–80 canlı, %80–100 **kuruma** (emissive → 0, renk → #6E3560 mat, kenarlar çatlar), sonra 2 s solma |
| Kayma ipuçları (zombi) | ayak altında iki magenta sürtme çizgisi (0,04 m, 0,6 s), 4 damlacık fırlar; ragdoll düşüş (WP-071) sırasında gövdede jel lekeleri; jelle ölümde **erime**: dissolve kenarı magenta ×4, 0,8 s, geride 0,6 m jel lekesi birikintiye katılır (Sinematik/Yüksek; Perf: anlık düşüş + splat) |
| Oyuncu kayarken | iki paralel iz (0,04 m, 1,0 s), ayaktan damlacık; S-07 kamera hafif yatış; FP'de ekran alt kenarında 0,3 s magenta parıltı |
| Reload (canister_out/in) | boş hazne (kalıntı film, mat) çıkar, dolu hazne çalkalanarak oturur, seviye 0,3 s'de yükselir |
| Pickup / beacon | beacon sütunu Gold400 + zemin halkası magenta; nozuldan 1 damla / 2 s, yerde 0,1 m parlak nokta (3 s) |

### §9 · Kalite kademeleri (DEC-092: 60 fps)
Kademe bütçesi: **Yüksek = varsayılan hedef** (kare ort ≤ 16,6 ms, p99 ≤ 20 ms, 1080p, referans Mac); **Sinematik** kare tavanı ≤ 25 ms (kullanıcı tercihiyle 40 fps kabul); **Performans** ≤ 8,0 ms (eski DEC-088 düzeyi, M3 taban). Arsenal VFX payı (iz + darbe + duman + ışık + kovan + Special, 40 zombi + 256 mermi tavanında) önerisi: Sinematik ≤ 4,0 ms · Yüksek ≤ 2,2 ms · Performans ≤ 0,8 ms; S-07 ölçer, aşan öğe bir alt kademe değerine iner. Kimlik kesilmez: iz oranları, renkler, "her atışta flaş", "her isabette darbe" üç kademede aynı; kesilen şey **yoğunluk, ömür, ışık, havuz**. Her satır S-07'de kademe başına ms olarak ölçülür ve raporlanır (R1 §4.7).
| Öğe | Sinematik (≤ 25 ms) | Yüksek (≤ 16,6 ms, varsayılan) | Performans (≤ 8,0 ms) |
|---|---|---|---|
| İz (tracer) | tasarım oranı, baş bloom tam | aynı | aynı, bloom yarı yoğunluk (×0,5 HDR) |
| Parıltı (her mermi) | açık | açık | kapalı (yalnız iz) |
| Mermi ışığı | sr_1, rl_1, sw_arc, sw_gel, lmg heavy iz | sr_1, rl_1, Special | yalnız rl_1 + Special küre |
| Namlu ışığı | her atış + gölge | her atış, gölgesiz | her 2. atış, gölgesiz |
| Eşzamanlı VFX ışığı tavanı | 8 | 4 | 2 |
| Namlu dumanı | 0,8 s + auto pusu | 0,5 s + pus | 0,25 s, pus yok |
| Kovan | 8–12 s, havuz 96, fizik sekme | 5–8 s, 48 | 3–4 s, 24, sekme yok |
| Darbe parçacık | ×1,0 | ×0,7 | ×0,4 |
| Darbe decal | havuz 256, 12 s | 128, 12 s | 64, 8 s |
| Darbe ışığı | Metal/Armor/heavy | heavy yalnız | yok |
| Roket dumanı | 3,5 s, Ø 0,9 | 2,0 s | 1,0 s |
| Patlama katmanları | 8 katman + buhar + gölge | 7 (buhar yok) | 5 (kalıntı, buhar yok; kor ×12) |
| Ark dalları / çarpma | 4 dal, rim + eklem arkları + yer halkası | 3 dal, rim + eklem | 2 dal, yalnız rim |
| Jel | kabarcık + nabız + erime | kabarcık + nabız | düz emissive, erime yok |
| Projectile mesh | mesh her mesafede | mesh ≤ 25 m, sonra billboard | mesh ≤ 15 m |
| Sniper buhar izi | 0,7 s | 0,5 s | 0,3 s |
Ölçülebilir öneri (perf kısmadan): (1) tüm izler tek GPU-instanced ribbon materyali, silah farkı yalnız vertex color + parametre; (2) darbe decal'ları yüzeye göre 4 atlas, tek çekim; (3) VFX ışıkları öncelik kuyruğu (yakın kamera > uzak), tavan aşımında en eski söner; (4) duman/pus tek "haze" parçacık sistemi, namlu başına emitter değil.

### §10 · Açık sorular
| # | Soru | Öneri / karar sahibi |
|---|---|---|
| 1 ✅ | Jel rengi: orijinale sadakat (magenta #FF5FD2) mi, dünya uyumu mu? | **karar: DEC-103** — sadakat (kullanıcı 30.09) |
| 2 🔴 | Special gövdesinde pirinç alanı tavanı ≤ %25 — HUD altın anlamı korunuyor mu? | %25 kabul; C-00/S-09 |
| 3 🔴 | Kara barut duman kimliği (K4): tepeden okunurluk için α ≤ 0,35 / pus ≤ 0,25 yeterli mi, yoksa duman tamamen kapalı mı? | kimlik olarak açık; kullanıcı oynayınca karar |
| 4 🔴 | Kovan malzemesi: bakır-kâğıt kartuş (öneri) mi, kovansız (fitil dünyası saf) mı? | bakır-kâğıt (eject olayları ve K5 canlılık); kullanıcı |
| 5 🔴 | sw_arc "iskelet parıltısı": rim + eklem arkları (öneri, ek mesh yok) yeterli mi, yoksa ayrı iskelet sprite katmanı isteniyor mu? | rim + eklem; kullanıcı |
| 6 | FP LOD0 20 000 tri / 4K BaseColor+Normal (DEC-093 5 GB içinde ≈ 0,6 GB): mip streaming ile VRAM'de ≤ 2 silah tam çözünürlük tutulabiliyor mu; WP-015 `ArtImportContract`'a `weapon_fp` 4K rolü eklenir mi? | P-07 + unity-specialist doğrular (WP-015 sözleşme güncellemesi) |
| 7 | K2 ekran-uzayı 2 px kalınlık kuralı ribbon shader'da uygulanabilir mi (kamera mesafesine göre genişleme)? | S-07 fizibilite; olmazsa tepeden modda dünya kalınlığı ×2,5 |
| 8 | Islak zemin yansıması: yalnız ışık (öneri) mi, Sinematik'te düzlemsel yansıma da mı? | yalnız ışık; düzlemsel yansıma WP-075 kapsamında ölçülür |

Devir (P-07 AP-021'e): her silah için konsept görseli üç ölçekte denetlenir — 32 px silüet (sınıf), TP kadraj (aynı sınıf ayrımı, §2 satırı), FP yakın çekim (§1.7 malzeme/aşınma/gravür kuralları); rezerv denetimi AP-013 §4 listesiyle (klasik: altın/beacon piksel 0; Special: pirinç ≤ %25, emissive yalnız hücre/hazne/jel).

## §3 · A1 Ses kimlikleri (sound-designer)
Kaynaklar: `00-BRIEF.md` (S-06 mekanizma olayları, S-08 istekleri), mevcut `AudioSoundId` / `CombatAudioEvent` / `SurfaceKind` sözlüğü, `Audio/README.md` (bus: Weapon/Impact/Target/Interface/Ambience/Music; World 16/32 · Signal 8 voice; SFX −18 LUFS). Sayılar (RPM, hasar) game-designer'ındır; buradaki RPM/mod değerleri **ses kararı için varsayım**, GD sayıları belirleyince döngü kararı §2 kuralıyla yeniden okunur. Üretim S-08 WP-072 (35 kr). Koda yazılmadı.

### §1 · Ses dili

1. **"Gerçekçi metalik" = gerçek malzeme fiziği, modern silah kaydı değil.** Her ateş dört katman: mekanik tık (0–15 ms) → gövde "punch" (15–60 ms) → namlu patlaması (40–200 ms) → kuyruk/yankı (200 ms – 2 s). Mekanik tık her zaman patlamadan **önce** duyulur; silahı "makine" yapan budur.
2. **Kara barut karakteri:** modern nitro barutun kuru "çat"ı yerine daha geniş, alçak-orta şişkin (120–250 Hz) ve dumanlı "POF-BAM"; ama transient sıkı tutulur (oyun temposu için 40 ms'de zirve). Fitil kilidi üçlemesi (serpantin düşer → tava alevlenir → ana şarj) **tek** vuruşa sıkıştırılır: tık + 8–12 ms "fıs" + patlama.
3. **Demir mekanizma:** esmerleştirilmiş demir = mat, kısa çınlama (≤ 80 ms), çelik yay "tınk". **Pirinç/bronz çınlaması klasiklerde YOK** — görsel altın rezervi gibi parlak metal çınlaması da **Special'a ayrılmıştır** (sw_arc bobin/cam, sw_gel bakır hazne).
4. **Lake ahşap gövde rezonansı:** her atışın kuyruğunda kundak/gövde "tok"u (180–400 Hz, 60–120 ms); FP'de omuz teması olarak da duyulur. Silah büyüdükçe rezonans aşağı kayar (SMG 380 Hz → SR 190 Hz).
5. **İlk 50 ms'de tanınma ilkesi:** sınıf = mekanik tık karakteri + gövde frekans merkezi + tempo. Aynı sınıfın iki silahı **kardeş, ikiz değil**: aynı tık ailesi, farklı kalibre/namlu uzunluğu (≥ 3 yarım ton merkez farkı, farklı kuyruk uzunluğu).
6. **Kalibre sınıfı = darbe sesi büyüklüğü** (light/medium/heavy/explosive), GD ile aynı mantık; darbe sesi silahtan değil kalibreden türetilir (aynı yüzey, aynı kalibre → aynı aile, silah `audioId` yalnız varyant seçer).
7. **Yorulma:** ateş sesleri ≥ 4 varyant + ardışık tekrar engeli; auto döngüde sürdürme katmanı granüler (atış başına yeni tane), 6 s'den uzun sürekli ateşte kuyruk gain'i −3 dB (sürekli ateş "duvar" olmasın).
8. **Karışım yeri:** ateş Weapon bus (limiter −8 dB/10 ms/90 ms korunur), darbe Impact, et/zırh Target. Ateş, darbeleri 0,35 s kısar (mevcut kural); Special ateşi tüm World'ü 0,5 s kısar.

### §2 · Silah başına ses kimliği

#### 2a · Kimlik, mod, döngü, kalibre, kovan, kuru tetik

| Yuva | `audioId` | Ateş modu / RPM (varsayım) | Döngü kararı | Kalibre | Kovan · yere düşme | Kuru tetik |
|---|---|---|---|---|---|---|
| ar_1 | `AR1` | auto ~510 (A göç) | **tek tek** (< 600); atış başına one-shot, kuyruk üst üste biner | medium | rifle · taş üstünde "tınk-tık" 2 sıçrama | serpantin boşa düşer: "tık-çıt", tava kuru "fıs" |
| ar_2 | `AR2` | burst-3 ~700 (burst içi) | **tek tek** (burst = 3 one-shot + burst kuyruğu; auto değil) | medium (ağır uç) | rifle (uzun) · daha ağır "tonk" | ağır horoz "TAK", boş |
| smg_1 | `SMG1` | auto ~780 (B göç) | **döngü**: başla / sürdür / bırak | light | pistol · hafif "çıt-çıt" yağmur gibi | ince "tik", yay geri |
| smg_2 | `SMG2` | auto ~1000, çift tambur | **döngü**, sürdür katmanı "dikiş makinesi" | light | pistol · yoğun, düşük gain | çift "tik-tik" (iki yuva) |
| sg_1 | `SG1` | pompalı tek ~1,2/s | tek tek + pompa foley ateşten 180 ms sonra | heavy | shell · kartondan "top" düşüşü | pompa boşta "klak-klak", tetik "tık" |
| sg_2 | `SG2` | kırma çift namlu, 2 atış | tek tek; ikinci atış ilkine kısa üst üste binebilir | heavy | shell (yalnız `eject`'te, 2 kovan) | tetik "tık" + boş yuva çınlaması |
| lmg_1 | `LMG1` | auto ~640, şerit/kutu | **döngü** + şerit bağı tıkırtısı katmanı | medium | rifle + link · sürekli "şıkır" halısı | kol boşta "ÇAK" |
| lmg_2 | `LMG2` | auto ~420, ağır kalibre | **tek tek** (her atış tek "balyoz"; < 600) | heavy | rifle (büyük) · "TONK" tek sıçrama | ağır kol "ÇANK" |
| sr_1 | `SR1` | tek, sürgülü | tek tek; her atış sonrası sürgü dizisi (§3) | heavy | rifle (uzun) · gecikmeli tek "tınnk" | sürgü "tık", ince yay |
| rl_1 | `RL1` | tek, tüp | tek tek + uçuş döngüsü (§4) | explosive | none (arka üfleme yerine geçer) | tetik "tak" + fitil boşa "çıt" |
| sw_arc | `SW_ARC` | tek (şarjlı) | şarj döngüsü + ateş + küre döngüsü (§6) | — (Special darbe seti) | none | ampul "vız-sönme", hücre boş |
| sw_gel | `SW_GEL` | tek, basınçlı | tek tek + birikinti döngüsü (§6) | — (Special darbe seti) | none | hazne boş "hışş-tık" |

Döngü kuralı (S-08): auto ≥ ~600 RPM → `FireStarted` ile **başla** (ilk atış tam + mekanizma), **sürdür** (RPM'e kilitli granüler tane, 3 varyant havuzu), `FireStopped` ile **bırak** (son atış kuyruğu + mekanizma durması + kovan son düşüşü). Altında her atış one-shot; burst'ler one-shot dizisi.

#### 2b · Katmanlar (karakter 3–6 kelime)

| `audioId` | Mekanik tık | Gövde punch | Namlu patlaması | Kuyruk / yankı |
|---|---|---|---|---|
| `AR1` | serpantin düşer, tava "fıs" kısa | orta, sıkı, ahşap tok 300 Hz | uzun sekizgen namlu: odaklı, orta-parlak | 0,9 s, temiz, hafif ıslak taş yansıması |
| `AR2` | ağır horoz, demir "TAK" | daha kalın, göğüste hissedilir | kısa namlu: daha kaba, geniş | burst sonunda tek uzun kuyruk 1,2 s |
| `SMG1` | yay "tik", hafif kızak | ince, hızlı, 380 Hz | kısa karabina: çatırtılı, üst-orta | 0,4 s, kuru |
| `SMG2` | çift tambur "tik-tik", saat mekanizması | daha da ince, sert | dikiş makinesi, parlak | 0,3 s; bırakışta tambur "dönüş" tıkırtısı |
| `SG1` | tetik "tık" + pompa (gecikmeli) | ağır, geniş, 160 Hz | çan ağız: geniş "BOOM", saçma "hışırtı" | 1,4 s, koridorda gürleme |
| `SG2` | iki tetik, kırma menteşe hissi | en ağır, çift vuruş | çift namlu: kısa-kalın, dumanlı | 1,6 s, en uzun düşük kuyruk |
| `LMG1` | kol + şerit bağı "şıkır" | orta-ağır, ritmik | ısınmış namlu: metalik "çınlamalı" | döngüde sürekli halı; bırakışta 1,0 s |
| `LMG2` | ağır kol, dökme demir kapak titrer | "balyoz", 120 Hz, yavaş | her atış top gibi, uzun namlu | 1,8 s, her atışta yeni kuyruk |
| `SR1` | sürgü kilidi ince "klik" | sert, sıkı, 190 Hz | süpersonik "ÇAT" + derin gövde | 2,2 s, en uzun yankı; uzak dağ dönüşü |
| `RL1` | fitil "çıt" + tüp basınç | yok (omuzdan kaçış hissi) | "FUUUM" fırlama + arka üfleme | itki uzaklaşır (§4 döngü) |

### §3 · Reload ve mekanizma sesleri

S-06 adları birebir. Yorum: `chamber` = mermi sürme (kol/pompa/sürgü), `charge` = serpantin/horoz kurma (fitil kilidi; boş reloadda zorunlu), `eject` = kovan/boş şarjör/şerit atma. Karakterler: demir mat, yay çelik, ahşap tok; hiçbir mekanizma sesi 250 ms'den uzun değil (animasyon temasına kilitli, gecikme ≤ 20 ms).

| Yuva | Taktik reload (sıra) | Boş reload (sıra) | Olay karakterleri |
|---|---|---|---|
| ar_1 | mag_out → mag_in | mag_out → mag_in → chamber → charge | mag_out: demir mandal + kutu kayması; mag_in: "ÇAK" + gövde tok; chamber: kol çek-bırak çift metal; charge: serpantin yay "kırrk-tık" |
| ar_2 | mag_out → mag_in | mag_out → mag_in → charge | daha ağır kutu (uzun kovan), mag_in "TUNK"; charge: horoz kalın "TAK-tık" |
| smg_1 | mag_out → mag_in | mag_out → mag_in → chamber | ince sac şarjör, yay "vın"; chamber: küçük kol, hızlı |
| smg_2 | mag_out → mag_in → mag_in_2 | mag_out → mag_in → mag_in_2 → chamber | iki tambur: ilki "çak", ikincisi yarım ton yukarı "çık"; chamber: tambur dönüş tıkırtısı |
| sg_1 | shell_in ×n (indeks: her biri +2 % pitch) | eject (pompa boş, kovan) → shell_in ×n → chamber (pompa) | shell_in: karton+pirinçsiz demir taban "tık-şık"; pompa: çift demir kızak |
| sg_2 | eject (kırma açılır, 1 kovan) → shell_in → chamber (kapanır) | eject (2 kovan, gecikmeli) → shell_in → shell_in → chamber | kırma menteşe "klak" + kovan fırlar; kapanış ağır "ÇAK" + ahşap tok |
| lmg_1 | box_open → belt_seat → box_close | box_open → belt_seat → box_close → charge | kapak sac "kank"; şerit yerleşir "şıkır-ÇAT"; kol "ÇAK-ÇAK" |
| lmg_2 | box_open → belt_seat → box_close | box_open → belt_seat → box_close → charge | dökme demir kapak, derin; şerit kalın halkalar; kol iki elle "ÇANK" |
| sr_1 | mag_out → mag_in | bolt_up → bolt_back (eject birleşik) → mag_out → mag_in → bolt_fwd → bolt_down | atış sonrası döngü: bolt_up "klik" · bolt_back "şşk-tınk" (kovan) · bolt_fwd "şuk" · bolt_down "ÇAK" |
| rl_1 | rocket_in → rocket_seat | aynı (tek atış) | rocket_in: tüpe kayma "hşşş"; rocket_seat: kilit "ÇAK" + fitil geriliyor "çıt" |
| sw_arc | cell_out → cell_in | aynı | cell_out: cam ampul "tın" + kontak kıvılcımı; cell_in: cam yerleşir + bobin yeniden vınlar |
| sw_gel | canister_out → canister_in | aynı | canister_out: basınç "pşşt" + bakır; canister_in: vida + jel "vıcık" |

Kuşanma / indirme / ADS (silah başına varyant, aynı aile):

| Olay | Karakter | Not |
|---|---|---|
| SwitchIn | kayış+ahşap "tok" → metal temas → kısa yay; sınıf ağırlığına göre 120–260 ms | FP'de ek: kol kumaşı hışırtı |
| SwitchOut | kumaş + metal indiriliyor, kısa | mevcut WeaponXLowered ailesi |
| ADS in | kumaş sürtünme (haori) + kundak omuz "tuk" + metal temas | FP'de 1,0 gain, TP 0,6, tepeden 0 |
| ADS out | nefes/kumaş bırakış, kısa | — |
| Sprint/hareket takırtısı | şarjör-kayış "şıkır" (S-06 klip olayı isteği: `weapon_rattle`) | yalnız FP/TP, düşük gain |

### §4 · Projectile sesleri

**Uçuş (flyby):** dinleyiciye ≤ 2,5 m yakın geçen mermi; karakter hıza bağlı (GD'nin mermi hızı → S-08 eşikleri).

| Kaynak | Uçuş sesi | Tetik |
|---|---|---|
| light (SMG) | ince "vızz" 60–90 ms, pitch hızla ↑ | Flyby (hız < ~250 m/s → yalnız whiz) |
| medium (AR) | "vız" + kısa "çıt" crack | Flyby (hız ≥ ~300 → crack + whiz) |
| heavy (LMG2/SR) | süpersonik "ÇAT-vııı" whipcrack, SR en uzun | Flyby |
| shell (SG) | saçma bulutu "hışş" kısa, çok kanallı | Flyby (pellet 0 yalnız, tek ses) |
| rocket | itki döngüsü "FUUU" + Doppler, uzaklaştıkça lowpass | RocketFlightStart/End |
| sw_arc küre | bobin uğultusu 110 Hz + cam "vın" 2,4 kHz, hafif titrek | SwArcSphereFlightStart/End |
| sw_gel | ıslak "vıcık-flap" düşük hız, damla kuyruğu | SwGelFlightStart/End (kısa) |

**Çarpma matrisi** (yüzey × kalibre; her hücre 2 varyant + tek ardışık engeli; ses `Impact<Surface>` ailesi, kalibre gain/katman seçer):

| Yüzey | light | medium | heavy | explosive |
|---|---|---|---|---|
| Default | kuru "tık" | "tak" + toz | "TAK" + moloz | patlama (aşağı) |
| Stone | "çıt" + kıvılcım | "çat" + taş tozu | "ÇAT" + parça | + taş yağmuru |
| Wood | "tok" kısa | "TOK" + kıymık | "TONK" + çatlak | + kalas kırılması |
| Foliage | "hış" | "hışş" + dal | "hışş" + dal kırılır | + yaprak yağmuru |
| Water | "plip" | "plop" | "PLOSH" | + su sütunu düşüşü |
| Metal | "tink" çınlama | "TANK" | "TONNG" uzun | + sac buruşması |
| Dirt | "puf" | "pof" + çakıl | "POF" derin | + toprak yağmuru |
| Paper | "pıt" + yırtık | "pat" + yırtık | "PAT" + shoji | + kağıt parçaları |
| Flesh | ıslak "çak" | "ÇAK" + gövde | "ÇAK" + kemik | et + gövde düşüşü |
| Armor | "tınk" tutar | "TANK" + çatlak | "TANK" + parça | zırh sıyrılır |

Et/zırh ayrımı: Flesh → `FleshHit` ailesi (Target bus, kalibre gain + `CriticalHit` kafada); Armor → mevcut `ArmorImpact/Crack/Break` basamakları (REQ-129), kalibre yalnız gain. Zırhlı bölgeye light kalibre: "tınk" belirgin (oyuncu zırhı okusun).

**Patlama katmanları** (RL1 + Explosion dalı; mesafe dinleyiciden):

| Katman | Yakın (< 6 m) | Orta (6–20 m) | Uzak (> 20 m) |
|---|---|---|---|
| Vuruş | sub "THUD" 40 Hz + "ÇAT" | "BUM" gövde | "bum" lowpass |
| Kuyruk | 2,5 s, koridorda 3,5 s | 1,8 s | 3 s yayvan, yalnız düşük |
| Kalıntı | moloz yağmuru, taş tıkırtısı, "sss" | moloz seyrek | yok |
| Tinnitus | evet (aşağıda) | hayır | hayır |

**Kulak çınlaması (tinnitus) önerisi:** patlama merkezine ≤ 4 m (veya oyuncu patlama hasarı aldı): 3,6 kHz sinüs 1,2–2,2 s (−22 dB'den fade), World bus lowpass 1,2 kHz → 0,8 s'de açılır, Music −6 dB. Ardışık tetik 4 s içinde yenilenmez (yorulma). Erişilebilirlik: ayarlardan kapatılabilir.

### §5 · Perspektif ve kamera modu

Dinleyici oyuncunun başında kalır (AudioListenerAnchor); mod farkı **mix** farkıdır, mesafe değil. Ateş için katman gain'leri (dB, FP referans 0):

| Katman | FP | TP | Tepeden |
|---|---|---|---|
| Mekanik tık | 0 | −4 | −9 |
| Gövde punch / omuz teması | 0 (+ FP-özel "kundak omuz" alt katman) | −3 | −6 |
| Namlu patlaması | 0 | 0 | −2, hafif lowpass 9 kHz |
| Kuyruk / mekân | −2 (kulak yakın, kuyruk az) | 0 | +2 (daha mekânsal, geniş stereo) |
| Kovan düşme | 0, 2D yakın | −3, 3D | −8, 3D |
| ADS / kumaş / takırtı | 0 | −4 | kapalı |
| Uçuş (flyby) | 0, baş etrafında pan | −2 | −6 |

**FP juice:** mekanik tık + omuz teması + gövde rezonansı öne; auto döngüde sürdür katmanına düşük "gövde titreşim" halısı (80–140 Hz, −18 dB) eklenir; recoil zirvesiyle senkron "kundak" vuruşu (S-06 additive Fire ile aynı kare). Yürüyüşte silah takırtısı ve kumaş yalnız FP/TP'de.

**İç/dış mekân (N3 dünya v5 koridor/kapalı alan):** kuyruk iki varyant — dış: uzun, yumuşak, ıslak taş yansıması (Look B); iç/koridor: kısa yoğun erken yansımalar (30–80 ms), 1,5× gain, gürleme. Seçim: ateş anında dinleyici bölgesi bayrağı (`indoor`), 0,4 s çapraz geçiş. Patlama ve SG kuyrukları içeride ayrıca +3 dB.

**Uzak katman kuralı:** dinleyiciden > 20 m'deki darbe/patlama/Special isabeti uzak varyanta geçer (lowpass, geç kuyruk, transient yumuşar); oyuncu tek atıcı olduğundan uzak katman yalnız projectile/darbe/patlama ve Special alanı için vardır. Voice: ateş en yüksek öncelik, darbe `MaxInstances` 6 / 60 ms, flyby 3 / 90 ms, kovan 4 / 120 ms; zincir sıçraması hedef başına 40 ms aralıklı sıra.

### §6 · İki Special ses yönü (A2)

#### (i) sw_arc — `SW_ARC` (Wunderwaffe DG-2 benzeri zincir yıldırım)

| Olay | Özgün öneri (Edo fantastik) | Orijinal referans (DG-2 karakteri) |
|---|---|---|
| Şarj (SwArcChargeStart/End) | bobin uğultusu 110 Hz yükselir + cam ampul vızıltısı 2,4 kHz; altına çok kısık taiko "gök gürültüsü" pedali; pirinç çınlaması burada serbest | Tesla bobini yükselen elektrik uğultusu, cam kap enerji dolumu |
| Ateş (SwArcFire) | elektrik patlaması "ÇZZAK" + küre fırlarken cam "vınn" + hava boşalması; oyuncu FP'de elde titreşim vızıltısı 120 ms | keskin elektrik deşarjı + küre fırlatma "zap" |
| Küre uçuşu (SphereFlight) | bobin uğultusu + titrek cam tonu, Doppler; küre yaklaşan zombiye "çekiliyor" hissi için gain dalgalanması | havada giden yüksek gerilim uğultusu |
| Zincir sıçraması (ChainJump{targetIndex}) | hedef başına kısa çatırtı "çıt-ÇZT", indeks arttıkça pitch +1 yarım ton, 40 ms gecikmeli dizi; 6+ hedefte kümeleme (tek uzun çatırtı) | ark zincirinin zombiden zombiye çatırdayarak atlaması |
| Elektrik çarpması (ShockStart/End) | gövde başına çatırtı döngüsü + kas gerilme "hırıltı" (ZombieReact ailesi yeni "sw_shock"); End: "sss" + kül/duman | zombinin çarpılıp titremesi, sönerken çıtırtı |
| Hücre değişimi (cell_out/cell_in) | cam ampul "tın" + kontak kıvılcımı; yerleşince bobin yeniden vınlar | ampul değişimi, elektrik "hazır" tonu |
| Yerde beacon (mevcut SpecialWeaponBeaconLoop) | mevcut gong + altın çınlama; sw_arc için üstüne bobin uğultusu (kısık, 3D) | — |

#### (ii) sw_gel — `SW_GEL` (Sliquifier benzeri kaydırıcı jel)

| Olay | Özgün öneri | Orijinal referans (Sliquifier karakteri) |
|---|---|---|
| Ateş (SwGelFire) | basınçlı bakır hazne "PŞŞT" + jel "PLOP" fırlar, hafif gurultu; FP'de hazne titreşimi | basınçlı sıvı püskürtme "plorp" |
| Uçuş (SwGelFlightStart/End) | ıslak "vıcık-flap", damla kuyruğu; kısa (< 1 s) | havada uçan sıvı kütle |
| İsabet (SwGelHit{surface, flesh}) | yapışkan "ŞLAP" + sıçrama; et üstünde daha boğuk | ıslak şlap |
| Birikinti (PuddleCreated/Expired) | oluş: yayılma "şşlıp"; döngü: kısık kabarcık + damla (3D, Impact bus, Special Field havuzu); bitiş: kuruma "çıt-çıt" + hava | parlak jel birikintisi, kuruyunca kaybolur |
| Zombi kayma-düşme (SlideStart/End, enemy) | kayma sürtünmesi "vıjjj" (pitch hızla ↑) + gövde düşüşü "TUK" + zırh parçası tıkırtısı; kalabalıkta 60 ms kümeleme | zombilerin komik kayıp düşmesi |
| Oyuncu kayma (SlideStart/End, player) | ayak kayması "vıjj" + kumaş; End: denge "tuk" (FP'de yakın) | oyuncunun da kayması |
| Hazne değişimi (canister_out/in) | "pşşt" basınç + bakır vida, jel "vıcık" | hazne değişimi |

Her iki Special için ortak: altın/pirinç çınlaması yalnız burada; pickup anında mevcut `StingerSpecialPickup` + silaha özgü "hazır" tonu (arc: bobin dolar, gel: hazne basınçlanır).

### §7 · Olay listesi (S-08 bağlar)

Mevcut yüzeyler korunur (`AudioEvents`, `EnemyCombatEvents`, `SpecialWeaponUsageEvents`, `CharacterAnimationEvents`); aşağıdakiler v3 sözleşmesi. `audioId` dizge, `SurfaceKind` mevcut enum, konum her olayda var.

| Olay | Tetikleyen | Veri | Tür |
|---|---|---|---|
| FireStarted | ilk atış (auto/burst/tek hepsi) | audioId, cameraMode, indoor | one-shot / döngü başla |
| FireStopped | tetik bırakıldı / şarjör bitti / askıya | audioId, roundsFired | döngü bırak |
| FireShot | her atış (döngü dışı silahlar + döngü içi sayaç) | audioId, shotIndex, lastRounds(≤3 → tık varyantı) | one-shot |
| DryFire | boş tetik | audioId | one-shot |
| ReloadEvent | animasyon olayı | audioId, eventName (S-06 adı), index, isEmpty | one-shot |
| ReloadCancelled | reload kesildi | audioId, eventName(last) | one-shot (kısa metal) |
| WeaponSwitchIn / WeaponSwitchOut | kuşanma/indirme | audioId | one-shot |
| AdsStart / AdsEnd | nişan | audioId, cameraMode | one-shot |
| WeaponRattle | S-06 klip olayı (sprint/iniş) | audioId, intensity | one-shot, düşük öncelik |
| CasingEjected | kovan çıkışı | audioId, casingType (rifle/pistol/shell) | one-shot |
| CasingLanded | kovan yere değdi (fizik/gecikme) | casingType, SurfaceKind, bounceIndex | one-shot |
| ProjectileSpawn | mermi doğdu (VFX ile aynı kare) | audioId, caliber, speed | bilgi (ateşe bağlı; ses yok) |
| ProjectileFlyby | mermi dinleyiciye ≤ 2,5 m geçti | audioId, caliber, speed, closestPoint | one-shot |
| ProjectileImpact | yüzey/hedef temas | audioId, caliber, SurfaceKind, speed, isFlesh, isArmor, region, tier | one-shot |
| ProjectileRicochet | Ricochet dalı | audioId, SurfaceKind, bounceIndex | one-shot |
| ProjectilePierce | Piercing dalı geçiş | audioId, pierceIndex | one-shot (ıslak "geçiş") |
| ExplosionAt | roket/Explosion dalı | audioId, radius, distanceToListener, indoor | katmanlı one-shot |
| PlayerNearExplosion | oyuncu ≤ 4 m veya hasar aldı | distance, damage | tinnitus döngüsü |
| RocketFlightStart / End | roket doğdu / patladı-yok oldu | projectileId, position(takip) | 3D döngü |
| SwArcChargeStart / ChargeEnd | tetik basılı / bırakıldı-ateş | chargeRatio | döngü |
| SwArcFire | küre fırladı | position | one-shot |
| SwArcSphereFlightStart / End | küre doğdu / ilk temas | projectileId | 3D döngü |
| SwArcChainJump | zincir hedef | targetIndex, targetTier, position | one-shot dizi |
| SwArcShockStart / ShockEnd | gövde çarpılıyor / bitti | enemyId, tier | gövde döngüsü |
| SwGelFire | jel fırladı | position | one-shot |
| SwGelFlightStart / End | jel uçuşu | projectileId | kısa döngü |
| SwGelHit | jel temas | SurfaceKind, isFlesh, position | one-shot |
| SwGelPuddleCreated / Expired | birikinti | puddleId, radius, position | 3D döngü |
| SwGelSlideStart / SlideEnd | üstünde kayma | actorId, isPlayer, speed | döngü + düşüş one-shot |
| SwReadyTone | Special alındı ve hazır (pickup sonrası) | audioId | one-shot |

Eksik gördüğüm ve eklediğim olaylar: FireShot (döngü içi sayaç ve son mermi tık varyantı), ReloadCancelled, CasingEjected/Landed ayrımı, ProjectilePierce, PlayerNearExplosion, SwGelFlightStart/End, SwReadyTone, WeaponRattle. Var olan `HitMarker/KillMarker`, `ArmorBreak*`, `EnemyDeath` olduğu gibi kalır (ProjectileImpact bunları ikilemez; ImpactFlesh yalnız temas sesidir).

### §8 · Ses render üretim önceliği — 35 kr

1 render ≈ 2–2,8 kr → 12–15 render. Sıra "en çok fark yaratan":

| # | Render | Neden | Türetilen (0 kr) |
|---|---|---|---|
| 1 | AR1 ateş çekirdeği (tık+punch+patlama, kuyruksuz) | en çok duyulan ses; sınıf temeli | AR2 (pitch −3 st + kısa namlu EQ + burst kuyruğu) |
| 2 | SMG1 ateş çekirdeği + 1 s sürdür örneği | döngü tabanı | SMG2 (hız ×1,3, +2 st, dikiş makinesi katmanı) |
| 3 | SG1 ateş | en ağır klasik, FP juice | SG2 (çift vuruş üst üste, −2 st, uzun kuyruk) |
| 4 | LMG2 ağır tek atış | "balyoz" karakteri | LMG1 (kısaltılmış + link tıkırtısı katmanı, döngü) |
| 5 | SR1 ateş + uzun kuyruk | en belirgin tek ses | SR flyby whipcrack (crack kısmı kesilir) |
| 6 | RL1 fırlatma + arka üfleme | tek launcher | — |
| 7 | Patlama yakın (sub + crack + moloz) | RL1 + Explosion dalı | orta/uzak (lowpass + kuyruk uzatma) |
| 8 | SW_ARC ateş + zincir çatırtı (tek render, iki bölüm) | Special vitrini | ChainJump dizisi (dilimleme + pitch) |
| 9 | SW_ARC şarj/bobin uğultusu (döngü) | Special kimliği | küre uçuş döngüsü (aynı kaynak, Doppler) |
| 10 | SW_GEL ateş + şlap isabeti (tek render) | Special vitrini | kayma "vıjj" (zaman germe), birikinti kabarcık |
| 11 | Mekanizma "alet kutusu" (kol, sürgü, kapak, kırma, pompa — tek uzun render) | 20 reload olayı dilimlenir | tüm §3 olayları |
| 12 | Flyby whiz (light/medium) | projectile hissi | heavy (SR crack ile birleştirme) |
| 13 (bütçe dışı) | Kovan düşüşleri (rifle/pistol/shell taş) | mevcut foley'den türetilebilir | — |
| 14 (bütçe dışı) | İç mekân kuyruk IR | evrişim için | Unity reverb zone ile ikame |

35 kr ile #1–#12 (≈ 12 render, 24–34 kr); kalan her şey türetme.

### §9 · Açık sorular

1. **RPM eşiği:** GD `ar_1`'i ≥ 600 RPM'e çekerse AR1 döngüye geçer; hangi silahlar döngü olacak kesinleşince S-08 tane havuzunu ona göre böler. (GD)
2. **`charge` anlamı:** serpantin/horoz kurma yorumum doğru mu; boş reloadda ar_1/ar_2/lmg'de var, taktikte yok — S-06 onaylasın.
3. **smg_2 çift tambur (`mag_in_2`) ve sg_2 kırma çift namlu** ses için önerildi; GD/S-06 tasarımı farklıysa §3 satırları düşer.
4. **Tinnitus + iç mekân bayrağı:** `indoor` bayrağını kim üretir (dünya v5 bölge verisi mi, dinleyici ray'i mi)? (S-05/S-01)
5. **Flyby olayı:** zombiler ateş etmediğinden flyby yalnız oyuncunun kendi mermisinde (tepeden modda dinleyici yakınından geçmez); TP/FP'de omuz geçişi için mi tutulacak, yoksa yalnız ricochet dönüşleri için mi? (GD)
6. **Kovan fiziği:** `CasingLanded` gerçek fizikten mi (VFX kovan parçacığı) yoksa sabit gecikmeli sanal düşüş mü — 0 B/kare kuralıyla S-07/S-02 karar versin.

## §4 · A1 Animasyon ve kamera tepkisi (animation-specialist)
Kapsam: hareket dili, silah başına animasyon seti, reload koreografisi (normalize olay zamanları), görsel recoil, kamera tepkisi (mod × silah), launcher, iki Special, klip sayısı/öncelik. **Oyun sayıları (reload süresi, RPM, nişan kayması, hasar) game-designer'da; burada yalnız oranlar, alt sınırlar ve görsel tepki.** Motor: S-06 WP-071 (taban controller + AnimatorOverrideController, klip kimliği `AP022_<tp|fp>_<weaponId>_<action>`, sınıf fallback `AP022_<tp|fp>_<class>_<action>`). Üretim: P-06 AP-022. Kamera API: S-01 WP-070. Juice uygulaması: S-07 WP-073.

Mevcut motor gerçekleri (uyum): sarsıntı metre cinsinden, toplam tavan 0,22 m, 12 eşzamanlı, çekirdekler Punch / Rumble (7 Hz) / Bump / Rattle (34 Hz), yönlü sarsıntı var; silah görsel rig'i `RecoilKick` m + 90°/m pitch, toparlanma 0,10 s, yay 0,25 / sönüm 0,82; WP-022G `ReduceFlashes` 0,4×. 30 fps yazarlık (FACTS).

### §1 Animasyon dili

| Ağırlık sınıfı | Yuvalar | TP (tüm gövde) | FP (kol/görüntü modeli) | Eğri karakteri |
|---|---|---|---|---|
| W1 hafif | smg_1, smg_2 | dirsekler gövdeye yakın, hareket bilekten; kalça sabit | kısa, sert bilek kick'i; hızlı toparlanma | kick 1 kare → tepe, kritik sönümlü yay (ζ 0,7) |
| W2 orta | ar_1, ar_2, sg_1, sg_2, sw_arc | omuzdan geri tepme, hafif kalça karşı-dönüşü; reload'da baş silaha bakar | dirsek + omuz; pompalı/kırmada sol kol öne uzanır | kick 1–2 kare, yay ζ 0,65; reload vuruşları "hazırlık–eylem–oturma" üçlüsü |
| W3 ağır | lmg_1, lmg_2, sr_1, sw_gel | omuz + üst gövde pitch, ayak değişimi (ağırlık aktarımı); reload'da diz kırılır, silah kalçaya yaslanır | iki kol yükü paylaşır; sway yavaş ve büyük | kick 2 kare, yay ζ 0,55 (daha uzun salınım), sway 0,10 s gecikme |
| W4 çok ağır | rl_1 | tüm gövde: ateşte kalça geri, baş yana; omuz üstü taşıma | görüntü modeli ekran ortasında büyük; tek büyük tepki | kick 2 kare, tek büyük salınım + gövde "sarsıntı kuyruğu" 0,5 s |

- Ortak kural: tüm kick'ler **additive ve sıfıra döner**; kalıcı tek yer değiştirme yalnız game-designer'ın nişan kaymasıdır. TP ve FP aynı normalize olay zamanlarını paylaşır (ses/mekanizma senkronu tek kaynak).
- Kesintiye uğrayabilirlik: switch ve koşu her reload'u keser; ateş yalnız pompalı `shell_in` döngüsünü keser (§3.3); ADS hiçbir şeyi beklemez.
- Cephane sayacı animasyon sonunda değil **olayda** güncellenir: taktik reload'da `mag_in`, boş reload'da `charge`/`chamber` (kesilirse mekanik gerçeğe uyar).
- Hareket azaltma: her kamera/görüntü tepkisinin ölçeklenmiş eşdeğeri var (§5.5); flaş ≤ 3/sn (namlu alevi stroboskopu dahil: auto silahta namlu ışığı 600 RPM'de 10 Hz → ışık yoğunluğu değil **süreklilik** modeli, S-07).

### §2 Silah başına animasyon seti

Mekanizma varsayımları (koreografi bunlara bağlı; game-designer/art-director onaylar, §9):

| Yuva | Sınıf | Mekanizma (fantastik erken dönem) | Ateş modu | Besleme |
|---|---|---|---|---|
| ar_1 | ar | fitil-kilit tüfek (AP-013 A), alttan kutu şarjör, serpantin kolu = `charge` | auto | kutu 30 |
| ar_2 | ar | daha ağır kalibre, **yandan** kutu şarjör, üst kol = `charge` | burst 3 | kutu 20–25 |
| smg_1 | smg | kısa karabina (AP-013 B), kabzadan şarjör, küçük mandal | auto | kutu 42 |
| smg_2 | smg | yan yatay şarjör (Sten benzeri), tambur yok | auto (yüksek RPM) | kutu 32–40 |
| sg_1 | sg | pompalı çan ağız (AP-013 C), tüp şarjör, tek tek `shell_in` | pompalı | tüp 7 |
| sg_2 | sg | **kırma çift namlu** çan ağız; kırma = `eject`, 2 × `shell_in`, kapama = `chamber` | 2 atış (tek/çift) | 2 |
| lmg_1 | lmg | kayış beslemeli, üstten kapak: `box_open` / `belt_seat` / `box_close` / `charge` | auto | kutu 100 |
| lmg_2 | lmg | üst tepsi tambur (Lewis benzeri): `mag_out` / `mag_in` / `charge` | auto | tepsi 60–75 |
| sr_1 | sr | sürgülü, alttan kutu şarjör; her atışta `bolt_*` döngüsü | tek | kutu 5 |
| rl_1 | rl | namludan yüklenen roket: `rocket_in` / `rocket_seat`; omuz üstü | tek | 1 (tac reload yok) |
| sw_arc | sw | üstten 3 cam ampul hücresi: `cell_out` / `cell_in` / `charge` (bobin şarjı) | tek (zincir) | hücre 3 atış |
| sw_gel | sw | alttan cam hazne: `canister_out` / `canister_in` / `charge` (pompa) | tek (blob) | hazne 6–8 |

Paylaşım kuralı: **reload_tac, reload_empty ve fire silaha özgüdür (ZORUNLU, TP ve FP)** — kullanıcının "hepsinin kendi reload'u" isteği ve mekanizmaların farklılığı. Sınıf içinde paylaşılan: aim_idle, ads_idle, ads_in/out, switch_in/out, lower/raise, sprint_pose; kavrama farkı `grip_l` IK + `socket_weapon_r` ofsetiyle çözülür (yeni klip değil).

| Yuva | animSet | Sınıftan alınan | Silaha özgü (ZORUNLU) | Silaha özel ek eylem | FP notu |
|---|---|---|---|---|---|
| ar_1 | ar_1 (ar fallback) | aim_idle, ads_idle, ads_in/out, switch_in/out, lower/raise, sprint | fire, reload_tac, reload_empty | — | FP reload ayrı; şarjör kameraya gösterilir |
| ar_2 | ar_2 (ar fallback) | aynı | fire (burst 3 kick zinciri), reload_tac/empty (yan şarjör) | `charge` üst kol | FP'de kol üstten çekilir, kamera merkezine girer |
| smg_1 | smg_1 (smg fallback) | aynı | fire, reload_tac/empty | — | kabzadan şarjör: el ekran altında, hızlı |
| smg_2 | smg_2 (smg fallback) | aynı | fire, reload_tac/empty (yan şarjör) | — | sol el ekran solunda çalışır (asimetrik kadraj) |
| sg_1 | sg_1 (sg fallback) | aynı | fire (pump dahil: `eject`, `chamber`), reload_start, reload_shell (döngü), reload_end | pump ayrı klip (ADS'den çıkışta) | FP'de fişek tek tek kameranın önünden geçer |
| sg_2 | sg_2 (sg fallback) | aynı | fire (namlu 1/2 farklı kick), reload_empty (kırma) | reload_tac = tek fişek kırma varyantı | kırma açısı FP'de namlu ekrandan düşer |
| lmg_1 | lmg_1 (lmg fallback) | aynı | fire (sürekli), reload_tac/empty (kutu + kayış) | fire_loop_settle (ateş bitti) | en uzun FP reload; kapak açılışı ekranı doldurur |
| lmg_2 | lmg_2 (lmg fallback) | aynı | fire, reload_tac/empty (tepsi) | — | tepsi üstten iner, kamera hafif aşağı bakar |
| sr_1 | sr_1 = sınıf | (tek silah) | fire (+ bolt döngüsü), reload_tac/empty | bolt_cycle (ADS'de kısa varyant) | ADS'de bolt döngüsü kameraya yakın, dürbün kalkar |
| rl_1 | rl_1 = sınıf | (tek silah) | fire, reload_empty | shoulder_in/out (omuza alma = switch) | görüntü modeli merkezi kaplar; §6 |
| sw_arc | sw_arc | SwHold ortak duruş | SwFire, SwReload (cell), switch_in (ampuller yanar) | player_shock (yakın ateş tepkisi) | §7 |
| sw_gel | sw_gel | SwHold ortak duruş | SwFire, SwReload (canister) | player_slide_enter/loop/exit | §7 |

FP klip gereksinimi: **her silah kendi FP reload'unu ve FP fire'ını alır** (görüntü modeli kadrajı, parmak detayı). FP aim/ADS/switch/sprint sınıf paylaşımlı. FP klipler aynı PlayerRig üzerinde, FP kol mesh'iyle; FP kadraj kuralı: mekanizma olayı anında eylem ekranın orta üçte-ikisinde okunur olmalı (şarjör/fişek/hücre kameraya dönük).

### §3 Reload koreografisi

#### §3.1 Normalize olay zamanları (0–1) — taktik reload (`ReloadTac`)

| Yuva | Adımlar ve olaylar (t) | Okunur en kısa süre |
|---|---|---|
| ar_1 | 0,00–0,18 sol el şarjöre; `mag_out` 0,24; 0,24–0,50 yeni şarjör kemerden; `mag_in` 0,62; 0,62–0,80 tokat/oturtma; 0,80–1,00 el kavramaya döner | 1,30 s |
| ar_2 | sol el yan şarjöre; `mag_out` 0,26 (yana düşer); `mag_in` 0,64 (yandan itilir); 0,64–0,82 mandal; oturma | 1,45 s |
| smg_1 | sağ bilek döner; `mag_out` 0,22 (kabzadan); `mag_in` 0,58; 0,58–0,72 avuç tokadı; oturma | 1,05 s |
| smg_2 | sol el yan şarjör; `mag_out` 0,24; `mag_in` 0,60; oturma | 1,15 s |
| sg_1 | segmentli (§3.3): reload_start 0,35 s + `shell_in` × n (her biri ≥ 0,40 s) + reload_end 0,25 s | 0,35 + 0,40·n + 0,25 |
| sg_2 | kırma `eject` 0,28 (yalnız boş namlu); `shell_in`(1) 0,60; kapama `chamber` 0,86 | 1,25 s |
| lmg_1 | silah kalçaya iner 0,00–0,15; `box_open` 0,22; eski kayış çıkar 0,30; kutu değişimi `mag_out` 0,42 / `mag_in` 0,58; `belt_seat` 0,72; `box_close` 0,84; omuza dönüş | 3,40 s |
| lmg_2 | silah kalçaya; tepsi mandalı; `mag_out` 0,30 (tepsi yukarı kalkar); `mag_in` 0,66 (üstten oturur, çeyrek tur kilit 0,78); omuza dönüş | 2,60 s |
| sr_1 | silah kalçaya, sürgü kapalı kalır; `mag_out` 0,30; `mag_in` 0,66; oturma; omuza dönüş | 1,90 s |
| rl_1 | (tac yok — kapasite 1; §3.2 boş reload tek koreografi) | — |
| sw_arc | `cell_out` 0,32 (3 ampul küme olarak üstten fırlar, cam şıngırtısı); `cell_in` 0,66 (yeni küme); `charge` 0,84 (ampuller sırayla yanar 0,84/0,90/0,96) | 2,40 s |
| sw_gel | silah yana yatar; `canister_out` 0,30 (hazne aşağı çekilir, jel damlar); `canister_in` 0,64; `charge` 0,84 (pompa iki vuruş 0,84/0,92) | 2,00 s |

#### §3.2 Boş reload (`ReloadEmpty`) — taktik adımlar + mekanizma kurma; sayaç `charge`/`chamber`/`bolt_down`'da dolar

| Yuva | Ek adımlar ve olaylar (t) | Okunur en kısa süre |
|---|---|---|
| ar_1 | `mag_out` 0,20; `mag_in` 0,54; sağ el serpantin koluna 0,60–0,74; `charge` 0,78; kol döner; oturma | 1,80 s |
| ar_2 | `mag_out` 0,22; `mag_in` 0,56; üst kol çekilir `charge` 0,80 | 1,95 s |
| smg_1 | `mag_out` 0,20; `mag_in` 0,52; mandal `charge` 0,74 | 1,45 s |
| smg_2 | `mag_out` 0,22; `mag_in` 0,54; `charge` 0,76 | 1,55 s |
| sg_1 | reload_start (0,45 s: `eject` boş kovan 0,60) + `shell_in` × 7 + reload_end pompa varyantı (0,45 s: `chamber` 0,70) | 0,45 + 0,40·n + 0,45 |
| sg_2 | kırma 0,18; `eject` 0,30 (iki kovan uçar); `shell_in`(1) 0,52; `shell_in`(2) 0,70; kapama `chamber` 0,88 | 1,60 s |
| lmg_1 | §3.1 + `charge` 0,92 (kol çekilir, silah omuzda) | 4,10 s |
| lmg_2 | §3.1 + `charge` 0,90 | 3,10 s |
| sr_1 | `bolt_up` 0,10; `bolt_back` 0,18 (`eject` yok — boş); `mag_out` 0,34; `mag_in` 0,62; `bolt_fwd` 0,80 (`chamber` 0,82); `bolt_down` 0,88 | 2,60 s |
| rl_1 | tüp omuzdan iner 0,00–0,18; sol el sırttaki roket kılıfına 0,18–0,36; `rocket_in` 0,56 (roket namluya girer); `rocket_seat` 0,70 (klik); tüp omuza 0,78–1,00 | 2,80 s |
| sw_arc | = §3.1 (hücre değişimi tek koreografi; boşken ampuller sönük, `charge`'da yanar) | 2,40 s |
| sw_gel | = §3.1 (+ boşken hazne görünür boş, `canister_in`'de jel dolar) | 2,00 s |

- Alt sınırların anlamı: bu sürelerin altında `mag_out → mag_in` arası 6 kareden kısa kalır ve göz mekanizmayı kaybeder (AAA ölçütü: ana vuruşlar arası ≥ 6 kare @30 fps, mekanizma vuruşu ≥ 4 kare).
- **ReloadSpeed kartı kısaltma stratejisi:** önce kuyruk (oturma 0,80–1,00) ve hazırlık (0,00–0,15) kesilir (klip toplamının ≤ %25'i), sonra klip hızı ≤ ×1,35; hiçbir koşulda mekanizma vuruşu 4 karenin altına inmez. Kısaltma tavanı: en kısa okunur sürenin %70'i.
- İptal: `mag_in` öncesi kesilirse cephane değişmez; `mag_in` sonrası, `charge` öncesi (boş reload) kesilirse silah "şarjör takılı, kurulmamış" — bir sonraki ateş basışı kısa `charge_only` klibini oynatır (ar 0,45 s, smg 0,35 s, lmg 0,60 s). S-06'ya öneri: `charge_only` sınıf klibi.

#### §3.3 Pompalı (sg_1) tek tek doldurma kuralı (kesilebilir reload)

| Segment | Süre alt sınırı | Olay | Kesilme |
|---|---|---|---|
| reload_start | 0,35 s (tac) / 0,45 s (boş, `eject` 0,60) | — | ateş/switch/koşu: anında reload_end (hızlı varyant) |
| reload_shell (döngü, her fişek) | 0,40 s; `shell_in`(i) segmentin 0,55'inde (+1 cephane) | `shell_in` i=1..n | **ateş:** olay geçtiyse segment sonuna kadar oynar, sonra reload_end hızlı (0,18 s); olay geçmediyse anında reload_end; **ADS:** aynı; **şarjör dolunca:** doğal çıkış |
| reload_end | 0,25 s (hazne dolu) / 0,45 s (`chamber` 0,70, pompa) | `chamber` | kesilemez (mekanik tamamlanır; 0,45 s) |

sg_2 kırma reload'u segmentli değildir; tac (1 fişek) ve boş (2 fişek) ayrı kliplerdir, `eject` sonrası kesilirse fişekler kaybolmaz (yalnız `shell_in` geçmişse sayılır).

### §4 Ateş animasyonu ve görsel recoil

| Yuva | TP additive (omuz geri cm / üst gövde pitch ° / baş) | FP görüntü modeli (geri cm / yukarı cm / pitch ° / yaw ± ° / roll ± °) | Toparlanma s | Desen |
|---|---|---|---|---|
| ar_1 | 2,5 / 1,2 / — | 3,0 / 0,6 / 2,2 / 0,5 / 0,7 | 0,11 | auto: kick üst üste biner, 6. atıştan sonra tavan ×1,8; titreşim 20 Hz 0,15° (ateş sürerken) |
| ar_2 | 3,0 / 1,5 / — | 3,5 / 0,8 / 2,6 / 0,6 / 0,8 | 0,14 | burst: 3 kick 55 ms arayla yığılır (1,0 / 1,15 / 1,3), tek toparlanma |
| smg_1 | 1,6 / 0,7 / — | 2,0 / 0,4 / 1,4 / 0,8 / 0,5 | 0,09 | auto: küçük kick, yaw baskın (sağ-sol yalpalama), titreşim 24 Hz 0,12° |
| smg_2 | 1,8 / 0,8 / — | 2,2 / 0,5 / 1,6 / 0,9 / 0,6 | 0,09 | auto: yan şarjör → roll asimetrik (sola) |
| sg_1 | 7,0 / 4,0 / baş 1,5° geri | 8,0 / 2,0 / 6,5 / 0,8 / 1,4 | 0,30 | tek büyük tepki; pump 0,45 s içinde (`eject` 0,45, `chamber` 0,85 normalize) |
| sg_2 | 8,0 / 4,5 / baş 2° | 9,0 / 2,4 / 7,5 / 1,0 / 1,6 (namlu 2'de roll ters) | 0,32 | tek/çift: çift atışta kick ×1,6, toparlanma 0,45 s |
| lmg_1 | 3,0 / 1,6 / — | 3,5 / 0,7 / 2,6 / 1,0 / 1,2 | 0,12 | auto: sürekli 8 Hz düşük frekanslı gövde sallantısı (rumble) + kick; 0,25 s ramp-in, 0,20 s ramp-out |
| lmg_2 | 2,8 / 1,5 / — | 3,2 / 0,6 / 2,4 / 0,9 / 1,0 | 0,12 | auto: tepsi dönüşü görünür (her atışta 1 diş) |
| sr_1 | 9,0 / 5,0 / baş 2° | 9,0 / 2,6 / 7,5 / 0,5 / 1,2 | 0,34 | tek büyük tepki; bolt döngüsü 0,85 s (ADS'de kısa: `bolt_up` 0,15 / `bolt_back` 0,32 `eject` 0,36 / `bolt_fwd` 0,66 `chamber` 0,70 / `bolt_down` 0,84) |
| rl_1 | 12 / 6 / baş 3° yana | 12 / 3,0 / 9,0 / 1,5 / 1,8 | 0,55 | tek büyük tepki + gövde geri itme (§6) |
| sw_arc | 5 / 2,5 / — | 5,0 / 1,2 / 4,0 / 1,0 / 0,8 | 0,40 | bobin geri tepmesi + 28 Hz 0,20° titreşim 0,35 s; şarj 0,20 s ön-hazırlık (charge) |
| sw_gel | 3 / 1,2 / — | 3,0 / 0,8 / 2,0 / 0,6 / 0,5 | 0,20 | jöle sallantısı: 6 Hz 0,3° 0,3 s "wobble" (ζ 0,3) |

- Kick eğrisi: tepe 1 kare (W1–W2) / 2 kare (W3–W4); dönüş kritik-altı sönümlü yay (mevcut yay 0,25 / sönüm 0,82 W2 için doğru; W1 sönüm 0,86, W3 0,78, W4 0,72).
- Auto yığılma: kick_n = kick_1 × min(1 + 0,13·(n−1), 1,8); ateş bırakılınca 0,25 s'de sıfıra. Titreşim yalnız görüntü modelinde, kameraya taşmaz (kamera §5).
- ADS'de görüntü modeli kick'i ×0,6 (geri cm ×0,4; pitch korunur — nişangâh kalkışı okunsun).
- TP: additive `Fire_<w>` yalnız üst gövde; ağırda (`lmg`, `sr`, `rl`) kalça 1 kare gecikmeli takip (0,3–0,6°), ayak sabit.

### §5 Kamera tepkisi (görsel; nişan kayması game-designer'da)

#### §5.1 Tepeden (mevcut CameraShakeService; genlik metre, tavan 0,22 m)

| Yuva | Genlik m | Süre s | Çekirdek | Yön vuruşu |
|---|---|---|---|---|
| ar_1 / ar_2 | 0,018 / 0,024 (burst 3 üst üste) | 0,07 / 0,08 | Punch | namlu tersi %30 |
| smg_1 / smg_2 | 0,010 / 0,012 | 0,05 | Punch | %20 |
| sg_1 / sg_2 | 0,055 / 0,065 | 0,13 / 0,14 | Punch | %45 |
| lmg_1 / lmg_2 | 0,020 atış + 0,030 sürdürme | atış 0,07; sürdürme "ateş başladı/bitti" | Punch + Rumble | %30 |
| sr_1 | 0,080 | 0,16 | Punch | %50 |
| rl_1 | 0,130 ateş; patlama 0,160 (3,2 m → 12 m'de sönüm) | 0,28 / 0,45 | Rumble + yön | %60 / patlama merkezi |
| sw_arc | 0,045 | 0,30 | Rattle | — |
| sw_gel | 0,015 | 0,08 | Bump | %20 |

#### §5.2 Üçüncü şahıs (omuz kamerası; kick pitch °, yaw rastgele ± °, FOV punch °, toparlanma s)

| Yuva | Pitch | Yaw ± | FOV | Toparlanma | Not |
|---|---|---|---|---|---|
| ar_1 / ar_2 | 0,35 / 0,50 | 0,12 / 0,15 | +0,8 / +1,0 | 0,14 | auto: kick yığılmaz (kamera), yalnız görüntü modeli yığılır |
| smg_1 / smg_2 | 0,22 / 0,25 | 0,15 / 0,18 | +0,5 | 0,12 | — |
| sg_1 / sg_2 | 1,4 / 1,7 | 0,40 / 0,45 | +2,5 / +3,0 | 0,30 | omuz kamerası 4 cm geri ittirilir (yay) |
| lmg_1 / lmg_2 | 0,40 / 0,38 | 0,20 | +0,8 | 0,14 | + Rumble 7 Hz 0,08° sürekli |
| sr_1 | 1,8 | 0,30 | +3,0 | 0,36 | — |
| rl_1 | 2,6 | 0,60 | +4,5 | 0,50 | + Rumble 0,4 s |
| sw_arc | 0,8 | 0,50 | +2,0 | 0,40 | Rattle 0,3 s |
| sw_gel | 0,3 | 0,20 | +0,6 | 0,18 | — |

#### §5.3 Birinci şahıs (kafa: kick pitch/yaw/roll °, FOV punch °, sarsıntı Hz / genlik ° / süre s, toparlanma s)

| Yuva | Pitch | Yaw ± | Roll ± | FOV | Sarsıntı | Toparlanma |
|---|---|---|---|---|---|---|
| ar_1 | 0,55 | 0,18 | 0,15 | +1,2 | 20 Hz / 0,06 / 0,08 | 0,12 |
| ar_2 | 0,70 (×3 burst) | 0,20 | 0,20 | +1,4 | 20 Hz / 0,07 / 0,10 | 0,14 |
| smg_1 | 0,35 | 0,22 | 0,12 | +0,8 | 24 Hz / 0,05 / 0,06 | 0,10 |
| smg_2 | 0,40 | 0,25 | 0,15 | +0,9 | 24 Hz / 0,05 / 0,06 | 0,10 |
| sg_1 | 2,2 | 0,60 | 0,60 | +3,5 | 14 Hz / 0,25 / 0,14 | 0,30 |
| sg_2 | 2,6 | 0,70 | 0,70 | +4,0 | 14 Hz / 0,28 / 0,16 | 0,32 |
| lmg_1 | 0,60 | 0,30 | 0,25 | +1,2 | 8 Hz / 0,12 / sürekli (ateş başladı/bitti) | 0,13 |
| lmg_2 | 0,55 | 0,28 | 0,22 | +1,1 | 8 Hz / 0,11 / sürekli | 0,13 |
| sr_1 | 2,8 (ADS'de 2,0) | 0,40 | 0,80 | +4,5 (ADS'de +2,5) | 12 Hz / 0,20 / 0,12 | 0,36 |
| rl_1 | 3,6 | 0,90 | 1,20 | +6,0 | 9 Hz / 0,40 / 0,30 | 0,55 |
| sw_arc | 1,2 | 0,60 | 0,40 | +2,5 | 28 Hz / 0,15 / 0,35 | 0,40 |
| sw_gel | 0,5 | 0,30 | 0,20 | +0,8 | 6 Hz / 0,10 / 0,20 | 0,20 |

- FOV punch her zaman **dışa** (FOV artar) ve ≤ 0,08 s'de tepe, toparlanma süresince yayla döner; auto silahta FOV punch yığılmaz (tek seferlik, ateş sürerken sabit +0,4°).
- FP kick eğrisi görüntü modeliyle aynı yay ama ×0,25 genlik ve 1 kare gecikme (kafa, silahtan sonra hareket eder — "ağırlık" hissi).

#### §5.4 FP yürüme/koşma bob, sway, ADS

| Durum | Kamera dikey / yatay cm (adım başına) | Kamera roll ± ° | Görüntü modeli bob (kamera × ) | Sway (fare gecikmesi) | Nefes |
|---|---|---|---|---|---|
| Duruş (idle) | — | — | 0,3 cm 0,3 Hz salınım | konum 1,2 cm @100°/s, dönüş ≤ 2,0°, yumuşatma 0,07 s | 0,12° @ 0,3 Hz |
| Yürüyüş (strafe/geri) ≈ 1,8 Hz adım | 1,4 / 0,9 | 0,35 | ×1,5, 90° faz | ×1,0 | 0,10° |
| Koşu 6,2 m/s ≈ 2,7 Hz adım | 3,0 / 1,7 | 0,70 | ×1,6, silah %15 indirilmiş | ×1,2, yumuşatma 0,09 s | — |
| Uçuş fazı inişi (koşu klibi 15 f) | iniş bump 1,5 cm / 0,12 s | 0,2 | ×1,4 | — | — |
| ADS | ×0,30 | ×0,20 (roll 0) | ×0,20 | ×0,35, yumuşatma 0,05 s | 0,06° @ 0,25 Hz (sr_1: 0,10°, nefes tutma varsa 0) |
| Ağırlık sınıfı çarpanı | W1 ×0,85 · W2 ×1,0 · W3 ×1,25 · W4 ×1,4 | aynı | aynı | W1 ×0,8 hızlı (0,05 s) · W3/W4 ×1,4, 0,10 s | — |

- Bob sinüs değil **adım olayına kilitli** eğri (StrideCalibrator ayak olayları): temas = çukur (hızlı, 3 kare), geçiş = tepe (yavaş); yatay bileşen ayak tarafına göre işaretli.
- Kayma ("ayaklar hızdan yavaş") durumunda bob frekansı ayak olayını izler, hızı değil — ayak/kamera senkronu korunur.

#### §5.5 Hareket hastalığı güvenliği — "Kamera sarsıntısı" kaydırıcısı (Ayarlar > Erişilebilirlik, WP-022G paneli)

| Öğe | Kaydırıcı %0–100 (varsayılan 100) etkisi | Mutlak tavan (kaydırıcıdan bağımsız) |
|---|---|---|
| Sarsıntı genliği (§5.1–5.3) | doğrusal ölçek | toplam 0,22 m (mevcut); FP sarsıntı ≤ 0,5° tek olayda |
| Kick pitch / yaw (kamera) | doğrusal | pitch ≤ 4,0°, yaw ≤ 1,0° |
| Roll (kamera) | doğrusal; **≤ %50'de 0** | ≤ 1,5° toplam (ateş + kayma + bob) |
| FOV punch | doğrusal | ≤ 6,0°; yalnız dışa |
| Bob | ×(0,5 + 0,5·s) | dikey ≤ 3,5 cm, yatay ≤ 2,0 cm |
| Sürekli sarsıntı (lmg, sw_arc, kayma) | doğrusal | ≤ 9 Hz sürekli; > 12 Hz yalnız ≤ 0,4 s |
| Görüntü modeli kick / titreşim | ölçeklenmez (kamera değil) | titreşim ≤ 0,3°, kick pitch ≤ 10° |
| Mevcut "hareket azaltma" anahtarı | kaydırıcıyı %35'e çeker + roll 0 + bob ×0,5 | — |

Namlu alevi/flaş: `ReduceFlashes` (0,4×) namlu ışığı ve sw_arc çakmasına da uygulanır (S-07); sw_arc zincir çakması ≤ 3/sn kuralına uyar (hedef başına ışık değil, tek zincir ışığı + ısı izi).

### §6 Launcher rl_1

| Aşama | Koreografi | Olay / zaman | Süre alt sınırı |
|---|---|---|---|
| Omuza alma (switch_in) | tüp sırttan kalça hizasına, sağ omuza; sol el ön kavrama, baş nişan hattına eğilir; kalça hafif sola | `switch_ready` 0,92 | 0,55 s |
| Duruş (aim_idle) | ağır sway (W4), omuz nefesle 0,3 Hz; kamera yükü hissi: silah ekran ortasını kaplar | `breath` 0 | döngü 2,0 s |
| Ateş | tüp 12 cm geri, gövde 8 cm arkaya kayar (TP: arka ayak yük alır, kalça 6 cm geri, göğüs 4° pitch, baş 3° yana), arka üfleme (VFX S-07); FP §4/§5.3 | `fire` 0; gövde tepkisi 0,05–0,50 | tepki kuyruğu 0,55 s |
| Roket yükleme (reload_empty) | §3.2: tüp iner, roket sırt kılıfından, `rocket_in` 0,56, `rocket_seat` 0,70 (klik + tüp titreşimi 1 kare), omuza | rocket_in / rocket_seat | 2,80 s |
| İndirme (switch_out) | tüp omuzdan kalçaya, sonra sırta | `switch_away` 0,90 | 0,45 s |

Kendine yakın patlama (oyuncu ≤ 3,2 m yarıçap): mevcut `hurt_B` (tüm gövde) + patlama merkezinden 2 m itki 0,3 s; kamera: Rumble 0,16 m 0,45 s (tepeden), FP roll 2,0° + pitch 3° + FOV +6 → 0,6 s toparlanma, 0,35 s "kulak çınlaması" ses filtresi (S-08). 3,2–8 m: `hit_light` + Rumble 0,06 m 0,3 s. Hasar game-designer'da; öneri: yakın patlama hem cezalandırıcı hem okunur olsun (oyuncu ne yaptığını görsün).

### §7 İki Special (A2)

> özgün öneri · orijinal referans: mekanik/his orijinal CoD BO Zombies'e çok yakın (kullanıcı kararı), ad/model/dünya özgün (C-00 §6.3 önerisi). Wunderwaffe DG-2 hissi: üstten takılan 3 cam hücre, bobin geri tepmesi, zincir yıldırım, hücre değişiminde hücrelerin fırlaması. Sliquifier hissi: cam hazne, mor jel blob'u, yerde birikinti, kayan zombi düşünce ölür, oyuncu da birikintide hızlanarak kayar. Edo çevirisi art-director'da (ampul → tilki ateşi feneri hücreleri; jel → lake/kudzu özü vb.).

#### §7.1 sw_arc (zincir yıldırım)

| Eylem | Koreografi | Olaylar (t) | Süre alt sınırı |
|---|---|---|---|
| Kuşanma (switch_in) | silah yukarı kalkar, 3 ampul sırayla yanar (0,50 / 0,62 / 0,74), bobin 1 tur döner | `charge` 0,50 (S-08 "şarj başladı"), `switch_ready` 0,92 | 0,90 s |
| SwHold | bobin yavaş dönüş (döngü), ampul ışığı nefesle; W2 sway + 0,15° elektrik titremesi 12 Hz | `breath` | döngü 2,0 s |
| SwFire | 0,00–0,20 bobin hızlanır (ön-şarj, kesilemez), 0,20 küre çıkar; bobin 5 cm geri teper, ampul biri söner | `charge` 0, `fire` 0,20 | 0,60 s (toparlanma dahil) |
| SwReload (hücre) | §3.1: `cell_out` 0,32 (küme fırlar), `cell_in` 0,66, `charge` 0,84 (ampuller 0,84/0,90/0,96) | cell_out / cell_in / charge | 2,40 s |
| Oyuncu yakın ateş tepkisi `player_shock` | zincir düğümü oyuncuya ≤ 1,5 m: 0,40 s kollar kilitlenir (silah yukarı 5°), gövde 20 Hz 0,3 cm titrer, baş geri; FP kamera 22 Hz 0,20° 0,35 s + roll 0,6°; hasar game-designer | `shock_start` 0, `shock_end` 0,40 (öneri) | 0,40 s |

Zombi tarafı (S-03 WP-076 / S-06 WP-071 ile): `zombie_shock_loop` — kollar dışa gerilir, çene açık, tüm gövde 22 Hz 1,2 cm titreme, ayak parmak ucunda; süre 0,55 s (Standard/Runner) / 0,80 s (Guard) / 1,10 s (Executioner); zincir sıçraması hedef başına 0,06 s → titreme başlangıcı sıçrama indeksiyle kayar. Ardından `zombie_shock_collapse` 2 varyant (A: dizler bükülür, öne çöker; B: kaskatı arkaya devrilir), 0,35'te ragdoll devri (WP-071 ölüm kancası), duman/kıvılcım S-07. Ölmeyen (Elite/Ultra hasar eşiği game-designer) → `heavy_stagger` mevcut + 0,4 s titreme kuyruğu.

#### §7.2 sw_gel (kaydırıcı jel)

| Eylem | Koreografi | Olaylar (t) | Süre alt sınırı |
|---|---|---|---|
| Kuşanma | silah alttan gelir, hazne çalkalanır (jöle 6 Hz 0,3 s), pompa bir vuruş | `charge` 0,60, `switch_ready` 0,90 | 0,80 s |
| SwHold | hazne jöle salınımı (döngü), W3 sway | `breath` | döngü 2,0 s |
| SwFire | 0,00 pompa hissi, blob 0,04'te çıkar; silah 3 cm geri + wobble | `fire` 0,04 | 0,35 s |
| SwReload (hazne) | §3.1: `canister_out` 0,30, `canister_in` 0,64, `charge` 0,84 | canister_out / canister_in / charge | 2,00 s |

Zombi kayma-düşme: birikintiye giren zombi ≥ 1,3 m/s ise `zombie_slip_enter` 0,25 s (ayaklar öne kayar, kollar havaya) → `zombie_slip_fall` 3 varyant (geriye sırt üstü; yana sol; yana sağ — geliş yönü/ayak fazına göre) 0,45 s, 0,30'da ragdoll devri → **kalkamaz:** yerde `floor_struggle` döngüsü 1,2 s (ayaklar çırpınır, kayar) — Sliquifier kuralı: düşüş = ölüm (game-designer; öneri: düşüşte ölüm, Ultra hariç 2. düşüş). Rise klipleri birikinti içindeyken kilitli. Standard/Runner ayrı, Guard/Executioner ağır varyant (yavaş, ×1,3 süre).

Oyuncu kayma (birikintide hızlanarak): `player_slide_enter` 0,20 s (dizler kırılır, gövde 8° arkaya, kollar dengede, silah %30 indirilir) → `player_slide_loop` (ayaklar sabit/kayar, her 0,4 s küçük denge düzeltmesi, gövde birikinti hızına göre 6–12° eğilir; nişan alınabilir, ateş serbest) → `player_slide_exit` 0,30 s (bir tökezleme adımı, silah kalkar). FP: bob kapalı, kamera roll ±1,5° 1,2 Hz salınım (kaydırıcı %50 altında 0), görüntü modeli %20 aşağı + sway ×2,0 + 6 Hz 0,15° "titreme"; kontrol kaybı miktarı game-designer. İsteğe bağlı `player_slip_fall` (sert dönüşte düşme 0,6 s + kalkış 0,9 s) §9.

Olay adı önerileri (S-06/S-08 eşlemesi): `slide_start` / `slide_end` (oyuncu ve zombi), `shock_start` / `shock_end`, birikinti `puddle_enter` / `puddle_exit` (S-03 tetikler).

### §8 Klip sayısı tahmini ve öncelik (AP-022 P-06)

| Grup | TP | FP | Not |
|---|---|---|---|
| Sınıf paylaşımlı (ar, smg, sg, lmg, sr, rl × 9: aim_idle, ads_idle, ads_in, ads_out, switch_in, switch_out, lower, raise, sprint_pose) | 54 | 54 | sr/rl tek silah = sınıf; lower/raise sr/rl için aynı klip (−2) |
| Silaha özgü (10 × fire + reload_tac + reload_empty) | 30 | 30 | rl_1 tac yok (−1); sg_1 segmentli (start/shell/end + pump) (+2); sg_2 tac varyantı sayıldı |
| Silaha özgü ek (sr_1 bolt_cycle ADS varyantı, lmg fire_loop_settle ×2, ar_2 burst = fire, charge_only ×3 sınıf) | 6 | 6 | — |
| Special (sw_arc, sw_gel × 5: hold, fire, reload, switch_in, switch_out) | 10 | 10 | SwHold ortak duruş tabanı, hücre/hazne farkı klipte |
| Tepki: player_shock, player_slide (3), launcher self-blast (mevcut hurt_B yeniden kullanım = 0) | 4 | 4 (FP kol) | FP'de kamera prosedürel, kol klibi kısa |
| Zombi (WP-071 kapsamı, burada sayım): shock_loop, shock_collapse ×2, slip_enter, slip_fall ×3, floor_struggle | 8 (+4 ağır varyant) | — | tier varyantı hızla değil ayrı klip (ağırlık okunsun) |
| **Toplam** | **≈ 112 (+4)** | **≈ 104** | **≈ 220**; paylaşım olmasa 10 silah × 12 × 2 = 240 yalnız klasiklerde → paylaşım ≈ 110 klip kazandırır |

Öncelik sırası (her dilim TP + FP birlikte, önce reload_tac/empty + fire — kullanıcının en görünür isteği):

| Sıra | Dilim | Neden |
|---|---|---|
| 1 | ar_1, smg_1, sg_1 (AP-013 yer tutucu modeller hazır) + ar/smg/sg sınıf setleri | mevcut modelle hemen üretilir; FP boru hattı bunlarla kurulur |
| 2 | sr_1, rl_1 | benzersiz mekanizma, "kahraman anı" reload'ları; AP-021 model önceliği eşleşmeli |
| 3 | lmg_1 + lmg sınıf seti | en uzun/karmaşık reload (kayış), en yüksek juice (sürekli sarsıntı) |
| 4 | sw_arc, sw_gel + zombi shock/slip + oyuncu slide | Special görsel/etkileşim; S-03/S-07 bağımlılığı |
| 5 | ar_2, smg_2, sg_2, lmg_2 | sınıf seti hazır → yalnız fire + 2 reload |

Dış kaynak 10 kr (AP-022 tavanı) yalnız hareket referansı (LMG kayış, kırma tüfek, roket yükleme, kayma) için; klipler Blender'da elle. FP kol mesh'i için 2 twist kemiği/önkol önerisi (FACTS "candy-wrapper" riski; reload yakın planda görünür) — WP-071'e.

### §9 Açık sorular

1. sg_2 mekanizması: kırma çift namlu (buradaki koreografi) mı, tambur/pompalı varyant mı? (game-designer + art-director)
2. sr_1 besleme: ayrılabilir kutu şarjör (buradaki) mi, şerit klipsle üstten doldurma mı? (koreografi ve olaylar değişir)
3. rl_1 kapasite 1 varsayıldı (tac reload yok); 2 roket olursa aynı klip tekrar mı, çift yükleme mi?
4. FP klip yazarlığı 30 fps mi (mevcut), 60 fps mi? FP reload yakın planda 30 fps'de mekanizma vuruşları (4 kare = 133 ms) yeterli; 60 fps yalnız kick tepe karesi için — öneri: 30 fps + kick tepeleri prosedürel (S-06 kararı).
5. Special'larda ADS var mı? Koşuda (WP-070 sprint?) silah indirme pozu (sprint_pose) sözleşmede mi?
6. Oyuncu kayma → düşme (`player_slip_fall`) olsun mu? Dalga atıcıda 1,5 s kontrol kaybı ağır ceza; öneri: yalnız sert dönüşte ve kaydırıcıdan bağımsız kısa (0,6 + 0,9 s). (game-designer)

## §5 · A3 · ADR-069-1 — Projectile mimarisi (S-02, mühendislik)

**Bağlam.** M3'te klasik atış `HitscanEngine.Fire` ile anlık ışındır; iz kozmetiktir (`HitFeedbackDirector.ReportTracer`). Kullanıcı mermilerin "silik ve amatör" olduğunu, mermi hızının silahtan silaha değişmesini ve ateş–gidiş–çarpmanın profesyonel olmasını istedi (N8). Hasar işlemi (WP-006 olay sözleşmesi, `DamageTransaction`/`TransactionId`, ADR-034-1 pellet, ADR-056-1 SpecialUsageEvents, StaggerGovernor REQ-124, bölgesel zırh, geliştirme dalları) korunmalıdır.

**Karar.**
1. **Veri düzeni.** `ProjectileSystem` (statik servis + tek `ProjectileDriver` MonoBehaviour, run sahnesinde) canlı mermileri **havuzlu struct dizisinde** tutar: `ProjectileState[] s_live` + boş liste; mermi başına GameObject, bileşen, coroutine yok. Kapasite `ProjectileSystem.Capacity` (varsayılan **256**, kademeden bağımsız oyun kuralı); dolunca en eski **kozmetik-öncelikli** mermi (menzilin sonuna en yakın) sonlandırılır ve `OverflowCount` artar (ölçülür, raporlanır). `ProjectileState`: konum, hız, yerçekimi, kalan menzil, kalan ömür, hasar, pellet indeksi, `WeaponId`, `ProjectileKind`, kalan penetrasyon + hasar koruma, **dokunulan hedefler için satır içi sabit tampon** (8 hedef kimliği; ADR-034-1 "aynı mermi aynı gövdeye bir kez"), `TransactionId`, `PresentationGroupId`, roket/küre/jel alt durumu (itki süresi, arming mesafesi), bayraklar.
2. **Zaman.** Sabit adım `FixedStep = 1/120 s` + birikimli alt adım (kare başına en çok 8 adım; aşılırsa kalan süre tek süpürmeyle tüketilir, mermi durmaz). Ölçekli zaman (`Time.deltaTime`): pause ve hit-stop mermiyi de dondurur (silahla aynı saat). **Deterministik test saati:** `ProjectileSystem.Step(dt)` / `SimulateFor(seconds)` testlerden doğrudan sürülür; sürücü yalnız `Tick(Time.deltaTime)` çağırır.
3. **Çarpışma.** Her adımda segment `p → p + v·dt` (+ ½·g·dt² yerçekimi) **süpürülür**: `sweepRadius == 0` → `Physics.RaycastNonAlloc`, `> 0` → `Physics.SphereCastNonAlloc` (roket, küre, jel); maske `PrototypeLayers.BulletHitMask`, tetikleyiciler dahil (bugünkü hitbox'lar). Segment tüm adımı kapsadığı için hız ne olursa olsun **tünelleme yoktur** (test: 600 m/s mermi 0,05 m kalınlığında duvarı 1/20 s karede geçemez). Sonuçlar mesafeye göre sıralanır (mevcut `SortByDistance`).
4. **Hasar işlemi.** Mermi doğarken `DamageTransaction` kimliği ayrılır ve mermiyle taşınır; her çarpma çözümü o kimlikle **devam ettirilen** bir işlem kapsamında yürür (`DamageTransaction.Resume(id)` — B1'de eklenir), böylece bir merminin penetrasyonu, zinciri, sekmesi ve patlaması bugünkü gibi **tek işlem**dir ve düşman ikinci uygulamayı reddeder. Hedef çözümü, bölge, `ApplyHit` (StaggerGovernor, `EnemyHitFeedbackInfo`), prop darbesi (`PropImpacts.Report`), yüzey darbesi ve `ResolveBranches` (Chain/Ricochet/Explosion, ADR-034-1: yalnız pellet 0) **HitscanEngine'den paylaşılan çekirdeğe** taşınır (`ShotResolver`); hitscan yolu testler ve F1 için kalır, oyun silahları projectile kullanır.
5. **Penetrasyon.** Gövdeye çarpan mermi `penetration > 0` ise hasarı `penetrationDamageKeep` ile çarpılarak aynı adımda yoluna devam eder (dokunulan tampon aynı gövdeyi eler); dünya geometrisi mermiyi durdurur (M3 kuralı). Sniper'ın "zırh deler" çarpanı `armorDamageMultiplier` ile `DamageHit`'e taşınır.
6. **Menzil düşüşü.** Hasar = taban × `Lerp(1, falloffMinShare, InverseLerp(falloffStart, falloffEnd, katedilen))` — katedilen yol mermide tutulur (namludan).
7. **Nişan ışını ve paralaks (S-01 WP-070).** Tepeden modda bugünkü nişan düzlemi korunur. TP/FP'de hedef noktası **kamera merkezinden** atılan ışının ilk çarptığı nokta (yoksa `maxRange`); mermi namludan bu noktaya doğru çıkar (paralaks düzeltmesi). Namlu ile kamera arasında engel varsa (duvar dibinde nişan) mermi namlu–hedef segmentinin ilk engelinde çarpar (duvarın arkasına ateş yok). API: `IAimRaySource` (S-01 `CameraMode` sağlar), `WeaponController` okur.
8. **Görsel olay yüzeyi (S-07 WP-073).** `ProjectileEvents` (statik, struct yük, 0 ayırma): `Spawned{index, weaponId, kind, position, direction, speed, muzzleSocket}`, `Impact{weaponId, kind, position, normal, SurfaceKind, speed, caliber, flesh, armor, killed, transactionId}`, `Expired{index, position}`; roket/küre için `FlightStarted/FlightEnded`. İz çizimi olay değil **durum okuma**yla yapılır: `ProjectileSystem.LiveCount` + `ref readonly ProjectileState Get(i)` — S-07 kare başına örneklenmiş (instanced) iz çizer; mermi başına LineRenderer yok. B1 geçici olarak basit instanced iz çizer (S-07 değiştirene kadar oyun okunur kalır).
9. **Darbe verisi (S-06/S-03).** `DamageHit`'e eklenir: `WeaponClass`, `WeaponId`, `HitBone` (çarpan hitbox'ın kemiği/transform'u), mevcut `Direction` + `ImpactForce`; `EnemyHitFeedbackInfo` ve `EnemyDamageInfo` aynı alanları taşır (ragdoll/ölüm yönü, WP-071/WP-076).
10. **Ses olayları (S-08 WP-072).** `WeaponEvents.FireStarted/FireStopped{weaponId}` (döngülü ateş), `ProjectileEvents` darbe yükü ses için yeterli (SurfaceKind, hız, audioId türetilir, flesh/armor); yakından geçiş (flyby) için `ProjectileEvents.NearMiss{weaponId, position, speed}` — oyuncu kulağına ≤ 2 m geçen düşman dışı mermiler (yalnız TP/FP modunda anlamlı; tepeden modda kapalı).
11. **Special'lar** aynı sistemi kullanır: `sw_arc` küresi (Orb, yavaş, süpürme yarıçaplı; çarpmada `ArcChain` çözücüsü), `sw_gel` jel (Gel, yerçekimli yay; çarpmada doğrudan isabet kuralı + zemine yapışma → `GelPuddleSystem` havuzlu birikinti dizisi, tavanlı). Olaylar ADR-056-1 `SpecialUsageEvents` üzerinden.

**Bütçe ve ölçüm (§4.8: kısılmaz, ölçülür).** Hedef 0 B/kare (havuzlu struct, NonAlloc sorgular, önceden boyutlu tamponlar); CPU: 256 mermi × 2 alt adım ≈ 512 ışın/küre sorgusu/kare üst sınırı → B1'de ProfilerRecorder ile ms ölçülür (40 zombi + LMG sürekli ateş + pompalı), kademe tablosuna yazılır. Ayırma testi WP-059D kalıbında (tek karede N mermi + kontrol karesi + pozitif kontrol).

**Sonuçlar.** + hız/düşüş/penetrasyon gerçek; + çizim ve ses tek olay yüzeyinden; + hitscan'e göre deterministik test edilebilir. − Hasar çözümü bir-iki kare gecikir (mermi uçuşu) → 4–25 m çatışmada hız ≥ 200 m/s ile ≤ 0,125 s (tasarım kabulü); − `DamageTransaction` artık kare aşırı yaşar (Resume API + testleri). **Reddedilen seçenekler:** mermi başına GameObject + Rigidbody (ayırma, tünelleme, 256 × fizik maliyeti); görsel-yalnız projectile + hitscan hasar (hız farkı oyuna yansımaz, kullanıcı isteğine aykırı).

## §6 · A4 · WeaponDefinition v3 veri sözleşmesi (S-02, mühendislik)

**Tek kaynak kuralı.** Bir silahın OYUN sayıları (hasar, RPM, balistik, dağılım, oyun etkili recoil, cephane, reload, patlama, Special mekaniği) **silah başına bir ScriptableObject**'te tutulur: `WeaponDefinition` (v3). `PrototypeConfig.Weapons.ClassicWeapons` (v1 sayı listesi) ve `ClassicWeaponDefinition` (v1 kimlik SO'su) B1'de bu tek varlığa göç eder. **His (juice) sayıları** — görsel kick, mod başına kamera sarsıntısı/FOV punch, rumble — WP-062 `FeelConfig`'te kalır (S-07 WP-073 sahibi, A/B anahtarı ve profil içe/dışa aktarımı bozulmasın), aynı `weaponId` ile anahtarlanır. Görsel/ses/animasyon **varlıkları** tüketici oturumlarda kalır; tanımda yalnız **kimlikleri** vardır.

**Konum.** `Assets/_Prototype/Weapons/Arsenal/Resources/Arsenal/WD_<weaponId>.asset` (12 varlık: 10 klasik + 2 Special) + katalog `Arsenal/Resources/Arsenal/ArsenalCatalog.asset` (sıralı liste, doğrulama). Çalışma zamanı erişimi `Arsenal.Get(WeaponId)` / `Arsenal.Get("ar_1")` (0 ayırma, başlangıçta sözlük kurulur).

### Kimlikler (bağlayıcı)
| Anahtar (`weaponId`, veri/kayıt/telemetri) | Çalışma zamanı indeksi `WeaponId` (int, sabit) | Sınıf | Göç |
|---|---|---|---|
| `ar_1` | 0 | AssaultRifle | ← A `AssaultRifle` |
| `smg_1` | 1 | Smg | ← B `AutoSmg` |
| `sg_1` | 2 | Shotgun | ← C `CombatShotgun` |
| `ar_2` | 3 | AssaultRifle | yeni |
| `smg_2` | 4 | Smg | yeni |
| `sg_2` | 5 | Shotgun | yeni |
| `lmg_1` | 6 | Lmg | yeni |
| `lmg_2` | 7 | Lmg | yeni |
| `sr_1` | 8 | Sniper | yeni |
| `rl_1` | 9 | Launcher | yeni |
| `sw_arc` | 100 | Special | ← `KaguraBell` yerine (Kagura/Ofuda/Tide kaldırılır) |
| `sw_gel` | 101 | Special | yeni |
- `ClassicWeaponId` enum'u B1'de `WeaponId` olur; **0/1/2 değerleri korunur** (sahne/asset serileştirmesi int), üye adları v3'e döner (`Ar1`, `Smg1`, `Sg1`, …). Profilde saklanan eski adlar (`ProfileData.UnlockedWeapons`: "AssaultRifle"/"AutoSmg"/"CombatShotgun") yüklemede `WeaponIdLegacy` eşlemesiyle `ar_1`/`smg_1`/`sg_1`'e çevrilir — S-04 (WP-063 kayıt sözleşmesi) ile birlikte, kaybolan kilit yok. D (`HeavyPistol`, veri-yalnız) emekli: tanım dosyası silinir, git geçmişinde kalır.
- Türetilen kimlikler (tüketiciler bunlara bağlanır, tanımda ayrıca yazılmaz ama geçersiz kılınabilir): ses `audioId` = büyük harf (`AR1`, …, `RL1`, `SW_ARC`, `SW_GEL`; S-08); animasyon `animSet` = `weaponId` (S-06 klip kimliği `AP022_<tp|fp>_<animSet>_<action>`, yoksa sınıf fallback `AP022_<tp|fp>_<class>_<action>`); VFX profilleri `vfx.tracer/muzzle/impact` (S-07); HUD ikonu `Weapon.<weaponId>` (S-09); model `Arsenal/Models/<weaponId>_tp` + `_fp` (P-07 AP-021).

### Alan grupları (tip · birim · anlam)
**identity** — `weaponId` string · `id` WeaponId · `slot` {Classic, Special} · `weaponClass` {AssaultRifle, Smg, Shotgun, Lmg, Sniper, Launcher, Special} · `displayName` · `shortName` (HUD ≤ 10 karakter) · `description` (seçim ekranı 1 cümle) · `sortOrder` · `enabled` · `silhouette` {Long, LongX, Medium, ShortThick, Heavy, Tube} (LongX = sr_1, AD §1.1) · `legacyNames` string[] (profil göçü).

**fire** — `fireMode` {Single, Burst, Auto} · `rpm` (efektif; Single'da çevrim tavanı) · `burstCount` · `burstIntervalSeconds` (burst içi) · `burstDelaySeconds` (burst arası) · `damage` (pellet başına) · `pelletCount` · `pelletPattern` {Single, CenterRings, Gauss} · `rings[]` {count, radiusShare} (CenterRings: merkez 1 + halkalar, ör. sg_1 3 @ 0,40 + 4 @ 1,00) · `ringJitterShare` · `gaussSigmaShare` (Gauss, ör. sg_2 0,45) · `chargeSeconds` (tetik basılı şarj; bırakılırsa atış iptal, cephane harcanmaz — sw_arc 0,25) · `dryFire` bool.

**ammo** — `magazine` · `reserve` (koşu başı taşınan; −1 = sonsuz, M3 davranışı) · `chamberExtra` (boş olmayan reload'da +1) · `reloadKind` {Magazine, PerShell, Box, Single, Cell, Canister} · `reloadTacSeconds` · `reloadEmptySeconds` · `perShellSeconds` (PerShell) · `reloadCancellable` bool (PerShell: ateş iptal eder).

**projectile** — `kind` {Bullet, Pellet, Rocket, Orb, Gel} · `speed` m/s · `gravity` m/s² (aşağı) · `maxRange` m · `maxLifetimeSeconds` · `sweepRadius` m (0 = ışın; roket/küre/jel > 0) · `penetration` (gövde sayısı, 0 = yok) · `penetrationDamageKeep` (0–1, her gövdede kalan) · `falloffStart` m · `falloffEnd` m · `falloffMinShare` (0–1) · `impulse` (darbe kuvveti, DamageForce ölçeği) · `stagger` (0–1) · `headMultiplier` (bölgesel ×2,0'a EK çarpan; 1 = kural) · `armorDamageMultiplier` (zırh dayanıklılığına verilen hasar çarpanı) · `caliber` {Light, Medium, Heavy, Explosive} (ses/VFX/darbe verisi).

**accuracy** — `spread` 3 mod × 2 duruş: `topDown{hip, ads}`, `thirdPerson{hip, ads}`, `firstPerson{hip, ads}` (yarı açı °) · `bloomPerShot` (negatif = "oturma", lmg_2) · `bloomMax` · `settleFloorDegrees` (negatif bloom'un indiği taban; tetik bırakınca `bloomDecayPerSecond` ile geri açılır) · `bloomDecayPerSecond` · `bloomDecayDelaySeconds` (0,1) · `moveSpreadMultiplier` · `adsSeconds` · `moveSpeedMultiplier` (kuşanıkken) · `adsMoveSpeedMultiplier` · **oyun etkili recoil**: `recoilPitchPerShot` ° · `recoilYawPattern` {None, Random, Pattern} + `recoilYawValues` float[] (desen) · `recoilStableShots` · `recoilRecoverySeconds` · `recoilAccumulationCap` (× kick, varsayılan 4) · `topDownYawShare` (tepeden modda yatay desenin 2B payı, 0,6; dikey 0). Tepeden modda ADS = **odak** (sağ fare / sol tetik; zoom yok) ve `focusMoveSpeedMultiplier` (0,75). (Kamera sarsıntısı / görüntü modeli kick bu grupta DEĞİL → `FeelConfig` satırı `weaponId`.)

**explosive** (rl_1; ayrıca Explosion dalı veya Special kullanabilir) — `enabled` · `directHitDamage` · `blastDamage` · `innerRadius` m (tam hasar) · `outerRadius` m · `falloffExponent` · `regionShares` (boş = EnemyConfig %25/45/30) · `selfDamageShare` (0–1, oyuncuya) · `selfDamageMax` · `armingDistance` m (bu mesafeden önce çarpmada patlamaz) · `dudDamage` (kurulmamış roketin küt darbesi, 30) · `dudFizzleSeconds` · `impulse` · `lingerDps` + `lingerSeconds` (keystone "yanan zemin"; 0 = yok).

**rocket** (rl_1) — `launchSpeed` · `acceleration` · `maxSpeed` · `boostSeconds` · `gravityBoost` · `gravityCoast` · `wobbleDegreesPerStep` + `wobbleStepSeconds` (rastgele yürüyüş) · `selfDetonateSeconds` · `smokeTrailSeconds` (görsel süre; oyun etkisi yok).

**arcChain** (sw_arc) — `maxTargets` (ilk + sıçramalar) · `jumpRadius` m · `jumpDelaySeconds` · `targetRule` {NearestUnhit, Random} · `shockSeconds` (titreme; ölüm/ödül sonunda sayılır) · `tierRule[]` (tier → {Kill} | {MaxHpShare, StunSeconds}) · `selfShockRadius` + `selfShockDamage` · `selfShockFirstRadius` + `selfShockFirstDamage` · `selfSlowSeconds` · `orbFizzleSeconds` · `ignoresArmor`.

**gelPuddle** (sw_gel) — `directHitRule[]` (tier → {Kill} | {Kill if round ≤ R} | {MaxHpShare, KnockdownSeconds}) · `puddleRadius` m · `deathPuddleRadius` + `deathPuddleSeconds` (kayma ölümünden doğan birikinti; zincir) · `puddleLifetimeSeconds` · `maxPuddles` (en eskisi solar) · `fadeSeconds` · `zombieSlipRule[]` (tier → {FallSeconds, Kill} | {MaxHpShare, ImmuneSeconds}) · `playerSlideSpeedMultiplier` · `playerSteerMultiplier` · `slideExitSeconds` · `autoArcMaxRange` (tepeden modda imleç uzaklığına yay) · `ignoresArmor`.

**specialLifecycle** (yalnız Special) — `limit` {Time, Ammo, AmmoOrTime} (öneri: AmmoOrTime) · `activeCapSeconds` (Raiju 40, Namekuji 45) · `groundSeconds` · `spawnWeight`. WP-007 döngüsü (DEC-024..026) bu alanlarla sürülür; A2 kararı burada.

**upgrade** — `coreBranches` UpgradeBranch[3] (WP-034 kuralı; yeni dal türleri ArmorBreak / Impact / Cluster S-04 WP-077 kabulüne bağlı) · `keystone` {id, displayName, effect} (silaha özgü, koşu içi ağaç 3. kademe) · `explosionSemantics` {Chance, Blast} (rl_1 = Blast) · `forbiddenBranches` (ör. rl_1 Chain/Ricochet; Validate reddeder).

**unlock** — `unlockKind` {Default, Meta} (DEC-095: silah kilitleri kalıcı meta ağaçta) · `unlockOrder` · `metaCost` · `shopPriceBand` {min, max} (koşu içi satış varsa) (S-04 WP-077 doldurur; S-02 varsayılanı yazar).

**presentation (kimlikler)** — `audioId` · `fireLoop` bool (auto ≥ ~600 RPM, S-08) · `casing` {None, Rifle, Pistol, Shell, Link, Large} · `animSet` · `mechanismEvents` string[] (S-06 olay adları; S-08 aynı adlarla bağlar) · `vfx.muzzle` · `vfx.tracer` (profil kimliği; parametreleri S-07 profili taşır, varsayılanlar A1 sanat tablosundan) · `vfx.impact` (kalibre + yüzey matrisi) · `hudIconId` · `modelTp` · `modelFp` · `sockets` {root `socket_weapon_r`, `Muzzle`, `Ejection`, `grip_l`, `attach_barrel/top/stock`}.

### Çalışma zamanı yüzeyleri (tüketiciler için)
- `WeaponEvents.Classic` snapshot'ı `weaponId` + v3 sayıları (`CurrentSpreadDegrees`, U12a) taşımaya devam eder; `ClassicWeaponHudState.Id` tipi `WeaponId` olur.
- `WeaponEvents.FireStarted{weaponId}` / `FireStopped{weaponId}` (S-08 döngü sesi) — yeni.
- Projectile olayları (A3 ADR-069-1'de ayrıntı): `ProjectileEvents.Spawned / Impact / Expired` + roket/küre `FlightStarted / FlightEnded` (konum, yön, normal, SurfaceKind, hız, weaponId, caliber, flesh/armor bayrağı, TransactionId).
- `SpecialUsageEvents` (ADR-056-1) yeni türler: ArcCharge, ArcFire, ArcChainJump{index}, ArcShockStart/End, GelHit, GelPuddleCreated/Expired, GelSlideStart/End (oyuncu ve zombi ayrı).

### Doğrulama (EditMode kapısı, B1)
12 tanım var ve kimlikler benzersiz; her klasik silahın `coreBranches` 3 farklı çekirdek dal; sayılar bant içinde (RPM > 0, speed > 0, falloffEnd ≥ falloffStart, pelletCount ≥ 1, spread ≥ 0, reloadEmpty ≥ reloadTac); `fireLoop` ⇔ Auto ∧ rpm ≥ 600; Launcher'da `explosive.enabled` ve `rocket` dolu; Special'da `specialLifecycle` dolu; türetilen kimlikler tüketici katalogunda var mı (ses/anim/VFX eksikse uyarı, hata değil — üretim sürerken).

### Göç planı (B1)
1. `WeaponDefinition` + `ArsenalCatalog` + `Arsenal` erişimi eklenir; A/B/C sayıları (DEC-083 + WP-042 değerleri) `WD_ar_1/smg_1/sg_1`'e birebir kopyalanır → oyun davranışı değişmez (A/B/C karakterizasyon testleri yeşil kalır).
2. Tüketiciler (`WeaponController`, `ClassicWeaponRuntime`, `WeaponRoster`, seçim ekranı, HUD, ses haritası, anim `Weapon` int parametresi, FeelConfig `WeaponFeelId`, profil/sonuç, başarımlar, simülatör, QA botu) `PrototypeConfig.Weapons` yerine `Arsenal`'dan okur; F1 paneli etkin tanımın alanlarına bağlanır.
3. `PrototypeConfig.Weapons.ClassicWeapons` ve `ClassicWeaponDefinition` kaldırılır (git geçmişi); D emekli.
4. Profil: eski adlar → yeni kimlik (S-04 ile); anim `Weapon` int parametresi `WeaponId` değeri (0/1/2 aynı kalır → mevcut controller'lar kırılmaz; S-06 v3 override controller'a geçene kadar).
5. B2'de 7 yeni tanım A1 sayılarıyla eklenir (görseller P-07 gelene dek AP-013 blockout / sınıf yer tutucusu).

## §7 · Açık kararlar ve tüketici haritası

**🔴 Karar bekleyen (varsayılan = öneri, Faz B bununla ilerler):**
1. **Special yaşam döngüsü**: 11 s sabit süre → **cephane + süre tavanı** (Raiju 3/9 · 40 s, Namekuji 8/16 · 45 s; round-4 şansı, 22 s yerde bekleme ve tek Special kuralı korunur; §1 §7c). DEC-096 satırına girer; HUD (S-09) halka yerine şarjör/yedek + ikincil süre şeridi.
2. **Executioner direnci** (Raiju %60 + 2,5 s sersemleme; Namekuji devirme / %25 · %20) — referans "her şeyi öldürür"; tier ağırlığı için bilinçli sapma (§1 §7a/7b).
3. **Yeni dal türleri** ArmorBreak / Impact / Cluster + rl_1'de Explosion = "Blast" anlamı — S-04 WP-077 ağaç v2 kabulüne bağlı; kabul edilmezse yedek dallar (§1 §6).
4. **Kilit sırası** ar_1 → smg_1 → sg_1 → ar_2 → lmg_1 → sr_1 → smg_2 → sg_2 → lmg_2 → rl_1 (S-04 önerisinden 6.–7. sıra farkı; WP-077 karar verir).
5. **rl_1 kendine hasar** %45 / tavan 72 / kurma 4,0 m — ilk oynanış testinde ölüm oranı > %5 ise tavan 50.
6. **Görsel sadakat** (AD §10): jel rengi, Special pirinç payı, kara barut duman kimliği, kovan malzemesi (esmer bakır tabanlı balmumlu kâğıt kartuş), elektrik çarpması iskelet parıltısı yöntemi — varsayılan §0 madde 8.
7. **Tepeden "odak" tuşu** gamepad'de sol tetik — WP-024 eşlemesinde sol tetik boş mu (S-01/S-09 doğrular).

**Tüketiciler (kim neyi okur):**
| Oturum / paket | Okuyacağı bölüm | Bağlanacağı kimlik |
|---|---|---|
| P-07 AP-021 (görseller) | §0, §1 §2, §2 §1–§2 §5 §7 §8 | `weaponId`, silüet sınıfı, boy, soket/parça adları (P-07 blockout v001) |
| S-06 WP-071 / P-06 AP-022 (animasyon) | §4, §1 §3a (reload s), §0 | `animSet` = `weaponId`, klip `AP022_<tp\|fp>_<animSet>_<action>`, olay adları |
| S-07 WP-073 (VFX & his) | §2 §3–§7 §9, §4 §4–§5, §5 madde 8 | `ProjectileEvents`, `ProjectileSystem.Get(i)`, `vfx.*` profil kimlikleri, FeelConfig v2 `weaponId` satırları |
| S-08 WP-072 (ses) | §3, §5 madde 10, §6 presentation | `audioId`, `fireLoop`, `mechanismEvents`, `ProjectileEvents.Impact/NearMiss`, `SpecialUsageEvents` yeni türler |
| S-04 WP-077 (ağaç v2 / dükkân) | §1 §6, §6 upgrade/unlock | `coreBranches`, `keystone`, `unlockOrder`, `metaCost`, `shopPriceBand` |
| S-09 WP-068 R1 (seçim/HUD) | §0, §1 §2, §6 identity | `displayName`, `shortName`, `description`, `hudIconId`, `WeaponEvents.Classic` + reserve + spread |
| S-01 WP-070 (kamera) | §5 madde 7, §1 §4 | `IAimRaySource` / `TryGetAimRay`, `CameraModeService.IsTransitioning`, `CameraReactions.AddRecoil` |
| S-03 WP-076 (AI) | §5 madde 9, §1 §7 | `DamageHit.WeaponClass/WeaponId/HitBone`, Special tier kuralları |
| S-10 WP-078 (kabul) | §5 bütçe, §6 doğrulama | ayırma/perf ölçümleri, EditMode doğrulama kapısı |
