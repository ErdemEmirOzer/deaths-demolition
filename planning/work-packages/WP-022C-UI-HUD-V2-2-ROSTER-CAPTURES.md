# WP-022C — UI/HUD v2.2: Weapon Roster UI, Player Captures, Motion & Quality Polish

> Yürütme promptu (C-00, 2026-09-17) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-022C.

**Oturum:** S-09 · **Model:** UI tasarım/ana hatlar Fable 5.1 (429 → Opus stand-in), teknik Opus 5 · **Kredi:** 0 (AP-012 v003 kredi gerektiren kalemler bekler; mevcut ikon/çerçeve setiyle çalış, Adobe finisaj serbest §7) · **Taban:** main `48292245` (worktree'yi main'e taşı / yeni `wp-022c/`) · **Slot:** `tools/slot.sh acquire unity wp-022c` (≤2 Unity; 2/2 dolu — kuyruk), swap >12 GB ise Unity açma · **Direktif:** §14 UI/HUD kalitesi çok önemli (yüksek kalite kapısı).

## Kapsam
A. **Silah roster UI (WP-034 `IWeaponRosterSource`):** Selection ekranında A/B/C (+D veri, kilitli görünüm) kartları, glif eşlemesi (AP-012 v002 ikon seti + geçici glif için yer tutucu sözleşmesi → AP-013/AP-012 v003), HUD silah/şarjör/pellet göstergesi (pellet sözleşmesi), dükkân dal kapısı görünümü (silaha özgü 3 çekirdek + ortak dallar; kilit/erişilemez durumları), gamepad gezinme; kullanıcı silah kararı gelmeden veri değeri değiştirilmez (yalnız görünüm).
B. **Player yakalamaları:** Selection/Results/Settings/Pause için kanca (`-ddUiCapture <screen>` veya eşdeğeri, batch uyumlu; WP-025 build/smoke ile), 1080p/1440p/21:9 yakalama seti `wp-022/evidence/captures-c/`; oynanış kaydı S-10'da kalır.
C. **AD SHOULD'ları (kredisiz olanlar):** plaka görünürlüğü / 21:9 kart tavanı, round alt satırı 14 px, caption 14 px, UNLOCKED çipi tek yeşil; serif yüz = kullanıcı kararı (gelene kadar LiberationSans).
D. **Hareket/erişilebilirlik cilası:** UiMotion eğri/süre standardı (interaction-designer tek tur), reduced-motion tam kapsama, odak sırası/klavye, kontrast WCAG 2.2 AA ölçümü (DEC-077 UI eşikleri + tablo).
E. **Hazırlık:** `IAchievementProgressSource`/`ProfileCosmetics` (WP-035 sözleşmesi `planning/work-packages/WP-035-COSMETICS-CONTRACT.md` gelince) için Achievements paneli iskeleti; veri katmanına dokunma — WP-035 merge olunca bağlama WP-022D.

## Kapılar
EditMode UI seti + PlayMode HUD alloc (0 B/kare idle) · art-director (Fable) tek tur → MUST'lar · kontrast tablosu · tek commit, ≤15 satır not, rebase yok. Planning/04 yalnız kendi bölümün; `Assets/TextMesh Pro/` değişmez.
