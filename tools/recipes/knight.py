"""The knight, the hero: the Synty knight rebuilt as a suit of simple faceted plates,
every edge it designs drawn, around what names a knight: a great helm with a crest, a
heater shield and a broadsword.

The vendor mesh draws as a white tangle up close and a filled knot at game size: its
2,798-triangle body lights creases at every strap, rivet, finger and fold of mail, its
sword is a hairline and its sheath a second scribble at the hip. What changes:

- The helm: an eight-sided great helm (a flat crown, a brow ring, a narrowing neck
  guard), a visor slit across its face and a crest from brow to nape where the vendor's
  plume trails, wedge-shaped so its sides face up toward the camera.
- The body: a breastplate lofted from the waist to the shoulders with a keel down its
  front, a gorget, and faulds flaring from the waist over the hips.
- The limbs: each arm one six-sided prism from the shoulder through the elbow to a
  gauntlet flaring to the wrist, a mitten hand; each leg one prism from the hip through the knee
  to the ankle, a diamond poleyn on the knee and a wedge sabaton; faceted pauldrons on
  the shoulders.
- The broadsword: a broad blade of diamond section with a ridge, a crossguard, a grip
  and a pommel, on the vendor sword's axis and length. The shield: a flat heater on the
  vendor shield's plane, bevelled round its rim so its outline draws on both faces.
- Dropped: the sheath, straps, rivets, fingers, eyes and brows.

Each shape draws its polygons' outlines. The body's shapes are skinned by transfer
from the vendor's nearest vertices over their own bones (never across sides); the helm
is rigid on the head, the hands on theirs, the weapons on the hand bones that carry
the vendor's.

Source: Synty POLYGON Knights, Character_Knight_01_Orange.prefab, SM_Wep_Broadsword_01
and SM_Wep_Shield_01 (testbeds/look/vendor.json; pack sha256
ef0aedb72af33542c0b846b8b9c0e31e79949b7e46bbc90c41d93d115a04e9f8). Numpy only, under
Blender 5.2.3 LTS's Python. Built by every player build's crowd bake (roster entries
`knight` and `hero`); review: `just shots look` (`review`, `review-vendor`).
"""

from __future__ import annotations

import numpy as np

from kit import Builder, Mesh, Source, ring, unit

X, Y, Z = np.eye(3)


def skinned(b: Builder, source: Source, rings, n: int, region: list[str]) -> None:
    """A loft through `rings`, skinned by transfer from the vendor's `region`."""
    pts = np.vstack([np.atleast_2d(r) for r in rings])
    idx, w = source.transfer(pts, region)
    b.prism(rings, n, idx, w)


def helm(b: Builder) -> None:
    """An eight-sided great helm round the vendor's head, its crown flat, a visor slit
    across the face and a wedge crest from brow to nape."""
    z0 = 0.0

    def oct(y, rx, rz):
        return ring((0, y, z0), X, Z, (rx, rz), 8, np.pi / 8)

    b.prism(
        [
            oct(1.43, 0.1, 0.115),
            oct(1.5, 0.125, 0.14),
            oct(1.66, 0.125, 0.14),
            oct(1.74, 0.09, 0.105),
        ],
        8,
        "Head",
    )
    # The visor slit: a thin bar proud of the face.
    front = 0.14 * np.cos(np.pi / 8) + z0
    b.box((-0.085, 1.595, front - 0.01), (0.085, 1.615, front + 0.012), "Head")
    # The crest: a roof-shaped ridge from brow to nape where the vendor's plume trails,
    # its two slopes facing up toward the camera (a thin fin edge-on drew solid).
    stations = [  # z, foot, ridge, half width
        (0.1, 1.7, 1.74, 0.03),
        (0.03, 1.72, 1.84, 0.055),
        (-0.13, 1.71, 1.85, 0.055),
        (-0.29, 1.62, 1.72, 0.035),
    ]
    rings = [
        np.array([(-hw, foot, z), (0, ridge, z), (hw, foot, z)])
        for z, foot, ridge, hw in stations
    ]
    b.prism([*rings, np.array([0, 1.56, -0.37])], 3, "Head")


def body(b: Builder, source: Source) -> None:
    """The breastplate with a keel down its front, the gorget and the faulds."""

    def oct(y, rx, rz, z=0.0):
        return ring((0, y, z), X, Z, (rx, rz), 8, np.pi / 2)

    skinned(
        b,
        source,
        [
            oct(0.95, 0.17, 0.14),
            oct(1.18, 0.21, 0.18, 0.01),
            oct(1.33, 0.2, 0.15, -0.01),
            oct(1.42, 0.11, 0.09, -0.02),
        ],
        8,
        ["Hips", "Spine_01", "Spine_02", "Spine_03"],
    )
    skinned(
        b,
        source,
        [
            ring((0, 1.4, -0.02), X, Z, (0.075, 0.07), 6, np.pi / 2),
            ring((0, 1.47, -0.01), X, Z, (0.07, 0.065), 6, np.pi / 2),
        ],
        6,
        ["Spine_03", "Neck"],
    )
    skinned(
        b,
        source,
        [oct(0.99, 0.18, 0.15), oct(0.8, 0.22, 0.19), oct(0.72, 0.23, 0.2)],
        8,
        ["Hips", "UpperLeg_L", "UpperLeg_R"],
    )


def arm(b: Builder, source: Source, side: int, s: str) -> None:
    """A pauldron, one prism from the shoulder through the elbow to the wrist, a mitten."""
    x = side

    def hexa(at, r):
        return ring((x * at[0], at[1], at[2]), Y, Z, (r, r), 6, np.pi / 6)

    skinned(
        b,
        source,
        [
            hexa((0.21, 1.335, -0.045), 0.065),
            hexa((0.508, 1.307, -0.05), 0.052),
            hexa((0.75, 1.307, -0.035), 0.064),
        ],
        6,
        [f"Shoulder_{s}", f"Elbow_{s}", f"Hand_{s}"],
    )

    # The pauldron: a half-octagon lofted out along the arm, open side down.
    def cap(at, r, y, z=-0.05):
        t = np.linspace(0, np.pi, 5)
        return np.c_[np.full(5, x * at), y + r * np.sin(t) * 0.9, z + r * np.cos(t)]

    skinned(
        b,
        source,
        [cap(0.13, 0.11, 1.33), cap(0.27, 0.14, 1.32), cap(0.4, 0.11, 1.29)],
        5,
        [f"Clavicle_{s}", f"Shoulder_{s}"],
    )
    if s == "R":  # the left hand is behind the shield
        b.box((0.765, 1.26, -0.09), (0.9, 1.355, 0.035), "Hand_R")


def leg(b: Builder, source: Source, side: int, s: str) -> None:
    """One prism from the hip through the knee to the ankle, a poleyn, a sabaton."""
    x = side * 0.1

    def quad(y, r, dx=0.0, z=-0.02):
        return ring((x + side * dx, y, z), X, Z, (r, r * 1.1), 4, np.pi / 4)

    skinned(
        b,
        source,
        [
            quad(0.8, 0.1),
            quad(0.415, 0.07, 0.007),
            quad(0.25, 0.075, 0.009),
            quad(0.08, 0.06, 0.01, -0.03),
        ],
        4,
        [f"UpperLeg_{s}", f"LowerLeg_{s}", f"Ankle_{s}"],
    )
    knee = np.array((x + side * 0.007, 0.43, 0.065))
    pts = [knee + d for d in (0.05 * Y, -0.05 * Y, 0.045 * X, -0.045 * X, 0.04 * Z)]
    pts.append(knee - 0.02 * Z)
    faces = [[0, 2, 4], [0, 4, 3], [1, 4, 2], [1, 3, 4], [0, 3, 5], [0, 5, 2]]
    faces += [[1, 2, 5], [1, 5, 3]]
    b.add(pts, faces, f"LowerLeg_{s}")
    cx = x + side * 0.01
    heel = [(cx - 0.055, 0.0, -0.11), (cx + 0.055, 0.0, -0.11)]
    heel += [(cx + 0.05, 0.12, -0.09), (cx - 0.05, 0.12, -0.09)]
    toe = [(cx - 0.05, 0.0, 0.17), (cx + 0.05, 0.0, 0.17)]
    toe += [(cx + 0.035, 0.05, 0.17), (cx - 0.035, 0.05, 0.17)]
    pts = np.array(heel + toe)
    idx, w = source.transfer(pts, [f"Ankle_{s}", f"Ball_{s}"])
    faces = [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5]]
    faces += [[2, 3, 7, 6], [3, 0, 4, 7]]
    b.add(pts, faces, idx, w)


def frame(q: np.ndarray):
    """A part's middle and principal axes (longest first), and its points' coordinates
    along them."""
    mid = q.mean(axis=0)
    _, _, axes = np.linalg.svd(q - mid)
    return mid, axes, (q - mid) @ axes.T


def sword(b: Builder, source: Source) -> None:
    """A broad blade of diamond section, a crossguard, a grip and a pommel on the vendor
    sword's axis and length; the guard where the vendor's is widest."""
    p = source.part("SM_Wep_Broadsword_01(Clone)")
    q = source.positions[p.first : p.first + p.count].astype(np.float64)
    mid, axes, c = frame(q)
    u, w = axes[0], axes[1]
    t = unit(np.cross(u, w))
    along = c[:, 0]
    # The guard: the widest slice along the axis; the blade is the longer side of it.
    bins = np.linspace(along.min(), along.max(), 41)
    width = [
        (
            np.ptp(c[(along >= lo) & (along <= hi), 1])
            if ((along >= lo) & (along <= hi)).any()
            else 0
        )
        for lo, hi in zip(bins[:-1], bins[1:])
    ]
    g = int(np.argmax(width))
    guard_at = 0.5 * (bins[g] + bins[g + 1])
    tip, pommel = along.min(), along.max()
    if guard_at - tip < pommel - guard_at:
        u, tip, pommel, guard_at = -u, -pommel, -tip, -guard_at
    bone = source.part_bone(p.name)

    def at(s, dw=0.0, dt=0.0):
        return mid + u * s + w * dw + t * dt

    def diamond(s, wide, thick):
        return np.array(
            [
                at(s, wide / 2),
                at(s, 0, thick / 2),
                at(s, -wide / 2),
                at(s, 0, -thick / 2),
            ]
        )

    blade = guard_at - 0.01
    b.prism(
        [at(tip), diamond(tip + 0.12, 0.085, 0.03), diamond(blade, 0.1, 0.036)],
        4,
        bone,
    )
    half = 0.12
    box = [
        at(guard_at + dx, dw, dt)
        for dx in (-0.01, 0.025)
        for dw, dt in ((half, 0.022), (half, -0.022), (-half, -0.022), (-half, 0.022))
    ]
    faces = [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6]]
    faces += [[3, 0, 4, 7]]
    b.add(box, faces, bone)
    end = pommel - 0.04
    b.prism(
        [diamond(guard_at + 0.025, 0.03, 0.03), diamond(end, 0.026, 0.026)], 4, bone
    )
    b.prism([at(end), diamond(end + 0.02, 0.05, 0.05), at(pommel)], 4, bone)


def shield(b: Builder, source: Source) -> None:
    """A flat heater shield on the vendor shield's plane and extent, its broad end up,
    its rim bevelled to a ridge so the outline draws from both faces."""
    p = source.part("SM_Wep_Shield_01(Clone)")
    q = source.positions[p.first : p.first + p.count].astype(np.float64)
    mid, axes, c = frame(q)
    # Up along the shield is whichever of its two long axes is nearer world up.
    a0, a1, normal = axes
    up, across = (a0, a1) if abs(a0 @ Y) > abs(a1 @ Y) else (a1, a0)
    cu, ca = (c[:, 0], c[:, 1]) if up is a0 else (c[:, 1], c[:, 0])
    if up @ Y < 0:
        up, cu = -up, -cu
    top, bottom = cu.max(), cu.min()
    half = 0.5 * np.ptp(ca)
    centre = mid + across * 0.5 * (ca.max() + ca.min())
    h = top - bottom
    # The heater's outline from its top left, clockwise, as (across, up).
    outline = [(-1, 1), (1, 1), (1, 0.6), (0.85, 0.3), (0.5, 0.1), (0, 0)]
    outline += [(-0.5, 0.1), (-0.85, 0.3), (-1, 0.6)]

    def layer(scale, depth):
        return np.array(
            [
                centre
                + across * (a * half * scale)
                + up * (bottom + h * (0.5 + (v - 0.5) * scale))
                + normal * depth
                for a, v in outline
            ]
        )

    # Its front faces away from the hand.
    if (mid - source.joint("Hand_L")) @ normal < 0:
        normal = -normal
    bone = source.part_bone(p.name)
    b.prism(
        [layer(0.92, -0.012), layer(1.0, 0.0), layer(0.9, 0.018)],
        len(outline),
        bone,
    )


def build(source: Source) -> Mesh:
    b = Builder(source)
    helm(b)
    body(b, source)
    for side, s in ((-1, "L"), (1, "R")):
        arm(b, source, side, s)
        leg(b, source, side, s)
    sword(b, source)
    shield(b, source)
    return b.mesh()
