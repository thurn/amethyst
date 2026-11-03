# Project Amethyst

A 3D action game prototype for the Mac: Unity 6000.6.7f1 (URP) with a Rust
native plugin. It is built to try many ideas quickly, in small Unity testbeds
whose behavior is switched by flags. Free and open source, under the Apache
License 2.0.

- [AGENTS.md](AGENTS.md): how every change is made, tested and delivered.
- [FLAGS.md](FLAGS.md): the flag system and the catalog of flags.

## Setup

On an Apple silicon Mac:

| Tool                     | Install                                                                 |
| ------------------------ | ----------------------------------------------------------------------- |
| Xcode Command Line Tools | `xcode-select --install`                                                |
| Unity 6000.6.7f1         | Unity Hub, with Mac build support (Mono and IL2CPP) and a licence       |
| Rust                     | [rustup](https://rustup.rs); `native/rust-toolchain.toml` pins it       |
| .NET SDK 10              | `brew install --cask dotnet-sdk`                                        |
| uv, just                 | `brew install uv just`                                                  |
| Enact, Beads, Dolt       | [Enact](https://github.com/thurn/enact)'s setup, for delivering changes |

Optional: the [Unity Accelerator](https://unity.com/products/unity-accelerator)
on its default port, shared by every worktree.

Then `just` lists the commands. `just preview sandbox` builds the template
testbed and opens it with its variant switcher; `just test sandbox` runs its
tests headless.

Some testbeds use commercial Unity asset packs that each machine supplies
locally (AGENTS.md, Vendor assets); they are never committed.
