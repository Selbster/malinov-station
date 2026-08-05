---
trigger: always_on
---

# Rule: Defining the codebase prefix, project folder and edit markers

This rule is mandatory for any task in the Malinov Station fork of Space Station 14.

## 1. What you need to determine before starting work

Before analyzing, planning paths and making changes, always fix three values:

1. Active codebase prefix: `Malinov`.
2. Forked project folder: `_MalinovStation`.
3. The text of the edit marker that should be used in comments: `Malinov-Edit`.

Don't start editing vanilla files until you've confirmed you are actually in the Malinov Station
codebase (see below). Don't assume some other SS14 fork's prefix or markers apply here — this
repository is its own fork, not part of any other fork family.

## 2. How to confirm you are in the Malinov Station codebase

Collect the concrete signals before touching vanilla files:

1. Git remote repository slug: the repository name is `malinov-station`.
2. The name of the repository root folder / working directory (typically `malinov-station`).
3. The forked project folder actually present in the code base near the files being changed:
   `_MalinovStation` (for example `Content.Shared/_MalinovStation/Audio/...`).
4. The nearest existing edit markers in the adjacent code (`Malinov-Edit`, `Malinov edit start/end`).

If none of these signals match — for example the working tree is a different SS14 fork entirely —
stop and re-run the detection instead of assuming `Malinov`/`_MalinovStation` apply.

## 3. Forking this repository (template rule)

This repository is a fork parent: downstream projects fork it and inherit vanilla + `_MalinovStation`
as-is. When a child fork is created from this repository, do not rework this file for the child.

Instead, in the child fork:

1. Keep the `Malinov` row and all detection sections unchanged — inherited content stays `Malinov`.
2. Add one new row to the table with the child's own prefix, project folder (`_<Child>`), and markers.
3. Apply the detection logic per row: the child's own fork folder (`_<Child>`) and markers take
   priority over inherited `Malinov` content, but never mix the two families in one task.

In this repository (malinov-station) only the `Malinov` row applies. Never assume another fork's
markers or prefix apply here, even if the working tree contains inherited fork content.

## 4. Prefix, folder and marker

| Prefix | Project folder | Single-line marker | Block markers |
| --- | --- | --- | --- |
| `Malinov` | `_MalinovStation` | `Malinov-Edit` | `Malinov edit start/end`, `Malinov added start/end` |

## 5. How to apply a marker in a specific file

1. Use the `Malinov` prefix, the `_MalinovStation` project folder and the markers above.
2. Do not change the marker text, just adapt the comment syntax to the file language.
3. If the file already uses a local style (for example an existing `Malinov edit start/end` block
   nearby), continue with it instead of switching styles mid-file.

Select the comment syntax to match the file language:

- C#, C++, Java: `// Malinov-Edit`, `// Malinov edit start - reason`
- YAML, FTL, Python, Shell: `# Malinov-Edit`, `# Malinov edit start - reason`
- XML, HTML: `<!-- Malinov-Edit -->`, only if comments in this format are allowed and really needed

## 6. How does this affect the structure of edits?

1. Place new forked files under `_MalinovStation` (for example `Content.Shared/_MalinovStation/...`,
   `Resources/Prototypes/_MalinovStation/...`, `Resources/Locale/ru-RU/_MalinovStation/...`).
2. Prefer isolating custom content behind a helper/namespace inside `_MalinovStation` over sprinkling
   inline edit markers through vanilla files — see the existing `MalinovSoundCollectionHelper` /
   `MalinovSoundCollections` pattern in `Content.Shared/_MalinovStation/Audio/` for how vanilla
   systems (`ContentAudioSystem`, `NukeSystem`) pull in `_MalinovStation` content through a small,
   clearly marked hook rather than a rewritten vanilla file.
3. When a hook in a vanilla file truly can't be avoided, keep it minimal and mark it with the
   `Malinov-Edit` marker (see `ss14-upstream-maintenance` for the full editing patterns).
4. Do not use another fork's edit markers in this codebase, and don't move Malinov-only code out of
   `_MalinovStation` for the sake of "matching vanilla structure."
