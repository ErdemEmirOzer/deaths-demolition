C-00 → S-05

0) **Süren işler**
- **WP-038S:** Koşan probu bitir. Bulduğun kazanımı tek commit olarak teslim et. Yeni perf kısma turu açma — perf hedefi 🔴 kararına bağlı (60 fps önerildi).
- **WP-064 son kapısı:** S-10 stab26 sonucunu gönderdiğinde koşulacak; değişiklik yok.

1) **M4-R1 · WP-074 — Dünya v5** ve **WP-075 — Atmosfer ve Görünüm v3**
- PAKET SAHİBİ: S-05. ANA KOORDİNASYON: C-00.
- KAYIT: bu promptu birebir `planning/work-packages/WP-074-WORLD-V5.md` ve `WP-075-ATMOSPHERE-LOOK-V3.md` olarak kaydet.
- ÖNCE OKU: `~/Desktop/KADRO/context/projects/deaths-demolition/m4-r1/M4-R1-REVIZYON-TURU.md`.
  - §0 kullanıcı notları birebir. Bu paket N3, N4, N9, N13, N15 (Lightroom) ve N19 (renk) maddelerini karşılar.
  - §4 ortak kurallar geçerli; kalite kademeleri sözleşmesi §4.7 senin sorumluluğunda.
  - §7 görsel hedefler.
- Dallar: güncel main'den `wp-074-world-v5` ve `wp-075-look-v3`.

**Kullanıcı isteği (özet):**
- Harita daha dolu olacak: koridor sayısı, kapalı alan sayısı, konsepte uygun dekorasyon ve ağaçlar hepsi artacak. Gerekirse harita büyüyecek.
- Çevre ve atmosfer, projenin başında Higgsfield ile ürettiğimiz hedefin **neredeyse aynısı** olacak. Işık, VFX, asset, mesh, grafik ve cisim kalitesi buna göre yükselecek.
- Hava **parçalı bulutlu** olacak; aralardan **güneş huzmeleri** gelecek. Havada parçalı, **çok hafif, bölge bölge sis** olacak.
- Sahne çok daha sinematik ve gerçekçi olacak. Renkler daha canlı olacak.
- Hedef görseller: `wp-015/higgsfield/selected/DD_StyleRef_DEC076_overview_2k.png` ve `DD_StyleRef_DEC076_gameplay_2k.png`.

**WP-074 FAZ A** — Fable 5.1: art-director + seviye tasarımı için game-designer. Paydaşlar: S-03 (spawn/NavMesh), S-01 (kamera modları), P-05.
- Çıktı: `planning/work-packages/WP-074-WORLD-V5-DIRECTION.md`.
- Kapsam:
  - Mevcut dünyayı hedefle karşılaştırarak boşluk analizi: yoğunluk, kompozisyon, saray salonu, kapalı koridorlar (kairō), duvarlı avlular, kapılar, girilebilir köşk ve salonlar, dekor (fener çeşitleri, taş, köprü, çit, araba, fıçı, tapınak eşyası, sancak), ağaçlar (çam, akçaağaç, sakura, bambu yoğunluğu), yosun, çalı, çakıl ve tırmıklı kum, koi havuzu.
  - Büyütme kararı (±45 m'den ne kadar?) ve oyun etkisi: spawn mesafesi, tur temposu, bot, NavMesh.
  - **Göz hizası hazırlığı:** TP/FP için arka yüzler, iç mekânlar, ufuk, siluet ve gök görünürlüğü. S-01'in gereksinim listesiyle birlikte ele alınır.
  - Yerleşim planı ve eksik varlık listesini P-05 AP-023'e bildir. İlk teslim bu olsun.
- **FAZ B:** yerleşim v6; NavMesh v5 (tırmanma 0,30, halka, FallGuard, adacık bağlayıcı kuralları sürer); kamera moduna göre occluder/çatı politikası; yoğun bitki için LOD ve impostor; lightmap ve probe'lar; kademeye göre perf.

**WP-075 FAZ A/B:**
- Parçalı bulutlu gök: hareketli bulut katmanlı gök kubbesi + P-05 gök v2 plakaları.
- Güneş huzmeleri: URP ekran-uzayı ışık huzmesi / god ray render feature.
- Hareketli bulut gölgesi: ana ışık cookie'si.
- Bölgesel çok hafif sis: yerel sis hacimleri + gürültülü yükseklik sisi.
- Canlı renk: Lightroom sınıfı Adobe reçetesi (§4.6). WP-029 yöntemiyle LUT v2 üretilir; Look B, güneş kırılımı sıcaklığı ve canlılıkla evrilir.
- Sinematik post: bloom, hafif film greni, vignette. Islak yüzey yansımaları (probe/planar).
- **Kalite kademeleri v2 sözleşmesi** (Sinematik / Yüksek / Performans) tüm oturumlara yayımlanır. İlk tesliminin parçası.

**KANIT:** Hedef görselle yan yana karşılaştırma (genel + oynanış kadrajı), üç kamera modunda. Ardından Fable AD turu.

Perf: §4.8 (ölç, raporla, kısma). Disk 34 GB: build/Library kararlarında §4.4.

**TESLİM:** C-00'a ≤10 satır. Dilim başına ayrı commit.
