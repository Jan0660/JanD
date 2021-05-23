# JanD
[![chat on discord](https://img.shields.io/discord/749601186155462748?logo=discord)](https://discord.gg/zBbV56e)

A process manager made in C# with NativeAOT in mind.
# Usage
See [the output of the help command](/JanD/help.txt). For other documentations see [Docs.md](Docs.md),
# Installation
## GNU/Linux
Binaries are available under the [newest release](https://github.com/Jan0660/JanD/releases).
## Arch Linux
An [AUR package](https://aur.archlinux.org/packages/jand-git/) for compiling from source is available on AUR. Just install `jand-git` using your favorite AUR client.
## Compiling from source
For building with [NativeAOT](https://github.com/dotnet/runtimelab/tree/feature/NativeAOT/) you need the .NET 5 sdk, git, clang and llvm.
```bash
git clone https://github.com/Jan0660/JanD.git
cd JanD/JanD
# for more RIDs available other than linux-x64 see https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#using-rids
dotnet publish -r linux-x64 -c release
# compiled binary is now available at ./bin/release/net5.0/linux-x64/publish/JanD
```
