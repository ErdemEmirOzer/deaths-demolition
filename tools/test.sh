#!/bin/zsh
# WP-051 · tools/test.sh {editmode|playmode} [--filter <regex>] [--out DIR] [--timeout-min N] [--no-wait] [--extra-arg TOKEN]…
#
# One command for Unity test runs with a RUNNER-LEVEL time limit: NUnit's [Timeout] only fires between
# frames on the main thread, so a test stuck inside one blocking call (or an editor stuck after the run)
# hangs forever — seen as 25–47 min "runs". This wrapper takes a machine slot (tools/slot.sh, FIFO),
# starts the editor, and kills it when --timeout-min passes (default: editmode 20, playmode 45), then
# prints the per-test duration table (tools/test-durations.py) from the XML.
# Output: <out>/<mode>.xml, <out>/<mode>.log, <out>/durations.json. Default <out> = $DD_BUILD_ROOT/tests-<mode>-<utc>.
# Exit: 0 all passed · 1 failures (see table) · 2 no XML produced · 124 killed by the watchdog · 64 usage · 75 busy (--no-wait)
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
MODE="${1:-}"; shift 2>/dev/null || true
[ "$MODE" = "editmode" ] || [ "$MODE" = "playmode" ] || { echo "usage: test.sh {editmode|playmode} [--filter re] [--out DIR] [--timeout-min N] [--no-wait] [--extra-arg TOKEN]…" >&2; exit 64; }
FILTER=""; OUT=""; TMIN=0; NOWAIT=0; DIAG_ARGS=(); typeset -A S06_META S06_META_SEEN DIAG_SEEN
while [ $# -gt 0 ]; do
  case "$1" in
    --filter|--out|--timeout-min|--extra-arg)
      [ $# -ge 2 ] || { echo "missing value for $1" >&2; exit 64; }
      case "$1" in
        --filter) FILTER="$2" ;;
        --out) OUT="$2" ;;
        --timeout-min) TMIN="$2" ;;
        --extra-arg) DIAG_ARGS+=("$2") ;;
      esac
      shift 2 ;;
    --s06-source-sha256|--s06-input-json|--s06-input-sha256|--s06-execution-id|--s06-action-id)
      [ $# -ge 2 ] || { echo "missing value for $1" >&2; exit 64; }
      [ -z "${S06_META_SEEN[$1]:-}" ] || { echo "duplicate option $1" >&2; exit 64; }
      S06_META_SEEN[$1]=1; S06_META[$1]="$2"; shift 2 ;;
    --extra-arg=*) DIAG_ARGS+=("${1#--extra-arg=}"); shift ;;
    --no-wait) NOWAIT=1; shift ;;
    *) echo "unknown option: $1" >&2; exit 64 ;;
  esac
done

# Narrow argv contract for existing opt-in PlayMode observers. Tokens are
# passed as an array, never evaluated, split, or resolved from environment strings.
# GameFeel/DEC identity values record caller claims for evidence intake. S06
# additionally binds its actual consumer file and read-only P06 prerequisite.
diag_usage() { echo "diagnostic arguments: $*" >&2; exit 64; }
if [ ${#DIAG_ARGS[@]} -gt 0 ]; then
  [ "$MODE" = "playmode" ] || diag_usage "PlayMode only"
  d_index=1
  while [ "$d_index" -le ${#DIAG_ARGS[@]} ]; do
    d_key="${DIAG_ARGS[$d_index]}"
    [ -z "${DIAG_SEEN[$d_key]:-}" ] || diag_usage "duplicate $d_key"
    case "$d_key" in
      -ddGameFeelGateDiag|-ddDec082Diag)
        DIAG_SEEN[$d_key]=enabled ;;
      -ddGateDiagSourceCommit|-ddGateDiagInputHash|-ddGateDiagExecutionId|-ddGateDiagSourceHash|-ddGateDiagFixtureHash|-ddDecDiagSourceHash|-ddDecDiagFixtureHash|-ddS06PalmProof|-ddS06PalmSourceSha|-ddS06CorrespondenceProof)
        d_index=$((d_index+1))
        [ "$d_index" -le ${#DIAG_ARGS[@]} ] || diag_usage "missing value for $d_key"
        d_value="${DIAG_ARGS[$d_index]}"
        case "$d_key" in
          -ddS06PalmProof|-ddS06CorrespondenceProof)
            [ -n "$d_value" ] || diag_usage "empty S06 output path" ;;
          -ddGateDiagSourceCommit|-ddS06PalmSourceSha)
            [[ "$d_value" =~ '^[0-9a-f]{40}$' ]] && [ -n "${d_value//0/}" ] || diag_usage "invalid source commit" ;;
          -ddGateDiagExecutionId)
            [[ "$d_value" =~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$' ]] || diag_usage "invalid execution id"
            case "${(L)d_value}" in unknown|missing|null|none|undefined) diag_usage "unbound execution id" ;; esac ;;
          *)
            [[ "$d_value" =~ '^[0-9a-f]{64}$' ]] && [ -n "${d_value//0/}" ] || diag_usage "invalid SHA256 for $d_key" ;;
        esac
        DIAG_SEEN[$d_key]="$d_value" ;;
      *) diag_usage "unsupported token $d_key" ;;
    esac
    d_index=$((d_index+1))
  done
  if [ -n "${DIAG_SEEN[-ddGameFeelGateDiag]:-}${DIAG_SEEN[-ddDec082Diag]:-}" ]; then
    [ -z "${DIAG_SEEN[-ddS06PalmProof]:-}${DIAG_SEEN[-ddS06PalmSourceSha]:-}${DIAG_SEEN[-ddS06CorrespondenceProof]:-}" ] || diag_usage "S06 requires its own single diagnostic action"
  for d_key in -ddGateDiagSourceCommit -ddGateDiagInputHash -ddGateDiagExecutionId; do
    [ -n "${DIAG_SEEN[$d_key]:-}" ] || diag_usage "required $d_key"
  done
  for d_observer in GameFeelGate Dec082; do
    if [ "$d_observer" = GameFeelGate ]; then d_prefix=Gate; else d_prefix=Dec; fi
    for d_suffix in SourceHash FixtureHash; do
      d_key="-dd${d_prefix}Diag${d_suffix}"
      if [ -n "${DIAG_SEEN[-dd${d_observer}Diag]:-}" ]; then
        [ -n "${DIAG_SEEN[$d_key]:-}" ] || diag_usage "required $d_key"
      else
        [ -z "${DIAG_SEEN[$d_key]:-}" ] || diag_usage "$d_key requires its observer"
      fi
    done
  done
  else
    for d_key in -ddGateDiagSourceCommit -ddGateDiagInputHash -ddGateDiagExecutionId -ddGateDiagSourceHash -ddGateDiagFixtureHash -ddDecDiagSourceHash -ddDecDiagFixtureHash; do
      [ -z "${DIAG_SEEN[$d_key]:-}" ] || diag_usage "$d_key requires GameFeel/DEC observer"
    done
    for d_key in -ddS06PalmProof -ddS06PalmSourceSha -ddS06CorrespondenceProof; do
      [ -n "${DIAG_SEEN[$d_key]:-}" ] || diag_usage "required $d_key"
    done
  fi
fi
[ "$TMIN" -gt 0 ] || TMIN=$( [ "$MODE" = "editmode" ] && echo 20 || echo 45 )
[ -n "$OUT" ] || OUT="$DD_BUILD_ROOT/tests-$MODE-$(dd_utc)"
# S06 wrapper metadata binds read-only prerequisite inputs. Only the three
# actual S06 native arguments are forwarded; P06 data is not runtime-consumed.
S06_RECEIPT=""
if [ -n "${DIAG_SEEN[-ddS06PalmProof]:-}" ]; then
  for d_key in --s06-source-sha256 --s06-input-json --s06-input-sha256 --s06-execution-id --s06-action-id; do
    [ -n "${S06_META[$d_key]:-}" ] || diag_usage "required $d_key"
  done
  S06_RECEIPT=$(python3 - "$DD_REPO" "$DD_PROJECT" "$OUT" "$FILTER" \
    "${DIAG_SEEN[-ddS06PalmProof]}" "${DIAG_SEEN[-ddS06CorrespondenceProof]}" "${DIAG_SEEN[-ddS06PalmSourceSha]}" \
    "${S06_META[--s06-source-sha256]}" "${S06_META[--s06-input-json]}" "${S06_META[--s06-input-sha256]}" \
    "${S06_META[--s06-execution-id]}" "${S06_META[--s06-action-id]}" <<'S06_PY'
import hashlib, json, math, os, pathlib, re, stat, subprocess, sys, unicodedata

def reject(message):
    raise ValueError(message)

def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            reject("duplicate JSON key")
        result[key] = value
    return result

def finite_float(text):
    value = float(text)
    if not math.isfinite(value):
        reject("non-finite JSON number")
    return value

def invalid_constant(text):
    reject("non-finite JSON constant")

def sha256(data):
    return hashlib.sha256(data).hexdigest()

def digest(value, length):
    return bool(re.fullmatch("[0-9a-f]{" + str(length) + "}", value) and set(value) != {"0"})

def path_key(path):
    return unicodedata.normalize("NFD", str(path)).casefold()

def clean_path(raw, absolute=False):
    # POSIX preserves exactly two leading slashes in some path operations;
    # reject that grammar so all ancestor/protected keys use a single root.
    if raw.startswith("//"):
        reject("multiple leading slashes are not canonical output paths")
    if not raw or any(c in raw for c in "\x00\r\n") or any(p in (".", "..") for p in raw.split("/")):
        reject("ambiguous/escaped output path")
    if absolute and not pathlib.Path(raw).is_absolute():
        reject("native output path must be absolute")
    path = pathlib.Path(os.path.abspath(raw))
    for ancestor in (path, *path.parents):
        if ancestor.is_symlink():
            reject("symlink or dangling ancestor")
        if ancestor != path and ancestor.exists() and not ancestor.is_dir():
            reject("non-directory ancestor")
    return path

try:
    repo, project, out, selected_filter, palm, table, origin, source_hash, data_path, data_hash, execution, action = sys.argv[1:]
    full_id = "DD.Art.Tests.PlayMode.AnimationTests.S06_LegacyAr1_ImportedSurface_OnePostHumanoidFrame"
    if action != full_id or selected_filter != full_id:
        reject("S06 action and explicit exact test filter must match the consumer")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,127}", execution) or execution.lower() in {"unknown", "missing", "null", "none", "undefined"}:
        reject("invalid S06 execution identity")
    if not digest(origin, 40) or not digest(source_hash, 64) or not digest(data_hash, 64):
        reject("invalid source/input digest")
    output_root = clean_path(out)
    if output_root.exists() and (not output_root.is_dir() or any(output_root.iterdir())):
        reject("S06 output root must be fresh/empty")
    outputs = [clean_path(palm, True), clean_path(table, True)]
    protected = [pathlib.Path.home() / "Library" / "Application Support", pathlib.Path.home() / "Library" / "Preferences"]
    guard_root = os.environ.get("DD_GUARD_ROOT")
    if guard_root:
        protected.extend([pathlib.Path(guard_root) / "AS", pathlib.Path(guard_root) / "PR"])
    forbidden_names = {"prototype-profile.json", "ui-settings.json", "load-timing.json", "input-settings.json", "input-overrides.json", "ui-hints.json"}
    for target in [output_root, *outputs]:
        key = path_key(target)
        if any(key == path_key(p) or key.startswith(path_key(p) + "/") for p in protected):
            reject("user profile/preferences cannot be an output")
    for target in outputs:
        if target.suffix.lower() != ".json" or target.name.lower() in forbidden_names:
            reject("proof must be a diagnostic JSON target")
        if output_root not in target.parents:
            reject("proof outside selected output root")
        if os.path.lexists(target):
            reject("existing proof cannot be overwritten")
    keys = [path_key(p) for p in outputs]
    if keys[0] == keys[1] or any(a.startswith(b + "/") for a, b in (keys, keys[::-1])):
        reject("proof paths alias/conflict with each other")
    reserved = [output_root / name for name in ("s06-argv-preflight.json", "playmode.xml", "playmode.log", "durations.json", "timeline.txt")]
    if any(key == path_key(p) or key.startswith(path_key(p) + "/") for key in keys for p in reserved):
        reject("proof conflicts with wrapper artifacts")
    ancestor_identities = {}
    for target in [output_root, *outputs]:
        for ancestor in (target, *target.parents):
            try:
                observed = ancestor.lstat()
            except FileNotFoundError:
                continue
            if not stat.S_ISDIR(observed.st_mode):
                reject("output ancestor is no longer a regular directory")
            ancestor_identities[str(ancestor)] = {"path": str(ancestor), "device": observed.st_dev, "inode": observed.st_ino}
    relative_source = "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Tests/PlayMode/AnimationTests.cs"
    source = pathlib.Path(project) / "Assets/_Art/Tests/PlayMode/AnimationTests.cs"
    if not source.is_file() or source.is_symlink():
        reject("S06 consumer source must be a regular file")
    source_bytes = source.read_bytes()
    committed = subprocess.check_output(["git", "-C", repo, "show", origin + ":" + relative_source], stderr=subprocess.PIPE)
    if sha256(source_bytes) != source_hash or sha256(committed) != source_hash:
        reject("source commit/file hash do not bind the actual consumer")
    # Byte identities reviewed for this exact three-consumer action. Authentic
    # caller-bound older/arbitrary code is not a supported capability version.
    # A changed consumer must be reviewed and registered explicitly here.
    reviewed_consumers = {"f332e089720218a6b655da828c1ce041cdd34d4f200953bd923d51a131c183fe":
                          "s06.same-frame-imported-baked-arrays.v1",
                          "34a2227ae7f801b7bb594425a265b8f27a6b5b01a189eef5d54162f2850240a6":
                          "s06.same-frame-imported-baked-arrays.v1"}
    if source_hash not in reviewed_consumers:
        reject("consumer bytes have no reviewed correspondence capability")
    input_path = pathlib.Path(data_path)
    if not input_path.is_absolute() or input_path.is_symlink() or not input_path.is_file():
        reject("P06 canonical JSON must be an absolute regular input")
    data = input_path.read_bytes()
    if sha256(data) != data_hash:
        reject("P06 input hash mismatch")
    package = json.loads(data, object_pairs_hook=unique, parse_float=finite_float, parse_constant=invalid_constant)
    if not isinstance(package, dict) or package.get("schema") != "AP022.sha-bound-anatomical-source-package.v001" or package.get("package") != "AP-022-ANATOMICAL-SOURCE-BIND":
        reject("wrong P06 canonical input contract")
    if not isinstance(package.get("source_identity"), dict) or not isinstance(package.get("source_anatomical_cp_mask"), list) or not isinstance(package.get("ordered_source_triangle_anchor"), dict):
        reject("wrong P06 canonical field types")
    execution_head = subprocess.check_output(["git", "-C", repo, "rev-parse", "HEAD"], text=True, stderr=subprocess.PIPE).strip()
    if not digest(execution_head, 40):
        reject("unknown execution HEAD")
    print(json.dumps({"actionId": action, "executionId": execution, "executionCommit": execution_head,
                      "observerSourceCommit": origin, "observerFileSha256": source_hash,
                      "observerCapability": reviewed_consumers[source_hash],
                      "p06InputPath": str(input_path), "p06InputSha256": data_hash,
                      "p06Schema": package["schema"], "p06RuntimeConsumed": False,
                      "selectedOut": str(output_root), "nativeProofTargets": [str(p) for p in outputs],
                      "outputAncestorIdentities": list(ancestor_identities.values()),
                      "validation": "read-only preflight only, not runtime proof or acceptance"}, sort_keys=True))
except Exception as error:
    print("S06 preflight rejected: " + str(error), file=sys.stderr)
    sys.exit(64)
S06_PY
  ) || diag_usage "S06 preflight failed"
elif [ ${#S06_META[@]} -gt 0 ]; then
  diag_usage "S06 metadata requires its observer arguments"
fi
if [ -n "$S06_RECEIPT" ]; then
  mkdir -p "$OUT"
  S06_IO_EXIT=$?
  [ "$S06_IO_EXIT" -eq 0 ] || exit "$S06_IO_EXIT"
  # Bind all output ancestors again after mkdir. Receipt operations use a
  # nofollow directory descriptor; repeat identity checks before the slot.
  S06_OUTPUT_HELPER=$(cat <<'S06_OUTPUT_PY'
import errno, json, os, pathlib, secrets, stat, sys
phase, raw_out, raw_receipt = sys.argv[1:4]
receipt = json.loads(raw_receipt)
root = pathlib.Path(os.path.abspath(raw_out))
root_fd = None
name = None
io_exit = 0
flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
anchors = {v['path']: (v['device'], v['inode']) for v in receipt['outputAncestorIdentities']}
expected_output = json.loads(sys.argv[4]) if phase == 'verify' else None
if expected_output is not None:
    anchors.update({v['path']: (v['device'], v['inode']) for v in expected_output['ancestors']})
observed_ancestors = {}

def identity(fd):
    value = os.fstat(fd)
    return {'device': value.st_dev, 'inode': value.st_ino}

def match_anchor(path, fd):
    observed = identity(fd)
    observed_ancestors[str(path)] = dict(observed, path=str(path))
    if str(path) in anchors:
        if (observed['device'], observed['inode']) != anchors[str(path)]:
            raise OSError(errno.ESTALE, 'output ancestor identity changed', str(path))

def open_root():
    current = pathlib.Path('/')
    fd = os.open('/', flags)
    try:
        match_anchor(current, fd)
        for part in root.parts[1:]:
            next_fd = os.open(part, flags, dir_fd=fd)
            os.close(fd); fd = next_fd
            current = current / part
            match_anchor(current, fd)
        return fd
    except Exception:
        os.close(fd)
        raise

def check_proof_targets(fd):
    for raw_target in receipt['nativeProofTargets']:
        target = pathlib.Path(raw_target)
        parts = target.relative_to(root).parts
        if not parts:
            raise ValueError('proof aliases output directory')
        parent_fd = os.dup(fd)
        current = root
        try:
            for part in parts[:-1]:
                try:
                    next_fd = os.open(part, flags, dir_fd=parent_fd)
                except FileNotFoundError:
                    if str(current / part) in anchors:
                        raise  # previously bound ancestor must still exist
                    break  # remaining proof ancestor directories are still absent
                os.close(parent_fd); parent_fd = next_fd
                current = current / part
                match_anchor(current, parent_fd)
            else:
                try:
                    os.stat(parts[-1], dir_fd=parent_fd, follow_symlinks=False)
                except FileNotFoundError:
                    continue
                raise OSError(errno.EEXIST, 'proof target is no longer fresh', raw_target)
        finally:
            os.close(parent_fd)

try:
    if str(root) != receipt['selectedOut']:
        raise ValueError('output directory differs from validated path')
    root_fd = open_root()  # each component is opened relative to its parent, nofollow
    observed = identity(root_fd)
    check_proof_targets(root_fd)
    bound = [observed_ancestors[k] for k in sorted(observed_ancestors)]
    receipt = dict(receipt, outputDirectoryIdentity=observed, outputBoundAncestorIdentities=bound)
    data = (json.dumps(receipt, sort_keys=True) + '\n').encode('utf-8')
    if phase == 'publish':
        candidate_name = '.s06-argv-preflight-' + secrets.token_hex(16)
        fd = os.open(candidate_name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=root_fd)
        name = candidate_name  # cleanup only the file this invocation created
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data); stream.flush(); os.fsync(stream.fileno())
        os.link(name, 's06-argv-preflight.json', src_dir_fd=root_fd, dst_dir_fd=root_fd, follow_symlinks=False)
    elif phase == 'verify':
        if observed != expected_output['directory'] or bound != expected_output['ancestors']:
            raise OSError(errno.ESTALE, 'output directory identity changed after publication')
    else:
        raise ValueError('unknown output preflight phase')
    fd = os.open('s06-argv-preflight.json', os.O_RDONLY | os.O_NOFOLLOW, dir_fd=root_fd)
    with os.fdopen(fd, 'rb') as stream:
        if not stat.S_ISREG(os.fstat(stream.fileno()).st_mode) or stream.read() != data:
            raise OSError(errno.EIO, 'receipt readback mismatch')
    if phase == 'publish':
        print(json.dumps({'directory': observed, 'ancestors': bound}, sort_keys=True))
except OSError as error:
    print('S06 output I/O rejected: ' + str(error), file=sys.stderr)
    io_exit = error.errno if error.errno and 0 < error.errno < 126 else 1
except ValueError as error:
    print('S06 output preflight rejected: ' + str(error), file=sys.stderr)
    io_exit = 64
finally:
    if name is not None and root_fd is not None:
        try:
            os.unlink(name, dir_fd=root_fd)
        except OSError as error:
            print('S06 receipt cleanup rejected: ' + str(error), file=sys.stderr)
            if not io_exit:
                io_exit = error.errno if error.errno and 0 < error.errno < 126 else 1
    if root_fd is not None:
        os.close(root_fd)
sys.exit(io_exit)
S06_OUTPUT_PY
)
  S06_OUTPUT_IDENTITY=$(python3 -c "$S06_OUTPUT_HELPER" publish "$OUT" "$S06_RECEIPT")
  S06_IO_EXIT=$?
  [ "$S06_IO_EXIT" -eq 0 ] || exit "$S06_IO_EXIT"
  [ -f "$OUT/s06-argv-preflight.json" ] && [ ! -L "$OUT/s06-argv-preflight.json" ] || exit 74
else
  mkdir -p "$OUT"
fi
DD_TIMELINE="$OUT/timeline.txt"; XML="$OUT/$MODE.xml"; LOG="$OUT/$MODE.log"
dd_log "test.sh mode=$MODE filter='${FILTER:-all}' timeout=${TMIN}min out=$OUT head=$(git -C "$DD_REPO" rev-parse --short=12 HEAD 2>/dev/null)"
# WP-052 note (S-02 finding): the Editor test runner leaves ProfilerRecorder.LastValue at 0 while
# Application.targetFrameRate == 60; tests that read recorders must set targetFrameRate = -1 in SetUp.
[ "$MODE" = "playmode" ] && dd_log "note: ProfilerRecorder-based tests need Application.targetFrameRate = -1 in SetUp (LastValue stays 0 at 60 in the Editor runner)"
if [ "$NOWAIT" -eq 1 ] && [ "$(dd_unity_count)" -ge "$DD_MAX_UNITY" ]; then dd_log "busy"; exit 75; fi
if [ -n "$S06_RECEIPT" ]; then
  python3 -c "$S06_OUTPUT_HELPER" verify "$OUT" "$S06_RECEIPT" "$S06_OUTPUT_IDENTITY"
  S06_IO_EXIT=$?
  [ "$S06_IO_EXIT" -eq 0 ] || exit "$S06_IO_EXIT"
fi
trap 'dd_slot_release' EXIT
dd_slot_acquire
PLAT=$( [ "$MODE" = "editmode" ] && echo EditMode || echo PlayMode ); NOGFX=(); [ "$MODE" = "editmode" ] && NOGFX=(-nographics)
FARGS=(); [ -n "$FILTER" ] && FARGS=(-testFilter "$FILTER")
T0=$(date +%s)
# watchdogs (WP-051D dd_unity_watched): total limit TMIN per attempt + log-stall (20 min silent → stop that pid, retry once)
dd_unity_watched "$LOG" "$TMIN" "$DD_UNITY" -batchmode "${NOGFX[@]}" -forgetProjectPath -projectPath "$DD_PROJECT" -runTests -testPlatform "$PLAT" "${FARGS[@]}" -testResults "$XML" -logFile "$LOG" "${DIAG_ARGS[@]}"
X=$?
T1=$(date +%s)
KILLED=0; case "$DD_UNITY_OUTCOME" in killed|stalled) KILLED=1 ;; esac
[ "$DD_UNITY_OUTCOME" != ok ] && dd_log "unity outcome=$DD_UNITY_OUTCOME"
ERRS=$(grep -c 'error CS' "$LOG" 2>/dev/null); ERRS=${ERRS:-0}
if [ ! -f "$XML" ]; then dd_log "$MODE exit=$X seconds=$((T1-T0)) errorsCS=$ERRS NO XML killed=$KILLED"; [ "$KILLED" -eq 1 ] && exit 124; exit 2; fi
SUM=$(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$XML" | head -1)
dd_log "$MODE exit=$X seconds=$((T1-T0)) errorsCS=$ERRS $SUM killed=$KILLED"
python3 "$DD_TOOLS_DIR/test-durations.py" "$XML" --json "$OUT/durations.json" --top 15 | tee -a "$DD_TIMELINE"
[ "$KILLED" -eq 1 ] && exit 124
grep -q 'failed="0"' <<< "$SUM" && [ "$X" -eq 0 ] && exit 0
exit 1
