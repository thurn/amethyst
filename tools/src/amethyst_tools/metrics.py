"""Test metrics: each test run's outcomes, appended to a per-machine log (never committed), and a
report of what every test has been worth.

Each record carries two content keys of the checkout: one over code, one over tests. When a test
fails and later passes in the same checkout, what changed in between says what the failure was:

- caught: the code changed and the tests didn't, so the failure found a real problem;
- detector: only the tests changed, so the test was rewritten to accept new behavior;
- flake: nothing changed (or the same keys both passed and failed);
- load: the failure message looks like a timeout or exhausted resources.

A test that has cost time and failed without ever catching anything is a deletion candidate.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import time
import xml.etree.ElementTree as ET
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from . import paths

LOAD: re.Pattern[str] = re.compile(
    r"timed? ?out|timeout|out of memory|outofmemory|killed|signal \d+|"
    r"too many open files|no space left|resource temporarily",
    re.IGNORECASE,
)


@dataclass(frozen=True)
class TestResult:
    name: str
    outcome: str  # pass, fail, skip or error (could not run: crash, timeout, resources)
    seconds: float
    message: str = ""


def metrics_file() -> Path:
    return paths.cache_root() / "test-metrics.jsonl"


def parse_nunit(path: Path) -> list[TestResult]:
    """Unity's NUnit 3 results."""
    outcomes = {"Passed": "pass", "Failed": "fail", "Skipped": "skip"}
    results = []
    for case in ET.parse(path).getroot().iter("test-case"):
        message = (case.findtext("failure/message") or "").strip()
        results.append(
            TestResult(
                case.get("fullname", "?"),
                outcomes.get(case.get("result", ""), "error"),
                float(case.get("duration", "0") or 0),
                message,
            )
        )
    return results


def parse_trx(path: Path) -> list[TestResult]:
    """dotnet test's TRX results."""
    outcomes = {"Passed": "pass", "Failed": "fail", "NotExecuted": "skip"}
    results = []
    for node in ET.parse(path).getroot().iter():
        if not node.tag.endswith("UnitTestResult"):
            continue
        h, m, s = (node.get("duration") or "0:0:0").split(":")
        message = next(
            (n.text or "" for n in node.iter() if n.tag.endswith("Message")), ""
        )
        results.append(
            TestResult(
                node.get("testName", "?"),
                outcomes.get(node.get("outcome", ""), "error"),
                int(h) * 3600 + int(m) * 60 + float(s),
                message.strip(),
            )
        )
    return results


def parse_junit(path: Path) -> list[TestResult]:
    """pytest's JUnit XML."""
    results = []
    for case in ET.parse(path).getroot().iter("testcase"):
        name = f"{case.get('classname', '')}.{case.get('name', '?')}".strip(".")
        outcome, message = "pass", ""
        for tag, kind in (("failure", "fail"), ("error", "error"), ("skipped", "skip")):
            child = case.find(tag)
            if child is not None:
                outcome, message = kind, child.get("message", "")
                break
        results.append(
            TestResult(name, outcome, float(case.get("time", "0") or 0), message)
        )
    return results


def parse_cargo(output: str) -> list[TestResult]:
    """`cargo test`'s `test NAME ... ok` lines (it reports no per-test times)."""
    outcomes = {"ok": "pass", "FAILED": "fail", "ignored": "skip"}
    return [
        TestResult(m.group(1), outcomes[m.group(2)], 0.0)
        for m in re.finditer(r"^test (\S+) \.\.\. (ok|FAILED|ignored)", output, re.M)
    ]


def is_test_path(path: str) -> bool:
    parts = path.split("/")
    return any(
        p in ("tests", "Tests") or p.endswith(".Tests") or p.startswith("test_")
        for p in parts
    )


def content_keys(root: Path = paths.REPO) -> tuple[str, str]:
    """(code key, test key): hashes of the checkout's files as they are on disk, tracked or
    not (ignored files aside)."""

    def git(*args: str) -> str:
        return subprocess.run(
            ["git", *args], cwd=root, capture_output=True, text=True, check=False
        ).stdout

    blobs: dict[str, str] = {}
    for line in git("ls-files", "-s").splitlines():
        meta, _, path = line.partition("\t")
        blobs[path] = meta.split()[1]
    changed = git("diff", "--name-only", "HEAD").split()
    for path in changed + git("ls-files", "-o", "--exclude-standard").splitlines():
        # Parallel lanes create and delete files meanwhile: a vanished one counts as absent.
        try:
            blobs[path] = hashlib.sha256((root / path).read_bytes()).hexdigest()
        except OSError:
            blobs[path] = "-"
    code, tests = hashlib.sha256(), hashlib.sha256()
    for path in sorted(blobs):
        (tests if is_test_path(path) else code).update(
            f"{path} {blobs[path]}\n".encode()
        )
    return code.hexdigest()[:12], tests.hexdigest()[:12]


def record(suite: str, results: list[TestResult]) -> None:
    """Appends a run's results. AM_LANE names the lane (fast, slow); else `manual`."""
    if not results:
        return
    code, tests = content_keys()
    head = subprocess.run(
        ["git", "rev-parse", "--short", "HEAD"],
        cwd=paths.REPO,
        capture_output=True,
        text=True,
        check=False,
    ).stdout.strip()
    common = {
        "t": round(time.time()),
        "suite": suite,
        "lane": os.environ.get("AM_LANE", "manual"),
        "checkout": paths.checkout_id(),
        "head": head,
        "code": code,
        "tests": tests,
    }
    with open(metrics_file(), "a", encoding="utf-8") as out:
        for r in results:
            outcome = r.outcome
            if outcome == "fail" and LOAD.search(r.message):
                outcome = "error"
            row = {
                **common,
                "test": r.name,
                "outcome": outcome,
                "s": round(r.seconds, 3),
            }
            if r.message and outcome != "pass":
                row["msg"] = r.message[:200]
            out.write(json.dumps(row, separators=(",", ":")) + "\n")


@dataclass
class Worth:
    suite: str
    test: str
    runs: int = 0
    fails: int = 0
    load: int = 0
    flakes: int = 0
    detectors: int = 0
    caught: int = 0
    seconds: float = 0.0
    keys: dict[tuple[str, str], set[str]] = field(default_factory=dict)

    @property
    def verdict(self) -> str:
        if self.caught:
            return "keep"
        if self.fails or self.load or self.flakes or self.detectors:
            return "delete?"
        if self.runs and self.seconds / self.runs > 10:
            return "slow"
        return ""


def summarize(rows: list[dict[str, Any]]) -> list[Worth]:
    worth: dict[tuple[str, str], Worth] = {}
    history: dict[tuple[str, str, str], list[dict[str, Any]]] = defaultdict(list)
    for row in sorted(rows, key=lambda r: r["t"]):
        key = (row["suite"], row["test"])
        w = worth.setdefault(key, Worth(*key))
        w.runs += 1
        w.seconds += row.get("s", 0.0)
        if row["outcome"] == "error":
            w.load += 1
        if row["outcome"] in ("fail", "error"):
            w.fails += 1
        w.keys.setdefault((row["code"], row["tests"]), set()).add(row["outcome"])
        history[(*key, row["checkout"])].append(row)
    for (suite, test, _), runs in history.items():
        w = worth[(suite, test)]
        for before, after in zip(runs, runs[1:]):
            if before["outcome"] != "fail" or after["outcome"] != "pass":
                continue
            code, tests = (
                before["code"] != after["code"],
                before["tests"] != after["tests"],
            )
            if code and not tests:
                w.caught += 1
            elif tests and not code:
                w.detectors += 1
            elif not code and not tests:
                w.flakes += 1
    for w in worth.values():
        w.flakes += sum(1 for o in w.keys.values() if {"pass", "fail"} <= o)
    return sorted(worth.values(), key=lambda w: w.seconds, reverse=True)


def load_rows(days: float) -> list[dict[str, Any]]:
    f = metrics_file()
    if not f.exists():
        return []
    since = time.time() - days * 86400
    rows = []
    for line in f.read_text(encoding="utf-8").splitlines():
        try:
            row = json.loads(line)
        except ValueError:
            continue
        if row.get("t", 0) >= since:
            rows.append(row)
    return rows


def report(args: argparse.Namespace) -> int:
    worth = summarize(load_rows(args.days))
    if args.suite:
        worth = [w for w in worth if args.suite in w.suite]
    if args.json:
        print(
            json.dumps([{**vars(w), "keys": None, "verdict": w.verdict} for w in worth])
        )
        return 0
    if not worth:
        print(
            f"no test runs recorded in the last {args.days:g} days ({metrics_file()})"
        )
        return 0
    print(
        f"{'test':60} {'runs':>5} {'fail':>5} {'load':>5} {'flaky':>5} "
        f"{'detect':>6} {'caught':>6} {'total s':>8}  verdict"
    )
    for w in worth[: args.limit]:
        name = f"{w.suite}:{w.test}"
        name = name if len(name) <= 60 else "…" + name[-59:]
        print(
            f"{name:60} {w.runs:5} {w.fails:5} {w.load:5} {w.flakes:5} "
            f"{w.detectors:6} {w.caught:6} {w.seconds:8.1f}  {w.verdict}"
        )
    doomed = [w for w in worth if w.verdict == "delete?"]
    if doomed:
        print(
            f"\n{len(doomed)} deletion candidates: failed without ever catching a bug"
        )
    return 0


def register(sub: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    p = sub.add_parser("metrics", help="what each test has been worth (test-report)")
    p.add_argument("--days", type=float, default=30)
    p.add_argument("--suite", help="only suites containing this text")
    p.add_argument("--limit", type=int, default=40)
    p.add_argument("--json", action="store_true")
    p.set_defaults(func=report)
