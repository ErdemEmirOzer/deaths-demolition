# WP-055B — Kalabalık kimlik kuralları (AP-011B cw2, AP-007C aksesuar) + v4 NavMesh köprü kuyruğu yeniden doğrulama

Paket promptu (C-00, 2026-09-17, üç ayrı mesaj; S-03 sahip; kredi 0; "WP-055 sonrası küçük commit"). Kısaltılmış kayıt.

- **Taban:** main `5605fc8` (WP-038 r2) + WP-055 (`ae8411b`, cbb685a'nın main üstüne cherry-pick'i). Branch `wp-055b-crowd-navmesh`, worktree `wp-055/`. Tek commit.
- **AP-011B (P-06 kuralı, C-00 kararı DEC-077 okunabilirlik):** zırhlı spawn ⇒ cw2 (soluk kumaş) giysiler — Elite/Ultra zaten, Standard zırhlı spawn'lar cw2; canlı cw1 payı ≥ %30 (kimlik). `SpawnMixSimulator` R1–R12 eşleştirme kuralı + `ZombieLookConstraints` / spawn giysi seçimi; S-10 run3 v003 albedo ile birlikte ölçer.
- **AP-007C v003 (P-06 kalabalık çeşitliliği):** `AP-007/exports/v003/` 29 FBX — H6/H7 baş, yeni `accessory` slotu (A1 hasır şapka `occludesHead=true`, yalnız Standard, zırh / tier baş parçası / T7 ile dışlanır, ağırlık 0,20; A2 yüz sargısı), T8 rolled kosode; tavan ≤ 11 700 tri. S-03 payı (AD M2/M3): `ZombieLookConstraints` binder kuralı + SpawnMixSimulator 200 örnek (0 A1+zırh, A1 %15–25); prefab/import S-05 WP-038.
- **WP-038 devri (main `b738c87`):** gerçek plakalar + NavMesh (havuz Not-Walkable, köprü güverte 0,45 m koridor, korkuluk dikmeleri bake dışı) — kuyruk mantığı güvertede tetiklenmedi (163 yeniden rota, 0 kuyruk) → NavMesh yeniden doğrulama + köprü kuyruğu düzeltmesi.
