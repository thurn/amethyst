"""Vendor assets: the paid packs the Unity projects use. The repository is public, and those
licenses forbid redistributing the assets, so no vendor file may ever reach git.

- A project's tracked `vendor.json` lists the vendor files it uses. Each entry names a source by
  root, never by a machine's path, and pins its SHA-256 and GUID. No vendor.json means no
  vendor assets.
- This machine's `vendor-roots.json` (in the cache) maps each root to a folder: `synty` holds
  Synty's `.unitypackage.gz` files (source `synty:<file>`), `unity_assets` and `more_assets` are
  Asset Store folders (an `Assets/` of packs, files beside their `.meta`), and `local` is a
  local-only git repository of the vendor files we modified (source `local:<origin>`), which
  must never be pushed to GitHub.
- Sync fills the project's gitignored `Assets/Vendor/` from vendor.json, following `guid:`
  references (prefab to material to texture) within a root but never into scripts or scenes.
  The result is cached per manifest and cloned into the project; a stamp in Library/ makes an
  up-to-date project's check take milliseconds. A sync never overwrites unsaved changes.
- `save` copies the files changed under Assets/Vendor/ into the local repository, commits them
  there, and repoints their entries at it.
- The guard refuses anything under `Assets/Vendor/` or `Assets/Baked/`, and any file (or .meta
  GUID) in the record of every file under this machine's roots.
"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import tarfile
import time
import zipfile
from collections.abc import Iterable, Iterator
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
from typing import IO

from . import clone, paths

# Bump when resolving or copying changes what a manifest syncs to, so no cached folder synced
# by older logic is reused.
SYNC_VERSION = "1"
MANIFEST = "vendor.json"
STAMP = "amethyst-vendor.json"  # in Library/: what sync last wrote to Assets/Vendor/
LOCAL = "local"
# The roots of .unitypackage(.gz) files; the others are asset folders.
PACKAGE_ROOTS: frozenset[str] = frozenset({"synty"})
PACKAGE_SUFFIXES: tuple[str, ...] = (".unitypackage.gz", ".unitypackage")
ARCHIVE_SUFFIXES: tuple[str, ...] = (*PACKAGE_SUFFIXES, ".zip")

# Unity's built-in resources (default materials, the Standard shader): never in a source.
BUILTIN_GUIDS: frozenset[str] = frozenset(
    {"0000000000000000e000000000000000", "0000000000000000f000000000000000"}
)
GUID_REF: re.Pattern[bytes] = re.compile(rb"guid:\s*([0-9a-fA-F]{32})")
META_GUID: re.Pattern[bytes] = re.compile(rb"^guid:\s*([0-9a-fA-F]{32})", re.MULTILINE)
HEX_GUID: re.Pattern[str] = re.compile(r"[0-9a-fA-F]{32}")
NEVER_FOLLOW: frozenset[str] = frozenset(
    {".cs", ".dll", ".js", ".asmdef", ".asmref", ".rsp", ".unity", ".pdb"}
)
YAML_LIKE: frozenset[str] = frozenset(
    {
        ".prefab", ".mat", ".asset", ".controller", ".overridecontroller", ".mask",
        ".anim", ".shadergraph", ".shadersubgraph", ".vfx", ".playable", ".preset",
        ".physicmaterial", ".terrainlayer", ".lighting", ".spriteatlas", ".mixer",
        ".flare", ".rendertexture", ".cubemap", ".guiskin", ".fontsettings", ".signal",
    }
)  # fmt: skip
SKIP_NAMES: frozenset[str] = frozenset({"Thumbs.db", "__MACOSX"})
# What Unity's project template puts at the top of an asset folder's Assets/: not vendor files.
TEMPLATE: frozenset[str] = frozenset(
    {
        "Scenes",
        "Settings",
        "TutorialInfo",
        "Readme.asset",
        "InputSystem_Actions.inputactions",
    }
)
# Binary types the local repository stores with git LFS, so a private remote can be added.
LFS_TYPES: tuple[str, ...] = tuple(
    """fbx obj blend dae 3ds max ma mb glb png jpg jpeg tga tif tiff psd psb exr hdr dds
    gif bmp webp ktx ktx2 wav mp3 ogg aif aiff flac m4a mp4 mov webm ttf otf bytes bin
    unitypackage zip gz""".split()
)
VENDOR_PATH: re.Pattern[str] = re.compile(
    r"(^|/)assets/(vendor|baked)(/|\.meta$)", re.I
)
TEXT_LIMIT: int = 8 << 20  # files up to this size are read whole (for CRLF and GUIDs)
EMPTY_SHA256: str = hashlib.sha256(b"").hexdigest()
ZERO_SHA = "0" * 40
HOOK_MARK = "Amethyst's vendor guard"

# A file's record: size, mtime_ns, SHA-256, SHA-256 with CRLF as LF ("" when the same), and a
# .meta's GUID ("" otherwise). An archive member's: its name, SHA-256, LF SHA-256 and GUID.
FileEntry = tuple[int, int, str, str, str]
Member = tuple[str, str, str, str]
ArchiveEntry = tuple[int, int, list[Member]]


class VendorError(Exception):
    pass


# Where things are.


def cache() -> Path:
    path = paths.cache_root() / "vendor"
    path.mkdir(parents=True, exist_ok=True)
    return path


def roots_file() -> Path:
    return paths.cache_root() / "vendor-roots.json"


def default_roots() -> dict[str, str]:
    home = Path.home()
    return {
        "synty": str(home / "synty"),
        "unity_assets": str(home / "unity_assets" / "UnityAssets"),
        "more_assets": str(home / "unity_assets" / "MoreAssets" / "MoreAssets"),
        LOCAL: str(home / "amethyst-vendor"),
    }


def load_roots() -> dict[str, Path]:
    """This machine's roots, from vendor-roots.json (written with the defaults on first use)."""
    path = roots_file()
    if not path.exists():
        write_json(path, default_roots())
    saved: dict[str, str] = json.loads(path.read_text(encoding="utf-8"))
    return {name: Path(p).expanduser() for name, p in saved.items()}


def local_root() -> Path:
    """The local-only repository of modified vendor files."""
    return load_roots().get(LOCAL) or Path(default_roots()[LOCAL])


def manifest_file(project: paths.Project) -> Path:
    return project.path / MANIFEST


def origin_dir(origin: str) -> str:
    """The local repository's folder for files modified from `origin`: `synty:X.unitypackage.gz`
    is synty/X, `unity_assets` is unity_assets."""
    folder = origin.replace(":", "/")
    for suffix in PACKAGE_SUFFIXES:
        folder = folder.removesuffix(suffix)
    return folder


def vendor_rel(item_path: str) -> str:
    """Where a source's `Assets/...` path lands under Assets/Vendor/."""
    parts = PurePosixPath(item_path).parts
    return (
        PurePosixPath(*parts[1:]).as_posix() if parts[:1] == ("Assets",) else item_path
    )


def tilde(path: Path | str) -> str:
    text, home = str(path), str(Path.home())
    return "~" + text[len(home) :] if text.startswith(home + "/") else text


# Files and hashes.


def write_json(path: Path, data: object) -> None:
    """Writes JSON atomically (other checkouts may read it at the same time)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f"{path.name}.{os.getpid()}.tmp")
    tmp.write_text(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    os.replace(tmp, path)


def sha256_file(path: Path) -> str:
    with open(path, "rb") as f:
        return digest(f, path.stat().st_size)[0]


def digest(f: IO[bytes], size: int) -> tuple[str, str]:
    """The SHA-256 of a stream, and of it with CRLF as LF when it is text with CRLFs (git may
    store such a file with LFs), else ""."""
    if size > TEXT_LIMIT:
        h = hashlib.sha256()
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
        return h.hexdigest(), ""
    return sha_forms(f.read())


def sha_forms(data: bytes) -> tuple[str, str]:
    lf = ""
    if b"\r\n" in data and b"\x00" not in data[:8192]:
        lf = hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest()
    return hashlib.sha256(data).hexdigest(), lf


def meta_guid(data: bytes) -> str:
    m = META_GUID.search(data[:4096])
    return m.group(1).decode().lower() if m else ""


def walk_files(base: Path, skip_top: frozenset[str] = frozenset()) -> Iterator[Path]:
    """Every file under `base`, sorted, without hidden files and folders, and without the
    names in `skip_top` (or their .meta) at its top."""
    top = str(base)
    for dirpath, dirs, files in os.walk(base):
        skip = skip_top if dirpath == top else frozenset()
        dirs[:] = sorted(
            d
            for d in dirs
            if not d.startswith(".") and d not in SKIP_NAMES and d not in skip
        )
        for f in sorted(files):
            if f.startswith(".") or f in SKIP_NAMES or f.removesuffix(".meta") in skip:
                continue
            yield Path(dirpath) / f


def is_archive(name: str) -> bool:
    return name.lower().endswith(ARCHIVE_SUFFIXES)


def package_member(name: str) -> tuple[str, str] | None:
    """(guid, leaf) for a .unitypackage member `<guid>/asset`, `/asset.meta` or `/pathname`."""
    parts = [p for p in PurePosixPath(name).parts if p not in (".", "")]
    if len(parts) != 2 or parts[1] not in ("asset", "asset.meta", "pathname"):
        return None
    return (parts[0].lower(), parts[1]) if HEX_GUID.fullmatch(parts[0]) else None


def archive_members(file: Path) -> list[Member]:
    """The files inside a .unitypackage(.gz) (named by their Unity paths) or a .zip."""
    out: list[Member] = []
    if file.name.lower().endswith(".zip"):
        with zipfile.ZipFile(file) as z:
            for info in z.infolist():
                if not info.is_dir():
                    with z.open(info) as f:
                        sha, lf = digest(f, info.file_size)
                    out.append((info.filename, sha, lf, ""))
        return out
    names: dict[str, str] = {}
    found: list[tuple[str, str, str, str]] = []  # guid, leaf, sha, lf
    with tarfile.TarFile.open(file, "r|gz") as tar:
        for member in tar:
            parsed = package_member(member.name)
            f = tar.extractfile(member) if parsed and member.isfile() else None
            if parsed is None or f is None:
                continue
            guid, leaf = parsed
            if leaf == "pathname":
                names[guid] = (
                    f.read().decode("utf-8", "replace").splitlines()[0].strip()
                )
            else:
                found.append((guid, leaf, *digest(f, member.size)))
    for guid, leaf, sha, lf in found:
        meta = leaf == "asset.meta"
        name = names.get(guid, guid) + (".meta" if meta else "")
        out.append((name, sha, lf, guid if meta else ""))
    return out


# The record: every file under this machine's roots, for the guard.


def record_file() -> Path:
    return cache() / "record.json"


def scan_base(name: str, root: Path) -> Path:
    """The part of a root the record covers: an asset folder's Assets/, else all of it."""
    return root if name in PACKAGE_ROOTS or name == LOCAL else root / "Assets"


@dataclass
class Record:
    """Every file under the roots, by path, with its hashes (and an archive's members), kept
    by size and mtime so only new or changed files are hashed again. It never forgets a file:
    a vendor file deleted from a root may still have been copied somewhere."""

    files: dict[str, FileEntry] = field(default_factory=dict)
    archives: dict[str, ArchiveEntry] = field(default_factory=dict)
    dirty: bool = False

    @classmethod
    def load(cls) -> Record:
        try:
            raw = json.loads(record_file().read_text(encoding="utf-8"))
            if raw.get("version") == 1:
                return cls(raw["files"], raw["archives"])
        except (OSError, ValueError, KeyError, AttributeError):
            pass
        return cls()

    def save(self) -> None:
        if self.dirty:
            data = {"version": 1, "files": self.files, "archives": self.archives}
            tmp = record_file().with_name(f"record.json.{os.getpid()}.tmp")
            tmp.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
            os.replace(tmp, record_file())
            self.dirty = False

    def sha(self, file: Path) -> str:
        """A file's SHA-256, hashed only when new or changed."""
        st = file.stat()
        hit = self.files.get(str(file))
        if hit and hit[0] == st.st_size and hit[1] == st.st_mtime_ns:
            return hit[2]
        entry = hash_file(file)
        self.files[str(file)] = entry
        self.dirty = True
        return entry[2]

    def stale(self, file: Path) -> bool:
        st = file.stat()
        hit = self.files.get(str(file))
        if not hit or hit[0] != st.st_size or hit[1] != st.st_mtime_ns:
            return True
        arch = self.archives.get(str(file)) if is_archive(file.name) else None
        return is_archive(file.name) and (
            not arch or arch[0] != st.st_size or arch[1] != st.st_mtime_ns
        )

    def refresh(self, roots: dict[str, Path]) -> int:
        """Hashes every new or changed file under the roots; returns how many it hashed."""
        todo: list[Path] = []
        for name, root in sorted(roots.items()):
            base = scan_base(name, root)
            skip = TEMPLATE if base != root else frozenset()
            todo += [f for f in walk_files(base, skip) if self.stale(f)]
        if not todo:
            return 0
        size = sum(f.stat().st_size for f in todo)
        print(
            f"vendor record: hashing {len(todo)} new or changed vendor files "
            f"({size / 1e9:.1f} GB)",
            file=sys.stderr,
        )
        with ThreadPoolExecutor(max_workers=8) as pool:
            for file, entry, members in pool.map(hash_one, todo):
                self.files[str(file)] = entry
                if members is not None:
                    self.archives[str(file)] = (entry[0], entry[1], members)
        self.dirty = True
        return len(todo)

    def lookup(self) -> tuple[dict[str, str], dict[str, str]]:
        """Every recorded SHA-256 and every .meta GUID, each with where it came from."""
        hashes: dict[str, str] = {}
        guids: dict[str, str] = {}

        def add(label: str, sha: str, lf: str, guid: str) -> None:
            for h in (sha, lf):
                if h and h != EMPTY_SHA256:
                    hashes[h] = label
            if guid:
                guids[guid] = label

        for path, (_size, _mtime, sha, lf, guid) in self.files.items():
            add(tilde(path), sha, lf, guid)
        for path, (_size, _mtime, members) in self.archives.items():
            for name, sha, lf, guid in members:
                add(f"{tilde(path)}: {name}", sha, lf, guid)
        return hashes, guids


def hash_file(file: Path) -> FileEntry:
    st = file.stat()
    with open(file, "rb") as f:
        if file.name.endswith(".meta") and st.st_size <= TEXT_LIMIT:
            data = f.read()
            return (st.st_size, st.st_mtime_ns, *sha_forms(data), meta_guid(data))
        return (st.st_size, st.st_mtime_ns, *digest(f, st.st_size), "")


def hash_one(file: Path) -> tuple[Path, FileEntry, list[Member] | None]:
    entry = hash_file(file)
    members: list[Member] | None = None
    if is_archive(file.name):
        try:
            members = archive_members(file)
        except (tarfile.TarError, zipfile.BadZipFile, OSError, EOFError) as e:
            print(f"vendor record: can't read {tilde(file)}: {e}", file=sys.stderr)
            members = []
    return file, entry, members


# Sources: each maps GUIDs and paths to the files it can provide.


@dataclass
class Item:
    """One asset of a source: its Unity path, GUID, and where its bytes are."""

    source: str
    path: str  # "Assets/..."
    guid: str
    asset: Path | None  # None for a folder
    meta: Path | None


@dataclass
class Source:
    name: str  # "synty:<file>", "unity_assets", "local:<origin>", ...
    sha256: str | None  # a package's own hash; None for an asset folder
    by_guid: dict[str, Item] = field(default_factory=dict)
    by_path: dict[str, Item] = field(default_factory=dict)

    def add(self, item: Item) -> None:
        self.by_guid[item.guid] = item
        self.by_path[item.path.lower()] = item


def unpack(pkg: Path, sha: str) -> Path:
    """Unpacks a .unitypackage(.gz) once into the cache, keyed by its SHA-256: a folder per
    GUID with `asset`, `asset.meta` and `pathname`."""
    out = cache() / "packages" / sha[:16]
    if out.is_dir():
        return out
    tmp = out.with_name(f"{out.name}.{os.getpid()}.tmp")
    clone.remove_tree(tmp)
    tmp.mkdir(parents=True)
    with tarfile.TarFile.open(pkg, "r|gz") as tar:
        for member in tar:
            parsed = package_member(member.name)
            f = tar.extractfile(member) if parsed and member.isfile() else None
            if parsed is None or f is None:
                continue
            (tmp / parsed[0]).mkdir(exist_ok=True)
            with open(tmp / parsed[0] / parsed[1], "wb") as dest:
                shutil.copyfileobj(f, dest)
    try:
        tmp.rename(out)
    except OSError:  # another sync unpacked it first
        clone.remove_tree(tmp)
    return out


def load_package(name: str, pkg: Path, sha: str) -> Source:
    src = Source(name, sha)
    for d in sorted(unpack(pkg, sha).iterdir()):
        pathname = d / "pathname"
        if not pathname.exists():
            continue
        path = (
            pathname.read_text(encoding="utf-8", errors="replace")
            .splitlines()[0]
            .strip()
        )
        asset, meta = d / "asset", d / "asset.meta"
        src.add(Item(name, path, d.name, asset if asset.exists() else None, meta))
    return src


def load_folder(name: str, root: Path) -> Source:
    """An asset folder's files (each beside its .meta) under root/Assets/."""
    src = Source(name, None)
    assets = root / "Assets"
    for meta in walk_files(assets, TEMPLATE):
        if not meta.name.endswith(".meta"):
            continue
        with open(meta, "rb") as f:
            guid = meta_guid(f.read(4096))
        if guid:
            target = meta.with_name(meta.name.removesuffix(".meta"))
            path = "Assets/" + target.relative_to(assets).as_posix()
            src.add(Item(name, path, guid, target if target.is_file() else None, meta))
    return src


@dataclass
class Sources:
    roots: dict[str, Path]
    record: Record  # remembers source files' hashes
    loaded: dict[str, Source] = field(default_factory=dict)

    def get(self, name: str) -> Source:
        if name in self.loaded:
            return self.loaded[name]
        root, _, rest = name.partition(":")
        if root not in self.roots:
            known = ", ".join(sorted(self.roots))
            raise VendorError(
                f"{name}: no root {root!r} (roots: {known}; {tilde(roots_file())})"
            )
        base = self.roots[root]
        if root in PACKAGE_ROOTS:
            pkg = base / rest
            if not rest or not pkg.is_file():
                raise VendorError(f"{name}: no package at {tilde(pkg)}")
            src = load_package(name, pkg, self.record.sha(pkg))
        else:
            if (root == LOCAL) != bool(rest):
                form = f"{LOCAL}:<origin source>" if root == LOCAL else root
                raise VendorError(f"{name}: name this source {form}")
            folder = base / origin_dir(rest) if rest else base
            if not (folder / "Assets").is_dir():
                raise VendorError(f"{name}: no Assets/ folder in {tilde(folder)}")
            src = load_folder(name, folder)
        self.loaded[name] = src
        return src

    def family(self, name: str) -> Iterator[Source]:
        """Where a source's references are looked up: itself, then the other loaded sources of
        its root; for a local source, then its origin's."""
        root, _, rest = name.partition(":")
        yield self.get(name)
        if root == LOCAL:
            yield from self.family(rest)
            return
        yield from [
            s
            for n, s in self.loaded.items()
            if n != name and n.partition(":")[0] == root
        ]

    def find(self, guid: str, near: str) -> Item | None:
        for src in self.family(near):
            item = src.by_guid.get(guid)
            if item is not None and item.asset is not None:
                return item
        return None


def follow(item: Item) -> set[str]:
    """The GUIDs an asset's YAML and .meta refer to."""
    refs: set[str] = set()
    for f in (item.asset, item.meta):
        if f is None or not f.exists():
            continue
        if f is item.asset and PurePosixPath(item.path).suffix.lower() not in YAML_LIKE:
            continue
        data = f.read_bytes()
        if f is item.asset and b"\x00" in data[:8192]:
            continue  # a binary-serialized asset
        refs.update(m.group(1).decode().lower() for m in GUID_REF.finditer(data))
    refs.discard(item.guid)
    return refs


def check_entry(entry: dict[str, str], src: Source, item: Item, record: Record) -> None:
    """Refuses a source whose files differ from the manifest's, so every sync is identical."""
    asset = item.asset
    if asset is None:
        raise VendorError(f"{entry['path']}: a folder in {src.name}; list its files")
    if entry.get("guid") and entry["guid"].lower() != item.guid:
        raise VendorError(
            f"{entry['path']}: GUID {item.guid}, not the manifest's {entry['guid']}"
        )
    pinned = [("sourceSha256", src.sha256 or record.sha(asset))]
    if entry.get("metaSha256") and item.meta is not None:
        pinned.append(("metaSha256", record.sha(item.meta)))
    for key, actual in pinned:
        if entry.get(key) != actual:
            raise VendorError(
                f"{entry['path']}: {src.name} has SHA-256 {actual}, not the manifest's "
                f"{key} {entry.get(key)}; refusing to sync"
            )


def resolve(
    entries: list[dict[str, str]], sources: Sources, unresolved: dict[str, str]
) -> list[tuple[Item, str]]:
    """Every asset to sync, with its use: the manifest's entries and everything they refer to.
    References no source provides (package assets such as URP's shaders, or missing files) go
    into `unresolved` (GUID -> the first asset referring to it)."""
    chosen: dict[str, tuple[Item, str]] = {}
    for entry in entries:  # first, so an entry (a local copy) wins over a reference
        src = sources.get(entry["source"])
        item = src.by_path.get(entry["path"].lower())
        if item is None:
            raise VendorError(f"{entry['source']}: no asset at {entry['path']}")
        check_entry(entry, src, item, sources.record)
        chosen[item.guid] = (item, entry.get("use", ""))
    queue = list(chosen.values())
    while queue:
        item, _use = queue.pop()
        for ref in sorted(follow(item)):
            if ref in chosen:
                continue
            dep = sources.find(ref, item.source)
            if dep is None:
                if ref not in BUILTIN_GUIDS:
                    unresolved.setdefault(ref, item.path)
            elif PurePosixPath(dep.path).suffix.lower() not in NEVER_FOLLOW:
                chosen[ref] = (dep, f"dependency of {item.path}")
                queue.append(chosen[ref])
    return sorted(chosen.values(), key=lambda t: t[0].path.lower())


def copy_items(items: list[tuple[Item, str]], out: Path) -> list[dict[str, str]]:
    """Clones each asset and its .meta into `out`; returns what it wrote."""
    wrote: list[dict[str, str]] = []
    for item, use in items:
        asset = item.asset
        assert asset is not None
        dest = out / vendor_rel(item.path)
        dest.parent.mkdir(parents=True, exist_ok=True)
        pairs = [(asset, dest)]
        if item.meta is not None and item.meta.exists():
            pairs.append((item.meta, dest.with_name(dest.name + ".meta")))
        for src, dst in pairs:
            clone.remove_tree(dst)
            clone.clone(src, dst)
        wrote.append(
            {
                "source": item.source,
                "path": item.path,
                "guid": item.guid,
                "use": use,
                "sha256": sha256_file(dest),
                "metaSha256": sha256_file(pairs[1][1]) if len(pairs) > 1 else "",
            }
        )
    return wrote


# Sync: the manifest's files, cached per manifest and cloned into the project.


def read_entries(project: paths.Project) -> list[dict[str, str]]:
    return list(
        json.loads(manifest_file(project).read_text(encoding="utf-8"))["assets"]
    )


def write_entries(project: paths.Project, entries: Iterable[dict[str, str]]) -> None:
    order = ("source", "path", "sourceSha256", "metaSha256", "guid", "use")
    rows = sorted(entries, key=lambda e: (e["source"], e["path"].lower()))
    rows = [{**{k: e[k] for k in order if k in e}, **e} for e in rows]
    write_json(manifest_file(project), {"assets": rows})


def manifest_key(project: paths.Project) -> str:
    """The synced folder's key: vendor.json's bytes and the sync's version."""
    h = hashlib.sha256(
        SYNC_VERSION.encode() + b"\n" + manifest_file(project).read_bytes()
    )
    return h.hexdigest()[:16]


@dataclass
class Stamp:
    """What sync last wrote to a project's Assets/Vendor/ (in Library/): the manifest key, each
    asset with its hashes, and each file's size and mtime when written."""

    key: str
    assets: list[dict[str, str]]
    stats: dict[str, list[int]]

    @classmethod
    def load(cls, project: paths.Project) -> Stamp | None:
        try:
            raw = json.loads((project.library / STAMP).read_text(encoding="utf-8"))
            return cls(str(raw["key"]), list(raw["assets"]), dict(raw["stats"]))
        except (OSError, ValueError, KeyError, TypeError):
            return None

    def hashes(self) -> dict[str, str]:
        return synced_hashes(self.assets)


def synced_hashes(assets: list[dict[str, str]]) -> dict[str, str]:
    """Each file sync writes (relative to Assets/Vendor/) with its SHA-256."""
    out: dict[str, str] = {}
    for a in assets:
        rel = vendor_rel(a["path"])
        out[rel] = a["sha256"]
        if a.get("metaSha256"):
            out[rel + ".meta"] = a["metaSha256"]
    return out


def write_stamp(project: paths.Project, key: str, assets: list[dict[str, str]]) -> None:
    stats: dict[str, list[int]] = {}
    for f in walk_files(project.vendor_dir):
        st = f.stat()
        stats[f.relative_to(project.vendor_dir).as_posix()] = [
            st.st_size,
            st.st_mtime_ns,
        ]
    write_json(project.library / STAMP, {"key": key, "assets": assets, "stats": stats})


def modified(project: paths.Project, stamp: Stamp | None) -> dict[str, str]:
    """The files under Assets/Vendor/ that aren't as sync last wrote them (changed, or not
    written by it), relative to it, with their SHA-256. Unity's folder .meta files don't count.
    """
    wrote = stamp.hashes() if stamp else {}
    stats = stamp.stats if stamp else {}
    out: dict[str, str] = {}
    vendor = project.vendor_dir
    for f in walk_files(vendor) if vendor.is_dir() else ():
        rel = f.relative_to(vendor).as_posix()
        if rel.endswith(".meta") and f.with_suffix("").is_dir():
            continue
        st = f.stat()
        if stats.get(rel) == [st.st_size, st.st_mtime_ns]:
            continue
        sha = sha256_file(f)
        if wrote.get(rel) != sha:
            out[rel] = sha
    return out


def build(project: paths.Project, key: str) -> tuple[Path, bool]:
    """The cached sync of the project's manifest, made first if missing: (folder, made)."""
    entry = cache() / "synced" / key
    if entry.is_dir():
        return entry, False
    tmp = entry.with_name(f"{key}.{os.getpid()}.tmp")
    clone.remove_tree(tmp)
    (tmp / "Vendor").mkdir(parents=True)
    record = Record.load()
    unresolved: dict[str, str] = {}
    try:
        items = resolve(
            read_entries(project), Sources(load_roots(), record), unresolved
        )
        write_json(tmp / "assets.json", copy_items(items, tmp / "Vendor"))
    finally:
        record.save()
    if unresolved:
        listing = "".join(
            f"{g}  referenced by {p}\n" for g, p in sorted(unresolved.items())
        )
        (tmp / "unresolved.txt").write_text(listing, encoding="utf-8")
        print(
            f"vendor: {len(unresolved)} referenced GUIDs are in no source (package assets "
            f"such as URP's shaders, or missing files): {tilde(entry / 'unresolved.txt')}",
            file=sys.stderr,
        )
    try:
        tmp.rename(entry)
    except OSError:  # another checkout synced the same manifest first
        clone.remove_tree(tmp)
    return entry, True


def sync(project: paths.Project, force: bool = False) -> str:
    """Fills the project's Assets/Vendor/ from its vendor.json, refusing to lose a change made
    there unless `force`; returns a one-line summary."""
    key = manifest_key(project)
    entry, made = build(project, key)
    assets: list[dict[str, str]] = json.loads(
        (entry / "assets.json").read_text("utf-8")
    )
    if not force:
        new = synced_hashes(assets)
        lost = sorted(
            r
            for r, h in modified(project, Stamp.load(project)).items()
            if new.get(r) != h
        )
        if lost:
            shown = "\n  ".join(lost[:10]) + ("\n  ..." if len(lost) > 10 else "")
            raise VendorError(
                f"{project.name}: a sync would lose {len(lost)} changed or added files under "
                f"Assets/Vendor/:\n  {shown}\nkeep the changes with `am vendor save "
                f"{project.name}`, or discard them with `am vendor sync {project.name} --force`"
            )
    n = clone.clone_tree(entry / "Vendor", project.vendor_dir)
    write_stamp(project, key, assets)
    return f"{project.name}: {'synced' if made else 'cloned'} {n} vendor files (manifest {key})"


def ensure_synced(project: paths.Project) -> None:
    """Before a Unity step: nothing without vendor.json, a stamp check when Assets/Vendor/ is
    current, a sync otherwise."""
    if not manifest_file(project).exists():
        return
    stamp = Stamp.load(project)
    if stamp and stamp.key == manifest_key(project) and project.vendor_dir.is_dir():
        return
    try:
        print(f"vendor: {sync(project)}")
    except VendorError as e:
        raise SystemExit(f"vendor: {e}") from e


# Curation: adding entries, and saving modified files to the local repository.


def add(
    project: paths.Project,
    source: str,
    pattern: str,
    use: str | None,
    dry_run: bool = False,
) -> list[dict[str, str]]:
    """Adds the source's files at `pattern` (a path, or a glob) to vendor.json; returns them."""
    path = pattern if pattern.lower().startswith("assets/") else f"Assets/{pattern}"
    record = Record.load()
    try:
        src = Sources(load_roots(), record).get(source)
        exact = src.by_path.get(path.lower())
        hits = [exact] if exact and exact.asset else []
        hits = hits or sorted(
            (
                i
                for p, i in src.by_path.items()
                if i.asset and fnmatch.fnmatchcase(p, path.lower())
            ),
            key=lambda i: i.path.lower(),
        )
        if not hits:
            raise VendorError(f"{source}: no file matches {path}")
        exists = manifest_file(project).exists()
        current = {
            (e["source"], e["path"].lower()): e
            for e in (read_entries(project) if exists else [])
        }
        added: list[dict[str, str]] = []
        for item in hits:
            assert item.asset is not None
            old = current.get((source, item.path.lower()), {})
            entry = {"source": source, "path": item.path}
            entry["sourceSha256"] = src.sha256 or record.sha(item.asset)
            if source.startswith(LOCAL + ":") and item.meta is not None:
                entry["metaSha256"] = record.sha(item.meta)
            entry["guid"] = item.guid
            entry["use"] = use if use is not None else old.get("use", "")
            current[(source, item.path.lower())] = entry
            added.append(entry)
    finally:
        record.save()
    if not dry_run:
        write_entries(project, current.values())
    return added


def git_local(root: Path, *args: str) -> str:
    done = subprocess.run(
        ["git", *args], cwd=root, capture_output=True, text=True, check=False
    )
    if done.returncode != 0:
        raise VendorError(
            f"git {' '.join(args)} in {tilde(root)}: {done.stderr.strip()}"
        )
    return done.stdout


def init_local(root: Path) -> bool:
    """Creates the local-only repository of modified vendor files if missing (binary types in
    git LFS, so a private remote can be added later); True if it did."""
    if (root / ".git").exists():
        return False
    root.mkdir(parents=True, exist_ok=True)
    git_local(root, "init", "--quiet", "--initial-branch=main")
    git_local(root, "lfs", "install", "--local")
    nocase = [
        "*." + "".join(f"[{c}{c.upper()}]" if c.isalpha() else c for c in t)
        for t in LFS_TYPES
    ]
    git_local(root, "lfs", "track", *nocase)
    (root / ".gitignore").write_text(".DS_Store\n", encoding="utf-8")
    git_local(root, "add", ".gitattributes", ".gitignore")
    git_local(
        root,
        "commit",
        "--quiet",
        "-m",
        "Vendor files we modified: local only, never GitHub",
    )
    return True


def save(project: paths.Project, dry_run: bool = False) -> list[str]:
    """Copies the vendor files changed under Assets/Vendor/ into the local repository at
    <origin>/<path>, commits them there, and repoints their vendor.json entries at it (adding
    one for a changed dependency); returns their paths."""
    stamp = Stamp.load(project)
    if stamp is None:
        raise VendorError(
            f"{project.name}: Assets/Vendor/ has no sync stamp to compare with"
        )
    by_rel: dict[str, dict[str, str]] = {}
    for a in stamp.assets:
        by_rel[vendor_rel(a["path"])] = by_rel[vendor_rel(a["path"]) + ".meta"] = a
    changes = modified(project, stamp)
    for rel in sorted(r for r in changes if r not in by_rel):
        print(
            f"vendor: not from vendor.json, so not saved: Assets/Vendor/{rel}",
            file=sys.stderr,
        )
    changed = {by_rel[r]["guid"]: by_rel[r] for r in sorted(changes) if r in by_rel}
    names = [a["path"] for a in changed.values()]
    if dry_run or not changed:
        return names
    local = local_root()
    init_local(local)
    entries = {e["guid"].lower(): e for e in read_entries(project)}
    saved: list[str] = []
    for guid, a in changed.items():
        origin = (
            a["source"].partition(":")[2]
            if a["source"].startswith(LOCAL + ":")
            else a["source"]
        )
        here = project.vendor_dir / vendor_rel(a["path"])
        dest = local / origin_dir(origin) / a["path"]
        if not here.with_name(here.name + ".meta").exists():
            raise VendorError(
                f"Assets/Vendor/{vendor_rel(a['path'])} has no .meta to save"
            )
        dest.parent.mkdir(parents=True, exist_ok=True)
        for src, dst in (
            (here, dest),
            (here.with_name(here.name + ".meta"), dest.with_name(dest.name + ".meta")),
        ):
            shutil.copy2(src, dst)
            saved.append(dst.relative_to(local).as_posix())
        entry = entries.setdefault(guid, {"path": a["path"], "use": a.get("use", "")})
        entry["source"] = f"{LOCAL}:{origin}"
        entry["sourceSha256"] = sha256_file(dest)
        entry["metaSha256"] = sha256_file(dest.with_name(dest.name + ".meta"))
        entry["guid"] = guid
    git_local(local, "add", "--", *saved)
    if git_local(local, "status", "--porcelain", "--", *saved).strip():
        files = [PurePosixPath(n).name for n in names]
        summary = ", ".join(files[:3]) + (
            f" and {len(files) - 3} more" if len(files) > 3 else ""
        )
        git_local(local, "commit", "--quiet", "-m", f"{project.name}: {summary}")
    write_entries(project, entries.values())
    record = Record.load()
    for name in saved:  # so the guard knows the modified files too
        record.sha(local / name)
    record.save()
    return names


# The guard: what git would carry, checked blob by blob.


def git(repo: Path, *args: str, stdin: bytes | None = None) -> bytes:
    return subprocess.run(
        ["git", *args], cwd=repo, input=stdin, check=True, capture_output=True
    ).stdout


def raw_entries(out: bytes) -> list[tuple[str, str]]:
    """(path, blob) pairs from `git diff-tree -z` raw output, without deletions and submodules."""
    found: set[tuple[str, str]] = set()
    parts = out.split(b"\0")
    i = 0
    while i < len(parts) - 1:
        if not parts[i].startswith(b":"):
            i += 1
            continue
        fields = parts[i][1:].split()
        path = parts[i + 1].decode("utf-8", "surrogateescape")
        i += 2
        if not fields[4].startswith(b"D") and fields[1] != b"160000":
            found.add((path, fields[3].decode()))
    return sorted(found)


def tree_entries(repo: Path, rev: str) -> list[tuple[str, str]]:
    found: list[tuple[str, str]] = []
    for row in git(repo, "ls-tree", "-r", "-z", "--full-tree", rev).split(b"\0"):
        if row:
            meta, path = row.split(b"\t", 1)
            _mode, kind, sha = meta.split()
            if kind == b"blob":
                found.append((path.decode("utf-8", "surrogateescape"), sha.decode()))
    return found


def index_entries(repo: Path) -> list[tuple[str, str]]:
    """What is staged: the index's changes from HEAD (all of it before the first commit)."""
    head = subprocess.run(
        ["git", "rev-parse", "--verify", "--quiet", "HEAD^{commit}"],
        cwd=repo,
        capture_output=True,
        check=False,
    )
    if head.returncode == 0:
        return raw_entries(
            git(repo, "diff-index", "--cached", "-z", "--no-renames", "HEAD")
        )
    found: list[tuple[str, str]] = []
    for row in git(repo, "ls-files", "-s", "-z").split(b"\0"):
        if row:
            meta, path = row.split(b"\t", 1)
            mode, sha, _stage = meta.split()
            if mode != b"160000":
                found.append((path.decode("utf-8", "surrogateescape"), sha.decode()))
    return found


def commit_entries(repo: Path, commits: list[str]) -> list[tuple[str, str]]:
    """Every blob the commits add or change (a merge's against each parent)."""
    if not commits:
        return []
    out = git(
        repo,
        *(
            "diff-tree",
            "--stdin",
            "-r",
            "-z",
            "-m",
            "--root",
            "--no-renames",
            "--no-commit-id",
        ),
        stdin="\n".join(commits).encode() + b"\n",
    )
    return raw_entries(out)


def range_entries(repo: Path, revs: str) -> list[tuple[str, str]]:
    return commit_entries(repo, git(repo, "rev-list", revs).decode().split())


def pre_push_entries(
    repo: Path, remote: str, lines: Iterable[str]
) -> list[tuple[str, str]]:
    """What a push carries, from git's pre-push input: each ref's commits the remote lacks."""
    commits: list[str] = []
    for line in lines:
        fields = line.split()
        if not fields:
            continue
        if len(fields) != 4:
            raise VendorError(f"unexpected pre-push input: {line!r}")
        _local_ref, local_sha, _remote_ref, remote_sha = fields
        if local_sha == ZERO_SHA:  # a delete carries nothing
            continue
        known = remote_sha != ZERO_SHA and (
            subprocess.run(
                ["git", "cat-file", "-e", f"{remote_sha}^{{commit}}"],
                cwd=repo,
                capture_output=True,
                check=False,
            ).returncode
            == 0
        )
        revs = (
            [f"{remote_sha}..{local_sha}"]
            if known
            else [local_sha, "--not", f"--remotes={remote}"]
        )
        commits += git(repo, "rev-list", *revs).decode().split()
    return commit_entries(repo, sorted(set(commits)))


def read_blobs(
    repo: Path, entries: list[tuple[str, str]]
) -> Iterator[tuple[str, bytes]]:
    if not entries:
        return
    proc = subprocess.Popen(
        ["git", "cat-file", "--batch"],
        cwd=repo,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
    )
    stdin, stdout = proc.stdin, proc.stdout
    assert stdin is not None and stdout is not None
    try:
        for path, sha in entries:
            stdin.write(sha.encode() + b"\n")
            stdin.flush()
            header = stdout.readline().split()
            if len(header) < 3:
                raise VendorError(f"git cat-file can't read {path} ({sha})")
            data = stdout.read(int(header[2]))
            stdout.read(1)  # the newline after each object
            yield path, data
    finally:
        stdin.close()
        proc.wait()


def blob_reasons(
    path: str, data: bytes, hashes: dict[str, str], guids: dict[str, str]
) -> list[str]:
    """Every reason the file at `path` (repository-relative) may not leave the machine."""
    reasons: list[str] = []
    if VENDOR_PATH.search(path):
        reasons.append(
            "inside Assets/Vendor/ or Assets/Baked/ (vendor and derived files)"
        )
    for h in sha_forms(data):
        if h in hashes:
            reasons.append(f"SHA-256 {h[:12]}… is the vendor file {hashes[h]}")
            break
    guid = meta_guid(data) if path.lower().endswith(".meta") else ""
    if guid in guids:
        reasons.append(f"GUID {guid} is the vendor asset {guids[guid]}")
    return reasons


def check(
    repo: Path, entries: list[tuple[str, str]], record: Record
) -> list[tuple[str, str]]:
    """The guard: (path, reason) for each file the entries carry that may not leave."""
    hashes, guids = record.lookup()
    return [
        (path, reason)
        for path, data in read_blobs(repo, entries)
        for reason in blob_reasons(path, data, hashes, guids)
    ]


HOOK = """#!/bin/sh
# Amethyst's vendor guard, installed by `am vendor hook`. The repository is public, and a vendor
# file that reaches it can't be taken back, so this refuses any push carrying one. Never bypass it.
root=$(git rev-parse --show-toplevel) || exit 1
uv=$(command -v uv || echo @UV@)
if [ ! -x "$uv" ]; then
  echo "pre-push: uv is not installed, so the vendor guard can't run; refusing the push" >&2
  exit 1
fi
if [ ! -f "$root/tools/src/amethyst_tools/vendor.py" ]; then
  echo "pre-push: $root has no tools/ to run the vendor guard with; refusing the push" >&2
  exit 1
fi
exec "$uv" run --quiet --project "$root/tools" python -m amethyst_tools.vendor guard --pre-push "$1"
"""


def install_hook(repo: Path = paths.REPO) -> Path:
    """Installs the pre-push guard in the clone's shared hooks folder, which every checkout
    and worktree of it runs. A core.hooksPath would make git skip that folder, so it refuses one.
    """
    hooks_path = subprocess.run(
        ["git", "config", "--get", "core.hooksPath"],
        cwd=repo,
        capture_output=True,
        text=True,
    ).stdout.strip()
    if hooks_path:
        raise VendorError(
            f"core.hooksPath is set ({hooks_path}), so git would skip the guard"
        )
    common = git(
        repo, "rev-parse", "--path-format=absolute", "--git-common-dir"
    ).decode()
    hook = Path(common.strip()) / "hooks" / "pre-push"
    if hook.exists() and HOOK_MARK not in hook.read_text(
        encoding="utf-8", errors="replace"
    ):
        raise VendorError(
            f"{hook} is another pre-push hook; merge the guard into it by hand"
        )
    uv = shutil.which("uv") or "uv"
    hook.parent.mkdir(parents=True, exist_ok=True)
    tmp = hook.with_name(f"pre-push.{os.getpid()}.tmp")
    tmp.write_text(HOOK.replace("@UV@", shlex.quote(uv)), encoding="utf-8")
    tmp.chmod(0o755)
    tmp.replace(hook)
    return hook


# The command line.


def cmd_init(args: argparse.Namespace) -> int:
    local = local_root()
    try:
        made = init_local(local)
    except VendorError as e:
        return fail(e)
    state = "created" if made else "already exists:"
    print(
        f"vendor: {state} {tilde(local)}, the local-only repository of modified vendor files"
    )
    return 0


def cmd_sync(args: argparse.Namespace) -> int:
    if args.all:
        chosen = [p for p in paths.projects() if manifest_file(p).exists()]
    elif args.project:
        chosen = [paths.project(args.project)]
    else:
        return fail("name a project, or pass --all")
    for project in chosen:
        if not manifest_file(project).exists():
            return fail(f"{project.name} has no {MANIFEST}")
        try:
            print(f"vendor: {sync(project, args.force)}")
        except VendorError as e:
            return fail(e)
    return 0


def cmd_add(args: argparse.Namespace) -> int:
    project = paths.project(args.project)
    try:
        entries = add(project, args.source, args.path, args.use, args.dry_run)
    except VendorError as e:
        return fail(e)
    for e in entries:
        print(f"  {e['path']}  ({e['guid']})")
    verb = "would add" if args.dry_run else "added or updated"
    print(f"vendor: {verb} {len(entries)} entries in {project.name}/{MANIFEST}")
    return 0


def cmd_save(args: argparse.Namespace) -> int:
    project = paths.project(args.project)
    try:
        names = save(project, args.dry_run)
    except VendorError as e:
        return fail(e)
    for name in names:
        print(f"  {name}")
    if not names:
        print(f"vendor: {project.name}: no vendor file changed since the last sync")
    elif args.dry_run:
        print(f"vendor: {project.name}: would save {len(names)} changed assets")
    else:
        print(
            f"vendor: {project.name}: saved {len(names)} changed assets to "
            f"{tilde(local_root())} and repointed {MANIFEST} at them"
        )
        try:
            print(f"vendor: {sync(project)}")
        except VendorError as e:
            return fail(e)
    return 0


def cmd_guard(args: argparse.Namespace) -> int:
    repo = paths.REPO
    started = time.monotonic()
    roots = load_roots()
    try:
        if args.index:
            entries = index_entries(repo)
        elif args.range:
            entries = range_entries(repo, args.range)
        elif args.tree:
            entries = tree_entries(repo, args.tree)
        else:
            entries = pre_push_entries(repo, args.pre_push, sys.stdin)
        record = Record.load()
        record.refresh(roots)
        record.save()
        failures = check(repo, entries, record)
    except (VendorError, OSError, ValueError, subprocess.CalledProcessError) as e:
        err = e.stderr if isinstance(e, subprocess.CalledProcessError) else None
        detail = (
            err.decode(errors="replace").strip() if isinstance(err, bytes) else str(e)
        )
        print(f"vendor guard: FAIL: could not check ({detail})", file=sys.stderr)
        return 1
    seconds = time.monotonic() - started
    print(
        f"vendor guard: {'FAIL' if failures else 'PASS'} ({len(entries)} files, {seconds:.1f} s)"
    )
    if not any(scan_base(n, r).is_dir() for n, r in roots.items()):
        print(
            f"  warning: no vendor root exists here ({tilde(roots_file())})",
            file=sys.stderr,
        )
    width = max((len(p) for p, _ in failures), default=0)
    for path, reason in failures:
        print(f"  {path.ljust(width)}  {reason}")
    if failures:
        print("vendor files never reach the public repository: unstage or drop them")
    return 1 if failures else 0


def cmd_record(args: argparse.Namespace) -> int:
    started = time.monotonic()
    record = Record.load()
    hashed = record.refresh(load_roots())
    record.save()
    total = len(record.files) + sum(len(a[2]) for a in record.archives.values())
    print(
        f"vendor record: {total} vendor files ({hashed} hashed now) at {tilde(record_file())} "
        f"in {time.monotonic() - started:.1f} s"
    )
    return 0


def cmd_hook(args: argparse.Namespace) -> int:
    try:
        hook = install_hook()
    except (VendorError, subprocess.CalledProcessError) as e:
        return fail(e)
    print(f"vendor: pre-push guard installed for every checkout of this clone ({hook})")
    return 0


def fail(error: object) -> int:
    print(f"vendor: {error}", file=sys.stderr)
    return 1


def register(sub: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    parser = sub.add_parser(
        "vendor",
        help="vendor assets: sync, add, save, and the guard",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    cmds = parser.add_subparsers(dest="vendor_command", required=True)

    p = cmds.add_parser(
        "init", help="create the local-only repository of modified files"
    )
    p.set_defaults(func=cmd_init)

    p = cmds.add_parser(
        "sync", help="fill a project's Assets/Vendor/ from its vendor.json"
    )
    p.add_argument("project", nargs="?")
    p.add_argument(
        "--all", action="store_true", help="every project with a vendor.json"
    )
    p.add_argument(
        "--force", action="store_true", help="discard changes under Assets/Vendor/"
    )
    p.set_defaults(func=cmd_sync)

    p = cmds.add_parser("add", help="add a source's files to a project's vendor.json")
    p.add_argument("project")
    p.add_argument(
        "source", help="synty:<package file>, unity_assets, more_assets, ..."
    )
    p.add_argument("path", help="its Assets/... path, or a glob of them")
    p.add_argument("--use", help="what the project uses it for")
    p.add_argument(
        "--dry-run", action="store_true", help="list the matches, add nothing"
    )
    p.set_defaults(func=cmd_add)

    p = cmds.add_parser(
        "save", help="keep changed vendor files in the local repository"
    )
    p.add_argument("project")
    p.add_argument(
        "--dry-run", action="store_true", help="list the changed files, save nothing"
    )
    p.set_defaults(func=cmd_save)

    p = cmds.add_parser("guard", help="refuse vendor files in what git would carry")
    mode = p.add_mutually_exclusive_group(required=True)
    mode.add_argument("--index", action="store_true", help="the staged changes")
    mode.add_argument("--range", metavar="A..B", help="the commits in a range")
    mode.add_argument("--tree", metavar="REV", help="every file of a commit")
    mode.add_argument(
        "--pre-push", metavar="REMOTE", help="a push, from git's hook input"
    )
    p.set_defaults(func=cmd_guard)

    p = cmds.add_parser(
        "record", help="hash every file under this machine's vendor roots"
    )
    p.set_defaults(func=cmd_record)

    p = cmds.add_parser("hook", help="install the pre-push guard for every checkout")
    p.set_defaults(func=cmd_hook)


def main(argv: list[str] | None = None) -> int:
    """`python -m amethyst_tools.vendor ...`: the vendor commands without the rest of `am`
    (the pre-push hook runs the guard this way)."""
    parser = argparse.ArgumentParser(prog="am")
    register(parser.add_subparsers(dest="command", required=True))
    args = parser.parse_args(["vendor", *(sys.argv[1:] if argv is None else argv)])
    return int(args.func(args))


if __name__ == "__main__":
    sys.exit(main())
