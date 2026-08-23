# AnimationPlayerSystem — extended example catalog

Companion of `SKILL.md`. Every snippet carries a verification recipe; confirm against
RobustToolbox/Content HEAD before reuse.

## Example 4: combined flick + light pulse on one timeline

```csharp
private static readonly Animation ProximityAnim = new()
{
    Length = TimeSpan.FromSeconds(0.6f),
    AnimationTracks =
    {
        new AnimationTrackSpriteFlick
        {
            LayerKey = ProximityTriggerVisualLayers.Base,
            KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame("flashing", 0f) }
        },
        new AnimationTrackComponentProperty
        {
            ComponentType = typeof(PointLightComponent),
            Property = nameof(PointLightComponent.AnimatedRadius),
            InterpolationMode = AnimationInterpolationMode.Nearest,
            KeyFrames =
            {
                new AnimationTrackProperty.KeyFrame(0.1f, 0f),
                new AnimationTrackProperty.KeyFrame(3f, 0.1f),
                new AnimationTrackProperty.KeyFrame(0.1f, 0.5f)
            }
        }
    }
};
// Verify: Content.Client/Trigger/Systems/ProximityTriggerAnimationSystem.cs — grep AnimatedRadius
```

Note: `AnimatedRadius` is a client-side animation proxy property of `SharedPointLightComponent`;
animating it does not dirty networked light state.

## Example 5: manual loop via `AnimationCompletedEvent`

```csharp
private void OnAnimationCompleted(EntityUid uid, RotatingLightComponent comp, AnimationCompletedEvent args)
{
    if (args.Key != "rotating_light")
        return;

    // Replay only after natural completion; Finished == false means Stop() was called.
    if (!args.Finished)
        return;

    if (!TryComp<AnimationPlayerComponent>(uid, out var player))
        return;

    _anim.Play((uid, player), BuildRotation(comp.Speed), "rotating_light");
}
// Verify: AnimationPlayerSystem.cs — grep AnimationCompletedEvent / Finished
```

The handler fires during client `FrameUpdate`; keep it purely client-side and cheap.
Re-playing inside the handler is safe: completions are raised after the iteration over
playing animations finishes.

## Example 6: infinite flick with optional sound

```csharp
private Animation BuildPrimingAnimation(ResolvedSoundSpecifier? sound)
{
    var anim = new Animation
    {
        // MaxValue keeps the flick "held" until something stops it explicitly.
        Length = TimeSpan.MaxValue,
        AnimationTracks =
        {
            new AnimationTrackSpriteFlick
            {
                LayerKey = TriggerVisualLayers.Base,
                KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame("primed", 0f) }
            }
        }
    };

    if (sound != null)
    {
        anim.AnimationTracks.Add(new AnimationTrackPlaySound
        {
            KeyFrames = { new AnimationTrackPlaySound.KeyFrame(sound.Value, 0f) }
        });
    }

    return anim;
}
// Verify: Content.Client/Trigger/Systems/TimerTriggerVisualizerSystem.cs — grep TimeSpan.MaxValue
```

## Example 7: color flash helper

```csharp
private Animation BuildColorFlash(Color from, Color to, float seconds)
{
    return new Animation
    {
        Length = TimeSpan.FromSeconds(seconds),
        AnimationTracks =
        {
            new AnimationTrackComponentProperty
            {
                ComponentType = typeof(SpriteComponent),
                Property = nameof(SpriteComponent.Color),
                InterpolationMode = AnimationInterpolationMode.Linear,
                KeyFrames =
                {
                    new AnimationTrackProperty.KeyFrame(from, 0f),
                    new AnimationTrackProperty.KeyFrame(to, seconds)
                }
            }
        }
    };
}
// Verify: Robust.Client/Animations/AnimationTrackProperty.cs — grep InterpolateLinear (Color case)
```

`Color` interpolation is natively supported by `InterpolateLinear`.

## Example 8: driving animations from appearance data without spam

```csharp
private void UpdateTriggeredState(EntityUid uid, AnimationPlayerComponent player,
    ProximityTriggerVisuals state)
{
    switch (state)
    {
        case ProximityTriggerVisuals.Active:
            // Guard prevents restarting an already running loop on every appearance event.
            if (!_anim.HasRunningAnimation(uid, player, "proximity"))
                _anim.Play((uid, player), ProximityAnim, "proximity");
            break;
        case ProximityTriggerVisuals.Inactive:
        case ProximityTriggerVisuals.Off:
            _anim.Stop((uid, player), "proximity");
            break;
    }
}
// Verify: sibling skill ss14-graphics-generic-visualizer-appearance — AppearanceChangeEvent pipeline
```

Pattern: the visualizer/appearance side reports discrete states; the animation system owns the
timeline. Keep the boundary — do not put timelines into appearance payloads.
