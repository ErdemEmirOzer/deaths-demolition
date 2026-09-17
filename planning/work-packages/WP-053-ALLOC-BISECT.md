# WP-053 — Ayırma fırtınası bisect'i (S-02, 2026-09-17, HIZ MODU)

Prompt (C-00): boş kare 3 975 ayırma / 471 KB (WP-037'de 6 / 162 B) bulgusunu şüpheli commit'lerde frame sayaçlarıyla ölç; aboneye/bileşene göre tablo; kök nedenler sahibine tek satır. Yöntem: `AllocationProbeTests` (frame sayaçları `GC Allocated In Frame` / `GC Allocation In Frame Count`, `Profiler.enabled`) her commit'e detached checkout ile kopyalandı, tek PlayMode koşusu, build yok. Kanıt: `wp-053/evidence/bisect-<sha>.txt`, `heavy-<sha>.txt`, `contract-alone.log`, `audit-fixed.log`.

## Sonuç tablosu (ayırma sayısı / bayt, kare başına)

| Ölçüm | 7924e69 WP-037 | 818e9d2 WP-032B | 56ebec9 WP-040 | db10700 WP-045 | e7e0963 WP-019 | ca7e690 WP-042B |
|---|---|---|---|---|---|---|
| Boş kare, 0 zombi (5 kare ort.) | 3 / 122 B | 4 / 166 | 4 / 166 | 4 / 166 | 4 / 166 | 4 / 166 |
| Boş kare, 4 yerleşmiş zombi | **3 / 122** | **8 / 342** | 8 / 342 | 9 / 374 | 8 / 342 | 9 / 374 |
| 40 vuruş karesi (tracer, zırhsız) | 161 / 6,9 KB | 166 / 7,1 | 166 / 7,1 | 166 / 7,1 | **275 / 11,7** | 275 / 11,7 |
| Ağır pencere (300 vuruş + 30 patlama, zırhlı, sayılar açık; 2. kare) | 329 / 9,6 KB | — | — | — | **817 / 23,6** | 810 / 23,7 |
| Ağır pencere sonrası boş kare (t+1 s) | 4 / 177 | — | — | — | 9 / 397 | 9 / 397 |

Sözleşme testi (`HotPathAllocationContractTests`, main ca7e690, tek başına koştu — sanat eseri değil): idle 11 / 382 B; **çekirdek (sayılar KAPALI) 112 / 5,7 KB** (WP-037: 13 / 620 B); sayılar AÇIK 1013 / 29,4 KB. Denetim (`HotPathAllocationAuditTests`, düzeltilmiş): pencere 611, sayılar kapalı 86, tüm bus aboneleri ayrıkken 632 → ayırmalar bus abonelerinde DEĞİL, yükseltme/giydirme yolunda.

## Bulgular ve sahipler

1. **"Boş kare 3 975 ayırma" bir ölçüm artefaktıydı** — `HotPathAllocationAuditTests` boş kareyi spawn'dan 1 kare sonra okuyordu: DEC-081 kalkışı (1,2 s) + zırh giydirme sırasında geçici ~800 ayırma/kare (≤ 2,5 s); yerleşmiş boş kare 4–9. Düzeltme bu pakette (2,5 s yerleşme + 5 kare ortalama; son boş kare de yerleşik). Spawn geçici yükü (4 zombi, ≤ 2,5 s, ~200/zombi/kare) gözlem olarak S-03/S-05'e (zırh binder / kalkış); ürün etkisi düşük.
2. **WP-019 (S-07) vuruş yolunu ~+1,6 ayırma/vuruş büyüttü** (ağır pencere 329 → 817; hafif 161 → 275) ve **hasar sayıları sıfır-alloc değil** (~525 ayırma / 300 sayı ≈ 1,75/sayı — tam sayı tablosu yolun tamamını kapatmıyor); sözleşme testi (`core ≤ idle + 12`) main'de bu yüzden kırmızı. Sahip S-07: `VfxDressing.PlayFleshHit/PlayBodyImpact` → `BloodSystem.PlayHit` (`PlaySplashCard`/`PlayDroplets`/`PlaceMark`) ve `DamageNumberEffect` yolu; sözleşme testi artık Editor GUI'de de koşar (`Profiler.enabled`).
3. **WP-032B (S-06)**: yerleşik boş kare 3 → 8 (4 zombi): `Animator` + `FootIkAdapter` ≈ 1,2 ayırma / zombi / kare (bileşen kapatma tablosu). 40 zombide ≈ 50/kare; küçük ama sürekli — S-06'ya not (IK/look-at hedef güncellemesinde ayırma).
4. WP-040 / WP-045 / WP-042B: ölçülebilir fark yok.

Ayrıca (C-00 kalemi): `CoreCombatSliceTests.GameFeelOlaylari…` (REQ-127 hit-stop) main'de kırmızı — WP-042C koşusunda okunur.
