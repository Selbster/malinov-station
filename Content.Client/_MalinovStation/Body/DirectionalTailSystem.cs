using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._MalinovStation.Body;

/// <summary>
/// Keeps tails below the body when facing the viewer and above it when facing away.
/// </summary>
public sealed partial class DirectionalTailSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IEyeManager _eye = default!;

    /// <summary>
    /// Tracks an existing marking layer and invalidates its cached order after a marking update.
    /// </summary>
    public void Track(EntityUid uid, string layerKey)
    {
        var directionalTail = EnsureComp<DirectionalTailComponent>(uid);
        if (!directionalTail.Layers.Contains(layerKey))
            directionalTail.Layers.Add(layerKey);
        directionalTail.InFront = null;
    }

    /// <summary>
    /// Stops tracking a layer before its removal; removes the tracking component when no layers remain.
    /// Safe to call for unrelated or already removed markings.
    /// </summary>
    public void Untrack(EntityUid uid, string layerKey)
    {
        if (!TryComp<DirectionalTailComponent>(uid, out var directionalTail))
            return;

        if (!directionalTail.Layers.Remove(layerKey))
            return;
        directionalTail.InFront = null;
        if (directionalTail.Layers.Count == 0)
            RemComp<DirectionalTailComponent>(uid);
    }

    /// <summary>
    /// Uses the same view rotation and direction override as SpriteView/RenderSprite.
    /// </summary>
    public void UpdateOrder(EntityUid uid, Angle worldRotation, Angle eyeRotation, Direction? direction = null)
    {
        if (!TryComp<DirectionalTailComponent>(uid, out var directionalTail) || !TryComp<SpriteComponent>(uid, out var sprite))
            return;

        UpdateOrder((uid, directionalTail, sprite), worldRotation, eyeRotation, direction);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var eyeRotation = _eye.CurrentEye.Rotation;
        var query = EntityQueryEnumerator<DirectionalTailComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var directionalTail, out var sprite, out var transform))
        {
            UpdateOrder((uid, directionalTail, sprite), _transform.GetWorldRotation(transform), eyeRotation,
                sprite.EnableDirectionOverride ? sprite.DirectionOverride : null);
        }
    }

    private void UpdateOrder(Entity<DirectionalTailComponent, SpriteComponent> ent, Angle worldRotation,
        Angle eyeRotation, Direction? direction = null)
    {
        var (uid, directionalTail, sprite) = ent;
        var facing = direction?.Convert(RsiDirectionType.Dir4) ?? SpriteComponent.Layer.GetDirection(
            RsiDirectionType.Dir4, (worldRotation + eyeRotation).Reduced().FlipPositive());
        var inFront = facing == RsiDirection.North;
        if (directionalTail.InFront == inFront)
            return;

        var anchor = inFront ? HumanoidVisualLayers.Tail : HumanoidVisualLayers.Chest;
        if (!_sprite.LayerMapTryGet((uid, sprite), anchor, out _, logMissing: true))
            return;

        var offset = 1;
        foreach (var key in directionalTail.Layers)
        {
            if (!_sprite.RemoveLayer((uid, sprite), key, out var layer))
                continue;

            var index = _sprite.LayerMapGet((uid, sprite), anchor);
            if (inFront)
                index += offset++;

            // Reuse the layer itself so animation timing, tint, visibility and shaders survive turns.
            _sprite.AddLayer((uid, sprite), layer, index);
            _sprite.LayerMapSet((uid, sprite), key, index);
        }
        directionalTail.InFront = inFront;
    }
}
