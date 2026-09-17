# WP-059 — Oyuncu deneyimi tamamlama v1 (prompt kaydı, C-00 2026-09-17, HIZ MODU)

Sahip S-02. Taban main f9ff487 (WP-056). Kredi 0. Kapsam (C-00 mesajı): oyuncu yaralanma / ölüm zinciri (WP-032C `player_hurt_A/B`, `player_death_side` → `PlayHurt` / ölüm; hasar yönüne göre seçim; kamera / vinyet / hit-stop knob'ları tek kaynak `GameFeelSettings` + `FlashScale`), ölüm sekansı (yavaşlama / kamera, Results'a geçiş zamanlaması, S-08 StingerDeath ile), yeniden atanmış tuşların istem gliflerine yansıması (S-09 `UiInputScheme`), nişan yardımı seviyeleri (S-09 AIM ASSIST Off / Light / Full → WP-024 verisi), Special / pickup sırasında vurulma kuralları testi, hot-path 0 ayırma; QaRunBot ölüm kareleri. Tek commit, ≤10 satır not.

Not: oyuncu / silah tasarımı proje sonunda ortak revize edilecek (kullanıcı 16.09 son revizyon listesi) — buradaki sayılar PROTOTYPE-ONLY teknik varsayılanlardır, `DD_GameFeel` asset'i ayar yüzeyidir.

Sonuç: `planning/04-IMPLEMENTATION-PLAN.md` § WP-059.
