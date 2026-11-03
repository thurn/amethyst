"""`am`: Amethyst's command line. `am --help` lists the commands; the justfile wraps them."""

from __future__ import annotations

import argparse
import sys

from . import checks, flags, metrics, native, testbed, unity, vendor


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="am", description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    for module in (unity, flags, checks, metrics, native, testbed, vendor):
        module.register(sub)
    args = parser.parse_args(argv)
    try:
        return int(args.func(args))
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
