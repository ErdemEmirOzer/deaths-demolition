#!/usr/bin/env python3
"""WP-101 R4 · AP-011 v025 → Unity: real-take bullet impact variations + the sr_1 impact layer (S-08 12.10, C-00 approval 12.10).

python3 tools/audio/ap011_v025_import.py <worktree-root> [--src DIR] [--dry-run]

Reads K/asset-production/AP-011/v025/final (tools/dd_paths.py KPROJ by default): MANIFEST.json (files[].file / sound / sha256 /
sources / edit / limiterDb, sounds[].sound / added / new), SHA256SUMS and the WAVs (48 kHz / 24-bit / mono). Writes, under
<worktree-root>/prototype/DeathsDemolition_PlayableConcept/Assets, only:
  1. _Art/Audio/AP011/<Sound>_NN.wav — byte copies, SHA-checked against MANIFEST and SHA256SUMS; a destination with the same
     SHA is left alone, another SHA stops the run (never overwritten).
  2. _Art/Audio/AP011/<Sound>_NN.wav.meta — ap011_r4_import's one-shot template (ChainArc_01.wav.meta: what
     AudioCatalogBuilder.PinImporter pins for a one-shot < 8 s) with GUID = sha256("dd-v025-ap011:" + name)[:32], checked
     against every .meta GUID under Assets/ and Packages/.
  3. _Art/Audio/AP011-audio-manifest.json —
     - every ImpactV3{Surface}{Calibre} row v025 adds to: the new files appended to "files" (the v018 _01.._03 stay first and
       untouched), the worst-of stamps re-taken over all files (truePeakDb, dcDb, endSilenceDb max; clipped sum), "lufs" and
       "noiseFloorDb" stay file 1's (the AP-011 rule: the EditMode test re-measures clips[0]), " · v025 (WP-101): +N real
       Sonniss takes …" appended to provenance once;
     - one new row ImpactSniperCrack (the 18 AP-011 fields in order, SFX-World, spatial, target -22 LUFS: files peak-bound at
       -24.4, the sound gate's level), merged in sound order;
     - "version" → ap011-v025.
     Every row passes ap011_r4_import.gate_failures (the manifest half of the EditMode AP-011 gates) or nothing is written.
Not touched: AudioSoundId.cs (ImpactSniperCrack = 1064 is code, committed with the binding) and DD_AudioCatalog.asset
(AudioCatalogBuilder rebuilds it in the EditMode run: the AP011 manifest hash changed).
Idempotent: a second run writes nothing. Exit 0 done / nothing to do · 1 a check failed · 64 usage.
Test: python3 -B tools/qa/tests/test_ap011_v025_import.py
"""
import argparse, json, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dd_paths  # noqa: E402
import ap011_r4_import as r4  # noqa: E402  (stamps, meta template, GUID index, gate rules, manifest format)

SRC = dd_paths.KPROJ / "asset-production" / "AP-011" / "v025" / "final"
SRC_LABEL = "K/asset-production/AP-011/v025/final/"
VERSION = "ap011-v025"
SALT = "dd-v025-ap011:"
SNIPER = "ImpactSniperCrack"
SNIPER_EVENT = "ProjectileEvents.Impact sr_1 layer (over its Heavy impact)"
# The house SFX floor (EditMode band [-22.1; -15.9]); the files sit at -24.4..-24.25 LUFS-I, peak-bound: at the -1.5 dBTP ceiling
# 6 dB of look-ahead limiting does not reach -22 (the v025 sound gate measured it), the placement gain (1.0) carries the rest.
SNIPER_TARGET = -22.0
TAG = " · v025 (WP-101)"


class Failed(Exception):
    """One or more checks failed; nothing was written. args[0] = the failures."""


def guid_of(name):
    import hashlib
    return hashlib.sha256((SALT + name).encode("utf-8")).hexdigest()[:32]


def plan(root, src=SRC):
    root, src = Path(root), Path(src)
    project = root / r4.PROJECT
    out_dir, man_path = root / r4.OUT_DIR, root / r4.MANIFEST
    bad = []
    man_src = json.loads((src / "MANIFEST.json").read_text(encoding="utf-8"))
    sums = r4.read_sums(src) or {}
    files = man_src["files"]
    by_sound = {}
    for f in files:
        by_sound.setdefault(f["sound"], []).append(f)

    text = man_path.read_text(encoding="utf-8")
    trailing = text[len(text.rstrip("\n")):]
    man = json.loads(text)
    if r4.dump(man, trailing) != text:
        bad.append("AP011 manifest does not round-trip in json.dumps(indent=1, ensure_ascii=False)")
    rows = {e["sound"]: e for e in man["entries"]}

    template = (out_dir / r4.TEMPLATES["oneshot"]).read_text(encoding="utf-8")
    taken = r4.guid_index(project)
    copies, metas = [], []
    for f in files:
        name, p = f["file"], src / f["file"]
        sha = r4.a27.sha(p) if p.is_file() else None
        if sha is None:
            bad.append(f"{name}: missing in {src}")
            continue
        if sha != f["sha256"] or sums.get(name) != sha:
            bad.append(f"{name}: SHA differs from MANIFEST / SHA256SUMS")
        ch, bits, rate, _n = r4.wav_format(p)
        if (ch, bits, rate) != (1, 24, r4.RATE):
            bad.append(f"{name}: {ch} ch / {bits}-bit / {rate} Hz (want mono 24-bit 48 kHz)")
        dst = out_dir / name
        if dst.exists():
            if r4.a27.sha(dst) != sha:
                bad.append(f"{name}: a different file is already in AP011 (never overwritten)")
        else:
            copies.append((p, dst))
        g = guid_of(name)
        want = r4.meta_text(template, g)
        meta = out_dir / (name + ".meta")
        if meta.exists():
            if meta.read_text(encoding="utf-8") != want:
                bad.append(f"{name}.meta exists with other content")
        else:
            others = [str(x) for x in taken.get(g, []) if str(x) != str(meta)]
            if others:
                bad.append(f"{name}: GUID {g} already used by {others[0]}")
            metas.append((meta, want))

    new_rows = {}
    for sound, fl in by_sound.items():
        names = [f["file"] for f in fl]
        if sound == SNIPER:
            row = dict(rows.get(SNIPER, {}))
            all_files = names
        else:
            if sound not in rows:
                bad.append(f"{sound}: no AP011 row to add to")
                continue
            row = dict(rows[sound])
            all_files = list(row["files"]) + [n for n in names if n not in row["files"]]
        st = []
        for n in all_files:
            p = out_dir / n if (out_dir / n).exists() else src / n
            if not p.exists():
                bad.append(f"{sound}: {n} not found")
                continue
            st.append(r4.stamps(p))
        if not st:
            continue
        if sound == SNIPER:
            row = {k: None for k in r4.FIELDS}
            row.update(sound=SNIPER, event=SNIPER_EVENT, loop=False, lufs=round(st[0]["lufs"], 2), noiseFloorDb=round(st[0]["floor"], 1),
                       bitDepth=24, targetLufs=SNIPER_TARGET, ceilingDbtp=r4.CEILING, continuous=False, layers="derived (v025)",
                       bus="SFX-World", spatial=True,
                       provenance=(f"{SRC_LABEL} v025 (WP-101 user note 1, S-08 12.10): the sr_1 layer over its Heavy impact - "
                                   "band-limited N-wave snap + Sonniss PMSFX brick front + 80 Hz punch (-12 dB) + gravel spray, -24 LUFS-I peak-bound "
                                   "(sound gate K/wp-097/evidence/v025-gate-1210); "
                                   + "; ".join(sorted({s.rsplit('/', 1)[-1] for f in fl for s in f['sources']}))))
        row["files"] = all_files
        row["truePeakDb"] = round(max(s["tp"] for s in st), 2)
        row["dcDb"] = round(max(s["dc"] for s in st), 1)
        row["clipped"] = int(sum(s["clipped"] for s in st))
        row["endSilenceDb"] = round(max(s["end"] for s in st), 1)
        if sound != SNIPER and TAG not in row["provenance"]:
            srcs = sorted({s.rsplit('/', 1)[-1] for f in fl for s in f["sources"]})
            row["provenance"] += f"{TAG}: +{len(names)} real Sonniss takes through the v018 calibre transform, levelled to the set ({'; '.join(srcs)})"
        row = {k: row[k] for k in r4.FIELDS}
        bad += r4.gate_failures(row)
        new_rows[sound] = row

    if bad:
        raise Failed(bad)
    changed_rows = [s for s, r in new_rows.items() if rows.get(s) != r]
    merged = dict(man)
    entries = [new_rows.get(e["sound"], e) for e in man["entries"]]
    if SNIPER not in rows and SNIPER in new_rows:
        entries.append(new_rows[SNIPER])
        entries.sort(key=lambda e: e["sound"])
    merged["entries"] = entries
    if changed_rows:
        merged["version"] = VERSION if man.get("version", "") < VERSION else man["version"]
    out = r4.dump(merged, trailing)
    if json.loads(out) != merged:
        raise Failed(["manifest round trip failed"])
    return dict(copies=copies, metas=metas, manifest=(man_path, out if changed_rows else None), rows=changed_rows)


def apply(pl):
    changed = []
    for p, dst in pl["copies"]:
        tmp = dst.with_name("." + dst.name + ".tmp")
        tmp.write_bytes(p.read_bytes())
        meta = dst.with_name(dst.name + ".meta")
        want = next((t for m, t in pl["metas"] if m == meta), None)
        if want is not None and not meta.exists():
            meta.write_text(want, encoding="utf-8")
        tmp.rename(dst)
        changed.append(dst.name)
    for meta, want in pl["metas"]:
        if not meta.exists():
            meta.write_text(want, encoding="utf-8")
            changed.append(meta.name)
    man_path, out = pl["manifest"]
    if out is not None:
        man_path.write_text(out, encoding="utf-8")
        changed.append(man_path.name)
    return changed


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("root")
    ap.add_argument("--src", default=str(SRC))
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args(argv)
    if not (Path(a.root) / r4.PROJECT).is_dir() or not (Path(a.src) / "MANIFEST.json").is_file():
        print("usage: worktree root and a v025 source folder with MANIFEST.json", file=sys.stderr)
        return 64
    try:
        pl = plan(a.root, a.src)
    except Failed as e:
        print("\n".join(e.args[0]), file=sys.stderr)
        return 1
    changed = [] if a.dry_run else apply(pl)
    print(json.dumps(dict(dryRun=a.dry_run, copies=len(pl["copies"]), metas=len(pl["metas"]), rows=pl["rows"],
                          wouldChange=bool(pl["copies"] or pl["metas"] or pl["manifest"][1]), changed=changed), indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
