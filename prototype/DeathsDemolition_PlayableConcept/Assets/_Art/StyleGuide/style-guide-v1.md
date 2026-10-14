# Deaths Demolition · Stil Kılavuzu v1 — FİNAL (WP-015, DEC-076)

**Durum:** DEC-076 (2026-09-15, kullanıcı kararı) üzerinde **final**. Bu belge karar üretmez; DEC-076..080, ADR-011-1/3/4, ADR-012-1/2, ADR-013-1, ADR-015-1/2 ve WP-015 worktree kod değerlerini tek yerde toplar. WP-011 taslağının (`wp-011/reports/art-11-style-guide-v1-draft.md`) yapısı ve kaynak sınıfı disiplini korunmuştur; taslağın §9 açık maddeleri §9'da kapanış kaynağıyla yazılıdır.
**Kaynak sınıfı etiketleri (her sayı için zorunlu):** `[AP-001 ölçümü]` AP-001 kanıt JSON'undan okunan değer · `[AP-006 konsept]` `AP-006/02-concept/AP006-concept-summary.md` değeri · `[kod]` WP-015 worktree kaynak dosyası (dosya:satır) · `[DEC-xxx]` / `[ADR-xxx]` / `[REQ-xxx]` karar/kayıt · `[AD hesabı]` art-director'ın kanıt değerlerinden türettiği dönüşüm (lineer→sRGB IEC 61966-2-1, HSV, oran, sis yüzdesi); bağımsız doğrulama ister · `[çalışma değeri]` test sabiti; kullanıcı kararı değildir · `[WP-015 A/B]` bu kılavuzla birlikte teslim edilen sanat incelemesi görüşü (yakalamalar `wp-015/evidence/captures/`) · `[brief beyanı]` S-05 promptunda verilen, kanıt dosyasında yeniden okunmayan değer.
**Renk uzayı:** script renkleri sRGB'dir (`Color32` hex, `new Color(...)`); AP-001 lineer değerleri sRGB'ye `[AD hesabı]` ile çevrilmiştir; düz lineer renkler `_BaseColor`'a `Color.gamma` ile yazılır (§7.4).

## 1 · Görsel sütunlar (DEC-012 · DEC-013/014 · DEC-068 · DEC-076 · REQ-100..105) — kapalı
| # | Sütun | Ölçülebilir karşılığı | Yapılır | Yapılmaz | Kapanış |
|---|---|---|---|---|---|
| S1 | **Stilize gerçekçilik** (DEC-012/REQ-100): doğru oran, sade yüzey | Doku sınıfı tablosu §7.3 `[ADR-015-1]`; zemin 512 px/m `[AD hesabı: 2048 / 4 m]`; botanik düz lineer renk `[AP-001 ölçümü]`; kir/leke katmanı yok | AP-001 plaster: hafif ton dalgası, tek renk gövde | Fotogerçekçi grunge, yosun sızıntısı, çatlak decal yığını | ADR-015-1 + WP-015 doku A/B (görünür kayıp yok, §7.3) |
| S2 | **Sessiz çevre, konuşan oynanış** (REQ-105) | Çevre paleti rezerv dışı (§2.2); pickup/beacon rezerv içinde; zemin rolleri s ≤ 0.46 `[AD hesabı]` | Zemin/duvar düşük doygunluk (§2.1) | Çevrede hue 35–65° & s > 0.30; herhangi yerde s > 0.55 & v > 0.80 | `WorldPalette.Validate` `[kod World/WorldPalette.cs:178–225]` |
| S3 | **Tek hava: yağmur sonrası kapalı gök, hafif sis** (DEC-068 → DEC-076) | Güneş diski yok; gölge gücü 0.5 `[kod]`; gök düz gradyan, ufuk = sis rengi `[kod Editor/ArtSkyBuilder.cs:39]` | Sıva/taş üzerinde nemli düşük parlama (stone roughness ort. 0.574) | Sert öğle güneşi, keskin gölge, gün/gece döngüsü | DEC-076: Presentation A + DEC-068 paleti final |
| S4 | **Okunabilirlik atmosferden önce** (REQ-104) | Sis başlangıcı 52 m kamera-bağıl `[kod World/WorldAtmosphere.cs:48]`; parçacık ≤ 18 cm `[kod :200]`; resmi eşikler §6.1 `[DEC-077]` | Sis yalnız 45 m savaş menzili dışında | Kamera-yakın volumetrik; polen/petal oynanış bandında büyütme | DEC-077 eşikleri `[kod Runtime/ReadabilityThresholds.cs]` |
| S5 | **Bölge kimliği landmark ile** (REQ-114) | Bölge başına 1 baskın rol rengi | Kuzey: saray sıva+çatı+ahşap kafes · Doğu: sakura · Güney: su+köprü · Batı: bambu · Merkez: taş döşeme `[WP-014/015 yakalamaları: world-{north,east,south,west,center}]` | Tüm bölgelerde aynı karışım | AP-005 yerleşimi (2354 instance) `[ADR-014-1]` |

## 2 · Palet (final, DEC-076)
### 2.1 Çevre tabanı — AP-001 v009 materyalleri (lineer → sRGB hex `[AD hesabı]`)
| Rol | AP-001 lineer RGB `[AP-001 ölçümü]` | sRGB hex | HSV (h°, s, v) | Roughness `[AP-001 ölçümü]` | Not |
|---|---|---|---|---|---|
| stone (doku ort.) | (0.0971, 0.1093, 0.1247) readability-v002 | #585D63 | 213, 0.11, 0.39 | map ort. 0.574 | Yazarlık rengi #686E75 (0.138, 0.156, 0.178); doku kasıtlı 0.70 gövde |
| wood (doku ort.) | (0.0851, 0.0583, 0.0437) readability-v002 | #52443B | 24, 0.28, 0.32 | map ort. 0.671 | Yazarlık #594A40 |
| bark (doku ort.) | (0.0927, 0.0701, 0.0543) bark-v001 | #564B42 | 27, 0.23, 0.34 | map ort. 0.866 | |
| earth (doku ort.) | (0.0750, 0.0610, 0.0410) compacted-earth-v001 | #4D4639 | 39, 0.26, 0.30 | map ort. 0.910 | Altın bandında (35–65°) s 0.26 — rezerv eşiğine 0.04 marj; geçer, izlenir (§9 #5) |
| plaster (yazarlık) | (0.6105, 0.5972, 0.5333) | #CDCBC1 | 50, 0.06, 0.80 | 0.90 (URP smoothness 0.10 `[kod Editor/ArtMaterialBuilder.cs:177]`) | Doku AD-004 gövdesi; doku ortalaması ölçülmedi |
| roof (yazarlık) | (0.0630, 0.0723, 0.0887) | #474C54 | 217, 0.15, 0.33 | 0.78 (smoothness 0.22 `[kod :181]`) | Doku AD-005 |
| moss (düz) | (0.0497, 0.0762, 0.0273) | #3F4E2E | 88, 0.41, 0.31 | 0.95 | |
| green (düz) | (0.0513, 0.1046, 0.0307) `[kod ArtMaterialBuilder.cs:373 aynı]` | #405B31 | 99, 0.46, 0.36 | 0.86 | |
| sakura_pink (düz) | (0.5647, 0.2789, 0.3712) | #C690A4 | 338, 0.27, 0.78 | 0.78 | |
| sakura_purple (düz) | (0.2307, 0.1441, 0.3185) | #846A99 | 273, 0.31, 0.60 | 0.80 | |

**Değer sırası (lineer Y `[AD hesabı]`, kural `[DEC-076 ile final]`):** plaster 0.60 > sakura_pink 0.35 > sakura_purple 0.18 > stone 0.11 > green 0.09 > bark 0.07 ≈ roof 0.07 ≈ moss 0.07 > wood 0.06 ≈ earth 0.06. Bu sıra üretimde korunur; sıva her zaman en açık opak yüzeydir ve gökten (ufuk #D0D7DE, v 0.87) koyudur. Yeni bir çevre materyali bu sırada bir rolün yerini alamaz; bir rolün ±1 basamağına yerleşir.

### 2.2 Rezerv kuralları `[kod World/WorldPalette.cs:36–50]` (`WorldPalette.Validate` ile derleme anında denetlenir)
| Rezerv | Tanım | Kime açık | Kod sabiti |
|---|---|---|---|
| Altın bandı | hue 35–65° **ve** s > 0.30 | Yalnız pickup (gold orb, Special Weapon pickup gövdeleri) | `GoldHueMin 35`, `GoldHueMax 65`, `ReservedSaturation 0.30` |
| Beacon | s > 0.55 **ve** v > 0.80 | Yalnız oynanış (beacon, uyarı, UI durum rengi) | `BeaconSaturation 0.55`, `BeaconValue 0.80` |
| Vermilion | çevrede parça bütçesi | En fazla 6 parça, `UseAccent()` ile sayılır | `VermilionBudget 6` |
AP-001 çevre paleti denetimi `[AD hesabı]`: 10 rolün 10'u geçer; en yakın aday earth (39°, s 0.26). Prototip blockout paleti (`WorldPalette` sabitleri :56–74) aynı denetimden geçer; `KoiOrange` (0.780, 0.396, 0.212) → (19°, 0.73, 0.78) beacon eşiğinin v 0.02 altında `[AD hesabı]` — pickup değil, su altı aksanı; izlenir.

<!-- ui-tokens:begin (generated by DD/UI/Export Tokens from UiTokens.cs - do not edit by hand) -->
#### 2.2 UI · arayüz token tablosu (WP-068 token v3)

| token | Default | ColorBlindSafe |
|---|---|---|
| `Transparent` | #00000000 | #00000000 |
| `ScrimOverlay` | #12100FE0 | #12100FE0 |
| `SurfaceBase` | #1A1816 | #1A1816 |
| `SurfacePanel` | #232120 | #232120 |
| `SurfaceRaised` | #34322F | #34322F |
| `SurfaceInset` | #12100F | #12100F |
| `HudInk` | #12100FFA | #12100FFA |
| `BorderHairline` | #4A4744D9 | #4A4744D9 |
| `BorderStrong` | #F3EEE373 | #F3EEE373 |
| `BorderFocus` | #F2D98C | #F2D98C |
| `BorderFocusHalo` | #12100F | #12100F |
| `TextPrimary` | #F3EEE3 | #F3EEE3 |
| `TextSecondary` | #B9B2A6 | #B9B2A6 |
| `TextMuted` | #9A948C | #9A948C |
| `TextOnAccent` | #F3EEE3 | #F3EEE3 |
| `TextAccent` | #E8806F | #E8806F |
| `AccentPrimary` | #C4362B | #C4362B |
| `AccentPrimaryPressed` | #9E2A21 | #9E2A21 |
| `AccentSakura` | #E8A0B8 | #E8A0B8 |
| `AccentWisteria` | #A99BD4 | #A99BD4 |
| `AccentMoss` | #59714A | #59714A |
| `DamageEdge` | #C4362B | #C4362B |
| `StateDanger` | #EE7063 | #EE7063 |
| `StateWarning` | #F7C766 | #F7C766 |
| `StateSuccess` | #8FC079 | #7FA6D9 |
| `StateGold` | #D9A93C | #D9A93C |
| `BarTrack` | #12100F | #12100F |
| `BarHealth` | #748F60 | #5F8CC4 |
| `BarHealthLow` | #EE7063 | #EE7063 |
| `BarXp` | #E8A0B8 | #E8A0B8 |
| `BarReload` | #F2D98C | #F2D98C |
| `BarSpecial` | #D9A93C | #D9A93C |
| `ShadowKey` | #12100F | #12100F |
| `SheenRim` | #F3EEE31A | #F3EEE31A |
| `InkStroke` | #12100FE6 | #12100FE6 |
| `GoldLeaf` | #D9A93C | #D9A93C |
| `SilverLeaf` | #F3EEE3 | #F3EEE3 |
| `HoldFill` | #F3EEE326 | #F3EEE326 |
| `LanternGlow` | #F7C766 | #F7C766 |
| `GlyphIwa` | #FFBB9E | #FFBB9E |
| `GlyphNami` | #8DDBEB | #8DDBEB |
| `GlyphArc` | #ADC6FF | #ADC6FF |
| `GlyphGel` | #E0A3FF | #E0A3FF |

UI rezerv kuralları (WP-068 AD MUST 3):
- **Altın** (`StateGold`, `BarSpecial`, `GoldLeaf` = kinpaku) yalnız para sayacı, fiyat ve Special HUD'dadır; tören **ginpaku**'dur (`SilverLeaf`, krem): madalya, NEW BEST, seviye.
- **Uyarı** (`StateWarning`) para altınından ayrıdır: Amber300 #F7C766 (ΔL* 10,8); aynı panelde uyarı ile odak altını buluşursa uyarı rengi odak halkasının yerini alır.
- **Şarjör/reload** (`BarReload`) Focus400'dür ve varak almaz (para değil, Special değil).
- **Fener parıltısı** (`LanternGlow`, Amber300) dekoratiftir, anlam taşımaz: yalnız menü plakasının fener maskesi, α ≤ 0,35 (nefes tabanı 0,19), altın bandının dışında. Durum tokenları dekorasyona bağlanmaz.
- **Sakura** (`AccentSakura`, `BarXp`) = NEW ve ilerleme; **vermilion** (`AccentPrimary`) = ekran/modal başına bir birincil CTA + başlık tiki + mühür.
- **İki yeşil**: Jade (`StateSuccess`, kazanılmış) ve Moss (`BarHealth`, can); renk körlüğü paletinde ikisi de indigo ailesine geçer.
- Durum hiçbir zaman yalnız renkle taşınmaz: kelime + şekil (renk körlüğü paleti, WCAG 1.4.1).
<!-- ui-tokens:end -->

#### 2.2 UI · yarı saydam çiftler — doğrusal ışık (WP-068 12b D1, AD 8a taşınan MUST)

Proje **Linear** renk uzayındadır: GPU UI katmanlarını doğrusal ışıkta harmanlar ve `UiContrast.Composite` aynı hesabı yapar. Önceki değerler sRGB uzayında tahmin edilmişti; bu tahmin koyu zemin üstündeki açık yarı saydam katmanları (parıltı, washi, fener) ve açık dünya üstündeki koyu perdeleri iyimser okur. Her çift `UiTranslucentTokensTests` ile yeniden ölçüldü; eşik altı çift metni taşıyarak değil α ile düzeltildi.

| çift | değer | doğrusal ölçüm (eşik) |
|---|---|---|
| menü başlık bloğu, plakanın en parlak tekseli (21:9 / Mac × 1,25) | `SurfaceBase` **0,90**, x 0'dan (0,55'ten) | alt başlık ≥ 4,9, başlık ≥ 8,9 (4,5) |
| sol üçte bir (T10, S-05 kenedi) | `SurfaceBase` 0,55 sabit; plakadaki en soluk metin `TextSecondary` | 4,60 (4,5); TextMuted 3,2 → plakada kullanılmaz |
| parıltı alanı altında `TextMuted` (SurfacePanel) | `SheenFieldAlphaMax` **0,014** (0,05'ten) | 4,52 (4,5) |
| `SheenRim` 1 px kenar | 0,10 | 2,29 panel / 2,43 taban (dekoratif; metin ≥ 8 px uzakta) |
| washi, en yoğun lif (katmanın 0,73'ü), `TextSecondary` | `WashiAlphaMax` **0,07** (0,08'den) | 4,60 (4,5); TextMuted washi üstünde kullanılmaz |
| kumiko kapılarda (`SurfaceInset`) | 0,045 | TextSecondary 5,33 · TextPrimary 9,69 |
| `LanternGlow` nefes | 0,35 × 0,55–1 | altın bandına ΔE76 ≥ 28 (10); T10 tepe nefeste ölçülür |
| modal kartının sınırı (dolgu ya da `BorderStrong` çerçeve), siyah → krem-beyaz dünya | diyalog perdesi **0,88** (0,72'den); `ScrimOverlay` 0,88 | ≥ 3,14 (3,0); 0,72'de 2,81 |
| tutma etiketi `HoldFill` üstünde | 0,15 | dinlenme 4,86 · basılı 4,73 (4,5) |

### 2.3 Oynanış renkleri (kod değerleri sRGB; WP-015 worktree'de yeniden okundu)
| Öğe | Değer `[kod]` | Dosya:satır | HSV `[AD hesabı]` | Rezerv sınıfı |
|---|---|---|---|---|
| Gold orb | (1.00, 0.78, 0.30) ≈ #FFC74D | Economy/GoldOrbTuning.cs:69 | 41°, 0.70, 1.00 | Altın bandı + beacon |
| Special Weapon beacon (varsayılan) | (1.00, 0.82, 0.35, a 0.3) | Weapons/SpecialWeaponPickup.cs:500 | 43°, 0.65, 1.00 | Altın bandı + beacon |
| Ofuda Loom beacon | (0.96, 0.93, 0.84, a 0.3) | SpecialWeaponPickup.cs:494 | 45°, 0.13, 0.96 | Rezerv dışı; sıva #CDCBC1'e Δv 0.16 / Δs 0.07 — REQ-105 riski (§9 açık #A) |
| Tide Jewel beacon | (0.55, 0.78, 0.86, a 0.3) | SpecialWeaponPickup.cs:497 | 196°, 0.36, 0.86 | Rezerv dışı; sis/ufuk hue'su (210°) ile komşu; Δv 0.01 ufka karşı — yalnız a 0.3 ve hareketle ayrılır (§9 açık #A) |
| Acil (son saniye) beacon | (1.00, 0.36, 0.20, a 0.5) | SpecialWeaponPickup.cs:482 | 12°, 0.80, 1.00 | Beacon |
| Kagura Bell bronz | (0.72, 0.55, 0.24) | SpecialWeaponPickup.cs:377 | 39°, 0.67, 0.72 | Altın bandı (pickup, izinli) |
| Ofuda Loom kâğıt / mürekkep | (0.94, 0.90, 0.80) / (0.70, 0.18, 0.14) | SpecialWeaponPickup.cs:396–397 | 43°, 0.15, 0.94 / 4°, 0.80, 0.70 | Rezerv dışı / vermilion ailesi (pickup gövdesi, bütçe dışı) |
| Tide Jewel inci | (0.68, 0.85, 0.90) | SpecialWeaponPickup.cs:413 | 194°, 0.24, 0.90 | Rezerv dışı |
| UI StateGold / BarSpecial (Gold400) | #D9A93C | UI/Runtime/Kit/UiTokens.cs:230 (:284, :291) | 42°, 0.72, 0.85 | Beacon |
| UI StateDanger / BarHealthLow (Coral400) | #EE7063 | UiTokens.cs:231 (:281, :288) | 6°, 0.58, 0.93 | Beacon |
| UI AccentPrimary / DamageEdge (Vermilion600) | #C4362B | UiTokens.cs:222 (:273, :279) | 4°, 0.78, 0.77 | Vermilion (UI; çevre bütçesi dışı) |
| UI AccentPrimaryPressed (Vermilion700) | #9E2A21 | UiTokens.cs:221 | 4°, 0.79, 0.62 | Vermilion (UI) |
| UI TextPrimary (Cream100) / HudInk (Sumi900 a 0.98, WP-068 slice 8a — AA in the project's linear-light UI blending; 0.92 before) | #F3EEE3 / #12100F | UiTokens.cs:219 / :212, :260 | 41°, 0.07, 0.95 / 20°, 0.17, 0.07 | Rezerv dışı; HudInk üstünde 12.54:1 beyaz dünya (doğrusal harmanlama) `[kod :195]` |
| Çevre vermilion (prototip blockout) | (0.678, 0.243, 0.161) | World/WorldPalette.cs:72 | 10°, 0.76, 0.68 | Bütçeli: 6 parça |

### 2.4 Yapılır / yapılmaz (final)
- Yapılır: sıva paneli tek gövde rengi (#CDCBC1) + ahşap çerçeve koyu (#52443B) — Presentation A dikey ritmi.
- Yapılmaz: sıva üzerine sıcak sarı boya (h 35–65°, s > 0.30) — altın orb ile karışır; `Validate` reddeder.
- Yapılır: sakura pembe/mor ikilisi; pembe açık kütle (Y 0.35), mor koyu kütle (Y 0.18).
- Yapılmaz: sakura doygunluğu s > 0.45 — Coral400/vermilion uyarılarıyla yarışır.
- Yapılır: zemin karosu (stone) nemli düşük parlama (smoothness map ort. 0.43).
- Yapılmaz: ayna gibi ıslak zemin (smoothness > 0.60) — beacon yansımaları sahte pickup okutur.
- Yapılır: su yüzeyi `PondWater` (0.180, 0.294, 0.310, a 0.78) smoothness 0.88 `[kod WorldPalette.cs:73, 276]` — tek parlak yüzey, kamera açısında spekülerler noktasal kalır (`world-south` yakalaması).
- Yapılmaz: çevrede emisyon; `BloomThreshold 1.25` `[kod WorldAtmosphere.cs:123]` üstüne yalnız oynanış nesneleri çıkar.

## 3 · Oyuncu işareti kuralı (AP-006 Faz 1, kullanıcı seçimi: **yön C — haori ronin, epik kostüm**)
**Kural `[AP-006 konsept, "Player-mark rule"]`:** İşaret her yönde ve her skinde aynı iki yerdedir: (1) **bel bandı** (obi/kuşak) — sürekli, gövdeyi tam saran, **≥ 12 cm** genişlik; (2) **bir baş seviyesi aksanı** — C için **kafa bandı**. C ayrıca **pelerin (mantle) kenarını** aksan rengiyle taşır (üç yükseklik: kafa bandı, pelerin kenarı, obi). İşaret rengi = skin aksanı; **alt bacaklara asla konmaz**. Ten sıcak tan kalır; eller her yönde çıplak (ten okunur).
**Aksan sınırları `[AP-006 konsept]`:** aksan v ≥ 0.55 **ve** s ≤ 0.55 (beacon rezervi s > 0.55 & v > 0.80 içine asla girmez); **kırmızı ailesi aksanlar v ≥ 0.60** (zombi SashRed #702022 v 0.44'ün ≥ 0.20 üstü). **Değer ayrımı kuralı:** oyuncunun birincil ve aksan rengi zombi kumaş kümesinden (§4) **Δv ≥ 0.30**.
**C silüet sınırları `[AP-006 konsept, Direction C]`:** pelerin yalnız SOL omuzda, dirsek boyu, kalınlık ≥ 8 cm; pelerin kenar bandı **≥ 10 cm** (88 px'te kaybolmaması için); topuz ≥ 9 cm; pelerin = 2 kemikli sert kabuk (~2 k tris), cloth sim yok; LOD0 tahmini 19–21 k tris.

| Skin `[AP-006 konsept]` | Birincil (en büyük üst görünüm alanı) | İkincil | Aksan / işaret | HSV `[AD hesabı]` (birincil · aksan) | Zombi kümesine Δv `[AD hesabı]` | Rezerv / kural denetimi `[AD hesabı]` |
|---|---|---|---|---|---|---|
| 1 Ivory/Crimson | fildişi #E6E0CF (v 0.90) — C haori | sıcak gri-bej #C2BAA9 (v 0.76) — kimono + pelerin | kızıl #A32126 (v 0.64, s 0.80) | (44°, 0.10, 0.90) · (358°, 0.80, 0.64) | koyu küme (0.12–0.29) 0.61 ✓; DirtyCream 0.66'ya 0.24 (sınırda, aynı sıcak hue) → ayrımı kızıl bant taşır | Aksan beacon rezervi dışı (v 0.64 < 0.80) ✓; SashRed'e Δv 0.20 (kuralın tam sınırı) ✓; **literal "s ≤ 0.55" sınırını aşar (s 0.80)** → §9 açık #B |
| 2 Steel/Indigo (**2b — kullanıcı kararı 2026-09-16 07:50**; WP-017 doku takası, AP-006 v003) | soluk çelik #BCC3E6 (v 0.90, s 0.18) — C haori `[manifest v003; S-05 hesabı]` | çelik-gri #4B4F61 (v 0.38) — kimono + pelerin alt katman | çivit #515D99 (v 0.60, s 0.47) | (230°, 0.18, 0.90) · (230°, 0.47, 0.60) | koyu küme (0.12–0.29) 0.61 ✓ (DarkIndigo aynı hue ailesi, 0.63); DirtyCream 0.66'ya 0.24 (sınırda, farklı hue: mavi ↔ sıcak) | Aksan: s 0.47 ≤ 0.55 ✓, v 0.60 ≥ 0.55 ✓, beacon rezervi dışı ✓; motor içi (WP-017, iki koşu): oyuncu↔zombi luma 0.102 / 0.074 (2a 0.031; poz bağımlı bant), gövde/çevre üstten 0.011–0.023 (taş plaka ile aynı luma; hue ayırır — AP-006 devri) |
| _(2a Indigo/Black — reddedildi, referans)_ | çivit #4E5A94 (v 0.58; prototip (0.20, 0.22, 0.37) v 0.37 yalnız 0.10 üstte olurdu) | kömür #1C1C26 (v 0.15; yalnız alt gövde/alt katman, üst görünüm alanının ≤ %35'i) | soluk çelik mavisi #8FA0DB (v 0.86, s 0.35) | (230°, 0.47, 0.58) · (227°, 0.35, 0.86) | DarkIndigo 0.27'ye 0.31 ✓ (aynı hue ailesi); FadedBrown 0.29'a 0.29 (farklı aile, 200° uzak) — Faz F render'ında ölçülür; kömür ikincil zombi koyu kümesinin **içinde** (0.15) → alan sınırı ≤ %35 zorunlu | Aksan: s 0.35 ≤ 0.55 ✓, v 0.86 ≥ 0.55 ✓, beacon rezervi dışı ✓ |
| 3 Moss/Cream | krem #E0D9BD (v 0.88) gövde | yosun #545F47 (v 0.37) — kimono + pelerin | zeytin-altın #8C7A4A (v 0.55, s 0.47, h 44°) | (48°, 0.16, 0.88) · (44°, 0.47, 0.55) | koyu küme 0.59 ✓; DirtyCream'e 0.22 (sınırda) → işaret + yosun alt gövde ayırır; yosun (88°) zombi Flesh (87°) ile aynı hue, 0.16 koyu, tene asla konmaz | Aksan v 0.55 (kural sınırı) ✓, s 0.47 ≤ 0.55 ✓; **altın bandı içinde (44°, s 0.47 > 0.30)** — AP-006 koşulu: sahne içi testte orb ile yarışırsa s → 0.30 (§9 açık #C) |

Skinler yalnız kumaş rengini değiştirir; silüet değişmez (AP-006: 9 skin render'ında aynı poz/çizgi). Kompozit ölçek: 1.8 m figür oynanış kamerasında ~85–90 px (kamera matematiği) / 120 px (brief plan değeri) `[AP-006 konsept]` — Faz F gerçek modelde ölçülür.

## 4 · Düşman paleti rezervi `[kod Enemies/ZombiePalette.cs:100–114]`
| Sınıf | Ad | sRGB `[kod]` | HSV `[AD hesabı]` | Roughness `[kod]` | Not |
|---|---|---|---|---|---|
| Kumaş | DarkIndigo | #2A2F45 | 229°, 0.39, 0.27 | 0.92 | koyu küme |
| Kumaş | Black | #1A1A1E | 240°, 0.13, 0.12 | 0.95 | koyu küme |
| Kumaş | DirtyCream | #A99E86 | 41°, 0.21, 0.66 | 0.88 | altın bandında, s < 0.30 → izinli; oyuncu fildişi/krem skinlerine en yakın değer |
| Kumaş | FadedBrown | #4A3B2E | 28°, 0.38, 0.29 | 0.90 | koyu küme |
| Kumaş | WashedSlate | #3A3E42 | 210°, 0.12, 0.26 | 0.90 | koyu küme |
| Aksan | Flesh | #7E8674 | 87°, 0.13, 0.53 | 0.75 | gri-yeşil ten |
| Aksan | MetalOxidised | #4C5149 | 98°, 0.10, 0.32 | 0.42, metallic 0.55 | gri-yeşil |
| Aksan | MetalDark | #303237 | 223°, 0.13, 0.22 | 0.35, metallic 0.65 | |
| Aksan | SashRed | #702022 | 359°, 0.71, 0.44 | 0.85 | tek doygun zombi rengi; oyuncu kırmızı aksanı v ≥ 0.60 kuralının referansı |
| Aksan | SashDark | #22222A | 240°, 0.19, 0.16 | 0.90 | |
| Aksan | UnderCloth | #5C5346 | 35°, 0.24, 0.36 | 0.95 | altın bandı alt sınırında, s < 0.30 → izinli |

**Zombiye ayrılmış bant (mevcut değerlerin zarfı `[AD hesabı]`):**
- Gri-yeşil / soluk ten: h 87–98°, s 0.10–0.13, v 0.32–0.53 (Flesh, MetalOxidised). Oyuncu teni (sıcak tan) ve oyuncu birincil rengi bu zarfa girmez; yosun ikincil (88°, 0.25, 0.37) aynı hue'dadır ama yalnız alt gövde/pelerin.
- Koyu kumaş kümesi: v 0.12–0.29; soğuk alt küme h 210–240°, s 0.12–0.39; sıcak alt küme h 28–35°, s 0.24–0.38. Tek açık kumaş DirtyCream (41°, 0.21, 0.66).
- Doygun aksan: yalnız SashRed (359°, 0.71, 0.44).
**Kurallar:** (1) Oyuncunun birincil ve aksan rengi koyu kumaş kümesinden **Δv ≥ 0.30** (§3 tablosu ölçer); DirtyCream'e karşı ayrımı aksan bandı taşır. (2) Zombide **emisyon ve aura yok** `[kod dosya başlığı :16–18]`; efekt yoğunluğu savaş tepkisine ayrılmıştır. (3) Zombi materyalleri sabit paylaşılan set; tint/MaterialPropertyBlock yok (SRP batching) `[kod :9–14]`. (4) Yeni zombi varyantı bu zarfın dışına çıkacaksa (ör. elit) s ≤ 0.45 ve v ≤ 0.60 kalır; beacon rezervine ve oyuncu aksan bandına (v ≥ 0.55 & s ≤ 0.55) girmez `[AD kuralı; §2.2/§3'ten türetilmiş]`.

## 5 · Işık ve sis dili (WorldAtmosphere.cs; DEC-068 → DEC-076)
| Parametre | Presentation A `[AP-001 ölçümü]` | Run.unity / World_AP005 `[kod World/WorldAtmosphere.cs]` | Durum |
|---|---|---|---|
| Anahtar (güneş) | SUN 2.0 W/m², disk 18°, beyaz; XYZ (35, −25, −28)° | Euler **(42, 25, 0)**, yoğunluk **1.15**, renk (1.0, 0.971, 0.925) ≈ #FFF8EC `[:82–87]` | Final `[ADR-011-4 + DEC-076]` (§9 #1) |
| Dolgu / ambient | AREA OvercastFill 20 kW 55 m; FrontBounce 7 kW 35 m | Trilight sky (0.616, 0.655, 0.702) #9DA7B3 · equator (0.447, 0.470, 0.420) #72786B · ground (0.216, 0.231, 0.192) #373B31 · intensity 1 `[:89–91, :333–337]`; yansıma yoğunluğu 0.55 `[:340]` | Final; taslağın "equator'ı sky'a %15 yaklaştır" adayı **uygulanmadı** (karar yok, mevcut korunur) |
| Gölge | 18° disk → yumuşak penumbra | Soft, **strength 0.5**, bias 0.06, normalBias 0.5, near 0.2 `[:85, :314–318]`; mesafe 45 m (REQ-104 savaş menzili) `[:22]` | Final 0.5 (§9 #3) |
| Sis | Finite mist 0.003/m, Y 20–30 m'den smoothstep, renk lineer (0.63, 0.68, 0.73) | **Linear 52 → 210 m** kamera-bağıl, renk sRGB **(0.815, 0.843, 0.870) ≈ #D0D7DE** (210°, 0.06, 0.87) `[:48, :59, :66]` | **KABUL `[WP-015 A/B]`** = DEC-076 "hafif sisli" çevirisi (§9 #2). WP-013 değerleri 52→138 m, (0.694, 0.714, 0.706) #B1B6B4 yalnız A/B referansı `[:69, :72]` |
| Gök | World background (0.63, 0.68, 0.73) × 0.65 `[brief beyanı]` | Paylaşılan `DD_Sky_Overcast_v1` gradyanı: **ufuk = sis rengi** `[kod Editor/ArtSkyBuilder.cs:39]`, **zenit (0.60, 0.64, 0.68)** #99A3AD `[:42]`; güneş diski/ufuk parlaması yok; 4×256, streaming kapalı `[ADR-015-1 Sky sınıfı]` | Final |
| Post-fx (WP-013'ten beri oynanış kamerasında **açık**) | AgX view transform | Tonemapping Neutral; contrast 8; saturation −2; WB temp −3 / tint 1; vignette 0.20 / 0.45; bloom threshold **1.25**, intensity 0.5, scatter 0.62; colour filter (1, 0.99, 0.96) `[:97–128]` | Final; taslağın "post-fx kapalı" varsayımı WP-013 ile değişti (renderer PostProcessData atandı) |
| Rüzgâr `[ADR-013-1]` | — | yön 24°, taban 0.35, gust 0.4 / 9 s, sway 0.28 m @ 0.45 Hz, flutter 0.035 m @ 2.6 Hz `[:140–164]` | Final şema; değerler PROTOTYPE-ONLY etiketiyle kodda |

**REQ-104 kuralı:** kamera (0, +15, −13) → oyuncu 19.8 m, en uzak angaje düşman ~25 m → **45 m içinde hiçbir piksel sislenmez** `[kod :19–28]`; mod Linear'dır, Exponential yasaktır (her mesafede yoğunluğu vardır).
**Sis rampası `[AD hesabı, (d−52)/(210−52)]`:** 52 m %0 · 90 m **%24** (Presentation A 0.003/m ile aynı: e^(−0.27) → %24) · 100 m %30 · 120 m %43 · 138 m **%54** · 210 m %100. WP-013 rampası: 90 m %44, 138 m %100. Not: `WorldAtmosphere.cs:54` yorumundaki "41 % at 138 m" değeri 86/210'dur; doğru lineer değer 86/158 = %54 — yalnız yorum düzeltmesi, sabit doğru.
**Derinlik katmanları:** 0–20 m tam kontrast (oyuncu) · 20–45 m tam kontrast + gölge (savaş) · 45–52 m gölgesiz, sissiz geçiş · 52–210 m sis rampası; 90 m'de landmark (saray kafesi, çatı kulesi, torii, sakura tacı) okunur `[WP-015 A/B: world-overview]`. Stand-in zemin (±80 m `[DEC-078]`) kenarı 150–200 m'de %60–95 sislenir; kalan kenar çizgisi AP-015 tam harita zemin/yol plakalarıyla kapanır, sis parametresiyle değil.

### 5.1 Referans kurulu (kullanıcı direktifi 2026-09-15) `[harici kaynak; referans, teslim değil]`
| Dosya (harici kaynak, `private-ledger:DD_StyleRef_DEC076`) | Ne gösterir | Kullanım | Çekince |
|---|---|---|---|
| `DD_StyleRef_DEC076_overview_2k.png` (16:9, 2688×1520, sha `c67264aa…`) | ~90 m genel bakış: yağmur sonrası kapalı düz gök, uzak üçte birde hafif pus (#D0D7DE), kuzey saray sıva+ahşap+çatı, doğu sakura, güney koi göleti+köprü, batı bambu+koridor | §5 ışık/sis dilinin ve S5 bölge kimliğinin görsel hedefi; §9 #2 sis kararının resim karşılığı | Koi balığı turuncusu (h ≈ 20°) çevre rezerv kuralı dışında — koi_fish (AP-005/AP-015) üretilirken altın bandına göre denetlenir; referanstır |
| `DD_StyleRef_DEC076_gameplay_2k.png` (aynı kaynak/ayar, sha `7f7d491e…`) | Oynanış kadrajı (20 m, −49°): ıslak taş döşeme, sıva+ahşap kafes duvar, toprak saksı+yosun+çalı, fener, yaprak; savaş menzilinde sis yok | §2.1 palet ve §7.1 nemli taş parlaması için ton/doku hedefi | Döşeme parlaması kılavuz kuralından daha parlak okunur (stone smoothness map ort. 0.43, ayna gibi asla — §2.5); sayı kılavuzundur |
Kaynak kaydı: `private-ledger:DD_StyleRef_DEC076` — 2 referans karesi + kullanıcı izniyle (2026-09-16) 2 adet 4K büyütme (`DD_StyleRef_DEC076_{overview,gameplay}_4k.png`, 4096×2323). Gök/bulut plakası ve stand-in zemin dokusu gerekmedi — gök düz gradyan, zemin üretim hero_earth materyali.

### 5.2 Görünüm reçetesi — Look B "Islak Arduvaz" (kullanıcı kararı 2026-09-17, DEC-084 [S-02 WP-042 kaydı]; WP-029, ADR-029-1) `[kod Lighting/LUT/DD_Look_v1.recipe.json]`
| Katman | Değer | Not |
|---|---|---|
| Kaynak | Adobe Lightroom API, 8 referans karesi (`LookReferenceTests`, oynanış grade'i **açık** = `post`) | 3 aday (A Sessiz Pus / B Islak Arduvaz / C Sakura Baharı) kontak sayfasıyla sunuldu; kullanıcı **B** |
| Preset | `Creative - Cool Shadows & Warm Highlights` (split-tone: soğuk gölge, ılık ışık) | yalnız global ton/renk → LUT'a taşınır |
| Ayarlar (preset sonrası) | gamma 0,94 · contrast +16 · shadows +6 · darks −4 · lights +4 · **highlights −6** · tempA 0 / tempB −8 (luminance 50) · vibrance +6 · **saturation 0** · **sarı kanal +12** | Sunulan B: highlights −10, saturation −8 — DEC-077/REQ-105 ince ayarı (seçim korunarak): altın orb doygunluğu 0,878 → 0,672 ve çevreden ayrışması 0,513 → 0,324 (< %80) düşmüştü; ayarla 0,832 / 0,458 (kapı ≥ 0,60 / ≥ 0,41 ✓). S-eğrisi orta-koyu ayrımı; taş = ıslak arduvaz; değer kontrastı taşır |
| LUT | `Lighting/LUT/DD_Look_v1.png` 1024×32 (nötr şerit `DD_Neutral_32.png` aynı reçeteyle işlendi; sRGB kapalı, RGBA32, mip yok) → `DD_Overcast_v1` Color Lookup, contribution 1 | reçete `post` kareye göre → WP-013 grade'i (Neutral tonemap, contrast +8, sat −2, WB −3/+1, vignette 0,20, bloom 1,25) aynen altında kalır, çift uygulama yok |
| Doğrulama | Lightroom karesi ↔ Unity LUT'lu kare CIE76 ΔE: nötr şerit taşıma **0,68** ort (p95 1,31; kapı ≤ 6, `LookLightingTests`); reçetenin kendi hareketi 15,5 ΔE | siyah 0 kalır, beyaz ~228 |
| DEC-077 | LUT sonrası ölçüm (v2 dünya, ön kontrol): oyuncu↔zombi luma 0,138 / 0,101 / 0,119 (skin 1/2b/3; LUT'suz 0,085 / 0,070 / 0,084 — contrast +16 yardımcı), kapsül kontrastı 0,095 (0,060), altın orb s 0,832 / ayrışma 0,458; zırh bandı ölçümü v3'te tekrar | eşik altına düşerse yalnız parametre ince ayarı (seçim korunur) — B4 böyle doğdu |
| Yeniden uygulama | dünya v3/v4, karakter, VFX paketlerinden sonra referans set + reçete (`tools/finalize-look.py`) tekrar; reçete değişikliği = kullanıcı kararı | AP-015P plakaları (WP-038) sonrası kontrol |

## 6 · Okunabilirlik kuralları (REQ-104 / REQ-105)
### 6.1 Resmi eşikler `[DEC-077]` `[kod Runtime/ReadabilityThresholds.cs:20–29]`
| # | Ölçüt | Eşik | Yöntem | Test |
|---|---|---|---|---|
| R1 | Rota örnek noktası kapsül örtülmesi | Beş eksenel ışının katı çevrece kesilen oranı > **%50** olan örnek = başarısız; başarısız örnek sayısı **= 0** (`OcclusionRatioMax 0.5`) | WP-011 kapsül yöntemi / WP-014 dünya testi: her 2 m rota örneğine 1.8 m kapsül, oynanış rig'inden (0, +15, −13) 5 eksenel ışın; fade occluder'lar ve ince kapsüller (yarıçap ≤ 0.15 m, `ThinCapsuleRadius`) ayrı raporlanır, katı sayılmaz | `WorldAp005Tests.Okunabilirlik_*` |
| R2 | Oyuncu/çevre kontrastı | Michelson kontrastı ≥ **0.06** (`CapsuleContrastMin`) | WP-013 yöntemi: düşman-vekil kapsül (0.7 × 1.8 m, gri Lit (0.32, 0.30, 0.28)) çevresine karşı, oynanış pozunda, sis + parçacık + post-fx **açık** | `AtmosphereVfxTests.Pilot_Okunabilirlik_*` `[kod :202–263]` |
### 6.2 Çalışma değerleri `[çalışma değeri; kullanıcı kararı değil]` `[kod AtmosphereVfxTests.cs:254–262]`
- Kapsül kontrastı, atmosfer açıkken kapalıya göre ≥ %90.
- Altın bant (REQ-105): doygunluk mutlak ≥ **0.60**; çevre halkasından ayrışma (altın s − halka s) ≥ **0.30**; ayrışma grade'siz karenin ≥ **%80**'i (Neutral tonemapping ölçümü 0.878 → 0.809, ayrışma 0.516 → 0.430 olduğu için %90 değil %80).
### 6.3 Albedo kontrast kuralları (WCAG biçimi (Y1+0.05)/(Y2+0.05), `[AD hesabı]`, tasarım kuralı; ekranda §6.1 ölçer)
| # | Kural | Hesaplanan durum |
|---|---|---|
| K1 | Zeminde duran pickup/beacon ile zemin rolü ≥ 3:1 | Gold orb: stone 4.3 · earth 6.0 · moss 5.8 · green 4.9 → geçer |
| K2 | Plaster arka planında parlaklık ayrımı yok (gold/plaster 1.05:1) → doygunluk farkı ≥ 0.50 zorunlu | Gold s 0.70 − plaster 0.06 = 0.64 → geçer; Ofuda beacon s 0.13 → kalır (§9 açık #A) |
| K3 | Düşman gövdesinin en az bir ana parçası yerel zeminle ≥ 1.75:1 | DarkIndigo: stone 1.98 · green 1.73 · moss 1.47 · earth 1.41; Flesh: earth 2.47 · moss 2.37 · stone 1.76 → birleşik her zeminde ≥ 1.73, sınırda geçer; ekran ölçümü R2 ile 0.06 üstü doğrulanır |
| K4 | Uyarı kırmızıları (Coral400 / Vermilion600) ile çevre vermilion aynı ekranda ≤ 1 parça (bütçe 6) | AP-005 dünyasında çevre vermilion kullanımı `Validate` ile sayılır |
### 6.4 Silüet ve rota kuralları
- Düşman dik kapsül ~1.8 m; bitki kütleleri yatay/yuvarlak → biçim zıtlığı taşıyıcı, renk ikincil (K3 sınırda).
- Occluder aileleri (plaster_wall, palace_roof_bay, roof_segment, gate_transition, open_corridor, sakura_foliage) → CameraOccluder; fade hedef alfa **0.22**, **0.15 s** `[kod Camera/CameraOcclusionFader.cs:42–43]`. Kural: 0.22 alfada düşman kumaşı (#2A2F45) sakura_pink perdesinden ayırt edilir — fade bölgesinde düşman/perde parlaklık oranı ≥ 1.5:1 `[AD kuralı]`.
- Rota geometrisi `[DEC-079]`: güney köprü **1.2 m tek sıra** ve kuzey **14 m koridor** değişmez; playtest sonrası yeniden değerlendirilir. Kamera düzlemi bambu kulmları placement override ile taşındı (i1532, i1535, i1605, i1549; ≤ 1 m) `[README §Placement overrides]`; override sonrası ince/fade occluder raporu boş.
- Mimari dikey ritim (timber_post, lattice): dikme aralığı ≥ 2 kapsül genişliği (1.4 m) `[AD kuralı]`; ahşap dikme (Y 0.06) / düşman kumaşı (Y 0.03) 1.4:1 — ayrımı genişlik (0.36 m) ve sabitlik yapar.
- Parçacık: petal ≤ **18 cm** `[kod WorldAtmosphere.cs:200]`; bütçe petal 110 global + 130 doğu bahçesi, polen 240 `[:197–199]`; `maxParticleSize 0.02` ekran tavanı `[:611]`; 20 m kamerada 18 cm ≈ 5 px @1080p.
- HUD: TextPrimary/HudInk beyaz dünya üzerinde 13.50:1 `[kod UiTokens.cs:195]`; sıva (#CDCBC1) en açık opak yüzey olduğundan HUD kontrastı korunur.
- Foliage LOD silüet alanı: LOD1 ≥ LOD0'ın %85'i, LOD2 ≥ %70'i `[AD kuralı; PilotPerfProbe ile ölçülür]`.

## 7 · Materyal ve doku kuralları (URP/Lit; Import Contract v1 `[ADR-011-1]`; sınıf tablosu `[ADR-015-1]`)
### 7.1 Rol başına roughness aralığı (AP-001 değerleri temel; kılavuz aralığı)
stone 0.50–0.65 (map ort. 0.574) · wood 0.60–0.75 (0.671) · bark 0.80–0.90 (0.866) · earth 0.85–0.95 (0.910) · plaster 0.85–0.95 (0.90) · roof 0.70–0.85 (0.78) · moss 0.90–1.00 (0.95) · green 0.80–0.90 (0.86) · sakura 0.75–0.85 (0.78/0.80) · zombi kumaş 0.85–0.95, ten 0.75, metal 0.35–0.42 (metallic 0.55–0.65) `[kod ZombiePalette.cs]`. Metallic 0 her çevre rolünde (AP-001 packed B = 0); normal ölçeği 1.0. Su tek istisna: smoothness 0.88 `[kod]`.
### 7.2 Texel yoğunluğu
Zemin/döşeme: **512 px/m ±%25** (2048 px / 4 m karo; `PilotGround.TileMetres` 4 m) `[AD hesabı; ADR-015-1 Ground sınıfından türetilmiş; tolerans kılavuz çalışma değeri]`. Yapı/bitki: 256 px/m ±%25 (1024 / 4 m) `[AD hesabı]`. Prop: ≤ 512 px haritada nesne boyutuna göre; üretim hattı yüz başına ölçer (AP-001 `face_texel_density_px_m` "numeric density only" `[AP-001 ölçümü]`).
### 7.3 Doku sınıfı tablosu `[ADR-015-1]` `[kod Editor/ArtImportContract.cs:106–191]` (her `T_<rol>_<Harita>.png` rolünün AP-005'teki en zorlu kullanımıyla sınıflanır; tablo dışı rol Prop boyutunda içe alınır ve kapı raporlar)
| Sınıf | Maks | Mip streaming | Roller | Gerekçe `[README §Texture policy]` |
|---|---|---|---|---|
| Ground / paving | **2048** | açık | `earth`, `gravel`, `stone` | Sabit kameranın 4 m karo üzerinde ≤ 20 m'den gördüğü tek yüzeyler |
| Structure | **1024** | açık | `wood`, `plaster`, `plaster_hf003`, `roof`, `earth_01..03` | Duvar, ahşap, çatı, modül yüzeyleri 20–45 m |
| Foliage / atlas | **1024** | açık | `bark`, `bark_01..03`, `moss_01..03`, `green_foliage_01..03`, `sakura_foliage_01..03`, `bamboo_culm` | Kart ve gövdeler; cutout alfa mip kapsamını korur (WP-012) |
| Prop | **512** | açık | — (AP-015 prop setlerine ayrılmış) | Fener, bank, sandık bugün structure/foliage rollerini paylaşır |
| Breakable | **512** | açık | — (ayrılmış) | `small_breakable` `bamboo_culm`/`roof`/`wood` paylaşır |
| Sky | **256** | kapalı | `sky` (4×256 gradyan, `ArtSkyBuilder`) | Skybox'ın streaming sisteminin puanlayacağı renderer'ı yok |
**Formatlar `[kod ArtImportContract.cs:250–252]`:** BC7 (BaseColor sRGB; MetallicSmoothness lineer), BC5 (Normal lineer); mipmap, trilinear, aniso 4; AP `Packed` haritaları depo içinde kalır, bağlanmaz, gemiye binmez. **Streaming `[kod :147]`:** her kalite seviyesinde açık, bütçe **160 MB** (`QualitySettings streamingMipmapsMemoryBudget`). Kamera 19.9 m / −49° 1080p'de ~55–70 px/m çözer → 4 m zemin karosu 2048 haritanın mip ≥ 2'sini örnekler; 1024 duvar/bitki ve 512 prop sabit kameranın gösterebileceği hiçbir şeyi kaybetmez — **WP-015 doku A/B `[WP-015 A/B]`: 5 bölgede (center/north/east/south/west, WP-014 tümü-2048 vs WP-015 sınıf boyutları, aynı WP-013 sisi) görünür kayıp yok; streaming bulanıklığı, dikiş, magenta yok.** Plaster 1024 hiçbir oynanış karesinde 45 m içinde görünmüyor (kuzey kare ahşap kafesi gösterir) — texel matematiğiyle kabul, ilk saray-duvarı karesinde göz teyidi (§9 açık #D).
**Ölçüm `[README §Texture policy]`:** gemiye binen doku seti 63 harita 337.2 MB (WP-014, tümü 2048) → **110.7 MB** tahmini (WP-015 sınıf boyutları; tümü-2048 olsaydı 330.7 MB); AP-005 seti sınıf boyutlarında tam yerleşik **116 MB** `[kod ArtImportContract.cs:145 yorumu]` → 160 MB bütçe koruma, sıkıştırma değil.
### 7.4 Renk uzayı (taslak §7.3 kapanışı) `[kod Editor/ArtMaterialBuilder.cs:34–41, 398–402]`
Düz lineer AP-001 renkleri `_BaseColor`'a `EncodeForColorProperty` = `linear.gamma` ile sRGB kodlanarak yazılır; Unity Color-tipli özelliği tek dönüşümle lineere çevirir, lineer sayılar `FlatColour`'da değişmez. Test lineer albedo isterse `GetColor("_BaseColor").linear` karşılaştırır. Taslağın "iki durak koyu sakura" riski **oluşmaz**; sözleşme düzeltmesi gerekmedi (§9 #7).

## 8 · LOD ve görsel bütçe notları
| Konu | Değer | Kaynak |
|---|---|---|
| LOD eşikleri | Yazarlık **16 / 26 / 69 m** (LOD0→1 / LOD1→2 / cull) = amaçlanan **32 / 52 / 138 m** ÷ `ShippedLodBias 2` (Ultra) | `[ADR-012-1]` `[kod Editor/ArtFamilyPrefabBuilder.cs:57–75]` |
| Geçiş | `LODFadeMode.CrossFade` `[kod :162; ArtLibraryPrefabBuilder.cs:291]`; kural: 20–45 m savaş bandında görünür pop yok (LOD0→1 32 m'de kamera-bağıl; oynanış kamerası nesneleri 20–45 m'de görür → geçiş bandın içinde; cross-fade zorunlu) | `[ADR-012-1]` |
| Gölge | LOD0/1 gölge atar; **LOD2 atmaz** (45 m gölge mesafesinin ötesinde) | `[kod :313]` |
| LOD cull ↔ sis sonu | Cull 138 m WP-013 sis sonuna bağlanmıştı; WP-015'te sis sonu 210 m → 138 m'de nesne %54 sisli, görünmez değil. Oynanış kamerası 138 m'yi görmez (harita 90 m; rig'den en uzak nokta ≈ 100 m) → oynanışta pop yok; genel bakış/dev pozlarında olası | `[AD hesabı]`; S-05 notu (§9 açık #E) |
| Doku bütçesi | Sanat doku seti yerleşik ≤ **160 MB** (`ArtTextureMemoryBudgetMb`; probe sayaç deltası, prosedürel Run'a karşı) | `[ADR-011-3 → ADR-015-1]` `[kod ArtImportContract.cs:150]` |
| Yayın derlemesi | ≤ **400 MB** app bundle (`ReleaseBuildBudgetMb`); etkin sahne listesi Boot, MainMenu, Selection, Run, Results, World_AP005; dev-only sahneler yalnız development build | `[ADR-015-2]` `[kod :153; README §Build list policy]` |
| Ölçüm platformu | Yalnız **macOS (M1 Pro)** ölçülür; Windows hedefi süresiz ertelendi | `[DEC-080]` |
| Zemin | Unity stand-in zemin (±80 m) kalır; tam harita zemin/yol plakaları AP-015 üretimine | `[DEC-078]` |
| Üçgen tavanı | Oyuncu LOD0 tavanı ~25 k tris (AP-006 B "at the ceiling"); C tahmini 19–21 k; helper kemik ≤ 70 | `[AP-006 konsept]` |
| Performans | Perf ham p99 makine gürültüsü → M3 kapanış tam regresyonunda (S-10) ölçülür; bu kılavuz kare süresi eşiği koymaz | `[WP-013/014 notu]` |

## 9 · Kapanan maddeler (taslak §9) ve açık kalanlar
| Taslak # | Madde | Kapanış |
|---|---|---|
| 1 | Güneş yönü: Run.unity (42, 25, 0) mı, AP-001 türevi (48, 59, 0) mı | **Kapandı:** (42, 25, 0), 1.15 korunur `[kod WorldAtmosphere.cs:82–84]`; ADR-011-4 kapanışı (WP-012) + DEC-076 mevcut ışık kurulumunu Presentation A referansıyla final yapar. Karşılaştırma yakalaması üretilmedi, gerek kalmadı |
| 2 | Sis rengi: (0.694, 0.714, 0.706) mı, AP-001 pusu adayı mı | **Kapandı `[DEC-076]`:** Presentation A pusu sRGB (0.815, 0.843, 0.870) #D0D7DE, 52→210 m `[kod :59, :66]`; sanat incelemesi **KABUL** `[WP-015 A/B]`: 90 m landmark okunurluğu WP-013 rampasına göre belirgin iyileşti (saray kafes ritmi ve çatı kulesi seçilir, sakura tacı pembe kalır), ufuk = sis rengi olduğundan gök/sis dikişi yok |
| 3 | Gölge gücü 0.5 mi, 0.45 mi | **Kapandı:** 0.5 korunur `[kod :85]`; DEC-076 mevcut değer üzerinde final; 0.45 adayı üretilmedi |
| 4 | Ofuda Loom beacon ↔ sıva çakışması | **Açık (#A)** — aşağıda |
| 5 | Earth altın bandı marjı; asset-gate HSV denetimi | **Marj kapandı (ölçüm):** earth (39°, 0.26) rezerv dışı, 0.04 marj `[AD hesabı]`; `WorldPalette.Validate` prototip paletini denetler `[kod]`. **Asset-gate HSV denetimi (üretim materyalleri için) yok** → açık (#F), S-05/test-engineer kapsamı, karar gerektirmez |
| 6 | Texel yoğunluğu 512 px/m ±%25 | **Kapandı `[ADR-015-1]`:** Ground 2048 / 4 m = 512 px/m; ±%25 kılavuz çalışma değeri (§7.2) |
| 7 | §7.3 renk uzayı → sözleşme düzeltmesi | **Kapandı `[kod ArtMaterialBuilder.cs:34–41, 398–402]`:** `linear.gamma` kodlaması; düzeltme gerekmedi (§7.4) |
| — | Palet/hava (WP-011 §23 #1) | **Kapandı `[DEC-076]`** |
| — | Okunabilirlik eşiği (WP-011 §23 #4) | **Kapandı `[DEC-077]`:** §6.1 |
| — | Bütçe + lodBias (WP-011 §23 #2) | **Kapandı `[ADR-012-1, ADR-015-1/2]`:** §8 |
| — | Windows (WP-011 §23 #3) | **Kapandı `[DEC-080]`:** süresiz ertelendi |
| — | Zemin plakaları / rota darlıkları | **Kapandı `[DEC-078, DEC-079]`** |
| — | Oyuncu yönü | **Kapandı:** AP-006 yön C (kullanıcı seçimi) |

**Açık kalanlar (yalnızca gerçekten açık; karar veya ölçüm bekler):**
- **#A Beacon renkleri (M4 devri):** Ofuda Loom (45°, 0.13, 0.96) sıvaya Δv 0.16 / Δs 0.07; Tide Jewel (196°, 0.36, 0.86) ufuk/sis hue'suna komşu. Prototip kodu (`SpecialWeaponPickup.cs:494, :497`); R2 ölçümü kapsül içindir, beacon'ı ölçmez. Seçenek: renk değişikliği (M4) veya beyan kabulü — kullanıcı kararı.
- **#B Skin 1 kızıl aksan s 0.80:** AP-006 aksan sınırı "s ≤ 0.55" ile #A32126 (s 0.80) çelişir; beacon rezervi dışında (v 0.64) ve kırmızı ailesi kuralını (v ≥ 0.60) sağlar. Ya sınır "beacon rezervi dışı" olarak okunur ya kızıl s ≤ 0.55'e çekilir — concept-artist/kullanıcı, Faz C'den önce.
- **#C Skin 3 zeytin-altın aksan (44°, s 0.47)** altın bandında; AP-006 koşulu: Faz F sahne içi testte orb ile yarışırsa s → 0.30. Ölçüm: §6.2 altın ayrışma yöntemi, oyuncu aksanı halka olarak.
- **#D Plaster 1024 göz teyidi:** ilk saray-duvarı içeren oynanış karesinde 1:1 kırpma (§7.3); texel matematiğiyle kabul edilmiştir, ret beklenmez.
- **#E LOD cull 138 m ↔ sis sonu 210 m** (§8): oynanış etkisi yok; S-05 notu — cull'u 210'a bağlamak ya da 138'i belgelemek teknik tercih, sanat kararı değil.
- **#F Üretim materyalleri için asset-gate HSV denetimi** (rezerv kuralı doku ortalaması üzerinde): S-05/test-engineer kapsamı; karar gerektirmez.
- **#G Skin 2 çivit (v 0.58)** FadedBrown'a Δv 0.29 (farklı hue ailesi): Faz F gerçek model render'ında §6.1 R2 ile ölçülür; kural aynı hue ailesinde 0.31 ile sağlanmıştır.
