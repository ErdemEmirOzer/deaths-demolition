# CX-AP025-B — Zombiler "Boğulmuş Muhafızlar" (O1): yüz, ten, beden ve kan üretimi · Faz B

> Önce `K/c-00/codex/CODEX-SISTEM-R2.md` ve `K/c-00/codex/KADRO-CODEX-AJANLARI.md`'yi oku (K = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition`). Sondaki **HIZ MODU 1–7 bağlayıcı**: ayrı inceleme alt ajanı yok, kaliteyi lider kontrol eder, teslimden önce tek mekanik `dd_validator`. Alt ajanlar üretimde paralel.
> Paket: **AP-025** · Alan: `K/asset-production/AP-025/` · Faz A raporun: `K/c-00/codex/reports/CX-AP025-A-RAPOR-05.10-1.md` · Spesifikasyon taslağın: `01-design/AP025-A-uretim-spesifikasyonu.md` · Tur planı: `K/c-00/R2/R2-PLAN.md` (N2-03)
> Higgsfield ve Adobe MCP sınırsız (ledger zorunlu). Blender `tools/slot.sh` ile; disk ≥ 15 GB.

## Kullanıcı kararı (05.10, G3)
Seçilen yön: **O1 — Boğulmuş Muhafızlar / Drowned Retainers**. Yağmurla doygun indigo kumaş, gri-mor çürümüş ten, sarkık duruş, ıslak kale hizmetkârları. Yüzde yara kenarı, diş, donuk göz, seçici ıslaklık. Palet `#B5B6AD · #8C9292 · #69666D · #2F3948 · #787160 · #661418` (ilk üç ten; kan `#661418`). Kullanıcı notu N2-03: "AAA zombiler + sinematik figürler"; bol yara ve kan istendi.

## Faz A'dan bu yana değişen (önemli)
- **Materyal teşhisin doğrulandı ve düzeltildi.** S-03 ölçtü: 984 alt-mesh'in 456'sı (%46) yanlış atlasla geliyordu. 240 kumaş parçası gövde atlasıyla çiziliyordu (yeşil-gri görünüm), 216'sı hiç çizilmiyordu. Ayrıca havuz renderer'ı eski ikinci materyali tutuyordu.
- Düzeltme main'de (e612d12b). Yanlış atlas artık 0/1016.
- Önce/sonra kareleri: `K/wp-088/evidence/ap025-material/compare/`.
- **Bugünkü kalan açık:** baş. 267 vertex yüzsüz manken başı, ten yeşilimsi. Bu faz tam olarak bunu çözer.

## Kapsam
1. **Baş:** 3 modüler anatomik baş (sculpt → retopo → ayrı baş UV ve bake). Yara kenarı, diş, donuk göz, ıslak saç. 3 ten tonu (O1 paleti). Baş uzaktan (üstten 50°, 64–100 px) okunur kalsın: büyük değer kütleleri, mikro ayrıntı ikinci planda.
2. **Gövde:** O1'in istenen ağır, suyla şişmiş bedeni (Faz A riski: görsel dar kaldı). Mevcut modüler giysi sistemiyle (AP-007/AP-007C kıyafetleri, AP-014 tier parçaları) uyumlu kalır. Tier parçaları korunur; Executioner ölçeği 1,48.
3. **Ten ve maske sistemi:** wet / blood / skin / dirt maskeleri. Kan ten tint'inden sonra uygulanır. 3 ten × 3 kir × 3 kan = 27 varyasyon.
   - **MaterialPropertyBlock KULLANILMAZ** (SRP Batcher'dan çıkarır).
   - Oyun tarafı (S-03 + S-05): doku dizisi; zombi başına indeks renderingLayerMask'in 16–31 bitlerinde; `DD_TierLit` shader'ında `_ZOMBIE_MASKS` anahtarı.

## Teslim sözleşmesi (S-03, `K/wp-088/R2-ZOMBI-V2-ALIM.md` §8 — bağlayıcı)
- **3 yüz:**
  - aynı iskelet, ≤ 74 kemik;
  - tek boyun dikişi: her fit'te aynı halka;
  - 4 LOD;
  - ten atlası TEK materyal: `M_Zombie_Atlas` UV alanı ya da yeni tek ten atlası.
  - Oyunda yeni "face" slotu olarak takılır (+1 renderer); dikiş tekse gövdeye birleştirme alternatifi de var.
- **O1 bedeni:** fit **C** oranlarıyla. Mevcut giysi varyantları yeniden kullanılır; yeni fit D açma.
- **Maskeler (C-00 05.10, S-03 ölçümüyle sıkılaştırıldı):** 3 × 3 (ten / kir / kan), **1024², tek kanal, BC4. Toplam ≤ 12 MB** (9 dilim × ~0,7 MB).
  - Gerekçe: zombi doku toplamı bugün tier parçaları dahil 77–88 MB; R2 Z3b sınırı 96 MB.
- **Ten atlası mevcut gövde atlasının YERİNE geçer, eklenmez.** Yeni O1 ten atlası (2K ×2 + 1K ×2 düzeyinde) eski gövde atlası setinin yerini alır (≈ 27 MB serbest kalır). Yüzlerin ve maskelerin toplamı bu payın içinde.
- **Doku toplamı kapısı:** 40 zombi sahnesinde zombi dokusu ≤ 96 MB (S-03 Z3b ölçer). Teslim README'sine dosya başına MB tablosu koy (mip dahil, hedef sıkıştırmayla).
- **Göz ve diş ayrıntısı** yüz mesh'inde; ayrı materyal yok.
- Alım notunun tamamını oku; çelişki görürsen raporun "C-00'A SORULAR" bölümüne yaz.
4. **Gore:** bol yara ve kan, seçili ıslaklık. Hedef kan bandı H355–5 / S0,70–0,85 / V0,25–0,45. Uzuvlar bağlı, parlayan göz yok.
5. **Sinematik figür seti (N2-03):** tek kahraman-kalite zombi render'ı (yakın portre + tam boy) ve 4 tier sıralı render. Blender, oyun ışığına yakın.

## Teknik sınırlar (R2 kapısı Z3, bağlayıcı)
- Karakter dokusu ≤ 2048 px; aile başına atlas ≤ 2; atlas başına harita ≤ 3 (`_BaseColor`, `_Normal`, `_ORM`); aile başına materyal ≤ 4.
- Kemik ≤ 74. Mevcut 73 joint iskeletini (56 ana + 17 ikincil) koru; AP-026 rig standardı isim/parent sözleşmesi geçerli.
- Sevk edilen karakter verisi toplamı ≤ 400 MB.
- Faz A bütçe taslağın (üçgen, LOD) üst sınırdır: Standard 24.000 / 12.000 / 5.000 / 1.500 · Special 28.000 / 14.000 / 6.000 / 2.000 · Elite 32.000 / 16.000 / 7.000 / 2.500 · Ultra 36.000 / 18.000 / 8.000 / 3.000. 40 zombi kalabalık hesabını güncelle.
- FBX 7.4 binary, metre, leaf bone kapalı, ≤ 4 ağırlık.

## Teslimatlar
- `exports/v001/`: 3 baş (LOD0–3), gövde varyantları, ten/maske dokuları, materyal tablosu (`README`), Unity alım notu (hangi modül hangi slota).
- `06-renders/v001/`: portre, tam boy, 4 tier sırası, 40 kalabalık üstten proxy (Blender).
- `manifest/AP025-manifest-v003.json` + `SHA256SUMS`; `LICENSES.csv`.
- Unity'ye alma yok; entegrasyonu S-03 + S-05/S-06 yapar (WP-084).

## Kabul ölçütleri
- Z3 sınırları (tablo) sayılarla.
- Baş: yüz okunurluğu üstten 100 px karede (göz/ağız/yara kütlesi seçilebilir); yakın plan kalite renderı.
- Kan HSV bandı ölçülü (raster değil, doku verisi üzerinden).
- Tier sırası: Standard / Special / Elite / Ultra kendi siluet farkını koruyor (DEC-077 siluet ≥ %30 hedefi; kesin ölçümü Unity'de S-03 yapar).

## Durma ve rapor
- Teslim ve doğrulama bitince raporu yaz ve DUR.
- Rapor: `K/c-00/codex/reports/CX-AP025-B-RAPOR-<gg.aa>-1.md`. Son mesaj tek satır: rapor yolu.
