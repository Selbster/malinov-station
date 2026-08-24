# Verification Commands (Documentation)

Executable pre-PR checks for documentation quality. Run these before merging so the documented rules are actually enforced, not just described.

## How to use

1. Run the checks against the parts of the codebase you touched.
2. Investigate every hit before dismissing it: a hit is either a real violation or an intentional exception that deserves a comment.
3. If a check produces noise, narrow the scope to the files in your change instead of the whole tree.
4. For C# checks, prefer `rg` when available; PowerShell `Get-ChildItem` + `Select-String` is given as a portable fallback.

## Checks

### 1. Broken FTL headers (`##foo` without a space)

Headers must have a space after `##` (`## Group Name`). A missing space breaks format consistency.

```powershell
Get-ChildItem Resources\Locale -Recurse -Filter *.ftl | Select-String '^##[^ #]'
```

```bash
rg -n '^##[^ #]' Resources/Locale --glob '*.ftl'
```

Expected: no output. A hit means a broken header in an `.ftl` file.

### 2. Invalid XML doc tags

Doc tags must be well-formed: quoted attribute values, correct tag casing, and closed tags.

```powershell
Get-ChildItem Content.Server, Content.Shared, Content.Client, RobustToolbox\Robust.Shared -Recurse -Filter *.cs | Select-String '<param name=[A-Za-z]'
Get-ChildItem Content.Server, Content.Shared, Content.Client, RobustToolbox\Robust.Shared -Recurse -Filter *.cs | Select-String '</summa(?!ry>)'
```

```bash
rg -n '<param name=[A-Za-z]' Content.Server Content.Shared Content.Client RobustToolbox/Robust.Shared --glob '*.cs'
# Broken /summary variants: anything that is not exactly </summary> (missing '>', wrong case, extra chars)
rg -n '</summa(?!ry>)' Content.Server Content.Shared Content.Client RobustToolbox/Robust.Shared --glob '*.cs'
```

Expected: no output. A hit means a malformed XML tag (e.g. `<param name=isClient">`, `</summarY>`, `</summary`).

### 3. Russian or mixed-language doc comments in code

Doc comments in the code must be in English only. This targets `///`, `//`, `/*`, and `*` comment lines containing Cyrillic.

```powershell
Get-ChildItem Content.Server, Content.Shared, Content.Client -Recurse -Filter *.cs | Select-String '^\s*(///|//|/\*|\*)\s*.*[а-яА-ЯёЁ]'
```

```bash
rg -n '^\s*(///|//|/\*|\*)\s*.*[а-яА-ЯёЁ]' Content.Server Content.Shared Content.Client --glob '*.cs'
```

Expected: no output (except `obj/` build artifacts, which are excluded by ignoring generated code). A hit means a non-English comment in shipped source.

### 4. Edit markers instead of behavior descriptions in YAML

Prototype comments must describe behavior, not act as edit markers like `Malinov edit start/end`.

```powershell
Get-ChildItem Resources\Prototypes -Recurse -Filter *.yml | Select-String 'Malinov (edit|added|start|end)'
```

```bash
rg -n 'Malinov (edit|added|start|end)' Resources/Prototypes --glob '*.yml'
```

Expected: no output. A hit means a marker-style comment that should be replaced with a short behavioral description.

### 5. Public API points without `summary` (heuristic)

Public methods, public API points and DataField contracts require `/// <summary>`. This heuristic finds public members that are not immediately preceded by a doc comment; it is intentionally noisy, so review each hit manually.

```powershell
Get-ChildItem Content.Server, Content.Shared, Content.Client -Recurse -Filter *.cs | Select-String '^\s*public (sealed |abstract |partial |static )?(class|interface|struct|record|enum|[A-Za-z_][A-Za-z0-9_<>?,\[\]. ]*\([^;]*\)$)' -Context 1,0 | Select-String -NotMatch '///'
```

```bash
# Print the line before each public member; a missing '///' above a public contract is a gap.
rg -n -B1 '^\s*public ' Content.Server Content.Shared Content.Client --glob '*.cs'
```

Interpretation: for every candidate, decide whether it is a public contract (needs `summary`), a complex block (needs a short comment plus `summary`), or obvious local code (no comment — see the skip-list in SKILL.md).

## When a hit is acceptable

1. The code is generated (`obj/`, `bin/`, `Content.*.AssemblyInfo.cs`) — always ignore.
2. The file predates the documentation standard and a migration would be out of scope — leave a narrow task instead of a permanent doc comment.
3. The hit is an intentional exception with a comment explaining why — that is the documented behavior, not a violation.
