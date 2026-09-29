#!/usr/bin/env python3
"""Fail-closed artifact and process checks for userdata-guard.sh (stdlib only)."""
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import sys
import tempfile
import time

VOLATILE = {'unity.player_session_count', 'unity.player_sessionid'}
FIXED = {'prototype-profile.json', 'ui-settings.json', 'load-timing.json',
         'input-settings.json', 'input-overrides.json', 'ui-hints.json'}
SUFFIX = ' (launch-counter keys excluded)'


def require(ok, message):
    if not ok:
        raise ValueError(message)


def regular(path):
    require(path.is_file() and not path.is_symlink(), f'not a regular file: {path}')
    return path


def digest(path):
    return hashlib.sha256(regular(path).read_bytes()).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def plist(path):
    if not path.exists():
        require(not path.is_symlink(), f'dangling symlink: {path}')
        return {}
    value = plistlib.loads(regular(path).read_bytes())
    require(isinstance(value, dict), f'plist is not a dictionary: {path}')
    return value


def plist_hash(path):
    value = {k: v for k, v in plist(path).items() if k not in VOLATILE}
    # An absent domain and a domain containing only volatile launch keys are equivalent.
    return hashlib.sha256(json.dumps(value, sort_keys=True, default=repr).encode()).hexdigest() if value else 'MISSING'


def allowed(name, area):
    return (name in (FIXED if area == 'editor' else {'prototype-profile.json'}) or
            (name.startswith('prototype-profile.json.') and not name.endswith('.lock')))


def files(folder, area):
    if not folder.exists():
        require(not folder.is_symlink(), f'dangling folder symlink: {folder}')
        return []
    require(folder.is_dir() and not folder.is_symlink(), f'unsafe folder: {folder}')
    result = sorted(p for p in folder.iterdir() if allowed(p.name, area))
    for path in result:
        require('\n' not in str(path) and '\r' not in str(path), 'newline in guarded path')
        regular(path)
    return result


def state(area, snapshot=False):
    folder, pref, label = locations[area]
    source = root / 'snap' / label if snapshot else folder
    pp = root / 'snap' / 'PR' / ('p2.plist' if area == 'editor' else 'p1.plist') if snapshot else pref
    rows = {str(folder / p.name): digest(p) for p in files(source, area)}
    rows[str(pref) + SUFFIX] = plist_hash(pp)
    return rows


def text_state(rows):
    return ''.join(f'{value}  {path}\n' for path, value in sorted(rows.items()))


def states(stage, snapshot=False):
    values = {area: text_state(state(area, snapshot)) for area in locations}
    for area, value in values.items():
        (root / f'{stage}-{area}.txt').write_text(value)
    (root / f'{stage}.txt').write_text(''.join(values.values()))


def read_state(path, area):
    text = regular(path).read_text()
    rows = {}
    folder, pref, _ = locations[area]
    for line in text.splitlines():
        parts = line.split('  ', 1)
        require(len(parts) == 2, f'invalid state row: {path}')
        value, name = parts
        is_pref = name == str(pref) + SUFFIX
        require(re.fullmatch('[0-9a-f]{64}', value) or (is_pref and value == 'MISSING'), f'invalid digest: {path}')
        require(is_pref or (Path(name).parent == folder and allowed(Path(name).name, area)), f'foreign state path: {name}')
        require(name not in rows, f'duplicate state path: {name}')
        rows[name] = value
    require(str(pref) + SUFFIX in rows, f'missing plist row: {path}')
    require(text == text_state(rows), f'noncanonical state: {path}')
    return rows


def identity(pid):
    require(isinstance(pid, int) and pid > 1, 'invalid sampler PID')
    result = subprocess.run(['/bin/ps', '-o', 'uid=,lstart=,command=', '-p', str(pid)], capture_output=True, text=True)
    if result.returncode == 1 and not result.stdout.strip():
        return None
    require(result.returncode == 0, 'process observation failed')
    parts = result.stdout.strip().split(None, 6)
    require(len(parts) == 7 and parts[0].isdigit(), 'invalid process observation')
    return {'pid': pid, 'uid': int(parts[0]), 'birth': ' '.join(parts[1:6]), 'command': parts[6]}


def same_identity(observed, original):
    return observed is not None and all(observed[k] == original[k] for k in ('pid', 'uid', 'birth'))


def snapshot_hashes():
    snap = root / 'snap'
    require(snap.is_dir() and not snap.is_symlink(), 'missing snapshot')
    paths = []
    for area, (_, _, label) in locations.items():
        folder = snap / label
        require(folder.is_dir() and not folder.is_symlink(), 'missing snapshot folder')
        found = files(folder, area)
        require(set(folder.iterdir()) == set(found), 'unrecognized snapshot file')
        paths += found
    pr = snap / 'PR'
    require(pr.is_dir() and not pr.is_symlink(), 'missing snapshot preferences folder')
    for p in pr.iterdir():
        require(p.name in {'p1.plist', 'p2.plist'}, 'unrecognized snapshot plist')
        plist(p)
        paths.append(p)
    require(set(p.name for p in snap.iterdir()) == {'D1', 'D2', 'PR'}, 'unrecognized snapshot area')
    return {str(p.relative_to(root)): digest(p) for p in sorted(paths)}


def context():
    return {'scope': scope, 'root': str(root), 'locations': {a: [str(p) for p in v] for a, v in locations.items()}}


def validate():
    manifest_path = root / 'snapshot-manifest.json'
    require(digest(manifest_path) == regular(root / 'snapshot-manifest.sha256').read_text().strip(), 'snapshot manifest binding mismatch')
    m = json.loads(manifest_path.read_text())
    require(m.get('version') == 1 and m.get('context') == context(), 'snapshot context/scope mismatch')
    require(m.get('snapshotFiles') == snapshot_hashes(), 'snapshot file binding mismatch')
    for area in locations:
        require(read_state(root / f'before-{area}.txt', area) == state(area, True), f'before/snapshot mismatch: {area}')
    for name, sha in m['reports'].items():
        require(name in {'before.txt', 'before-editor.txt', 'before-player.txt', 'sampler.json'}, 'unknown snapshot report')
        require(digest(root / name) == sha, f'snapshot report binding mismatch: {name}')
    require(set(m['reports']) == {'before.txt', 'before-editor.txt', 'before-player.txt', 'sampler.json'}, 'missing snapshot report binding')
    require((root / 'before.txt').read_text() == ''.join((root / f'before-{a}.txt').read_text() for a in locations), 'combined baseline mismatch')
    sampler = json.loads((root / 'sampler.json').read_text())
    require(set(sampler) == {'pid', 'uid', 'birth', 'command'} and type(sampler['uid']) is int and sampler['uid'] == os.getuid(), 'invalid sampler identity')
    require(isinstance(sampler['birth'], str) and sampler['birth'] and isinstance(sampler['command'], str), 'invalid sampler birth/command')
    require(str(root) in sampler['command'] and 'userdata-guard.sh' in sampler['command'] and 'snapshot' in sampler['command'], 'sampler command mismatch')
    require(isinstance(sampler['pid'], int) and sampler['pid'] > 1, 'invalid sampler PID')
    return m, sampler


def delta(before, after, area):
    rows = []
    for name in sorted(set(before) | set(after)):
        if before.get(name) == after.get(name):
            continue
        kind = 'added' if name not in before else 'removed' if name not in after else 'modified'
        rows.append(f'{area}\t{kind}\t{name}\t{before.get(name, "ABSENT")}\t{after.get(name, "ABSENT")}\n')
    return ''.join(rows)


def other_report():
    rows = ''
    for area in locations:
        after = read_state(root / f'after-run-{area}.txt', area)
        if area not in areas:
            rows += delta(read_state(root / f'before-{area}.txt', area), after, area)
    require(regular(root / 'after-run.txt').read_text() == ''.join((root / f'after-run-{a}.txt').read_text() for a in locations), 'combined after-run mismatch')
    return rows


def exported(domain, disk):
    result = subprocess.run(['defaults', 'export', domain, '-'], capture_output=True)
    if result.returncode == 1 and not disk.exists() and not result.stdout.strip():
        return {}
    require(result.returncode == 0 and bool(result.stdout.strip()), f'defaults export failed: {domain} ({result.returncode})')
    value = plistlib.loads(result.stdout)
    require(isinstance(value, dict), 'export is not a dictionary')
    return value


def safe_folder(folder):
    for p in [folder, *folder.parents]:
        require(not p.is_symlink(), f'symlink directory: {p}')
    folder.mkdir(parents=True, exist_ok=True)


def run():
    if action == 'snapshot-copy':
        snap = root / 'snap'
        if snap.exists():
            require(snap.is_dir() and not snap.is_symlink(), 'unsafe snapshot directory')
            shutil.rmtree(snap)
        for area, (folder, pref, label) in locations.items():
            dest = snap / label
            dest.mkdir(parents=True)
            for p in files(folder, area):
                shutil.copy2(p, dest / p.name)
        (snap / 'PR').mkdir()
        for i, pref in enumerate([locations['player'][1], locations['editor'][1]], 1):
            if pref.exists():
                shutil.copy2(regular(pref), snap / 'PR' / f'p{i}.plist')
        states('before', True)
        states('live-at-snapshot')
    elif action == 'sampler-record':
        value = identity(int(extra[0]))
        require(value and value['uid'] == os.getuid() and str(root) in value['command'] and 'userdata-guard.sh' in value['command'], 'sampler not observed in own snapshot lifecycle')
        write_json(root / 'sampler.json', value)
    elif action == 'bind':
        for area in locations:
            require(read_state(root / f'before-{area}.txt', area) == state(area, True), 'snapshot baseline mismatch')
        write_json(root / 'snapshot-manifest.json', {'version': 1, 'context': context(), 'snapshotFiles': snapshot_hashes(), 'reports': {n: digest(root / n) for n in ['before.txt', 'before-editor.txt', 'before-player.txt', 'sampler.json']}})
        (root / 'snapshot-manifest.sha256').write_text(digest(root / 'snapshot-manifest.json') + '\n')
        validate()
    elif action == 'validate':
        validate()
    elif action == 'sampler-close':
        _, original = validate()
        require(not (root / '.sampling').exists(), 'sampler flag still present')
        deadline = time.monotonic() + 8
        while True:
            now = identity(original['pid'])
            if not same_identity(now, original):
                write_json(root / 'sampler-closure.json', {'original': original, 'observedAtClosure': now, 'closed': True, 'signalsSent': 0})
                break
            require(time.monotonic() < deadline, 'own sampler still alive after 8s')
            time.sleep(.1)
    elif action == 'state':
        states(extra[0])
    elif action == 'other':
        validate()
        (root / 'out-of-scope-changed.txt').write_text(other_report())
        write_json(root / 'out-of-scope-bindings.json', {'reports': {n: digest(root / n) for n in ['after-run.txt', 'after-run-editor.txt', 'after-run-player.txt', 'out-of-scope-changed.txt']}})
    elif action == 'restore-files':
        validate()
        for area in areas:
            folder, _, label = locations[area]
            src = root / 'snap' / label
            before_names = {p.name for p in files(src, area)}
            for path in files(folder, area):
                if path.name not in before_names:
                    dest = root / 'created' / label / path.name
                    safe_folder(dest.parent)
                    require(not dest.exists() and not dest.is_symlink(), f'created backup collision: {dest}')
                    shutil.move(str(path), str(dest))
            safe_folder(folder)
            for path in files(src, area):
                target = folder / path.name
                require(not target.is_symlink() and (not target.exists() or target.is_file()), f'unsafe restore target: {target}')
                shutil.copy2(path, target)
    elif action == 'plist-settle':
        domain, disk = extra[0], Path(extra[1])
        for _ in range(50):
            if exported(domain, disk) == plist(disk):
                return
            time.sleep(.2)
        raise ValueError('defaults/disk did not settle after 10s')
    elif action == 'plist-extras':
        domain, back, disk = extra[0], Path(extra[1]), Path(extra[2])
        for key in sorted(set(exported(domain, disk)) - set(plist(back)) - VOLATILE):
            require('\n' not in key and '\r' not in key, 'newline in preference key')
            print(key)
    elif action == 'plist-restore':
        domain, back, disk = extra[0], Path(extra[1]), Path(extra[2])
        for key in sorted(set(exported(domain, disk)) - set(plist(back)) - VOLATILE):
            require(subprocess.run(['defaults', 'delete', domain, key], capture_output=True).returncode == 0, 'defaults delete failed')
        if back.exists():
            # defaults import merges: do not overwrite the live Unity launch counters.
            baseline = {k: v for k, v in plist(back).items() if k not in VOLATILE}
            with tempfile.NamedTemporaryFile(dir=root, prefix='import-', suffix='.plist') as prepared:
                plistlib.dump(baseline, prepared)
                prepared.flush()
                require(subprocess.run(['defaults', 'import', domain, prepared.name], capture_output=True).returncode == 0, 'defaults import failed')
    elif action == 'final':
        m, original = validate()
        closure = json.loads(regular(root / 'sampler-closure.json').read_text())
        require(closure.get('original') == original and closure.get('closed') is True and type(closure.get('signalsSent')) is int and closure.get('signalsSent') == 0, 'invalid closure binding')
        require(not same_identity(identity(original['pid']), original) and not (root / '.sampling').exists(), 'original sampler still present')
        bindings = json.loads(regular(root / 'out-of-scope-bindings.json').read_text())
        names = {'after-run.txt', 'after-run-editor.txt', 'after-run-player.txt', 'out-of-scope-changed.txt'}
        require(set(bindings.get('reports', {})) == names, 'missing OTHER bindings')
        for name, sha in bindings['reports'].items():
            require(digest(root / name) == sha, f'OTHER binding mismatch: {name}')
        rows = other_report()
        require(regular(root / 'out-of-scope-changed.txt').read_text() == rows, 'OTHER report mismatch')
        require(regular(root / 'extra-keys.txt').read_text() == '', 'extra preference keys remain')
        require(regular(root / 'after.txt').read_text() == ''.join((root / f'after-{a}.txt').read_text() for a in locations), 'combined final state mismatch')
        for area in locations:
            current = state(area)
            require(read_state(root / f'after-{area}.txt', area) == current, 'final report/live mismatch')
            require(current == read_state(root / f'{"before" if area in areas else "after-run"}-{area}.txt', area), f'{area} changed or was not restored')
        ok = not rows
        write_json(root / 'guard-verdict.json', {'version': 1, 'status': 'PASS' if ok else 'FAIL', 'scope': scope, 'scopedVerified': True, 'verified': ok, 'samplerClosed': True, 'extraKeys': 0, 'outOfScopeChanged': len(rows.splitlines()), 'snapshotManifestSha256': digest(root / 'snapshot-manifest.json'), 'reports': {n: digest(root / n) for n in ['before.txt', 'after-run.txt', 'after.txt', 'extra-keys.txt', 'out-of-scope-changed.txt', 'out-of-scope-bindings.json', 'sampler-closure.json']}})
        require(ok, 'out-of-scope changes: scoped restore is not full guard PASS')
    elif action == 'failure':
        # Retain a more informative completed FAIL verdict (e.g. foreign delta).
        path = root / 'guard-verdict.json'
        old = {}
        if path.exists():
            try:
                old = json.loads(regular(path).read_text())
            except (ValueError, OSError):
                pass
        if not (extra[0] == 'final guard audit failed' and old.get('status') == 'FAIL' and old.get('verified') is False):
            write_json(path, {'version': 1, 'status': 'FAIL', 'scope': scope, 'verified': False, 'reason': extra[0], 'samplerClosed': 'UNKNOWN'})
    else:
        raise ValueError(f'unknown action: {action}')


if __name__ == '__main__':
    try:
        action, directory, scope, d1, d2, p1, p2, *extra = sys.argv[1:]
        root = Path(directory).absolute()
        require(scope in {'all', 'editor', 'player'}, 'invalid scope')
        require(root.is_dir() and not root.is_symlink(), 'unsafe evidence root')
        for entry in root.iterdir():
            require(not entry.is_symlink() and (entry.is_file() or entry.is_dir()), f'unsafe evidence entry: {entry}')
        locations = {'editor': (Path(d1), Path(p2), 'D1'), 'player': (Path(d2), Path(p1), 'D2')}
        areas = list(locations) if scope == 'all' else [scope]
        run()
    except Exception as exc:
        print(f'userdata-guard audit: {exc}', file=sys.stderr)
        sys.exit(30)
