---
name: ss14-upstream-maintenance
description: Guide to working with the Malinov Station Space Station 14 fork with project-folder pattern (`_MalinovStation`) to minimize merge conflicts with the upstream (space-wizards/space-station-14). Use when modifying vanilla code or prototypes.
---

# 🛡️ Working with Upstream code and minimizing conflicts

This skill describes the standards and patterns adopted in the Malinov Station fork of Space Station
14 for working with code inherited from the upstream, using project-folder isolation (`_MalinovStation`).
**Main goal:** Maintain the ability to easily receive updates from the upstream (merge), minimizing manual edits in case of conflicts.

Before making changes, confirm you are in the Malinov Station codebase using the
`ss14-codebase-prefix-detection` rule: prefix `Malinov`, project folder `_MalinovStation`, single-line
marker `Malinov-Edit`.

Keep all new fork-owned code under `_MalinovStation`. Do not scatter it into ad-hoc top-level folders
because the touched behavior is vanilla or "doesn't feel fork-specific" — new code, prototypes and
assets go under `_MalinovStation` even when the hook or parent prototype lives in vanilla.

## ⚠️ Golden rule

> [!IMPORTANT]
> **Minimizing changes to vanilla files is MORE IMPORTANT than "pretty" architecture.**
> It's better to leave the "dirty" hack in one line of the vanilla file than to rewrite half the system, creating hell when merging.

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
> **Isolation principle:**
> Try to keep 99% of your unique code inside the `_MalinovStation` folder.
> Only **minimal** edits (hooks, events) that connect vanilla code to yours should remain in vanilla folders.
>
> The existing `Content.Shared/_MalinovStation/Audio/MalinovSoundCollectionHelper.cs` +
> `MalinovSoundCollections.cs` pair is a good reference: vanilla `ContentAudioSystem`/`NukeSystem` gain
> a two-line hook that calls into `_MalinovStation` code, instead of vanilla logic being rewritten in place.

## 🛠️ Modification of C# code

When modifying existing vanilla code (outside the project folder), use the following patterns.

### 1. Pattern `Edit Start` / `Edit End`

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

### 2. Pattern `Added Start` / `Added End`

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

### 3. Partial Classes

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
