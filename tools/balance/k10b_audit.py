#!/usr/bin/env python3
"""WP-077 K10b balance audit (S-04): tree paths x weapon x round with the live tree effects.

Stdlib only, deterministic, no Unity. Reads the LIVE data — WD_*.asset (arsenal, DEC-099), PrototypeConfig.asset
(enemies, rounds, economy branches) and run-tree.json (node args / prices) — and reuses the world model of
r1_audit_run2.py (pool, tier mix, armour chances, gold, XP). WeaponUpgradeTuning has no asset, so its code
defaults are restated below (WeaponUpgradeTuning.cs).

    python3 tools/balance/k10b_audit.py                 # markdown report on stdout
    python3 tools/balance/k10b_audit.py --json out.json

Model (a first-order analytic model, NOT a combat simulation — every assumption is a constant below):
  * single-target DPS = sustained magazine cycle (reload from empty) x hit fraction, vs one target of the tier
    with the round's EXPECTED armour (plates = durability / armour multiplier, all plates hit);
  * crowd DPS = damage per shot summed over the bodies it reaches (pierce line, ricochet, chain, blasts) at the
    crowd density below, i.e. what the round's swarm can absorb;
  * a round's build = greedy purchase on the round's gold (90 % pickup) along the weapon lane: core tiers,
    keystones, seal, fork, capstone, Hearth (<= 3 per round) — the "Human" path of TreeModel, without the
    offer draw (the C# TreeModel owns the offer/luck timing);
  * node power = the node's marginal change of single/crowd DPS on the R10 build (add it, or remove it if owned).
"""
import argparse
import copy
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import r1_audit_run2 as base  # noqa: E402

TREE_JSON = os.path.join(base.PROJ, "Rounds", "RunTree", "Resources", "ProgressionTree", "run-tree.json")

# WeaponUpgradeTuning.cs code defaults (no asset in Resources)
T = dict(pierce_per_tier=1, pierce_falloff=0.82, ric_base=0.35, ric_per_tier=0.18, ric_radius=6.5, ric_scale=0.6,
         exp_base=0.14, exp_per_tier=0.10, exp_radius=2.2, exp_radius_per_tier=0.5, exp_scale=1.1, exp_scale_per_tier=0.45,
         chain_per_tier=1, chain_scale=0.5, chain_falloff=0.75, fr_per_tier=0.08, fr_max=2.2,
         rs_per_tier=0.10, rs_min=0.45, ab_per_tier=0.25, impact_per_tier=0.15)
# RocketImpacts.cs §6: rl_1 Blast tier = inner +0.3 m, outer +0.5 m, damage +12 %; Cluster bomblet 40 dmg r 1.5 m
RL_BLAST_PER_TIER = 0.12
RL_BLAST_INNER_PER_TIER, RL_BLAST_OUTER_PER_TIER = 0.3, 0.5
RL_BOMBLET = 40.0
# RunTreeHooks.HitEffects: cap.rl_1 Ash Rain = count blasts at radius x 0.6 and damage x RocketSplitDamageScale
# (DEC-106, user 30.09: 1.0 — was 0.5); the impact point takes ~0.6x of the unsplit blast at 0.5, ~1.2x at 1.0 (two overlap)
RL_SPLIT_RADIUS, RL_SPLIT_DAMAGE, RL_SPLIT_CENTRE = 0.6, 1.0, 1.2
HP_REF = [71.0]              # Standard HP of the evaluated round (overkill for burning ground); main() sets it

BRANCH = {0: "Piercing", 1: "Ricochet", 2: "Explosion", 3: "Chain", 4: "Fire Rate", 5: "Reload Speed",
          10: "Armor Break", 11: "Impact", 12: "Cluster"}
ABBR = {"Piercing": "P", "Ricochet": "R", "Explosion": "X", "Chain": "C", "Fire Rate": "FR", "Reload Speed": "RS",
        "Armor Break": "AB", "Impact": "I", "Cluster": "CL"}
KS_OF = {"Piercing": "ks_piercing", "Ricochet": "ks_ricochet", "Explosion": "ks_explosion", "Chain": "ks_chain",
         "Fire Rate": "ks_firerate", "Reload Speed": "ks_reload", "Armor Break": "ks_armorbreak",
         "Impact": "ks_impact", "Cluster": "ks_cluster"}

# ---- assumptions (named so the report can print them) ----
HIT = {"sg_1": 0.60, "sg_2": 0.80, "sr_1": 0.85, "rl_1": 1.0}   # pellet/shot hit fraction; others 0.70
HIT_DEFAULT = 0.70
# Head share by camera mode (C-00 30.09, top-down head decision B: TopDown vertical spread 0 → a head hit only
# from the pose; TP/FP keep the round cone). TOPDOWN: S-02's pose measurement when it lands (none yet → 0).
# TP/FP: 0.15 until telemetry. Never set the head share to 0 with the hit fraction kept at 0.70 (a fake −13…−18 %
# DPS): in top-down the former vertical misses and head hits land on the body, so the hit fraction is calibrated to
# the decision's judged +2–3 % expected damage per fired shot (TOPDOWN_SHOT_GAIN) until S-02's geometry measurement.
HEAD_SHARE_BY_MODE = {"topdown": 0.0, "tpfp": 0.15}
TOPDOWN_SHOT_GAIN = 1.025
MODE = ["topdown"]           # set by --mode; section E reports both
HEAD_SHARE = HEAD_SHARE_BY_MODE["tpfp"]   # legacy name: the TP/FP value (tree nodes that key on head hits)
LINE_P = 0.5                 # P(another body behind the current one on a pierce line), crowd
RIC_P = 0.80                 # P(a ricochet finds a target within 6.5 m), crowd; ks_ricochet (2x seek) -> 0.95
RIC_P_SEEK = 0.95
CHAIN_P = 0.85               # P(each chain jump finds a target within 5 m), crowd
BODIES_PER_M2 = 0.10         # crowd density for blasts (2.2 m radius -> ~1.5 extra bodies)
STAGGER_UPTIME = 0.35        # share of a target's life it spends staggered (ks_impact, smg_2 cap)
HELD_FIRE_S = 3.0            # a held auto burst length for the warm-up nodes (ks_firerate, smg_1 fork Y)
STILL_SHARE = 0.40           # share of firing time standing still (lmg_1 fork X)
ROUND_S = 75.0               # a round's firing window for the ammo cap (reserve + floor refills)
GROUND_OCCUPANCY = 0.5       # share of a burning-ground zone's time a body stands in it (ks_explosion, rl_1 seal)
PICKUP = 1.00                # B5: packets never expire and the round-end sweep credits them before the shop (RunDirector)
ROUNDS = (1, 3, 5, 8, 10, 12, 15)
TIERS = ("Standard", "Special", "Elite", "Ultra")   # config order = Standard / Runner / Guard / Executioner


def load_tree():
    with open(TREE_JSON, encoding="utf-8") as f:
        return json.load(f)


def node_args(node):
    return {a["name"]: float(a["value"]) for a in node.get("args", [])}


def live_weapon(mb, design):
    """WeaponDefinition asset -> the r1 audit dict shape (design dict fills what the asset does not state)."""
    f, a, p = mb["fire"], mb["ammo"], mb["projectile"]
    w = copy.deepcopy(design)
    w.update(dmg=float(f["damage"]), pel=int(f["pelletCount"]), rpm=float(f["rpm"]), mag=int(a["magazine"]),
             res=int(a["reserve"]), rt=float(a["reloadTacSeconds"]), re=float(a["reloadEmptySeconds"]),
             head=float(p["headMultiplier"]), arm=float(p["armorDamageMultiplier"]), stag=float(p["stagger"]),
             pen=int(p["penetration"]), keep=float(p["penetrationDamageKeep"]) if int(p["penetration"]) > 0 else T["pierce_falloff"],
             full=float(p["falloffStart"]),
             maxr=float(p["falloffEnd"]), minpct=float(p["falloffMinShare"]),
             refill_s=float(a.get("floorRefillDelaySeconds", 0) or 0), refill_mags=float(a.get("floorRefillMagazines", 0) or 0))
    w["mode"] = {0: "single", 1: "burst", 2: "auto"}[int(f["fireMode"])]
    if w["mode"] == "burst":
        w.update(bn=int(f["burstCount"]), bi=float(f["burstIntervalSeconds"]), bd=float(f["burstDelaySeconds"]))
    if int(a["reloadKind"]) == 1 and design.get("pershell"):
        a0, _, b0, ex = design["pershell"]
        w["pershell"] = (a0, float(a["perShellSeconds"]), b0, ex)
    else:
        w.pop("pershell", None)
    cb = str(mb["upgrade"]["coreBranches"])
    w["core"] = [BRANCH[int(cb[i:i + 8][:2], 16)] for i in range(0, len(cb), 8)]
    return w


# ------------------------------------------------------------------------------------------------ effect state
def fresh_state():
    return dict(tiers={}, ks=set(), seal=False, fork=None, cap=False, hearth=0)


def mods(slot, w, st, tree):
    """Everything the owned nodes change, as numbers the DPS model reads."""
    g = lambda k: st["tiers"].get(k, 0)  # noqa: E731
    m = dict(fr=min(1 + T["fr_per_tier"] * g("Fire Rate"), T["fr_max"]),
             rl=max(T["rs_min"], 1 - T["rs_per_tier"] * g("Reload Speed")),
             mag=1.0, dmg=1.0 + 0.02 * st["hearth"], pel_add=0, hit_add=0.0,
             pierce=w["pen"] + T["pierce_per_tier"] * g("Piercing"), keep=w.get("keep", 0.65), full_bodies=0,
             ric=(min(1.0, T["ric_base"] + T["ric_per_tier"] * (g("Ricochet") - 1)) if g("Ricochet") else 0.0),
             ric_bounces=1 + (g("Ricochet") // 2 if g("Ricochet") else 0), ric_p=RIC_P,
             chain=T["chain_per_tier"] * g("Chain"), chain_fork=0,
             exp=(min(1.0, T["exp_base"] + T["exp_per_tier"] * (g("Explosion") - 1)) if g("Explosion") and slot != "rl_1" else 0.0),
             exp_scale=T["exp_scale"] + T["exp_scale_per_tier"] * max(0, g("Explosion") - 1),
             exp_r=T["exp_radius"] + T["exp_radius_per_tier"] * max(0, g("Explosion") - 1), burn=0.0,
             ab=T["ab_per_tier"] * g("Armor Break"), plate_x2=False, stag_bonus=0.0,
             head=w["head"], arm_ignore=0.0, noreload=0.0, blast_mult=1.0 + RL_BLAST_PER_TIER * (g("Explosion") if slot == "rl_1" else 0),
             blast_r=1.0, blast_tier=(g("Explosion") if slot == "rl_1" else 0), bomblets=(g("Cluster") + 1 if g("Cluster") else 0), blast_split=1, extra_on_plate=0.0,
             extra_fr=0.0, first_n=0, first_bonus=0.0, second_barrel_x=0.0)
    ks = tree["ks"]
    for k in st["ks"]:
        a = ks[k]
        if k == "ks_piercing":
            m["full_bodies"] = int(a["count"])
        elif k == "ks_ricochet":
            m["ric_p"] = RIC_P_SEEK
        elif k == "ks_explosion":
            m["burn"] = a["value"] / 100.0 * a["seconds"] * GROUND_OCCUPANCY
        elif k == "ks_chain":
            m["chain_fork"] = 1
        elif k == "ks_firerate":
            m["extra_fr"] += a["value"] / 100.0 * max(0.0, HELD_FIRE_S - a["seconds"]) / HELD_FIRE_S
        elif k == "ks_reload":
            m["mag_bonus"] = a["value"] / 100.0      # RunTreeHooks.FullMagExtraRounds: Max(1, floor(mag x bonus))
        elif k == "ks_armorbreak":
            m["plate_x2"] = True
        elif k == "ks_impact":
            m["stag_bonus"] += a["value"] / 100.0 * STAGGER_UPTIME
        elif k == "ks_cluster":
            m["bomblets"] += int(a["count"])
    lane = tree["w"][slot]
    if st["seal"]:
        weapon_node(slot, "seal", node_args(lane["seal"]), m, w, st)
    if st["fork"]:
        weapon_node(slot, st["fork"], node_args(lane["forkX" if st["fork"] == "x" else "forkY"]), m, w, st)
    if st["cap"]:
        weapon_node(slot, "cap", node_args(lane["capstone"]), m, w, st)
    return m


def weapon_node(slot, which, a, m, w, st):
    """The per-weapon seal / fork X / fork Y / capstone (run-tree.json text + args), as model numbers."""
    k = (slot, which)
    if k == ("ar_1", "seal"):
        m["hit_add"] += 0.06                       # bloom -50 % after 6 shots of a string (accuracy proxy)
    elif k == ("ar_1", "x"):
        m["hit_add"] += 0.08                       # bloom -50 % always
    elif k == ("ar_1", "y"):
        m["fr"] *= 1 + a["value"] / 100.0; m["hit_add"] -= 0.04
    elif k == ("ar_1", "cap"):
        m["arm_ignore"] = max(m["arm_ignore"], 1.0 / a["count"]); m["pierce_unlimited_share"] = 1.0 / a["count"]
    elif k == ("smg_1", "seal"):
        pass                                       # movement x1.10 while firing: survival, no DPS
    elif k == ("smg_1", "x"):
        m["mag"] *= a["value"]
    elif k == ("smg_1", "y"):
        if "ks_firerate" in st["ks"]:              # warm-up 1.5 -> 0.8 s
            m["extra_fr"] = 0.20 * max(0.0, HELD_FIRE_S - a["seconds"]) / HELD_FIRE_S
    elif k == ("smg_1", "cap"):
        m["ric_bounces"] += int(a["count"]) - 1
    elif k == ("sg_1", "seal"):
        m["pel_add"] += int(a["value"] - a["base"])
    elif k == ("sg_1", "x"):
        m["hit_add"] += 0.12                       # spread -40 %: more pellets on target at 4-6 m
    elif k == ("sg_1", "y"):
        m["pel_add"] += int(a["count"])
    elif k == ("sg_1", "cap"):
        m["extra_on_plate"] = 1.0                  # within 3 m: one plate broken for free
    elif k == ("ar_2", "seal"):
        m["head"] = w["head"] * a["value"] / a["base"]
    elif k == ("ar_2", "x"):
        m["burst_delay"] = 1 - a["value"] / 100.0
    elif k == ("ar_2", "y"):
        m["burst_n"] = int(a["value"])
    elif k == ("ar_2", "cap"):                     # DEC-112: every burst's 3rd round +value % and chains (no head condition)
        n = m.get("burst_n", w["bn"])
        m["dmg"] *= 1 + a["value"] / 100.0 / n
        m["chain_bonus_share"] = 1.0 / n
    elif k == ("smg_2", "seal"):
        m["stag_bonus"] += 0.0                     # stagger 0.45 -> 0.7 s: control, DPS only via ks_impact
        m["stag_up"] = 0.7 / 0.45
    elif k == ("smg_2", "x"):
        m["stag_up"] = m.get("stag_up", 1.0) * (1 + a["value"] / 100.0)
    elif k == ("smg_2", "y"):
        m["fr"] *= 1 + a["value"] / 100.0
    elif k == ("smg_2", "cap"):                    # DEC-112: + value % on a staggered body (adds to ks_impact), chain kept
        m["stag_bonus"] += a["value"] / 100.0 * STAGGER_UPTIME
        m["chain_bonus_share"] = STAGGER_UPTIME / a["count"]
    elif k == ("sg_2", "seal"):
        m["double_trigger"] = True                 # both barrels on one trigger: same DPS, burst front-loaded
    elif k == ("sg_2", "x"):
        m["dmg"] *= 1 + a["value"] / 100.0 * 0.6   # 60 % of sg_2 shots are within 4 m
    elif k == ("sg_2", "y"):
        m["rl"] *= 1 - a["value"] / 100.0
    elif k == ("sg_2", "cap"):
        m["second_barrel_x"] = 0.5                 # P(both barrels hit the same target) x blast
    elif k == ("lmg_1", "seal"):
        m["mag"] *= a["value"] / a["base"]
    elif k == ("lmg_1", "x"):
        m["dmg"] *= 1 + a["value"] / 100.0 * STILL_SHARE
    elif k == ("lmg_1", "y"):
        pass                                       # no movement penalty: survival
    elif k == ("lmg_1", "cap"):
        m["noreload"] = a["seconds"]               # one 6 s window of free fire per magazine cycle
    elif k == ("lmg_2", "seal"):
        m["hit_add"] += 0.06
    elif k == ("lmg_2", "x"):
        m["fr"] *= 1 + a["value"] / 100.0
    elif k == ("lmg_2", "y"):
        m["first_n"] = int(a["count"]); m["first_bonus"] = a["value"] / 100.0
    elif k == ("lmg_2", "cap"):
        pass                                       # DEC-112: a plate break staggers the body 0.35 s — survival, no DPS
    elif k == ("sr_1", "seal"):
        m["arm_ignore"] = max(m["arm_ignore"], head_share())
    elif k == ("sr_1", "x"):
        m["aimed_x"] = a["value"]; m["aimed_s"] = a["seconds"]
    elif k == ("sr_1", "y"):
        m["fr"] *= 1 + a["value"] / 100.0
    elif k == ("sr_1", "cap"):
        m["arm_ignore"] = 1.0; m["pierce_unlimited_share"] = 1.0
    elif k == ("rl_1", "seal"):
        m["rl_burn"] = a["value"] * a["seconds"]      # HP per body in the blast area
    elif k == ("rl_1", "x"):
        m["blast_mult"] *= 1 + a["value"] / 100.0; m["blast_r"] = a["radius"] / 100.0
    elif k == ("rl_1", "y"):
        m["blast_r"] = 1 + a["value"] / 100.0
    elif k == ("rl_1", "cap"):
        m["blast_split"] = int(a["count"])


# ------------------------------------------------------------------------------------------------ DPS model
def cycle(w, m):
    """(shots per magazine, seconds per magazine cycle) with the owned nodes."""
    mag0 = max(1, int(round(w["mag"] * m["mag"])))
    mag = mag0 + (max(1, int(math.floor(mag0 * m["mag_bonus"]))) if m.get("mag_bonus") else 0)
    fr = m["fr"] * (1 + m["extra_fr"])
    if w["mode"] == "burst":
        bn = m.get("burst_n", w["bn"])
        bd = w["bd"] * m.get("burst_delay", 1.0)
        per_burst = (bn - 1) * w["bi"] + bd
        fire_t = mag / bn * per_burst / fr
    elif "blast" in w:
        fire_t = 0.2 * mag
    else:
        fire_t = (mag - 1) * 60.0 / (w["rpm"] * fr)
    reload_t = base.reload_empty(w) * m["rl"]
    if m["noreload"]:
        # lmg_1 capstone: after 40 hits the next 6 s fire without draining the magazine
        free = m["noreload"] * w["rpm"] * fr / 60.0
        mag += free
        fire_t += m["noreload"]
    cyc = fire_t + reload_t
    # ammo cap: a round's shots cannot exceed magazine + reserve + floor refills (ks_reload's extra rounds come
    # from the reserve, ClassicWeaponRuntime l.419, so they speed up the burst, not the round)
    if w.get("refill_s", 0) > 0:
        ammo_rate = (w["mag"] + w["res"]) / ROUND_S + w["refill_mags"] * w["mag"] / (w["refill_s"] + base.reload_empty(w))
        cyc = max(cyc, mag / ammo_rate)
    return mag, cyc


def head_share():
    return HEAD_SHARE_BY_MODE[MODE[0]]


def per_shot_primary(w, m, slot):
    pel = w["pel"] + m["pel_add"]
    hit = min(1.0, HIT.get(slot, HIT_DEFAULT) + m["hit_add"])
    share = head_share()
    if MODE[0] == "topdown" and slot not in ("sg_1", "sg_2", "rl_1"):
        # the TP/FP expected value of a fired shot, moved onto the body, times the judged top-down gain
        base_head = 1 + HEAD_SHARE_BY_MODE["tpfp"] * (2 * w["head"] - 1)
        hit = min(1.0, hit * base_head * TOPDOWN_SHOT_GAIN)
    head = 1 + share * (2 * m["head"] - 1) if slot not in ("sg_1", "sg_2", "rl_1") else 1.0
    d = w["dmg"] * pel * hit * m["dmg"] * head
    if "blast" in w:
        blast = w["blast"]["dmg"] * m["blast_mult"] * m["dmg"]
        centre = RL_SPLIT_CENTRE if m["blast_split"] > 1 else 1.0
        d = w["dmg"] * m["dmg"] + blast * centre
        # ks_explosion (RunTreeHooks.OnRocketBlast): the whole disc burns at share x blast for seconds — the primary
        # took the full blast, so the burn only counts if it survived it
        d += min(blast * m["burn"], max(0.0, HP_REF[0] - blast * centre)) + m.get("rl_burn", 0) * GROUND_OCCUPANCY
        d += m["bomblets"] * RL_BOMBLET * 0.5 * m["dmg"]           # half the bomblets land on the target
    # a second shot-level effect on the same target
    d *= 1 + m["exp"] * m["exp_scale"] * (1 + m["burn"])          # branch explosion includes the primary
    d *= 1 + m["stag_bonus"] * m.get("stag_up", 1.0)
    if m["first_n"]:
        mag = w["mag"] * m["mag"]
        d *= 1 + m["first_bonus"] * min(1.0, m["first_n"] / mag)
    if m.get("second_barrel_x"):
        d *= 1 + m["second_barrel_x"] * 0.5
    return d


def plates(world, tier, r):
    a = world.armor
    dur = {"H": a["HelmetDurability"], "B": a["BodyDurability"], "L": a["LegDurability"]}
    return {s: world.armor_chance(tier, s, r) * dur[s] for s in "HBL"}


def eff_hp(world, w, m, tier, r, slot):
    """Health + expected plates in health-equivalent damage for this build."""
    pl = plates(world, tier, r)
    mult = w["arm"] * (1 + m["ab"])
    tot = 0.0
    for s, dur in pl.items():
        if dur <= 0:
            continue
        per_shot = w["dmg"] * mult * m["dmg"]
        eff = dur / mult
        if m["plate_x2"] and per_shot > 0:
            eff -= min(eff, w["dmg"] * m["dmg"])                  # the first hit counts double
        tot += eff
    tot *= 1 - m["arm_ignore"]
    if m["extra_on_plate"]:
        tot *= 1 - 0.5 * m["extra_on_plate"] / 3.0                 # one plate of ~3 broken on half the shots
    return world.hp(tier, r) + tot


def single(world, w, m, tier, r, slot):
    """(effective DPS vs the tier with expected plates, sustained seconds per kill) — shots x (cycle / magazine)."""
    mag, cyc = cycle(w, m)
    hp = world.hp(tier, r)
    ehp = eff_hp(world, w, m, tier, r, slot)
    per = per_shot_primary(w, m, slot)
    modes = [(per, cyc / mag)]
    if m.get("aimed_x"):                           # sr_1 fork X: waiting 2.5 s doubles the shot (a choice, not free)
        modes.append((per * m["aimed_x"], max(cyc / mag, m["aimed_s"] + base.reload_empty(w) * m["rl"] / mag)))
    best = None
    for dmg, per_shot_t in modes:
        eff = dmg / per_shot_t * hp / ehp
        t = math.ceil(ehp / dmg - 1e-9) * per_shot_t
        if best is None or t < best[1]:
            best = (eff, t)
    return best


def cyc_fire_time(w, m, n):
    if n <= 1:
        return 0.0
    fr = m["fr"] * (1 + m["extra_fr"])
    if w["mode"] == "burst":
        bn = m.get("burst_n", w["bn"])
        return ((n - 1) // bn * ((bn - 1) * w["bi"] + w["bd"] * m.get("burst_delay", 1.0)) + ((n - 1) % bn) * w["bi"]) / fr
    return (n - 1) * 60.0 / (w["rpm"] * fr)


def crowd_bodies(w, m, slot):
    """Health-equivalent bodies one shot damages beyond the primary, at the crowd density (sum of damage shares)."""
    extra = 0.0
    # pierce line
    n = m["pierce"] if not m.get("pierce_unlimited_share") else m["pierce"] + 3 * m["pierce_unlimited_share"]
    keep, share, p = m["keep"], 1.0, 1.0
    for i in range(1, int(math.ceil(n)) + 1):
        p *= LINE_P
        share = 1.0 if i <= m["full_bodies"] else share * keep
        extra += p * share
    # ricochet (bounces at 0.6)
    if m["ric"]:
        extra += m["ric"] * sum(m["ric_p"] ** b * T["ric_scale"] for b in range(1, m["ric_bounces"] + 1))
    # chain (branch tiers), then a capstone's bonus chain on a share of the shots (cap.ar_2 / cap.smg_2): one extra chain of
    # chain_bonus_jumps jumps at the chain scale, forked on its first jump when chain_bonus_fork — weighted by its share
    def chain_value(jumps, fork):
        sc, tot = T["chain_scale"], 0.0
        for j in range(jumps):
            tot += CHAIN_P ** (j + 1) * sc * (T["chain_falloff"] ** j) * (2 if (j == 0 and fork) else 1)
        return tot
    if m["chain"]:
        extra += chain_value(m["chain"], m["chain_fork"])
    if m.get("chain_bonus_share"):
        extra += m["chain_bonus_share"] * chain_value(m.get("chain_bonus_jumps", 1), m.get("chain_bonus_fork", False))
    # branch explosion around the primary
    if m["exp"]:
        extra += m["exp"] * m["exp_scale"] * (1 + m["burn"]) * BODIES_PER_M2 * math.pi * m["exp_r"] ** 2
    if "blast" in w:
        inner = (w["blast"]["inner"] + RL_BLAST_INNER_PER_TIER * m["blast_tier"]) * m["blast_r"]
        outer = (w["blast"]["outer"] + RL_BLAST_OUTER_PER_TIER * m["blast_tier"]) * m["blast_r"]
        per = max(1.0, per_shot_primary(w, m, slot))
        blast = w["blast"]["dmg"] * m["blast_mult"] * m["dmg"]
        area = BODIES_PER_M2 * math.pi * outer ** 2
        bodies = area * 0.5                                        # average falloff share over the disc
        if m["blast_split"] > 1:                                   # Ash Rain: damage x area of count smaller blasts
            bodies *= m["blast_split"] * RL_SPLIT_DAMAGE * RL_SPLIT_RADIUS ** 2
        extra += bodies * blast * (1 + m["bomblets"] * 0.15) / per
        # burning ground (ks_explosion on the whole disc, Hibana): only bodies the blast left alive take it (overkill)
        hp = HP_REF[0]
        if blast > hp and outer > inner:
            d_kill = inner + (outer - inner) * (1.0 - hp / blast)  # linear falloff inner → outer
            survivors = max(0.0, 1.0 - (min(d_kill, outer) / outer) ** 2)
        else:
            survivors = 1.0
        burn = min(blast * m["burn"], hp) + m.get("rl_burn", 0) * GROUND_OCCUPANCY
        extra += area * survivors * burn / per
    return extra


def crowd(w, m, slot):
    mag, cyc = cycle(w, m)
    return per_shot_primary(w, m, slot) * (1 + crowd_bodies(w, m, slot)) * mag / cyc


# ------------------------------------------------------------------------------------------------ builds
def lane_plan(world, tree, slot, w):
    core = w["core"]
    costs = {b: base.branch_costs(world, b) for b in core}
    p = [("tier", core[0], 1), ("tier", core[1], 1), ("tier", core[2], 1), ("purse", None, 0),
         ("tier", core[0], 2), ("tier", core[1], 2), ("ks", core[0], 0), ("ks", core[1], 0),
         ("seal", None, 0), ("fork", None, 0), ("cap", None, 0), ("tier", core[2], 2), ("ks", core[2], 0)]
    for k in range(3, 6):
        for b in core:
            if k <= len(costs[b]):
                p.append(("tier", b, k))
    return p, costs


def price(tree, kind, b, k, costs, slot):
    if kind == "tier":
        return costs[b][k - 1]
    if kind == "ks":
        return tree["ks"]["price"][KS_OF[b]]
    if kind == "purse":
        return tree["purse"]
    lane = tree["w"][slot]
    return {"seal": lane["seal"]["cost"], "fork": lane["forkX"]["cost"], "cap": lane["capstone"]["cost"]}[kind]


def build_at(world, tree, slot, w, fork="x", last_round=15, cards=2):
    """Greedy build per round (dict round -> state copy)."""
    plan, costs = lane_plan(world, tree, slot, w)
    st = fresh_state()
    wallet = 0.0
    purse = False
    out = {}
    spent = income = 0.0
    for r in range(1, last_round + 1):
        inc = world.gold(r, PICKUP) * (1.1 if purse else 1.0)
        wallet += inc
        income += inc
        bought = 0
        for kind, b, k in plan:
            if bought >= cards:
                break
            if kind == "tier" and (st["tiers"].get(b, 0) >= k or st["tiers"].get(b, 0) != k - 1):
                continue
            if kind == "ks" and (KS_OF[b] in st["ks"] or st["tiers"].get(b, 0) < 2):
                continue
            if kind == "purse" and purse:
                continue
            if kind == "seal" and (st["seal"] or not st["ks"]):
                continue
            if kind == "fork" and (st["fork"] or not st["seal"]):
                continue
            if kind == "cap" and (st["cap"] or not st["fork"] or len(st["ks"]) < 2):
                continue
            c = price(tree, kind, b, k, costs, slot)
            if wallet < c:
                break                                  # saves for the next planned node (TreeModel Human)
            wallet -= c
            spent += c
            bought += 1
            if kind == "tier":
                st["tiers"][b] = k
            elif kind == "ks":
                st["ks"].add(KS_OF[b])
            elif kind == "purse":
                purse = True
            elif kind == "seal":
                st["seal"] = True
            elif kind == "fork":
                st["fork"] = fork
            elif kind == "cap":
                st["cap"] = True
        h = 0
        while st["ks"] and h < 3 and st["hearth"] < 20:
            hp_ = tree["hearth"]["cost"] + tree["hearth"]["step"] * st["hearth"]
            if wallet < hp_ + 60:                      # keeps a card's price back
                break
            wallet -= hp_
            spent += hp_
            st["hearth"] += 1
            h += 1
        out[r] = dict(state=copy.deepcopy(st), wallet=wallet, income=income, spent=spent)
    return out


def build_label(st):
    parts = ["%s%d" % (ABBR[b], k) for b, k in st["tiers"].items()]
    if st["ks"]:
        parts.append("KS%d" % len(st["ks"]))
    if st["seal"]:
        parts.append("S")
    if st["fork"]:
        parts.append("F" + st["fork"].upper())
    if st["cap"]:
        parts.append("C")
    if st["hearth"]:
        parts.append("H%d" % st["hearth"])
    return " ".join(parts) or "—"


# ------------------------------------------------------------------------------------------------ report
def med(xs):
    s = sorted(xs)
    n = len(s)
    return s[n // 2] if n % 2 else 0.5 * (s[n // 2 - 1] + s[n // 2])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json")
    ap.add_argument("--mode", choices=["topdown", "tpfp"], default="topdown", help="camera mode for sections A–D (E reports both)")
    args = ap.parse_args()
    MODE[0] = args.mode
    cfg = base.load_config()
    world = base.World(cfg)
    live = base.load_live_weapons()
    raw = load_tree()
    tree = dict(ks={}, w={}, purse=raw["purse"]["cost"],
                hearth=dict(cost=raw["hearth"]["cost"], step=raw["hearth"]["costStep"]))
    tree["ks"]["price"] = {}
    for n in raw["branchKeystones"]:
        tree["ks"][n["id"]] = node_args(n)
        tree["ks"]["price"][n["id"]] = n["cost"]
    for wl in raw["weapons"]:
        tree["w"][wl["slot"]] = wl
    W = {s: live_weapon(live[s], base.W[s]) for s in base.ORDER if s in live}
    out = dict(weapons={}, ttk={}, nodes=[], economy={})
    P = print

    P("# WP-077 K10b balance audit — tree paths × weapon × round\n")
    P("Generated by `tools/balance/k10b_audit.py` from the live WD_*.asset, PrototypeConfig.asset and run-tree.json. "
      "First-order analytic model (see the script header); camera mode %s; hit %.2f (sg_1 %.2f, sg_2 %.2f, sr_1 %.2f), head share %.2f, "
      "crowd: line %.2f, ricochet %.2f, chain %.2f, %.2f bodies/m², pickup %.0f %%.\n"
      % (MODE[0], HIT_DEFAULT, HIT["sg_1"], HIT["sg_2"], HIT["sr_1"], head_share(), LINE_P, RIC_P, CHAIN_P, BODIES_PER_M2, PICKUP * 100))

    # ---- A. per weapon x round
    P("## A · Weapon × round (greedy lane build, fork X)\n")
    P("TTK = sustained seconds per kill (shots × magazine cycle / magazine) vs one target with the round's expected plates.\n")
    P("| weapon | core | R | build | eff. DPS vs Elite | crowd DPS | TTK Standard | Special | Elite | Ultra |")
    P("|---|---|---|---|---|---|---|---|---|---|")
    builds = {}
    for s, w in W.items():
        b = build_at(world, tree, s, w)
        builds[s] = b
        out["weapons"][s] = {}
        for r in ROUNDS:
            HP_REF[0] = world.hp(0, r)
            st = b[r]["state"]
            m = mods(s, w, st, tree)
            st_nc = dict(st, cap=False)
            m_nc = mods(s, w, st_nc, tree)
            row_nc = [None if world.tiers[t]["IntroductionRound"] > r else single(world, w, m_nc, t, r, s)[1] for t in range(4)]
            row = []
            for t in range(4):
                if world.tiers[t]["IntroductionRound"] > r:
                    row.append(None)
                    continue
                row.append(single(world, w, m, t, r, s)[1])
            d1 = single(world, w, m, 2, r, s)[0]
            dc = crowd(w, m, s)
            out["weapons"][s][r] = dict(build=build_label(st), dps=d1, crowd=dc, ttk=row, ttk_nocap=row_nc, crowd_nocap=crowd(w, m_nc, s))
            P("| %s | %s | R%d | %s | %.0f | %.0f | %s |" % (
                s, "+".join(ABBR[c] for c in w["core"]), r, build_label(st), d1, dc,
                " | ".join("—" if x is None else "%.2f" % x for x in row)))
    P("")

    # ---- B. TTK spread
    P("## B · TTK spread of the 10 weapons (seconds, round build)\n")
    P("| R | tier | median | fastest | slowest | max/min | outliers (>1.5× or <0.67× median) |")
    P("|---|---|---|---|---|---|---|")
    for r in (5, 10, 15):
        for t in (0, 2, 3):
            xs = {s: out["weapons"][s][r]["ttk"][t] for s in W if out["weapons"][s][r]["ttk"][t] is not None}
            if not xs:
                continue
            md = med(list(xs.values()))
            lo = min(xs, key=xs.get)
            hi = max(xs, key=xs.get)
            outl = ["%s %.2f" % (s, v) for s, v in xs.items() if v > 1.5 * md or v < 0.67 * md]
            out["ttk"]["R%d-%s" % (r, TIERS[t])] = dict(median=md, **xs)
            P("| R%d | %s | %.2f | %s %.2f | %s %.2f | %.1f | %s |" % (
                r, TIERS[t], md, lo, xs[lo], hi, xs[hi], xs[hi] / max(1e-6, xs[lo]), ", ".join(outl) or "—"))
    P("")
    P("Crowd DPS at R10 (swarm throughput, round build): " + ", ".join(
        "%s %.0f" % (s, out["weapons"][s][10]["crowd"]) for s in sorted(W, key=lambda s: -out["weapons"][s][10]["crowd"])))
    P("")
    P("### B2 · the same without the capstone (the tree model's human reaches it in ~1 run in 5)\n")
    P("| R | tier | median | fastest | slowest | max/min | outliers |")
    P("|---|---|---|---|---|---|---|")
    for r in (10, 15):
        for t in (0, 2, 3):
            xs = {s: out["weapons"][s][r]["ttk_nocap"][t] for s in W if out["weapons"][s][r]["ttk_nocap"][t] is not None}
            md = med(list(xs.values()))
            lo, hi = min(xs, key=xs.get), max(xs, key=xs.get)
            outl = ["%s %.2f" % (s, v) for s, v in xs.items() if v > 1.5 * md or v < 0.67 * md]
            out["ttk"]["R%d-%s-nocap" % (r, TIERS[t])] = dict(median=md, **xs)
            P("| R%d | %s | %.2f | %s %.2f | %s %.2f | %.1f | %s |" % (
                r, TIERS[t], md, lo, xs[lo], hi, xs[hi], xs[hi] / max(1e-6, xs[lo]), ", ".join(outl) or "—"))
    P("")
    P("Crowd DPS at R10 without the capstone: " + ", ".join(
        "%s %.0f" % (s, out["weapons"][s][10]["crowd_nocap"]) for s in sorted(W, key=lambda s: -out["weapons"][s][10]["crowd_nocap"])))
    P("")

    # ---- C. node power
    HP_REF[0] = world.hp(0, 10)
    P("## C · Node power on the R10 build (Δ % single-target Elite DPS / Δ % crowd DPS; per 100 gold)\n")
    P("| weapon | node | cost | Δ 1-target | Δ crowd | Δ best per 100 g | flag |")
    P("|---|---|---|---|---|---|---|")
    for s, w in W.items():
        st10 = copy.deepcopy(builds[s][10]["state"])
        st10["cap"] = False
        st10["fork"] = None
        base_m = mods(s, w, st10, tree)
        b1 = single(world, w, base_m, 2, 10, s)[0]
        bc = crowd(w, base_m, s)
        lane = tree["w"][s]
        cand = [("seal", lane["seal"]["cost"]), ("x", lane["forkX"]["cost"]), ("y", lane["forkY"]["cost"]),
                ("cap", lane["capstone"]["cost"])] + [(KS_OF[b], tree["ks"]["price"][KS_OF[b]]) for b in w["core"]]
        rows = []
        for nid, c in cand:
            st = copy.deepcopy(st10)
            if nid == "seal":
                if not st["seal"]:
                    st["seal"] = True
                else:
                    st0 = copy.deepcopy(st); st0["seal"] = False
                    m0 = mods(s, w, st0, tree)
                    b1n, bcn = single(world, w, m0, 2, 10, s)[0], crowd(w, m0, s)
                    rows.append((nid, c, b1 / b1n - 1, bc / bcn - 1))
                    continue
            elif nid in ("x", "y"):
                st["seal"] = True; st["fork"] = nid
            elif nid == "cap":
                st["seal"] = True; st["fork"] = "x"; st["cap"] = True
                sx = copy.deepcopy(st); sx["cap"] = False
                mx = mods(s, w, sx, tree)
                m1 = mods(s, w, st, tree)
                rows.append((nid, c, single(world, w, m1, 2, 10, s)[0] / single(world, w, mx, 2, 10, s)[0] - 1,
                             crowd(w, m1, s) / crowd(w, mx, s) - 1))
                continue
            else:
                if nid in st["ks"]:
                    st0 = copy.deepcopy(st); st0["ks"].discard(nid)
                    m0 = mods(s, w, st0, tree)
                    rows.append((nid, c, b1 / single(world, w, m0, 2, 10, s)[0] - 1, bc / crowd(w, m0, s) - 1))
                    continue
                st["ks"].add(nid)
            m1 = mods(s, w, st, tree)
            ref = mods(s, w, dict(st10, seal=True), tree) if nid in ("x", "y") else base_m
            r1_ = single(world, w, ref, 2, 10, s)[0]
            rc_ = crowd(w, ref, s)
            rows.append((nid, c, single(world, w, m1, 2, 10, s)[0] / r1_ - 1, crowd(w, m1, s) / rc_ - 1))
        for nid, c, d1, dc in rows:
            best = max(d1, dc)
            out["nodes"].append(dict(weapon=s, node=nid, cost=c, single=d1, crowd=dc, per100=best * 100 / c))
    # flags: by node class across weapons
    def cls(n):
        return "keystone" if n.startswith("ks_") else {"x": "fork", "y": "fork"}.get(n, n)
    by = {}
    for n in out["nodes"]:
        by.setdefault(cls(n["node"]), []).append(max(n["single"], n["crowd"]))
    meds = {k: med(v) for k, v in by.items()}
    for n in out["nodes"]:
        best = max(n["single"], n["crowd"])
        mdv = meds[cls(n["node"])]
        flag = ""
        if best < 0.02:
            flag = "no DPS (utility/survival)"
        elif best > 2.0 * max(mdv, 0.02):
            flag = "HIGH (>2× class median %.0f %%)" % (mdv * 100)
        elif best < 0.5 * mdv:
            flag = "low (<½ class median %.0f %%)" % (mdv * 100)
        n["flag"] = flag
        P("| %s | %s | %d | %+.0f %% | %+.0f %% | %+.1f %% | %s |" % (
            n["weapon"], n["node"], n["cost"], n["single"] * 100, n["crowd"] * 100, n["per100"] * 100, flag))
    P("")
    P("Class medians (best of single/crowd): " + ", ".join("%s %.0f %%" % (k, v * 100) for k, v in sorted(meds.items())))
    P("")
    # fork balance inside a weapon
    P("Fork X vs Y inside a weapon (best Δ): " + "; ".join(
        "%s X %+.0f / Y %+.0f %%" % (s, 100 * max(a["single"], a["crowd"]), 100 * max(b["single"], b["crowd"]))
        for s in W
        for a in [next(n for n in out["nodes"] if n["weapon"] == s and n["node"] == "x")]
        for b in [next(n for n in out["nodes"] if n["weapon"] == s and n["node"] == "y")]))
    P("")

    # ---- D. economy
    P("## D · Economy (gold per round at %.0f %% pickup, greedy lane spend; Kül = XP/30)\n" % (PICKUP * 100))
    P("| R | gold in round | cum. gold | ar_1 cum. spent | ar_1 wallet | ar_1 build | XP in round | Kül if the run ends here |")
    P("|---|---|---|---|---|---|---|---|")
    cum = 0.0
    cum_xp = 0.0
    for r in range(1, 16):
        g = world.gold(r, PICKUP)
        cum += g
        cum_xp += world.xp_round(r)
        b = builds["ar_1"][r]
        out["economy"][r] = dict(gold=g, cum=cum, spent=b["spent"], wallet=b["wallet"], xp=cum_xp, kul=cum_xp / 30.0)
        if r in (1, 3, 5, 8, 10, 12, 15):
            P("| R%d | %.0f | %.0f | %.0f | %.0f | %s | %.0f | %.0f |" % (
                r, g, cum, b["spent"], b["wallet"], build_label(b["state"]), world.xp_round(r), cum_xp / 30.0))
    P("")
    lane_total = {}
    for s, w in W.items():
        plan, costs = lane_plan(world, tree, s, w)
        lane_total[s] = sum(price(tree, k, b, t, costs, s) for k, b, t in plan)
    P("Full weapon lane price (tiers + 3 keystones + seal + fork + capstone + purse): " +
      ", ".join("%s %d" % (s, v) for s, v in lane_total.items()))
    firsts = {}
    for s in W:
        cap_r = next((r for r in range(1, 16) if builds[s][r]["state"]["cap"]), None)
        seal_r = next((r for r in range(1, 16) if builds[s][r]["state"]["seal"]), None)
        firsts[s] = (seal_r, cap_r)
    P("\nSeal / capstone round on the greedy path (no offer draw): " +
      ", ".join("%s R%s/R%s" % (s, a or "–", c or "–") for s, (a, c) in firsts.items()))
    out["economy"]["lane_total"] = lane_total
    out["economy"]["firsts"] = firsts
    # ---- E. sr_1 against the ar_1 × 1.2 target, by camera mode (C-00 30.09 top-down head decision)
    P("\n## E · sr_1 TTK vs the ar_1 × 1.2 target, by camera mode (no-capstone round build; ≤ 1.00 = target met)\n")
    P("Top-down: head share %.2f (S-02 pose measurement pending) and hit × the TP/FP head value × %.3f; TP/FP: head share %.2f.\n"
      % (HEAD_SHARE_BY_MODE["topdown"], TOPDOWN_SHOT_GAIN, HEAD_SHARE_BY_MODE["tpfp"]))
    P("| mode | R | sr_1 TTK Std / Elite / Ultra | ar_1 × 1.2 | sr_1 / (ar_1 × 1.2) |")
    P("|---|---|---|---|---|")
    keep_mode = MODE[0]
    out["sr1_vs_ar1"] = {}
    for mode in ("topdown", "tpfp"):
        MODE[0] = mode
        for r in (8, 10, 12, 15):
            HP_REF[0] = world.hp(0, r)
            row = {}
            for s in ("sr_1", "ar_1"):
                st = dict(builds[s][r]["state"], cap=False)
                m = mods(s, W[s], st, tree)
                row[s] = [single(world, W[s], m, t, r, s)[1] for t in (0, 2, 3)]
            ratio = [a / (1.2 * b) for a, b in zip(row["sr_1"], row["ar_1"])]
            out["sr1_vs_ar1"]["%s-R%d" % (mode, r)] = ratio
            P("| %s | R%d | %s | %s | %s |" % (mode, r, " / ".join("%.2f" % x for x in row["sr_1"]),
                                              " / ".join("%.2f" % (1.2 * x) for x in row["ar_1"]), " / ".join("%.2f" % x for x in ratio)))
    MODE[0] = keep_mode
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(out, f, indent=1, default=str)


if __name__ == "__main__":
    main()
