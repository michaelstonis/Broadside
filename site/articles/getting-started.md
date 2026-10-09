---
title: "Getting started"
---

# Getting started

There is nothing to install yet. Broadside has no release and no package on NuGet.org; installation instructions and a first example arrive with the first release.

The packages that will ship are listed in the [README](https://github.com/michaelstonis/Broadside#packages). They target `net10.0` only ([ADR 0002](https://github.com/michaelstonis/Broadside/blob/main/docs/adr/0002-target-net10-only.md)).

## Building from source

The repository builds today, although the assemblies contain no public API yet. It needs the .NET SDK pinned in [`global.json`](https://github.com/michaelstonis/Broadside/blob/main/global.json).

```sh
git clone https://github.com/michaelstonis/Broadside.git
cd Broadside
dotnet build Broadside.Core.slnf
```

## Building this site

The site is built with [Lunet](https://lunet.io), installed as a local .NET tool:

```sh
dotnet tool restore
./check-docs.sh        # builds the site into site/.lunet/build/www and checks its routes and links
```

To follow progress until there is something to install, watch the [roadmap](roadmap.md).
