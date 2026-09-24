#!/usr/bin/env python3
"""WP-064 v2 (S-10 finding 2) · Record the state of the Unity Library a build ran on, right after the build.

    library_provenance.py <repo> <out.json>

The gate (wp064_gate.py) compares it with the gated commit: same HEAD, clean tree, no git-lfs pointer on disk,
the Assets tree hash of HEAD, and a present ArtifactDB. Library file times are recorded for audit (Unity only
rewrites them when an import happens, so they are not compared).
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import time

PROJECT = "prototype/DeathsDemolition_PlayableConcept"
POINTER = b"version https://git-lfs.github.com/spec/v1"


def git(repo, *args):
    return subprocess.check_output(["git", "-C", str(repo), *args], text=True, errors="replace").strip()


def stat(path):
    try:
        info = path.stat()
        return info.st_size, info.st_mtime
    except OSError:
        return 0, None


def main():
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 64
    repo, out = Path(sys.argv[1]).resolve(), Path(sys.argv[2])
    library = repo / PROJECT / "Library"
    pointers = missing = 0
    for name in git(repo, "lfs", "ls-files", "-n").splitlines():
        path = repo / name
        if not path.is_file():
            missing += 1
            continue
        with path.open("rb") as handle:
            if handle.read(len(POINTER)) == POINTER:
                pointers += 1
    artifact_bytes, artifact_mtime = stat(library / "ArtifactDB")
    source_bytes, source_mtime = stat(library / "SourceAssetDB")
    _, scene_setup_mtime = stat(library / "LastSceneManagerSetup.txt")
    _, assemblies_mtime = stat(library / "ScriptAssemblies")
    record = {
        "schema": "dd-wp064-library-provenance-v1",
        "phase": "post-build",
        "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "epoch": time.time(),
        "repo": str(repo),
        "head": git(repo, "rev-parse", "HEAD"),
        "porcelain": git(repo, "status", "--porcelain"),
        "assetsTree": git(repo, "rev-parse", f"HEAD:{PROJECT}/Assets"),
        "lfsPointersOnDisk": pointers,
        "lfsMissing": missing,
        "projectVersion": (repo / PROJECT / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8").splitlines()[0],
        "library": {
            "path": str(library),
            "artifactDbBytes": artifact_bytes,
            "artifactDbMtime": artifact_mtime,
            "sourceAssetDbBytes": source_bytes,
            "sourceAssetDbMtime": source_mtime,
            "lastSceneManagerSetupMtime": scene_setup_mtime,
            "scriptAssembliesMtime": assemblies_mtime,
        },
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    clean = record["porcelain"] == "" and pointers == 0 and missing == 0 and artifact_bytes > 0
    print(f"library provenance head={record['head'][:12]} clean={record['porcelain'] == ''} pointers={pointers} missing={missing} artifactDb={artifact_bytes} -> {out}")
    return 0 if clean else 1


if __name__ == "__main__":
    raise SystemExit(main())
