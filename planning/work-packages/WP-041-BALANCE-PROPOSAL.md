# WP-041 — Denge Simülasyonu v1: Bulgular ve Öneri (S-04, 2026-09-17)

Kaynak: `EconomySimulator` (ADR-041-1) · seed 1 · R1–R15 · sevk edilen veriler (main `a39c20c`), hiçbir değer değiştirilmedi. Tam tablo: `wp-041/evidence/balance-v1.md` / `.json` (komut: `-executeMethod DD.Prototype.Diagnostics.EditorTools.EconomySimulator.RunFromCommandLine -ddOut <dir>`). Model varsayımları (isabet %70, altın toplama %90, temas süresi %30 × 3 saldıran, round başına en ucuz çekirdek kart) belgede işaretlidir; **mutlak sayılar vekildir, eğilimler ve oranlar bulgudur.**

## 1. En önemli 5 bulgu

| # | Bulgu | Kanıt (A · Assault Rifle; B/C aynı eğilim) | Sahip |
|---|---|---|---|
| 1 | **Altın enflasyonu:** tek satın alma/round (DEC-071) + fiyat büyümesi 1,5–1,6 gelirin ~%12'sini emiyor. R15'te gelir 6 009, harcama 713, cüzdan **5 296** harcanmamış; R5'te bile cüzdan 334 iken en pahalı kart 45. | gold sütunu: R1 +23, R5 +182, R10 +508, R15 +851; kart t5 = 76–112 g | S-04 (ekonomi) + ürün (DEC-071) |
| 2 | **Zırh doygunluğu:** slot başına şans R1 %8–15 → R8'de her tier'da zırhlı pay **%90–100**, Guard/Executioner girişten itibaren %100. Zırh "istisna" olmaktan çıkıp varsayılan oluyor; TTK R8 sonrası düzleşiyor (Standard 1,3→1,7 s) çünkü zırh tavana dayanmış. | armoured% sütunu; `ChanceGrowthPerRound` 0,025–0,03, `MaxChance` 0,75–0,95 | S-03 (tier/zırh verisi) |
| 3 | **İlerleme çok hızlı (DEC-008):** ilk koşu R8'de biterse profil **L4**; B (L2) R3'te, C (L3) R5'te — ikisi de ilk koşuda açılıyor. Level eşiği 100/175/250… ile round XP 15+5r + kill XP. | XP cum → L sütunu: R3 142 → L2, R5 331 → L3, R8 771 → L4 | S-02 (unlock seviyeleri) + S-04 (XP eğrisi) |
| 4 | **Geç oyunda silah paritesi kayıyor:** kalabalık DPS R15 A 247 · B 166 · C 139; tek hedef A 71 · B 51 · C 38. Executioner TTK R10: A 6,2 s · B 7,8 s · C 10,0 s. B'nin tek-hedef DPS'i en düşük ve zırhla en çok cezalanıyor (7 hasar × 22/38/28 dayanıklılık). | TTK ve DPS sütunları R10–R15 | S-02 (silah verisi) |
| 5 | **Ölüm riski vekili R8'de 1,0'ı geçiyor** (üç silahta da) ve R15'te 2–3×; havuz 147 düşman / round-temizleme 80–120 s. Zorluk artışı monoton ve tutarlı, ancak R8–R10 sonrası büyüme "kayıp verilecek" bölge — DEC-021 elle tanımlı round değişiklikleri (tablo boş) burada devreye girmeli. | risk sütunu; pool 12→147; clear s 8→83 | S-03 (round/DEC-021 tablosu) |

Ek gözlemler: Special Weapon beklenen alma R4'ten itibaren 0,35→0,75/round, 15 round'da ~7 (WP-007 verisi, makul); ilk kart R1'de alınabiliyor (DEC-070 bandı 15–45 tüm dallarda sağlanıyor, klamp gerekmiyor); R15 gelir düşüşü (898→851) seed/tier karışımı gürültüsü.

## 2. Öneri (kullanıcı kararı — "öneriyi onaylıyorum" yeterli)

**Ö-1 Altın enflasyonu (S-04 verisi, bant içi):** `CostGrowthPerTier` 1,5–1,6 → **2,0 (tüm dallar)**; `BaseCost` aynı (DEC-070 açılış bandı korunur). Etki (model): t3 kart ≈ 60–180 g, t5 ≈ 240–720 g; R10 cüzdanı ~1 855 → ~600. Alternatif Ö-1b: DEC-071'i "round başına 2 satın alma" yapmak (ürün kararı, WP-008 kuralı değişir). **Öneri: Ö-1 önce; Ö-1b kullanıcı isterse ayrı DEC.**

**Ö-2 Zırh doygunluğu (S-03 verisi):** `ChanceGrowthPerRound` 0,025/0,03 → **0,012/0,015** ve `MaxChance` Standard/Runner 0,75 → **0,55**; Guard/Executioner giriş şansları (0,45–0,7) korunur. Hedef: R10'da Standard zırhlı pay ~%50, R15 ~%65. DEC-021 elle tanımlı round tablosu (boş) ile belirli roundlarda (R5, R10) zırh sıçraması yazılabilir. **S-03'e öneri satırı; S-04 değiştirmez.**

**Ö-3 İlerleme eğrisi (DEC-008):** B `UnlockProfileLevel` 2 → **3**, C 3 → **5** (S-02 verisi, `ClassicWeaponSettings`); yani B ilk koşu sonunda (R5+), C ikinci/üçüncü koşuda. XP eğrisi (100 + 75·(L−1)) değişmez. Alternatif: XP eğrisi `LevelRequirementBase` 100 → 150 (S-04) — daha yavaş, ama başarım/unvan ritmini de yavaşlatır; **önerilmez.**

**Ö-4 Silah paritesi (S-02 verisi):** B `Damage` 7 → **8** (zırh dayanıklılığına karşı), C `Damage` 7 → 8 pellet başına **ya da** `FireRate` 1,2 → 1,4. Geç oyunda B/C'nin A'ya oranı %67/%56 → ~%80. **S-02'ye öneri; kullanıcı silah kararı sonrası.**

**Ö-5 DEC-021 elle tanımlı tablo (S-03):** R8, R11, R14 için `RoundDifficultyOverride` satırları: zırh sıçraması + Executioner payı; simülatör aynı komutla tekrar koşulur (bayraklar). Bu paket boş tabloyla ölçtü.

**Dal katalog girdileri (WP-035 devri):** çekirdek dallar silaha bağlı (`WeaponRoster.IsBranchOffered`), profil seviyesiyle açılan dallar (`UnlockProfileLevel` Explosion 2, Chain 3) merdivenle zaten açılıyor → katalog `Branch` girdisi **gerekmez**; öneri: mevcut durum kalsın.

## 3. Yorum (game-designer turu — Opus stand-in, tek tur)
Ekonomi "kazan, harca, güçlen" döngüsünü kırıyor: harcayamayan oyuncu için altın anlamsızlaşır (Ö-1). Zırh ilk 5 round'da öğretici, sonra sabit vergi — kırılma animasyonu/gold bonusu okunurluğu kaybolur (Ö-2). Üç silah tek koşuda açılırsa "yeni silah" ödülü tükenir (Ö-3). B'nin kimliği "hızlı ve zayıf" ise zırha karşı kasıtlı olabilir; zırh doygunluğu düşürülürse (Ö-2) B kendiliğinden toparlar → **Ö-2 önce, Ö-4 sonra ölç.** Kabul sırası önerisi: Ö-1 → Ö-2 → Ö-3 → tekrar simülasyon → Ö-4/Ö-5.

## 4. Karar biçimi
- "Ö-1'i onaylıyorum" → S-04 `CostGrowthPerTier` 2,0 (config asset, PrototypeOnly) + test + simülatör yeniden koşusu (ayrı küçük commit).
- "Ö-2/Ö-4/Ö-5'i onaylıyorum" → S-03/S-02 kendi paketlerinde uygular; S-04 simülatörü tekrar koşar.
- "Ö-3'ü onaylıyorum" → S-02 `UnlockProfileLevel`; S-04 unlock kataloğu roster'ı referansladığı için kendiliğinden güncellenir.

## 5. Ek — Ö-6 (WP-049 yeniden koşusu, 2026-09-17; kalibre model, Ö-1 + Ö-3 sevkte)

Ö-1 sonrası harcama payı %12 → %19–22, ama tek kart/round (DEC-071) tavanı yüzünden R15'te cüzdan ~5 200 harcanmamış (gelir 6 677). Klon senaryoları (`-ddScenario`):

| senaryo | harcama payı | cüzdan R15 | risk R15 | not |
|---|---|---|---|---|
| sevk | %19–22 | 5 186–5 389 | 8,83 | tek kart, çekirdek üçlü dolu, yan dallar boş |
| **o6a** — round başına 2 kart | **%38–44** | 3 744–4 159 | **6,09** | MS/HP/RD/RR dalları da doluyor → altın güce dönüşüyor |
| o6b — fiyat ×(1+0,15·(R−1)) | %43–55 | 3 013–3 812 | 8,83 | altın yakılıyor, güç aynı |
| o23456a — Ö-2..Ö-5 + 2 kart | %36–46 | 3 500–4 091 | 6,09 | Ö-5 örnek satırı R8 zırhı %96 (Ö-2 tavanına uymalı) |

**Ö-6a (öneri):** DEC-071 "round başına tek geliştirme kartı" → **iki kart** (ürün kararı; WP-008 kuralı + WP-022 dükkân UI ikinci seçim). **Ö-6b önerilmez.** Karar biçimi: "Ö-6a'yı onaylıyorum" → S-04 dükkân sayacı/`UpgradeOfferPolicy` + S-09 UI, ayrı küçük paket; simülatör `purchasesPerRound=2` sevk varsayılanı olur.
