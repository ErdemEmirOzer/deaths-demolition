# WP-050 — Spawn/Threat tuning v1

Paket promptu (C-00, 2026-09-17, HIZ MODU / HIZLI HAT §6; S-03 sahip). Kısaltılmış kayıt.

- **Taban:** main `56ebec9` (WP-040 entegre). Branch `wp-050-spawn-threat-tuning`, worktree `wp-050/`. Rebase yok (C-00 cherry-pick); not ≤ 10 satır.
- **Kullanıcı onayı (2026-09-17, C-00 üzerinden):** WP-041 önerisi **Ö-2** (zırh `ChanceGrowthPerRound` 0,025/0,03 → 0,012/0,015; `MaxChance` Standard/Runner 0,75 → 0,55; Guard/Executioner girişleri aynı) ve **Ö-5** (`RoundDifficultyOverride` R8/R11/R14: zırh sıçraması + Executioner payı) → tek commit; testler + SpawnMixSimulator yeniden koşusu; DEC-085 satırını S-04 yazar (referans).
- **Kapsam:** Ö-2/Ö-5 · WP-039 E2 (WP-040'ta kapandı) · S-10 "[Spawn] No spawn point had a complete NavMesh path" bulgusu (wp-026 quick11 bot log) · WP-032C `SetWaiting` çağrısı · AP-015P plakalarıyla NavMesh yeniden doğrulama (WP-038 sonrası).
- **Ek:** main'de REQ-156 çakışması (WP-037B ↔ WP-040) → kalkış satırı REQ-157, ProjectIntegrityTests son-REQ beklentisi 157.
