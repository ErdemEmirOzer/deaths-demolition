"""WP-091 C-00 B-09 (06.10) · tools/qa/log_scan.py — the last RC stage.

Real use: rc.sh `stage log-scan python3 "$HERE/log_scan.py" "$OUT" --since "$START_EPOCH"`; the stage exit code is
recorded in stages/log-scan/exit.txt and rc_aggregate.py reads <OUT>/log-scan.json and requires `pass is True`.
The tests drive the public CLI exactly like that. They are hermetic: every run gets its own temporary root and its own
temporary HOME, so the crash-report folder ($HOME/Library/Logs/DiagnosticReports) is a fixture and the machine's real
crash reports are never read. No network, no Unity, no user data.

Known defects are written as @unittest.expectedFailure with the reason; the tool itself is not changed here.
"""
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "log_scan.py"
REPORT_KEYS = {"schema", "utc", "pass", "filesScanned", "unreadable", "matches", "newCrashReports"}   # unreadable: LS-1 (S-10 06.10)
SUMMARY = re.compile(r"^(PASS|FAIL) logs=(\d+) matches=(\d+) crashes=(\d+)$")

# One realistic line per pattern in log_scan.PATTERNS (Unity Player/Editor, IL2CPP/Mono crash blocks, C# compiler,
# shader compiler, build pipeline, QaRunBot, NUnit console, Debug.Assert). Removing any pattern fails its sub-test.
# `\bCrash!!!\b` is not here: it cannot match Unity's own "Crash!!!" line (defect LS-3, expectedFailure below).
PATTERN_LINES = (
    ("NullReferenceException", "NullReferenceException: Object reference not set to an instance of an object"),
    ("ArgumentException", "ArgumentException: An item with the same key has already been added. Key: Run"),
    ("ArgumentOutOfRangeException", "ArgumentOutOfRangeException: Index was out of range. Parameter name: index"),
    ("IndexOutOfRangeException", "IndexOutOfRangeException: Index was outside the bounds of the array."),
    ("Unhandled exception", "Unhandled exception. System.InvalidOperationException: Sequence contains no elements"),
    ("Native Crash Reporting", "\tNative Crash Reporting"),
    ("Segmentation fault", "Segmentation fault: 11"),
    ("error CS", "Assets/_Prototype/Foo.cs(12,5): error CS0103: The name 'x' does not exist in the current context"),
    ("Shader error in", "Shader error in 'DD/Lit': undeclared identifier 'foo' at line 12 (on metal)"),
    ("BuildFailedException", "BuildFailedException: Build failed with 3 errors"),
    ("[QaRunBot] write failed", "[QaRunBot] write failed: IOException: Disk full"),
    ("WITH FAILURES", "Test run completed WITH FAILURES"),
    ("Assertion failed", "Assertion failed: Value was False"),
)

# Lines a healthy run prints; none of them may be a finding (the "warnings only" contract).
BENIGN_LINES = (
    "Assets/_Prototype/Foo.cs(3,1): warning CS0618: 'Physics.autoSimulation' is obsolete",
    "Shader warning in 'DD/Lit': implicit truncation of vector type (on metal)",
    "[Warning] Sprite 'icon_gold' is missing from atlas UiAtlas",
    "Build completed with a result of 'Succeeded' in 412 seconds",
    "Test run completed: 812 passed, 0 failed, 2 skipped",
    "0 errors, 2 warnings",
    "Exception handling: first-chance exceptions are logged",
    "MyNullReferenceExceptionHandler registered",
)


def hermetic_env(home):
    env = {key: value for key, value in os.environ.items() if not key.startswith("DD_")}
    env.update(HOME=str(home), PYTHONDONTWRITEBYTECODE="1", PYTHONIOENCODING="utf-8")
    return env


class LogScanTestCase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory(prefix="dd-b09-logscan-")
        self.addCleanup(self._tmp.cleanup)
        base = Path(self._tmp.name)
        self.root = base / "rc-out"
        self.root.mkdir()
        self.home = base / "home"
        self.home.mkdir()
        self.crash_dir = self.home / "Library" / "Logs" / "DiagnosticReports"
        self.since = int(time.time()) - 60

    # ---- fixtures
    def write(self, relative, content):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        if isinstance(content, bytes):
            path.write_bytes(content)
        else:
            path.write_text(content, encoding="utf-8")
        return path

    def crash(self, name, mtime):
        self.crash_dir.mkdir(parents=True, exist_ok=True)
        path = self.crash_dir / name
        path.write_text('{"app_name":"Deaths Demolition"}\n', encoding="utf-8")
        os.utime(path, (mtime, mtime))
        return path

    # ---- runner (same argv shape as rc.sh)
    def scan(self, *args, root=None):
        """args replace the default `--since <epoch>`; pass ("--no-since",) for none at all."""
        argv = [sys.executable, str(SCRIPT), str(root or self.root)]
        if not args:
            args = ("--since", str(self.since))
        argv += [a for a in args if a != "--no-since"]
        proc = subprocess.run(argv, capture_output=True, text=True, encoding="utf-8", env=hermetic_env(self.home),
                              cwd=self._tmp.name, timeout=120)
        json_path, txt_path = self.root / "log-scan.json", self.root / "log-scan.txt"
        report = json.loads(json_path.read_text(encoding="utf-8")) if json_path.exists() else None
        text = txt_path.read_text(encoding="utf-8") if txt_path.exists() else None
        if report is not None and proc.returncode in (0, 1):
            self.assert_contract(proc, report, text)
        return proc, report, text

    def assert_contract(self, proc, report, text):
        """Invariants every successful run keeps: rc_aggregate reads `pass is True`, rc.sh gates on the exit code."""
        self.assertEqual(REPORT_KEYS, set(report), report)
        self.assertEqual("dd-log-scan-v1", report["schema"])
        self.assertIs(type(report["pass"]), bool)
        self.assertRegex(report["utc"], r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")
        self.assertIs(type(report["filesScanned"]), int)
        # S-10 06.10: an unreadable log (LS-1) and an empty root fail too
        self.assertEqual(report["pass"], not report["matches"] and not report["newCrashReports"]
                         and not report["unreadable"] and report["filesScanned"] > 0)
        self.assertEqual(0 if report["pass"] else 1, proc.returncode, proc.stdout + proc.stderr)
        for hit in report["matches"]:
            self.assertEqual({"path", "line", "text"}, set(hit))
            self.assertIs(type(hit["line"]), int)
            self.assertGreaterEqual(hit["line"], 1)
            self.assertFalse(os.path.isabs(hit["path"]), hit)
            self.assertLessEqual(len(hit["text"]), 500)
        for item in report["unreadable"]:
            self.assertEqual({"path", "error"}, set(item))
            self.assertFalse(os.path.isabs(item["path"]), item)
        first = text.splitlines()[0]
        match = SUMMARY.match(first)
        self.assertIsNotNone(match, first)
        self.assertEqual(("PASS" if report["pass"] else "FAIL", str(report["filesScanned"]), str(len(report["matches"])),
                          str(len(report["newCrashReports"]))), match.groups())
        self.assertEqual(first + "\n", proc.stdout)

    def hits(self, report):
        return sorted((hit["path"], hit["line"]) for hit in report["matches"])


class NormalInputTests(LogScanTestCase):
    def test_TemizLoglar_Pass_Cikis0(self):
        self.write("stages/full-editmode/stdout.log", "Batchmode quit successfully invoked - shutting down!\n")
        self.write("m3/bot/events.txt", "t=1.0 pos=(0,0,0) mode=kite kills=3\n")
        proc, report, text = self.scan()
        self.assertEqual(0, proc.returncode)
        self.assertTrue(report["pass"])
        self.assertEqual(2, report["filesScanned"])
        self.assertEqual([], report["matches"])
        self.assertEqual([], report["newCrashReports"])
        self.assertEqual("PASS logs=2 matches=0 crashes=0\n", text)

    def test_YalnizUyarilar_Pass(self):
        self.write("stages/release-smoke/stdout.log", "\n".join(BENIGN_LINES) + "\n")
        proc, report, _ = self.scan()
        self.assertEqual(0, proc.returncode, report["matches"])
        self.assertEqual([], report["matches"])

    def test_HerDesen_TekIsabet_Fail_Cikis1(self):
        for label, line in PATTERN_LINES:
            with self.subTest(pattern=label):
                for leftover in self.root.iterdir():
                    leftover.unlink()
                self.write("player.log", "Loading scene Run\n" + line + "\nRound 1 started\n")
                proc, report, text = self.scan()
                self.assertEqual(1, proc.returncode)
                self.assertFalse(report["pass"])
                self.assertEqual([{"path": "player.log", "line": 2, "text": line.strip()}], report["matches"])
                self.assertIn("player.log:2: " + line.strip() + "\n", text)

    def test_Desenler_BuyukKucukHarfDuyarsiz(self):
        self.write("a.log", "nullreferenceexception: lower case\nAssets/x.cs(1,1): Error cs0246: missing type\n")
        _, report, _ = self.scan()
        self.assertEqual([("a.log", 1), ("a.log", 2)], self.hits(report))

    def test_Desenler_KelimeSinirinaBagli(self):
        self.write("a.log", "MyNullReferenceExceptionHandler\nSegmentation faults counted: 0\nerror CSV export done\n")
        _, report, _ = self.scan()
        self.assertEqual([], report["matches"])

    def test_IsabetMetni_KirpilirVeBicimlenir(self):
        self.write("stages/x/stdout.log", "   \tNullReferenceException: trailing spaces   \t\n")
        _, report, text = self.scan()
        self.assertEqual("NullReferenceException: trailing spaces", report["matches"][0]["text"])
        self.assertEqual(["FAIL logs=1 matches=1 crashes=0",
                          os.path.join("stages", "x", "stdout.log") + ":1: NullReferenceException: trailing spaces"],
                         text.splitlines())

    def test_YalnizLogVeTxtUzantisiTaranir(self):
        bad = "NullReferenceException: should only count in .log/.txt\n"
        for name in ("a.log", "b.txt", "deep/er/c.log", "d.json", "e.xml", "f.LOG", "g.log.1", "h", "i.csv"):
            self.write(name, bad)
        _, report, _ = self.scan()
        self.assertEqual(3, report["filesScanned"])
        self.assertEqual([("a.log", 1), ("b.txt", 1), (os.path.join("deep", "er", "c.log"), 1)], self.hits(report))


class BoundaryTests(LogScanTestCase):
    def test_BosKok_Fail_SifirDosya(self):
        # S-10 06.10 (C-00's B-09 residual risk): a root with no .log/.txt proves nothing — FAIL, never a vacuous PASS.
        # rc.sh always has stage logs under stages/<name>/stdout.log, so a real RC root is never empty.
        proc, report, text = self.scan()
        self.assertEqual(1, proc.returncode)
        self.assertEqual(0, report["filesScanned"])
        self.assertFalse(report["pass"])
        self.assertEqual("FAIL logs=0 matches=0 crashes=0\nno log scanned: an empty root proves nothing\n", text)

    def test_BosLogDosyasi_SayilirAmaTemiz(self):
        self.write("empty.log", "")
        self.write("stages/x/exit.txt", "0\n")
        _, report, _ = self.scan()
        self.assertTrue(report["pass"])
        self.assertEqual(2, report["filesScanned"])

    def test_CokSatirliHata_HerEslesenSatirAyriIsabet(self):
        trace = (
            "Round 3 started\n"
            "Unhandled exception. System.NullReferenceException: Object reference not set to an instance of an object\n"
            "  at DD.Prototype.RunDirector.Tick () [0x00012] in RunDirector.cs:88\n"
            "  at DD.Prototype.Boot.Update () [0x00001] in Boot.cs:12\n"
            "\n"
            "=================================================================\n"
            "\tNative Crash Reporting\n"
            "=================================================================\n"
            "Got a SIGSEGV while executing native code.\n"
            "Segmentation fault: 11\n"
        )
        self.write("player.log", trace)
        _, report, _ = self.scan()
        self.assertEqual([("player.log", 2), ("player.log", 7), ("player.log", 10)], self.hits(report))
        self.assertTrue(report["matches"][0]["text"].startswith("Unhandled exception. System.NullReferenceException"))

    def test_CRLF_SatirNumarasiVeMetinDogru(self):
        self.write("win.log", b"ok\r\nIndexOutOfRangeException: crlf line\r\nok\r\n")
        _, report, _ = self.scan()
        self.assertEqual([{"path": "win.log", "line": 2, "text": "IndexOutOfRangeException: crlf line"}], report["matches"])

    def test_TekBasinaCR_SatirSonuSayilir(self):
        # Universal newlines: a lone "\r" (progress output) ends a line, so the hit is on line 3.
        self.write("progress.log", b"10%\r50%\rSegmentation fault\r")
        _, report, _ = self.scan()
        self.assertEqual([("progress.log", 3)], self.hits(report))

    def test_Unicode_MetinKorunur(self):
        line = "Ölüm ekranı: NullReferenceException — ğüşiöç İstanbul 🧟"
        self.write("stages/tr/stdout.log", line + "\n")
        _, report, text = self.scan()
        self.assertEqual(line, report["matches"][0]["text"])
        self.assertIn(line, text)

    def test_GecersizUtf8_CokmezYerineKoyar(self):
        self.write("binary.log", b"\xff\xfe\x00garbage NullReferenceException: after junk\n\xc3\x28 tail\n")
        proc, report, _ = self.scan()
        self.assertEqual(1, proc.returncode)
        self.assertEqual([("binary.log", 1)], self.hits(report))
        self.assertIn("\ufffd", report["matches"][0]["text"])

    def test_CokBuyukSatir_500KarakteraKesilir(self):
        filler = "x" * (2 * 1024 * 1024)
        self.write("huge.log", "BuildFailedException: " + filler + "\n" + filler + " Segmentation fault\n" + filler + "\n")
        _, report, _ = self.scan()
        self.assertEqual([("huge.log", 1), ("huge.log", 2)], self.hits(report))
        first, second = report["matches"]
        self.assertEqual(500, len(first["text"]))
        self.assertTrue(first["text"].startswith("BuildFailedException: xxx"))
        self.assertEqual("x" * 500, second["text"])  # the finding at the end of the line is cut away from the text

    def test_100denFazlaIsabet_TxtIlk100_JsonHepsi(self):
        self.write("many.log", "".join("Assertion failed: #%d\n" % i for i in range(150)))
        proc, report, text = self.scan()
        self.assertEqual(150, len(report["matches"]))
        lines = text.splitlines()
        self.assertEqual("FAIL logs=1 matches=150 crashes=0", lines[0])
        self.assertEqual(101, len(lines))
        self.assertEqual("many.log:100: Assertion failed: #99", lines[-1])
        self.assertEqual("FAIL logs=1 matches=150 crashes=0\n", proc.stdout)


class MultiFileTests(LogScanTestCase):
    def test_CokDosya_TumIsabetlerToplanir(self):
        self.write("stages/full-editmode/stdout.log", "ok\nerror CS0103: missing name\n")
        self.write("stages/release-smoke/stdout.log", "ok\n")
        self.write("m3/bot/player.log", "Round 1\nRound 2\nNullReferenceException: bot\n")
        self.write("m3/perf-1/player.log", "Shader error in 'DD/Lit': x\nok\nok\nAssertion failed: perf\n")
        self.write("window-modes/c2.txt", "[QaRunBot] write failed: disk\n")
        _, report, text = self.scan()
        self.assertEqual(5, report["filesScanned"])
        expected = [
            (os.path.join("m3", "bot", "player.log"), 3),
            (os.path.join("m3", "perf-1", "player.log"), 1),
            (os.path.join("m3", "perf-1", "player.log"), 4),
            (os.path.join("stages", "full-editmode", "stdout.log"), 2),
            (os.path.join("window-modes", "c2.txt"), 1),
        ]
        self.assertEqual(expected, self.hits(report))
        self.assertTrue(text.startswith("FAIL logs=5 matches=5 crashes=0\n"))

    def test_KendiCiktisiTaranmaz_IkinciKosuAyni(self):
        self.write("stages/x/stdout.log", "NullReferenceException: once\n")
        _, first, _ = self.scan()
        # log-scan.txt now repeats the finding; the second run must not count it again
        self.assertIn("NullReferenceException: once", (self.root / "log-scan.txt").read_text(encoding="utf-8"))
        _, second, _ = self.scan()
        self.assertEqual(first["filesScanned"], second["filesScanned"])
        self.assertEqual(first["matches"], second["matches"])

    def test_RcReportTxt_Taranmaz(self):
        self.write("rc-report.txt", "FAIL log-scan: NullReferenceException in player.log\n")
        self.write("log-scan.txt", "FAIL logs=1 matches=1 crashes=0\nplayer.log:1: Segmentation fault: 11\n")
        self.write("stages/x/stdout.log", "clean\n")   # one real log, so the root is not empty (that FAILs on its own)
        proc, report, _ = self.scan()
        self.assertEqual(0, proc.returncode)
        self.assertEqual(1, report["filesScanned"])

    def test_HATA_UnityCrashSatiri_Yakalanmaz(self):   # LS-3 fixed (S-10 06.10): was expectedFailure
        # DEFECT LS-3 (medium): the pattern is r"\bCrash!!!\b". "!" is not a word character, so the trailing \b needs a
        # word character right after "!!!" and the line Unity's crash handler actually writes ("Crash!!!" alone, or
        # "Crash!!!" + space) never matches. Only the DiagnosticReports check can still catch such a crash.
        # Real format (S-10 06.10): Unity 2022.3.22f1's win64 UnityPlayer.dll holds the string "Crash!!!\n" — the line is
        # "Crash!!!" alone. The macOS player and editor carry no such string (a macOS native crash prints Mono's "Native
        # Crash Reporting" and "Obtained %zu stack frames."), so this line is what an R2 win64 build writes.
        # soak_gate.py uses r"Crash!!!" (matches); wp064_gate.py:23 has the same \b defect.
        self.write("m3/perf-2/player.log", "Obtained 31 stack frames.\nCrash!!!\n")
        proc, report, _ = self.scan()
        self.assertEqual(1, proc.returncode)
        self.assertEqual([(os.path.join("m3", "perf-2", "player.log"), 2)], self.hits(report))

    def test_HATA_SonekDislamasi_BaskaDosyalariYutar(self):   # LS-2 fixed (S-10 06.10)
        # DEFECT LS-2 (low): the self-output exclusion is `path.endswith(("log-scan.txt", "rc-report.txt"))`, a suffix
        # test on the whole path, not a basename comparison. Any log whose name merely ends that way
        # ("backlog-scan.txt", "src-report.txt") is skipped, so its errors never reach the gate.
        self.write("tools/backlog-scan.txt", "NullReferenceException: hidden by the suffix rule\n")
        self.write("stages/x/src-report.txt", "Segmentation fault: 11\n")
        proc, report, _ = self.scan()
        self.assertEqual(1, proc.returncode)
        self.assertEqual(2, len(report["matches"]))
        # the cause (C-00 06.10 N2): exactly those two look-alike names were scanned, line 1 each
        self.assertEqual([(os.path.join("stages", "x", "src-report.txt"), 1), (os.path.join("tools", "backlog-scan.txt"), 1)],
                         sorted(self.hits(report)))

    @unittest.skipIf(hasattr(os, "geteuid") and os.geteuid() == 0, "root can read a mode-000 file")
    def test_HATA_OkunamayanLog_SessizceTemizSayilir(self):   # LS-1 fixed (S-10 06.10)
        # DEFECT LS-1 (medium, fail-open): open() errors are swallowed (`except OSError: pass`) after the file was
        # already counted in filesScanned. A log that exists but cannot be read is reported as scanned and clean, so
        # the stage passes without having seen it. Expected: an unreadable log is not a PASS.
        path = self.write("stages/x/stdout.log", "NullReferenceException: unreadable\n")
        path.chmod(0)
        self.addCleanup(path.chmod, 0o600)
        proc, report, text = self.scan()
        self.assertNotEqual(0, proc.returncode)
        self.assertFalse(report["pass"])
        # the cause (C-00 06.10 N2): the log is named unreadable, not counted as scanned, and said so in the text
        self.assertEqual([os.path.join("stages", "x", "stdout.log")], [u["path"] for u in report["unreadable"]])
        self.assertEqual(0, report["filesScanned"])
        self.assertIn("unreadable: " + os.path.join("stages", "x", "stdout.log") + ": ", text)


class S10PatternTests(LogScanTestCase):
    """S-10 06.10 (C-00's B-09 residual risks): the exceptions soak_gate already catches, and the readability writer."""

    def test_ArgumentNull_InvalidOperation_MissingReference_ReadabilityWriteFailed(self):
        lines = ("ArgumentNullException: Value cannot be null.", "InvalidOperationException: Collection was modified",
                 "MissingReferenceException: The object of type 'Transform' has been destroyed",
                 "[QaReadabilityGate] write failed: disk full")
        for i, line in enumerate(lines):
            self.write(f"stages/s{i}/stdout.log", "ok\n" + line + "\n")
        proc, report, _ = self.scan()
        self.assertEqual(1, proc.returncode)
        self.assertEqual(4, len(report["matches"]))

    def test_an_unreadable_log_is_listed_and_not_counted_as_scanned(self):
        if hasattr(os, "geteuid") and os.geteuid() == 0:
            self.skipTest("root can read a mode-000 file")
        self.write("stages/a/stdout.log", "clean\n")
        path = self.write("stages/b/stdout.log", "x\n")
        path.chmod(0)
        self.addCleanup(path.chmod, 0o600)
        proc, report, text = self.scan()
        self.assertEqual((1, 1), (proc.returncode, report["filesScanned"]))
        self.assertEqual([os.path.join("stages", "b", "stdout.log")], [u["path"] for u in report["unreadable"]])
        self.assertIn("unreadable: " + os.path.join("stages", "b", "stdout.log"), text)


class CrashReportTests(LogScanTestCase):

    def test_YeniDeathsCrashRaporu_Fail(self):
        path = self.crash("Deaths Demolition-2026-10-06-010203.ips", self.since + 30)
        self.write("player.log", "clean\n")
        proc, report, text = self.scan()
        self.assertEqual(1, proc.returncode)
        self.assertEqual([str(path)], report["newCrashReports"])
        self.assertEqual([], report["matches"])
        self.assertEqual(["FAIL logs=1 matches=0 crashes=1", "crash: " + str(path)], text.splitlines())

    def test_SinceSiniri_EsitZamanSayilir(self):
        self.write("stages/clean/stdout.log", "clean\n")   # a non-empty root: an empty one FAILs on its own (S-10 06.10)
        path = self.crash("Deaths Demolition-boundary.ips", self.since)
        _, report, _ = self.scan()
        self.assertEqual([str(path)], report["newCrashReports"])

    def test_EskiCrashRaporu_YokSayilir(self):
        self.write("stages/clean/stdout.log", "clean\n")   # a non-empty root: an empty one FAILs on its own (S-10 06.10)
        self.crash("Deaths Demolition-old.ips", self.since - 1)
        proc, report, _ = self.scan()
        self.assertEqual(0, proc.returncode)
        self.assertEqual([], report["newCrashReports"])

    def test_BaskaUygulamaCrashRaporu_YokSayilir(self):
        self.write("stages/clean/stdout.log", "clean\n")   # a non-empty root: an empty one FAILs on its own (S-10 06.10)
        self.crash("Safari-2026-10-06-010203.ips", self.since + 30)
        self.crash("Unity-2026-10-06-010203.ips", self.since + 30)
        proc, report, _ = self.scan()
        self.assertEqual(0, proc.returncode)
        self.assertEqual([], report["newCrashReports"])

    def test_CrashKlasoruYok_Pass(self):
        self.write("stages/clean/stdout.log", "clean\n")   # a non-empty root: an empty one FAILs on its own (S-10 06.10)
        self.assertFalse(self.crash_dir.exists())
        proc, report, _ = self.scan()
        self.assertEqual(0, proc.returncode)
        self.assertEqual([], report["newCrashReports"])

    def test_LogIsabetiVeCrash_BirlikteRaporlanir(self):
        path = self.crash("Deaths Demolition-both.ips", self.since + 1)
        self.write("player.log", "Segmentation fault: 11\n")
        _, report, text = self.scan()
        self.assertEqual(1, len(report["matches"]))
        self.assertEqual([str(path)], report["newCrashReports"])
        self.assertEqual(["FAIL logs=1 matches=1 crashes=1", "player.log:1: Segmentation fault: 11", "crash: " + str(path)],
                         text.splitlines())


class CliTests(LogScanTestCase):
    def assertUsageError(self, proc):
        self.assertEqual(2, proc.returncode, proc.stdout + proc.stderr)
        self.assertIn("usage:", proc.stderr)
        self.assertFalse((self.root / "log-scan.json").exists())
        self.assertFalse((self.root / "log-scan.txt").exists())

    def test_SinceEksik_Cikis2(self):
        self.write("a.log", "clean\n")
        proc, _, _ = self.scan("--no-since")
        self.assertUsageError(proc)

    def test_SinceTamsayiDegil_Cikis2(self):
        self.write("a.log", "clean\n")
        proc, _, _ = self.scan("--since", "yesterday")
        self.assertUsageError(proc)

    def test_KokArgumaniEksik_Cikis2(self):
        proc = subprocess.run([sys.executable, str(SCRIPT), "--since", "0"], capture_output=True, text=True,
                              env=hermetic_env(self.home), cwd=self._tmp.name, timeout=60)
        self.assertEqual(2, proc.returncode)
        self.assertIn("usage:", proc.stderr)

    def test_OlmayanKok_PassVermez(self):
        missing = self.root / "does-not-exist"
        proc, _, _ = self.scan(root=missing)
        self.assertNotEqual(0, proc.returncode)
        self.assertNotIn("PASS", proc.stdout)
        self.assertFalse(missing.exists())

    def test_JsonCiktisi_OkunabilirVeGirintili(self):
        self.write("a.log", "Assertion failed: x\n")
        self.scan()
        raw = (self.root / "log-scan.json").read_text(encoding="utf-8")
        self.assertTrue(raw.startswith('{\n  "schema": "dd-log-scan-v1"'), raw[:60])


if __name__ == "__main__":
    unittest.main()
