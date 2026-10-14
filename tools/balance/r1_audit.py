#!/usr/bin/env python3
"""Balance audit R1: independent recomputation of the M4-R1 designs.

Inputs are transcribed from the repo (no Unity needed, stdlib only):
  * planning/work-packages/WP-069-ARSENAL-V3-DIRECTION.md  (tables 3a/3b/3c, §5, §7)
  * planning/work-packages/WP-077-PROGRESSION-TREE-V2-DIRECTION.md (§2.6, §3.2, §4.2)
  * Assets/_Prototype/Core/Resources/PrototypeConfig.asset (enemy, round, economy, progression)
  * Assets/_Prototype/Weapons/Arsenal/Resources/Arsenal/WD_*.asset (live WeaponDefinition v3)
  * Damage/RegionalDamageBody.cs rules: an armoured region absorbs the whole hit, surplus is
    discarded (REQ-083/REQ-090); blast split 25/45/30, no crit multiplier on blast (REQ-089).

The live WD_*.asset values are parsed from disk when the files are present, so a later B2 data
change shows up in section A0 without editing this script.

Usage:  python3 tools/balance/r1_audit.py            (prints every table as markdown)
        python3 tools/balance/r1_audit.py --seeds 500 (more Monte-Carlo runs for the median player)
"""
import argparse
import math
import os
import random
import re

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ASSETS = os.path.join(ROOT, "prototype", "DeathsDemolition_PlayableConcept", "Assets", "_Prototype")
ARSENAL_DIR = os.path.join(ASSETS, "Weapons", "Arsenal", "Resources", "Arsenal")

# ---------------------------------------------------------------- live config (PrototypeConfig.asset)
BASE_HP = 34.0
HP_PER_ROUND = 0.12
TIERS = {  # tier: hpMul, gold, bonusXp, intro, weight, growth, max, armour(h,b,l,growth,max), speed
    "Standard":    dict(hp=1.0, gold=2, xp=0, intro=1, w=100, g=0.0, wmax=100, arm=(0.15, 0.12, 0.08, 0.012, 0.55), speed=2.7),
    "Runner":      dict(hp=0.85, gold=5, xp=1, intro=3, w=18, g=3.0, wmax=45, arm=(0.15, 0.12, 0.08, 0.012, 0.55), speed=4.6),
    "Guard":       dict(hp=2.2, gold=10, xp=3, intro=5, w=9, g=1.6, wmax=26, arm=(0.45, 0.50, 0.35, 0.015, 0.90), speed=2.2),
    "Executioner": dict(hp=5.0, gold=25, xp=8, intro=8, w=3, g=0.7, wmax=12, arm=(0.60, 0.70, 0.50, 0.020, 0.95), speed=1.7),
}
PLATE = {"head": 22.0, "upper": 38.0, "lower": 28.0}
EXPOSED = {"head": 2.0, "upper": 1.0, "lower": 0.8}
BLAST_SHARE = {"head": 0.25, "upper": 0.45, "lower": 0.30}
ARMOR_GOLD = {"head": 1, "upper": 2, "lower": 1}
# DEC-021 rows (exact-round match, PrototypeConfig.TryGetRoundOverride): armour rows scope Standard+Runner.
OVERRIDES = {
    8: dict(arm=(0.40, 0.36, 0.30), exec_w=6, forced=0),
    11: dict(arm=(0.48, 0.44, 0.38), exec_w=9, forced=2),
    14: dict(arm=(0.55, 0.50, 0.45), exec_w=12, forced=3),
}
POOL = dict(base=12, per=6, pscale=2.5, pexp=1.15)
ACTIVE = dict(base=7, per=2, cap=32, ceiling=40)
REFILL_INTERVAL, REFILL_MAX = 0.45, 2
SPECIAL = dict(first=4, base=0.35, growth=0.03, max=0.65, win=(0.3, 0.7), second_from=8, second=0.10)
XP = dict(kill=1, round_base=15, round_per=5, old_base=100, old_per=75)
GOLD_COLLECTION = 0.90   # WP-041 model assumption (orbs collected)
BRANCH_COST = {  # UpgradeBranch: BaseCost, MaxTier (PrototypeConfig.asset _economy.Branches)
    "Piercing": (30, 4), "Ricochet": (30, 4), "Explosion": (45, 3), "Chain": (40, 3), "FireRate": (18, 5),
    "ReloadSpeed": (15, 5), "Movement": (22, 4), "MaxHealth": (25, 5), "RegenDelay": (18, 4), "RegenRate": (18, 4),
    # new WP-069/077 branches have no price yet; assume the Piercing row
    "ArmorBreak": (30, 4), "Impact": (30, 4), "Cluster": (45, 3),
}


def pool(r):
    s = r - 1
    p = 0.0 if s <= 0 else s ** POOL["pexp"]
    return max(1, int(round(POOL["base"] + POOL["per"] * s + math.floor(POOL["pscale"] * p))))


def active(r):
    return max(1, min(int(ACTIVE["base"] + ACTIVE["per"] * r), ACTIVE["cap"], ACTIVE["ceiling"]))


def hp(tier, r):
    return BASE_HP * TIERS[tier]["hp"] * (1 + HP_PER_ROUND * max(0, r - 1))


def armour_chance(tier, slot_i, r):
    t = TIERS[tier]
    if r in OVERRIDES and tier in ("Standard", "Runner"):
        return OVERRIDES[r]["arm"][slot_i]
    h, b, l, g, mx = t["arm"]
    return min(mx, (h, b, l)[slot_i] + g * max(0, r - 1))


def tier_weights(r):
    w = {}
    for name, t in TIERS.items():
        if r < t["intro"]:
            continue
        w[name] = min(t["w"] + t["g"] * max(0, r - t["intro"]), t["wmax"])
    if r in OVERRIDES:
        w["Executioner"] = OVERRIDES[r]["exec_w"]
    return w


def tier_counts(r):
    """Expected enemies of each tier in round r (weighted draw + forced/debut minimums)."""
    n = pool(r)
    w = tier_weights(r)
    tot = sum(w.values())
    c = {k: n * v / tot for k, v in w.items()}
    forced = {"Executioner": OVERRIDES.get(r, {}).get("forced", 0)}
    for name, t in TIERS.items():  # REQ-071 debut guarantee
        if r == t["intro"] and name != "Standard":
            forced[name] = max(forced.get(name, 0), 1)
    for k, m in forced.items():
        if k in c and c[k] < m:
            c["Standard"] -= m - c[k]
            c[k] = m
    return c


def round_gold(r):
    g = 0.0
    for k, cnt in tier_counts(r).items():
        arm = sum(armour_chance(k, i, r) * v for i, v in enumerate(ARMOR_GOLD.values()))
        g += cnt * (TIERS[k]["gold"] + arm)
    return g * GOLD_COLLECTION


def round_xp(r, completed=True):
    x = 0.0
    for k, cnt in tier_counts(r).items():
        x += cnt * (XP["kill"] + TIERS[k]["xp"])
    return x + (XP["round_base"] + XP["round_per"] * r if completed else 0)


# ---------------------------------------------------------------- arsenal v3 (WP-069 §1 tables 3a/3b)
# cycle = seconds between shots (burst: mean), mag, reserve, tac/empty reload, headX, armorX,
# stagger, speed, fullR, minPct, maxR, pen, keep, move (speed while firing), docs (§3c claims)
W = {
    "ar_1": dict(dmg=12, pel=1, rpm=600, mag=30, res=210, rt=1.6, re=2.1, head=1.0, arm=1.0, stg=0.25, v=260, full=30, minp=0.70, maxr=60, pen=1, keep=0.60, move=0.92,
                 doc=dict(dps=120, s1=3, s10=6, g=13, gh=7, ex=30, ttk=1.20, magd=360)),
    "ar_2": dict(dmg=16, pel=1, rpm=500, burst=(3, 0.05, 0.26), mag=24, res=168, rt=1.8, re=2.3, head=1.15, arm=1.2, stg=0.30, v=320, full=40, minp=0.75, maxr=70, pen=1, keep=0.65, move=0.92,
                 doc=dict(dps=133, s1=3, s10=5, g=10, gh=5, ex=23, ttk=1.08, magd=384)),
    "smg_1": dict(dmg=8, pel=1, rpm=900, mag=42, res=294, rt=1.5, re=2.0, head=1.0, arm=0.7, stg=0.12, v=170, full=14, minp=0.55, maxr=40, pen=0, keep=1.0, move=1.00,
                  doc=dict(dps=120, s1=5, s10=9, g=20, gh=10, ex=45, ttk=1.27, magd=336)),
    "smg_2": dict(dmg=14, pel=1, rpm=540, mag=24, res=192, rt=1.7, re=2.2, head=1.0, arm=0.9, stg=0.45, v=200, full=18, minp=0.60, maxr=42, pen=1, keep=0.50, move=0.96,
                  doc=dict(dps=126, s1=3, s10=6, g=12, gh=6, ex=26, ttk=1.22, magd=336)),
    "sg_1": dict(dmg=9, pel=8, rpm=72, mag=7, res=42, rt=4.6, re=5.1, head=1.0, arm=0.8, stg=0.60, v=120, full=6, minp=0.35, maxr=24, pen=0, keep=1.0, move=0.90, cone=7.5, focus=5.5,
                 doc=dict(dps=86, s1=1, s10=1, g=3, gh=2, ex=5, ttk=1.67, magd=504)),
    "sg_2": dict(double=True, dmg=11, pel=10, rpm=200, mag=2, res=40, rt=1.7, re=1.7, head=1.0, arm=0.8, stg=0.70, v=130, full=8, minp=0.40, maxr=20, pen=0, keep=1.0, move=0.92, cone=5.0, focus=3.8,
                 doc=dict(dps=367, s1=1, s10=1, g=2, gh=1, ex=4, ttk=0.30, magd=220)),
    "lmg_1": dict(dmg=11, pel=1, rpm=720, mag=100, res=300, rt=4.4, re=5.2, head=1.0, arm=1.0, stg=0.20, v=250, full=28, minp=0.65, maxr=55, pen=1, keep=0.60, move=0.80,
                  doc=dict(dps=132, s1=4, s10=7, g=15, gh=8, ex=33, ttk=1.17, magd=1100)),
    "lmg_2": dict(dmg=24, pel=1, rpm=420, mag=60, res=180, rt=5.0, re=5.8, head=1.0, arm=1.3, stg=0.55, v=300, full=32, minp=0.70, maxr=60, pen=2, keep=0.70, move=0.70,
                  doc=dict(dps=168, s1=2, s10=3, g=7, gh=4, ex=15, ttk=0.86, magd=1440)),
    "sr_1": dict(dmg=55, pel=1, rpm=48, mag=5, res=40, rt=2.6, re=3.2, head=1.25, arm=1.6, stg=0.80, v=420, full=60, minp=0.85, maxr=90, pen=4, keep=0.80, move=0.85,
                 doc=dict(dps=44, s1=1, s10=2, g=3, gh=2, ex=7, ttk=2.50, magd=275)),
}
ROCKET = dict(direct=110, blast=160, inner=1.8, outer=5.0, cycle=0.2, reload=2.8, self_share=0.45, self_cap=72, doc_dps=96)
TARGETS = [("Std R1", "Standard", 1), ("Std R10", "Standard", 10), ("Guard R10", "Guard", 10), ("Exec R10", "Executioner", 10)]


def shot_interval(w):
    if "burst" in w:
        n, inner, gap = w["burst"]
        return ((n - 1) * inner + gap) / n
    return 60.0 / w["rpm"]


def shot_times(w, n):
    """Time of shot k (0-based) for the first n shots from a trigger pull at t = 0."""
    if "burst" in w:
        b, inner, gap = w["burst"]
        per = (b - 1) * inner + gap
        return [(k // b) * per + (k % b) * inner for k in range(n)]
    return [k * 60.0 / w["rpm"] for k in range(n)]


def dps(w):
    return w["dmg"] * w["pel"] / shot_interval(w)


def sustained(w):
    if w.get("double"):
        return w["dmg"] * w["pel"] * 2 / (0.3 + w["re"])  # two barrels 0.3 s apart, break-open 1.7 s
    t_mag = w["mag"] * shot_interval(w)
    return w["dmg"] * w["pel"] * w["mag"] / (t_mag + w["re"])


def falloff(w, d):
    if d <= w["full"]:
        return 1.0
    t = min(1.0, (d - w["full"]) / (w["maxr"] - w["full"]))
    return 1.0 + (w["minp"] - 1.0) * t


def shots_to_kill(w, target_hp, region="upper", plate=0.0, use_armor_mult=True, pellets_on=None, dist=0.0):
    """Sequential per-pellet simulation of RegionalDamageBody.ApplyDirectHit (surplus discarded)."""
    per = w["dmg"] * falloff(w, dist)
    head = w["head"] if region == "head" else 1.0
    pellets = w["pel"] if pellets_on is None else pellets_on
    amult = w["arm"] if use_armor_mult else 1.0
    hpv, pl, shots = target_hp, plate, 0
    while hpv > 1e-6 and shots < 999:
        shots += 1
        for _ in range(pellets):
            if pl > 0:
                pl -= per * amult  # absorbed; any surplus is thrown away
            else:
                hpv -= per * EXPOSED[region] * head
            if hpv <= 1e-6:
                break
    return shots


def ttk(w, n):
    if n <= 1:
        return 0.0
    if w.get("double"):
        pairs, rem = divmod(n - 1, 2)
        return pairs * (0.3 + w["re"]) + rem * 0.3  # the 3rd barrel needs a reload
    if n > w["mag"]:
        return shot_times(w, w["mag"])[-1] + w["re"] + shot_times(w, n - w["mag"])[-1] + shot_interval(w)
    return shot_times(w, n)[-1]


def pellets_on_body(cone_deg, dist, body_r=0.45):
    """sg_1 centre+3@40%+4@100% ring: pellets whose ring radius at dist falls inside the body radius."""
    r = dist * math.tan(math.radians(cone_deg))
    return 1 + 3 * (0.4 * r <= body_r) + 4 * (r <= body_r)


# ---------------------------------------------------------------- live WD assets (B1 migration state)
def parse_wd(path):
    txt = open(path, encoding="utf-8").read()
    out = {}
    for key in ("weaponId", "rpm", "damage", "pelletCount", "magazine", "reloadTacSeconds", "reloadEmptySeconds",
                "speed", "maxRange", "falloffStart", "falloffEnd", "falloffMinShare", "penetration", "stagger",
                "headMultiplier", "armorDamageMultiplier", "bloomPerShot", "bloomDecayPerSecond", "profileLevel"):
        m = re.search(r"^\s+%s: (.+)$" % key, txt, re.M)
        if m:
            v = m.group(1).strip()
            try:
                v = float(v)
            except ValueError:
                pass
            out[key] = v
    return out


def live_rows():
    rows = []
    if not os.path.isdir(ARSENAL_DIR):
        return rows
    doc_keys = [("damage", "dmg"), ("rpm", "rpm"), ("pelletCount", "pel"), ("magazine", "mag"), ("reloadTacSeconds", "rt"),
                ("reloadEmptySeconds", "re"), ("speed", "v"), ("maxRange", "maxr"), ("falloffStart", "full"),
                ("falloffMinShare", "minp"), ("penetration", "pen"), ("headMultiplier", "head"), ("armorDamageMultiplier", "arm"),
                ("stagger", "stg")]
    for f in sorted(os.listdir(ARSENAL_DIR)):
        if not (f.startswith("WD_") and f.endswith(".asset")):
            continue
        live = parse_wd(os.path.join(ARSENAL_DIR, f))
        wid = live.get("weaponId")
        if wid not in W:
            continue
        design = W[wid]
        diffs = []
        for lk, dk in doc_keys:
            lv, dv = live.get(lk), design.get(dk)
            if lv is None or dv is None:
                continue
            if abs(float(lv) - float(dv)) > 1e-3:
                diffs.append("%s %g→%g" % (lk, lv, dv))
        live_dps = live["damage"] * live["pelletCount"] * live["rpm"] / 60.0
        rows.append((wid, live_dps, dps(design), diffs))
    return rows


# ---------------------------------------------------------------- output helpers
def table(headers, rows):
    out = ["| " + " | ".join(headers) + " |", "|" + "---|" * len(headers)]
    for r in rows:
        out.append("| " + " | ".join(str(c) for c in r) + " |")
    return "\n".join(out)


def f1(x):
    return "%.1f" % x


def f2(x):
    return "%.2f" % x


def mark(ok):
    return "" if ok else " ⚠"


def section_arsenal():
    print("## A0 · Live WeaponDefinition assets vs design (WD_*.asset)\n")
    rows = [(wid, f1(ld), f1(dd), "%+.0f%%" % ((dd / ld - 1) * 100), "; ".join(d) or "—") for wid, ld, dd, d in live_rows()]
    print(table(["weapon", "live DPS", "design DPS", "Δ", "field differences (live→design)"], rows))

    print("\n## A1 · §3c recomputation (upper body, unarmoured, point blank)\n")
    rows, mism = [], []
    for wid, w in W.items():
        d = w["doc"]
        s = [shots_to_kill(w, hp(t, r)) for _, t, r in TARGETS]
        gh = shots_to_kill(w, hp("Guard", 10), region="head")
        g_ttk = ttk(w, s[2])
        mag_d = w["dmg"] * w["pel"] * w["mag"]
        checks = [("DPS", dps(w), d["dps"], 1.5), ("StdR1", s[0], d["s1"], 0), ("StdR10", s[1], d["s10"], 0), ("Guard", s[2], d["g"], 0),
                  ("GuardHead", gh, d["gh"], 0), ("Exec", s[3], d["ex"], 0), ("GuardTTK", g_ttk, d["ttk"], 0.02), ("MagDmg", mag_d, d["magd"], 0.5)]
        cells = []
        for name, got, want, tol in checks:
            ok = abs(got - want) <= tol
            if not ok:
                mism.append((wid, name, got, want))
            cells.append(("%g" % round(got, 2)) + mark(ok))
        rows.append([wid] + cells + [f1(sustained(w))])
    print(table(["weapon", "DPS", "Std R1", "Std R10", "Guard R10", "Guard head", "Exec R10", "Guard TTK s", "mag dmg", "sustained DPS (incl. empty reload)"], rows))
    print("\nMismatches vs doc §3c:", mism or "none")

    rl_cycle = ROCKET["cycle"] + ROCKET["reload"]
    rl_dps = (ROCKET["direct"] + ROCKET["blast"]) / rl_cycle
    print("\nrl_1: direct+blast %d per %.1f s cycle → %.1f DPS (doc §3c says %d)" % (ROCKET["direct"] + ROCKET["blast"], rl_cycle, rl_dps, ROCKET["doc_dps"]))
    lmg1 = W["lmg_1"]
    print("lmg_1 Guards per magazine at R10 (no overkill carry): %d (doc: 7)" % (lmg1["mag"] // shots_to_kill(lmg1, hp("Guard", 10))))
    sr = W["sr_1"]
    line = [sr["dmg"] * sr["keep"] ** i for i in range(4)]
    print("sr_1 4-body line damage %s = %.1f per shot → %.0f DPS (doc: 176)" % ([round(x, 1) for x in line], sum(line), sum(line) / shot_interval(sr)))
    print("sr_1 bodies killed per shot at R10 Standard (70.7 HP): %d; at R5 (50.3 HP): %d" % (sum(x >= hp("Standard", 10) for x in line), sum(x >= hp("Standard", 5) for x in line)))

    print("\n## A2 · Armour (live rule: plate absorbs the whole hit, surplus discarded)\n")
    rows = []
    for wid, w in W.items():
        g = hp("Guard", 10)
        bare = shots_to_kill(w, g)
        with_m = shots_to_kill(w, g, plate=PLATE["upper"])
        no_m = shots_to_kill(w, g, plate=PLATE["upper"], use_armor_mult=False)
        per = w["dmg"] * w["arm"]
        dead = all(math.ceil(p / (w["dmg"] * w["arm"])) == math.ceil(p / w["dmg"]) for p in PLATE.values())
        rows.append([wid, "×%g" % w["arm"], bare, with_m, no_m, f2(ttk(w, with_m)), f2(ttk(w, with_m) - ttk(w, bare)),
                     "no effect on any plate" if dead and w["arm"] != 1.0 else ""])
    print(table(["weapon", "armorMult", "Guard R10 bare", "body plate (design mult)", "body plate (live: mult not read)", "TTK plated s", "Δ TTK s", "note"], rows))

    print("\nArmour chance by round (Standard body / Guard body):",
          ", ".join("R%d %.2f/%.2f" % (r, armour_chance("Standard", 1, r), armour_chance("Guard", 1, r)) for r in (1, 5, 7, 8, 9, 10, 11, 12, 14, 15)))

    print("\n## A3 · rl_1 blast vs regional armour (REQ-090)\n")
    rows = []
    for tier, r in (("Standard", 10), ("Guard", 10), ("Guard", 15)):
        for dist in (1.8, 3.5):
            blast = ROCKET["blast"] * max(0.0, min(1.0, (ROCKET["outer"] - dist) / (ROCKET["outer"] - ROCKET["inner"])))
            pk = 0.0
            pk_direct = 0.0
            ch = [armour_chance(tier, i, r) for i in range(3)]
            for mask in range(8):
                p = 1.0
                armoured = []
                for i, reg in enumerate(("head", "upper", "lower")):
                    on = bool(mask >> i & 1)
                    p *= ch[i] if on else 1 - ch[i]
                    armoured.append(on)
                dmg_hp = sum(blast * BLAST_SHARE[reg] for i, reg in enumerate(("head", "upper", "lower")) if not armoured[i])
                if dmg_hp >= hp(tier, r):
                    pk += p
                direct = 0.0 if armoured[1] else ROCKET["direct"]
                if dist == 1.8 and dmg_hp + direct >= hp(tier, r):
                    pk_direct += p
            rows.append(["%s R%d" % (tier, r), "%.1f m" % dist, f1(blast), "%.0f%%" % (pk * 100),
                         ("%.0f%%" % (pk_direct * 100)) if dist == 1.8 else "—"])
    print(table(["target", "distance", "blast", "P(kill) blast only", "P(kill) direct hit + blast"], rows))
    print("Fully armoured Guard: blast 160 → health 0 (plates break, surplus discarded); doc §5 line 175 says 72 remains.")

    print("\n## A4 · Range falloff and shotgun geometry\n")
    rows = []
    for wid, w in W.items():
        rows.append([wid] + ["%.1f" % (w["dmg"] * w["pel"] * falloff(w, d)) for d in (5, 10, 15, 20, 30, 45)] +
                    [shots_to_kill(w, hp("Standard", 10), dist=20)])
    print(table(["weapon", "5 m", "10 m", "15 m", "20 m", "30 m", "45 m", "Std R10 STK @20 m"], rows))
    for wid in ("sg_1",):
        w = W[wid]
        print("\n%s pellets on a 0.45 m body (hip %.1f° / focus %.1f°): " % (wid, w["cone"], w["focus"]) +
              ", ".join("%g m %d/%d" % (d, pellets_on_body(w["cone"], d), pellets_on_body(w["focus"], d)) for d in (2, 3, 4, 5, 6, 8)))
        print("R10 Standard one-shot needs 8 pellets (72 ≥ 70.7): hip ≤ %.2f m, focus ≤ %.2f m (doc: ≤ 6 m)" %
              (0.45 / math.tan(math.radians(w["cone"])), 0.45 / math.tan(math.radians(w["focus"]))))
    for wid in ("smg_1", "smg_2", "ar_1"):
        w = W[wid]
        t = w["maxr"] / w["v"]
        print("%s gravity drop at max range %d m: %.2f m (doc §1.6: ≤ 0.2 m at 45 m)" % (wid, w["maxr"], 0.5 * 9.81 * t * t))

    print("\n## A5 · Roles: overlap and dominance\n")
    prof = {wid: [shots_to_kill(w, hp(t, r)) for _, t, r in TARGETS] for wid, w in W.items()}
    pairs = []
    keys = list(W)
    for i, a in enumerate(keys):
        for b in keys[i + 1:]:
            if all(abs(x - y) <= 1 for x, y in zip(prof[a][:3], prof[b][:3])):
                ta, tb = ttk(W[a], prof[a][2]), ttk(W[b], prof[b][2])
                pairs.append([a, b, prof[a], prof[b], f2(ta), f2(tb), f1(dps(W[a])), f1(dps(W[b])), f1(sustained(W[a])), f1(sustained(W[b]))])
    print(table(["A", "B", "STK A", "STK B", "Guard TTK A", "Guard TTK B", "DPS A", "DPS B", "sust A", "sust B"], pairs))
    metrics = {wid: dict(dps=dps(w), sus=sustained(w), gttk=-ttk(w, shots_to_kill(w, hp("Guard", 10), plate=PLATE["upper"])),
                         total=w["dmg"] * w["pel"] * (w["mag"] + w["res"]), rng=w["full"], pen=w["pen"], move=w["move"], stg=w["stg"])
               for wid, w in W.items()}
    dom = []
    for a in keys:
        for b in keys:
            if a != b and all(metrics[a][k] >= metrics[b][k] for k in metrics[a]) and any(metrics[a][k] > metrics[b][k] for k in metrics[a]):
                dom.append("%s dominates %s" % (a, b))
    near = []
    for a in keys:
        for b in keys:
            if a == b:
                continue
            worse = [k for k in metrics[a] if metrics[a][k] < metrics[b][k]]
            if len(worse) == 1:
                near.append("%s beats %s on everything except %s (%.2f vs %.2f)" % (a, b, worse[0], metrics[a][worse[0]], metrics[b][worse[0]]))
    print("\nStrict Pareto dominance (DPS, sustained, plated-Guard TTK, total ammo dmg, full-dmg range, penetration, move-while-firing, stagger):", dom or "none")
    print("Near-dominance (loses on exactly one axis):")
    for n in near:
        print("-", n)
    print("\nTotal carried damage (mag+reserve):", ", ".join("%s %d" % (k, v["total"]) for k, v in metrics.items()))

    print("\n## A6 · Progression-curve claims (WP-069 §1.3)\n")
    rows = []
    for r in range(1, 16):
        row = [r, f1(hp("Standard", r))]
        for wid, w in W.items():
            row.append(shots_to_kill(w, hp("Standard", r)))
        rows.append(row)
    print(table(["R", "Std HP"] + list(W), rows))
    print("\nActive enemy target (live cap %d, ceiling %d):" % (ACTIVE["cap"], ACTIVE["ceiling"]), ", ".join("R%d %d" % (r, active(r)) for r in range(1, 16)))


def section_specials():
    print("\n## B · Specials: kill potential per activation\n")
    raiju_ammo = 12
    raiju_time = raiju_ammo * 1.15 + 3 * 3.6
    gel_time = 24 * 0.45 + 2 * 2.8
    print("Raiju: 12 shots × ≤8 targets = 96 max; all ammo spent in %.1f s (cap 40 s). Namekuji: 24 shots spent in %.1f s (cap 45 s); puddles live 25 s after the last shot." % (raiju_time, gel_time))
    rows = []
    for r in range(4, 16):
        p = pool(r)
        rem_lo, rem_hi = p * (1 - SPECIAL["win"][1]), p * (1 - SPECIAL["win"][0])
        a = active(r)
        refill = min(REFILL_MAX / REFILL_INTERVAL, 99) * raiju_time
        raiju = min(96, rem_hi + refill)
        mean_d = 26.5 / TIERS["Standard"]["speed"]  # preferred spawn band 18-35 m, walking at 2.7 m/s
        arrive = min(a / mean_d, REFILL_MAX / REFILL_INTERVAL)
        window = min(45.0, gel_time + 25.0)
        gel = arrive * window
        chance = min(SPECIAL["base"] + SPECIAL["growth"] * (r - SPECIAL["first"]), SPECIAL["max"])
        rows.append([r, p, a, "%.0f–%.0f" % (rem_lo, rem_hi), "%.0f" % raiju, "%.0f%%" % (100 * min(1, 96 / p)),
                     "%.1f/s" % arrive, "%.0f" % gel, "%.0f%%" % (100 * min(1.0, gel / p)), "%.2f" % chance])
    print(table(["R", "pool", "active target", "left when it drops (30–70 %)", "Raiju kills ≤", "Raiju ≤ % of pool",
                 "arrival rate", "Namekuji kills (arrival × window)", "Namekuji % of pool", "P(drop)"], rows))
    print("\nNote: an active Special survives RoundEnded (SpecialWeaponDirector.OnRoundEnded cancels only a *scheduled* drop), so ammo carries into the next round.")
    ex = sum(min(SPECIAL["base"] + SPECIAL["growth"] * (r - 4), SPECIAL["max"]) + (SPECIAL["second"] if r >= 8 else 0) for r in range(4, 16))
    print("Expected Special drops R4–R15: %.1f" % ex)


# ---------------------------------------------------------------- tree v2 (WP-077 §2)
def build_tree(weapon_core=("Piercing", "FireRate", "ReloadSpeed")):
    nodes = {}

    def add(nid, kind, lane, cost, **kw):
        nodes[nid] = dict(id=nid, kind=kind, lane=lane, cost=cost, owned=0, **kw)

    for b in weapon_core:
        base, mx = BRANCH_COST[b]
        add("W:" + b, "tier", "W", base, max=mx, branch=b)
        add("W:K:" + b, "key", "W", 110, need=("W:" + b, 2))
    add("W:ForkX", "fork", "W", 90, excl="W:ForkY")
    add("W:ForkY", "fork", "W", 90, excl="W:ForkX")
    add("W:Seal", "seal", "W", 150)
    add("W:Cap", "cap", "W", 380)
    add("W:Link1", "link", "W", 80)
    add("W:Link2", "link2", "W", 160)
    for b in ("Movement", "MaxHealth", "RegenDelay", "RegenRate"):
        base, mx = BRANCH_COST[b]
        add("B:" + b, "tier", "B", base, max=mx, branch=b)
        add("B:K:" + b, "key", "B", 140, need=("B:" + b, 2))
    add("B:ForkX", "fork", "B", 100, excl="B:ForkY")
    add("B:ForkY", "fork", "B", 100, excl="B:ForkX")
    add("B:Cap", "bcap", "B", 320)
    add("B:Purse", "purse", "B", 60)
    return nodes


def price(n):
    return n["cost"] * (2 ** n["owned"]) if n["kind"] == "tier" else n["cost"]


def keys_in(nodes, lane):
    return sum(1 for n in nodes.values() if n["lane"] == lane and n["kind"] == "key" and n["owned"])


def available(nodes, n, special_seen, purse_needs_key):
    if n["kind"] == "tier":
        return n["owned"] < n["max"]
    if n["owned"]:
        return False
    k = n["kind"]
    if k == "key":
        src, lvl = n["need"]
        return nodes[src]["owned"] >= lvl
    if k == "fork":
        return keys_in(nodes, n["lane"]) >= 1 and not nodes[n["excl"]]["owned"]
    if k == "seal":
        return keys_in(nodes, "W") >= 1
    if k == "cap":
        return nodes["W:Seal"]["owned"] and keys_in(nodes, "W") >= 2 and (nodes["W:ForkX"]["owned"] or nodes["W:ForkY"]["owned"])
    if k == "bcap":
        return keys_in(nodes, "B") >= 2 and (nodes["B:ForkX"]["owned"] or nodes["B:ForkY"]["owned"])
    if k == "link":
        return special_seen
    if k == "link2":
        return special_seen and nodes["W:Link1"]["owned"]
    if k == "purse":
        return (not purse_needs_key) or keys_in(nodes, "B") >= 1
    return False


OFFER_W = {"tier": 1.0, "key": 1.4, "fork": 1.2, "seal": 1.4, "cap": 2.0, "bcap": 2.0, "link": 1.2, "link2": 1.2, "purse": 0.8}
# cheapest capstone route for ar_1: (node, owned count required after this step)
CAP_PATH = [("W:ReloadSpeed", 1), ("W:FireRate", 1), ("W:ReloadSpeed", 2), ("W:FireRate", 2), ("W:K:ReloadSpeed", 1),
            ("W:K:FireRate", 1), ("W:ForkX", 1), ("W:Seal", 1), ("W:Cap", 1)]


def next_path_node(nodes):
    for nid, need in CAP_PATH:
        if nodes[nid]["owned"] < need:
            return nodes[nid]
    return None


def run_tree(policy, rounds=15, seed=1, pin=True, gold_scale=1.0, cushion=0.30, hearth_cap=3, hearth_floor=150.0):
    """policy: bot_spend (greedy, most expensive affordable card ×2 + Ocak ×3, perfect offers),
    bot_rush (perfect offers, cheapest capstone route first, no Ocak until the capstone is owned),
    median (3 weighted frontier offers, optional pin on the capstone route, 30 % cushion, Ocak while wallet > 150)."""
    rng = random.Random(seed)
    nodes = build_tree()
    wallet = earned = spent = 0.0
    hearth = 0
    first_key = first_cap = None
    special_seen = False
    purse = 1.0
    spent_10_15 = earned_10_15 = 0.0

    def buy(n, cost):
        nonlocal wallet, spent, spent_10_15, purse
        wallet -= cost
        spent += cost
        if r >= 10:
            spent_10_15 += cost
        if n is not None:
            n["owned"] += 1
            if n["kind"] == "purse":
                purse = 1.1

    for r in range(1, rounds + 1):
        inc = round_gold(r) * purse * gold_scale
        if policy == "median":
            inc *= rng.uniform(0.85, 1.05)  # misses, orbs that expire
        wallet += inc
        earned += inc
        if r >= 10:
            earned_10_15 += inc
        if r >= 4 and rng.random() < min(SPECIAL["base"] + SPECIAL["growth"] * (r - 4), SPECIAL["max"]):
            special_seen = True
        front = lambda: [n for n in nodes.values() if available(nodes, n, special_seen, False)]
        reserve, h_max = 0.0, 3
        if policy in ("bot_spend", "bot_rush"):
            for _ in range(2):
                f = front()
                want = None
                if policy == "bot_rush" and not nodes["W:Cap"]["owned"]:
                    nxt = next_path_node(nodes)
                    if nxt is not None and nxt in f and price(nxt) <= wallet:
                        want = nxt
                    elif nodes["B:Purse"] in f and price(nodes["B:Purse"]) <= wallet and r <= 3:
                        want = nodes["B:Purse"]
                else:
                    aff = [n for n in f if price(n) <= wallet]
                    want = max(aff, key=price) if aff else None
                if want is None:
                    break
                buy(want, price(want))
            if policy == "bot_rush" and not nodes["W:Cap"]["owned"]:
                h_max = 0
        else:
            f = front()
            offer = []
            nxt = next_path_node(nodes) if pin else None
            if nxt is not None and nxt in f:
                offer.append(nxt)
            while len(offer) < 3:
                cand = [n for n in f if n not in offer and (n["kind"] != "cap" or pin)]
                if not cand:
                    break
                offer.append(rng.choices(cand, [OFFER_W[n["kind"]] for n in cand])[0])
            reserve = cushion * wallet
            picks = 0
            for n in sorted(offer, key=lambda n: (n is not nxt, price(n))):
                if picks < 2 and available(nodes, n, special_seen, False) and price(n) <= wallet - reserve:
                    buy(n, price(n))
                    picks += 1
            reserve, h_max = max(hearth_floor, cushion * wallet), hearth_cap
        if keys_in(nodes, "W") + keys_in(nodes, "B") >= 1:
            done = 0
            while done < h_max and hearth < 20 and 50 + 30 * hearth <= wallet - reserve:
                buy(None, 50 + 30 * hearth)
                hearth += 1
                done += 1
        if first_key is None and keys_in(nodes, "W") + keys_in(nodes, "B") >= 1:
            first_key = r
        if first_cap is None and (nodes["W:Cap"]["owned"] or nodes["B:Cap"]["owned"]):
            first_cap = r
    return dict(earned=earned, spent=spent, share=spent / earned, share_10_15=spent_10_15 / earned_10_15 if earned_10_15 else 0,
                wallet=wallet, hearth=hearth, first_key=first_key, first_cap=first_cap)


def median(vals):
    v = sorted(x for x in vals if x is not None)
    return v[len(v) // 2] if v else None


def pct(vals, q):
    v = sorted(99 if x is None else x for x in vals)
    return v[min(len(v) - 1, int(q * len(v)))]


def section_economy(seeds):
    print("\n## C1 · Round income (expected, from live config; 90 % orb collection)\n")
    rows, cum_g, cum_x = [], 0.0, 0.0
    for r in range(1, 16):
        g = round_gold(r)
        x = round_xp(r)
        cum_g += g
        cum_x += x
        c = tier_counts(r)
        rows.append([r, pool(r), "/".join("%.1f" % c.get(k, 0) for k in TIERS), f1(g), "%.0f" % cum_g, "%.0f" % x, "%.0f" % cum_x])
    print(table(["R", "pool", "Std/Run/Grd/Exe", "gold", "cum gold", "XP", "cum XP"], rows))
    print("\nDoc WP-077 §4.2: R1–R15 income 6 573; §3.1: R8 run 790 XP, R15 run 2 900 XP.")
    x_r8_death = sum(round_xp(r) for r in range(1, 8)) + 0.5 * round_xp(8, completed=False)
    x_r8_done = sum(round_xp(r) for r in range(1, 9))
    print("Computed: R8 run = %.0f XP if R8 is completed, %.0f XP if the player dies halfway through R8; R15 run = %.0f XP." % (x_r8_done, x_r8_death, sum(round_xp(r) for r in range(1, 16))))

    print("\n## C2 · Tree v2 simulation (bot upper bound vs median player)\n")
    brow = lambda label, x: [label, "%.0f" % x["earned"], "%.0f" % x["spent"], "%.0f %%" % (100 * x["share"]), "%.0f %%" % (100 * x["share_10_15"]),
                             "%.0f" % x["wallet"], x["hearth"], "R%s" % x["first_key"], "R%s" % x["first_cap"] if x["first_cap"] else "none"]
    print(table(["policy", "income R1–15", "spent", "share", "share R10–15", "wallet R15", "Ocak", "1st keystone", "1st capstone"], [
        ["doc §4.2 bot", "6 573", "6 536", "99 %", "—", "37", "15", "R4", "R8"],
        brow("bot_spend (greedy, 90 % orbs)", run_tree("bot_spend")),
        brow("bot_rush (capstone first, 90 % orbs)", run_tree("bot_rush")),
        brow("bot_spend, 100 % orbs", run_tree("bot_spend", gold_scale=1 / GOLD_COLLECTION)),
        brow("bot_rush, 100 % orbs", run_tree("bot_rush", gold_scale=1 / GOLD_COLLECTION)),
    ]))
    rows = []
    for label, kw in (("median, pins capstone route", dict(pin=True)), ("median, no pin (capstone not offered before M3)", dict(pin=False)),
                      ("hoarder: pins, keeps 50 %, ≤ 1 Ocak/round", dict(pin=True, cushion=0.5, hearth_cap=1))):
        res = [run_tree("median", seed=s, **kw) for s in range(1, seeds + 1)]
        rows.append([label, "%.0f %%" % (100 * median([x["share"] for x in res])), "%.0f %%" % (100 * median([x["share_10_15"] for x in res])),
                     "%.0f" % median([x["wallet"] for x in res]), median([x["hearth"] for x in res]),
                     "R%s" % median([x["first_key"] for x in res]), "R%s / p90 %s" % (median([x["first_cap"] for x in res]), (lambda q: "none" if q == 99 else "R%d" % q)(pct([x["first_cap"] for x in res], 0.9))),
                     "%.0f %%" % (100 * sum(x["first_cap"] is None for x in res) / len(res))])
    print()
    print(table(["median player (%d seeds)" % seeds, "share R1–15", "share R10–15", "wallet R15", "Ocak", "1st keystone", "1st capstone", "no capstone by R15"], rows))
    print("Targets §4.3: share 65–80 %, R10–15 ≥ 60 %, wallet ≤ 900, keystone R4–6, capstone R9–11 (p90 ≤ R13).")

    print("\n## C3 · XP curve and hours\n")
    req = lambda l: 100 * l + 10 * l * l
    cum = lambda L: sum(req(l) for l in range(1, L))
    old_cum = lambda L: sum(XP["old_base"] + XP["old_per"] * (l - 1) for l in range(1, L))
    # round time model: WP-041 clear time 8 s (R1) → 83 s (R15) scaled on pool, + 3 s countdown + 20 s shop
    clear = lambda r: 8 + 75 * (pool(r) - pool(1)) / (pool(15) - pool(1))
    rt = lambda r: clear(r) + 3 + 20
    raw15 = sum(rt(r) for r in range(1, 16))
    scale = 28 * 60 / raw15
    t_run = lambda last: scale * sum(rt(r) for r in range(1, last + 1))
    xp_run = lambda last: sum(round_xp(r) for r in range(1, last + 1))
    profiles = [("doc (4 500 XP/h flat)", 4500.0)]
    for label, death in (("reaches R15, dies mid-R15", 15), ("strong: dies in R12", 12), ("median: dies in R9", 9), ("new player: dies in R6", 6)):
        t = t_run(death - 1) + 0.5 * scale * rt(death)
        x = xp_run(death - 1) + 0.5 * round_xp(death, completed=False)
        profiles.append(("%s (%.0f XP / %.1f min)" % (label, x, t / 60), x / (t / 3600)))
    rows = []
    for label, rate in profiles:
        rows.append([label, "%.0f" % rate] + ["%.1f" % (cum(L) / rate) for L in (10, 20, 30, 50)])
    print(table(["profile", "XP/h", "h→L10", "h→L20", "h→L30", "h→L50"], rows))
    print("\nCumulative XP check: " + ", ".join("L%d %d" % (L, cum(L)) for L in (2, 5, 10, 15, 20, 30, 40, 50)) + " (doc row L2 shows 0)")
    print("Old curve for comparison: L9 = %d XP, L20 = %d XP" % (old_cum(9), old_cum(20)))
    r15 = xp_run(15)
    lv = max(L for L in range(1, 51) if cum(L) <= r15)
    print("First full R15 run (%.0f XP) → new curve L%d, old curve L%d" % (r15, lv, max(L for L in range(1, 60) if old_cum(L) <= r15)))
    kul_weapons = 1980
    for label, rate in profiles[1:]:
        kul_h = rate / 40
        print("  %s: Kül %.0f/h from XP → all 10 weapons (1 980 Kül) after %.1f h; L30 gate reached after %.1f h" % (label.split(" (")[0], kul_h, kul_weapons / kul_h, cum(30) / rate))


def section_kul(seeds):
    print("\n## C4 · Kül pacing vs WP-077 §3.4 ring budgets\n")
    req = lambda l: 100 * l + 10 * l * l
    cum = lambda L: sum(req(l) for l in range(1, L))
    rings = [("Ring 1 (≤ 1 h)", 330, 1.0), ("Ring 2 (≤ 10 h)", 330 + 2000, 10.0), ("Ring 3 (30+ h)", 7300, 30.0)]
    one_off = 300 + 400 + 25 + 15  # first R10 per weapon, achievement rarity awards, first capstone, first link II (§3.1)
    rows = []
    for label, rate in (("R15 runner", 6060.0), ("median R9", 4450.0), ("new player R6", 3446.0)):
        k = rate / 40.0
        cells = [label, "%.0f" % k]
        for name, need, target in rings:
            # ring 1: only early common achievements (~50 Kül) are realistic; rings 2-3: every one-off bonus counted
            t = max(0.0, need - (50 if target <= 1 else one_off)) / k
            cells.append("%.1f h (target %s)" % (t, "≤ %g h" % target if target < 30 else "30+ h"))
        rows.append(cells)
    print(table(["profile", "Kül/h (XP/40)", "Ring 1", "Ring 2", "Ring 3 (whole meta)"], rows))
    print("One-off Kül: 50 counted for ring 1, all %d counted for rings 2–3 (upper bound on speed)." % one_off)
    print("\nWeapon unlocks, Kül spent on weapons only (no one-off bonus): which gate binds?\n")
    order = [("smg_1", 3, 40), ("sg_1", 5, 60), ("ar_2", 8, 120), ("lmg_1", 11, 160), ("sr_1", 14, 200),
             ("smg_2", 17, 240), ("sg_2", 21, 300), ("lmg_2", 26, 380), ("rl_1", 30, 480)]
    rows, kul = [], 0
    for wid, gate, cost in order:
        kul += cost
        xg, xk = cum(gate), kul * 40
        rows.append([wid, "L%d" % gate, xg, kul, xk, "Kül" if xk > xg else "level", "%.1f h" % (max(xg, xk) / 4450.0)])
    print(table(["weapon", "level gate", "XP for gate", "cum Kül", "XP for Kül", "binding", "median hours"], rows))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", type=int, default=200)
    a = ap.parse_args()
    print("# r1_audit.py output\n")
    section_arsenal()
    section_specials()
    section_economy(a.seeds)
    section_kul(a.seeds)


if __name__ == "__main__":
    main()
