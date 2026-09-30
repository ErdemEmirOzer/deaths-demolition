"""Integration status failures using isolated copies and a nondelegating fake Git."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "integrate.sh"
FAKE_GIT = r'''#!/usr/bin/env python3
import json, os, pathlib, sys
root = pathlib.Path(os.environ['FIXTURE_ROOT'])
a = sys.argv[1:]
with (root/'calls.jsonl').open('a') as f: f.write(json.dumps(a)+'\n')
directory = root/'repo'
if len(a)>=2 and a[0]=='-C': directory=pathlib.Path(a[1]); a=a[2:]
if not a: sys.exit(99)
cmd=a[0]; main='a'*40; source='b'*40
if cmd=='status':
 target='source' if directory.name=='source' else 'main'
 state=root/('count-'+target)
 count=int(state.read_text()) if state.exists() else 0
 state.write_text(str(count+1))
 series=json.loads(os.environ['STATUS_SERIES']).get(target, [['empty',0]])
 output,code=series[min(count,len(series)-1)]
 if output=='dirty':
  sys.stdout.buffer.write(b' M sentinel.txt\0' if '-z' in a else b' M sentinel.txt\n')
 if code: print('simulated status acquisition failure',file=sys.stderr)
 sys.exit(code)
if cmd=='rev-parse':
 if '--git-common-dir' in a: print(root/'common');sys.exit(0)
 if any(x.startswith('refs/') for x in a):sys.exit(1)
 print(main if a[-1]=='main' or a[-1].endswith('^') else source);sys.exit(0)
if cmd=='log':print('Test: fixture' if '--format=%b' in a else 'WP-000: fixture source (C-00 30.09)');sys.exit(0)  # K-02 message gate
if cmd=='rev-list':print(source+' '+main);sys.exit(0)
if cmd=='merge-base':sys.exit(1)
if cmd=='worktree':print('worktree '+str(root/'source')+'\nbranch refs/heads/fake-source');sys.exit(0)
if cmd=='diff-tree':print('tools/fixture.py');sys.exit(0)
if cmd in ('diff','lfs'):sys.exit(0)
if cmd=='cat-file':sys.exit(0 if a[-1]=='HEAD:sentinel.txt' else 99)
if cmd in ('merge','cherry-pick','tag','commit','restore','rm'):
 if cmd=='tag' and len(a)==1:print('fixture-tag')
 sys.exit(0)
print('unexpected fake Git command: '+repr(a),file=sys.stderr);sys.exit(99)
'''
MUTATIONS = {'merge', 'cherry-pick', 'tag', 'commit', 'restore', 'rm', 'add'}


class IntegrateStatusTests(unittest.TestCase):
    def execute(self, series, rollback=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for folder in ('repo', 'source', 'bin', 'common/lfs/objects', 'tools'):
                (root/folder).mkdir(parents=True, exist_ok=True)
            sentinels = {'sentinel.txt': b'pre-existing tracked edit\n',
                         'untracked.txt': b'pre-existing untracked content\n'}
            for name, content in sentinels.items(): (root/'repo'/name).write_bytes(content)
            git = root/'bin/git'
            git.write_text(FAKE_GIT)
            git.chmod(0o700)
            production = SOURCE.read_text()
            script = root/'tools/integrate.sh'
            if rollback:
                function = production[production.index('abort_pick() {'):production.index('\nFAILS=0')]
                script.write_text('set -u\nDD_REPO="'+str(root/'repo')+'"\nG=(git -C "$DD_REPO")\n'+function+'\nabort_pick\n')
                args = []
            else:
                script.write_text(production)
                (root/'tools/dd-env.sh').write_text('DD_REPO="'+str(root/'repo')+'"\n')
                args = ['fake-source', '--tag', 'fixture-tag', '--apply', '--no-post', '--repo', str(root/'repo')]
            env = dict(os.environ, PATH=str(root/'bin')+':'+os.environ['PATH'],
                       FIXTURE_ROOT=str(root), STATUS_SERIES=json.dumps(series))
            result = subprocess.run(['zsh', str(script), *args], capture_output=True, text=True, env=env)
            calls = [json.loads(line) for line in (root/'calls.jsonl').read_text().splitlines()]
            commands = [call[2:] if call[0]=='-C' else call for call in calls]
            self.assertEqual(set(sentinels), {p.name for p in (root/'repo').iterdir()})
            for name, content in sentinels.items(): self.assertEqual(content, (root/'repo'/name).read_bytes())
            return result, commands

    def gate_failure(self, target, output, code):
        result, calls = self.execute({target: [[output, code]]})
        self.assertEqual(3, result.returncode, result.stdout+result.stderr)
        self.assertFalse([a for a in calls if a[0] in MUTATIONS], calls)
        self.assertIn('GATE FAIL', result.stdout)

    def clean_control(self, target):
        result, calls = self.execute({target: [['empty', 0]]})
        self.assertEqual(0, result.returncode, result.stdout+result.stderr)
        self.assertTrue(any(a[0]=='merge' and '--ff-only' in a for a in calls))
        self.assertTrue(any(a[0]=='tag' and '-a' in a for a in calls))
        self.assertIn('integrated:', result.stdout)

    def rollback_failure(self, position, output):
        responses = [['dirty', 0]]*(position-1)+[[output, 128]]
        result, calls = self.execute({'main': responses}, rollback=True)
        self.assertEqual(7, result.returncode, result.stdout+result.stderr)   # Codex-audit M-08: unverified rollback = 7
        self.assertIn('ROLLBACK UNVERIFIED', result.stdout)
        self.assertNotIn('aborted: 0 dirty paths', result.stdout)
        mutations = [a for a in calls if a[0] in MUTATIONS]
        if position==1:
            self.assertEqual([], mutations)
        elif position==2:
            self.assertEqual([['cherry-pick', '--quit']], mutations)
        else:
            self.assertEqual([['cherry-pick', '--quit'],
                              ['restore', '--staged', '--worktree', '--', 'sentinel.txt']], mutations)
            self.assertEqual(3, sum(a[0]=='status' for a in calls))

    def test_main_empty_status_error(self): self.gate_failure('main', 'empty', 128)
    def test_main_dirty_status_error(self): self.gate_failure('main', 'dirty', 128)
    def test_main_dirty_status_success(self): self.gate_failure('main', 'dirty', 0)
    def test_source_empty_status_error(self): self.gate_failure('source', 'empty', 128)
    def test_source_dirty_status_error(self): self.gate_failure('source', 'dirty', 128)
    def test_source_dirty_status_success(self): self.gate_failure('source', 'dirty', 0)
    def test_clean_main_allows_fake_apply(self): self.clean_control('main')
    def test_clean_source_allows_fake_apply(self): self.clean_control('source')
    def test_first_rollback_status_empty_error(self): self.rollback_failure(1, 'empty')
    def test_first_rollback_status_dirty_error(self): self.rollback_failure(1, 'dirty')
    def test_second_rollback_status_empty_error(self): self.rollback_failure(2, 'empty')
    def test_second_rollback_status_dirty_error(self): self.rollback_failure(2, 'dirty')
    def test_postrestore_status_empty_error(self): self.rollback_failure(3, 'empty')
    def test_postrestore_status_dirty_error(self): self.rollback_failure(3, 'dirty')


if __name__ == '__main__':
    unittest.main()
