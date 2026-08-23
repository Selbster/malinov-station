---
name: ss14-graphics-animation-player
description: A deep practical guide to entity animations using the AnimationPlayerSystem in SS14: lifecycle, API, track types, keyframes/interpolation/easing, completion events, production patterns and anti-patterns. Use it when a visual must change over time under local client control — tweens, RSI flicks, timed effects — or when choosing between animating and discrete GenericVisualizer mapping.
---

# Animations via AnimationPlayer in SS14

Scope: entity animations through `AnimationPlayerSystem` only. UI animations (`Control.PlayAnimation`) are out of
scope; discrete visual-state mapping belongs to `ss14-graphics-generic-visualizer-appearance`, low-level sprite
work to `ss14-graphics-sprite-system`, sound specifiers to `ss14-audio-system-api`.

## Reading order

1. This file top-to-bottom: runtime facts -> API -> track selection -> examples.
2. Extended catalog: `references/examples.md` (flick + light pulse, manual loops, sound tracks, color flash).

Source of truth: RobustToolbox client code wins over docs; verify every API against HEAD before reuse.

An animation is a local client-side timeline of an effect, not network business logic.

> **Single most important pattern**: guard every repeatable `Play` with `HasRunningAnimation(uid, key)` and branch
> on `args.Finished` in every `AnimationCompletedEvent` handler. Unguarded `Play` throws on a duplicate key;
> ignoring `Finished` restarts loops right after a forced stop.

## When to use

Use `AnimationPlayerSystem` when a visual must change **over time** under local client control:

- smooth property tweens (`SpriteComponent`, `TransformComponent`, `PointLightComponent`, ...);
- RSI state flicks on a layer along a timeline;
- synchronized "visual + sound" inside one timeline;
- completion/interruption handled through events.

Triggering condition: the effect has duration and phases.
Non-triggering condition: a discrete `data -> layer/state/color` mapping with no timeline — use `GenericVisualizer`
(sibling skill) instead of animating.

## Mental model

1. Build an `Animation` (`Length` + list of `AnimationTracks`).
2. Play it via `AnimationPlayerSystem.Play(...)` under a unique string `key`.
3. The system advances playback every frame and applies track values.
4. Natural completion raises `AnimationCompletedEvent` with `Finished = true`.
5. Manual `Stop(...)` raises the same event, but with `Finished = false`.

## Client/server boundary and runtime facts

- `AnimationPlayerComponent` exists only in Robust.Client; the server never registers it.
- `Play(uid, ...)` auto-adds `AnimationPlayerComponent` via `EnsureComp`;
  prefer passing `(uid, component)` explicitly when the component already exists.
- `FrameUpdate` skips paused entities: animations freeze while paused and resume on unpause.
- Debug builds log an error if the entity lacks the animated component (the animation aborts),
  and warn when an `[AutoNetworkedField]` property of a networked component is animated
  (every frame dirties that component -> network spam).

## API parsing `AnimationPlayerSystem`

### Launch

- `Play(EntityUid uid, Animation animation, string key)`
- `Play(Entity<AnimationPlayerComponent> ent, Animation animation, string key)` — preferred
- Obsolete `Play(EntityUid, AnimationPlayerComponent?, Animation, string)` — do not use in new code.

Practice: stable key constants; guard with `HasRunningAnimation` before replaying a possibly active key.

### Checking running

- `HasRunningAnimation(EntityUid uid, string key)`
- `HasRunningAnimation(EntityUid uid, AnimationPlayerComponent? component, string key)`
- `HasRunningAnimation(AnimationPlayerComponent component, string key)`

### Stop

- `Stop(Entity<AnimationPlayerComponent?> entity, string key)`
- `Stop(EntityUid uid, AnimationPlayerComponent? component, string key)`
- Obsolete `Stop(AnimationPlayerComponent, string)` — do not use in new code.

Stopping removes playback immediately and raises `AnimationCompletedEvent(Finished = false)`.

### Events

- `AnimationStartedEvent { Uid, AnimationPlayer, Key }`
- `AnimationCompletedEvent { Uid, AnimationPlayer, Key, Finished }`

Both are raised locally on the entity bus, client-side only
(start from `Play`, completion from `FrameUpdate`); subscribe from client systems.

// Verify: RobustToolbox/Robust.Client/GameObjects/EntitySystems/AnimationPlayerSystem.cs — grep Play( / HasRunningAnimation / Stop(

## Structure `Animation`

- `Length`: total duration; must cover the sum of all track keyframe deltas.
- `AnimationTracks`: several tracks run synchronously on one shared timeline.

Cache reusable animations in `static readonly` fields. Playback state lives in a separate
playback object created per play, so one cached instance can play concurrently on many entities.

## Track types and when to take which one

### `AnimationTrackComponentProperty`

Animates a component property by keyframes.
Fields: `ComponentType = typeof(...)`, `Property = nameof(...)`, `KeyFrames`,
optional `InterpolationMode` (default `Linear`).

Use for client-side properties: `SpriteComponent.Scale/Offset/Color/Rotation`,
`TransformComponent.LocalPosition`, `PointLightComponent.AnimatedRadius/AnimatedEnable/Rotation`, ...

Extension point: if the target component implements `IAnimationProperties`, values arrive through its
`SetAnimatableProperty(Property, value)` instead of reflection — useful for custom animatable state.

Never target properties marked `[AutoNetworkedField]` on networked components — see runtime facts above.

### `AnimationTrackSpriteFlick`

Plays an RSI state on one sprite layer over time, disabling auto-animation for that layer.
Fields: `LayerKey` (layer map key, usually a layer enum), `KeyFrames(StateId, delta)`.

### `AnimationTrackPlaySound`

Fires a resolved sound specifier at given keyframes; precise "visual + sound" sync in one timeline.

## KeyFrame and interpolation

`AnimationTrackProperty.KeyFrame(value, keyTime, easing?)`:

- `keyTime` is a **delta from the previous keyframe**, not an absolute timestamp;
- `easing` (`Func<float, float>`, e.g. `Easings.OutQuad`) reshapes interpolation toward this keyframe.

`AnimationInterpolationMode`: `Linear` (default) / `Cubic` / `Nearest` / `Previous`.
Linear/Cubic interpolate Vector2/3/4, float, double, int, Angle and Color;
unsupported types fall back to discrete steps (previous value).

## Practical examples

### Example 1: guarded launch by key

```csharp
private const string AnimKey = "rotating_light";

private void TryPlayRotation(EntityUid uid, AnimationPlayerComponent player, Animation anim)
{
    // Playback storage throws on a duplicate key, so gate replays.
    if (_anim.HasRunningAnimation(uid, player, AnimKey))
        return;

    _anim.Play((uid, player), anim, AnimKey);
}
// Verify: AnimationPlayerSystem.cs — grep HasRunningAnimation / Play(
```

### Example 2: restore visuals, then stop

```csharp
private void StopFallAnimation(EntityUid uid, AnimationPlayerComponent player,
    SpriteComponent sprite, Vector2 originalScale)
{
    // Restore base visuals BEFORE stopping so no further frame rewrites them.
    _sprite.SetScale((uid, sprite), originalScale);

    // Raises AnimationCompletedEvent with Finished = false.
    _anim.Stop((uid, player), "chasm_fall");
}
// Verify: AnimationPlayerSystem.cs — grep public void Stop(
```

### Example 3: property-track tween with easing

```csharp
private static Animation BuildPickupAnim(Vector2 from, Vector2 to, Color startColor)
{
    return new Animation
    {
        Length = TimeSpan.FromMilliseconds(175),
        AnimationTracks =
        {
            new AnimationTrackComponentProperty
            {
                ComponentType = typeof(TransformComponent),
                Property = nameof(TransformComponent.LocalPosition),
                InterpolationMode = AnimationInterpolationMode.Linear,
                KeyFrames =
                {
                    // keyTime - delta from the previous keyframe.
                    new AnimationTrackProperty.KeyFrame(from, 0f),
                    new AnimationTrackProperty.KeyFrame(to, 0.175f, Easings.OutQuad)
                }
            },
            new AnimationTrackComponentProperty
            {
                ComponentType = typeof(SpriteComponent),
                Property = nameof(SpriteComponent.Color),
                KeyFrames =
                {
                    new AnimationTrackProperty.KeyFrame(startColor, 0f),
                    new AnimationTrackProperty.KeyFrame(startColor.WithAlpha(0f), 0.175f, Easings.OutQuad)
                }
            }
        }
    };
}
// Verify: Content.Client/Animations/EntityPickupAnimationSystem.cs — grep AnimationTrackComponentProperty;
// easing samples live in melee swing effects (grep Easings.OutQuart there).
```

Extended catalog lives in `references/examples.md`: combined flick + light pulse, manual loop,
infinite flick + sound, color flash helper, driving animations from appearance data without spam.

## Patterns 🙂

1. Store animation keys as constants next to the consuming system — prevents typo drift between Play/Stop/listener.
2. Gate repeated `Play` with `HasRunningAnimation`; applies whenever the same key may fire twice (toggles, retriggers); unnecessary for strictly one-shot lifecycles.
3. Drive replays from `AnimationCompletedEvent` + `args.Finished == true` — prevents restart after forced stop.
4. Restore original visual values on `ComponentShutdown` or forced stop — prevents stuck scale/offset/color.
5. Combine tracks that need phase lock into one `Animation` instead of parallel plays.
6. Use `nameof(...)` for `Property` — survives refactors.
7. Cache built animations in `static readonly` fields — avoids rebuilding graphs per play.

## Anti-patterns ❌

- Playing with a key that is already active (exception, playback lost).
- Leaving visuals dirty after an interrupted animation.
- Animating non-existent components or `[AutoNetworkedField]` networked properties (abort / dirty-spam warning).
- Embedding server business logic into client animation systems.
- Rebuilding heavy `Animation` objects every frame without need.
- Confusing entity animation (`AnimationPlayerSystem`) with UI animation (`Control.PlayAnimation`).
- Subscribing to animation events from shared/server code — they fire only on the client.

## Checklist before change ✅

- Stable `AnimationKey` constant?
- Guard for repeated `Play`?
- `args.Finished` checked in the completion handler?
- Original visual parameters restored on shutdown/stop?
- Does `Length` cover all keyframe deltas?
- Correct `ComponentType` + `Property`, and the property is not networked?

## Common errors

- Treating `keyTime` as an absolute timestamp instead of a delta.
- Stopping an animation but not restoring the original `SpriteComponent` state.
- String literals for keys/properties instead of constants/`nameof`.
- Not filtering `args.Key` in completion handlers.
- Animating a component the entity lacks — debug error log and aborted animation.
- Ignoring `Finished = false`, which restarts a loop at the wrong moment.

## SS14 dimension checklist

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating | N/A | Client-only component; no predicted writes involved |
| Server / Client / Shared split | Covered | Component + system exist only in Robust.Client |
| Event / update ordering | N/A | Driven by client `FrameUpdate`; no fixed-step ordering constraints |
| Component lifecycle | Covered | Restore visuals on shutdown/stop; events raised during `FrameUpdate` |
| Hot path & allocations | Covered | Cache `static readonly Animation`; avoid `[AutoNetworkedField]` targets |
| PVS / network visibility | Covered | Out-of-PVS entities are detached and force-paused, so playback freezes; resumes automatically when metadata state reapplies |

## Extension and change rule

Add new recipes to `references/examples.md` rather than growing this file past its budget.
When upstream changes overloads, verify signatures against RobustToolbox HEAD and update the
API parsing block in place; keep obsolete overloads listed only as "do not use".

Verified against code state: 2026-08-23
