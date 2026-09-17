# WP-022G — Erişilebilirlik & seçenekler v1

**Sahip:** S-09 · **Taban:** main `fb14c45` · **Dal:** `wp-022g-accessibility` · **Kredi:** 0 · **Slot:** `wp-022g` · **Hat:** HIZLI HAT §6 · **Not:** ≤10 satır.

C-00 talimatı (2026-09-17): "renk körlüğü güvenli alternatifler (tehdit çipleri/hasar sayıları/HUD; kan kırmızı kalır — direktif), metin ölçeği, ekran sarsıntısı/flaş azaltma toggle'ları (WP-042 knob'ları + WP-019 vignette'e bağlı), tuş atama UI'ı (WP-024 rebinding altyapısı), gamepad ölü bölge/nişan yardımı seviyesi, kontrast tablosu; `ui-settings.json`; AD tek tur; tek commit."

## Kapsam
- A. ACCESSIBILITY paneli (`UiContext.Accessibility`): PALETTE · DEFAULT / COLOUR-BLIND SAFE (`UiTokens.Palette`: yeşil ailesi → mavi; tehlike vermilion; kan dokunulmaz), TEXT SCALE 100–125 % (Body/Label/Caption), REDUCE FLASHES (HUD hasar kenarı + son-saniye vinyeti + WP-019 vignette flash %40; kan yok), REDUCED MOTION (Settings'ten taşındı).
- B. CONTROLS paneli (`UiContext.Controls`): klavye / gamepad slotları (WP-024 `IInputRemapService`: PRESS A KEY, geçersiz kılınanlar sakura, RESET ALL), gamepad STICK DEAD ZONE, AIM ASSIST Off/Light/Full (`InputPreferencesStore`), RUMBLE.
- C. Kontrast tablosu iki palette (EditMode); ayar dosyası round-trip; navigasyon modeli katmanları; pad yolu (PlayMode).
- D. Kamera sarsıntısı: mevcut Pause kaydırıcısı (WP-042 knob'ları `PrototypeConfig.Camera.ShakeIntensity`) — yeni knob yok; hasar sayıları rengi S-07 modülünde (devir: `UiTokens.Palette` okuması).
