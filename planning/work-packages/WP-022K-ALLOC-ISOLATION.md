# WP-022K — Batch ayırma izolasyonu, ana menü ayırma tanısı, AD turu bekleme

**Sahip:** S-09 · **Taban:** main `f319190` · **Dal:** `wp-022k-alloc-isolation` · **Kredi:** 0 · **Slot:** `wp-022k` · **Hat:** HIZLI HAT §6 · **Not:** ≤10 satır.

C-00 talimatı (2026-09-17): "heap-büyüme testlerini fixture izolasyonuyla batch'te de PASS yap, HUD ONLINE bandı önceki ad (AP-020 — WP-022J r2 ile kapandı), AD turu r8/r12 (S-10 run5); S-10 bulgusu: MainMenu ~4 ayırma/kare UI kaynaklı → tanı testinle kaynağı bul."

## Sonuç (özet)
- Batch FAIL kök nedeni ayırma değil, **tek kare**: HUD açık pencerede regen düşük-can eşiğini geçince `HudLowHealthLeft` yükseliyor → tüketiciler (ses cue / VFX) o karede ~197 KB tek seferlik kurulum; diğer 599 kare ≈ 40 B (recorder zaman çizelgesi). Test artık eşik geçişini bekleyip sessiz pencereyi ölçer; ölçüm yöntemi hakem (recorder toplamı, yığın deltası yedek).
- Ana menü: taban 4 ayırma/kare harness+diğer (canvas'lar kapalıyken de 4); UI +2 → `UiInput.CancelPressed` her karede `Input.GetButtonDown("Cancel")` ArgumentException fırlatıp yakalıyordu → tek seferlik sonda. Kalan ~1 (UiDrift) uGUI rebuild.
- HUD numerics auto-size kapatıldı (HUD değerleri tasarımda sınırlı; TMP auto-size değişimde çok geçişli) — koruyucu.
