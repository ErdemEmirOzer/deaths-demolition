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
COMMON = {'-ddGateDiagSourceCommit': 'a' * 40,
          '-ddGateDiagInputHash': 'b' * 64,
          '-ddGateDiagExecutionId': 'offline-fixture_20260929.01'}
GAME = {'-ddGateDiagSourceHash': 'c' * 64, '-ddGateDiagFixtureHash': 'd' * 64}
DEC = {'-ddDecDiagSourceHash': 'e' * 64, '-ddDecDiagFixtureHash': 'f' * 64}


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

    def tokens(self, game=True, dec=False):
        tokens = []
        if game:
            tokens.append('-ddGameFeelGateDiag')
        if dec:
            tokens.append('-ddDec082Diag')
        values = dict(COMMON)
        if game:
            values.update(GAME)
        if dec:
            values.update(DEC)
        for flag, value in values.items():
            tokens.extend((flag, value))
        return tokens

    @staticmethod
    def extra(tokens):
        return [part for token in tokens for part in ('--extra-arg', token)]

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

    def test_each_observer_and_combined_tokens_preserve_order_and_bytes(self):
        baseline = self.run_wrapper(self.base_args(), original=True)
        for game, dec in ((True, False), (False, True), (True, True)):
            with self.subTest(game=game, dec=dec):
                tokens = self.tokens(game, dec)
                argv = self.run_wrapper(self.base_args()+self.extra(tokens))
                self.assertEqual(baseline, argv[:-len(tokens)])
                self.assertEqual(tokens, argv[-len(tokens):])

    def test_equals_syntax_and_values_before_switch_keep_token_order(self):
        tokens = self.tokens()
        tokens = tokens[1:]+tokens[:1]
        argv = self.run_wrapper(self.base_args()+['--extra-arg='+token for token in tokens])
        self.assertEqual(tokens, argv[-len(tokens):])

    def test_every_required_identity_is_mandatory_for_its_observer(self):
        for game, dec in ((True, False), (False, True), (True, True)):
            tokens = self.tokens(game, dec)
            for flag in (*COMMON, *(GAME if game else ()), *(DEC if dec else ())):
                with self.subTest(game=game, dec=dec, missing=flag):
                    i = tokens.index(flag)
                    self.run_wrapper(self.base_args()+self.extra(tokens[:i]+tokens[i+2:]), expected=64)

    def test_invalid_commit_hash_and_execution_identity_stop_before_launch(self):
        variants = {
            '-ddGateDiagSourceCommit': ('', 'UNKNOWN', 'a'*39, 'A'*40, '0'*40, 'a'*41),
            '-ddGateDiagInputHash': ('', 'UNKNOWN', 'b'*63, 'B'*64, '0'*64, 'b'*65),
            '-ddGateDiagExecutionId': ('', 'UNKNOWN', 'unknown', 'missing', 'null', 'bad id', '-flag', 'x'*129,
                                       'x\nnew-line', '$(touch '+str(self.marker)+')'),
        }
        for flag, values in variants.items():
            for value in values:
                with self.subTest(flag=flag, value=value):
                    tokens = self.tokens()
                    tokens[tokens.index(flag)+1] = value
                    self.run_wrapper(self.base_args()+self.extra(tokens), expected=64)
        self.assertFalse(self.marker.exists())

    def test_all_observer_source_and_fixture_hashes_reject_wrong_shape(self):
        for flag in (*GAME, *DEC):
            for wrong in ('', 'unknown', 'a'*63, '0'*64, 'G'*64):
                with self.subTest(flag=flag, wrong=wrong):
                    tokens = self.tokens(True, True)
                    tokens[tokens.index(flag)+1] = wrong
                    self.run_wrapper(self.base_args()+self.extra(tokens), expected=64)

    def test_duplicate_boolean_and_identity_tokens_are_rejected(self):
        tokens = self.tokens(True, True)
        for flag in ('-ddGameFeelGateDiag', '-ddDec082Diag', *COMMON, *GAME, *DEC):
            with self.subTest(flag=flag):
                addition = [flag] if flag.endswith('Diag') else [flag, tokens[tokens.index(flag)+1]]
                self.run_wrapper(self.base_args()+self.extra(tokens+addition), expected=64)

    def test_unknown_native_arguments_and_shell_strings_cannot_be_passed_or_executed(self):
        for token in ('-projectPath', '-executeMethod', '-quit', '-ddPilotLodBias', '-ddQaBotInvulnerable',
                      '-ddDecDiagExecutionId', '', '--', '$(touch '+str(self.marker)+')',
                      '`touch '+str(self.marker)+'`'):
            with self.subTest(token=token):
                self.run_wrapper(self.base_args()+self.extra(self.tokens()+[token]), expected=64)
        self.assertFalse(self.marker.exists())

    def test_missing_native_value_and_unbound_identity_are_rejected(self):
        for tokens in (self.tokens()+['-ddDecDiagSourceHash'], self.tokens()[1:],
                       ['-ddGameFeelGateDiag'], ['-ddDec082Diag'], ['-ddGateDiagInputHash'],
                       ['-ddGateDiagInputHash', '-ddGameFeelGateDiag']):
            with self.subTest(tokens=tokens):
                self.run_wrapper(self.base_args()+self.extra(tokens), expected=64)

    def test_identity_for_disabled_observer_is_rejected(self):
        for game, dec, extra in ((True, False, DEC), (False, True, GAME)):
            tokens = self.tokens(game, dec)
            for flag, value in extra.items():
                with self.subTest(flag=flag):
                    self.run_wrapper(self.base_args()+self.extra(tokens+[flag, value]), expected=64)

    def test_diagnostics_are_playmode_only(self):
        self.run_wrapper(self.base_args('editmode')+self.extra(self.tokens(True, True)), expected=64)

    def test_wrapper_options_missing_values_fail_before_fake_slot_or_output(self):
        for option in ('--filter', '--out', '--timeout-min', '--extra-arg'):
            with self.subTest(option=option):
                self.run_wrapper(['playmode', option], expected=64)

    def test_environment_extra_arguments_cannot_enable_observers(self):
        self.env.update(DD_TEST_EXTRA=' '.join(self.tokens(True, True)), DD_BOT_EXTRA='-ddGameFeelGateDiag',
                        DD_GAMEFEEL_GATE_DIAG='1', DD_DEC082_DIAG='1')
        argv = self.run_wrapper(self.base_args())
        self.assertFalse(any(s.startswith('-dd') for s in argv))


if __name__ == '__main__':
    unittest.main()
