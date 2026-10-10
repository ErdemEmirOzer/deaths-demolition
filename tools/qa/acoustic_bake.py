#!/usr/bin/env python3
"""WP-099 R3 (C-00 10.10) · is DD_AcousticSpaces.asset baked from acoustic-spaces-v6.json? Unity-free.

AudioCatalogBuilder.BuildAcousticSpaces (prototype/…/Audio/Editor/AudioCatalogBuilder.cs) re-bakes the map whenever its
_sourceHash differs from sha256(File.ReadAllText(json) as UTF-8)[:16]. A stale pair makes every Unity run rewrite a tracked
file: main-ci refuses its dirty CI checkout (exit 3), a FINAL tree goes dirty. One check for every caller:
  acoustic_bake.py files <json> <map>   two files (a missing json = nothing to bake = ok)
  acoustic_bake.py tree <repo> <rev>    the pair in commit <rev> (integrate.sh gate 4c's model is built by the caller)
  acoustic_bake.py index <repo>         the pair in <repo>'s index (integrate.sh after `cherry-pick --no-commit`)
Exit 0 baked (or no json) · 1 not baked — the reason on stdout · 2 usage / git error."""
import hashlib
import re
import subprocess
import sys

# the same paths as AudioCatalogBuilder.AcousticSourcePath / AcousticMapPath, under the repo root (pinned by a test)
JSON_PATH = "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Environment/World/V6/acoustic-spaces-v6.json"
MAP_PATH = "prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Audio/Resources/DD_AcousticSpaces.asset"
FIX = "re-bake (AudioCatalogBuilder.BuildAcousticSpaces) and commit the asset with the json"
OTHER_BOMS = (b"\xff\xfe", b"\xfe\xff", b"\x00\x00\xfe\xff")


def source_hash(json_bytes):
    """sha256 of the text File.ReadAllText returns, re-encoded by Encoding.UTF8.GetBytes: a UTF-8 BOM is dropped, nothing
    added. (None, why) when the bytes are not BOM-less-or-UTF-8-BOM UTF-8 — the C# would decode them differently."""
    b = json_bytes[3:] if json_bytes.startswith(b"\xef\xbb\xbf") else json_bytes
    if json_bytes.startswith(OTHER_BOMS):
        return None, "acoustic-spaces-v6.json carries a UTF-16/32 BOM - save it as UTF-8 (GreyboxV6Builder writes UTF-8)"
    try:
        b.decode("utf-8")
    except UnicodeDecodeError:
        return None, "acoustic-spaces-v6.json is not valid UTF-8 - save it as UTF-8 (GreyboxV6Builder writes UTF-8)"
    return hashlib.sha256(b).hexdigest()[:16], ""


def map_hash(map_bytes):
    m = re.search(rb"^[ \t]*_sourceHash:[ \t]*(\S*)[ \t]*$", map_bytes, re.M)
    return m.group(1).decode("ascii", "replace") if m and m.group(1) else "(none)"


def verdict(json_bytes, map_bytes):
    """(ok, why) for one pair of file contents (None = the file does not exist)."""
    if json_bytes is None:
        return True, "no acoustic-spaces-v6.json: nothing to bake"
    if map_bytes is None:
        return False, "acoustic-spaces-v6.json without DD_AcousticSpaces.asset - " + FIX
    want, why = source_hash(json_bytes)
    if want is None:
        return False, why
    got = map_hash(map_bytes)
    if got != want:
        return False, "DD_AcousticSpaces.asset _sourceHash %s != sha256(acoustic-spaces-v6.json)[:16] %s - %s" % (got, want, FIX)
    return True, "DD_AcousticSpaces.asset baked from acoustic-spaces-v6.json (%s)" % want


def git_blob(repo, spec):
    """bytes of `git show <spec>`, None when the path is not there (any other git failure raises)."""
    r = subprocess.run(["git", "-C", repo, "cat-file", "-e", spec], capture_output=True)
    if r.returncode != 0:
        return None
    r = subprocess.run(["git", "-C", repo, "show", spec], capture_output=True)
    if r.returncode != 0:
        raise RuntimeError("git show %s: %s" % (spec, r.stderr.decode(errors="replace").strip()))
    return r.stdout


def read(path):
    try:
        with open(path, "rb") as f:
            return f.read()
    except FileNotFoundError:
        return None


def main(argv):
    try:
        if len(argv) == 3 and argv[0] == "files":
            ok, why = verdict(read(argv[1]), read(argv[2]))
        elif len(argv) == 3 and argv[0] == "tree":
            subprocess.run(["git", "-C", argv[1], "rev-parse", "--verify", "-q", argv[2] + "^{commit}"], check=True,
                           capture_output=True)
            ok, why = verdict(git_blob(argv[1], "%s:%s" % (argv[2], JSON_PATH)), git_blob(argv[1], "%s:%s" % (argv[2], MAP_PATH)))
        elif len(argv) == 2 and argv[0] == "index":
            ok, why = verdict(git_blob(argv[1], ":" + JSON_PATH), git_blob(argv[1], ":" + MAP_PATH))
        else:
            print(__doc__.split("\n\n")[1] if "\n\n" in __doc__ else __doc__, file=sys.stderr)
            return 2
    except (subprocess.CalledProcessError, RuntimeError, OSError) as e:
        print("acoustic_bake: %s" % e, file=sys.stderr)
        return 2
    print(why)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
