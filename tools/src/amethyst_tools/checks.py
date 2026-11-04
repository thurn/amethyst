"""The checks. `am check` is the fast one `enact push` runs (budget 60 s); `am ci` is slow CI
on the staging tip (budget 300 s, and a run over it fails). Both run lanes in parallel, each a
sequence of timed steps, and print failed lanes last.

The fast check runs the lanes whose files changed since the branch left origin/staging, and
never Unity. Slow CI runs every lane plus `unity`: each project's EditMode tests (memoized by
their inputs, so only changed projects run), then the game's player and a headless run of it.
Test steps record per-test outcomes for `am metrics`.
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path

from . import metrics, paths

FAST_BUDGET = 60
SLOW_BUDGET = 300
COMMIT: re.Pattern[str] = re.compile(
    r"^(feat|fix|perf|refactor|test|docs|build|ci|chore|style|revert)"
    r"(\([a-z0-9-]+\))?!?: \S"
)
COMMIT_MAX = 60
TEXT_SUFFIXES = (
    ".cs",
    ".py",
    ".rs",
    ".md",
    ".toml",
    ".json",
    ".flags",
    ".asmdef",
    ".yaml",
)

AM: list[str] = [sys.executable, "-m", "amethyst_tools.cli"]
UV_TOOLS = ["uv", "run", "--quiet", "--project", "tools"]


@dataclass
class Lane:
    name: str
    steps: list[tuple[str, list[str], Path]] = field(default_factory=list)

    def step(self, name: str, cmd: list[str], cwd: Path = paths.REPO) -> Lane:
        self.steps.append((name, cmd, cwd))
        return self


@dataclass
class Outcome:
    lane: str
    ok: bool
    seconds: float
    log: str
    timings: list[str]


def run_lane(lane: Lane, script: str, env: dict[str, str]) -> Outcome:
    started = time.monotonic()
    log: list[str] = []
    timings: list[str] = []
    for name, cmd, cwd in lane.steps:
        t = time.monotonic()
        run = subprocess.run(
            cmd,
            cwd=cwd,
            env=env,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            errors="replace",
            check=False,
        )
        seconds = time.monotonic() - t
        if run.stdout.strip():
            log.append(run.stdout.rstrip())
        if run.returncode != 0:
            log.append(
                f"{script} {lane.name} {name}: FAILED (exit {run.returncode}) after {seconds:.0f} s"
            )
            timings.append(f"{name} FAILED")
            return Outcome(
                lane.name, False, time.monotonic() - started, "\n".join(log), timings
            )
        log.append(f"{script} {lane.name} {name}: {seconds:.1f} s")
        timings.append(f"{name} {seconds:.0f} s")
    return Outcome(lane.name, True, time.monotonic() - started, "\n".join(log), timings)


def run_lanes(lanes: list[Lane], script: str, budget: int, strict: bool) -> int:
    started = time.monotonic()
    env = {**os.environ, "AM_LANE": "fast" if script == "check" else "slow"}
    with ThreadPoolExecutor(max_workers=max(1, len(lanes))) as pool:
        outcomes = list(pool.map(lambda lane: run_lane(lane, script, env), lanes))
    for o in sorted(outcomes, key=lambda o: not o.ok):
        print(f"=== {script} {o.lane}: {'passed' if o.ok else 'FAILED'}\n{o.log}")
    for o in outcomes:
        print(f"{script}: {o.lane} {o.seconds:.0f} s ({', '.join(o.timings)})")
    elapsed = time.monotonic() - started
    failed = [o.lane for o in outcomes if not o.ok]
    over = elapsed > budget
    print(
        f"{script}: {'FAILED: ' + ' '.join(failed) if failed else 'passed'} in {elapsed:.0f} s (budget {budget} s)"
    )
    if over:
        print(
            f"{script}: over budget: speed up or delete the slowest step"
            + (" (this fails slow CI)" if strict else "")
        )
    return 1 if failed or (over and strict) else 0


# What changed.


def git(*args: str) -> str:
    run = subprocess.run(
        ["git", *args], cwd=paths.REPO, capture_output=True, text=True, check=False
    )
    return run.stdout.strip() if run.returncode == 0 else ""


def branch_base() -> str:
    for ref in ("origin/staging", "staging", "origin/master"):
        base = git("merge-base", ref, "HEAD")
        if base:
            return base
    return git("rev-parse", "--verify", "--quiet", "HEAD^")


def changed_paths(base: str) -> list[str] | None:
    """Paths changed since `base`, committed or not; None when there is no base."""
    if not base:
        return None
    diff = git("diff", "--name-only", "--no-renames", base).splitlines()
    untracked = git("ls-files", "--others", "--exclude-standard").splitlines()
    return sorted(set(diff + untracked))


def conflict_markers(files: list[str]) -> list[str]:
    hits = []
    for rel in files:
        f = paths.REPO / rel
        if not f.is_file() or not rel.endswith(TEXT_SUFFIXES):
            continue
        for i, line in enumerate(f.read_text(errors="replace").splitlines(), 1):
            if re.match(r"^(<{7}|>{7}|={7})( |$)", line):
                hits.append(f"{rel}:{i}: conflict marker")
    return hits


def commit_problems(base: str) -> list[str]:
    log = (
        git("log", "--no-merges", "--format=%h%x09%s", f"{base}..HEAD") if base else ""
    )
    problems = []
    for row in log.splitlines():
        sha, _, subject = row.partition("\t")
        if not COMMIT.match(subject):
            problems.append(
                f"{sha} {subject!r}: not a Conventional Commit (`fix: ...`)"
            )
        elif len(subject) > COMMIT_MAX:
            problems.append(f"{sha} {subject!r}: over {COMMIT_MAX} characters")
    return problems


def markdown_files() -> list[str]:
    """Every tracked Markdown file but the beads' and the agent skills' (whose YAML
    front matter mdformat would rewrite)."""
    return [
        p
        for p in git("ls-files", "*.md").splitlines()
        if not p.startswith((".beads/", ".agents/"))
    ]


def csharp_files() -> list[str]:
    files = git("ls-files", "-co", "--exclude-standard", "*.cs").splitlines()
    return [p for p in files if "/Assets/Vendor/" not in p]


# Lanes.


def repo_lane(base: str, full: bool) -> Lane:
    lane = Lane("repo")
    lane.step("just", ["just", "--fmt", "--check", "--unstable"])
    lane.step("flags", [*AM, "flags", "check"])
    lane.step(
        "markdown",
        [
            sys.executable,
            "-m",
            "mdformat",
            "--check",
            "--wrap",
            "80",
            "--number",
            *markdown_files(),
        ],
    )
    if full:
        lane.step("guard", [*AM, "vendor", "guard", "--tree", "HEAD"])
    else:
        lane.step("guard", [*AM, "vendor", "guard", "--index"])
        if base:
            lane.step(
                "guard-range", [*AM, "vendor", "guard", "--range", f"{base}..HEAD"]
            )
    return lane


def python_lane() -> Lane:
    tools = paths.REPO / "tools"
    return (
        Lane("python")
        .step("black", [sys.executable, "-m", "black", "--check", "--quiet", "tools"])
        .step("pyre", ["pyre", "--noninteractive", "check"], tools)
        .step("pytest", [*AM, "suite", "pytest"])
    )


def rust_lane() -> Lane:
    native = paths.REPO / "native"
    return (
        Lane("rust")
        .step("fmt", ["cargo", "fmt", "--all", "--check"], native)
        .step(
            "clippy",
            ["cargo", "clippy", "--locked", "--all-targets", "--", "-D", "warnings"],
            native,
        )
        .step("tests", [*AM, "suite", "cargo"])
    )


def csharp_lane(dotnet: bool) -> Lane:
    lane = Lane("csharp").step("tools", ["dotnet", "tool", "restore"])
    lane.step("csharpier", ["dotnet", "csharpier", "check", *csharp_files()])
    if dotnet:
        lane.step("dotnet", [*AM, "suite", "dotnet"])
    return lane


def unity_lane() -> Lane:
    return Lane("unity").step("projects", [*AM, "suite", "unity"])


def fast(args: argparse.Namespace) -> int:
    base = branch_base()
    changed: list[str] | None = changed_paths(base)
    every: bool = changed is None

    def touched(pattern: str) -> bool:
        return every or any(re.match(pattern, p) for p in changed or [])

    problems = conflict_markers(changed or []) + commit_problems(base)
    for p in problems:
        print(f"check: {p}")
    if problems:
        return 1
    print(
        f"check: {'every lane (no base)' if every else f'{len(changed or [])} paths changed since {base[:8]}'}"
    )
    lanes = [repo_lane(base, full=False)]
    if touched(r"^tools/"):
        lanes.append(python_lane())
    if touched(r"^native/"):
        lanes.append(rust_lane())
    if touched(r".*\.cs$|^dotnet/|^\.config/dotnet-tools\.json$"):
        lanes.append(
            csharp_lane(dotnet=touched(r"^packages/[^/]+/Runtime/Pure/|^dotnet/"))
        )
    return run_lanes(lanes, "check", FAST_BUDGET, strict=False)


def slow(args: argparse.Namespace) -> int:
    lanes = {
        "repo": repo_lane(git("merge-base", "origin/master", "HEAD"), full=True),
        "python": python_lane(),
        "rust": rust_lane(),
        "csharp": csharp_lane(dotnet=True),
        "unity": unity_lane(),
    }
    chosen = args.lanes or list(lanes)
    unknown = set(chosen) - set(lanes)
    if unknown:
        raise SystemExit(f"unknown lanes {sorted(unknown)}; lanes: {' '.join(lanes)}")
    budget = int(os.environ.get("CI_BUDGET_S", SLOW_BUDGET))
    return run_lanes([lanes[n] for n in chosen], "ci", budget, strict=True)


# Test suites, run as lane steps: each records its results for `am metrics`.


def suite(args: argparse.Namespace) -> int:
    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp)
        if args.name == "pytest":
            xml = out / "pytest.xml"
            code = subprocess.run(
                [sys.executable, "-m", "pytest", "-q", f"--junitxml={xml}"],
                cwd=paths.REPO / "tools",
                check=False,
            ).returncode
            if xml.exists():
                metrics.record("pytest", metrics.parse_junit(xml))
            return 0 if code == 5 else code  # 5: no tests collected
        if args.name == "cargo":
            run = subprocess.run(
                ["cargo", "test", "--locked", "--workspace"],
                cwd=paths.REPO / "native",
                capture_output=True,
                text=True,
                check=False,
            )
            print(run.stdout[-4000:] + run.stderr[-4000:])
            metrics.record("cargo", metrics.parse_cargo(run.stdout))
            return run.returncode
        if args.name == "dotnet":
            code = subprocess.run(
                [
                    "dotnet",
                    "test",
                    "dotnet/Amethyst.slnx",
                    "--nologo",
                    "-v",
                    "q",
                    "--logger",
                    "trx",
                    "--results-directory",
                    str(out),
                ],
                cwd=paths.REPO,
                check=False,
            ).returncode
            for trx in out.rglob("*.trx"):
                metrics.record("dotnet", metrics.parse_trx(trx))
            return code
        return unity_suite()


def unity_suite() -> int:
    """Each project's EditMode tests, in parallel up to the Unity slots; then the game's player,
    built and run headless for 5 s (a project can't host two editors, so builds follow tests).
    """

    def project_steps(name: str) -> int:
        code = subprocess.run([*AM, "test", name], check=False).returncode
        if code == 0 and name == "game":
            code = subprocess.run([*AM, "build", "game"], check=False).returncode
            if code == 0:
                code = subprocess.run(
                    [*AM, "play", "game", "--headless", "--no-build", "--seconds", "5"],
                    check=False,
                ).returncode
        return code

    names = [p.name for p in paths.projects()]
    with ThreadPoolExecutor(max_workers=len(names) or 1) as pool:
        codes = dict(zip(names, pool.map(project_steps, names)))
    failed = [n for n, c in codes.items() if c != 0]
    print(
        f"unity: {len(names) - len(failed)} of {len(names)} projects passed"
        + (f"; failed: {' '.join(failed)}" if failed else "")
    )
    return 1 if failed else 0


def register(sub: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    sub.add_parser("check", help="the fast check (enact push)").set_defaults(func=fast)
    p = sub.add_parser("ci", help="slow CI, or some of its lanes")
    p.add_argument("lanes", nargs="*")
    p.set_defaults(func=slow)
    p = sub.add_parser("suite", help="run a test suite and record its results")
    p.add_argument("name", choices=["pytest", "cargo", "dotnet", "unity"])
    p.set_defaults(func=suite)
