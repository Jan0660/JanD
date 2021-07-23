# JanD
[![chat on discord](https://img.shields.io/discord/749601186155462748?logo=discord)](https://discord.gg/zBbV56e)
[![TeamCity build status](https://ci.nekos.cloud/app/rest/builds/aggregated/strob:(branch:(buildType:(id:JanD_Build),policy:active_history_and_active_vcs_branches),locator:(buildType:(id:JanD_Build)))/statusIcon)](https://ci.nekos.cloud/buildConfiguration/JanD_Build/lastFinished?buildTab=artifacts&guest=1)
[![TeamCity build status](https://ci.nekos.cloud/app/rest/builds/aggregated/strob:(branch:(buildType:(id:JanD_Linux),policy:active_history_and_active_vcs_branches),locator:(buildType:(id:JanD_Linux)))/statusIcon)](https://ci.nekos.cloud/buildConfiguration/JanD_Linux/lastFinished?buildTab=artifacts&guest=1)

A process manager made in C# with NativeAOT in mind.
# Usage
See [the output of the help command](/JanD/Resources/help.txt). For other documentations see [Docs.md](Docs.md),
# Installation
## MacOS
Grab the latest build from [here](https://ci.nekos.cloud/buildConfiguration/JanD_Build/lastFinished?buildTab=artifacts&guest=1), courtesy of NekosCloud.
## GNU/Linux
Binaries are available under the [newest release](https://github.com/Jan0660/JanD/releases) and latest builds [here](https://ci.nekos.cloud/buildConfiguration/JanD_Linux/lastFinished?buildTab=artifacts&guest=1), courtesy of NekosCloud.
## Arch Linux
An [AUR package](https://aur.archlinux.org/packages/jand-git/) for compiling from source is available on AUR. Just install `jand-git` using your favorite AUR client.
## Compiling from source
For building with [NativeAOT](https://github.com/dotnet/runtimelab/tree/feature/NativeAOT/) make sure you fill the [prerequisites](https://github.com/dotnet/runtimelab/blob/feature/NativeAOT/docs/using-nativeaot/prerequisites.md) first.
```bash
git clone https://github.com/Jan0660/JanD.git
cd JanD/JanD
# for more RIDs available other than linux-x64 see https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#using-rids
dotnet publish -r linux-x64 -c release
# compiled binary is now available at ./bin/release/net5.0/linux-x64/publish/JanD
```
