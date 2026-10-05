#!/usr/bin/env python3
"""WP-079G G4 D2 / D3 / D4 (design v3.1 §3, C-00 01.10 FINAL-blocking):
  D2 CallerRestoreFirstTests — wp078_perf / wp078_bot: on an error or a signal the child gets TERM (≤ grace, then KILL), the
     restore waits for no game Player to be left (still up → NOT restored, marker, 65 — never a wait code), runs with INT/TERM/HUP deferred
     (the guard child ignores them), the signal is raised only afterwards, the quiet window is released after the restore;
     an unresolved marker stops the next wp078_perf run (65). wp064_run: stop_player ends a TERM-ignoring tree.
  D3 SmokeK2Tests — smoke.sh takes 'player' then 'smoke' before the user-data copy; a live Player → 30, nothing copied;
     a release shell holding 'player' + a standalone smoke do not deadlock (T33 ABBA).
  D4 CanonicalGuardTests — wp078_* and main-ci run only the canonical driver guard; DD_GUARD_ROOT set or the canonical
     guard missing → 64 with nothing taken.
Fixture only: fake guards, fake children, the real slot.sh on a DD_SLOT_BASE in a tempdir, HOME in a tempdir, a fake
`defaults` first on PATH for smoke.sh, fixture "Players" whose names never match the real Player pattern. No Unity, no real
Player, no real user data, no real slot directory.
usage: python3 tools/tests/test_guard_d234.py"""
import argparse, os, shutil, signal, subprocess, sys, tempfile, textwrap, time, unittest
from pathlib import Path
from unittest import mock

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import dd_paths  # noqa: E402  (WP-089: the KADRO root; fixtures follow it on move day)
sys.path.insert(0, str(TOOLS / "qa"))
sys.path.insert(0, str(TOOLS / "tests"))
import guard_receipt as gr  # noqa: E402
import wp064_run  # noqa: E402
import wp078_bot  # noqa: E402
import wp078_perf  # noqa: E402
from test_ci_signal_traps import CiFixture, alive  # noqa: E402  (fixture only: no tests are imported)

CANON_TAIL = dd_paths.KADRO_REL + "/context/projects/deaths-demolition/wp-079/driver/tools/userdata-guard.sh"
FIXTURE_PLAYER = "dd-fixture-player-k2-7f3c"   # never matches Contents/MacOS/(Deaths Demolition|DeathsDemolition)


def done(code, out=""):
    return subprocess.CompletedProcess([], code, out, "")


def perf_args():
    return argparse.Namespace(dry_run=False, player_wait=0, commit="a" * 40, load_max=6.0)


def write_exec(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)
    path.chmod(0o755)


def wait_for(predicate, seconds=20.0):
    end = time.time() + seconds
    while time.time() < end:
        if predicate():
            return True
        time.sleep(0.1)
    return False


# --- D2 ---------------------------------------------------------------------------------------------------------------
class CallerRestoreFirstTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-g4-d2-")
        self.root = Path(self.tmp.name).resolve()

    def tearDown(self):
        self.tmp.cleanup()

    def window(self, launch, live=(), close=None):
        closed = []
        close = close or (lambda ug, *a, **k: closed.append(Path(ug)) or {"code": 0, "why": "touched=0"})
        # no Player before the window (the pre-launch wait); `live` = what the restore half sees after it
        with mock.patch.object(wp078_perf, "player_up", return_value=False), \
                mock.patch.object(wp078_perf, "players", return_value=list(live)), \
                mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(gr, "close", side_effect=close), \
                mock.patch.object(gr.time, "sleep"), \
                mock.patch.object(wp078_perf, "launch", side_effect=launch):
            try:
                result = wp078_perf.run_window(perf_args(), self.root, Path("/b/P"), 60, None, lambda s: None, "yuksek", "tp", 1)
            except BaseException as error:   # noqa: B902 — the test checks what escapes
                return error, closed
        return result, closed

    def test_Wp078_HucreIstisna_FinallyRestoreCagrilir(self):
        outcome, closed = self.window(RuntimeError("wp064_run crashed"))
        self.assertIsInstance(outcome, RuntimeError)
        self.assertEqual(1, len(closed))

    def test_Wp078_PlayerYasiyor_RestoreCagrilmazIsaret65(self):
        for launch in (lambda cmd: done(0), KeyboardInterrupt):
            with self.subTest(launch=launch):
                shutil.rmtree(self.root / "ug", ignore_errors=True)
                outcome, closed = self.window(launch, live=["4242 /x/MacOS/Deaths Demolition - Playable Concept"])
                self.assertEqual(65, outcome)   # the marker wins over the in-flight Ctrl-C too
                self.assertEqual([], closed)
                marker = self.root / "ug/ug-perf-yuksek-tp-1" / gr.MARKER
                self.assertTrue(marker.is_file())
                text = marker.read_text()
                self.assertIn("4242", text)
                self.assertIn("C-00", text)
                self.assertIn("no inline inventory", text)

    def test_Wp078_PlayerKisaSuredeKapanir_RestoreYapilir(self):
        seq = iter([["1 P"], ["1 P"], []])
        closed = []
        with mock.patch.object(gr, "close", side_effect=lambda ug, *a, **k: closed.append(ug) or {"code": 0, "why": "ok"}):
            receipt, signals = gr.finish(self.root / "ug", "player", lambda: next(seq), sleep=lambda s: None)
        self.assertEqual(0, receipt["code"])
        self.assertEqual(1, len(closed))
        self.assertFalse((self.root / "ug" / gr.MARKER).exists())

    def test_Wp078_SnapshotSifirDegil_RestoreCagrilmaz(self):
        for snapshot in (30, 75, 31):
            closed = []
            with mock.patch.object(wp078_perf, "players", return_value=[]), \
                    mock.patch.object(gr, "run_guard", return_value=done(snapshot)), \
                    mock.patch.object(gr, "close", side_effect=lambda ug, *a, **k: closed.append(ug)), \
                    mock.patch.object(wp078_perf, "launch") as launch:
                wp078_perf.run_window(perf_args(), self.root, Path("/b/P"), 60, None, lambda s: None, "yuksek", "tp", 1)
            launch.assert_not_called()
            self.assertEqual([], closed)

    def test_Wp078_RestoreSirasindaSinyal_ErtelenirSonraYukseltilir(self):
        def close(ug, *a, **k):
            os.kill(os.getpid(), signal.SIGTERM)   # arrives while the restore runs: recorded, not acted on
            time.sleep(0.2)
            closed.append(ug)
            return {"code": 0, "why": "touched=0"}
        closed = []
        previous = signal.signal(signal.SIGTERM, lambda n, _f: sys.exit(128 + n))
        try:
            outcome, _ = self.window(lambda cmd: done(0), close=close)
        finally:
            signal.signal(signal.SIGTERM, previous)
        self.assertEqual(1, len(closed))              # the restore completed
        self.assertIsInstance(outcome, SystemExit)    # then the signal was raised
        self.assertEqual(143, outcome.code)

    def test_Bot_KomsuDizindeCozulmemisIsaret_Cikis65HicbirSeyBaslamaz(self):
        sibling = self.root / "a2" / "gameplay-tp" / "ug"
        sibling.mkdir(parents=True)
        (sibling / gr.MARKER).write_text("x\n")
        argv = ["wp078_bot.py", "--bin", str(self.root / "no.app"), "--out", str(self.root / "a2" / "gameplay-fp"),
                "--commit", "a" * 40, "--mode", "fp"]
        with mock.patch.object(sys, "argv", argv), mock.patch.object(gr, "canon_refusal", return_value=None), \
                mock.patch.object(gr, "run_guard") as run_guard, mock.patch.object(wp078_bot, "launch") as launch:
            self.assertEqual(65, wp078_bot.main())
        run_guard.assert_not_called()
        launch.assert_not_called()
        self.assertFalse((self.root / "a2" / "gameplay-fp").exists())

    def test_Players_PsBasarisiz_CanliSayilir_SinyalYoksayar(self):
        failed = subprocess.CompletedProcess([], -2, "", "")
        with mock.patch.object(wp078_perf.subprocess, "run", return_value=failed) as run:
            live = wp078_perf.players()
        self.assertEqual(1, len(live))
        self.assertIn("ps exit -2", live[0])
        self.assertIs(gr._ignore_signals, run.call_args[1]["preexec_fn"])

    def test_DeferredSignals_OncedenYoksayilan_YoksayilmayaDevam(self):
        previous = signal.signal(signal.SIGHUP, signal.SIG_IGN)   # like nohup
        try:
            block = gr.DeferredSignals()
            with block:
                os.kill(os.getpid(), signal.SIGHUP)
                time.sleep(0.1)
            block.reraise()                                        # nothing recorded, nothing raised
            self.assertIsNone(block.pending)
            self.assertEqual(signal.SIG_IGN, signal.getsignal(signal.SIGHUP))
        finally:
            signal.signal(signal.SIGHUP, previous)

    def test_DeferredSignals_CagiraninIsleyicisiCagrilir_ReceiptDoner(self):
        seen = []
        previous = signal.signal(signal.SIGTERM, lambda n, _f: seen.append(n))   # like wp078_accept.on_sigterm: a flag
        try:
            def fake_restore(ug, scope, **_k):
                os.kill(os.getpid(), signal.SIGTERM)
                time.sleep(0.1)
                return 0, done(0, "restore scope=player touched=0 verified=1")
            with mock.patch.object(gr, "restore_with_retry", side_effect=fake_restore), mock.patch.object(gr, "export_prefs"):
                receipt = gr.close(self.root / "ug", "player")
            self.assertEqual(0, receipt["code"])                    # the receipt came back (not a synthesized SystemExit)
            self.assertEqual([signal.SIGTERM], seen)                # the caller's own handler saw the TERM, once, afterwards
        finally:
            signal.signal(signal.SIGTERM, previous)

    def test_Wp078Perf_NohupHUP_YoksayilmayaDevam(self):
        code = textwrap.dedent(f'''
            import signal, sys
            sys.path.insert(0, {str(TOOLS / "qa")!r})
            import guard_receipt, wp078_perf
            guard_receipt.GUARD = "/nonexistent/userdata-guard.sh"
            sys.argv = ["wp078_perf.py", "--bin", "/nonexistent.app", "--out", {str(self.root / "o")!r}, "--commit", "a" * 40]
            rc = wp078_perf.main()
            print(rc, signal.getsignal(signal.SIGHUP) == signal.SIG_IGN, callable(signal.getsignal(signal.SIGTERM)))
        ''')
        r = subprocess.run([sys.executable, "-c", code], capture_output=True, text=True, timeout=60,
                           preexec_fn=lambda: signal.signal(signal.SIGHUP, signal.SIG_IGN))
        self.assertEqual("64 True True", r.stdout.strip().splitlines()[-1], r.stdout + r.stderr)

    def test_Wp064Run_StopPlayerSirasindaTERM_KillYedegiKesilmez(self):
        grand = self.root / "grand.pid"
        h = self.root / "h.py"
        h.write_text(textwrap.dedent(f'''
            import signal, subprocess, sys
            sys.path.insert(0, {str(TOOLS / "qa")!r})
            import wp064_run
            signal.signal(signal.SIGTERM, lambda n, _f: sys.exit(128 + n))   # what wp064_run installs
            p = subprocess.Popen(["/bin/sh", "-c", "trap '' TERM; (trap '' TERM; exec sleep 60) & echo $! > {grand}; wait"])
            import time
            while not __import__("os").path.exists({str(grand)!r}): time.sleep(0.05)
            open({str(self.root / "ready")!r}, "w").close()
            wp064_run.stop_player(p)
            print("stop_player done", p.poll() is not None)
        '''))
        p = subprocess.Popen([sys.executable, str(h)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.assertTrue(wait_for((self.root / "ready").exists))
        time.sleep(1.0)
        os.kill(p.pid, signal.SIGTERM)                    # wp078_perf's TERM, mid stop_player's 10 s wait
        out, err = p.communicate(timeout=60)
        self.assertIn("stop_player done True", out, out + err)
        gpid = int(grand.read_text())
        self.assertTrue(wait_for(lambda: not alive(gpid), 5))   # the KILL fallback ran

    def test_Wp064Run_StopPlayer_ZamanAsimiYolu_ErtelenenSinyalSonraYukselir(self):
        p = subprocess.Popen(["sleep", "30"])
        real = wp064_run._stop_player

        def stop_with_term(player):
            os.kill(os.getpid(), signal.SIGTERM)   # arrives mid stop: recorded, not acted on
            time.sleep(0.1)
            real(player)
        previous = signal.signal(signal.SIGTERM, lambda n, _f: sys.exit(128 + n))
        try:
            with mock.patch.object(wp064_run, "_stop_player", side_effect=stop_with_term):
                block = wp064_run.stop_player(p)
            self.assertIsNotNone(p.poll())                    # the stop completed first
            with self.assertRaises(SystemExit) as caught:     # the timeout path's .reraise() then raises it
                block.reraise()
            self.assertEqual(143, caught.exception.code)
        finally:
            signal.signal(signal.SIGTERM, previous)

    def test_Wp078_CozulmemisIsaret_Cikis65HicbirSeyBaslamaz(self):
        ug = self.root / "out/ug/ug-perf-yuksek-tp-1"
        ug.mkdir(parents=True)
        (ug / gr.MARKER).write_text("x\n")
        argv = ["wp078_perf.py", "--bin", str(self.root / "no.app"), "--out", str(self.root / "out"), "--commit", "a" * 40,
                "--tiers", "yuksek", "--modes", "tp", "--windows", "1", "--no-quiet"]
        with mock.patch.object(sys, "argv", argv), mock.patch.object(gr, "canon_refusal", return_value=None), \
                mock.patch.object(wp078_perf, "run_all") as run_all:
            self.assertEqual(65, wp078_perf.main())
            run_all.assert_not_called()
            (ug / gr.MARKER_RESOLVED).write_text("C-00\n")
            run_all.return_value = 0
            self.assertEqual(0, wp078_perf.main())
            run_all.assert_called_once()

    def test_Wp078_Sira_RestoreQuietReleaseOncesi(self):
        order = []

        class FakeQuiet:
            def __init__(self, *a, **k):
                pass

            def before_window(self, seconds):
                order.append("quiet-acquire")
                return "quiet"

            def release(self):
                order.append("quiet-release")

        argv = ["wp078_perf.py", "--bin", str(self.root / "no.app"), "--out", str(self.root / "out"), "--commit", "a" * 40,
                "--tiers", "yuksek", "--modes", "tp", "--windows", "1"]
        with mock.patch.object(sys, "argv", argv), mock.patch.object(gr, "canon_refusal", return_value=None), \
                mock.patch.object(wp078_perf, "QuietWindow", FakeQuiet), \
                mock.patch.object(wp078_perf, "players", return_value=[]), \
                mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(gr, "close", side_effect=lambda *a, **k: order.append("restore") or {"code": 0, "why": "ok"}), \
                mock.patch.object(wp078_perf, "launch", side_effect=KeyboardInterrupt):
            with self.assertRaises(KeyboardInterrupt):
                wp078_perf.main()
        self.assertEqual(["quiet-acquire", "restore", "quiet-release"], order)

    # -- real processes: TERM → child, then restore; a group signal does not cut the guard restore ---------------------
    HARNESS = textwrap.dedent('''
        import os, sys, time
        from pathlib import Path
        sys.path.insert(0, {qa!r})
        import guard_receipt as gr, wp078_perf
        log = Path({log!r})
        gr.GUARD = {guard!r}
        gr.export_prefs = lambda target: None
        wp078_perf.players = lambda: []
        real_launch = wp078_perf.launch
        wp078_perf.launch = lambda command: real_launch([sys.executable, {child!r}], grace=5.0)
        import signal
        for n in (signal.SIGTERM, signal.SIGHUP):
            signal.signal(n, lambda s, _f: sys.exit(128 + s))
        try:
            code = wp078_perf.run_window(__import__("argparse").Namespace(dry_run=False, player_wait=0, commit="a" * 40,
                                         load_max=6.0), Path({root!r}), Path("/b/P"), 60, None, lambda s: None, "yuksek", "tp", 1)
        except KeyboardInterrupt:
            with log.open("a") as f: f.write("harness KeyboardInterrupt\\n")
            sys.exit(130)
        except SystemExit as e:
            with log.open("a") as f: f.write(f"harness SystemExit {{e.code}}\\n")
            raise
        with log.open("a") as f: f.write(f"harness return {{code}}\\n")
        sys.exit(code or 0)
    ''')
    CHILD = textwrap.dedent('''
        import os, signal, sys, time
        from pathlib import Path
        log = Path({log!r})
        def term(n, _f):
            with log.open("a") as f: f.write("child TERM\\n")
            sys.exit(0)
        signal.signal(signal.SIGTERM, term)
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        Path({pidfile!r}).write_text(str(os.getpid()))
        time.sleep({sleep})
        with log.open("a") as f: f.write("child done\\n")
    ''')
    # like ff06: a zsh restore that sleeps, with no INT/TERM/HUP trap of its own (ff06 has only an EXIT trap; zsh, unlike
    # POSIX sh, still runs a trap set for a signal that was ignored at entry — so the guard must never add one)
    GUARD = textwrap.dedent('''\
        #!/bin/zsh
        cmd=$1
        if [ "$cmd" = restore ]; then echo $$ > {gpid}; echo "guard restore start" >> {log}; sleep 2
          echo "guard restore end" >> {log}; echo "restore scope=player touched=0 verified=1"; fi
        exit 0
        ''')

    def harness(self, child_sleep):
        log = self.root / "log.txt"
        child = self.root / "child.py"
        child.write_text(self.CHILD.format(log=str(log), pidfile=str(self.root / "child.pid"), sleep=child_sleep))
        guard = self.root / "fake-guard.sh"
        write_exec(guard, self.GUARD.format(log=str(log), gpid=str(self.root / "guard.pid")))
        h = self.root / "harness.py"
        h.write_text(self.HARNESS.format(qa=str(TOOLS / "qa"), log=str(log), guard=str(guard), child=str(child),
                                         root=str(self.root / "out")))
        p = subprocess.Popen([sys.executable, str(h)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                             start_new_session=True)
        self.addCleanup(lambda: p.poll() is None and os.killpg(p.pid, signal.SIGKILL))
        return p, log

    def test_Wp078_Sigterm_Wp064RunTermSonraRestoreCikis143(self):
        p, log = self.harness(child_sleep=30)
        self.assertTrue(wait_for(lambda: (self.root / "child.pid").exists()))
        os.kill(p.pid, signal.SIGTERM)                       # the runner only (not the group)
        out, err = p.communicate(timeout=60)
        self.assertEqual(143, p.returncode, out + err)
        lines = log.read_text().splitlines()
        self.assertIn("child TERM", lines)                   # TERM, not an immediate SIGKILL
        self.assertLess(lines.index("child TERM"), lines.index("guard restore start"))
        self.assertIn("guard restore end", lines)

    def test_Wp078_SurecGrubunaInt_GuardRestoreKesilmez(self):
        p, log = self.harness(child_sleep=0)
        self.assertTrue(wait_for(lambda: (self.root / "guard.pid").exists()))
        os.killpg(p.pid, signal.SIGINT)                      # Ctrl-C to the whole group, mid-restore
        time.sleep(0.2)
        os.kill(p.pid, signal.SIGTERM)                       # a second signal to the runner while the restore still runs
        out, err = p.communicate(timeout=60)
        lines = log.read_text().splitlines()
        self.assertIn("guard restore end", lines, out + err)   # the restore was not cut (the zsh default on INT is to die)
        self.assertLess(lines.index("guard restore end"), lines.index("harness KeyboardInterrupt"))
        self.assertEqual(130, p.returncode, out + err)       # the first deferred signal (INT) is raised after the restore

    # -- wp078_bot -----------------------------------------------------------------------------------------------------
    def bot(self, launch, live=()):
        closed = []
        args = argparse.Namespace(timeout=60, quiet=False, load_max=6.0, kind="gameplay", mode="tp")
        out = self.root / "bot"
        out.mkdir()
        with mock.patch.object(gr, "run_guard", return_value=done(0)), \
                mock.patch.object(wp078_perf, "players", return_value=list(live)), \
                mock.patch.object(gr.time, "sleep"), \
                mock.patch.object(gr, "close", side_effect=lambda ug, *a, **k: closed.append(ug) or {"code": 0, "why": "touched=0"}), \
                mock.patch.object(wp078_bot, "launch", side_effect=launch):
            try:
                return wp078_bot.run_guarded(args, out, ["/b/P"], {"status": "starting"}), closed, out
            except BaseException as error:   # noqa: B902
                return error, closed, out

    def test_Bot_IstisnadaPlayerTermSonraRestore(self):
        class FakePlayer:
            def __init__(self):
                self.polls = 0

            def poll(self):
                self.polls += 1
                if self.polls > 1:
                    raise KeyboardInterrupt   # Ctrl-C while the Player runs
                return None

        player = FakePlayer()
        with mock.patch.object(gr, "stop_child") as stop:
            outcome, closed, _ = self.bot(lambda cmd: player)
        self.assertIsInstance(outcome, KeyboardInterrupt)
        stop.assert_called_once_with(player, 30.0)
        self.assertEqual(1, len(closed))

    def test_Bot_PlayerYasiyor_Isaret65RestoreYok(self):
        outcome, closed, out = self.bot(KeyboardInterrupt, live=["77 P"])
        self.assertEqual(65, outcome)
        self.assertEqual([], closed)
        self.assertTrue((out / "ug" / gr.MARKER).is_file())

    # -- stop_child / wp064_run.stop_player on real processes ----------------------------------------------------------
    def test_StopChild_TermYoksayan_GraceSonraKill(self):
        p = subprocess.Popen(["/bin/sh", "-c", "trap '' TERM; sleep 30"], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        time.sleep(0.3)
        t = time.time()
        gr.stop_child(p, grace=1.0)
        self.assertIsNotNone(p.poll())
        self.assertEqual(-signal.SIGKILL, p.returncode)
        self.assertLess(time.time() - t, 10)

    def test_Wp064Run_StopPlayer_TermYoksayanAgac_Oldurulur(self):
        grand = self.root / "grand.pid"
        p = subprocess.Popen(["/bin/sh", "-c", f"trap '' TERM; (trap '' TERM; exec sleep 60) & echo $! > {grand}; wait"])
        self.assertTrue(wait_for(lambda: grand.exists() and grand.read_text().strip()))
        gpid = int(grand.read_text())
        wp064_run.stop_player(p)
        self.assertIsNotNone(p.poll())
        self.assertTrue(wait_for(lambda: not alive(gpid), 5))


# --- D3 ---------------------------------------------------------------------------------------------------------------
class SmokeK2Tests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-g4-d3-")
        self.root = Path(self.tmp.name).resolve()
        self.tools = self.root / "repo/tools"
        self.tools.mkdir(parents=True)
        for name in ("smoke.sh", "dd-env.sh", "slot.sh"):
            shutil.copy2(TOOLS / name, self.tools / name)
        self.log = self.root / "log"
        self.log.mkdir()
        # a fake `defaults` first on PATH: whatever happens, smoke.sh can never touch a real preferences domain here
        self.bin = self.root / "bin"
        write_exec(self.bin / "defaults", f"#!/bin/sh\necho \"$@\" >> {self.log}/defaults-calls.txt\nexit 1\n")
        self.app = self.root / "FixtureApp.app"
        write_exec(self.app / "Contents/MacOS/Deaths Demolition - Playable Concept",
                   f"#!/bin/sh\necho ran >> {self.log}/player-ran.txt\nexit 0\n")
        (self.root / "home").mkdir()
        self.env = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
        self.env.update(HOME=str(self.root / "home"), TMPDIR=str(self.root), DD_SLOT_BASE=str(self.root / "slots"),
                        DD_SLOT_POLL="1", DD_GUARD_PLAYER_RE=FIXTURE_PLAYER, PATH=f"{self.bin}:{os.environ['PATH']}")
        self.procs = []

    def tearDown(self):
        for p in self.procs:
            if p.poll() is None:
                os.killpg(p.pid, signal.SIGKILL)   # only fixture processes started here
            p.communicate(timeout=10)             # also closes the pipes
        self.tmp.cleanup()

    def start(self, argv):
        p = subprocess.Popen(argv, env=self.env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                             start_new_session=True)
        self.procs.append(p)
        return p

    def live_player(self):
        exe = self.root / FIXTURE_PLAYER
        exe.symlink_to("/bin/sleep")   # ps comm shows the path it was started by (smoke.sh reads comm, not argv)
        p = self.start([str(exe), "60"])
        self.assertTrue(wait_for(lambda: FIXTURE_PLAYER in subprocess.run(["ps", "-Ao", "comm="], capture_output=True, text=True).stdout))
        return p

    def smoke(self, out):
        return self.start(["zsh", str(self.tools / "smoke.sh"), str(self.app), str(out), "--mode", "release"])

    def assert_refused_before_copy(self, out, p):
        o, e = p.communicate(timeout=30)
        self.assertEqual(30, p.returncode, o + e)
        self.assertFalse((out / "userdata-backup").exists())
        self.assertFalse((self.log / "player-ran.txt").exists())
        self.assertFalse((self.log / "defaults-calls.txt").exists())
        self.assertIn("smoke: REFUSED — a Player is running", (out / "timeline.txt").read_text())

    def held(self, kind):
        return list((self.root / "slots" / kind).glob("*")) if (self.root / "slots" / kind).exists() else []

    def test_Smoke_CanliPlayer_Cikis30KopyaYok(self):
        self.live_player()
        out = self.root / "out"
        self.assert_refused_before_copy(out, self.smoke(out))
        self.assertEqual([], self.held("player") + self.held("smoke"))   # both released by the EXIT trap

    def test_Smoke_SlotSirasi_PlayerOnceSmokeSonra(self):
        self.live_player()
        out = self.root / "out"
        self.assert_refused_before_copy(out, self.smoke(out))
        lines = [l for l in (out / "timeline.txt").read_text().splitlines() if "slot:" in l or "REFUSED" in l]
        acq_p = next(i for i, l in enumerate(lines) if "acquiring player" in l)
        acq_s = next(i for i, l in enumerate(lines) if "acquiring smoke" in l)
        refused = next(i for i, l in enumerate(lines) if "REFUSED" in l)
        rel_s = next(i for i, l in enumerate(lines) if "released smoke" in l)
        rel_p = next(i for i, l in enumerate(lines) if "released player" in l)
        self.assertLess(acq_p, acq_s)
        self.assertLess(acq_s, refused)
        self.assertLess(refused, rel_s)
        self.assertLess(rel_s, rel_p)

    def test_SmokeAbba_ReleaseVeBagimsizSmoke_KilitlenmeYok(self):
        """T33: A = a release shell that holds 'player' (like the guard snapshot) and then runs smoke.sh; B = a standalone
        smoke.sh started while A holds 'player'. Old order (smoke → player): B holds smoke and waits for player, A's smoke
        re-enters player and waits for smoke — deadlock. New order: both finish (each refuses at the live fixture Player)."""
        self.live_player()
        go = self.root / "go"
        outer = self.root / "release-shell.sh"
        write_exec(outer, textwrap.dedent(f'''\
            #!/bin/bash
            "{self.tools}/slot.sh" acquire player release-A >/dev/null || exit 9
            : > "{self.root}/a-holds"
            while [ ! -f "{go}" ]; do sleep 0.1; done
            zsh "{self.tools}/smoke.sh" "{self.app}" "{self.root}/out-a" --mode release; echo $? > "{self.root}/a-rc"
            "{self.tools}/slot.sh" release player release-A >/dev/null
            '''))
        a = self.start([str(outer)])
        self.assertTrue(wait_for(lambda: (self.root / "a-holds").exists()))
        b = self.smoke(self.root / "out-b")
        self.assertTrue(wait_for(lambda: (self.root / "out-b/timeline.txt").exists()
                                 and "acquiring player" in (self.root / "out-b/timeline.txt").read_text()))
        time.sleep(1.5)   # B is now queued on 'player'
        go.write_text("")
        t = time.time()
        try:
            a.wait(timeout=20)
            b.wait(timeout=20)
        except subprocess.TimeoutExpired:
            self.fail("deadlock: release shell + standalone smoke did not both finish within 20 s")
        self.assertLess(time.time() - t, 20)
        self.assertEqual("30", (self.root / "a-rc").read_text().strip())
        self.assertEqual(30, b.returncode)
        for o in ("out-a", "out-b"):
            self.assertFalse((self.root / o / "userdata-backup").exists())


# --- D4 ---------------------------------------------------------------------------------------------------------------
class CanonicalGuardTests(unittest.TestCase):
    def test_Wp078Guard_KanonikYol_DriverKopyasiCagrilir(self):
        self.assertEqual(str(Path.home() / CANON_TAIL), gr.GUARD)
        with mock.patch.dict(os.environ, {"DD_USERDATA_GUARD": "/somewhere/else/userdata-guard.sh"}), \
                mock.patch.object(gr.subprocess, "run", return_value=done(0)) as run:
            gr.run_guard("snapshot", "/d", "player")
            gr.run_guard("restore", "/d", "player")
        self.assertEqual([gr.GUARD, "snapshot", "/d", "--scope", "player"], run.call_args_list[0][0][0])
        self.assertEqual(gr.GUARD, run.call_args_list[1][0][0][0])
        self.assertIsNone(run.call_args_list[0][1]["preexec_fn"])
        self.assertIsNotNone(run.call_args_list[1][1]["preexec_fn"])   # restore: signals ignored in the guard child

    def main_refuses(self, module, argv, env=None, guard="/nonexistent/userdata-guard.sh"):
        with mock.patch.object(sys, "argv", argv), mock.patch.object(gr, "GUARD", guard), \
                mock.patch.dict(os.environ, env or {}), \
                mock.patch.object(gr, "run_guard") as run_guard, mock.patch.object(module, "launch") as launch:
            code = module.main()
        run_guard.assert_not_called()
        launch.assert_not_called()
        return code

    def argv(self, tmp):
        return {wp078_perf: ["wp078_perf.py", "--bin", f"{tmp}/no.app", "--out", f"{tmp}/out", "--commit", "a" * 40,
                             "--tiers", "yuksek", "--modes", "tp", "--windows", "1", "--no-quiet"],
                wp078_bot: ["wp078_bot.py", "--bin", f"{tmp}/no.app", "--out", f"{tmp}/bot", "--commit", "a" * 40,
                            "--mode", "tp"]}

    def test_Wp078Guard_KanonikYolYok_Cikis64(self):
        with tempfile.TemporaryDirectory() as tmp:
            for module, argv in self.argv(tmp).items():
                with self.subTest(module=module.__name__):
                    self.assertEqual(64, self.main_refuses(module, argv))
                    self.assertFalse(Path(tmp, "out").exists() or Path(tmp, "bot").exists())

    def test_Wp078_DdGuardRootDolu_Cikis64(self):
        with tempfile.TemporaryDirectory() as tmp:
            guard = Path(tmp) / "canon-guard.sh"
            write_exec(guard, "#!/bin/sh\nexit 0\n")
            for module, argv in self.argv(tmp).items():
                with self.subTest(module=module.__name__):
                    self.assertEqual(64, self.main_refuses(module, argv, {"DD_GUARD_ROOT": tmp}, str(guard)))
            with mock.patch.object(gr, "GUARD", str(guard)), mock.patch.dict(os.environ, {}, clear=False):
                os.environ.pop("DD_GUARD_ROOT", None)
                self.assertIsNone(gr.canon_refusal())


    def test_Wp078Accept_KanonikYolYok_Cikis64(self):
        import wp078_accept
        with tempfile.TemporaryDirectory() as tmp, mock.patch.object(gr, "GUARD", "/nonexistent/userdata-guard.sh"), \
                mock.patch.object(wp078_accept, "Chain") as chain:
            self.assertEqual(64, wp078_accept.main(["--final", "a" * 40, "--evidence", tmp, "--stages", "a4"]))
            chain.assert_not_called()


class MainCiCanonicalGuardTests(CiFixture):
    def test_MainCi_GuardCanondanCagrilir(self):
        rc, out, err = self.finish(self.main_ci(mode="pass", playmode=True))   # green needs both XMLs
        self.assertEqual(0, rc, out + err)
        origins = self.lines("guard-origin.txt")
        self.assertGreaterEqual(len(origins), 2)            # snapshot + restore
        self.assertEqual({"canon"}, set(origins))           # never the driver worktree's own copy

    def test_MainCi_DdGuardRootDolu_Cikis64HicbirSeyYok(self):
        rc, out, err = self.finish(self.main_ci(mode="pass", DD_GUARD_ROOT=str(self.root / "groot")))
        self.assertEqual(64, rc, out + err)
        self.assertIn("DD_GUARD_ROOT", err)
        self.assertEqual([], self.lines("guard-calls.txt") + self.lines("slot-calls.txt") + self.lines("test-calls.txt"))
        self.assertEqual([], self.leases())
        self.assertFalse(self.out.exists())

    def test_MainCi_CanonYok_Cikis64(self):
        rc, out, err = self.finish(self.main_ci(mode="pass", prepare=lambda: self.canon_guard().unlink()))
        self.assertEqual(64, rc, out + err)
        self.assertIn("canonical guard", err)
        self.assertEqual([], self.lines("guard-calls.txt") + self.lines("slot-calls.txt") + self.lines("test-calls.txt"))
        self.assertEqual([], self.leases())


if __name__ == "__main__":
    unittest.main(verbosity=2)
