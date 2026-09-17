# WP-022H — Special Weapon HUD v2, düşman durum işaretleri, Ö-6a hazırlığı, AP-013 glif SHOULD

**Sahip:** S-09 · **Taban:** main `f9ff487` (WP-056 dâhil) · **Dal:** `wp-022h-special-hud` · **Kredi:** 0 · **Slot:** `wp-022h` · **Hat:** HIZLI HAT §6 · **Not:** ≤10 satır.

C-00 talimatı (2026-09-17): "Special Weapon HUD v2 (S-02 WP-056 `SpecialUsageEvent`: aktif süre/alan durumu/kalan atış, sw_* ikonları AP-014 sheet'lerinden), düşman durum işaretleri (S-03 WP-055 net/daze/çekim — küçük, okunur, 0 ayırma), Ö-6a 'ikinci seçim' akışı hazır ama kapalı (`PurchasesPerRound` 1 iken görünmez), AP-013 glif 56 px SHOULD, r8/r12 kareleri S-10 run3 gelince AD turu. WP-055/056 main'e girince bitir."

## Kapsam
- A. Special HUD v2: `SpecialWeaponUsageEvents.Usage` aboneliği → kullanım satırı ("FIELD 4s · 2 HIT" / "FIELD ENDED · 5 HIT" 2 s / "SHOTS n"), AP-014 el modeli siluet glifi (`Sw.<id>`; Tide Jewel AP-012 kalır); tamsayı değişince metin (0 kare-başı ayırma).
- B. Düşman durum işaretleri: WP-055 main'de değil → bu pakette yok (devir: WP-022I).
- C. Ö-6a: dükkân altbilgisi `UpgradeOfferPolicy.MaxPurchasesPerRound` + `IsLocked`'a göre; politika 1'de görünmez.
- D. AP-013 glifleri 22° eğik / 118 px sığdırma (56 px'te okunur).
- E. AP-014 sw glifleri aynı üreticiyle (`tools/build_ap013_icons.py <dst> ap014`).
