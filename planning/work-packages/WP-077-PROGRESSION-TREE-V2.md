M4-R1 · WP-077 — Gelişim ağacı v2: uzun vadeli, dallanan ağaç (tasarım + mantık + ekonomi)
Paket sahibi: S-04 · Ana koordinasyon: C-00
Kayıt: bu promptu birebir `planning/work-packages/WP-077-PROGRESSION-TREE-V2.md` olarak kaydet.

ÖNCE OKU: `~/Desktop/KADRO/context/projects/deaths-demolition/m4-r1/M4-R1-REVIZYON-TURU.md`
- §0: kullanıcı notları birebir. Bu paket N14'ü ve N12'nin ekonomi tarafını karşılar.
- §4: ortak kurallar.
- Dal: güncel main'den `wp-077-progression-tree-v2`.

Kullanıcı isteği (birebir): "geliştirme ekranı çok daha kaliteli, detaylı, uzun vadeli dallandırılabilir ağaç şeklinde ve hiç kaliteden ödün vermeden oyuncuyu gelişim hissine teşvik edecek şekilde olacak."

🔴 #4 ağaç yapısı kararı bekleniyor. Varsayılan (öneri) iki katman:
1. Koşu içi dallanan ağaç. Altınla alınır, mevcut tur-sonu dükkânı (WP-004/008, UpgradeCard, WeaponBranchContract: çekirdek 6 / ortak 4) ağaç düğümlerine dönüşür.
2. Kalıcı meta ağaç. Koşular arasında sürer; profil XP/kazanımla açılır: 10 silahın kilidi, Special eğilimleri, pasifler.
Belgeyi bu iki katmanla tasarla. Karar farklı gelirse C-00 bildirir; o zaman tek katmana indir.

FAZ A — Fable 5.1 (game-designer + product-designer)
Paydaşlar:
- S-02 WP-069 kimlik sayfası: 10 silah + 2 Special, fiyat ve kilit.
- S-09: ekran gereksinimleri.
Çıktı: `planning/work-packages/WP-077-PROGRESSION-TREE-V2-DIRECTION.md`. İçerik:
- Düğüm tipleri, dallar, derinlik, ön koşullar ve karşılıklı dışlama.
- Uzun vadeli tempo (onlarca saat), kaynak/sink.
- Ekonomi simülasyonu: WP-041 aracıyla.
- Başarım bağları: WP-023/035.
- Respec politikası.
- Ekran gereksinimleri (S-09'a): düğüm durumları, yol vurgusu, önce→sonra önizleme (U11), animasyonlu açılış.

FAZ B — Opus 5.5
- Veri (SO) + mantık.
- Kayıt şeması göçü: WP-009H exactly-once kuralları, profil dayanıklılığı WP-063.
- Testler.
- Ekranı S-09 kurar; veri yüzeyini ona sen verirsin.

Sayılar konseyin önerisidir. C-00 onayıyla DEC-09x olur; numarayı C-00'dan iste.

TESLİM: C-00'a ≤10 satır. Her dilim ayrı commit.
