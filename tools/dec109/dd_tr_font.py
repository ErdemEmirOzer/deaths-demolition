#!/usr/bin/env python3
"""DEC-109 (WP-068, C-00 30.09) · Shippori Mincho B1 Bold → "Shippori Mincho B1 DD-TR": Ğ ğ İ Ş ş as an OFL 1.1 derivative.

Run it with the fontTools interpreter the caller names (tools/dec109/dd-tr-font.sh --venv DIR does that); nothing here
knows where fontTools lives. Deterministic: the same source font gives the same glyphs, metrics and names.

What it writes (design: panel A, K/wp-068s9/evidence/dec109/PANEL-DECISION.md, recorded in WP-068-UI-V3-DIRECTION §13):
- brevecomb.dd / brevecomb.case.dd: the breve drawn once each as an unencoded mark at x = 0 — a modulated crescent on a
  160° arc (horns lean outward, ends cut square to the stroke), thick 60 / tip 24; ğ 280 × 122 bottom 548, Ğ 290 × 100
  bottom 797. Clockwise quadratic contours, integer points, fit error checked against the analytic outline.
- dotaccent.case.dd: one Ü dot (126), bottom 793 — U+0307 (135) would read darker than the Ü / Ö dots.
- Gbreve = G + brevecomb.case.dd at x 399 (G's highest outer point); gbreve = g + brevecomb.dd at x 282 (the upper-bowl
  counter centre 272 + 10 toward the ear); Idotaccent = I + dot centred on I; Scedilla / scedilla = S / s + uni0327 with
  the stem on the letter's lowest point and the cedilla top at least 10 units into the letter (the face's own Ç ç rule).
  The base carries USE_MY_METRICS, so advance and side bearings are the base's.
- cmap U+011E U+011F U+0130 U+015E U+015F; GDEF classes; vmtx rows; OS/2 code page 1254 + Latin Extended-A bits.
- name: family "Shippori Mincho B1 DD-TR" (the licence declares no Reserved Font Name), PostScript
  ShipporiMinchoB1DDTR-Bold; copyright and licence kept, one derivative sentence added. DSIG dropped (it no longer signs).
- --kerning: every hand pair with G / S / I (and g / s) is copied to Ğ / Ş / İ (ğ / ş); existing pairs win.

Exit 0 = written and every check held; 1 = a check failed (nothing is written); 64 = usage.
"""
import argparse
import json
import math
import sys

from fontTools.pens.basePen import BasePen
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont
from fontTools.misc.timeTools import timestampFromString
from fontTools.ttLib.tables._g_l_y_f import Glyph, GlyphComponent, GlyphCoordinates

FAMILY = "Shippori Mincho B1 DD-TR"
FAMILY_JA = "しっぽり明朝B1 DD-TR"
POSTSCRIPT = "ShipporiMinchoB1DDTR-Bold"
DERIVATIVE = ("Turkish glyphs G breve, g breve, I dot above, S cedilla, s cedilla added 2026 for Deaths Demolition "
              "(DEC-109) as a modified version under the SIL Open Font License 1.1.")

SWEEP = 160.0
SWEEP_REFIT = 0.5          # ± degrees: the square-cut corners are convex, so the sweep may move this little for them to land on the grid
LOW = dict(width=280, height=122, thick=60, tip=24, bottom=548)   # ğ
CAP = dict(width=290, height=100, thick=60, tip=24, bottom=797)   # Ğ
G_CX, g_CX = 399, 282
DOT_BOTTOM = 793
CEDILLA_MIN_OVERLAP = 10
FIT_LIMIT = 0.5
MODIFIED = "Wed Sep 30 00:00:00 2026"   # head.modified is the DEC-109 date, not the build time: the same source gives the same bytes

NEW = {0x011E: "Gbreve", 0x011F: "gbreve", 0x0130: "Idotaccent", 0x015E: "Scedilla", 0x015F: "scedilla"}
VARIANTS = {"G": "Ğ", "S": "Ş", "I": "İ", "g": "ğ", "s": "ş"}


# ------------------------------------------------------------------ breve geometry (the panel's construction)

def _ellipse(cx, ytop, a, b, t):
    return cx - a * math.cos(t), ytop - b * math.sin(t)


def _inner(cx, ytop, a, b, t, t0, t1, thick, tip):
    x, y = _ellipse(cx, ytop, a, b, t)
    u = (t - t0) / (t1 - t0)
    w = tip + (thick - tip) * math.sin(math.pi * u)
    nx, ny = (x - cx) / (a * a), (y - ytop) / (b * b)
    length = math.hypot(nx, ny) or 1.0
    return x - w * nx / length, y - w * ny / length


def breve_curves(p, sweep=SWEEP):
    """The breve at x = 0 as closed cubic segments, clockwise in y-up space: outer arc right → left, the square cut,
    the inner edge left → right, the square cut back."""
    a, b = p["width"] / 2.0, float(p["height"])
    ytop = p["bottom"] + b
    t0 = math.radians((180.0 - sweep) / 2.0)
    t1 = math.pi - t0
    outer = lambda t: _ellipse(0.0, ytop, a, b, t)
    inner = lambda t: _inner(0.0, ytop, a, b, t, t0, t1, p["thick"], p["tip"])

    def d_outer(t):
        return a * math.sin(t), -b * math.cos(t)

    def d_num(fn, t, h=1e-6):
        (xa, ya), (xb, yb) = fn(t - h), fn(t + h)
        return (xb - xa) / (2 * h), (yb - ya) / (2 * h)

    def hermite(fn, dfn, ta, tb):
        (x0, y0), (x3, y3) = fn(ta), fn(tb)
        (dx0, dy0), (dx3, dy3) = dfn(ta), dfn(tb)
        k = (tb - ta) / 3.0
        return (x0, y0), (x0 + dx0 * k, y0 + dy0 * k), (x3 - dx3 * k, y3 - dy3 * k), (x3, y3)

    def fit(fn, dfn, ta, tb, tol=0.02, depth=0):
        seg = hermite(fn, dfn, ta, tb)
        worst = 0.0
        for i in range(1, 16):
            s = i / 16.0
            bx, by = _cubic_at(seg, s)
            worst = max(worst, _dist_to_curve(fn, ta, tb, (bx, by)))
        if worst <= tol or depth > 8:
            return [seg]
        tm = (ta + tb) / 2.0
        return fit(fn, dfn, ta, tm, tol, depth + 1) + fit(fn, dfn, tm, tb, tol, depth + 1)

    n = 4
    ts = [t0 + (t1 - t0) * i / n for i in range(n + 1)]
    outer_segs = []
    for i in range(n, 0, -1):                       # right → left (t decreasing)
        for seg in reversed(fit(outer, d_outer, ts[i - 1], ts[i])):
            outer_segs.append(tuple(reversed(seg)))
    inner_segs = []
    for i in range(n):                              # left → right (t increasing)
        inner_segs += fit(inner, lambda t: d_num(inner, t), ts[i], ts[i + 1])
    truth = [outer(t1 - (t1 - t0) * i / 400.0) for i in range(401)] + [inner(t0 + (t1 - t0) * i / 400.0) for i in range(401)]
    return outer_segs, inner_segs, truth


def _cubic_at(seg, s):
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = seg
    m = 1 - s
    return (m ** 3 * x0 + 3 * m * m * s * x1 + 3 * m * s * s * x2 + s ** 3 * x3,
            m ** 3 * y0 + 3 * m * m * s * y1 + 3 * m * s * s * y2 + s ** 3 * y3)


def _dist_to_curve(fn, ta, tb, pt, samples=64):
    best = float("inf")
    prev = fn(ta)
    for i in range(1, samples + 1):
        cur = fn(ta + (tb - ta) * i / samples)
        best = min(best, _seg_dist(pt, prev, cur))
        prev = cur
    return best


def _seg_dist(p, a, b):
    (px, py), (ax, ay), (bx, by) = p, a, b
    dx, dy = bx - ax, by - ay
    ll = dx * dx + dy * dy
    t = 0.0 if ll == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / ll))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def draw_breve(p):
    """The breve on the nearest sweep to 160° (within SWEEP_REFIT) whose integer outline stays within FIT_LIMIT of the
    analytic one; the exact corners of a square cut are rarely near integers, and a convex corner can't be fitted closer."""
    best = None
    steps = int(round(SWEEP_REFIT / 0.05))
    for k in sorted(range(-steps, steps + 1), key=lambda v: (abs(v), v)):
        sweep = SWEEP + 0.05 * k
        g, truth = _draw_breve(p, sweep)
        err = outline_error(g, None, truth)[0]
        if best is None or err < best[0]:
            best = (err, g, truth, sweep)
        if err <= FIT_LIMIT - 0.05:
            break
    return best[1], best[2], best[3]


def _draw_breve(p, sweep):
    outer_segs, inner_segs, truth = breve_curves(p, sweep)
    pen = TTGlyphPen(None)
    q = Cu2QuPen(pen, max_err=0.05, reverse_direction=False)
    q.moveTo(outer_segs[0][0])
    for seg in outer_segs:
        q.curveTo(seg[1], seg[2], seg[3])
    q.lineTo(inner_segs[0][0])                      # square cut, left end
    for seg in inner_segs:
        q.curveTo(seg[1], seg[2], seg[3])
    q.closePath()                                   # square cut, right end
    g = pen.glyph(round=lambda v: v)                # exact quadratics; integers are chosen below
    exact = [tuple(c) for c in g.coordinates]
    flags = [fl & 1 for fl in g.flags]
    ints = snap_to_integers(exact, flags, truth)
    g.coordinates = GlyphCoordinates(ints)
    return g, truth


def _flatten_points(pts, flags, steps=16):
    """One closed TrueType quadratic contour (implied on-curve midpoints) → polyline."""
    n = len(pts)
    start = next(i for i in range(n) if flags[i])
    ring = [(pts[(start + k) % n], flags[(start + k) % n]) for k in range(n)] + [(pts[start], 1)]
    out = [ring[0][0]]
    prev_on, off = ring[0][0], None
    for pt, on in ring[1:]:
        if on:
            if off is None:
                out.append(pt)
            else:
                out += _quad(prev_on, off, pt, steps)
                off = None
            prev_on = pt
        else:
            if off is not None:
                mid = ((off[0] + pt[0]) / 2.0, (off[1] + pt[1]) / 2.0)
                out += _quad(prev_on, off, mid, steps)
                prev_on = mid
            off = pt
    return out


def _quad(p0, p1, p2, steps):
    res = []
    for i in range(1, steps + 1):
        s = i / float(steps)
        m = 1 - s
        res.append((m * m * p0[0] + 2 * m * s * p1[0] + s * s * p2[0], m * m * p0[1] + 2 * m * s * p1[1] + s * s * p2[1]))
    return res


class _SegGrid:
    CELL = 6.0

    def __init__(self, poly):
        self.segs, self.cells = list(zip(poly, poly[1:])), {}
        for k, (a, b) in enumerate(self.segs):
            for cx in range(int(math.floor((min(a[0], b[0]) - 2) / self.CELL)), int(math.floor((max(a[0], b[0]) + 2) / self.CELL)) + 1):
                for cy in range(int(math.floor((min(a[1], b[1]) - 2) / self.CELL)), int(math.floor((max(a[1], b[1]) + 2) / self.CELL)) + 1):
                    self.cells.setdefault((cx, cy), []).append(k)

    def dist(self, p):
        key = (int(math.floor(p[0] / self.CELL)), int(math.floor(p[1] / self.CELL)))
        ks = self.cells.get(key)
        segs = [self.segs[k] for k in ks] if ks else self.segs
        return min(_seg_dist(p, a, b) for a, b in segs)


def _outline_error(pts, flags, truth, truth_grid):
    poly = _flatten_points(pts, flags)
    grid = _SegGrid(poly)
    worst, where = 0.0, None
    for t in truth:
        e = grid.dist(t)
        if e > worst:
            worst, where = e, t
    for q in poly:
        e = truth_grid.dist(q)
        if e > worst:
            worst, where = e, q
    return worst, where


def snap_to_integers(exact, flags, truth):
    """Integer points for the exact quadratics: each point takes the floor or ceil of its exact value (never further),
    chosen greedily around the current worst spot until the maximum outline error stops falling. Deterministic."""
    truth_grid = _SegGrid(truth + [truth[0]])
    pts = [(int(round(x)), int(round(y))) for x, y in exact]
    err, where = _outline_error(pts, flags, truth, truth_grid)
    for _ in range(80):
        near = sorted(range(len(pts)), key=lambda k: (math.hypot(pts[k][0] - where[0], pts[k][1] - where[1]), k))[:5]
        best = None
        for k in near:
            fx, fy = exact[k]
            for nx in sorted({math.floor(fx), math.ceil(fx)}):
                for ny in sorted({math.floor(fy), math.ceil(fy)}):
                    if (nx, ny) == pts[k]:
                        continue
                    trial = pts[:]
                    trial[k] = (int(nx), int(ny))
                    e, w = _outline_error(trial, flags, truth, truth_grid)
                    if e < err - 1e-9 and (best is None or e < best[0]):
                        best = (e, w, trial)
        if best is None:
            break
        err, where, pts = best
    return pts


class _Flatten(BasePen):
    """Samples the written TrueType outline (implied on-curve points included) into polylines."""

    def __init__(self, glyphset=None, steps=48):
        super().__init__(glyphset)
        self.contours, self._cur, self.steps = [], None, steps

    def _moveTo(self, pt):
        self._cur = [pt]
        self.contours.append(self._cur)

    def _lineTo(self, pt):
        self._cur.append(pt)

    def _qCurveToOne(self, p1, p2):
        p0 = self._getCurrentPoint()
        for i in range(1, self.steps + 1):
            s = i / float(self.steps)
            m = 1 - s
            self._cur.append((m * m * p0[0] + 2 * m * s * p1[0] + s * s * p2[0], m * m * p0[1] + 2 * m * s * p1[1] + s * s * p2[1]))

    def _curveToOne(self, p1, p2, p3):
        p0 = self._getCurrentPoint()
        for i in range(1, self.steps + 1):
            self._cur.append(_cubic_at((p0, p1, p2, p3), i / float(self.steps)))

    def _closePath(self):
        if self._cur and self._cur[0] != self._cur[-1]:
            self._cur.append(self._cur[0])


def outline_error(glyph, glyf, truth):
    pen = _Flatten()
    glyph.draw(pen, glyf)
    poly = pen.contours[0]
    to_poly = max(min(_seg_dist(t, poly[i], poly[i + 1]) for i in range(len(poly) - 1)) for t in truth)
    closed = truth + [truth[0]]
    to_truth = max(min(_seg_dist(p, closed[i], closed[i + 1]) for i in range(len(closed) - 1)) for p in poly[::4])
    area = sum(poly[i][0] * poly[i + 1][1] - poly[i + 1][0] * poly[i][1] for i in range(len(poly) - 1)) / 2.0
    return max(to_poly, to_truth), area, len(pen.contours)


# ------------------------------------------------------------------ font helpers

def contours(glyf, name):
    g = glyf[name]
    coords, ends, flags = g.getCoordinates(glyf)
    out, start = [], 0
    for end in ends:
        out.append([(coords[i][0], coords[i][1], flags[i] & 1) for i in range(start, end + 1)])
        start = end + 1
    return out


def lowest_point(glyf, name):
    pts = [p for c in contours(glyf, name) for p in c if p[2]]
    y = min(p[1] for p in pts)
    xs = sorted(p[0] for p in pts if p[1] == y)
    return xs[len(xs) // 2], y


def crossings(glyf, name, y):
    """x where the flattened outline crosses the horizontal line y."""
    pen = _Flatten(steps=64)
    glyf[name].draw(pen, glyf)
    xs = []
    for c in pen.contours:
        for (ax, ay), (bx, by) in zip(c, c[1:]):
            if (ay - y) * (by - y) < 0:
                xs.append(ax + (y - ay) * (bx - ax) / (by - ay))
    return sorted(xs)


def vertical_crossings(glyf, name, x):
    pen = _Flatten(steps=64)
    glyf[name].draw(pen, glyf)
    ys = []
    for c in pen.contours:
        for (ax, ay), (bx, by) in zip(c, c[1:]):
            if (ax - x) * (bx - x) < 0:
                ys.append(ay + (x - ax) * (by - ay) / (bx - ax))
    return sorted(ys)


def simple_from_contour(points, dx=0, dy=0):
    pen = TTGlyphPen(None)
    pts = [(x + dx, y + dy, on) for x, y, on in points]
    # Rebuild with the original on / off pattern (quadratic TrueType semantics).
    start = next(i for i, p in enumerate(pts) if p[2])
    ring = pts[start:] + pts[:start]
    pen.moveTo(ring[0][:2])
    offs = []
    for x, y, on in ring[1:] + [ring[0]]:
        if on:
            if offs:
                pen.qCurveTo(*([o for o in offs] + [(x, y)]))
                offs = []
            else:
                pen.lineTo((x, y))
        else:
            offs.append((x, y))
    if offs:
        pen.qCurveTo(*(offs + [ring[0][:2]]))
    pen.closePath()
    return pen.glyph()


def composite(parts):
    g = Glyph()
    g.numberOfContours = -1
    g.components = []
    for i, (name, x, y) in enumerate(parts):
        c = GlyphComponent()
        c.glyphName, c.x, c.y = name, int(x), int(y)
        c.flags = 0x4 | (0x200 if i == 0 else 0)     # ROUND_XY_TO_GRID; the base's USE_MY_METRICS
        g.components.append(c)
    return g


def fail(msg):
    print("DEC-109 FAIL: " + msg, file=sys.stderr)
    sys.exit(1)


# ------------------------------------------------------------------ build

def build(src, out, report):
    f = TTFont(src)
    glyf, cmap = f["glyf"], f.getBestCmap()
    for cp in NEW:
        if cp in cmap:
            fail("U+%04X is already in the source font (%s)" % (cp, cmap[cp]))
    for need in ("G", "g", "I", "S", "s", "Udieresis", "uni0327"):
        if need not in glyf.glyphs:
            fail("source glyph missing: " + need)

    new_glyphs, checks = {}, []

    # Breves
    for name, p in (("brevecomb.dd", LOW), ("brevecomb.case.dd", CAP)):
        g, truth, sweep = draw_breve(p)
        g.recalcBounds(glyf)
        err, area, n = outline_error(g, glyf, truth)
        if n != 1:
            fail("%s: %d contours" % (name, n))
        if area >= 0:
            fail("%s: counter-clockwise (TrueType outer contours are clockwise)" % name)
        if err > FIT_LIMIT:
            fail("%s: fit error %.3f > %.2f" % (name, err, FIT_LIMIT))
        on = sum(1 for fl in g.flags if fl & 1)
        checks.append("%s: clockwise, %d points (%d on-curve), sweep %.2f°, fit error %.3f u, bbox %d..%d × %d..%d" %
                      (name, len(g.coordinates), on, sweep, err, g.xMin, g.xMax, g.yMin, g.yMax))
        new_glyphs[name] = g

    # İ dot: Ü's left dot, centred on x = 0
    dots = [c for c in contours(glyf, "Udieresis") if min(p[1] for p in c) >= 700]
    if len(dots) != 2:
        fail("Udieresis: expected 2 dots, found %d" % len(dots))
    dot = min(dots, key=lambda c: min(p[0] for p in c))
    dx0, dx1 = min(p[0] for p in dot), max(p[0] for p in dot)
    dy0 = min(p[1] for p in dot)
    centre = (dx0 + dx1) // 2
    new_glyphs["dotaccent.case.dd"] = simple_from_contour(dot, -centre, DOT_BOTTOM - dy0)
    checks.append("dotaccent.case.dd: Ü dot %d wide, bottom %d" % (dx1 - dx0, DOT_BOTTOM))

    # Cedilla: stem on the lowest point of S / s (panel rule); its top at least 10 units into the letter and no nearer the
    # counter than the face's own Ç / ç keep it (measured here, not assumed).
    ced = contours(glyf, "uni0327")[0]
    ced_top = max(p[1] for p in ced)
    ced_bottom = min(p[1] for p in ced)
    top_rel = [x for x, y, _ in ced if y == ced_top]
    top_mid_rel = sum(top_rel) / len(top_rel)
    ref_margins = []
    for merged, base in (("Ccedilla", "C"), ("ccedilla", "c")):
        bottom_x = min(x for c in contours(glyf, merged) for x, y, _ in c if y == ced_bottom)
        mdx = bottom_x - min(x for x, y, _ in ced if y == ced_bottom)
        ys = [y for y in vertical_crossings(glyf, base, top_mid_rel + mdx) if y < 250]
        ref_margins.append(ys[1] - ced_top)
    ref_margin = min(ref_margins)
    checks.append("face reference: Ç / ç keep the cedilla top %.1f / %.1f units below the stroke's inner edge" % tuple(ref_margins))
    parts = {}
    for base, target in (("S", "Scedilla"), ("s", "scedilla")):
        lx, ly = lowest_point(glyf, base)
        xs = crossings(glyf, "uni0327", ly)
        if len(xs) < 2:
            fail("uni0327: no stem at y %d" % ly)
        stem = (xs[0] + xs[1]) / 2.0
        dx = int(round(lx - stem))
        ys = [y for y in vertical_crossings(glyf, base, top_mid_rel + dx) if y < 250]
        if len(ys) < 2:
            fail("%s: no stroke above the cedilla top" % base)
        inner_edge = ys[1]
        dy = min(0, int(math.floor(inner_edge - ref_margin - ced_top)))
        overlap = ced_top + dy - ly
        if overlap < CEDILLA_MIN_OVERLAP:
            fail("%s: cedilla overlap %d < %d" % (target, overlap, CEDILLA_MIN_OVERLAP))
        parts[target] = (base, dx, dy)
        checks.append("%s = %s + uni0327 (%d, %d): stem centre %.1f on the lowest point x %d; overlap %d; %.1f units below the stroke's inner edge" %
                      (target, base, dx, dy, stem + dx, lx, overlap, inner_edge - ced_top - dy))

    ix0, ix1 = glyf["I"].xMin, glyf["I"].xMax
    i_cx = (ix0 + ix1) // 2
    comps = {
        "Gbreve": [("G", 0, 0), ("brevecomb.case.dd", G_CX, 0)],
        "gbreve": [("g", 0, 0), ("brevecomb.dd", g_CX, 0)],
        "Idotaccent": [("I", 0, 0), ("dotaccent.case.dd", i_cx, 0)],
        "Scedilla": [("S", 0, 0), ("uni0327", parts["Scedilla"][1], parts["Scedilla"][2])],
        "scedilla": [("s", 0, 0), ("uni0327", parts["scedilla"][1], parts["scedilla"][2])],
    }
    for name, c in comps.items():
        new_glyphs[name] = composite(c)
    checks.append("Idotaccent: dot centred on I's bbox centre %d" % i_cx)

    # Glyph order, glyf, metrics
    order = f.getGlyphOrder()
    added = ["brevecomb.dd", "brevecomb.case.dd", "dotaccent.case.dd", "Gbreve", "gbreve", "Idotaccent", "Scedilla", "scedilla"]
    for n in added:
        if n in glyf.glyphs:
            fail("glyph name already used: " + n)
    f.setGlyphOrder(order + added)
    glyf.glyphOrder = order + added
    for n in added:
        glyf.glyphs[n] = new_glyphs[n]
        new_glyphs[n].recalcBounds(glyf)
    hmtx = f["hmtx"].metrics
    vmtx = f["vmtx"].metrics if "vmtx" in f else None
    for n in ("brevecomb.dd", "brevecomb.case.dd", "dotaccent.case.dd"):
        hmtx[n] = (0, new_glyphs[n].xMin)
        if vmtx is not None:
            vmtx[n] = vmtx["uni0307"]
    for n, c in comps.items():
        base = c[0][0]
        hmtx[n] = hmtx[base]
        if vmtx is not None:
            vmtx[n] = vmtx[base]
        g = new_glyphs[n]
        if g.xMin != glyf[base].xMin:
            fail("%s: the mark reaches left of %s, so the base's side bearing would lie (xMin %d vs %d)" % (n, base, g.xMin, glyf[base].xMin))
        checks.append("%s: advance %d = %s, bbox %d..%d × %d..%d" % (n, hmtx[n][0], base, g.xMin, g.xMax, g.yMin, g.yMax))

    # cmap
    for table in f["cmap"].tables:
        if table.isUnicode() and hasattr(table, "cmap") and isinstance(table.cmap, dict):
            for cp, name in NEW.items():
                table.cmap[cp] = name

    # GDEF glyph classes
    if "GDEF" in f and f["GDEF"].table.GlyphClassDef is not None:
        classes = f["GDEF"].table.GlyphClassDef.classDefs
        for n in ("brevecomb.dd", "brevecomb.case.dd", "dotaccent.case.dd"):
            classes[n] = 3
        for n in comps:
            classes[n] = 1

    # OS/2: Turkish code page, Latin Extended-A
    os2 = f["OS/2"]
    os2.ulCodePageRange1 |= 1 << 4
    os2.ulUnicodeRange1 |= 1 << 2

    # Names
    name = f["name"]
    for rec in list(name.names):
        s = rec.toUnicode()
        nid = rec.nameID
        val = None
        if nid == 0:
            val = s if DERIVATIVE in s else s + " " + DERIVATIVE
        elif nid == 1:
            val = FAMILY_JA if rec.langID == 0x411 else FAMILY
        elif nid == 3:
            val = s.replace("ShipporiMinchoB1-Bold", POSTSCRIPT).replace("FTDS", "FTDS DD-TR")
        elif nid == 4:
            val = FAMILY_JA + " Bold" if rec.langID == 0x411 else FAMILY + " Bold"
        elif nid == 5:
            val = s if "DD-TR" in s else s + "; DD-TR DEC-109"
        elif nid == 6:
            val = POSTSCRIPT
        if val is not None and val != s:
            name.setName(val, nid, rec.platformID, rec.platEncID, rec.langID)

    if "DSIG" in f:
        del f["DSIG"]

    f.recalcTimestamp = False
    f["head"].modified = timestampFromString(MODIFIED)
    f.save(out)

    # Re-open and verify what was written
    r = TTFont(out)
    rc, rg = r.getBestCmap(), r["glyf"]
    for cp, n in NEW.items():
        if rc.get(cp) != n:
            fail("cmap U+%04X → %s after save" % (cp, rc.get(cp)))
        g = rg[n]
        if not g.isComposite():
            fail(n + " is not a composite after save")
    if r["name"].getDebugName(1) != FAMILY or r["name"].getDebugName(6) != POSTSCRIPT:
        fail("names not written")
    checks.append("written: %d glyphs (+%d), cmap + GDEF + vmtx + OS/2 cp1254, DSIG dropped" % (len(r.getGlyphOrder()), len(added)))
    return checks


def kerning(path_in, path_out):
    with open(path_in, encoding="utf-8") as fh:
        doc = json.load(fh)
    pairs = doc["pairs"]
    have = {(p["first"], p["second"]) for p in pairs}
    added = []
    for p in list(pairs):
        firsts = [p["first"]] + ([VARIANTS[p["first"]]] if p["first"] in VARIANTS else [])
        seconds = [p["second"]] + ([VARIANTS[p["second"]]] if p["second"] in VARIANTS else [])
        for a in firsts:
            for b in seconds:
                if (a, b) not in have:
                    have.add((a, b))
                    q = dict(p)
                    q["first"], q["second"] = a, b
                    added.append(q)
    pairs.extend(added)
    if "DEC-109" not in doc.get("note", ""):
        doc["note"] = doc.get("note", "") + "; DEC-109: G/S/I (g/s) pairs copied to Ğ/Ş/İ (ğ/ş) by tools/dec109/dd_tr_font.py"
    with open(path_out, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, indent=1)
        fh.write("\n")
    return len(added)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--src", required=True, help="the original ShipporiMinchoB1-Bold.ttf")
    ap.add_argument("--out", required=True, help="the derivative TTF to write")
    ap.add_argument("--kerning", help="kerning.json to extend in place (optional)")
    ap.add_argument("--report", help="write the check lines here too")
    args = ap.parse_args()
    checks = build(args.src, args.out, args.report)
    if args.kerning:
        checks.append("kerning.json: %d pairs copied to Ğ/Ş/İ (ğ/ş)" % kerning(args.kerning, args.kerning))
    text = "\n".join(checks) + "\n"
    sys.stdout.write(text)
    if args.report:
        with open(args.report, "w", encoding="utf-8") as fh:
            fh.write(text)


if __name__ == "__main__":
    main()
