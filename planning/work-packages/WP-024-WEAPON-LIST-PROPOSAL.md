# WP-024 — Klasik Silah Listesi: ÖNERİ NOTU

**Durum:** Öneri, karar değil; kullanıcı seçer. **Yazar:** game-designer · 2026-09-16 · Tek sayfa. Sayısal değer içermez (hasar/menzil/tempo hepsi PrototypeOnly).

## 1. Amaç ve sınır

- DEC-005 çerçevesi korunur: koşu başında **tek** klasik silah (REQ-050/051), build kimliği **dallarla** oluşur (REQ-017); Special Weapon ayrı ve etkilenmez (REQ-059).
- İsim/lore icat edilmez; yalnız **tür** tanımı. Görsel dil DEC-013 ile uyumlu **erken dönem ateşli silah sınıfı** (fitilli/kibritli kilit, ahşap kundak, uzun sekizgen namlu hissi); mekanizma görsel dildir, ateş temposu oynanış soyutlamasıdır (REQ-039 otomatik ateş korunur, DEC-012 stilize gerçekçilik).
- Dal kimlikleri mevcut `UpgradeBranch` enum'undan. **Silah-çekirdek dallar:** Piercing, Ricochet, Explosion, Chain, FireRate, ReloadSpeed. **Oyuncu dalları** (MovementSpeed, MaximumHealth, RegenerationDelay, RegenerationRate) silahtan bağımsız, herkese ortak kalır.
- Kod bulgusu (dal atamasını şekillendirir): dallar **pellet başına** çözülür; çok saçmalı silahta Explosion şansı pellet sayısına bölünür ama **Ricochet ve Chain bölünmez** (`ClassicWeaponRuntime.BuildShot`); Chain sabit hafif güç taşır. Öneri bu etkileşimi kod değişikliği yerine dal atamasıyla yönetir.

## 2. Aday liste

| # | Aday (tür) | Rol / his (menzil · tempo · kontrol) | Hitscan / dal uyumu | Siluet (REQ-105, yükseltilmiş kamera) | Önerilen 3 çekirdek dal | Placeholder eşleme |
|---|---|---|---|---|---|---|
| A | **Tüfek tipi** (uzun namlulu, tek ışın, otomatik) | Orta menzil · orta tempo · kontrollü; genel amaçlı omurga | Tek düz ışın → **Piercing** doğal (sürüyü çizgi hâlinde deler); tempo dalı doğrudan DPS | En uzun yatay çizgi; en okunur sınıf; referans siluet | Piercing · FireRate · ReloadSpeed | AssaultRifle (varsayılan, açık) |
| B | **Karabina tipi** (kısa namlu, hızlı tekrar, yayılma büyümeli) | Kısa-orta menzil · yüksek tempo · düşük kontrol; hareketli/yakın sürü | Çok mermi/sn → **Chain** ve **Ricochet** atış başına tetiklendiği için en verimli taşıyıcı; hafif güç Chain ile uyumlu | Orta uzunluk, kompakt; tüfekten ayrışması için belirgin kısa kundak/kabza gerekir | Chain · Ricochet · FireRate | AutoSmg |
| C | **Pompalı tipi / geniş ağızlı çok saçmalı** | Yakın menzil · düşük tempo · yüksek geri tepme; sendeletme/kapı tutma | Pellet başına **Piercing** çarpan olur (istenen); **Explosion** şansı zaten pellet-normalize; Chain/Ricochet verilmez (pellet çarpanı dengeyi kırar) | Kısa, kalın, geniş ağızlı; genişlik/uzunluk oranıyla en ayrık ikinci sınıf | Explosion · Piercing · ReloadSpeed | CombatShotgun |
| D | **Tabanca tipi (ağır tek atış)** — isteğe bağlı 4. | Uzun menzil · çok düşük tempo · yüksek hassasiyet; "her atış önemli" | Atış başına yüksek hasar → **Explosion** ve **Ricochet** hasar ölçeğiyle en güçlü tekil vuruş; Piercing hattı | En küçük siluet: kamera mesafesinde risk; tek elle tutuş + belirgin namlu alevi/tracer ile telafi | Explosion · Ricochet · Piercing | Eşleşme yok (yeni `ClassicWeaponId`) |

Dal kesişim mantığı: her silahın 3 çekirdek dalı **farklı vurgu** taşır (A: hat/tempo, B: yayılım/sıçrama, C: alan/hat, D: tekil patlayıcı); iki silah aynı üçlüyü paylaşmaz, her çekirdek dal en az iki silahta bulunur (Chain yalnız B: karar sorusu 3).

## 3. AP-013 üretim etkisi

| Kalem | Öneri | Not |
|---|---|---|
| Model sayısı | Silah başına **1 taban model + 3 dal eklentisi** (soket tabanlı) | 3 silah → 12 mesh; 4 silah → 16 mesh. Kademe (tier) yeni mesh değil: malzeme/emissive/küçük parça ölçeği |
| Alternatif | Ortak 6 dal × N silah | 3 silah → 21, 4 silah → 28 mesh; siluet uyum riski, önerilmez |
| Renk | Altın bandı (stil kılavuzu v1 §altın) **yasak**: pickup rezervi; silah gövdesi nötr ahşap/demir, dal eklentileri rezerv dışı vurgu | REQ-105 çelişmez |
| Siluet sınıfı | Uzun-ince (A) · orta-kompakt (B) · kısa-geniş (C) · kısa-ince (D) | Kamera mesafesinde yalnız oran okunur; uzunluk sınıfı zorunlu |
| Kaba iş sırası | 1) A taban (varsayılan, mevcut kod) → 2) C taban (en ayrık siluet, soket testi) → 3) B taban → 4) D taban (seçilirse) → 5) A-Piercing eklentisi pilot → 6) kalan eklentiler | Eklentiler taban modeller kabul edildikten sonra |

## 4. Karar soruları (kullanıcı)

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kaç silah? | 3 (A/B/C) · 4 (A/B/C/D) | 3 ile başla; D sonraki dilim |
| 2 | Hangi adaylar? | Tablodaki A–D; tür değişikliği serbest | A/B/C mevcut placeholder'ları birebir karşılar |
| 3 | Dal atama ilkesi | (a) silaha özgü 3 çekirdek dal + ortak oyuncu dalları · (b) 6 çekirdek dal herkese ortak · (c) karma: 2 özgü + 1 ortak | (a): build kimliği + üretim maliyeti dengesi |
| 4 | Açılış sırası (DEC-008) | (a) A açık, B/C(/D) profil seviyesiyle · (b) A+B açık, C(/D) kilitli · (c) hepsi açık | (a): mevcut `UnlockProfileLevel` sırası (A→B→C) |
| 5 | D için siluet riski kabul mü? | Kabul (alev/tracer telafi) · Ret (D yerine "uzun tek atış tüfek tipi") | Karar 1'e bağlı |

## C-00 için 5 satır özet

1. Öneri notu yazıldı; karar değil, kod ve planlama dosyaları değişmedi.
2. 4 aday tür: tüfek (A), karabina (B), pompalı/geniş ağızlı (C), ağır tek atış tabanca (D, isteğe bağlı); A/B/C mevcut placeholder'larla eşleşir, D yeni id ister.
3. Dal ilkesi önerisi: silaha özgü 3 çekirdek dal + ortak 4 oyuncu dalı; Chain/Ricochet pompalıya verilmez (pellet çarpanı kod bulgusu).
4. AP-013 etkisi: silah başına 1 taban + 3 dal eklentisi (3 silah = 12, 4 silah = 16 mesh); altın bandı yasak; iş sırası A→C→B→D→eklentiler.
5. Kullanıcı kararı bekleyen 5 soru: silah sayısı, adaylar, dal ilkesi, açılış sırası, D siluet riski.
