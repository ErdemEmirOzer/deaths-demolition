#!/usr/bin/env python3
"""WP-077 B5 (S-04): the DEC-098 tree simulator (EconomySimulator.TreeModel) as stdlib Python — no Unity.

A line-for-line port of `Diagnostics/Editor/EconomySimulator.TreeModel.cs` on top of the shipped tree rules of
`Rounds/RunTree/RunTreeState.cs` + `RunTreeGraph.cs` (K1 hybrid draw, luck guard, fork pairs, Nişan path and the
RT6 pin rules CanPin / CanReach, K6 capstone gate, Hearth, Purse, Special links), with .NET `System.Random` and the
float32 draw arithmetic, so the same seeds give the same runs as the C# model. Round income is the WP-041 round
model's seed-1 gold (EconomySimulator); it is read from a `dd-balance-v1` report (R1–R15, pickup 1.00) and scaled by
the pickup fraction; R16–R20 (only the pool check and the Hearth cap round read them) extend it with the r1 audit's
expected gold, scaled to the report's R11–R15 level.

    python3 tools/balance/tree_model.py --balance <balance-v1.json> [--m1] [--late-cards 3] [--hearth-cap 30]
        [--link-price catalog] [--capstone-cost 300] [--pickup 0.93] [--csv out.csv] [--json out.json]

Options that are not in the C# model are marked WHAT-IF in the output header.
"""
import argparse
import json
import math
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(__file__))
import r1_audit_run2 as base  # noqa: E402

ROOT = base.ROOT
PROJ = base.PROJ
TREE_JSON = os.path.join(PROJ, "Rounds", "RunTree", "Resources", "ProgressionTree", "run-tree.json")

# UpgradeBranch enum (Core/Config) and the ClassicWeaponId values the C# seed formula uses.
BRANCHES = ["Piercing", "Ricochet", "Explosion", "Chain", "FireRate", "ReloadSpeed", "MovementSpeed",
            "MaximumHealth", "RegenerationDelay", "RegenerationRate", "ArmorBreak", "Impact", "Cluster"]
BODY = ["MovementSpeed", "MaximumHealth", "RegenerationDelay", "RegenerationRate"]   # WeaponBranchContract.SharedPlayerBranches
WEAPONS = [("AssaultRifle", 0, "ar_1"), ("AutoSmg", 1, "smg_1"), ("CombatShotgun", 2, "sg_1"), ("Sr1", 8, "sr_1")]
BOTS = ["SpendMax", "Rush", "Human", "Hoarder"]

TIER, KEYSTONE, FORK, SEAL, CAPSTONE, HEARTH, PURSE, LINK = range(8)
WEAPON_LANE, BODY_LANE = 0, 1
AVAILABLE, UNAFFORDABLE, PREREQ, PURCHASED, MAXTIER, EXCLUDED, LOCKED = range(7)
PITY_ROUNDS = 3
INT_MAX = 2 ** 31 - 1


def f32(x):
    """Round to IEEE float32, as C# float arithmetic does."""
    return struct.unpack("f", struct.pack("f", x))[0]


def round_half_even(x):
    return int(round(x))   # Python round() and Mathf.RoundToInt both round half to even


class NetRandom:
    """System.Random(int seed) — the .NET Framework / Mono subtractive generator (Knuth)."""
    MBIG, MSEED = INT_MAX, 161803398

    def __init__(self, seed):
        self.seed_array = [0] * 56
        subtraction = INT_MAX if seed == -2 ** 31 else abs(seed)
        mj = self.MSEED - subtraction
        self.seed_array[55] = mj
        mk = 1
        for i in range(1, 55):
            ii = (21 * i) % 55
            self.seed_array[ii] = mk
            mk = mj - mk
            if mk < 0:
                mk += self.MBIG
            mj = self.seed_array[ii]
        for _ in range(1, 5):
            for i in range(1, 56):
                self.seed_array[i] -= self.seed_array[1 + (i + 30) % 55]
                self.seed_array[i] = _wrap32(self.seed_array[i])
                if self.seed_array[i] < 0:
                    self.seed_array[i] += self.MBIG
        self.inext, self.inextp = 0, 21

    def _sample(self):
        self.inext = 1 if self.inext + 1 >= 56 else self.inext + 1
        self.inextp = 1 if self.inextp + 1 >= 56 else self.inextp + 1
        ret = _wrap32(self.seed_array[self.inext] - self.seed_array[self.inextp])
        if ret == self.MBIG:
            ret -= 1
        if ret < 0:
            ret += self.MBIG
        self.seed_array[self.inext] = ret
        return ret * (1.0 / self.MBIG)

    def next_value(self):   # SeededUpgradeOfferRandom.NextValue: (float)NextDouble()
        return f32(self._sample())


def _wrap32(v):
    v &= 0xFFFFFFFF
    return v - 0x100000000 if v >= 0x80000000 else v


# ---------------------------------------------------------------------------------------------------- data
class Node:
    __slots__ = ("id", "kind", "lane", "branch", "parents", "flat", "weight", "value", "excl", "max_tier",
                 "step", "per_round", "link_level")

    def __init__(self, **kw):
        self.parents, self.flat, self.weight, self.value, self.excl = [], 0, 1.0, 0.0, 0
        self.max_tier, self.step, self.per_round, self.link_level, self.branch = 1, 0, 0, 0, None
        for k, v in kw.items():
            setattr(self, k, v)


def load_economy():
    cfg = base.load_config()
    econ = cfg["_economy"]
    branches = {}
    for b in econ["Branches"]:
        branches[BRANCHES[int(b["Branch"])]] = b
    return cfg, econ, branches


def core_of(slot):
    mb = base.load_live_weapons()[slot]
    cb = str(mb["upgrade"]["coreBranches"])
    return [BRANCHES[int(cb[i:i + 8][:2], 16)] for i in range(0, len(cb), 8)]


def build_graph(slot, cat, branches, opts):
    """RunTreeGraph.Build — node order matters (fork pair = lower index, draw order)."""
    g, index = [], {}

    def push(n):
        if n.id in index:
            return index[n.id]
        index[n.id] = len(g)
        g.append(n)
        return len(g) - 1

    def add(c, lane, kind, excl=0):
        return push(Node(id=c["id"], kind=kind, lane=lane, flat=int(c["cost"]), weight=float(c.get("offerWeight", 1.0)),
                         value=float(c.get("effectValue", 0.0)), excl=excl))

    def add_tier(lane, br):
        s = branches[br]
        return push(Node(id="tier_" + br, kind=TIER, lane=lane, branch=br, max_tier=max(1, int(s["MaxTier"])),
                         weight=float(s["OfferWeight"])))

    ks_cat = {k["branch"]: k for k in cat["branchKeystones"]}

    def add_keystone(lane, br, tier):
        k = ks_cat.get(br)
        if k is None:
            return -1
        i = add(k, lane, KEYSTONE)
        g[i].branch, g[i].parents = br, [tier]
        return i

    info = {}
    for br in core_of(slot):
        t = add_tier(WEAPON_LANE, br)
        add_keystone(WEAPON_LANE, br, t)
    w = next(x for x in cat["weapons"] if x["slot"] == slot)
    add(w["forkX"], WEAPON_LANE, FORK, 1)
    add(w["forkY"], WEAPON_LANE, FORK, 1)
    info["seal"] = add(w["seal"], WEAPON_LANE, SEAL)
    info["cap"] = add(w["capstone"], WEAPON_LANE, CAPSTONE)
    if opts.capstone_cost:
        g[info["cap"]].flat = opts.capstone_cost
    h = cat["hearth"]
    info["hearth"] = add(h, WEAPON_LANE, HEARTH)
    g[info["hearth"]].step = max(0, int(h["costStep"]))
    g[info["hearth"]].max_tier = max(1, opts.hearth_cap or int(h["maxStacks"]))
    g[info["hearth"]].per_round = max(1, int(h["perRound"]))
    info["link1"] = push(Node(id="link_1", kind=LINK, lane=WEAPON_LANE, link_level=1))
    info["link2"] = push(Node(id="link_2", kind=LINK, lane=WEAPON_LANE, link_level=2, parents=[info["link1"]]))
    for br in BODY:
        t = add_tier(BODY_LANE, br)
        add_keystone(BODY_LANE, br, t)
    add(cat["bodyForks"][0], BODY_LANE, FORK, 2)
    add(cat["bodyForks"][1], BODY_LANE, FORK, 2)
    info["purse"] = add(cat["purse"], BODY_LANE, PURSE)
    info["bcap"] = add(cat["bodyCapstone"], BODY_LANE, CAPSTONE)
    info["links"] = {l["id"]: l for l in cat["specialLinks"]}
    return g, info


# ---------------------------------------------------------------------------------------------------- tree state
class Tree:
    def __init__(self, g, info, branches, econ, opts):
        self.g, self.info, self.branches, self.econ, self.opts = g, info, branches, econ, opts
        self.owned = [False] * len(g)
        self.unseen = [0] * len(g)
        self.tiers = {}                     # the build (WeaponModifierState tiers)
        self.hearth = self.hearth_round = 0
        self.linked = ""
        self.pinned = -1
        self.earned_mark = 0
        self.m1 = opts.m1
        self.pin_changes, self.pin_round = 0, -1

    # ---- queries
    def owned_tier(self, i):
        n = self.g[i]
        if n.kind == TIER:
            return self.tiers.get(n.branch, 0)
        if n.kind == HEARTH:
            return self.hearth
        return 1 if self.owned[i] else 0

    def cost(self, i):
        n = self.g[i]
        if n.kind == TIER:
            s = self.branches[n.branch]
            owned = self.owned_tier(i)
            c = max(1, round_half_even(f32(float(s["BaseCost"]) * f32(float(s["CostGrowthPerTier"]) ** owned))))
            if owned == 0:
                lo = max(1, min(self.econ["StartingCostMin"], self.econ["StartingCostMax"]))
                hi = max(lo, max(self.econ["StartingCostMin"], self.econ["StartingCostMax"]))
                c = min(max(c, lo), hi)
            return max(1, c)
        if n.kind == HEARTH:
            return max(1, n.flat + n.step * self.hearth)
        if n.kind == LINK and self.opts.link_price == "catalog" and self.linked:
            return max(1, int(self.link_cat(n)["cost"]))
        return max(1, n.flat)

    def link_cat(self, n):
        key = {"sw_arc": "link_arc_", "sw_gel": "link_gel_"}[self.linked] + str(n.link_level)
        return self.info["links"][key]

    def excluded_by(self, i):
        n = self.g[i]
        if n.excl == 0:
            return -1
        for j, m in enumerate(self.g):
            if j != i and self.owned[j] and m.excl == n.excl:
                return j
        return -1

    def count_owned(self, lane, kind):
        return sum(1 for j, m in enumerate(self.g) if self.owned[j] and m.lane == lane and m.kind == kind)

    def cheapest_unowned(self, lane, kind, skip=None):
        best, best_cost = -1, INT_MAX
        for j, n in enumerate(self.g):
            if n.lane != lane or n.kind != kind or self.owned[j] or (skip is not None and j in skip) or self.excluded_by(j) >= 0:
                continue
            c = self.cost(j)
            if n.kind == KEYSTONE and n.parents:
                t = n.parents[0]
                have = self.owned_tier(t)
                if have < 2 and self.g[t].max_tier < 2:
                    continue
                c += (2 - have) * self.cost(t) if have < 2 else 0
            if c < best_cost:
                best, best_cost = j, c
        return best

    def prereq_met(self, i):
        n = self.g[i]
        if n.kind in (TIER, PURSE):
            return True
        if n.kind == KEYSTONE:
            return bool(n.parents) and self.owned_tier(n.parents[0]) >= 2
        if n.kind in (FORK, SEAL, HEARTH):
            return self.count_owned(n.lane, KEYSTONE) >= 1
        if n.kind == CAPSTONE:
            if n.lane == WEAPON_LANE and not self.owned[self.info["seal"]]:
                return False
            return self.count_owned(n.lane, KEYSTONE) >= 2 and self.count_owned(n.lane, FORK) >= 1
        if n.kind == LINK:
            if not self.linked:
                return False
            return not (n.link_level >= 2 and not self.owned[self.info["link1"]])
        return False

    def status(self, i, gold):
        n = self.g[i]
        if n.kind == TIER:
            if self.owned_tier(i) >= n.max_tier:
                return MAXTIER
            return AVAILABLE if self.cost(i) <= gold else UNAFFORDABLE
        if n.kind == LINK and not self.linked:
            return LOCKED
        if n.kind == HEARTH:
            if self.hearth >= n.max_tier:
                return MAXTIER
        elif self.owned[i]:
            return PURCHASED
        if self.excluded_by(i) >= 0:
            return EXCLUDED
        if not self.prereq_met(i):
            return PREREQ
        return AVAILABLE if self.cost(i) <= gold else UNAFFORDABLE

    def offerable(self, i):
        n = self.g[i]
        if n.kind == HEARTH:
            return False
        if n.kind == CAPSTONE and not self.m1 and i != self.pinned:
            return False
        return self.status(i, INT_MAX) == AVAILABLE

    # ---- pin (Nişan): RT6 CanPin / CanReach, path
    def can_pin(self, i):
        if i < 0 or i >= len(self.g) or self.g[i].kind == HEARTH:
            return False
        if self.status(i, INT_MAX) not in (AVAILABLE, UNAFFORDABLE, PREREQ):
            return False
        return self.can_reach(i, 0)

    def can_reach(self, i, depth):
        if i < 0 or i >= len(self.g) or depth > 8:
            return False
        n = self.g[i]
        if n.kind != TIER and self.owned[i]:
            return True
        s = self.status(i, INT_MAX)
        if s in (AVAILABLE, UNAFFORDABLE):
            return True
        if s != PREREQ:
            return False
        if n.kind == KEYSTONE:
            return bool(n.parents) and self.g[n.parents[0]].max_tier >= 2 and self.can_reach(n.parents[0], depth + 1)
        if n.kind in (FORK, SEAL):
            return self.reachable_count(n.lane, KEYSTONE, depth + 1) >= 1
        if n.kind == CAPSTONE:
            return ((n.lane != WEAPON_LANE or self.can_reach(self.info["seal"], depth + 1))
                    and self.reachable_count(n.lane, KEYSTONE, depth + 1) >= 2
                    and self.reachable_count(n.lane, FORK, depth + 1) >= 1)
        if n.kind == LINK:
            return n.link_level >= 2 and self.can_reach(self.info["link1"], depth + 1)
        return False

    def reachable_count(self, lane, kind, depth):
        return sum(1 for j, m in enumerate(self.g) if m.lane == lane and m.kind == kind and self.can_reach(j, depth))

    def set_pin_run_start(self, i):
        """RunTreeState.SetPin (the model pins directly, as TreeModel.Simulate does)."""
        self.pinned = i if 0 <= i < len(self.g) else -1

    def shop_set_pin(self, i, round_no):
        """RoundUpgradeShop.SetPin with the RT6 rules: one assignment per round, clear never refunds, only CanPin targets."""
        if round_no != self.pin_round:
            self.pin_round, self.pin_changes = round_no, 0
        if i >= 0 and self.opts.pin_rules and not self.can_pin(i):
            return False
        if i == self.pinned:
            return True
        if i >= 0 and self.opts.pin_rules and self.pin_changes >= 1:
            return False
        if i >= 0:
            self.pin_changes += 1
        self.pinned = i
        return True

    def path_to(self, target):
        into = []
        self._append_path(target, into, 0)
        return into

    def _append_path(self, i, into, depth):
        if depth > 8 or i in into:
            return
        n = self.g[i]
        done = False if n.kind == TIER else (self.hearth >= n.max_tier if n.kind == HEARTH else self.owned[i])
        if done:
            return
        if n.kind == KEYSTONE:
            if n.parents and self.owned_tier(n.parents[0]) < 2:
                self._append_path(n.parents[0], into, depth + 1)
        elif n.kind in (FORK, SEAL, HEARTH):
            if self.count_owned(n.lane, KEYSTONE) < 1:
                k = self.cheapest_unowned(n.lane, KEYSTONE)
                if k >= 0:
                    self._append_path(k, into, depth + 1)
        elif n.kind == CAPSTONE:
            if n.lane == WEAPON_LANE and not self.owned[self.info["seal"]]:
                self._append_path(self.info["seal"], into, depth + 1)
            need = 2 - self.count_owned(n.lane, KEYSTONE) - sum(1 for x in into if self.g[x].lane == n.lane and self.g[x].kind == KEYSTONE)
            while need > 0:
                k = self.cheapest_unowned(n.lane, KEYSTONE, into)
                if k < 0:
                    break
                self._append_path(k, into, depth + 1)
                need -= 1
            if self.count_owned(n.lane, FORK) + sum(1 for x in into if self.g[x].lane == n.lane and self.g[x].kind == FORK) < 1:
                f = self.cheapest_unowned(n.lane, FORK, into)
                if f >= 0:
                    self._append_path(f, into, depth + 1)
        elif n.kind == LINK:
            if n.link_level >= 2 and not self.owned[self.info["link1"]]:
                self._append_path(self.info["link1"], into, depth + 1)
        if i not in into:
            into.append(i)

    def pinned_offerable(self):
        if self.opts.pin_rules:
            if not self.can_pin(self.pinned):
                return -1
        elif self.pinned < 0:
            return -1
        for k in self.path_to(self.pinned):
            if self.offerable(k):
                return k
        return -1

    # ---- the K1 draw
    def fork_partner(self, i):
        n = self.g[i]
        if n.kind != FORK or n.excl == 0:
            return -1
        for j, m in enumerate(self.g):
            if j != i and m.kind == FORK and m.excl == n.excl:
                return j
        return -1

    def weight_of(self, i):
        n = self.g[i]
        w = n.weight
        if n.kind == LINK and self.opts.link_price == "catalog" and self.linked:
            w = float(self.link_cat(n).get("offerWeight", 1.0))
        w = f32(max(0.01, w))
        return f32(w * 2.0) if self.unseen[i] >= PITY_ROUNDS else w

    def draw_frontier(self, rnd, count):
        into, frontier, total = [], [], f32(0.0)
        for i in range(len(self.g)):
            if not self.offerable(i):
                continue
            p = self.fork_partner(i)
            if 0 <= p < i and self.offerable(p):
                continue
            frontier.append(i)
            total = f32(total + self.weight_of(i))
        while len(into) < count and frontier:
            r = f32(rnd.next_value() * total)
            k = 0
            while k < len(frontier) - 1:
                r = f32(r - self.weight_of(frontier[k]))
                if r < 0.0:
                    break
                k += 1
            into.append(frontier[k])
            total = f32(total - self.weight_of(frontier[k]))
            frontier.pop(k)
        pin = self.pinned_offerable()
        if pin >= 0 and not self._contains_card(into, pin):
            p = self.fork_partner(pin)
            card = p if (0 <= p < pin and self.offerable(p)) else pin
            if len(into) < count:
                into.append(card)
            else:
                into[-1] = card
        return into

    def _contains_card(self, cards, node):
        if node in cards:
            return True
        p = self.fork_partner(node)
        return p >= 0 and p in cards

    def record_offer(self, shown):
        for i, n in enumerate(self.g):
            if n.kind not in (KEYSTONE, SEAL, CAPSTONE):
                continue
            if i in shown:
                self.unseen[i] = 0
            elif self.offerable(i):
                self.unseen[i] += 1

    # ---- commands
    def mark_owned(self, i):
        n = self.g[i]
        if n.kind in (TIER, HEARTH) or self.owned[i]:
            return False
        self.owned[i] = True
        if self.pinned == i:
            self.pinned = -1
        return True

    def try_stoke(self, wallet):
        i = self.info["hearth"]
        n = self.g[i]
        if self.hearth >= n.max_tier or self.hearth_round >= n.per_round:
            return False
        if self.status(i, wallet.balance) != AVAILABLE:
            return False
        c = self.cost(i)
        if not wallet.spend(c):
            return False
        self.hearth += 1
        self.hearth_round += 1
        if self.pinned == i and self.hearth >= n.max_tier:
            self.pinned = -1
        return True

    def on_round_end(self, wallet):
        self.hearth_round = 0
        earned = wallet.total_earned - self.earned_mark
        bonus = 0
        if self.owned[self.info["purse"]] and earned > 0:
            bonus = int(math.floor(f32(earned * f32(self.g[self.info["purse"]].value))))
            if bonus > 0:
                wallet.credit(bonus)
        self.earned_mark = wallet.total_earned
        return bonus


class Wallet:
    def __init__(self):
        self.balance = self.total_earned = 0

    def credit(self, v):
        self.balance += v
        self.total_earned += v

    def spend(self, v):
        if v <= 0 or v > self.balance:
            return False
        self.balance -= v
        return True


# ---------------------------------------------------------------------------------------------------- the model
def cushion(o, bot):
    return {"Human": o.human_cushion, "Hoarder": o.hoarder_cushion}.get(bot, 0.0)


def simulate(o, slot, wid, seed, bot, rounds, g_info, branches, econ):
    rnd = NetRandom(seed * 7919 + wid * 131 + BOTS.index(bot))
    wallet = Wallet()
    g, info = g_info
    tree = Tree(g, info, branches, econ, o)
    if bot == "Rush":
        tree.set_pin_run_start(info["cap"])
    run = dict(weapon=slot, seed=seed, bot=bot, income=0, spent=0, incomeLate=0, spentLate=0, walletR15=0,
               firstKeystone=0, firstCapstone=0, hearth=0, hearthCapRound=0, purseRound=0, specialRound=0,
               poolExhaustedRound=0)
    for r, (gold, special) in enumerate(rounds, start=1):
        wallet.credit(gold)
        if run["specialRound"] == 0 and rnd.next_value() < min(1.0, max(0.0, special)):
            run["specialRound"] = r
            if not tree.linked:
                tree.linked = "sw_arc" if rnd.next_value() < 0.5 else "sw_gel"
            else:
                rnd.next_value()
        purse = tree.on_round_end(wallet)
        round_income = gold + purse
        before = wallet.balance
        floor = f32(wallet.balance * f32(cushion(o, bot)))
        cards = tree.draw_frontier(rnd, o.offer_cards)
        if not cards and run["poolExhaustedRound"] == 0:
            run["poolExhaustedRound"] = r
        tree.record_offer(cards)
        limit = o.late_cards if (o.late_cards and o.late_from <= r <= (o.late_to or 99)) else o.card_limit
        for _ in range(limit):
            card = choose(tree, cards, wallet.balance, floor, bot, rnd)
            if card < 0:
                break
            buy(tree, wallet, card, r, run)
            cards.remove(card)
        cap = {"Rush": 0 if tree.pinned >= 0 else 10 ** 9, "Human": o.human_hearth, "Hoarder": o.hoarder_hearth}.get(bot, 10 ** 9)
        for _ in range(cap):
            price = tree.cost(info["hearth"])
            if wallet.balance - price < floor:
                break
            if not tree.try_stoke(wallet):
                break
        spent = before - wallet.balance
        if r <= 15:
            run["income"] += round_income
            run["spent"] += spent
            if r >= 10:
                run["incomeLate"] += round_income
                run["spentLate"] += spent
            if r == 15:
                run["walletR15"] = wallet.balance
        if run["hearthCapRound"] == 0 and tree.hearth >= g[info["hearth"]].max_tier:
            run["hearthCapRound"] = r
        if run["purseRound"] == 0 and tree.owned[info["purse"]]:
            run["purseRound"] = r
    run["hearth"] = tree.hearth
    return run


def choose(tree, cards, gold, floor, bot, rnd):
    best, best_cost, affordable = -1, 0, 0
    for i in cards:
        if not tree.offerable(i):
            continue
        c = tree.cost(i)
        if gold - c < floor:
            continue
        affordable += 1
        if bot == "SpendMax":
            if best < 0 or c > best_cost:
                best, best_cost = i, c
        elif bot == "Rush":
            if i == tree.pinned_offerable():
                return i
            if best < 0 or c < best_cost:
                best, best_cost = i, c
        else:
            if f32(rnd.next_value() * affordable) < 1.0:
                best, best_cost = i, c
    if bot == "Rush" and best >= 0 and tree.pinned >= 0:
        step = tree.pinned_offerable()
        reserve = tree.cost(step) if step >= 0 else 0
        if gold - best_cost < reserve:
            return -1
    return best


def buy(tree, wallet, i, r, run):
    c = tree.cost(i)
    if not wallet.spend(c):
        return
    n = tree.g[i]
    if n.kind == TIER:
        tree.tiers[n.branch] = tree.tiers.get(n.branch, 0) + 1
    else:
        tree.mark_owned(i)
    if n.lane == WEAPON_LANE:
        if n.kind == KEYSTONE and run["firstKeystone"] == 0:
            run["firstKeystone"] = r
        if n.kind == CAPSTONE and run["firstCapstone"] == 0 and r <= 15:
            run["firstCapstone"] = r


def percentile(v, p):
    if not v:
        return 0
    v = sorted(v)
    return v[min(max(math.ceil(f32(p) * len(v)) - 1, 0), len(v) - 1)]


def summarise(runs, bot):
    rs = [x for x in runs if x["bot"] == bot]
    share = [x["spent"] / x["income"] if x["income"] else 0.0 for x in rs]
    late = [x["spentLate"] / x["incomeLate"] if x["incomeLate"] else 0.0 for x in rs]
    ks = [x["firstKeystone"] or 16 for x in rs]
    cap = [x["firstCapstone"] or 16 for x in rs]
    return dict(bot=bot, runs=len(rs), share=percentile(share, 0.5), shareLate=percentile(late, 0.5),
                walletMed=percentile([x["walletR15"] for x in rs], 0.5), walletP90=percentile([x["walletR15"] for x in rs], 0.9),
                ks=percentile(ks, 0.5), capMed=percentile(cap, 0.5), capP90=percentile(cap, 0.9),
                capBy15=sum(1 for x in rs if x["firstCapstone"]) / max(1, len(rs)),
                hearth=percentile([x["hearth"] for x in rs], 0.5),
                hearthCap=percentile([x["hearthCapRound"] or 21 for x in rs], 0.5),
                pool=sum(1 for x in rs if x["poolExhaustedRound"]))


def round_income(balance_json, pickup, last_round):
    """(gold, expected Special pickups) per round: the report's seed-1 gold R1–R15 (pickup 1.00 → × pickup), R16+ extended."""
    with open(balance_json, encoding="utf-8") as f:
        rep = json.load(f)
    rows = rep["weapons"][0]["rounds"]
    base_pickup = float(rep.get("goldPickupFraction", 1.0))
    gold = [r["goldIncome"] / base_pickup for r in rows]
    special = [float(r.get("expectedSpecialPickups", 0.0)) for r in rows]
    if last_round > len(gold):
        world = base.World(base.load_config())
        ratio = sum(gold[10:15]) / sum(world.gold(r, 1.0) for r in range(11, 16))
        for r in range(len(gold) + 1, last_round + 1):
            gold.append(world.gold(r, 1.0) * ratio)
            special.append(special[-1])
    return [(round_half_even(f32(f32(g) * f32(pickup))), s) for g, s in zip(gold[:last_round], special[:last_round])]


class Opts:
    def __init__(self, a):
        self.m1 = a.m1
        self.offer_cards, self.card_limit = 3, 2
        self.late_cards, self.late_from, self.late_to = a.late_cards, a.late_from, a.late_to
        self.human_cushion, self.hoarder_cushion = 0.30, 0.50
        self.human_hearth, self.hoarder_hearth = 2, 1
        self.hearth_cap = a.hearth_cap
        self.pin_rules = not a.no_pin_rules
        self.link_price = a.link_price
        self.capstone_cost = a.capstone_cost


def run_model(a):
    o = Opts(a)
    cfg, econ, branches = load_economy()
    econ = dict(econ, StartingCostMin=int(econ["StartingCostMin"]), StartingCostMax=int(econ["StartingCostMax"]))
    with open(TREE_JSON, encoding="utf-8") as f:
        cat = json.load(f)
    rounds = round_income(a.balance, a.pickup, 20)
    runs = []
    for name, wid, slot in WEAPONS:
        gi = build_graph(slot, cat, branches, o)
        for seed in range(1, 21):
            for bot in BOTS:
                runs.append(simulate(o, slot, wid, seed, bot, rounds, gi, branches, econ))
    return runs, [summarise(runs, b) for b in BOTS], rounds


def label(a):
    parts = ["M1 met" if a.m1 else "before M1", "pickup %.2f" % a.pickup]
    if a.late_cards:
        parts.append("%d cards R%d–%s" % (a.late_cards, a.late_from, ("R%d" % a.late_to) if a.late_to else "end"))
    if a.hearth_cap:
        parts.append("Hearth cap %d" % a.hearth_cap)
    if a.link_price == "catalog":
        parts.append("WHAT-IF link price = catalog 80/160")
    if a.capstone_cost:
        parts.append("WHAT-IF capstone %d" % a.capstone_cost)
    if a.no_pin_rules:
        parts.append("WHAT-IF pre-RT6 pin (no CanPin)")
    return " · ".join(parts)


def to_markdown(a, summ):
    out = ["### tree model [%s] — seeds 1–20 × ar_1/smg_1/sg_1/sr_1" % label(a), "",
           "| bot | runs | share R1–15 | share R10–15 | wallet R15 med / p90 | 1st keystone | capstone med / p90 | capstone by R15 | Hearth (R20) / capped at | pool empty |",
           "|---|---|---|---|---|---|---|---|---|---|"]
    for s in summ:
        cap = lambda v: "—" if v > 15 else "R%d" % v  # noqa: E731
        out.append("| %s | %d | %.0f%% | %.0f%% | %d / %d | R%d | %s / %s | %.0f%% | %d / %s | %d |" % (
            s["bot"], s["runs"], s["share"] * 100, s["shareLate"] * 100, s["walletMed"], s["walletP90"], s["ks"],
            cap(s["capMed"]), cap(s["capP90"]), s["capBy15"] * 100, s["hearth"], "—" if s["hearthCap"] > 20 else "R%d" % s["hearthCap"], s["pool"]))
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--balance", required=True, help="dd-balance-v1 report JSON (EconomySimulator, seed 1)")
    ap.add_argument("--m1", action="store_true", help="capstone mastery M1 met (K6)")
    ap.add_argument("--pickup", type=float, default=0.90)
    ap.add_argument("--late-cards", type=int, default=0)
    ap.add_argument("--late-from", type=int, default=10, help="first round of --late-cards (C# lateCardFromRound = 10)")
    ap.add_argument("--late-to", type=int, default=0, help="WHAT-IF last round of --late-cards (0 = to the end)")
    ap.add_argument("--hearth-cap", type=int, default=0)
    ap.add_argument("--link-price", choices=["shipped", "catalog"], default="shipped")
    ap.add_argument("--capstone-cost", type=int, default=0)
    ap.add_argument("--no-pin-rules", action="store_true")
    ap.add_argument("--csv")
    ap.add_argument("--json")
    ap.add_argument("--compare", help="a C# TreeModel tree-model-runs.csv: prints how many runs match exactly (exit 1 if not all)")
    a = ap.parse_args()
    runs, summ, rounds = run_model(a)
    print(to_markdown(a, summ))
    if a.csv:
        cols = ["weapon", "seed", "bot", "income", "spent", "incomeLate", "spentLate", "walletR15", "firstKeystone",
                "firstCapstone", "hearth", "purseRound", "specialRound", "poolExhaustedRound"]
        with open(a.csv, "w", encoding="utf-8") as f:
            f.write(",".join(cols) + "\n")
            for x in runs:
                f.write(",".join(str(x[c]) for c in cols) + "\n")
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump(dict(label=label(a), bots=summ, income=[g for g, _ in rounds]), f, indent=1)
    if a.compare:
        import csv
        with open(a.compare, encoding="utf-8") as f:
            ref = {(r["weapon"], r["seed"], r["bot"]): r for r in csv.DictReader(f)}
        cols = ["income", "spent", "walletR15", "firstKeystone", "firstCapstone", "hearth", "purseRound", "specialRound"]
        same = sum(1 for x in runs if (x["weapon"], str(x["seed"]), x["bot"]) in ref
                   and all(str(x[c]) == ref[(x["weapon"], str(x["seed"]), x["bot"])][c] for c in cols))
        print("\ncompare: %d / %d runs identical to %s" % (same, len(ref), a.compare))
        sys.exit(0 if same == len(ref) else 1)


if __name__ == "__main__":
    main()
