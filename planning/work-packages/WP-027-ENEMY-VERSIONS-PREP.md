# WP-027 — Enemy Versions Prep: Special/Elite/Ultra kimlik & yetenek öneri notu, spawn karışımı simülatörü, WP-018 saldırı penceresi denge kontrolü

Paket promptu (C-00, 2026-09-16, HIZLI HAT; S-03 sahip). Kısaltılmış kayıt — tam metin C-00 mesajındadır.

- **Başlangıç:** main `00bcde0c` (`wp-017-m3-world-v2-armor-r2`), EditMode 158/158, PlayMode 23/23. Branch `wp-027-enemy-versions-prep`.
- **Amaç:** AP-014'ü açacak kullanıcı kararını hazırlamak (tier kimlik/yetenek/görsel işaret önerisi — karar kullanıcıya), spawn karışımını ölçülebilir kılmak (simülatör; S-10/WP-026 tüketir), WP-018 saldırı penceresi animasyona geçince hasar/denge sayılarının değişmediğini doğrulayan kontrol aracı, WP-011'den devreden `EnemyVersionSliceTests` RNG FAIL'ini kapatmak.
- **Bağlayıcı:** DEC-021/022 tablo değerleri, hasar/zırh kuralları, animator/visual dosyaları (S-06/S-05), Packages/ProjectSettings, planning karar metinleri değişmez; tier yetenekleri = kullanıcı kararı (yalnız öneri); paket/sürüm değişikliği yok; push yok.
- **Kapsam:** A öneri notu + 5 satır özet · B `SpawnMixSimulator` (Editor + EditMode testi + JSON) · C saldırı penceresi karşılaştırma aracı/testi · D RNG FAIL düzeltmesi · E planning/04 + ADR-027-1 + bu kayıt.
- **Durma koşulları:** simülatör oynanış kodu değişikliği gerektirirse (yalnız okuma); RNG düzeltmesi politika davranışını değiştirirse.
- **DoD:** öneri notu · simülatör + testler + JSON · saldırı penceresi tablosu · RNG FAIL kapandı · 04 girişi + ADR-027-1 + prompt kaydı · tek commit + C-00 notu.
