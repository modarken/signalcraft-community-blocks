# SignalCraft community blocks

A block library for [SignalCraft](https://github.com/modarken/signalcraft), and a
worked example of how to write one.

It ships **two C# blocks** (`Rds`, `Squelch`) and **one C++ block** (`Notch`),
and it is published **three ways from this one repository** — which is the point
of it. If you are writing your own library, copy this layout.

| Block | Language | What it does |
|---|---|---|
| `Rds` | C# | Decodes the RDS subcarrier out of an FM multiplex — station name, programme type, radio text |
| `Squelch` | C# | Passes audio while it is loud enough, mutes it when it is not |
| `Notch` | C++ | Removes one frequency and leaves the rest |

**`Rds` is also the largest worked example here**, and the most honest one about
what a library is for. It is ~400 lines of hand-written DSP — mixer, matched
filter, AGC, timing recovery, carrier recovery, slicer — because SignalCraft has
no digital-demodulation blocks yet. The file says so at the top and lists what
each stage will become. A library is where that work can live and be useful
today, instead of waiting for the core to grow.

---

## Using it

Pick **one** of the three. They deliver the same blocks, so taking two means the
same blocks arrive twice and SignalCraft refuses the design (`SCG118`).

### 1. Import this repository directly

In the studio: **Blocks → Import block library…**, and paste this repository's
URL.

You get the **source**. You can read it, edit your copy, and it compiles on your
machine. Works for C# and C++ designs. This is the simplest route and the one to
start with.

### 2. NuGet — `SignalCraft.Community.Blocks`

For **C# designs**. A prebuilt package; your generated project gets a
`PackageReference` and NuGet restores it.

### 3. vcpkg — `signalcraft-community-blocks`

For **C++ designs**. A prebuilt port; your generated `CMakeLists.txt` gets a
`find_package` and a link line.

---

## What makes this a library

Two files, and neither is large.

**`signalcraft-library.json`** at the root marks the folder as a library:

```json
{
  "id": "community-blocks",
  "version": "1.1.0",
  "name": "SignalCraft community blocks",
  "also": {
    "nuget": "SignalCraft.Community.Blocks",
    "vcpkg": "signalcraft-community-blocks"
  }
}
```

`id` is the **identity** and never changes — not when the version moves, not
ever. A library that renames itself between releases becomes an unrecognised
stub in every design that used it, which is a real bug people hit in other
software and fix by hand-editing project files.

`also` is what makes the three routes one library rather than three. The names
are unrelated strings chosen by three different packaging systems, and nobody
but you could know they are the same thing — so you say so, and SignalCraft can
then tell somebody they are about to import the same library twice.

**`blocks.json`** describes the blocks in the same format `signalcraft catalog`
writes. The **C++** one has to be there: nothing can ask a compiled binary what
it contains. The **C#** ones are there because this library is ALSO a NuGet
package, and a package is restored when the generated project builds - long
after `signalcraft build` needed to type-check the design. A library shipping
C# blocks only as source needs no entry for them; the source is compiled and
read.

---

## Layout

```
signalcraft-library.json     marks this as a library, names the three routes
blocks.json                  describes the C++ blocks, and the C# ones for NuGet
blocks/
  Rds.cs                     the C# blocks, as source
  Squelch.cs

csharp/                      the NuGet package
cpp/                         the C++ block, plain CMake
vcpkg/                       the vcpkg port

NuGet.config                 where SignalCraft.Hosting comes from
eng/sync-sdk.ps1             puts it there
artifacts/nuget/             the local feed (contents gitignored)
```

The three packaging folders are independent — each system reads its own files
and ignores the rest, which is why one repository can be all three.

### Building the NuGet half

Only needed if you are changing this repository; importing it needs none of
this. A block derives from `BlockBase`, which lives in `SignalCraft.Hosting`,
and SignalCraft is not published anywhere — so the package has to come from a
local build:

```powershell
./eng/sync-sdk.ps1            # copies it from a sibling signalcraft checkout
dotnet build csharp/SignalCraft.Community.Blocks.csproj -c Release
```

The version is pinned exactly, in the csproj and in that script. A library
published to strangers that floated with whatever SDK happened to be on the
author's machine would not be something anybody could depend on.

---

## Writing your own

Copy this repository, then:

1. Change `id` in `signalcraft-library.json` to something nobody else will use.
   Reverse-DNS (`com.example.blocks`) is fine and so is a plain dashed name.
2. Put your `.cs` files in `blocks/`. That is all a C#-only library needs — you
   can delete `csharp/`, `cpp/`, `vcpkg/` and `blocks.json` and it will work.
3. `git push`. That is publishing. There is no account to make, nothing to
   upload and no registry to be listed in.
4. Tag a release (`git tag v1.0.0 && git push --tags`) when you want people to
   have something stable to pin to. SignalCraft offers the newest **release**
   tag and will not push anybody onto a release candidate.

---

## Licensing

**Blocks here are MIT. SignalCraft itself is not** — it is all rights reserved
(see its `LICENSE`), and that is deliberate on its author's part rather than an
oversight. The two are separate works with separate terms, and this repository
being MIT says nothing about the SDK it is written against.

What that means in practice: you may take, change and redistribute the blocks in
this repository freely. Running them still requires a SignalCraft you are
entitled to have.

An out-of-tree library is also the right home for a block whose licensing does
**not** fit — the licence stays yours, and somebody who wants the block chooses
it deliberately by importing it. That is a large part of why this mechanism
exists.
