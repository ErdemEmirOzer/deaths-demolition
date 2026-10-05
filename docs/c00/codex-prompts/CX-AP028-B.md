# CX-AP028-B — Menü v4: AAA ikon ailesi (derinlik, yansıma, motion design) + sinematik harita arka planı + yükleme finalleri · Faz B

> Önce `K/c-00/codex/CODEX-SISTEM-R2.md` ve `K/c-00/codex/KADRO-CODEX-AJANLARI.md`'yi oku (K = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition`). Sondaki **HIZ MODU 1–7 bağlayıcı**: ayrı inceleme alt ajanı yok, kaliteyi lider kontrol eder, teslimden önce tek mekanik `dd_validator`.
> Paket: **AP-028** · Alan: `K/asset-production/AP-028/` · Faz A raporun: `K/c-00/codex/reports/CX-AP028-A-RAPOR-05.10-1.md` · Envanter: `01-design/AP028-A-envanter.md` · S-09 metin envanteri: `K/wp068s9/WP-081-B3-METIN-ENVANTERI.md`
> Higgsfield ve Adobe MCP sınırsız (ledger zorunlu).

## Kullanıcı kararları (05.10)
- **İkon ve menü dili: O1 Fırça mürekkebi.** Kaynak: `02-concept/options-v001/O1.png` + `main-menu-O1.png`.
- **Yükleme sanatları: L01, L02, L04, L05, L08.** L07 seçilmedi; L04 seçildi.
- **Ana menü ilk açılışta Profil paneli: kapalı.** Profil rotası korunur.
- **YENİ, kullanıcı (05.10):**
  1. "Menüde arka planda hareketli mapimizi gösteren sinematik görüntü olacak ve AAA kalitesinde olacak."
  2. "Tüm ikonlara motion design eklenecek ve ikonlara derinlik ve yansıma ile AAA kalitesine çıkarılacak."

## Adımlar

### 1. AAA ikon ailesi (O1)
- **Aile:** 35 işlev + S-09 §5 eksik listesi (stat, kostüm, yol, seviye/XP, atış modu, dil/görüntü/veri/ipucu/kamera, ana menü, açık kilit). Hepsi tek 64 birim ızgarada, semantik SVG kaynaklı.
- **Oyun içi atlas (S-09 alım kuralı, `UI/Art/README`):** beyaz tek renk glif, 64² SDF, spread 6, Alpha8, 24 px ızgara, 24 px'te 2 px çizgi. Tint shader'da yapılır.
- **Derinlik ve yansıma (AAA):** her ikon için SDF'ten türetilmiş yükseklik/normal haritası (bevel; fırça mürekkebi kalınlığı ve kâğıt/mühür kabartması hissi).
  - Materyal spesifikasyonu (S-09 uGUI shader'ında uygular):
    - bevel derinliği;
    - üst-sol sıcak ışık / soğuk gölge (oyunun ışık dili);
    - rim;
    - parlaklık süpürmesi (sheen) ile yansıma;
    - ıslak mürekkep parlaklığı.
  - Her durum için referans render: normal / hover-focus / basılı / seçili / devre dışı.
  - 24 px'te okunurluk korunur: derinlik efekti glif kontrastını düşürmez (kontrast oranını ölç ve yaz).
- **Motion design (tüm ikonlar):**
  - Durum başına animasyon: hover/focus, basma, seçilme, devre dışı, kilit açılma, bildirim.
  - Gerekirse ince bir bekleme hareketi; yalnız seçili ya da odaktaki ikonda, dikkat dağıtmayacak kadar.
  - Fırça/mürekkep diline özgü: çizgi akışı (stroke draw), mürekkep yayılması, mühür basma.
  - Çıktı:
    - motion spesifikasyonu (`01-design/AP028-B-motion.json`: süre ms, easing eğrisi, ölçek/öteleme/dönme/sheen ilerlemesi/glow; durum başına anahtar kareler);
    - her ikonun önizleme videosu (MP4 ya da PNG dizisi).
  - Ortak motion token'ları (kısa / orta / uzun süre, 2–3 easing) tanımla.
  - "Hareketi azalt" ayarında: yalnız opaklık ya da anlık durum değişimi.

### 2. Sinematik harita arka planı (ana menü, AAA)
- **Hedef:** ana menünün arkasında oyunun GERÇEK haritası (World V6, kale) üzerinde, oyun motorunda gerçek zamanlı akan sinematik kamera. Video değil, motor içi (S-09 + S-05 + S-07 uygular).
- **Senin teslimin: tasarım ve previz.**
  - 4–6 çekimlik çekim listesi ve storyboard: avlu, kapı, bahçe köprüsü, iç salon, çatılar/gök gibi gerçek mekânlar.
    - Kaynak: mevcut dünya kareleri `git show main:` ile menü plakaları ve AP-005 yerleşimi; S-05 menü plakası kareleri.
  - Kamera hareketi: dolly / crane / yavaş pan. Çekim başına süre, kesme ya da çapraz geçiş. Toplam 45–90 s, dikişsiz döngü.
  - Kompozisyon: O1 dikey menü solda; sol üçte bir sakin ve koyu kalsın (metin kontrastı).
  - Atmosfer: yağmur sonrası, ıslak arduvaz, sis, ışık huzmeleri, uzakta zombi silueti (isteğe bağlı, Special silah gösterme). Renk derecelendirmesi oyunun "Look B" ıslak arduvaz diliyle uyumlu.
  - Previz: Higgsfield video ya da görüntü ile her çekimin stil hedef karesi ve kısa hareket önizlemesi. Kullanıcıya bunlar gösterilecek.
  - Uygulama notu (S-09/S-05 için): çekim başına kamera başlangıç/bitiş konumu ve hedef nokta. Dünya koordinatlarını mevcut harita verisinden çıkar; çıkaramıyorsan landmark adıyla yaz.
  - Menü sahnesi performans hedefi: menüde oyun içi bütçeyi aşmayan kalite. Ağır efekt listesi ve maliyet tahmini.

### 3. Yükleme sanatı finalleri
- Seçilenler: L01, L02, L04, L05, L08. Masterlar `exports/v001/Loading/` (2560×1440).
- **L04 kimlik kontrolü:** raftaki Special silahlar oyunun gerçek Special silahlarıyla aynı mı?
  - Kaynak: `git show main:planning/work-packages/WP-069-ARSENAL-V3.md` (Special bölümü) + AP-013 / AP-021 silah görselleri.
  - Uymuyorsa L04'ü gerçek Special tasarımlarıyla yeniden üret (aynı kompozisyon ve ışık). Kurmaca silah gösterme.
- **Yazı güvenli bandı (BAĞLAYICI; S-09 05.10 ölçümü: L01/L02/L05/L08 bugün KALIYOR).**
  - Ekranın ortasındaki ilerleme halkası ve etiketler sanatın ÜSTÜNE çizilir; altlarında kutu/scrim yok (R1 kararı).
  - Bugünkü durum: krem halka kontrastı 1,01–1,15:1 (gereken ≥ 3:1), etiketler 1,03–4,26:1 (gereken ≥ 4,5:1). Sebep: orta bantta parlak gök ve huzmeler.
  - **Bağlayıcı ölçü (main'de):** `python3 tools/wallpaper/wallpaper_intake.py --src <master> --name loading_LNN --master-ref <yol> --text-safe --dry-run` → **PASS olmalı**.
    - Araç Unity'deki LoadingKeyArtTests'in birebir kopyası: 4 ekran, cover-fit, itiş 1,0/1,05; halka Ø240, etiket yığını, ipucu şeridi.
    - Sayıları Unity loguyla yüzde birine kadar aynı. Bugün L01: 1,13 / 1,38 / 13,45; L05: 1,04 / 4,26 / 14,33. R1 kurtyard geçiyordu: 3,45 / 5,09 / 10,48.
    - Repo dosyasını okumak için ana repo yolundan `python3` ile çalıştır (dosyayı değiştirme).
  - **Karartma zarfı (yalnız yer gösterir):** x 0,35–0,65 / y 0,30–0,72 (üstten), kelimelerin oturduğu bant. Karartmayı orada **ressamca** yap: kompozisyonu ve sahneyi koru, ışık kaynağını banttan dışarı taşı ya da bandı gölgeye al. Kutu ya da vinyet yok. Gereğinden fazla karartma; ölçü PASS olunca dur.
  - Kullanıcıya önce/sonra kareleri gösterilecek; her sanat için önce/sonra PNG'si üret.
- **Manifest:** S-09'un `codex_manifest_check` koşusu 465 biçim hatası verdi. Faz A konsept yolları `exports/` dışındaydı; opak duvar kâğıtları "UI PNG alfa" kuralına takıldı.
  - B manifestinde teslim dosyaları yalnız `exports/` altında olsun.
  - Duvar kâğıtları opak sınıf olarak işaretlensin (alfa gerekmez).
  - Teslimden önce aracı kendin koş: `git show main:tools/codex_manifest_check.py`.
- **sRGB ICC:** final PNG'lere sRGB profili göm. Faz A'da profil yoktu.
- S-09 bunları `tools/wallpaper/wallpaper_intake.py` ile JPEG q90'a çevirip alır. Bütçe Wallpapers ≤ 10 MB; seçilen beşin q90 toplamı ≈ 3,7 MB.

### 4. Diğer ekranlar v4 taslağı (S-09 kılavuzu)
- Seçim, ayarlar, duraklat, yükseltme ağacı, sonuçlar, başarımlar. O1 dilinde, ikon + az kelime.
- Harf sayısını her taslağa yaz. Hedef: ana menü ≤ 150, seçim ≤ 300 harf. U1 kapısı en az %40 azalma ister.
- Korunacaklar (Faz A envanteri): ayar adları ve değerleri, ekonomik değerler, önce→sonra, kayıp onayı.

## Teslimatlar
- `exports/v002/Icons/`: SVG kaynak, SDF atlas PNG (Alpha8 uyumlu), yükseklik/normal haritaları, motion JSON, önizleme videoları, materyal spesifikasyonu (`README`).
- `exports/v002/MenuCinematic/`: çekim listesi, storyboard, previz kareleri/videoları, uygulama notu.
- `exports/v002/Loading/`: 5 final master PNG (sRGB gömülü), L04 kimlik notu.
- `exports/v002/Screens/`: ekran taslakları + harf sayımları.
- `manifest/AP028-manifest-v002.json` + `SHA256SUMS`; `LICENSES.csv`; Higgsfield ledger.

## Kabul ölçütleri
- İkon: 24 px'te okunurluk (Faz A ölçüm yöntemi); derinlik efektiyle glif/arka plan kontrastı ölçülü ve en az Faz A düz glif kadar.
- Motion: her durum için spesifikasyon + önizleme; azaltılmış hareket varyantı tanımlı.
- Sinematik: döngü süresi, çekim sayısı ve sol üçte bir sakin alan kanıtı (previz karelerinde).
- Yükleme: 5 master, sRGB gömülü, L04 kimlik kararı yazılı.

## Durma ve rapor
- Sinematik previz hazır olunca raporun dışında `exports/v002/MenuCinematic/HAZIR.md` bırak; C-00 bunu kullanıcıya önizleme olarak gösterir. Diğer işlere devam et.
- Tüm teslimatlar bitince raporu yaz ve DUR.
- Rapor: `K/c-00/codex/reports/CX-AP028-B-RAPOR-<gg.aa>-1.md`. Son mesaj tek satır: rapor yolu.
