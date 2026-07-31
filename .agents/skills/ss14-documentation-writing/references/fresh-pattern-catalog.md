# Fresh Pattern Catalog (Documentation)

> All entries below have passed the freshness filter: the modification date is not older than 2024-02-20 and without TODO/HACK/FIXME on the documentation topic.
>
> Status legend:
> - `Use` — confirmed on a concrete, verifiable example; safe to copy.
> - `Aspirational` — a real standard to review against, but not yet broadly confirmed in code.
> - `Check` — needs re-verification against current code (a command is provided).

| Domain | Pattern | Confirmed with a recent example | Status |
|---|---|---|---|
| Engine API docs | `summary` + `remarks` + parameterized contract description | `SharedContainerSystem.Insert(...)` (`RobustToolbox/Robust.Shared/Containers/SharedContainerSystem.Insert.cs`) | Use |
| Engine architecture docs | Documentation of the abstract container API through properties and methods | `BaseContainer` — pattern is real, but its docs contain a malformed XML tag (`<param name=isClient">`), so cite only after fixing or pick a clean source | Aspirational |
| Shared gameplay docs | `summary` on actions + short comments on critical checks | `MimePowersSystem.OnInvisibleWall(...)` (`Content.Shared/Abilities/Mime/MimePowersSystem.cs`) | Use |
| Client gameplay docs | `summary` + explanation of complex transformations, not all lines in a row | `ClickableSystem.CheckClick(...)` (`Content.Client/Clickable/ClickableSystem.cs`) | Use |
| Server gameplay docs | `summary` + `remarks` for an invariant important for calls to other methods | `AccessOverriderSystem.PrivilegedIdIsAuthorized(...)` (`Content.Server/Access/Systems/AccessOverriderSystem.cs`) | Use |
| Large subsystem docs | Dividing a large system into partial parts with a separate area of responsibility | `Shared*System` partial-file families (`MapLoaderSystem.Save/Load/LoadMap.cs`, `SharedPlayerManager.State/Sessions/Data.cs`, `PrototypeManager.Categories.cs`, `SharedReplayRecordingManager.Write.cs`, `ExplosionSystem.*.cs`) | Use |
| Cross-system docs | Check for unusual API calls in system consumers | non-default optional flags, e.g. `TryDoSomething(..., force: true, quiet: true)` — real callsites with `quiet: true`/`force: true` mostly lack the explanatory comment, so treat as a review target | Aspirational |
| YAML docs | Concise group comments (1 line) between prototype blocks | `salvage reward` catalog groups (`Resources/Prototypes/Catalog/Cargo/salvage_rewards.yml`, `# Rank 2`, `# Rank 3`) | Use |
| FTL docs | Correct group separator in the format `## Group Name` | `battery menu` localization section (`Resources/Locale/en-US/power/battery.ftl`) | Use |
| FTL docs hygiene | Checking the validity of headers before merging | broken `##bombs` header no longer exists in the uplink directory (verified 2026-08-01); run `rg '^##[^ #]' Resources/Locale --glob '*.ftl'` instead of hunting a fixed line | Check |

## Re-verification commands

- Engine API docs: `rg -n 'param name="|param name=[A-Za-z]' RobustToolbox/Robust.Shared/Containers --glob '*.cs'`
- Engine architecture docs: `rg -n 'name=[A-Za-z]' RobustToolbox/Robust.Shared/Containers/BaseContainer.cs`
- FTL docs hygiene: `rg -n '^##[^ #]' Resources/Locale --glob '*.ftl'`
- Cross-system docs: `rg -n 'quiet: true|force: true' Content.Server Content.Shared Content.Client --glob '*.cs'`

## Short output

1. `summary` remains the main contract documentation format in C#.
2. Pointed comments are needed where the logic is not obvious (coordinates, invariants, non-standard branches).
3. YAML/FTL benefits from conciseness: short separators and a minimum of "explanatory walls".
4. Only cite a file as a pattern after verifying its docs are actually correct; otherwise move it to `rejected-snippets.md` and mark the pattern `Aspirational`.
