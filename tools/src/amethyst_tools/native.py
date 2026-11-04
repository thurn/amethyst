"""Builds the Rust plugin (native/) into packages/com.amethyst.native/Plugins/macOS/, where every
Unity project that uses the package loads it. Every Unity step runs this first; cargo makes an
unchanged build a fraction of a second."""

from __future__ import annotations

import argparse
import fcntl
import shutil
import subprocess
from pathlib import Path

from . import paths

DYLIB = "libamethyst_native.dylib"
WORKSPACE: Path = paths.REPO / "native"
PLUGIN_DIR: Path = paths.REPO / "packages" / "com.amethyst.native" / "Plugins" / "macOS"


def ensure(quiet: bool = True) -> None:
    """Serialized per worktree: cargo briefly removes the uplifted dylib even on a fresh build,
    so a parallel project's copy could find it missing."""
    lock = WORKSPACE / "target" / ".am-native.lock"
    lock.parent.mkdir(exist_ok=True)
    with open(lock, "w") as handle:
        fcntl.flock(handle, fcntl.LOCK_EX)
        _build_and_install(quiet)


def _build_and_install(quiet: bool) -> None:
    run = subprocess.run(
        ["cargo", "build", "--locked", "--release", "-p", "amethyst_native"],
        cwd=WORKSPACE,
        capture_output=quiet,
        text=True,
        check=False,
    )
    if run.returncode != 0:
        raise SystemExit(f"native: cargo build failed\n{run.stderr if quiet else ''}")
    built = WORKSPACE / "target" / "release" / DYLIB
    installed = PLUGIN_DIR / DYLIB
    if installed.exists() and installed.read_bytes() == built.read_bytes():
        return
    PLUGIN_DIR.mkdir(parents=True, exist_ok=True)
    shutil.copy2(built, installed)
    print(f"native: installed {installed.relative_to(paths.REPO)}")


def build(args: argparse.Namespace) -> int:
    ensure(quiet=False)
    return 0


def register(sub: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    p = sub.add_parser("native", help="build the Rust plugin into its Unity package")
    p.set_defaults(func=build)
