# WP-022F — UI/HUD kalite geçişi v3 (gerçek oynanış): bot HUD kareleri, AD SHOULD'lar, girdi glif istemleri, hareket cilası

**Sahip:** S-09 · **Taban:** main `ca7e690` (WP-052 animator hotfix + WP-042B dâhil) · **Dal:** `wp-022f-ui-quality-v3` · **Kredi:** 0 · **Slot:** `wp-022f` · **Hat:** HIZLI HAT §6 · **Not:** ≤10 satır.

C-00 talimatı (2026-09-17): "(1) S-10 QaRunBot ile r1/r4/r8/r12 HUD kareleri (tehdit çipleri, Special, düşük can, upgrade dükkânı; `-ddUiCapture` + bot) → AD tek tur (Fable) → MUST'lar; (2) AD v004 SHOULD'ları; (3) girdi glif istemleri: klavye/gamepad otomatik geçiş (WP-024), buton glif seti (AP-012 kalıbı, kredisiz vektör), tüm istemlerde tek kaynak; (4) round geçişi/Results açılış hareket cilası (UiMotion standardı, reduced-motion); (5) AP-013 ikonları WP-042B main'e girince `RosterGlyph` değişimi." + S-04 incelemesi: `RunTelemetry` CLI-kapalı/abonelik bayrağı ayrımı.

## Kapsam
- A. `UiCaptureAgent` **hud gözlemcisi** (`-ddUiCapture hud -ddUiCaptureRounds 1,4,8,12` + `-ddQaBot`): round kareleri, ilk upgrade ekranı, Special yerde/elde, ilk tehdit çipi, ilk düşük can; `tools/bot-hud-captures.sh`. AD tek tur → MUST'lar.
- B. Girdi istemleri: `UiInputScheme` (Input System cihaz zaman damgalarından son dokunulan cihaz, kare başı ayırma yok), `UiInputGlyphs` (prosedürel: pad yüz düğmesi disk+harf, MENU pill, tuş kapağı), `UiPrompt` (cihaz değişince yeniden çizer), `UiInput.Prompt(button, action)`; Selection BACK/START RUN, Results MAIN MENU, Pause RESUME, Upgrade CONTINUE/REROLL.
- C. Hareket: `UiStaggerIn` (Results üç sütun 60 ms kademeli, MotionBase, 12 px kalkış; reduced-motion anında).
- D. AD v004 SHOULD-5 (`UiFactory.CardChipHeight/RowChipHeight`), SHOULD-6 (pad ile açılan Achievements paneli NEXT'e odaklanır).
- E. `RunTelemetry` S-04 düzeltmesi: `s_commandLineOff` + `s_subscribed`; `-ddTelemetry 0` varken `SetEnabled(true)` no-op; test.
- F. AP-013: WP-042B modeller/atlas getirdi, ikon sheet'i yok → `RosterGlyph` değişmedi (devir: ortho karelerden 128² ikon, kredisiz).
