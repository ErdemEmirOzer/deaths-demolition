# WP-077 · Gelişim ağacı v2 — Yön belgesi (Faz A)

Sahip S-04 · 2026-09-24 · DEC-095 (iki katman: koşu içi altın ağacı + kalıcı meta ağaç).
Konsey (Fable 5.1): game-designer (lider, `wp-077/council/GD-PROPOSAL.md`), product-designer (`wp-077/council/PD-PROPOSAL.md`). Girdiler: `wp-077/council/FACTS.md` (mevcut ekonomi, simülasyon), `wp-077/council/S09-CONSTRAINTS.md` (S-09 bağlayıcı ızgara), WP-069 A1/A4 (`WP-069-ARSENAL-V3-DIRECTION.md` §1 §6 ve §6 veri sözleşmesi, 104a911).
Bütün sayılar **PrototypeOnly** konsey önerisidir; C-00 onayıyla DEC-097+ olur (§9). Bu belge iki önerinin uzlaştırılmış hâlidir: ayrıştıkları yerde seçilen yol ve gerekçesi yazılır, seçilmeyen alternatif §9'da kalır.

---

## 0 · Özet

| Katman | Kaynak | Ne açar | Ekran | Kalıcılık |
|---|---|---|---|---|
| **Koşu içi ağaç** | altın (koşu) | silah şeridi (13 düğüm) + beden şeridi (12 düğüm): kademe, dal keystone'u, kavşak, silah mührü, doruk, Special bağı, Ocak (sonsuz), Kese | Tur sonu ağaç ekranı (UiShopCard → sabit detay paneli), LB/RB iki şerit | koşu sonunda silinir (REQ-018) |
| **Meta ağaç** ("YOL / PATH") | **Kül** (profil) + seviye kapısı | 10 silah kilidi, Special eğilimleri, pasifler (ekonomi / beden ≤ %8 / yaşam kalitesi), kozmetik bağ | Ana menü PROGRESS › PATH, üç dal sayfası LB/RB, NEXT şeridi | profil şeması v5 |

Hedef his: **"Bu koşuda bir şey inşa ettim; koşular arasında ise ustalaştım."** Koşu içi = derinlik (dönüştürücü kararlar), meta = genişlik (yeni oyun tarzları). Meta ham hasar vermez.

Çözülen üç sorun (FACTS): (a) R10'dan sonra altın batağı yok → Ocak + doruk + kavşak; (b) doğrusal XP ile birkaç koşuda L20 → karesel eğri + Kül; (c) üç kartlık dükkân plan/kimlik taşımıyor → cepheden çekilen teklif + Nişan (pin) + görünür yol.

---

## 1 · Tasarım sütunları

| # | Sütun | Kural |
|---|---|---|
| S1 | Görünür yol, sınırlı el | Ağacın tamamı baştan görünür; her round 3 kart + Nişan + Ocak (DEC-047/087/050 korunur) |
| S2 | Her düğüm bir cümle | Kademe = sayı; keystone/kavşak/mühür/doruk = davranış (U11 önce→sonra satırı) |
| S3 | Altın hep bir yere gider | Ocak (kart dışı, sonsuz) + doruk fiyatları; cüzdan R15 ≤ 900 |
| S4 | Meta genişletir, ezmez | Meta hasar 0, beden gücü ≤ %8; silah/eğilim/keystone erişimi = seçenek |
| S5 | Onlarca saatlik ufuk | Karesel XP eğrisi + Kül: L10 ≈ 1,6 s · L20 ≈ 10 s · L30 ≈ 29 s · tam ağaç ≈ 45 s |

---

## 2 · Koşu içi ağaç

### 2.1 Düğüm tipleri

| Tip (`Kind`) | Davranış | Fiyat | Ön koşul | Teklife girer |
|---|---|---|---|---|
| **Tier** (kademe) | Mevcut dal, tekrar alınır (pip dolar) | `BaseCost × 2^tier` (DEC-085/070 aynen) | — | evet (OfferWeight) |
| **BranchKeystone** | Dalın davranışını dönüştürür, tek sefer | 110 (silah) / 140 (beden) | bağlı dal tier ≥ 2 | evet ×1,4 |
| **Fork** (kavşak) | İkili; biri alınınca diğeri DIŞLANMIŞ | 90 / 100 | şeritte ≥ 1 keystone | evet ×1,2 (çift tek kart, kartta X/Y) |
| **WeaponSeal** (silah mührü) | Silaha özgü keystone (WP-069 §6: Kōdō, Kaze, Manin, Kubi, Nagaya, Kabuto-wari, Tsuchi, Sōhatsu, Ishizue, Hibana) | 150 | şeritte ≥ 1 BranchKeystone | evet ×1,4 |
| **Capstone** (doruk) | Silahın kimliğini tanımlar | 380 (silah) / 320 (beden) | WeaponSeal + 2 BranchKeystone + 1 Fork | evet ×2,0 |
| **SpecialLink** | Elde taşınan Special'ı klasik silahla bağlar | I 80 / II 160 | koşuda o Special en az bir kez alınmış (meta "Önsezi" koşulu kaldırır) | evet ×1,2 |
| **Hearth** ("Ocak", sonsuz) | +%2 silah hasarı / ocak, tavan 20 | `50 + 30·n` | herhangi bir BranchKeystone | **hayır** — doğrudan alım, round başına ≤ 3 |
| **Purse** ("Kese") | Koşunun kalanında altın +%10 | 60 | — | evet ×0,8 |

### 2.2 Izgara (S-09: ≤ 4 kademe × 4 satır, kaydırma yok; iki şerit LB/RB)

**Silah şeridi (13 düğüm)** — satırlar = silahın üç çekirdek dalı (WP-069 `coreBranches`) + Special bağları

| Satır | K1 | K2 | K3 | K4 |
|---|---|---|---|---|
| 1 | Çekirdek dal 1 (Tier) | Keystone dal 1 | Kavşak X | — |
| 2 | Çekirdek dal 2 | Keystone dal 2 | Kavşak Y (X ile dışlayıcı) | **Doruk** |
| 3 | Çekirdek dal 3 | Keystone dal 3 | **Ocak** (sonsuz) | — |
| 4 | Special bağı I | Special bağı II | **Silah mührü** (WP-069) | — |

**Beden şeridi (12 düğüm, tüm silahlarda ortak)**

| Satır | K1 | K2 | K3 | K4 |
|---|---|---|---|---|
| 1 | Hareket (4 tier) | Kaçış (keystone) | Kavşak "Kül Adımı" | — |
| 2 | Azami Can (5) | Kalın Deri (keystone) | Kavşak "Demir Diz" (Kül Adımı ile dışlayıcı) | **İkinci Nefes** (doruk) |
| 3 | Yenilenme Gecikmesi (4) | Nefes (keystone) | Kese | — |
| 4 | Yenilenme Hızı (4) | Kanlı Adım (keystone) | — | — |

Toplam 25 düğüm; aynı anda okunan ≤ 13 (S-09 tavanı 16). Kenarlar: K1→K2 satır içi, K2→K3 "≥ 1 keystone" (şerit geneli; Silah mührü ve Ocak da bu kuralla), K3→K4 doruk kuralı (§2.1). Cephe (frontier) = ön koşulu sağlanmış, maks olmamış, dışlanmamış, meta ile açık düğümler. Tüm kademeler ekranda (4); PD'nin "cephe penceresi" gerekmez.

### 2.3 Dal keystone'ları (dal başına tek; 9 dal)

| Dal | Keystone | Önce → sonra |
|---|---|---|
| Piercing | **Hat** | delerken her gövdede hasar kaybı → kayıpsız ilk iki gövde |
| Ricochet | **Sekme Ustası** | rastgele sekme → en yakın düşmana yönelir |
| Explosion (şans; rl_1'de Blast) | **Kül Bulutu** | anlık patlama → 2 s yanan alan (hasar/s = patlama × 0,25) |
| Chain | **Ağ** | tek zincir → ilk sıçrama 2'ye çatallanır |
| FireRate | **Isınma** | sabit atış hızı → tetik 1,5 s basılı kalınca +%20 |
| ReloadSpeed | **Dolu Şarjör** | normal dolum → boş şarjörden dolumda bu şarjör +%25 |
| **ArmorBreak** (yeni, WP-069) | **Zırh Sökücü** | zırh çarpanı → plakaya ilk isabet 2× |
| **Impact** (yeni, WP-069) | **Sendele** | sersemletme → sersemlemiş hedef +%15 hasar alır |
| **Cluster** (yeni, WP-069 rl_1) | **Tohum** | 2–4 bombacık → +1 bombacık, her biri Blast kademesini devralır |

**WP-069 🔴 cevabı (S-04 kararı):** üç yeni dal (ArmorBreak / Impact / Cluster) ve rl_1'de Explosion = **Blast** anlamı **kabul**. `UpgradeBranch` enum'una 10–12 olarak eklenir (0–9 değerleri korunur, profil/sahne int serileştirmesi); `WeaponBranchContract.WeaponCoreBranches` 9 dala genişler; `branchSemantics.explosion` WP-069 v3 verisinde.

### 2.4 Silah alt ağaçları (10 sınıf; çekirdek dallar ve mühür WP-069 §6'dan, kavşak/doruk konsey önerisi — B2'de S-02 ile ince ayar)

| Yuva | Çekirdek 3 | Silah mührü (WP-069) | Kavşak X / Y | Doruk (380) |
|---|---|---|---|---|
| ar_1 Hinawa | Piercing · FireRate · ReloadSpeed | Kōdō | Uzun Nefes (bloom −%50) / Kısa Namlu (+%15 atış hızı, +bloom) | **Ronin Nişanı**: her 5. mermi zırhı yok sayar, sınırsız deler |
| ar_2 Sanren | Piercing · Ricochet · FireRate | Kubi | Sıkı Salvo (burst arası −%20) / Uzun Salvo (burst 3→4) | **Üçüncü Göz**: salvonun 3. mermisi kafaya isabet ederse zincirleme sekme |
| smg_1 Shippu | Ricochet · FireRate · Chain | Kaze | Çift Kabza (şarjör ×1,5) / Kızgın Namlu (Isınma eşiği 0,8 s) | **Kurşun Yağmuru**: sekme 2 kez |
| smg_2 Tetsui | Impact · Chain · FireRate | Tsuchi | Ağır Çekirdek (+sendeleme süresi) / Hızlı Çekiç (+%15 atış hızı) | **Yıldırım Dişi**: sersemlemiş hedefe her 3. isabet garantili zincir |
| sg_1 Rappa | Piercing · Explosion · ReloadSpeed | Manin | Sıkı Saçma (−%40 yayılma) / Geniş Saçma (+2 saçma) | **Kül Duvarı**: ≤ 3 m atış geri iter + 1 plaka kırar |
| sg_2 Nito | Explosion · Impact · ReloadSpeed | Sōhatsu | Ateş Ağzı (yakın +%20) / Hızlı Kırma (dolum −%25) | **İki Kılıç**: iki namlu aynı hedefe isabet ederse ikincisi patlar |
| lmg_1 Mukade | Piercing · Chain · ReloadSpeed | Nagaya | Sehpa (dururken +%25 hasar) / Kayış (ateşte hız cezası yok) | **Kesintisiz**: 40 isabetten sonra 6 s dolum gerekmez |
| lmg_2 Kurogane | Piercing · ArmorBreak · ReloadSpeed | Ishizue | Kızgın Kutu (+%15 atış hızı) / Soğuk Namlu (dolum sonrası ilk 10 mermi +%30) | **Kara Demir**: plaka kıran her mermi arkadaki gövdeye tam hasarla geçer |
| sr_1 Tomi | Piercing · Ricochet · ArmorBreak | Kabuto-wari | Nefes Tut (1 s bekle → ×2) / Hızlı Sürgü (+%35 atış hızı) | **Tek Kurşun**: ilk hedefte zırh yok, sınırsız delme |
| rl_1 Bohiya | Explosion(Blast) · Cluster · ReloadSpeed | Hibana | Yakın Fitil (küçük yarıçap, +hasar) / Uzun Fitil (büyük yarıçap) | **Kül Yağmuru**: patlama 3 küçük patlamaya bölünür |

Pellet sözleşmesi ADR-034-1 geçerli. Yeni motor kancası gerektirenler (Sehpa, Kayış, Kesintisiz, Kül Bulutu alan hasarı, Sekme Ustası hedefli sekme, Sendele, Kara Demir, İki Kılıç, İkinci Nefes ölümcül vuruş yakalama, Ocak çarpanı) efekt kimliği olarak veride tanımlanır; davranış kancaları WP-069 B2/B3'te S-02 ile (S-04 efekt → kanca eşlemesini yayımlar, §8).

**Special bağları (DEC-094: sw_arc Raiju, sw_gel Namekuji)**

| Special | Bağ I (80) | Bağ II (160) |
|---|---|---|
| Raiju (zincir yıldırım) | İletken: Special eldeyken klasik silahın Chain'i +1 sıçrama | Fırtına: Special öldürmesi klasik şarjörün %10'unu doldurur |
| Namekuji (kaydırıcı jel) | Kaygan Zemin: yavaşlamış/kayan düşmana +%15 hasar | Donma: jelde ölen düşman +1 altın |

### 2.5 Teklif / seçim — **hibrit** (GD; PD'nin serbest ağacı yerine)

| Seçenek | Karar | Gerekçe |
|---|---|---|
| Serbest ağaç (PD Q1-B) | ret (alternatif, §9 K1) | DEC-047/050/087'yi kaldırır; 25 düğümde karar süresi (PD Ö3 ≤ 25 s hedefi riskli); RNG heyecanı gider |
| Saf teklif | ret | ağaç dekor kalır, plan hissi yok |
| **Hibrit: cepheden 3 kart + Nişan + Ocak** | **seçildi** | plan (Nişan + görünür yol), kural sürekliliği (dükkân kodu ve S-09 Y = reroll aynen), sink (Ocak) |

Kurallar:
- Teklif = cepheden 3 farklı düğüm, ağırlıklı, yerine koymadan (`UpgradeOfferPolicy` korunur; havuz = cephe). Kavşak çifti tek kart.
- **Nişan (pin)** — PD'nin pin'i ile GD'nin Nişan'ı tek kavram: X / sağ tık, round başına 1, ücretsiz; işaretli düğüm cephedeyse **sonraki teklifte kesin gelir** ve reroll onu değiştirmez; cephede değilse en ucuz yol vurgulanır (PD §3.5) ve yol üstündeki ilk cephe düğümü garantilenir. Koşu boyunca yaşar, profile girmez.
- Şans koruması: cephede 3 round görünmeyen keystone/mühür/doruk ağırlığı ×2.
- Round başına 2 kart alımı (DEC-087) + Ocak ≤ 3 doğrudan alım. Reroll 10 altın, ilk alımda kapanır (DEC-050).
- **Son alımı geri al** (PD §7): aynı ekranda, CONTINUE'dan önce, son kart/Ocak alımı tam iade; reroll'u geri vermez; geri sayımın son 3 s'sinde kapalı. Yanlış basış sigortası (koşu içi alım tek basış, tutma yok — S-09).
- Koşu içi respec "Yeniden Dövme": yalnız kavşak X↔Y, koşuda 1 kez, R5+, yeni yolun fiyatı ödenir.
- `PoolExhausted` yalnız Ocak tavanı dolunca (n = 20).

### 2.6 Tempo ve fiyat (DEC-070 açılış bandı 15–45 kademelerde korunur)

| Düğüm | Fiyat | Beklenen ilk alım (medyan, ön iz §4.2) |
|---|---|---|
| Kademe T1…T5 (ör. FireRate) | 18 / 36 / 72 / 144 / 288 | R1–R2 / R3 / R7 / R12 / — |
| Dal keystone silah / beden | 110 / 140 | R4–R5 / R9 |
| Kavşak silah / beden | 90 / 100 | R6–R7 / R10 |
| Silah mührü | 150 | R6–R8 |
| Doruk silah / beden | 380 / 320 | R8–R10 / R12–R13 |
| Special bağı I / II | 80 / 160 | R9 / R14 (pickup'a bağlı) |
| Ocak n = 1…5 / 10 / 15 / 20 | 50 80 110 140 170 / 320 / 470 / 620 | R4 / R12 / R15 / R18+ |
| Kese | 60 | R3–R5 |

Ocak toplamı (20) 6 700 altın ≈ tüm R1–R15 geliri → geç oyun batağı doygunluğa ulaşmaz.

---

## 3 · Meta ağaç ("YOL" / PATH)

### 3.1 Kaynak: **Kül** + seviye kapısı
- Seviye = tempo kapısı (tüketilmez), Kül = seçim (harcanır). PD tek kaynak önerdi (Q3); GD ikili. **Seçilen: ikili** — seviye kapısı onlarca saatlik ufku ve "bir sonraki kapı" ritmini taşır, Kül seçim sahipliğini. Selection kilit metni tek satırda iki koşulu gösterir: "PATH · 120 Kül · L11" (ikisi de sağlanınca "PATH › ready").
- `Kül_koşu = floor(XP_koşu / 40) + ilk-kez bonusları + başarım ödülleri`. R8 koşusu (790 XP) → 19 Kül; R15 (2 900) → 72 Kül.
- İlk-kez: bir silahla ilk R10 → +30 (10 silah = 300); ilk doruk → +25; ilk Special bağı II → +15.
- Başarım ödülü nadirliğe göre: Common 5 · Uncommon 10 · Rare 20 · Epic 40 (27 mevcut tanım ≈ 400 Kül).
- Kül yalnız `ApplyRun` içinde, koşunun ordinal'iyle aynı transaction'da yazılır (WP-009H exactly-once); harcama ayrı atomik profil komutu (§7.3).

### 3.2 XP / seviye eğrisi: `Req(L) = 100·L + 10·L²` (L → L+1), tavan L50

| L | Req | Kümülatif | Saat @ 4 500 XP/s* |
|---|---|---|---|
| 2 | 110 | 0 | 0 |
| 5 | 750 | 1 300 | 0,3 |
| 10 | 2 000 | 7 350 | 1,6 |
| 15 | 3 750 | 20 650 | 4,6 |
| 20 | 6 000 | 43 700 | 9,7 |
| 30 | 12 000 | 129 050 | 28,7 |
| 40 | 20 000 | 283 400 | 63 |
| 50 | 30 000 | 526 750 | 117 |

\*4 500 XP/saat = R15 koşusu (~28 dk, 2 900 XP) ile R8 ölümlerinin karışımı; koşu süresi telemetriyle (WP-045) kalibre edilir. Bugünkü eğride ilk R15 koşusu L9; yenisinde L6.

### 3.3 Dal sayfaları (S-09: sayfa ≤ 6 kademe × 3 satır, LB/RB)

**WEAPONS (10 düğüm)** — kilit sırası WP-069 §6 (S-02 önerisi kabul: sr_1 6., smg_2 7.)

| Sıra | Yuva | Kademe · satır | Seviye kapısı | Kül |
|---|---|---|---|---|
| 0 | ar_1 Hinawa | 1 · s1 | açık | 0 |
| 1 | smg_1 Shippu | 2 · s1 | L3 | 40 |
| 2 | sg_1 Rappa | 2 · s2 | L5 | 60 |
| 3 | ar_2 Sanren | 3 · s1 | L8 | 120 |
| 4 | lmg_1 Mukade | 3 · s3 | L11 | 160 |
| 5 | sr_1 Tomi | 4 · s2 | L14 | 200 |
| 6 | smg_2 Tetsui | 4 · s1 | L17 | 240 |
| 7 | sg_2 Nito | 5 · s2 | L21 | 300 |
| 8 | lmg_2 Kurogane | 5 · s3 | L26 | 380 |
| 9 | rl_1 Bohiya | 6 · s2 | L30 | 480 |

Toplam 1 980 Kül. Silah ustalığı (öldürme sayacı, Kül harcamaz): M1 150 · M2 400 · M3 900 · M4 1 800 · M5 3 500 → M2 ikinci kavşak seçeneği görünür, **M3 doruk teklife girer** (öncesinde doruk yalnız Nişan ile), M5 kozmetik silah mührü. Eşikler koşu başı öldürme sayısıyla B3 simülasyonunda kalibre edilir. WP-069'daki koşu içi silah satış fiyat bandı **kullanılmaz** (silah kilidi yalnız meta; koşu içi silah satışı yok).

**SPECIAL (10 düğüm)**

| Kademe | Raiju (s1) | Namekuji (s2) | Ortak (s3) | Kapı / Kül |
|---|---|---|---|---|
| 1 | Çekim I: pickup ağırlığı +%25 | Çekim I | — | L4 / 50 |
| 2 | Önsezi: Bağ I pickup'tan önce cephede | Önsezi | Erken Çan: ilk Special ≤ R4 garanti | L7 / 90 · 90 · 120 |
| 3 | Bağ II erişimi | Bağ II erişimi | — | L12 / 140 |
| 4 | Raiju Doruğu: süre +%20 | Namekuji Doruğu: alan +%20 | Çift Çan: koşuda ikinci Special türü de düşer | L18 / 220 · 220 · 260 |

Toplam ≈ 1 360 Kül. Doruklar dışlayıcı (koşu başında aktif eğilim seçilir).

**PASSIVES (18 düğüm; 3 satır × 6 kademe)**

| Satır | K1 | K2 | K3 | K4 | K5 | K6 |
|---|---|---|---|---|---|---|
| Ekonomi (seçenek) | Nişan II (2 nişan) | Reroll 10 → 7 | Ocak eğimi 30 → 27 | Kese açılışta | İlk teklif 4 kart (R1) | Doruk −%10 fiyat |
| Beden (stat, toplam ≤ %8) | +%2 hareket | +5 can | −0,2 s regen gecikme | +%2 hareket | +5 can | −0,2 s regen gecikme |
| Yaşam kalitesi (0 güç) | Ağaç önizleme (menü) | Teklif geçmişi | Koşu istatistik paneli | Önce→sonra ayrıntı satırı | HUD yol göstergesi | Mıknatıs +0,5 m (🔴 ürün kararı) |

Kül K1 60 · K2 90 · K3 130 · K4 180 · K5 240 · K6 320 (satır 1 020; sayfa 3 060). Kapı K1 L2 · K2 L6 · K3 L10 · K4 L15 · K5 L22 · K6 L28.

**Kozmetik bağ (≤ 12 düğüm, PROFILE içinde)**: unvan/rozet/HUD göstergesi; başarımla (mevcut unlock kataloğu, 9 giriş) **veya** Kül (80–200) açılır.

### 3.4 Halka bütçesi (hedef süre)

| Halka | Süre | Açılan | Kül ihtiyacı |
|---|---|---|---|
| 1 | ≤ 1 s | smg_1, sg_1, Çekim I, Pasif K1 satırı | ≈ 330 |
| 2 | ≤ 10 s | ar_2, lmg_1, sr_1, smg_2, Special K2–K3, Pasif K2–K4 | ≈ 2 000 |
| 3 | 30+ s | sg_2, lmg_2, rl_1, Special dorukları, Pasif K5–K6, kozmetik | ≈ 3 900 (kümülatif ≈ 7 300) |

Halka 2 sonunda bilinçli kıtlık (tercih zorlaması); Halka 3'te seviye kapısı bağlar.

### 3.5 Güç tavanı
Meta hasar 0. Beden satırı toplamı +%4 hareket, +10 can, −0,4 s regen gecikme → etkin güç ≤ %8 (simülasyon ölçütü: R15 risk farkı ≤ 0,5). Ekonomi satırı gelir etkisi ≤ +%10. Ustalık ve eğilimler yalnız erişim.

---

## 4 · Ekonomi

### 4.1 Kaynak / sink

| Kaynak | Source | Sink | Hedef |
|---|---|---|---|
| Altın (koşu) | öldürme + zırh bonusu, Kese +%10, Donma +1 | kademe, keystone, kavşak, mühür, doruk, Special bağı, Ocak, reroll, Yeniden Dövme | R1–R15 harcama payı %65–80, R10–R15 ≥ %60, cüzdan R15 ≤ 900 |
| XP (profil, harcanmaz) | round + öldürme (değişmez) | seviye kapısı | L10 1,2–2,0 s · L20 7–12 s |
| Kül (profil) | XP/40, ilk-kez, başarım | meta düğümler, meta respec kesintisi | Halka 2 sonu bakiye ≤ 150 |
| Ustalık (silah başına, harcanmaz) | o silahla öldürme | doruk teklif kapısı | M3 ≈ 10–12 koşu/silah |

### 4.2 Ön iz (GD, aç gözlü bot = üst sınır; FACTS round geliri)
R1–R15 gelir 6 573 · harcama 6 536 (**%99 üst sınır**) · 15 Ocak · ilk keystone R4 · ilk doruk R8 · cüzdan R15 37. İnsan oyuncu rezerv tuttuğu için beklenen pay %65–80 (bugün %36–45). Duyarlılık: gelir ±%20 eşikleri bozmaz. **Bağlayıcı kısıt altın değil 2-kart limiti** → Ocak'ın kart dışı olması şart (K3).
Bu iz scratch hesabıdır; **WP-041 simülatöründe ağaç modeli B3 diliminde** kurulur (EconomySimulator.TreeModel: cephe, ağırlıklı teklif, Nişan, Ocak, 2 alım) ve §4.3 ölçütleri seed 1–20 × 4 silah ile raporlanır; sonuç bu belgeye tablo olarak eklenir.

### 4.3 Simülasyon kabul ölçütleri

| Ölçüt | Hedef | Kırmızı |
|---|---|---|
| Harcama payı R1–R15 | %65–80 (bot üst sınırı ≤ %99) | < %55 |
| Harcama payı R10–R15 | ≥ %60 | < %40 |
| Cüzdan R15 | ≤ 900 | > 1 500 |
| İlk keystone / doruk round'u (medyan) | R4–6 / R9–11 | doruk p90 > R13 |
| PoolExhausted R20 öncesi | 0 | > 0 |
| Risk vekili R15 | 4,5–5,5 (bugün 6,09) | > 6,5 → Ocak +%1,5 |
| Meta güç farkı (tam beden satırı vs 0) | R15 risk Δ ≤ 0,5 | > 0,8 |
| XP tempo | L10 1,2–2,0 s · L20 7–12 s | — |

### 4.4 B3 sonucu (DEC-098, 2026-09-24)
`EconomySimulator.TreeModel` (WP-041 round modeli + sevk edilen RunTreeState, K1 çekilişi `DrawFrontier`, 2 kart, Ocak, Kese, ilk Special), %90 toplama, seed 1–20 × ar_1/smg_1/sg_1 (sr_1 sim kadrosunda yok). §4.2 iki bot olarak yeniden yazıldı (denetim #6):

| bot | pay R1–15 | pay R10–15 | cüzdan R15 medyan / p90 | ilk keystone | doruk medyan / p90 | Ocak |
|---|---|---|---|---|---|---|
| harcama-azami (pinsiz) | %96 | %97 | 233–259 / 918–2 673 | R7 | M1 sonrası R15, %50 koşuda | 20 (R19 dolu) |
| acele (doruğa pin) | %96 | %106 | 234 / 438 | **R4** | **R9 / R10** | 20 (R18) |
| insan (%30 yastık, ≤ 2 Ocak) | %86–87 | %90–91 | **819–888** / 2 664–2 890 | R8 | M1 sonrası %23 koşuda | 20 |
| biriktirici (%50 yastık, ≤ 1 Ocak) | %44–46 | %42–48 | 3 356–3 462 / 4 132–4 208 | R10 | — | 11 |

Ne-olursa: Ocak tavanı 30 R15'e kadar hiçbir şeyi değiştirmez (tavan R18–19'da dolar); R10'dan itibaren 3 kart insan cüzdanını 721'e indirir ama havuz 60 koşunun 34–37'sinde R20 öncesi tükenir → reddedildi. Biriktirici cüzdanı ekonomi dışı bir kararın konusu (açık). Kül temposu (Kül = XP / 30): halka 1 150 Kül = 4 500 XP/s'te 1,0 s; halka 1+2 2 080 Kül ≈ 11,3 s (tek seferlik havuzunun yarısıyla; havuz 775 Kül); tümü 5 830 Kül ≈ 39 s. Round modelinin kendi hızı (WP-045 bot kalibrasyonu, 2 600–3 300 XP/s) bu süreleri ~1,5× uzatır; gerçek oyuncu hızı S-10 telemetrisiyle ölçülecek. Risk vekili ve meta güç farkı satırları ağaç modelinde henüz yok (Ocak hasarı risk modeline girmiyor).

Telemetri olayları (WP-045 `dd-run-telemetry` v2 alanları): `tree_node_bought{id,round,price,kind,lane}`, `tree_pin_set{id,round}`, `offer_drawn{ids,frontier,pity}`, `hearth_stoked{n,round}`, `tree_undo`, `fork_reforged`, `meta_node_bought{id,level,kul}`, `meta_respec{refund}`, `mastery_tick{weapon,m}`.

---

## 5 · Başarım bağları (WP-023/035)

| Mevcut | Bağ |
|---|---|
| reach_round_10 / 15 / 20 | Kül 10 / 20 / 40 |
| defeat_an_ultra / ultra_hunter | Kül 20 / 40; ultra_hunter → sg_1 doruğu için ustalık M3 şartı kalkar |
| armor_breaker_100 | Kül 20; Zırh Sökücü keystone R1'den cephede |
| special_pickup_10 | Kül 10; SPECIAL K1 seviye kapısı anında |
| flawless_rounds_10 | Kül 40 |
| run_gold_400 / run_upgrades_5 | Kül 20 / 10 |
| level_5 / level_10 | Kül 10 / 20 (seviyeler yeni eğride yeniden hesaplanır, §7.2) |

Yeni başarımlar (10, hiçbiri ham güç): `first_capstone` (C) · `three_keystones_one_run` (U) · `hearth_10_one_run` (U) · `hearth_20` (E) · `full_lane` — kademe maks + keystone + kavşak + doruk aynı şeritte (R) · `capstones_5_weapons` (E, Lifetime) · `second_breath_used` (U) · `both_special_links` (R) · `mastery_5_any` (E) · `no_reforge_r15` (R). Yeni koşullar `AchievementCondition`'a eklenir (koşu içi olanlar `RunFeatCounters` üstünden, SingleRun; REQ-018 sınırı korunur).

---

## 6 · Respec

| Kapsam | Ne | Bedel | Kısıt |
|---|---|---|---|
| Koşu içi "son alımı geri al" | son kart/Ocak alımı | tam iade | aynı ekran, CONTINUE öncesi, geri sayımın son 3 s'si hariç |
| Koşu içi "Yeniden Dövme" | kavşak X ↔ Y | yeni yolun fiyatı; iade yok | koşuda 1, R5+ |
| Meta — pasif / eğilim | tüm pasif + Special eğilimi | ilk respec ücretsiz; sonra %75 iade (%25 Kül sink) | HoldConfirm 500 ms + modal; **silah düğümleri iade edilmez** (kazanılmış içerik, roster geri daralmaz) |

---

## 7 · Veri, mantık, kayıt (Faz B girdisi)

### 7.1 Veri
- `ProgressionTreeDefinition` (SO, Resources; sevk verisi JSON'dan içe alınır — WP-023 katalog deseni): düğümler `{id, layer: Run|Meta, lane, tier, row, kind, parents[], exclusionGroup, maxTier, branch?, weaponSlot?, costBase, costGrowth, costFlat, levelGate, kulCost, effectId, effectParams, offerWeight, glyphId, titleKey, descKey}`. Doğrulama: id tekil, ebeveyn var, döngü yok, dışlama grubu ≥ 2 üye, ızgara sınırı (Run 4×4 şerit başına, Meta 6×3 sayfa başına), DEC-070 bandı.
- Koşu içi silah şeridi silah başına şablondan kurulur: `coreBranches` + `keystone` WP-069 WeaponDefinition v3'ten (sözleşme §6 `upgrade`/`unlock` alanları), kavşak/doruk ağaç verisinde silah yuvasıyla eşlenir.

### 7.2 Profil şeması v5 (WP-009H + WP-063)
- Yeni alanlar: `KulBalance`, `KulEarnedTotal`, `KulSpentTotal`, `MetaNodes` (id listesi), `MetaRespecCount`, `MetaPinnedNode`, `WeaponMastery` (yuva → öldürme), `MetaSeen`.
- Göç v4 → v5: lineage/ordinal aynen (uygulanmış hiçbir koşu yeniden uygulanmaz). Seviye yeni eğriyle `TotalXpEarned`'dan yeniden hesaplanır. **Açık silahlar asla geri kilitlenmez**: `UnlockedWeapons` (eski adlar AssaultRifle/AutoSmg/CombatShotgun → ar_1/smg_1/sg_1, WP-069 kimlik göçü) karşılığı meta düğümler "sahip" olarak yazılır. Geriye dönük Kül = `floor(TotalXpEarned/40)` + mevcut başarımların nadirlik ödülleri. Seviyeye bağlı mevcut unlock/başarımlar (level_5/10) korunur.
- Kül kazanımı `ApplyRun` içinde (istatistik → XP → Kül → başarım → unlock → ordinal → tek yazım); meta satın alma/respec ayrı komut: aday profil klonu → doğrula → `ProfileStore.Save` → başarıysa yayımla; başarısızsa RAM değişmez; aynı düğümü ikinci alım `AlreadyOwned` (idempotent). REQ-018: altın/koşu içi ağaç profile girmez (sınır testi genişler: `RunTree*` üyeleri profile yok).

### 7.3 S-09 veri yüzeyi (salt okunur snapshot, 0 B/kare)
`TreeSnapshot`: `Layer`, `LaneCount`, `Nodes[] {NodeId, Tier, Lane, Row, Kind, ParentIds[], ExclusionGroup, MaxTier, OwnedTier, Status(7), Cost, Shortfall, MissingParentId, ClosedById, BeforeAfter[≤2], NewlyUnlocked, IsPinned, InOffer}`, `FocusPath(nodeId) → ids[]`, `PinPath[]`, `PathTotalCost`, `Gold | Kul`, `PicksUsed/PicksMax`, `HearthCount/HearthThisRound`, `UndoAvailable`, `ReforgeAvailable`, `NextMetaNode`, `AvgKulPerRun`, `PathCompletion`, `LastRunKulDelta`. Durumlar (S-09 7): Locked · PrereqMissing · Available · Unaffordable · Purchased · Excluded · MaxTier; bindirme bayrakları New / Pinned / InOffer ("TEKLİFTE DEĞİL" 8. durum yerine `InOffer=false` bindirmesi — K9). Komutlar: `TryPurchase(id)`, `TryStokeHearth()`, `TryUndoLast()`, `TryReforge(forkId)`, `SetPin(id?)`, `TryReroll()`, `TryMetaPurchase(id)`, `TryMetaRespec()`, `MarkSeen(id)`; red nedeni enum.

### 7.4 Ekran gereksinimleri (S-09 WP-068 R1)
S-09'un 24.09 sınırları aynen (S09-CONSTRAINTS). PD §5 kabul edilen kısımlar: 7 durum şekil+glif+kelime tablosu (§5.1), yol vurgusu (§5.2), U11 + ghost pip (§5.3), açılış Beat 120 / ≤ 600 ms ve satın alma dizisi (§5.4), gamepad kenar-komşu modeli — **X = Nişan/pin**, Y = reroll, A = al (koşu içi tek basış, meta HoldConfirm 500), B = geri (§5.5), perf (kenarlar tek Graphic, tek atlas, ≤ 5 canvas, 0 B; §5.8), Results PATH bloğu + NEXT şeridi + Selection kilit metni (§4). PD'nin "cephe penceresi" gerekmez (koşu içi ağaç 4 kademe). Açık S-09 konuları: meta kademe adımı (6 kademe × 214 px pane'e sığmaz → PD 119 px öneriyor) ve pin tuşu X.

---

## 8 · Paydaş sözleşmeleri
- **S-02 WP-069:** kilit sırası (§3.3) ve üç yeni dal + Blast kabul; S-04 `UpgradeBranch` 10–12 ekler ve efekt kimliği → davranış kancası eşleme tablosunu B2'de yayımlar; kanca uygulamaları (projectile/pellet/patlama tarafı) WP-069 B2/B3. Profil `UnlockedWeapons` ad göçü: **S-04 `ProfileMigration` v5 adımında yapar** (kayıt dosyaları S-04'te), S-02 `WeaponId` enum'u ve eşleme tablosunu verir.
- **S-09 WP-068 R1:** ekranlar; veri yüzeyi §7.3 B1/B2 dilimleriyle gelir (ilk önce koşu içi).
- **S-10 WP-078:** yeni XP eğrisi ve Kül tempo telemetrisi kabul ölçümüne girer.

## 9 · Açık kararlar (C-00 → DEC-097+)

| # | Karar | Öneri (bu belge) | Alternatif |
|---|---|---|---|
| K1 | Teklif modeli | Hibrit: cepheden 3 kart + Nişan, DEC-047/050/087 korunur | PD: serbest ağaç, reroll kalkar (DEC-050 kapanır); ya da + Omen |
| K2 | Nişan bedeli | ücretsiz, 1/round | 5 altın; ya da meta pasifle açılır |
| K3 | Ocak (yeni kural) | kart dışı doğrudan alım ≤ 3/round, `50+30n`, tavan 20, +%2 hasar | tavan 30; ya da Ocak kart olarak gelir |
| K4 | XP eğrisi | `100L + 10L²`, tavan L50 | `120L + 8L²`; ya da tavansız + prestij |
| K5 | Meta ham güç | beden satırı ≤ %8, hasar 0 | yanal: beden satırı kaldırılır, 6 yaşam kalitesi düğümü |
| K6 | Doruk erişimi | ustalık M3 ile teklife girer, öncesinde yalnız Nişan | ustalık kapısı yok |
| K7 | Meta kaynak | ikili: seviye kapısı + Kül | PD: tek harcanabilir puan |
| K8 | Keystone düzeni | dal başına tek (9) + silah mührü (WP-069, 10) | dal başına A/B çifti (ızgaraya sığmaz) |
| K9 | "Teklifte değil" gösterimi | `InOffer` bindirme bayrağı (7 durum korunur) | 8. durum |
| K10 | Koşu içi respec | son alımı geri al + Yeniden Dövme (1/koşu) | yalnız geri al; ya da hiç |
| K11 | Mevcut profil göçü | seviye yeni eğriyle yeniden hesaplanır (düşebilir), açık silahlar korunur, geriye dönük Kül | eski seviye taban tutulur (XP eğrisi yalnız sonrası için) |

Dilimler (her biri ayrı commit, C-00'a teslim): **B1** ağaç verisi + koşu içi mantık (cephe teklifi, Nişan, Ocak, geri al, Yeniden Dövme) + S-09 snapshot · **B2** meta ağaç + profil v5 göçü + Kül/ustalık + XP eğrisi + meta komutları + efekt→kanca tablosu · **B3** EconomySimulator ağaç modeli + ölçüt tablosu · **B4** başarım bağları + yeni başarımlar.
