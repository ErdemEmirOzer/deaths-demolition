# WP-081 · İkon v2 (AP-028-B "O1 Fırça Mürekkebi"): yön ve API kararı (Faz A)

**Durum:** İMZALI, şerhler işlendi (06.10). Lider ui-ux-designer imzaladı (Ş1–Ş4; ui-ux-lead.md §10); eş imza design-systems-engineer (şerhli; design-systems.md §8). 🔴 maddeler C-00'ın kararıdır; karar gelene kadar önerilen varsayılan uygulanır (§1.16).
**Tarih:** 06.10.2026.
**Hazırlayan:** S-09 (entegrasyon sorumlusu).
**Konsey notları** (`K/wp-068s9/icon-v2/council/`):
- `ui-ux-lead.md`: lider; okunurluk, kabul ölçütleri ve AD kareleri
- `design-systems.md`: glif eşlemesi, G1 ve testler
- `unity-material.md`: malzeme ve kanallar
- `interaction-motion.md`: hareket ve hakem

**Girdi:**
- AP-028-B teslimi: `K/asset-production/AP-028/exports/v002/Icons/` (README, atlas.json, motion.json)
- Taslaklar: 4895ac1d (iskelet, dal wp-081-v4a-r2), `K/wp-068s9/tools/merge_o1_g1.py`, `K/wp-068s9/devir-05.10/{g1-o1,icon}/`

Hiçbir ölçüm Unity'de koşulmadı. Sayılar teslim dosyalarından, koddan ve hafif Python ölçümlerinden geliyor; kaynakları notlarda.

## 1. Kararlar (konsey uzlaşısı)

1. **Tek G1 sayfası.**
   - İkinci runtime sayfası açılmaz.
   - Height, Normal, States ve Previews runtime'a alınmaz.
   - Kabartma SDF'den türetilir. Ölçüm: 58 glifte yükseklik hatası ≤ 0,0094, normal açı hatası ortalama 0,31°, p99 0,52° (design-systems §3.4).
   - Bütçe: bugün 1,34 MiB / 12 MiB; birleşim yaklaşık +33 KB.
2. **Aile çerçevesi sabit SVG viewBox'ıdır (0..256).**
   - Taslağın "58 glifin sınır birleşimi" çerçevesi (29..228) kullanılmaz. O çerçevede 34/58 hücrede mürekkep sprite kenarına 0–2 px dayanıyor. Sonuçları: HUD dış çizgisi ve O1 glow/spread kırpılıyor, 24 px'te çizgi 2,57 px oluyor (AP-028 kanıtında 2 px).
   - viewBox çerçevesinde pay ≥ 5,9 px, çizgi 2 px.
   - Bedeli: glifler taslağa göre yaklaşık %22 küçük. 32 px rayda mürekkep 18–25 px kalıyor.
   - `merge_o1_g1.py` bu çerçeveye göre yeniden yazılır.
   - `DD_ICON_SD_AP` = 64/52 = 16/13 = **1,2308** (1 G1 px = 1,2308 AP birimi; hücre 6 + 52 + 6 = 64 G1 px). 58 glifte AP'nin kendi SDF'ine medyan hata 0,197 AP; birleşim çerçevesinin 0,9567'si 0,349 AP veriyordu (design-systems §8.1).
   - Bu çerçevede değişen öteki sabitler: ekran pikseli başına AP S/M/L = 2,667 / 2,0 / 1,6; S'de ölçek 1,00× (çizgi 2,00 px, bevel 0,75 px, README ile birebir); lob ötelemesi 0,542 G1 px; quad genişletme varsayılan E = 0 (glow yarıçapı ~5 G1 px'i aşarsa açılır).
3. **Malzeme ve hareket yalnız etkileşimli ikon kontrollerinde** (UiIconButton: ray, sekme, düğme).
   - HUD glifleri ve paylaşılan SdfGlyph varsayılan yolu birebir kalır (IK-M18).
   - Gerekçe: HUD'un kendi mürekkep/derinlik dili ve "yeni HUD döngüsü 0" kuralı.
4. **Göstergenin tek sahibi kittir.**
   - Odak: FocusRing ve köşeler. Seçili: ChosenBar. Disabled: UiButton'ın çifti.
   - O1'in köşe, kısa alt çizgi ve geniş alt çizgi işaretleri, kit göstergesi olan kontrolde çizilmez (IK-M7).
   - Kitte üç boşluk var; ikon v2 bunlar kapanmadan bitmez (interaction-motion §5):
     - kelimesiz ikonda disabled bugün yalnız tonla gösteriliyor;
     - bildirim noktası kitte yok;
     - odaktaki disabled kontrolde halka gizleniyor (UiButton.cs:390).
5. **Malzeme mimarisi** (unity-material §1–§5):
   - DD/UI/Fx içindeki Icon modu (8) baştan yazılır; ayrı shader yok.
   - Eğim atlas uzayında `tex2Dlod` taplarıyla alınır (ddx/ddy değil). Piksel ayak izi filtresi `wb = max(fwidth, 2·AP)`.
   - Işık sRGB-kodlu uzayda hesaplanır. Son adım kanal bazında `max(lit, tint)` olur ve premultiply'dan önce uygulanır (IK-M5).
   - Soğuk lob ve glow `sd ≤ −w/2` bölgesindedir; hiçbir glif pikseline girmez.
   - Parametreler mevcut uv1–uv3 kanallarında taşınır: uv1 = (8, sheen, glow, spread), uv2 = (draw, ışık açık/kapalı, opacity).
   - 0 yeni anahtar kelime, 0 sampler, 0 materyal kopyası; MPB yok. Beklenen Δ SetPass 0, A/B ile ölçülecek.
   - Kademe: Sinematik = Yüksek = tam malzeme; Performans = düz token.
   - `Canvas.vertexColorAlwaysGammaSpace` açılmaz.
6. **Hareket** (interaction-motion §1–§4):
   - Dört kalıcı bayrak (lifted, chosen, interactable, notified) + tek atımlık izler + iz sonunda 120 ms settle.
   - Yeni iz son gösterilen transformdan başlar.
   - Öncelik: disabled > basma (50 ms temas korumalı) > seçilme > unlock > bildirim > hover > idle.
   - Sürücü `UiMotion.Tick` içindeki tek havuzdur; ikon başına Update yok (T6 kilidi). Ayırma 0 B.
   - Bekleme hakemi: yalnız odaktaki tek ikonda, odak bölümü başına bir döngü. Bastırılır: RM, odak dışı pencere, perde, başka ikon izi, açık pane, HUD ya da Performans kademesi.
   - RM: her şey `Still()`, 0 ms. Flaş azaltma: sheen ve glow × FlashScale.
   - Pad basma için `PressPulse` eklenir; bugün Submit basma durumu üretmiyor.
   - Ray seçili durumu ikona bağlanır; bugün `SetSelectedChoice` ikonu duymuyor.
7. **Alias:** her alias ayrı hücredir; SDF bayt-eşit, manifestte `aliasOf` alanıyla.
   - Rect paylaşımı yok; UiArtThemeTests.cs:386-389 bunu yasaklıyor.
   - Okuyucu adı string tablosundaki `nameKey`'den gelir.
   - Aynı ekranda aynı geometrili iki kontrol görünüyorsa ikisinin de görünür kelimesi olur (IK-M17).
8. **Kit boyut/isabet sözleşmesi değişmez:** 24/32/40 → 44/48/56.
   - Glif tabanı 24 canvas px. Altında kalan çağrılar 24'e çıkar (lider §1.2).
   - Ray boyu 32'de kalır. Daha büyüğü gerekirse AD B5a karesinde söyler (K7).

## 2. Dilimler

Her dilim ayrı commit + kanıt + AD turu + gt.

| Dilim | İçerik | Kapı |
|---|---|---|
| **B5a** G1 birleşimi + eşleme (menü, paneller, Results; HUD hariç) | 22 anahtar yerinde değişir (ray 8, Menu.Back, Unlock.Title/Badge ve test-dışı tüketicisi olmayan 11 anahtar), 30 anahtar eklenir (29 + State.Locked). Bayt-eşit kalanlar: 137 G1 içerik glifi + Hud.* beşlisi + Medal.Locked + Shop.Purchased = 144 hücre. | IK-M1 (58/58 ad çözülür; O1 geometrisi 51 + State.Locked), M2, M3, M6, M8a (kilit şekli), M9 (Back ve ayar yardımcısı 22 → 24), M10–M13, M17; tema = manifest testi; O1 kenar payı ≥ 4 px; alias bayt-eşitliği; tüketici taraması yeni ad alanlarında; U2 genişletilmiş (ikon–kelime çakışması, tooltip taşması, 8 odak, glifin hit içinde kalması); AD: M1–M5, M6 (yalnız erişilebilirlik ikonları), M7, M8 (Back 24 + karusel), M10; **Ş1:** S boyutundaki her O1 kontrolünün 4x3 karesi (AD "okunmuyor" derse kelimesiz S → M, hit 48, U2 yeniden); S-07'ye G1 paylaşım bildirimi (S-07'nin 3 hücresi bayt-eşit) |
| **B5b** Malzeme | Icon modu 8, kanal düzeni, renk tabanı testi (UiIconMaterialTests T1–T5) | IK-M5, M15, M16 (koruma), M18, S6; **Ş2:** Player karesinden glife bitişik halka ölçümü: 5 oran × S01-t00.00 × odak {Play, Accessibility, Quit} × {dinlenme, odak, seçili}, halka ≥ düz yolun aynı öğesi ve ≥ 3:1 (D3/D7 AD kararı ölçümden önce); maliyet A/B (SetPass, rebatch) |
| **B5c** Hareket | UiIconMotion (taslağın 3 ciddi hatası düzeltilir: tüm pozun ezilmesi, sıcak yolda Resources.Load, her glifte süren sheen döngüsü) + hakem + 3 kit boşluğu | IK-M7, M14, M16 (koruma), M8a'nın disabled parçası (kelimesiz disabled şekli); EditMode T1–T15; RM Player karesi; kanıt E1–E9 |
| **B5d** HUD (ertelendi, 🔴-1) | `Hud.*` beşlisi, S-07 ile birlikte; O1 düşük can kararından sonra | IK-M4, M8b (düşük can), M9 (Enemies 20 → 24, palet örneği), M16; IK-S1 (gold, enemies); AD H1–H5, M6 palet kalbi, M9 duraklat |

## 3. 🔴 C-00 kararları (önerilen varsayılanla)

| # | Konu | Öneri |
|---|---|---|
| 🔴-1 | HUD glifleri bu işte değişsin mi? | **B5d'ye ertelensin.** Gerekçeler: <br>• `Hud.Health` O1'e geçerken `Hud.LowHealth` AP-012'de kalırsa HUD'da aile bölünür (RunHudScreen.cs:1431). <br>• O1 sette düşük can glifi yok. <br>• S-07 açık değil; HUD işiyle çakışmamalı. <br>Lider notu B5a'da düz SDF olarak almayı önermişti; design-systems ertelemeyi önerdi. S-09 ertelemeyi seçiyor. |
| 🔴-2 | Düşük can glifi | AP-012 çatlak kalp korunur (prior AD MUST-6, MainMenuScreen.cs:1772-1779). O1 "health_low" deltası gerekiyorsa Codex'e iş. |
| 🔴-3 | `Shop.Locked`, `Notice.Warning/Error` | B5a'da AP-012'de kalır; AD bakışından sonra delta kararı. |
| 🔴-4 | Hareket token'ları | 640 ms → 600 ms'ye kırpılır (son 40 ms'de Δ görünmez: Δsheen ≤ 0,005). 3,6 s bekleme izi DEC olarak yazılır. `IconIdleDelay` 2,4 s `[PrototypeOnly]` olarak eklenir ya da C-00 bir sayı verir. in_out easing için yeni token gerekir. |
| 🔴-5 | Gösterge sahibi | Kit (§1.4). AD imzası gerekir. |
| 🔴-6 | Flaş azaltma katsayısı | **Konsey uzlaştı: 0,4** (kitin tek politika noktası; lider eş imzada katıldı). C-00 bilgisine. |
| 🔴-7 | README ↔ referans kod tutarsızlıkları (unity-material §6.3) | D1 sheen açısı: kod (~21,5°). <br>D2 spread birimi: `render()` etkisi. <br>D3 glow: doğrusal karşılık ≈ 0,015–0,02; yarıçap AD'de. <br>D4 rim: 0. <br>D5 konusuz (viewBox'ta S ölçeği 1,00×). <br>D6 sheen tavanı: yeni token gerekir (GLINT_MAX 0,25'i aşıyor) ya da 0,25'e kırpılır. <br>D7 lob rengi #293B46: AD. |
| 🔴-8 | design-systems K3 / K4 / K5 | K3: O1 locked → `State.Locked`. <br>K4: `Shop.Purchased` değişmez. <br>K5: alias çakışmalarında "görünüm başına bir üye ya da ikisi de kelimeli" kuralı + AD istisna listesi. Gerekirse Codex'ten ayrı geometri. |

## 3a. SHOULD'lar (Ş4)

| Kimlik | Konu | Dilim |
|---|---|---|
| IK-S1 | Kör tanıma (kelimesiz ray okuması, 4:3 S; ux-researcher protokolü, C-00 eşiği) | B5a sonrası |
| IK-S2 | Tooltip gecikmesi: kod 0,3 s (UiIconButton.cs:55) ↔ token 350/500 ms | B5a |
| IK-S6 | Kelimesiz ray ikonları ≥ 4,5:1 | B5b |
| IK-S7 | Gerçek Mac çözünürlüğünde U2, 8 odak | B5a |

Kapananlar: IK-S4 (UiNotice'e bugün glif veren çağıran yok), IK-S5 (Medal.Locked ve Shop.Purchased değişmiyor; O1 kilit `State.Locked`).

## 4. İcat edilmeyecekler

- İkinci runtime sayfası.
- S-09'un kendi çizdiği O1 geometrisi. Eksik glif (düşük can, Display, Gamepad) C-00 üzerinden Codex'e gider.
- Mevcut G1 anahtarlarının yeniden adlandırılması.
- Yeni odak göstergesi.
- Yalnız renkle verilen durum.
- Onaysız `UiDuration` / `UiEase` token'ı.
- B5a'da HUD değişikliği.
- Unity/URP/paket değişikliği.

## 5. Kanıt ve AD

- Kareler `cine-player.sh` v2 ile alınır (scratch-frames; evidence'a ölçüm + ≤ 12 örnek).
- AD kareleri M1–M10 (lider §5.2). HUD kareleri H1–H5 B5d'ye kalır.
- Ölçümler motor içindedir: cine-contrast, doğrusal UiContrast, Player karesi. AP-028'in sRGB `icon-metrics.json` ölçümü kanıt sayılmaz.

## 6. İmzalar

- Lider (ui-ux-designer): **İMZA (şerhli Ş1–Ş4)**, 06.10. Şerhler §2 ve §3a'da işlendi.
- Eş imza (design-systems-engineer): **İMZA (şerhli)**, 06.10. §8.1 sabitleri §1.2'de, §8.2 kapı bölünmesi §2'de işlendi.
- C-00: 🔴-1…8
