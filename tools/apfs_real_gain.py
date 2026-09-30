#!/usr/bin/env python3
"""WP-079G (C-00 30.09) · estimated real gain of deleting a folder on APFS, where Library folders are `cp -cR` clones and
`du` counts every cloned block once per copy (the apparent size overstates what a removal frees).

Method: the sum, over the regular files of the tree, of APFS's per-file *private size* — getattrlist(2)
ATTR_CMNEXT_PRIVATESIZE (FSOPT_ATTR_CMN_EXTENDED): "the number of bytes that are not trapped inside a clone or snapshot,
and which would be freed immediately if the file were deleted". A hard-linked file counts only when every link lives
inside the tree. The result is a LOWER BOUND: blocks shared only between files of the same tree (or of two trees removed
together) count as not private, and directory/metadata blocks are ignored. When the volume does not return the attribute
(not APFS, attribute unsupported, unreadable file) the path prints "n/a" — never a guess.

usage: apfs_real_gain.py PATH...   → one line per path: "<MiB, floor>\t<path>" or "n/a\t<path>"
       apfs_real_gain.py --bytes FILE → the private size of one file in bytes (tests) · exit 0 (2 when n/a)"""
import ctypes, ctypes.util, os, stat, sys

ATTR_BIT_MAP_COUNT = 5
ATTR_CMN_RETURNED_ATTRS = 0x80000000
ATTR_CMNEXT_PRIVATESIZE = 0x00000008
FSOPT_NOFOLLOW = 0x00000001
FSOPT_ATTR_CMN_EXTENDED = 0x00000020


class _AttrList(ctypes.Structure):
    _fields_ = [("bitmapcount", ctypes.c_ushort), ("reserved", ctypes.c_uint16), ("commonattr", ctypes.c_uint32),
                ("volattr", ctypes.c_uint32), ("dirattr", ctypes.c_uint32), ("fileattr", ctypes.c_uint32),
                ("forkattr", ctypes.c_uint32)]


class _Reply(ctypes.Structure):  # u_int32 length · attribute_set_t returned (5 × u_int32) · off_t private size
    _pack_ = 4
    _fields_ = [("length", ctypes.c_uint32), ("r_common", ctypes.c_uint32), ("r_vol", ctypes.c_uint32),
                ("r_dir", ctypes.c_uint32), ("r_file", ctypes.c_uint32), ("r_ext", ctypes.c_uint32),
                ("private", ctypes.c_int64)]


_libc = ctypes.CDLL(ctypes.util.find_library("c"), use_errno=True) if sys.platform == "darwin" else None
_REQ = _AttrList(ATTR_BIT_MAP_COUNT, 0, ATTR_CMN_RETURNED_ATTRS, 0, 0, 0, ATTR_CMNEXT_PRIVATESIZE)


def private_bytes(path):
    """Private (freed-on-delete) bytes of one file, or None when the volume does not report it."""
    if _libc is None:
        return None
    rep = _Reply()
    rc = _libc.getattrlist(os.fsencode(path), ctypes.byref(_REQ), ctypes.byref(rep), ctypes.sizeof(rep),
                           FSOPT_NOFOLLOW | FSOPT_ATTR_CMN_EXTENDED)
    if rc != 0 or not (rep.r_ext & ATTR_CMNEXT_PRIVATESIZE) or rep.private < 0:
        return None
    return rep.private


def tree_private_bytes(root):
    """Lower bound of the bytes freed by deleting `root`, or None (n/a) when any file's private size is unknown."""
    total, links = 0, {}
    stack = [root]
    try:
        st = os.lstat(root)
    except OSError:
        return 0  # missing folder: nothing to free
    if stat.S_ISREG(st.st_mode):
        stack, files = [], [(root, st)]
    else:
        files = []
    while stack:
        d = stack.pop()
        try:
            with os.scandir(d) as it:
                for e in it:
                    try:
                        if e.is_dir(follow_symlinks=False):
                            stack.append(e.path)
                        elif e.is_file(follow_symlinks=False):
                            files.append((e.path, e.stat(follow_symlinks=False)))
                    except OSError:
                        return None
        except OSError:
            return None
    for p, s in files:
        b = private_bytes(p)
        if b is None:
            return None
        if s.st_nlink > 1:  # count a hard-linked inode once, and only when all its links are inside the tree
            key = (s.st_dev, s.st_ino)
            seen, nlink, size = links.get(key, (0, s.st_nlink, b))
            links[key] = (seen + 1, nlink, size)
            continue
        total += b
    total += sum(size for seen, nlink, size in links.values() if seen >= nlink)
    return total


def main(argv):
    if len(argv) == 2 and argv[0] == "--bytes":
        b = private_bytes(argv[1])
        print("n/a" if b is None else b)
        return 0 if b is not None else 2
    if not argv:
        print(__doc__, file=sys.stderr)
        return 64
    for p in argv:
        b = tree_private_bytes(p)
        print(f"{'n/a' if b is None else b // 1048576}\t{p}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
