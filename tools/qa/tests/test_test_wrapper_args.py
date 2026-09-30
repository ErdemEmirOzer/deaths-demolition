"""Execute the real test wrapper against isolated environment/Unity stand-ins."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[3]
SCRIPT = REPO / 'tools/test.sh'
BASE = '877a9aefb0bc413744b83d47f21efa63de7386fe'
# H12 (C-00 01.10): the S02_R2 observer tokens once passed through --extra-arg; now every form of it is refused
RETIRED = ['-ddGameFeelGateDiag', '-ddDec082Diag', '-ddGateDiagSourceCommit', 'a' * 40, '-ddGateDiagInputHash', 'b' * 64,
           '-ddGateDiagExecutionId', 'offline-fixture_20260929.01', '-ddGateDiagSourceHash', 'c' * 64,
           '-ddDecDiagSourceHash', 'e' * 64]


class TestWrapperArgContract(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='dd-test-argv-')
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.capture = self.root / 'argv.json'
        self.trace = self.root / 'trace.txt'
        self.out = self.root / 'output with spaces'
        self.marker = self.root / 'MUST-NOT-EXIST'
        unity = self.root / 'fake-unity.py'
        unity.write_text('''#!/usr/bin/env python3
import json, os, pathlib, sys
pathlib.Path(os.environ['ARGV_CAPTURE']).write_text(json.dumps(sys.argv, ensure_ascii=False))
args=sys.argv[1:]
xml=pathlib.Path(args[args.index('-testResults')+1])
log=pathlib.Path(args[args.index('-logFile')+1])
xml.write_text('<test-run total="1" passed="1" failed="0"><test-case name="Fixture" result="Passed"/></test-run>')
log.write_text('fake Unity, no runtime or compiler invocation\\n')
''')
        unity.chmod(0o700)
        fake_bin = self.root / 'fake-bin'
        fake_bin.mkdir()
        git = fake_bin / 'git'
        git.write_text('#!/bin/zsh\nprint -r -- fixture-read-only-head\n')
        git.chmod(0o700)
        self.env = dict(os.environ, DD_UNITY=str(unity), DD_PROJECT=str(self.root / 'project Ü with spaces'),
                        DD_REPO=str(self.root / 'fixture-repo'), DD_BUILD_ROOT=str(self.out),
                        ARGV_CAPTURE=str(self.capture), ARGV_TRACE=str(self.trace),
                        PATH=str(fake_bin)+os.pathsep+os.environ['PATH'], PYTHONDONTWRITEBYTECODE='1')
        self.original = subprocess.check_output(['git', 'show', BASE+':tools/test.sh'], cwd=REPO)
        for label, source in (('original', self.original), ('candidate', SCRIPT.read_bytes())):
            folder = self.root / label
            folder.mkdir()
            (folder / 'test.sh').write_bytes(source)
            (folder / 'dd-env.sh').write_text('''DD_TOOLS_DIR="${0:A:h}"
DD_MAX_UNITY=2
dd_utc() { print fixture-time; }
dd_log() { print -r -- "$*"; }
dd_unity_count() { print 0; }
dd_slot_acquire() { print acquired >> "$ARGV_TRACE"; }
dd_disk_gate() { return 0; }   # C-00 30.09 dd_disk_gate (dd26c3e6): the fixture's dd-env provides every function test.sh calls
dd_slot_release() { print released >> "$ARGV_TRACE"; }
dd_unity_watched() { DD_UNITY_OUTCOME=ok; shift 2; "$@"; }
''')
            (folder / 'test-durations.py').write_text('print("fixture duration parser")\n')

    def run_wrapper(self, args, original=False, expected=0):
        for path in (self.capture, self.trace):
            path.unlink(missing_ok=True)
        if self.out.exists():
            shutil.rmtree(self.out)
        folder = 'original' if original else 'candidate'
        result = subprocess.run(['zsh', str(self.root / folder / 'test.sh'), *args],
                                env=self.env, capture_output=True, text=True)
        self.assertEqual(expected, result.returncode, result.stdout+result.stderr)
        argv = json.loads(self.capture.read_text()) if self.capture.exists() else None
        if expected == 64:
            self.assertIsNone(argv, 'invalid arguments must stop before fake Unity')
            self.assertFalse(self.trace.exists(), 'invalid arguments must stop before slot acquisition')
            self.assertFalse(self.out.exists(), 'invalid arguments must stop before output creation')
        return argv

    def base_args(self, mode='playmode'):
        return [mode, '--out', str(self.out), '--timeout-min', '2', '--no-wait']

    def test_disabled_editmode_and_playmode_argv_match_frozen_baseline_bytes(self):
        for mode in ('editmode', 'playmode'):
            with self.subTest(mode=mode):
                args = self.base_args(mode)
                before = self.run_wrapper(args, original=True)
                after = self.run_wrapper(args)
                self.assertEqual([s.encode('utf-8') for s in before], [s.encode('utf-8') for s in after])
                self.assertFalse(any(s.startswith('-dd') for s in after))

    def test_baseline_filter_literal_metacharacters_spaces_and_unicode_are_preserved(self):
        literal = 'Suite "quoted" Ü; $(touch '+str(self.marker)+') `touch '+str(self.marker)+'`'
        args = self.base_args()+['--filter', literal]
        before = self.run_wrapper(args, original=True)
        after = self.run_wrapper(args)
        self.assertEqual(before, after)
        self.assertEqual(literal, after[after.index('-testFilter')+1])
        self.assertFalse(self.marker.exists())

    def test_extra_arg_is_retired_in_every_form(self):
        for mode in ('editmode', 'playmode'):
            for token in (*RETIRED, '-ddPilotLodBias', '-executeMethod', '', '$(touch '+str(self.marker)+')'):
                for form in (['--extra-arg', token], ['--extra-arg='+token]):
                    with self.subTest(mode=mode, form=form):
                        self.run_wrapper(self.base_args(mode)+form, expected=64)
        self.assertFalse(self.marker.exists())

    def test_extra_arg_refusal_names_the_retirement(self):
        result = subprocess.run(['zsh', str(self.root / 'candidate' / 'test.sh'), 'playmode', '--extra-arg', '-ddGameFeelGateDiag'],
                                env=self.env, capture_output=True, text=True)
        self.assertEqual(64, result.returncode)
        self.assertIn('retired (H12', result.stderr)

    def test_wrapper_options_missing_values_fail_before_fake_slot_or_output(self):
        for option in ('--filter', '--out', '--timeout-min', '--extra-arg'):   # --extra-arg: refused, value or not
            with self.subTest(option=option):
                self.run_wrapper(['playmode', option], expected=64)

    def test_environment_extra_arguments_cannot_enable_observers(self):
        self.env.update(DD_TEST_EXTRA=' '.join(RETIRED), DD_BOT_EXTRA='-ddGameFeelGateDiag',
                        DD_GAMEFEEL_GATE_DIAG='1', DD_DEC082_DIAG='1')
        argv = self.run_wrapper(self.base_args())
        self.assertFalse(any(s.startswith('-dd') for s in argv))


if __name__ == '__main__':
    unittest.main()
