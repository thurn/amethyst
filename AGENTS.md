# Working on Project Amethyst

Amethyst is a 3D action game prototype: Unity 6000.6.7f1 (URP, C#) with a Rust
native plugin. We test many ideas quickly, so everything here serves iteration
speed: small Unity testbeds, behavior behind flags, few tests, little prose.
Delete freely. The repository is public.

## Layout

- `packages/`: the game's code, as local Unity packages every project references
  by path. `com.amethyst.core`: flags, launch arguments, run isolation, editor
  tasks. `com.amethyst.action`: the 3D action baseline (arena, hero, camera).
  `com.amethyst.native`: the Rust plugin's bindings. Engine-free code goes in a
  package's `Runtime/Pure/` (an asmdef with `noEngineReferences`), which
  `dotnet/` also compiles and tests without Unity.
- `testbeds/<name>/`: small Unity projects, one per idea. `sandbox` is the
  template: `just testbed-new <name>` forks it (or `--from` another) with its
  warm Library. A testbed's throwaway code lives in its `Assets/<Name>/`; code
  that wins moves into a package. Delete testbeds once they have served.
- `game/`: the Unity project that pulls the packages together.
- `native/`: the Rust workspace behind `libamethyst_native.dylib`.
- `docs/`: reference material the human provides, such as
  [the combat feel reference](docs/arpg-combat-feel.md). Read what bears on your
  work; agents don't add files here.
- `tools/`: `am`, the Python CLI behind the justfile. `just` lists the commands.
- Outside the tree: `~/Library/Caches/amethyst` (`$AMETHYST_CACHE`: warm
  Libraries, run folders, test metrics), the vendor packs in `~/synty` and
  `~/unity_assets`, and modified vendor files in `~/amethyst-vendor`. Never
  crawl `~` or `/` with `find`.

## Enact

`enact` below means `python3 ~/enact/scripts/enact.py`.

- **Every change goes through a worktree and CI**, whether a human, an ad hoc
  agent session or an Enact run makes it:
  1. From the main checkout, `enact worktree create <short-name>`. Work only in
     the printed path; never edit or commit in the main checkout.
  2. Commit there (see Conventions).
  3. Run `enact push` in the worktree. It rebases onto `origin/staging` (merging
     `master` if someone bypassed staging), runs the fast check, pushes to
     `staging`, starts slow CI in the background and removes the worktree. On
     exit code 2, fix the reported conflict or failing check in the same
     worktree and run it again.
  4. If its output has `watch_ci`, no Enact run is around to repair a slow-CI
     failure: follow it (`enact ci --wait <sha>` until it passes; on failure,
     claim and fix the Steward bead) before finishing.
- **Session titles show status.** Any agent session making a change here titles
  itself `⚒️ <short summary>` when it starts (Claude Desktop:
  `set_session_title` on `self`; Codex: `set_thread_title`), 🔮 while it waits on
  slow CI, 🚒 while it fixes a Steward bead, and `✅ <same summary>` as its last
  step when done, or `⛔` if it stops without finishing.
- Never commit to, or push directly to, `master` or `staging`. `master` advances
  only when slow CI passes on `staging`; a slow-CI failure files a P0 Steward
  bead. `enact ci --status` shows the runner and its log. Keep the main checkout
  clean: Enact fast-forwards its `staging` and `master`.
- The checks are `.enact.toml`'s: `just check` (fast) and `just ci` (slow).
- Beads live in this repo (`bd`, prefix `am`); publish bead changes with
  `enact sync`. Plans, designs and decisions go in beads, not markdown: a bead's
  history is the record.
- File beads freely, in parallel. Merge conflicts are cheap: `enact push`
  rebases, you fix and push again. A filed bead may start within minutes, so put
  what it builds on in `staging` first or hold it back with a dependency.
- `/enact <bead|epic|task>` runs work inline in one session;
  `/enact-plan <bead|plan|task>` runs it unattended across sessions. Most work
  uses neither.
- **Runs are light**: treat every `/enact` and `/enact-plan` invocation as if it
  carried the keyword **light** (Executor, Warden and Steward only; at most one
  final Warden), unless it says **full**.

## Testing your work

Cheapest first. Agents never open a window, the editor or sound; every `am`
command detects an agent session and stays headless and muted.

1. Logic: `just test-dotnet` runs the engine-free C# in seconds.
2. Unity: `just test <project>` runs its EditMode tests in batch mode with
   `-nographics`: no window, no Dock icon, no GPU, no shader compilation. A pass
   is memoized by a hash of everything the project reads, in every worktree;
   `--force` reruns.
3. Behavior: `just play <project> --seconds 5` builds the player if needed, runs
   it headless and reports exceptions and bad flags; the exit code fails on any
   logged error.
4. Looks: `just shots <testbed>` renders every variant headless at fixed time
   and prints the PNG paths. Look at them.

Every run gets its own folder (`just logs <project>`) for its logs, results,
captures and saves, so parallel runs never collide. Save through `Run.DataDir`;
never use `PlayerPrefs` or `Application.persistentDataPath`, which every player
of a project shares.

## Keeping Unity fast

- Logic belongs in `Runtime/Pure/`, tested under dotnet. Unity tests cover only
  what needs the engine.
- Work in the smallest testbed that shows the idea, never in `game/` to try
  something. Each project compiles only the packages it lists.
- A worktree's first Unity step clones a warm Library from the cache in seconds;
  removing the worktree saves it back. A cold import takes about 30 s.
- Mono players. IL2CPP (`--backend il2cpp`) only for performance work.
- `am` runs a few Unity processes per machine at most (`AMETHYST_UNITY_SLOTS`)
  and waits for a slot, niced. Don't start Unity outside `am`.
- Shaders dominate build time: few shaders, few keywords, URP's own or our own
  small ones rather than vendor shader packs. Builds log their variant count.
- `am` lists tracked files a Unity step rewrote (ProjectSettings, lock files):
  commit them deliberately or revert them.
- Install the Unity Accelerator (port 10080) to share imports and compiled
  shaders between worktrees; `am` uses it whenever it answers.

## Flags and concepts

[FLAGS.md](FLAGS.md) is the guide and the generated catalog.

- Put every idea behind a flag, at the level where behavior forks: an enum that
  selects a whole implementation beats scattered booleans.
- Visual and feel work happens in a testbed as two or three variants of the same
  scene (`Assets/StreamingAssets/variants/*.flags`), never a single guess. Check
  them with `just shots`; when a human should judge, end with the command, e.g.
  `just preview sword` (keys 1–9 switch variants).
- When a question is settled, make the winner the default and delete the flag
  and the losing code. `just flags-unused` lists flags nothing reads.

## Tests

- Few tests, black-box, of behavior that matters. No tests of simple code, no
  change detectors, no golden images of looks: looks are judged by eye.
- Delete tests aggressively. A test that fails without catching a real bug gets
  deleted, not fixed. `just test-report` ranks tests by cost and flags deletion
  candidates from every run's recorded outcomes (flaky, failing under load,
  rewritten to pass, never catching anything).
- Budgets: fast check 60 s, slow CI 300 s (a run over it fails). Speed up, move
  or delete the slowest step rather than raise a budget.

## Conventions

- Commits: short Conventional Commits, e.g. `feat: dash cancels into slash`;
  subjects of 60 characters at most, a body only when the why isn't obvious. The
  fast check enforces it.
- `just fmt` formats everything: black, rustfmt, CSharpier, mdformat (80
  columns), the justfile, FLAGS.md. Python is typed (pyre, strict); Rust passes
  clippy with `-D warnings`. C# is Unity's C# 9.
- Scripts are Python in `tools/`, standard library unless a dependency earns its
  place.
- Markdown: agents add none. README.md, AGENTS.md, FLAGS.md and the human's
  `docs/` are all there is; plans, designs and notes go in beads.
- Never commit logs, captures, screenshots or other evidence of a run; they stay
  in the cache. No "operator pending" lists or checklists for the human either:
  say what they must see in one line of your final message, or nothing.
- Keep things lightweight: the simplest version that answers the question, no
  speculative infrastructure.

## Vendor assets

The game uses commercial packs (Synty, Asset Store) that each machine supplies
and the public repository must never contain, nor anything derived from them.

- A project lists the vendor files it uses in its `vendor.json` (source root,
  path, hash, GUID); every Unity step syncs them into the ignored
  `Assets/Vendor/`, following what they reference.
  `just vendor add <project> <source> <path>` adds files (a glob works);
  `just vendor --help` lists the rest.
- To modify a vendor file, edit it in place under `Assets/Vendor/`, then run
  `just vendor save <project>`: it commits the changed files (binaries in LFS)
  to `~/amethyst-vendor`, a local-only repository, and repoints the manifest at
  them. Sync refuses to overwrite unsaved edits. Never push that repository
  anywhere public.
- The vendor guard (fast check and pre-push hook) refuses any vendor file, by
  hash, and any `Assets/Vendor/` path in a commit.
