#!/usr/bin/env python3
"""WP-085 B5 · AP-027 v002 → Unity (C-00 05.10, G4 = direction C).

ap027_import.py <worktree-root> <weapon …> [--ap027 DIR] [--fades-log TSV]      e.g. ar1 sg1 smg1
Reads Codex's manifest (K/asset-production/AP-027/manifest/AP027-manifest-v002.json), picks the runtime layers of the
named weapons (AP027-B-integration.md): Fire, Mechanical, TailOpen / TailCorridor / TailHall, DistanceMid / DistanceFar,
Casing_<w>_{StoneSlate, WetStone, Wood, Dirt, Puddle}, Link_<w>_{same}. Not imported: Body / Sub / MuzzleTransient
(production stems), FireTP / FireFP (offline perspective candidates — the runtime lifts by perspective itself), Gravel
(no SurfaceKind), Cock / Pump (slice 2). Each file's SHA-256 must equal the manifest's; it is copied to
Assets/_Art/Audio/AP027/<name>.wav and listed in Assets/_Art/Audio/AP027-audio-manifest.json (AP011 schema, merged:
other weapons' entries are kept). Sound ids: Weapon_ar1_Fire → WeaponAr1Fire, Casing_ar1_Dirt → CasingAr1Dirt,
Link_lmg1_Dirt → LinkLmg1Dirt.

Moved into the repository (C-00 06.10) from K/wp-085/tools/ap027_import.py (sha256 4746f3ff3bd7…) so the manifest's
measured truePeakDb / codexLufs can be reproduced from main; the output is byte-identical to that script's. Two changes:
  --ap027 DIR      the AP-027 package root; default tools/dd_paths.py KPROJ/asset-production/AP-027 (the one KADRO root,
                   was a Path.home() literal). The manifest's source_manifest keeps the K/ label: it names the package by
                   its place in KADRO and its sha256, never the machine path it was read from.
  --fades-log TSV  the import-fade record (file, Codex sha12, imported sha12; rows of re-imported files replaced, others
                   kept) is written only when asked — the script wrote K/wp-085/evidence/ap027-import-fades.tsv and created
                   <worktree-root>/../../evidence on every run; the tool now writes nothing outside <worktree-root>.
Exit 0 imported (summary on stdout) · 1 SHA mismatch (AssertionError: v002 file, overlay base or overlay file; files
copied before it stay, the manifest is not written) · 64 usage / no worktree root / no AP-027 manifest.
Test: python3 tools/qa/tests/test_ap027_import.py (synthetic AP-027 tree, numpy + scipy).
"""
import argparse, hashlib, json, shutil, sys, wave
from collections import defaultdict
from pathlib import Path
import numpy as np
import scipy.signal as ss

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dd_paths  # noqa: E402  (WP-089: the one KADRO root for the Python tools)

AP = dd_paths.KPROJ / "asset-production" / "AP-027"   # default --ap027
SRC_MANIFEST = "manifest/AP027-manifest-v002.json"    # under the AP-027 root
SRC_MANIFEST_LABEL = "K/asset-production/AP-027/" + SRC_MANIFEST   # what the Unity manifest names (+ sha12), wherever it was read
# C-00 05.10: v003-recut = natural end decay on the files S-08 found cut (same names, overlay on v002). Rule: overlay wins,
# except ar1 Mechanical (its recut pulled in the bolt-return transient; it stays v002 until v003b).
# C-00 05.10 (final): v002 → v003-recut (ar1 Mechanical included: its 114 ms carries the real bolt-return) → v003b (ar1
# Mechanical excluded) → v003c (ar1 / lmg1 DistanceFar_04/_06 only; ar1 Mechanical excluded) → v003d (SG far, when it lands).
# v003d = ar1 Mechanical as v003-recut's copy (supersedes the dropped v003c one); v003e = sg1 / sg2 DistanceFar (SPAS 50 m proxy).
# v003f = TailCorridor / TailHall lengthened to the spec RT60 (hall ~1.7 s, corridor ~1.25 s; C-00 05.10, supersedes v002).
# Overlay folders under <AP-027>/exports, applied in this order.
OVERLAYS = ["v003-recut", "v003b-recut", "v003c-recut", "v003d-recut", "v003e-recut",
            "v003f-recut", "v003g-recut"]   # v003g = all three tail classes phase-aligned 400–900 Hz (mono notch ≤ −6 dB)
OVERLAY_EXCLUDE = {"v003b-recut": ("Weapon_ar1_Mechanical_",), "v003c-recut": ("Weapon_ar1_Mechanical_",)}
# C-00 05.10: peak-limited, ~3.5 dB quiet — out of the random pool (the file stays in the export).
POOL_EXCLUDE = ("Weapon_lmg2_Fire_01.wav",)
# Named, reasoned exceptions to the house noise-floor stamp gate (−60 dBFS); the EditMode gate honours and logs them.
NOISE_FLOOR_EXCEPTION = {
    "WeaponAr1Mechanical": "v003-recut 114 ms bolt-return (C-00 05.10): the recording's own floor −59 dBFS in a −55 LUFS stem played "
                           "≥ 10 dB under Fire (Mechanical/Body −10/−7/−3) — ≤ −69 dBFS in the mix",
}
WEAPON_LAYERS = ["Fire", "Mechanical", "TailOpen", "TailCorridor", "TailHall", "DistanceMid", "DistanceFar"]
SURFACES = ["StoneSlate", "WetStone", "Wood", "Dirt", "Puddle"]
EVENT = {"Fire": "ClassicWeaponFire", "Mechanical": "ClassicWeaponFire", "DistanceMid": "ClassicWeaponFire",
         "DistanceFar": "ClassicWeaponFire", "TailOpen": "ClassicWeaponRelease", "TailCorridor": "ClassicWeaponRelease",
         "TailHall": "ClassicWeaponRelease"}


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def pcm(p):
    w = wave.open(str(p)); n = w.getnframes(); ch = w.getnchannels(); sw = w.getsampwidth()
    b = np.frombuffer(w.readframes(n), np.uint8)
    assert sw == 3, p
    b = b.reshape(-1, 3); x = b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16)
    x = np.where(x >= 1 << 23, x - (1 << 24), x) / float(1 << 23)
    return x.reshape(-1, ch), w.getframerate()


FADE_MS, FADE_WITHIN_DB, PAD_MS = 10.0, 40.0, 20.0   # 20 ms pad: Vorbis smears ~10 ms into a 10 ms pad (smg1 DistanceFar_01 −57.9)


def write24(p, x, r):
    q = np.clip(np.round(x * (1 << 23)), -(1 << 23), (1 << 23) - 1).astype(np.int32)
    q = np.where(q < 0, q + (1 << 24), q).astype(np.uint32)
    b = np.stack([(q & 0xFF), (q >> 8) & 0xFF, (q >> 16) & 0xFF], -1).astype(np.uint8).reshape(-1).tobytes()
    w = wave.open(str(p), "wb"); w.setnchannels(x.shape[1]); w.setsampwidth(3); w.setframerate(r); w.writeframes(b); w.close()


def needs_fade(x, r):
    """The file stops while sounding (last 5 ms within 40 dB of the peak), or its last 10 ms break the house end gate
    (AudioSliceEditModeTests UstKalite: manifest endSilenceDb ≤ −70, shipped clip ≤ −60 dBFS RMS over 10 ms)."""
    m = np.abs(x).max(1)
    rel = 20 * np.log10(m[-int(0.005 * r):].max() + 1e-12) - 20 * np.log10(m.max() + 1e-12)
    tail = x[-int(0.010 * r):].mean(1)
    return rel > -FADE_WITHIN_DB or 20 * np.log10(np.sqrt(np.mean(tail ** 2)) + 1e-12) > -70.0   # the manifest end gate


def faded(x, r):
    """Import-time end fade (WP-085 B5, C-00 05.10): the v002 file stops while still sounding; a 10 ms raised-cosine fade
    removes the click and 10 ms of silence closes it (the shortened body stays — Codex v003 re-cut asked for)."""
    n = int(FADE_MS / 1000 * r); y = x.copy()
    y[-n:] *= (0.5 * (1 + np.cos(np.linspace(0, np.pi, n))))[:, None]
    return np.concatenate([y, np.zeros((int(PAD_MS / 1000 * r), x.shape[1]))])   # + 20 ms of digital silence: the end gate's window past the codec


def has_dc(x):
    return 20 * np.log10(abs(x.mean()) + 1e-12) > -60.0


def dc_removed(x, r):
    """Import-time subsonic / DC clean-up (WP-085 B5): zero-phase 15 Hz high-pass, then the residual mean out (house gate
    DC ≤ −60 dBFS; e.g. Weapon_smg1_Fire_06 carried −35 dBFS below 10 Hz)."""
    y = ss.sosfiltfilt(ss.butter(2, 15, "highpass", fs=r, output="sos"), x, axis=0)
    return y - y.mean(axis=0)


def stamps(p):
    """The AP-011 manifest stamps (AudioCatalogBuilder v003 quality gate): quietest 100 ms window (dBFS, mono), DC (dBFS),
    clipped samples, level of the last 10 ms (dBFS)."""
    x, r = pcm(p); m = x.mean(axis=1)
    # AP-027 one-shots are 0.2–0.8 s: the quietest 20 ms window (AP-011's 100 ms cannot fit a background stretch in them).
    w = min(len(m), r // 50); n = max(1, len(m) // w)
    floor = min(np.sqrt(np.mean(m[i * w:(i + 1) * w] ** 2)) for i in range(n))
    tail = m[-max(1, r // 100):]
    db = lambda v: float(20 * np.log10(abs(v) + 1e-12))
    return db(floor), db(m.mean()), int((np.abs(x) >= 0.999).sum()), db(np.sqrt(np.mean(tail ** 2)))


def house_lufs(p):
    """The house stamp (AP-011 manifests, Unity LoudnessMeter): BS.1770 K-weighting on the MONO MEAN, 400 ms / 100 ms blocks,
    −70 absolute and −10 relative gates, a clip shorter than 400 ms zero-padded. Codex stamps stereo tails as a channel SUM
    (≈ +6 dB for a decorrelated tail): kept apart as codexLufs."""
    x, r = pcm(p); m = x.mean(1)
    if len(m) < int(0.4 * r): m = np.concatenate([m, np.zeros(int(0.4 * r) - len(m))])
    b1, a1 = [1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585]
    b2, a2 = [1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621]
    z = ss.lfilter(b2, a2, ss.lfilter(b1, a1, m)) ** 2
    n, h = int(0.4 * r), int(0.1 * r)
    ms = np.array([z[i:i + n].mean() for i in range(0, len(z) - n + 1, h)])
    l = -0.691 + 10 * np.log10(ms + 1e-20); g = ms[l > -70]
    if len(g) == 0: return -120.0
    rel = -0.691 + 10 * np.log10(g.mean()) - 10; g2 = ms[(l > -70) & (l > rel)]
    return float(-0.691 + 10 * np.log10(g2.mean()))


def true_peak_db(p):
    """BS.1770-4 true peak of the shipped file (S-08 06.10, replaces the v002 Codex estimate `true_peak_db_est`): 4×
    oversampling per channel (scipy polyphase FIR), the largest |sample| in dBTP."""
    x, r = pcm(p)
    return float(20 * np.log10(np.abs(ss.resample_poly(x, 4, 1, axis=0)).max() + 1e-12))


def stereo_lufs(p):
    """BS.1770 integrated loudness of the shipped file as Codex stamps it (S-08 06.10, replaces the v002 `lufs_gated_est`):
    K-weighting per channel, channel powers SUMMED (G = 1 for L and R), 400 ms / 100 ms blocks, −70 absolute and −10
    relative gates, a clip shorter than 400 ms zero-padded. A mono file is its one channel."""
    x, r = pcm(p)
    if len(x) < int(0.4 * r): x = np.concatenate([x, np.zeros((int(0.4 * r) - len(x), x.shape[1]))])
    b1, a1 = [1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585]
    b2, a2 = [1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621]
    z = (ss.lfilter(b2, a2, ss.lfilter(b1, a1, x, axis=0), axis=0) ** 2).sum(axis=1)
    n, h = int(0.4 * r), int(0.1 * r)
    ms = np.array([z[i:i + n].mean() for i in range(0, len(z) - n + 1, h)])
    l = -0.691 + 10 * np.log10(ms + 1e-20); g = ms[l > -70]
    if len(g) == 0: return -120.0
    rel = -0.691 + 10 * np.log10(g.mean()) - 10; g2 = ms[(l > -70) & (l > rel)]
    return float(-0.691 + 10 * np.log10(g2.mean()))


def has_silent_stretch(p):
    """Any 20 ms window of the delivered file below −60 dBFS RMS — else the file is signal end to end (the noise floor is
    not measurable from it; AP-011's `continuous` exemption, stated in noiseFloorNote)."""
    x, r = pcm(p); m = x.mean(1); w = int(0.02 * r)
    return any(np.sqrt(np.mean(m[i:i + w] ** 2)) < 10 ** (-60 / 20) for i in range(0, max(1, len(m) - w), w // 2))


def sound_name(cat, w, layer):
    return cat + w[0].upper() + w[1:] + layer


def run(root, weapons, ap=AP, fades_log=None):
    """Import the named weapons from the AP-027 root `ap` into the worktree `root`; returns the summary printed by main."""
    root, ap = Path(root), Path(ap)
    src_manifest = ap / SRC_MANIFEST
    assets = root / "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Audio"
    out_dir = assets / "AP027"; out_dir.mkdir(parents=True, exist_ok=True)
    with open(src_manifest, encoding="utf-8") as f:
        src = json.load(f)
    ovs = []
    for d in (ap / "exports" / v for v in OVERLAYS):
        if (d / "MANIFEST.json").exists():
            with open(d / "MANIFEST.json", encoding="utf-8") as f:
                ovs.append((d, {it["file"]: it for it in json.load(f)["items"]}))
    ov_used = []
    groups = defaultdict(list)
    for it in src["items"]:
        cat, w, layer = it["role"].split("_", 2)
        if w not in weapons: continue
        if cat == "Weapon" and layer not in WEAPON_LAYERS: continue
        if cat in ("Casing", "Link") and layer not in SURFACES: continue
        groups[(cat, w, layer)].append(it)

    man_path = assets / "AP027-audio-manifest.json"
    if man_path.exists():
        with open(man_path, encoding="utf-8") as f:
            man = json.load(f)
    else:
        man = {"version": "", "package": "AP-027", "standard": {}, "entries": []}
    keep = [e for e in man["entries"] if e.get("weapon") not in weapons]
    new, copied, fades, dcs = [], 0, [], []
    for (cat, w, layer), items in sorted(groups.items()):
        items.sort(key=lambda x: x["path"])
        files, lufs, tps, chans, st, silent, hl = [], [], [], set(), [], [], []
        for it in items:
            p = ap / it["path"]
            assert sha(p) == it["sha256"], ("SHA mismatch", it["path"])
            if p.name in POOL_EXCLUDE: continue
            rel = it["path"].split("exports/v002/", 1)[1]
            cur = it["sha256"]
            for d, ov in ovs:
                o = ov.get(rel)
                if o is None or p.name.startswith(OVERLAY_EXCLUDE.get(d.name, ())): continue
                base = o.get("superseded_sha256") or o.get("source_v002_sha256")
                if base != cur and o["sha256"] == cur: continue   # same content already in place (v003d's ar1 Mechanical copy)
                assert base == cur, ("overlay base mismatch", d.name, rel)
                p = d / rel
                cur = sha(p)
                assert cur == o["sha256"], ("overlay SHA mismatch", d.name, rel)
                ov_used.append((p.name, d.name))
            silent.append(has_silent_stretch(p))
            dst = out_dir / p.name
            x, r = pcm(p)
            dc = has_dc(x)
            if dc: x = dc_removed(x, r); dcs.append(p.name)
            if dc or needs_fade(x, r):
                y = faded(x, r) if needs_fade(x, r) else x
                if has_dc(y):   # the fade can move a short clip's mean: take it out of the body, the silence pad stays 0
                    body = len(y) - (int(PAD_MS / 1000 * r) if needs_fade(x, r) else 0)
                    y = y.copy(); y[:body] -= y.mean(axis=0) * len(y) / body
                    if p.name not in dcs: dcs.append(p.name)
                tmp = dst.with_suffix(".tmp.wav"); write24(tmp, y, r)
                if not dst.exists() or sha(dst) != sha(tmp): tmp.replace(dst); copied += 1
                else: tmp.unlink()
                fades.append((p.name, sha(p)[:12], sha(dst)[:12]))
            elif not dst.exists() or sha(dst) != sha(p):
                shutil.copyfile(p, dst); copied += 1
            files.append(p.name)
            p = dst   # stamps on what ships
            s = it["specs"]; chans.add(s["channels"])
            lufs.append(stereo_lufs(p)); tps.append(true_peak_db(p))   # measured on what ships (S-08 06.10; were the v002 estimates)
            st.append(stamps(p))
            hl.append(house_lufs(p))
        auth = items[0].get("source_authenticity", {})
        new.append({
            "sound": sound_name(cat, w, layer), "weapon": w, "event": EVENT.get(layer, "WeaponMechanism.CasingEjected"),
            "files": files, "loop": False, "lufs": round(hl[0], 2), "codexLufs": round(sum(lufs) / len(lufs), 2), "truePeakDb": round(max(tps), 2),
            "bitDepth": 24, "targetLufs": round(hl[0], 2), "ceilingDbtp": -1.0,
            "noiseFloorDb": round(st[0][0], 1), "noiseFloorWindowMs": 20,
            "noiseFloorException": NOISE_FLOOR_EXCEPTION.get(sound_name(cat, w, layer), ""), "dcDb": round(max(v[1] for v in st), 1), "clipped": sum(v[2] for v in st),
            "endSilenceDb": round(max(v[3] for v in st), 1), "continuous": layer.startswith("Tail") or not all(silent),
            "noiseFloorNote": "" if all(silent) else str(sum(not v for v in silent)) + "/" + str(len(silent)) + " file(s) never fall below −60 dBFS (cut body): noise floor not measurable, continuous exemption",
            "layers": items[0]["role"] + (" · stereo" if 2 in chans else ""),
            "bus": "SFX-Player" if cat == "Weapon" else "SFX-World", "spatial": True,
            "overlay": sorted({f + " " + v for f, v in ov_used if f in files}),
            "importFade": [f for f, *_ in fades if f in files],
            "importDcRemoved": [f for f in dcs if f in files],
            "provenance": "AP-027 v002 (Codex, Sonniss GDC sources, " + ", ".join(sorted({i for it in items for i in it.get("source_license_ids", [])})) + "; "
                          + ("proxy surface/caliber" if auth.get("caliber_surface_proxy") else "source recording") + ") · SHA " + items[0]["sha256"][:12] + "…",
        })

    man["entries"] = sorted(keep + new, key=lambda e: e["sound"])
    man["version"] = "ap027-v002-unity-" + "-".join(sorted({e["weapon"] for e in man["entries"]}))
    man["package"] = "AP-027"
    man["source_manifest"] = SRC_MANIFEST_LABEL + " (" + sha(src_manifest)[:12] + ")"
    for e in man["entries"]:
        base = e["provenance"].split(" · v003-recut")[0].split(" · import")[0]
        if e.get("overlay"):
            for v in ("v003-recut", "v003b-recut", "v003c-recut", "v003d-recut", "v003e-recut", "v003f-recut", "v003g-recut"):
                n = sum(1 for x in e["overlay"] if x.endswith(" " + v) and not any(y.startswith(x.split(" ")[0] + " v003b") for y in e["overlay"]) ) if v == "v003-recut" else sum(1 for x in e["overlay"] if x.endswith(" " + v))
                if n: base += " · AP-027 " + v + " on " + str(n) + " file(s)"
        if e.get("importFade"): base += " · import fade 10 ms + 20 ms silence on " + str(len(e["importFade"])) + " file(s) (end within 40 dB of peak / end gate)"
        if e.get("importDcRemoved"): base += " · import 15 Hz high-pass + DC out on " + ", ".join(e["importDcRemoved"])
        e["provenance"] = base
    with open(man_path, "w", encoding="utf-8") as f:
        json.dump(man, f, indent=1, ensure_ascii=False)
    if fades_log:   # only when asked (was a fixed K/wp-085/evidence path): the tool writes nothing outside <worktree-root> by itself
        fl = Path(fades_log); fl.parent.mkdir(parents=True, exist_ok=True)
        old = fl.read_text(encoding="utf-8").splitlines() if fl.exists() else ["file\tcodex_sha12\timported_sha12"]
        names = {f for f, *_ in fades}
        fl.write_text("\n".join([old[0]] + [l for l in old[1:] if l.split("\t")[0] not in names] + ["\t".join(t) for t in fades]) + "\n", encoding="utf-8")
    return {"weapons": weapons, "overlay": {v: sum(1 for _, d in ov_used if d == v) for v in ("v003-recut", "v003b-recut", "v003c-recut", "v003d-recut", "v003e-recut", "v003f-recut", "v003g-recut")}, "faded": len(fades), "dc_removed": dcs, "entries": len(new), "files": sum(len(e["files"]) for e in new), "copied": copied,
            "manifest_entries_total": len(man["entries"]), "new": [(e["sound"], len(e["files"]), e["lufs"], e["truePeakDb"]) for e in new]}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", help="worktree root (holds prototype/DeathsDemolition_PlayableConcept)")
    parser.add_argument("weapons", nargs="+", help="weapon ids, e.g. ar1 sg1 smg1")
    parser.add_argument("--ap027", default=str(AP), help="AP-027 package root (default: dd_paths KPROJ/asset-production/AP-027)")
    parser.add_argument("--fades-log", help="TSV of the import fades (file, codex_sha12, imported_sha12); not written when omitted")
    try:
        args = parser.parse_intermixed_args(argv)   # the options may stand between or after the weapons
    except SystemExit as e:
        return 0 if e.code == 0 else 64
    if not Path(args.root).is_dir():
        print("worktree root not found: %s" % args.root, file=sys.stderr)
        return 64
    if not (Path(args.ap027) / SRC_MANIFEST).is_file():
        print("AP-027 manifest not found: %s" % (Path(args.ap027) / SRC_MANIFEST), file=sys.stderr)
        return 64
    summary = run(args.root, args.weapons, args.ap027, args.fades_log)
    new = summary.pop("new")
    print(json.dumps(summary, indent=1))
    for sound, n, lufs, tp in new: print(sound, n, lufs, tp)
    return 0


if __name__ == "__main__":
    sys.exit(main())
