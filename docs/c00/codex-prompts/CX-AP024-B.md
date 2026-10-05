# CX-AP024-B — Hero v2 "Kırık Mühür" (O1): üretime hazır karakter · Faz B

> Önce `K/c-00/codex/CODEX-SISTEM-R2.md` ve `K/c-00/codex/KADRO-CODEX-AJANLARI.md`'yi oku (K = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition`). Sondaki **HIZ MODU 1–7 bağlayıcı**: ayrı inceleme alt ajanı yok, kaliteyi sen (lider) kontrol edersin, teslimden önce tek mekanik `dd_validator`. Alt ajanlar yalnız üretim için ve paralel çalışır.
> Paket: **AP-024** · Üretim alanın: `K/asset-production/AP-024/` · Faz A raporun: `K/c-00/codex/reports/CX-AP024-A-RAPOR-05.10-1.md` · Tur planı: `K/c-00/R2/R2-PLAN.md` (N2-04, N2-05)
> Higgsfield ve Adobe MCP sınırsız; her Higgsfield üretimini `03-higgsfield/ledger.csv`'ye yaz. Blender işleri `tools/slot.sh` Blender slotuyla; disk ≥ 15 GB.

## Kullanıcı kararı (05.10, G1)
Seçilen yön: **O1 — Kırık Mühür / Shattered Seal**. Kaynak: `02-concept/options-v002/O1.png` + `options.md` §O1. Kalenin koruma mührü bedene dönüşmüş: kırık elips seramik maske, sırtta kırık çember, dar üst gövde, bölünmüş seramik kalça kabukları, turkuaz sır imzası `#4C8D8A`. Palet: `#242A2E · #444E53 · #D6D8CE · #748A84 · #4C8D8A · #AAA395`.

Bu kahraman oyundaki tek oyuncu karakteridir. Kullanıcı notu (N2-04/05): "fantastik, kurgusal yeni kahraman, interaktif tasarlansın; silahlar omuza oturmalı, reload şarjör animasyonu doğru olmalı."

## Faz B'de çözülecek, Faz A'da açık kalan riskler
1. **Değer ayrımı.** Faz A ölçümünde gövde kontrastı eşik altı: torso proxy 0,0324 FAIL, full 0,0076 FAIL. Islak parlak arduvaz zeminde açık seramik tonları yakınlaşıyor. Oyun kapısı DEC-077: oyuncu/zemin ΔY ≥ 0,06 (Unity'de ölçülecek). Kimlik bozulmadan büyük bir değer kırılımı tasarla: ör. koyu alt katman / koyu kuşak / sırt çemberinin arkasında koyu dolgu.
2. **Birinci şahıs.** Bilek sargıları ve el sırtı seramik parçaları; 12 silahın tutuşunda ve reload'da (sol el → şarjör) çarpışma yok.
3. **Kalça plakaları.** Koşu ve çömelme açıklığı, şarjör yolu (sol el kalçaya iner).
4. **Silah oturuşu.** Tüfek dipçiği sağ omuz cebine, yanak hattına oturur (kullanıcının "silahlar omuza" notu). 12 silah listesi ve kimlikleri: `git show main:planning/work-packages/WP-069-ARSENAL-V3.md` + AP-021 klasik silah sayfaları.

## Adımlar

### Adım 1 — Rafine sayfa + değer varyantları (YUMUŞAK KAPI G1b)
- O1'in nihai turnaround'u: ön / yan / arka / 3/4, baş ve maske yakın planı, FP kollar (iki el tüfek tutuşu + reload'da sol el), üstten 50° oyun görünümü (100 px ve 64 px küçültme kanıtıyla).
- Değer çözümü için **3 küçük varyant** (yalnız renk/değer dağılımı farklı, biçim aynı). Her biri ıslak arduvaz zemin üstünde üstten 100/64 px ölçümüyle.
- Kısa kapı notunu `01-design/AP024-B-G1b.md` olarak yaz ve raporun "C-00'A SORULAR" bölümünde bildir.
- **DURMA:** kapı yumuşaktır. Renkten bağımsız işler (Adım 2 biçim/topoloji/rig) devam eder. Doku/renk finali G1b kararını bekler; bekleme sırasında öneri varyantla ilerle, karar gelince değiştir.

### Adım 2 — 3B model
- High → game-ready low. Kaynak üretim aracı serbest (Higgsfield 3D, Blender); temiz topoloji, deformasyon bölgelerinde (omuz, dirsek, bilek, kalça, diz) yeterli halka.
- LOD0–3 üst sınırları (üçgen): **LOD0 ≤ 35.000 · LOD1 ≤ 18.000 · LOD2 ≤ 8.000 · LOD3 ≤ 3.000**. FP kollar ayrı mesh, ≤ 12.000. Aşman gerekirse gerekçeyi raporda yaz, C-00 onaylar.
- Materyal: aile başına ≤ 4 materyal, ≤ 2 atlas, atlas başına ≤ 3 harita (`_BaseColor`, `_Normal`, `_ORM`), harita ≤ 2048 px. FP kollar aynı materyal setini paylaşır.
- Seramik / dokuma / mat turkuaz sır malzemeleri PBR doğru: seramik düşük metal, orta-düşük pürüzlülük; ıslak dünyada aşırı parlamasın.

### Adım 3 — Rig (AP-026 standardı)
- `K/asset-production/AP-026/exports/v001/AP026-A-rig-standardi-v1.md` + `AP026-bone-map-v1.json` **aynen** uygulanır: 77 default kemik, Humanoid eşleme, 30 parmak, 8 twist (yan dal), nötr anatomik bind/REST (eski başparmak REST 21°→72,8° kusuru düzeltilir).
- Soketler: `socket_weapon_r` (RightHand çocuğu), `socket_mag_l` (**LeftHand doğrudan çocuğu**, avuç ortası), `grip_l` sözleşmesi. Oyun kodu `socket_mag_l`'i rig'de bulursa onu kullanır (S-06 `MagazineSocket`, main'de).
- İkincil zincirler (sec_*): standart bütçe 6–12; seramik plakalar sert, kumaş uçları hareketli.
- FBX 7.4 binary, metre ölçeği, leaf bone kapalı, vertex başına ≤ 4 ağırlık. TP gövde ve FP kollar aynı kemik adlarını taşır.

### Adım 4 — Doku ve görsel final (G1b kararından sonra)
- Doku seti, kararlaştırılan değer varyantıyla.
- Blender'da oyun ışığına yakın render: turntable (360°, 30 fps, en az 120 kare PNG) + üç kamera (üstten 50°, TP omuz arkası, FP) + ıslak zemin üstünde 100/64 px değer kanıtı. Bu renderlar kullanıcıya gösterilecek "sinematik figür" setinin ilk parçasıdır (N2-03).

## Teslimatlar
- `exports/v001/`: `Hero_v2_LOD0..3.fbx`, `Hero_v2_FPArms.fbx`, dokular (PNG, sRGB etiketli BaseColor; Normal/ORM doğrusal), `README` (kemik listesi, soketler, materyal tablosu).
- `06-renders/v001/`: turntable, üç kamera, değer kanıtı.
- `manifest/AP024-manifest-v003.json` + `SHA256SUMS`; `03-sources/LICENSES.csv` güncel.
- Unity'ye ALMA yok; repo'ya yazma yok. Entegrasyonu S-06 (rig/anim) ve S-02 (silah/FP) yapar (WP-082).

## Kabul ölçütleri (ölçülü)
- Üçgen sayıları LOD başına sınır içinde (tablo).
- Kemik adları/parent'ları `AP026-bone-map-v1.json` ile birebir; 0 eksik, 0 fazla (sec_* bütçe içinde).
- Blender'da çömelme / koşu / iki el tutuş / reload pozlarında görünür kesişme yok (pose test kareleri).
- Üstten 100 px ıslak zemin karesinde oyuncu/zemin değer farkı, Faz A ölçüm yöntemiyle torso proxy ≥ 0,06. Kesin kapı Unity'de S-06/S-05 ölçer.
- Harita boyutu ≤ 2048; materyal ≤ 4; atlas ≤ 2.

## Durma ve rapor
- G1b'de durma yok; yalnız rapora yaz.
- Tüm teslimatlar ve doğrulama bitince raporu yaz ve DUR.
- Rapor: `K/c-00/codex/reports/CX-AP024-B-RAPOR-<gg.aa>-1.md`. Kullanıcıya son mesajın tek satır: rapor yolu.
