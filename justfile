# Amethyst's commands; `just` lists them. Most wrap `am` (tools/), and extra
# arguments pass straight through: `just play sandbox --variant floaty-orb`.

set positional-arguments := true

am := "uv run --quiet --project tools am"

default:
    @just --list --unsorted

# Build if needed and play: a window with sound for humans; headless, muted and 10 s for agents (--variant V, --flag k=v, --preset P, --seconds N)
[group('play')]
play project *args:
    {{ am }} play "$@"

# Build a testbed and open it in a window with its variant switcher, for a human to compare concepts
[group('play')]
preview testbed *args:
    {{ am }} play --window "$@"

# Headless stills of every variant (or the project) into its run folder, for agents to look at (--at 1,3)
[group('play')]
shots project *args:
    {{ am }} shots "$@"

# Build a project's Mac player unless it is up to date (--backend il2cpp, --development, --force)
[group('play')]
build project *args:
    {{ am }} build "$@"

# Open a project in the Unity editor (humans)
[group('play')]
editor project:
    {{ am }} editor "$1"

# Fork a testbed, warm Library included (--from sandbox)
[group('play')]
testbed-new name *args:
    {{ am }} testbed new "$@"

# The Unity projects
[group('play')]
projects:
    {{ am }} projects

# The latest run folders (logs, results, captures) of a project
[group('play')]
logs project:
    {{ am }} logs "$1"

# A project's EditMode tests, headless; memoized by their inputs (--force, --filter NAME)
[group('test')]
test project *args:
    {{ am }} test "$@"

# The engine-free C# tests under dotnet, in seconds (dotnet test flags, e.g. --filter Flag)
[group('test')]
test-dotnet *args:
    dotnet test dotnet/Amethyst.slnx --nologo "$@"

# The native plugin's tests
[group('test')]
test-rust *args:
    cd native && cargo test "$@"

# The tools' tests
[group('test')]
test-py *args:
    cd tools && uv run --quiet pytest "$@"

# What each test has been worth: time, flakes, change detectors, real catches, deletion candidates
[group('test')]
test-report *args:
    {{ am }} metrics "$@"

# The fast check `enact push` runs (lanes for what changed, budget 60 s)
[group('checks')]
check:
    {{ am }} check

# Slow CI, or some of its lanes: repo, python, rust, csharp, unity (budget 300 s)
[group('checks')]
ci *lanes:
    {{ am }} ci "$@"

# Format everything: black, rustfmt, CSharpier, mdformat (80 columns), the justfile, FLAGS.md
[group('checks')]
fmt:
    uv run --quiet --project tools black --quiet tools
    cd native && cargo fmt --all
    dotnet tool restore >/dev/null && dotnet csharpier format packages game/Assets testbeds dotnet
    just --fmt --unstable
    {{ am }} flags doc
    git ls-files '*.md' | grep -v -e '^.beads/' -e '^.agents/' | xargs uv run --quiet --project tools mdformat

# Build the Rust plugin into packages/com.amethyst.native (every Unity step does this too)
[group('checks')]
native:
    {{ am }} native

# Every flag, or those matching a filter
[group('flags')]
flags *filter:
    {{ am }} flags list "$@"

# Regenerate FLAGS.md's tables from the declarations
[group('flags')]
flags-doc:
    {{ am }} flags doc

# Check declarations, presets, variants and FLAGS.md
[group('flags')]
flags-check:
    {{ am }} flags check

# Flags nothing reads: delete them, or the branches that lost
[group('flags')]
flags-unused:
    {{ am }} flags unused

# Vendor assets: sync PROJECT, add PROJECT SOURCE PATH, save PROJECT, guard, init
[group('vendor')]
vendor *args:
    {{ am }} vendor "$@"
