# M3 kapanış — UI test süitleri: sahip ve bilinen koşullar (S-09, 2026-09-17)

| Süit | Tür | Sahip | Durum (main 11fd628 / WP-022J dalı) | Bilinen koşul / FAIL adayı |
|---|---|---|---|---|
| `UiArtThemeTests` | EditMode | S-09 | geçer (kontrast iki palet, ayar dosyası, glif sözlüğü, kapak) | — |
| `UiEventsTests` | EditMode | S-09 | geçer (matris ↔ enum) | matrise UiEvent olmayan satır eklenirse FAIL (WP-022F dersi) |
| `UiTextAuditTests` | EditMode | S-09 | geçer | ASCII "->" / Türkçe literal yasağı |
| `UiNavigationModelTests` | EditMode | S-09 | geçer (+Controls/Accessibility katmanları) | — |
| `ProfilePresentationTests` | EditMode | S-09 | geçer (WP-035 sahte kaynaklar, ad plakası) | — |
| `ResultsViewModelTests` | EditMode | S-09 | geçer | — |
| `RunHudViewModelTests` | EditMode | S-09 | geçer | — |
| `InputParityContractTests` | EditMode | S-02 | geçer (WP-022I r2 sonrası) | UI'da `UnityEngine.InputSystem` → FAIL |
| `UiArtSliceTests` | PlayMode | S-09 | geçer (HUD ikon/güvenli bölge/Special usage, seçim, pad yolları, panolar) | `Force/Release` şema dikişi; gerçek profil yazmaz (test profili) |
| `MetaUiSliceTests` | PlayMode | S-09 (WP-005 kökeni S-04) | batch'te geçer (WP-022K: pencere eşik geçişinden sonra, hakem ölçüm) | tek kare ~197 KB = HudLowHealthLeft tüketicisi (S-08/S-07 ısınma); başarım "First Blood" ilk ziyaret NEW |
| `SpecialWeaponHudAllocationTests` | PlayMode | S-09 | batch'te geçer (WP-022K hakem ölçüm, targetFrameRate −1) | WP-052 notu |
| `SessionSelectionTests` | PlayMode | S-09 | geçer | — |
| `EconomyUpgradeRulesSliceTests` | PlayMode | S-04 (UI bağlama S-09) | geçer | Ö-6a politikası değişirse altbilgi metni |
| `PlayerHitDirectionHudTests` | PlayMode | S-09 | geçer (WP-022F 150 px rampa, 0,45 düşük-can) | `DamageEdgeDepth` değişirse |
| `SceneLoadDoubleTriggerTests` | PlayMode | S-01 (WP-046B güncelledi) | S-10 tablosuna göre | World_AP005 additive dizisi |
| `HandoffIntegrityTests.BelgelenmisDevir` | EditMode | ilgili sahip | WP-021B ile kapandı | yeni devir satırı eksikse FAIL |

Kural: UI süitleri tek acquire içinde EditMode UI seti + PlayMode UI slice; alloc fixture'ları `targetFrameRate = -1` ile; S-10 M3 tam regresyonunda FAIL çıkarsa bu tablo sahibi gösterir.
