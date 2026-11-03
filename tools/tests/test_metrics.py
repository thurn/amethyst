from typing import Any

from amethyst_tools import metrics


def row(t: int, test: str, outcome: str, code: str, tests: str) -> dict[str, Any]:
    return {
        "t": t,
        "suite": "s",
        "test": test,
        "outcome": outcome,
        "s": 1.0,
        "checkout": "c",
        "code": code,
        "tests": tests,
    }


def test_failures_are_judged_by_what_changed_before_the_pass() -> None:
    rows = [
        row(1, "catches", "fail", "a", "x"),
        row(2, "catches", "pass", "b", "x"),
        row(1, "detector", "fail", "a", "x"),
        row(2, "detector", "pass", "a", "y"),
        row(1, "flaky", "fail", "a", "x"),
        row(2, "flaky", "pass", "a", "x"),
        row(1, "steady", "pass", "a", "x"),
    ]
    verdicts = {w.test: w.verdict for w in metrics.summarize(rows)}
    assert verdicts == {
        "catches": "keep",
        "detector": "delete?",
        "flaky": "delete?",
        "steady": "",
    }
