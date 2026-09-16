# WP-033 — Threat Director: tehdit/kuşatılma metriği, müzik/ambiyans yoğunluk beslemesi (DEC-065), HUD savaş uyarısı sinyali (REQ-105), dünya v3 yeniden doğrulama

Paket promptu (C-00, 2026-09-16, HIZLI HAT; S-03 sahip). Kısaltılmış kayıt — tam metin C-00 mesajındadır.

- **Başlangıç:** main `a5a05488` (`wp-030-ai-spawn-world-v3`). Branch `wp-033-threat-director`. WP-020 merge olunca WP-030 testleri v3 üstünde tekrar (bölge kutuları GATE-N 5,2 m).
- **Amaç:** oyun durumundan tek bir "tehdit" sinyali (0–1 + bileşenler: yakın düşman/açı dağılımı = kuşatılma, tier varlığı, round ilerlemesi, oyuncu can oranı, saldırı hazırlığı) türetip müzik (DEC-065), ambiyans/vurgu, HUD uyarı (REQ-105: kuşatılma/arkadan saldırı/düşük can) ve S-07 game feel için deterministik, GC 0, ≤ 0,05 ms/kare bir servis (ADR-033-1).
- **Bağlayıcı:** yalnız okuma; histerezis + yumuşatma; kuşatılma = 8 dilim, ≥ 5 dilimde ≤ 6 m düşman (teknik varsayılan); arkadan saldırı = arka yarım dairede saldırı hazırlığı; round arası intensity ≤ 0,15 (DEC-065); ayarlar ScriptableObject; senaryo tabanlı test. Hasar/ekonomi/AI kararları, müzik/HUD dosyaları (S-08/S-09), Packages, sahne dokunulmaz.
- **Kapsam:** A `ThreatDirector` + `ThreatSnapshot` + ayar · B `IThreatSource` + S-08/S-09 bağlantı noktaları (adaptör, S-08 dosyasına dokunmadan) · C testler (EditMode senaryo, PlayMode 1 round + GC + maliyet) · D WP-020 v3 yeniden doğrulama (merge sonrası) · E planning/04 + ADR-033-1 + prompt kaydı.
- **Test/Build:** release build + e1/e4 (slot); restore 6/6.
