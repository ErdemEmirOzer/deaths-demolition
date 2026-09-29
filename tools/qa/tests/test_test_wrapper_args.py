"""Execute the real test wrapper against isolated environment/Unity stand-ins."""
import json
import errno
import hashlib
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
S06_ORIGIN = '0de570932852021a17b9b063d7bae9cd3b3b03c1'
S06_SOURCE = 'prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Tests/PlayMode/AnimationTests.cs'
S06_FILE_HASH = 'f332e089720218a6b655da828c1ce041cdd34d4f200953bd923d51a131c183fe'
S06_METADATA_ORIGIN = '8ebabd81288f38b82595a1cd0220898b6029a4d6'
S06_METADATA_HASH = '34a2227ae7f801b7bb594425a265b8f27a6b5b01a189eef5d54162f2850240a6'
S06_ACTION = 'DD.Art.Tests.PlayMode.AnimationTests.S06_LegacyAr1_ImportedSurface_OnePostHumanoidFrame'
EXECUTION = '7' * 40
S06_OPTIONS = ('--s06-source-sha256', '--s06-input-json', '--s06-input-sha256', '--s06-execution-id', '--s06-action-id')


class TestWrapperArgContract(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='dd-test-argv-')
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name).resolve()
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
        git.write_text("""#!/usr/bin/env python3
import os, pathlib, sys
args=sys.argv[1:]
if args == ['-C', os.environ['DD_REPO'], 'show', os.environ['S06_ORIGIN']+':'+os.environ['S06_SOURCE']]:
    if os.environ.get('S06_GIT_ERROR'): sys.exit(128)
    sys.stdout.buffer.write(pathlib.Path(os.environ['S06_GIT_BLOB']).read_bytes())
elif args == ['-C', os.environ['DD_REPO'], 'rev-parse', 'HEAD']:
    if os.environ.get('S06_GIT_ERROR'): sys.exit(128)
    print(os.environ['S06_EXECUTION_HEAD'])
elif args == ['-C', os.environ['DD_REPO'], 'rev-parse', '--short=12', 'HEAD']:
    print('fixture-read-only-head')
else: sys.exit(128)
""")
        git.chmod(0o700)
        self.env = dict(os.environ, DD_UNITY=str(unity), DD_PROJECT=str(self.root / 'project Ü with spaces'),
                        DD_REPO=str(self.root / 'fixture-repo'), DD_BUILD_ROOT=str(self.out),
                        ARGV_CAPTURE=str(self.capture), ARGV_TRACE=str(self.trace),
                        PATH=str(fake_bin)+os.pathsep+os.environ['PATH'], PYTHONDONTWRITEBYTECODE='1')
        self.original = subprocess.check_output(['git', 'show', BASE+':tools/test.sh'], cwd=REPO)
        consumer = subprocess.check_output(['git', 'show', S06_ORIGIN+':'+S06_SOURCE], cwd=REPO)
        self.assertEqual(S06_FILE_HASH, hashlib.sha256(consumer).hexdigest())
        self.source = Path(self.env['DD_PROJECT']) / 'Assets/_Art/Tests/PlayMode/AnimationTests.cs'
        self.source.parent.mkdir(parents=True)
        self.source.write_bytes(consumer)
        self.blob = self.root / 'origin-consumer.cs'
        self.blob.write_bytes(consumer)
        self.package = self.root / 'canonical package Ü.json'
        self.package.write_text(json.dumps({'schema':'AP022.sha-bound-anatomical-source-package.v001',
            'package':'AP-022-ANATOMICAL-SOURCE-BIND', 'source_identity':{},
            'source_anatomical_cp_mask':[1, 2, 3], 'ordered_source_triangle_anchor':{}}))
        self.env.update(S06_ORIGIN=S06_ORIGIN, S06_SOURCE=S06_SOURCE, S06_GIT_BLOB=str(self.blob),
                        S06_EXECUTION_HEAD=EXECUTION)
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
dd_slot_release() { print released >> "$ARGV_TRACE"; }
dd_unity_watched() { DD_UNITY_OUTCOME=ok; shift 2; "$@"; }
''')
            (folder / 'test-durations.py').write_text('print("fixture duration parser")\n')

    def run_wrapper(self, args, original=False, expected=0, preserve=False):
        for path in (self.capture, self.trace):
            path.unlink(missing_ok=True)
        if self.out.exists() and not preserve:
            shutil.rmtree(self.out)
        snapshot = self.tree_snapshot() if preserve else None
        folder = 'original' if original else 'candidate'
        result = subprocess.run(['zsh', str(self.root / folder / 'test.sh'), *args],
                                env=self.env, capture_output=True, text=True)
        self.assertEqual(expected, result.returncode, result.stdout+result.stderr)
        argv = json.loads(self.capture.read_text()) if self.capture.exists() else None
        if expected != 0:
            self.assertIsNone(argv, 'preflight failure must stop before fake Unity')
            self.assertFalse(self.trace.exists(), 'preflight failure must stop before slot acquisition')
        if expected == 64:
            if preserve:
                self.assertEqual(snapshot, self.tree_snapshot(), 'preflight rejection must preserve all fixture bytes/paths')
            else:
                self.assertFalse(self.out.exists(), 'invalid arguments must stop before output creation')
        return argv

    def tree_snapshot(self):
        return {str(p.relative_to(self.root)): ('symlink', str(p.readlink())) if p.is_symlink()
                else ('file', p.read_bytes()) if p.is_file() else ('directory', None)
                for p in self.root.rglob('*') if p not in (self.capture, self.trace)}

    def s06_args(self, palm=None, table=None, metadata=None, selected_out=None):
        outputs = [str(palm if palm is not None else self.out / 'palm Ü.json'),
                   str(table if table is not None else self.out / 'same-frame.json')]
        options = dict(zip(S06_OPTIONS, (S06_FILE_HASH, str(self.package),
            hashlib.sha256(self.package.read_bytes()).hexdigest(), 's06-offline.01', S06_ACTION)))
        options.update(metadata or {})
        native = ['-ddS06PalmProof', outputs[0], '-ddS06PalmSourceSha', S06_ORIGIN,
                  '-ddS06CorrespondenceProof', outputs[1]]
        base = self.base_args()
        if selected_out is not None:
            base[base.index('--out')+1] = str(selected_out)
        return base+['--filter', S06_ACTION]+self.extra(native)+[s for pair in options.items() for s in pair]

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

    def test_s06_exact_three_native_tokens_and_prerequisite_receipt(self):
        argv = self.run_wrapper(self.s06_args())
        expected = ['-ddS06PalmProof', str(self.out/'palm Ü.json'), '-ddS06PalmSourceSha', S06_ORIGIN,
                    '-ddS06CorrespondenceProof', str(self.out/'same-frame.json')]
        self.assertEqual(expected, argv[-6:])
        self.assertFalse(any(s.startswith('--s06-') for s in argv))
        receipt = json.loads((self.out/'s06-argv-preflight.json').read_text())
        self.assertEqual(S06_ORIGIN, receipt['observerSourceCommit'])
        self.assertEqual(EXECUTION, receipt['executionCommit'])
        self.assertNotEqual(receipt['observerSourceCommit'], receipt['executionCommit'])
        self.assertEqual(S06_FILE_HASH, receipt['observerFileSha256'])
        self.assertEqual('s06.same-frame-imported-baked-arrays.v1', receipt['observerCapability'])
        observed=self.out.stat()
        self.assertEqual({'device':observed.st_dev,'inode':observed.st_ino}, receipt['outputDirectoryIdentity'])
        self.assertFalse(list(self.out.glob('.s06-argv-preflight-*')), 'successful publication removes temporary file')
        self.assertEqual(hashlib.sha256(self.package.read_bytes()).hexdigest(), receipt['p06InputSha256'])
        self.assertIs(False, receipt['p06RuntimeConsumed'])
        self.assertFalse((self.out/'palm Ü.json').exists(), 'stand-in is not native proof generation')

    def use_reviewed_metadata_consumer(self):
        content = subprocess.check_output(['git', 'show', S06_METADATA_ORIGIN+':'+S06_SOURCE], cwd=REPO)
        self.assertEqual(S06_METADATA_HASH, hashlib.sha256(content).hexdigest())
        self.source.write_bytes(content)
        self.blob.write_bytes(content)
        self.env['S06_ORIGIN'] = S06_METADATA_ORIGIN
        args = self.s06_args(metadata={'--s06-source-sha256': S06_METADATA_HASH})
        args[args.index(S06_ORIGIN)] = S06_METADATA_ORIGIN
        return args, content

    def test_s06_reviewed_loaded_metadata_consumer_exact_bytes_supported(self):
        args, _ = self.use_reviewed_metadata_consumer()
        argv = self.run_wrapper(args)
        self.assertEqual(['-ddS06PalmProof', str(self.out/'palm Ü.json'),
                          '-ddS06PalmSourceSha', S06_METADATA_ORIGIN,
                          '-ddS06CorrespondenceProof', str(self.out/'same-frame.json')], argv[-6:])
        self.assertFalse(any(s.startswith('--s06-') for s in argv))
        receipt = json.loads((self.out/'s06-argv-preflight.json').read_text())
        self.assertEqual(S06_METADATA_HASH, receipt['observerFileSha256'])
        self.assertEqual(S06_METADATA_ORIGIN, receipt['observerSourceCommit'])
        self.assertEqual(EXECUTION, receipt['executionCommit'])
        self.assertEqual('s06.same-frame-imported-baked-arrays.v1', receipt['observerCapability'])
        self.assertIs(False, receipt['p06RuntimeConsumed'])
        self.assertFalse((self.out/'palm Ü.json').exists())
        self.assertFalse((self.out/'same-frame.json').exists())

    def test_s06_loaded_metadata_consumer_one_bit_revision_still_rejected(self):
        args, content = self.use_reviewed_metadata_consumer()
        mutant = bytes([content[0] ^ 1]) + content[1:]
        self.source.write_bytes(mutant)
        self.blob.write_bytes(mutant)
        args[args.index('--s06-source-sha256')+1] = hashlib.sha256(mutant).hexdigest()
        self.run_wrapper(args, expected=64, preserve=True)
        self.assertFalse(self.out.exists())

    def test_s06_loaded_metadata_local_and_origin_bytes_both_required(self):
        args, content = self.use_reviewed_metadata_consumer()
        mutant = bytes([content[0] ^ 1]) + content[1:]
        for path in (self.source, self.blob):
            with self.subTest(path=path.name):
                path.write_bytes(mutant)
                self.run_wrapper(args, expected=64, preserve=True)
                path.write_bytes(content)

    def test_s06_default_computed_out_and_token_reordering(self):
        self.env['DD_BUILD_ROOT'] = str(self.root/'builds')
        default = Path(self.env['DD_BUILD_ROOT'])/'tests-playmode-fixture-time'
        args = self.s06_args(palm=default/'palm.json', table=default/'table.json')
        i = args.index('--out'); del args[i:i+2]
        native_start = args.index('--extra-arg')
        native = args[native_start:native_start+12]
        args[native_start:native_start+12] = native[8:]+native[:8]
        argv = self.run_wrapper(args)
        self.assertEqual(str(default/'playmode.xml'), argv[argv.index('-testResults')+1])
        self.assertEqual(str(default), json.loads((default/'s06-argv-preflight.json').read_text())['selectedOut'])

    def test_s06_all_metadata_and_native_fields_required_and_duplicate_rejected(self):
        args = self.s06_args()
        for flag in S06_OPTIONS:
            with self.subTest(missing=flag):
                i = args.index(flag)
                self.run_wrapper(args[:i]+args[i+2:], expected=64)
            with self.subTest(duplicate=flag):
                self.run_wrapper(args+[flag,args[args.index(flag)+1]], expected=64)
            with self.subTest(duplicate_empty_first=flag):
                self.run_wrapper(args[:1]+[flag,'']+args[1:], expected=64)
        for flag in ('-ddS06PalmProof','-ddS06PalmSourceSha','-ddS06CorrespondenceProof'):
            with self.subTest(missing=flag):
                i = args.index(flag)-1
                self.run_wrapper(args[:i]+args[i+4:], expected=64)
            with self.subTest(duplicate=flag):
                self.run_wrapper(args+self.extra([flag,args[args.index(flag)+2]]), expected=64)
        for flag in S06_OPTIONS:
            self.run_wrapper(['playmode',flag], expected=64)

    def test_s06_orphan_mixed_and_invented_native_arguments_rejected(self):
        self.run_wrapper(self.base_args()+['--s06-execution-id','orphan'], expected=64)
        self.run_wrapper(self.s06_args()+self.extra(self.tokens()), expected=64)
        self.run_wrapper(self.s06_args()+self.extra(self.tokens(False,True)), expected=64)
        self.run_wrapper(self.s06_args()+self.extra(['-ddGateDiagInputHash','b'*64]), expected=64)
        for flag in ('-ddS06InputJson','-ddS06InputHash','-ddS06ExecutionId','-ddS06ActionId'):
            with self.subTest(flag=flag):
                self.run_wrapper(self.s06_args()+self.extra([flag,'invented']), expected=64)
        args=self.s06_args();args[0]='editmode';self.run_wrapper(args,expected=64)

    def test_s06_action_filter_and_execution_identity_are_bounded(self):
        for value in ('', 'UNKNOWN', 'none', 'bad id', 'x'*129, 'x\ny', '$(touch '+str(self.marker)+')'):
            with self.subTest(execution=value):
                self.run_wrapper(self.s06_args(metadata={'--s06-execution-id':value}), expected=64)
        for value in ('', 'Other.Test', S06_ACTION+';Other.Test'):
            with self.subTest(action=value):
                self.run_wrapper(self.s06_args(metadata={'--s06-action-id':value}), expected=64)
            with self.subTest(filter=value):
                args=self.s06_args();args[args.index('--filter')+1]=value
                self.run_wrapper(args,expected=64)
        args=self.s06_args();i=args.index('--filter');del args[i:i+2]
        self.run_wrapper(args,expected=64)
        self.assertFalse(self.marker.exists())

    def test_s06_actual_source_and_commit_blob_are_both_hash_bound(self):
        for path in (self.source,self.blob):
            original=path.read_bytes()
            try:
                path.write_bytes(original+b'// modified source\n')
                self.run_wrapper(self.s06_args(),expected=64,preserve=True)
            finally:path.write_bytes(original)
        for option in ('--s06-source-sha256','--s06-input-sha256'):
            for value in ('','0'*64,'A'*64,'a'*63,'b'*64):
                with self.subTest(option=option,value=value):
                    self.run_wrapper(self.s06_args(metadata={option:value}),expected=64)
        args=self.s06_args();args[args.index(S06_ORIGIN)]='a'*40
        self.run_wrapper(args,expected=64)
        original=self.source.read_bytes();self.source.unlink()
        self.run_wrapper(self.s06_args(),expected=64,preserve=True)
        self.source.symlink_to(self.blob)
        self.run_wrapper(self.s06_args(),expected=64,preserve=True)
        self.source.unlink();self.source.write_bytes(original)

    def test_s06_git_failures_and_unknown_execution_head_fail_closed(self):
        self.env['S06_GIT_ERROR']='1'
        self.run_wrapper(self.s06_args(),expected=64)
        del self.env['S06_GIT_ERROR']
        for value in ('','UNKNOWN','7'*39,'A'*40,'0'*40,'7'*41,'7'*40+'\nother'):
            with self.subTest(head=value):
                self.env['S06_EXECUTION_HEAD']=value
                self.run_wrapper(self.s06_args(),expected=64)

    def test_s06_p06_canonical_regular_input_and_hash_required(self):
        link=self.root/'input-link.json';link.symlink_to(self.package)
        for path in (self.root/'absent.json',self.package.parent,link,Path('relative.json')):
            with self.subTest(input=path):
                self.run_wrapper(self.s06_args(metadata={'--s06-input-json':str(path)}),expected=64,preserve=True)
        original=self.package.read_bytes()
        args=self.s06_args();self.package.write_bytes(original+b' ')
        self.run_wrapper(args,expected=64,preserve=True)

    def test_s06_p06_typed_json_rejects_duplicates_and_all_nonfinite_forms(self):
        valid=self.package.read_text();package=json.loads(valid)
        malformed=['{', 'null','[]',valid.replace('"source_identity": {}','"source_identity": [], "source_identity": {}')]
        for key,wrong in (('schema','bad'),('package','bad'),('source_identity',[]),
                          ('source_anatomical_cp_mask',{}),('ordered_source_triangle_anchor',[])):
            item=dict(package);item[key]=wrong;malformed.append(json.dumps(item))
        for number in ('NaN','Infinity','-Infinity','1e999','-1e999'):
            malformed.append(valid[:-1]+',"nested":{"value":'+number+'}}')
        for raw in malformed:
            with self.subTest(raw=raw):
                self.package.write_text(raw)
                self.run_wrapper(self.s06_args(),expected=64,preserve=True)

    def test_s06_output_escape_relative_dot_control_and_alias_paths_rejected(self):
        pairs=[('relative.json',self.out/'table.json'), (self.root/'outside.json',self.out/'table.json'),
               (str(self.out)+'/../escape.json',self.out/'table.json'),
               (str(self.out)+'/./palm.json',self.out/'table.json'),
               (str(self.out)+'/line\nbreak.json',self.out/'table.json'),
               (self.out/'same.json',self.out/'same.json'),
               (self.out/'PALM.json',self.out/'palm.json'),
               (self.out/'pálm.json',self.out/'pálm.json'),
               (self.out/'palm.json',self.out/'palm.json'/'table.json')]
        for palm,table in pairs:
            with self.subTest(palm=palm,table=table):
                self.run_wrapper(self.s06_args(palm=palm,table=table),expected=64,preserve=True)

    def test_s06_existing_output_bytes_and_nonregular_ancestors_preserved(self):
        self.out.mkdir()
        sentinel=self.out/'existing.json';sentinel.write_bytes(b'original proof\x00bytes')
        self.run_wrapper(self.s06_args(palm=sentinel),expected=64,preserve=True)
        self.assertEqual(b'original proof\x00bytes',sentinel.read_bytes())
        sentinel.unlink()
        link=self.out/'link.json';link.symlink_to(self.root/'missing.json')
        self.run_wrapper(self.s06_args(palm=link),expected=64,preserve=True)
        link.unlink()
        directory=self.out/'directory.json';directory.mkdir()
        self.run_wrapper(self.s06_args(palm=directory),expected=64,preserve=True)
        directory.rmdir()
        self.out.rmdir()
        parent=self.root/'file-parent';parent.write_bytes(b'unchanged')
        self.run_wrapper(self.s06_args(selected_out=parent/'output',palm=parent/'output/palm.json',
                        table=parent/'output/table.json'),expected=64,preserve=True)
        target=self.root/'real';target.mkdir()
        link=self.root/'linked';link.symlink_to(target, target_is_directory=True)
        self.run_wrapper(self.s06_args(selected_out=link/'output',palm=link/'output/palm.json',
                        table=link/'output/table.json'),expected=64,preserve=True)
        dangling=self.root/'dangling';dangling.symlink_to(self.root/'nonexistent')
        self.run_wrapper(self.s06_args(selected_out=dangling/'output',palm=dangling/'output/palm.json',
                        table=dangling/'output/table.json'),expected=64,preserve=True)

    def test_s06_wrapper_artifacts_and_user_profile_are_never_proof_targets(self):
        for name in ('s06-argv-preflight.json','S06-ARGV-PREFLIGHT.JSON','durations.json',
                     'playmode.xml/nested.json','timeline.txt/nested.json',
                     'prototype-profile.json','ui-settings.json','input-overrides.json'):
            with self.subTest(name=name):
                self.run_wrapper(self.s06_args(palm=self.out/name),expected=64,preserve=True)
        self.env['DD_GUARD_ROOT']=str(self.root/'guard')
        for root in (Path.home()/'Library/Preferences/S06',Path.home()/'Library/Application Support/S06',
                     self.root/'guard/AS/S06',self.root/'guard/PR/S06'):
            with self.subTest(protected=root):
                self.run_wrapper(self.s06_args(selected_out=root,palm=root/'palm.json',table=root/'table.json'),
                                 expected=64,preserve=True)

    def test_s06_environment_metadata_cannot_enable_diagnostic(self):
        self.env.update(DD_S06_PALM_PROOF=str(self.out/'palm.json'), DD_S06_INPUT_JSON=str(self.package),
                        DD_TEST_EXTRA='-ddS06PalmProof '+str(self.out/'palm.json'))
        argv=self.run_wrapper(self.base_args())
        self.assertFalse(any(token.startswith('-ddS06') for token in argv))
        self.assertFalse((self.out/'s06-argv-preflight.json').exists())

    def test_s06_authentic_legacy_consumer_rejects_unconsumed_third_flag(self):
        legacy = subprocess.check_output(['git', 'show', BASE+':'+S06_SOURCE], cwd=REPO)
        legacy_hash = hashlib.sha256(legacy).hexdigest()
        self.assertNotEqual(S06_FILE_HASH, legacy_hash)
        self.source.write_bytes(legacy); self.blob.write_bytes(legacy)
        self.env['S06_ORIGIN'] = BASE
        args = self.s06_args(metadata={'--s06-source-sha256':legacy_hash})
        args[args.index(S06_ORIGIN)] = BASE
        self.run_wrapper(args, expected=64, preserve=True)

    def test_s06_authentic_arbitrary_or_commented_capability_is_not_reviewed(self):
        current=self.source.read_bytes()
        for content in (b'// -ddS06PalmProof -ddS06PalmSourceSha -ddS06CorrespondenceProof',
                        current+b'// unreviewed revision\n'):
            with self.subTest(hash=hashlib.sha256(content).hexdigest()):
                self.source.write_bytes(content);self.blob.write_bytes(content)
                self.run_wrapper(self.s06_args(metadata={'--s06-source-sha256':hashlib.sha256(content).hexdigest()}),
                                 expected=64, preserve=True)

    def test_s06_output_mkdir_failure_preserves_exit_and_never_launches(self):
        shim=self.root/'fake-bin/mkdir'
        shim.write_text('#!/bin/zsh\nexit 17\n');shim.chmod(0o700)
        self.run_wrapper(self.s06_args(),expected=17,preserve=True)
        self.assertFalse(self.out.exists())

    def test_s06_atomic_receipt_write_fsync_failure_preserves_exit_and_input_bytes(self):
        real_python=shutil.which('python3')
        shim=self.root/'fake-bin/python3'
        shim.write_text('#!'+real_python+'\n'+"""import errno, os, sys
if len(sys.argv)>1 and sys.argv[1]=='-c':
    def fail_sync(fd): raise OSError(errno.ENOSPC, 'injected receipt write failure')
    os.fsync=fail_sync
    code=sys.argv[2];sys.argv=sys.argv[2:];exec(code)
else: os.execv("""+repr(real_python)+""", ["""+repr(real_python)+"""]+sys.argv[1:])
""")
        shim.chmod(0o700)
        originals={p:p.read_bytes() for p in (self.source,self.blob,self.package)}
        self.run_wrapper(self.s06_args(),expected=28,preserve=True)
        self.assertTrue(self.out.is_dir())
        self.assertEqual([],list(self.out.iterdir()), 'failed write must not publish a partial receipt or leak its temporary file')
        for path,data in originals.items():self.assertEqual(data,path.read_bytes())

    def test_s06_atomic_receipt_collision_preserves_existing_file_bytes(self):
        shim=self.root/'fake-bin/mkdir'
        shim.write_text('#!/bin/zsh\n/bin/mkdir "$@" || exit $?\nprint -n -r -- original-receipt-bytes > "${@: -1}/s06-argv-preflight.json"\n')
        shim.chmod(0o700)
        originals={p:p.read_bytes() for p in (self.source,self.blob,self.package)}
        self.run_wrapper(self.s06_args(),expected=17,preserve=True)
        self.assertEqual(b'original-receipt-bytes',(self.out/'s06-argv-preflight.json').read_bytes())
        self.assertEqual(['s06-argv-preflight.json'],[p.name for p in self.out.iterdir()])
        for path,data in originals.items():self.assertEqual(data,path.read_bytes())

    def test_s06_missing_receipt_after_false_success_cannot_launch(self):
        real_python=shutil.which('python3')
        shim=self.root/'fake-bin/python3'
        shim.write_text('#!'+real_python+'\n'+"""import os, sys
if len(sys.argv)>1 and sys.argv[1]=='-c': sys.exit(0)
os.execv("""+repr(real_python)+""", ["""+repr(real_python)+"""]+sys.argv[1:])
""")
        shim.chmod(0o700)
        self.run_wrapper(self.s06_args(),expected=74,preserve=True)
        self.assertEqual([],list(self.out.iterdir()))

    def nofollow_directory_error(self):
        link=self.root/'nofollow-error-link'
        link.symlink_to(self.root,target_is_directory=True)
        try:
            fd=os.open(link,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        except OSError as error:
            return error.errno
        else:
            os.close(fd);self.fail('platform must reject nofollow directory symlink')
        finally:link.unlink()

    def test_s06_post_preflight_mkdir_symlink_cannot_publish_or_launch(self):
        expected=self.nofollow_directory_error()
        escaped=self.root/'escaped-output';escaped.mkdir()
        sentinel=escaped/'sentinel';sentinel.write_bytes(b'escaped-original-bytes')
        shim=self.root/'fake-bin/mkdir'
        shim.write_text('#!/bin/zsh\n/bin/ln -s "$S06_ESCAPED_OUT" "${@: -1}"\n')
        shim.chmod(0o700);self.env['S06_ESCAPED_OUT']=str(escaped)
        originals={p:p.read_bytes() for p in (self.source,self.blob,self.package,sentinel)}
        self.run_wrapper(self.s06_args(),expected=expected,preserve=True)
        self.assertTrue(self.out.is_symlink())
        self.assertEqual(['sentinel'],[p.name for p in escaped.iterdir()])
        for path,data in originals.items():self.assertEqual(data,path.read_bytes())

    def test_s06_existing_ancestor_replacement_after_mkdir_is_rejected(self):
        parent=self.root/'existing-parent';parent.mkdir()
        sentinel=parent/'sentinel';sentinel.write_bytes(b'original-parent-bytes')
        selected=parent/'run';saved=self.root/'saved-parent'
        shim=self.root/'fake-bin/mkdir'
        shim.write_text('#!/bin/zsh\n/bin/mkdir "$@" || exit $?\n/bin/mv "$S06_EXISTING_PARENT" "$S06_SAVED_PARENT" || exit $?\n/bin/mkdir -p "${@: -1}"\n')
        shim.chmod(0o700)
        self.env.update(S06_EXISTING_PARENT=str(parent),S06_SAVED_PARENT=str(saved))
        originals={p:p.read_bytes() for p in (self.source,self.blob,self.package)}
        self.run_wrapper(self.s06_args(selected_out=selected,palm=selected/'palm.json',table=selected/'table.json'),
                         expected=errno.ESTALE,preserve=True)
        self.assertEqual(b'original-parent-bytes',(saved/'sentinel').read_bytes())
        self.assertEqual([],list(selected.iterdir()))
        for path,data in originals.items():self.assertEqual(data,path.read_bytes())

    def test_s06_prelaunch_recheck_rejects_directory_swap_after_publication(self):
        expected=self.nofollow_directory_error()
        escaped=self.root/'escaped-output';escaped.mkdir()
        sentinel=escaped/'s06-argv-preflight.json';sentinel.write_bytes(b'escaped-original-bytes')
        saved=self.root/'original-output';marker=self.root/'swap-done'
        self.env.update(S06_TARGET_OUT=str(self.out),S06_SAVED_OUT=str(saved),
                        S06_ESCAPED_OUT=str(escaped),S06_SWAP_MARKER=str(marker))
        envfile=self.root/'candidate/dd-env.sh'
        envfile.write_text(envfile.read_text()+"""dd_log() {
  if [ ! -e "$S06_SWAP_MARKER" ]; then
    /bin/mv "$S06_TARGET_OUT" "$S06_SAVED_OUT" || return $?
    /bin/ln -s "$S06_ESCAPED_OUT" "$S06_TARGET_OUT" || return $?
    print done > "$S06_SWAP_MARKER"
  fi
  print -r -- "$*"
}
""")
        originals={p:p.read_bytes() for p in (self.source,self.blob,self.package,sentinel)}
        self.run_wrapper(self.s06_args(),expected=expected,preserve=True)
        self.assertTrue(self.out.is_symlink())
        self.assertEqual(['s06-argv-preflight.json'],[p.name for p in escaped.iterdir()])
        receipt=json.loads((saved/'s06-argv-preflight.json').read_text());observed=saved.stat()
        self.assertEqual({'device':observed.st_dev,'inode':observed.st_ino},receipt['outputDirectoryIdentity'])
        for path,data in originals.items():self.assertEqual(data,path.read_bytes())

    def test_s06_post_mkdir_native_proof_ancestor_symlink_is_rejected(self):
        expected=self.nofollow_directory_error()
        escaped=self.root/'escaped-proof';escaped.mkdir()
        sentinel=escaped/'palm.json';sentinel.write_bytes(b'original-proof-bytes')
        shim=self.root/'fake-bin/mkdir'
        shim.write_text('#!/bin/zsh\n/bin/mkdir "$@" || exit $?\n/bin/ln -s "$S06_ESCAPED_PROOF" "${@: -1}/nested"\n')
        shim.chmod(0o700);self.env['S06_ESCAPED_PROOF']=str(escaped)
        self.run_wrapper(self.s06_args(palm=self.out/'nested/palm.json'),expected=expected,preserve=True)
        self.assertEqual(b'original-proof-bytes',sentinel.read_bytes())
        self.assertFalse((self.out/'s06-argv-preflight.json').exists())
        self.assertEqual(['palm.json'],[p.name for p in escaped.iterdir()])

    def test_s06_atomic_temporary_name_collision_never_removes_existing_bytes(self):
        mkdir=self.root/'fake-bin/mkdir'
        mkdir.write_text('#!/bin/zsh\n/bin/mkdir "$@" || exit $?\nprint -n -r -- original-temporary-bytes > "${@: -1}/.s06-argv-preflight-fixed"\n')
        mkdir.chmod(0o700)
        real_python=shutil.which('python3')
        shim=self.root/'fake-bin/python3'
        shim.write_text('#!'+real_python+'\n'+"""import os, secrets, sys
if len(sys.argv)>1 and sys.argv[1]=='-c':
    secrets.token_hex=lambda n: 'fixed'
    code=sys.argv[2];sys.argv=sys.argv[2:];exec(code)
else: os.execv("""+repr(real_python)+""", ["""+repr(real_python)+"""]+sys.argv[1:])
""")
        shim.chmod(0o700)
        self.run_wrapper(self.s06_args(),expected=17,preserve=True)
        self.assertEqual(b'original-temporary-bytes',(self.out/'.s06-argv-preflight-fixed').read_bytes())
        self.assertFalse((self.out/'s06-argv-preflight.json').exists())

    def test_s06_native_parent_identity_bound_at_publication_is_rechecked(self):
        nested=self.out/'nested';saved=self.root/'saved-native-parent';marker=self.root/'swap-done'
        mkdir=self.root/'fake-bin/mkdir'
        mkdir.write_text('#!/bin/zsh\n/bin/mkdir "$@" || exit $?\n/bin/mkdir "${@: -1}/nested" || exit $?\nprint -n -r -- original-native-parent-bytes > "${@: -1}/nested/sentinel"\n')
        mkdir.chmod(0o700)
        self.env.update(S06_NESTED=str(nested),S06_SAVED_PARENT=str(saved),S06_SWAP_MARKER=str(marker))
        envfile=self.root/'candidate/dd-env.sh'
        envfile.write_text(envfile.read_text()+"""dd_log() {
  if [ ! -e "$S06_SWAP_MARKER" ]; then
    /bin/mv "$S06_NESTED" "$S06_SAVED_PARENT" || return $?
    /bin/mkdir "$S06_NESTED" || return $?
    print done > "$S06_SWAP_MARKER"
  fi
  print -r -- "$*"
}
""")
        originals={p:p.read_bytes() for p in (self.source,self.blob,self.package)}
        self.run_wrapper(self.s06_args(palm=nested/'palm.json'),expected=errno.ESTALE,preserve=True)
        self.assertEqual(b'original-native-parent-bytes',(saved/'sentinel').read_bytes())
        self.assertEqual([],list(nested.iterdir()))
        receipt=json.loads((self.out/'s06-argv-preflight.json').read_text())
        identities={v['path']:v for v in receipt['outputBoundAncestorIdentities']}
        self.assertEqual(saved.stat().st_ino,identities[str(nested)]['inode'])
        for path,data in originals.items():self.assertEqual(data,path.read_bytes())

    def test_s06_multiple_leading_slashes_rejected_on_out_and_both_proofs(self):
        for count in (2,3):
            prefix='/'*(count-1)
            aliased=prefix+str(self.out)
            cases=[self.s06_args(selected_out=aliased,palm=aliased+'/palm.json',table=aliased+'/table.json'),
                   self.s06_args(palm=prefix+str(self.out/'palm.json')),
                   self.s06_args(table=prefix+str(self.out/'table.json'))]
            for index,args in enumerate(cases):
                with self.subTest(leading_slashes=count,path_case=index):
                    self.run_wrapper(args,expected=64,preserve=True)
                    self.assertFalse(self.out.exists())

    def test_s06_double_slash_existing_parent_replacement_never_reaches_mkdir(self):
        parent=self.root/'existing-parent';parent.mkdir()
        sentinel=parent/'sentinel';sentinel.write_bytes(b'original-parent-sentinel')
        original_stat=parent.stat();saved=self.root/'saved-parent';selected=parent/'run'
        shim=self.root/'fake-bin/mkdir'
        shim.write_text('#!/bin/zsh\n/bin/mkdir "$@" || exit $?\n/bin/mv "$S06_EXISTING_PARENT" "$S06_SAVED_PARENT" || exit $?\n/bin/mkdir -p "${@: -1}"\n')
        shim.chmod(0o700)
        self.env.update(S06_EXISTING_PARENT=str(parent),S06_SAVED_PARENT=str(saved))
        aliased='/'+str(selected)
        args=self.s06_args(selected_out=aliased,palm=aliased+'/palm.json',table=aliased+'/table.json')
        self.run_wrapper(args,expected=64,preserve=True)
        self.assertFalse(selected.exists())
        self.assertFalse(saved.exists(), 'invalid alias must stop before mkdir can move the parent')
        self.assertEqual((original_stat.st_dev,original_stat.st_ino),(parent.stat().st_dev,parent.stat().st_ino))
        self.assertEqual(b'original-parent-sentinel',sentinel.read_bytes())


if __name__ == '__main__':
    unittest.main()
