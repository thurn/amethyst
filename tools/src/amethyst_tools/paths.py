"""Where things are: this checkout, its Unity projects, the per-machine cache, the agent."""

from __future__ import annotations

import hashlib
import os
import re
from dataclasses import dataclass
from pathlib import Path

# tools/src/amethyst_tools/paths.py: the checkout (a worktree or the main checkout) is four up.
REPO: Path = Path(__file__).resolve().parents[3]

UNITY_HUB = Path("/Applications/Unity/Hub/Editor")

# Environment variables that mark a run as an agent's: it must stay windowless and silent.
AGENT_ENV = (
    "CLAUDECODE",
    "CLAUDE_CODE_SESSION_ID",
    "CODEX_THREAD_ID",
    "ENACT_SESSION",
    "CI",
)


def cache_root() -> Path:
    """The per-machine cache shared by every checkout: $AMETHYST_CACHE, else
    ~/Library/Caches/amethyst."""
    root = os.environ.get("AMETHYST_CACHE")
    path = Path(root) if root else Path.home() / "Library" / "Caches" / "amethyst"
    path.mkdir(parents=True, exist_ok=True)
    return path


def checkout_id(root: Path = REPO) -> str:
    """A stable name for a checkout: its folder name and a hash of its resolved path."""
    resolved = str(root.resolve())
    return f"{root.name}-{hashlib.sha256(resolved.encode()).hexdigest()[:8]}"


def is_agent() -> bool:
    return any(os.environ.get(name) for name in AGENT_ENV)


@dataclass(frozen=True)
class Project:
    """A Unity project: `game`, or a testbed under testbeds/."""

    name: str
    path: Path

    @property
    def assets(self) -> Path:
        return self.path / "Assets"

    @property
    def library(self) -> Path:
        return self.path / "Library"

    @property
    def vendor_dir(self) -> Path:
        return self.assets / "Vendor"

    @property
    def manifest(self) -> Path:
        return self.path / "Packages" / "manifest.json"

    @property
    def unity_version(self) -> str:
        text = (self.path / "ProjectSettings" / "ProjectVersion.txt").read_text()
        match = re.search(r"m_EditorVersion: (\S+)", text)
        if not match:
            raise RuntimeError(f"{self.name}: no m_EditorVersion in ProjectVersion.txt")
        return match.group(1)

    @property
    def product(self) -> str:
        text = (self.path / "ProjectSettings" / "ProjectSettings.asset").read_text()
        match = re.search(r"^  productName: (.+)$", text, re.MULTILINE)
        if not match:
            raise RuntimeError(f"{self.name}: no productName in ProjectSettings.asset")
        return match.group(1).strip()

    def uses_package(self, package: str) -> bool:
        return f'"{package}"' in self.manifest.read_text()


def projects(root: Path = REPO) -> list[Project]:
    found = [Project("game", root / "game")] if (root / "game").is_dir() else []
    testbeds = root / "testbeds"
    if testbeds.is_dir():
        found += [
            Project(p.name, p)
            for p in sorted(testbeds.iterdir())
            if (p / "ProjectSettings").is_dir()
        ]
    return found


def project(name: str, root: Path = REPO) -> Project:
    for p in projects(root):
        if p.name == name:
            return p
    known = ", ".join(p.name for p in projects(root))
    raise SystemExit(f"no Unity project {name!r} (known: {known})")


def unity_editor(version: str) -> Path:
    """The Unity binary for a version: $UNITY_EDITOR, else the Hub's install."""
    override = os.environ.get("UNITY_EDITOR")
    if override:
        return Path(override)
    path = UNITY_HUB / version / "Unity.app" / "Contents" / "MacOS" / "Unity"
    if not path.exists():
        raise SystemExit(
            f"Unity {version} is not installed at {path} (Unity Hub installs it)"
        )
    return path
