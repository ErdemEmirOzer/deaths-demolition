#!/usr/bin/env python3
"""M4-R1 balance audit (cloud, read-only): Arsenal v3 (WP-069), Specials, Progression tree v2 (WP-077).

Stdlib only. Reads the live Unity YAML (PrototypeConfig.asset, WD_*.asset) with a tiny block-YAML
parser, restates the design numbers of WP-069-ARSENAL-V3-DIRECTION.md / WP-077-...-DIRECTION.md as
literal tables (source line in comments), recomputes every derived value and prints a report.

    python3 tools/balance/r1_audit.py            # full text report
    python3 tools/balance/r1_audit.py --json out.json

Deterministic: every Monte Carlo uses random.Random(SEED). No Unity, no network.
"""
import argparse
import json
import math
import os
import random
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PROJ = os.path.join(ROOT, "prototype", "DeathsDemolition_PlayableConcept", "Assets", "_Prototype")
CONFIG_ASSET = os.path.join(PROJ, "Core", "Resources", "PrototypeConfig.asset")
ARSENAL_DIR = os.path.join(PROJ, "Weapons", "Arsenal", "Resources", "Arsenal")
SEED = 20260924

# --------------------------------------------------------------------------------------------
# Minimal Unity block-YAML reader (maps, "- " lists, scalars; flow values kept as strings)
# --------------------------------------------------------------------------------------------

def _scalar(v):
    v = v.strip()
    if v == "[]":
        return []
    if v == "{}":
        return {}
    try:
        if re.fullmatch(r"-?\d+", v) and not (len(v) > 1 and v.startswith("0")):
            return int(v)
        if re.fullmatch(r"-?\d*\.\d+(e-?\d+)?|-?\d+e-?\d+", v):
            return float(v)
    except ValueError:
        pass
    return v


def load_unity_yaml(path):
    lines = []
    with open(path, encoding="utf-8") as f:
        for raw in f:
            if raw.startswith("%") or raw.startswith("---"):
                continue
            s = raw.rstrip("\n")
            if not s.strip():
                continue
            ind = len(s) - len(s.lstrip(" "))
            lines.append([ind, s.strip()])
    # fold wrapped flow scalars ("m_Script: {fileID: 1,\n   guid: ...}") into their key line
    folded = []
    for ind, txt in lines:
        is_key = re.match(r"^(- )?[^\s:][^:]*:( |$)", txt) is not None
        if folded and not is_key and not txt.startswith("- "):
            folded[-1][1] += " " + txt
            continue
        folded.append([ind, txt])
    pos = [0]

    def parse_block(indent):
        if pos[0] >= len(folded):
            return None
        ind, txt = folded[pos[0]]
        if txt.startswith("- ") and ind == indent:
            out = []
            while pos[0] < len(folded):
                ind, txt = folded[pos[0]]
                if ind != indent or not txt.startswith("- "):
                    break
                rest = txt[2:]
                if re.match(r"^[^\s:][^:]*:( |$)", rest):
                    # map item: rewrite line as its first key at indent+2
                    folded[pos[0]] = [indent + 2, rest]
                    out.append(parse_map(indent + 2))
                else:
                    pos[0] += 1
                    out.append(_scalar(rest))
            return out
        return parse_map(ind)

    def parse_map(indent):
        out = {}
        while pos[0] < len(folded):
            ind, txt = folded[pos[0]]
            if ind != indent or txt.startswith("- "):
                break
            m = re.match(r"^([^:]+):\s*(.*)$", txt)
            if not m:
                pos[0] += 1
                continue
            key, val = m.group(1).strip(), m.group(2)
            pos[0] += 1
            if val == "":
                if pos[0] < len(folded):
                    nind, ntxt = folded[pos[0]]
                    if nind > indent or (nind == indent and ntxt.startswith("- ")):
                        out[key] = parse_block(nind)
                        continue
                out[key] = None
            else:
                out[key] = _scalar(val)
        return out

    return parse_block(folded[0][0])


def load_config():
    root = load_unity_yaml(CONFIG_ASSET)["MonoBehaviour"]
    return root


def load_live_weapons():
    out = {}
    if not os.path.isdir(ARSENAL_DIR):
        return out
    for fn in sorted(os.listdir(ARSENAL_DIR)):
        if fn.startswith("WD_") and fn.endswith(".asset"):
            mb = load_unity_yaml(os.path.join(ARSENAL_DIR, fn))["MonoBehaviour"]
            out[mb["weaponId"]] = mb
    return out


# --------------------------------------------------------------------------------------------
# Design data, WP-069-ARSENAL-V3-DIRECTION.md §0 / §1 3a (l.80-89) / 3b (l.97-106) / 4a (l.135-144)
# --------------------------------------------------------------------------------------------
# mode: auto | burst | single ; interval = seconds between shots at "rpm" (single/auto)
# burst: 3 shots 0.05 s apart, 0.26 s after the burst (l.58) -> cycle 0.36 s
W = {
    "ar_1": dict(dmg=12, pel=1, rpm=600, mode="auto", mag=30, res=210, rt=1.6, re=2.1, head=1.0, arm=1.0,
                 stag=0.25, pen=1, loss=0.40, full=30, minpct=0.70, maxr=60, cone=(1.4, 0.7), move=0.92, unlock=0),
    "ar_2": dict(dmg=16, pel=1, rpm=500, mode="burst", bn=3, bi=0.05, bd=0.26, mag=24, res=168, rt=1.8, re=2.3,
                 head=1.15, arm=1.2, stag=0.30, pen=1, loss=0.35, full=40, minpct=0.75, maxr=70, cone=(1.0, 0.45),
                 move=0.92, unlock=3),
    "smg_1": dict(dmg=8, pel=1, rpm=900, mode="auto", mag=42, res=294, rt=1.5, re=2.0, head=1.0, arm=0.7,
                  stag=0.12, pen=0, loss=0.0, full=14, minpct=0.55, maxr=40, cone=(2.8, 1.6), move=1.00, unlock=1),
    "smg_2": dict(dmg=14, pel=1, rpm=540, mode="auto", mag=24, res=192, rt=1.7, re=2.2, head=1.0, arm=0.9,
                  stag=0.45, pen=1, loss=0.50, full=18, minpct=0.60, maxr=42, cone=(2.2, 1.2), move=0.96, unlock=6),
    "sg_1": dict(dmg=9, pel=8, rpm=72, mode="single", mag=7, res=42, rt=None, re=None, pershell=(0.40, 0.55, 0.35, 0.5),
                 head=1.0, arm=0.8, stag=0.60, pen=0, loss=0.0, full=6, minpct=0.35, maxr=24, cone=(7.5, 5.5),
                 pattern=("rings", [(1, 0.0), (3, 0.40), (4, 1.00)]), move=0.90, unlock=2),
    "sg_2": dict(dmg=11, pel=10, rpm=200, mode="single", mag=2, res=40, rt=1.7, re=1.7, head=1.0, arm=0.8,
                 stag=0.70, pen=0, loss=0.0, full=8, minpct=0.40, maxr=20, cone=(5.0, 3.8),
                 pattern=("gauss", 0.45), move=0.92, unlock=7),
    "lmg_1": dict(dmg=11, pel=1, rpm=720, mode="auto", mag=100, res=300, rt=4.4, re=5.2, head=1.0, arm=1.0,
                  stag=0.20, pen=1, loss=0.40, full=28, minpct=0.65, maxr=55, cone=(2.4, 1.4), move=0.80, unlock=4),
    "lmg_2": dict(dmg=24, pel=1, rpm=420, mode="auto", mag=60, res=180, rt=5.0, re=5.8, head=1.0, arm=1.3,
                  stag=0.55, pen=2, loss=0.30, full=32, minpct=0.70, maxr=60, cone=(1.2, 0.8), move=0.70, unlock=8),
    "sr_1": dict(dmg=55, pel=1, rpm=48, mode="single", mag=5, res=40, rt=2.6, re=3.2, head=1.25, arm=1.6,
                 stag=0.80, pen=4, loss=0.20, full=60, minpct=0.85, maxr=90, cone=(3.0, 0.15), move=0.85, unlock=5),
    # rl_1: 110 direct + 160 blast; 20 RPM = 0.2 s + 2.8 s reload (l.89, §5 l.173-180)
    "rl_1": dict(dmg=110, pel=1, rpm=20, mode="single", mag=1, res=8, rt=2.8, re=2.8, head=1.0, arm=1.0,
                 stag=1.0, pen=0, loss=0.0, full=70, minpct=1.0, maxr=70, cone=(1.5, 0.6), move=0.78, unlock=9,
                 blast=dict(dmg=160, inner=1.8, outer=5.0, exp=1.0)),
}
ORDER = ["ar_1", "ar_2", "smg_1", "smg_2", "sg_1", "sg_2", "lmg_1", "lmg_2", "sr_1", "rl_1"]

# §1 3c as printed (l.114-123): DPS, StdR1, StdR10, Guard body, Guard head, Exec, Guard TTK, mag dmg
DOC_3C = {
    "ar_1": (120, 3, 6, 13, 7, 30, 1.20, 360),
    "ar_2": (133, 3, 5, 10, 5, 23, 1.08, 384),
    "smg_1": (120, 5, 9, 20, 10, 45, 1.27, 336),
    "smg_2": (126, 3, 6, 12, 6, 26, 1.22, 336),
    "sg_1": (86, 1, 1, 3, 2, 5, 1.67, 504),
    "sg_2": (367, 1, 1, 2, 1, 4, 0.30, 220),
    "lmg_1": (132, 4, 7, 15, 8, 33, 1.17, 1100),
    "lmg_2": (168, 2, 3, 7, 4, 15, 0.86, 1440),
    "sr_1": (44, 1, 2, 3, 2, 7, 2.50, 275),
    "rl_1": (96, 1, 1, 1, 1, 2, None, 270),
}
DOC_LINE_3C = {k: 114 + i for i, k in enumerate(ORDER)}

# Specials §7a (l.221-229), §7b (l.239-247), §7c (l.256-259)
RAIJU = dict(mag=3, res=9, chain=8, hop=6.0, charge=0.25, cycle=0.9, rt=3.0, re=3.6, cap=40.0, shock=1.2,
             exec_share=0.60)
NAMEKUJI = dict(mag=8, res=16, cycle=0.45, rt=2.4, re=2.8, cap=45.0, puddle_r=1.6, puddle_s=25.0, puddle_cap=12,
                death_r=0.9, death_s=8.0)

# Enemies/ZombieBodyBuilder.cs:81 UpperHitboxRadius 0.26 m (real hitbox); 0.45 = Standard BodyRadius (optimistic bound)
HIT_HALF_WIDTH = {"hitbox": 0.26, "bodyRadius": 0.45}


# --------------------------------------------------------------------------------------------
# Config-derived world (PrototypeConfig.asset)
# --------------------------------------------------------------------------------------------
class World:
    TIER_NAMES = ["Standard", "Runner", "Guard", "Executioner"]

    def __init__(self, cfg):
        e = cfg["_enemies"]
        self.base_hp = float(e["StandardBaseHealth"])
        self.tiers = e["Tiers"]
        self.armor = e["Armor"]
        self.round = cfg["_rounds"] if "_rounds" in cfg else self._find(cfg, "PoolBase")
        self.econ = cfg["_economy"]
        self.prog = cfg["_progression"]
        self.special = cfg["_weapons"]["Special"]
        self.overrides = {o["Round"]: o for o in (cfg.get("_roundDifficultyOverrides") or [])}

    @staticmethod
    def _find(cfg, key):
        for v in cfg.values():
            if isinstance(v, dict) and key in v:
                return v
        raise KeyError(key)

    # RoundConfigSection.cs
    def pool(self, r):
        rc = self.round
        s = r - 1
        p = 0.0 if s <= 0 else s ** rc["PoolPowerExponent"]
        return max(1, int(round(rc["PoolBase"] + rc["PoolPerRound"] * s + math.floor(rc["PoolPowerScale"] * p))))

    def active(self, r):
        rc = self.round
        return max(1, min(int(math.floor(rc["ActiveBase"] + rc["ActivePerRound"] * r)), rc["ActiveCap"],
                          rc["AbsoluteSafetyCeiling"]))

    def hp_mult(self, r):
        return 1.0 + self.round["HealthMultiplierPerRound"] * max(0, r - 1)

    def hp(self, tier_idx, r):
        return self.base_hp * self.tiers[tier_idx]["HealthMultiplier"] * self.hp_mult(r)

    def weight(self, t, r):
        ts = self.tiers[t]
        if r < ts["IntroductionRound"]:
            return 0.0
        o = self.overrides.get(r)
        if o and o.get("OverrideTierWeights"):
            for row in o.get("TierWeights") or []:
                if row["Tier"] == t:
                    return max(0.0, float(row["Weight"]))
        steps = max(0, r - ts["IntroductionRound"])
        return min(ts["SpawnWeight"] + ts["SpawnWeightGrowthPerRound"] * steps, ts["SpawnWeightMax"])

    def armor_chance(self, t, slot, r):
        ts = self.tiers[t]
        ac = ts["ArmorChances"]
        o = self.overrides.get(r)
        if o and o.get("OverrideArmorChances"):
            scope = o.get("ArmorTiers")
            tiers = [] if not scope else [int(scope[i:i + 8][:2], 16) for i in range(0, len(scope), 8)]
            if not tiers or t in tiers:
                ac = o["ArmorChances"]
        base = {"H": ac["HelmetChance"], "B": ac["BodyChance"], "L": ac["LegChance"]}[slot]
        return max(0.0, min(base + ac["ChanceGrowthPerRound"] * max(0, r - 1), ac["MaxChance"]))

    def mix(self, r):
        """Expected tier counts: pool x weight share, floored by REQ-071 debut and DEC-021 forced counts."""
        P = self.pool(r)
        w = [self.weight(t, r) for t in range(4)]
        tot = sum(w)
        n = [P * wi / tot for wi in w]
        floor_ = [0.0] * 4
        for t in range(1, 4):
            if self.tiers[t]["IntroductionRound"] == r:
                floor_[t] = 1.0
        o = self.overrides.get(r)
        if o and o.get("OverrideForcedCounts"):
            for row in o.get("ForcedTierCounts") or []:
                floor_[row["Tier"]] = max(floor_[row["Tier"]], float(row["Count"]))
        for t in range(1, 4):
            if n[t] < floor_[t]:
                n[0] -= floor_[t] - n[t]
                n[t] = floor_[t]
        return n

    def gold(self, r, pickup=1.0):
        n = self.mix(r)
        e = self.econ
        bonus = {"H": e["ArmorGoldBonusHelmet"], "B": e["ArmorGoldBonusBody"], "L": e["ArmorGoldBonusLeg"]}
        g = 0.0
        for t in range(4):
            per = self.tiers[t]["GoldReward"] + sum(self.armor_chance(t, s, r) * bonus[s] for s in "HBL")
            g += n[t] * per
        return g * pickup

    def xp_round(self, r, completed=True, kill_frac=1.0):
        n = self.mix(r)
        pr = self.prog
        kills = sum(n[t] * (pr["XpPerKill"] + self.tiers[t]["BonusKillXp"]) for t in range(4)) * kill_frac
        rx = pr["RoundXpBase"] + pr["RoundXpPerRound"] * max(1, r) if completed else 0
        return rx + kills

    def hp_round(self, r):
        n = self.mix(r)
        a = self.armor
        dur = {"H": a["HelmetDurability"], "B": a["BodyDurability"], "L": a["LegDurability"]}
        tot = 0.0
        for t in range(4):
            arm = sum(self.armor_chance(t, s, r) * dur[s] for s in "HBL")
            tot += n[t] * (self.hp(t, r) + arm)
        return tot


# --------------------------------------------------------------------------------------------
# Weapon math
# --------------------------------------------------------------------------------------------

def time_to_n_shots(w, n):
    """Elapsed seconds from shot 1 to shot n (0 for n==1), reloading when the magazine runs dry."""
    t = 0.0
    in_mag = w["mag"] - 1
    burst_i = 1
    for _ in range(n - 1):
        if in_mag == 0:
            t += reload_empty(w)
            in_mag = w["mag"]
            burst_i = 0
        else:
            if w["mode"] == "burst":
                t += w["bi"] if burst_i % w["bn"] != 0 else w["bd"]
            else:
                t += 60.0 / w["rpm"]
        in_mag -= 1
        burst_i += 1
    return t


def reload_empty(w):
    if w.get("pershell"):
        a, per, b, empty_extra = w["pershell"]
        return a + per * w["mag"] + b + empty_extra
    return w["re"]


def reload_tac(w):
    if w.get("pershell"):
        a, per, b, _ = w["pershell"]
        return a + per * w["mag"] + b
    return w["rt"]


def shot_damage(w):
    return w["dmg"] * w["pel"]


def burst_dps(w):
    if w["mode"] == "burst":
        cycle = (w["bn"] - 1) * w["bi"] + w["bd"]
        return shot_damage(w) * w["bn"] / cycle
    return shot_damage(w) * w["rpm"] / 60.0


def mag_time(w):
    return time_to_n_shots(w, w["mag"])


def sustained_dps(w):
    """Repeating period = (time from first to last shot of a magazine) + empty reload; reload starts
    on the last shot. For rl_1 the doc's own '0.2 s + 2.8 s' cycle (20 RPM) is used instead."""
    if "blast" in w:
        return (w["dmg"] + w["blast"]["dmg"]) / (60.0 / w["rpm"])
    return shot_damage(w) * w["mag"] / (mag_time(w) + reload_empty(w))


def falloff(w, d):
    if d <= w["full"]:
        return 1.0
    if d >= w["maxr"]:
        return w["minpct"]
    return 1.0 + (w["minpct"] - 1.0) * (d - w["full"]) / (w["maxr"] - w["full"])


def shots_to_kill(w, hp, head=False, dist=0.0, pellets_hit=None):
    mult = (2.0 * w["head"]) if head else 1.0
    per = w["dmg"] * mult * falloff(w, dist) * (w["pel"] if pellets_hit is None else pellets_hit)
    if "blast" in w:
        per = w["dmg"] * mult + w["blast"]["dmg"]
    return math.ceil(hp / per - 1e-9)


def ttk(w, hp, **kw):
    n = shots_to_kill(w, hp, **kw)
    return n, time_to_n_shots(w, n)


def plate_then_health_shots(w, durability, hp):
    """Regional armour (RegionalDamageBody REQ-083): every projectile (pellet) that lands on an armoured
    region goes into the plate, surplus discarded; after the plate breaks the rest goes to health."""
    to_break = math.ceil(durability / (w["dmg"] * w["arm"]) - 1e-9)
    to_kill = math.ceil(hp / w["dmg"] - 1e-9)
    return math.ceil((to_break + to_kill) / w["pel"])


def rocket_vs_guard(world, r, rl):
    """P(one rl_1 direct upper-body hit + full blast kills a Guard of round r), over armour combos.
    Direct 110 on Upper: plate absorbs if Body plate. Blast 160 split 25/45/30; armoured share -> plate."""
    hp = world.hp(2, r)
    ph = {s_: world.armor_chance(2, s_, r) for s_ in "HBL"}
    shares = {"H": 0.25, "B": 0.45, "L": 0.30}
    p_kill = 0.0
    for h in (0, 1):
        for b in (0, 1):
            for l in (0, 1):
                pr = (ph["H"] if h else 1 - ph["H"]) * (ph["B"] if b else 1 - ph["B"]) * (ph["L"] if l else 1 - ph["L"])
                arm = {"H": h, "B": b, "L": l}
                dmg = 0.0 if b else rl["dmg"]            # direct hit on the body plate is swallowed
                blast = rl["blast"]["dmg"]
                # direct hit first breaks the body plate (110 >= 38): the blast then sees Upper exposed
                arm_after = dict(arm, B=0)
                dmg += sum(blast * shares[s_] for s_ in "HBL" if not arm_after[s_])
                if dmg >= hp - 1e-9:
                    p_kill += pr
    return p_kill


def pellets_on_target(w, d, half_width, focus=False, planar=False):
    """Expected pellets inside a target of half-width `half_width` at distance d (top-down cone)."""
    theta = math.radians(w["cone"][1 if focus else 0])
    R = d * math.tan(theta)
    kind = w["pattern"][0]
    if kind == "rings":
        hits = 0.0
        for count, share in w["pattern"][1]:
            if planar:
                for k in range(count):
                    phi = 2 * math.pi * k / count
                    if abs(share * R * math.cos(phi)) <= half_width:
                        hits += 1
            else:
                hits += count if share * R <= half_width else 0
        return hits
    sigma = w["pattern"][1] * R
    if sigma <= 0:
        return w["pel"]
    if planar:
        p = math.erf(half_width / (sigma * math.sqrt(2)))
    else:
        p = 1 - math.exp(-half_width ** 2 / (2 * sigma ** 2))
    return w["pel"] * p


def blast_at(w, r):
    b = w["blast"]
    if r <= b["inner"]:
        return b["dmg"]
    if r >= b["outer"]:
        return 0.0
    return b["dmg"] * ((b["outer"] - r) / (b["outer"] - b["inner"])) ** b["exp"]


# --------------------------------------------------------------------------------------------
# Specials: chain Monte Carlo + activation simulation
# --------------------------------------------------------------------------------------------

def chain_len(n_alive, swarm_r, hop, max_targets, rng, trials=4000):
    """Zombies uniform in a disc of radius swarm_r around the player (min 1.5 m). The orb hits the
    nearest zombie in a random 20-degree aim cone (fallback: nearest overall); chain = nearest unhit within hop."""
    if n_alive <= 0:
        return 0.0
    total = 0
    for _ in range(trials):
        pts = []
        for _ in range(n_alive):
            rr = math.sqrt(rng.uniform((1.5 / swarm_r) ** 2, 1.0)) * swarm_r
            a = rng.uniform(0, 2 * math.pi)
            pts.append((rr * math.cos(a), rr * math.sin(a)))
        aim = rng.uniform(0, 2 * math.pi)
        cand = [p for p in pts if abs((math.atan2(p[1], p[0]) - aim + math.pi) % (2 * math.pi) - math.pi) < math.radians(10)]
        first = min(cand or pts, key=lambda p: p[0] ** 2 + p[1] ** 2)
        hit = {first}
        cur = first
        while len(hit) < max_targets:
            best, bd = None, hop * hop
            for p in pts:
                if p in hit:
                    continue
                dd = (p[0] - cur[0]) ** 2 + (p[1] - cur[1]) ** 2
                if dd <= bd:
                    best, bd = p, dd
            if best is None:
                break
            hit.add(best)
            cur = best
        total += len(hit)
    return total / trials


def raiju_activation(world, r, swarm_r, rng, walk_in=9.6):
    """Kills of one Raiju pickup: 12 orbs, chain vs the alive crowd, killed zombies are replaced by
    fresh spawns that need `walk_in` seconds (18-35 m band at 2.7 m/s) to rejoin the swarm."""
    P = world.pool(r)
    n = world.mix(r)
    exec_share = n[3] / P
    remaining = int(round(0.5 * P))           # Special spawns at 30-70 % of the pool (window mid 0.5)
    alive = world.active(r)
    remaining = max(0, remaining)             # unspawned
    killable = remaining + alive
    t, kills, shots, in_mag = 0.0, 0.0, 0, RAIJU["mag"]
    reserve = RAIJU["res"]
    arrivals = []                             # (time, count)
    cache = {}
    while shots < RAIJU["mag"] + RAIJU["res"] and t <= RAIJU["cap"] and killable - kills > 0.5:
        if in_mag == 0:
            if reserve == 0:
                break
            t += RAIJU["re"]
            take = min(RAIJU["mag"], reserve)
            reserve -= take
            in_mag = take
        # rejoiners
        for a in list(arrivals):
            if a[0] <= t:
                alive += a[1]
                arrivals.remove(a)
        near = int(round(min(alive, killable - kills)))
        if near not in cache:
            cache[near] = chain_len(near, swarm_r, RAIJU["hop"], RAIJU["chain"], rng, trials=600)
        c = cache[near]
        # an Executioner in the chain survives the first orb (60 % + stun): count it as 0.5 kill
        c_eff = c * (1 - exec_share) + c * exec_share * 0.5
        kills += c_eff
        alive -= c
        refill = min(c, max(0.0, remaining))
        remaining -= refill
        arrivals.append((t + RAIJU["shock"] + walk_in, refill))
        shots += 1
        in_mag -= 1
        t += RAIJU["charge"] + RAIJU["cycle"]
    return dict(round=r, pool=P, killable=killable, kills=kills, shots=shots, seconds=t,
                upper=min(RAIJU["chain"] * (RAIJU["mag"] + RAIJU["res"]), killable))


def namekuji_activation(world, r, coverage=0.8, walk_in=9.6):
    """Kills of one Namekuji pickup. Six 1.6 m puddles close a 3 m ring around the player (2*pi*3/3.2);
    every non-Executioner that crosses a live puddle dies. Throughput = spawn refill (2 per 0.45 s)
    delayed by walk-in; ring re-laid from the second/third magazine keeps it alive for the 45 s cap."""
    P = world.pool(r)
    n = world.mix(r)
    exec_share = n[3] / P
    remaining = 0.5 * P
    alive = world.active(r)
    killable = (remaining + alive) * (1 - exec_share)
    ring_puddles = math.ceil(2 * math.pi * 3.0 / (2 * NAMEKUJI["puddle_r"]))
    # ring uptime within the 45 s cap: lay 6 (2.7 s), re-lay at 25 s from mag 2/3 (24 shots >= 3 rings)
    rings_possible = (NAMEKUJI["mag"] + NAMEKUJI["res"]) // ring_puddles
    uptime = min(NAMEKUJI["cap"], rings_possible * NAMEKUJI["puddle_s"]) - ring_puddles * NAMEKUJI["cycle"]
    spawn_rate = 2 / 0.45
    arrivals = alive + spawn_rate * max(0.0, uptime - walk_in)
    kills = min(killable, arrivals * (1 - exec_share) * coverage)
    return dict(round=r, pool=P, killable=killable, kills=kills, ring_puddles=ring_puddles, uptime=uptime)


# --------------------------------------------------------------------------------------------
# WP-077 progression: tree, bot and median simulations
# --------------------------------------------------------------------------------------------

def branch_costs(world, name):
    for b in world.econ["Branches"]:
        if b["DisplayName"] == name:
            return [int(b["BaseCost"] * b["CostGrowthPerTier"] ** k) for k in range(b["MaxTier"])]
    raise KeyError(name)


def hearth_price(n, mode="table"):
    """WP-077 §2.1 says 50 + 30n; the §2.6 table (50 80 110 ... 620) is 50 + 30(n-1)."""
    return 50 + 30 * (n - 1) if mode == "table" else 50 + 30 * n


def build_plan(world, weapon_core=("Fire Rate", "Reload Speed", "Piercing")):
    """Priority list for the greedy bot on ar_1 (fastest capstone path, then fill)."""
    fr, rs, pc = (branch_costs(world, n) for n in weapon_core)
    plan = [
        ("tier", "FR1", fr[0]), ("tier", "RS1", rs[0]), ("purse", "Kese", 60),
        ("tier", "FR2", fr[1]), ("tier", "RS2", rs[1]),
        ("keystone", "KS_FR", 110), ("keystone", "KS_RS", 110), ("seal", "Seal", 150), ("fork", "Fork", 90),
        ("capstone", "Capstone", 380),
        ("tier", "PC1", pc[0]), ("tier", "PC2", pc[1]), ("keystone", "KS_PC", 110),
        ("tier", "FR3", fr[2]), ("tier", "RS3", rs[2]), ("tier", "PC3", pc[2]), ("tier", "FR4", fr[3]),
        ("tier", "RS4", rs[3]), ("tier", "PC4", pc[3]), ("tier", "FR5", fr[4]), ("tier", "RS5", rs[4]),
    ]
    mv, hpb, rd, rr = (branch_costs(world, n) for n in
                       ("Movement Speed", "Maximum Health", "Regeneration Delay", "Regeneration Rate"))
    body = []
    for i in range(5):
        for nm, arr in (("MV", mv), ("HP", hpb), ("RD", rd), ("RR", rr)):
            if i < len(arr):
                body.append(("tier", "%s%d" % (nm, i + 1), arr[i]))
        if i == 1:
            body += [("bkeystone", "Kacis", 140), ("bkeystone", "KalinDeri", 140), ("bfork", "BFork", 100)]
        if i == 2:
            body += [("bkeystone", "Nefes", 140), ("bkeystone", "KanliAdim", 140), ("bcap", "IkinciNefes", 320)]
    return plan + body


def simulate_tree(world, rounds=15, pickup=1.0, cards_per_round=(2,), reserve_share=0.0, hearth_per_round=3,
                  hearth_min_wallet_after=0, hearth_mode="table", reserve_abs=0.0):
    plan = build_plan(world)
    owned = []
    wallet = 0.0
    income_tot = spent_tot = 0.0
    purse = False
    hearth_n = 0
    first_ks = first_cap = None
    rows = []
    for r in range(1, rounds + 1):
        inc = world.gold(r, pickup) * (1.1 if purse else 1.0)
        wallet += inc
        income_tot += inc
        cards = cards_per_round[(r - 1) % len(cards_per_round)]
        budget_floor = max(wallet * reserve_share, reserve_abs)
        bought = []
        for _ in range(cards):
            pick = None
            for node in plan:
                if node[1] in owned:
                    continue
                if node[0] == "capstone" and not {"Seal", "Fork", "KS_FR", "KS_RS"} <= set(owned):
                    continue
                if node[0] in ("keystone", "seal", "fork") and not any(o.startswith("KS_") for o in owned) \
                        and node[0] != "keystone":
                    continue
                if node[1] == "KS_FR" and "FR2" not in owned:
                    continue
                if node[1] == "KS_RS" and "RS2" not in owned:
                    continue
                if node[1] == "KS_PC" and "PC2" not in owned:
                    continue
                if node[1][:2] in ("FR", "RS", "PC", "MV", "HP", "RD", "RR") and node[1][-1].isdigit():
                    k = int(node[1][-1])
                    if k > 1 and node[1][:-1] + str(k - 1) not in owned:
                        continue
                if node[0] == "bcap" and not {"Kacis", "KalinDeri", "BFork"} <= set(owned):
                    continue
                if node[0] == "bfork" and not any(o in owned for o in ("Kacis", "KalinDeri")):
                    continue
                if node[0] == "bkeystone":
                    base = {"Kacis": "MV2", "KalinDeri": "HP2", "Nefes": "RD2", "KanliAdim": "RR2"}[node[1]]
                    if base not in owned:
                        continue
                pick = node
                break
            if pick is None or wallet - pick[2] < budget_floor:
                break
            wallet -= pick[2]
            spent_tot += pick[2]
            owned.append(pick[1])
            bought.append(pick[1])
            if pick[0] == "purse":
                purse = True
            if pick[0] == "keystone" and first_ks is None:
                first_ks = r
            if pick[0] == "capstone" and first_cap is None:
                first_cap = r
        # Hearth: direct buys, needs any branch keystone, <= 3 per round, cap 20
        h_this = 0
        if any(o.startswith("KS_") for o in owned):
            while h_this < hearth_per_round and hearth_n < 20:
                price = hearth_price(hearth_n + 1, hearth_mode)
                if wallet - price < max(budget_floor, hearth_min_wallet_after):
                    break
                wallet -= price
                spent_tot += price
                hearth_n += 1
                h_this += 1
        rows.append(dict(round=r, income=round(inc, 1), bought=bought, hearth=hearth_n, wallet=round(wallet, 1),
                         spent_total=spent_tot))
    return dict(income=income_tot, spent=spent_tot, share=spent_tot / income_tot, first_keystone=first_ks,
                first_capstone=first_cap, hearth=hearth_n, wallet=wallet, rows=rows,
                spent_r10_15=None)


def xp_req(L):
    return 100 * L + 10 * L * L


def cum_xp(L):
    return sum(xp_req(k) for k in range(1, L))


def level_for(xp):
    L = 1
    while cum_xp(L + 1) <= xp:
        L += 1
    return L


def run_xp(world, death_round):
    """XP of a run that clears rounds 1..death_round-1 and dies half-way through death_round."""
    xp = sum(world.xp_round(r) for r in range(1, death_round))
    xp += world.xp_round(death_round, completed=False, kill_frac=0.5)
    return xp


def run_minutes(world, death_round, sec_per_hp, overhead_s=20.0):
    s = sum(world.hp_round(r) * sec_per_hp + overhead_s for r in range(1, death_round))
    s += 0.5 * world.hp_round(death_round) * sec_per_hp
    return s / 60.0


# --------------------------------------------------------------------------------------------
# Report
# --------------------------------------------------------------------------------------------

def fmt(x, nd=2):
    if x is None:
        return "-"
    if isinstance(x, float):
        return ("%." + str(nd) + "f") % x
    return str(x)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", help="write machine-readable results here")
    args = ap.parse_args()
    out = {}
    P = print

    cfg = load_config()
    world = World(cfg)
    live = load_live_weapons()

    targets = {
        "StdR1": world.hp(0, 1), "StdR10": world.hp(0, 10), "GuardR10": world.hp(2, 10), "ExecR10": world.hp(3, 10),
    }
    P("== Targets (PrototypeConfig.asset) ==")
    for k, v in targets.items():
        P("  %-9s %.1f HP" % (k, v))
    doc_t = {"StdR1": 34, "StdR10": 70.7, "GuardR10": 155.6, "ExecR10": 353.6}
    out["targets"] = targets

    # ---------------- 1. Arsenal 3c recompute ----------------
    P("\n== 1a. WP-069 §1 3c recomputed (doc value in brackets when different) ==")
    P("slot   DPS    StdR1 StdR10 G-body G-head ExecR10 G-TTK  mag-dmg  sustainedDPS  run-dmg  reloadE")
    mism = []
    rows3c = {}
    for k in ORDER:
        w = W[k]
        dps = burst_dps(w) if k != "rl_1" else (w["dmg"] + w["blast"]["dmg"]) * w["rpm"] / 60.0
        s1 = shots_to_kill(w, targets["StdR1"])
        s10 = shots_to_kill(w, targets["StdR10"])
        gb, gtt = ttk(w, targets["GuardR10"])
        gh = shots_to_kill(w, targets["GuardR10"], head=True)
        ex = shots_to_kill(w, targets["ExecR10"])
        magd = shot_damage(w) * w["mag"] + (w["blast"]["dmg"] * w["mag"] if "blast" in w else 0)
        sus = sustained_dps(w)
        rund = (shot_damage(w) + (w["blast"]["dmg"] if "blast" in w else 0)) * (w["mag"] + w["res"])
        mine = (round(dps), s1, s10, gb, gh, ex, round(gtt, 2) if k != "rl_1" else None, magd)
        doc = DOC_3C[k]
        cells = []
        for i, (a, b) in enumerate(zip(mine, doc)):
            same = (a == b) if not isinstance(a, float) else abs(a - b) < 0.015
            if i == 0:
                same = abs(a - b) <= 1
            cells.append(fmt(a) if same else "%s[%s]" % (fmt(a), fmt(b)))
            if not same:
                mism.append((k, ["DPS", "StdR1", "StdR10", "GuardBody", "GuardHead", "ExecR10", "GuardTTK", "MagDmg"][i], a, b))
        rows3c[k] = dict(dps=dps, std_r1=s1, std_r10=s10, guard_body=gb, guard_head=gh, exec_r10=ex, guard_ttk=gtt,
                         mag_dmg=magd, sustained=sus, run_damage=rund, reload_empty=reload_empty(w))
        P("%-6s %s  %6.1f  %6d  %5.2f" % (k, "  ".join("%-6s" % c for c in cells), sus, rund, reload_empty(w)))
    out["arsenal_3c"] = rows3c
    out["arsenal_3c_mismatch"] = mism
    P("mismatches:", mism if mism else "none")

    # rl_1 single-target DPS: doc 96 vs cycle
    rl = W["rl_1"]
    P("\nrl_1 single target: 270 dmg / cycle %.1f s = %.1f DPS (doc 3c: 96 = 270/2.8, ignores the 0.2 s shot)"
      % (60.0 / rl["rpm"], 270 * rl["rpm"] / 60.0))
    r_kill = rl["blast"]["outer"] - (rl["blast"]["outer"] - rl["blast"]["inner"]) * targets["StdR10"] / rl["blast"]["dmg"]
    P("rl_1 blast kill radius vs StdR10: %.2f m (doc: '5 m çapta' = 2.5 m radius); vs GuardR10 unarmoured: %.2f m"
      % (r_kill, rl["blast"]["outer"] - 3.2 * targets["GuardR10"] / 160))
    P("rl_1 blast table: " + ", ".join("%.1f m->%.0f" % (d, blast_at(rl, d)) for d in (2.5, 3.0, 3.5, 4.0)))
    for r in (10, 11, 12, 15):
        P("  Guard R%d HP %.1f -> blast-only kill (160) %s" % (r, world.hp(2, r), world.hp(2, r) <= 160))

    # lmg_1 'one magazine = 7 Guards at R10'
    g_per_mag = W["lmg_1"]["mag"] // shots_to_kill(W["lmg_1"], targets["GuardR10"])
    P("lmg_1 Guards R10 per 100-round box: %d (doc 3c 'tek şarjörle R10'da 7 Guard': 1100/155.6 = %.2f ignores overkill)"
      % (g_per_mag, 1100 / targets["GuardR10"]))

    # sr_1 penetration line
    P("\n== 1b. Penetration lines ==")
    for k in ("sr_1", "lmg_2", "ar_1"):
        w = W[k]
        dm = [w["dmg"] * (1 - w["loss"]) ** i for i in range(w["pen"] + 1)]
        P("  %-5s per body (pen = extra bodies): %s | line DPS %.1f ; if pen = total bodies: %.1f (doc sr_1: 176 = 44x4)"
          % (k, ", ".join("%.1f" % d for d in dm), sum(dm) * (w["rpm"] / 60.0), sum(dm[:max(1, w["pen"])]) * (w["rpm"] / 60.0)))
        hd = [d * 2 * w["head"] for d in dm]
        P("        headshot line R10 Std (70.7) kills %d ; R15 Std (%.1f) body kills %d, head kills %d"
          % (sum(1 for d in hd if d >= targets["StdR10"]), world.hp(0, 15), sum(1 for d in dm if d >= world.hp(0, 15)),
             sum(1 for d in hd if d >= world.hp(0, 15))))
        P("        R1 Standards one-shot on the line: %d ; R10: %d"
          % (sum(1 for d in dm if d >= targets["StdR1"]), sum(1 for d in dm if d >= targets["StdR10"])))

    # ---------------- armour ----------------
    P("\n== 1c. Armour (surplus discarded, armorDamageMultiplier on the plate) ==")
    dur = world.armor
    P("slot   bodyPlate38 helm22 (projectiles) | Guard R10 body-plated: shots, TTK s | vs unarmoured")
    armor_rows = {}
    for k in ORDER:
        w = W[k]
        if k == "rl_1":
            continue
        pb = math.ceil(dur["BodyDurability"] / (w["dmg"] * w["arm"]) - 1e-9)
        ph = math.ceil(dur["HelmetDurability"] / (w["dmg"] * w["arm"]) - 1e-9)
        n = plate_then_health_shots(w, dur["BodyDurability"], targets["GuardR10"])
        t = time_to_n_shots(w, n)
        armor_rows[k] = dict(body_plate=pb, helm_plate=ph, guard_armoured_shots=n, guard_armoured_ttk=t)
        P("%-6s %4d        %4d   | %3d shots  %5.2f s           | %5.2f s" % (k, pb, ph, n, t, rows3c[k]["guard_ttk"]))
    out["armour"] = armor_rows
    for slot in "HBL":
        P("  Guard R10 armour chance %s = %.3f" % (slot, world.armor_chance(2, slot, 10)))
    rk = {r: rocket_vs_guard(world, r, W["rl_1"]) for r in (5, 8, 10, 12, 15)}
    out["rocket_vs_guard"] = rk
    P("  rl_1 one rocket (direct upper + full blast) kills a Guard: " +
      ", ".join("R%d %.0f%%" % (r, 100 * v) for r, v in rk.items()) +
      "  (doc §5: 'R10 Guard: 160-88 = 72 kalır, doğrudan isabet şart' -> code REQ-088: fully plated = 0 health)")

    # ---------------- shotguns: pellets on target ----------------
    P("\n== 1d. Shotgun pellets on one Standard, top-down hip & focus; radial cone vs planar (yaw-only) spread ==")
    sg_rows = {}
    for wname, hw in HIT_HALF_WIDTH.items():
        P("  target half-width %.2f m (%s)" % (hw, wname))
        for k in ("sg_1", "sg_2"):
            w = W[k]
            for d in (2, 3, 4, 6, 8):
                vals = []
                for focus in (False, True):
                    for planar in (False, True):
                        vals.append(pellets_on_target(w, d, hw, focus, planar))
                dmg = [v * w["dmg"] * falloff(w, d) for v in vals]
                sg_rows["%s@%d/%s" % (k, d, wname)] = dict(pellets=vals, dmg=dmg)
                P("    %s %2d m  pellets hip %.1f/%.1f  focus %.1f/%.1f -> dmg %s  one-shot StdR10(70.7): %s"
                  % (k, d, vals[0], vals[1], vals[2], vals[3], "/".join("%.0f" % x for x in dmg),
                     "/".join("Y" if x >= targets["StdR10"] else "n" for x in dmg)))
    out["shotgun_geometry"] = sg_rows

    # ---------------- falloff at range ----------------
    P("\n== 1e. Shots to kill StdR10 by distance (body, falloff) ==")
    P("slot    " + "".join("%6dm" % d for d in (5, 10, 15, 20, 30, 40)))
    for k in ORDER:
        if k in ("sg_1", "sg_2", "rl_1"):
            continue
        w = W[k]
        cells = []
        for d in (5, 10, 15, 20, 30, 40):
            cells.append("%7s" % ("-" if d > w["maxr"] else shots_to_kill(w, targets["StdR10"], dist=d)))
        P("%-6s %s" % (k, "".join(cells)))

    # ---------------- ammo per run ----------------
    P("\n== 1f. Finite reserve (doc 'yedek (toplam)', contract: 'koşu başı taşınan') vs HP to clear ==")
    cum = 0.0
    cum_hp = {}
    for r in range(1, 16):
        cum += world.hp_round(r)
        cum_hp[r] = cum
    P("  cumulative enemy HP+armour R1..R5 %.0f, R1..R10 %.0f, R1..R15 %.0f" % (cum_hp[5], cum_hp[10], cum_hp[15]))
    ammo_rows = {}
    for k in ORDER:
        rund = rows3c[k]["run_damage"]
        dry = next((r for r in range(1, 16) if cum_hp[r] > rund), None)
        ammo_rows[k] = dict(run_damage=rund, dry_round_at_100pct=dry)
        P("  %-6s full load %6.0f dmg -> runs dry in R%s even at 100%% accuracy / no overkill" % (k, rund, dry))
    out["ammo"] = ammo_rows

    # ---------------- dominance ----------------
    P("\n== 1g. Pair dominance (same class) ==")
    def prof(k):
        r = rows3c[k]
        return dict(dps=r["dps"], sus=r["sustained"], g_ttk=r["guard_ttk"], s10=r["std_r10"], ex=r["exec_r10"],
                    run=r["run_damage"], arm=W[k]["arm"], full=W[k]["full"], move=W[k]["move"],
                    g_arm_ttk=armor_rows.get(k, {}).get("guard_armoured_ttk"))
    dom = {}
    for a, b in (("ar_1", "ar_2"), ("smg_1", "smg_2"), ("sg_1", "sg_2"), ("lmg_1", "lmg_2")):
        pa, pb = prof(a), prof(b)
        better = [m for m in ("dps", "sus", "run", "arm", "full", "move") if pb[m] > pa[m] + 1e-9] + \
                 [m for m in ("g_ttk", "s10", "ex", "g_arm_ttk") if pb[m] is not None and pb[m] < pa[m] - 1e-9]
        worse = [m for m in ("dps", "sus", "run", "arm", "full", "move") if pb[m] < pa[m] - 1e-9] + \
                [m for m in ("g_ttk", "s10", "ex", "g_arm_ttk") if pb[m] is not None and pb[m] > pa[m] + 1e-9]
        dom[a + "/" + b] = dict(b_better=better, b_worse=worse, a=pa, b=pb)
        P("  %s -> %s better on %s ; worse on %s" % (a, b, better, worse))
    out["dominance"] = dom

    # overlap: guard TTK and DPS within 6 %
    P("\n== 1h. Role overlap (unarmoured DPS within 6 % and Guard R10 TTK within 6 %) ==")
    ov = []
    for i, a in enumerate(ORDER):
        for b in ORDER[i + 1:]:
            ra, rb = rows3c[a], rows3c[b]
            if a == "rl_1" or b == "rl_1":
                continue
            if abs(ra["dps"] - rb["dps"]) / max(ra["dps"], rb["dps"]) <= 0.06 and \
                    abs(ra["guard_ttk"] - rb["guard_ttk"]) / max(ra["guard_ttk"], rb["guard_ttk"]) <= 0.06:
                ov.append((a, b, round(ra["dps"]), round(rb["dps"]), round(ra["guard_ttk"], 2), round(rb["guard_ttk"], 2)))
    for o in ov:
        P("  %s vs %s  DPS %d/%d  Guard TTK %.2f/%.2f" % o)
    out["overlap"] = ov

    # ---------------- progression curve claims ----------------
    P("\n== 1i. §1.3 curve: 'erken (R1-5) her şey Standard'ı 1-5 mermide düşürür' ==")
    curve = {}
    for k in ORDER:
        s = [shots_to_kill(W[k], world.hp(0, r)) for r in range(1, 6)]
        curve[k] = s
        flag = "" if max(s) <= 5 else "  <-- violates"
        P("  %-6s shots R1..R5: %s%s" % (k, s, flag))
    out["curve_early"] = curve
    P("  ActiveCap %d (doc §1.3 says '40 zombi tavanı'; 40 = AbsoluteSafetyCeiling), cap reached at R%d"
      % (world.round["ActiveCap"], next(r for r in range(1, 30) if world.active(r) >= world.round["ActiveCap"])))
    P("  Executioner IntroductionRound R%d (doc: 'geç (R11+) Executioner')" % world.tiers[3]["IntroductionRound"])
    for r in (7, 8, 9, 10, 11, 12, 14, 15):
        P("  R%-2d Standard body-armour chance %.3f (exact-round overrides R8/11/14 only)" % (r, world.armor_chance(0, "B", r)))

    # ---------------- live data cross-check ----------------
    P("\n== 1j. Live WeaponDefinition assets vs v3 design ==")
    live_rows = {}
    for wid, mb in live.items():
        f, a, p = mb["fire"], mb["ammo"], mb["projectile"]
        d = W.get(wid)
        if not d:
            continue
        pairs = [("damage", f["damage"], d["dmg"]), ("pellets", f["pelletCount"], d["pel"]), ("rpm", f["rpm"], d["rpm"]),
                 ("magazine", a["magazine"], d["mag"]), ("reserve", a["reserve"], d["res"]),
                 ("reloadTac", a["reloadTacSeconds"], reload_tac(d)), ("reloadEmpty", a["reloadEmptySeconds"], reload_empty(d)),
                 ("speed", p["speed"], None), ("maxRange", p["maxRange"], d["maxr"]), ("falloffStart", p["falloffStart"], d["full"]),
                 ("falloffMinShare", p["falloffMinShare"], d["minpct"]), ("penetration", p["penetration"], d["pen"]),
                 ("armorMult", p["armorDamageMultiplier"], d["arm"]), ("stagger", p["stagger"], d["stag"])]
        diffs = [(n, lv, dv) for n, lv, dv in pairs if dv is not None and abs(float(lv) - float(dv)) > 1e-6]
        live_rows[wid] = diffs
        P("  WD_%s.asset: %d field(s) differ: %s" % (wid, len(diffs), ", ".join("%s %s->%s" % x for x in diffs)))
        P("     live fireMode=%s (0 Single,1 Burst,2 Auto) — sg_1 live mode %s" % (f["fireMode"], f["fireMode"]))
    out["live_diff"] = live_rows

    # ---------------- 2. Specials ----------------
    P("\n== 2. Specials: round sizes and kill potential per activation ==")
    rng = random.Random(SEED)
    P("R   pool active Exec  | Raiju chain(active, swarm 8/12 m) | Raiju kills/act (12 m) | Namekuji kills/act | killable")
    sp = {}
    for r in (4, 6, 8, 10, 12, 15):
        a = world.active(r)
        c8 = chain_len(a, 8.0, RAIJU["hop"], RAIJU["chain"], rng, trials=800)
        c12 = chain_len(a, 12.0, RAIJU["hop"], RAIJU["chain"], rng, trials=800)
        ra = raiju_activation(world, r, 12.0, rng)
        na = namekuji_activation(world, r)
        sp[r] = dict(pool=world.pool(r), active=a, exec=world.mix(r)[3], chain8=c8, chain12=c12, raiju=ra, namekuji=na)
        P("R%-2d %4d %5d %5.1f | %4.1f / %4.1f                      | %5.1f (upper %d)       | %5.1f              | %5.1f"
          % (r, world.pool(r), a, world.mix(r)[3], c8, c12, ra["kills"], ra["upper"], na["kills"], ra["killable"]))
    out["specials"] = sp
    shots = RAIJU["mag"] + RAIJU["res"]
    t_raiju = shots * (RAIJU["charge"] + RAIJU["cycle"]) + (shots // RAIJU["mag"] - 1) * RAIJU["re"]
    t_gel = (NAMEKUJI["mag"] + NAMEKUJI["res"]) * NAMEKUJI["cycle"] + 2 * NAMEKUJI["re"]
    P("  Raiju: 12 orbs need %.1f s of the 40 s cap; Namekuji: 24 blobs need %.1f s of the 45 s cap" % (t_raiju, t_gel))
    sus_ar1 = rows3c["ar_1"]["sustained"]
    for r in (8, 10, 15):
        P("  classic comparison R%d: ar_1 sustained %.1f DPS x 40 s at 60%% hit = %.0f Standard kills"
          % (r, sus_ar1, sus_ar1 * 40 * 0.6 / world.hp(0, r)))
    specials_per_run = sum(min(world.special["MaxSpawnChance"], world.special["BaseSpawnChance"] +
                                world.special["SpawnChanceGrowthPerRound"] * (r - world.special["FirstEligibleRound"]))
                           for r in range(world.special["FirstEligibleRound"], 16))
    P("  expected Special pickups R4..R15 (first spawn only): %.2f" % specials_per_run)
    pool_tot = sum(world.pool(r) for r in range(1, 16))
    exp_r = exp_n = 0.0
    for r in range(world.special["FirstEligibleRound"], 16):
        pch = min(world.special["MaxSpawnChance"], world.special["BaseSpawnChance"] +
                  world.special["SpawnChanceGrowthPerRound"] * (r - world.special["FirstEligibleRound"]))
        exp_r += pch * 0.5 * raiju_activation(world, r, 12.0, rng)["kills"]
        exp_n += pch * 0.5 * namekuji_activation(world, r)["kills"]
    P("  expected Special kills in an R1..R15 run (50/50 type): %.0f of %d pool (%.0f%%) — Raiju-only %.0f%%, Namekuji-only %.0f%%"
      % (exp_r + exp_n, pool_tot, 100 * (exp_r + exp_n) / pool_tot, 200 * exp_r / pool_tot, 200 * exp_n / pool_tot))
    out["special_run_share"] = dict(raiju=exp_r, namekuji=exp_n, pool=pool_tot)

    # ---------------- 3. Progression ----------------
    P("\n== 3a. Gold income per round (expected value, pickup 1.0) ==")
    inc = {r: world.gold(r) for r in range(1, 21)}
    cum = 0
    for r in range(1, 16):
        cum += inc[r]
        P("  R%-2d pool %3d  mix %s  gold %6.1f  cum %7.1f   XP %6.1f" %
          (r, world.pool(r), "/".join("%.1f" % x for x in world.mix(r)), inc[r], cum, world.xp_round(r)))
    out["income"] = inc
    P("  R1..R15 income %.0f (WP-077 §4.2: 6 573)" % sum(inc[r] for r in range(1, 16)))

    # tree totals
    tiers_w = sum(sum(branch_costs(world, n)) for n in ("Fire Rate", "Reload Speed", "Piercing"))
    tiers_b = sum(sum(branch_costs(world, n)) for n in ("Movement Speed", "Maximum Health", "Regeneration Delay", "Regeneration Rate"))
    weapon_lane = tiers_w + 3 * 110 + 90 + 150 + 380 + 80 + 160
    body_lane = tiers_b + 4 * 140 + 100 + 60 + 320
    hearth_table = sum(hearth_price(n, "table") for n in range(1, 21))
    hearth_text = sum(hearth_price(n, "text") for n in range(1, 21))
    P("\n== 3b. Tree totals (ar_1) == weapon lane %d, body lane %d, Hearth x20 %d (table) / %d (50+30n as written)"
      % (weapon_lane, body_lane, hearth_table, hearth_text))
    P("  Hearth n=15 cumulative %d ; n=20 price %d (table) vs %d (text)" %
      (sum(hearth_price(n) for n in range(1, 16)), hearth_price(20), hearth_price(20, "text")))
    out["tree_totals"] = dict(weapon_lane=weapon_lane, body_lane=body_lane, hearth_table=hearth_table, hearth_text=hearth_text)

    bot = simulate_tree(world, pickup=1.0, cards_per_round=(2,), reserve_share=0.0)
    med = simulate_tree(world, pickup=0.9, cards_per_round=(1, 2), reserve_share=0.30, hearth_per_round=1,
                        hearth_min_wallet_after=150)
    for name, s in (("BOT (upper bound)", bot), ("MEDIAN", med)):
        inc10_15 = sum(x["income"] for x in s["rows"][9:15])
        sp10_15 = s["rows"][14]["spent_total"] - s["rows"][8]["spent_total"]
        P("  %s: R10..R15 spend share %.1f%%" % (name, 100 * sp10_15 / inc10_15))
        P("\n  %s: income %.0f spent %.0f share %.1f%%  first keystone R%s  first capstone R%s  Hearth %d  wallet R15 %.0f"
          % (name, s["income"], s["spent"], 100 * s["share"], s["first_keystone"], s["first_capstone"], s["hearth"], s["wallet"]))
        for row in s["rows"]:
            P("     R%-2d +%6.1f  %-40s hearth %2d  wallet %6.1f" % (row["round"], row["income"], ",".join(row["bought"]),
                                                                    row["hearth"], row["wallet"]))
    out["tree_bot"] = bot
    out["tree_median"] = med

    # sensitivity of the human model
    P("\n  MEDIAN sensitivity (gold kept in reserve / Hearth stokes per round) -> spend share, wallet R15, first KS/cap:")
    sens = {}
    for rs_ in (0.0, 150.0, 300.0):
        for hp_ in (1, 2, 3):
            m = simulate_tree(world, pickup=0.9, cards_per_round=(1, 2), reserve_abs=rs_, hearth_per_round=hp_)
            sens["%.0f/%d" % (rs_, hp_)] = (m["share"], m["wallet"], m["first_keystone"], m["first_capstone"])
            P("    reserve %4.0f hearth<=%d : %.1f%%  wallet %5.0f  KS R%s  cap R%s" % (rs_, hp_, 100 * m["share"], m["wallet"],
                                                                                m["first_keystone"], m["first_capstone"]))
    out["median_sensitivity"] = sens
    for mode in ("text",):
        b2 = simulate_tree(world, hearth_mode=mode)
        P("\n  BOT with Hearth price 50+30n (as §2.1 text): Hearth %d, share %.1f%%, wallet %.0f" % (b2["hearth"], 100 * b2["share"], b2["wallet"]))

    # ---------------- 3c. XP / hours ----------------
    P("\n== 3c. XP curve 100L+10L^2 and hours ==")
    for L in (5, 10, 15, 20, 30, 40, 50):
        P("  L%-2d cumulative %7d  Req(L-1->L) %6d" % (L, cum_xp(L), xp_req(L - 1)))
    xp15 = run_xp(world, 16)
    xp8 = run_xp(world, 9)
    P("  run XP: clears R1..R15 %.0f (doc 2 900) ; clears R1..R8 %.0f (doc 790)" %
      (sum(world.xp_round(r) for r in range(1, 16)), sum(world.xp_round(r) for r in range(1, 9))))
    hp15 = sum(world.hp_round(r) for r in range(1, 16))
    sec_per_hp = (28 * 60 - 15 * 20.0) / hp15   # doc: R15 run ~28 min, 20 s shop/countdown overhead per round
    P("  time model calibrated to doc 'R15 run ~28 min': %.4f s per HP (effective team DPS %.1f), 20 s/round overhead"
      % (sec_per_hp, 1 / sec_per_hp))
    hours = {}
    alt = {}
    for eff in (60.0, 42.0):
        sph = 1.0 / eff
        xph = run_xp(world, 16) / run_minutes(world, 16, sph) * 60
        alt[eff] = dict(r15_minutes=run_minutes(world, 16, sph), xp_per_h=xph, h_L20=cum_xp(20) / xph, h_L30=cum_xp(30) / xph)
        P("  alt. time model: effective DPS %.0f -> R15 run %.1f min, %.0f XP/h, L20 %.1f h, L30 %.1f h"
          % (eff, alt[eff]["r15_minutes"], xph, alt[eff]["h_L20"], alt[eff]["h_L30"]))
    P("  doc 4 500 XP/h -> L10 %.1f h, L20 %.1f h, L30 %.1f h; Kul/h 112 -> Ring1 %.1f h, Ring1+2 %.1f h"
      % (cum_xp(10) / 4500, cum_xp(20) / 4500, cum_xp(30) / 4500, 330 / 112.5, 2330 / 112.5))
    out["hours_alt"] = alt
    for label, death in (("bot R15 (clears 15, dies R16)", 16), ("median dies R9", 9), ("median dies R11", 11),
                         ("new player dies R6", 6)):
        xp = run_xp(world, death)
        mins = run_minutes(world, death, sec_per_hp)
        xph = xp / mins * 60
        kul_h = math.floor(xp / 40) / mins * 60
        h20 = cum_xp(20) / xph
        h30 = cum_xp(30) / xph
        hours[label] = dict(xp=xp, minutes=mins, xp_per_h=xph, kul_per_h=kul_h, h_L10=cum_xp(10) / xph, h_L20=h20, h_L30=h30)
        P("  %-30s run XP %6.0f  %5.1f min  %6.0f XP/h  %5.1f Kul/h  L10 %.1f h  L20 %.1f h  L30 %.1f h"
          % (label, xp, mins, xph, kul_h, cum_xp(10) / xph, h20, h30))
    out["hours"] = hours
    # Kul rings
    ring1, ring2, ring3 = 330, 2000, 3900
    for label, h in hours.items():
        kh = h["kul_per_h"]
        P("  %-30s Ring1 (330 Kul) %.1f h, Ring1+2 (2 330) %.1f h, full (7 300 incl. ~700 one-off/ach.) %.1f h"
          % (label, ring1 / kh, (ring1 + ring2) / kh, (ring1 + ring2 + ring3 - 700) / kh))


    # ---------------- suggested values (report recommendations), recomputed ----------------
    P("\n== 4. Suggested values re-checked ==")
    SUG = {
        "ar_2": dict(dmg=14, head=1.30),
        "smg_2": dict(dmg=12, arm=0.8),
        "sg_1": dict(cone=(6.0, 4.5), pattern=("rings", [(1, 0.0), (3, 0.25), (4, 0.55)]), pershell=(0.40, 0.45, 0.35, 0.5)),
        "sg_2": dict(pel=9, full=5, rt=2.4, re=2.4),
        "lmg_1": dict(rt=3.6, re=4.2, move=0.88),
        "lmg_2": dict(rpm=360),
        "sr_1": dict(dmg=58, loss=0.15),
    }
    import copy
    W2 = copy.deepcopy(W)
    for k, v in SUG.items():
        W2[k].update(v)
    sug = {}
    for k in ORDER:
        w = W2[k]
        if k == "rl_1":
            continue
        gb, gtt = ttk(w, targets["GuardR10"])
        gh, ghtt = ttk(w, targets["GuardR10"], head=True)
        sug[k] = dict(dps=burst_dps(w), sus=sustained_dps(w), g_ttk=gtt, g_head_ttk=ghtt,
                      g_arm_ttk=time_to_n_shots(w, plate_then_health_shots(w, dur["BodyDurability"], targets["GuardR10"])),
                      run=shot_damage(w) * (w["mag"] + w["res"]))
        P("  %-6s DPS %5.1f  sustained %5.1f  Guard TTK body %.2f head %.2f plated %.2f  run dmg %5.0f%s"
          % (k, sug[k]["dps"], sug[k]["sus"], gtt, ghtt, sug[k]["g_arm_ttk"], sug[k]["run"], "  *" if k in SUG else ""))
    for hw in (0.26,):
        for d in (2, 3, 4, 5, 6):
            a = pellets_on_target(W2["sg_1"], d, hw); b = pellets_on_target(W2["sg_2"], d, hw)
            P("  sg_1* %d m pellets %.1f (%.0f dmg)   sg_2* %d m pellets %.1f (%.0f dmg)"
              % (d, a, a * W2["sg_1"]["dmg"] * falloff(W2["sg_1"], d), d, b, b * W2["sg_2"]["dmg"] * falloff(W2["sg_2"], d)))
    w = W2["sr_1"]
    dm = [w["dmg"] * (1 - w["loss"]) ** i for i in range(4)]
    P("  sr_1* line (4 bodies): %s ; R1 body kills %d ; R10 head kills %d"
      % (", ".join("%.1f" % x for x in dm), sum(1 for x in dm if x >= targets["StdR1"]),
         sum(1 for x in dm if 2 * w["head"] * x >= targets["StdR10"])))
    # XP curve alternative under the doc's own 28-min anchor
    xph = hours["bot R15 (clears 15, dies R16)"]["xp_per_h"]
    for a_, b_ in ((100, 10), (110, 12), (120, 12)):
        c20 = sum(a_ * k + b_ * k * k for k in range(1, 20)); c30 = sum(a_ * k + b_ * k * k for k in range(1, 30))
        P("  XP curve %dL+%dL^2: L20 %6d XP = %.1f h, L30 %6d XP = %.1f h (at %.0f XP/h)" % (a_, b_, c20, c20 / xph, c30, c30 / xph, xph))
    out["suggested"] = sug

    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(out, f, indent=1, default=str)
    return 0


if __name__ == "__main__":
    sys.exit(main())
