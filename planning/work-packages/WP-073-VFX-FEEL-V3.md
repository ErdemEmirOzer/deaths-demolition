**M4-R1 · WP-073 — VFX ve Game Feel v3**
- PAKET SAHİBİ: S-07. ANA KOORDİNASYON: C-00.
- KAYIT: bu promptu birebir `planning/work-packages/WP-073-VFX-FEEL-V3.md` olarak kaydet.
- ÖNCE OKU: `~/Desktop/KADRO/context/projects/deaths-demolition/m4-r1/M4-R1-REVIZYON-TURU.md`.
  - §0 kullanıcı notları birebir. Bu paket N8 görsel, N10, N16 ve N18'i karşılar; N1 Special VFX'i de bu pakette.
  - §4 ortak kurallar.
- Dal: güncel main'den `wp-073-vfx-feel-v3`.

Kullanıcı isteği (özet):
- Mermiler çok silik ve amatör; projectile olacak.
- Ateş, merminin gidişi ve çarpması profesyonel animasyon + VFX olacak.
- Havada uçuşan taneler daha sinematik, animatik ve yoğun olacak.
- Silahların ve tüm etkileşimlerinin VFX'i eksiksiz olacak.
- Game feel ve juice daha yoğun olacak. Özellikle FP'de yürürken ve ateş ederken ekran sallanması ve recoil dikkatle uygulanacak.

FAZ A — Fable 5.1 (art-director + animation-specialist)
- Girdiler: S-02 WP-069 kimlik sayfası ve ADR-069-1 olay yüzeyi, S-01 WP-070 CameraMode API.
- Çıktı: `planning/work-packages/WP-073-VFX-FEEL-V3-DIRECTION.md`.

A1 · Silah başına profil
- Namlu ateşi: sınıfa göre şekil, ışık darbesi, duman, ısı dalgası.
- Mermi/iz:
  - uzunluk hızla orantılı
  - çekirdek + parıltı
  - her N'de iz
  - FP'de kameraya yakın iz davranışı
- Kovan atımı: havuzlu, hafif fizik, AP-013C kovanları.
- Darbe (yüzeye göre):
  - kıvılcım, toz, kıymık, sıva parçası
  - su sıçraması, yaprak
  - kan/gore (HIGGSFIELD direktifi §10, yüksek)
  - zırh metali kıvılcımı
  - sekme
- Dekallar: havuzlu, tavanlı.
- Launcher:
  - roket mesh'i, egzoz, duman izi
  - patlama: ateş topu, şok dalgası, enkaz, kurum dekalı, ışık, kamera sarsıntısı

A2 · Special VFX
- DG-2 benzeri:
  - yıldırım küresi
  - dallanan zincir arklar (prosedürel, titreşimli)
  - zombide elektrik çarpması: parıltı, kıvılcım, başından duman
  - zemin yanığı
- Sliquifier benzeri:
  - izli jel mermisi, şap sıçraması
  - animasyonlu parlak birikinti yüzeyi
  - kayma izleri
  - zombide jel damlaları

A3 · Atmosfer taneleri (N10)
- Yoğun, sinematik ve animatik: curl noise, rüzgâra bağlı.
- Güneş huzmelerinde parlarlar (S-05 WP-075).
- Bölgeye göre çeşitlilik.
- Göz hizasında da iyi görünmeli (TP/FP).

A4 · Juice v3 (mod başına)
- FP:
  - yürüme sallanması / salınımı (ölçülü)
  - silah salınımı
  - ateşte kamera tepmesi + recoil (desen S-02 verisinden; görsel tepme ile gerçek nişan ayrı)
  - toparlanma, ADS, iniş
- Hepsinde:
  - mikro hitstop
  - öldürme onayı
  - yönlü hasar efekti (S-09 ile)
- TP daha hafif; tepeden mevcut ayar.
- Erişilebilirlik: sarsıntı kaydırıcısı ve azaltılmış hareket korunur.

FAZ B — Opus 5.5 uygulama
- Kod havuzlu olacak, 0 B/kare.
- Kalite kademeleri (§4.7), kademe başına ms raporu.
- Testler: değişen süit.
- Kanıt: üç kamera modunda kareler/GIF'ler + Fable AD turu.
- Eski 3 Special VFX'i S-02 kaldırmasıyla birlikte çıkar.

Perf: §4.8.

TESLİM: C-00'a ≤10 satır. Dilim başına ayrı commit.
