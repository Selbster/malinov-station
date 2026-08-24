---
name: ss14-localization-code
description: A guide to using localization in Space Station 14 C# code. Describes the ILocalizationManager, LocId, and proper dependency injection patterns.
---

# SS14 Localization in Code

This skill describes the rules for working with localization (`ILocalizationManager`) in the C# code of Space Station 14.

## 1. Getting localization: String vs LocId

### LocId (Localization Identifier)
In modern SS14 code, the `LocId` structure should be used to store locale keys instead of a pure `string`. It implicitly converts to `string`, and when used in `[DataField]` it is validated at prototype load time via `LocIdSerializer`.

```csharp
// ✅ Good: Using LocId in components
[RegisterComponent]
public sealed partial class ArmableComponent : Component
{
    [DataField]
    public LocId? ExamineTextArmed = "armable-examine-armed";
}

// ❌ Bad: Using string for localization keys
public string ExamineTextArmed;
```

**Note:** `LocId` stores the **key** of a localized string, not the rendered text. This also applies to examine: a `LocId` field in a component holds only the key, while the examine text itself is assembled into a `FormattedMessage` via `ExaminedEvent.PushMarkup` / `PushText` / `PushMessage` (see the example in "Code examples"). Storing examine message keys as `LocId` is a common pattern in Content.Shared: `ArmableComponent.ExamineTextArmed`, `StaminaResistanceComponent.Examine`, `FireProtectionComponent.ExamineMessage`, `AccessReaderComponent.ExaminationText`, and others.

### Formatting strings
To substitute variables in the FTL message, `(string key, object value)` tuples are used.

```csharp
// FTL:
// my-message = Hello, { $name }! You have { $count } coins.

// C#:
var msg = _loc.GetString("my-message", ("name", "Urist"), ("count", 10));
```

## 2. Using LocalizationManager

The only correct way to work with localization in systems (`EntitySystem`) and controllers is through Dependency Injection.

### ✅ Pattern: Dependency Injection

`EntitySystem` already has the built-in `[Dependency] protected ILocalizationManager Loc` field — this is the DI pattern itself, nothing extra needs to be declared:

```csharp
public sealed class MySystem : EntitySystem
{
    public void DoSomething()
    {
        // Loc — the built-in EntitySystem DI field (ILocalizationManager)
        var text = Loc.GetString("my-localization-key");
    }
}
```

For classes that are not systems (controllers, managers, and other services), inject the manager via `[Dependency]`:

```csharp
using Robust.Shared.Localization;

public sealed class MyNotSystem : SomeBaseClass
{
    [Dependency] private readonly ILocalizationManager _loc = default!;

    public void DoSomething()
    {
        var text = _loc.GetString("my-localization-key");
    }
}
```

### 🚫 Anti-pattern: Manual Dependency Injection into EntitySystem
If a class already inherits from `EntitySystem`, creating a custom `[Dependency] ILocalizationManager _loc` is redundant duplication of the built-in `Loc`. Remove such fields during refactoring.

### 🚫 Anti-pattern: Manual Resolve
Never use `IoCManager.Resolve<T>()` inside systems or methods where `[Dependency]` can be used. This violates the principle of inversion of control and complicates testing.

```csharp
// ❌ VERY BAD
public void BadMethod()
{
    var loc = IoCManager.Resolve<ILocalizationManager>(); // NO!
    loc.GetString("...");
}
```

### ⚠️ Static class Loc: important clarification
There is also a static class `Loc` (`Robust.Shared.Localization.Loc`) — a wrapper over `IoCManager.Resolve<ILocalizationManager>()`. Inside `EntitySystem`, the identifier `Loc` resolves to the **DI field**, not to the static class, so:

```csharp
// ✅ Okay (inside EntitySystem): Loc is a DI instance
var text = Loc.GetString("my-key");

// ❌ Redundant (inside a system): custom [Dependency] ILocalizationManager _loc
[Dependency] private readonly ILocalizationManager _loc = default!;
```

The static class `Loc` is **not** marked `[Obsolete]` (only `Loc.TryGetString` is obsolete). It is acceptable only where DI is unavailable: static utilities and extension methods without IoC. Where possible, prefer passing `ILocalizationManager` as a method argument.

### 🚫 Anti-pattern: String concatenation
Never concatenate localized strings with variables via `+` or `$` (interpolation).
Word order differs in different languages (SVO vs SOV). Fluent supports safe argument substitution.

```csharp
// ❌ BAD: Breaks the grammar of other languages
var text = "Player " + _loc.GetString("traitor-title") + " won!";

// ✅ GOOD: Passing arguments to FTL
// traitor-win-msg = Player { $role } wins!
var text = _loc.GetString("traitor-win-msg", ("role", roleName));
```

### GetString overloads and error behavior

`GetString` has overloads for 0, 1, 2, and `params` arguments. The tuple key name corresponds to the `$var` variable name in the FTL:

```csharp
// FTL: my-message = Hello, { $name }! You have { $count } coins.
var msg = _loc.GetString("my-message", ("name", "Urist"), ("count", 10));
```

Localization debugging:

1. Unknown key → `Warning` in the `loc` sawmill, the method returns the key itself as a string.
2. Formatting error (FTL syntax, missing `{$user}` argument) → `Error` in the `loc` sawmill, the method returns the key itself.
3. If a raw key like `foo-bar-baz` appears on screen — the key is missing from the locale, there is an FTL syntax error, or an argument was not passed.

## 3. Automatic entity localization

### 🚫 Anti-pattern: Manual GetString for entity names
Do not manually fetch an entity's name via `_loc.GetString("ent-...")` — `Name` and `Description` in `EntityPrototype` and the `MetaData` component are already localized by the engine.

### ✅ Pattern: Get localized name

```csharp
// Getting the localized name of an entity
var name = Identity.Name(uid, EntityManager); // Takes into account ID cards, masking, etc.
// OR (raw prototype name)
var protoName = prototype.Name; // Already localized
```

Internal mechanics: `EntityPrototype.Name`/`Description`/`EditorSuffix` go through `ILocalizationManager.GetEntityData(ID)`, which looks up the `ent-{ID}` key with `.desc`/`.suffix` attributes and falls back to the English `name`/`description`/`suffix` YAML fields. If the prototype defines `localizationId`, that key is used instead of `ent-{ID}`. The FTL format for entities is described in the `ss14-localization-strings` skill.

## 4. Grammatical attributes (Gender)

### 🚫 Anti-pattern: Passing a name string instead of EntityUid
When passing entities to localization messages, the engine automatically tries to determine the gender and name. For this to work correctly, pass the entity itself (`EntityUid`), and not just its name as a string.

```csharp
// FTL:
// emote-jump = { THE($entity) } jumps!

// C#:
// ✅ Good: Pass the EntityUid, the engine will find the gender and name
_loc.GetString("emote-jump", ("entity", uid));

// ❌ Bad: We just pass the name, the THE() and GENDER() functions will not work
_loc.GetString("emote-jump", ("entity", Name(uid)));
```

## Code examples

### Registering a component with LocId

```csharp
[RegisterComponent]
public sealed partial class ClumsyComponent : Component
{
    [DataField]
    public LocId GunFailedMessage = "clumsy-gun-fail-message"; // Default value
}
```

### Use in the system

```csharp
public sealed class ClumsySystem : EntitySystem
{
    // We do not import ILocalizationManager — it is built into EntitySystem as Loc
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private void OnGunShootFailed(Entity<ClumsyComponent> ent)
    {
        // Loc — the built-in EntitySystem DI field (ILocalizationManager)
        var msg = Loc.GetString(ent.Comp.GunFailedMessage);
        _popup.PopupEntity(msg, ent, ent);
    }
}
```

### ExaminedEvent: FormattedMessage, not LocId

```csharp
private void OnExamine(EntityUid uid, ArmableComponent comp, ExaminedEvent args)
{
    if (!args.IsInDetailsRange || !comp.ShowStatusOnExamination)
        return;

    // The examine text is assembled via PushMarkup/PushText/PushMessage,
    // not via a LocId "Message" field — ExaminedEvent has no such field.
    args.PushMarkup(Loc.GetString(comp.ExamineTextArmed, ("name", uid)));
}
```
