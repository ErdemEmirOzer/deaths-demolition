"""Bounded offline contracts. Fake apps/backends only, no native runtime PASS."""
from dataclasses import replace
import importlib.util
import os
from pathlib import Path
import plistlib
import sys
import tempfile
import unittest
from unittest.mock import patch

import native_observation as n


SOURCE = 'a' * 40
GRANT = 'b' * 64
ATTEMPT = '01234567-89ab-4cde-8fab-0123456789ab'
ACTOR = n.ProcessIdentity(421, 501, 1720000000, 123456, 20, 421)
EXECUTABLE = 'Deaths Demolition - Playable Concept'


class FakeBackend:
    def __init__(self, binary, identity=ACTOR, args='fixture actor'):
        self.binary = str(binary)
        self.actor = identity
        self.comm = self.binary
        self.args = args
        self.identity_reads = 0
        self.path_reads = 0
        self.comm_reads = 0
        self.hook = lambda kind, count: None
        self.queries = []

    def identity(self, pid):
        self.identity_reads += 1
        self.queries.append(('identity', pid))
        self.hook('identity', self.identity_reads)
        return self.actor

    def executable(self, pid):
        self.path_reads += 1
        self.queries.append(('kernel', pid))
        self.hook('kernel', self.path_reads)
        return self.binary

    def text(self, pid, field):
        self.queries.append((field, pid))
        if field == 'comm':
            self.comm_reads += 1
        self.hook(field, self.comm_reads if field == 'comm' else 1)
        return getattr(self, field)


class Contract(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='dd-native-observation-fixture-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.app = self.make_app('Target', EXECUTABLE, 'com.dd.fixture')
        self.pinned = n.inspect_app(str(self.app))
        self.context = n.ObservationContext(self.pinned, SOURCE)
        self.backend = FakeBackend(self.pinned.resolved_binary)

    def make_app(self, name, executable, bundle_id):
        app = self.root / (name + '.app')
        (app / 'Contents/MacOS').mkdir(parents=True)
        self.write_info(app, executable, bundle_id)
        binary = app / 'Contents/MacOS' / executable
        binary.write_bytes(b'fixture-executable-only\n')
        binary.chmod(0o755)
        return app

    def write_info(self, app, executable=EXECUTABLE, bundle_id='com.dd.fixture'):
        data = {'CFBundleIdentifier': bundle_id}
        if executable is not None:
            data['CFBundleExecutable'] = executable
        (app / 'Contents/Info.plist').write_bytes(plistlib.dumps(data, fmt=plistlib.FMT_BINARY))

    def controlled(self):
        binding = n.ControllerBinding(ACTOR, self.pinned.fingerprint, SOURCE, GRANT, ATTEMPT)
        return replace(self.context, controlled=binding, grant_sha256=GRANT, attempt_id=ATTEMPT)

    def observe(self, context=None, backend=None):
        return n.observe(ACTOR.pid, context or self.context, backend or self.backend)

    def unknown(self, context=None, backend=None):
        result = self.observe(context, backend)
        self.assertEqual('UNKNOWN_STOP', result.classification, result)
        self.assertFalse(result.authorization_granted)
        return result

    def test_user_app_is_foreign_with_spaced_literal_executable(self):
        result = self.observe()
        self.assertEqual('FOREIGN', result.classification)
        self.assertEqual('PLAYER', result.family)
        self.assertEqual(EXECUTABLE, result.app.executable)
        self.assertEqual(self.pinned.binary.sha256, result.file.sha256)
        self.assertEqual(str(self.app), result.app.resolved_app)
        self.assertEqual(self.backend.comm, result.comm)
        self.assertFalse(result.authorization_granted)

    def test_exact_existing_controller_binding_is_own_but_not_authorized(self):
        result = self.observe(self.controlled())
        self.assertEqual('OWN', result.classification)
        self.assertFalse(result.authorization_granted)
        self.assertEqual(2, self.backend.identity_reads)
        self.assertEqual(2, self.backend.path_reads)

    def test_other_pid_cannot_inherit_ownership(self):
        backend = FakeBackend(self.pinned.resolved_binary, replace(ACTOR, pid=422))
        result = n.observe(422, self.controlled(), backend)
        self.assertEqual('FOREIGN', result.classification)

    def test_bundle_id_and_basename_on_other_app_do_not_make_own(self):
        for exe, bundle in ((EXECUTABLE, 'different.bundle'), ('OtherPlayer', 'com.dd.fixture')):
            with self.subTest(exe=exe):
                app = self.make_app(exe.replace(' ', ''), exe, bundle)
                backend = FakeBackend(str(app / 'Contents/MacOS' / exe), replace(ACTOR, pid=423))
                result = n.observe(423, self.controlled(), backend)
                self.assertEqual('FOREIGN', result.classification)
                self.assertEqual('PLAYER', result.family)

    def test_grant_source_attempt_and_app_fingerprint_binding_contradictions(self):
        controlled = self.controlled()
        for delta in ({'source_sha': 'c' * 40}, {'grant_sha256': 'd' * 64},
                      {'attempt_id': '11111111-1111-4111-8111-111111111111'},
                      {'app_fingerprint': 'e' * 64}):
            with self.subTest(delta=delta):
                self.unknown(replace(controlled, controlled=replace(controlled.controlled, **delta)))
        self.unknown(replace(controlled, grant_sha256=''))
        self.unknown(replace(controlled, attempt_id='not-a-uuid'))
        self.unknown(replace(self.context, source_sha='main'))

    def test_grant_without_controlled_actor_remains_foreign(self):
        self.assertEqual('FOREIGN', self.observe(replace(
            self.context, grant_sha256=GRANT, attempt_id=ATTEMPT)).classification)

    def test_explicit_friendly_app_alias_is_foreign_and_has_both_paths(self):
        alias = self.root / 'Friendly.app'
        alias.symlink_to(self.app, target_is_directory=True)
        aliases = (n.AppAlias(str(alias), str(self.app)),)
        pinned = n.inspect_app(str(alias), aliases)
        context = replace(self.context, app=pinned, approved_aliases=aliases)
        self.backend.comm = pinned.declared_binary
        self.backend.binary = pinned.declared_binary
        result = self.observe(context)
        self.assertEqual('FOREIGN', result.classification)
        self.assertEqual(str(alias), pinned.declared_app)
        self.assertEqual(str(self.app), pinned.resolved_app)
        self.assertEqual(self.pinned.binary, pinned.binary)

    def test_unapproved_or_wrong_alias_is_rejected(self):
        alias = self.root / 'Friendly.app'
        alias.symlink_to(self.app, target_is_directory=True)
        for aliases in ((), (n.AppAlias(str(alias), str(self.root / 'Wrong.app')),)):
            with self.subTest(aliases=aliases), self.assertRaises(n.ObservationError):
                n.inspect_app(str(alias), aliases)

    def test_alias_retarget_between_queries_stops(self):
        other = self.make_app('Different', EXECUTABLE, 'com.dd.fixture')
        alias = self.root / 'Friendly.app'
        alias.symlink_to(self.app, target_is_directory=True)
        aliases = (n.AppAlias(str(alias), str(self.app)),)
        context = replace(self.context, app=n.inspect_app(str(alias), aliases), approved_aliases=aliases)
        def mutate(kind, count):
            if kind == 'kernel' and count == 2:
                alias.unlink()
                alias.symlink_to(other, target_is_directory=True)
        self.backend.hook = mutate
        self.unknown(context)

    def test_safe_executable_basename_rejects_traversal_controls_types(self):
        for value in ('', '.', '..', '../outside', '/bin/sh', 'a/b', 'a\\b',
                      'bad\nname', 'bad\0name', True, None, 12, ['Unity']):
            with self.subTest(value=value):
                self.write_info(self.app, value)
                with self.assertRaises(n.ObservationError):
                    n.inspect_app(str(self.app))

    def test_missing_or_ambiguous_plist_no_first_binary_fallback(self):
        info = self.app / 'Contents/Info.plist'
        info.unlink()
        self.unknown()
        info.write_bytes(b'not a plist')
        self.unknown()
        info.write_bytes(plistlib.dumps(['Unity']))
        self.unknown()
        self.write_info(self.app, 'MissingBinary')
        self.unknown()

    def test_internal_symlinks_nonregular_and_nonexecutable_are_rejected(self):
        binary = Path(self.pinned.resolved_binary)
        outside = self.root / 'outside'
        outside.write_bytes(binary.read_bytes())
        outside.chmod(0o755)
        binary.unlink()
        binary.symlink_to(outside)
        self.unknown()
        binary.unlink()
        binary.mkdir()
        self.unknown()
        binary.rmdir()
        binary.write_bytes(b'fixture')
        binary.chmod(0o644)
        self.unknown()

    def test_info_and_interior_directory_symlink_rejected(self):
        info = self.app / 'Contents/Info.plist'
        outside = self.root / 'info.plist'
        outside.write_bytes(info.read_bytes())
        info.unlink()
        info.symlink_to(outside)
        self.unknown()
        info.unlink()
        info.write_bytes(outside.read_bytes())
        macos = self.app / 'Contents/MacOS'
        target = self.root / 'macos'
        macos.rename(target)
        macos.symlink_to(target, target_is_directory=True)
        self.unknown()

    def test_python_and_shell_argv_examples_do_not_become_native(self):
        for name in ('python3', 'zsh'):
            with self.subTest(name=name):
                binary = self.root / name
                binary.write_bytes(b'fixture executable')
                binary.chmod(0o755)
                backend = FakeBackend(binary, args='document: ' + self.pinned.resolved_binary + ' Unity Blender')
                result = self.observe(backend=backend)
                self.assertEqual('NON_NATIVE', result.classification)
                self.assertIn('Unity Blender', result.args)

    def test_framework_python_app_with_native_argv_is_non_native(self):
        app = self.make_app('Python', 'Python', 'org.python.python')
        backend = FakeBackend(app / 'Contents/MacOS/Python', args=self.pinned.resolved_binary)
        self.assertEqual('NON_NATIVE', self.observe(backend=backend).classification)

    def test_true_unity_blender_candidates_are_retained_as_foreign(self):
        backends = {}
        for pid, exe in ((500, 'Unity'), (501, 'Blender'), (502, 'blender')):
            app = self.make_app('Actor' + str(pid), exe, 'fixture.' + exe)
            backends[pid] = FakeBackend(app / 'Contents/MacOS' / exe, replace(ACTOR, pid=pid))
        class Router:
            def identity(self, pid): return backends[pid].identity(pid)
            def executable(self, pid): return backends[pid].executable(pid)
            def text(self, pid, field): return backends[pid].text(pid, field)
        results = n.observe_many(backends, self.context, Router())
        self.assertEqual('OBSERVED', results['state'])
        self.assertEqual(['FOREIGN'] * 3, [o.classification for o in results['observations']])
        self.assertEqual(['UNITY', 'BLENDER', 'BLENDER'], [o.family for o in results['observations']])
        self.assertFalse(results['authorizationGranted'])
        self.assertFalse(results['nativeRuntimePass'])

    def test_native_basename_without_app_metadata_cannot_clear(self):
        for exe in ('Unity', 'Blender', EXECUTABLE):
            with self.subTest(exe=exe):
                binary = self.root / exe
                binary.write_bytes(b'fixture executable')
                binary.chmod(0o755)
                self.unknown(backend=FakeBackend(binary))

    def test_comm_basename_truncation_and_contradiction_stop(self):
        for comm in (EXECUTABLE, self.pinned.resolved_binary[:-4], '/bin/sh', '', 'Unity...'):
            with self.subTest(comm=comm):
                self.backend.comm = comm
                self.unknown()

    def test_query_permission_resolver_and_missing_process_errors_stop(self):
        for failure in ('identity', 'comm', 'args', 'kernel'):
            with self.subTest(failure=failure):
                backend = FakeBackend(self.pinned.resolved_binary)
                def fail(kind, count):
                    if kind == failure:
                        raise PermissionError('fixture denied query')
                backend.hook = fail
                self.unknown(backend=backend)
        for path in ('', EXECUTABLE, '/nonexistent/path', '/tmp/a\0b'):
            with self.subTest(path=path):
                self.backend.binary = path
                self.unknown()

    def test_pid_uid_birth_ppid_pgid_reread_races_stop(self):
        for delta in ({'pid': 422}, {'uid': 502}, {'birth_sec': ACTOR.birth_sec + 1},
                      {'birth_usec': ACTOR.birth_usec + 1}, {'ppid': 21}, {'pgid': 422}):
            with self.subTest(delta=delta):
                backend = FakeBackend(self.pinned.resolved_binary)
                def mutate(kind, count):
                    if kind == 'identity' and count == 2:
                        backend.actor = replace(ACTOR, **delta)
                backend.hook = mutate
                self.unknown(backend=backend)

    def test_preexisting_controlled_pid_reuse_stops(self):
        backend = FakeBackend(self.pinned.resolved_binary, replace(ACTOR, birth_usec=654321))
        self.unknown(self.controlled(), backend)

    def test_bool_invalid_typed_process_identity_and_wrong_pid_stop(self):
        for delta in ({'pid': True}, {'uid': True}, {'birth_sec': 0}, {'birth_usec': 1000000},
                      {'ppid': -1}, {'pgid': False}, {'pid': 422}):
            with self.subTest(delta=delta):
                self.unknown(backend=FakeBackend(self.pinned.resolved_binary, replace(ACTOR, **delta)))
        self.assertEqual('UNKNOWN_STOP', n.observe(True, self.context, self.backend).classification)

    def test_binary_hash_and_inode_drift_before_observation_stop(self):
        binary = Path(self.pinned.resolved_binary)
        original = binary.read_bytes()
        binary.write_bytes(b'changed bytes')
        self.unknown()
        replacement = self.root / 'replacement'
        replacement.write_bytes(original)
        replacement.chmod(0o755)
        replacement.replace(binary)
        self.unknown()

    def test_info_hash_drift_before_observation_stops(self):
        self.write_info(self.app, bundle_id='different.bundle')
        self.unknown()

    def test_binary_hash_and_inode_toctou_stop(self):
        for mode in ('hash', 'inode'):
            with self.subTest(mode=mode):
                pinned = n.inspect_app(str(self.app))
                context = replace(self.context, app=pinned)
                backend = FakeBackend(pinned.resolved_binary)
                def mutate(kind, count):
                    if kind == 'kernel' and count == 2:
                        binary = Path(pinned.resolved_binary)
                        if mode == 'hash':
                            binary.write_bytes(b'same-window drift')
                        else:
                            replacement = self.root / 'replacement'
                            replacement.write_bytes(binary.read_bytes())
                            replacement.chmod(0o755)
                            replacement.replace(binary)
                backend.hook = mutate
                self.unknown(context, backend)

    def test_metadata_toctou_and_kernel_path_switch_stop(self):
        def mutate(kind, count):
            if kind == 'kernel' and count == 2:
                self.write_info(self.app, bundle_id='drift.bundle')
        self.backend.hook = mutate
        self.unknown()
        self.write_info(self.app)
        pinned = n.inspect_app(str(self.app))
        backend = FakeBackend(pinned.resolved_binary)
        def switch(kind, count):
            if kind == 'kernel' and count == 2:
                backend.binary = '/bin/sh'
        backend.hook = switch
        self.unknown(replace(self.context, app=pinned), backend)

    def test_comm_changes_after_initial_queries_stop(self):
        def mutate(kind, count):
            if kind == 'comm' and count == 2:
                self.backend.comm = '/bin/sh'
        self.backend.hook = mutate
        self.unknown()

    def test_unapproved_standalone_binary_and_comm_aliases_stop(self):
        alias = self.root / 'binary-alias'
        alias.symlink_to(self.pinned.resolved_binary)
        self.backend.binary = str(alias)
        self.unknown()
        self.backend.binary = self.pinned.resolved_binary
        self.backend.comm = str(alias)
        self.unknown()

    def test_other_native_actor_info_contradiction_stops(self):
        app = self.make_app('UnityActor', 'Unity', 'fixture.unity')
        other = app / 'Contents/MacOS/OtherExecutable'
        other.write_bytes(b'fixture')
        other.chmod(0o755)
        self.write_info(app, 'OtherExecutable', 'fixture.unity')
        self.unknown(backend=FakeBackend(app / 'Contents/MacOS/Unity'))

    def test_descriptor_and_path_change_during_hash_read_stop(self):
        binary = Path(self.pinned.resolved_binary)
        original_read = os.read
        mutated = False
        def mutate(fd, size):
            nonlocal mutated
            result = original_read(fd, size)
            if not mutated:
                mutated = True
                replacement = self.root / 'replacement'
                replacement.write_bytes(b'changed')
                replacement.chmod(0o755)
                replacement.replace(binary)
            return result
        with patch.object(n.os, 'read', side_effect=mutate), self.assertRaises(n.ObservationError):
            n._read_file(binary)

    def test_batch_retains_unknown_and_foreign_and_empty_is_not_clear(self):
        result = n.observe_many((421, True), self.context, self.backend)
        self.assertEqual('UNKNOWN_STOP', result['state'])
        self.assertEqual(['FOREIGN', 'UNKNOWN_STOP'], [o.classification for o in result['observations']])
        self.assertEqual('UNKNOWN_STOP', n.observe_many((), self.context, self.backend)['state'])
        self.assertEqual('supplied PIDs only', result['scope'])

    def test_darwin_ps_fields_are_separate_unlimited_width_and_exit_checked(self):
        backend = n.DarwinBackend()
        outputs = (self.pinned.resolved_binary + '\n', 'doc ' + self.pinned.resolved_binary + '\n')
        with patch.object(n.subprocess, 'run') as run:
            for field, output in zip(('comm', 'args'), outputs):
                run.return_value = n.subprocess.CompletedProcess([], 0, output.encode(), b'')
                self.assertEqual(output[:-1], backend.text(421, field))
                self.assertEqual(['/bin/ps', '-ww', '-p', '421', '-o', field + '='], run.call_args.args[0])
            for code, stdout, stderr in ((1, b'', b''), (0, b'', b''),
                                         (0, b'one\ntwo\n', b''), (0, b'name\n', b'denied')):
                run.return_value = n.subprocess.CompletedProcess([], code, stdout, stderr)
                with self.assertRaises(n.ObservationError):
                    backend.text(421, 'comm')
            self.assertEqual([0, 0, 1, 0, 0, 0], [q['originalExit'] for q in backend.queries])

    def test_kernel_resolver_denial_and_truncation_are_not_fallbacks(self):
        class Library:
            def __init__(self, count, value): self.count, self.value = count, value
            def proc_pidpath(self, pid, buffer, size):
                buffer.value = self.value
                return self.count
        backend = n.DarwinBackend()
        for length, value in ((0, b''), (-1, b''), (4096, b'/bin/sh'),
                              (10, b'relative'), (10, b'/bad\xff')):
            with self.subTest(length=length, value=value), patch.object(
                    backend, '_lib', return_value=Library(length, value)), self.assertRaises(n.ObservationError):
                backend.executable(421)

    def test_import_has_no_queries_or_native_library_load(self):
        name = 'dd_fixture_import_no_side_effect'
        spec = importlib.util.spec_from_file_location(name, n.__file__)
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        self.addCleanup(sys.modules.pop, name)
        with (patch.object(n.subprocess, 'run', side_effect=AssertionError('query on import')),
                patch.object(n.ctypes, 'CDLL', side_effect=AssertionError('library on import')),
                patch.object(n.os, 'open', side_effect=AssertionError('file open on import'))):
            spec.loader.exec_module(module)


if __name__ == '__main__':
    unittest.main(verbosity=2)
