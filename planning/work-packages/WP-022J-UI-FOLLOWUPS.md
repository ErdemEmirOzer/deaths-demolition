# WP-022J — UI takipleri: rebound etiket rezervi, ölüm overlay HUD sönümü, sayaç kare hızı; AD turu / süit tablosu

**Sahip:** S-09 · **Taban:** main `8039c5d` · **Dal:** `wp-022j-ui-followups` · **Kredi:** 0 · **Slot:** `wp-022j` · **Hat:** HIZLI HAT §6 · **Not:** ≤10 satır.

C-00 talimatı (2026-09-17): "`UiInputScheme` → S-02 Input okuma yüzeyi (WP-059B gelince), `ReservedWidth` rebound etiketleri, ölüm overlay desaturasyon UI tarafı (S-07 post-fx ile), AD turu S-10 run kareleri gelince, sonra M3 kapanış için UI süitleri FAIL→sahip tablosu."

## Kapsam
- A. `UiPrompt.AttachLabel`: düğme etiketi rezervi her rebind'de yeniden hesaplanır (`ReservedWidth` iki şema + rebound ad, taban 44 px) — etiket kaymaz, kapak örtmez.
- B. HUD ölüm sönümü: `RunPhase.Dead`'de HUD canvas'ı S-07 `DamageVignetteLayer.DeathOverlayT` ile 0,35 α'ya iner (EaseOut, unscaled 2 s; reduced motion anında).
- C. WP-052 notu: ProfilerRecorder testleri `Application.targetFrameRate = -1` (batch sırasındaki "sayaç 0" kök nedeni); teardown eski değeri geri koyar.
- D. `UiInputScheme` → WP-059B okuma yüzeyi: main'de yok → şimdilik `InputSourceRegistry.Current.ActiveScheme()` (WP-022I r2); gelince tek satır.
- E. AD turu: S-10 r8/r12 kareleri yok (bot artık dükkân alıyor; r8 bekleniyor) → kareler gelince.
- F. M3 kapanış UI süit tablosu: `planning/work-packages/M3-UI-SUITE-OWNERS.md` (süit → sahip → bilinen FAIL/koşul).
