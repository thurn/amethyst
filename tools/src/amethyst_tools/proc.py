"""Running heavy children politely: niced, logged to a file, and at most a few Unity
processes per machine however many agents and worktrees want one."""

from __future__ import annotations

import fcntl
import itertools
import os
import signal
import subprocess
import sys
import threading
import time
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path
from typing import IO

from . import paths

NICE = 10


def unity_slots() -> int:
    """Concurrent Unity processes per machine: $AMETHYST_UNITY_SLOTS, else a quarter of the
    cores (at least 2). Each holds gigabytes, so more just thrashes."""
    override = os.environ.get("AMETHYST_UNITY_SLOTS")
    return int(override) if override else max(2, (os.cpu_count() or 8) // 4)


@contextmanager
def unity_slot(label: str) -> Iterator[None]:
    """Holds one of the machine's Unity slots (a locked file under the cache) while the block
    runs, waiting for one if all are taken."""
    folder = paths.cache_root() / "slots"
    folder.mkdir(exist_ok=True)
    waited = time.monotonic()
    announced = False
    while True:
        for i in range(unity_slots()):
            handle = open(folder / f"unity-{i}.lock", "w")
            try:
                fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                handle.close()
                continue
            try:
                handle.write(f"{os.getpid()} {label}\n")
                handle.flush()
                if announced:
                    print(
                        f"{label}: got a Unity slot after {time.monotonic() - waited:.0f} s"
                    )
                yield
            finally:
                fcntl.flock(handle, fcntl.LOCK_UN)
                handle.close()
            return
        if not announced:
            print(f"{label}: all {unity_slots()} Unity slots are busy; waiting")
            announced = True
        time.sleep(1)


def niced(cmd: list[str]) -> list[str]:
    increment = max(0, NICE - os.getpriority(os.PRIO_PROCESS, 0))
    return ["nice", "-n", str(increment), *cmd] if increment else cmd


def run_logged(
    cmd: list[str], log: Path, echo: bool = False, env: dict[str, str] | None = None
) -> int:
    """Runs `cmd` with its output in `log` (and on the console with `echo`), until the process
    exits: Unity's package manager server can outlive it holding the pipe, so the pipe is read
    on a thread and the server is reaped once Unity exits."""
    log.parent.mkdir(parents=True, exist_ok=True)
    with open(log, "w", encoding="utf-8", errors="replace") as out:
        proc = subprocess.Popen(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            stdin=subprocess.DEVNULL,
            env=env,
        )
        stream = proc.stdout
        assert stream is not None
        reader = threading.Thread(target=_pump, args=(stream, out, echo), daemon=True)
        reader.start()
        code = proc.wait()
        reap_package_manager(proc.pid)
        reader.join(timeout=5)
    return code


def _pump(stream: IO[bytes], out: IO[str], echo: bool) -> None:
    try:
        for raw in stream:
            line = raw.decode("utf-8", "replace")
            out.write(line)
            if echo:
                sys.stdout.write(line)
    except ValueError:  # `out` closed under a pipe something else still holds
        pass


def reap_package_manager(unity_pid: int) -> None:
    """Kills the UnityPackageManager server a Unity process started (it names its Unity as
    `-s <pid>` and runs in its own session) if it outlived Unity."""
    ps = subprocess.run(
        ["ps", "-axo", "pid=,command="], check=False, capture_output=True, text=True
    )
    for row in ps.stdout.splitlines():
        pid, _, command = row.strip().partition(" ")
        args = command.split()
        if any(a.endswith("UnityPackageManager") for a in args) and any(
            a == "-s" and b == str(unity_pid) for a, b in itertools.pairwise(args)
        ):
            try:
                os.kill(int(pid), signal.SIGTERM)
            except OSError:
                pass
