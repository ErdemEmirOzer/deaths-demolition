# WP-068 — UI v3 · Yön belgesi (konsey sentezi, tur 3)

**Yazan:** product-designer · **Eş imza:** art-director — SIGNED_WITH_CHANGES, §14 (AD) · **Taban:** main `75ce517` · **Dal:** `wp-068-ui-v3` · **Tarih:** 2026-09-24
**Girdiler:** BRIEF + R2-BRIEF (C1–C12) · Faz A denetimi (`WP-068-UI-V3-AUDIT.md`) · dokuz r1 önerisi + dokuz r2 eleştirisi · `council/baseline-small/` (Look B, 23.09) · `UiTokens.cs` / `UiFactory.cs` / `UiButton.cs` / `RunHudScreen.cs` / `ResultsScreen.cs` · stil kılavuzu v1 §2.2/§2.4.
**Statü:** bağlayıcı uygulama spesifikasyonu. Tur 2 tur 1'in üstüne yazar; bu belge ikisinin de üstüne yazar. Burada karara bağlanmayan tek şey §13'teki kullanıcı kararlarıdır.

Kısaltmalar: AD art-director · UX ui-ux-designer · ID interaction-designer · AS animation-specialist · DSE design-systems-engineer · GD game-designer · GR graphic-designer · SD sound-designer · US unity-specialist. RM = Reduced Motion. Tüm px değerleri 1920×1080 referans tuvalinde.

---

## 0 · Özet

1. v3 = **ıslak bahçede lake kutu**: dört malzeme (sumi mürekkep, urushi lake, kinpaku varak, washi kâğıt), üç yükselti, tek yumuşak ışık; HUD düz kalır, derinlik menülerde ve anlarda yaşar.
2. Tipografi üçlüsü **Shippori Mincho B1 · Oswald · Source Sans 3** (OFL, statik SDF, ≈ 1,25 MB); Display yüzü kullanıcı onayına bağlı, Body/Numeric şimdi donar.
3. Renk v3: `StateWarning` **#F7C766** her iki palette (altın rezervinden ΔL* 10,8); CB mavileri indigo (#7FA6D9 / #5F8CC4); iki yeşil kalır.
4. Tek hareket motoru: `UiMotion` (256 struct, 3 saat, cue'lu zaman çizelgesi, MotionClass politikası), preset'ler tek giriş, `DD/UI/Fx` tek uber shader; süreler 80/140/220/280/400/600 ms; RM'nin asla bastırmadığı **Data** sınıfı.
5. Menü/Selection arka planı: **motor içi Look B plaka yığını** (S-05 yakalar, 3 katman, parallax 4/8/12 px); canlı 3B ve video yok.
6. Perde dürüst: kapan 280 → tam kapalı kare → senkron yükleme → aç 280; ilerleme çubuğu/spinner yok. **24.09 kullanıcı kapsamıyla değişti: async yükleme + gerçek yükleme ekranı, §15 bağlayıcıdır** (AD).
7. Koşu içinde toast yok; kare-doğru şeritler (NEW BEST ROUND, ELITE/ULTRA DOWN) afiş yuvasında.
8. Blur yalnız donmuş kare (pause/dükkân/ölüm), bir kez yakalanır.
9. Hold-to-confirm tek token 500 ms: RESET ALL BINDINGS (kesin) + REROLL (kullanıcı); END RUN diyaloğu kalır, QUIT tek dokunuş.
10. Mac: `CanvasScaler.Expand`; bağlayıcı sayfa genişliği **1408 px**; 12 sütun.
11. Kredi: görsel MUST 19 / tavan 32 (sert 35) · ses MUST 18 / tavan 24 (sert 30) · **toplam MUST 37, tavan 56, sert 65** (484'ün ≈ %13'ü).
12. Build: dal (a) tam MUST **+2,0 MB → 549,3 MB** (LZ4HC ≥ 5 MB açarsa); dal (b) tek plaka katmanı **+0,4 MB → 547,7 MB** + kesme sırası + 560 MB tavan seçeneği.

### Kararlar tablosu (C1–C12)

| # | karar | gerekçe (tek satır) | kaynak öneri |
|---|---|---|---|
| C1 | Shippori Mincho B1 700 (Display ≥ 24 px) · Oswald 500/600 (Numeric, basamak ilerlemeleri eşitlenir) · Source Sans 3 400/600 (H3→Caption); yedek: Spectral SC yalnız Display | Source Sans 3 Liberation'dan ≈ %12 dar → 1408 px sayfa + TextScale 1,25'te tek yardımcı gövde yüzü; Oswald 22 px'te HudInk üstünde 1/7, 6/9 ayırır | GR P1, AD r2 C1, DSE r2 C1, US U4 |
| C2 | Motor içi Look B plaka yığını (S-05 yakalar), 3 katman, parallax 4/8/12 px Follow 0,20 s; canlı 3B/video yok; portreler açılışta motor içi render (statik) MUST, turntable SHOULD | Yakalama LUT'tan geçmiş ve 0 kredi; canlı dünya menüde çift yükleme; boyalı plakalar 6 MB | US U7/U8, AD r2 C2, GR r2 §1 |
| C3 | İki dal: (a) LZ4HC ≥ 5 MB açarsa tam MUST (+2,0 MB); (b) açmazsa tek plaka (+0,4 MB) + kesme sırası + 560 MB seçeneği | 0,7 MB kâğıt marjı tahmin hatasının içinde; S-01 ölçümü marjı gerçek yapar | US r2 C3, AD r2 C3, GR r2 C3 |
| C4 | ~~Kapan 280 ms → iki `yield return null` ile tam kapalı kare → senkron `LoadScene` → ilk render karesinde aç 280 ms; statik kart (mühür + mevcut bir kural cümlesi); ilerleme/spinner/ipucu yok; RM: kes → ≥ 300 ms tut → kes~~ **§15 ile değişti (24.09): `LoadSceneAsync` + `allowSceneActivation`, dürüst ensō halkası 0–0,9 + aktivasyon adımı, U9 ipucu satırı** (AD) | `SceneManager.LoadScene(Single)` bloklar; donmuş spinner "asıldı" okunur; Accessibility paneli "screen fades are instant" diyor | US U10, AS r2 C4, ID r2 C4, UX r2 C4 |
| C5 | `StateWarning` #F7C766 (iki palet); CB `StateSuccess` #7FA6D9, CB `BarHealth` #5F8CC4; iki yeşil kalır; ayrım testi ΔL* ≥ 10 | Default'ta Warning == Gold400 birebir; #F7C766 altından ΔL* 10,8, tehlikeden 20,5; tablo 0 FAIL | DSE P2/r2, AD r2 C5, UX r2 C5 |
| C6 | Koşu içi başarım/unlock toast'ı yok; kare-doğru şeritler afiş yuvasında (NEW BEST ROUND kullanıcı, ELITE/ULTRA DOWN 0,6 s damga SHOULD); toast bileşeni v3 dışı | `AchievementEngine.Evaluate` yalnız koşu sonu çalışır; sıfır üretici için kuyruk ölü ağırlık | GD P11/r2, ID r2, AS r2, SD r2 |
| C7 | Tek `UiMotion` sürücüsü (AS P1) + DSE preset kaydı + US `DD/UI/Fx`; kanal listesinde GraphicColor yok (Tint = `CanvasRenderer.SetColor`); Data sınıfı RM'de değişmez | Üç runner üç politika noktası; `TMP.color` per-frame tam regen (bugünkü `UiButton` maliyeti) | AS P1/r2, DSE P5/r2, US U2/U3, GD r2 C7 |
| C8 | Donmuş kare blur (`UiFrozenFrameFeature`, ¼ çözünürlük dual Kawase + mürekkep yıkama, istek karesinde bir kez); scrim kalır, modal opak, metin blur üstüne oturmaz; Pause/Upgrade MUST, ölüm→Results SHOULD | Canlı blur her kare 0,2–0,35 ms; DEC-088 payı 0,5 ms | US U9, AD r2 C8, DSE r2 C8 |
| C9 | Tek `HoldConfirm` token 500 ms, Linear dolum `AccentPrimaryPressed`, Data sınıfı (RM'de kalır); RESET ALL BINDINGS kesin, REROLL kullanıcı; END RUN diyalog (CANCEL varsayılan odak), QUIT tek dokunuş | Diyalog + tutma çift sürtünme; 400 ms tetik basışının içinde, 600 ms iki ritim yaratıyor | ID r2 C9, DSE r2 C9, GD r2 C9, UX r2 C9, SD r2 C9 |
| C10 | `ScreenMatchMode.Expand`; 16:10 → 1920×1247; UI scale 1,2 → 1600×1039; **bağlayıcı sayfa 1408 px**; her yerleşim sütun kesri; Mac'e özel 474/350 sayıları düşer | `resizableWindow: 1` ile yükseklik eşlemesi keyfi darlık üretir; Expand 16:9/21:9'da özdeş | US U6, UX r2 §1.5, DSE r2 C10 |
| C11 | Görsel MUST 19 kr (A1–A3 9 · A4 3 · mürekkep kiti 3 · yeniden deneme 4), tavan 32, sert 35; ses MUST 18 (6 render), tavan 24, sert 30; toplam MUST 37 / tavan 56 / sert 65 | Plakalar, mühür, tuş kapakları, imleç, kumaşlar vektör/motor içi → 0; mürekkep pikselleri tek sahip | GR r2 C11, AD r2 C11, SD r2 C11 |
| C12 | Dokuz kusur dosya:satır ile doğrulandı (§10); hepsi **Dilim 0** | Kod ve karelerle görüldü | DSE r2, SD r2, US r2, ID r2, GD r2, UX r2 |

---

## 1 · Tasarım dili

**Tez.** Her panel dört malzemeden yapılmış fiziksel bir nesnedir: **sumi** (metin, maske, kontur), **urushi** (yüzey: neredeyse siyah, üst kenarda tek yumuşak parıltı, asla düz gri), **kinpaku** (yalnız altının zaten anlam taşıdığı yerde: para, Special), **washi** (hafif hissetmesi gereken katmanlar: perde kartı, tooltip, boş durum). Derinlik gölge yağmurundan değil malzemeden ve tek bulutlu ışıktan gelir. Hareket, malzemenin yaptığı şeydir: mürekkep sızar ve süpürülür, kâğıt kayar, lake bir kez parlar, mühür basılır — **lake sıçramaz**, hiçbir yüzey 1,0'ı aşmaz. Menü ve koşu aynı havadır: arka plan Look B'den motor içinde yakalanır. Bilgi hiyerarşisi dekorasyondan önce gelir: ekran başına tek odak nesnesi, her durum tasarlanmış, her metin bandı 1,25 için boyutlanmış.

**Üç yükselti (AD üçlüsü; DSE'nin beşlisi geri çekildi).**

| `UiElevation` | tarif | kullanan |
|---|---|---|
| `Elev0` düz | hairline + köşe çentikleri + 1 px `SheenRim` (Cream100 α 0,10 üst kenar, 2 px Sumi900 alt kenar); **gölge yok**; `HudInk` opak | HUD'un tamamı, wells (`SurfaceInset` bir yüzeydir, yükselti değil) |
| `Elev1` sayfa paneli | bevel + 9-slice yumuşak gölge 8 px, `ShadowKey` α 0,35 | menü pane'leri, kartlar (dinlenme), liste satırları |
| `Elev2` yüzen | bevel + gölge 24 px α 0,55 + altında `ScrimOverlay` | modal, hover/odak kartı, perde kartı, tooltip |

Gölgeler runtime kit atlasındaki tek 9-slice Gaussian sprite (64², border 24) ile çizilir; uGUI `Shadow`/`Outline` bileşeni yasak. Alan parıltısı (`SheenRim` yüzey üstü) α ≤ 0,05 (ölçüldü: 0,06 TextMuted'ı 4,56:1'e, 0,10 4,00:1'e düşürür); 1 px bevel kenar sayılır, zarfın dışındadır, metin kenardan ≥ 8 px uzak durur.

**Ornament kuralları (AD kesintisiyle).** Set: 2 fırça darbesi (alt çizgi, bölücü) · 1 yuvarlak hanko · kumiko karosu (yalnız perde dolgusu) · washi karosu · kinpaku karosu · mühür cihazı (reticle halkası + tek yaprak, vermilion, vektör). Mon kabartmaları ornament değil, **`Badge.*` veri glifleridir** (1 hücre mon roundel; rozet başına ayrı mon COULD — A2 sheet'i 16 hücredir; lore yok, metin yok) (AD). Kanji yok. Ekran başına en fazla bir tören öğesi (mühür damgası / varak parıltısı / mürekkep sızması). Seviyeler: L0 HUD → çentik, `Marker.Edge`, kinpaku yalnız altın çipi ve Special halkası altında; L1 panel → çerçeve v3, washi α ≤ 0,08, bölüm başına bir `Ornament.Divider`; L2 kimlik → H1 altında bir fırça alt çizgisi, profil kartında hanko (mürekkep, `TextSecondary` tonunda); L3 tören → boot, ölüm, Results, satın alma. Vermilion ornament = logo mührü + başlık tiki, başka yerde asla. Ornament asla durum taşımaz.

**HUD'un düz kalma kuralı.** Köşe panelleri `Elev0`, `HudInk` (Sumi900 α 0,92) opak; DEC-077 kontrastı buna karşı kanıtlanır. Panel altında gölge halesi yaklaşım bandını karartır (v005 MUST-2 minyatürü) → yasak. HUD'da doku yok, fırça yok, mühür yok; tek istisna altın çipi ve Special halkası altındaki kinpaku karosu (α 0,35).

**Rezervler (§2.2/§2.4, değişmez).** Altın = para + Special (+ fiyat); **`BarReload` Focus400 kalır, varak almaz** (para değil, Special değil; ΔL* 15,3 altından). Vermilion = birincil CTA (ekran/modal başına bir Primary) + başlık tiki + mühür. Uyarı/tehlike anlamı korunur. Beacon renkleri §9 #A dokunulmaz; UI kenar işaretçisi Special'ın beacon tonunu **almaz**, mevcut altın→kor aciliyet lerp'ini korur. Tören için **ginpaku** (`SilverLeaf`, krem) — madalya, NEW BEST, seviye; kinpaku yalnız `StateGold` ile zaten boyanmış öğenin altında. Varak parıltısı: 600 ms, ≥ 1 s aralık, birleştirilir, × `FlashScale`, kare başına ≤ 2.

---

## 2 · Token v3

Kaynak C# (`UiTokens.cs`) kanonik kalır; Editor exporter `UiTokens.hlsl` (S-07), `ui-tokens.json` (DTCG) ve stil kılavuzu §2.2 UI tablosunu yazar; `Export_IsFresh` ve `TableEqualsV2` testleri (refactor 0 değer değiştirir). `UiTheme` yalnız varlık referansı taşır.

### 2.1 Renk — iki palet (final hex)

Primitifler `UiTokens.cs:219–249` (Sumi900 #12100F, Sumi700 #232120, Stone600 #34322F, Stone350 #9A948C, Cream300 #B9B2A6, Cream100 #F3EEE3, Vermilion600 #C4362B, Vermilion700 #9E2A21, Vermilion300 #E8806F, Sakura400 #E8A0B8, Moss400 #748F60, Gold400 #D9A93C, Coral400 #EE7063, Jade400 #8FC079, Focus400 #F2D98C). Yeni primitifler: **Amber300 #F7C766**, **Indigo400 #7FA6D9**, **Indigo500 #5F8CC4**. Sky400/Sky500/Amber400 kaldırılır (kullanımsız).

| token | Default | ColorBlindSafe | ölçüm (WCAG 2.1) |
|---|---|---|---|
| `StateWarning` | **#F7C766** (eski Gold400) | **#F7C766** (eski #F2B44C) | L* 82,7 · inset 12,03 · panel 10,17 · base 11,23 · ink-on-fill 12,03 · ΔL* Gold 10,8 / Danger 20,5 |
| `StateSuccess` | Jade400 #8FC079 | **#7FA6D9** | CB: raised 5,09 · inset 7,55 · panel 6,4 |
| `BarHealth` | Moss400 #748F60 | **#5F8CC4** | CB: track 5,45; koral düşük-cana ΔL* 1,5 → 5,0 |
| `StateGold` / `BarSpecial` | Gold400 | Gold400 | değişmez |
| `StateDanger` / `BarHealthLow` | Coral400 | Coral400 | değişmez |
| `BarReload` | Focus400 | Focus400 | değişmez; varak yok |
| `ShadowKey` NEW | Sumi900, α yükseltiye göre | aynı | gölge |
| `SheenRim` NEW | Cream100 α ≤ 0,05 (alan) / 0,10 (1 px kenar) | aynı | zarf testi |
| `InkStroke` NEW | Sumi900 α 0,90 | aynı | TMP outline preset 1–1,5 px |
| `GoldLeaf` NEW | Gold400 + kinpaku karo | aynı | yalnız rezerv |
| `SilverLeaf` NEW | Cream100 + kinpaku karo (renksiz) | aynı | tören |

Ayrım testi (`UiSeparationTests`, iki palet): Warning↔Gold ≥ 10 ✓ 10,8 · Warning↔Danger ✓ 20,5 · Focus↔Gold ✓ 15,3 · Danger↔Vermilion ✓ 17,2 · **Warning↔Focus 4,5 ✗** → iki buluşma yeri var: Controls dinleme yuvası (uyarı konturu odak halkasının yerini alır) ve E11 silah paneli (`BarReload` Focus400 + "LOW" caption #F7C766); ikincisinde kural: LOW durumunda reload çubuğu da `StateWarning`'e döner (v005 SHOULD-6), LOW dışında panelde uyarı metni yoktur → aynı panelde iki soluk altın iki anlam taşımaz (AD). Kural: `TextMuted` (Stone350) asla `SurfaceRaised`/Elev2 üstünde (4,25:1). İki yeşil kalır (Jade = kazanılmış, Moss = can); tek yeşil seçilirse yalnız Jade400 olabilir (Moss400 metin olarak panelde 4,46:1 kalır) — §13.

Rezerv kaydı testi (`UiReserveRegistryTests`, kaynak taraması): `StateGold`/`BarSpecial`/`GoldLeaf` yalnız para sayacı, fiyat, Special HUD; `AccentPrimary` yalnız Primary/Danger buton, başlık tiki, BUY pill, mühür (boot/logo); tier/pick pipleri krem — E24 ile tutarlı, "sahipli pip" kayıttan çıkarıldı (AD); ekran/modal başına ≤ 1 Primary (runtime). Çip kurucusu kelimesiz veya glifsiz durum çipini reddeder.

### 2.2 Tipografi

| rol | yüz (OFL) | ağırlık | `UiType` boyutları | izleme | büyük/küçük |
|---|---|---|---|---|---|
| Display | **Shippori Mincho B1** | 700 | Display 56 · **Banner 48** (NEW) · H1 34 · H2 24 | +0,06 / +0,05 / +0,04 / +0,03 em | caps |
| Numeric | **Oswald** | 500 (600 NumericM) | NumericXL 44 · L 32 · M 22 · **S 16** (NEW) · **WorldNumber S18/M22/L26/XL30** (NEW, S-07) | 0 | rakam |
| Text | **Source Sans 3** | 400 / 600 | H3 20 · Body 18 · BodyStrong 18 · Label 15 (caps +0,08 em) · Caption 14 (taban) | 0 | Label caps, sahte small-caps yok |
| yedek | Spectral SC yalnız Display; Numeric/Body sabit | | | | |

Kurallar: Display yalnız ≥ 24 px; Body/Caption asla serif; italik yok; `LineHeight()` sabit 1,25 yerine `faceInfo` oranını okur (Source Sans 3 ≈ 1,26 em); **metin taşıyan her bileşenin yüksekliği/asgari genişliği = yüz metriği × TextScale, build'de bir kez `GetPreferredValues` ile** (sabit 22/24/30 bantları biter). Basamak ilerlemeleri import adımında eşitlenir (`Glyph Table → horizontalAdvance = max`), `<mspace>` yok (`richText = false`). Kerning: TMP 3.0.6 yalnız legacy `kern` tablosunu okur; Shippori GPOS → ≈ 20 caps çifti (AV, TA, LT, RA, WA, To…) script ile glyph-adjustment tablosuna eklenir; kabul öncesi AV/To testi.

Atlaslar (statik, Alpha8, Player'da dinamik kapalı, TTF'ler `Resources/` dışında, yalnız `.asset` `Resources/UI/Fonts/`): Display 1024×512 @ 64 pt padding 8 (≈ 62 glif: caps, rakam, noktalama) 0,5 MB · Text 1024×512 @ 44 pt padding 6 Latin-1 (≈ 200) 0,5 MB · Numeric 512² @ 64 pt padding 12 (rakam + `: / % + − × .`) 0,25 MB → **1,25 MB** (tavan 1,5). Yüz başına ≤ 2 materyal preset'i (`Face`, `FaceOutline` = InkStroke); toplam metin SetPass ≤ 6. `UiFontFallbackGuard` üç yüzü de korur (WP-022M dersi); oyuncu adı için yedek LiberationSans dinamik kalır. Kanji metin olarak yok.

### 2.3 Boşluk · yarıçap · çizgi · boyut

`UiSpace` mevcut değerleriyle kalır (XS 4 · S 8 · M 12 · L 16 · XL 24 · XXL 32 · XXXL 48 · Section 64 · Page 96; `UiTokens.cs:13–25`) — hiçbir konsey önerisi enum değerlerini değiştirmedi, yeniden değerleme her mevcut çağrı noktasını sessizce kaydırırdı (AD). Grid: 12 sütun, oluk 24, sayfa marjı 96; sütun = (sayfa − 264) / 12 → 1408'de 95,3 px, 1728'de 122 px. Dikey ritim 8; satır 40/48/56; kart iç boşluğu 24; bölüm arası 32. `UiRadius` {4, 8, 10, 14, Pill} · `UiStroke` {Hairline 1, Rim 2, Focus 3} · `UiSize` {RowChip 22→metrik, CardChip 28→metrik, Toggle 40, HudChip 40, Compact 44, Target 48, CTA 56}. Hedef boyutu ≥ 44 (ikon buton 44², slider tutamaç hit 44).

### 2.4 Malzeme

`UiMaterial` { Ink (düz), Lacquer (FadeDown parıltı + rim), Washi (256² lif karosu BC4, α 0,04–0,08, `Image.Tiled`), GoldLeaf (kinpaku 128² karo α 0,35 + S-07 glint), SilverLeaf (aynı karo, Cream100 tint) }. Metin çiftleri malzemenin min/maks texeli üstünde kompozit test edilir (T1 zarfı).

### 2.5 Hareket tokenları

`UiMotionTokens` (C#) ↔ `UiTokens.hlsl` ↔ `design-system/tokens/motion/motion.json`.

| `UiDuration` | ms | kullanım |
|---|---|---|
| Instant | 80 | basma, durum rengi, sayaç tiki |
| Fast | 140 | hover yıkama, odak seyahati, toggle, çip giriş/çıkış, prompt, **tüm çıkışlar** |
| Base | 220 | pane içeriği, kart kaldırma/dağıtma, modal, damga bağlamı |
| Screen | 280 | ekran giriş, afiş metni, perde kapan/aç |
| Emphasis | 400 | fırça arka planı, kart dağıtma toplamı, tutma bağlamı |
| Reveal | 600 | sayaç tırmanışı, çubuk dolumu (Results/Profile), glint |

Kural: çıkış = girişten bir adım kısa; dekoratif hiçbir şey > 600 ms (afiş tutma hariç); döngü ≤ 1,1 Hz (final saniye bandı hariç). Stagger: Row 40 ms (≤ 6 öğe, 7+ altıncının gecikmesini paylaşır) · Card 60 · Beat 120. Tutmalar: `TrailHold` 350 · `TooltipDelay` 350 fare / 500 odak · `BannerHold` toplam zarf 2,4 s · `HoldConfirm` 500.

| `UiEase` | cubic-bezier | anlam |
|---|---|---|
| Settle | (0.33, 1.00, 0.68, 1.00) | varış ≤ 280 ms |
| SettleLong | (0.16, 1.00, 0.30, 1.00) | yalnız Emphasis/Reveal uzun yolculuk |
| Exit | (0.32, 0.00, 0.67, 0.00) | ayrılış |
| Move | (0.65, 0.00, 0.35, 1.00) | ekranda iki konum arası (pane, odak halkası, slider, toggle) |
| Brush | (0.25, 0.10, 0.25, 1.00) | reveal ilerlemesi |
| Stamp | iki faz: 1,12 → 1,00 InCubic 90 ms + 40 ms tut (toplam 130); **1,0'ı aşmaz, altına inmez** | mühür, PURCHASED, madalya, sikke |
| Linear | — | veri: reload, halka, geri sayım, tutma dolumu |
| Follow(h) | yarı ömür 0,05 / 0,12 / 0,20 s | değer-bağlı: reticle yayılımı, parallax, ghost |

OutBack, Spring, sıçrama yok. Eğriler `UiEase.cs`'te kapalı form + HLSL'de aynı; 64 örnekli LUT yalnız DCC'den gelen eğriler için.

**Hareket sınıfları** (her preset birini beyan eder; sürücü politikayı uygular):

| class | RM | flash |
|---|---|---|
| Decor (translate, scale, stagger, parallax, drift, glint, perde kayması) | t=0'da son durum; ekran/pane/öğe fade'leri **0 ms** | — |
| Delta (ghost, iz, sayaç tırmanışı, "+N") | son değer; ghost 350 ms statik gösterilir, sonra kalkar | — |
| **Data** (reload çubuğu, Special halkası, geri sayım, recovery track, tutma dolumu, projeksiyon-bağlı işaretçi/çip, timeout çizgisi) | **değişmez, Linear** | — |
| Attention (mevcut iki pulse: final saniye bandı, Results NEW) | tam α'da tutulur | — |
| Flash (hasar kenarı, glint, kıvılcım, veil) | çalar | tepe × `FlashScale` + FlashGate |

FlashGate: 43 000 px² (1080p'de WCAG 2.3.1 alanı) üstünde saniyede ≤ 3 onset (global, aşan onset **birleşir**, kuyruklanmaz — `UiHitDirectionLayer` `Max(visible, level)` deseni kural olur); öğe başına ≤ 2/s; küçük glintler sayılmaz ama FlashScale'e tabidir. Metin taşıyan öğede scale pop ≤ 1,06 (TextScale 1,25'te denetlenir); hiçbir tween `fontSize`/`sizeDelta`/LayoutGroup geometrisi/`TMP.color` dokunmaz. Shader globalleri: `_DD_UiTime` (unscaled), `_DD_WorldTime`, `_DD_Motion` (RM'de 0), `_DD_Flash`, `_DD_Ink`, `_DD_Paper`.

### 2.6 Sıralama katmanları

`UiLayer`: WorldNumber 40 · FrozenFrame 90 · WorldMarker 95 · Hud 100 · HudFeedback 110 · HudBanner 120 · HudRibbon 130 · Screen 200 · ScreenOverlay 250 · Pause 300 · Modal 320 · Tooltip 360 · Curtain 400 · Debug 1000. Mevcut `SortingHud/Screen/Pause/Dialog` (100/200/300/320) alias kalır.

### 2.7 Canvas ölçekleme — Expand + 1408 kuralı

`CanvasScaler` referans 1920×1080, `ScreenMatchMode.Expand`, `pixelPerfect` kapalı. Sonuç: 16:9 1920×1080 · 21:9 2560×1080 · 1440p 1920×1080 (×1,33) · Mac 3024×1964 → **1920×1247**; UI scale 1,2 → 1600×1039. **Bağlayıcı sayfa = 1600 − 2×96 = 1408 px** (her ekranda geçerli en dar durum); tavan `ContentMaxWidth` 1728. Her yerleşim sütun kesridir; sabit genişlikli her öğe 1408'e sığmalı (tehdit satırı 3×240+16 = 736 ✓; Selection 3×453+48 = 1407 ✓; dükkân 3×453 ✓). Yerleşimler +%15 yükseklik toleranslı (üstten ankraj, asgari yükseklik). HUD köşeleri gerçek kenarlara yapışır; merkez blokları gerçek merkezde; arka plan "cover" ölçekli. Yakalama matrisi: 4 çözünürlük × UI scale {0,9, 1,2} × TextScale {1, 1,25}.

---

## 3 · Hareket sistemi

**Mimari (tek).** `UiMotion` — sahne yüklemesini aşan tek `UiMotionDriver`, `[DefaultExecutionOrder(900)]`, `LateUpdate` (ekran `Update`'inden sonra, `Canvas.willRenderCanvases`'tan önce), boşta kendini kapatır (idle 0). Depolama `UiTween[256]` struct + serbest yığın; `{index, generation}` tutamaçları; sınıf/delegate/boxing yok. Taşma: sığmayan tween **son durumuna iner** (en eskiyi düşürmek öğeyi orta yolda dondurur). Kanallar: Alpha (CanvasGroup) · Tint (`CanvasRenderer.SetColor`) · Pos · Scale · RotZ · Fill · Clip (`RectMask2D.padding` + softness) · Count (int değişince `SetText("{0}", v)`, 0 ayırma) · Float · FxStart (UV1'e başlangıç zamanı bir kez yazılır, GPU ilerlemeyi hesaplar). **GraphicColor kanalı yok.** Sahiplik: (hedef, kanal) başına bir tween; yeni istek mevcut değerden yeniden hedefler, süre kalan mesafeyle ölçeklenir (taban Instant); `UiScreen.OnDestroy` → `KillOwner`.

**Saatler.** Unscaled (varsayılan: menü, HUD, pause, timeScale 0 donması) · Scaled (hasar sayıları, durum damgaları: pause'da durur, `DeathSequence` ile yavaşlar) · External (kayıtlı float: `GroundBlinkClock` fazı, `DeathOverlayT`). Test saati override'ı; zaman tabanlı, 60/120 Hz aynı.

**Preset'ler tek giriş.** `UiMotion.Play(UiPreset.X, target)`; preset = {kanallar, tokenlar, MotionClass, RM eşlemesi, flash sınıfı, cue}. Ekranlarda ham süre/`Lerp`/`MoveTowards`/`unscaledDeltaTime` kaynak denetiminde başarısız (T6). Preset listesi (DSE): EnterScreen, ExitScreen (140 fade + 8 px aşağı itme), PaneOut/PaneIn, EnterModal (0,96→1 + scrim), CardDeal, CardWipe (reroll), StampIn, CounterRoll, FocusTravel, PressDepth, BannerReveal, RibbonIn, ChipPop, BarGhost, RegenTrack, DeniedNudge (±4 px, 2 döngü, 160 ms, RM kapalı, HUD oyun bandında asla), HoldFill, CurtainClose/Open, InkBleed, Glint, ParallaxFollow, AmbientParticles.

**Zaman çizelgeleri.** Havuz 32, her biri ≤ 24 adım + 8 cue: `.At(t).Play(preset, target)`, `.Stagger(targets, step)`, `.Cue(UiEvent, t)`. Cue'lar `UiEvents`'i **tam zamanda, kare içinde sıralı** raise eder (S-08 kancası); geri çağrı `IUiMotionListener.OnCue(int)` (closure yok). Her kahraman çizelge atlanabilir: Submit/Cancel/tık `Complete()` → görseller t=1, **cue'lar son cue'ya çöker**, döngü sesleri 30 ms fade; sonraki basış eylemi yapar. Ses hareketi beklemez: temas sesleri `Stamp` temas karesine (t=90 ms) cue olarak oturur; RM'de görsel t=0'da biter, cue'lar yazarlık zamanını korur.

**Canvas bölümleme.** Her yüzey: statik kök + hareket adacıkları (her biri ≤ 24 grafik, yalnız etkileşimliyse `GraphicRaycaster`); gizleme `Canvas.enabled = false` (asla `SetActive`); yasak: `Mask` (stencil), `Outline`/`Shadow`, `LayoutGroup`/`ContentSizeFitter`; yumuşak kırpma `RectMask2D.softness`; yüzey başına ≤ 4 tam ekran alfa katmanı (savaşta ≤ 2). HUD adacıkları: Health, Ammo, Gold, Round, Banner, Threat, DamageEdges (yeni) + Special, Marker/Status, Reticle (mevcut). Dükkân: 3 kart alt-canvas'ı, her biri kendi raycaster'ı. Görseller metinden önce (batch).

**`DD/UI/Fx` uber shader (S-07).** UI-Default tabanı, yalnız CLIP_RECT + ALPHACLIP keyword'leri (4 varyant), stencil + `_ClipRect` korunur. `BaseMeshEffect` mod + başlangıç zamanını uv1'e, parametreleri uv2'ye bir kez yazar (uGUI `MaterialPropertyBlock`'u yok sayar). Modlar: **InkReveal** (G kanalında zaman haritası eşiği; 0,04 genişlik band koyulaşır ve titrer — afiş, alt çizgi, mühür, ölüm sızması, perde) · **Leaf** (glint bandı × sprite alfa × varak çatlak karosu; one-shot uv1.x veya döngü `_DD_UiTime` + faz; kinpaku yalnız para/Special, ginpaku başka yerde; ≤ +0,25 parlaklık) · **Stamp** · **Sheen** (yalnız menü) · **SegmentBar** (segment + iz) · **SDF glyph** (BC4 mesafe alanı: outline/glow/erozyon bedava) · **Particles** (bir kez kurulan quad'lar vertex shader'da: yaprak, yağmur, Special hazır kıvılcımı; `UiDrift`'in ≈ 1,2 ayırma/karesini sıfırlar; HUD'da yalnız one-shot kıvılcım ≤ 0,3 s, ambient yok). Tek materyal → yalnız atlas değişimi batch kırar. TMP `BaseMeshEffect` almaz → metin reveal'i Clip kanalıyla. Shader `UiTokens.hlsl` include eder, `UNITY_UI_CLIP_RECT` + CanvasGroup alfa destekler. GPU savaşta < 0,05 ms.

**RM / flash politikası sistem düzeyinde.** Sınıf tablosu §2.5; `UiSettings.ReducedMotion` → `_DD_Motion = 0` + sürücü Decor'u t=0'a alır; `UiSettings.ReduceFlashes` → `FlashScale` + FlashGate. Accessibility önizleme paneli yerel override ile aynı preset'leri çalar. Ekran/pane fade'i RM'de 0 ms (panel vaadi: "screen fades are instant"); perde RM'de kes → ≥ 300 ms tut → kes.

**Geçiş şimleri (AS P10).** Çekirdek, eski bileşenleri özdeş zamanlamayla preset üstüne ince şim olarak gönderir: `UiScreenFade`, `UiStaggerIn`, `UiPulse`, `UiDrift`, `UiBar`/`UiButton` lerp'leri, RunHud afişi, Upgrade `TickFade`. Dinlenme kareleri öncesi/sonrası birebir; sonra şimler kaldırılır, v3 preset'leri eklenir. `UiButton.Update` biter (etiket rengi anında, dolgu Tint ile).

**Performans hedefleri.** 16 canlı tween ≈ 0,012 ms, 0 B/kare ve 0 B/Play; HUD'da ≤ 4 eşzamanlı tween; savaş HUD Δ ≤ +0,15 ms (§8).

---

## 4 · Bileşen envanteri v3

Sütunlar: amaç · varyant · durum · hareket · ses (UiEvent) · erişilebilirlik · ekranlar. ★ = MUST.

| # | bileşen | amaç / varyant | durumlar | hareket | ses | a11y | ekranlar |
|---|---|---|---|---|---|---|---|
| 1 | ★ `UiButton` v3 | Primary / Secondary / Ghost / Nav / Card / Danger / Icon 44² / **Hold**; L56 / M48 / S40 | default · hover (yıkama 0→0,08) · focus (halka 3 + hale 3 dışta, kare 1'de görünür) · pressed (koyu dolgu tokenı — Primary/Danger `AccentPrimaryPressed`, diğerleri `SurfacePanel` (mevcut `UiButton` matrisi) — + 2 px iç bevel, **scale yok**) (AD) · disabled (`OpacityDisabled` + neden caption) · selected · locked · hold (Linear dolum) | PressDepth Instant; hover Fast Tint; focus halkası anında + `Focus.Brackets` Move 140 (RM: atlar) | `PressDown` (pointer-down, yalnız Primary/Secondary), `Confirm`, `UiHover` (yalnız fare), `FocusMoved` (yalnız klavye/pad) | hedef ≥ 44; halka %100; Danger yalnız modal içinde | hepsi |
| 2 | ★ FocusRing | tek seyahat eden halka + hale; kontrol başına halka ilk-kare yedeği | — | Move 140; RM atlar | — | ring kare 1 | menüler, dükkân, pause |
| 3 | ★ `UiSwitch` | 44×24 anahtar + ON/OFF kelimesi | on/off/disabled | knob 20 px Move 140, kelime %50'de | `UiToggle` | knob konumu = şekil | Settings, Accessibility, Controls |
| 4 | ★ `UiStepper` | ‹ değer › (44² ikon butonlar); Quality, Display, Palette, Aim Assist | uçta disabled; ≤ 3 kategori sarar, sıralı değerler sarmaz | ok basma 0,94 Instant, değer crossfade Fast | `SliderChanged` detail | A/→ ileri, ← geri | Settings, Accessibility, Controls |
| 5 | ★ `UiSlider` v3 | tutamaç 28 px lake boncuk / hit 44, ray 6, değer etiketi, durak tikleri | — | sürükleme 1:1; adımda etiket anında, knob Move 140 | ≤ 12 tık/s, pitch 0,92→1,08 | odak = Focus400 halka boncukta | Settings, Accessibility, Controls, Pause |
| 6 | `UiPager` (mevcut) / TabBar SHOULD | sayfa ‹ n/6 ›; LB/RB, Q/E | — | pane-switch koreografisi yönlü | `PaneOpened` | — | Achievements |
| 7 | ★ `UiBindingRow` | tuş kapağı/disk (vektör 9-slice art) + ad + override elması | dinleme (uyarı konturu + Linear timeout çizgisi; halka yerine) · çakışma (Notice Warning satır altı) | yakalanan glif Stamp | `UiDialogOpen`, `Confirm`, `Back` | kapak kontrastı v005 MUST-4 | Controls |
| 8 | ★ `UiPrompt` v3 | glif-art yuvası + canlı metin (rebinding bedava) | şema değişimi | crossfade Fast, aynı kutu | sessiz | — | HUD, Selection, Pause |
| 9 | ★ `UiChip` v3 | Row / Card / Hud / World; Neutral / State (α 0,6 kontur + glif + kelime) / New / Selected / Value; **yükseklik metrikten, genişlik içerikten (min korunur)** | — | ChipPop Stamp Base in, Exit Fast out | çip türüne göre (tehdit: HudThreat*) | kelime + glif zorunlu (kurucu reddeder) | hepsi |
| 10 | ★ `UiNotice` | Info / Warning / Error; 4 px bar + glif + metin; 1,25'te 1–2 satır otomatik | — | PaneIn | — | — | Profile, Settings, Controls, Shop |
| 11 | ★ `UiModal` | S 560×264 · M 640×340 · L 680×520 + Confirm preset; Elev2, opak | odak tuzağı, varsayılan güvenli eylem, B/Esc = ikincil, iptal odağı iade | scrim Fast → 0,72; 0,96→1 + α Base; kapanış Exit Fast | `UiDialogOpen`/`Back` | metin yalnız opak yüzeyde | Pause, Controls RESET (hold ile), Profile picker |
| 12 | ★ `UiEmptyState` | 48 glif + Body + eylem | — | PaneIn | — | — | Profile, Achievements, Results, Shop |
| 13 | ★ `UiCard` (FramedCard façade) | katman sözleşmesi: gölge → malzeme → görsel → AP-012 çerçeve → başlık → metin → inset raf; Elev0/1/2 | seçilebilir: dinlenme Elev1, hover/odak Elev2 (y +4 px) + `Focus.Brackets`; seçili (mevcut tercih) = Elev1 + `SurfaceRaised` dolgu + krem kontur + çip, kalıcı gölge büyümesi yok (AD) | hover/odak y +4, gölge 8→24 Fast; locked kaldırmaz | `UiSelect`, `Denied` 0,4 | — | Selection, Shop, Results, Profile |
| 14 | ★ `UiShopCard` | 453–560 × 480; başlık 64 (ikon well 64, dal Label, tier pip 8×12) · H2 32 · açıklama 5×40 · bölücü · **önce→sonra satırı** · inset raf 96 (fiyat + BUY) | Available / Unaffordable / Purchased / Locked / MaxTier; çip 208×30 sağ üst | CardDeal (y +24→0, α, Settle 280, stagger 60); CardWipe yalnız değişen kart; PURCHASED StampIn + kinpaku | `CardDealt` ×3, `UpgradePurchase` + `StampLanded`, `CardFlipped`, `Denied` | BUY 3:1 raf; kelime + glif | Shop |
| 15 | `UiRewardCard` | Results unlock satırı 92 h | — | StampIn | `UiUnlock`/`UiAchievement` | — | Results |
| 16 | ★ `UiBar` v3 | hairline ray, 10 segment (1 px Sumi900 tik) + %30 güçlü tik, **ghost iz** (Cream300 α 0,5, tut 350, boşal 400), regen track 4 px, düşük = glif + kelime | low · regen | fill Base Settle; ghost Delta; regen Data | sessiz | ghost luminance-only (CB) | HUD, Profile, Results, Achievements |
| 17 | ★ `UiRing` | `Image.Filled Radial360`; reload 64, Special 132, başarım ilerleme, boot | — | Data Linear; hazır kıvılcımı one-shot | sessiz | — | HUD, Achievements |
| 18 | ★ `UiPips` | tier merdiveni (krem, dolu/boş), pick yuvaları (2 mühür yuvası) | — | Stamp Instant | sessiz | dolu/boş = şekil | Shop, Selection |
| 19 | ★ `UiCounter` | Plain / Currency / Fraction (iki etiket, rich text yok) / Timer / Delta (+N 16 px yükselir) | — | CounterRoll ≤ 600 Reveal; Currency Δ birleşik (0,2 s), pop 1,06 | `CounterRolling`/`CounterSettled` (yalnız Δ ≥ 10) | RM son değer | HUD, Results, Statistics, Shop |
| 20 | ★ HeroTile | 414×120 (1408'de 3 sütun = 334): NumericXL + Label | — | CounterRoll | — | — | Statistics, Results |
| 21 | ★ StatStrip + CompareBars | 2 sabit çip satırı (DMG · RATE · MAG · RELOAD · sınıf · isabet) + 4×6 px çubuk, roster maksimumuna normalize, sayı yanında | — | çubuk Base Settle odakta | sessiz | "relative to the other classic weapons" caption | Selection |
| 22 | ★ `UiMedal` | 56 px `Medal.Common/Rare/Epic` + kilitli silüet α 0,35 + 4 px ilerleme halkası | earned / in-progress / locked / new | StampIn + ginpaku glint (NEW) | `StampLanded` (Common tok / Rare tok+çan / Epic +varak) | tier = şekil | Achievements, Results, Profile |
| 23 | ★ `UiBanner` / Ribbon / Stamp | Banner 48 Display + InkStroke; S varyantı 380×56; Ribbon (NEW BEST ROUND); Stamp (ELITE DOWN 0,6 s) | — | metin Clip 220 → fırça arka planı Brush 400 → tut → Exit 280 (α + y −8) | `RoundStart`/`RoundComplete` (sahip Run), `HudNewBestRound`, `HudEliteDown`/`HudUltraDown` | flash yok | HUD, Results |
| 24 | ★ `UiCurtain` | `UiFlowRunner` üstünde DontDestroyOnLoad canvas (sıra 400); iki lake kapı (kumiko karosu A6, α ≤ 0,08 — perde dışında kullanılmaz) (AD) + washi kart (mühür + kural cümlesi); menüde ScreenSpaceCamera, Run'da Overlay; `sceneLoaded`'da kameraya yeniden bağlanır | — | kapan 280 Move / aç 280 Settle; RM kes | `UiCurtainClose` (`LoadScene`'den ≥ 1 kare önce) / `UiCurtainOpen` | ink asla beyaz | tüm sahne geçişleri |
| 25 | ★ `UiBackdrop` | plaka yığını 3 katman + ParallaxFollow + shader yaprak/yağmur (`Particles`) + fener nefesi (emissive maske) | — | Follow 0,20; RM statik kompozit | menü bed'i `AmbCommon` + uzak `AmbFurin` | metin-güvenli dikdörtgen (T10) | MainMenu, Selection |
| 26 | ★ `UiCursor` | donanım `Cursor.SetCursor`, 32 + 64 px (Retina) RGBA32; Arrow / Press | — | sprite swap 0 ms, kare başına asla | sessiz | Run'da ve pad'de gizli, fare hareketinde döner | menüler |
| 27 | `UiTooltip` SHOULD | washi kart ≤ 320, pad 12, 8 px ok; yalnız karar ekranları | — | in Fast (α + 4 px) 350 fare / 500 odak; out 0 ms | sessiz | odaklanan kontrolü asla örtmez; bir kez ölçülür | Selection, Shop, Achievements, Profile |
| 28 | ★ EdgeMarker | 44 disk `HudInk` + 28 glif + mesafe Caption; beacon VFX ile aynı silüet | — | projeksiyon-bağlı, eased lag yok | `SwBeaconLoop` (var) | altın→kor aciliyet lerp korunur | HUD |
| 29 | ★ StatusStamp (`UiEnemyStatusMarkers`) | NET (ofuda etiketi) / DAZED (spiral) / PULLED (rezerv) 24 px outer-only glif + kelime; havuz 16 en yakın | — | Stamp Fast in, Exit Fast out; Scaled saat | `ZombieReact*` (var) | kelime + şekil | HUD |
| 30 | ★ DamageNumber (S-07) | havuzlu `TextMeshProUGUI`, tek Overlay canvas (sıra 40), kapasite 48, Numeric SDF + `FaceOutline`, yalnız yukarı ofset | tier boyutları §5 E18 | 24 px yükseliş 600 Settle, α son 200; pop 1,15→1 100 ms; Scaled saat; faz değişiminde 200 ms unscaled flush | dünya vuruş sesi (var) | outline ≥ 4,5:1 her dünya pikselinde | HUD |
| 31 | ★ HitMarker (S-02) / HitDirection | reticle çizgi ağırlığında; şekilli kademeler §5 E16; yay sprite'ı fırça kenarlı + oyuncu-yay 64 px | — | 40 ms in / 60 tut / 120 out; `Exit` sönümü | `HitMarker`/`KillMarker` (var) | × FlashScale | HUD |

---

## 5 · Ekran ekran tasarım

Ortak: her ekran `Expand` tuvalinde 1408 sayfaya tasarlanır, 1728'e sütun kesriyle büyür; 21:9'da içerik ortada, arka plan cover; Mac'te +167 px yükseklik üstten ankrajlı içeriğe akar. Odak: her pane/ekran son odağını hatırlar (enum-indeksli dizi); nav tekrar 400/100 ms, slider 400/80 ms; stick engage 0,50 / release 0,35; her koreografi herhangi bir girdiyle atlanır.

### E1 · Boot / ilk kare
- **Yerleşim:** `SurfaceBase` (Unity splash arka planı #1F1F20 → #12100F); merkezde mühür 240 px (reticle halkası + yaprak, Vermilion600 — ekranda CTA yok, bütçe tutar); altında wordmark Display 700 tracking +0,08 "DEATHS DEMOLITION" (canlı TMP, 0 kredi); kicker Label "PLAYABLE CONCEPT · PROTOTYPE".
- **Koreografi ≤ 1,6 s, her girdi atlar:** t0 mürekkep lekesi InkReveal 500 ms → t 0,55 mühür Stamp (temas t 0,64) → t 0,70 wordmark Clip wipe 280 → tut 600 → perde MainMenu'ye. Boot ve ilk yükleme tek nesnedir: MainMenu t0'da async yüklenir, mühür yükleme ekranının merkezi olur — el verme §15.3 (AD). Parlaklık asla > %20 sıçramaz (ink on ink).
- **Ses:** mürekkep yayılma 0,5 s → mühür `BootLogo` tok (çan kullanıcı §13); atlamada 120 ms fade.
- **RM:** statik kompozit 1,0 s, kes. **Sıklık:** §13.

### E2 · MainMenu (D/O)
- **IA değişmez:** sol omurga PLAY (72 h, Primary) · PROGRESS grubu (Profile/Statistics/Achievements) · SETTINGS grubu (Settings/Controls/Accessibility) · QUIT altta. Sekmeli IA reddedildi (gamepad'de bir odak seviyesi daha, yeni metin, kareler çalışıyor); LB/RB komşu pane'e atlar.
- **Yerleşim:** omurga 3 sütun (1408'de x 96, w 334), kicker y 48, Display başlık, Body alt başlık, fırça alt çizgisi (tek L2 ornament); PLAY y 232; build Caption omurgada. Pane 9 sütun (w 1050), y 232–984, Elev1 lake; başlık 64 (tik + H2), iç boşluk 24.
- **Arka plan:** `UiBackdrop` 3 katman (uzak: gök + sis + çatı/latis · orta: fener/veranda · yakın: bambu/yaprak — §6.1 ile aynı üç bant) (AD) motor içi yakalama; parallax 4/8/12 px fare/stick, Follow 0,20; yaprak = 1 tek-yaprak SDF glifi `Particles` modunda, shader'da döndürme/ölçekle 6 örnek (64², `AccentSakura` α 0,35–0,6, 12–22 px) (AD); yağmur α ≤ 0,06 prosedürel; fener nefesi 2,4 s emissive maske. Sol üçte bir `SurfaceBase` α 0,55 scrim kalır (metin-güvenli dikdörtgen T10). Frosted mip-bias reddedildi.
- **Koreografi:** giriş arka plan Fade 280 → başlık Clip 280 → nav satırları Row stagger 40 (α + 12 px) → pane Settle. Pane geçişi: çıkan α 1→0 + x −8 Exit 140; giren x +12→0 Base 220, 60 ms örtüşme; odak t=0'da taşınır. Ekran çıkışı (Selection'a): ExitScreen 140 → perde.
- **Ses:** `UiTransition` (+ su damlası kuyruğu menüde), `PaneOpened` (aynı pane sessiz), bed `AmbCommon` + `AmbFurin`.
- **Odak:** omurga ↕ (varsayılan PLAY); →/A pane'e girer; B/Esc omurgaya döner; pane açılışı odağı çalmaz.
- **21:9 / Mac:** 21:9'da arka plan yükseklik kırpar, içerik 1728'de ortalı; Mac'te pane 1247'ye uzar (listelere satır).
- **RM:** parallax/yağmur/yaprak statik, fener sabit.

### E3 · Profile (D/B/H)
- **Yerleşim:** iki sütun üstten. Sol 5 sütun **kimlik kartı** Elev1 (dinlenen kart yüzmez; Elev2 yalnız modal/hover/perde/tooltip — §1) (AD): hanko halkası 96 (mürekkep, `TextSecondary`; içinde seçili rozet glifi) (AD), oyuncu adı H2, unvan Label, LEVEL NumericXL + XP bar 16 (bevel), "TITLE ›" / "BADGE ›" liste satırları 56 → **Picker modal** (M, radyo liste; LOCKED = kilit glifi + koşul metni). Sağ 7 sütun: 3 stat satırı 40 · bölücü · UNLOCKED CONTENT (satır 40, çip metrik) · "2 of 27 achievements earned" · seviye merdiveni "you are here" işareti (SHOULD). "Another copy…" `Notice Error` iki sütunun üstünde otomatik yükseklik (klip H durumunda görülüyordu, frame 06).
- **B:** EmptyState "No runs yet" + PLAY eylemi.
- **Hareket:** XP bar Reveal 600 oturum başına ilk girişte; seviye Stamp; unvan/rozet seçimi → satırda hanko Stamp. **Ses:** kart girişi tek `StampLanded`, seçim `UiSelect`.
- **Odak:** kart satırları ↕ → sağ sütun; picker tuzaklı, iptal odağı iade.

### E4 · Statistics
- **Yerleşim:** 4 HeroTile üstte (Runs Played · Highest Round · Total Kills · Lifetime XP — hepsi mevcut veri) → altında iki 6-sütun kart: COMBAT (Standard/Special/Elite/Ultra/Armour Pieces Broken) · PROGRESS (Rounds Completed, Profile Level, Best Single Run XP); satır 40, Numeric tabular; `Stat.Trend` glifi COULD (A3'ten çıkarıldı, sheet 16 hücre) (AD). 12 çubuklu mini histogram COULD (koşu geçmişi verisi gerekli).
- **Hareket:** yalnız 4 kahraman sayaç CounterRoll ≤ 600; satırlar sayılmaz. **Ses:** pane başına tek `CounterRolling`.

### E5 · Achievements (D/K/B + sayfalayıcı)
- **Yerleşim:** 27 → 2 sütun × 4 satır = 8/sayfa (4 sayfa); karo 96 h: `UiMedal` 56 (27 glif A1/A2 içinde) + başlık BodyStrong + Caption + sağda çip; filtre çipleri ALL · EARNED · IN PROGRESS · LOCKED; pager Ghost + "Page n of 4" + LB/RB. Earned = lake inset üstünde tintli madalya (`StateSuccess` kontur); locked = silüet α 0,35 + kilit; in-progress = 4 px radyal halka + "2 / 5" Caption + mevcut 8 px bar; NEW = sakura pill + Stamp bir kez, sonra temizlenir. B: EmptyState "Achievements unlock at the end of a run".
- **Hareket:** sayfa geçişi yönlü pane koreografisi; NEW madalyalar Stamp stagger 120; barlar Reveal girişte. NEW pulse yok (pulse bütçesi 2). **Ses:** `PaneOpened(AchievementsPage)`; kilitli hover sessiz.

### E6 · Settings
- **Yerleşim:** bölümler small-caps + `Ornament.Divider`: DISPLAY (Display mode Stepper, UI scale Slider durak tikli) · AUDIO (Master Slider; **UI SOUNDS Slider kullanıcı onayıyla**) · GRAPHICS (Quality **Stepper** Low‥Ultra — ayrık değer slider değil) · DATA (Telemetry Switch). Satır 56; kontrol sütunu ≤ 720 ölçü; sağ 5 sütun önizleme kuyusu (1:2 HUD minyatürü seçili UI scale'de) SHOULD. Slider tutamacı 28 (v006 SHOULD-5).
- **Hareket/ses:** Switch/Stepper/Slider mikro-etkileşimleri (§4); slider pitch haritası.
- **Odak:** ↕ satır, ↔ ayar; B omurgaya.

### E7 · Controls (D/dinleme/override)
- **Yerleşim:** iki sütun **ortak satır ızgarası** 48 pitch (v006 SHOULD-6 kapanır); `UiBindingRow` vektör tuş kapağı/disk art 32 px (K6; canlı metin kalır); STICK DEAD ZONE Slider, AIM ASSIST Stepper (+ FULL'un ne yaptığı için bir cümle, S-02 sözcükleri, SHOULD), RUMBLE Switch; RESET ALL BINDINGS = **Hold** buton 500 ms, Secondary 440 w; "Overrides: input-overrides.json" Caption.
- **Dinleme:** yuva `StateWarning` kontur (odak halkası yerine — Warning↔Focus ΔL* 4,5) + yuva altında 1 px Linear timeout çizgisi (Data); yakalanan glif Stamp; çakışma → Notice Warning satır altı; iptal `Back`. Cihaz illüstrasyonu COULD.
- **Ses:** dinleme `UiDialogOpen`, yakalama `Confirm`, şema glif geçişi sessiz, tutma tık + 500 ms fırça sürtme → `Confirm`/erken bırakma `Back`.

### E8 · Accessibility
- **Yerleşim:** sol 7 sütun: PALETTE Stepper (Default/Colour-blind safe), TEXT SCALE Slider (durak 100/110/125), REDUCE FLASHES Switch, REDUCED MOTION Switch; sağ 5 sütun **canlı örnek kuyusu**: 4 durum çipi seçili palette (HEALTH · EARNED · WARNING · DANGER — Warning artık #F7C766), Caption örneği, segmentli can çubuğu düşükte, hareket örneği (ghost'lu bar + Stamp + glint mevcut FlashScale'de), gerçek hasar kenarı + final saniye bandı statik önizlemesi (GD P17). Kuyu **yerel** RM/Flash override ile aynı preset'leri çalar — ayar uygulanmadan görülür.
- **Metin:** mevcut cümleler aynen ("Pulses hold still, screen fades are instant and the menu petals stop drifting." kalır ve v3 buna uyar).

### E9 · Selection (D/K/Y)
- **Yerleşim:** üç 4-sütun kolon (1408'de 453 / 1728'de 560), y 200–920, altbilgi 88. Sol CHARACTER SKIN: 3 kart 120 h (portre 88 + renk şeridi + ad + "Cosmetic only"). Orta **sahne** Elev1 453×720: seçili portre motor içi Look B render (`UiViewport` RT ≤ 512×768, açılışta bir kez; README 5 kapanır; −0,77 MB PNG), ad plakası, seçili silah glifi, loadout StatStrip; canlı turntable SHOULD (aynı dikdörtgen, S-06, yalnız `EnsureUiCamera` düzeltmesi + Selection yükleme ≤ 0,5 s ölçüldükten sonra; stick 1:1, bırakma Reveal Move, boşta 4°/s 3 s sonra, RM statik ¾ poz). Sağ CLASSIC WEAPON: 3 kart 200 h: ikon 56 (AP-013) + H3 + **StatStrip 2 satır** (tek satır stat dizisi 1,25'te Mac'te taşıyordu) + **CompareBars** 4×6 px + CORE dal glifleri 26 (outer-only ikizler) + SELECTED/LOCKED çipi.
- **Durumlar:** locked = disabled + kilit + "Reach profile level 3 · you are level 1" + 120 px XP mini-bar (asla 4,5:1 altına solmaz); loading = sahnede skeleton + "LOADING"; hover/odak = Elev2 kaldırma; selected = Elev1 + `SurfaceRaised` + krem kontur + çip (dinlenen seçili kart yüzmez) (AD); LOCKED'a basma → çip DeniedNudge (kart değil, metin okunur kalır) + `Denied` 0,4.
- **Koreografi:** kartlar Row stagger; seçimde krem kontur + çip (fırça alt çizgisi ekran başına bir, o da H1 altında — L2 kuralı) (AD); çubuklar Base Settle odakta; START RUN → ExitScreen → perde (caption loadout'u yankılar: "Ivory / Crimson · Assault Rifle").
- **Odak:** skinler ↕ → ↔ → silahlar ↕ → ↓ START RUN (varsayılan mevcut silah); B = BACK.
- **Ses:** `UiSelect`, hover derinliği sessiz, turntable sessiz.

### HUD genel (E10–E23)
**Yuva haritası v2 (1080p; köşeler gerçek kenarda):** gold 220×48 @ (32,32) · round 360×150 üst-merkez · tehdit satırı round altında (y 168), çipler içerik-boyutlu min 208 / maks 240, boşluk 8, **maks 3, sabit yuva sırası** SURROUNDED · BEHIND YOU · ELITE/ULTRA NEAR (satır 736 ≤ 1408) · afiş H1/Banner 48 y 260 · kenar işaretçisi + ad plakası sağ üst · health 420×100 sol alt · **Special 220×216 alt-merkez (kalır; kod `RunHudScreen.cs:477` ankrajı, oyuncuya 200 px)** · countdown/prompt 380×56 alt-merkez, **Special görünürken üstüne yanaşır (boşluk 8)** · weapon 420×150 sağ alt · hasar kenarları 150 px. Toast yuvası yok. **Diegetik karar:** köşe panelleri non-diegetik, opak `HudInk`, Elev0 (DEC-077); yarı-diegetik katman dünya nesnesine bağlı: reticle, oyuncu-yayı, gövde üstü durum damgaları, hasar sayıları, dünya işaretçisi; **oyuncu modeli üstünde hiçbir şey**.

**GD okuma modeli:** T0 (≤ 150 ms; oyuncunun 120 px çevresi veya çevresel büyük şekil): hasar yönü (kenar + oyuncu-yayı), düşük can, reticle durumu, düşman durumu, Special final 3 s. T1 (bir sakkad): can sayısı, mermi/reload, tehdit çipleri. T2 (çatışma arası): round/kalan, gold, build, kullanım, ad plakası. T0 tam `FlashScale`; T1 hareket ≤ 8 px; T2 ≤ 4 px veya yalnız alfa.

**Savaş HUD hareket sözleşmesi (ID P6 + düzeltmeler):**
1. Merkezi %60×%60 oyun bandında yalnız şunlar hareket eder: reticle (yayılım = gerçek `CurrentSpreadDegrees`, seyahat ≤ 6 px), dünya hasar sayıları, düşman durum damgaları (projeksiyon-bağlı), **oyuncu-bağlı yaylar** (durum-bağlı), prompt'lar (alt), afiş (üst üçte bir, olay başına bir kez).
2. HUD panelleri: translate ≤ 8 px, scale ≤ 1,12; **yeni HUD döngüsü sıfır** (kalp atışı, regen dalgası, nefes alan segment, per-shot mermi dip — hepsi geri çekildi); sürekli hareket yalnız mevcut iki pulse.
3. Önce gerçek: sayı/fill hedefi modelin değiştiği karede güncellenir; hareket takip eder, asla öncelemez.
4. Tehdit çipleri gösterim histerezisi: min 800 ms açık, 300 ms kapalı; sabit yuvalar (yeniden akış yok).
5. ≤ 4 eşzamanlı HUD tween, 0 ayırma; projeksiyon-bağlı şeyler (işaretçi, çip, reticle) doğrudan yazılır, eased lag yok (Data, T6 izin listesi).
6. Flash olan her tepe × `FlashScale`; tekrar eden değişim < 3 Hz; FlashGate.

**GD geri bildirim hiyerarşisi (AD düzeltmesiyle: kill = krem X, kademeler şekil-ayrık; amber yok — `StateWarning` oyuncuya "uyarı" der, olumlu vuruş kademesine ikinci anlam veremez, kısıt 2):** (AD)

| olay | işaret (S-02) | sayı (S-07) | stop / shake | HUD | S-08 |
|---|---|---|---|---|---|
| et | 4 kısa çizgi 170 ms, krem | WorldNumber M 22 krem | 0 | — | tik |
| zırh tuttu | 4 düz plaka (şekil), çelik | S 18 gri | 0 | — | clank |
| zırh kırıldı | plaka + patlama (şekil) | M 22 krem + mevcut zırh glifi (AD) | `ArmorBreak` shake | — | crack |
| kritik | 4 uzun çizgi, krem (AD) | L 26 krem + konturlu starburst (şekil) (AD) | 0 | — | — |
| kill | **krem X** (şekil + 1,4× taşır) | son sayı bold | 0 | — | — |
| elite kill | X 1,4×, 240 ms | XL 30 + tier glifi | REQ-127 hit-stop, `HeavyDeath` shake | 0,6 s "ELITE DOWN" damgası tehdit satırı altında (SHOULD) | `HudEliteDown` lake tok + kısa koyu çan (stinger değil, öncelik 60, duck yok) |
| ultra kill | + gövde üstü mürekkep damgası 0,8 s | XL 30 + glif | aynı | "ULTRA DOWN" | `HudUltraDown` + varak parıltı |

100 ms başına bir T0 efekt (`FeedbackBudget`); işaret `MinInterval` 35 ms ve sayı birleştirme kalır.

### E10 · Can / regen
- `UiBar` v3 20 h: 10 segment + %30 güçlü tik (`LowHealthFraction` kural değeri değişmez, aralık okunur olur); hasar → ghost (Cream300 α 0,5, luminance-only) tut 350 boşal 400, yeni vuruş anında yakalar, yükselirken fill önde/ghost gizli; regen → `SecondsUntilRegen` 4 px track Linear (Data) + "recovery in 4.5s" metni kalır; fill edge'de statik krem kapak. Düşük (< 0,30): renk swap anında + çatlak kalp Stamp bir kez + "LOW" kelimesi + koral hairline + mevcut kenar tabanı; **kalp pulse yok**. Sayı ≤ 120 ms tik, vuruşta snap. Çerçeve 2 px (hiyerarşi: can > round > gold). Ses: `HudLowHealth` one-shot, döngü yok; çubuk sessiz.

### E11 · Mermi / reload
- Panel anatomisi düzeltilir (§10): ad → build çip satırı ("P1 / FR1 / RD1" çipler) → tür Caption **kendi 24 px bandında**. Şarjör NumericXL 44 tabular + "/ 30" NumericM `TextSecondary` aynı taban çizgisi (Fraction). Tik merdiveni: tek `Image.Filled Horizontal` 4×12 px tik sprite üstünde, `MagazineSize` tik (> 40 → yalnız sayı); pellet 8 px pipler. ≤ %20: "LOW" caption `StateWarning` #F7C766 (kelime + şekil; altınla çakışmaz). Boş: "0" + EMPTY çipi (#F7C766) + `UiPrompt` RELOAD glifi; **shake yok**. Reload = 64 px `UiRing` silah glifi çevresinde Focus400 (varak yok) + mevcut bar, Data Linear; LOW durumunda halka + bar `StateWarning`'e döner ki Focus400 ile #F7C766 aynı panelde iki anlam taşımasın (§2.1 ayrım notu) (AD); tamamlanınca sayı Stamp Base, bar Exit Fast. Per-shot dip yok (8,5 Hz). Kuru tetik: merdiven 4 px kick Instant (RM kapalı). Ses: `HudMagazineEmpty` var; reload halkası/basamak sessiz; `HudAmmoLow` COULD.

### E12 · Gold
- `UiCounter.Currency`: değer Base Settle ile tırmanır (Δ 0,2 s içinde birleşir, retarget); "+N" Delta çipi Gold400 16 px yükselir 0,5 s; kinpaku glint pickup burst başına ≤ 1 / 1 s, × FlashScale, flaş azaltmada kapalı; harcama `TextSecondary` ile Base Exit sayar. Altın çipi altında kinpaku karo α 0,35 (L0 istisnası). HUD'da satın alınabilirlik ipucu yok (teklif round sonuna kadar yok). Ses: `GoldPickup`/`GoldMagnet` orb başına kalır; sayaç yalnız Δ ≥ 10'da rulo + varak kuyruğu (`MinRetrigger` 0,4).

### E13 · Round / tehdit
- Round bloğu: ROUND Label + NumericXL + kalan satırı; round basamağı Stamp 200; mid-round breakdown `TextMuted` (v005 SHOULD-8). Afiş: Banner 48 Display + InkStroke 1,5 px; **metin ≤ 220 ms okunur** (Clip wipe 220 Settle) → fırça arka planı Brush 400 arkadan → tut 1,9 s → Exit 280 (α + y −8); toplam 2,4 s (`BannerSeconds`, S-08 stinger kırpması aynı kare). "NEXT ROUND IN 3": `CountdownTick`'te 1,0→1,06 (metin üstü tavan) 140 ms; son 3 s uyarı rengi **yok** (geri sayım zaten 3 s). Havuz bitti: kalan sayı `StateSuccess`. Tehdit çipleri: y −6→0 + α Fast Settle giriş, Exit Fast çıkış, sabit yuva, histerezis 800/300, ELITE/ULTRA NEAR tier glifi outer-only; BEHIND YOU caret'i şekil. **Arka yay (SD, SHOULD):** "BEHIND YOU" boolean'ı 360° tarama gerektiriyor → oyuncu ekran konumunda 60° yay, yarıçap ≥ 72 px (figür dışı), 4 px çizgi, Coral400 α ≤ 0,6, çipin ömrü boyunca, saldırgana bakar; `ThreatDirector.RearDirection` (S-03, yalnız veri). Windup dünya telegraph'ında kalır. Ses: `RoundStart`/`RoundComplete` (sahip Run), `HudThreat{Encircled,Rear,Strong}` girişte, çıkış sessiz.

### E14 · Special
- Halka 132 `UiRing` `GoldLeaf` dolumu (Data Linear; aktifken 11 tik = 1/s, "bir çatışma daha?" mekânsal okunur); final 3 s halka Coral400 + mevcut veil (2,2 Hz bandı değişmez, × FlashScale); pickup: halka 0→1 320 ms Settle + tek kinpaku glint + hazır kıvılcımı (12 shader quad ≤ 0,3 s, × FlashScale; RM kapalı). "<SW> ONLINE" afişi E13 zarfıyla (metin ≤ 220 ms; "SPECIAL WEAPON ON THE FIELD" 22 s kararı açar) + glif Stamp; kullanım satırı (FIELD 4s · 2 HIT) değişmez, HIT sayısı pop 1,06 / 120 ms; bitince klasik blok Base'de un-dim. Pip blink ve dünya beacon'ı paylaşılan aciliyet saatinde (`GroundUrgencyStart` 0,35, External clock). Dünya işaretçisi halkası 56→40 px 10→40 m. Ses: `SwPickup` (sahip Combat), ek ses yok; halka sessiz; `SwFinalSeconds` 1 Hz kilitli.

### E15 · Reticle (S-02 davranış, S-09 görsel)
- Sprite seti `Reticle.Ring/Ticks/Spread×2/Hit` 128², çizgi 2,5 px @1080, krem 1,5 px + Sumi α 0,6 kontur. **Dürüst reticle:** halka yarıçapı / tik boşluğu = projeksiyonlu `CurrentSpreadDegrees` (S-02 `ClassicWeaponHudState`'e ekler, yalnız veri) nişan mesafesinde (1,6° @ 20 m ≈ 0,56 m → tipik 11–27 px), Follow 0,05; tikler 48 px üstünde ayrılır (doygunluk şekli); **konserve kick yok**; pellet silahı `RingRadiusShare` ikinci halka. Reload: halka `Filled Radial360` `ReloadProgress01`, tikler α 0,4 (göz nişanda kalır). Boş: tikler 6 px dışa ayrılır (şekil). Special aktif: Gold400 (rezerv = Special). Reticle asla flaşlamaz; isabet `HitMarkerLayer`'da. RM: bloom 3 adıma kuantize. Sessiz.

### E16 · İsabet işareti (S-02)
- Tablo §5 HUD genel; reticle ile aynı çizgi; 40/60/120 ms; yeniden tetik yeniden başlatır (cap 1); kill Stamp 1,2→1 (HUD dışı, projeksiyon); krem, altın/kırmızı yok. Ses var, değişmez.

### E17 · Hasar yönü
- Kenarlar kalır (150 px, tut 60 / sönüm 420, `DamageEdge` Vermilion600 tepe 0,92 × FlashScale, sönüm Exit); yay sprite'ı `Arc.Soft` 256×64 fırça kenarlı; + oyuncu-yayı 64 px aynı 0,48 s (GD; 40 düşmanda göz oyuncuda). Flash sınıfı; RM: yalnız alfa, genişleme yok. Ses `PlayerDamage` sahip.

### E18 · Hasar sayıları (S-07)
- Dünya `TextMesh` → havuzlu `TextMeshProUGUI` tek Overlay canvas (sıra 40, kapasite 48, cache'li string, **1 batch**), Oswald `FaceOutline` (1 px InkStroke + 2 px gölge; bot-upgrade'deki gri konturlu olmayan "22" kapanır — v005 SHOULD-4). Boyutlar WorldNumber S18/M22/L26/XL30 kademeye göre; kritik = L + konturlu starburst (şekil). Yalnız yukarı ofset (yanal ofset komşu gövdeye yanlış atfeder), DEC-077 oyuncudan uzak clamp. Hareket: 24 px yükseliş 600 Settle, α son 200, pop 1,15→1 100 ms, Scaled saat; **`PhaseChanged` → RoundComplete/Dead'de 200 ms unscaled fade ile flush** (bot-upgrade'de ROUND 1 COMPLETE boyunca donmuş üç "22"). Ekranda ≤ 12 görünür. RM: 500 ms dinlenmede belirir, fade. Sessiz (dünya vuruşu).

### E19 · Düşman durum çipleri
- `StatusStamp`: NET = ofuda etiketi şekli, DAZED = spiral, PULLED rezerv (`Status.Pulled`, WP-055 açığa çıkarırsa); 24 px outer-only glif + kelime, 88×24 → metrik yükseklik; Stamp Fast giriş (1,12→1), Exit Fast; projeksiyon takip; Scaled saat; efekt kalan süresi açıksa 2 px alt çizgi boşalır, yoksa yok (süre uydurulmaz); havuz 16 en yakın. Ses `ZombieReact*` var. Bot enemy-status karesi r8'de alınır (T-kapsama).

### E20 · Ad plakası / unvan
- **Değişmez** (yalnız yüz değişimi + `Title` glifi 16 px); statik kimlik metni; HUD stagger'ıyla girer; sessiz.

### E21 · Dünya işaretleri
- Tek `Marker.Edge` silüeti (AP-010 sprite) beacon VFX (S-07) ve UI kenar oku için; UI oku altın→kor aciliyet lerp'i (beacon tonu almaz, §9 #A); `EdgeMarker` 44 disk + 28 glif + mesafe Caption; projeksiyon-bağlı, Follow yok; G-21 ritmi tek saat. Ses `SwBeaconLoop` var.

### E22 · Ölüm overlay → Results
- t0: HUD `DeathDimFloor`'a söner (External `DeathOverlayT`), **can bloğu 0,6 s α 1,0 tutar ve "DOWN · killed by <KilledBy>" gösterir** (öğrenme anı; `KilledBy` mevcut) · t 0,6→2,0: köşelerden mürekkep sızması InkBleed (Brush, S-07 `_DD_Ink` maskesi) = perdenin kapanışı · `DeathResultsDelaySeconds` 2,4 değişmez · Results aynı mürekkepten açılır, arka planı donmuş ölüm karesi (SHOULD). WP-061J iptal yolu korunur (mürekkep geri çekilir, perde açılır). Ses: `StingerDeath`/`PlayerDeath` t0; mürekkep dokusu (`AmbPondBed` LPF türevi, −26 LUFS) 0,6–2,0 s; Results girişi perde açılış karesinde. RM: dim anında, statik, 2,4 s'de kes.

### E23 · Prompt'lar
- v005 MUST-4 durur; 44 h; α + 4 px Fast in / Exit Fast out; glif crossfade; Special görünürken countdown ile birlikte Special üstüne yanaşır. Sessiz.

### E24 · Upgrade dükkânı (D/B/H/K; DEC-087 2 seçim)
- **Yerleşim:** donmuş kare blur + scrim 0,86 altta; gold şeridi 128 üstte (sikke 32 + NumericXL + **2 mühür yuvası** = picks: boş halka → satın almada hanko Stamp; "1 of 2 picks" cümlesi aynen). Kartlar `UiShopCard` 453×480 (1728'de 560), oluk 24, y 176; açıklama 5×40 (200 karakter 1,25'te sığar); **önce→sonra satırı** modifier tablosundan ("RELOAD 1.5 s → 1.3 s"; tek sayısı olmayan dal katalog cümlesini korur; yer kartta varsayılan / tooltip — §13); tier pipleri krem (altın değil); BUY Primary inset rafta (vermilion'un karttaki tek 3:1 yeri). Satır y 680: REROLL Secondary 220 (**Hold 500 ms, kullanıcı**; `Shop.Reroll` glifi + "1 left"; sonra disabled + "USED") · picks yuvaları · geri sayım 380×56 (v005 MUST-3) · CONTINUE 320×56 sağda. B/H: mevcut 760×260 panel → EmptyState / Notice Error.
- **Koreografi:** scrim Fast → kartlar CardDeal (translate + α yalnız; rotasyon yok — TMP Mac 1,8× tuvalde aliasing) stagger 60; **satın alma:** basma Instant → PURCHASED çipi Stamp + kinpaku mühür (rezerv: para) → kart `SurfaceRaised` Fast → fiyat/şerit Base Exit sayar → tier pip Stamp Instant; odak → **sonraki satın alınabilir kart; limitte CONTINUE**. **Reroll:** yalnız değişen kart(lar) CardWipe (Clip Exit 140 → Settle 220); kalan kart yerinde durur (DEC-072: 1–2 değişir, asla 3); RM crossfade 0. **Kilit** (OFFER LOCKED): kalan kartlar α `OpacityDisabled` Base + çip. Alamıyor: çip DeniedNudge + `Denied` 0,4 + notice; "Leaves you N gold" kalır. Deal sırasında tık dağıtımı tamamlar sonra tıkı işler.
- **Ses:** `CardDealt` ×3 60 ms basamak pitch +0/+2/+4 % + son kart yerleşmesinde tok; `UpgradePurchase` + `StampLanded` + rulo; `CardFlipped` başına 1 hışırtı; `UiReroll`/`UiLock` var; `CountdownTick` son 3 s pitch 1,00/1,06/1,12; `RerollHoldStarted`.
- **Odak:** kart1 BUY → kart2 → kart3 → REROLL → CONTINUE.

### E25 · Pause + onay
- `UiModal L` 680×520 Elev2 opak: PAUSED H1, Body (mevcut cümle), CAMERA SHAKE Slider 28, nav satırları 56 RESUME (varsayılan) · MAIN MENU; okunur "run so far" şeridi (round · kills · gold · build · picks used — HUD/Results üreticileri, GD P15). Arkada donmuş kare blur (¼ dual Kawase + mürekkep yıkama, bir kez, sıra 90) + scrim; HUD 0,5 α. Scrim Fast (pause anında hissedilmeli), blur scrim ile t=0'da `Canvas.enabled`, panel 0,98→1 + 16 px Base; kapanış Exit Fast; RESUME t=0'da çalışır.
- **END RUN onayı** = Modal S 560×264: H2 "END RUN?", Body (mevcut), CANCEL Secondary sol (**varsayılan odak**), END RUN Danger sağ; tuzaklı; Esc/B önce diyaloğu sonra Pause'u kapatır; iptal odağı iade. Hold yok.
- **Ses:** pause aç = perde-kapanış kısa varyantı + snapshot Pause, resume = açılış; `UiDialogOpen`.

### E26 · Results (D/B)
- **Yerleşim:** kahraman bandı y 48–200 (kicker, H1 "DEFEATED IN ROUND 7", alt başlık) · 4 HeroTile (Round · Kills · Gold · Time) y 224 · sol 7 sütun RUN REPORT (satır 40, silah + upgrade glif şeridi 40) · sağ 5 sütun REWARDS (XP bar Reveal 600 önce→sonra — önce değeri açık değilse dolum animasyonu yok; LEVEL UP çipi Stamp; NEWLY UNLOCKED `UiRewardCard` 92 h; başarımlar `UiMedal`; "next unlock: Combat Shotgun at level 5 · 2 levels away" (`UnlockLevelFor`)) · altbilgi 88 MAIN MENU Primary sağ. Sütunlar içeriğe göre, boş 460 px kart yok. NEW BEST = sakura şerit + `Ornament.Divider` + ginpaku glint (AP-012 sorun 1 kapanır). Boş ödül: EmptyState "No new unlocks this run".
- **Koreografi ≤ 2,2 s, atlanabilir (ilk A/tık atlar, ikinci MAIN MENU):** perde açılır → başlık Screen Settle → sütunlar Row stagger → kahraman sayaçlar 120 ms arayla CounterRoll → XP Reveal + LevelUp Stamp → unlock damgaları 120 ms → başarımlar → **NEW BEST son**, en büyük hareket. Her beat mevcut `UiEvent`'ini **cue** olarak raise eder (`Start()` yığını biter — §10).
- **Ses:** grup başına tek rulo; damga ≤ 110 ms (binmez); `UiUnlock`/`UiAchievement`; `UiNewBest` son damgadan ≥ 250 ms sonra; tepe ≤ −3 dBTP; `StingerRunEnd` altta. RM: dinlenmede, NEW BEST yine görünür.

### E27 · Toast — **koşu içinde YOK**
- Kare-doğru şeritler afiş yuvasında: `HudNewBestRound` (`RoundStarted`'da round > profil en iyisi; koşu başına bir kez; 2,4 s; `RearAttack`/`Encircled` sürerken ≤ 3 s ertelenir; hasar kenarından ≥ 0,5 s uzak; FIFO derinlik 2, aynı sınıf değiştirir) — **kullanıcı §13**; ELITE/ULTRA DOWN 0,6 s damga SHOULD. Menü/Results toast bileşeni COULD (üretici yok).

### E28 · Tooltip (SHOULD)
- `UiTooltip` yalnız karar ekranlarında (Selection stat/dal, Shop dal/tier, Achievements ölçüt, Profile unlock); içerik = katalog cümlesi + arkasındaki sayılar; kural cümlelerinin yerini asla almaz (kısıt 1). Fare 350 ms / pad odak 500 ms (nav tekrar 100 ms'de açılıp kapanmasın), odak taşınınca 0 ms; ankrajın sağı/üstü, 80 px içinde çevirir; boyut bir kez ölçülür. Sessiz.

### E29 · Yükleme / sahne geçişi — perde (C4) — **§15 bu satırın üstüne yazar (async yükleme ekranı, 24.09 kullanıcı kapsamı); aşağıdaki senkron tarif yalnız tarihçe** (AD)
- `UiCurtain`: kapan 280 Move → `yield return null` ×2 (batchmode'da `WaitForEndOfFrame` dönmez) → senkron `LoadScene` → yeni sahnenin ilk render karesinde aç 280 Settle. Kapalı kart: washi, mühür 96 + "LOADING" Label + **mevcut bir kural cümlesi** (dükkân girişi / "Skins are cosmetic" / "Profile progress unlocks content only…"), asla strateji ipucu; ilerleme çubuğu/halka/spinner **yok**; ipucu-plaka yok. Eklenen gecikme ≈ 0,56 s (bugünkü karartmanın yerine geçer). WP-061J iptal → kapı açılır. RM: kes → ≥ 300 ms → kes. Ses `UiCurtainClose` `LoadScene`'den ≥ 1 kare önce, `UiCurtainOpen` ilk karede; yükleme döngüsü/tık yok. Async yükleme + gerçek ilerleme = COULD, sahip UI dışı (S-01/S-05).

### E30 · İmleç
- `Cursor.Arrow` uygulanır: donanım imleci 32 px (hotspot 4,3) + 64 px Retina, RGBA32 (≈ 40 KB); Arrow / Press (12° eğik, dolu); hover halkası Focus400 varyantı (altın değil). Menü/Pause/Upgrade gösterir; Run gizler (reticle nişandır); `InputSourceRegistry` gamepad bildirince gizli, fare hareketinde döner; `ForceSoftware` asla. Sessiz.

### E31 · Debug paneli — kapsam dışı (oyuncu yüzü değil).

---

## 6 · Büyük öneriler

**6.1 Menü/Selection arka planı — karar: motor içi Look B plaka yığını, parallax, canlı 3B yok.** S-05, World_AP005'te Look B altında sabit `MenuPlateCamera`'dan (AD + S-05 konum seçer: veranda'dan avlu, fenerler yanık, yağmur sonrası) 2560×1440 yakalar; üç derinlik bandı uzak (gök + çatı, DXT1 crunched, mip), orta (fener/veranda, alfa, DXT5 crunched), yakın (bambu/yaprak, alfa); sanat güvenli bölge merkez 2217×1080; metin-güvenli dikdörtgen sol üçte bir (T10, `TextSecondary` ≥ 4,5:1 scrim ile). Parallax 4/8/12 px Follow 0,20 (`InputSourceRegistry` üzerinden; UI InputSystem okumaz); yaprak/yağmur `Particles` modu; fener nefesi 2,4 s. Selection aynı plakaları sağa kaydırır (kartlar karanlık fener tarafına). **Maliyet:** +2,0 MB (−0,58 eski plaka) = +1,4 net; GPU ≤ 0,4 ms native Mac; 0 kr (+3 kr Photoshop paint-over rezervi); RM statik. Dal (b): yalnız uzak katman 1536×864 DXT1 crunched ≈ 0,4 MB. Canlı 3B: çift dünya yüklemesi, Look B/sis yeniden doğrulama, bellek — reddedildi; video 10–20 MB/döngü — reddedildi.

**6.2 Boot / logo.** §5 E1: ≤ 1,6 s, mühür vektör (0 kr), wordmark canlı TMP, mürekkep lekesi paylaşılan `_DD_Ink`. Sıklık ve çan kullanıcı.

**6.3 Perde → yükleme ekranı (24.09).** Kullanıcı kapsamı "oyunda yükleme olacaksa bir yükleme ekranı tasarımı yapmak gerekir" ile senkron perde yerini **async yükleme ekranına** bıraktı: `UiCurtain` iki derinlikli tek bileşen (CURTAIN = yalnız kapılar; SCREEN = kapılar + plaka + dürüst ensō halkası + U9 ipucu), seçim ölçülen son yükleme süresinden; ölümde mürekkep sızması yine perdenin kendisidir (SCREEN · ink variant). Geçiş haritası, akış, görsel, metin, testler ve sahipler **§15** (AD).

**6.4 Donmuş kare blur.** `UiFrozenFrameFeature` (URP renderer feature, AfterRenderingPostProcessing) yalnız istek karesinde pass sıraya koyar: ¼ çözünürlük dual Kawase + mürekkep yıkama → kalıcı RT (0,5–1,5 MB RAM, 0 disk); ≈ 0,3 ms bir kez, sonra 0/kare; `RunFlowClock` tutması dünyayı dondurduğu için geçerli. Sıra 90'da çizilir, HUD 0,5'e söner; ölümde yakalanan kare Results arka planı olur (`UiFlowRunner` tutar). Metin blur üstüne oturmaz; modaller opak; scrim kalır.

---

## 7 · Varlıklar (tekilleştirilmiş)

Kredi birimi Higgsfield (1:1 şeffaf 2K sheet = 3 kr; Seed Audio render ≈ 3 kr). MB = release build'de.

### 7.1 Görsel (sahip GR üretim, AD inceleme; S-09 içe alma; mürekkep kiti S-07 ile paylaşımlı)

| # | kalem | biçim | MB | kr | öncelik |
|---|---|---|---|---|---|
| F1 | 3 statik SDF yüz + 2 preset/yüz + fallback guard + basamak eşitleyici + caps kerning çiftleri | Alpha8 1024×512 / 1024×512 / 512² | +1,25 | 0 | MUST |
| A1 | Achievements 1/2 (16 glif) | 2K sheet → SDF atlas | (A1–A3 birlikte) | 3 | MUST |
| A2 | Achievements 2/2 (11) + Title + Badge (mon roundel ×1) + Tier.Special + Shop.Purchased + Shop.MaxTier = 16 hücre (önceki liste 20 hücreydi, 4×4 sheet'e sığmaz) (AD) | sheet | | 3 | MUST |
| A3 | Outer-only ikizler ×6 (Elite/Ultra/Netted/Dazed/Pulled/Behind) + Shop.Locked + Pip.Full/Empty + Toast.Record (şerit glifi) + Marker.Edge + Reticle.Spread×2/Hit (Ring/Ticks mevcut AP-012'den SDF'e taşınır) + Special.Ready + 1 tek-yaprak = 16 hücre; Stat.Trend COULD (AD) | sheet | | 3 | MUST |
| G1 | SDF glif atlası 1024² BC4 (mevcut 54: 32 ikon + 16 durum + AP013 3 + AP014 3; + A1–A3 48 + mühür), hücre 64² SDF (yayılım 6 px; 18–128 px gösterimde keskin) → 256 yuva — 128² hücre 64 yuva verir, sığmazdı; imleç RGBA32 donanım dokusu olarak V1'de kalır (AD); `build_atlas.py` v2 mesafe dönüşümü; **RGBA icon/status/AP013/AP014 atlaslarının yerine** | BC4 | +0,5 / −1,34 | 0 | MUST |
| A4 | Frames v3 (HudPanel 2×, Card, Modal çift çizgi, Toast→Ribbon, Tooltip, ListRow, Chip, Slider ray/tutamaç, ProgressRing, Bar.Segments, Shadow.Soft 9-slice) 2× yazılmış, `pixelsPerUnitMultiplier` 2; **AP012_frames yerine** | 1024² BC4 | ±0 | 3 | MUST |
| A8 | Mürekkep kiti (paylaşımlı: 2 fırça, 1 hanko, bölücü, leke/sızma zaman haritaları; her darbe tek kanal, 1,0 = boş) | 1024² BC4 | +0,5 | 3 | MUST |
| A6 | Karo seti washi 256² / kumiko 256² / kinpaku 128² | BC4 | +0,1 | 0 (Photoshop) · 3 rezerv | MUST |
| V1 | Tuş kapağı/disk 9-slice + 26 giriş şekli + imleç 32/64 | vektör, SDF/RGBA32 | +0,06 | 0 | MUST |
| V2 | Mühür (reticle halkası + yaprak) | vektör | 0 | 0 (sheet 3 SHOULD) | MUST |
| P1 | Menü plakası uzak katman, motor içi Look B | 2560×1440 DXT1 crunched (dal b: 1536×864 ≈ 0,4) | +0,7 / −0,58 | 0 | MUST |
| P2 | Orta + yakın katmanlar | DXT5 crunched | +1,3 | 0 (+3 paint-over rezervi) | MUST dal (a) / ilk kesilen dal (b) |
| P3 | Portreler açılışta motor içi render | RT, disk yok | −0,77 | 0 | MUST |
| S1 | `DD/UI/Fx` shader + `UiFrozenFrameFeature` | — | +0,1 | 0 | MUST |
| M1 | Medals v3 256² (halka çizgisi ≥ %6) | sheet | 0 (G1 içinde) | 3 | SHOULD |
| R | Yeniden deneme rezervi (4'te 1 sheet 24 px maske testini geçmez) | — | — | 4 | MUST |
| — | Yükleme sumi-e plakası | BC4 | +0,29 | 1,5 | COULD (kart < 0,6 s görünür) |
| — | Cihaz illüstrasyonu, mon kabartma ornament, PS/Nintendo şekilleri | — | — | — | COULD |

Görsel MUST: 9 + 3 + 3 + 4 = **19 kr**; rezervler (karo 3, paint-over 3) + SHOULD (medals 3, mühür sheet 3) → **tavan 32, sert 35**. Görsel MB MUST dal (a): 1,25 + 0,5 − 1,34 + 0 + 0,5 + 0,1 + 0,06 + 0,7 − 0,58 + 1,3 − 0,77 + 0,1 = **+1,82 MB**.

### 7.2 Ses (S-08; Seed Audio; Vorbis q0.8 mono ≈ 7 KB/s)

| # | kaynak | türetilen | render | kr | öncelik |
|---|---|---|---|---|---|
| R1 | rin kâse çanı + suzu | UiBell{S,M}, BootLogo, Rare/Epic katmanı, UiNewBest v2, HudEliteDown koyu çan | 1 | 3 | MUST |
| R2 | washi kayma/açılma | UiPaper{Slide,Unfold}, CardDealt, PaneOpen v2, CardFlipped hışırtı, hold sürtme | 1 | 3 | MUST |
| R3 | lake kapak + go taşı + hyōshigi | UiTok{Light,Mid}, PressDown, Confirm/Select v2, CounterSettled, HudEliteDown tok | 1 | 3 | MUST |
| R4 | hanko bas/kaldır | StampLanded, satın alma katmanı, profil mührü | 1 | 3 | MUST |
| R5 | fusuma rayda kayma + durak | UiCurtainClose/Open, pause aç/kapa, ScreenExiting katmanı | 1 | 3 | MUST |
| R7 | soroban boncuk | UiCounterRoll (loop) | 1 | 3 | MUST |
| R6 | varak/ince metal parıltı | UiGoldShimmer, HudUltraDown | türev önce; 1 | 3 | SHOULD |
| R1b/R3b | ikinci çan / ikinci ahşap | varyantlar | 2 | 3 | SHOULD |
| — | su/mürekkep | `AmbPondBed`/`AmbDrip` türevi | 0 | 0 | MUST (türev) |

Ses MUST **18 kr**, SHOULD +6 → **tavan 24, sert 30**. Disk ≈ 52 dosya ≈ 20 s → **+0,16 MB (≤ 0,2)**; çekirdek 7 kimliğin yeniden türetimi ±0; manifest v015; ~26 yeni `AudioSoundId`; RAM +1,9 MB DecompressOnLoad **veya** ≥ 0,5 s cue'lar CompressedInMemory +0,3 MB (sound-engineer). Müzik q0.7→q0.6 kaldıracı önerilmiyor.

### 7.3 Tavanlar
- Görsel: MUST 19 · tavan 32 · sert 35. Ses: MUST 18 · tavan 24 · sert 30. **Toplam: MUST 37 · tavan 56 · sert 65** (484'ün %13,4'ü). Her varlık PR'ı MB beyan eder (S-01 `tools/build-report.sh`).
- **İlk atılacak (kredi):** SHOULD ses render'ları (−6) → medals v3 (−3) → mühür sheet (−3) → karo/paint-over rezervleri (−6). **İlk atılacak (MB):** §9.

---

## 8 · Performans bütçesi

Bütçe **delta**dır (DEC-088 payı bugün 0,5 ms: kapanış 7,5 / 8,0). S-10 kapanışta bir kez ölçer (§7 hızlı hat): `-ddUiPerf` probu 300 kare/yüzey: `PostLateUpdate.PlayerUpdateCanvases`, `Canvas.SendWillRenderCanvases`, `Canvas.BuildBatch`, `UGUI.Rendering.RenderOverlays`, `EventSystem.Update`, `TMP.GenerateText` **Count** (regen/kare), SetPass, Batches, GC/kare (yığın-delta + pozitif kontrol; `ProfilerRecorderHandle.GetAvailable` ile marker adları önce doğrulanır; `targetFrameRate` kısıtı `LastValue`'yu sıfırlar — WP-059 dersi). UI deltası = UI canvas'ları kapalı A/B koşusu.

| yüzey (M1 Pro) | ana iş parçacığı ort / p99 | GPU UI | canvas | batch | UI SetPass | GC |
|---|---|---|---|---|---|---|
| HUD savaş 1080p 40 düşman | ≤ 0,25 / 0,45 ms (**Δ ≤ +0,15 bugünkü HUD'a**) | ≤ 0,15 ms | ≤ 10 | ≤ 28 | ≤ 10 toplam (3 yüz × ≤ 2 preset + Fx + UI/Default + glif) | 0 B |
| HUD olay karesi (afiş + çip + glint) | ≤ 0,6 ms | — | — | ≤ 34 | — | 0 B |
| Upgrade / Pause (blur bir kez 0,3 ms) | ≤ 0,8 / 1,5 ms | ≤ 0,8 ms | ≤ 8 | ≤ 45 | ≤ 10 | 0 B |
| Menüler 3024×1964 | ≤ 1,0 / 2,0 ms (koreografi ≤ 1,0; boşta ≤ 0,2) | ≤ 2,5 ms | ≤ 10 | ≤ 60 | ≤ 12 | 0 B |
| Selection (turntable varsa) | ≤ 1,5 / 2,5 ms | ≤ 4,0 ms | ≤ 10 | ≤ 60 | ≤ 16 | 0 B |
| Ekran açılışı (bir kez) | ≤ 12 ms, ≤ 256 KB | | | | | |

Kurallar: kararlı durumda ≤ 2 metin regen/kare; savaşta ≤ 2 tam ekran alfa katmanı; tween 16 canlı ≈ 0,012 ms; HUD hedef ≤ 3 doku (glif SDF, frames, Body SDF) + Numeric SDF; shader particle GPU < 0,05 ms; EditMode bekçileri: bileşen yasakları, ekran başına sayım (WP-038J kodu), RM sözleşmeleri, `Update()` izin listesi.

---

## 9 · BUILD-SIZE İKİ DAL (C-00 talimatı)

Taban: release **547,3 MB / 550 tavan** (headroom 2,7). S-01 WP-066C LZ4HC ölçümü (`BuildOptions.None` → LZ4HC, ≈ 40 s) bekleniyor; "yeter" eşiği: **LZ4HC release ≤ 545,0 MB (headroom ≥ 5 MB)**.

### Dal (a) — LZ4HC yeter → tam MUST v3

| kalem | MB |
|---|---|
| fontlar (3 statik SDF) | +1,25 |
| SDF glif atlası | +0,50 |
| RGBA icon/status/AP013/AP014 atlasları kaldırıldı | −1,34 |
| frames v3 (AP012_frames yerine) | ±0 |
| mürekkep kiti BC4 1024² | +0,50 |
| karo seti | +0,10 |
| giriş şekilleri + imleç | +0,06 |
| plakalar 3 katman | +2,00 |
| eski menü plakası kaldırıldı | −0,58 |
| portre PNG'leri kaldırıldı | −0,77 |
| shader + frozen-frame feature | +0,10 |
| ses (52 dosya) | +0,20 |
| **net** | **+2,02 MB → ≈ 549,3 MB (LZ4HC öncesi)** |

Kâğıtta 550'nin altında ama 0,7 MB marj tahmin hatasının (±1,5) içinde; LZ4HC headroom'u marjı gerçek yapar. SHOULD'lar (medals 0 MB, mühür sheet 0, turntable 0 disk, SHOULD ses +0,05) dal (a)'da serbest; yükleme plakası (+0,29) COULD kalır.

### Dal (b) — LZ4HC yetmez → kesme sırası

| adım | kesilen | MB | kümülatif |
|---|---|---|---|
| 0 | tam MUST (a) | +2,02 | 549,3 |
| 1 | **orta + yakın plaka katmanları → yalnız uzak 1536×864 DXT1** (parallax = tek katman + shader yaprak/yağmur derinliği) | −1,60 | **+0,42 → 547,7 MB** |
| 2 | mürekkep kiti 1024² → 512² | −0,37 | +0,05 → 547,35 |
| 3 | Display atlası 1024×512 → 768×512, örnekleme 56 pt korunur (AD vetosu: Display 56 Mac'te 88 px çizilir; 40 pt örnekleme 2,2× büyütme = görünür yumuşama, başlık yüzü v3'ün tek büyük kalite kaldıracıdır) (AD) | −0,12 | −0,07 → 547,23 (AD) |
| 4 | SHOULD ses varyantları, boot çanı + mürekkep | −0,07 | −0,14 (AD) |
| 5 | tuş kapağı art → metin kapaklar | −0,02 | −0,16 (AD) |

**Öneri:** dal (b)'de adım 1 yeter (547,7 MB, headroom 2,3); adım 2+ yalnız LZ4HC ölçümü **büyüme** gösterirse. Asla kesilmeyen: fontlar, glif SDF, frames v3, tek mürekkep sheet'i, motor içi portreler, ses MUST.

### ~560 MB tavan seçeneği (kullanıcı)
Tavan 560'a çıkarsa dal (a) LZ4HC sonucundan bağımsız uygulanır + SHOULD'lar + yükleme plakası → ≈ **549,7 MB**, headroom ≥ 10 MB; ayrıca M4 için turntable/ek plaka payı kalır. Öneri sırası: LZ4HC ölçümü → yeterse (a) → yetmezse kullanıcıya 560 sorusu → hayırsa (b).

---

## 10 · Mevcut kusurlar = Dilim 0

Hepsi bu belgede karar; hepsi S-09 tek commit (E18 flush S-07 küçük commit), testleriyle.

| # | kusur | kanıt | düzeltme |
|---|---|---|---|
| D1 | Silah paneli "P1 / FR1 / RD1" ↔ "RIFLE · HITSCAN" çakışması | `RunHudScreen.cs:593–605`: `buildRect.offsetMax` merkez-pivot stretch rect'te x'i −79'a çeker, `modelRect.anchoredPosition = buildRect.anchoredPosition` bunu sağ-ankrajlı rect'e kopyalar; bot-hud-r4, special-ground, low-health | tür Caption kendi 24 px bandına (üç satır: ad · build çipleri · tür); iki etiketli satırlar `UiStack.AddPair`; T11 çakışma testi |
| D2 | Tehdit çipi kesiliyor "SURROUNDE" / "ELITE NEAR" | `RunHudScreen.cs:823` `ThreatChipWidth 208` sabit; 208 − iç boşluk − 24 glif ≈ 136 px Body 18 bold +4 izleme için; bot-threat, bot-special-ground, bot-low-health; TextScale 1,0'da | çip genişliği içerikten (`GetPreferredValues` build'de bir kez), min 208 maks 240; satır 3 sabit yuva; yüz değişimi düzeltme sayılmaz; T3 `isTextTruncated` = 0 |
| D3 | Fare hover iki ses; klavye/pad odak seyahati sıfır ses | `UiButton.cs:199` `UiEvents.Raise(ButtonHover)` → UiHover **ve** `:204` `UiSound.Play(Move)` → FocusMoved aynı çağrıda; `OnSelect` yolu yok | hover = yalnız `ButtonHover`; `OnSelect` (Navigation yolu) → `FocusMoved` tek raise; `UiEventsTests` + matris satırı |
| D4 | Results dört olay tek karede | `ResultsScreen.cs:125–139` `Start()` Reveal + LevelUp + UnlockRevealed + AchievementRevealed; `:167` NewBestRound yapımda | Dilim 0'da: olaylar `UiStaggerIn` adımında öğe belirirken raise (120 ms); Dilim 10'da zaman çizelgesi cue'ları |
| D5 | `EnsureUiCamera` ilk kamerayı seçer | `UiFactory.cs:194–199` `FindObjectsOfType<Camera>()[0]` sırasız | `targetTexture != null` kameraları atla; yoksa mevcut yol; turntable ve perde bundan önce gelmez |
| D6 | TextScale 1,25 sabit bantları boşaltır | `UiFactory.cs:375` `richText = false`, `:382` `Truncate`; sabit bantlar `RewardTitleBand 24` (`ResultsScreen.cs:54`), `OptionHeight 30` (`MainMenuScreen.cs:58`), `CoreBranchRowHeight 30` (`SelectionScreen.cs:427`), `StateChipHeight 24` / `ToggleRowHeight 40` (`MainMenuScreen.cs:941–942`), `UiEnemyStatusMarkers.cs:26` 24; yüz değişimi satır yüksekliğini 1,15 → 1,26 em'e çıkarır | metin taşıyan her bileşenin yüksekliği/asgari genişliği = yüz metriği × TextScale (build'de); 1,25 yakalaması matrise girer; T3 kapısı |
| D7 | Rich text kapalı → inline glif/`<color>` önerileri çalışmaz | `UiFactory.cs:375` | etiket başına `richText` opt-in bayrağı; varsayılan kapalı kalır |
| D8 | Special bloğu ile countdown/prompt alt-merkez çifte rezervasyon | `RunHudScreen.cs:477` `(0.5, 0)` ankraj; bot-special-ground "ON THE FIELD" ile bot-upgrade "NEXT ROUND IN" aynı yer; Special sahadayken round bitebilir | Special görünürken countdown/prompt Special'ın üstüne yanaşır (boşluk 8) |
| D9 | Hasar sayıları ROUND COMPLETE boyunca donuyor (scaled clock) + konturlu değil | bot-upgrade: üç gri "22"; v005 SHOULD-4 açık | S-07: `PhaseChanged` → RoundComplete/Dead'de 200 ms unscaled fade flush (Dilim 0b); TMP + `FaceOutline` Dilim 8 |
| D10 | Profil pane'i Notice altında klipliyor (H durumu) | frame 06 (eski), baseline default sığar | Notice otomatik yükseklik üstte + Picker modal (Dilim 6) |

Dilim 0 testleri: T11 (panel içinde iki görünür TMP sınırı kesişmez; `isTextTruncated` = 0 — D1 ve D2'yi yakalardı), `UiEventsTests` (D3/D4), `EnsureUiCamera` EditMode (D5), band metrik testi (D6). Yakalama: bot-hud-r4, threat, low-health, special-ground, upgrade + results 1080p; AD turu gerekmez, kareler eklenir.

---

## 11 · Dilimleme planı + sahip önerisi

Her dilim = bir commit + testleri + yakalama seti + AD turu gerekip gerekmediği. Sahipler: S-09 kit/ekranlar/HUD UI · S-07 UI shader/VFX/hasar sayıları/beacon · S-08 UI ses v3 · S-02 savaş HUD geri bildirimi + `CurrentSpreadDegrees` · S-03 `RearDirection` verisi · S-05 plaka yakalama · S-06 turntable (SHOULD) · S-01 build boyutu.

| # | dilim | sahip | içerik | test | yakalama | AD turu | bağımlılık |
|---|---|---|---|---|---|---|---|
| 0 | Kusurlar | S-09 (+S-07 0b) | §10 D1–D9 | T11, UiEventsTests, EnsureUiCamera, band metrik | 6 bot + results | hayır | — |
| 12a | LZ4HC ölçümü | S-01 | WP-066C → dal (a)/(b) kararı; `build-report.sh` varlık PR kapısı | — | — | hayır | — (önce) |
| 1 | Token v3 | S-09 | renk v3 (C5), `UiElevation`/`UiMaterial`/`UiRadius`/`UiStroke`/`UiSize`, `UiMotionTokens`, `UiLayer`, `Expand` + 1408, exporter (HLSL/JSON/§2.2), rezerv kaydı, ayrım tablosu | T1, T2, Export_IsFresh, TableEqualsV2, matris 4×2×2 | 10 ekran × matris | hayır (2 renk görünür; AD onayı C5 ile verildi) | 0 |
| 2 | Hareket çekirdeği | S-09 | `UiMotion` sürücü + preset kaydı + zaman çizelgesi + cue + MotionClass + FlashGate; şimler (AS M2); ekran çıkışı; `UiButton` Tint; odak halkası seyahati | T4 (0 B, 256 tween), T5, T6, FlashGate (10 onset → ≤ 3), test saati EditMode | dinlenme kareleri öncesi/sonrası birebir | hayır | 1 |
| 3 | Tipografi | S-09 (+GR varlık) | `UiFonts` üç yuva (Body/Numeric şimdi; Display kullanıcı kararı sonra), basamak eşitleyici, kerning çiftleri, fallback guard, presetler | T9, T3 1,25, AV/To kerning | 10 ekran + HUD 1,25 | **evet** (yüz gerçek karelerde; Display için iki aday — Shippori Mincho B1 / Spectral SC — aynı 3 karede (mainmenu, results, HUD afişi) yan yana; U1 bu karelerle karara gider) (AD) | 1 |
| 4 | Fx shader + donmuş kare | S-07 | `DD/UI/Fx` modlar + globaller + `UiTokens.hlsl`; `UiFrozenFrameFeature`; mürekkep kiti içe alma (A8) | varyant sayısı 4, SetPass sayımı, `_ClipRect`/CanvasGroup testi | Pause/Upgrade blur, afiş reveal, glint | **evet** | 1, 2, A8 |
| 5 | Bileşen kütüphanesi | S-09 (+GR A3/A4/V1) | §4 ★ bileşenler; Kit Sheet `-ddUiCapture kit` (4 PNG: {Default, CB} × {1,0, 1,25} + hareket şeritleri t 0/.25/.5/1 + RM) | T1, T3, T5, T7, T8 | kit sheet | **evet** | 2, 3, 4 |
| 6 | MainMenu + arka plan + pane'ler | S-09 + S-05 (yakalama) | E2–E8; plaka yığını + parallax + particles; Profile kart/picker, Statistics tiles, Achievements 2×4 + medals (A1/A2), Settings/Controls/Accessibility | T8, T10 plaka metin-güvenli | 7 ekran × matris | **evet** (+ yakalama öncesi kadraj onayı: S-05 `MenuPlateCamera` için 3 aday kare, 0 kr, AD seçer — yanlış kadraj üç katmanı birden yeniden yakalatır) (AD) | 5, P1/P2 |
| 7 | Selection | S-09 (+S-06 SHOULD) | E9: 3 kolon, motor içi portreler, StatStrip/CompareBars, locked/loading; turntable ayrı SHOULD commit (yükleme ≤ 0,5 s ölçümüyle) | T3, T8, EnsureUiCamera | selection × matris | **evet** | 5, 6 |
| 8 | HUD v3 | S-09 + S-02 + S-07 + S-03 | E10–E23: yuva haritası v2, bar/ring/counter/chip, afiş/şerit; S-02 dürüst reticle (`CurrentSpreadDegrees`) + şekilli işaretler; S-07 TMP hasar sayıları + yay sprite + `Marker.Edge` beacon; S-03 `RearDirection` (arka yay SHOULD) | T4 HUD preset'leri, SpecialWeaponHudAllocationTests, T11 | bot r1/r4/r8/threat/low-health/special/enemy-status/upgrade | **evet** | 5, 4 |
| 9 | Dükkân + Pause | S-09 (+S-07 blur) | E24, E25: ShopCard, deal/buy/wipe/lock, hold (RESET kesin, REROLL kullanıcı), modal, donmuş kare | T4 deal/wipe/stamp 0 B, T8 | upgrade/pause × matris | **evet** | 5, 4 |
| 10 | Results + ölüm + **yükleme ekranı (§15)** + boot | S-09 (+S-01 async `Load`/eşik tablosu/watchdog, +S-07 ink, +S-08 cue) | E26, E22, E1 + §15: cue'lu zaman çizelgesi, InkBleed, `UiCurtain` iki derinlik, ensō `UiRing`, mühür el verme, U9 ipucu seti (`loading.*`), hata modalı | T4, cue sıra testi, **T15–T19 (§15.7)**, WP-061J iptal PlayMode | results/boot kareleri + §15.7 yükleme kare seti (0 / 0,5 / 0,9 / ENTERING, ink variant, hata, 21:9, Mac, RM, 1,25) | **evet** | 5, 4, 12a (ölçülen süreler) (AD) |
| 11 | UI ses v3 | S-08 | M1 sözleşme (50 olay), M2 varyasyon/hız, M3 mix + `UiVolume` (kullanıcı), M4 6 render + türev, §5 zamanlama tablosu | `UiEventsTests`, kapsama %100, ardışık 0, `AudioOutputCapture` ≥ 6 LU | `-ddAudioCapture` | hayır (dinleme turu) | 2 (cue'lar), her ekran diliminin olayları |
| 12b | Kapanış ölçümü | S-01 + S-10 | build boyutu final, `-ddUiPerf` §8 tablosu, matris kareleri | §8 | — | AD nihai | hepsi |

Sıra: 0 → 12a → 1 → 2 → 3 ∥ 4 → 5 → 6 ∥ 7 ∥ 8 ∥ 9 ∥ 10 (ekran dilimleri birbirinden bağımsız; **Results + Dükkân önce** (MUST yuvası), sonra HUD, MainMenu, Selection) → 11 (2'den itibaren paralel) → 12b. Her dilim altı ekranı çalışır tutar, EditMode yeşil, `UiFactory` façade (89 Label / 21 Button / 46 NewImage çağrısı derlenir); `[Obsolete]` bir dilim yaşar. r2 kuralı: cherry-pick sonrası yabancı dosyalarda `git diff main` boş.

---

## 12 · Test / kabul

| # | kapı | eşik |
|---|---|---|
| T1 | Kontrast v3: her bileşen çiftlerini beyan eder (`IUiContrastSource`), 2 palet × malzeme zarfı (min/maks texel) | 0 FAIL; beyansız bileşen FAIL |
| T2 | Rezerv kaydı + ayrım tablosu (ΔL* ≥ 10; Warning↔Focus istisnası: dinleme yuvası + E11 LOW durumu, §2.1 kuralıyla) (AD) | 0 ihlal |
| T3 | Taşma: her bileşen, en uzun gerçek dize (VM sabitleri + silah/dal/başarım verisi), TextScale 1,25, `GetPreferredValues`; PlayMode `isTextTruncated` | 0 |
| T4 | 0 ayırma (yığın-delta + pozitif kontrol): boşta HUD, her HUD preset'i, menü odak süpürmesi, dükkân deal/wipe/stamp, 256 tween, Results çizelgesi | 0 B |
| T5 | Her preset'in RM eşlemesi (Decor 0 ms / Data değişmez) ve flash sınıfı; FlashGate 10 onset/s → ≤ 3 | %100 |
| T6 | Sürüklenme kilidi: sayısal literal ≤ taban (Hud 307, MainMenu 265, Upgrade 120, Selection 112, Results 81, Pause 63) ve düşen; `Update()` izin listesi; `new Color(` yalnız `UiTokens`; `Screens/`'te `unscaledDeltaTime|MoveTowards|Mathf.Lerp` yok | 0 ihlal |
| T7 | Her kit sınıfı özetinde USAGE / DON'T / ACCESSIBILITY | %100 |
| T8 | Erişilebilirlik: her etkileşimli öğe ilk odaktan ulaşılır, çıkmaz yok, modal tuzağı | %100 |
| T9 | Glif kapsama/yüz, eşit basamak ilerlemesi, atlas ≤ 1,5 MB, OFL dosyası yanında, AV/To kerning | geçer |
| T10 | Plaka metin-güvenli dikdörtgen: `TextSecondary` ≥ 4,5:1 scrim ile | geçer |
| T11 | Çakışma/kesilme: panel içinde iki görünür TMP sınırı kesişmez; çip metni kesilmez (D1/D2 regresyonu) | 0 |
| T12 | Kit Sheet regresyonu: 4 PNG, ΔE > 6 olan piksel > %0,5 → FAIL (S-10) | geçer |
| T13 | `EnsureUiCamera` `targetTexture` kameralarını atlar; perde `sceneLoaded`'da yeniden bağlanır; WP-061J iptal yolu PlayMode | geçer |
| T14 | ADR-024-2 paritesi: UI modülü `UnityEngine.InputSystem` referanssız (hold `IPointerDown/Up` + legacy `Input.GetButton("Submit")`) | geçer |

Yakalama seti dilim başına §11; menüler 4 çözünürlük × UI scale {0,9, 1,2} × TextScale {1, 1,25}; HUD bot r1/r4/r8/threat/low-health/special-ground/enemy-status/upgrade + 1,25; kit sheet. **AD turu kuralı:** ekran dilimleri (3, 4, 5, 6, 7, 8, 9, 10) ff öncesi kareler üstünde AD kararı (ACCEPT / ACCEPT_WITH_FIXES / REJECT, MUST'lar aynı dilimde kapanır); token/çekirdek/kusur/ses dilimleri kareyle geçer. Karar biçimi v004–v007 ile aynıdır: dilim başına sabit ölçüt tablosu + puan + MUST/SHOULD, her MUST bir ölçüte ve bir kareye bağlanır; kişisel beğeni gerekçe sayılmaz; kareler §12 matrisinden (1080p + 21:9 + Mac + TextScale 1,25 + CB paleti) gelir (AD). S-10 §7 hızlı hat: perf/alloc/build ölçümü yalnız kapanışta bir kez.

---

## 13 · Kullanıcı kararları (yalnız gerçek olanlar; konsey önerisiyle)

**Kabul (kullanıcı 2026-09-24, C-00 aracılığıyla):** U1–U12 konsey önerileriyle aynen kabul edildi — U1 Shippori Mincho B1 · U2 release varsayılanı LZ4HC (S-01 ölçümü 547 → 323 MB, yükleme cezası yok; tavan 550 kalır → **dal (a) tam MUST**) · U3 kredi 56 (görsel 32 / ses 24; sert 65) · U4 `StateWarning` #F7C766 · U5 REROLL hold 500 ms · U6 NEW BEST ROUND koşu içi · U7 boot mührü her açılış + çan · U8 UI SOUNDS kaydırıcısı · U9 ipuçları ilk 3 koşu açık · U10 iki yeşil · U11 önce→sonra kartta · U12 S-02 `CurrentSpreadDegrees` + S-03 `RearDirection` onay. Ek kapsam (kullanıcı): gerçek yükleme ekranı → §15.

| # | karar | seçenekler | konsey önerisi |
|---|---|---|---|
| U1 | Display yüzü (AD v004 SHOULD-1'den beri açık) | **Shippori Mincho B1** (Mincho-uyumlu Latin serif) · Spectral SC (Batı gerçek small-caps) | Shippori Mincho B1; Body/Numeric her iki durumda sabit |
| U2 | Build tavanı rotası | S-01 LZ4HC ölçümü → yeterse dal (a) · yetmezse **560 MB tavan** · yetmez + hayırsa dal (b) | ölçüm önce; yetmezse 560 |
| U3 | Kredi tavanları | görsel 32 (sert 35) + ses 24 (sert 30) = **56 / 65**; MUST-only 37 | onay |
| U4 | Uyarı rengi görünür değişimi `StateWarning` Gold400 → #F7C766 (Accessibility swatch'ı "WARNING" değişir; anlam aynı) | evet / hayır | **evet** (Default'ta uyarı = para altını birebir) |
| U5 | REROLL'da hold-to-confirm 500 ms | evet / hayır (RESET ALL BINDINGS her durumda hold) | **evet** (geri alınamaz, altın harcar, pad zincirinde kayan A) |
| U6 | NEW BEST ROUND şeridi koşu içinde (afiş yuvası, 2,4 s, ertelenebilir) | koşu içi / yalnız Results | **koşu içi** (kare-doğru, tek yuva) |
| U7 | Boot mührü (≤ 1,6 s, atlanabilir) | her uygulama açılışı / yalnız ilk açılış / yok; + çan sesi evet/hayır | **her açılış, çan evet** (tek rin vuruşu, atlamada 120 ms fade) |
| U8 | Settings'te ayrı "UI SOUNDS" kaydırıcısı (`UiVolume`, Interface bus) | evet / hayır | **evet** (işitsel yorgunluk; düşük maliyet) |
| U9 | İpuçları ilk 3 koşuda varsayılan açık (prompt yuvası, ≤ 4, her biri bir kez, Settings anahtarı) | açık / kapalı / yok | **açık** (SHOULD dilimi) |
| U10 | Bir yeşil / iki yeşil | iki (Jade = kazanılmış, Moss = can) / bir (yalnız Jade400 mümkün) | **iki** |
| U11 | Önce→sonra sayıları | kartta varsayılan / yalnız tooltip'te | **kartta** (dükkân bir satın alma kararıdır; kural zaten bu sayıları kullanır) |
| U12 | Çapraz-sahip veri açığa çıkarmaları: S-02 `CurrentSpreadDegrees` (dürüst reticle) + S-03 `RearDirection` (arka yay) | onay / ret | **onay** (yalnız veri, kural değişmez) |

Çözüldü, kullanıcıya gitmez: Special bloğu alt-merkezde kalır (countdown üstüne yanaşır); perde gecikmesi (≈ 0,56 s, bugünkü karartmanın yerine); menü IA (sekmesiz omurga kalır); Selection turntable (SHOULD, ölçüme bağlı); tooltip süreleri; damga şekli; reroll = mürekkep silme yalnız değişen kartta.

---

## 14 · AD eş imzası

**Karar: SIGNED_WITH_CHANGES** (art-director, 2026-09-24). İnceleme tabanı: r1/r2 dosyaları (dokuz disiplin), `council/baseline-small/` Look B kareleri, stil kılavuzu v1 §2.2/§2.4, `UiTokens.cs`. Görsel dil, üç yükselti, malzeme/ornament kuralları, C1 tipografi üçlüsü, C5 hex değerleri, C2 motor içi plaka yığını, C11 kredi tekilleştirmesi ve dilim başına AD turu kuralı tur 2 mutabakatıyla örtüşür. Aşağıdaki değişiklikler belgede "(AD)" ile işaretlidir; hepsi ya iç çelişki ya rezerv ihlali ya da aritmetik hatasıdır, hiçbiri yeni bir tasarım kararı açmaz.

**Yapılan değişiklikler**

| # | yer | ne | neden |
|---|---|---|---|
| 1 | §2.1 rezerv kaydı | `AccentPrimary` listesinden "sahipli pip" çıkarıldı | E24 tier/pick piplerini krem yapıyor; kayıt kendi kuralıyla çelişiyordu; vermilion = CTA + tik + mühür (§1) |
| 2 | §5 HUD geri bildirim tablosu · başlık | zırh kırıldı / kritik: amber → krem + şekil | `StateWarning` oyuncuya "uyarı" der; olumlu vuruş kademesine ikinci anlam kısıt 2'yi ihlal eder |
| 3 | §2.1 ayrım notu · §5 E11 · §12 T2 | Warning↔Focus buluşma yeri yalnız Controls değil, E11 de (Focus400 reload + #F7C766 LOW); kural: LOW'da reload halkası/barı da `StateWarning` (v005 SHOULD-6) | aynı panelde iki soluk altın iki anlam taşıyamaz |
| 4 | §2.3 | `UiSpace` mevcut değerleriyle kalır (M 12, L 16, XL 24, XXL 32, XXXL 48, Section 64, Page 96) | belge enum'u sessizce yeniden değerliyordu; hiçbir öneri bunu istemedi, her çağrı noktası kayardı |
| 5 | §1 · §7.1 A2 · §5 E3 | Badge mon roundel ×5 → ×1 (rozet başına mon COULD) | A2 20 hücreydi, 4×4 sheet 16 |
| 6 | §7.1 A3 · §5 E2 · §5 E4 | A3 20 → 16 hücre: Reticle.Ring/Ticks mevcut AP-012'den taşınır, 3 yaprak → 1 (shader'da 6 örnek), Stat.Trend COULD | sheet aritmetiği |
| 7 | §7.1 G1 | 1024² SDF atlası 64² hücre → 256 yuva; imleç atlastan çıkarıldı (V1 RGBA32) | 128² hücrede 64 yuva vardı, ~103 glif sığmazdı; imleç donanım dokusudur |
| 8 | §5 E2 · §6.1 | "3 katman" dört bant sayıyordu → uzak / orta / yakın | §6.1 ile tutarlılık, S-05 yakalama listesi |
| 9 | §5 E3 · §5 E9 · §4 #13 | dinlenen kimlik kartı ve seçili kart Elev1; Elev2 yalnız hover/odak/modal/perde/tooltip; DSE adları (Raised→Lifted) üçlü adlarına çevrildi | §1 yükselti tanımıyla çelişki |
| 10 | §5 E9 | seçimde ikinci fırça alt çizgisi kaldırıldı | L2 kuralı: ekran başına bir fırça alt çizgisi, H1 altında |
| 11 | §4 #1 | pressed dolgu: Primary/Danger `AccentPrimaryPressed`, diğerleri `SurfacePanel` | mevcut `UiButton` durum matrisi |
| 12 | §4 #24 | perde kapıları kumiko karosu A6 ile (prosedürel değil) | varlık listesiyle tutarlılık |
| 13 | §9 dal (b) adım 3 | Display 40 pt örnekleme vetolandı; 768×512 @ 56 pt (−0,12), kümülatifler güncellendi | Display 56 Mac'te 88 px; 2,2× büyütme görünür yumuşama; başlık yüzü v3'ün tek büyük kalite kaldıracı |
| 14 | §11 dilim 3 · dilim 6 · §12 AD turu | Display için iki aday aynı 3 karede (U1 kanıtı); plaka yakalama öncesi 3 aday kadraj AD onayı; karar biçimi v004–v007 sabit ölçüt tablosu | AD turu kuralının ölçülebilirliği (O7) |

**Dilimler başlamadan önce kalan AD MUST'ları**

1. **Sheet kapısı:** A1–A4, A6, A8 her biri içe alınmadan önce AD incelemesinden geçer (24 px maske testi + AP-012 stil notları sözlüğü + rezerv: sheet'te renk yok, beyaz + alfa); ret = yeniden üretim, `R` rezervinden.
2. **U4 "hayır" senaryosu:** kullanıcı uyarı hex değişimini reddederse `StateWarning` Gold400 kalır ve **LOW/EMPTY/dinleme durumları rengi bırakır** — yalnız kelime + şekil, `TextPrimary`; ayrım testi istisnası kayda geçer. Hiçbir durumda uyarı metni para altınıyla aynı hex'te panelde durmaz.
3. **Stil kılavuzu §2.2 UI tablosu** dilim 1 exporter'ıyla güncellenir: sakura = NEW/ilerleme, altın şarjör-barı istisnası (`BarReload` Focus400, varak yok), iki yeşil, uyarı hex'i, ginpaku/kinpaku kuralı — v003'ten beri taşınan v004 SHOULD-2/7 kapanır.
4. **Ornament sayımı T6'ya:** ekran başına vermilion ornament ≤ 2 (tik + mühür yalnız boot/logo), fırça alt çizgisi ≤ 1, tören öğesi ≤ 1; sayım EditMode'da (WP-038J sayım kodu).
5. **Plaka kadrajı:** dilim 6 yakalaması AD'nin seçtiği kadrajla yapılır (madde 14); sol üçte bir metin-güvenli dikdörtgen T10 ile ölçülür, scrim α 0,55 sabit.
6. **Display kararı dilim 3'ü bloklamaz:** Body/Numeric donar, Display yuvası boş kalır (bugünkü davranış: `UiFonts.Primary` → LiberationSans); U1 sonrası tek commit.

Bu altı madde dışında belge bağlayıcıdır; ekran dilimlerinin AD turu kararları bu belgeye ve stil kılavuzu §2.2/§2.4'e göre verilir.

---

## 15 · Yükleme ekranı (dilim 10, kullanıcı kapsamı 24.09)

**Kaynak:** `council/LOADING-BRIEF.md` (kullanıcı: "oyunda yükleme olacaksa bir yükleme ekranı tasarımı yapmak gerekir"), `loading-interaction-designer.md` (ID), `loading-art-director.md` (AD), `loading-ux-writer.md` (UXW), `PrototypeUiFlow.cs`, S-01 ölçümleri (release LZ4HC: menü ≈ 3,6 s, dünya ≈ 4,1 s). Bu bölüm §5 E29, §6.3 ve C4'ün senkron tarifinin üstüne yazar; §4 #24 `UiCurtain` bileşeni burada iki derinlik kazanır.

**Tek çatışmanın kararı (AD):** ID'nin eşik kuralı — hedef sahnenin *ölçülen son* yükleme süresi < 0,9 s ise CURTAIN, değilse (ya da ölçülmemişse) SCREEN — **mekanizmadır**; AD'nin geçiş başına sanatı (mühür varyantı, kapı + plaka, mürekkep varyantı) **hedefe göre varyanttır** ve eşikle birlikte yaşar. Bugün altı geçiş de 3,6–4,1 s → altısı da SCREEN; AD'nin MainMenu→Selection perdesi ölçülmemiş bir tahmine dayanıyordu, düşer; Run→Results mürekkebi "SCREEN · ink variant" olarak aynen kalır. CURTAIN derinliği, yüklemelerin hızlandığı gün tasarımın yalan söylememesi için vardır.

### 15.1 Geçiş haritası

| geçiş | ölçülen | yüzey · varyant | arka plan (hepsi yerleşik, 0 MB) | eyebrow / hedef etiketi (UXW §2) | ipucu | asgari gösterim |
|---|---|---|---|---|---|---|
| Boot → MainMenu | 3,6 s | SCREEN · **mühür varyantı** (kapı yok; U7 mührü yükleme ekranıdır) | mürekkep `SurfaceBase` | — / wordmark `DEATHS DEMOLITION` + kicker | yok | mühür koreografisi 1,6 s |
| MainMenu → Selection | 3,6 s | SCREEN · kapı + plaka | uzak plaka P1, cover, scrim 0,55→0,80 | `LOADING` / `BEFORE THE RUN` (Selection kicker'ı) | havuzdan | 1 200 ms |
| Selection → Run | 4,1 s | SCREEN · kapı + plaka | uzak + orta plaka (fenerler yanık: gidilen yer) | `LOADING` / `ROUND 1` · alt satır silah `DisplayName` | havuzdan (en ağır yükleme, ipucunun yerini kazandığı tek yer) | 1 200 ms |
| Run → Results | 3,6 s | SCREEN · **mürekkep varyantı** (E22 sızması kapının kapanışıdır; kapı çizilmez) | donmuş ölüm karesi (§6.4) + InkBleed | `LOADING` / `RUN RESULTS` | yok | ölüm vuruşu 2,4 s (`DeathResultsDelaySeconds`) |
| Results → MainMenu | 3,6 s | SCREEN · kapı + plaka | uzak plaka P1 | `LOADING` / `MAIN MENU` | havuzdan (FACT) | 1 200 ms |
| Run → MainMenu (Pause'dan iptal) | 3,6 s | SCREEN · kapı + plaka | donmuş pause karesi (blur) altında kapılar, scrim 0,72 | `LOADING` / `MAIN MENU` | **yok** (çıkıştan sonra koçluk yok) | 1 200 ms |

Kural: hiçbir sahne spekülatif yüklenmez (`LoadSceneAsync` iptal edilemez); yükleme yalnız geçiş isteğiyle başlar — ölüm vuruşu sırasında Results ön yüklemesi yok. Aynı anda tek yükleme: bekleyen yükleme varken `Load()` log satırıyla yok sayılır. Eşik tablosu `ui-settings.json`'da (profil değil), her tamamlanan yükleme süresini geri yazar.

### 15.2 Async akış (istek t0'dan; §2.5 tokenları)

1. **t0 istek** — `Load()`: `s_transition++`, `CanStreamedLevelBeLoaded` (yoksa log, hiçbir şey kımıldamaz), `UiCurtainClose` cue, raycast'ler kapalı + `EventSystem.SetSelectedGameObject(null)`, imleç gizli. Çıkan ekran `ExitScreen` Fast 140 kapıların altında. `CompletionAuthority.InvalidateLive()` / `ProfileService.ClearLastReport()` istek anında kalır.
2. **t0–280 kapan** — kapılar Move 280, merkezde buluşur (mürekkep varyantı: InkBleed Brush; mühür varyantı: kapı yok, mürekkep lekesi). Menü bed'i sürer.
3. **t280 kapalı kare** — `yield return null` ×2 (batchmode kuralı) → `LoadSceneAsync(scene, Single)`, `allowSceneActivation = false`. Çağrı kapılar kapandıktan sonra: kendi takılması tween ortasında görünmez.
4. **t300–520 grup girişi** — plaka + halka + etiketler + ipucu şeridi tek `CanvasGroup`, PaneIn Base 220 (yalnız SCREEN). Halka gerçek ilerlemeden başlar.
5. **yükleme** — halka §15.3; **dünya donuk kalır: `ReleaseSimulation()` istekten 7. adıma taşınır** (bugün bloklayan yüklemenin önünde; async'te kapı arkasında düşmanlar yürür, hasar ve ölüm olayı üretirdi).
6. **kapı (gate)** — aktivasyon üçünü ister: `op.progress ≥ 0,9` · asgari gösterim doldu (grup girişinden ≥ 1 200 ms = 2 × Reveal; kapıdan kapıya ≥ 1 760 ms) · Boot'ta mühür koreografisi bitti.
7. **aktivasyon** — mühür `Stamp` (130 ms; temas t 90'da `ReleaseSimulation()` + `allowSceneActivation = true`), eyebrow `ENTERING`. `sceneLoaded` → `OnSceneLoaded` `s_transition++` + Run'da hand-off yeniden kurulur (değişmez). `op.isDone` → `yield return null` ×2: yeni sahne kapalı kapının **arkasında** bir kez render edilir; yeni ekranlar intro'yu `Start()`'ta değil `UiCurtain.Revealed`'da başlatır. Halka son 36°'yi bu iki karede Settle 220 ile kapatır.
8. **açılış** — grup Exit Fast 140 → kapılar Settle 280; hedef ekranın `EnterScreen`'i kapılar %50 açıkken (t +140) başlar; `UiCurtainOpen` cue ilk açık karede; imleç ve raycast'ler ilk `EnterScreen` karesiyle döner; hedef ekranın odak hafızası `Revealed`'da uygulanır.

**Anti-flicker:** grup asla < 600 ms görünmez; yükleme t < 500'de ≥ 0,9 bildirirse grup hiç gösterilmez, geçiş CURTAIN'e düşer (kapalı ≥ 300 ms tut, aç). **Beklenenden hızlı (SCREEN seçili):** halka 0,9'da açık kalır, asgari gösterim dolunca damga — dürüst, aktivasyon gerçekten ertelenmiştir. **Beklenenden yavaş (CURTAIN seçili, > 900 ms):** grup t 900'de geç girer, asgari gösterim girişten sayılır, süre geri yazılır. **Takılma:** ilerleme 10 s değişmezse ipucu altında `UiNotice Info` `loading.state.stalled`; ≥ 60 s hata modalı (§15.4). Ham yüklemeye eklenen gecikme ≈ 0,7 s (280 + 2 kare + 140 + 280).

**WP-061J:** `Load()` tek giriş kalır; `s_transition` istekte ve aktivasyonda artar → bekleyen `GoToResultsAfterDelay(scheduledAt)` uyuşmazlıkta vazgeçer, taze Run `s_resultsScheduled = false` ile yeniden kurar; additive dünya yüklemesi geçiş değildir. Results ön yüklenmediği için ölüm vuruşunda iptal bugünkü gibi davranır.

### 15.3 Görsel spesifikasyon (AD)

**Yerleşim (1920×1080 referans; `Expand`, 1408 sayfa kuralı).** Kart yok: halka plakanın üstünde yüzer (Elev0), kapılar ve plaka yüzeyi zaten verir; 560×240 kart 96 mühür + halka + üç satırı sıkıştırırdı. Plaka `cover`, statik (parallax/yağmur yok — yükleme ekranı menü değildir); üstünde `ScrimOverlay` gradyanı Sumi900 α 0,55 (üst) → 0,80 (alt): alt üçte bir metin-güvenli (T10 en koyu uçla ölçülür). **Ensō halkası Ø 240, merkez (960, 500)** (gerçek merkezin 40 px üstü); içinde mühür cihazı 96 (reticle halkası + yaprak, mürekkep). Eyebrow `LOADING`/`ENTERING` Label 15 caps `TextSecondary` +0,08 em, halkanın 24 px altı (üst y 644); hedef etiketi Display H2 24 caps `TextPrimary` 8 px altında; alt satır (silah adı) Label `TextSecondary`. **İpucu şeridi:** sol alt, x 96, alt kenar 1080 − 96, genişlik ≤ 720 (`MaxMeasure`), yükseklik 56 (1,25'te iki satır → 80, yukarı büyür); `SurfaceBase` α 0,72, 1 px `BorderHairline` üst kenar, Elev0 (gölge/bevel yok — washi şerit); `Tooltip.Info` glifi 20 px `TextSecondary` + Body 18 Source Sans 3 `TextPrimary`. Prompt yok. **21:9:** plaka yükseklik kırpar, halka gerçek merkezde (1280, 500), şerit gerçek kenara yapışır (x 96; HUD panelleri gibi köşe öğesi — AD 10b-2 SHOULD-4). **Mac 1920×1247:** halka (960, 583), şerit altı 1247 − 96, fazla yükseklik plakaya. **Kapılar:** iki lake panel, kumiko karosu A6 α ≤ 0,08, `UiLayer.Curtain` 400, `UiFlowRunner` canvas'ı (menüde ScreenSpaceCamera, Run'da Overlay, `sceneLoaded`'da yeniden bağlanır — T13). Grup ≤ 12 grafik, raycaster yok.

**Ensō halkası — dürüst ilerleme.** Tek `UiRing` (`Image.Filled Radial360`, 12'den saat yönü), `DD/UI/Fx` InkReveal ile ensō fırça darbesi boyunca çizilir (A8 mürekkep kiti üçüncü darbe, G kanalında zaman haritası). **Data sınıfı:** `fillAmount` = `op.progress` 0→0,9 → 0→324°, Linear, asla ease yok, mevcut değerden yeniden hedefler; gösterim gerçeğin önüne geçmez, gerisinde Reveal 600'den fazla kalmaz; yazım yalnız Δ ≥ 1/256'da.

| durum | görünen |
|---|---|
| 0 | hayalet halka Cream100 α 0,12 (tam ensō, kuru); mühür cihazı içeride mürekkep α 0,35; `LOADING` |
| 0,5 | darbe 162°'ye çizilmiş; uçta 0,04 genişlik ıslak-kenar bandı koyulaşır (InkReveal); darbe `TextPrimary` krem, 14 px (fırça gibi 10–18 arası); ileride hayalet halka görünür |
| 0,9 | darbe 324°'de; ensō'nun ağzı (36°) **açık** — iş bitmediği için daire kapanmaz |
| aktivasyon | mühür Stamp (1,12→1,00, 130 ms, mürekkep → `TextPrimary`) adımı işaretler, eyebrow `ENTERING`; `sceneLoaded` sonrası son 36° Settle 220 ile kapanır, yeni sahnenin ilk karesi kapalı kapı arkasındayken → grup Exit 140 → kapılar açılır. Aktivasyon takılması damganın altına düşer, boş bekleyişe değil |

Glint yok, altın yok, flaş yok: halka krem, mühür mürekkep, ıslak uç darbenin son 0,04'ü (krem α 0,6, ön kenarı ilerlemeden geriye titrer; ilerlemenin ötesinde piksel yok — AD 10b-2 MUST-1). Mürekkep varyantında aynı halka Ø 160, yalnız 1,2 s'den sonra belirir (mürekkebin içinde), aynı durumlar; kapı-yalnız yavaş yüklemeler §15.2'ye göre tam grubu 0,9 s'de alır (AD 10b-2 SHOULD-3).

**Boot mührü → ilk yükleme (U7: her açılış + çan).** Boot ve ilk yükleme tek nesnedir: Boot sahnesi t0'da `LoadSceneAsync(MainMenu)`'yü aktivasyon tutarak başlatır, E1 koreografisi yükleme canvas'ında oynar: t0 mürekkep lekesi InkReveal 500 → t 0,55 mühür Stamp, temas t 0,64 = `BootLogo` çan (tek rin vuruşu) → t 0,70 wordmark Display 700 Clip wipe 280, kicker üstte → t 1,6'ya tut. **El verme t 1,6'da (ya da atlamada):** mühür Tint Vermilion600 → mürekkep `TextSecondary` 220 ms (vermilion logonundur, yükleme mührü mürekkeptir), wordmark Exit 140, `LOADING` aynı taban çizgisinde girer, ensō hayalet halkası mührün çevresinde **o anki** ilerlemede belirir (çoğu kez 0,9 — anında çizilir, damgalar). Menü t 1,6'dan önce hazırsa fazladan hiçbir şey görünmez: mühür tutması yüklemeyi yutar, t 1,6 damgası aktivasyondur. Atlama: boot tween'leri t = 1, çan 120 ms fade, halka gerçek ilerlemede — atlamak hazırlığı asla taklit etmez. Parlaklık > %20 sıçramaz, beyaz kare yok.

### 15.4 Metin (UXW; anahtarlar `loading.*`)

- **İpucu havuzu:** UXW §1'in 24 ipucu (FACT/HINT; her biri ≤ 90 karakter, kod/karar satırıyla doğrulanmış) + ID'nin üç bağlama ipucu (`Reload`, `Special Weapon`, `Interact` — `UiPrompt` ile canlı bağlama metni + şema glifi, yapı gereği doğru) HINT sınıfında. **Seçim kuralları (UXW §1.1):** ipuçları açıkken (profil koşusu < 3 ve HINTS ON) önce HINT, her HINT profil başına bir kez (9+3 bitlik maske), sonra FACT; ipuçları kapalıyken yalnız FACT; beş grup arası round-robin, oturumda tekrar yok, oturumun ilk yükleme ekranı survival ipucu göstermez; ipucu yükleme ortasında değişmez. Boot, Results ve iptal geçişlerinde ipucu yok. Settings anahtarı `HINTS` ON/OFF, caption UXW'de. R1–R4 kural cümleleri kullanılmaz (CURTAIN derinliği metinsizdir; < 0,9 s'de okunacak vakit yok).
- **Durum satırları:** `LOADING` (0–0,9) · `ENTERING` (aktivasyon → ilk render; asla `READY`) · RM'de `LOADING · {0}%` (0–90 op'tan, 90–100 aktivasyonda, 10'ar adım, Count kanalı `SetText`, 0 ayırma) · `loading.rm.static` satırı ("Loading. The screen changes on its own; no input is needed.") ipucu şeridinde. "Your run is already recorded." alt satırı ve hata ekindeki türevi **düşer** (yükleme sırasında `LastVerdict` okunmaz; Results zaten söyler).
- **Hata modalı (UXW §2.2):** `UiModal M` 640×340 opak, scrim yok; başlık `THE NEXT SCREEN DID NOT LOAD` Display H2; gövde `loading.error.body.missing` / `.stalled` ({destination} cümle düzeninde); birincil `RETURN TO MAIN MENU` (hedef MainMenu değilse; odak burada) veya `TRY AGAIN` (MainMenu'nün kendisi çöktüyse); ikincil `QUIT` (mevcut etiket), Cancel'a bağlı değil; Esc/B hiçbir şey yapmaz. Tetik: `CanStreamedLevelBeLoaded` false → log (kapılar kımıldamaz, modal yok); `LoadSceneAsync` null → kapılar asgari gösterimden sonra eski sahnenin üstüne açılır, modal onun üstünde; Run→Results çökerse eski sahne ölü koşudur (donmuş kare + modal), profil `ProfileRunCommitter` ile zaten yazılmıştır; ≥ 60 s takılma → `.stalled` modalı yalnız `QUIT` ile (async yükleme iptal edilemez; üst üste ikinci `Load()`'un güvenliği S-01 testinde kanıtlanırsa `RETURN TO MAIN MENU` eklenir). Hiçbir yol profile yazmaz.

### 15.5 Erişilebilirlik ve girdi

- **RM:** kapılar t0'da kapalıya kesilir (0 ms — panel vaadi), grup t0'da dinlenmede, **halka Linear kalır (Data)**, ıslak-kenar titremesi yok, damga anında, asgari gösterim tutulur, kesilerek açılır, hedef `EnterScreen` dinlenmede; Boot: statik kompozit 1,0 s + halka; mürekkep varyantı: dim anında, statik mürekkep, 2,4 s'de kes; RM metin satırı §15.4.
- **Reduce Flashes:** her yüzey mürekkep/lake (#12100F), asla beyaz; kapı kapan/aç parlaklığı 280 ms'de kademeli; RM'deki iki kesme (koyu, ≥ 1,76 s sonra açık) 3 Hz'in çok altında; mühür Stamp Decor'dur, Flash değil; burada hiçbir şey `FlashScale` ile çarpılmaz çünkü hiçbir şey flaşlamaz.
- **TextScale 1,25:** şerit iki satıra akar ve yukarı büyür (117 karakter rezerv, 1408 sayfada iki satır); halka ve etiketler dokunulmaz; hata modalı gövdesi otomatik yükseklik.
- **CB paleti:** durum = kelime (`LOADING`/`ENTERING`) + şekil (açık/kapalı ensō); renk taşımaz.
- **Girdi:** yükleme sırasında yok (atlanacak şey yok; Esc/B yok sayılır, OS çıkış yolu kalır; tekrar zamanlayıcıları ve odak hafızası donuk). Boot koreografisi Submit/Cancel/tık ile sessizce atlanır, görünür prompt yok (1,6 s vuruşta gürültü). Hata modalı standart `UiButton` + pad odağı; altbilgi eklenirse `{Submit} RETURN TO MAIN MENU` glifi `UiInputGlyphs`'tan, `{Cancel}` girdisi yok. UI modülü InputSystem okumaz (T14).

### 15.6 Varlıklar

| kalem | kaynak | MB | kr |
|---|---|---|---|
| uzak / orta plakalar | P1/P2 dilim 6 yakalaması (§7.1) | 0 yeni | 0 |
| ensō darbesi + zaman haritası | A8 mürekkep kiti (üçüncü darbe, aynı 1024² BC4 sheet) | 0 yeni | 0 (A8'in 3'ü içinde) |
| mühür cihazı | V2 vektör (G1 SDF hücresi) | 0 | 0 |
| `Tooltip.Info` glifi | A3 | 0 | 0 |
| kapılar | kumiko A6 + `Frame.Modal` | 0 | 0 |
| ipucu dizeleri `loading.*` (27) + durum/hata (≈ 12) | UXW, kod | ≈ 0,01 | 0 |
| **toplam** | | **+0,01 MB** | **+0 kr** (görsel 19 MUST / 32 tavan değişmez; U3 içinde) |

Sumi-e yükleme plakası COULD kalır (Look B yakalaması anahtar sanattır; boyalı plaka ikinci bir hava olurdu).

### 15.7 Testler (T15–T19; dilim 10 kapısı)

| # | kapı | eşik |
|---|---|---|
| T15 | Async akış PlayMode: 2 s yapay yükleme → halka monoton artan, kapılar `op.isDone` öncesi asla açılmaz, `sceneLoaded` Single `s_transition`'ı artırır, hedef `EnterScreen` yalnız `Revealed` sonrası; `ReleaseSimulation()` aktivasyondan önce çağrılmaz (dünya kapı arkasında 0 kare ilerler) | geçer |
| T16 | Asgari gösterim / anti-flicker: 0,2 s yükleme → grup hiç görünmez, CURTAIN (kapalı ≥ 300 ms); 0,7 s → grup ≥ 1 200 ms; SCREEN seçili + erken biten yükleme → halka 0,9'da bekler, damga kapıda | geçer |
| T17 | WP-061J: ölüm t 1,0'da iptal → MainMenu, Results yok; doğrudan Run boot'u hand-off'u yeniden kurar; bekleyen yükleme varken `Load()` yok sayılır + log; Run→Results 2 s yapay yükleme → Results açılır, ölüm karesi arka plan | geçer |
| T18 | 0 ayırma yükleme boyunca: yığın-delta + pozitif kontrol, 300 kare SCREEN (halka + ipucu + plaka), `fillAmount` yalnız Δ ≥ 1/256'da yazılır, ipucu bir kez set; konsola hata/uyarı yok (kriter 27) | 0 B, 0 log |
| T19 | Metin: 27 ipucu ≤ 90 karakter; 1408 sayfada 1,25'te ≤ 2 satır (`GetPreferredValues`); oturumda tekrar yok; HINT maskesi profil başına bir kez; ilk ekranda survival yok; hata başlığı +%30'da tek satır; `isTextTruncated` = 0 | geçer |

Ek: T13 (perde `sceneLoaded` yeniden bağlanma) ve T14 (InputSystem paritesi) dilim 10'da tekrar koşar. AD turu kare seti: boot t 0,64 ve t 1,6; Run'a yükleme 0 / 0,5 / 0,9 / ENTERING; Results mürekkep varyantı; iptal (pause karesi altında kapılar); hata modalı; 21:9; Mac 1247; RM; TextScale 1,25.

### 15.8 Dilim 10 sahip bölüşümü

| sahip | iş |
|---|---|
| S-01 | `PrototypeUiFlow.Load` → `UiFlowRunner` üstünde async coroutine (aktivasyon tutma, `ReleaseSimulation` aktivasyona, tek yükleme kuralı), sahne başına son-yükleme eşik tablosu (`ui-settings.json`) + geri yazma, watchdog (10 s / 60 s), T15–T17'nin akış yarısı, ölçülen sürelerin kaydı (12a) |
| S-09 | `UiCurtain` iki derinlik + varyantlar (mühür / kapı+plaka / mürekkep), ensō `UiRing` + InkReveal bağlaması, etiketler ve eyebrow durumları, ipucu seçimi + HINT maskesi + `HINTS` ayarı, hata modalı, RM/CB/1,25 yolları, T16/T18/T19, kare seti |
| S-07 | A8 ensō darbesi + zaman haritası, InkReveal/InkBleed modları, donmuş kare (ölüm/pause) yeniden kullanımı |
| S-08 | `UiCurtainClose`/`Open` cue'ları (kapanış `LoadSceneAsync`'ten ≥ 1 kare önce), `BootLogo` çanı + atlamada 120 ms fade, yükleme sırasında döngü/tık yok, menü bed'i sürer |
| UXW | `loading.*` dize dosyası (27 ipucu + durum + hata), pseudo-localization geçişi |
| AD | kadraj/plaka seçimi (dilim 6 ile ortak), dilim 10 AD turu (kare seti §15.7) |

Sıra: S-01 async `Load` + T15/T17 → S-09 `UiCurtain` v2 + T16/T18/T19 → S-07 mürekkep → S-08 cue'lar → AD turu → ff.
