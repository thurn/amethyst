---
name: wireframe-mesh
description: Design a low-poly wireframe mesh for one Amethyst figure (a crowd type or the hero) from its vendor model, so it reads in the neon edge look as a retro 3D neon wireframe (simple faceted shapes, glowing connected edges over a dark body) at game scale, in a crowd and up close, still driven by the vendor rig and clips. Use when a vendor mesh scribbles into a fill in the edge look, or a figure's look needs reworking at the mesh level.
---

# Design a figure's wireframe mesh

One figure: its vendor model goes in, and a low-poly model comes out that
reads as a **retro 3D neon wireframe**, the synthwave look of glowing
low-poly mountains, palm trees and cities on black. This is an art task.
Your eye decides it, not a number.

Read first:

- docs/game-design.md, Look (and its Crowds and meshes): the Playlist
  Survivors neon edge trailer is the reference, and changes to it are
  judged by eye.
- `~/playlist_survivors/docs/champions/style.md`, The Edge Look, and the
  synthwave references in `~/playlist_survivors/captures/style-references/`
  (outside the tree). Look at them; they are the target.
- AGENTS.md: worktrees, checks, flags and the vendor rules. The repository
  is public: your recipe is committed; every mesh, export and bake it
  produces stays in ignored folders (the project's `Library/` and
  `Assets/Baked/`).

## How the edge look draws a mesh

The look package's `MonsterEdge` (crowd) and `HeroEdge` (the hero) shaders
draw:

- **a line on every crease sharper than 24 degrees**, and on every open
  edge, in the figure's colour, a little brighter toward the silhouette;
- a dark glass core, each facet a shade apart;
- a fresnel rim around the figure.

**What it should look like: a retro 3D neon wireframe.**

- A 3D model with a deliberately simple, low-poly topology, usually a few
  simple faceted volumes joined together.
- Its edges are glowing lines that connect at vertices into closed facets,
  so the silhouette is closed.
- The dark body hides the lines behind it, and every facet stays dark
  inside.
- Not lit facet patches, glassy fills, broken or blotchy scratches, or a
  flat outline drawing.

If the vendor mesh shows patches or broken lines in our build but not in
the trailer, find out whether the renderer is off before reworking the
mesh: compare with the trailer's own frames.

So **the mesh's topology is the drawing.** With the 24-degree rule, a crease
you want is a sharp edge, and a surface you want quiet is smooth
(neighbours under 24 degrees apart) or flat. Every sharp edge on the model
is a stroke on screen, whether you meant it or not. A designed low-poly
mesh can instead draw every one of its edges, a true wireframe as the
references are, if the recipe flags its edges (see Build it). Then each
edge of the topology is a line you chose, so lay it out with care:

- clean quads and triangles of even size;
- edges that follow the form, like bands around a limb or ridges along a
  back.

## What went wrong before, so you don't repeat it

Playlist Survivors' deleted "edge cuts" picked edges one at a time to hit a
black-share number and a segment cap. They lost the outline, the head and
every closed shape, and passed every threshold while looking like
scattered scratches.

- Don't optimise a metric.
- Don't choose strokes in isolation from the shapes they belong to.
- Don't delete what names the figure to make it emptier.

## 1. Look before you touch anything

- Render the vendor mesh: `just shots look` renders every variant of the
  look testbed; `review-vendor` poses the knight and the skeleton up close
  from their vendor meshes, `vendor` the whole crowd. Look at the stills
  at native size first, then zoomed. A figure without a review row yet:
  add it to `ReviewDemo.Types` (testbeds/look/Assets/Look/).
- Watch the trailer itself,
  `~/playlist_survivors/captures/trailer-action-edge.mp4`. Pull frames with
  `ffmpeg -ss <s> -i <mp4> -frames:v 1 out.jpg` into your scratch folder.
- Look at the figures already done (`tools/recipes/`), so this one joins
  them as a roster.
- Write down in plain words, on your bead:
  - whether it reads as a 3D neon wireframe (connected lines, closed
    facets, a closed silhouette, dark inside) or as lit patches and broken
    scratches, and where;
  - what reads and what doesn't, at game scale, in a crowd and up close;
  - what names the figure: its silhouette, head, weapon, shield, posture;
  - what turns to noise, or merges into a fill.

The vendor's drawing may already be right. Keeping the vendor mesh (no
recipe) is an accepted outcome; say why on the bead.

## 2. Design the figure

Decide the figure before the geometry:

- **The big masses**: head, torso, limbs, weapon, shield. How many, and how
  they join.
- **The few features that name it**, which must survive at game scale.
- **What goes**: detail that only adds scribble at game scale.

Guidelines, not rules. A frame that looks better beats any of them:

- **Simple faceted shapes, joined together, are the usual way**: a clean
  low-poly volume per mass (a faceted dome of a head, a prism of a
  forearm), meeting at clear joints.
- **Fewer, larger facets read at game scale**; many small facets with
  sharp angles between them merge into a fill.
- **Keep the silhouette's landmarks and negative spaces**: the gap between
  arm and body, the shield's circle, a plume's point.
- **The head almost always matters.** It's the first thing a viewer looks
  for.
- **Weapons and shields belong to the figure.** Draw them as clean shapes,
  but not so bright or clean that they outdraw the body.
- **Tell siblings apart by shape**: types that share a colour family must
  differ in silhouette.
- **The hero outranks every monster** (style.md's value ladder): white, a
  rung brighter, and the cleanest drawing on screen.

## 3. Build it

Write a recipe at `tools/recipes/<recipe>.py` and name it in the figure's
roster entry (`packages/com.amethyst.look/Editor/roster.json`,
`"recipe": "<recipe>"`; the knight's serves both the `hero` figure and the
`knight` crowd type). It is a module whose docstring says what it changes
and why, with

```python
from kit import Builder, Mesh, Source, creases, keep_parts, ring, unit

def build(source: Source) -> Mesh:
    mesh = source.mesh()  # the vendor mesh, unchanged: start here
    ...
    return mesh
```

- `tools/recipes/kit.py` is the kit. `Source` is the vendor model as the
  crowd baker combines it: every active renderer in root space, metres at
  the entry's height, its rest pose. Numpy arrays `positions`, `normals`,
  `triangles`, `bones` and `weights` (four influences per vertex, into
  `bone_names`; `bone_paths` too), and `parts` (each renderer's vertex
  range: `source.part("Sword_LP")`); `source.bone("Hand_R")` finds a bone.
  A held weapon rides its hand's bone.
- Return a `Mesh` skinned to the same bones (`positions`, `triangles`,
  `bones`, `weights`; `normals` optional). It shares the vendor bone
  texture (crowd) or skeleton (hero), so the vendor rig and clips drive it.
- Which edges draw: by default every crease sharper than 24 degrees.
  `mesh.edge_angle = 1` draws every edge but a flat quad's diagonal (a true
  wireframe of your topology); `mesh.edges`, shape (triangles, 3), flags
  each triangle's edges yourself (1 for the edge opposite that corner;
  `creases()` computes the 24-degree rule's). A recipe's mesh is never
  decimated: its topology is the drawing, so keep it low-poly.
- `Builder` adds convex shapes (`add`, `prism` lofted through rings,
  `box`), each vertex rigid to one bone or weighted by hand;
  `keep_parts(source, drop)` keeps the vendor's other parts as they are;
  `builder.mesh(kept)` joins them.
- Recipes run under Blender's bundled Python (numpy, no install), so
  Blender must be at /Applications/Blender.app. `blender(script, *args)`
  runs a script in Blender headless for its modelling tools;
  `source.save(path)` and `Mesh.load(path)` trade `.npz` files with it.

Every player build of a project with the vendor files bakes the recipes
(the crowd baker runs before each build and reruns an entry only when its
roster entry, the baker or the recipe changes): `just shots look` rebuilds
and renders. The recipe's export and output are in
`testbeds/look/Library/AmethystRecipes/<id>/` (`source/`, `mesh/`) for a
look. `look.mesh = vendor` draws every figure from its vendor mesh again.

Techniques that tend to work (from Playlist Survivors' roster):

- **Planar dissolve and limited decimation, part by part**, to quiet busy
  surfaces into fewer, larger facets.
- **Remodel a part from simple primitives** fitted to the source (a helmet
  as a faceted dome, a forearm as a tapered prism, a hand as a mitten).
  Skin it rigidly to one bone, or by transferring weights from the source
  part it replaces.
- **Sharpen or soften creases on purpose**: move vertices or bevel so the
  angle crosses 24 degrees where you want a line, and stays under it where
  you don't. Keep each crease well clear of 24: one near it draws on some
  facets and not others, a dashed line.
- **Replace fiddly accessories** with one clean shape, or drop them if they
  don't name the figure.
- **Drop what is hidden**: parts inside the armour in every view (toes in
  boots, ribs under a cuirass) cost triangles and draw nothing.
- **Keep big facets from turning far from the camera**: a large facet seen
  at a grazing angle catches the fresnel rim as a lit patch (a domed
  shield). Thin plates, and facets facing up toward the game camera, stay
  dark. Where a big facet must turn away, give its vertices normals that
  lean up (`Mesh.normals`), about 0.4 straight up at most: leant far, a
  face turned over draws solid.
- **Mind the camera's 55 degrees on a round body**: seen from above, rings
  around a vertical axis nest into a target and an apex's spokes into an
  umbrella.
- **One prism per limb**: separate segments, each capped at the knee and
  elbow, read as a kit of boxes with gaps; one loft through all the joints
  closes the outline.
- **Lean a body's normals up its limbs**: a round trunk's and a hanging
  arm's long facets light in the rim as gradient bands. Leaning each
  facet's normal toward its shape's root keeps them dark.
- **Turn a hanging blade's flat to the camera**: edge-on from the hand, a
  blade draws as a solid bright bar; turned a quarter on its grip, as an
  outlined dark shape. Keep its flats flat.
- **Look at the head from behind too**: walking away, the camera shows
  whatever stands in front of the skull above it, which can read as horns
  or a crown.
- **Few rings round a big body**: each ring round a round trunk reads as a
  hoop; let long lines shape it, with one ring at most where a form turns.
- **Close a plate's outline with bevels**: a thin plate's rim is edge-on
  whenever the plate faces the camera, so its outline draws thin and
  fades. Two narrow bevels meeting at a ridge round the outline give it a
  lit face on both sides.
- **Check an enlarged feature in the clips**: a small bone's motion made
  for the vendor's size swings a feature you lengthen much further.

`tools/recipes/skeleton.py` is the worked example (Playlist Survivors'):
vendor plate kept, scribbling parts dropped and rebuilt from primitives
rigid to one bone each, the 24-degree creases flagged by the recipe itself.
`tools/recipes/knight.py` builds a whole armoured humanoid from shapes on
the vendor's joints.

Keep the vendor rig, bind poses and clips; transfer weights by region and
side, never across limbs; keep the four influences. Triangle count is a
diagnostic: stay near or under the vendor's (the crowd draws hundreds).

## 4. Judge it

- `just shots look`: `review` and `review-vendor` pose the figures up
  close (still, mid-stride, mid-swing, recoiling; the hero running at the
  end of the knights' row), `default` and `vendor` show the crowd at the
  trailer's framing. Look at game scale first, then zoom. Watch every pose:
  a shape that reads at idle can collapse mid-stride.
- **Get a blind critique** from a fresh agent that knows nothing of your
  intent. Give it only the images, no docs, no recipe, no numbers:

  > You're an art director for a top-down neon game drawn as glowing line
  > art on black. Here are two drawings of the same figures, A and B, up
  > close in four poses and in a crowd: <paths>. The style aimed for is
  > retro 3D neon wireframe, as in synthwave art. Does each read as a
  > low-poly 3D model of simple shapes whose glowing edges connect into
  > closed facets and a closed silhouette, dark inside? Or does it break
  > into patches and scratches, and where? Which reads better at the
  > in-game size and in the crowd? What's wrong with each, most important
  > first, concretely enough to act on? Under 300 words.

  Label the vendor's stills and yours A and B in random order.
- Act on what it finds that you agree with, and render again. Repeat until
  the new mesh is clearly better than the vendor's drawing, or until you're
  sure it isn't.
- Never trade away a feature that names the figure for a number.

## 5. Deliver

- The committed recipe, its docstring saying what changed and why, the
  vendor source it reads (its vendor.json path and hash) and the Blender
  version.
- A comment on your bead: what you saw in the vendor's drawing, what you
  changed and why, the blind critique and what you did about it, and what
  still looks weak. Captures stay in the cache (AGENTS.md); never commit
  them.
- The roster names the recipe only if its mesh reads better than the
  vendor's; otherwise say on the bead why the vendor mesh stays.
