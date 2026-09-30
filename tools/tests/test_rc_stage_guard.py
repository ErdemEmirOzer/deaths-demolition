"""Y2 (C-00 30.09): tools/qa/rc.sh guards the user data PER STAGE.

A fake repository in a tempdir carries the real rc.sh, dd-env.sh, window-modes.sh, economy-acceptance.sh, regression.sh,
wp064_run.py and player-run.sh where a test needs them, and fakes for everything that would start Unity, a Player, the
real guard or the machine slot. Every fake appends its argv to one call log; the tests read the ORDER from it.
No Unity, no Player, no ~/Library data, no real slot directory (HOME points into the tempdir).
"""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

REPO = Path(__file__).resolve().parents[2]

FAKE_PY = r'''
import json, os, sys, time
from pathlib import Path

def log(tool, argv):
    with open(os.environ["CALL_LOG"], "a", encoding="utf-8") as handle:
        handle.write(json.dumps([tool] + argv) + "\n")

def flag(argv, name):
    return argv[argv.index(name) + 1] if name in argv else None

def main(tool, argv):
    log(tool, argv)
    if os.environ.get("ENV_DUMP"):   # M-10: what each child sees (DD_X must be gone, HOME kept)
        with open(os.environ["ENV_DUMP"], "a", encoding="utf-8") as handle:
            handle.write(json.dumps([tool, "DD_X" in os.environ, os.environ.get("HOME")]) + "\n")
    rules = json.loads(os.environ.get("FAKE_RULES", "{}"))
    if tool == "userdata-guard.sh":
        ok = (len(argv) == 4 and argv[0] in ("snapshot", "restore") and argv[2] == "--scope"
              and argv[3] in ("all", "editor", "player"))
        if not ok:
            print("usage", file=sys.stderr); return 64
        cmd, ug, scope = argv[0], Path(argv[1]), argv[3]
        stage = ug.parent.name
        ug.mkdir(parents=True, exist_ok=True)
        code = int(rules.get(cmd + ":" + stage, 0))
        if cmd == "restore" and code == 0 and not rules.get("unverified:" + stage):
            print(f"2026-09-30T00:00:00Z userdata-guard restore scope={scope} touched=0 verified=1")
        return code
    sleep_for = rules.get("sleep:" + tool + ":" + (argv[0] if argv else ""))
    if sleep_for:
        Path(os.environ["CALL_LOG"] + ".sleeper").write_text(str(os.getpid()))
        time.sleep(float(sleep_for))
    if tool == "regression.sh":
        binary = Path(argv[0]) / "build-dev/FixtureGame.app/Contents/MacOS/Fixture Product"   # = FIXTURE_APP / FIXTURE_PRODUCT
        binary.parent.mkdir(parents=True, exist_ok=True)
        binary.write_text("#!/bin/sh\nexit 0\n"); binary.chmod(0o755)
    if tool == "player-run.sh":
        if flag(argv, "-ddEconomyProbe"):
            out = Path(flag(argv, "-ddEconomyProbe"))
            for index in range(1, 12):
                (out / f"{index:02d}-stage.json").write_text(json.dumps({"pass": True}))
        if flag(argv, "-ddQaWindowModes"):
            (Path(flag(argv, "-ddQaWindowModes")) / "qa-report.json").write_text(json.dumps({"pass": True}))
    return int(rules.get("exit:" + tool, 0))
'''

SHELL_FAKES = ("tools/test.sh", "tools/build.sh", "tools/slot.sh", "tools/player-run.sh", "tools/userdata-guard.sh",
               "tools/qa/regression.sh")
PY_FAKES = ("tools/qa/wp064_run.py", "tools/qa/library_provenance.py", "tools/qa/wp064_gate.py", "tools/qa/log_scan.py",
            "tools/qa/rc_aggregate.py", "tools/qa/aggregate.py")
PLAYER_STAGES = ("m3-regression", "soak-stability-61", "soak-stability-26", "real-2", "real-3", "real-4", "real-5",
                 "economy-acceptance", "window-modes")


# C-00 30.09 disk gate: fixtures must not depend on the real machine's free disk / swap — df and sysctl report plenty.
def plenty_bin(root):
    d = Path(root) / "plenty-bin"
    d.mkdir(exist_ok=True)
    (d / "df").write_text("#!/bin/sh\necho 'Filesystem 1024-blocks Used Available'\necho '/dev/fixture 999999999 1 999999999'\n")
    (d / "sysctl").write_text("#!/bin/sh\n[ \"$2\" = vm.swapusage ] && { echo 'total = 1.00M  used = 0.00M  free = 1.00M'; exit 0; }\nexec /usr/sbin/sysctl \"$@\"\n")
    for f in ("df", "sysctl"):
        (d / f).chmod(0o755)
    return str(d)


FIXTURE_APP = "FixtureGame.app"
FIXTURE_PRODUCT = "Fixture Product"
REAL_PLAYER_RE = "Contents/MacOS/(Deaths Demolition|DeathsDemolition)"


class FakeRepo:
    """A throw-away repository layout: real scripts under test, fakes for everything else."""

    def __init__(self, test, real=(), fake_over=()):
        self.dir = Path(tempfile.mkdtemp(prefix="rc-guard-"))
        test.addCleanup(shutil.rmtree, self.dir, True)
        self.root = self.dir / "repo"
        self.fakes = self.dir / "fakes"
        self.fakes.mkdir()
        (self.fakes / "fake.py").write_text(FAKE_PY)
        self.log = self.dir / "calls.jsonl"
        self.log.touch()
        for rel in ("tools/dd-env.sh",) + tuple(real):
            target = self.root / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(REPO / rel, target)
        # C-00 30.09 (S-03's real guard saw a fixture "Player"): the fixture's app/product names must never match the real
        # Player pattern `Contents/MacOS/(Deaths Demolition|DeathsDemolition)` that guards and player-run pgrep for.
        with open(self.root / "tools/dd-env.sh", "a") as env:
            env.write(f'\nDD_APP_NAME="{FIXTURE_APP}"; DD_PRODUCT="{FIXTURE_PRODUCT}"\n')
        for rel in SHELL_FAKES:
            if rel in real:
                continue
            self.write(rel, f'#!/bin/sh\nexec python3 -c \'import sys; sys.path.insert(0, "{self.fakes}"); import fake; '
                            f'sys.exit(fake.main("{Path(rel).name}", sys.argv[1:]))\' "$@"\n')
        for rel in PY_FAKES:
            if rel in real:
                continue
            self.write(rel, f'import sys\nsys.path.insert(0, "{self.fakes}")\nimport fake\n'
                            f'sys.exit(fake.main("{Path(rel).stem}", sys.argv[1:]))\n')
        for rel, text in fake_over:
            self.write(rel, text)
        # Codex-audit M-10: rc.sh scrubs what the C# of $REPO reads (git grep) — the fake repo is a git repo with one .cs
        cs = self.root / "prototype/DeathsDemolition_PlayableConcept/Assets/Probe.cs"
        cs.parent.mkdir(parents=True, exist_ok=True)
        cs.write_text('class P { string a = System.Environment.GetEnvironmentVariable("DD_X");\n'
                      '  string b = System.Environment.GetEnvironmentVariable("HOME"); }\n')
        subprocess.run(["git", "init", "-q"], cwd=self.root, check=True, capture_output=True)
        subprocess.run(["git", "add", "."], cwd=self.root, check=True, capture_output=True)
        unity = self.dir / "Unity"
        unity.write_text("#!/bin/sh\nexit 0\n")
        unity.chmod(0o755)
        (self.dir / "home").mkdir()
        self.env = {"PATH": plenty_bin(self.dir) + ":" + os.environ["PATH"], "HOME": str(self.dir / "home"), "TMPDIR": str(self.dir),
                    "CALL_LOG": str(self.log), "DD_UNITY": str(unity), "DD_SLOT_BASE": str(self.dir / "slots"),
                    "PYTHONDONTWRITEBYTECODE": "1", "GIT_CEILING_DIRECTORIES": str(self.dir)}

    def write(self, rel, text):
        path = self.root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        path.chmod(0o755)

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines() if line.strip()]


class RcStageGuardTests(unittest.TestCase):
    REAL = ("tools/qa/rc.sh", "tools/qa/window-modes.sh", "tools/qa/economy-acceptance.sh")

    def rc(self, *args, rules=None, repo=None, env_extra=None):
        repo = repo or FakeRepo(self, self.REAL)
        out = repo.dir / "out"
        env = dict(repo.env, FAKE_RULES=json.dumps(rules or {}), **(env_extra or {}))
        result = subprocess.run(["/bin/zsh", str(repo.root / "tools/qa/rc.sh"), str(out), *args],
                                capture_output=True, text=True, env=env, timeout=120)
        return repo, out, result

    @staticmethod
    def guard_calls(calls):
        return [(i, c) for i, c in enumerate(calls) if c[0] == "userdata-guard.sh"]

    def window(self, calls, out, stage, scope):
        """(snapshot index, restore index) of the stage's guard; asserts nothing else guarded in between."""
        ug = str(out / "stages" / stage / "ug")
        snaps = [i for i, c in self.guard_calls(calls) if c[1:] == ["snapshot", ug, "--scope", scope]]
        restores = [i for i, c in self.guard_calls(calls) if c[1:] == ["restore", ug, "--scope", scope]]
        self.assertEqual((1, 1), (len(snaps), len(restores)), f"{stage}: {snaps} {restores}")
        inner = [i for i, _ in self.guard_calls(calls) if snaps[0] < i < restores[0]]
        self.assertEqual([], inner, f"{stage}: another guard call inside its window")
        return snaps[0], restores[0]

    def assert_inside(self, calls, out, stage, scope, predicate):
        snap, restore = self.window(calls, out, stage, scope)
        work = [i for i, c in enumerate(calls) if predicate(c)]
        self.assertTrue(work, f"{stage}: work not called")
        self.assertTrue(all(snap < i < restore for i in work), f"{stage}: work {work} outside {snap}..{restore}")

    def test_Asamalar_HerAsamaKendiGuardPenceresi_SnapshotIsRestoreSirali(self):
        repo, out, result = self.rc()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        calls = repo.calls()
        self.assert_inside(calls, out, "full-editmode", "editor", lambda c: c[:2] == ["test.sh", "editmode"])
        self.assert_inside(calls, out, "full-playmode", "editor", lambda c: c[:2] == ["test.sh", "playmode"])
        self.assert_inside(calls, out, "m3-regression", "player", lambda c: c[0] == "regression.sh")
        for seed in (61, 26):
            self.assert_inside(calls, out, f"soak-stability-{seed}", "player",
                               lambda c, s=seed: c[0] == "wp064_run" and str(out / f"m3/soak-{s}") in c)
        for i in (2, 3, 4, 5):
            self.assert_inside(calls, out, f"real-{i}", "player",
                               lambda c, n=i: c[0] == "wp064_run" and str(out / f"m3/real-{n}") in c)
        self.assert_inside(calls, out, "economy-acceptance", "player",
                           lambda c: c[0] == "player-run.sh" and "-ddEconomyProbe" in c)
        self.assert_inside(calls, out, "window-modes", "player",
                           lambda c: c[0] == "player-run.sh" and "-ddQaWindowModes" in c)
        # every guard call belongs to exactly the windows above: no all-scope RC snapshot, release-smoke not double-wrapped
        guarded = {"full-editmode", "full-playmode", *PLAYER_STAGES}
        self.assertEqual(2 * len(guarded), len(self.guard_calls(calls)))
        self.assertFalse(any(c[-1] == "all" for _, c in self.guard_calls(calls)))
        build = [i for i, c in enumerate(calls) if c[0] == "build.sh"]
        self.assertEqual(1, len(build))
        windows = [self.window(calls, out, s, "editor" if s.startswith("full-") else "player") for s in guarded]
        self.assertFalse(any(a < build[0] < b for a, b in windows), "release-smoke must not sit inside a guard window")
        self.assertFalse((out / "stages/release-smoke/ug").exists())

    def test_PlayerAsamalari_PlayerRunIleVeSandboxBayraklariyla_Baslar(self):
        repo, out, result = self.rc()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        runs = [c for c in repo.calls() if c[0] == "player-run.sh"]
        self.assertEqual(2, len(runs), runs)
        app_bin = str(out / f"m3/build-dev/{FIXTURE_APP}/Contents/MacOS/{FIXTURE_PRODUCT}")
        for name, probe in (("economy-acceptance", "-ddEconomyProbe"), ("window-modes", "-ddQaWindowModes")):
            call = next(c for c in runs if probe in c)
            self.assertEqual(app_bin, call[1])
            self.assertEqual(str(out / name / "ui-data"), call[call.index("-ddUiDataRoot") + 1])
            self.assertEqual(str(out / name / "telemetry"), call[call.index("-ddTelemetryDir") + 1])

    def test_GuardRestoreHatasi_30_31_75_32_RcFailVeC00Mesaji(self):
        for code in (30, 31, 75, 32):
            with self.subTest(code=code):
                repo, out, result = self.rc(rules={f"restore:window-modes": code})
                self.assertNotEqual(0, result.returncode)
                self.assertIn(f"guard restore {code} in stage window-modes — tell C-00", result.stderr + result.stdout)
                self.assertEqual(str(code), (out / "stages/window-modes/exit.txt").read_text().strip())
                agg = [c for c in repo.calls() if c[0] == "rc_aggregate"][-1]
                self.assertEqual("window-modes", agg[agg.index("--failed-stage") + 1])

    def test_GuardRestoreHatasi_TaniDevamindaBile_RcDurur(self):
        repo, out, result = self.rc("--diagnostic-continue", rules={"restore:full-editmode": 31})
        self.assertEqual(31, result.returncode, result.stdout + result.stderr)
        self.assertIn("guard restore 31 in stage full-editmode — tell C-00", result.stderr)
        self.assertFalse(any(c[:2] == ["test.sh", "playmode"] for c in repo.calls()), "RC must stop after a failed restore")

    def test_DogrulanmamisRestore_Cikis0AmaVerifiedYok_31Sayilir(self):
        repo, out, result = self.rc(rules={"unverified:soak-stability-26": True})
        self.assertEqual(31, result.returncode, result.stdout + result.stderr)
        self.assertIn("guard restore 31 in stage soak-stability-26 — tell C-00", result.stderr)

    def test_RaporKosusuRestoreHatasi_RcFail_FailedStageRealN(self):
        repo, out, result = self.rc(rules={"restore:real-4": 75})
        self.assertEqual(75, result.returncode, result.stdout + result.stderr)
        self.assertIn("guard restore 75 in stage real-4 — tell C-00", result.stderr)
        agg = [c for c in repo.calls() if c[0] == "rc_aggregate"][-1]
        self.assertEqual("real-4", agg[agg.index("--failed-stage") + 1])
        self.assertFalse(any(c[0] == "wp064_run" and str(out / "m3/real-5") in c for c in repo.calls()))

    def test_Snapshot30_CanliPlayer_AsamaKosmaz_Bloklanir(self):
        repo, out, result = self.rc(rules={"snapshot:soak-stability-61": 30})
        self.assertEqual(30, result.returncode, result.stdout + result.stderr)
        calls = repo.calls()
        self.assertFalse(any(c[0] == "wp064_run" and str(out / "m3/soak-61") in c for c in calls), "stage ran unguarded")
        ug = str(out / "stages/soak-stability-61/ug")
        self.assertFalse(any(c[:3] == ["userdata-guard.sh", "restore", ug] for c in calls))
        self.assertEqual("30", (out / "stages/soak-stability-61/exit.txt").read_text().strip())
        self.assertIn("BLOCKED soak-stability-61: guard snapshot 30", (out / "timeline.txt").read_text())

    def test_FiksturOyuncusu_GercekPlayerDesenineUymaz_BaskaOturumunGuardiniEngellemez(self):
        # C-00 30.09: S-03's real guard snapshot returned 30 on this fixture's fake Player. While a fixture Player stage is
        # alive, the real pattern (guard / player-run pgrep) must find nothing from this fixture.
        repo = FakeRepo(self, self.REAL)
        out = repo.dir / "out"
        dev_bin = str(out / f"m3/build-dev/{FIXTURE_APP}/Contents/MacOS/{FIXTURE_PRODUCT}")
        env = dict(repo.env, FAKE_RULES=json.dumps({"sleep:player-run.sh:" + dev_bin: 30}))
        proc = subprocess.Popen(["/bin/zsh", str(repo.root / "tools/qa/rc.sh"), str(out)], env=env,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        sleeper = Path(str(repo.log) + ".sleeper")
        deadline = time.monotonic() + 60
        while not sleeper.exists() and time.monotonic() < deadline:
            time.sleep(0.1)
        try:
            self.assertTrue(sleeper.exists(), "fake Player stage never started")
            ours = subprocess.run(["pgrep", "-fl", FIXTURE_APP], capture_output=True, text=True).stdout
            self.assertIn(sleeper.read_text().strip(), ours, "sanity: the fixture Player is visible under its own name")
            real = subprocess.run(["pgrep", "-fl", REAL_PLAYER_RE], capture_output=True, text=True).stdout
            self.assertEqual([l for l in real.splitlines() if str(repo.dir) in l], [], real)
        finally:
            proc.send_signal(signal.SIGTERM)
            proc.communicate(timeout=90)

    def test_TermAsamaOrtasinda_OnceRestoreSonraSifirDisiCikis(self):
        repo = FakeRepo(self, self.REAL)
        out = repo.dir / "out"
        env = dict(repo.env, FAKE_RULES=json.dumps({"sleep:test.sh:playmode": 60}))
        proc = subprocess.Popen(["/bin/zsh", str(repo.root / "tools/qa/rc.sh"), str(out)], env=env,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        sleeper = Path(str(repo.log) + ".sleeper")
        deadline = time.monotonic() + 30
        while not sleeper.exists() and time.monotonic() < deadline:
            time.sleep(0.1)
        self.assertTrue(sleeper.exists(), "fake PlayMode stage never started")
        proc.send_signal(signal.SIGTERM)
        stdout, stderr = proc.communicate(timeout=60)
        self.assertEqual(143, proc.returncode, stdout + stderr)
        calls = repo.calls()
        ug = str(out / "stages/full-playmode/ug")
        restore = [i for i, c in enumerate(calls) if c[1:] == ["restore", ug, "--scope", "editor"]]
        self.assertEqual(1, len(restore), "a signal must still restore the open snapshot")
        with self.assertRaises(ProcessLookupError):
            os.kill(int(sleeper.read_text()), 0)   # the stage's process tree was stopped before the restore
        self.assertFalse(any(c[0] == "build.sh" for c in calls), "no stage after the signal")
        agg = [c for c in calls if c[0] == "rc_aggregate"][-1]
        self.assertEqual("full-playmode", agg[agg.index("--failed-stage") + 1])
        self.assertEqual("143", (out / "stages/full-playmode/exit.txt").read_text().strip())

    def test_M10_DD_XIleBaslatilir_AsamalarGormez_HomeKalir_RaporaVeTimelineaYazilir(self):
        repo = FakeRepo(self, self.REAL)
        dump = repo.dir / "env-dump.jsonl"
        repo, out, result = self.rc(repo=repo, env_extra={"DD_X": "1", "ENV_DUMP": str(dump)})
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        seen = [json.loads(line) for line in dump.read_text().splitlines()]
        self.assertTrue(any(tool == "test.sh" for tool, _, _ in seen) and any(tool == "build.sh" for tool, _, _ in seen))
        self.assertEqual([], [tool for tool, has_x, _ in seen if has_x], "a stage still saw DD_X")
        self.assertEqual({str(repo.dir / "home")}, {home for _, _, home in seen})
        agg = [c for c in repo.calls() if c[0] == "rc_aggregate"][-1]
        self.assertEqual("DD_X", agg[agg.index("--env-scrubbed") + 1])
        self.assertIn(f"env-scrub: cleared 1 (DD_X) from {repo.root.resolve()}", (out / "timeline.txt").read_text())
        self.assertIn("envScrubbed=DD_X", (out / "environment.txt").read_text())

    def test_M10_GitGrepHatasi_Cikis3_HicbirAsamaKosmaz_Preflight(self):
        repo = FakeRepo(self, self.REAL)
        shutil.rmtree(repo.root / ".git")
        repo, out, result = self.rc(repo=repo, env_extra={"DD_X": "1"})
        self.assertEqual(3, result.returncode, result.stdout + result.stderr)
        self.assertIn("env-scrub: git grep failed", result.stderr)
        calls = repo.calls()
        self.assertEqual(["rc_aggregate"], [c[0] for c in calls], "nothing but the final aggregate may run")
        self.assertEqual("preflight", calls[-1][calls[-1].index("--failed-stage") + 1])

    def test_PerfRuns2_KullanimHatasi64_HicbirSeyKosmaz(self):
        for value in ("2", "0", "x"):
            with self.subTest(value=value):
                repo, out, result = self.rc("--perf-runs", value)
                self.assertEqual(64, result.returncode, result.stderr)
                self.assertIn("--perf-runs >= 3", result.stderr)
                self.assertEqual([], repo.calls())
                self.assertFalse(out.exists())

    def test_GuardYolu_TekKanonikDegisken_TestDaliYok(self):
        text = (REPO / "tools/qa/rc.sh").read_text()
        self.assertEqual(1, text.count('DD_GUARD="$REPO/tools/userdata-guard.sh"'))
        body = text.split('DD_GUARD="$REPO/tools/userdata-guard.sh"', 1)[1]
        self.assertNotIn("userdata-guard.sh", body)   # every guard call after it goes through $DD_GUARD


class PlayerLaunchTests(unittest.TestCase):
    def test_Wp064RunPerf_PlayerRunIle_UiDataVeTelemetryBayraklari(self):
        repo = FakeRepo(self, ("tools/qa/wp064_run.py",))
        binary = repo.dir / "Player"
        binary.write_text("#!/bin/sh\nexit 0\n")
        binary.chmod(0o755)
        spec = importlib.util.spec_from_file_location("wp064_run_y2", repo.root / "tools/qa/wp064_run.py")
        run = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(run)
        clean = {"utc": "x", "load1": 0.0, "forbidden": [], "heavy": [], "activeOver5Pct": []}
        for extra, ui_root in (([], None), (["--extra-arg=-ddUiDataRoot", "--extra-arg=/caller/ui"], "/caller/ui")):
            out = repo.dir / f"perf-{len(extra)}"
            argv = ["wp064_run.py", "perf", "--bin", str(binary), "--out", str(out), "--commit", "a" * 40,
                    "--preflight-samples", "1", *extra]
            with self.subTest(extra=extra), mock.patch.dict(os.environ, {"CALL_LOG": str(repo.log), "FAKE_RULES": "{}"}), \
                    mock.patch.object(run, "snapshot", return_value=clean), mock.patch.object(sys, "argv", argv):
                run.main()
                launched = [c for c in repo.calls() if c[0] == "player-run.sh"][-1]
                command = json.loads((out / "command.json").read_text())
                self.assertEqual(command, launched[1:], "command.json is the Player argv player-run.sh received")
                self.assertEqual(str(binary.resolve()), command[0])
                self.assertEqual(1, command.count("-ddUiDataRoot"))
                self.assertEqual(ui_root or str(out.resolve() / "ui-data"), command[command.index("-ddUiDataRoot") + 1])
                self.assertEqual(str(out.resolve() / "telemetry"), command[command.index("-ddTelemetryDir") + 1])
                attempt = json.loads((out / "attempt.json").read_text())
                self.assertEqual(str((repo.root / "tools/player-run.sh").resolve()), attempt["launcher"])

    def test_Wp064RunMonitor_PlayerRunAltindakiPlayer_YasakSayilmaz(self):
        spec = importlib.util.spec_from_file_location("wp064_run_y2b", REPO / "tools/qa/wp064_run.py")
        run = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(run)
        launcher = {"pid": 100, "ppid": 1, "cpu": 0.0, "args": "/bin/zsh tools/player-run.sh /b/DeathsDemolition.app/Contents/MacOS/x"}
        player = {"pid": 101, "ppid": 100, "cpu": 90.0, "args": "/b/DeathsDemolition.app/Contents/MacOS/x -ddQaBot"}
        foreign = {"pid": 200, "ppid": 1, "cpu": 1.0, "args": "/c/DeathsDemolition.app/Contents/MacOS/x"}
        rows = {r["pid"]: r for r in (launcher, player, foreign)}
        self.assertFalse(run.forbidden(player, 100, rows))
        self.assertTrue(run.forbidden(foreign, 100, rows))
        self.assertTrue(run.forbidden(player, 100), "without the own-tree view (preflight) nothing changes")

    def test_PlayerRun_TermGelince_PlayeriDaDurdurur(self):
        repo = FakeRepo(self, ("tools/player-run.sh",))
        pidfile = repo.dir / "player.pid"
        binary = repo.dir / "Player"
        binary.write_text(f"#!/bin/sh\necho $$ > '{pidfile}'\nexec sleep 60\n")
        binary.chmod(0o755)
        proc = subprocess.Popen(["/bin/zsh", str(repo.root / "tools/player-run.sh"), str(binary), "-ddTelemetryDir", "x"],
                                env=dict(repo.env, FAKE_RULES="{}"), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        deadline = time.monotonic() + 20
        while not pidfile.exists() and time.monotonic() < deadline:
            time.sleep(0.1)
        self.assertTrue(pidfile.exists())
        time.sleep(0.3)
        proc.send_signal(signal.SIGTERM)
        proc.communicate(timeout=20)
        self.assertNotEqual(0, proc.returncode)
        time.sleep(0.2)
        with self.assertRaises(ProcessLookupError):
            os.kill(int(pidfile.read_text()), 0)

    def test_RegressionOkunabilirlik_PlayerRunIle_SandboxBayraklari(self):
        repo = FakeRepo(self, ("tools/qa/regression.sh",))
        out = repo.dir / "m3"
        binary = out / f"build-dev/{FIXTURE_APP}/Contents/MacOS/{FIXTURE_PRODUCT}"
        binary.parent.mkdir(parents=True)
        binary.write_text("#!/bin/sh\nexit 0\n")
        binary.chmod(0o755)
        result = subprocess.run(["/bin/zsh", str(repo.root / "tools/qa/regression.sh"), str(out), "--skip-build",
                                 "--steps", "readability"], capture_output=True, text=True, timeout=60,
                                env=dict(repo.env, FAKE_RULES="{}", DD_PLAYER_TIMEOUT="5"))
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        call = next(c for c in repo.calls() if c[0] == "player-run.sh")
        self.assertEqual(str(binary), call[1])
        self.assertIn("-ddQaReadability", call)
        self.assertEqual(str(out / "readability/ui-data"), call[call.index("-ddUiDataRoot") + 1])
        self.assertEqual(str(out / "readability/telemetry"), call[call.index("-ddTelemetryDir") + 1])


if __name__ == "__main__":
    unittest.main()
