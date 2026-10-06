#!/usr/bin/env python3
"""WP-085 R2 S3 (S-08 06.10): Dense-40 loudness and peak — the r2-S3.json writer (dd-r2-measure-v1).

No C# test writes S3: it is measured offline on three kinds of recording the owner's run makes (tools/qa/
r2-measure-contract.md, S3):
  dense      Dense-40 Player captures, one or more per camera mode (tp and fp required, topdown when recorded; every run
             counts, the worst wins): a dev build stamped
             with the commit, QaRunBot from round 12 (-ddQaBotStartRound 12 -ddQaBotSeed 26 -ddQaBotInvulnerable 1),
             -ddAudioDense40 -ddAudioDenseRound 12 -ddAudioDenseSeconds 40, -ddCameraMode <mode>, -ddAudioCapture
             <dir>/mix.wav. The folder holds mix.wav with its .markers.txt / .telemetry.tsv sidecars, bot/qa-report.json,
             bot/bot-summary.json, player.log and (optional) audio_capture_report.py's report.json.
  a9         the A9 stack render of AudioSliceTests.A9_EnYuksekYigin_LimiterOncesiZarf_Olcum (master limiter opened to
             0 dB), with its sidecars.
  listening  the listening-kit takes of AudioSliceTests.DinlemePaketi_AB_Uret: <no>_<mode>_take.wav, <no>_<mode>_blocks.csv
             (block start frame, variant A B C W R) and the take's sidecars; 1_ustten = top-down, 2_birinci-sahis = FP.

Values (the worst over the takes; checks in r2-gates.json S3, judged by r2_gates.evaluate_gate):
  truePeakDbtp      the whole Dense-40 recording, max over channels: the higher of a 4x polyphase FIR interpolation (scipy
                    resample_poly, Kaiser beta 5 — the BS.1770-4 Annex 2 method, not the Annex's example coefficients) and
                    audio_capture_report's 4x Catmull-Rom (the runtime LoudnessMeter; R1's numbers).
  momentaryMaxLufs  the same recordings: BS.1770 K-weighting, 400 ms window on a 10 ms hop (EBU Tech 3341 asks >= 10 Hz; R1's
                    100 ms grid can miss up to ~0.4 LU of a peak — it is kept next to it as the R1-comparable value).
  preLimiterGrDb    the A9 render: max(0, envelope max + 8 dB) — the detector of the -8 dBFS master limiter (10 ms attack,
                    90 ms release, linked stereo; AudioMixerBuilder), as K/wp-072/tools/a9_stack.py.
  weaponMarginLu    the listening takes: 10 log10(W / R), the K-weighted mean power of the W stem (SFX-Player alone) over the R
                    stem (every other bus) in the burst windows 0.15-1.2 s of each 2 s fire cycle (two per 4 s block); the
                    worse of top-down and FP. Today's Run snapshot is R1's mix B, so this is R1's "mix B margin" directly.

The source must be in the recording (contract, S-08 05.10): every take is checked first — capture dropped 0, telemetry ring
overflow 0, 40 alive and a dense window of -ddAudioDenseSeconds - 5 s, the camera the take claims, weapon shots delivered and
heard (shot-window peak median >= -30 dBFS), a dev build stamped with the commit and not dirty, the A9 stack loud in every
second and not clipped, W and R blocks present and adding up to A within 1 dB, the named tests Passed in the given NUnit XML.
One failed check and nothing is written (exit 1): S3 stays BLOCKED by name in the R2 report.

KİRLİ (SABİT-KURALLAR 5.8: a dirty measurement is ranking information, not a gate value): a load log with load1 >= 6, a
qa-report load1 >= 6, no load log at all, or --kirli <reason> → the values go to r2-S3.KIRLI.json next to --out with a note,
the gate file is not written (the chain binds only r2-S3.json, so S3 stays BLOCKED) and the exit is 3.

usage: r2_s3.py --dense tp=DIR --dense fp=DIR [--dense tp=DIR2 …] [--dense topdown=DIR] --a9 WAV --listening DIR --xml PM.xml
                [--load FILE …] [--kirli REASON] [--dense-seconds 40] [--commit 40-hex] [--out r2-S3.json] [--dry-run]
--commit defaults to DD_R2_COMMIT, --out to $DD_R2_MEASURE_DIR/r2-S3.json. --dry-run measures and prints, never writes.
exit: 0 written, every check met · 2 written, a check fails (the judge FAILs S3) · 1 a source check failed, nothing written
      · 3 KİRLİ (r2-S3.KIRLI.json only) · 64 usage
"""
import argparse
import csv
import hashlib
import json
import math
import os
import re
import statistics
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import numpy as np
from scipy.signal import resample_poly

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import audio_capture_report as acr  # noqa: E402  (the BS.1770 K-weighting, windows and Catmull-Rom of R1)
import r2_gates  # noqa: E402  (the contract and its judge: one copy of the thresholds)

GATE = "S3"
KEYS = ("truePeakDbtp", "momentaryMaxLufs", "preLimiterGrDb", "weaponMarginLu")
SHA = re.compile(r"[0-9a-f]{40}")
SOURCE = ("tools/qa/r2_s3.py (Dense-40 Player captures + AudioSliceTests.A9_EnYuksekYigin_LimiterOncesiZarf_Olcum "
          "+ AudioSliceTests.DinlemePaketi_AB_Uret W/R stems)")

# dense: -ddCameraMode value → QaRunBot bot-summary cameraMode
CAMERA = {"topdown": "TopDown", "tp": "ThirdPerson", "fp": "FirstPerson"}
REQUIRED_DENSE = ("tp", "fp")            # R1 closing measured these two
DENSE_ALIVE = 40                         # the spawner's safety ceiling AudioDense40EvidenceDriver holds
DENSE_SLACK_S = 5.0                      # the dense window may end this much short of -ddAudioDenseSeconds (quit latency)
MIN_SHOTS_DELIVERED = 20                 # combat_event ClassicWeaponFire delivered (R1: 80-104)
MIN_FIRE_STARTS = 10                     # Weapon*Fire voices started inside the dense window (R1: 24-37)
SHOT_WINDOW_S = 0.25                     # cue → heard: one output buffer (1024 x 4 at 48 kHz = 85 ms) plus the attack
SHOT_PEAK_FLOOR_DBFS = -30.0             # contract (S-08 05.10): shot-window peak median >= -30 dBFS
FIRE_SOUND = re.compile(r"Weapon\w+Fire")
M_HOP_S = 0.01                           # the gate's momentary hop
R1_HOP_S = 0.1                           # audio_capture_report's hop (R1's numbers)
REPORT_TOLERANCE = 0.01                  # report.json vs this tool's R1-method recomputation

# a9: the master limiter (AudioMixerBuilder LimiterThresholdDb / AttackMs / ReleaseMs)
LIMITER_THRESHOLD_DB = -8.0
LIMITER_ATTACK_S = 0.010
LIMITER_RELEASE_S = 0.090
A9_MIN_SECONDS = 10.0                    # the test renders 12 s
A9_BLOCK_FLOOR_DBFS = -20.0              # SG1 every 0.9 s: every second holds a shot (R1: -6.7 … -3.2 dBFS)
A9_MIN_LOUD_BLOCKS = 10                  # of the 1 s blocks
A9_CLIP_DBFS = -0.1                      # a clipped uncompressed render hides the gain reduction
A9_MIN_FIRE = 6                          # combat_event ClassicWeaponFire (SG1 + RL1, ~19 in 12 s)
A9_MIN_EXPLOSIONS = 3                    # combat_event Explosion (every 2 s)

# listening: DinlemePaketi_AB_Uret
LISTEN_MODES = {"ustten": "topdown", "birinci-sahis": "fp"}
BLOCK_S = 4.0
CYCLE_OFFSETS_S = (0.0, 2.0)             # two 2 s fire cycles per block
BURST_S = (0.15, 1.2)                    # inside a cycle: the 1.2 s burst after the switch transient
STEM_MIN_BLOCKS = 2                      # of each of A, W, R (R1: 3)
ADDITIVITY_DB = 1.0                      # 10 log10(A / (W + R)) within this: the stems split the mix (R1: -0.58 / -0.37)
MIN_TAKE_SHOTS = 20

LOAD1_QUIET = 6.0                        # SABİT-KURALLAR 5.9 / Ç17: a quiet machine is load1 < 6
TESTS = {"a9": "DD.Prototype.Tests.PlayMode.AudioSliceTests.A9_EnYuksekYigin_LimiterOncesiZarf_Olcum",
         "listening": "DD.Prototype.Tests.PlayMode.AudioSliceTests.DinlemePaketi_AB_Uret"}

# R1 closing, R1 method (Catmull-Rom true peak, 100 ms momentary grid): K/wp-072/evidence/b7s/dense40-r3/{tp,fp}/report.json,
# b7s/a9.json, b7-r7/kit-splice.txt (the mix B margin = the pre-B take's W/R margin + 4 dB, exact while GR = 0).
R1_CLOSING = {"dense": {"tp": {"truePeakDbtpR1": -3.57, "momentaryMaxLufsR1": -14.85},
                        "fp": {"truePeakDbtpR1": -2.73, "momentaryMaxLufsR1": -14.65}},
              "a9": {"preLimiterGrDb": 0.0, "envelopeMaxDbfs": -9.98},
              "listening": {"topdown": {"weaponMarginLu": 1.57}, "fp": {"weaponMarginLu": 3.18}}}


# ---------------------------------------------------------------------------------------------------- readers


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def db(value):
    return 20.0 * math.log10(value) if value > 1e-12 else -240.0


def read_markers(path):
    """AudioOutputCapture's marker sidecar: {"dropped": int|None, "density": [(seconds, alive, dsp)]} (seconds = capture
    position of the audio thread, so dsp - seconds is the capture's clock offset)."""
    out = {"dropped": None, "density": []}
    for raw in Path(path).read_text(encoding="utf-8").splitlines():
        fields = raw.split("\t", 3)
        if raw.startswith("#") or len(fields) != 4:
            continue
        seconds, _, kind, detail = fields
        if kind == "capture" and detail.startswith("end "):
            m = re.search(r"\bdropped (\d+)\b", detail)
            out["dropped"] = int(m.group(1)) if m else None
        elif kind == "density":
            alive = re.search(r"\balive (\d+)\b", detail)
            dsp = re.search(r"\bdsp ([0-9.]+)", detail)
            if alive and dsp:
                out["density"].append((float(seconds), int(alive.group(1)), float(dsp.group(1))))
    return out


def read_telemetry(path):
    """The playback telemetry sidecar: ring overflow, combat events delivered, and every voice start (dsp time, sound)."""
    out = {"overflow": None, "combat": {}, "starts": []}
    section = None
    for raw in Path(path).read_text(encoding="utf-8").splitlines():
        if raw.startswith("# ring_overflow"):
            parts = raw.split("\t")
            out["overflow"] = int(parts[1]) if len(parts) > 1 and parts[1].strip().isdigit() else None
            continue
        if raw.startswith("# combat_event"):
            section = "combat"
            continue
        if raw.startswith("# sequence"):
            section = "sequence"
            continue
        if raw.startswith("#"):
            section = None
            continue
        f = raw.split("\t")
        if section == "combat" and len(f) >= 2 and f[1].isdigit():
            out["combat"][f[0]] = int(f[1])
        elif section == "sequence" and len(f) >= 7 and f[4] == "Started":
            try:
                out["starts"].append((float(f[3]), f[6]))
            except ValueError:
                continue
    return out


def read_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def build_info(qa_report):
    """qa-report.json carries the Player's build-info.json as a JSON string (QaReport)."""
    raw = (qa_report or {}).get("buildInfo")
    if isinstance(raw, dict):
        return raw
    try:
        return json.loads(raw) if isinstance(raw, str) else None
    except ValueError:
        return None


def xml_results(paths):
    """{test full name: result} over every NUnit XML given."""
    results = {}
    for path in paths:
        try:
            root = ET.parse(path).getroot()
        except (OSError, ET.ParseError):
            continue
        for case in root.iter("test-case"):
            if case.get("fullname"):
                results[case.get("fullname")] = case.get("result")
    return results


def load_peak(paths):
    """(max load1, file) over load logs: `sysctl -n vm.loadavg` lines ("<time> { 1.23 1.10 1.05 }") or "load1 <x>"."""
    best, where = None, None
    for path in paths:
        try:
            text = Path(path).read_text(encoding="utf-8")
        except OSError:
            continue
        for line in text.splitlines():
            m = re.search(r"\{\s*([0-9]+(?:\.[0-9]+)?)", line) or re.search(r"\bload1\s+([0-9]+(?:\.[0-9]+)?)", line)
            if m and (best is None or float(m.group(1)) > best):
                best, where = float(m.group(1)), str(path)
    return best, where


# ---------------------------------------------------------------------------------------------------- measures


def true_peak_fir(data):
    """max |x| over channels of the 4x oversampled signal (polyphase FIR, Kaiser beta 5)."""
    peak = 0.0
    for c in range(data.shape[1]):
        up = resample_poly(data[:, c].astype(np.float64), 4, 1)
        peak = max(peak, float(np.max(np.abs(up))) if up.size else 0.0)
    return peak


def momentary_max(energy, rate, hop_s, offset_s=0.0):
    """(max momentary LUFS over 400 ms windows on hop_s, start second of that window)."""
    means = acr.window_means(energy, rate, 0.4, hop_s)
    loud = acr.to_lufs(means)
    i = int(np.argmax(loud))
    return float(loud[i]), round(offset_s + i * hop_s, 3)


def clock_offset(density):
    """dsp time - capture seconds (median over the checkpoints after the first), so a cue's dsp time maps onto the file."""
    offsets = [dsp - s for s, _, dsp in density if s > 0]
    return statistics.median(offsets) if offsets else None


def shot_peaks(data, rate, times):
    """sample peak (dBFS) in [t, t + SHOT_WINDOW_S] after each cue time (seconds in the file)."""
    out = []
    n = data.shape[0]
    for t in times:
        a, b = int(t * rate), int((t + SHOT_WINDOW_S) * rate)
        if 0 <= a < b <= n:
            out.append(db(float(np.max(np.abs(data[a:b])))))
    return out


def measure_dense(mode, folder, commit, dense_seconds, label=None):
    """One Dense-40 capture: (detail, findings); label names it in the findings (tp, tp#2 for a repeat)."""
    label = label or mode
    folder = Path(folder)
    wav = folder / "mix.wav"
    paths = {"wav": wav, "markers": Path(str(wav) + ".markers.txt"), "telemetry": Path(str(wav) + ".telemetry.tsv"),
             "qaReport": folder / "bot" / "qa-report.json", "botSummary": folder / "bot" / "bot-summary.json",
             "playerLog": folder / "player.log"}
    findings = [f"dense {label}: missing {name} ({p})" for name, p in paths.items() if not p.is_file()]
    if findings:
        return None, findings
    try:
        data, rate = acr.read_wav(wav)
        energy = acr.k_energy(data, rate)
    except (ValueError, OSError) as exc:
        return None, [f"dense {label}: {wav}: {exc}"]
    markers, telemetry = read_markers(paths["markers"]), read_telemetry(paths["telemetry"])
    qa, bot = read_json(paths["qaReport"]), read_json(paths["botSummary"]) or {}
    log = paths["playerLog"].read_text(encoding="utf-8", errors="replace")
    seconds = data.shape[0] / rate

    if markers["dropped"] != 0:
        findings.append(f"dense {label}: capture dropped samples {markers['dropped']} (must be 0; no end marker = None)")
    if telemetry["overflow"] != 0:
        findings.append(f"dense {label}: telemetry ring overflow {telemetry['overflow']} (must be 0)")
    if bot.get("cameraMode") != CAMERA[mode]:
        findings.append(f"dense {label}: bot-summary cameraMode {bot.get('cameraMode')!r}, the take claims {CAMERA[mode]}")
    if "[AudioDense40Evidence] capture window complete" not in log:
        findings.append(f"dense {label}: player.log has no '[AudioDense40Evidence] capture window complete' (no Dense-40 window)")
    max_alive = max((a for _, a, _ in markers["density"]), default=0)
    first = next((s for s, a, _ in markers["density"] if a >= DENSE_ALIVE), None)
    dense_len = seconds - first if first is not None else 0.0
    if max_alive < DENSE_ALIVE:
        findings.append(f"dense {label}: max alive {max_alive} < {DENSE_ALIVE}")
    elif dense_len < dense_seconds - DENSE_SLACK_S:
        findings.append(f"dense {label}: dense window {dense_len:.1f} s < {dense_seconds - DENSE_SLACK_S:.0f} s")
    delivered = telemetry["combat"].get("ClassicWeaponFire", 0)
    offset = clock_offset(markers["density"])
    fires = [dsp - offset for dsp, sound in telemetry["starts"] if FIRE_SOUND.fullmatch(sound)] if offset is not None else []
    fires_in = [t for t in fires if first is not None and first <= t <= seconds]
    peaks = shot_peaks(data, rate, fires_in)
    peak_median = statistics.median(peaks) if peaks else None
    if delivered < MIN_SHOTS_DELIVERED:
        findings.append(f"dense {label}: ClassicWeaponFire delivered {delivered} < {MIN_SHOTS_DELIVERED}")
    if len(fires_in) < MIN_FIRE_STARTS:
        findings.append(f"dense {label}: weapon Fire voices started in the dense window {len(fires_in)} < {MIN_FIRE_STARTS}")
    elif peak_median is None or peak_median < SHOT_PEAK_FLOOR_DBFS:
        findings.append(f"dense {label}: shot-window peak median {peak_median} dBFS < {SHOT_PEAK_FLOOR_DBFS}")
    info = build_info(qa) or {}
    if not info.get("stamped"):
        findings.append(f"dense {label}: the build is unstamped (qa-report buildInfo) — the recording is not tied to a commit")
    else:
        if info.get("mode") != "dev":
            findings.append(f"dense {label}: build mode {info.get('mode')!r}, Dense-40 needs a dev build")
        if str(info.get("describe", "")).endswith("-dirty"):
            findings.append(f"dense {label}: the build was made from a dirty tree ({info.get('describe')})")
        stamp = str(info.get("commit", ""))
        if commit and not (len(stamp) >= 7 and commit.startswith(stamp)):
            findings.append(f"dense {label}: build commit {stamp!r} is not {commit}")

    sample = float(np.max(np.abs(data))) if data.size else 0.0
    cr = acr.true_peak(data)
    fir = true_peak_fir(data)
    m_max, m_at = momentary_max(energy, rate, M_HOP_S)
    m_r1, _ = momentary_max(energy, rate, R1_HOP_S)
    detail = {"mode": mode, "camera": bot.get("cameraMode"), "wav": str(wav), "wavSha256": sha256(wav),
              "seconds": round(seconds, 3), "truePeakDbtp": max(db(sample), db(cr), db(fir)),
              "truePeakFirDbtp": round(db(fir), 3), "truePeakDbtpR1": round(db(cr), 3), "samplePeakDbfs": round(db(sample), 3),
              "momentaryMaxLufs": m_max, "momentaryMaxAt": m_at, "momentaryMaxLufsR1": round(m_r1, 3),
              "maxAlive": max_alive, "denseStart": first, "denseSeconds": round(dense_len, 2),
              "weaponFireDelivered": delivered, "weaponFireStartsDense": len(fires_in),
              "shotPeakMedianDbfs": round(peak_median, 2) if peak_median is not None else None,
              "build": {k: info.get(k) for k in ("stamped", "describe", "commit", "mode")},
              "meanFrameMs": bot.get("meanFrameMs"), "qaLoad1": [(qa or {}).get("load1Before"), (qa or {}).get("load1After")]}
    if first is not None and first < seconds - 0.4:
        a = int(first * rate)
        detail["denseWindow"] = {"truePeakDbtpR1": round(db(acr.true_peak(data[a:])), 3),
                                 "momentaryMaxLufs": round(momentary_max(energy[a:], rate, M_HOP_S, first)[0], 3)}
    report = folder / "report.json"
    if report.is_file():
        rep = read_json(report) or {}
        full = rep.get("full") or {}
        if rep.get("wav_sha256") != detail["wavSha256"]:
            findings.append(f"dense {label}: report.json was made from another WAV (sha256)")
        else:
            for key, mine in (("true_peak_dbtp", db(cr)), ("lufs_m_max", m_r1)):
                theirs = full.get(key)
                if not isinstance(theirs, (int, float)) or abs(theirs - mine) > REPORT_TOLERANCE:
                    findings.append(f"dense {label}: report.json {key} {theirs} disagrees with the WAV ({mine:.3f})")
    return detail, findings


def limiter_envelope(data, rate):
    """Peak detector of the master limiter on the linked (max |channel|) signal, one-pole attack/release; dBFS per sample."""
    a = np.abs(data.astype(np.float64)).max(axis=1)
    att, rel = math.exp(-1.0 / (LIMITER_ATTACK_S * rate)), math.exp(-1.0 / (LIMITER_RELEASE_S * rate))
    env = np.empty_like(a)
    e = 0.0
    for i, v in enumerate(a.tolist()):
        c = att if v > e else rel
        e = c * e + (1.0 - c) * v
        env[i] = e
    return 20.0 * np.log10(env + 1e-12)


def measure_a9(wav):
    wav = Path(wav)
    markers_path, telemetry_path = Path(str(wav) + ".markers.txt"), Path(str(wav) + ".telemetry.tsv")
    findings = [f"a9: missing {p}" for p in (wav, markers_path, telemetry_path) if not p.is_file()]
    if not wav.is_file():
        return None, findings
    try:
        data, rate = acr.read_wav(wav)
    except (ValueError, OSError) as exc:
        return None, findings + [f"a9: {wav}: {exc}"]
    seconds = data.shape[0] / rate
    if seconds < A9_MIN_SECONDS:
        findings.append(f"a9: render {seconds:.1f} s < {A9_MIN_SECONDS:.0f} s")
    sample = float(np.max(np.abs(data))) if data.size else 0.0
    if db(sample) > A9_CLIP_DBFS:
        findings.append(f"a9: uncompressed sample peak {db(sample):.2f} dBFS > {A9_CLIP_DBFS} — clipped, the GR would read low")
    mono_peak = np.abs(data).max(axis=1)
    blocks = [db(float(mono_peak[i:i + rate].max())) for i in range(0, data.shape[0] - rate + 1, rate)]
    loud = sum(1 for b in blocks if b >= A9_BLOCK_FLOOR_DBFS)
    if loud < A9_MIN_LOUD_BLOCKS:
        findings.append(f"a9: {loud} one-second blocks with a peak >= {A9_BLOCK_FLOOR_DBFS} dBFS < {A9_MIN_LOUD_BLOCKS} (no stack)")
    combat = {}
    if markers_path.is_file():
        dropped = read_markers(markers_path)["dropped"]
        if dropped != 0:
            findings.append(f"a9: capture dropped samples {dropped} (must be 0)")
    if telemetry_path.is_file():
        tel = read_telemetry(telemetry_path)
        combat = tel["combat"]
        if combat.get("ClassicWeaponFire", 0) < A9_MIN_FIRE or combat.get("Explosion", 0) < A9_MIN_EXPLOSIONS:
            findings.append(f"a9: ClassicWeaponFire {combat.get('ClassicWeaponFire', 0)} / Explosion {combat.get('Explosion', 0)} "
                            f"delivered, the stack needs >= {A9_MIN_FIRE} / {A9_MIN_EXPLOSIONS}")
    env = limiter_envelope(data, rate)
    env_max = float(env.max()) if env.size else -240.0
    gr = np.maximum(0.0, env - LIMITER_THRESHOLD_DB)
    detail = {"wav": str(wav), "wavSha256": sha256(wav), "seconds": round(seconds, 3),
              "preLimiterGrDb": max(0.0, env_max - LIMITER_THRESHOLD_DB), "envelopeMaxDbfs": round(env_max, 3),
              "headroomToLimiterDb": round(LIMITER_THRESHOLD_DB - env_max, 3), "uncompressedSamplePeakDbfs": round(db(sample), 3),
              "grP99Db": round(float(np.percentile(gr, 99)), 3), "timeInGrPct": round(float((gr > 0).mean() * 100), 3),
              "loudBlocks": f"{loud}/{len(blocks)}", "weaponFireDelivered": combat.get("ClassicWeaponFire"),
              "explosionsDelivered": combat.get("Explosion")}
    return detail, findings


def read_blocks(path):
    rows = []
    with open(path, encoding="utf-8") as f:
        for row in csv.DictReader(f):
            try:
                rows.append((int(row["frame"]), row["variant"].strip()))
            except (KeyError, ValueError, AttributeError):
                continue
    return rows


def burst_windows(blocks, variant, rate, frames):
    """[(start, end)] sample ranges of the burst windows in every complete block of `variant`."""
    out = []
    for start, v in blocks:
        if v != variant or start + int(BLOCK_S * rate) > frames:
            continue
        for cycle in CYCLE_OFFSETS_S:
            out.append((start + int((cycle + BURST_S[0]) * rate), start + int((cycle + BURST_S[1]) * rate)))
    return out


def measure_take(mode, wav):
    """One listening take: (detail, findings)."""
    wav = Path(wav)
    blocks_path = wav.with_name(wav.name.replace("_take.wav", "_blocks.csv"))
    markers_path, telemetry_path = Path(str(wav) + ".markers.txt"), Path(str(wav) + ".telemetry.tsv")
    findings = [f"listening {mode}: missing {p}" for p in (blocks_path, markers_path, telemetry_path) if not p.is_file()]
    if not blocks_path.is_file():
        return None, findings
    try:
        data, rate = acr.read_wav(wav)
        energy = acr.k_energy(data, rate)
    except (ValueError, OSError) as exc:
        return None, findings + [f"listening {mode}: {wav}: {exc}"]
    blocks = read_blocks(blocks_path)
    windows = {v: burst_windows(blocks, v, rate, data.shape[0]) for v in "AWR"}
    counts = {v: len(w) // len(CYCLE_OFFSETS_S) for v, w in windows.items()}
    short = [v for v in "AWR" if counts[v] < STEM_MIN_BLOCKS]
    if short:
        return None, findings + [f"listening {mode}: complete blocks {counts}, each of A W R needs >= {STEM_MIN_BLOCKS}"]
    power = {v: float(np.mean([energy[a:b].mean() for a, b in w])) for v, w in windows.items()}
    if min(power.values()) <= 0.0:
        return None, findings + [f"listening {mode}: a stem is silent in its burst windows ({power})"]
    w_peaks = [db(float(np.max(np.abs(data[a:b])))) for a, b in windows["W"]]
    w_peak_median = statistics.median(w_peaks)
    if w_peak_median < SHOT_PEAK_FLOOR_DBFS:
        findings.append(f"listening {mode}: W-stem burst peak median {w_peak_median:.1f} dBFS < {SHOT_PEAK_FLOOR_DBFS} (no weapon)")
    additivity = 10.0 * math.log10(power["A"] / (power["W"] + power["R"]))
    if abs(additivity) > ADDITIVITY_DB:
        findings.append(f"listening {mode}: A vs W + R {additivity:+.2f} dB, beyond ±{ADDITIVITY_DB} (the stems do not split the mix)")
    shots = None
    if markers_path.is_file():
        dropped = read_markers(markers_path)["dropped"]
        if dropped != 0:
            findings.append(f"listening {mode}: capture dropped samples {dropped} (must be 0)")
    if telemetry_path.is_file():
        tel = read_telemetry(telemetry_path)
        shots = tel["combat"].get("ClassicWeaponFire", 0)
        if shots < MIN_TAKE_SHOTS:
            findings.append(f"listening {mode}: ClassicWeaponFire delivered {shots} < {MIN_TAKE_SHOTS}")
        if tel["overflow"] != 0:
            findings.append(f"listening {mode}: telemetry ring overflow {tel['overflow']} (must be 0)")
    detail = {"mode": mode, "wav": str(wav), "wavSha256": sha256(wav), "blocksSha256": sha256(blocks_path),
              "weaponMarginLu": 10.0 * math.log10(power["W"] / power["R"]),
              "weaponBurstDb": round(10.0 * math.log10(power["W"]), 3), "restBurstDb": round(10.0 * math.log10(power["R"]), 3),
              "additivityDb": round(additivity, 3), "blocks": counts, "wBurstPeakMedianDbfs": round(w_peak_median, 2),
              "weaponFireDelivered": shots}
    return detail, findings


# ---------------------------------------------------------------------------------------------------- the artifact


def measure(dense, a9, listening, xmls, commit, dense_seconds):
    """(values, detail, findings, samples) over every take."""
    findings, detail = [], {"dense": {}, "a9": None, "listening": {}}
    for mode in REQUIRED_DENSE:
        if mode not in {m for m, _ in dense}:
            findings.append(f"dense {mode}: no capture given (--dense {mode}=DIR)")
    seen = {}
    for mode, folder in dense:   # a mode may repeat (several runs): every run counts, the worst wins
        seen[mode] = seen.get(mode, 0) + 1
        label = mode if seen[mode] == 1 else f"{mode}#{seen[mode]}"
        d, f = measure_dense(mode, folder, commit, dense_seconds, label)
        findings += f
        if d:
            detail["dense"][label] = d
    if a9 is None:
        findings.append("a9: no render given (--a9 WAV)")
    else:
        d, f = measure_a9(a9)
        findings += f
        detail["a9"] = d
    takes = {}
    if listening is not None:
        for wav in sorted(Path(listening).glob("*_take.wav")):
            parts = wav.stem.split("_")
            if len(parts) >= 3 and parts[1] in LISTEN_MODES:
                takes[LISTEN_MODES[parts[1]]] = wav
    for mode in LISTEN_MODES.values():
        if mode not in takes:
            findings.append(f"listening {mode}: no take in {listening} (DinlemePaketi_AB_Uret)")
    for mode, wav in sorted(takes.items()):
        d, f = measure_take(mode, wav)
        findings += f
        if d:
            detail["listening"][mode] = d
    results = xml_results(xmls)
    for kind, name in TESTS.items():
        if results.get(name) != "Passed":
            findings.append(f"{kind}: {name} is {results.get(name) or 'not in the given XML'} (must be Passed)")
    values = {}
    if detail["dense"]:
        values["truePeakDbtp"] = max(d["truePeakDbtp"] for d in detail["dense"].values())
        values["momentaryMaxLufs"] = max(d["momentaryMaxLufs"] for d in detail["dense"].values())
    if detail["a9"]:
        values["preLimiterGrDb"] = detail["a9"]["preLimiterGrDb"]
    if detail["listening"]:
        values["weaponMarginLu"] = min(d["weaponMarginLu"] for d in detail["listening"].values())
    samples = len(detail["dense"]) + (1 if detail["a9"] else 0) + len(detail["listening"])
    return values, detail, findings, samples


def kirli_reasons(loads, manual, detail):
    reasons = [manual] if manual else []
    peak, where = load_peak(loads)
    if peak is None:
        reasons.append("no load log (--load): the quiet machine is unproven")
    elif peak >= LOAD1_QUIET:
        reasons.append(f"load1 max {peak:g} >= {LOAD1_QUIET:g} ({where})")
    for mode, d in sorted(detail["dense"].items()):
        for value in d.get("qaLoad1") or []:
            try:
                if value not in (None, "") and float(value) >= LOAD1_QUIET:
                    reasons.append(f"dense {mode}: qa-report load1 {value} >= {LOAD1_QUIET:g}")
                    break
            except (TypeError, ValueError):
                continue
    return reasons


def artifact(values, detail, samples, commit, reasons):
    data = {"schema": "dd-r2-measure-v1", "gate": GATE, "commit": commit, "source": SOURCE,
            "values": {k: values[k] for k in KEYS}, "samples": samples, "takes": detail, "r1Closing": R1_CLOSING}
    if reasons:
        data["note"] = ("KİRLİ: " + "; ".join(reasons) + " — ranking only, not a gate value (SABİT-KURALLAR 5.8); "
                        "re-measure in a quiet window")
        data["kirli"] = True
    return data


def judge(data):
    contract = r2_gates.load_contract((HERE / "r2-gates.json").read_text(encoding="utf-8"))
    gate = next(g for g in contract["gates"] if g["id"] == GATE)
    return r2_gates.evaluate_gate(gate, data["commit"], measure=data)


def kirli_path(out):
    out = Path(out)
    return out.with_name(out.name[:-5] + ".KIRLI.json" if out.name.endswith(".json") else out.name + ".KIRLI.json")


def summary_lines(values, detail):
    lines = []
    for mode, d in sorted(detail["dense"].items()):
        r1 = R1_CLOSING["dense"].get(d["mode"], {})
        lines.append(f"dense {mode:7} TP {d['truePeakDbtp']:7.2f} dBTP (FIR {d['truePeakFirDbtp']:.2f}, CR {d['truePeakDbtpR1']:.2f}"
                     f"{'; R1 ' + format(r1['truePeakDbtpR1'], '.2f') if r1 else ''}) · M max {d['momentaryMaxLufs']:6.2f} LUFS "
                     f"@{d['momentaryMaxAt']} s (100 ms grid {d['momentaryMaxLufsR1']:.2f}"
                     f"{'; R1 ' + format(r1['momentaryMaxLufsR1'], '.2f') if r1 else ''}) · alive {d['maxAlive']} "
                     f"dense {d['denseSeconds']} s · shots {d['weaponFireDelivered']}/{d['weaponFireStartsDense']} "
                     f"peak med {d['shotPeakMedianDbfs']} dBFS")
    if detail["a9"]:
        a = detail["a9"]
        lines.append(f"a9            GR {a['preLimiterGrDb']:.2f} dB (envelope max {a['envelopeMaxDbfs']:.2f} dBFS, headroom "
                     f"{a['headroomToLimiterDb']:.2f} dB; R1 GR {R1_CLOSING['a9']['preLimiterGrDb']:.2f}, envelope "
                     f"{R1_CLOSING['a9']['envelopeMaxDbfs']:.2f}) · peak {a['uncompressedSamplePeakDbfs']:.2f} dBFS")
    for mode, t in sorted(detail["listening"].items()):
        lines.append(f"listening {mode:7} W/R margin {t['weaponMarginLu']:+.2f} LU (R1 mix B "
                     f"{R1_CLOSING['listening'][mode]['weaponMarginLu']:+.2f}) · additivity {t['additivityDb']:+.2f} dB · "
                     f"blocks {t['blocks']} · W peak med {t['wBurstPeakMedianDbfs']} dBFS")
    lines.append("S3 values " + " · ".join(f"{k} {values[k]:.3f}" if k in values else f"{k} -" for k in KEYS))
    return lines


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dense", action="append", default=[], metavar="MODE=DIR",
                        help="Dense-40 run folder (tp, fp, topdown); a mode may repeat, the worst run counts")
    parser.add_argument("--a9", help="the A9 stack render WAV (sidecars next to it)")
    parser.add_argument("--listening", help="the DinlemePaketi_AB_Uret folder (*_take.wav + *_blocks.csv + sidecars)")
    parser.add_argument("--xml", action="append", default=[], help="NUnit XML of the A9 / listening PlayMode runs")
    parser.add_argument("--load", action="append", default=[], help="load log of the run window (sysctl vm.loadavg lines)")
    parser.add_argument("--kirli", help="mark the run KİRLİ with this reason (C-00's call)")
    parser.add_argument("--dense-seconds", type=float, default=40.0, help="the run's -ddAudioDenseSeconds")
    parser.add_argument("--commit", default=os.environ.get("DD_R2_COMMIT"), help="40-hex commit under test (DD_R2_COMMIT)")
    parser.add_argument("--out", help="artifact path (default $DD_R2_MEASURE_DIR/r2-S3.json)")
    parser.add_argument("--dry-run", action="store_true", help="measure and print, never write")
    args = parser.parse_args(argv)

    dense = []
    for item in args.dense:
        mode, _, folder = item.partition("=")
        if mode not in CAMERA or not folder:
            print(f"--dense wants MODE=DIR with MODE one of {', '.join(CAMERA)}: {item!r}", file=sys.stderr)
            return 64
        dense.append((mode, folder))
    commit = args.commit or ""
    if not args.dry_run and not SHA.fullmatch(commit):
        print("--commit (or DD_R2_COMMIT) must be the 40-hex commit under test", file=sys.stderr)
        return 64
    out = args.out or (str(Path(os.environ["DD_R2_MEASURE_DIR"]) / "r2-S3.json") if os.environ.get("DD_R2_MEASURE_DIR") else None)
    if not args.dry_run and not out:
        print("--out (or DD_R2_MEASURE_DIR) is required", file=sys.stderr)
        return 64

    values, detail, findings, samples = measure(dense, args.a9, args.listening, args.xml, commit, args.dense_seconds)
    for line in summary_lines(values, detail):
        print(line)
    for finding in findings:
        print("SOURCE:", finding)
    complete = all(k in values for k in KEYS)
    if findings or not complete:
        if args.dry_run and complete:
            row = judge(artifact(values, detail, samples, commit or "0" * 40, []))
            print(f"dry run: the values alone would judge S3 {row['status']}" + (f" ({row['reason']})" if row["reason"] else ""))
        sys.stdout.flush()
        print(f"S3: {len(findings)} source finding(s) — nothing written, S3 stays BLOCKED", file=sys.stderr)
        return 1
    reasons = kirli_reasons(args.load, args.kirli, detail)
    data = artifact(values, detail, samples, commit or "0" * 40, reasons)
    row = judge(data)
    print(f"S3 {row['status']}" + (f": {row['reason']}" if row["reason"] else ""))
    if reasons:
        print(data["note"])
    if args.dry_run:
        print("dry run: nothing written")
        return 3 if reasons else (0 if row["status"] == "PASS" else 2)
    path = kirli_path(out) if reasons else Path(out)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=1, allow_nan=False) + "\n", encoding="utf-8")
    print(f"written {path}")
    if reasons:
        return 3
    return 0 if row["status"] == "PASS" else 2


if __name__ == "__main__":
    sys.exit(main())
