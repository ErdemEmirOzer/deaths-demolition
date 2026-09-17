# M3 kapanış — combat süitleri: durum ve FAIL → sahip tablosu (WP-059C, S-02, 2026-09-17)

Ölçüm: main `0430fcf` (= WP-059B) + WP-059C değişiklikleri (PickedUp sırası, test düzeltmeleri), wp-059c worktree, tek acquire; teslim main `f6eff6f` (WP-047F) üstünde; EditMode `wp-059c/evidence/emc.xml` (25 fixture), PlayMode `pmc.xml` (25 fixture, 598 s). S-10 WP-044 ara regresyon tablosuyla (`wp-044/evidence/REPORT.md` §3, main dc32ecf → ca7e690) çapraz okundu. Kapı değil, envanter: "combat" = hasar çekirdeği, silahlar (klasik + Special), oyuncu sağlığı/yaralanma, düşman tepkileri, girdi paritesi, ayırma sözleşmeleri.

## 1 · Süit envanteri (sahip · bu koşu)

| Süit | Mod | Sahip | Sonuç |
|---|---|---|---|
| `RegionalDamageTests`, `RegionalArmorSliceTests` (EM 28 + PM 9), `HitReactionContractTests` | EM/PM | S-02 (WP-006) | 20/20 · 28/28 · 9/9 · 10/10 |
| `CoreCombatSliceTests` (10), `EnemyDamagesPlayerTests` (3), `PlayerHealthContractTests` (4), `PauseFireTests` (1) | PM | S-02 (WP-002) | tümü yeşil — `GameFeelOlaylari…` REQ-127 hit-stop (WP-039/WP-044 kırmızısı) S-07 WP-047D/E ile kapandı |
| `PlayerHitDirectionHudTests` (4), `UiArtSliceTests` (6, HUD Special banner dâhil) | PM | S-09 | 4/4 · 6/6 (WP-059C PickedUp sırası ile) |
| `WeaponRosterTests`, `WeaponFeelTests`, `WeaponBlockoutGateTests`, `WeaponModelGateTests` | EM | S-02 (WP-034/042/042B) | 8/8 · 9/9 · 2/2 · 6/6 |
| `WeaponFeelSliceTests` (6), `WeaponModelSliceTests` (1, canlı montaj çerçevesi) | PM | S-02 | 5/6 → 6/6 (bkz. F2) · 1/1 |
| `SpecialWeapon*` EM: Contract 9, Chance 2, Identity 23, PlacementPolicy 5, Runtime 7, SchedulingPolicy 6, SourceScan 4, UsageIds 1, UsageV2 3, AssetGate 6, PrototypeCharacterization 22 | EM | S-02 (WP-007/037/056) | tümü yeşil; Chance 0/2 → 2/2 (bkz. F1) |
| `SpecialWeapon*` PM: DamageSlice 4, DamageParity 3, LifecycleSlice 21, RunFlow 4 (beyaz liste WP-059B), UsageSlice 3, Visual 1, HudAllocation 3, PrototypeBehaviour 5 | PM | S-02 | tümü yeşil |
| `SpecialReactionTests` (6), `SpecialReactionSliceTests` (4), `TierAbilitiesTests` (7), `TierAbilitiesSliceTests` (4), `CrowdFeelTests` (5), `CrowdFeelSliceTests` (2) | EM/PM | S-03 | tümü yeşil |
| `HotPathAllocationContractTests` (2), `HotPathAllocationAuditTests` (2) | PM | S-02 (WP-037/053) | 2/2 · 2/2 — `VurusYolu_KareBasiAyirma_SifirSozlesmesi` (WP-044 P8, wp-056'da 112–131 > idle+12) S-07'nin WP-047D/E düzeltmeleriyle yeşil |
| `VfxHotPathBisectTests` (1) | PM | S-07 (WP-047) | 0/1 (bkz. F3) |
| `GamepadParityTests` (2), `InputParityContractTests` (10), `InputRemapPersistenceTests` (3) | PM/EM | S-02 (WP-024) | 2/2 · 10/10 (WP-022F ihlali S-09 WP-022J ile kapandı) · 3/3 |
| `PlayerExperienceTests` (4), `PlayerExperienceSliceTests` (4) | EM/PM | S-02 (WP-059) | 4/4 · 4/4 |
| `ProjectIntegrityTests` (17) | EM | S-01 | 17/17 |

Toplam: EditMode 224 → 222 + 2 düzeltildi; PlayMode 109 → 107 + 1 düzeltildi + 1 açık (S-07). Doğrulama: em1 37/37, pmfix `WeaponFeelSliceTests` 6/6, pm1 Lifecycle 21/21 · UiArt 6/6 · RunFlow 4/4; release b1 551,7 MB (550 tavanı +1,7 MB — main geneli, WP-038B içerik; S-05/C-00), e1/e4 exit 0.

## 2 · FAIL → sahip

| # | Test | Bulgu | Sahip / durum |
|---|---|---|---|
| F1 | `SpecialWeaponChanceTests.KlasikSilahlar_SabitDegerler`, `.ArmorChance_TurBasinaBuyumeVeTavan` | Karakterizasyon testi DEC-085 öncesi sayıları taşıyordu: B/C hasar 7 (Ö-4 → 8, WP-042 S-02) ve zırh şansı büyüme 0,025 / tavan 0,75 (Ö-2 → 0,012 / 0,55, WP-050 S-03). Veri kararla değişti, test güncellenmemişti. | **S-02 — WP-059C'de düzeltildi** (beklentiler DEC-085'e; sayılar PrototypeOnly) |
| F2 | `WeaponFeelSliceTests.Mekanizma_C_KovanPumpta…` | "pump chamber 1 bekleniyor, 2": C fire klibi (v005b'den beri eject f12 + chamber f20) reload basılmadan bitip chamber'ı yayınlıyor (v006b klip süresi); test toplam 1 sayıyordu. Reload'un chamber'ı yine tam bir kez. | **S-02 — WP-059C'de düzeltildi** (toplam 1–2; son mekanizma olayı reload'un chamber'ı) |
| F3 | `VfxHotPathBisectTests.VurusYolu_VfxKaynaklarinaGoreAyirma` | Pozitif kontrol 0: `Application.targetFrameRate` kısıtlıyken (bootstrapper 60; tam akışı açan bir fixture sonrası) ProfilerRecorder `LastValue` Editor test koşucusunda güncellenmiyor — WP-059 bulgusu, S-10 P8 hakem yöntemi (targetFrameRate −1) ile aynı. Tek başına koşunca yeşil olur. | **S-07** (ölçümde `targetFrameRate = -1`, `PlayerExperienceSliceTests.YaralanmaYolu…` kalıbı; WP-032F/022J aynı düzeltmeyi yaptı) |
| P1 (S-10) | `perf.gcAllocatedInFrame` boş kare 372 → 903 B/kare (WP-032B/WP-019 katkıları, WP-053 bisect) | Combat süitlerinde karşılığı yok (idle ölçümü Player'da); WP-053 tablosu sahipleri S-06/S-07 | S-06 / S-07 (WP-053 tablosu), S-10 kapanış ölçümü |
| P8 (S-10) | `VurusYolu_SifirAyirma` aralıklı | Bu koşuda yeşil (idle 12 / core ≤ 24); aralıklılık kaynağı: kirli pencere + targetFrameRate kısıtı (F3) | S-10 kapanış hakem yöntemiyle yan yana okur |

Kapanış notu: main'de combat süitlerinde bilinen açık kırmızı yalnız F3 (S-07, ölçüm yöntemi). Kullanıcı 16.09 son revizyon listesi (silahlar / Special / tier saldırıları) ayrı; buradaki sayılar PrototypeOnly.

## 3 · K1 boş-kare GC bisect (WP-053C, S-10 kapanış FAIL: 372 B → 14,2 KB/kare, gen0 2–3 → 53/60 s)

`AllocationProbeTests` (kit sahnesi: spawner durmuş, 4 zombi; byte tabanlı tip taraması + oyuncu kökü taraması eklendi) dört noktada tek koşu — `wp-059c/evidence/probe-<sha>.log`:

| Nokta | idle0 | idle4 | 40 vuruş karesi | ağır pencere | sonrası (3 s) |
|---|---|---|---|---|---|
| 5e417fc WP-055 | 3 / 122 B | 10 / 635 B | 39 / 1,6 KB | 109 / 27,9 KB | 11–13 / 0,74–0,78 KB |
| ced21c1 WP-059 | 3 / 122 B | 11 / 643 B | 39 / 1,6 KB | 111 / 28,0 KB | 10–15 / 0,71–0,83 KB |
| 92a6e72 WP-047E | 3 / 122 B | 11 / 643 B | 40 / 1,7 KB | 117 / 31,8 KB | 11–13 / 0,74–0,78 KB |
| 52580a7 WP-052C | 5 / 178 B | 13 / 715 B | 42 / 1,8 KB | 121 / 31,9 KB | 15 / 0,83 KB |

Sonuç: combat çekirdeği, oyuncu rig'i (17 Behaviour tipi), zombi tipleri ve sahne tipleri (61) dört noktada da boş karede ≤ 0,9 KB — 14 KB/kare fırtınası burada değil; tip taraması hiçbir tipte > 2 KB tasarruf göstermedi. Fırtına spawner açık koşuya bağlı sistemlerde (round/spawn/ölüm akışı, HUD, telemetri, VFX dressing, müzik/ses yönetimi) → S-10 perf-1 penceresinde `PilotPerfProbe`'un tip taramasıyla (probe (e) kalıbı, spawner açık) tek koşu; S-02 dosyalarında düzeltilecek bir şey yok.
