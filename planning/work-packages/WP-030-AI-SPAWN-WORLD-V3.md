# WP-030 — AI & Spawn on World v2/v3: bölgesel NavMesh maliyetleri, kapı/köprü/koridor geçiş davranışı, gerçek gövdelerle sürü akışı, spawn/rise entegrasyonu, 40-zombi pathing perf

Paket promptu (C-00, 2026-09-16, HIZLI HAT; S-03 sahip). Kısaltılmış kayıt — tam metin C-00 mesajındadır.

- **Başlangıç:** main `b0d5ee9a` (`wp-028-enemy-versions-bbb`). Branch `wp-030-ai-spawn-world-v3`. WP-020 (dünya v3 delta) merge olunca rebase + v3 üstünde doğrulama tekrar.
- **Amaç:** gerçek dünyada zombi akışını doğru ve okunur kılmak: bölgesel NavMesh maliyetleri (köprü/koridor/kapı), boğazlarda tıkanma önleme (DEC-033 gerçek gövde boyutlarıyla), kapı/portal geçişleri, spawn görünürlük/rise entegrasyonu, 40 zombi pathing perf bütçesi.
- **Bağlayıcı:** DEC-021/022 tabloları, hız değerleri, aktif düşman tavanı (40) değişmez; maliyetler yalnız rota tercihini çeşitlendirir (REQ-112), erişilebilirlik 12/12+12/12+49/49 korunur; ayrışma yarıçapı fit'e göre, çarpışma kapsülü aynı; boğazda kuyruk (bekleme/alternatif) — kuşatma tehdidi (DEC-032) korunur; rise sırasında aktif sayım (DEC-023); spawn görünürlük ≥ %60 kamera dışı (teknik varsayılan); pathing ≤ 1,5 ms @ 40 (ort.), GC 0. Sahne dosyası (S-05), animator (S-06), UI, Packages dokunulmaz; yan yaklaşım steering YOK.
- **Kapsam:** A `RegionNavProfile` + bake-sonrası atama · B boğaz/kuyruk + fit yarıçapı + kapı/köprü testi · C spawn görünürlük + rise kancası · D 40×5 stres, tıkanma ≤ %2, perf · E testler · F planning/04 + ADR-030-1 + prompt kaydı.
- **Test/Build:** EditMode + PlayMode; release build bir kez + e1/e4 (slot); restore 6/6 (plist notu).
