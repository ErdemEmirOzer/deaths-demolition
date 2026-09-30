#!/usr/bin/env python3
"""Codex-audit M-05 (C-00 30.09) · the CI checkout owner lease, one source in tools/dd-env.sh (dd_ci_lease_acquire /
dd_ci_lease_release / dd_ci_lease_status), used by tools/main-ci.sh and tools/compile-check.sh:
busy (live owner) → 75 · malformed owner file / symlinked lease / stale lease → 3, never reclaimed · two checkouts hold their
own leases at once, the same canonical checkout (also through an alias) cannot · INT/TERM release it · compile-check.sh
--lease-status verdicts (FREE 0 · HELD-LIVE 75 · STALE-DEAD-OWNER / STALE-PID-REUSED / MALFORMED / SYMLINK 3) and it deletes
nothing · the start comparison is `env -u TZ LC_ALL=C ps -o lstart=` (a LANG=tr_TR TZ=UTC caller reads a live owner as live).
Isolated: leases next to tempdir checkouts; holders are zsh processes started here; the script-level cases reuse the fake
repo / driver / slot / guard fixture of test_ci_signal_traps.py. No Unity, no Player, no real slot dir, no real user data.
usage: python3 tools/tests/test_ci_lease.py"""
import os, signal, subprocess, sys, tempfile, time, unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from test_ci_signal_traps import CiFixture, alive  # noqa: E402  (fixture only: no tests are imported)

TOOLS = Path(__file__).resolve().parents[1]
DD_ENV = TOOLS / "dd-env.sh"
COMPILE_CHECK = TOOLS / "compile-check.sh"
# Holds the lease of $2 until TERM/INT; writes "<rc> <lease path>" to $3 once acquire returned.
HOLDER = r'''DD_TIMELINE=""; source "$1"
trap 'dd_ci_lease_release; exit 143' TERM; trap 'dd_ci_lease_release; exit 130' INT
dd_ci_lease_acquire "$2"; rc=$?; print -r -- "$rc $DD_CI_LEASE" > "$3.tmp"; mv "$3.tmp" "$3"
[ $rc -eq 0 ] || exit $rc
sleep 60 & wait'''
TR_UTC = {"LANG": "tr_TR.UTF-8", "LC_ALL": "tr_TR.UTF-8", "TZ": "UTC"}


def c_pstart(pid):
    """The fixed format: `env -u TZ LC_ALL=C ps -o lstart= -p <pid>`, spaces squeezed and trimmed."""
    r = subprocess.run(["env", "-u", "TZ", "LC_ALL=C", "ps", "-o", "lstart=", "-p", str(pid)], capture_output=True, text=True)
    return " ".join(r.stdout.split())


def tree(root):
    """Every path under root (symlinks not followed) with its content / link target — for 'deletes nothing'."""
    out = {}
    for dirpath, dirnames, filenames in os.walk(root):
        for name in dirnames + filenames:
            p = Path(dirpath) / name
            if p.is_symlink():
                out[str(p)] = "->" + os.readlink(p)
            elif p.is_file():
                out[str(p)] = p.read_text()
            else:
                out[str(p)] = "<dir>"
    return out


class LeaseFixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="dd-ci-lease-")
        self.root = Path(self.tmp.name).resolve()
        self.a = self.root / "ci-a"; self.a.mkdir()
        self.b = self.root / "ci-b"; self.b.mkdir()
        self.procs = []; self.n = 0

    def tearDown(self):
        for p in self.procs:
            if p.poll() is None:
                p.send_signal(signal.SIGTERM)
                try:
                    p.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    p.kill(); p.wait(timeout=10)
        self.tmp.cleanup()

    def env(self, **extra):
        e = {k: v for k, v in os.environ.items() if not k.startswith("DD_")}
        e.update(extra)
        return e

    def zsh(self, script, *args, **env):
        r = subprocess.run(["zsh", "-c", 'DD_TIMELINE=""; source "$1"; shift; ' + script, "zsh", str(DD_ENV), *args],
                           env=self.env(**env), capture_output=True, text=True, timeout=60)
        return r.returncode, r.stdout, r.stderr

    def acquire_once(self, checkout, **env):
        """One process: acquire, print the lease path, release (only when it got the lease); returns acquire's code."""
        return self.zsh('dd_ci_lease_acquire "$1"; rc=$?; print -r -- "lease=$DD_CI_LEASE"; '
                        '[ $rc -eq 0 ] && { dd_ci_lease_release || exit 99; }; exit $rc', str(checkout), **env)

    def hold(self, checkout, **env):
        self.n += 1
        ready = self.root / f"holder-{self.n}.rc"
        p = subprocess.Popen(["zsh", "-c", HOLDER, "zsh", str(DD_ENV), str(checkout), str(ready)], env=self.env(**env),
                             stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                             start_new_session=True)
        self.procs.append(p)
        end = time.time() + 30
        while time.time() < end and not ready.exists():
            time.sleep(0.05)
        self.assertTrue(ready.exists(), "holder did not report")
        rc, lease = ready.read_text().strip().split(" ", 1)
        return p, int(rc), Path(lease)

    def lease_of(self, checkout):
        rc, out, err = self.zsh('dd_ci_lease_locate "$1" && print -r -- "$DD_CI_LEASE"', str(checkout))
        self.assertEqual(rc, 0, err)
        return Path(out.strip())

    def status(self, checkout, **env):
        r = subprocess.run(["zsh", str(COMPILE_CHECK), "--lease-status", "--ci-dir", str(checkout)], env=self.env(**env),
                           capture_output=True, text=True, timeout=60)
        return r.returncode, r.stdout, r.stderr

    def write_owner(self, lease, pid, start, token="0f0f0f0f-0000-4000-8000-000000000000", name=None):
        lease.mkdir(mode=0o700)
        (lease / (name or f"owner-{token}")).write_text(f"pid={pid}\nstart={start}\ntoken={token}\n")

    def sleeper(self):
        p = subprocess.Popen(["sleep", "60"], stdin=subprocess.DEVNULL, start_new_session=True)
        self.procs.append(p)
        return p


class CiLeaseTests(LeaseFixture):
    # --- acquire: busy / malformed / symlink ---------------------------------------------------------------------------
    def test_Acquire_CanliSahip_Mesgul75LeaseDokunulmaz(self):
        holder, rc, lease = self.hold(self.a)
        self.assertEqual(rc, 0)
        before = tree(self.root)
        rc, out, err = self.acquire_once(self.a)
        self.assertEqual(rc, 75, out + err)
        self.assertIn(f"CI checkout lease busy: {self.a} (owner pid {holder.pid})", err)
        self.assertEqual(tree(self.root), before)

    def test_Acquire_BozukSahipDosyasi_Cikis3AsiriAlinmaz(self):
        lease = self.lease_of(self.a); lease.mkdir(mode=0o700)
        (lease / "owner-x").write_text("garbage\n")
        before = tree(self.root)
        rc, out, err = self.acquire_once(self.a)
        self.assertEqual(rc, 3, out + err)
        self.assertIn("is MALFORMED", err)
        self.assertIn("--lease-status", err)
        self.assertEqual(tree(self.root), before)

    def test_Acquire_SahipDosyasiYok_EdinmeSurerSayilir_Mesgul75(self):
        # mkdir done, owner file not renamed in yet: an acquire in progress → busy, not an error.
        self.lease_of(self.a).mkdir(mode=0o700)
        rc, out, err = self.acquire_once(self.a)
        self.assertEqual(rc, 75, out + err)
        self.assertIn("no owner file yet", err)

    def test_Acquire_SymlinkLease_Cikis3HedefDokunulmaz(self):
        target = self.root / "elsewhere"; target.mkdir(); (target / "keep").write_text("x")
        self.lease_of(self.a).symlink_to(target)
        before = tree(self.root)
        rc, out, err = self.acquire_once(self.a)
        self.assertEqual(rc, 3, out + err)
        self.assertIn("is SYMLINK", err)
        self.assertEqual(tree(self.root), before)

    def test_Acquire_OluSahip_Cikis3AsiriAlinmaz(self):
        p = self.sleeper(); p.kill(); p.wait()
        lease = self.lease_of(self.a); self.write_owner(lease, p.pid, "Mon Jan 1 00:00:00 2001")
        before = tree(self.root)
        rc, out, err = self.acquire_once(self.a)
        self.assertEqual(rc, 3, out + err)
        self.assertIn("is STALE-DEAD-OWNER", err)
        self.assertEqual(tree(self.root), before)

    def test_Release_SahipDosyasiDegismis_Cikis3LeaseKalir(self):
        rc, out, err = self.zsh('dd_ci_lease_acquire "$1" || exit 90; print -r -- other > "$DD_CI_LEASE/owner-$DD_CI_LEASE_TOKEN"; '
                                'dd_ci_lease_release; print "release=$?"', str(self.a))
        self.assertIn("release=3", out, err)
        self.assertTrue(self.lease_of(self.a).is_dir())

    def test_Acquire_SahipKaydiSabitBicimde_CTZsizLstart(self):
        # The acquirer runs as LANG=tr_TR TZ=UTC; its record must still be the C / system-TZ form (read in-process).
        rc, out, err = self.zsh('dd_ci_lease_acquire "$1" || exit 90; cat "$DD_CI_LEASE"/owner-*; '
                                'print -r -- "c=$(env -u TZ LC_ALL=C ps -o lstart= -p $$)"; dd_ci_lease_release', str(self.a), **TR_UTC)
        self.assertEqual(rc, 0, err)
        lines = out.splitlines()
        self.assertEqual(lines[1], "start=" + " ".join(lines[3][2:].split()))
        self.assertRegex(lines[1], r"^start=[A-Z][a-z]{2} [A-Z][a-z]{2} +\d+ \d\d:\d\d:\d\d \d{4}$")
        self.assertEqual(list(self.root.glob(".dd-ci-*.lease")), [])

    # --- concurrency: per canonical checkout ---------------------------------------------------------------------------
    def test_Eszamanlilik_IkiCheckoutAyniAnda_AyniCheckoutAliasDahilReddedilir(self):
        _, rc_a, lease_a = self.hold(self.a)
        _, rc_b, lease_b = self.hold(self.b)
        self.assertEqual((rc_a, rc_b), (0, 0))
        self.assertNotEqual(lease_a, lease_b)
        self.assertTrue(lease_a.is_dir() and lease_b.is_dir())
        alias = self.root / "alias-to-a"; alias.symlink_to(self.a)
        for path in (self.a, alias, Path(str(self.a) + "/")):
            rc, out, err = self.acquire_once(path)
            self.assertEqual(rc, 75, f"{path}: {out}{err}")
            self.assertIn(f"lease={lease_a}", out)
        _, rc_second, _ = self.hold(self.a)
        self.assertEqual(rc_second, 75)

    def test_Eszamanlilik_OnParalelEdinme_TekKazanan(self):
        procs = [subprocess.Popen(["zsh", "-c", HOLDER, "zsh", str(DD_ENV), str(self.a), str(self.root / f"race-{i}.rc")],
                                  env=self.env(), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                  start_new_session=True) for i in range(10)]
        self.procs.extend(procs)
        end = time.time() + 30
        while time.time() < end and len(list(self.root.glob("race-*.rc"))) < 10:
            time.sleep(0.05)
        codes = sorted(int(f.read_text().split()[0]) for f in self.root.glob("race-*.rc"))
        self.assertEqual(codes, [0] + [75] * 9)

    # --- --lease-status: read-only verdicts ----------------------------------------------------------------------------
    def assert_status(self, checkout, code, verdict, **env):
        before = tree(self.root)
        rc, out, err = self.status(checkout, **env)
        self.assertEqual(rc, code, out + err)
        self.assertIn(f"verdict: {verdict} — ", out)
        self.assertIn(f"lease: {self.lease_of(checkout)}", out)
        self.assertEqual(tree(self.root), before, "--lease-status must not change anything")
        return out

    def test_LeaseStatus_Serbest_FREE0(self):
        self.assert_status(self.a, 0, "FREE")
        self.assertEqual(list(self.root.glob(".dd-ci-*")), [])

    def test_LeaseStatus_CanliSahip_HELDLIVE75(self):
        holder, rc, _ = self.hold(self.a)
        out = self.assert_status(self.a, 75, "HELD-LIVE")
        self.assertIn(f"owner pid: {holder.pid}", out)
        self.assertIn(f"recorded start: {c_pstart(holder.pid)}", out)
        self.assertIn(f"current start: {c_pstart(holder.pid)}", out)
        self.assertIn("owner alive: yes", out)
        self.assertIn("same process: yes", out)

    def test_LeaseStatus_OluSahip_STALEDEADOWNER3(self):
        p = self.sleeper(); p.kill(); p.wait()
        self.write_owner(self.lease_of(self.a), p.pid, "Mon Jan 1 00:00:00 2001")
        out = self.assert_status(self.a, 3, "STALE-DEAD-OWNER")
        self.assertIn("owner alive: no", out)

    def test_LeaseStatus_PidBaskaSurecte_STALEPIDREUSED3(self):
        p = self.sleeper()
        self.write_owner(self.lease_of(self.a), p.pid, "Mon Jan 1 00:00:00 2001")
        out = self.assert_status(self.a, 3, "STALE-PID-REUSED")
        self.assertIn("owner alive: yes", out)
        self.assertIn("same process: no", out)
        self.assertIn("recorded start: Mon Jan 1 00:00:00 2001", out)

    def test_LeaseStatus_BozukSahip_MALFORMED3(self):
        lease = self.lease_of(self.a)
        self.write_owner(lease, "notapid", "x")
        self.assert_status(self.a, 3, "MALFORMED")
        for f in lease.iterdir():
            f.unlink()
        (lease / "owner-aaaa").write_text("pid=1\nstart=x\ntoken=bbbb\n")   # token ≠ file name
        self.assert_status(self.a, 3, "MALFORMED")
        (lease / "owner-aaaa").unlink()
        self.assert_status(self.a, 3, "MALFORMED")   # empty lease: no owner file

    def test_LeaseStatus_SymlinkLease_SYMLINK3(self):
        target = self.root / "elsewhere"; target.mkdir()
        self.lease_of(self.a).symlink_to(target)
        self.assert_status(self.a, 3, "SYMLINK")

    def test_LeaseStatus_RefVeyaOutIle_Kullanim64(self):
        r = subprocess.run(["zsh", str(COMPILE_CHECK), "main", "--lease-status", "--ci-dir", str(self.a)], env=self.env(),
                           capture_output=True, text=True, timeout=60)
        self.assertEqual(r.returncode, 64, r.stdout + r.stderr)

    # --- locale / TZ independence ---------------------------------------------------------------------------------------
    def test_TrTRUTCCagiran_CanliSahibiYanlisYargilamaz(self):
        # The owner acquires under another TZ/locale; the caller runs with LANG=tr_TR.UTF-8 TZ=UTC. Plain `ps lstart` would
        # differ on both sides (Turkish day/month names, UTC clock) — the fixed C/TZ-less form does not.
        holder, rc, _ = self.hold(self.a, TZ="Asia/Tokyo", LANG="en_US.UTF-8", LC_ALL="en_US.UTF-8")
        self.assertEqual(rc, 0)
        plain_tr = subprocess.run(["ps", "-o", "lstart=", "-p", str(holder.pid)], env=self.env(**TR_UTC),
                                  capture_output=True, text=True).stdout.strip()
        self.assertNotEqual(" ".join(plain_tr.split()), c_pstart(holder.pid), "fixture: the caller's plain form must differ")
        out = self.assert_status(self.a, 75, "HELD-LIVE", **TR_UTC)
        self.assertIn("same process: yes", out)
        rc, out, err = self.acquire_once(self.a, **TR_UTC)
        self.assertEqual(rc, 75, out + err)   # busy, not "stale" (3)


class CiLeaseScriptTests(CiFixture):
    """main-ci.sh / compile-check.sh call the dd-env.sh lease (fixture of test_ci_signal_traps.py)."""
    def lease_path(self):
        r = subprocess.run(["zsh", "-c", 'DD_TIMELINE=""; source "$1"; dd_ci_lease_locate "$2" && print -r -- "$DD_CI_LEASE"',
                            "zsh", str(DD_ENV), str(self.ci)], capture_output=True, text=True, timeout=60)
        return Path(r.stdout.strip())

    def test_Scriptler_TekKaynak_KopyaLeaseFonksiyonuYok(self):
        for name in ("main-ci.sh", "compile-check.sh"):
            text = (TOOLS / name).read_text()
            self.assertNotIn("ci_lease_acquire()", text)
            self.assertNotIn("mkdir -m 700", text)
            self.assertIn('dd_ci_lease_acquire "$CI_DIR"', text)
            self.assertIn("dd_ci_lease_release", text)

    def test_MainCi_LeaseCanliSahipte_Cikis75HicbirSeyAlinmaz(self):
        self.make_repo()
        holder = subprocess.Popen(["zsh", "-c", HOLDER, "zsh", str(DD_ENV), str(self.ci), str(self.root / "h.rc")],
                                  env=self.env(), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                  stderr=subprocess.DEVNULL, start_new_session=True)
        self.pids.append(holder.pid); self.procs.append(holder)
        end = time.time() + 30
        while time.time() < end and not (self.root / "h.rc").exists():
            time.sleep(0.05)
        rc, out, err = self.finish(self.start([str(self.tools / "main-ci.sh"), self.sha, "--out-root", str(self.out),
                                                "--ci-dir", str(self.ci), "--skip-playmode"], FAKE_MODE="pass"))
        self.assertEqual(rc, 75, out + err)
        self.assertIn("CI checkout lease busy", err)
        self.assertEqual(self.lines("slot-calls.txt"), []); self.assertEqual(self.lines("guard-calls.txt"), [])
        self.assertTrue(self.lease_path().is_dir())   # the holder's lease is not touched
        holder.send_signal(signal.SIGTERM); holder.wait(timeout=10)
        self.assertEqual(self.leases(), [])

    def test_CompileCheck_BozukLease_Cikis3LeaseKalir(self):
        self.make_repo()
        lease = self.lease_path(); lease.mkdir(mode=0o700); (lease / "owner-x").write_text("garbage\n")
        rc, out, err = self.finish(self.start([str(self.tools / "compile-check.sh"), self.sha, "--ci-dir", str(self.ci),
                                                "--out", str(self.out)], FAKE_UNITY_MODE="pass"))
        self.assertEqual(rc, 3, out + err)
        self.assertIn("is MALFORMED", err)
        self.assertEqual(self.lines("slot-calls.txt"), [])
        self.assertEqual((lease / "owner-x").read_text(), "garbage\n")

    def assert_main_ci_signal_releases(self, sig, code):
        p = self.main_ci()
        self.wait_file(self.log / "child-editmode.pid"); time.sleep(0.3)
        held = self.leases()
        self.assertEqual(len(held), 1)
        owner = list(held[0].glob("owner-*"))
        self.assertEqual(len(owner), 1)
        self.assertIn(f"pid={p.pid}\n", owner[0].read_text())
        os.kill(p.pid, sig)
        rc, out, err = self.finish(p)
        self.assertEqual(rc, code, out + err)
        self.assertFalse(held[0].exists())

    def test_MainCi_SIGINT_LeaseDiziniKalkar(self):
        self.assert_main_ci_signal_releases(signal.SIGINT, 130)

    def test_MainCi_SIGTERM_LeaseDiziniKalkar(self):
        self.assert_main_ci_signal_releases(signal.SIGTERM, 143)

    def test_CompileCheck_SIGTERM_LeaseDiziniKalkar(self):
        p = self.compile_check("sleep")
        self.wait_file(self.log / "unity.pid"); time.sleep(0.3)
        held = self.leases(); self.assertEqual(len(held), 1)
        os.kill(p.pid, signal.SIGTERM)
        rc, out, err = self.finish(p)
        self.assertEqual(rc, 143, out + err)
        self.assertFalse(held[0].exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
