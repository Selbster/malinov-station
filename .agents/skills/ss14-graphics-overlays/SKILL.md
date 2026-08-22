---
name: ss14-graphics-overlays
description: An in-depth practical guide to the SS14 overlay architecture: OverlaySpace, lifecycle and system registration, communication with shaders, ScreenTexture, render targets, stencil composition, graphics primitives and render optimization.
---

# Overlays and graphics pipeline SS14

This skill only covers overlays: their lifecycle, render passes, resource caching, stencil and primitives :)
For the shader language and built-in functions see the sibling skill `ss14-graphics-shaders`.

## Source of truth

- Engine side: `Overlay`, `OverlayManager` and the drawing handles in Robust.Client graphics.
- Per-viewport resource pooling is a content-side helper (`OverlayResourceCache<T>`, defined in the content project's Graphics folder), not a RobustToolbox engine class.
- Cross-check any copied pattern against a fresh content overlay before reuse; mark unverifiable details `[unverified]`.

## How overlays are actually rendered

1. The manager stores one instance per overlay type and sorts them by `ZIndex`.
2. For each overlay, `BeforeDraw(...)` is called.
3. If `RequestScreenTexture == true`, the engine copies the current framebuffer to `ScreenTexture` (expensive).
4. If `OverwriteTargetFrameBuffer == true`, the target buffer is cleared before drawing.
5. `Draw(...)` runs in the corresponding space (`ScreenSpace`/`WorldSpace`, etc.).

Conclusion:
- Put early-exit logic into `BeforeDraw` so unnecessary screen copies never happen.
- `Draw` must stay short and deterministic: reading ECS is fine, mutating game state from it is not.

## Registration lifecycle

Client systems register overlays on `Initialize` and remove them on `Shutdown`/state exit:

```csharp
public override void Initialize()
{
    base.Initialize();
    _overlayMan.AddOverlay(new ExampleOverlay());   // one instance per overlay type
}

public override void Shutdown()
{
    _overlayMan.RemoveOverlay<ExampleOverlay>();
    base.Shutdown();
}
// Verify: OverlayManager.AddOverlay / RemoveOverlay<T>; e.g. PlanetLightSystem registers its overlay stack on init.
```

An overlay can also override `FrameUpdate(FrameEventArgs)` for per-frame state that does not depend on drawing.

## Select OverlaySpace

- `ScreenSpace`: UI-like effects on top of the world.
- `ScreenSpaceBelowWorld`: UI effects under the world.
- `WorldSpace`: effects over the world, often fullscreen by `WorldBounds`.
- `WorldSpaceBelowFOV`: effects under the final FOV.
- `WorldSpaceEntities`: effects on the same layer as entities.
- `BeforeLighting`: special passes before applying the light buffer.

## Basic shader-overlay template

```csharp
public sealed class ExampleOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Early exit: do not render (and do not copy the screen) without required state.
        return _effectStrength > 0f && args.Viewport.Eye == _playerEye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("Strength", _effectStrength);

        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldBounds, Color.White); // fullscreen pass
        args.WorldHandle.UseShader(null); // be sure to reset shader-state
    }
}
// Verify: Overlay.cs — RequestScreenTexture/ScreenTexture/OverwriteTargetFrameBuffer/BeforeDraw; pattern matches GasTileHeatBlurOverlay.
```

## Multi-pass via render target (blur/compose)

```csharp
protected override bool BeforeDraw(in OverlayDrawArgs args)
{
    var size = (Vector2i) (args.Viewport.Size * _downscale);
    var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());

    // Create/recreate RTs only when the viewport size changes.
    if (res.PassA == null || res.PassA.Size != size)
    {
        res.PassA?.Dispose();
        res.PassB?.Dispose();
        res.PassA = _clyde.CreateRenderTarget(size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb));
        res.PassB = _clyde.CreateRenderTarget(size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb));
    }

    return true;
}

protected override void Draw(in OverlayDrawArgs args)
{
    if (ScreenTexture == null)
        return;

    var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());
    var bounds = new Box2(Vector2.Zero, res.PassA!.Size);

    // Pass 1
    args.WorldHandle.RenderInRenderTarget(res.PassA, () =>
    {
        _blurX.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        args.WorldHandle.UseShader(_blurX);
        args.WorldHandle.DrawRect(bounds, Color.White);
    }, Color.Transparent);

    // Pass 2
    args.WorldHandle.RenderInRenderTarget(res.PassB!, () =>
    {
        _blurY.SetParameter("SCREEN_TEXTURE", res.PassA.Texture);
        args.WorldHandle.UseShader(_blurY);
        args.WorldHandle.DrawRect(bounds, Color.White);
    }, Color.Transparent);

    // Composite
    _final.SetParameter("SCREEN_TEXTURE", ScreenTexture);
    _final.SetParameter("BLURRED_TEXTURE", res.PassB.Texture);
    args.WorldHandle.UseShader(_final);
    args.WorldHandle.DrawRect(args.WorldBounds, Color.White);
    args.WorldHandle.UseShader(null);
}
// Verify: GasTileHeatBlurOverlay.cs — same RT-cache + RenderInRenderTarget flow; CachedResources holds IDisposable RT handles.
```

## Stencil composition: mask + rendering

```csharp
// 1) Write the mask into an intermediate RT.
handle.RenderInRenderTarget(stencilRt, () =>
{
    handle.SetTransform(localMatrix);
    handle.DrawRect(maskRegion, Color.White); // area where the effect should take effect
}, Color.Transparent);

// 2) Reset transform, then apply the stencil-mask shader to stamp the RT into the main target's stencil buffer.
handle.SetTransform(Matrix3x2.Identity);
handle.UseShader(_proto.Index(stencilMaskShader).Instance());
handle.DrawTextureRect(stencilRt.Texture, worldBounds);

// 3) Reset again, then draw the final layer only where the stencil test allows.
handle.SetTransform(Matrix3x2.Identity);
handle.UseShader(_proto.Index(stencilDrawShader).Instance());
handle.DrawTextureRect(effectRt.Texture, worldBounds);
handle.UseShader(null);
// Verify: StencilOverlay.RestrictedRange.cs — identical StencilMask -> DrawTextureRect(rt.Texture) -> StencilDraw sequence with identity transform between passes.
```

## Primitives for visualization and tactics

```csharp
// Direction line.
args.WorldHandle.DrawLine(start, end, Color.Aqua);

// Circle at destination.
args.WorldHandle.DrawCircle(end, 0.4f, Color.Red, false);
// Verify: DrawingHandleWorld — DrawLine/DrawCircle overloads exist for world-space drawing.
```

Use this for:
- tactical indicators;
- debugging vectors;
- guides/zone boundaries.

## Patterns 😎

- Check `args.Viewport.Eye` against the local player's eye before expensive rendering.
- Keep the expensive condition in `BeforeDraw`, not in the middle of `Draw`.
- Cache per-viewport resources via content's `OverlayResourceCache<T>`; it is disposable, so release it in `DisposeBehavior`.
- Recreate render targets only on resize or format change.
- Always reset graphical state after use: `UseShader(null)` and, when transforms were changed, `SetTransform(Matrix3x2.Identity)`.
- Control the order of effects through `ZIndex`, not through "random" registration order.

## Anti-patterns ❌

- Request `ScreenTexture` if the shader doesn't read it.
- Create a render target every frame without cache.
- Leave shader/transform active after `Draw` and break subsequent overlay passes.
- Rely on equal `ZIndex` for strict stacking order — ties draw in random order.
- Mutate game state from inside `Draw`; overlays are read-only views of the world.
- Use `WorldAABB` for a fullscreen effect where you need a correctly rotated `WorldBounds`.

## Mini checklist before PR

- The overlay has a clear single `OverlaySpace`.
- Expensive checks are moved to `BeforeDraw`.
- The overlay is registered on Initialize and removed on Shutdown.
- All temporary RTs are released via `Dispose`/`DisposeBehavior`.
- The render state after `Draw` returns to neutral.
- Behavior tested at several viewport scales/zoom values.

## SS14 dimension checklist

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating (`InPrediction` / `IsPredictionEnabled`) | N/A | rendering runs outside prediction |
| Server / Client / Shared split + `[NetworkedComponent]` | N/A | overlays are Robust.Client/content-client only |
| Event and `UpdatesBefore`/`UpdatesAfter` ordering | ☑ | registration happens in system `Initialize`/`Shutdown`; order vs other overlays set by `ZIndex` |
| Component lifecycle (Add/Remove/Initialize/Shutdown) | ☑ | dispose per-viewport caches in `DisposeBehavior` |
| Hot path and allocations | ☑ | recreate RTs only on resize; reuse cached instances; no per-frame allocations in `Draw` |
| PVS / network visibility of client-side logic | N/A | rendering is never networked |

## Extension/change rule

Add new pass types or caching techniques under their existing section; whole new subsystems (shader language details) belong to `ss14-graphics-shaders`. Keep this file under 300 lines; move long catalogs into `references/`.

Verified against code state: 2026-08-23
