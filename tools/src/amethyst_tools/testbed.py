"""`am testbed new NAME`: forks a testbed (default sandbox) into testbeds/NAME.

The fork is an APFS clone of the whole project, its Library included, so the new testbed opens
warm. Its product name, bundle id and own code folder (Assets/<Title>/: the asmdef, namespace
and class names) are renamed to the new testbed's.
"""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import uuid
from pathlib import Path

from . import paths, worktree

SKIP = ("Builds", "Logs", "Temp", "UserSettings")


def title(name: str) -> str:
    return "".join(part.capitalize() for part in name.split("-"))


def rename_code(folder: Path, old: str, new: str) -> None:
    for f in sorted(folder.rglob("*"), key=lambda p: len(p.parts), reverse=True):
        if f.is_file() and f.suffix in (".cs", ".asmdef"):
            f.write_text(f.read_text().replace(old, new))
        if old in f.name:
            f.rename(f.with_name(f.name.replace(old, new)))


def new(args: argparse.Namespace) -> int:
    if not re.fullmatch(r"[a-z][a-z0-9-]*", args.name):
        raise SystemExit("testbed names are lowercase-kebab, like sword-arcs")
    source = paths.project(args.source)
    dest = paths.REPO / "testbeds" / args.name
    if dest.exists():
        raise SystemExit(f"{dest} already exists")
    subprocess.run(["cp", "-c", "-R", str(source.path), str(dest)], check=True)
    for skip in SKIP:
        shutil.rmtree(dest / skip, ignore_errors=True)
    for pattern in worktree.BEE_GRAPHS:
        for f in (dest / "Library" / "Bee").glob(pattern):
            f.unlink()
    old, new_title = title(source.name), title(args.name)
    spaced = " ".join(part.capitalize() for part in args.name.split("-"))
    settings = dest / "ProjectSettings" / "ProjectSettings.asset"
    text = settings.read_text()
    text = re.sub(r"productGUID: \w+", f"productGUID: {uuid.uuid4().hex}", text)
    text = re.sub(r"(?m)^  productName: .*$", f"  productName: Amethyst {spaced}", text)
    text = re.sub(
        r"Standalone: ca\.thurn\.amethyst\.[\w-]+",
        f"Standalone: ca.thurn.amethyst.{args.name}",
        text,
    )
    settings.write_text(text)
    if (dest / "Assets" / old).is_dir():
        rename_code(dest / "Assets" / old, old, new_title)
        (dest / "Assets" / old).rename(dest / "Assets" / new_title)
        meta = dest / "Assets" / f"{old}.meta"
        if meta.exists():
            meta.rename(dest / "Assets" / f"{new_title}.meta")
    print(f"testbeds/{args.name}: forked from {source.name}, Library included")
    print(f"  code: testbeds/{args.name}/Assets/{new_title}/")
    print(f"  variants: testbeds/{args.name}/Assets/StreamingAssets/variants/")
    print(
        f"  try it: just shots {args.name} (agents), just preview {args.name} (humans)"
    )
    return 0


def register(sub: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    p = sub.add_parser("testbed", help="fork a testbed")
    actions = p.add_subparsers(dest="action", required=True)
    n = actions.add_parser("new", help="fork a testbed, Library included")
    n.add_argument("name")
    n.add_argument("--from", dest="source", default="sandbox")
    n.set_defaults(func=new)
