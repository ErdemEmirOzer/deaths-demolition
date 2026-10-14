#!/usr/bin/env python3
"""WP-097 R4 · AP-011 v024 → Unity, data layer only (S-08 delivery 11.10, contract K/wp-097/r4-bind/IDS.md 12.10).

python3 tools/audio/ap011_r4_import.py <worktree-root> [--src DIR] [--dry-run] [--known-guids FILE]

Reads the R4 sound set (DIR = K/asset-production/AP-011/v024/final by default, tools/dd_paths.py KPROJ): MANIFEST.json
(entries[].id / sound / bus / target_lufs / loop / files[].file / sha256 / lufs / tp_dbtp / limiter_gr_db / edits / origin),
SHA256SUMS and the WAVs (<Sound>_NN.wav, 48 kHz / 24-bit / mono). The R4 table below is the contract (AudioSoundId name, value,
v024 id, bus, loop, event): the source manifest must carry exactly these 33 sounds with the same v024 id, loop flag and bus
family (Interface = UI, SFX = SFX-World), files numbered _01, _02 … in order.

Writes, under <worktree-root>/prototype/DeathsDemolition_PlayableConcept/Assets, only:
  1. _Art/Audio/AP011/<Sound>_NN.wav — byte copies. Each source SHA-256 must equal its MANIFEST files[].sha256 and its
     SHA256SUMS line. A destination with the same SHA is left alone; one with another SHA stops the run (never overwritten).
  2. _Art/Audio/AP011/<Sound>_NN.wav.meta — the text of an existing AP011 .meta with only its guid line changed. The template
     is the one whose importer settings are what AudioCatalogBuilder.PinImporter pins for that file, so the first catalog
     build reimports nothing: one-shot < 8 s → ChainArc_01.wav.meta (SFX: Vorbis q0.8, DecompressOnLoad, preload); loop < 8 s
     → SwArcSphereLoop.wav.meta (Vorbis q0.7, DecompressOnLoad, preload: every Amb* loop on main is ≥ 8 s and streamed, which
     PinImporter would undo on a short loop); ≥ 8 s, loop or not (PinImporter's longClip) → AmbBambooGroveLoop.wav.meta
     (Amb* loop: Vorbis q0.7, Streaming, load in background). The template's settings are checked against that rule.
     GUID = the first 32 hex digits of sha256("dd-r4-ap011:" + "<Sound>_NN.wav") — deterministic. It must not be the guid
     of any other .meta under Assets/ (or Packages/) nor one listed in --known-guids. An existing .meta with the same text is
     left alone; any other existing .meta for the file stops the run.
  3. _Art/Audio/AP011-audio-manifest.json — one row per R4 sound in the AP-011 schema (the 18 fields every row has, in
     their order); rows merged in sound order (the file is sorted), rows of the same 33 sounds replaced (the tool owns them),
     every other row untouched; "version" → ap011-v024 (a later ap011-vNNN is kept). Written as json.dumps(indent=1,
     ensure_ascii=False) plus the file's own trailing newline (main has none); the run stops if the file does not round-trip
     in that format before the merge, and the output is round-tripped again before it is written.
     Stamps, measured on the shipped bytes with ap027_import's house functions (the AP-011 / AP-027 / AP-029 house stamp):
       lufs          BS.1770 integrated of file 1 (AP-011 rule, ap011_manifest.py: "lufs = the FIRST variation's stamp
                     (the EditMode test re-measures clips[0]); peak = worst of all")
       truePeakDb    the largest 4x-oversampled true peak over the files
       noiseFloorDb  file 1's quietest 100 ms window (AudioCatalogBuilder.ManifestEntry: "quietest 100 ms window of file 1")
       dcDb / clipped / endSilenceDb   the largest DC, the summed |x| >= 0.999 samples, the largest last-10 ms RMS (dBFS)
       continuous    a loop, or file 1 never rests under -60 dBFS in a 100 ms window (its floor is not measurable: the
                     AP-011 continuous exemption, named in the row's provenance)
       bitDepth 24 · targetLufs = MANIFEST target_lufs · ceilingDbtp = MANIFEST ceiling_dbtp (house -1.5; TreeShieldBreak -2.5, codec margin) · bus / spatial / event from the R4 table ·
       layers / provenance from the MANIFEST origin (external take / synthesis / derived), edits and limiter gain, v024.
     The rows must pass the manifest half of the EditMode AP-011 gates (AudioSliceEditModeTests Katalog_ManifestLufsVeTepe /
     UstKalite: target band, ceiling, level vs target unless peak-bound, true peak, 24-bit, clipped 0, DC <= -60, floor
     <= -60 unless continuous, end <= -70 unless loop) — else the run stops.
  4. _Prototype/Audio/Library/AudioSoundId.cs — the 33 members, each with one `/// <summary>` line (R4, v024 id, event) and a
     blank line after it, as one block under a `// ----` header, inserted before the last member's doc comment: the last
     member (WeaponSlap = 990) stays last and byte-identical, the diff is insert-only. All 33 present with their values =
     no change; a partial set, another value for one of the names, or another name on one of the values stops the run.

Not touched: Assets/_Prototype/Audio/Resources/DD_AudioCatalog.asset. AudioCatalogBuilder.BuildCatalog rebuilds it in Unity
(DD › Audio › Build Catalog + Region Map, or -executeMethod DD.Prototype.Audio.EditorTools.AudioCatalogBuilder.BuildAllBatch):
its _sourceHash starts with the SHA-256 of AP011-audio-manifest.json, so the changed manifest is out of date and is rebuilt
with the R4 rows (PinImporter finds the .meta settings already pinned). Commit the rebuilt catalog with the import; until
then the catalog has no R4 row.

Every check and measurement runs first; any failure → exit 1 with every failure on stderr and nothing written. Unity must
not be open on the worktree while the tool writes (a WAV without its .meta would get a random GUID): each WAV is copied to
a hidden temp name (Unity ignores dot files), its .meta is written, then the WAV is renamed into place.
Idempotent: a second run writes nothing (summary "changed": []).
  --dry-run          every check and measurement; prints what would change; writes nothing
  --known-guids FILE extra GUIDs counted as taken (every 32-hex token in the file, e.g.
                     --known-guids <(git -C <repo> grep -h '^guid:' main -- '*.meta')), for a check against a tree other
                     than the worktree (a full worktree is already covered by the Assets/ scan)
Exit 0 imported / nothing to do (summary on stdout) · 1 a check failed · 64 usage / no worktree root / no source manifest.
Test: python3 -B tools/qa/tests/test_ap011_r4_import.py (synthetic v024 package, numpy + scipy).
"""
import argparse, hashlib, json, os, re, sys, wave
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dd_paths  # noqa: E402  (WP-089: the one KADRO root for the Python tools)
import ap027_import as a27  # noqa: E402  (sha, pcm, house_lufs, true_peak_db: the house stamp)

SRC = dd_paths.KPROJ / "asset-production" / "AP-011" / "v024" / "final"   # default --src
SRC_LABEL = "K/asset-production/AP-011/v024/final/"   # what the Unity manifest names, wherever it was read
PROJECT = "prototype/DeathsDemolition_PlayableConcept"
AUDIO = PROJECT + "/Assets/_Art/Audio"
OUT_DIR = AUDIO + "/AP011"
MANIFEST = AUDIO + "/AP011-audio-manifest.json"
ENUM = PROJECT + "/Assets/_Prototype/Audio/Library/AudioSoundId.cs"
CATALOG = PROJECT + "/Assets/_Prototype/Audio/Resources/DD_AudioCatalog.asset"   # never written (see above)
VERSION = "ap011-v024"
GUID_SALT = "dd-r4-ap011:"
RATE = 48000
CEILING = -1.5
FLOOR_DB = -60.0
LONG_CLIP_S = 8.0   # AudioCatalogBuilder.PinImporter: probe.length >= 8f → streamed
FIELDS = ("sound", "event", "files", "loop", "lufs", "truePeakDb", "noiseFloorDb", "dcDb", "clipped", "bitDepth", "targetLufs",
          "ceilingDbtp", "continuous", "endSilenceDb", "layers", "bus", "spatial", "provenance")

# The contract (K/wp-097/r4-bind/IDS.md, S-08 12.10): AudioSoundId name, value, v024 id, manifest bus, loop, event (verbatim).
# Bus: IDS "UI" → UI; IDS "SFX" (wall-buy stations are positional world sounds, the tree procs happen in the world) → SFX-World;
# the player-centred tree sounds (shield, life drain, dodge, undying oath, endless window, aura) → SFX-Player, 2D (S-08 12.10).
R4 = (
    ("WallBuyCoins", 1000, "wb_coins", "SFX-World", False, "WallBuyEvents.Resolved Bought/Refilled (wp-107-wallbuy)"),
    ("WallBuyRack", 1001, "wb_rack", "SFX-World", False, "Resolved Bought (silah)"),
    ("WallBuyDenied", 1002, "wb_denied", "SFX-World", False, "Resolved Denied"),
    ("WallBuyHeavy", 1003, "wb_heavy", "SFX-World", False, "Resolved Bought, roketatar istasyonu"),
    ("WallBuyPrompt", 1004, "wb_prompt", "UI", False, "PromptChanged false→true"),
    ("ModeSelectHard", 1010, "mode_hard", "UI", False, "zorluk seçimi Hard (wp-102-difficulty)"),
    ("ModeSelectDemolition", 1011, "mode_demolition", "UI", False, "zorluk seçimi Demolition"),
    ("TreeOpen", 1020, "tree_open", "UI", False, "ağaç ekranı açılışı (S-09)"),
    ("TreeStamp", 1021, "tree_stamp", "UI", False, "TreeEvents.NodePurchased"),
    ("TreeSynergy", 1022, "tree_synergy", "UI", False, "TreeEvents.SynergyUnlocked"),
    ("TreeReroll", 1023, "tree_reroll", "UI", False, "öneri şeridi reroll (S-09)"),
    ("TreeFireIgnite", 1030, "fx_fire_ignite", "SFX-World", False, "TreeProcKind.Ignite"),
    ("TreeFireLoop", 1031, "fx_fire_loop", "SFX-World", True, "TreeProcKind.FireGround"),
    ("TreeIceFreeze", 1032, "fx_ice_freeze", "SFX-World", False, "TreeProcKind.Freeze"),
    ("TreeIceShatter", 1033, "fx_ice_shatter", "SFX-World", False, "TreeProcKind.Shatter"),
    ("TreePoisonLoop", 1034, "fx_poison", "SFX-World", True, "TreeProcKind.VenomCloud"),
    ("TreeThunder", 1035, "fx_thunder", "SFX-World", False, "TreeProcKind.ShockChain (Thunder Lord; zincirin kendisi mevcut ChainArc)"),
    ("TreeElementNova", 1036, "fx_cataclysm", "SFX-World", False, "TreeProcKind.ElementNova"),
    ("TreeCrit", 1040, "fx_crit", "SFX-World", False, "TreeProcKind.Crit / CritRing"),
    ("TreeMiniNuke", 1041, "fx_mininuke", "SFX-World", False, "TreeProcKind.MiniNuke"),
    ("TreeBlackHoleLoop", 1042, "fx_blackhole_loop", "SFX-World", True, "TreeProcKind.BlackHoleOpen (2,5 sn)"),
    ("TreeBlackHoleCollapse", 1043, "fx_blackhole_collapse", "SFX-World", False, "TreeProcKind.BlackHoleCollapse"),
    ("TreeDoomsdayBell", 1044, "fx_doomsday_bell", "SFX-World", False, "TreeProcKind.DoomsdayBell"),
    ("TreeShieldUp", 1050, "fx_shield_up", "SFX-Player", False, "(sözleşmede yok: kalkan açılışı — S-04'e sorulacak)"),
    ("TreeShieldHit", 1051, "fx_shield_hit", "SFX-Player", False, "(sözleşmede yok)"),
    ("TreeShieldBreak", 1052, "fx_shield_break", "SFX-Player", False, "TreeProcKind.ShieldBreak"),
    ("TreeLifesteal", 1053, "fx_lifesteal", "SFX-Player", False, "TreeProcKind.Leech"),
    ("TreeDodge", 1054, "fx_dodge", "SFX-Player", False, "TreeProcKind.Dodge"),
    ("TreeUndying", 1055, "fx_undying", "SFX-Player", False, "TreeProcKind.UndyingOath"),
    ("TreeRicochet", 1060, "fx_ricochet", "SFX-World", False, "(sözleşmede yok: sekme — S-02'ye sorulacak)"),
    ("TreeEndlessOn", 1061, "fx_endless_on", "SFX-Player", False, "TreeProcKind.EndlessWindowOpen"),
    ("TreeMiniRocket", 1062, "fx_minirocket", "SFX-World", False, "(Barrage roketi: rl_1 PowerOverride ateş yolu — S-02)"),
    ("TreeAuraUp", 1063, "fx_aura_up", "SFX-Player", False, "TreeEvents.AuraChanged (seviye artışı)"),
)
SRC_BUS = {"UI": "Interface", "SFX-World": "SFX", "SFX-Player": "SFX"}   # v024 MANIFEST bus per manifest bus
TARGET = {"UI": -22.0, "SFX-World": -20.0, "SFX-Player": -20.0}          # v024 chain: UI -22 / world -20 LUFS
SPATIAL = {"UI": False, "SFX-World": True, "SFX-Player": False}

# .meta templates (files in the worktree's AP011 folder) and what PinImporter pins for a non-weapon sound in each class.
TEMPLATES = {"oneshot": "ChainArc_01.wav.meta", "loop": "SwArcSphereLoop.wav.meta", "long": "AmbBambooGroveLoop.wav.meta"}
_PIN = {"compressionFormat": 1, "sampleRateSetting": 0, "forceToMono": 1, "ambisonic": 0}   # Vorbis, PreserveSampleRate, mono
WANTED = {
    "oneshot": dict(_PIN, quality=0.8, loadType=0, preloadAudioData=1, loadInBackground=0),   # SfxQuality, DecompressOnLoad
    "loop": dict(_PIN, quality=0.7, loadType=0, preloadAudioData=1, loadInBackground=0),      # MusicQuality, DecompressOnLoad
    "long": dict(_PIN, quality=0.7, loadType=2, preloadAudioData=0, loadInBackground=1),      # MusicQuality, Streaming
}
MEMBER_RE = re.compile(r"^\s*([A-Z][A-Za-z0-9]*)\s*=\s*(\d+)\s*,?\s*$")


class CheckFailed(Exception):
    """One or more checks failed; nothing was written. args[0] = the list of failures."""


def guid_of(name):
    """The deterministic Unity GUID of an R4 file name (32 lowercase hex digits)."""
    return hashlib.sha256((GUID_SALT + name).encode("utf-8")).hexdigest()[:32]


def template_kind(loop, seconds):
    return "long" if seconds >= LONG_CLIP_S else ("loop" if loop else "oneshot")


def meta_settings(text):
    """The importer fields of a .meta that PinImporter decides (numbers)."""
    out = {}
    for key in WANTED["oneshot"]:
        m = re.search(r"^\s*" + key + r":\s*(\S+)\s*$", text, re.M)
        if m:
            out[key] = float(m.group(1))
    return out


def meta_text(template, guid):
    new, n = re.subn(r"^guid: [0-9a-f]{32}$", "guid: " + guid, template, count=1, flags=re.M)
    assert n == 1, "template without a guid line"
    return new


def guid_index(project, known=None):
    """{guid: [paths]} over every .meta under Assets/ and Packages/; --known-guids tokens map to the label 'known-guids'."""
    idx = {}
    for top in ("Assets", "Packages"):
        base = project / top
        if not base.is_dir():
            continue
        for dirpath, _dirs, files in os.walk(base):
            for fn in files:
                if not fn.endswith(".meta"):
                    continue
                p = Path(dirpath) / fn
                with open(p, "r", encoding="utf-8", errors="replace") as f:
                    m = re.search(r"^guid: ([0-9a-f]{32})\s*$", f.read(512), re.M)
                if m:
                    idx.setdefault(m.group(1), []).append(p)
    for g in known or ():
        idx.setdefault(g, []).append("known-guids")
    return idx


def read_known_guids(path):
    return set(re.findall(r"\b[0-9a-f]{32}\b", Path(path).read_text(encoding="utf-8", errors="replace")))


def read_sums(src):
    sums = {}
    p = src / "SHA256SUMS"
    if not p.is_file():
        return None
    for line in p.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line:
            h, f = line.split(None, 1)
            sums[f.lstrip("*").strip()] = h.lower()
    return sums


def wav_format(p):
    with wave.open(str(p)) as w:
        return w.getnchannels(), w.getsampwidth() * 8, w.getframerate(), w.getnframes()


def _db(v):
    return float(20 * np.log10(abs(float(v)) + 1e-12))


def stamps(p):
    """The AP-011 stamps of one shipped file (mono mean): house lufs and 4x true peak (ap027_import), the quietest 100 ms
    window (non-overlapping from the start; a file under 100 ms is its own window), DC, clipped samples, last-10 ms RMS."""
    x, r = a27.pcm(p)
    m = x.mean(axis=1)
    w = max(1, min(len(m), r // 10))
    n = max(1, len(m) // w)
    floor = np.sqrt((m[:n * w].reshape(n, w) ** 2).mean(axis=1)).min()
    tail = m[-max(1, r // 100):]
    return {"lufs": a27.house_lufs(p), "tp": a27.true_peak_db(p), "floor": _db(floor), "dc": _db(m.mean()),
            "clipped": int((np.abs(x) >= 0.999).sum()), "end": _db(np.sqrt(np.mean(tail ** 2))), "seconds": len(m) / float(r)}


def _num(s):
    return "%g" % s


def _edit(op):
    if op and op[0] == "cut" and len(op) == 3:
        return "cut at %s s + %s ms fade" % (_num(op[1]), _num(op[2]))
    if op and op[0] == "hpf" and len(op) == 2:
        return "HPF %s Hz" % _num(op[1])
    if op and op[0] == "fade_last" and len(op) == 2:
        return "last %s s faded" % _num(op[1])
    if op and op[0] == "fade_in" and len(op) == 2:
        return "%s ms fade-in" % _num(op[1])
    return " ".join(str(v) for v in op)


# WP-113: the MANIFEST names in-house origins "synthesis" / "derived"; any other named kind is an external single take,
# recorded in the private ledger (r4) and in LICENSES-R4.csv — the vendor name stays there, not in the Unity manifest.
IN_HOUSE_KINDS = ("synthesis", "derived")


def _external(kind):
    return kind not in IN_HOUSE_KINDS and kind not in (None, "", "?")


def _origin(o):
    kind = o.get("kind", "?")
    if _external(kind):
        return "external take"
    if kind == "synthesis":
        return "synthesis (%s seed %s)" % (o.get("tool", "?"), o.get("seed", "?"))
    if kind == "derived":
        pack = Path(str(o.get("source", ""))).parent.name or "?"
        return "derived (%s seed %s) from Sonniss GameAudioGDC '%s'" % (o.get("tool", "?"), o.get("seed", "?"), pack)
    return kind


def layers_of(files):
    short = {"synthesis": "synth", "derived": "derived (Sonniss)"}
    kinds = [f["origin"].get("kind", "?") for f in files]
    kinds = ["external" if _external(k) else k for k in kinds]
    if len(set(kinds)) == 1:
        k = kinds[0]
        return {"external": "single take", "synthesis": "synth: ap011_r4_synth.py (seeded)",
                "derived": "derived: ap011_r4_derive.py (Sonniss source, seeded)"}.get(k, k)
    return " · ".join("_%s %s" % (f["file"][-6:-4], "single take (external)" if k == "external" else short.get(k, k))
                      for f, k in zip(files, kinds))


def provenance(src_entry, man_sha12, target, continuous_floor):
    files = src_entry["files"]
    parts = ["AP-011 v024 (WP-097 R4, S-08 11.10; %sMANIFEST.json %s) · v024 id %s" % (SRC_LABEL, man_sha12, src_entry["id"])]
    ff = [f["file"][-6:-4] for f in files if _external(f["origin"].get("kind"))]
    if ff:
        parts.append("external takes ×%d (_%s; private-ledger:r4)" % (len(ff), " _".join(ff)))
    for f in files:
        if not _external(f["origin"].get("kind")):
            parts.append("_%s %s" % (f["file"][-6:-4], _origin(f["origin"])))
    parts.append("licences LICENSES-R4.csv")
    edits = ["_%s %s" % (f["file"][-6:-4], ", ".join(_edit(op) for op in f.get("edits") or [])) for f in files if f.get("edits")]
    if edits:
        parts.append("edits: " + "; ".join(edits))
    lim = ["_%s %s dB" % (f["file"][-6:-4], _num(f["limiter_gr_db"])) for f in files if float(f.get("limiter_gr_db") or 0) > 0]
    if lim:
        parts.append("TP-limited (look-ahead ≤ 6 dB): " + ", ".join(lim))
    parts.append("house chain %s LUFS / %s dBTP%s" % (_num(target), _num(src_entry.get("ceiling_dbtp", CEILING)), ", seamless loop" if src_entry.get("loop") else ", 8 ms silence"))
    if continuous_floor:
        parts.append("noise floor not measurable (file 1 never rests under −60 dBFS in 100 ms: signal end to end) → continuous exemption")
    parts.append("SHA " + files[0]["sha256"][:12] + "…")
    return " · ".join(parts)


def gate_failures(e):
    """The manifest half of the EditMode AP-011 gates (AudioSliceEditModeTests) for one row."""
    s, bad = e["sound"], []
    low = -30.1 if e["bus"] == "UI" else -26.1 if e["bus"] == "Ambience" else -22.1
    if e["targetLufs"] < low or e["targetLufs"] > -15.9:
        bad.append("%s: target %s outside [%s; -15.9]" % (s, e["targetLufs"], low))
    if e["ceilingDbtp"] > CEILING + 0.01:
        bad.append("%s: ceiling %s > %s" % (s, e["ceilingDbtp"], CEILING))
    peak_bound = e["truePeakDb"] >= e["ceilingDbtp"] - 0.15 and e["lufs"] <= e["targetLufs"] + 0.1
    if abs(e["lufs"] - e["targetLufs"]) > 1.5 and not peak_bound:
        bad.append("%s: %s LUFS vs target %s ± 1.5 and not peak-bound (TP %s)" % (s, e["lufs"], e["targetLufs"], e["truePeakDb"]))
    if e["truePeakDb"] > e["ceilingDbtp"] + 0.1:
        bad.append("%s: true peak %s > %s" % (s, e["truePeakDb"], e["ceilingDbtp"]))
    if e["bitDepth"] != 24:
        bad.append("%s: %s-bit" % (s, e["bitDepth"]))
    if e["clipped"] > 0:
        bad.append("%s: %d clipped samples" % (s, e["clipped"]))
    if e["dcDb"] > -60.0:
        bad.append("%s: DC %s dBFS" % (s, e["dcDb"]))
    if not e["continuous"] and e["noiseFloorDb"] > FLOOR_DB:
        bad.append("%s: noise floor %s dBFS" % (s, e["noiseFloorDb"]))
    if not e["loop"] and e["endSilenceDb"] > -70.0:
        bad.append("%s: end %s dBFS > -70" % (s, e["endSilenceDb"]))
    return bad


def dump(man, trailing):
    return json.dumps(man, indent=1, ensure_ascii=False) + trailing


def _xml(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def enum_block(prepend_blank):
    lines = [""] if prepend_blank else []
    lines += ["        // ---- WP-097 R4 (S-08 12.10) · AP-011 v024 new sounds, tools/audio/ap011_r4_import.py (contract K/wp-097/r4-bind/IDS.md):",
              "        // wall buy 1000–1004, difficulty mode 1010–1011, tree screen 1020–1023, tree procs 1030–1063. Rows in",
              "        // AP011-audio-manifest.json (ap011-v024); the catalog takes them on its next build (DD › Audio › Build Catalog)."]
    for sound, value, sid, _bus, _loop, event in R4:
        lines.append("        /// <summary>R4 · AP-011 v024 %s · %s</summary>" % (sid, _xml(event)))
        lines.append("        %s = %d," % (sound, value))
        lines.append("")
    return lines


def enum_plan(text):
    """(new_text or None when unchanged, failures)."""
    lines = text.split("\n")
    try:
        start = next(i for i, l in enumerate(lines) if re.match(r"^\s*public enum AudioSoundId\b", l))
        end = next(i for i in range(start + 1, len(lines)) if lines[i].strip() == "}")
    except StopIteration:
        return None, ["AudioSoundId.cs: no `public enum AudioSoundId { … }`"]
    members = [(i, MEMBER_RE.match(lines[i])) for i in range(start + 1, end)]
    members = [(i, m.group(1), int(m.group(2))) for i, m in members if m]
    if not members:
        return None, ["AudioSoundId.cs: no members"]
    by_name = {n: v for _i, n, v in members}
    by_value = {}
    for _i, n, v in members:
        by_value.setdefault(v, []).append(n)
    bad, present = [], 0
    for sound, value, *_ in R4:
        if sound in by_name:
            present += 1
            if by_name[sound] != value:
                bad.append("AudioSoundId.%s = %d, the contract says %d" % (sound, by_name[sound], value))
        others = [n for n in by_value.get(value, []) if n != sound]
        if others:
            bad.append("AudioSoundId value %d is taken by %s (contract: %s)" % (value, ", ".join(others), sound))
    if by_name.get("WeaponSlap") != 990:
        bad.append("AudioSoundId.WeaponSlap = 990 not found (the contract's last value on main)")
    if bad:
        return None, bad
    if present == len(R4):
        return None, []
    if present:
        return None, ["AudioSoundId.cs carries %d of the %d R4 members: a partial set (fix by hand)" % (present, len(R4))]
    last = members[-1][0]
    if lines[last].rstrip().endswith(","):
        return None, ["AudioSoundId.cs: the last member has a trailing comma (unexpected layout)"]
    at = last
    while at - 1 > start and lines[at - 1].strip().startswith("///"):
        at -= 1
    block = enum_block(prepend_blank=lines[at - 1].strip() != "")
    return "\n".join(lines[:at] + block + lines[at:]), []


def plan(root, src=SRC, known_guids=None):
    """Every check and measurement; returns the plan (dict) or raises CheckFailed([...]). Writes nothing."""
    root, src = Path(root), Path(src)
    project = root / PROJECT
    out_dir = root / OUT_DIR
    bad = []

    # ---- source package
    man_path = src / "MANIFEST.json"
    src_man = json.loads(man_path.read_text(encoding="utf-8"))
    man_sha = a27.sha(man_path)
    sums = read_sums(src)
    if sums is None:
        raise CheckFailed(["SHA256SUMS missing in " + str(src)])
    if "MANIFEST.json" in sums and sums["MANIFEST.json"] != man_sha:
        bad.append("MANIFEST.json: SHA-256 differs from SHA256SUMS")
    by_sound = {}
    for e in src_man.get("entries", []):
        if e.get("sound") in by_sound:
            bad.append("source MANIFEST lists %s twice" % e.get("sound"))
        by_sound[e.get("sound")] = e
    contract = {r[0]: r for r in R4}
    extra = sorted(set(by_sound) - set(contract))
    if extra:
        bad.append("source MANIFEST sounds outside the R4 contract: " + ", ".join(map(str, extra)))
    listed = set()
    files = []   # (sound, src entry, src file entry, path)
    for sound, value, sid, bus, loop, event in R4:
        e = by_sound.get(sound)
        if e is None:
            bad.append("%s: not in the source MANIFEST" % sound)
            continue
        if e.get("id") != sid:
            bad.append("%s: v024 id %s, the contract says %s" % (sound, e.get("id"), sid))
        if bool(e.get("loop")) != loop:
            bad.append("%s: loop %s, the contract says %s" % (sound, e.get("loop"), loop))
        if e.get("bus") != SRC_BUS[bus]:
            bad.append("%s: source bus %s, the contract's %s expects %s" % (sound, e.get("bus"), bus, SRC_BUS[bus]))
        if float(e.get("target_lufs", 0)) != TARGET[bus]:
            bad.append("%s: target_lufs %s, expected %s for %s" % (sound, e.get("target_lufs"), TARGET[bus], bus))
        if not e.get("files"):
            bad.append("%s: no files" % sound)
        for k, f in enumerate(e.get("files", []), 1):
            name = f.get("file", "")
            listed.add(name)
            if name != "%s_%02d.wav" % (sound, k):
                bad.append("%s: file %d is %s, expected %s_%02d.wav" % (sound, k, name, sound, k))
            p = src / name
            if not p.is_file():
                bad.append("%s: missing in %s" % (name, src))
                continue
            h = a27.sha(p)
            if h != f.get("sha256"):
                bad.append("%s: SHA-256 %s… differs from the MANIFEST %s…" % (name, h[:12], str(f.get("sha256"))[:12]))
            if sums.get(name) != h:
                bad.append("%s: SHA-256 differs from SHA256SUMS (%s)" % (name, (sums.get(name) or "not listed")[:12]))
            ch, bits, rate, _n = wav_format(p)
            if (ch, bits, rate) != (1, 24, RATE):
                bad.append("%s: %d ch / %d-bit / %d Hz, expected mono / 24-bit / 48 kHz" % (name, ch, bits, rate))
            files.append((sound, e, f, p))
    unlisted = sorted(p.name for p in src.glob("*.wav") if p.name not in listed)
    if bad:
        raise CheckFailed(bad)

    # ---- worktree
    for need in (project / "Assets", out_dir, root / MANIFEST, root / ENUM):
        if not need.exists():
            bad.append("worktree: missing " + str(need.relative_to(root)))
    if bad:
        raise CheckFailed(bad)
    templates = {}
    for kind, name in TEMPLATES.items():
        p = out_dir / name
        if not p.is_file():
            bad.append("template .meta missing: " + str(p.relative_to(root)))
            continue
        t = p.read_text(encoding="utf-8")
        got = meta_settings(t)
        want = WANTED[kind]
        diff = {k: (got.get(k), v) for k, v in want.items() if got.get(k) is None or abs(got[k] - v) > 1e-6}
        if diff:
            bad.append("template %s does not carry PinImporter's %s settings: %s" % (name, kind, diff))
        templates[kind] = t
    idx = guid_index(project, known_guids)

    # ---- per file: copy status, .meta, stamps
    stamp, ops, seen_guids = {}, [], {}
    for sound, e, f, p in files:
        name = f["file"]
        dst = out_dir / name
        meta = out_dir / (name + ".meta")
        st = stamps(p)
        stamp[name] = st
        kind = template_kind(bool(e.get("loop")), st["seconds"])
        guid = guid_of(name)
        if guid in seen_guids:
            bad.append("GUID %s twice (%s, %s)" % (guid, seen_guids[guid], name))
        seen_guids[guid] = name
        wav_state = "new"
        if dst.exists():
            wav_state = "same" if a27.sha(dst) == f["sha256"] else "conflict"
            if wav_state == "conflict":
                bad.append("%s: exists in the worktree with another SHA-256 (not overwritten)" % dst.relative_to(root))
        text = meta_text(templates[kind], guid) if kind in templates else None
        meta_state = "new"
        if meta.exists():
            meta_state = "same" if text is not None and meta.read_text(encoding="utf-8") == text else "conflict"
            if meta_state == "conflict":
                bad.append("%s: exists with other content (not overwritten)" % meta.relative_to(root))
        # the file's own .meta is not a collision; nor is a --known-guids hit when that .meta is already ours (a re-run on a
        # tree whose main carries the import)
        owners = [o for o in idx.get(guid, []) if o != meta and not (o == "known-guids" and meta_state == "same")]
        if owners:
            bad.append("%s: GUID %s already used by %s" % (name, guid, ", ".join(str(o) for o in owners)))
        ops.append({"file": name, "src": p, "dst": dst, "meta": meta, "meta_text": text, "kind": kind, "guid": guid,
                    "wav": wav_state, "meta_state": meta_state, "sha256": f["sha256"]})
        # the producer's own stamps (ap011_r4_final.py: the same BS.1770 and 4x polyphase peak) must agree with the house measure
        for key, mine, tol in (("lufs", st["lufs"], 0.05), ("tp_dbtp", st["tp"], 0.1)):
            if key in f and abs(float(f[key]) - mine) > tol:
                bad.append("%s: measured %s %.2f, the source MANIFEST says %s" % (name, key, mine, f[key]))

    # ---- manifest rows
    entries = []
    for sound, value, sid, bus, loop, event in R4:
        e = by_sound[sound]
        sts = [stamp[f["file"]] for f in e["files"]]
        floor_free = sts[0]["floor"] > FLOOR_DB
        continuous = bool(loop) or floor_free
        row = {
            "sound": sound, "event": event, "files": [f["file"] for f in e["files"]], "loop": bool(loop),
            "lufs": round(float(sts[0]["lufs"]), 2), "truePeakDb": round(float(max(s["tp"] for s in sts)), 2),
            "noiseFloorDb": round(float(sts[0]["floor"]), 1), "dcDb": round(float(max(s["dc"] for s in sts)), 1),
            "clipped": int(sum(s["clipped"] for s in sts)), "bitDepth": 24, "targetLufs": float(TARGET[bus]),
            "ceilingDbtp": float(e.get("ceiling_dbtp", CEILING)), "continuous": continuous, "endSilenceDb": round(float(max(s["end"] for s in sts)), 1),
            "layers": layers_of(e["files"]), "bus": bus, "spatial": SPATIAL[bus],
            "provenance": provenance(e, man_sha[:12], TARGET[bus], floor_free and not loop),
        }
        assert tuple(row) == FIELDS
        bad += gate_failures(row)
        entries.append(row)

    mpath = root / MANIFEST
    text = mpath.read_text(encoding="utf-8")
    trailing = text[len(text.rstrip("\n")):]
    man = json.loads(text)
    if dump(man, trailing) != text:
        bad.append("AP011-audio-manifest.json does not round-trip as json.dumps(indent=1, ensure_ascii=False): format drift, not rewritten")
    old_entries = man.get("entries", [])
    names = [x.get("sound") for x in old_entries]
    r4names = set(contract)
    dup = sorted({n for n in names if n in r4names and names.count(n) > 1})
    if dup:
        bad.append("AP011-audio-manifest.json lists R4 sounds twice: " + ", ".join(dup))
    old = {x["sound"]: x for x in old_entries if x.get("sound") in r4names}
    states = {r["sound"]: ("unchanged" if old.get(r["sound"]) == r else "replaced" if r["sound"] in old else "added") for r in entries}
    keep = [x for x in old_entries if x.get("sound") not in r4names]
    was_sorted = names == sorted(names, key=lambda n: str(n))
    merged = sorted(keep + entries, key=lambda x: str(x.get("sound"))) if was_sorted else keep + entries
    new_man = dict(man)
    new_man["entries"] = merged
    m = re.match(r"^ap011-v(\d+)$", str(man.get("version", "")))
    if not (m and int(m.group(1)) > int(VERSION.rsplit("v", 1)[1])):
        new_man["version"] = VERSION
    new_text = dump(new_man, trailing)
    if dump(json.loads(new_text), trailing) != new_text:
        bad.append("merged manifest does not round-trip (internal error)")

    etext = (root / ENUM).read_text(encoding="utf-8")
    new_enum, ebad = enum_plan(etext)
    bad += ebad
    if bad:
        raise CheckFailed(bad)
    return {"root": root, "src": src, "ops": ops, "entries": entries, "states": states, "manifest_text": new_text,
            "manifest_changed": new_text != text, "manifest_version": (man.get("version"), new_man["version"]),
            "manifest_was_sorted": was_sorted, "manifest_total": len(merged), "enum_text": new_enum, "unlisted": unlisted,
            "src_manifest_sha12": man_sha[:12], "guids_indexed": len(idx)}


def _write_text(p, text):
    with open(p, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def apply(pl):
    """Writes what the plan found missing; returns the changed paths (relative to the worktree root)."""
    root, changed = pl["root"], []
    for op in pl["ops"]:
        if op["wav"] == "new":
            tmp = op["dst"].parent / ("." + op["dst"].name + ".r4tmp")   # a dot file: Unity does not import it
            try:
                with open(op["src"], "rb") as fi, open(tmp, "wb") as fo:
                    while True:
                        b = fi.read(1 << 20)
                        if not b:
                            break
                        fo.write(b)
                if a27.sha(tmp) != op["sha256"]:
                    raise CheckFailed(["%s: copy SHA-256 mismatch" % op["file"]])
                if op["meta_state"] == "new":
                    _write_text(op["meta"], op["meta_text"]); changed.append(op["meta"])
                os.replace(tmp, op["dst"]); changed.append(op["dst"])
            finally:
                if tmp.exists():
                    tmp.unlink()
        elif op["meta_state"] == "new":
            _write_text(op["meta"], op["meta_text"]); changed.append(op["meta"])
    if pl["manifest_changed"]:
        _write_text(root / MANIFEST, pl["manifest_text"]); changed.append(root / MANIFEST)
    if pl["enum_text"] is not None:
        _write_text(root / ENUM, pl["enum_text"]); changed.append(root / ENUM)
    return [str(Path(p).relative_to(root)) for p in changed]


def summary(pl, changed, dry):
    ops = pl["ops"]
    count = lambda key, val: sum(1 for o in ops if o[key] == val)
    return {
        "dryRun": dry, "src": str(pl["src"]), "srcManifestSha12": pl["src_manifest_sha12"],
        "sounds": len(pl["entries"]), "files": len(ops),
        "wav": {"new": count("wav", "new"), "same": count("wav", "same")},
        "meta": {"new": count("meta_state", "new"), "same": count("meta_state", "same"),
                 "templates": {k: sum(1 for o in ops if o["kind"] == k) for k in TEMPLATES}},
        "manifest": {"version": list(pl["manifest_version"]), "entriesTotal": pl["manifest_total"],
                     "added": sorted(s for s, v in pl["states"].items() if v == "added"),
                     "replaced": sorted(s for s, v in pl["states"].items() if v == "replaced"),
                     "unchanged": sum(1 for v in pl["states"].values() if v == "unchanged"), "changed": pl["manifest_changed"]},
        "enum": {"members": len(R4), "inserted": pl["enum_text"] is not None},
        "guidsIndexed": pl["guids_indexed"], "unlistedSourceWavs": pl["unlisted"],
        "changed": changed if not dry else [],
        "wouldChange": ([str(o["dst"].relative_to(pl["root"])) for o in ops if o["wav"] == "new"]
                        + [str(o["meta"].relative_to(pl["root"])) for o in ops if o["meta_state"] == "new"]
                        + ([MANIFEST] if pl["manifest_changed"] else []) + ([ENUM] if pl["enum_text"] is not None else [])) if dry else [],
        "rows": [[e["sound"], len(e["files"]), e["lufs"], e["truePeakDb"], e["noiseFloorDb"], e["continuous"]] for e in pl["entries"]],
    }


def run(root, src=SRC, dry_run=False, known_guids=None):
    """plan + apply; returns the summary dict. Raises CheckFailed (nothing written) when a check fails."""
    pl = plan(root, src, known_guids)
    changed = [] if dry_run else apply(pl)
    return summary(pl, changed, dry_run)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", help="worktree root (holds prototype/DeathsDemolition_PlayableConcept)")
    parser.add_argument("--src", default=str(SRC), help="AP-011 v024 final folder (default: dd_paths KPROJ/asset-production/AP-011/v024/final)")
    parser.add_argument("--dry-run", action="store_true", help="check and measure, print what would change, write nothing")
    parser.add_argument("--known-guids", help="file whose 32-hex tokens are GUIDs already taken (e.g. git grep -h '^guid:' main -- '*.meta')")
    try:
        args = parser.parse_args(argv)
    except SystemExit as e:
        return 0 if e.code == 0 else 64
    if not (Path(args.root) / PROJECT).is_dir():
        print("worktree root not found (no %s): %s" % (PROJECT, args.root), file=sys.stderr)
        return 64
    if not (Path(args.src) / "MANIFEST.json").is_file():
        print("source MANIFEST.json not found: %s" % (Path(args.src) / "MANIFEST.json"), file=sys.stderr)
        return 64
    if args.known_guids and not Path(args.known_guids).exists():   # exists, not is_file: <(git grep …) is a /dev/fd pipe
        print("--known-guids file not found: %s" % args.known_guids, file=sys.stderr)
        return 64
    known = read_known_guids(args.known_guids) if args.known_guids else None
    try:
        s = run(args.root, args.src, args.dry_run, known)
    except CheckFailed as e:
        fails = e.args[0]
        print("ap011_r4_import: %d check(s) failed, nothing written:" % len(fails), file=sys.stderr)
        for f in fails:
            print("  " + f, file=sys.stderr)
        return 1
    print(json.dumps(s, indent=1, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
