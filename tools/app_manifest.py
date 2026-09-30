#!/usr/bin/env python3
"""WP-079H · content manifest of a built player, so a shipped copy can later be proven to be that build.
usage: app_manifest.py write <out-dir> <app-name>   → <out-dir>/app-manifest.sha256, prints the manifest's own sha256
       app_manifest.py verify <out-dir>             → re-hashes every listed file; exit 0 all match · 1 mismatch/missing/extra file · 2 unreadable
The manifest is plain `shasum -a 256` format ("<hex>  <path>", paths relative to <out-dir>, sorted), so
`cd <out-dir> && shasum -a 256 -c app-manifest.sha256` verifies it without this script. Symlinks are hashed
through their target (as shasum does); a dangling link or a path shasum cannot represent (newline, backslash) fails."""
import hashlib, os, sys

MANIFEST = "app-manifest.sha256"


def file_sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def entries(out_dir, app_name):
    app = os.path.join(out_dir, app_name)
    if not os.path.isdir(app):
        raise ValueError(f"not a directory: {app}")
    rows = []
    for root, dirs, files in os.walk(app):
        dirs.sort()
        for name in files:
            path = os.path.join(root, name)
            rel = os.path.relpath(path, out_dir)
            if "\n" in rel or "\\" in rel:
                raise ValueError(f"path shasum cannot represent: {rel!r}")
            if not os.path.isfile(path):
                raise ValueError(f"not a readable file (dangling link?): {rel}")
            rows.append((rel, file_sha256(path)))
    if not rows:
        raise ValueError(f"no files under {app}")
    return sorted(rows, key=lambda r: r[0].encode("utf-8"))


def write(out_dir, app_name):
    text = "".join(f"{digest}  {rel}\n" for rel, digest in entries(out_dir, app_name))
    path = os.path.join(out_dir, MANIFEST)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        f.write(text)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, path)
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def verify(out_dir):
    """Like `shasum -c`, and also reports files present under the app that the manifest does not list."""
    bad, listed = 0, set()
    with open(os.path.join(out_dir, MANIFEST), encoding="utf-8") as f:
        for line in f:
            digest, rel = line.rstrip("\n").split("  ", 1)
            listed.add(rel)
            path = os.path.join(out_dir, rel)
            if not os.path.isfile(path) or file_sha256(path) != digest:
                print(f"MISMATCH {rel}")
                bad += 1
    for app_name in sorted({rel.split("/", 1)[0] for rel in listed}):
        for rel, _ in entries(out_dir, app_name):
            if rel not in listed:
                print(f"EXTRA {rel}")
                bad += 1
    return bad


def main(argv):
    try:
        if len(argv) == 4 and argv[1] == "write":
            print(write(argv[2], argv[3]))
            return 0
        if len(argv) == 3 and argv[1] == "verify":
            bad = verify(argv[2])
            print("app-manifest ok" if bad == 0 else f"app-manifest FAILED: {bad} file(s)")
            return 0 if bad == 0 else 1
    except (OSError, ValueError) as error:
        print(f"app-manifest error: {error}", file=sys.stderr)
        return 2
    print(__doc__, file=sys.stderr)
    return 64


if __name__ == "__main__":
    sys.exit(main(sys.argv))
