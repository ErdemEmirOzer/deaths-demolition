# WP-022E — UI/HUD v2.4: ad plakası, telemetri ayarı, dükkân dal kapısı görünümü, AP-013 glif hazırlığı

**Sahip:** S-09 · **Taban:** main `e0ef509` (WP-022D `4a9463e` dâhil) · **Dal:** `wp-022e-ui-polish` · **Kredi:** 0 · **Slot:** `wp-022d` · **Hat:** HIZLI HAT §6 · **Not:** ≤10 satır.

C-00 talimatı (2026-09-17, hız modu): "WP-022E: WP-042 silah seviyeleri/dal kapısı HUD-dükkân görünümü, AP-013 silah ikonları gelince (P-06) glif değişimi, telemetri/ayar ekranı toggle'ı (WP-045 `-ddTelemetry`), §14 kalite kapısı."

## Kapsam
- A. Settings: "RUN TELEMETRY · ON/OFF" düğmesi (`UiSettings.Telemetry`, `ui-settings.json`, varsayılan açık) → `RunTelemetry.SetEnabled` (WP-045 modülüne 1 küçük API; komut satırı `-ddTelemetry 0` üstün); açıklama notu (yerel dosya, kimlik yok).
- B. Ad plakası: seçili unvan · rozet — Profil başlığının sağında (sakura) ve Results eyebrow'unda ("RUN RESULTS · BLOODED"); `CosmeticsViewModel.NamePlate`.
- C. Dükkân dal kapısı: WP-034/042 kartları zaten "WEAPON CORE / UTILITY" eyebrow + tier pip'leriyle; Selection CORE glif satırı (022C) ile tutarlı — değişiklik gerekmedi (kayıt).
- D. AP-013: teslim yok; `UiArt.RosterGlyph(HudIconId)` sözleşmesi hazır — ikonlar `Assets/_Prototype/UI/Art/Resources/UI/AP013/` altına `Weapon.<HudIconId>` anahtarıyla düşünce kod değişmeden alınır.
- E. Kapı: EditMode (ayar dosyası + plaka testi), PlayMode pad (telemetri toggle), dev build, yakalama (settings/profile/results), AD tek tur (görsel değişiklik küçük: plaka + toggle → v004 kapsamında SHOULD listesiyle birlikte, ayrı tur açılmadı).
