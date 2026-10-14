# Cloud balance audit R1: numeric check of the M4-R1 designs (WP-069 Arsenal v3 · Specials · WP-077 tree v2)

Date 2026-09-24 · branch `cloud/balance-audit-r1` from `main` a7f2557 · findings only, no game code or data changed.
Reproduce: `python3 tools/balance/r1_audit.py` (stdlib only; deterministic, `--seeds N` for more median-player runs). Every number below comes from that script's output.

**Sources read:** `planning/work-packages/WP-069-ARSENAL-V3-DIRECTION.md` (§1 tables 3a/3b/3c, §5, §6, §7), `planning/work-packages/WP-077-PROGRESSION-TREE-V2-DIRECTION.md`, `Assets/_Prototype/Core/Resources/PrototypeConfig.asset` (enemies, rounds, economy, progression, Special, DEC-021 rows), `Weapons/Arsenal/Resources/Arsenal/WD_{ar_1,smg_1,sg_1}.asset`, `Damage/RegionalDamageBody.cs`, `Core/Config/RoundConfigSection.cs`, `Core/Weapons/WeaponDefinition.cs`, `Weapons/Projectiles/ProjectileSystem.cs`, `Weapons/SpecialWeaponDirector.cs`, `planning/00-PROJECT-PLAN.md` (DEC table), the WP-041 balance proposal.

**Model assumptions** (marked in the script): upper-body hits at point blank unless stated; the armour rule is the live one (an armoured region absorbs the whole hit and any surplus is thrown away, `RegionalDamageBody.cs:142-157`, blast `:238-251`); orb collection is 90 % as in the WP-041 simulator; the tier mix is the expected value of the weighted draw plus the REQ-071 debut and DEC-021 forced minimums. Round time is WP-041 clear time (8 s at R1 to 83 s at R15, scaled on pool) plus 3 s countdown and 20 s shop, calibrated so a full R15 run takes the doc's ~28 min. The tree simulation models ar_1's weapon lane and the body lane (25 nodes) with WP-077 §2.1/§2.6 prices.

**What checks out:** almost every derived number in WP-069 §3c. That covers DPS, shots-to-kill against all four targets, Guard TTK and magazine damage for all 9 bullet weapons (only sg_2's "sustained" figure depends on how it's counted), the §3b 20 m flight times, the rl_1 blast falloff points (125/100/75/50) and self-damage (72/45/22), the rocket kinematics (7.9 m at 0.25 s; ≈23.6 m at 0.6 s; ≈0.52 s to 20 m), the Special cycle rates (Raiju 52 RPM, Namekuji 133 RPM, 0.56 s chain), and the enemy HP targets (34 / 70.7 / 155.6 / 353.6). On the WP-077 side, the XP-curve cumulative column (except the L2 row), the Ocak total 6 700, and the R8 = 790 / R15 = 2 900 XP runs (computed 795 / 2 919) also match. The problems are in the **rules the numbers meet** (armour, penetration, geometry), in **how the weapons compare with each other**, and in **pacing**.

---

## Findings ranked by impact

### 1 · 🔴 rl_1 Bohiya does almost nothing to armoured targets. The doc's armour math contradicts the live rule
- **Doc:** WP-069 §5 line 175 says a full blast on an R10 Guard leaves "160 − 88 = 72" after the plates, so a direct hit settles it. §3c line 123 says one direct hit kills a Guard R10, and "blast alone 160 ≥ 155.6".
- **Live rule:** the blast is split 25/45/30. Each armoured share is absorbed by its plate **and the surplus is discarded** (REQ-090, `RegionalDamageBody.cs:250`). A fully armoured Guard takes **0** health damage: the plates break and nothing carries over. The 110 direct hit lands on the upper-body plate and is absorbed too.
- **Recomputed** (armour odds from `PrototypeConfig.asset:191-196`):

| target | P(kill) blast only @1.8 m | P(kill) direct + blast | blast @3.5 m |
|---|---|---|---|
| Standard R10 | 91 % | 91 % | 47 % (doc: "siler" = wipes out) |
| Guard R10 | 8 % | **36 %** (doc: 100 %) | 0 % |
| Guard R15 | 0 % | **18 %** | 0 % |

- **Impact:** the launcher's advertised late-game "area + armour" role (§1 line 45) fails exactly where WP-069 says it decides the game (R11+). At 90 DPS and a 3 s cycle, it is the worst Guard answer in the arsenal.
- **Suggested value:** give the Launcher class an exception to REQ-090, a new `explosive.plateSurplusCarry = 1.0` for rl_1 only, so surplus flows to health. That makes the doc's own "72 remains" true, and direct hit + blast then kills a fully armoured R10 Guard. The direct 110 minus the 38 body plate carries 72 into health; the blast then deals 18 + 72 + 20 = 110 (the body plate is already gone). Total 182 ≥ 155.6. If REQ-090 must stay, set `blastDamage` 160 → 240 and `directHitDamage` 110 → 150 and re-measure. Either way this needs a DEC line and a test ("rl_1 vs HBL Guard R10").

### 2 · 🔴 Armour and head multipliers are dead: not read at runtime, and sr_1's ×1.6 could not matter anyway
- **Not read:** `headMultiplier` and `armorDamageMultiplier` exist only as fields (`WeaponDefinition.cs:102-103`) and in the asset builder. `ProjectileSystem.cs:40-56` copies falloff and penetration into the launch but **not** these two, and nothing else reads them (grep, excluding tests). So ar_2's ×1.15 head, sr_1's ×1.25 head, and every armour factor in table 3a (SMG ×0.7, ar_2 ×1.2, lmg_2 ×1.3, sr_1 ×1.6) do nothing in the current code.
- **sr_1 even once wired:** 55 damage already exceeds the largest plate (38), and surplus is discarded. So ×1.6 changes nothing on any plate. That also makes **sr_1's ArmorBreak core branch** (WP-069 §6 line 195, "+25 %/level") and the "Zırh Sökücü" keystone (WP-077 §2.3) **dead picks** on sr_1.
- **lmg_2 even once wired:** ×1.3 only helps against the leg plate (24 → 31.2 breaks 28 in one hit instead of two). The body and helmet plates need the same number of hits.
- **Recomputed plated-Guard R10 TTK** (body plate on), design multiplier vs live (multiplier not read): ar_2 12 vs 13 shots, smg_1 27 vs 25, smg_2 16 vs 15. Today SMGs are *better* against armour than designed.
- **Suggested value:** (a) B2 must carry both fields through `ProjectileLaunch`/`DamageHit` and get an EditMode test. (b) Replace sr_1's ArmorBreak with **Impact**, or redefine ArmorBreak as "surplus carries 50 % into health". (c) Keep lmg_2 ×1.3 only if a leg-plate niche is intended; otherwise drop it to ×1.0 and move lmg_2's armour identity onto ArmorBreak with the surplus-carry meaning.

### 3 · 🔴 Three dominated weapons: ar_1 (the default!), sg_1 and lmg_1
Pareto check on DPS, sustained DPS (including empty reload), plated-Guard TTK, total carried damage, full-damage range, penetration, move-while-firing and stagger (script §A5):

| pair | winner | loser | only compensation for the loser |
|---|---|---|---|
| **ar_2 ≻ ar_1** (strict) | 133 DPS · sustained 74 · plated Guard 1.18 s · range 40 m | 120 · 70.6 · 1.60 s · 30 m | magazine 30 vs 24, reload 0.2 s faster |
| **sg_2 ≻ sg_1** (strict) | 367 burst / 110 sustained · 4 620 total damage | 86 / 46 · 3 528 | 7-shell tube, cancellable reload |
| **lmg_2 ≻ lmg_1** (everything except move) | 168 DPS · sustained 100 · 5 760 total · pen 2 | 132 · 81 · 4 400 · pen 1 | move ×0.80 vs ×0.70 |

The unlock order (WP-077 §3.3) makes ar_1 obsolete at L8 (≈2 h), sg_1 at L21 and lmg_1 at L26. §1 line 43's "two different questions" does not show in the numbers.
- **ar_2:** `burstDelaySeconds` 0.26 → **0.32**. DPS drops 133 → 114 and Guard TTK becomes 1.26 s vs ar_1's 1.20. ar_2 keeps its plated-Guard (1.36 s) and headshot edge.
- **sg_2:** `reloadEmpty/Tac` 1.7 → **2.4** and `reserve` 40 → **24**. Sustained DPS drops 110 → 81 and total damage 4 620 → 2 860 (below sg_1's 3 528); the burst stays.
- **lmg_2:** `damage` 24 → **20** and `reserve` 180 → **120**. DPS 140, Guard TTK 1.00 s, total 3 600 (below lmg_1's 4 400). lmg_1 becomes the ammo/uptime pick.

### 4 · 🟠 Namekuji is 2–3× stronger than the doc estimates and overtakes Raiju. Both end whole rounds from R4 to R10
- **Doc:** §7c line 259 says Raiju gets ≤ 96 kills ("in practice ≤ 40 + respawns") and Namekuji ≈ 30–50.
- **Recomputed** (pool and active target from `RoundConfigSection.cs:14-44`; the drop comes at 30–70 % round progress; zombies do **not** avoid puddles per DEC-096(2); spawn band 18–35 m at 2.7 m/s):

| R | pool | left at drop | Raiju ≤ | Namekuji ≈ (arrival × window) |
|---|---|---|---|---|
| 4 | 38 | 11–27 | 96 | 63 |
| 8 | 77 | 23–54 | 96 | 97 |
| 10 | 97 | 29–68 | 96 | 114 |
| 15 | 147 | 44–103 | 96 (65 % of the pool) | 135 (92 %) |

- **Raiju:** the 12 shots are fired in 24.6 s, well under the 40 s cap. Ammo is the limit, not density: refill runs at 4.4 spawns/s, and the live active cap is **32**, not the 40 the doc uses.
- **Namekuji:** its 24 shots take 16.4 s, and the puddles keep killing for 25 s after that. Arrival rate is the only limit (1.5 → 3.3 zombies/s).
- **Carry-over:** an active Special survives `RoundEnded` (`SpecialWeaponDirector.cs:384-395` cancels only a *scheduled* drop), so leftover ammo continues into the next round.
- **Frequency:** expected drops over R4–R15 are 7.0.
- **"Wonder feel":** met with a lot of margin. The risk is the opposite: with P(drop) 0.35–0.53, R4–R10 become trivial on about half the rounds.
- **Suggested value:** keep Raiju as is (it is the reference feel). Namekuji: `puddleLifetimeSeconds` 25 → **12**, `maxPuddles` 12 → **8**, and slip death on Guard only when R ≤ 10 (mirror the direct-hit rule, §7b line 240). That brings R15 to ≈ 94 kills, in line with Raiju. Also correct the doc's "40 cap" to 32.

### 5 · 🟠 Kül pacing misses WP-077's ring budgets, and the level gates don't bind the first 6 weapons
Kül from XP (`XP/40`, §3.1 line 149) comes to 152 / 111 / 86 per hour for R15 / median-R9 / new-player profiles.

| ring (§3.4 line 213) | target | R15 runner | median | new player |
|---|---|---|---|---|
| 1 (330 Kül) | ≤ 1 h | 1.8 h | **2.5 h** | 3.3 h |
| 2 (cumulative 2 330) | ≤ 10 h | 10.5 h | **14.3 h** | 18.5 h |
| 3 (whole meta, 7 300) | 30+ h / "≈ 45 h" (§1 S5) | 43 h | 59 h | 76 h |

(Rings 2–3 count all 740 one-off Kül, so they are optimistic.)

For smg_1 through smg_2, the Kül cost binds before the level gate. smg_1 needs 1 600 XP of Kül but its L3 gate is only 350 XP, and it goes the same way up to smg_2 (32 800 vs 28 560). The level gate only starts to bind from sg_2 (L21) onward. §3.1's "seviye = tempo kapısı" (level is the pacing gate) therefore holds only for the last three weapons.
- **Suggested value:** Kül divisor 40 → **30**, and ring-1 prices smg_1 40 → **30**, sg_1 60 → **40**, Çekim I 50 → **40** (ring 1 ≈ 250 Kül). The median player then reaches ring 1 in ≈ 1.35 h and the R15 runner in ≈ 1.0 h. The script's §C4 recomputes this if the constants are changed.

### 6 · 🟠 Capstone timing depends on the pin, and the §4.2 bot's four claims don't all hold for one bot
- **Income:** the doc's R1–R15 figure of 6 573 sits between the live-config model at 90 % collection (5 855 without Purse, 6 272 with Purse from R3) and 100 % collection (7 139). That is within the doc's ±20 % tolerance, but it implies ~100 % orb pickup.
- **Bots at 90 % collection:** a greedy spend bot reaches 97 % spend and 13 Ocak, but its **first capstone is R13**. A capstone-rush bot gets **R8** but only 94 % spend, 13 Ocak and a 362 wallet. Only rush + 100 % collection reproduces the doc (R7, 97 %, 15 Ocak).
- **Median (200 seeds, 30 % cushion):**
  - *Pins the capstone route:* keystone R5, capstone **R9** ✓.
  - *Does not pin:* WP-077 K6 keeps the capstone out of offers until mastery M3 (≈ 10–12 runs per weapon), so **98 % of runs reach R15 without a capstone**.
  - *Hoarder (keeps 50 %, ≤ 1 Ocak/round):* spend 66 %, wallet R15 **1 963**, which is past the > 1 500 red line in §4.3 line 247.
- **Suggested value:**
  - Offer the capstone without M3 once all its prerequisites are owned (M3 → M1 = 150 kills), or have the NEXT strip point at the pin whenever a capstone is one step away.
  - Restate §4.2 as two bots (spend-max / rush) at 90 % collection.
  - Add a ≤ 900 wallet test for the hoarder profile in B3 (`EconomySimulator.TreeModel`).

### 7 · 🟠 sr_1's line-piercing claim is overstated. It doesn't one-shot a Standard from R7
- **Claims:** §3c line 122 says "4 bodies → effective 176 DPS", and the role is "line piercer" (§1 line 44).
- **Recomputed:** with −20 % per body the line deals 55 / 44 / 35.2 / 28.2 = 162 per shot, i.e. **130 DPS**, not 176. It kills **0** bodies per shot against an R10 Standard (70.7 HP) and one body at R5. Its single-target DPS is 44 and sustained 29.1, the lowest of all 10 weapons.
- **Suggested value:** `damage` 55 → **75** and `penetrationDamageKeep` 0.80 → **0.90**. The line becomes 75 / 67.5 / 60.8 / 54.7: it one-shots Standard up to R10 and kills all 4 bodies up to R6. DPS rises to 60, still the lowest.

### 8 · 🟡 ar_1 and smg_2 have the same kill profile
- **Shots to kill** (Std R1 / Std R10 / Guard / Exec): ar_1 3/6/13/30, smg_2 3/6/12/26.
- **Guard TTK:** 1.20 vs 1.22 s. **Sustained DPS:** 70.6 vs 69.0.
- smg_2's "stopper" identity rests only on stagger 0.45.
- **Suggested value:** smg_2 `damage` 14 → **17**, `rpm` 540 → **440**, `magazine` 24 → **20**, `reserve` 192 → **160**. Shots to kill become 2/5/10/21 ("few heavy hits"), DPS stays 124.7 and Guard TTK 1.23 s.

### 9 · 🟡 The early-curve claim is broken by smg_1, the first unlock
- **Claim:** §1 line 45 says "R1–5: everything drops a Standard in 1–5 bullets".
- **Recomputed:** smg_1 needs **6 at R3–R4 and 7 at R5**. Keeping it ≤ 5 through R5 would take damage ≥ 10.06 at 900 RPM (151 DPS), which is off-identity.
- **Suggested value:** change the claim to "1–7 bullets (smg_1 7)", or use `damage` 9 / `rpm` 800 (DPS unchanged 120; 4–6 bullets for R1–R5).
- **Also on the curve claims:**
  - *Mid-game:* the rise in armour is **one-round spikes**, not a trend. DEC-021 rows match the exact round only (`PrototypeConfig.TryGetRoundOverride`), so the Standard body-plate chance runs R7 0.19 → **R8 0.36** → R9 0.22, and likewise at R11 and R14.
  - *Late game:* the "40-zombie cap" is 32 live (`PrototypeConfig.asset:242`); 40 is the safety ceiling.

### 10 · 🟡 sg_1's one-shot range is 3.4 m, not 6 m
- **Claim:** §3c says one shot needs ≥ 8 pellets at ≤ 6 m.
- **Geometry:** the outer ring of 4 pellets sits at 100 % of the cone half-angle, 7.5° hip / 5.5° focus. It stays on a 0.45 m body (`BodyRadius`, `PrototypeConfig.asset:138`) only out to **3.42 m hip / 4.67 m focus**. Beyond that, 4 pellets land (36 damage), which is enough only for R1–R2 Standards.
- **Suggested value:** change the claim to "≤ 3.5 m hip / ≤ 4.5 m focus". If 6 m is really wanted, set the outer ring `radiusShare` 1.00 → **0.57**.

### 11 · 🟡 Live data is still v1 (B1 state). §3c describes numbers no build has yet
`WD_*.asset` vs table 3a:

| weapon | live DPS | design DPS | change |
|---|---|---|---|
| ar_1 | 93.5 (dmg 11, 510 RPM; `WD_ar_1.asset:29,33`) | 120 | **+28 %** |
| smg_1 | 104 | 120 | +15 % |
| sg_1 | 76.8 | 86.4 | +12 % |

- **Fields that also change:** no falloff at all live (`falloffStart == falloffEnd`), penetration 0 live vs 1 designed (ar_1), sg_1 reload 2.2 s live vs 4.6/5.1 s per-shell designed, and the sg_1 ring pattern (7 @ 0.6 live vs 1 + 3 @ 0.4 + 4 @ 1.0 designed).
- This matches migration step 1 (`WP-069 §6`, "birebir kopyalanır": copied one-for-one), so it is not a bug.
- **Action for B2:** re-run the WP-041 simulator and WP-042 TTK tests after B2, because the jump in default-weapon DPS moves every pacing number above.

### 12 · 🟡 DEC number clash
WP-069 line 5 says the Arsenal numbers "become **DEC-096 (Arsenal denge v3)**". `00-PROJECT-PLAN.md:168` already assigns **DEC-096** to the WP-076 AI v3 rules. Next free per the plan: ask C-00. Suggested: renumber the WP-069 reference to the next free DEC (≥ DEC-097; WP-077 claims DEC-097+ too, so C-00 should assign both).

### 13 · ⚪ Small arithmetic and consistency slips (fix in the doc)
| where | doc | recomputed / issue | fix |
|---|---|---|---|
| WP-069 §3c line 123 | rl_1 96 DPS | 270 / (0.2 + 2.8 s) = **90** | 90 |
| WP-069 §3c line 120 | lmg_1 "7 Guards per magazine at R10" | 100 / 15 = **6** (no overkill carry) | 6 |
| WP-069 §3c line 123 | "5 m çapta" (5 m diameter) | kill radius vs R10 Standard is 3.5 m (**7 m** diameter), unarmoured only (47 % overall) | "3,5 m yarıçapta, zırhsız" (3.5 m radius, unarmoured) |
| WP-069 §3c line 119 | sg_2 "110 sustained" | 220 / (0.3 + 1.7) = 110 ✓, but Exec R10 needs 4 shells → one reload → TTK 2.3 s | add the TTK |
| WP-069 §1 line 48 | ballistic drop ≤ 0.2 m at 45 m | smg_1 0.27 m at 40 m, ar_1 0.26 m at 60 m, smg_2 0.22 m at 42 m | "≤ 0,3 m" |
| WP-077 §3.2 line 158 | L2 row cumulative 0 | 110 (Req(1)); rows mix "Req to leave L" and "cumulative to reach L" | relabel the row L1 or set 110 |
| WP-077 §2.1 line 46 / §2.6 line 138 | Ocak `50 + 30·n`, first price 50 | only true with n = number already stoked (0-based) | write `50 + 30·owned` |
| WP-077 §2.1 line 47 vs §2.2 line 69 | Purse prerequisite "—", expected R3–R5 | the grid rule K2→K3 requires ≥ 1 body keystone (expected R9) | exempt Purse from the K3 rule explicitly |

### 14 · ✅ The XP curve holds its L10/L20 targets for most players
`Req(L) = 100L + 10L²` reproduces the doc's cumulative column (L10 7 350 · L20 43 700 · L30 129 050 · L50 526 750). A first full R15 run reaches L6 on the new curve vs L9 on the old one, as claimed.

| profile | XP/h | L10 | L20 | L30 | L50 |
|---|---|---|---|---|---|
| doc flat 4 500 | 4 500 | 1.6 h | 9.7 h | 28.7 h | 117 h |
| reaches R15 | 6 060 | 1.2 h | **7.2 h** | **21.3 h** | 87 h |
| median (dies R9) | 4 450 | 1.7 h | **9.8 h** | **29.0 h** | 118 h |
| new player (dies R6) | 3 446 | 2.1 h | 12.7 h | 37.5 h | 153 h |

The §4.3 target band (L10 1.2–2.0 h, L20 7–12 h) holds for everyone except new players at L20 (12.7 h, just over). No change needed. Note that the doc's "4 500 XP/h" is a median-R9 figure, not an "R15 + R8 mix".

---

## Round, income and XP reference (live config, 90 % collection)
| R | pool | active | gold | cumulative gold | XP | cumulative XP |
|---|---|---|---|---|---|---|
| 1 | 12 | 9 | 27 | 27 | 32 | 32 |
| 5 | 48 | 17 | 166 | 424 | 106 | 326 |
| 8 | 77 | 23 | 404 | 1 287 | 194 | 795 |
| 10 | 97 | 27 | 465 | 2 151 | 238 | 1 242 |
| 11 | 107 | 29 | 649 | 2 800 | 284 | 1 526 |
| 14 | 137 | 32 | 924 | 5 013 | 382 | 2 531 |
| 15 | 147 | 32 | 842 | 5 855 | 388 | 2 919 |

The income spikes at R8/R11/R14 come from the DEC-021 Executioner rows (25 gold each, plus armour bonus). The full per-round table and every intermediate number are in the script output.

## What this audit could not check
- **Nothing ran in Unity:** no hit-registration spread, no crowd density or pathing. Raiju's "8 targets per shot" and Namekuji's arrival rate are upper-bound models.
- **Missing input:** the FACTS.md referenced by WP-077 (`wp-077/council/FACTS.md`) is not in the repo. The income comparison is against the doc's quoted totals.
- **New branches unpriced:** ArmorBreak, Impact and Cluster have no economy rows yet. The script prices them like Piercing / Explosion.
