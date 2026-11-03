"""Copies files and trees by cloning them: on APFS a clone shares its source's blocks until
either is written (copy-on-write), so a gigabyte copies in milliseconds. Across volumes, or on
a filesystem without clones, it falls back to plain copies."""

from __future__ import annotations

import ctypes
import errno
import os
import shutil
import sys
from pathlib import Path

# macOS's clonefile(2): a file, or a whole tree in one call.
_LIBC: ctypes.CDLL | None = (
    ctypes.CDLL(None, use_errno=True) if sys.platform == "darwin" else None
)
# The errors that mean "can't clone here" (another volume, another filesystem), not a failure.
_NO_CLONE: frozenset[int] = frozenset({errno.EXDEV, errno.ENOTSUP, errno.EOPNOTSUPP})


def clone(src: Path, dst: Path) -> None:
    """Clones the file or tree at `src` to `dst`, which must not exist; copies where it can't."""
    if _LIBC is not None:
        if _LIBC.clonefile(os.fsencode(src), os.fsencode(dst), 0) == 0:
            return
        err = ctypes.get_errno()
        if err not in _NO_CLONE:
            raise OSError(err, os.strerror(err), str(dst))
    if src.is_dir():
        shutil.copytree(src, dst, symlinks=True)
    else:
        shutil.copy2(src, dst)


def remove_tree(path: Path) -> None:
    """Removes a file or a tree, if there is one."""
    if path.is_symlink() or path.is_file():
        path.unlink()
    elif path.exists():
        shutil.rmtree(path)


def clone_tree(src: Path, dst: Path) -> int:
    """Replaces the tree at `dst` with a clone of `src`; returns its number of files.

    The clone is made beside `dst` under a hidden name (Unity skips those) and swapped in, so a
    failed clone leaves the old tree in place."""
    dst.parent.mkdir(parents=True, exist_ok=True)
    tmp = dst.with_name(f".{dst.name}.{os.getpid()}.tmp")
    old = dst.with_name(f".{dst.name}.{os.getpid()}.old")
    remove_tree(tmp)
    remove_tree(old)
    clone(src, tmp)
    if dst.exists() or dst.is_symlink():
        dst.rename(old)
    tmp.rename(dst)
    remove_tree(old)
    return sum(len(files) for _, _, files in os.walk(dst))
