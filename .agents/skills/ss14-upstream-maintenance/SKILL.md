---
name: ss14-upstream-maintenance
description: Guide to working with the Malinov Station Space Station 14 fork with project-folder pattern (`_MalinovStation`) to minimize merge conflicts with the upstream (space-wizards/space-station-14). Use when modifying vanilla code or prototypes.
---

# 🛡️ Working with Upstream code and minimizing conflicts

This skill describes the standards and patterns adopted in the Malinov Station fork of Space Station
14 for working with code inherited from the upstream, using project-folder isolation (`_MalinovStation`).
**Main goal:** Maintain the ability to easily receive updates from the upstream (merge), minimizing manual edits in case of conflicts.

Before making changes, confirm you are in the Malinov Station codebase by checking for:
- Prefix `Malinov` in custom identifiers
- Project folder `_MalinovStation`
- Single-line marker `Malinov-Edit` in modified vanilla files

New code, prototypes and assets go under `_MalinovStation` even when the hook or parent prototype lives in vanilla.
Currently the fork is small: most custom content lives in `_MalinovStation` helpers called via minimal hooks from vanilla files. As the fork grows, migrate toward partial classes in `_MalinovStation`.

## ⚠️ Golden rule

> [!IMPORTANT]
> **Minimizing changes to vanilla files is MORE IMPORTANT than "pretty" architecture.**
> It's better to leave the "dirty" hack in one line of the vanilla file than to rewrite half the system, creating hell when merging.

## ⚠️ Parent contract

This repository is a fork parent: downstream projects fork it and inherit vanilla plus `_MalinovStation`
as-is. Because of that, `_MalinovStation` is a public API surface for child forks:

1. Prototype IDs and `[DataField]` names inside `_MalinovStation` are a stable contract. Renaming or
   removing them for style reasons breaks saved maps and inherited child content — always rename
   through `Resources/migration.yml` and treat it as a visible change.
2. Prefer extending over rewriting: a child that needs a different variant inherits the parent
   prototype (`parent: <MalinovId>`) instead of editing the shared `_MalinovStation` file in place.
3. Full rewrites of `_MalinovStation` files are the same anti-pattern as full rewrites of vanilla
   files — they break the parent/child merge flow.
4. Child fork logic lives in the child's own `_<Child>` folder, never inside the shared
   `_MalinovStation` folder.


## 📁 Folder structure and Project Folder

To clearly separate vanilla code from our modifications, a special project folder is used, starting with the symbol `_`: `_MalinovStation`.

**Why `_`?**
- The folder is always at the top of the file list and is easy to find.
- Visually separates “our” code from “their” (vanilla) code.

**What should I put in `_MalinovStation`?**
1. **New files:** Completely new systems, components, prototypes.
2. **Partial classes:** Extensions of vanilla classes (see below).
3. **Assets:** New sprites, sounds, textures, locale strings.

The project folder is always `_MalinovStation`, regardless of subsystem. Do not move new files to a
differently-named folder just because the original code or inspiration came from another SS14 fork.

> [!TIP]
> **Current pattern (recommended now):**
> Use minimal vanilla hooks that call into `_MalinovStation` helpers. This keeps merge conflicts tiny
> while still isolating custom logic.
>
> The existing `Content.Shared/_MalinovStation/Audio/MalinovSoundCollectionHelper.cs` +
> `MalinovSoundCollections.cs` pair is the reference: vanilla `ContentAudioSystem`/`NukeSystem` gain
> a two-line hook that calls into `_MalinovStation` code, instead of vanilla logic being rewritten in place.
>
> **Target pattern (use when the feature needs new fields/methods on a vanilla class):**
> Partial classes in `_MalinovStation` (see below). Not needed for simple helper calls.

## 🛠️ Modification of C# code

> [!IMPORTANT]
> **Fork logic lives in `_MalinovStation`, vanilla files only get hooks and markers.**
> A vanilla file may contain a call into a `_MalinovStation` helper (Pattern 1), a `Malinov edit start/end`
> block (Pattern 2/3), or a partial-class extension (Pattern 4) — but it must **never** contain the
> implementation of custom logic itself (helper bodies, new algorithms, custom state). If you find
> yourself writing more than a few lines of fork logic inside a vanilla file, move it into
> `_MalinovStation` and leave a minimal hook.

When modifying existing vanilla code (outside the project folder), use the following patterns.

### 1. Pattern: Minimal hook + `_MalinovStation` helper (RECOMMENDED)

Use this when a vanilla system needs to call custom logic, but the feature does **not**
require adding new fields or methods to the vanilla class.

**Example (from real codebase):**

*Vanilla file (`Content.Server/Audio/ContentAudioSystem.cs`):*
```csharp
// Malinov added start - mix custom lobby music with vanilla playlist
using Content.Shared._MalinovStation.Audio;
var merged = MalinovSoundCollectionHelper.GetMergedFiles(vanillaPlaylist, "LobbyMusicMalinov");
// Malinov added end
```

*Custom helper (`Content.Shared/_MalinovStation/Audio/MalinovSoundCollectionHelper.cs`):*
```csharp
namespace Content.Shared._MalinovStation.Audio;

public static class MalinovSoundCollectionHelper
{
    public static List<string> GetMergedFiles(List<string> vanilla, string customCollection)
    {
        // ... custom logic ...
    }
}
```

**Why this is the default:**
- Only 2 lines touch the vanilla file → tiny merge conflict surface.
- All complex logic lives in `_MalinovStation` → easy to find and expand.
- No need to change vanilla class signatures or make them `partial`.

### 2. Pattern `Edit Start` / `Edit End`

Used when you need to change existing logic inside a method or property.
Makes it easy to see your changes against the background of vanilla code.

**Format:**
```csharp
// Malinov edit start - brief reason for changes
```
...your code...
```csharp
// Malinov edit end
```

**Example (change value):**
```csharp
component.Field2 = 321;
// Malinov edit start - increasing radius for balance
component.Field = 123;
// Malinov edit end
```

**Example (changing logic):**
```csharp
// Malinov edit start - fixing double gateways
if (TryComp<AirlockComponent>(uid, out var airlock))
{
    // ...new logic...
}
// Malinov edit end
```

### 3. Pattern `Added Start` / `Added End`

Used when you add a **new** block of code (eg calling an event, checking) that was not in the original.

**Format:**
```csharp
// Malinov added start - brief reason for adding
```
...new code...
```csharp
// Malinov added end
```

**Example:**
```csharp
// Malinov added start - to know when the target was hit
_eventBus.RaiseLocalEvent(uid, new ProjectileHitEvent(projectile, entity));
// Malinov added end
```

### 4. Partial Classes (TARGET PATTERN)

If you need to add a **new field, property or method** to an existing class or system, **DO NOT** write it in a vanilla file.
Instead, create a `partial` class in `_MalinovStation`.

**Pattern:**
1. Find the vanilla class (eg `SharedAirlockSystem`).
2. Create a file in your folder: `Content.Shared/_MalinovStation/Doors/SharedAirlockSystem.Malinov.cs`.
3. Declare the class as `partial` with the same namespace.
4. **Important:** Suppress the namespace mismatch warning if necessary.

**Example:**
*Vanilla file (`Content.Shared/Doors/Systems/SharedAirlockSystem.cs`):*
```csharp
namespace Content.Shared.Doors.Systems;

public abstract partial class SharedAirlockSystem : EntitySystem
{
    // Vanilla code...
}
```

*Your file (`Content.Shared/_MalinovStation/Doors/SharedAirlockSystem.Malinov.cs`):*
```csharp
using Content.Shared.Doors.Systems; // We use vanilla namespace

// We suppress warning, since the file is physically located in another folder
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Shared.Doors.Systems;

public abstract partial class SharedAirlockSystem
{
    // Your new logic, accessible "as if" inside the original class
    public void MyNewMethod() { ... }
}
```

## 📋 When to use what

| Situation | Use |
|-----------|-----|
| Need to inject custom logic into a vanilla method (call helper, modify args, side effect) | **Pattern 1** — Hook + Helper |
| Need to change an existing vanilla value or logic block | **Pattern 2** — `Edit Start` / `Edit End` |
| Need to add a new code block (event call, extra check) to vanilla | **Pattern 3** — `Added Start` / `Added End` |
| Need to add a **new field, property or method** to an existing vanilla class | **Pattern 4** — Partial Class in `_MalinovStation` |
| Completely new subsystem (new components, systems, entities) | **Full isolation** — everything in `_MalinovStation` |

## 🧬 Modifying Prototypes (YAML)

Changing vanilla YAML files (`Resources/Prototypes/Entities/...`) is **BAD PRACTICE**. This is a guaranteed conflict with any change to this file in the upstream.

### 🌌 Ideal Prototype Change Pattern

Instead of editing the original, we create a **replacement heir**.

**Algorithm:**
1. Find the vanilla entity ID (for example, `AirlockHatchSyndicate`).
2. Create a **new** YAML file in `_MalinovStation` (for example, `Resources/Prototypes/_MalinovStation/.../access.yml`).
3. Create a new entity:
    - `id`: Add a suffix or prefix (for example, `AirlockHatchSyndicateLocked`).
    - `parent`: Specify a vanilla ID.
    - Make the necessary changes (add components, change fields).
4. **Migration Magic:** Register the replacement in `Resources/migration.yml`.

**Implementation example:**

*1. New prototype (`_MalinovStation/Entities/Structures/Doors/Airlocks/access.yml`):*
```yaml
- type: entity
  parent: AirlockHatchSyndicate  # Inherit from the original
  id: AirlockHatchSyndicateLocked # New ID
  suffix: Syndicate, Locked
  categories: [ HideSpawnMenu ] # Malinov added - hide from spawn if this is a technical entity
  components:
  - type: AccessReader
    access: [["SyndicateAgent"]] # Add the required changes
```

*2. Migration file (`Resources/migration.yml`):*
Add an entry to the end of the file or to the appropriate section.
```yaml
# ... existing migrations ...

# Malinov-Edit
AirlockHatchSyndicate: AirlockHatchSyndicateLocked
```

**Result:**
When loading the map, the engine will automatically replace all `AirlockHatchSyndicate` with `AirlockHatchSyndicateLocked`.
When updating the upstream, if new components are added to `AirlockHatchSyndicate`, your `AirlockHatchSyndicateLocked` will automatically receive them through inheritance (`parent`). File conflicts - **0**.

> [!WARNING]
> **Migration DOES NOT update links in other prototypes!**
> The `migration.yml` file tells the engine to replace the entity ONLY when spawning on the map and in saves.
> If the old ID (`AirlockHatchSyndicate`) is used in:
> - Spawn Pools
> - Crafting recipes
> - Fields of other components (for example, `SpawnOnDeath`)
>
> ...the old essence will remain there! You need to find all uses of the old ID and replace them with the new one manually (via the `edit` pattern or overriding).

### 🛠️ Minor changes in vanilla files

If migration is not possible (but try to use it!), use `edit` comments directly in YAML, but try to do it on one line.

```yaml
- type: entity
  id: VanillaEntity
  components:
  - type: Item
    path: _MalinovStation/Objects/123.rsi # Malinov edit - sprite replacement
```

### 🚫 Anti-patterns (What NOT to do)

❌ **Direct code removal.**
Instead of deleting, comment out the code and leave the mark `edit`.
```csharp
// BAD:
// public void DeletedMethod() { }

// GOOD:
// Malinov edit start - deleted because interferes with mechanics X
// public void DeletedMethod() { ... }
// Malinov edit end
```

❌ **Rewriting entire files.**
If you copy the entire file into your folder and disable the original, you lose all future updates to that file. DO this ONLY if the logic changes fundamentally and irreversibly.

❌ **Replacement of entity ID without inheritance.**
If you simply copy the YAML of an entity and change it, you will not receive updates to the parent components from upstream. Always use `parent`.

## 🔗 Related skills

The ECS skills (`ss14-ecs-components`, `ss14-ecs-entities`, `ss14-ecs-prototypes`,
`ss14-ecs-systems`) describe the architecture rules that are **mandatory for new `_MalinovStation`
code**. Each of them has a "Scope of applicability" section stating that vanilla upstream code is a
reference, not a refactoring target. This skill is the practical counterweight: it defines **how**
to integrate fork code with vanilla without breaking the merge.

When touching a vanilla component/prototype, remember the **serialization contract**: existing
`[DataField]` names (including legacy/typo'd ones like `decayhreshold`) and prototype IDs must not
be renamed for style reasons — saved maps and other prototypes reference them. Renaming requires
data migration and a YAMLLinter run (`dotnet run --project Content.YAMLLinter`).
