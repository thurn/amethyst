"""Unity runs that stay out of the way.

- Every launch gets its own run folder, $AMETHYST_CACHE/runs/<checkout>/<project>/<kind>/
  <time>-<pid>/, for its logs, results, captures and the player's data, so parallel runs never
  share a log or save file. `am logs PROJECT` shows the latest.
- Editor runs are batch mode (no window, no Dock icon), `-nographics` unless they render,
  niced, and limited to a few per machine (proc.unity_slot).
- Before any Unity step a project gets a warm Library (worktree.restore), the Rust plugin
  (native.ensure) and its vendor files (vendor.ensure_synced).
- Tests and player builds are memoized by a hash of everything they read, so an unchanged
  project costs nothing in any worktree: `--force` reruns.
- Players run headless in agent sessions (batch mode, muted, `--seconds 10` unless told
  otherwise) and in a window with sound for humans.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import plistlib
import re
import shutil
import socket
import subprocess
import time
from pathlib import Path

from . import metrics, native, paths, proc, vendor, worktree

ACCELERATOR = "127.0.0.1:10080"
# The player's own informational lines (Run.cs, RunDriver.cs); any other `amethyst:` line is
# an error it logged.
PLAYER_INFO: re.Pattern[str] = re.compile(
    r"amethyst: (flags|captured|music playing|quitting with 0)"
)
KEEP_RUNS = 20


def accelerator() -> str | None:
    """The Unity Accelerator's endpoint ($UNITY_ACCELERATOR or the default) if one answers."""
    endpoint = os.environ.get("UNITY_ACCELERATOR", ACCELERATOR)
    host, _, port = endpoint.rpartition(":")
    try:
        with socket.create_connection((host, int(port)), timeout=0.3):
            return endpoint
    except (OSError, ValueError):
        return None


def run_dir(project: paths.Project, kind: str) -> Path:
    base = paths.cache_root() / "runs" / paths.checkout_id() / project.name / kind
    stamp = time.strftime("%Y%m%d-%H%M%S", time.gmtime())
    path = base / f"{stamp}-{os.getpid()}"
    path.mkdir(parents=True)
    latest = base / "latest"
    latest.unlink(missing_ok=True)
    latest.symlink_to(path.name)
    old = sorted(p for p in base.iterdir() if p.is_dir() and not p.is_symlink())
    for stale in old[:-KEEP_RUNS]:
        shutil.rmtree(stale, ignore_errors=True)
    return path


def main_checkout() -> Path | None:
    common = subprocess.run(
        ["git", "rev-parse", "--path-format=absolute", "--git-common-dir"],
        cwd=paths.REPO,
        capture_output=True,
        text=True,
        check=False,
    ).stdout.strip()
    main = Path(common).parent if common else None
    return main if main and main != paths.REPO else None


def prepare(project: paths.Project) -> None:
    done = worktree.restore(project.name, project.path, main_checkout())
    if done:
        print(f"{project.name}: Library {done}")
    if project.uses_package("com.amethyst.native"):
        native.ensure()
    vendor.ensure_synced(project)


def git_status() -> set[str]:
    out = subprocess.run(
        ["git", "status", "--porcelain"],
        cwd=paths.REPO,
        capture_output=True,
        text=True,
        check=False,
    ).stdout
    return set(out.splitlines())


def run_editor(
    project: paths.Project, folder: Path, extra: list[str], graphics: bool = False
) -> int:
    """One batch-mode editor launch; its log is folder/editor.log."""
    cmd = [str(paths.unity_editor(project.unity_version)), "-batchmode"]
    if not graphics:
        cmd.append("-nographics")
    cmd += ["-projectPath", str(project.path), *extra, "-logFile", "-"]
    endpoint = accelerator()
    if endpoint:
        cmd += ["-EnableCacheServer", "-cacheServerEndpoint", endpoint]
        cmd += [
            "-cacheServerEnableDownload",
            "true",
            "-cacheServerEnableUpload",
            "true",
        ]
    before = git_status()
    with proc.unity_slot(f"{project.name} {folder.parent.name}"):
        started = time.monotonic()
        code = proc.run_logged(proc.niced(cmd), folder / "editor.log")
    print(
        f"{project.name}: Unity exited {code} after {time.monotonic() - started:.0f} s "
        f"(log {folder / 'editor.log'})"
    )
    changed = sorted(git_status() - before)
    if changed:
        print(f"{project.name}: Unity changed tracked files; commit or revert them:")
        print("\n".join(f"  {line}" for line in changed))
    return code


def compile_errors(log: Path) -> str:
    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    lines = [line for line in text.splitlines() if "error CS" in line]
    return "\n".join(dict.fromkeys(lines))


def inputs_key(project: paths.Project, purpose: str) -> str:
    """A hash of everything a project's Unity step reads: its files, the packages it uses,
    the native plugin, the mesh recipes, its vendor manifest, the Unity version and this
    launcher."""
    roots = [project.path]
    manifest = json.loads(project.manifest.read_text())
    for spec in manifest.get("dependencies", {}).values():
        if isinstance(spec, str) and spec.startswith("file:"):
            roots.append((project.manifest.parent / spec[5:]).resolve())
    listed = subprocess.run(
        ["git", "ls-files", "-co", "--exclude-standard", "--", *map(str, roots)],
        cwd=paths.REPO,
        capture_output=True,
        text=True,
        check=False,
    ).stdout.splitlines()
    h = hashlib.sha256(f"{purpose}\n{project.unity_version}\n".encode())
    extras = [Path(__file__)]
    if project.uses_package("com.amethyst.native"):
        extras.append(native.PLUGIN_DIR / native.DYLIB)
    if project.uses_package("com.amethyst.look"):
        # Its crowd baker runs the wireframe mesh recipes before every build.
        extras.extend(sorted((paths.REPO / "tools" / "recipes").glob("*.py")))
    for f in sorted(paths.REPO / rel for rel in listed) + extras:
        if f.is_file():
            h.update(f"{f.relative_to(paths.REPO)}\n".encode())
            h.update(hashlib.sha256(f.read_bytes()).digest())
    return h.hexdigest()[:20]


# Commands.


def test(args: argparse.Namespace) -> int:
    project = paths.project(args.project)
    prepare(project)
    key = inputs_key(project, "editmode")
    cached = paths.cache_root() / "results" / project.name / f"{key}.json"
    if cached.exists() and not args.force and not args.filter:
        prior = json.loads(cached.read_text())
        print(
            f"{project.name}: EditMode tests already passed for these inputs "
            f"({prior['passed']} tests; --force reruns)"
        )
        return 0
    folder = run_dir(project, "test")
    xml = folder / "results.xml"
    extra = ["-runTests", "-testPlatform", "EditMode", "-testResults", str(xml)]
    if args.filter:
        extra += ["-testFilter", args.filter]
    run_editor(project, folder, extra)
    if not xml.exists():
        errors = compile_errors(folder / "editor.log")
        print(
            f"{project.name}: no test results; {'compile errors:' if errors else 'see the log'}"
        )
        if errors:
            print(errors)
        return 1
    results = metrics.parse_nunit(xml)
    metrics.record(f"unity:{project.name}", results)
    failed = [r for r in results if r.outcome in ("fail", "error")]
    passed = sum(1 for r in results if r.outcome == "pass")
    print(f"{project.name}: {passed} passed, {len(failed)} failed (results {xml})")
    for r in failed:
        print(f"  FAILED {r.name}: {r.message}")
    if failed:
        return 1
    if not args.filter:
        cached.parent.mkdir(parents=True, exist_ok=True)
        cached.write_text(json.dumps({"passed": passed, "at": time.time()}))
    return 0


def app_path(project: paths.Project, backend: str) -> Path:
    return project.path / "Builds" / backend / f"{project.product}.app"


def build_player(
    project: paths.Project, backend: str, development: bool, force: bool
) -> Path | None:
    """Builds the project's player, unless the last build had the same inputs; the app, or
    None if the build failed."""
    prepare(project)
    app = app_path(project, backend)
    stamp = app.parent / ".inputs"
    key = inputs_key(project, f"player {backend} {development}")
    if not force and app.exists() and stamp.exists() and stamp.read_text() == key:
        print(f"{project.name}: player up to date ({app})")
        return app
    stamp.unlink(missing_ok=True)
    folder = run_dir(project, "build")
    extra = ["-executeMethod", "Amethyst.Editor.Tasks.BuildPlayer", "-quit"]
    extra += ["-amOut", str(app), "-amBackend", backend]
    if development:
        extra.append("-amDevelopment")
    code = run_editor(project, folder, extra)
    log = (folder / "editor.log").read_text(encoding="utf-8", errors="replace")
    line = re.search(r"Tasks\.BuildPlayer: .*", log)
    if line:
        print(f"{project.name}: {line.group(0)}")
    if code != 0 or not app.exists():
        print(compile_errors(folder / "editor.log") or f"{project.name}: build failed")
        return None
    make_headless(app)
    stamp.write_text(key)
    return app


def build(args: argparse.Namespace) -> int:
    project = paths.project(args.project)
    app = build_player(project, args.backend, args.development, args.force)
    return 0 if app else 1


def headless_app(app: Path) -> Path:
    return app.parent / "headless" / app.name


def make_headless(app: Path) -> None:
    """A clone of the player marked LSBackgroundOnly (and signed again), which headless runs
    use: the player itself registers as a Dock app even in batch mode."""
    copy = headless_app(app)
    shutil.rmtree(copy.parent, ignore_errors=True)
    copy.parent.mkdir(parents=True)
    subprocess.run(["cp", "-c", "-R", str(app), str(copy)], check=True)
    plist = copy / "Contents" / "Info.plist"
    subprocess.run(
        ["plutil", "-replace", "LSBackgroundOnly", "-bool", "YES", str(plist)],
        check=True,
    )
    subprocess.run(
        ["codesign", "--force", "--deep", "--sign", "-", str(copy)],
        check=True,
        capture_output=True,
    )


def player_exe(app: Path) -> Path:
    with open(app / "Contents" / "Info.plist", "rb") as f:
        info = plistlib.load(f)
    return app / "Contents" / "MacOS" / str(info["CFBundleExecutable"])


def launch(
    project: paths.Project,
    app: Path,
    folder: Path,
    player_args: list[str],
    window: bool,
) -> int:
    """Runs a built player with its logs and data in `folder`: headless in batch mode
    (waiting for it), or in a window."""
    args = ["-logFile", str(folder / "player.log"), "--data-dir", str(folder / "data")]
    if not window:
        args.insert(0, "-batchmode")
        if not {"--seconds", "--capture"} & {a.split("=")[0] for a in player_args}:
            args += ["--seconds", "10"]
    if paths.is_agent() and "--audio" not in player_args:
        args.append("--mute")
    args += player_args
    if window:
        flags = (
            ["-g"] if paths.is_agent() else []
        )  # never steal an agent's user's focus
        subprocess.run(["open", "-n", *flags, str(app), "--args", *args], check=True)
        print(f"{project.name}: playing {app}\n  log {folder / 'player.log'}")
        return 0
    started = time.monotonic()
    code = proc.run_logged(
        proc.niced([str(player_exe(headless_app(app))), *args]), folder / "stdout.log"
    )
    log = folder / "player.log"
    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    problems = [
        line
        for line in text.splitlines()
        if "Exception" in line
        or (line.startswith("amethyst: ") and not PLAYER_INFO.match(line))
    ]
    print(
        f"{project.name}: player exited {code} after {time.monotonic() - started:.0f} s "
        f"(log {log})"
    )
    for line in problems[:10]:
        print(f"  {line}")
    return code


def own_options(args: argparse.Namespace) -> None:
    """Takes this command's options back out of the player's arguments, where argparse puts
    them when they follow the project name."""
    rest: list[str] = []
    it = iter(args.player_args)
    for arg in it:
        key = arg.lstrip("-").replace("-", "_")
        if arg in ("--window", "--headless", "--no-build"):
            setattr(args, key, True)
        elif arg in ("--backend", "--at"):
            setattr(args, key, next(it))
        else:
            rest.append(arg)
    args.player_args = rest


def play(args: argparse.Namespace) -> int:
    own_options(args)
    project = paths.project(args.project)
    app = app_path(project, args.backend)
    if not args.no_build:
        built = build_player(project, args.backend, False, False)
        if not built:
            return 1
        app = built
    if not app.exists():
        raise SystemExit(
            f"{project.name}: no player at {app}; run `just build {project.name}`"
        )
    window = args.window or not (args.headless or paths.is_agent())
    return launch(project, app, run_dir(project, "play"), args.player_args, window)


def shots(args: argparse.Namespace) -> int:
    """Stills of every variant (or of the project), headless, for an agent to look at."""
    own_options(args)
    project = paths.project(args.project)
    app = build_player(project, args.backend, False, False)
    if not app:
        return 1
    folder = run_dir(project, "shots")
    player_args = ["--capture", str(folder), "--at", args.at, "--fixed-time"]
    if (project.assets / "StreamingAssets" / "variants").is_dir():
        player_args.append("--capture-variants")
    code = launch(project, app, folder, player_args + args.player_args, False)
    for png in sorted(folder.glob("*.png")):
        print(f"  {png}")
    return code


def editor(args: argparse.Namespace) -> int:
    project = paths.project(args.project)
    if paths.is_agent() and not args.force:
        raise SystemExit("agents don't open the editor; use test, build, play or shots")
    prepare(project)
    unity_app = paths.unity_editor(project.unity_version).parents[2]
    subprocess.run(
        ["open", "-na", str(unity_app), "--args", "-projectPath", str(project.path)],
        check=True,
    )
    return 0


def task(args: argparse.Namespace) -> int:
    project = paths.project(args.project)
    prepare(project)
    method = (
        args.method if "." in args.method else f"Amethyst.Editor.Tasks.{args.method}"
    )
    folder = run_dir(project, "task")
    extra = ["-executeMethod", method, *args.task_args, "-quit"]
    return run_editor(project, folder, extra, graphics=args.graphics)


def logs(args: argparse.Namespace) -> int:
    base = paths.cache_root() / "runs" / paths.checkout_id() / args.project
    if not base.is_dir():
        print(f"no runs of {args.project} from this checkout yet")
        return 0
    for kind in sorted(base.iterdir()):
        latest = kind / "latest"
        if latest.exists():
            files = ", ".join(sorted(p.name for p in latest.resolve().iterdir()))
            print(f"{kind.name:10} {latest.resolve()}  ({files})")
    return 0


def projects(args: argparse.Namespace) -> int:
    for p in paths.projects():
        print(f"{p.name:16} {p.path.relative_to(paths.REPO)}  ({p.product})")
    return 0


def register(sub: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    p = sub.add_parser("test", help="a project's EditMode tests, headless")
    p.add_argument("project")
    p.add_argument("--filter", help="Unity's -testFilter (skips the result cache)")
    p.add_argument(
        "--force", action="store_true", help="rerun even if these inputs passed"
    )
    p.set_defaults(func=test)

    p = sub.add_parser("build", help="a project's Mac player, unless it is up to date")
    p.add_argument("project")
    p.add_argument("--backend", choices=["mono", "il2cpp"], default="mono")
    p.add_argument("--development", action="store_true")
    p.add_argument("--force", action="store_true")
    p.set_defaults(func=build)

    for name, func, text in (
        (
            "play",
            play,
            "build if needed and play: windowed for humans, headless for agents",
        ),
        ("shots", shots, "headless stills of every variant, for agents"),
    ):
        p = sub.add_parser(name, help=text)
        p.add_argument("project")
        p.add_argument("--backend", choices=["mono", "il2cpp"], default="mono")
        if name == "play":
            p.add_argument(
                "--window", action="store_true", help="a window even for agents"
            )
            p.add_argument(
                "--headless", action="store_true", help="batch mode even for humans"
            )
            p.add_argument(
                "--no-build", action="store_true", help="play the last build"
            )
        else:
            p.add_argument(
                "--at", default="1.5", help="seconds after each load, e.g. 1,3"
            )
        p.add_argument("player_args", nargs=argparse.REMAINDER, help="player flags")
        p.set_defaults(func=func)

    p = sub.add_parser("editor", help="open a project in the Unity editor (humans)")
    p.add_argument("project")
    p.add_argument("--force", action="store_true")
    p.set_defaults(func=editor)

    p = sub.add_parser("unity", help="run an editor task method in batch mode")
    p.add_argument("project")
    p.add_argument("method", help="Amethyst.Editor.Tasks.NAME, or a full name")
    p.add_argument("--graphics", action="store_true", help="without -nographics")
    p.add_argument("task_args", nargs=argparse.REMAINDER)
    p.set_defaults(func=task)

    p = sub.add_parser("logs", help="the latest run folders of a project")
    p.add_argument("project")
    p.set_defaults(func=logs)

    p = sub.add_parser("projects", help="list the Unity projects")
    p.set_defaults(func=projects)
