#!/usr/bin/env python3
"""Fail-closed WP-064 closure gate for one commit and immutable attempts."""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import re
import subprocess
import sys


MEAN_MAX = 8.0
P99_MAX = 10.0
RELEASE_CAP_MB = 550.0
LOG_FAILURES = tuple(re.compile(pattern, re.IGNORECASE) for pattern in (
    r"\bShader error in\b",
    r"\berror CS\d+\b",
    r"\bUnhandled exception\b",
    r"\bNative Crash Reporting\b",
    r"\bCrash!!!\b",
    r"\bSegmentation fault\b",
    r"\bBuildFailedException\b",
    r"\[QaRunBot\] write failed",
    # WP-064 v2 (S-10 finding 5): the black-world failure modes of the 147b02d candidate — Metal, texture import and
    # un-materialised LFS pointers imported as assets.
    r"\bMetal: Error\b",
    r"\bShader\b.*\bnot supported\b",
    r"\bCould not create texture\b",
    r"\bFile could not be read\b",
    r"\bUnable to import\b",
    r"version https://git-lfs",
))
# Exact known-benign lines that a v2 pattern would otherwise catch (whole-line match after strip; never a substring rule).
# URP 14 probe-volume voxelisation shader: Apple GPUs lack conservative rasterisation; APV is not used by this project.
LOG_ALLOWED_EXACT = frozenset((
    "Shader Hidden/ProbeVolume/VoxelizeScene is not supported: GPU does not support conservative rasterization",
))
LFS_POINTER_MAGIC = b"version https://git-lfs.github.com/spec/v1"
STALL_FRACTION_MAX = 0.01      # v2 finding 6: the stall-filtered p99 may exclude at most 1 % of the window's frames
STALL_MS = 33.33               # PilotPerfProbe WP-014 rule: frames above two 60 Hz intervals are host presentation stalls
MIN_COMPLETE_PERF = 3
HEX_COMMIT = re.compile(r"[0-9a-f]{7,40}")
ASSETS_PATH = "prototype/DeathsDemolition_PlayableConcept/Assets"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def finite_number(value, minimum=None, maximum=None):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        return False
    return (minimum is None or value >= minimum) and (maximum is None or value <= maximum)


def exact_int(value, minimum=None):
    return isinstance(value, int) and not isinstance(value, bool) and (minimum is None or value >= minimum)


def valid_gpu_stage(gpu):
    return (
        isinstance(gpu, dict)
        and gpu.get("pass") is True
        and exact_int(gpu.get("gpuSampledFrames"), 1)
        and finite_number(gpu.get("gpuFrameAvgMs"), 0.0)
        and finite_number(gpu.get("gpuFrameMaxMs"), 0.0)
        and gpu.get("gpuMeasurementSource") == "FrameTimingManager"
    )


def base_mode(footprint):
    """build-report.sh labels the mode with the target / compression ("release-lz4hc", "release-win64-lz4hc"): the base is the build mode."""
    mode = footprint.get("mode") if isinstance(footprint, dict) else None
    return mode.split("-")[0] if isinstance(mode, str) else None


def split_dir_commit(item, default):
    """r3: "<dir>@<sha>" names the commit a soak / report run executed; a bare "<dir>" uses the default."""
    name, _, commit = item.partition("@")
    return name, (commit or default)


def valid_release_footprint(footprint):
    return (
        isinstance(footprint, dict)
        and base_mode(footprint) == "release"
        and footprint.get("capApplicable") is True
        and footprint.get("capExceeded") is False
        and finite_number(footprint.get("appMB"), 0, RELEASE_CAP_MB)
    )


def load_json(path, check):
    try:
        with path.open(encoding="utf-8") as handle:
            value = json.load(handle)
        if not isinstance(value, dict):
            raise ValueError("root must be an object")
        return value
    except Exception as error:
        check("input." + path.name, False, f"{path}: {type(error).__name__}: {error}")
        return {}


def flag_value(command, flag):
    if not isinstance(command, list) or any(not isinstance(item, str) for item in command):
        return None
    positions = [index for index, item in enumerate(command) if item == flag]
    if len(positions) != 1 or positions[0] + 1 >= len(command):
        return None
    return command[positions[0] + 1]


def csv_metrics(path):
    values = []
    try:
        with path.open(newline="", encoding="utf-8") as handle:
            for index, row in enumerate(csv.reader(handle)):
                if not row:
                    raise ValueError(f"empty row {index + 1}")
                try:
                    value = float(row[0])
                except ValueError:
                    if index == 0:
                        continue
                    raise
                if not math.isfinite(value) or value <= 0:
                    raise ValueError(f"invalid frame value at row {index + 1}: {row[0]}")
                values.append(value)
    except Exception as error:
        return [], f"{type(error).__name__}: {error}"
    return values, ""


def csv_rows(path):
    """Compatibility helper used by the gate's focused unit tests."""
    values, error = csv_metrics(path)
    return 0 if error else len(values)


def log_failures(paths):
    """v2 (finding 4): every path handed in is REQUIRED — a missing or empty log is a failure, not a pass."""
    hits = []
    for path in paths:
        if not path.is_file():
            hits.append(f"{path}: missing (required log)")
            continue
        if path.stat().st_size == 0:
            hits.append(f"{path}: empty (required log)")
            continue
        try:
            for number, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
                if line.strip() in LOG_ALLOWED_EXACT:
                    continue
                if any(pattern.search(line) for pattern in LOG_FAILURES):
                    hits.append(f"{path}:{number}:{line[:240]}")
        except OSError as error:
            hits.append(f"{path}: unreadable: {error}")
    return hits


def lfs_pointers_in_build(build_dir):
    """v2 (finding 5): a Player whose data carries a git-lfs pointer shipped a text stub instead of the asset
    (the 147b02d black world). Scans every file of every *.app under the build directory for the pointer magic."""
    hits, scanned = [], 0
    apps = sorted(build_dir.glob("*.app"))
    if not apps:
        return ["no .app in " + str(build_dir)], 0
    overlap = len(LFS_POINTER_MAGIC) - 1
    for app in apps:
        for path in sorted(p for p in app.rglob("*") if p.is_file() and not p.is_symlink()):
            scanned += 1
            try:
                with path.open("rb") as handle:
                    tail = b""
                    while True:
                        block = handle.read(4 * 1024 * 1024)
                        if not block:
                            break
                        if LFS_POINTER_MAGIC in tail + block:
                            hits.append(str(path.relative_to(build_dir)))
                            break
                        tail = block[-overlap:]
            except OSError as error:
                hits.append(f"{path}: unreadable: {error}")
    return hits, scanned


def app_binary(build_dir):
    apps = sorted(build_dir.glob("*.app"))
    if len(apps) != 1:
        return None
    macos = apps[0] / "Contents" / "MacOS"
    if not macos.is_dir():
        return None
    binaries = [item for item in macos.iterdir() if item.is_file()]
    return binaries[0] if len(binaries) == 1 else None


def editor_build_matches(report, mode, commit):
    """v2 (finding 1): the stamp must name this commit (7–40 lowercase hex, a prefix of the execution SHA — an empty
    or "unknown" stamp is a prefix of everything and used to pass) and `git describe --dirty` must be clean."""
    stamp = report.get("stamp") if isinstance(report.get("stamp"), dict) else {}
    stamped_commit = stamp.get("commit")
    describe = stamp.get("describe")
    return (
        report.get("mode") == mode
        and report.get("result") == "Succeeded"
        and report.get("totalErrors") == 0
        and stamp.get("stamped") is True
        and isinstance(stamped_commit, str)
        and HEX_COMMIT.fullmatch(stamped_commit) is not None
        and isinstance(commit, str)
        and commit.startswith(stamped_commit)
        and isinstance(describe, str)
        and describe not in ("", "unknown")
        and "-dirty" not in describe
        and stamp.get("mode") == mode
    )


def library_provenance_matches(provenance, commit, assets_tree, check, prefix):
    """v2 (finding 2): the Library the build ran on imported exactly the gated commit — recorded right after the
    build (tools/qa/library_provenance.py): same HEAD, clean tree, no LFS pointer on disk, the Assets tree hash
    equal to the commit's, Library present. Library mtimes are recorded for audit."""
    check(prefix + ".schema", provenance.get("schema") == "dd-wp064-library-provenance-v1" and provenance.get("phase") == "post-build",
          {"schema": provenance.get("schema"), "phase": provenance.get("phase")})
    check(prefix + ".commit", provenance.get("head") == commit, provenance.get("head"))
    check(prefix + ".clean", provenance.get("porcelain") == "", str(provenance.get("porcelain"))[:300])
    check(prefix + ".lfsPointers", provenance.get("lfsPointersOnDisk") == 0 and provenance.get("lfsMissing") == 0,
          {"pointers": provenance.get("lfsPointersOnDisk"), "missing": provenance.get("lfsMissing")})
    check(prefix + ".assetsTree", bool(assets_tree) and provenance.get("assetsTree") == assets_tree,
          {"recorded": provenance.get("assetsTree"), "commit": assets_tree})
    library = provenance.get("library") if isinstance(provenance.get("library"), dict) else {}
    check(prefix + ".libraryPresent", library.get("artifactDbBytes", 0) > 0 and finite_number(library.get("artifactDbMtime"), 1.0),
          {key: library.get(key) for key in ("artifactDbBytes", "artifactDbMtime", "sourceAssetDbMtime", "lastSceneManagerSetupMtime")})


def read_command(path, name, check):
    try:
        with path.open(encoding="utf-8") as handle:
            command = json.load(handle)
        if not isinstance(command, list) or any(not isinstance(item, str) for item in command):
            raise ValueError("command root must be a string array")
        return command
    except Exception as error:
        check(name, False, f"{type(error).__name__}: {error}")
        return []


def read_ledger(path, check):
    """v2 (finding 6): the runner appends one line per Player attempt (start and end) to attempts-ledger.jsonl."""
    entries = {}
    try:
        with path.open(encoding="utf-8") as handle:
            for number, line in enumerate(handle, 1):
                if not line.strip():
                    continue
                row = json.loads(line)
                if not isinstance(row, dict) or not isinstance(row.get("attemptId"), str) or not isinstance(row.get("dir"), str):
                    raise ValueError(f"line {number}: malformed ledger row")
                entries.setdefault(row["attemptId"], {}).update(row)
    except Exception as error:
        check("ledger.read", False, f"{path}: {type(error).__name__}: {error}")
        return None
    check("ledger.read", True, f"{len(entries)} attempts")
    return list(entries.values())


def classify_perf_ledger(rows):
    """v2 (finding 6): complete attempts are all gated; a discard is legal only for host reasons."""
    complete, discarded, unexplained = [], [], []
    for row in rows:
        status = row.get("status")
        if status == "complete":
            complete.append(row)
        elif status == "preflight-failed" or (status == "failed" and row.get("hostClean") is False and row.get("timedOut") is False):
            discarded.append({"dir": row.get("dir"), "status": status, "hostClean": row.get("hostClean"), "timedOut": row.get("timedOut")})
        else:
            unexplained.append({"dir": row.get("dir"), "status": status, "exitCode": row.get("exitCode"), "hostClean": row.get("hostClean")})
    return complete, discarded, unexplained


def stall_exclusion(window, frames):
    """v2 (finding 6): the filtered p99 may drop at most STALL_FRACTION_MAX of the frames, and the probe's own count of
    dropped frames must equal the raw CSV's frames above STALL_MS."""
    excluded, frame_count = window.get("framesExcludedAsStalls"), window.get("frameCount")
    stalls_csv = sum(1 for value in frames if value > STALL_MS)
    raw_p99 = sorted(frames)[min(len(frames) - 1, int(math.ceil(0.99 * len(frames))) - 1)] if frames else None
    counted = exact_int(excluded, 0) and exact_int(frame_count, 1)
    ok = counted and excluded == stalls_csv and excluded <= STALL_FRACTION_MAX * frame_count
    return ok, {"excluded": excluded, "csvOver33ms": stalls_csv, "fraction": (excluded / frame_count) if counted else None,
                "max": STALL_FRACTION_MAX, "rawP99FromCsv": raw_p99}


def validate_attempt(attempt_dir, kind, commit, binary_hash, required, check):
    provenance = load_json(attempt_dir / "attempt.json", check)
    command_path = attempt_dir / "command.json"
    command = read_command(command_path, attempt_dir.name + ".commandJson", check)
    prefix = attempt_dir.name
    check(prefix + ".provenanceSchema", provenance.get("schema") == "dd-wp064-attempt-v1", provenance.get("schema"))
    check(prefix + ".provenanceKind", provenance.get("kind") == kind, provenance.get("kind"))
    check(prefix + ".provenanceCommit", provenance.get("executionCommit") == commit, provenance.get("executionCommit"))
    check(prefix + ".provenanceComplete", provenance.get("status") == "complete" and provenance.get("exitCode") == 0,
          f"{provenance.get('status')} exit={provenance.get('exitCode')}")
    check(prefix + ".binary", provenance.get("binarySha256") == binary_hash, provenance.get("binarySha256"))
    check(prefix + ".commandHash", command_path.is_file() and provenance.get("commandSha256") == sha256(command_path),
          provenance.get("commandSha256"))
    started = provenance.get("startedEpoch")
    completed = provenance.get("completedEpoch")
    check(prefix + ".timeRange", finite_number(started, 0) and finite_number(completed, 0) and completed >= started,
          f"{started}..{completed}")
    hashes = provenance.get("artifactSha256") if isinstance(provenance.get("artifactSha256"), dict) else {}
    missing, mismatched = [], []
    for name in required:
        path = attempt_dir / name
        if not path.is_file():
            missing.append(name)
        elif hashes.get(name) != sha256(path):
            mismatched.append(name)
    check(prefix + ".artifacts", not missing and not mismatched, {"missing": missing, "hashMismatch": mismatched})
    return command, provenance


def external_build(soak_root, name, soak_commit, check, prefix):
    """DEC-091 soak evidence from tools/qa-style per-run dev builds (S-10 soak.sh): <soak_root>/build-<name> must be a clean
    dev build of the soak commit; returns the Player binary hash the run used."""
    build = soak_root / ("build-" + name)
    report = load_json(build / "report.json", check)
    editor = load_json(build / "build-report.json", check)
    binary = app_binary(build)
    check(prefix + ".build", report.get("mode") == "dev" and report.get("ok") is True and report.get("head") == soak_commit,
          {key: report.get(key) for key in ("mode", "ok", "head")})
    check(prefix + ".buildEditor", editor_build_matches(editor, "dev", soak_commit),
          {"result": editor.get("result"), "errors": editor.get("totalErrors"), "stamp": editor.get("stamp")})
    check(prefix + ".buildBinary", bool(binary and binary.is_file()), str(binary) if binary else None)
    return (sha256(binary) if binary and binary.is_file() else ""), binary


def quiet_before_start(wait_log, name, needed=3):
    """soak.sh / clean-run.sh host record: the last lines before 'START build+soak <name>' show >= needed consecutive quiet
    minutes (no Unity/Blender/Player, no slot held, load under the runner limit)."""
    try:
        lines = wait_log.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as error:
        return False, f"{wait_log}: {error}"
    starts = [i for i, line in enumerate(lines) if ("START build+soak " + name) in line]
    if not starts:
        return False, "no START line"
    before = [line for line in lines[:starts[-1]] if "quiet=" in line]
    last = before[-1] if before else ""
    match = re.search(r"busy=(\d+) slots=(\d+) load=([0-9.]+) quiet=(\d+)", last)
    ok = bool(match) and int(match.group(1)) == 0 and int(match.group(2)) == 0 and int(match.group(4)) >= needed
    return ok, last.strip()


def check_external_soak(soak, name, seed, rounds, soak_commit, soak_root, check, prefix):
    """A stability attempt recorded by tools/qa-independent soak.sh (no attempt.json): same contract, own provenance."""
    required = ["player.log", "exit.txt", "bot-summary.json", "qa-report.json", "events.txt", "userdata-restore.txt"]
    missing = [item for item in required if not (soak / item).is_file() or (soak / item).stat().st_size == 0]
    check(prefix + ".artifacts", not missing, {"missing": missing})
    binary_hash, binary = external_build(soak_root, name, soak_commit, check, prefix)
    summary_path = soak / "bot-summary.json"
    fresh = bool(binary and binary.is_file() and summary_path.is_file() and summary_path.stat().st_mtime >= binary.stat().st_mtime)
    check(prefix + ".freshArtifacts", fresh, "bot-summary newer than the Player binary")
    try:
        exit_ok = (soak / "exit.txt").read_text(encoding="utf-8").strip() == "exit=0"
    except OSError:
        exit_ok = False
    check(prefix + ".exit", exit_ok, "exit=0")
    host_ok, host_line = quiet_before_start(soak_root / (name + ".wait.log"), name)
    check(prefix + ".host", host_ok, host_line)
    try:
        log_text = (soak / "player.log").read_text(encoding="utf-8", errors="replace")
    except OSError:
        log_text = ""
    armed = re.search(r"\[QaRunBot\] armed rounds=(\d+) seed=(-?\d+)", log_text)
    check(prefix + ".command", bool(armed) and int(armed.group(1)) == rounds and int(armed.group(2)) == seed,
          armed.group(0) if armed else "no armed line")
    try:
        restore = (soak / "userdata-restore.txt").read_text(encoding="utf-8")
        inventory_diff = (soak / "userdata-inventory.diff").read_text(encoding="utf-8") if (soak / "userdata-inventory.diff").is_file() else "missing"
    except OSError:
        restore, inventory_diff = "", "missing"
    check(prefix + ".userData", "USERDATA_RESTORED_OK" in restore and "MISMATCH" not in restore and inventory_diff == "",
          {"restore": restore.strip()[-200:], "inventoryDiff": inventory_diff[:200]})
    return binary_hash


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("root", help="evidence root containing build-dev/perf-* (and the soak directories unless --soak-root)")
    parser.add_argument("--commit", required=True)
    parser.add_argument("--release", help="release build evidence directory; default <root>/build-release")
    # DEC-091 (user 2026-09-24): the soak MUST is stability — two seeds, invulnerable-by-design bot, 12/12 each; real-bot runs
    # are a report (>= 5 runs, reached-round distribution), not a gate
    parser.add_argument("--soak-dirs", default="soak-61,soak-26")
    parser.add_argument("--seeds", default="61,26")
    parser.add_argument("--report-dirs", default="", help="comma-separated real-bot soak attempt dirs (>= 5 required, not gated)")
    # r3: the stability / report runs may come from another commit's dev builds (S-10 soak.sh: <soak-root>/build-<dir>) —
    # perf / release stay on --commit; the report names both commits
    parser.add_argument("--soak-root", help="directory holding the soak / report dirs; default <root>")
    parser.add_argument("--soak-commit", help="commit the soak / report runs executed (a dir may carry its own: <dir>@<sha>); default --commit")
    parser.add_argument("--seed", type=int, default=61)
    parser.add_argument("--rounds", type=int, default=12)
    parser.add_argument("--window", type=int, default=60)
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parents[2]))
    args = parser.parse_args()
    root = Path(args.root).resolve()
    release_dir = Path(args.release).resolve() if args.release else root / "build-release"
    checks = []

    def check(name, passed, value):
        checks.append({"name": name, "pass": bool(passed), "value": value})

    dev_dir = root / "build-dev"
    dev_report = load_json(dev_dir / "report.json", check)
    dev_editor_report = load_json(dev_dir / "build-report.json", check)
    dev_footprint = load_json(dev_dir / "footprint.json", check)
    release_report = load_json(release_dir / "report.json", check)
    release_editor_report = load_json(release_dir / "build-report.json", check)
    release_footprint = load_json(release_dir / "footprint.json", check)
    dev_binary = app_binary(dev_dir)
    release_binary = app_binary(release_dir)
    dev_hash = sha256(dev_binary) if dev_binary else ""
    release_hash = sha256(release_binary) if release_binary else ""

    check("build.dev", dev_report.get("mode") == "dev" and dev_report.get("ok") is True and dev_report.get("head") == args.commit,
          {key: dev_report.get(key) for key in ("mode", "ok", "head")})
    check("build.devEditor", editor_build_matches(dev_editor_report, "dev", args.commit),
          {"mode": dev_editor_report.get("mode"), "result": dev_editor_report.get("result"), "errors": dev_editor_report.get("totalErrors"), "stamp": dev_editor_report.get("stamp")})
    check("build.devBinary", bool(dev_binary and dev_binary.is_file()), dev_hash)
    check("build.devCapNotApplicable", base_mode(dev_footprint) == "dev" and dev_footprint.get("capApplicable") is False,
          {key: dev_footprint.get(key) for key in ("mode", "appMB", "capApplicable", "capExceeded")})
    check("build.release", release_report.get("mode") == "release" and release_report.get("ok") is True and release_report.get("head") == args.commit,
          {key: release_report.get(key) for key in ("mode", "ok", "head")})
    check("build.releaseEditor", editor_build_matches(release_editor_report, "release", args.commit),
          {"mode": release_editor_report.get("mode"), "result": release_editor_report.get("result"), "errors": release_editor_report.get("totalErrors"), "stamp": release_editor_report.get("stamp")})
    app_mb = release_footprint.get("appMB")
    check("build.releaseFootprintMode", base_mode(release_footprint) == "release" and release_footprint.get("capApplicable") is True,
          {key: release_footprint.get(key) for key in ("mode", "capApplicable")})
    check("build.releaseFootprint", valid_release_footprint(release_footprint),
          f"{app_mb} MB <= {RELEASE_CAP_MB} MB")
    check("build.releaseBinary", bool(release_binary and release_binary.is_file()), release_hash)

    try:
        head = subprocess.check_output(["git", "-C", args.repo, "rev-parse", "HEAD"], text=True).strip()
        dirty = subprocess.check_output(["git", "-C", args.repo, "status", "--porcelain"], text=True).strip()
        pointers = subprocess.check_output(["git", "-C", args.repo, "lfs", "ls-files"], text=True)
        missing_lfs = [line for line in pointers.splitlines() if " - " in line]
    except Exception as error:
        head, dirty, missing_lfs = "", "unreadable", [str(error)]
    check("git.executionCommit", head == args.commit, head)
    check("git.clean", dirty == "", dirty[:500])
    check("git.lfsMaterialized", not missing_lfs, missing_lfs[:20])

    # v2 (finding 2): Library provenance recorded at build time, compared with the commit's Assets tree.
    try:
        assets_tree = subprocess.check_output(["git", "-C", args.repo, "rev-parse", f"{args.commit}:{ASSETS_PATH}"], text=True).strip()
    except Exception:
        assets_tree = ""
    library_provenance_matches(load_json(dev_dir / "library-provenance.json", check), args.commit, assets_tree, check, "library.dev")
    library_provenance_matches(load_json(release_dir / "library-provenance.json", check), args.commit, assets_tree, check, "library.release")

    # v2 (finding 5): no git-lfs pointer shipped inside either Player.
    for label, build_dir in (("dev", dev_dir), ("release", release_dir)):
        pointer_hits, scanned = lfs_pointers_in_build(build_dir)
        check(f"build.{label}NoLfsPointer", not pointer_hits and scanned > 0, {"scannedFiles": scanned, "hits": pointer_hits[:20]})

    attempt_ids = set()
    player_logs = []
    # v2 (finding 6): the perf attempts come from the runner's ledger, not from a fixed perf-1..3 — every perf-* directory
    # must be in the ledger, every COMPLETE attempt is gated (a failed clean measurement cannot be dropped for a better
    # one), and an attempt may only be discarded for host reasons (preflight-failed / contaminated); a timed-out Player is a product failure.
    ledger = read_ledger(root / "attempts-ledger.jsonl", check)
    perf_dirs = sorted(path.name for path in root.glob("perf-*") if path.is_dir())
    ledger_perf = [row for row in (ledger or []) if row.get("kind") == "perf"]
    ledger_dirs = sorted(row.get("dir") for row in ledger_perf)
    check("ledger.coversPerfDirs", ledger is not None and ledger_dirs == perf_dirs, {"ledger": ledger_dirs, "disk": perf_dirs})
    complete, discarded, unexplained = classify_perf_ledger(ledger_perf)
    check("ledger.discardsHostOnly", not unexplained, {"discarded": discarded, "unexplained": unexplained})
    check("ledger.completePerf", len(complete) >= MIN_COMPLETE_PERF, f"{len(complete)} complete (>= {MIN_COMPLETE_PERF})")
    for row in sorted(complete, key=lambda item: item.get("dir", "")):
        attempt = root / row.get("dir", "")
        prefix = attempt.name
        required = ["command.json", "preflight.json", "host-monitor.json", "player.log", "exit.txt",
                    "01-scene-ready.json", "02-force-40.json", "03-warmup.json", "04-window.json",
                    "04d-gpu.json", "frame-times-ms.csv"]
        command, provenance = validate_attempt(attempt, "perf", args.commit, dev_hash, required, check)
        attempt_id = provenance.get("attemptId")
        check(prefix + ".uniqueAttempt", isinstance(attempt_id, str) and bool(attempt_id) and attempt_id not in attempt_ids and attempt_id == row.get("attemptId"), attempt_id)
        attempt_ids.add(attempt_id)
        preflight = load_json(attempt / "preflight.json", check)
        monitor = load_json(attempt / "host-monitor.json", check)
        scene = load_json(attempt / "01-scene-ready.json", check)
        force = load_json(attempt / "02-force-40.json", check)
        warm = load_json(attempt / "03-warmup.json", check)
        window = load_json(attempt / "04-window.json", check)
        gpu = load_json(attempt / "04d-gpu.json", check)
        player_logs.append(attempt / "player.log")
        check(prefix + ".host", preflight.get("pass") is True and monitor.get("pass") is True and monitor.get("timedOut") is False, "preflight+monitor")
        forbidden = {"-ddPilotLodBias", "-ddPilotAtmosphere", "-ddArmor", "-ddPilotVfxStress", "-ddPilotProfilerLog"}
        command_ok = not forbidden.intersection(command) and flag_value(command, "-ddPilotScene") == "Run" and flag_value(command, "-ddPilotResolution") == "1920x1080"
        try:
            command_ok = command_ok and float(flag_value(command, "-ddPilotWarmupSeconds")) == 10.0 and float(flag_value(command, "-ddPilotWindowSeconds")) == float(args.window)
        except (TypeError, ValueError):
            command_ok = False
        check(prefix + ".command", command_ok, command)
        check(prefix + ".scene", scene.get("scene") == "Run" and scene.get("screenWidth") == 1920 and scene.get("screenHeight") == 1080 and scene.get("graphicsDeviceType") == "Metal", scene.get("note"))
        force_ok = all(exact_int(force.get(key), 0) and force.get(key) == 40 for key in ("configuredSafetyCeiling", "ceiling", "forcedTarget", "achievedCount"))
        check(prefix + ".exact40Source", force_ok and force.get("pass") is True,
              {key: force.get(key) for key in ("configuredSafetyCeiling", "ceiling", "forcedTarget", "achievedCount")})
        check(prefix + ".warmup", finite_number(warm.get("warmupSeconds"), 10.0) and warm.get("pass") is True, warm.get("warmupSeconds"))
        exact_load = all(exact_int(window.get(key)) and window.get(key) == 40 for key in ("activeMin", "activeMax", "liveBodiesMin", "liveBodiesMax", "crowdMemberMin", "crowdMemberMax"))
        check(prefix + ".exact40Window", exact_load and window.get("loadIntegrityFloor") == 40 and window.get("loadIntegrityFraction") == 1.0 and window.get("pass") is True, window.get("note"))
        frame_count = window.get("frameCount")
        frames, csv_error = csv_metrics(attempt / "frame-times-ms.csv")
        check(prefix + ".csv", exact_int(frame_count, 1) and len(frames) == frame_count and not csv_error,
              f"rows={len(frames)} frames={frame_count} {csv_error}")
        mean, p99 = window.get("frameMeanMs"), window.get("frameP99FilteredMs")
        check(prefix + ".mean", finite_number(mean, 0.0, MEAN_MAX), mean)
        check(prefix + ".cleanP99", finite_number(p99, 0.0, P99_MAX), p99)
        # v2 (finding 6): the stall exclusion behind the filtered p99 is bounded and re-derived from the raw frames.
        stall_ok, stall_value = stall_exclusion(window, frames)
        check(prefix + ".stallExclusion", stall_ok, stall_value)
        visual = exact_int(scene.get("rendererCount"), 1) and exact_int(scene.get("renderersVisible"), 1) and finite_number(window.get("drawCallsAvg"), 100) and finite_number(window.get("trianglesAvg"), 100000)
        check(prefix + ".visualWorkload", visual,
              f"renderers={scene.get('rendererCount')} visible={scene.get('renderersVisible')} draw={window.get('drawCallsAvg')} tris={window.get('trianglesAvg')}")
        gpu_ok = valid_gpu_stage(gpu)
        check(prefix + ".gpu", gpu_ok,
              {key: gpu.get(key) for key in ("pass", "gpuSampledFrames", "gpuFrameAvgMs", "gpuFrameMaxMs", "gpuMeasurementSource")})

    soak_root = Path(args.soak_root).resolve() if args.soak_root else root
    default_soak_commit = args.soak_commit or args.commit
    soak_specs = [split_dir_commit(d, default_soak_commit) for d in args.soak_dirs.split(",") if d]
    report_specs = [split_dir_commit(d, default_soak_commit) for d in args.report_dirs.split(",") if d]
    soak_commits = sorted({commit for _, commit in soak_specs + report_specs})
    check("soak.commit", all(HEX_COMMIT.fullmatch(c or "") is not None and len(c) == 40 for c in soak_commits), soak_commits)
    soak_dirs = [name for name, _ in soak_specs]
    soak_dir_commit = dict(soak_specs)
    seeds = [int(x) for x in args.seeds.split(",") if x]
    check("soak.stabilitySeeds", len(soak_dirs) == len(seeds) == 2 and len(set(seeds)) == 2, {"dirs": soak_dirs, "seeds": seeds})
    soak_required = ["command.json", "preflight.json", "host-monitor.json", "player.log", "exit.txt", "bot-summary.json", "qa-report.json", "events.txt"]
    soak_binaries = {}
    for soak_name, seed in zip(soak_dirs, seeds):
        soak = soak_root / soak_name
        prefix = "soak." + soak_name
        soak_commit = soak_dir_commit[soak_name]
        if (soak / "attempt.json").is_file():
            # wp064_run.py layout (attempt provenance, preflight + host monitor, command.json)
            binary_hash = dev_hash if soak_commit == args.commit else external_build(soak_root, soak_name, soak_commit, check, prefix)[0]
            soak_command, soak_provenance = validate_attempt(soak, "soak", soak_commit, binary_hash, soak_required, check)
            soak_id = soak_provenance.get("attemptId")
            check(prefix + ".uniqueAttempt", isinstance(soak_id, str) and bool(soak_id) and soak_id not in attempt_ids, soak_id)
            attempt_ids.add(soak_id)
            soak_preflight = load_json(soak / "preflight.json", check)
            soak_monitor = load_json(soak / "host-monitor.json", check)
            check(prefix + ".host", soak_preflight.get("pass") is True and soak_monitor.get("pass") is True and soak_monitor.get("timedOut") is False, "preflight+monitor")
            try:
                command_ok = (int(flag_value(soak_command, "-ddQaBotRounds")) == args.rounds and int(flag_value(soak_command, "-ddQaBotSeed")) == seed
                              and "-ddQaBotStability" in soak_command and "-ddQaBotInvulnerable" not in soak_command)
            except (TypeError, ValueError):
                command_ok = False
            check(prefix + ".command", command_ok, soak_command)
        else:
            # soak.sh layout (S-10 WP-064B chain): per-run dev build, quiet-machine wait log, user-data restore record
            binary_hash = check_external_soak(soak, soak_name, seed, args.rounds, soak_commit, soak_root, check, prefix)
            check(prefix + ".uniqueAttempt", str(soak) not in attempt_ids, str(soak))
            attempt_ids.add(str(soak))
        soak_binaries[soak_name] = binary_hash
        player_logs.append(soak / "player.log")
        soak_summary = load_json(soak / "bot-summary.json", check)
        # the stability contract (rounds, mode, memory, Player-log errors) lives in one place: tools/qa/soak_gate.py --stability
        gate = subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "soak_gate.py"), str(soak / "bot-summary.json"),
                               "--rounds", str(args.rounds), "--seed", str(seed), "--stability", "--player-log", str(soak / "player.log")],
                              capture_output=True, text=True)
        check(prefix + ".stabilityGate", gate.returncode == 0, gate.stdout.strip().splitlines()[:12])
        check(prefix + ".modeSeparated", soak_summary.get("runMode") == "stability" and soak_summary.get("pass") is False and soak_summary.get("stabilityPass") is True,
              {"runMode": soak_summary.get("runMode"), "pass": soak_summary.get("pass"), "stabilityPass": soak_summary.get("stabilityPass")})

    # real-bot runs: presence, provenance and mode are required, the reached-round distribution is not gated (DEC-091)
    present = []
    for report_name, report_commit in report_specs:
        run = soak_root / report_name
        prefix = "report." + report_name
        summary = load_json(run / "bot-summary.json", check)
        if not (run / "attempt.json").is_file():
            external_build(soak_root, report_name, report_commit, check, prefix)
        real = summary.get("runMode") == "normal" and summary.get("seed") in seeds and summary.get("roundsRequested") == args.rounds
        check(prefix + ".realBot", real, {key: summary.get(key) for key in ("runMode", "seed", "roundsRequested", "roundsCompleted", "deathRound")})
        if real:
            present.append(str(run / "bot-summary.json"))
    real_bot = subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "soak_gate.py"), "--report", *present],
                              capture_output=True, text=True) if present else None
    real_bot_lines = real_bot.stdout.strip().splitlines() if real_bot and real_bot.stdout else []
    check("soak.realBotReport", len(present) >= 5 and real_bot is not None and real_bot.returncode == 0,
          real_bot_lines[0] if real_bot_lines else f"{len(present)} summaries")

    build_logs = [dev_dir / "build.log", release_dir / "build.log"]
    extra_release_logs = [path for path in sorted(release_dir.rglob("*.log")) if path not in build_logs]
    hits = log_failures(build_logs + extra_release_logs + player_logs)   # v2 (finding 4): each of these is required
    check("logs.buildAndPlayer", not hits, hits[:50])

    passed = all(item["pass"] for item in checks)
    report = {
        "schema": "dd-wp064-closure-v4",
        "pass": passed,
        "executionCommit": args.commit,
        "devBinarySha256": dev_hash,
        "releaseBinarySha256": release_hash,
        "soakCommits": {name: commit for name, commit in soak_specs + report_specs},
        "soakBinarySha256": soak_binaries,
        "seeds": seeds,
        "rounds": args.rounds,
        "realBotReport": real_bot_lines,
        "checks": checks,
    }
    (root / "wp064-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    lines = [f"WP-064 {'PASS' if passed else 'FAIL'} commit={args.commit} soakCommits={','.join(soak_commits)}"]
    lines += [f"{'PASS' if item['pass'] else 'FAIL'} {item['name']} = {item['value']}" for item in checks]
    (root / "wp064-report.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
