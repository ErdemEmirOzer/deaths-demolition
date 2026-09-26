# WP-077 · K10b balance audit (S-04, 2026-09-26)

Base main 9578a2eb (K10b effects + U11 on main). Tools: `tools/balance/k10b_audit.py` (new — tree path × weapon × round
over the live WD_*.asset / PrototypeConfig / run-tree.json; full tables in `WP-077-K10B-AUDIT-TABLES.md`) and the C#
`EconomySimulator` / `TreeModel` (offer draw, bots, Kül tempo). The Python model is first-order and analytic: TTK =
sustained seconds per kill with the round's expected plates, crowd DPS at a fixed density, an ammo cap (reserve + floor
refills over a 75 s round), greedy lane purchases at 90 % pickup. Every assumption is a named constant in the script;
read the numbers as ranks and ratios, not as play-feel truth.

## 1 · TTK of the 10 weapons (R10, round build, seconds)

| | median | fastest | slowest | max/min |
|---|---|---|---|---|
| Standard | 1.20 | lmg_2 0.71 | rl_1 4.28 · sr_1 2.37 | 6.1 |
| Elite | 2.50 | lmg_2 1.24 | rl_1 4.28 · sr_1 4.25 | 3.5 |
| Ultra | 4.49 | lmg_2 2.83 | sr_1 7.12 | 2.5 |

- **sr_1 is the weakest classic** at every tier (≈ 2× the median TTK, lowest crowd DPS 124 vs median ≈ 250) even with
  its lane — its own nodes do not close the gap (seal +3 %, capstone +17 % on a single target).
- **lmg_2 is the fastest single-target killer** at every tier (≈ 0.5× the median).
- **rl_1 and sg_2 are ammo-bound** (reserve 8 / 40): over a round a rocket every ≈ 4.3 s, so their magazine and reload
  nodes buy burst, not round damage. rl_1 still has the highest crowd throughput (1 148 vs sg_2 623, sg_1 517).
- Weapon numbers are DEC-099 (S-02) — not touched here; see proposal P1.

## 2 · Nodes out of band (R10 build; Δ effective DPS vs Elite / Δ crowd DPS)

Class medians: fork +10 %, keystone +7 %, seal +6 %, capstone +5 %.

**Strong**
- `ks_explosion` on **rl_1**: `RunTreeHooks.OnRocketBlast` ignites the WHOLE blast disc (r 5 m) at 25 %/s of the blast
  for 2 s → ≈ 80 HP to every body in it per rocket (+13 % / +30 %). rl_1's own seal (Hibana, 4 HP/s × 3 s = 12 HP)
  is 7× weaker than this keystone → P2.
- `ks_explosion` on the shotguns (+25–29 % crowd), `ks_piercing` on ar_1 / lmg_2 (+30 % / +22 % crowd), `ks_chain` on
  smg_2 (+20 % crowd): 2–4× the keystone median; crowd numbers depend on the density assumption — watch in playtests.
- Capstones rl_1 (+46 % crowd, 3 blasts) and sg_2 (+25 %) are far above the other capstones.

**Weak / dead**
- `ks_piercing` on **sg_1** does nothing: sg_1's `penetrationDamageKeep` is 1, so there is no drain to remove → P3.
- `ks_reload` = `Max(1, floor(mag × 25 %))` → rl_1 +1 rocket (+100 %), sg_2 +1 shell (+50 %) per empty reload, drawn
  from the reserve: burst only, +0 % over an ammo-bound round. Not dangerous; info.
- Capstones ar_2 (+1 %), lmg_2 (0 %), smg_2 (+4 %), ar_1 / sg_1 / lmg_1 (+5 %) for 380 g — partly utility the model
  cannot price (armour ignore, knock-back, plate pass), but the spread 0–46 % is wide → P4.
- Forks with no DPS side: ar_1 Y (−5 %: +15 % fire rate eaten by bloom and ammo), smg_1 Y (+3 %, only with
  `ks_firerate`), lmg_1 Y (survival), sg_2 Y (0 %, ammo-bound), smg_2 X (+2 %, control). rl_1 seal +2 %.

## 3 · Data fixes applied (run-tree.json args; PrototypeOnly council numbers, text via placeholders)

| node | was → now | effect (model) |
|---|---|---|
| `fork_lmg_2_y` | first 10 → **20** rounds after a reload +30 % | Y +5 → +10 % (X +10 %): the pair is even |
| `fork_sr_1_x` | wait 2.5 → **2** s for ×2 | 2.5 s = two bolt cycles, so ×2 bought nothing; R10 TTK Standard 2.83 → 2.37 s, Ultra 8.49 → 7.12 s; the normal 1.25 s tempo still gets ×1 |
| `fork_rl_1_x` | damage +30 % / radius 75 % → **+50 % / 80 %** | was a trap (crowd −22 %); now +28 % single target, −2 % crowd |
| `fork_rl_1_y` | radius +25 → **+15 %** | crowd +48 → +27 %: X = single target, Y = crowd, both ≈ +28 % |

Considered and not applied: sg_2 Y reload −25 → −15 % (the ammo cap makes it +0 % either way), smg_1 Y warm-up
0.8 → 0.4 s (+2 → +3 %; not worth the change).

## 4 · Economy (gold / Kül)

- Gold at 90 % pickup (before Purse): cumulative R5 424, R10 2 151, R15 5 855. A full weapon lane (tiers + 3 keystones
  + seal + fork + capstone + Purse) costs 2 105 (rl_1) – 2 483 (ar_1).
- Greedy path (no offer draw): seal R6–R7, capstone R8, lane full ≈ R12; Hearth takes the rest (H6 at R10, H14 at
  R15 = +28 % damage), wallet ≈ 500 at R15 — Hearth is the late sink, as DEC-098 intends.
- Kül = XP / 30: a run ending at R10 → 41 Kül, at R15 → 97. Ring 1 (150) ≈ 4 R10 runs; the whole meta (5 830) ≈ 140
  R10 runs / 60 R15 runs.
- C# TreeModel (offer draw, 4 bots): see §6 — the draw, not gold, gates the capstone.

## 5 · Proposals that change a DEC (for C-00)

- **P1 (DEC-099, S-02)** sr_1 needs about +40 % single-target (e.g. damage 55 → 70 or bolt 1.25 → 1.0 s); lmg_2 is the
  single-target top at every tier — consider trimming its armour multiplier 1.3 → 1.15.
- **P2 (DEC-095 / code)** the Ash Cloud on a rocket blast should not burn the whole 5 m disc at 25 %/s: e.g. rocket
  blasts ignite the inner radius only, or at 10 %/s; then rl_1's seal (Hibana, 4 HP/s) can go up to ≈ 10 HP/s.
- **P3 (DEC-095 lane)** sg_1's lane carries a dead `ks_piercing`: swap in the Impact or Armour Break keystone, or give
  sg_1 a pierce drain (keep 0.8, DEC-099).
- **P4 (DEC-095 content)** capstone floor: the weak capstones (ar_2, lmg_2, smg_2) need either a damage clause or a
  lower price (380 → 300) so that none is below ≈ +10 %.
- **P5 (DEC-098)** the Human bot ends R15 with a median 783 gold (p90 2 911) and reaches its capstone in 20 % of runs
  (M1 met): the per-round card limit (2 buys) is the bottleneck, not gold. The what-if "3 cards from R10" lifts that to 55 % and halves the
  p90 wallet (1 416) — recommended if the capstone should be a normal-run goal.

## 6 · C# models on this base

EditMode `TreeModelTests|EconomySimulatorTests` 12/12 (seeds 1–20 × 4 weapons, pickup 0.90; full report in
`wp-077/evidence/audit/tree/tree-model.md`).

| bot (M1 met) | spend share R1–15 | wallet R15 med / p90 | 1st keystone | capstone by R15 | Hearth 20 at |
|---|---|---|---|---|---|
| SpendMax | 95 % | 287 / 922 | R7 | 48 % | R19 |
| Rush | 97 % | 209 / 427 | R4 | 100 % (R10) | R18 |
| Human | 87 % | 783 / 2 911 | R8 | 20 % | R19 |
| Hoarder | 46 % | 3 341 / 4 025 | R9 | 20 % | — (12) |

Before M1 no bot except the pinned path reaches a capstone (K6 gate, as designed). Kül tempo (XP / 30): ring 1 in
1.4–1.7 h, rings 1+2 in 14–24 h, the whole meta in 54–66 h (R15 player / new player) — the DEC-098 direction reference
(4 500 XP/h) gives 1.0 / 14 / 39 h; the live round pace (2 640–3 260 XP/h) is ≈ 30 % slower than that reference.
