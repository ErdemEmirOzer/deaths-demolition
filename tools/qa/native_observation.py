"""Read-only macOS process/app observations; no launch, restore, signal or grant.

OWN means that supplied controller evidence matches a bounded observation. The
caller must authenticate that evidence and its grant; this module cannot issue
or authenticate permission. Even OWN never grants authorization or clearance.
No system query occurs on import. Backends are injected for offline fixtures.

proc_pidinfo birth includes microseconds (Darwin proc_bsdinfo, flavor 3);
proc_pidpath identifies the executable independently of argv. ps comm and args
are queried separately with unlimited width. Every failed/ambiguous query stops
the observation; argv is diagnostic text only. Repeated reads bound, but cannot
eliminate, TOCTOU: proc_pidpath does not attest loaded memory or its original
vnode after replacement. Observations expire immediately; no monitoring lease.
"""

import ctypes
from dataclasses import asdict, dataclass
import hashlib
import json
import os
from pathlib import Path
import plistlib
import stat
import subprocess
import sys
import uuid


class ObservationError(ValueError):
    """Missing, contradictory or unresolvable observation."""


def _integer(value, minimum=0):
    return type(value) is int and value >= minimum


def _hex(value, length):
    return (type(value) is str and len(value) == length
            and all(c in '0123456789abcdef' for c in value))


def _path(value):
    value = os.fspath(value)
    if (not value.startswith('/') or any(ord(c) < 32 or ord(c) == 127 for c in value)
            or any(part in ('.', '..') for part in value.split('/'))):
        raise ObservationError('unsafe or nonabsolute path')
    return Path(value)


def _basename(value):
    if (type(value) is not str or not value or value in ('.', '..')
            or '/' in value or '\\' in value
            or any(ord(c) < 32 or ord(c) == 127 for c in value)):
        raise ObservationError('CFBundleExecutable must be a literal safe basename')
    return value


@dataclass(frozen=True)
class FileIdentity:
    sha256: str
    device: int
    inode: int
    size: int
    mode: int
    mtime_ns: int
    ctime_ns: int


def _stat_key(s):
    return (s.st_dev, s.st_ino, s.st_size, s.st_mode, s.st_mtime_ns, s.st_ctime_ns)


def _read_file(path, keep_bytes=False):
    """Hash a regular nonsymlink file; detect descriptor/path replacement."""
    before = path.lstat()
    if not stat.S_ISREG(before.st_mode):
        raise ObservationError('nonregular or symlink file: ' + str(path))
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    try:
        start = os.fstat(fd)
        if _stat_key(start) != _stat_key(before):
            raise ObservationError('file replaced before read')
        digest, chunks = hashlib.sha256(), []
        while True:
            chunk = os.read(fd, 1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
            if keep_bytes:
                chunks.append(chunk)
        end = os.fstat(fd)
        if _stat_key(start) != _stat_key(end) or _stat_key(end) != _stat_key(path.lstat()):
            raise ObservationError('file changed during read')
        identity = FileIdentity(digest.hexdigest(), end.st_dev, end.st_ino,
                                end.st_size, end.st_mode, end.st_mtime_ns, end.st_ctime_ns)
        return identity, b''.join(chunks)
    finally:
        os.close(fd)


@dataclass(frozen=True)
class AppAlias:
    declared: str
    canonical: str


@dataclass(frozen=True)
class AppIdentity:
    declared_app: str
    resolved_app: str
    declared_binary: str
    resolved_binary: str
    executable: str
    bundle_id: str
    info: FileIdentity
    binary: FileIdentity

    @property
    def fingerprint(self):
        return hashlib.sha256(json.dumps(asdict(self), sort_keys=True,
                                         separators=(',', ':')).encode()).hexdigest()


def inspect_app(declared_app, approved_aliases=()):
    """Inspect Info-declared binary; never glob or infer it from bundle name."""
    declared = _path(declared_app)
    canonical = declared.resolve(strict=True)
    if str(declared) != str(canonical):
        if AppAlias(str(declared), str(canonical)) not in approved_aliases:
            raise ObservationError('app alias not explicitly approved')
    if canonical.suffix != '.app' or not canonical.is_dir():
        raise ObservationError('not an app directory')
    for directory in (canonical / 'Contents', canonical / 'Contents/MacOS'):
        if directory.is_symlink() or not directory.is_dir():
            raise ObservationError('app interior directory symlink or missing')
    info_path = canonical / 'Contents/Info.plist'
    info, raw = _read_file(info_path, keep_bytes=True)
    try:
        data = plistlib.loads(raw)
    except Exception as exc:
        raise ObservationError('invalid Info.plist') from exc
    if type(data) is not dict:
        raise ObservationError('Info.plist is not a dictionary')
    executable = _basename(data.get('CFBundleExecutable'))
    bundle_id = data.get('CFBundleIdentifier')
    if (type(bundle_id) is not str or not bundle_id
            or any(ord(c) < 33 or ord(c) == 127 for c in bundle_id)):
        raise ObservationError('invalid bundle identifier')
    binary_path = canonical / 'Contents/MacOS' / executable
    binary, _ = _read_file(binary_path)
    if not binary.mode & 0o111:
        raise ObservationError('binary is not executable')
    if declared.resolve(strict=True) != canonical:
        raise ObservationError('alias retargeted during inspection')
    if _read_file(info_path)[0] != info:
        raise ObservationError('Info changed during inspection')
    return AppIdentity(str(declared), str(canonical),
                       str(declared / 'Contents/MacOS' / executable),
                       str(binary_path), executable, bundle_id, info, binary)


@dataclass(frozen=True)
class ProcessIdentity:
    pid: int
    uid: int
    birth_sec: int
    birth_usec: int
    ppid: int
    pgid: int

    def validate(self):
        if (not _integer(self.pid, 1) or not _integer(self.uid)
                or not _integer(self.birth_sec, 1) or not _integer(self.birth_usec)
                or self.birth_usec >= 1000000 or not _integer(self.ppid)
                or not _integer(self.pgid, 1)):
            raise ObservationError('invalid process identity')


class _BSDInfo(ctypes.Structure):
    # sys/proc_info.h proc_bsdinfo: MAXCOMLEN=16, natural Darwin ABI alignment.
    _fields_ = [(n, ctypes.c_uint32) for n in (
        'flags', 'status', 'xstatus', 'pid', 'ppid', 'uid', 'gid', 'ruid',
        'rgid', 'svuid', 'svgid', 'reserved')]
    _fields_ += [('comm', ctypes.c_char * 16), ('name', ctypes.c_char * 32)]
    _fields_ += [(n, ctypes.c_uint32) for n in (
        'nfiles', 'pgid', 'jobc', 'tdev', 'tpgid')]
    _fields_ += [('nice', ctypes.c_int32), ('birth_sec', ctypes.c_uint64),
                ('birth_usec', ctypes.c_uint64)]


class DarwinBackend:
    """Lazy libproc and separate read-only ps queries, with original results."""
    def __init__(self):
        self.queries = []
        self._library = None

    def _lib(self):
        if sys.platform != 'darwin':
            raise ObservationError('Darwin resolver unavailable')
        if self._library is None:
            lib = ctypes.CDLL('/usr/lib/libproc.dylib', use_errno=True)
            lib.proc_pidpath.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.c_uint32]
            lib.proc_pidpath.restype = ctypes.c_int
            lib.proc_pidinfo.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_uint64,
                                         ctypes.c_void_p, ctypes.c_int]
            lib.proc_pidinfo.restype = ctypes.c_int
            if ctypes.sizeof(_BSDInfo) != 136:
                raise ObservationError('unsupported proc_bsdinfo ABI')
            self._library = lib
        return self._library

    def identity(self, pid):
        if not _integer(pid, 1):
            raise ObservationError('invalid PID')
        info = _BSDInfo()
        ctypes.set_errno(0)
        length = self._lib().proc_pidinfo(pid, 3, 0, ctypes.byref(info), ctypes.sizeof(info))
        self.queries.append({'query': 'proc_pidinfo', 'pid': pid, 'return': length,
                             'errno': ctypes.get_errno(), 'expectedSize': 136})
        if length != 136 or info.pid != pid or info.status in (0, 4):
            raise ObservationError('kernel process identity query failed')
        result = ProcessIdentity(info.pid, info.uid, info.birth_sec,
                                 info.birth_usec, info.ppid, info.pgid)
        result.validate()
        return result

    def executable(self, pid):
        if not _integer(pid, 1):
            raise ObservationError('invalid PID')
        buffer = ctypes.create_string_buffer(4096)  # PROC_PIDPATHINFO_MAXSIZE
        ctypes.set_errno(0)
        length = self._lib().proc_pidpath(pid, buffer, len(buffer))
        self.queries.append({'query': 'proc_pidpath', 'pid': pid, 'return': length,
                             'errno': ctypes.get_errno(), 'bufferSize': len(buffer)})
        if length <= 0 or length >= len(buffer) or b'\0' not in buffer.raw:
            raise ObservationError('kernel executable query failed or truncated')
        try:
            value = buffer.value.decode('utf-8', errors='strict')
        except UnicodeError as exc:
            raise ObservationError('invalid executable encoding') from exc
        return str(_path(value))

    def text(self, pid, field):
        if not _integer(pid, 1) or field not in ('comm', 'args'):
            raise ObservationError('invalid ps query')
        result = subprocess.run(['/bin/ps', '-ww', '-p', str(pid), '-o', field + '='],
                                capture_output=True, timeout=5, check=False,
                                env={**os.environ, 'LC_ALL': 'C'})
        self.queries.append({'query': 'ps.' + field, 'pid': pid,
                             'originalExit': result.returncode,
                             'stderr': result.stderr.decode('utf-8', errors='replace')})
        if result.returncode != 0 or result.stderr:
            raise ObservationError('ps query failed')
        try:
            value = result.stdout.decode('utf-8', errors='strict').removesuffix('\n')
        except UnicodeError as exc:
            raise ObservationError('invalid ps encoding') from exc
        if (not value or '\n' in value or '\r' in value or '\0' in value
                or len(result.stdout) >= 1024 * 1024):
            raise ObservationError('missing, multiline or oversized ps result')
        return value


@dataclass(frozen=True)
class ControllerBinding:
    """Caller-authenticated, exact controlled actor evidence; not a new grant."""
    process: ProcessIdentity
    app_fingerprint: str
    source_sha: str
    grant_sha256: str
    attempt_id: str


@dataclass(frozen=True)
class ObservationContext:
    app: AppIdentity
    source_sha: str
    approved_aliases: tuple = ()
    grant_sha256: str = ''
    attempt_id: str = ''
    controlled: ControllerBinding = None


@dataclass(frozen=True)
class Observation:
    pid: int
    classification: str
    reason: str
    family: str = ''
    process: ProcessIdentity = None
    comm: str = ''
    args: str = ''
    executable: str = ''
    file: FileIdentity = None
    app: AppIdentity = None
    authorization_granted: bool = False


def _family(binary, app, expected):
    if app is not None:
        if app.executable == 'Unity':
            return 'UNITY'
        if app.executable in ('Blender', 'blender'):
            return 'BLENDER'
        if app.executable == expected.executable or app.bundle_id == expected.bundle_id:
            return 'PLAYER'
        return 'OTHER_APP'
    if binary.name in ('Unity', 'Blender', 'blender', expected.executable):
        raise ObservationError('native candidate lacks consistent app metadata')
    return 'NON_NATIVE'


def _binding_matches(context, identity):
    binding = context.controlled
    if binding is None or binding.process.pid != identity.pid:
        return False
    binding.process.validate()
    if binding.process != identity:
        raise ObservationError('controlled PID identity changed or reused')
    try:
        valid_attempt = str(uuid.UUID(context.attempt_id)) == context.attempt_id
    except (ValueError, AttributeError):
        valid_attempt = False
    if (not _hex(context.grant_sha256, 64) or not valid_attempt
            or binding.app_fingerprint != context.app.fingerprint
            or binding.source_sha != context.source_sha
            or binding.grant_sha256 != context.grant_sha256
            or binding.attempt_id != context.attempt_id):
        raise ObservationError('controller/source/grant/app binding contradiction')
    return True


def observe(pid, context, backend):
    """One bounded observation. All uncertainty is UNKNOWN_STOP, never CLEAR."""
    try:
        if not _integer(pid, 1) or not _hex(context.source_sha, 40):
            raise ObservationError('invalid PID or exact source SHA')
        before_app = inspect_app(context.app.declared_app, context.approved_aliases)
        if before_app != context.app:
            raise ObservationError('pinned app metadata/hash/inode drift')
        before = backend.identity(pid)
        before.validate()
        if before.pid != pid:
            raise ObservationError('queried PID mismatch')
        comm, args = backend.text(pid, 'comm'), backend.text(pid, 'args')
        if (type(args) is not str or not args or '\n' in args or '\0' in args
                or len(args) >= 1024 * 1024):
            raise ObservationError('invalid or truncated args')
        raw_path = _path(backend.executable(pid))
        binary_path = raw_path.resolve(strict=True)
        # No unapproved binary aliases: kernel path must designate the literal
        # executable, except the explicitly pinned app alias.
        if raw_path != binary_path and str(raw_path) != before_app.declared_binary:
            raise ObservationError('unapproved executable alias')
        comm_path = _path(comm)
        if (comm_path != comm_path.resolve(strict=True)
                and str(comm_path) != before_app.declared_binary):
            raise ObservationError('unapproved comm alias')
        if comm_path.resolve(strict=True) != binary_path:
            raise ObservationError('comm contradicts kernel executable or is truncated')
        executable_file, _ = _read_file(binary_path)
        if not executable_file.mode & 0o111:
            raise ObservationError('resolved executable is not executable')
        actor_app = None
        if (binary_path.parent.name == 'MacOS'
                and binary_path.parent.parent.name == 'Contents'
                and binary_path.parent.parent.parent.suffix == '.app'):
            actor_app = inspect_app(str(binary_path.parent.parent.parent))
            if actor_app.resolved_binary != str(binary_path):
                raise ObservationError('kernel executable contradicts actor Info.plist')
        if str(binary_path) == before_app.resolved_binary:
            if executable_file != before_app.binary:
                raise ObservationError('pinned binary identity contradiction')
            family = 'PLAYER'
        else:
            family = _family(binary_path, actor_app, before_app)
        # Repeat independent path/process/app/file observations after metadata.
        second_path = _path(backend.executable(pid))
        if second_path != raw_path or second_path.resolve(strict=True) != binary_path:
            raise ObservationError('kernel executable changed')
        after = backend.identity(pid)
        after.validate()
        if before != after:
            raise ObservationError('PID/UID/birth/PPID/PGID changed')
        if backend.text(pid, 'comm') != comm:
            raise ObservationError('comm changed')
        if _read_file(binary_path)[0] != executable_file:
            raise ObservationError('executable hash/inode drift')
        if actor_app is not None and inspect_app(actor_app.declared_app) != actor_app:
            raise ObservationError('actor app changed')
        if inspect_app(context.app.declared_app, context.approved_aliases) != before_app:
            raise ObservationError('pinned app changed during observation')
        controlled = _binding_matches(context, before)
        if controlled and str(binary_path) != before_app.resolved_binary:
            raise ObservationError('controlled actor executable contradiction')
        classification = ('OWN' if controlled else 'FOREIGN') if family in (
            'UNITY', 'BLENDER', 'PLAYER') else 'NON_NATIVE'
        reason = ('exact caller-supplied controller binding' if classification == 'OWN'
                  else 'no exact controlled native actor' if classification == 'FOREIGN'
                  else 'kernel executable is outside native candidate families')
        return Observation(pid, classification, reason, family, before, comm, args,
                           str(binary_path), executable_file, actor_app)
    except Exception as exc:
        # A vanished PID, denied query, missing file, unsupported platform, or
        # malformed backend result cannot silently become absence/clearance.
        return Observation(pid, 'UNKNOWN_STOP', type(exc).__name__ + ': ' + str(exc))


def observe_many(pids, context, backend):
    """Explicit caller inventory only; this result does not attest global absence."""
    observations = tuple(observe(pid, context, backend) for pid in pids)
    return {'state': 'UNKNOWN_STOP' if not observations or any(
        o.classification == 'UNKNOWN_STOP' for o in observations) else 'OBSERVED',
        'observations': observations, 'scope': 'supplied PIDs only',
        'authorizationGranted': False, 'nativeRuntimePass': False}
