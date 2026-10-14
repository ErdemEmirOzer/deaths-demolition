#!/usr/bin/env python3
"""WP-097 K2 · AP-029 v001 → Unity (S-08, C-00 07.10).

ap029_import.py <worktree-root> [--parts fire,space,special,foley] [--ap029 DIR]

Reads the AP-029 export folders (DIR = K/asset-production/AP-029/exports/v001 by default, tools/dd_paths.py KPROJ):
  fire/     Weapon_<w>_Fire_NN.wav       → the AP-027 Fire rows' own sound names (WeaponAr1Fire …): v5 re-render, same RR
  space/    WeaponTail<C><Echo|Release|Room>_NN.wav → the WP-097 K1 900 block (stereo class tails)
  special/  SwArcFire_NN.wav …           → the AP-011 rows' own sound names (event / bus / spatial / loop / RR taken from the
                                           AP-011 Unity manifest, which the AP-029 row supersedes)
  foley/    Casing_<w>_<Metal|Mat|Grass>_NN.wav → Casing<W><Surface>; Link_<lmg1|lmg2>_<Surface>_NN.wav → Link<W><Surface>
            (CasingLink_ is accepted as an alias of Link_); MagDrop_<Rifle|Pistol>_<Surface>_NN.wav → MagDrop<Family><Surface>
Each part needs its MANIFEST.json and SHA256SUMS; a part without a MANIFEST.json is skipped (listed under "skipped": the foley
part may land later). Every file a manifest entry names must be in the part's SHA256SUMS with the same SHA-256 (and equal to the
entry's perFile sha256 when given) — else AssertionError (exit 1); files already copied stay, the manifest is not written.
Files are copied to Assets/_Art/Audio/AP029/<name>.wav and listed in Assets/_Art/Audio/AP029-audio-manifest.json in the AP-011
schema (the fields ap027_import.py writes, measured on the shipped file with its functions: house lufs of file 1, stereoLufs,
truePeakDb, noise floor / DC / clipped / end stamps, continuous) plus "part" and "durationsS". Entries of parts not imported in
this run are kept (merge by part). Import-time processing follows ap027_import.py and runs only where a gate needs it:
DC out (> −60 dBFS), end fade (stops within 40 dB of its peak, or the last 10 ms over −70 dBFS — a manifest "tailExempt" sound
only on the absolute end gate; never on a loop), the Fire true-peak shave (> −2 dBTP; sr1 excluded). Each is logged in the
entry (importFade / importDcRemoved / importPeakLimit) and in the provenance. .meta files are left to Unity (as ap027_import.py).
Exit 0 imported (summary on stdout) · 1 SHA mismatch · 64 usage / no worktree root / no part to import.
Test: python3 tools/qa/tests/test_ap029_import.py (synthetic AP-029 tree).
"""
import argparse, json, re, shutil, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dd_paths  # noqa: E402
import ap027_import as a27  # noqa: E402  (sha, pcm, write24, fades, DC, true peak, house / stereo LUFS, stamps)

AP029 = dd_paths.KPROJ / "asset-production" / "AP-029" / "exports" / "v001"
PARTS = ("fire", "space", "special", "foley")
AUDIO = "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Audio"
OUT_DIR = "AP029"
OUT_MANIFEST = "AP029-audio-manifest.json"
SRC_LABEL = "K/asset-production/AP-029/exports/v001/"

FIRE_RE = re.compile(r"^Weapon_(?P<w>[a-z]+\d)_Fire_\d\d\.wav$")
TAIL_RE = re.compile(r"^(?P<sound>WeaponTail(?P<cls>Ar|Smg|Sg|Lmg|Sr|Rl)(?P<kind>Echo|Release|Room))_\d\d\.wav$")
CASING_RE = re.compile(r"^Casing_(?P<w>(?:ar|smg|sg|lmg|sr)\d)_(?P<s>StoneSlate|WetStone|Wood|Dirt|Puddle|Metal|Mat|Grass)_\d\d\.wav$")
LINK_RE = re.compile(r"^(?:CasingLink|Link)_(?P<w>lmg\d)_(?P<s>StoneSlate|WetStone|Wood|Dirt|Puddle|Metal|Mat|Grass)_\d\d\.wav$")
MAG_RE = re.compile(r"^MagDrop_(?P<f>Rifle|Pistol)_(?P<s>StoneSlate|Wood|Dirt|Metal|WetStone)_\d\d\.wav$")
TAIL_EVENT = {"Echo": "ClassicWeaponFire (top-down open-air echo)", "Release": "ClassicWeaponRelease (top-down string release tail)",
              "Room": "ClassicWeaponFire (top-down room tail)"}


def pascal(w):
    return w[0].upper() + w[1:]


def foley_sound(name):
    """The AudioSoundId a foley file belongs to, or None (unrecognised name)."""
    m = CASING_RE.match(name)
    if m: return "Casing" + pascal(m["w"]) + m["s"]
    m = LINK_RE.match(name)
    if m: return "Link" + pascal(m["w"]) + m["s"]
    m = MAG_RE.match(name)
    if m: return "MagDrop" + m["f"] + m["s"]
    return None


def expected_sound(part, name):
    """The sound a file name implies in its part (None = no naming rule for the part / unrecognised)."""
    if part == "fire":
        m = FIRE_RE.match(name)
        return "Weapon" + pascal(m["w"]) + "Fire" if m else None
    if part == "space":
        m = TAIL_RE.match(name)
        return m["sound"] if m else None
    if part == "foley":
        return foley_sound(name)
    return None


def read_sums(d):
    sums = {}
    p = d / "SHA256SUMS"
    assert p.is_file(), ("SHA256SUMS missing", str(d))
    for line in p.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line: continue
        h, f = line.split(None, 1)
        sums[f.lstrip("*").strip()] = h.lower()
    return sums


def sound_ids(root):
    """Names in AudioSoundId.cs (worktree) — a row naming an unknown id is reported (the catalog builder would skip it)."""
    p = Path(root) / "prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Audio/Library/AudioSoundId.cs"
    if not p.is_file(): return None
    return set(re.findall(r"^\s*([A-Z][A-Za-z0-9]*)\s*=\s*\d+", p.read_text(encoding="utf-8"), re.M))


def groups_of(part, man, sums):
    """[(sound, [files], entry-or-None)] of a part; foley entries are cross-checked against the names of their files."""
    out, seen = [], set()
    for e in man.get("entries", []):
        sound, files = e["sound"], list(e["files"])
        if part == "foley" and sound.startswith("CasingLink"): sound = "Link" + sound[len("CasingLink"):]
        for f in files:
            want = expected_sound(part, f)
            assert want is None or want == sound, ("file name / sound mismatch", part, f, sound, want)
        assert sound not in seen, ("sound twice in one manifest", part, sound)
        seen.add(sound)
        out.append((sound, files, e))
    return out


def measure(dst, x, r):
    """Stamps on what ships (ap027_import functions)."""
    return {"house": a27.house_lufs(dst), "stereo": a27.stereo_lufs(dst), "tp": a27.true_peak_db(dst), "st": a27.stamps(dst),
            "silent": a27.has_silent_stretch(dst), "dur": len(x) / float(r), "ch": x.shape[1]}


def import_file(src, dst, *, loop, tail_exempt, fire_weapon):
    """Copy (or process where a gate needs it) one file; returns (flags, unshaved house lufs or None)."""
    x, r = a27.pcm(src)
    flags, src_lufs = [], None
    dc = a27.has_dc(x)
    if dc: x = a27.dc_removed(x, r); flags.append("dc")
    lim = fire_weapon is not None and fire_weapon not in a27.PEAK_EXCLUDE_WEAPONS and a27.tp_db_x(x) > a27.PEAK_CEILING_DB
    if loop: fd = False   # a loop's end joins its start: never faded
    elif tail_exempt: fd = a27.end_gate_fails(x, r)   # designed abrupt end: only the absolute house end gate
    else: fd = a27.needs_fade(x, r)
    if lim:
        src_lufs = a27.house_lufs_x(x, r)
        x = a27.peak_limited(x, r); flags.append("peak")
        fd = fd or (not loop and a27.end_gate_fails(x, r))
    if fd: flags.append("fade")
    if dc or lim or fd:
        y = a27.faded(x, r) if fd else x
        if a27.has_dc(y):
            body = len(y) - (int(a27.PAD_MS / 1000 * r) if fd else 0)
            y = y.copy(); y[:body] -= y.mean(axis=0) * len(y) / body
            if "dc" not in flags: flags.append("dc")
        tmp = dst.with_suffix(".tmp.wav"); a27.write24(tmp, y, r)
        if not dst.exists() or a27.sha(dst) != a27.sha(tmp): tmp.replace(dst); copied = True
        else: tmp.unlink(); copied = False
    elif not dst.exists() or a27.sha(dst) != a27.sha(src):
        shutil.copyfile(src, dst); copied = True
    else:
        copied = False
    return flags, src_lufs, copied


def run(root, parts=PARTS, ap029=AP029, overlay=False):
    root, ap029 = Path(root), Path(ap029)
    assets = root / AUDIO
    out_dir = assets / OUT_DIR
    man_path = assets / OUT_MANIFEST
    ap011 = {}
    p011 = assets / "AP011-audio-manifest.json"
    if p011.is_file():
        ap011 = {e["sound"]: e for e in json.loads(p011.read_text(encoding="utf-8"))["entries"]}
    ap027 = {}
    p027 = assets / "AP027-audio-manifest.json"
    if p027.is_file():
        ap027 = {e["sound"]: e for e in json.loads(p027.read_text(encoding="utf-8"))["entries"]}
    ids = sound_ids(root)

    todo, skipped = [], []
    for part in parts:
        d = ap029 / part
        if not (d / "MANIFEST.json").is_file():
            skipped.append(part)
            continue
        todo.append((part, d))
    if not todo:
        return None, {"skipped": skipped}

    out_dir.mkdir(parents=True, exist_ok=True)
    man = json.loads(man_path.read_text(encoding="utf-8")) if man_path.exists() else {"version": "", "package": "AP-029", "standard": {}, "entries": []}
    done_parts = {p for p, _ in todo}
    if overlay:
        # a later export version (v002 …) replaces the rows of the sounds it carries, sound by sound (each such row lists that sound's
        # full file set, S-08 08.10); every other row of the part stays as the earlier version imported it
        carried = set()
        for part, d in todo:
            carried |= {sound for sound, _, _ in groups_of(part, json.loads((d / "MANIFEST.json").read_text(encoding="utf-8")), read_sums(d))}
        keep = [e for e in man.get("entries", []) if e.get("sound") not in carried]
    else:
        keep = [e for e in man.get("entries", []) if e.get("part") not in done_parts]
    new, warnings, copied, log = [], [], 0, []
    sources = dict(man.get("source_manifests", {}))
    for part, d in todo:
        src_man_path = d / "MANIFEST.json"
        src = json.loads(src_man_path.read_text(encoding="utf-8"))
        sources[part + ("@" + ap029.name if overlay else "")] = SRC_LABEL.replace("v001", ap029.name) + part + "/MANIFEST.json (" + a27.sha(src_man_path)[:12] + ", " + str(src.get("version", "")) + ")"
        sums = read_sums(d)
        listed = set()
        exempt = set((src.get("tailExempt") or {}).keys())
        for sound, files, e in groups_of(part, src, sums):
            per_sha = {pf["file"]: pf.get("sha256") for pf in (e or {}).get("perFile", []) if isinstance(pf, dict) and "file" in pf}
            old = ap011.get(sound) if part == "special" else ap027.get(sound) if part == "fire" else None
            if part in ("fire", "special"):
                if old is None: warnings.append(f"{part} {sound}: no {'AP-011' if part == 'special' else 'AP-027'} row to supersede")
                elif len(old["files"]) != len(files): warnings.append(f"{part} {sound}: {len(files)} files, the superseded row has {len(old['files'])}")
            if ids is not None and sound not in ids: warnings.append(f"{part} {sound}: not an AudioSoundId name (the catalog builder skips it)")
            loop = bool(e.get("loop", old.get("loop", False) if old else False)) if e else False
            weapon = e.get("weapon") if e else None
            ms, flags_by = [], {}
            for f in files:
                listed.add(f)
                p = d / f
                assert f in sums, ("not in SHA256SUMS", part, f)
                h = a27.sha(p)
                assert h == sums[f], ("SHA mismatch", part, f)
                if per_sha.get(f): assert h == per_sha[f], ("perFile sha256 mismatch", part, f)
                dst = out_dir / f
                flags, src_lufs, c = import_file(p, dst, loop=loop, tail_exempt=sound in exempt,
                                                 fire_weapon=weapon if part == "fire" else None)
                copied += c
                if flags: flags_by[f] = flags; log.append((part, f, ",".join(flags), h[:12], a27.sha(dst)[:12]))
                x, r = a27.pcm(dst)
                m = measure(dst, x, r)
                m["src_lufs"] = src_lufs
                ms.append(m)
            hl = [m["src_lufs"] if m["src_lufs"] is not None else m["house"] for m in ms]
            stamp_note = ""
            if part == "foley" and sound.startswith("MagDrop") and len(files) > 1:
                # MagazineDropAudio reads this row's catalog stamp (LufsOf = file 1's house LUFS) to lift a row the −1 dBTP ceiling
                # left under −22 (S-08 07.10): file 1 must stand for the set, so the median-loudness RR goes first (the deck shuffles
                # the order anyway). Without it a quiet file 1 would lift every RR of the row (MagDropRifleWood: file 1 −26.9, mean −23.2).
                srt = sorted(hl)
                med = (srt[(len(srt) - 1) // 2] + srt[len(srt) // 2]) / 2.0
                first = min(range(len(hl)), key=lambda i: (abs(hl[i] - med), i))
                if first:
                    order = [first] + [i for i in range(len(files)) if i != first]
                    files, ms, hl = [files[i] for i in order], [ms[i] for i in order], [hl[i] for i in order]
                    stamp_note = " · stamp file = median-loudness RR " + files[0] + " (listed first)"
            st = [m["st"] for m in ms]
            silent = [m["silent"] for m in ms]
            tail = part == "space"
            if part == "fire":
                event, bus, spatial = e.get("event", "ClassicWeaponFire"), e.get("bus", "SFX-Player"), bool(e.get("spatial", True))
            elif part == "space":
                event, bus, spatial = TAIL_EVENT[TAIL_RE.match(files[0])["kind"]], e.get("bus", "SFX-Player"), True
            elif part == "special":
                event = old.get("event", sound) if old else sound
                bus = old.get("bus", "SFX-World") if old else "SFX-World"
                spatial = bool(old.get("spatial", True)) if old else True
            else:
                event = e.get("event") or ("WeaponMechanism.MagazineDropped" if sound.startswith("MagDrop") else "WeaponMechanism.CasingEjected")
                bus, spatial = e.get("bus", "SFX-World"), bool(e.get("spatial", True))
            target = e.get("targetLufs") if e and isinstance(e.get("targetLufs"), (int, float)) else None
            ceiling = e.get("ceilingDbtp") if e and isinstance(e.get("ceilingDbtp"), (int, float)) else -1.0
            fades = [f for f in files if "fade" in flags_by.get(f, [])]
            dcs = [f for f in files if "dc" in flags_by.get(f, [])]
            peaks = [f for f in files if "peak" in flags_by.get(f, [])]
            prov = "AP-029 " + ap029.name + " " + part + " (" + str(src.get("version", "")) + ", SHA " + a27.sha(d / files[0])[:12] + "…)"
            if e and e.get("supersedes"): prov += " · supersedes " + str(e["supersedes"])
            if part == "fire" and old is not None: prov += " · supersedes AP-027 " + sound
            prov += stamp_note
            if fades: prov += " · import fade 10 ms + 20 ms silence on " + str(len(fades)) + " file(s)"
            if dcs: prov += " · import 15 Hz high-pass + DC out on " + ", ".join(dcs)
            if peaks: prov += " · import true-peak shave ≤ " + str(a27.PEAK_CEILING_DB) + " dBTP on " + str(len(peaks)) + " file(s)"
            new.append({
                "sound": sound, "part": part, "weapon": weapon or "", "event": event, "files": list(files), "loop": loop,
                "lufs": round(hl[0], 2), "lufsMean": round(sum(hl) / len(hl), 2), "stereoLufs": round(sum(m["stereo"] for m in ms) / len(ms), 2),
                "truePeakDb": round(max(m["tp"] for m in ms), 2), "bitDepth": 24,
                "targetLufs": round(target if target is not None else hl[0], 2), "ceilingDbtp": float(ceiling),
                "noiseFloorDb": round(st[0][0], 1), "noiseFloorWindowMs": 20, "noiseFloorException": "",
                "dcDb": round(max(v[1] for v in st), 1), "clipped": sum(v[2] for v in st), "endSilenceDb": round(max(v[3] for v in st), 1),
                "continuous": tail or loop or not all(silent),
                "noiseFloorNote": "" if all(silent) else str(sum(not v for v in silent)) + "/" + str(len(silent)) + " file(s) never fall below −60 dBFS: noise floor not measurable, continuous exemption",
                "durationsS": [round(m["dur"], 3) for m in ms], "channels": max(m["ch"] for m in ms),
                "layers": sound + (" · stereo" if any(m["ch"] == 2 for m in ms) else ""),
                "bus": bus, "spatial": spatial,
                "importFade": fades, "importDcRemoved": dcs, "importPeakLimit": peaks,
                "provenance": prov,
            })
        unlisted = sorted(f for f in sums if f.endswith(".wav") and f not in listed)
        if unlisted: warnings.append(f"{part}: {len(unlisted)} delivered file(s) in no manifest entry (not imported): " + ", ".join(unlisted[:6]))

    man["entries"] = sorted(keep + new, key=lambda e: e["sound"])
    names = [e["sound"] for e in man["entries"]]
    dup = sorted({n for n in names if names.count(n) > 1})
    assert not dup, ("sound in two parts", dup)
    man["version"] = "ap029-" + "+".join(sorted({k.split("@")[1] for k in sources if "@" in k} | {"v001"})) + "-unity-" + "-".join(sorted({e["part"] for e in man["entries"]}))
    man["package"] = "AP-029"
    man["source_manifests"] = dict(sorted(sources.items()))
    with open(man_path, "w", encoding="utf-8") as f:
        json.dump(man, f, indent=1, ensure_ascii=False)
    summary = {"parts": [p for p, _ in todo], "skipped": skipped, "entries": len(new), "files": sum(len(e["files"]) for e in new),
               "copied": copied, "processed": [list(t) for t in log], "warnings": warnings, "manifest_entries_total": len(man["entries"])}
    return new, summary


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", help="worktree root (holds prototype/DeathsDemolition_PlayableConcept)")
    parser.add_argument("--parts", default=",".join(PARTS), help="comma list of fire,space,special,foley (default: all)")
    parser.add_argument("--overlay", action="store_true", help="a later version folder (v002 …): replace only the rows of the sounds it carries")
    parser.add_argument("--ap029", default=str(AP029), help="AP-029 export version folder (default: dd_paths KPROJ/asset-production/AP-029/exports/v001)")
    try:
        args = parser.parse_args(argv)
    except SystemExit as e:
        return 0 if e.code == 0 else 64
    parts = [p.strip() for p in args.parts.split(",") if p.strip()]
    bad = [p for p in parts if p not in PARTS]
    if bad or not parts:
        print("unknown part(s): %s (use %s)" % (",".join(bad), ",".join(PARTS)), file=sys.stderr)
        return 64
    if not Path(args.root).is_dir():
        print("worktree root not found: %s" % args.root, file=sys.stderr)
        return 64
    new, summary = run(args.root, parts, args.ap029, overlay=args.overlay)
    if new is None:
        print("no part to import (no MANIFEST.json in: %s)" % ", ".join(summary["skipped"]), file=sys.stderr)
        return 64
    print(json.dumps(summary, indent=1, ensure_ascii=False))
    for e in new: print(e["sound"], len(e["files"]), e["lufs"], e["truePeakDb"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
