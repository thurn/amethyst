"""The wireframe mesh recipes' kit (.agents/skills/wireframe-mesh/SKILL.md).

The look package's crowd baker (packages/com.amethyst.look/Editor/CrowdBaker.cs) runs
a roster entry's recipe, tools/recipes/ID.py, under Blender's bundled Python (numpy at
hand, no install) before it bakes the entry:

    python kit.py RECIPE SOURCE_DIR OUT_DIR

SOURCE_DIR holds the vendor mesh as the crowd baker combines it (`Source`); the recipe's
`build(source) -> Mesh` returns the new mesh, written to OUT_DIR for the baker to read
back. Both folders live in the project's ignored Library/: nothing here is committed but
the recipes.

A recipe is a module whose docstring says what it changes and why, with

    def build(source: Source) -> Mesh: ...

`Source` is the vendor model as the crowd draws it: every active renderer combined in
root space, metres at the entry's height, its rest pose, each vertex skinned to four of
its bones (a held weapon rides its hand's bone). The returned `Mesh` is skinned to the
same bones, so the vendor rig and clips drive it. Which edges draw: `mesh.edges` flags
each triangle's edges (1 for the edge opposite that corner), else every crease sharper
than `mesh.edge_angle` degrees (24, the trailer's; 1 draws every edge but a flat quad's
diagonal). The baker never decimates a recipe's mesh: its topology is the drawing.
"""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

BLENDER = Path("/Applications/Blender.app/Contents/MacOS/Blender")


class RecipeError(Exception):
    pass


@dataclass
class Part:
    """One renderer of the vendor model: its name and vertex range; rigid when it rides
    one bone (a held weapon)."""

    name: str
    first: int
    count: int
    rigid: bool


@dataclass
class Source:
    """The vendor mesh as the crowd baker combined it: numpy arrays over its vertices
    and triangles."""

    id: str
    height_m: float
    positions: np.ndarray  # (n, 3) float32, root space, metres
    normals: np.ndarray  # (n, 3) float32
    triangles: np.ndarray  # (m, 3) int32
    bones: np.ndarray  # (n, 4) int32, into bone_names
    weights: np.ndarray  # (n, 4) float32, summing to 1
    bone_names: list[str]
    bone_paths: list[str]
    binds: np.ndarray  # (bones, 4, 4): root space into each bone's, at the rest pose
    parts: list[Part]

    @classmethod
    def read(cls, folder: Path) -> Source:
        meta = json.loads((folder / "source.json").read_text(encoding="utf-8"))
        n, m = meta["vertices"], meta["triangles"]

        def array(name: str, dtype: type, count: int, width: int) -> np.ndarray:
            return np.fromfile(folder / name, dtype=dtype).reshape(count, width)

        return cls(
            id=meta["id"],
            height_m=meta["heightM"],
            positions=array("positions.f32", np.float32, n, 3),
            normals=array("normals.f32", np.float32, n, 3),
            triangles=array("triangles.i32", np.int32, m, 3),
            bones=array("bones.i32", np.int32, n, 4),
            weights=array("weights.f32", np.float32, n, 4),
            bone_names=[b["name"] for b in meta["bones"]],
            bone_paths=[b["path"] for b in meta["bones"]],
            binds=np.fromfile(folder / "binds.f32", dtype=np.float32).reshape(-1, 4, 4),
            parts=[
                Part(p["name"], p["first"], p["count"], p["rigid"])
                for p in meta["parts"]
            ],
        )

    def part(self, name: str) -> Part:
        for p in self.parts:
            if p.name == name:
                return p
        names = ", ".join(p.name for p in self.parts)
        raise RecipeError(f"{self.id}: no part {name!r} (parts: {names})")

    def bone(self, name: str) -> int:
        """A bone's index by its name."""
        try:
            return self.bone_names.index(name)
        except ValueError:
            raise RecipeError(f"{self.id}: no bone {name!r}") from None

    def joint(self, name: str) -> np.ndarray:
        """A bone's origin in root space at the rest pose."""
        return np.linalg.inv(self.binds[self.bone(name)].astype(np.float64))[:3, 3]

    def part_bone(self, name: str) -> int:
        """The bone a rigid part (a held weapon) rides."""
        return int(self.bones[self.part(name).first, 0])

    def transfer(self, points, bones: list[str]) -> tuple[np.ndarray, np.ndarray]:
        """Skin weights for `points` by transfer from the source's nearest vertices
        whose strongest bone is one of `bones` (a shape's own region: never across
        limbs or sides), averaged over the three nearest by inverse distance and kept
        to the four strongest of `bones`. Bone indices and weights, (n, 4) each."""
        allowed = np.array([self.bone(b) for b in bones])
        pool = np.nonzero(np.isin(self.bones[:, 0], allowed))[0]
        if len(pool) == 0:
            raise RecipeError(f"{self.id}: no vertex rides {bones}")
        pts = np.atleast_2d(np.asarray(points, np.float64))
        d = np.linalg.norm(pts[:, None, :] - self.positions[pool][None, :, :], axis=2)
        near = np.argsort(d, axis=1)[:, :3]
        idx = np.zeros((len(pts), 4), np.int32)
        w = np.zeros((len(pts), 4), np.float32)
        for i in range(len(pts)):
            total: dict[int, float] = {}
            for j in near[i]:
                v = pool[j]
                share = 1.0 / (d[i, j] + 1e-4)
                for b, wt in zip(self.bones[v], self.weights[v]):
                    if wt > 0 and b in allowed:
                        total[int(b)] = total.get(int(b), 0.0) + share * float(wt)
            top = sorted(total.items(), key=lambda kv: -kv[1])[:4]
            s = sum(t for _, t in top)
            for k, (b, t) in enumerate(top):
                idx[i, k], w[i, k] = b, t / s
        return idx, w

    def mesh(self) -> Mesh:
        """The source as a recipe's mesh, unchanged (a recipe's starting point)."""
        return Mesh(
            self.positions.copy(),
            self.triangles.copy(),
            self.bones.copy(),
            self.weights.copy(),
            self.normals.copy(),
        )

    def save(self, path: Path) -> None:
        """The arrays and bone names in one .npz, for a Blender script."""
        np.savez(
            path,
            positions=self.positions,
            normals=self.normals,
            triangles=self.triangles,
            bones=self.bones,
            weights=self.weights,
            bone_names=np.array(self.bone_names),
        )


@dataclass
class Mesh:
    """A recipe's mesh: triangles over vertices skinned to the source's bones; normals
    optional (the baker computes them). Which edges draw: `edges` (m, 3), 1 for the
    edge opposite each corner of each triangle; else every crease sharper than
    `edge_angle` degrees."""

    positions: np.ndarray  # (n, 3), root space, metres at the entry's height
    triangles: np.ndarray  # (m, 3) into positions
    bones: np.ndarray  # (n, 4) into the source's bones
    weights: np.ndarray  # (n, 4), summing to 1
    normals: np.ndarray | None = field(default=None)
    edges: np.ndarray | None = field(default=None)  # (m, 3) 0 or 1
    edge_angle: float = 24.0

    @classmethod
    def load(cls, path: Path) -> Mesh:
        """A mesh a Blender script saved with numpy.savez (the arrays named as the
        fields)."""
        a = np.load(path)
        return cls(
            a["positions"],
            a["triangles"],
            a["bones"],
            a["weights"],
            a.get("normals"),
            a.get("edges"),
        )

    def check(self, bones: int) -> None:
        n = len(self.positions)
        problems = []
        if self.positions.shape != (n, 3) or n == 0:
            problems.append(f"positions {self.positions.shape}")
        if (
            self.triangles.ndim != 2
            or self.triangles.shape[1] != 3
            or len(self.triangles) == 0
        ):
            problems.append(f"triangles {self.triangles.shape}")
        elif self.triangles.min() < 0 or self.triangles.max() >= n:
            problems.append("triangles index past the vertices")
        if self.bones.shape != (n, 4) or self.weights.shape != (n, 4):
            problems.append(
                f"bones {self.bones.shape} and weights {self.weights.shape} need (n, 4)"
            )
        elif self.bones.min() < 0 or self.bones.max() >= bones:
            problems.append(f"bones index past the source's {bones}")
        elif not np.allclose(self.weights.sum(axis=1), 1, atol=1e-3):
            problems.append("weights don't sum to 1")
        if self.normals is not None and self.normals.shape != (n, 3):
            problems.append(f"normals {self.normals.shape}")
        if self.edges is not None and self.edges.shape != self.triangles.shape:
            problems.append(f"edges {self.edges.shape} need {self.triangles.shape}")
        if not 0 < self.edge_angle < 180:
            problems.append(f"edge_angle {self.edge_angle} is not in (0, 180)")
        if problems:
            raise RecipeError("the recipe's mesh is malformed: " + "; ".join(problems))

    def write(self, folder: Path) -> None:
        """The arrays the crowd baker reads back."""
        folder.mkdir(parents=True, exist_ok=True)
        for old in folder.iterdir():
            old.unlink()
        self.positions.astype("<f4").tofile(folder / "positions.f32")
        self.triangles.astype("<i4").tofile(folder / "triangles.i32")
        self.bones.astype("<i4").tofile(folder / "bones.i32")
        self.weights.astype("<f4").tofile(folder / "weights.f32")
        if self.normals is not None:
            self.normals.astype("<f4").tofile(folder / "normals.f32")
        if self.edges is not None:
            (self.edges != 0).astype(np.uint8).tofile(folder / "edges.u8")
        meta = {
            "vertices": len(self.positions),
            "triangles": len(self.triangles),
            "edgeAngleDeg": self.edge_angle,
        }
        (folder / "mesh.json").write_text(json.dumps(meta) + "\n", encoding="utf-8")


# ---------------------------------------------------------------------------------------
# Building shapes.


class Builder:
    """Shapes for a recipe: convex, wound outward, each vertex rigid to one bone or
    weighted by hand. Each shape draws its polygons' outlines, never the diagonals that
    cut them into triangles."""

    def __init__(self, source: Source):
        self.source = source
        self.positions: list[np.ndarray] = []
        self.triangles: list[np.ndarray] = []
        self.bones: list[np.ndarray] = []
        self.weights: list[np.ndarray] = []
        self.edges: list[np.ndarray] = []
        self.count = 0

    def add(self, points, faces, bone, weights=None) -> None:
        """A convex shape: points (n, 3) and faces (lists of point indices, convex
        polygons), each wound outward from the shape's centre. `bone`: one bone's name
        or index, or a name per point; or, with `weights` (n, 4), an (n, 4) array of
        bone indices (`Source.transfer` makes both)."""
        pts = np.asarray(points, np.float64)
        centre = pts.mean(axis=0)
        tris, edges = [], []
        for f in faces:
            for k in range(1, len(f) - 1):
                tri = [f[0], f[k], f[k + 1]]
                # The edge opposite each corner: fk-fk+1 is always the polygon's; f0-fk+1
                # only for its last triangle, f0-fk only for its first.
                flag = [1, int(k + 1 == len(f) - 1), int(k == 1)]
                a, b, c = pts[tri]
                if np.dot(np.cross(b - a, c - a), (a + b + c) / 3 - centre) < 0:
                    tri = [tri[0], tri[2], tri[1]]
                    flag = [flag[0], flag[2], flag[1]]
                tris.append(tri)
                edges.append(flag)
        if weights is None and isinstance(bone, (int, np.integer)):
            idx = np.zeros((len(pts), 4), np.int32)
            idx[:, 0] = bone
            w = np.zeros((len(pts), 4), np.float32)
            w[:, 0] = 1
        elif weights is None:
            names = [bone] * len(pts) if isinstance(bone, str) else bone
            idx = np.zeros((len(pts), 4), np.int32)
            idx[:, 0] = [self.source.bone(n) for n in names]
            w = np.zeros((len(pts), 4), np.float32)
            w[:, 0] = 1
        else:
            idx, w = np.asarray(bone, np.int32), np.asarray(weights, np.float32)
        self.positions.append(pts)
        self.triangles.append(np.asarray(tris, np.int64) + self.count)
        self.edges.append(np.asarray(edges, np.uint8))
        self.bones.append(idx)
        self.weights.append(w)
        self.count += len(pts)

    def prism(self, rings, faces_each: int, bone, weights=None) -> None:
        """A shape lofted through rings of `faces_each` points each (a ring may be one
        point, an apex), capped at both ends. `bone`: one name, one per ring, or one per
        point (or indices with `weights`, as for `add`)."""
        pts, offsets = [], []
        for r in rings:
            offsets.append(len(pts))
            pts.extend(np.atleast_2d(r))
        faces = []
        for i in range(len(rings) - 1):
            a, b = offsets[i], offsets[i + 1]
            na, nb = len(np.atleast_2d(rings[i])), len(np.atleast_2d(rings[i + 1]))
            for k in range(faces_each):
                if na == 1:
                    faces.append([a, b + k, b + (k + 1) % nb])
                elif nb == 1:
                    faces.append([a + k, a + (k + 1) % na, b])
                else:
                    faces.append([a + k, a + (k + 1) % na, b + (k + 1) % nb, b + k])
        if len(np.atleast_2d(rings[0])) > 1:
            faces.append(list(range(offsets[0], offsets[0] + len(rings[0]))))
        if len(np.atleast_2d(rings[-1])) > 1:
            faces.append(list(range(offsets[-1], offsets[-1] + len(rings[-1]))))
        if weights is None and isinstance(bone, list) and len(bone) == len(rings):
            bone = [bone[i] for i, r in enumerate(rings) for _ in np.atleast_2d(r)]
        self.add(pts, faces, bone, weights)

    def box(self, lo, hi, bone) -> None:
        """An axis-aligned box from corner `lo` to corner `hi`."""
        (x0, y0, z0), (x1, y1, z1) = lo, hi
        pts = [(x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]
        faces = [
            [0, 1, 3, 2],
            [4, 5, 7, 6],
            [0, 1, 5, 4],
            [2, 3, 7, 6],
            [0, 2, 6, 4],
            [1, 3, 7, 5],
        ]
        self.add(pts, faces, bone)

    def mesh(self, keep: Mesh | None = None) -> Mesh:
        """The shapes as a mesh, after `keep` (vendor parts kept as they are, drawn by
        their 24-degree creases), with every triangle's edges flagged."""
        pos = [*([keep.positions] if keep else []), *self.positions]
        n0 = len(keep.positions) if keep else 0
        tris = [*([keep.triangles] if keep else []), *(t + n0 for t in self.triangles)]
        bones = [*([keep.bones] if keep else []), *self.bones]
        weights = [*([keep.weights] if keep else []), *self.weights]
        kept = [creases(keep.positions, keep.triangles)] if keep else []
        return Mesh(
            np.vstack(pos).astype(np.float32),
            np.vstack(tris).astype(np.int32),
            np.vstack(bones).astype(np.int32),
            np.vstack(weights).astype(np.float32),
            edges=np.vstack([*kept, *self.edges]),
        )


def ring(centre, u, v, radii, n: int, phase: float = 0.0) -> np.ndarray:
    """n points round `centre` in the plane of unit vectors u and v, radii (ru, rv)."""
    t = phase + 2 * np.pi * np.arange(n) / n
    return (
        np.asarray(centre)
        + np.outer(np.cos(t) * radii[0], u)
        + np.outer(np.sin(t) * radii[1], v)
    )


def unit(v) -> np.ndarray:
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


def keep_parts(source: Source, drop) -> Mesh:
    """The source without the parts named in `drop`, its unused vertices gone."""
    keep_vertex = np.ones(len(source.positions), bool)
    for p in source.parts:
        if p.name in drop:
            keep_vertex[p.first : p.first + p.count] = False
    tris = source.triangles[keep_vertex[source.triangles].all(axis=1)]
    used = np.unique(tris)
    remap = np.full(len(source.positions), -1)
    remap[used] = np.arange(len(used))
    return Mesh(
        source.positions[used].copy(),
        remap[tris].astype(np.int32),
        source.bones[used].copy(),
        source.weights[used].copy(),
        source.normals[used].copy(),
    )


def creases(
    positions: np.ndarray, triangles: np.ndarray, angle_deg: float = 24.0
) -> np.ndarray:
    """Each triangle's edges that draw, as StyleMesh.FeatureEdges flags them (corners
    welded at 0.1 mm; an edge draws when its faces turn by more than `angle_deg`, or
    when it is open or shared by more than two)."""
    _, ids = np.unique(
        np.round(positions.astype(np.float64) * 1e4).astype(np.int64),
        axis=0,
        return_inverse=True,
    )
    corners = ids.reshape(-1)[triangles]
    p = positions.astype(np.float64)[triangles]
    normal = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
    length = np.linalg.norm(normal, axis=1)
    live = length > 1e-12
    normal = normal / np.where(live, length, 1)[:, None]
    sides: dict[tuple[int, int], list[int]] = {}
    for t in np.nonzero(live)[0]:
        for k in range(3):
            a, c = corners[t, (k + 1) % 3], corners[t, (k + 2) % 3]
            if a != c:
                sides.setdefault((min(a, c), max(a, c)), []).append(3 * t + k)
    flags = np.zeros(triangles.shape, np.uint8)
    cos = np.cos(np.radians(angle_deg))
    for faces in sides.values():
        if len(faces) != 2 or normal[faces[0] // 3] @ normal[faces[1] // 3] < cos:
            for f in faces:
                flags[f // 3, f % 3] = 1
    return flags


def blender(script: Path, *args: str, timeout: float = 600) -> None:
    """Runs a Python script in Blender headless, its arguments after `--`
    (sys.argv); raises on failure. `Source.save` and `Mesh.load` trade .npz files
    with it."""
    if not BLENDER.exists():
        raise RecipeError(f"no Blender at {BLENDER}")
    cmd = [
        str(BLENDER),
        "--background",
        "--factory-startup",
        "--python-exit-code",
        "1",
        "--python",
        str(script),
        "--",
        *args,
    ]
    res = subprocess.run(
        cmd, capture_output=True, text=True, timeout=timeout, check=False
    )
    if res.returncode != 0:
        raise RecipeError(
            f"Blender failed (exit {res.returncode}) on {script}:\n"
            f"{res.stdout[-2000:]}{res.stderr[-2000:]}"
        )


# ---------------------------------------------------------------------------------------
# The crowd baker's entry point.


def run(recipe: Path, source_dir: Path, out: Path) -> Mesh:
    """Builds the recipe's mesh from the exported source and writes it to `out`."""
    spec = importlib.util.spec_from_file_location(f"recipe_{recipe.stem}", recipe)
    if spec is None or spec.loader is None:
        raise RecipeError(f"can't load {recipe}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    if not callable(getattr(module, "build", None)):
        raise RecipeError(f"{recipe.name} needs build(source) -> Mesh")
    source = Source.read(source_dir)
    mesh = module.build(source)
    if not isinstance(mesh, Mesh):
        raise RecipeError(f"{recipe.name}'s build returned {type(mesh).__name__}")
    mesh.check(len(source.bone_names))
    mesh.write(out)
    return mesh


if __name__ == "__main__":
    # The recipes import this file as `kit`: run that module, not __main__, so their
    # Mesh is the one checked.
    sys.dont_write_bytecode = True  # no __pycache__ among the recipes
    import kit

    try:
        built = kit.run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]))
    except kit.RecipeError as e:
        print(f"recipe: {e}", file=sys.stderr)
        sys.exit(2)
    print(f"recipe: {len(built.triangles)} triangles, {len(built.positions)} vertices")
