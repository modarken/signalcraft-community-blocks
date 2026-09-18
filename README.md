# SignalCraft community blocks

A block library for [SignalCraft](https://github.com/modarken/signalcraft), and a
worked example of how to write one.

It ships **one C# block** (`Squelch`) and **one C++ block** (`Notch`), and it is
published **three ways from this one repository** — which is the point of it. If
you are writing your own library, copy this layout.

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
  "version": "1.0.0",
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

**`blocks.json`** describes the **C++** blocks, in the same format
`signalcraft catalog` writes. C# blocks need no description: the studio compiles
the source and reads what the class declares. Nothing can ask a compiled C++
binary what it contains, so that half has to be written down.

---

## Layout

```
signalcraft-library.json     marks this as a library, names the three routes
blocks.json                  describes the C++ blocks
blocks/
  Squelch.cs                 the C# block, as source

csharp/                      the NuGet package
cpp/                         the C++ block, plain CMake
vcpkg/                       the vcpkg port
```

The three packaging folders are independent — each system reads its own files
and ignores the rest, which is why one repository can be all three.

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

Blocks here are MIT, like SignalCraft itself.

An out-of-tree library is also the right home for a block whose licensing does
**not** fit SignalCraft's — the licence stays yours, and somebody who wants the
block chooses it deliberately by importing it. That is a large part of why this
mechanism exists.
