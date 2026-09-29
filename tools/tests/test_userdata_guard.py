#!/usr/bin/env python3
"""Isolated actual-zsh regressions, fake defaults, real owned sampler observation."""
import hashlib, importlib.util, json, os, plistlib, shutil, subprocess, tempfile, time, unittest
from pathlib import Path
TOOLS=Path(__file__).resolve().parents[1]
ROWS=[]
DEFAULTS='''#!/usr/bin/env python3
import os,pathlib,plistlib,sys
op,domain=sys.argv[1:3];allowed=pathlib.Path(os.environ['DD_GUARD_ROOT'])/'PR';p=pathlib.Path(domain+'.plist')
if p.parent!=allowed:sys.exit(77)
c=allowed.parent/'defaults-fault';fault=c.read_text() if c.exists() else ''
if fault==op:sys.exit(78)
if fault=='malformed' and op=='export':sys.stdout.buffer.write(b'bad plist');sys.exit(0)
if op=='export':
 if not p.is_file():sys.exit(1)
 sys.stdout.buffer.write(plistlib.dumps(plistlib.load(p.open('rb'))))
elif op=='delete':
 d=plistlib.load(p.open('rb'));d.pop(sys.argv[3],None);p.write_bytes(plistlib.dumps(d))
elif op=='import':
 d=plistlib.load(p.open('rb')) if p.exists() else {};d.update(plistlib.load(open(sys.argv[3],'rb')));p.write_bytes(plistlib.dumps(d))
else:sys.exit(78)
'''

class Fixture:
 def __init__(self,scope='editor',absent=False,slow=False):
  self.tmp=tempfile.TemporaryDirectory(prefix='dd-guard-regression-');self.root=Path(self.tmp.name).resolve();self.tools=self.root/'tools';self.tools.mkdir()
  for name in ['userdata-guard.sh','userdata_guard_audit.py','userdata_attrib.py']:shutil.copy2(TOOLS/name,self.tools/name)
  (self.tools/'dd-env.sh').write_text('DD_PRODUCT=FixtureProduct\nDD_BUNDLE_ID=com.fixture.guard\nDD_SLOT="$DD_GUARD_ROOT/fake-slot"\ndd_log(){ print -r -- "$*"; }\n')
  self.prefs=self.root/'prefs';self.pr=self.prefs/'PR';self.pr.mkdir(parents=True)
  self.d1=self.prefs/'AS/Deaths Demolition/FixtureProduct';self.d2=self.prefs/'AS/com.fixture.guard';self.d1.mkdir(parents=True);self.d2.mkdir(parents=True)
  self.local=self.d1/'prototype-profile.json';self.foreign=self.d2/'prototype-profile.json';self.local.write_bytes(b'local baseline');self.foreign.write_bytes(b'foreign baseline')
  self.p1=self.pr/'com.fixture.guard.plist';self.p2=self.pr/'unity.Deaths Demolition.FixtureProduct.plist'
  if not absent:
   for p in [self.p1,self.p2]:p.write_bytes(plistlib.dumps({'fixture':1}))
  slot=self.prefs/'fake-slot';slot.write_text('#!/bin/zsh\nprint fixture-slot\n');slot.chmod(0o700)
  self.bin=self.root/'bin';self.bin.mkdir()
  scripts={'defaults':DEFAULTS,'ps':'#!/bin/zsh\nif [ "$1" = -axo ]; then exit 0; fi\nexec /bin/ps "$@"\n','pgrep':'#!/bin/zsh\nexit 1\n','sleep':'#!/bin/zsh\nexec /bin/sleep '+('2' if slow else '.1')+'\n'}
  for name,text in scripts.items():p=self.bin/name;p.write_text(text);p.chmod(0o700)
  self.env=dict(os.environ,DD_GUARD_ROOT=str(self.prefs),PATH=str(self.bin)+os.pathsep+os.environ['PATH']);self.guard=self.root/'guard';self.scope=scope;self.sampler=None
  if slow:
   p=self.tools/'userdata_guard_audit.py';p.write_text(p.read_text().replace('time.monotonic() + 8','time.monotonic() + .15'))
 def command(self,op):return subprocess.run(['zsh',str(self.tools/'userdata-guard.sh'),op,str(self.guard),'--scope',self.scope],env=self.env,capture_output=True,text=True)
 def snapshot(self):
  p=self.command('snapshot')
  if p.returncode==0:self.sampler=json.loads((self.guard/'sampler.json').read_text())
  return p
 def audit(self,action):return subprocess.run(['python3',str(self.tools/'userdata_guard_audit.py'),action,str(self.guard),self.scope,str(self.d1),str(self.d2),str(self.p1),str(self.p2)],env=self.env,capture_output=True,text=True)
 def close(self):
  (self.guard/'.sampling').unlink(missing_ok=True)
  if self.sampler:
   deadline=time.monotonic()+4
   while time.monotonic()<deadline:
    p=subprocess.run(['/bin/ps','-p',str(self.sampler['pid']),'-o','uid=,lstart='],capture_output=True,text=True)
    if p.returncode==1 or self.sampler['birth'] not in ' '.join(p.stdout.split()[1:]):break
    time.sleep(.05)
   else:raise AssertionError('own sampler did not naturally close; signals0')
  self.tmp.cleanup()

class GuardTests(unittest.TestCase):
 def setUp(self):self.fixtures=[]
 def tearDown(self):
  for f in self.fixtures:f.close()
 def fixture(self,**kw):
  f=Fixture(**kw);self.fixtures.append(f);return f
 def snapshot(self,f):
  p=f.snapshot();self.assertEqual(p.returncode,0,p.stdout+p.stderr);self.assertEqual(f.sampler['uid'],os.getuid());self.assertIn(str(f.guard),f.sampler['command']);self.assertTrue(f.sampler['birth'])
 def restore(self,f,ok):
  p=f.command('restore');self.assertEqual(p.returncode,0 if ok else 30,p.stdout+p.stderr);v=json.loads((f.guard/'guard-verdict.json').read_text());self.assertEqual(v['verified'],ok);self.assertEqual(v['status'],'PASS' if ok else 'FAIL');self.assertEqual('verified=1' in p.stdout,ok)
  if ok:self.assertTrue(v['samplerClosed']);self.assertEqual(f.audit('final').returncode,0)
  row={'test':self.id(),'scope':f.scope,'snapshotExit':0,'restoreExit':p.returncode,'verdict':v,'realOwnSamplerIdentity':f.sampler,'nativeRuns':0,'realProfileWrites':0,'signalsSent':0}
  if os.environ.get('DD_GUARD_TEST_EVIDENCE'):
   case=Path(os.environ['DD_GUARD_TEST_EVIDENCE']).parent/'cases'/f'{len(ROWS)+1:03d}'
   case.mkdir(parents=True);shutil.copytree(f.guard,case/'guard',symlinks=True)
   (case/'restore.stdout').write_text(p.stdout);(case/'restore.stderr').write_text(p.stderr)
   row['proofCopy']=str(case);row['originalGuardDirectory']=str(f.guard)
   row['artifactHashes']={str(a.relative_to(case)):hashlib.sha256(a.read_bytes()).hexdigest() for a in case.rglob('*') if a.is_file() and not a.is_symlink()}
   (case/'receipt.json').write_text(json.dumps(row,indent=2)+'\n')
  ROWS.append(row);return p
 def test_unchanged_scopes(self):
  for scope in ['editor','player','all']:
   with self.subTest(scope=scope):f=self.fixture(scope=scope);self.snapshot(f);self.restore(f,True)
 def test_all_restores_both(self):
  f=self.fixture(scope='all');self.snapshot(f);f.local.write_bytes(b'changed');f.foreign.unlink();self.restore(f,True);self.assertEqual(f.local.read_bytes(),b'local baseline');self.assertEqual(f.foreign.read_bytes(),b'foreign baseline')
 def test_scoped_created_files_preserved(self):
  f=self.fixture();self.snapshot(f);f.local.unlink();p=f.d1/'prototype-profile.json.bak';p.write_bytes(b'new');self.restore(f,True);self.assertEqual(f.local.read_bytes(),b'local baseline');self.assertFalse(p.exists());self.assertEqual((f.guard/'created/D1'/p.name).read_bytes(),b'new')
 def test_foreign_add_remove_modify_both_scopes(self):
  for scope in ['editor','player']:
   for kind in ['added','removed','modified']:
    with self.subTest(scope=scope,kind=kind):
     f=self.fixture(scope=scope);self.snapshot(f);p=f.foreign if scope=='editor' else f.local
     if kind=='removed':p.unlink()
     elif kind=='added':p=p.with_name(p.name+'.bak');p.write_bytes(b'foreign addition')
     else:p.write_bytes(b'foreign modification')
     self.restore(f,False);s=(f.guard/'out-of-scope-changed.txt').read_text();self.assertIn('\t'+kind+'\t'+str(p)+'\t',s);self.assertEqual(len(s.splitlines()),1);self.assertTrue(json.loads((f.guard/'guard-verdict.json').read_text())['scopedVerified'])
     if kind=='removed':self.assertFalse(p.exists())
     else:self.assertEqual(p.read_bytes(),b'foreign addition' if kind=='added' else b'foreign modification')
 def test_foreign_plist_preserved(self):
  f=self.fixture();self.snapshot(f);f.p1.write_bytes(plistlib.dumps({'fixture':9}));self.restore(f,False);self.assertEqual(plistlib.loads(f.p1.read_bytes()),{'fixture':9});self.assertIn('\tmodified\t'+str(f.p1),(f.guard/'out-of-scope-changed.txt').read_text())
 def test_scoped_plist_keys_exact(self):
  f=self.fixture();self.snapshot(f);f.p2.write_bytes(plistlib.dumps({'fixture':4,'extra':9}));self.restore(f,True);self.assertEqual(plistlib.loads(f.p2.read_bytes()),{'fixture':1})
 def test_absent_domain_keys_removed(self):
  f=self.fixture(absent=True);self.snapshot(f);f.p2.write_bytes(plistlib.dumps({'extra':99}));self.restore(f,True);self.assertEqual(plistlib.loads(f.p2.read_bytes()),{})
 def test_volatile_only_change_ignored_preserved(self):
  f=self.fixture(absent=True);self.snapshot(f);f.p1.write_bytes(plistlib.dumps({'unity.player_session_count':10}));self.restore(f,True);self.assertEqual(plistlib.loads(f.p1.read_bytes()),{'unity.player_session_count':10})
 def test_missing_corrupt_before_snapshot_sampler(self):
  for name in ['before-player.txt','before-editor.txt','snap/D1/prototype-profile.json','snapshot-manifest.json','sampler.json']:
   for remove in [False,True]:
    with self.subTest(name=name,missing=remove):
     f=self.fixture();self.snapshot(f);f.local.write_bytes(b'run value');p=f.guard/name
     if remove:p.unlink()
     else:p.write_bytes(b'corrupt')
     self.restore(f,False);self.assertEqual(f.local.read_bytes(),b'run value')
 def test_wrong_scope_fails_before_writes(self):
  f=self.fixture();self.snapshot(f);f.scope='player';f.foreign.write_bytes(b'run value');self.restore(f,False);self.assertEqual(f.foreign.read_bytes(),b'run value')
 def test_defaults_export_failure_malformed(self):
  for fault in ['export','malformed']:
   with self.subTest(fault=fault):
    f=self.fixture();self.snapshot(f);(f.prefs/'defaults-fault').write_text(fault);f.local.write_bytes(b'run value');self.restore(f,False);self.assertEqual(f.local.read_bytes(),b'run value')
 def test_defaults_import_delete_failure(self):
  for fault in ['import','delete']:
   with self.subTest(fault=fault):
    f=self.fixture();self.snapshot(f);f.p2.write_bytes(plistlib.dumps({'fixture':9,'extra':1}));(f.prefs/'defaults-fault').write_text(fault);self.restore(f,False)
 def test_sampler_still_alive_is_not_pass(self):
  f=self.fixture(slow=True);self.snapshot(f);p=self.restore(f,False);self.assertIn('sampler still alive',p.stderr)
 def test_final_missing_corrupt_reports(self):
  f=self.fixture();self.snapshot(f);self.restore(f,True)
  for name in ['extra-keys.txt','out-of-scope-changed.txt','out-of-scope-bindings.json','after.txt','after-editor.txt','sampler-closure.json']:
   with self.subTest(name=name):
    p=f.guard/name;old=p.read_bytes();p.unlink();self.assertNotEqual(f.audit('final').returncode,0,name+' missing');p.write_bytes(b'corrupt');self.assertNotEqual(f.audit('final').returncode,0,name+' corrupt');p.write_bytes(old)
  self.assertEqual(f.audit('final').returncode,0)
 def test_late_foreign_change_is_not_pass(self):
  f=self.fixture();self.snapshot(f);self.restore(f,True);f.foreign.write_bytes(b'late foreign');self.assertNotEqual(f.audit('final').returncode,0);self.assertEqual(f.foreign.read_bytes(),b'late foreign')
 def test_symlink_target_not_written(self):
  f=self.fixture();self.snapshot(f);p=f.root/'unrelated';p.write_bytes(b'untouched');f.local.unlink();f.local.symlink_to(p);self.restore(f,False);self.assertEqual(p.read_bytes(),b'untouched')
 def test_previous_pass_cannot_survive_failed_restore(self):
  f=self.fixture();self.snapshot(f);self.restore(f,True);(f.guard/'before-editor.txt').unlink();self.restore(f,False)
 def test_bad_argument_shape_cannot_default_to_all(self):
  f=self.fixture()
  for args in [['snapshot',str(f.guard),'--bogus','editor'],['snapshot',str(f.guard),'--scope'],['snapshot',str(f.guard),'editor']]:
   p=subprocess.run(['zsh',str(f.tools/'userdata-guard.sh')]+args,env=f.env,capture_output=True,text=True);self.assertEqual(p.returncode,64);self.assertFalse(f.guard.exists())
 def test_corrupt_snapshot_plist_and_bad_export_refused(self):
  for fault in ['disk','export','malformed']:
   with self.subTest(fault=fault):
    f=self.fixture()
    if fault=='disk':f.p2.write_bytes(b'bad plist')
    else:(f.prefs/'defaults-fault').write_text(fault)
    p=f.snapshot();self.assertEqual(p.returncode,30,p.stdout+p.stderr);self.assertEqual(f.local.read_bytes(),b'local baseline');self.assertFalse((f.guard/'.sampling').exists())
 def test_evidence_symlink_does_not_write_foreign_file(self):
  f=self.fixture();self.snapshot(f);target=f.root/'unrelated';target.write_bytes(b'untouched');(f.guard/'out-of-scope-changed.txt').symlink_to(target)
  p=f.command('restore');self.assertEqual(p.returncode,30,p.stdout+p.stderr);self.assertEqual(target.read_bytes(),b'untouched');self.assertNotIn('verified=1',p.stdout)
 def test_scoped_restore_preserves_live_volatile_counter(self):
  f=self.fixture();f.p2.write_bytes(plistlib.dumps({'fixture':1,'unity.player_session_count':1}));self.snapshot(f);f.p2.write_bytes(plistlib.dumps({'fixture':9,'unity.player_session_count':10}));self.restore(f,True);self.assertEqual(plistlib.loads(f.p2.read_bytes()),{'fixture':1,'unity.player_session_count':10})
 def test_identity_pid_uid_birth_not_command(self):
  spec=importlib.util.spec_from_file_location('audit',TOOLS/'userdata_guard_audit.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);o={'pid':12,'uid':502,'birth':'birth','command':'snapshot'};self.assertTrue(m.same_identity(dict(o,command='new argv'),o));self.assertFalse(m.same_identity(None,o))
  for k,v in [('pid',13),('uid',503),('birth','other')]:self.assertFalse(m.same_identity(dict(o,**{k:v}),o))

if __name__=='__main__':
 result=unittest.main(exit=False)
 if os.environ.get('DD_GUARD_TEST_EVIDENCE'):
  Path(os.environ['DD_GUARD_TEST_EVIDENCE']).write_text(json.dumps({'testsRun':result.result.testsRun,'successful':result.result.wasSuccessful(),'rows':ROWS,'nativeRuns':0,'realProfileWrites':0,'signalsSent':0,'sourceSha256':{n:hashlib.sha256((TOOLS/n).read_bytes()).hexdigest() for n in ['userdata-guard.sh','userdata_guard_audit.py','tests/test_userdata_guard.py']}},indent=2)+'\n')
 raise SystemExit(0 if result.result.wasSuccessful() else 1)
