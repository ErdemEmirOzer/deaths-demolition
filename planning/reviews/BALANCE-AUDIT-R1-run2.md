# BALANCE-AUDIT-R1: numeric audit of M4-R1 Arsenal v3, Specials and Progression tree v2

- Base: `origin/main` @ `a7f2557`. Branch `review/balance-audit-r1`. This is a report only. No game code or data changed.
- Method: I read the design docs, the live YAML data and the C# formulas. Every number below comes from `tools/balance/r1_audit.py` (Python 3, stdlib only, deterministic seed `20260924`). Run it with `python3 tools/balance/r1_audit.py [--json out.json]`. I did not run Unity or fetch LFS binaries.
- Sources:
  - **DIR-069** = `planning/work-packages/WP-069-ARSENAL-V3-DIRECTION.md`
  - **DIR-077** = `planning/work-packages/WP-077-PROGRESSION-TREE-V2-DIRECTION.md`
  - **CFG** = `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Core/Resources/PrototypeConfig.asset`
  - Code paths are relative to `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/`.
- Targets are the Upper body with no armour, computed from CFG: Standard R1 **34.0**, Standard R10 **70.7**, Guard R10 **155.6**, Executioner R10 **353.6**. The HP formula is `34 × tier × (1 + 0.12(R−1))` (CFG:120, 124/174/199, 244; `Core/Config/RoundConfigSection.cs:75`). All four values match the doc.
- The council files `wp-077/council/FACTS.md` and the other council files cited by DIR-077 are **not in the repo**. So I rebuilt the round income from code instead of copying it. My R1–R15 income is **6 505** against the doc's 6 573 (−1.0 %), which confirms the doc's income base.

## Summary: findings ranked by impact

| # | Sev | Finding | Suggested value |
|---|---|---|---|
| 1 | CRIT | No resupply is defined for the finite reserves. Every weapon's full load runs dry in R3–R5, even at 100 % accuracy | Refill the reserve to full at every round start (DEC-096 row), or keep `reserve −1` for classics |
| 2 | HIGH | The second weapon of each class strictly dominates the first: ar_2 ≻ ar_1, sg_2 ≻ sg_1, lmg_2 ≻ lmg_1, and nearly smg_2 ≻ smg_1. The first weapons become dead options | ar_2 14 dmg / head ×1.30 · smg_2 12 dmg / armour ×0.8 · sg_2 9 pellets, full ≤ 5 m, reload 2.4 · lmg_1 reload 3.6/4.2, move ×0.88 · lmg_2 360 RPM |
| 3 | HIGH | The rl_1 armour arithmetic contradicts the code (REQ-088): a fully plated Guard takes **0** from the blast, not 72. One rocket kills an R10 Guard only 50 % of the time | Either correct the doc (2 rockets for plated Guards), or give the direct hit `ignoresArmor` + 160 dmg |
| 4 | HIGH | Namekuji clears ≈ all killable non-Executioners per pickup: 73/75 at R10, 101/106 at R15. The doc says ≈30–50. Specials take ≈31 % of all R1–R15 kills | Namekuji puddle 25 → 12 s, cap 12 → 6, reserve 16 → 8, Guard slip kill only R ≤ 10 |
| 5 | HIGH | sg_1's outer ring (4 pellets at 100 % cone) misses the real 0.26 m hitbox beyond 2 m. The doc's "Standard R10 in 1 shot ≤ 6 m" becomes ≤ 2 m hip, and 36–54 dmg at 6 m | Rings 3 @ 0.25 + 4 @ 0.55, cone hip/focus 6.0°/4.5°. That gives 8 pellets (72 dmg) out to 4 m |
| 6 | MED | DIR-077 uses 4 500 XP/h, but its own anchor (R15 run ≈ 28 min, 2 900 XP) implies ≈ 6 150 XP/h. L20 arrives at 7.1 h, not 9.7 h, and L30 at 21 h, not 28.7 h | Calibrate from WP-045 telemetry. If 28 min holds, use `110L + 12L²` (L20 8.2 h, L30 24.5 h) |
| 7 | MED | Meta ring budgets miss: Ring 1 (330 Kül) takes 2.0–2.9 h, not ≤ 1 h. Ring 1+2 (2 330 Kül) takes 11–21 h, not ≤ 10 h | Ring 1 prices smg_1 20 / sg_1 40 / Çekim I 30 / Passive K1 30 (≈180 Kül ≈ 1.0 h). Kül divisor 40 → 32 |
| 8 | MED | The human spend and wallet targets depend only on how often the player stokes the Hearth. At ≤ 1 stoke/round the R15 wallet is 2 100–3 700 (red). The Nişan (pin) bypasses the M3 capstone gate, so the capstone comes at R8 | Hearth slope 30 → 20; a pin cannot target a capstone before M3, or accept median capstone at R8 |
| 9 | MED | §1.3 progression-curve claims fail on three points: smg_1 takes 6–7 shots at R3–R5, armour only "rises" on R8/R11/R14, and the cap is 32, not 40 | Restate the claim, or make the DEC-021 rows sticky |
| 10 | LOW | Arithmetic slips in the docs: rl_1 DPS, lmg_1 Guards per box, sr_1 176 DPS and 4-body line, penetration semantics, Hearth formula, XP table row | See §10 |
| 11 | LOW | The live WD_* assets still carry M3 numbers (B1 step 1). v3 raises baseline DPS by 12–28 %. WD_sg_1 is `fireMode: 2` (Auto) and uses a Magazine reload | Re-run the WP-041 sim before DEC-096; set sg_1 to Single / PerShell in B2 |

---

## 1. [CRIT] No resupply rule exists for the finite reserves, so the whole arsenal runs dry by R3–R5

**Where**
- DIR-069 l.80–89: column "Şarjör / yedek (toplam)"
- DIR-069 l.1065: `reserve` is "koşu başı taşınan; −1 = sonsuz, M3 davranışı"
- Live data: `WD_ar_1.asset:43`, `WD_sg_1.asset:45` both have `reserve: -1`
- I found no ammo pickup or refill code anywhere in `_Prototype` (grep for `reserve|Refill|AmmoPickup`).

**Numbers** (`r1_audit.py` §1f). Cumulative enemy HP plus expected armour durability, from the CFG pool, mix and armour formulas: **R1–R5 8 608 · R1–R10 46 337 · R1–R15 137 956**.

| slot | full load (mag + reserve) × dmg | runs dry in (100 % hits, no overkill) |
|---|---|---|
| ar_1 | 240 × 12 = 2 880 | R3 |
| smg_1 / smg_2 | 2 688 / 3 024 | R3 |
| sr_1 / rl_1 | 2 475 / 2 430 | R3 |
| ar_2, sg_1, sg_2, lmg_1 | 3 072 – 4 620 | R4 |
| lmg_2 | 5 760 | R5 |

The doc never says whether the reserve refills per round. Read literally as "carried per run", no weapon reaches R6.

**Suggestion.** Add a DEC-096 row: "classic reserve refills to max at each round start". The reserve then limits play *within* a round, which is what the numbers suggest was meant. Check: R10 needs ≈ 8 700 HP, and ar_1's 2 880 covers about a third of the round, so reloading and swapping weapons still matter. An alternative is to keep `reserve −1` for classics and keep the finite ammo for rl_1 and the Specials only. Either way, B2 must not ship the table as is.

## 2. [HIGH] The second weapon of each class is a vertical upgrade, so the first becomes a dead option

**Where**: DIR-069 §1.1 l.43 ("iki farklı soruya birer cevap"), 3a l.80–89, 3c l.114–123, lock order §6 l.186 and DIR-077 l.175–184.

**Numbers** (`r1_audit.py` §1g). "Sustained" = magazine damage / (magazine time + empty reload). "Plated" = Guard R10 wearing its body plate; 63.5 % of R10 Guards do (CFG:192–196).

| pair | 2nd better on | 2nd worse on |
|---|---|---|
| ar_1 → **ar_2** (L8) | DPS 133 vs 120, sustained 78 vs 72, run damage 3 072 vs 2 880, armour ×1.2, full range 40 vs 30 m, Guard TTK 1.08 vs 1.20, plated 1.18 vs 1.60, Exec 23 vs 30 shots | nothing |
| sg_1 → **sg_2** (L21) | DPS 367 vs 86, sustained 110 vs 50, run damage 4 620 vs 3 528, full range 8 vs 6 m, move ×0.92 vs 0.90, Guard TTK 0.30 vs 1.67 | nothing |
| lmg_1 → **lmg_2** (L26) | DPS 168 vs 132, sustained 101 vs 82, run damage 5 760 vs 4 400, armour ×1.3, Guard TTK 0.86 vs 1.17 | move ×0.70 vs 0.80 |
| smg_1 → **smg_2** (L17) | DPS 126 vs 120, run damage 3 024 vs 2 688, armour ×0.9 vs 0.7, pen 1 vs 0, Guard TTK 1.22 vs 1.27 | sustained 70.7 vs 71.0, move ×0.96 vs 1.00 |

There is also role overlap. ar_1, smg_1, smg_2 and lmg_1 sit within 6 % of each other on unarmoured DPS (120–132) and Guard R10 TTK (1.17–1.27 s) (§1h). Unlocking smg_2 at L17 retires smg_1, sg_2 at L21 retires sg_1, and lmg_2 at L26 retires lmg_1. That breaks the "on silah = on ayrı soru" pillar.

**Suggestion.** Script §4 recomputes these values:

| slot | change | result |
|---|---|---|
| ar_2 | dmg 16 → **14**, head extra ×1.15 → **×1.30** | DPS 117 < 120, sustained 68 < 72, run damage 2 688. Guard head TTK stays 0.41 s vs ar_1 0.60 s, so the precision identity is kept |
| smg_2 | dmg 14 → **12**, armour ×0.9 → **×0.8** | DPS 108. Loses to smg_1 on raw DPS, keeps stagger 0.45, penetration and ammo efficiency (Runner-stopper role) |
| sg_2 | pellets 10 → **9**, full damage ≤ 8 → **5 m**, reload 1.7 → **2.4 s** | burst 330, sustained 73. Point-blank king, falls behind sg_1 after 4 m (with the #5 fix) |
| lmg_1 | reload 4.4/5.2 → **3.6/4.2**, move ×0.80 → **×0.88** | sustained 88 vs lmg_2 92: the mobile box vs the static breaker |
| lmg_2 | RPM 420 → **360** | DPS 144, Guard TTK 1.00 |

## 3. [HIGH] The rl_1 armour arithmetic contradicts REQ-088, so one rocket does not kill a plated Guard

**Where**
- DIR-069 §5 l.175: "R10 Guard tam patlamada 160 − 88 = 72 kalır → doğrudan isabet şart"
- l.123: "1 doğrudan"
- Code: `Damage/RegionalDamageBody.cs:16–21` and `:170–178`. "Surplus never carries … Fully armoured enemy takes zero health damage until a plate breaks (REQ-088)". The direct-hit path works the same way (`:122–127`): a hit on an armoured region goes entirely into the plate.

**Numbers** (§1c). Take a fully plated R10 Guard. The blast shares are 40 / 72 / 48 against plates of 22 / 38 / 28. All three plates break, the surplus is discarded, and **health takes 0**. The 110 direct hit on the Upper body only breaks the body plate. Across the CFG armour odds, the chance that one rocket (direct Upper hit plus full blast) kills a Guard is **R5 88 % · R8 54 % · R10 50 % · R12 34 % · R15 18 %**. Separately, blast-only kills of *unarmoured* Guards stop at R11, because Guard HP 164.6 > 160.

**Suggestion.** Either:
- (a) keep the code rule and fix the doc: plated Guards need 2 rockets, 3.0 s apart, which already fits "kendine risk, küme silahı"; or
- (b) add `explosive.directIgnoresArmor = true` and set the direct hit to 110 → **160**. The worst case at R10 is then 160 ≥ 155.6, so one rocket kills even a fully plated Guard.

Option (a) is simpler, and rl_1 still one-shots every Standard/Runner within 3.59 m at R10.

## 4. [HIGH] Special kill potential: Namekuji is a round-ender, and Specials take about a third of all kills

**Where**
- DIR-069 §7a l.221–229, §7b l.239–247, §7c l.259 ("Raiju azami 96 … ≤ 40 + yeniden doğanlar; Namekuji ≈ 30–50")
- Round data: CFG:236–243 (pool `12 + 6(R−1) + ⌊2.5(R−1)^1.15⌋`, active `min(7+2R, 32)`)
- Special spawn: CFG:106–117

**Model** (§2).
- The Special appears mid-pool (window 0.3–0.7, taken as 0.5). "Killable" = unspawned half of the pool + alive.
- **Raiju**: Monte Carlo nearest-unhit chain with a 6 m hop, crowd spread uniformly in a disc around the player. Each orb is charged 0.25 + 0.9 s, plus a 3.6 s empty reload. Zombies killed are replaced by spawns that walk in for 9.6 s (18–35 m band at 2.7 m/s). An Executioner in the chain counts as half a kill.
- **Namekuji**: six 1.6 m puddles close a 3 m ring. A non-Executioner that crosses a live puddle dies, with 80 % coverage. Throughput is bounded by the spawn refill of 2 per 0.45 s.

| R | pool | alive | Raiju chain (8 m / 12 m swarm) | **Raiju kills/pickup** | **Namekuji kills/pickup** | killable |
|---|---|---|---|---|---|---|
| 4 | 38 | 15 | 7.0 / 4.9 | 28 | 34 | 34 |
| 8 | 77 | 23 | 7.8 / 6.6 | 41 | 59 | 61 |
| 10 | 97 | 27 | 7.9 / 7.1 | 47 | 73 | 75 |
| 15 | 147 | 32 | 8.0 / 7.5 | 54 | 101 | 106 |

**Findings**
- Both Specials clear most of a round, so the "wonder" feel works. For comparison, ar_1 over the same 40 s at 60 % hits kills 19–28 Standards.
- Namekuji is **1.4–1.9× Raiju** from R8 on, and **1.5–2×** the doc's 30–50. From R6 on it removes nearly every killable non-Executioner.
- Raiju fires its 12 orbs in 24.6 s and Namekuji its 24 blobs in 16.4 s. The 40 s / 45 s time caps only bind when a player hoards ammo.
- The run expects 6.15 Special pickups (R4–R15, first spawn only). That gives about **366 of 1 167 pool kills (31 %)**: 24 % if every pickup is Raiju, 39 % if every pickup is Namekuji. The WP-041 risk proxy and the gold/XP pacing both ignore this share.
- The Guard rules are inconsistent. A direct gel hit on a Guard above R10 only takes 60 % (l.240), but walking into any puddle kills a Guard in every round (l.242). The puddle is strictly stronger, so the direct-hit rule never matters.

**Suggestion.** Namekuji: puddle life 25 → **12 s**, puddle cap 12 → **6**, reserve 16 → **8**, and the slip kill for Guards follows the direct-hit rule (R ≤ 10). Leave Raiju at 3/9 and 8 targets. Its per-pickup kill count of 28 → 54 already matches "most of an early round, half of a late one". Add a "Special kill share" column to the WP-041 simulator with a target band of ≤ 20 %.

## 5. [HIGH] sg_1 ring geometry: the outer ring misses the real hitbox, so the doc's 1-shot claims fail

**Where**
- DIR-069 3c l.118: "1 (≥ 4 pellet) / 1 (≥ 8 @ ≤ 6 m)"
- 3a l.84 pattern "iç halka 3 @ %40 + dış halka 4 @ %100"
- 4a l.139: cone hip/focus 7.5° / 5.5°
- Hitbox: `Enemies/ZombieBodyBuilder.cs:81` `UpperHitboxRadius = 0.26f`

**Numbers** (§1d, top-down hip / focus, radial cone model). The outer ring lands inside 0.26 m only up to **1.98 m hip** and **2.70 m focus**.

| dist | sg_1 pellets (dmg), hip / focus | sg_2 pellets (dmg), hip / focus |
|---|---|---|
| 2 m | 4 (36) / 8 (72) | 10 (110) / 10 (110) |
| 4 m | 4 (36) / 4 (36) | 7.4 (82) / 9.1 (100) |
| 6 m | 1 (9) / 4 (36) | 4.5 (50) / 6.5 (72) |

A planar, yaw-only projection is kinder (6 pellets, 54 dmg, at 3–4 m) but still misses 70.7. Only with the 0.45 m BodyRadius as the target does sg_1 one-shot an R10 Standard, and then only to about 3.4 m hip. sg_2's Gaussian pattern keeps more pellets on target at every distance. That adds to #2.

**Suggestion.** sg_1 rings: **3 @ 0.25 + 4 @ 0.55**, cone **6.0° / 4.5°**, per-shell 0.55 → **0.45 s**. The script checks the result: 8 pellets = 72 dmg ≥ 70.7 out to 4 m, and sustained DPS rises from 50 to 54.

## 6. [MED] The XP pacing uses a rate that contradicts the doc's own anchor

**Where**: DIR-077 §1 S5 l.30, §3.2 l.154–167, §4.3 l.252.

**Numbers** (§3c)
- The XP curve arithmetic is exact: cumulative L10 7 350, L20 43 700, L30 129 050, L50 526 750.
- Code XP matches the doc's run anchors: an R15 clear gives **2 919** (doc 2 900) and an R8 clear gives **795** (doc 790) (CFG:364–366, `ProgressionConfigSection.cs:145`).
- The doc's own "R15 ≈ 28 min" gives 2 900/28 × 60 ≈ **6 200 XP/h**, not 4 500. I calibrated clear time to 28 min with 20 s per-round overhead. Every death-round mix (R6, R9, R11, R16) then gives **6 100–6 600 XP/h**:
  - L10: **1.1–1.2 h** (doc 1.6)
  - L20: **6.6–7.1 h** (doc 9.7; target 7–12)
  - L30: **19.5–21.0 h** (doc 28.7)
- The 4 500 XP/h figure needs R15 runs of about 40 min. With an effective DPS of 60, the R15 run takes 47 min: L20 11.1 h, L30 32.7 h. With DPS 42 it takes 65 min: L20 15.3 h, L30 45 h.

So the S5 claims depend on a run length nobody has measured.

**Suggestion.** Take the XP/h from WP-045 telemetry before DEC-097. If 28-min runs hold, `110L + 12L²` gives **L20 8.2 h and L30 24.5 h**, which is mid-band. Also fix table row L2: it should read "Req 240, cum 110", not "Req 110, cum 0". The other rows list Req(L), and Req(2) = 240.

## 7. [MED] The meta ring budgets are not reachable in the stated time

**Where**: DIR-077 §3.1 l.149, §3.3 l.175–207, §3.4 l.211–217.

**Numbers** (§3c). XP-only Kül is 150–160 Kül/h at ≈ 6 150 XP/h, and 112 Kül/h at the doc's 4 500.

| ring | Kül needed | target | measured (XP-only) | with early achievements / first-R10 |
|---|---|---|---|---|
| Ring 1 | 330 | ≤ 1 h | 2.0–2.2 h (2.9 h at 4 500) | ≈ 1.7 h (≈ 45 early achievement Kül) |
| Ring 1+2 | 2 330 | ≤ 10 h | 14.5–15.2 h (20.7 h at 4 500) | ≈ 11–12 h (≈ 400 achievement + ≈ 150 first-R10) |
| full | ≈ 7 300 | ≈ 45 h | ≈ 34–36 h (≈ 58 h at 4 500) | — |

The full-tree time is the reverse problem: it runs *faster* than stated under the doc's own run anchor.

**Suggestion.** Ring 1 prices: smg_1 40 → **20**, sg_1 60 → **40**, Çekim I 50 → **30**, Passive K1 60 → **30** each. Ring 1 then costs ≈ 180 Kül ≈ 1.0 h. `Kül = XP/40` → **XP/32** brings Ring 1+2 to ≈ 10 h. Then recheck the full tree against the §4.3 XP-tempo row.

## 8. [MED] Spend share and wallet depend only on Hearth cadence, and the pin bypasses the capstone gate

**Where**: DIR-077 §2.1 l.46, §2.5 l.121, §2.6 l.132–141, §4.2 l.238, §4.3 l.245–249, K6 l.317 and l.186.

**Bot upper bound** (§3b). Greedy ar_1 capstone path, Kese at R3, ≤ 2 cards and ≤ 3 Hearth per round, all gold picked up:
- R1–R15 income **7 139** (6 505 base + Kese +10 %), spent **97.6 %**, R10–R15 spend 98.5 %
- first keystone **R4**, capstone **R8**, Hearth **16**, R15 wallet **173**

This confirms DIR-077 §4.2 (99 %, R4, R8, 15 Hearth, wallet 37). Gold, not the 2-card limit, is the binding constraint through R10: the wallet stays < 150 every round. The card limit only binds from R11 (wallet 280–340).

**Median model**: 90 % pickup, 1 and 2 cards in alternate rounds. Sensitivity grid (§3b):

| gold kept / Hearth stokes per round | spend share | R15 wallet | first KS / capstone |
|---|---|---|---|
| 0 / ≤ 1 | 67 % | **2 092** | R5 / R8 |
| 0 / ≥ 2 | 99 % | 42 | R5 / R8 |
| 150 / ≤ 1 | 51 % | **3 153** | R7 / R10 |
| 150 / ≥ 2 | 96 % | 283 | R7 / R10 |
| 300 / ≤ 1 | 42 % | **3 686** | R8 / R11 |

- The ≤ 900 wallet target and the ≥ 60 % R10–R15 share are met **only if players stoke the Hearth at least twice a round**. At one stoke a round, the wallet ends in the red zone (> 1 500).
- A median with a 150–300 reserve gets the first keystone at R7–R8, outside the R4–6 target.
- The capstone arrives at R8 for anyone who pins it. The M3 mastery gate only filters *offers*, and the Nişan guarantees the path, so K6 does not delay anything.

**Suggestion**
- Hearth slope 30 → **20**: 20 stokes cost 4 800 instead of 6 700, which makes a 1-stoke habit cheap to break.
- Add a HUD nudge when wallet > 2 × the next Hearth price.
- Decide K6: either a pin cannot target a capstone before M3, or restate the capstone median target as R8–10.
- Fix the formula text too: §2.1 and K3 say `50 + 30·n` (n = 20 → 650, total 7 300), but the §2.6 table is `50 + 30(n−1)` (620, total 6 700). The bot is barely sensitive to the difference (15 vs 16 Hearths), but B1 code needs a single formula.

## 9. [MED] The §1.3 progression-curve claims (early / mid / late) fail on three points

**Where**: DIR-069 §1.3 l.45.

- **Early, "her şey Standard'ı 1–5 mermide"**: smg_1 needs 5/5/**6/6/7** shots in R1–R5 (Standard HP 34 → 50.3). Every other weapon is ≤ 5. **Suggestion:** say "≤ 7 (smg_1 by design)", or use smg_1 dmg 9 / 800 RPM (same DPS, 6 shots at R5).
- **Mid, "zırh + Guard yükselince"**: the DEC-021 armour rows only apply on the exact round (`PrototypeConfig.cs:339–353`, `o.Round == round`). So Standard body-plate odds go **0.19 (R7) → 0.36 (R8) → 0.22 (R9) → 0.23 (R10) → 0.44 (R11) → 0.25 (R12)**. Armour spikes on 3 rounds instead of rising. Gold income is also non-monotone: R8 449 > R9 443, R11 721 > R12 675, R14 1 026 > R15 936. The armour-multiplier ranking itself holds: on a plated R10 Guard, ar_2 1.18 s and lmg_2 1.14 s against smg_1 1.73 s and sr_1 3.75 s. **Suggestion:** make the override rows sticky (a row applies from its round until the next row), or add rows for R9/10/12/13/15 (S-03 WP-050 owner).
- **Late, "Executioner ×5 ve 40 zombi tavanı"**: `ActiveCap` is **32** (CFG:242, reached at R13). 40 is only the safety ceiling (CFG:243). Executioners appear from **R8** (CFG:209, override weight 6 at R8), not R11+. For the claim that sr_1 decides the late game by piercing: at R15 (Standard 91.1 HP) a body shot never one-shots, and the headshot line kills 2. sr_1 is not decisive in the late game without Kabuto-wari. See #10 for the sr_1 fix.

## 10. [LOW] Arithmetic and wording slips in the docs

Every other derived number in DIR-069 3c recomputes exactly: DPS, all shot counts, Guard TTK and magazine damage for the 10 slots. The slips:

| where | doc | recomputed | fix |
|---|---|---|---|
| DIR-069 l.123 rl_1 DPS | 96 (270/2.8) | **90**: the doc's own 20 RPM = 0.2 + 2.8 s | write 90, or state reload-cancels-recovery |
| l.123 rl_1 "5 m çapta R10 Standard'ları siler" | 2.5 m radius | kill radius vs Standard R10 **3.59 m** (Ø 7.2 m); vs unarmoured Guard 1.89 m | "≈ 3.6 m yarıçap" |
| l.120 lmg_1 "tek şarjörle R10'da 7 Guard" | 7 | **6** (15 shots each; 1 100/155.6 ignores overkill) | 6 |
| l.122 sr_1 "4 gövde → efektif 176 DPS" | 44 × 4 | **130** if pen = 4 bodies total, **148** if pen = 4 extra (−20 %/body: 55/44/35.2/28.2/22.5) | define `penetration` as *extra* bodies (ar_1 pen 1 must mean one extra) and write 148 |
| l.122 sr_1 line | "dört gövde" | R1 body line kills **3** (4th takes 28.2 < 34); R10 head line kills **3** (4th takes 70.4 < 70.7) | sr_1 dmg 55 → **58**, loss 0.20 → **0.15**: R1 body line 4, R10 head line 4 (§4) |
| l.121 lmg_2 "en yüksek sürekli DPS" | — | lmg_2 101 vs sg_2 **110** point-blank | "8 m ötesinde en yüksek" |
| DIR-077 l.46 / l.314 Hearth | 50 + 30n | table = 50 + 30(n−1) | one formula |
| DIR-077 l.158 XP row L2 | Req 110 cum 0 | Req(2) = 240, cum 110 | fix row |
| DIR-077 l.238 income | 6 573 | 6 505 base, 7 139 with Kese | note that Kese is excluded |

## 11. [LOW] Live data is still M3 (by design), and v3 raises baseline DPS

**Where**: `Weapons/Arsenal/Resources/Arsenal/WD_ar_1.asset`, `WD_smg_1.asset`, `WD_sg_1.asset` (B1 migration step 1, DIR-069 l.1097).

**Differences** (§1j)
- ar_1: 9 fields differ, including dmg 11 → 12, RPM 510 → 600, falloff 45/45/1.0 → 30/60/0.70, penetration 0 → 1
- smg_1: 8 fields differ, including RPM 780 → 900 and armour ×1.0 → 0.7
- sg_1: 8 fields differ, including dmg 8 → 9, falloff start 22 → 6 m, armour ×0.8

The live sg_1 has `fireMode: 2` (Auto, `WD_sg_1.asset:28`) and `reloadKind: 0` (Magazine), but v3 wants a Single pump with PerShell reload.

v3 raises baseline DPS: ar_1 +28 % (93.5 → 120), smg_1 +15 % (104 → 120), sg_1 +12.5 % (76.8 → 86.4). The enemy curve does not change, and the Hearth adds up to +40 % on top. **Suggestion:** re-run the WP-041 risk proxy on v3 plus Hearth plus the Special share before DEC-096/097. Have B2 set sg_1 to `fireMode 0` and `reloadKind PerShell`.

---

## Appendix A: recomputed 3c (all pellets, Upper, unarmoured)

| slot | DPS | Std R1 | Std R10 | Guard body / head | Exec R10 | Guard TTK s | mag dmg | sustained DPS | run dmg | Guard plated TTK |
|---|---|---|---|---|---|---|---|---|---|---|
| ar_1 | 120 | 3 | 6 | 13 / 7 | 30 | 1.20 | 360 | 72.0 | 2 880 | 1.60 |
| ar_2 | 133 | 3 | 5 | 10 / 5 | 23 | 1.08 | 384 | 78.0 | 3 072 | 1.18 |
| smg_1 | 120 | 5 | 9 | 20 / 10 | 45 | 1.27 | 336 | 71.0 | 2 688 | 1.73 |
| smg_2 | 126 | 3 | 6 | 12 / 6 | 26 | 1.22 | 336 | 70.7 | 3 024 | 1.67 |
| sg_1 | 86 | 1 | 1 | 3 / 2 | 5 | 1.67 | 504 | 49.9 | 3 528 | 1.67 |
| sg_2 | 367 | 1 | 1 | 2 / 1 | 4 | 0.30 | 220 | 110.0 | 4 620 | 0.30 |
| lmg_1 | 132 | 4 | 7 | 15 / 8 | 33 | 1.17 | 1 100 | 81.8 | 4 400 | 1.50 |
| lmg_2 | 168 | 2 | 3 | 7 / 4 | 15 | 0.86 | 1 440 | 101.2 | 5 760 | 1.14 |
| sr_1 | 44 | 1 | 2 | 3 / 2 | 7 | 2.50 | 275 | 33.5 | 2 475 | 3.75 |
| rl_1 | **90** (doc 96) | 1 | 1 | 1 (unarmoured) | 2 | — | 270 | 90.0 | 2 430 | see #3 |

Notes on the table:
- Shotgun rows assume every pellet hits. #5 shows that this assumption fails for sg_1 beyond 2 m.
- Plated TTK counts each projectile or pellet into the 38 plate with the surplus discarded, then the rest to health.
- Falloff for StdR10 by distance (§1e): smg_1 needs 10 shots at 20 m, 13 at 30 m and 17 at 40 m. It is the only slot that falls off sharply inside 30 m. ar_1 needs 7 shots at 40 m, and ar_2, lmg_2 and sr_1 stay flat.

## Appendix B: round table from code (expected values, pickup 1.0)

| R | pool | mix S/Run/G/Ex | gold | cumulative | XP (round + kills) |
|---|---|---|---|---|---|
| 1 | 12 | 12/0/0/0 | 29.6 | 29.6 | 32 |
| 4 | 38 | 31.4/6.6/0/0 | 119.1 | 286.8 | 80 |
| 5 | 48 | 36.1/8.7/3.2/0 | 184.2 | 471.0 | 106 |
| 8 | 77 | 50.4/16.6/7.0/3.0 | 448.9 | 1 430.4 | 194 |
| 10 | 97 | 60.5/23.6/10.3/2.7 | 516.6 | 2 389.9 | 238 |
| 11 | 107 | 63.1/26.5/11.7/5.7 | 720.7 | 3 110.6 | 284 |
| 15 | 147 | 82.6/37.2/20.7/6.5 | 935.6 | 6 505.4 | 388 |

How the table is modelled:
- Pool, mix and armour follow `RoundConfigSection.cs:58/67`, `PrototypeConfig.cs:259–279` (tier weight with exact-round override), the REQ-071 debut floor and the DEC-021 forced count (R11 Executioner 2).
- Gold = tier reward + expected armour-piece bonus 1/2/1 (`Economy/GoldRewardCalculator.cs`, CFG:279–281).
- The full per-round table and the bot/median purchase logs are in the script output.

## Limits of this audit

- The Special kill numbers come from a geometric model (disc swarm, 3 m puddle ring, 80 % coverage, 9.6 s walk-in), not from gameplay. Treat them as order-of-magnitude figures. The ranking Namekuji > Raiju > classic holds under every coverage value from 0.5 to 1.0.
- The time/XP model has a single calibration anchor (the doc's 28 min R15 run). Any hour figure above scales linearly with real run length.
- The shotgun geometry uses a cone with no bloom and the Upper capsule radius. Headshots, the Lower capsule and TP/FP ADS cones are not modelled.
- There are no LFS binaries, no Unity runs and no MCP connectors. Every number can be reproduced with `python3 tools/balance/r1_audit.py`.
