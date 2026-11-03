"""The vendor tools on tiny fake roots: the guard, and sync with its save round trip."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path

import pytest

from amethyst_tools import paths, vendor

GUIDS: dict[str, str] = {
    name: f"{i:032x}"
    for i, name in enumerate(("prefab", "mat", "tex", "cs", "unused"), 1)
}


def fake_root(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    """An asset folder root holding a prefab, the material and texture it needs, its script
    and an unrelated material; the cache and the roots point into `tmp_path`."""
    root = tmp_path / "unity_assets"
    pack = root / "Assets" / "Pack"
    pack.mkdir(parents=True)
    files = {
        "Hero.prefab": f"m_Script: {{fileID: 1, guid: {GUIDS['cs']}, type: 3}}\n"
        f"m_Materials:\n- {{fileID: 2, guid: {GUIDS['mat']}, type: 2}}\n",
        "Hero.mat": f"m_Texture: {{fileID: 3, guid: {GUIDS['tex']}, type: 3}}\n",
        "Hero.png": "\x89PNG\x00 pretend pixels",
        "Hero.cs": "class Hero {}\n",
        "Unused.mat": "m_Shader: {fileID: 4}\n",
    }
    keys = ("prefab", "mat", "tex", "cs", "unused")
    for (name, text), key in zip(files.items(), keys):
        (pack / name).write_text(text)
        (pack / f"{name}.meta").write_text(
            f"fileFormatVersion: 2\nguid: {GUIDS[key]}\n"
        )
    monkeypatch.setenv("AMETHYST_CACHE", str(tmp_path / "cache"))
    for var in ("GIT_AUTHOR_NAME", "GIT_COMMITTER_NAME"):
        monkeypatch.setenv(var, "test")
    for var in ("GIT_AUTHOR_EMAIL", "GIT_COMMITTER_EMAIL"):
        monkeypatch.setenv(var, "test@example.com")
    roots = {"unity_assets": str(root), "local": str(tmp_path / "local")}
    vendor.write_json(vendor.roots_file(), roots)
    return root


def test_guard_refuses_vendor_files_by_hash_and_path(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = fake_root(tmp_path, monkeypatch)
    repo = tmp_path / "repo"
    files = {
        "game/Assets/Art/Copied.png": (root / "Assets/Pack/Hero.png").read_bytes(),
        "game/Assets/Vendor/Pack/Ours.txt": b"not a vendor file, but in Vendor/",
        "testbeds/sandbox/Assets/Baked/Lightmap.asset": b"baked from vendor files",
        "game/Assets/Game/Ours.cs": b"class Ours {}\n",
    }
    for rel, data in files.items():
        (repo / rel).parent.mkdir(parents=True, exist_ok=True)
        (repo / rel).write_bytes(data)
    subprocess.run(["git", "init", "-q", str(repo)], check=True)
    subprocess.run(["git", "-C", str(repo), "add", "-A", "-f"], check=True)
    subprocess.run(["git", "-C", str(repo), "commit", "-qm", "files"], check=True)

    record = vendor.Record()
    record.refresh(vendor.load_roots())
    failures = vendor.check(repo, vendor.range_entries(repo, "HEAD"), record)

    assert {p for p, _ in failures} == set(files) - {"game/Assets/Game/Ours.cs"}
    assert any("Pack/Hero.png" in reason for _, reason in failures)  # by hash


def test_sync_follows_guids_and_save_keeps_changes(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    fake_root(tmp_path, monkeypatch)
    project = paths.Project("proj", tmp_path / "proj")
    project.path.mkdir()
    vendor.add(project, "unity_assets", "Pack/Hero.prefab", "hero")

    vendor.ensure_synced(project)
    synced = {
        p.relative_to(project.vendor_dir).as_posix()
        for p in project.vendor_dir.rglob("*")
    }
    expected = {
        "Pack",
        *(f"Pack/Hero.{e}{m}" for e in ("prefab", "mat", "png") for m in ("", ".meta")),
    }
    assert synced == expected  # the script and the unused material stay out

    material = project.vendor_dir / "Pack" / "Hero.mat"
    material.write_text(material.read_text() + "m_Color: {r: 1}\n")
    vendor.ensure_synced(project)  # current: leaves the change alone
    assert "m_Color" in material.read_text()

    assert vendor.save(project) == ["Assets/Pack/Hero.mat"]
    entries = {
        e["path"]: e
        for e in json.loads(vendor.manifest_file(project).read_text())["assets"]
    }
    assert entries["Assets/Pack/Hero.mat"]["source"] == "local:unity_assets"
    saved = tmp_path / "local" / "unity_assets" / "Assets" / "Pack" / "Hero.mat"
    assert saved.read_text() == material.read_text()
    log = subprocess.run(
        ["git", "-C", str(saved.parent), "log", "--oneline"],
        capture_output=True,
        text=True,
    )
    assert "proj: Hero.mat" in log.stdout

    vendor.sync(project)  # now from the local copy, which still needs the texture
    assert "m_Color" in material.read_text()
    assert (project.vendor_dir / "Pack" / "Hero.png").exists()
