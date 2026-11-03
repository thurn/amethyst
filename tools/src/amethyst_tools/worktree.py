"""Warm Unity Libraries across worktrees, and Enact's worktree remove hook.

A Unity project's Library (imports, compiled scripts, shader cache) takes minutes to build
cold. Each worktree gets its own, seeded from the per-machine cache:

- `restore` APFS-clones (copy-on-write, seconds) the best cached Library into a project that
  has none: the one for its exact packages, else the project's newest, else the main checkout's,
  else any project's newest (Unity reimports only what differs).
- `save` moves a project's Library into the cache by rename, which is instant, keyed by the
  project and its Unity version and packages. The newest few per project are kept.

Enact runs `python3 worktree.py remove` (`.enact.toml`) to save every project's Library before
it deletes a worktree. Standard library only: the hook may run from a copy of this file.
"""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

KEEP_PER_PROJECT = 3

# Bee's build graphs name the worktree that made them by absolute path; another worktree's
# graph would compile its old file lists, so a restored Library drops them (Unity rebuilds
# them on the first compile).
BEE_GRAPHS = ("*.dag", "*.dag.*", "*.dag_*", "*-inputdata.json")


def cache_root() -> Path:
    root = os.environ.get("AMETHYST_CACHE")
    return Path(root) if root else Path.home() / "Library" / "Caches" / "amethyst"


def library_key(project: Path) -> str:
    """The Unity version and packages: Libraries with the same key differ only in assets."""
    h = hashlib.sha256()
    for rel in (
        "ProjectSettings/ProjectVersion.txt",
        "Packages/manifest.json",
        "Packages/packages-lock.json",
    ):
        f = project / rel
        h.update(rel.encode())
        h.update(f.read_bytes() if f.exists() else b"<missing>")
    return h.hexdigest()[:16]


def seeds(name: str) -> Path:
    return cache_root() / "library" / name


def unity_running(project: Path) -> bool:
    return (project / "Temp" / "UnityLockfile").exists()


def project_dirs(checkout: Path) -> list[tuple[str, Path]]:
    found = [("game", checkout / "game")]
    testbeds = checkout / "testbeds"
    if testbeds.is_dir():
        found += [(p.name, p) for p in sorted(testbeds.iterdir())]
    return [(n, p) for n, p in found if (p / "ProjectSettings").is_dir()]


def _newest(folder: Path) -> Path | None:
    if not folder.is_dir():
        return None
    entries = [p for p in folder.iterdir() if p.is_dir() and ".tmp" not in p.name]
    return max(entries, key=lambda p: p.stat().st_mtime, default=None)


def candidates(name: str, project: Path, main_checkout: Path | None) -> list[Path]:
    """Where a Library for this project could come from, best first."""
    found: list[Path] = []
    exact = seeds(name) / library_key(project)
    if exact.is_dir():
        found.append(exact)
    newest = _newest(seeds(name))
    if newest:
        found.append(newest)
    if main_checkout:
        rel = project.relative_to(_checkout_of(project))
        main_library = main_checkout / rel / "Library"
        if main_library.is_dir() and main_library != project / "Library":
            found.append(main_library)
    others = [_newest(d) for d in (cache_root() / "library").glob("*") if d.is_dir()]
    found += sorted(
        (p for p in others if p), key=lambda p: p.stat().st_mtime, reverse=True
    )
    return found


def _checkout_of(project: Path) -> Path:
    # game/ is one level down; testbeds/<name>/ two.
    return (
        project.parent.parent if project.parent.name == "testbeds" else project.parent
    )


def restore(name: str, project: Path, main_checkout: Path | None) -> str | None:
    """Clones the best cached Library into a project without one; what it did, or None."""
    library = project / "Library"
    if library.exists():
        return None
    for source in candidates(name, project, main_checkout):
        tmp = project / f"Library.tmp-{os.getpid()}"
        started = time.monotonic()
        clone = subprocess.run(
            ["cp", "-c", "-R", str(source), str(tmp)], capture_output=True, check=False
        )
        if clone.returncode != 0:
            shutil.rmtree(tmp, ignore_errors=True)
            continue
        for pattern in BEE_GRAPHS:
            for f in (tmp / "Bee").glob(pattern):
                f.unlink()
        tmp.rename(library)
        return f"cloned {source} in {time.monotonic() - started:.1f} s"
    return None


def save(name: str, project: Path) -> str | None:
    """Moves a project's Library into the cache as its newest seed; what it did, or None."""
    library = project / "Library"
    if not library.is_dir() or unity_running(project):
        return None
    folder = seeds(name)
    folder.mkdir(parents=True, exist_ok=True)
    dest = folder / library_key(project)
    tmp = folder / f"{dest.name}.tmp-{os.getpid()}"
    try:
        library.rename(tmp)
    except OSError:
        return None  # another volume: not worth copying gigabytes
    old = folder / f"{dest.name}.old-{os.getpid()}"
    if dest.exists():
        dest.rename(old)
    tmp.rename(dest)
    os.utime(dest)
    shutil.rmtree(old, ignore_errors=True)
    entries = sorted(
        (p for p in folder.iterdir() if p.is_dir() and ".tmp" not in p.name),
        key=lambda p: p.stat().st_mtime,
        reverse=True,
    )
    for stale in entries[KEEP_PER_PROJECT:]:
        shutil.rmtree(stale, ignore_errors=True)
    return f"saved {name}'s Library as {dest}"


def remove_hook() -> int:
    """Enact's remove hook: save each project's Library, then remove the worktree as Enact
    would (the hook replaces its removal)."""
    path = Path(os.environ["ENACT_WORKTREE_PATH"])
    repo: Path = Path(os.environ["ENACT_REPO"])
    branch = os.environ.get("ENACT_BRANCH", "")
    if path.is_dir():
        for name, project in project_dirs(path):
            done = save(name, project)
            if done:
                print(f"worktree: {done}", file=sys.stderr)

    def git(*args: str) -> str:
        run = subprocess.run(
            ["git", "-C", str(repo), *args], capture_output=True, text=True, check=False
        )
        return run.stdout

    git("worktree", "remove", "--force", str(path))
    # Leftovers a still-running build wrote meanwhile, once git no longer tracks the tree.
    if path.exists() and f"worktree {path.resolve()}\n" not in git(
        "worktree", "list", "--porcelain"
    ):
        shutil.rmtree(path, ignore_errors=True)
    if branch:
        git("branch", "-D", branch)
    git("worktree", "prune")
    return 0


if __name__ == "__main__":
    if sys.argv[1:] != ["remove"]:
        print("usage: python3 worktree.py remove (Enact's hook)", file=sys.stderr)
        sys.exit(2)
    sys.exit(remove_hook())
