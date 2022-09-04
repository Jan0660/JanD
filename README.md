# JanD

[![chat on discord](https://img.shields.io/discord/749601186155462748?logo=discord)](https://discord.gg/zBbV56e)

A process manager made in C# with NativeAOT in mind.

# Usage

See documentation at [jand.jan0660.dev](https://jand.jan0660.dev).

# Installation

| OS      | Methods                                                                                              |
| :------ | :--------------------------------------------------------------------------------------------------- |
| Windows | [CI](https://ci.nekos.cloud/buildConfiguration/JanD_Windows/lastFinished?buildTab=artifacts&guest=1) |
| Linux   | [CI](https://ci.nekos.cloud/buildConfiguration/JanD_Linux/lastFinished?buildTab=artifacts&guest=1)   |
| MacOS   | [CI](https://ci.nekos.cloud/buildConfiguration/JanD_Build/lastFinished?buildTab=artifacts&guest=1)   |

[other distro-specific instructions](https://jand.jan0660.dev/#installation)

## Compiling from source

For building with [NativeAOT](https://github.com/dotnet/runtimelab/tree/feature/NativeAOT/) make sure you fill the [prerequisites](https://github.com/dotnet/runtimelab/blob/feature/NativeAOT/docs/using-nativeaot/prerequisites.md) first.

```bash
git clone https://github.com/Jan0660/JanD.git
cd JanD/JanD
# for more RIDs available other than linux-x64 see https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#using-rids
dotnet publish -r linux-x64 -c release
# compiled binary is now available at ./bin/release/net5.0/linux-x64/publish/JanD
```
