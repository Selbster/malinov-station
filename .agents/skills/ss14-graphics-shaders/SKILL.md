---
name: ss14-graphics-shaders
description: An in-depth practical guide to SS14 and SWSL shaders: syntax, presets, built-in variables/functions, parameters, prototype YAML (kind/params/stencil), debugging and architectural solutions. Use it for tasks about shader prototypes, uniforms, light_mode/blend_mode, stencil setup, compatibility and GPU effects.
---

# SS14 shaders (SWSL)

This skill covers only the shader part: language, runtime, built-in functions, parameters and debugging :)
If the task is about the lifecycle of overlays, `OverlaySpace`, render targets and primitives, use the sibling skill `ss14-graphics-overlays`.

## Source of truth

- Engine code wins over docs: parsing lives in the client shader layer (`ShaderParser`, `ShaderPrototype`), vertex templates and the helper library live next to Clyde (`base-default.vert/.frag`, `base-raw.vert/.frag`, `z-library.glsl`).
- Content `.swsl` files are living usage examples; cross-check any borrowed fragment against a fresh one before reuse.
- Facts that cannot be verified against the fork's current code must be marked `[unverified]`, never invented.

## Short decision tree

1. Do you only need blending/lighting/stencil mode without custom math?
- Yes: select `kind: canvas`.
- No: choose `kind: source`.

2. Need a custom vertex (deformation, separate varyings)?
- Yes: add `vertex()`.
- No: `fragment()` is enough.

3. Need full control over vertex transformation without auto-lighting?
- Yes: `preset raw`.
- No: `preset default`.

## Execution model

1. The shader prototype defines `kind`, optional render state and default `params`.
2. SWSL is parsed into `uniform`/`varying`/`const`/functions + include dependencies.
3. The engine wraps the code in a `default` or `raw` template and adds a common library of functions.
4. During rendering, `ShaderInstance` receives parameter values from C# and is applied to the draw-call.

## SWSL language: what is supported

- Top level directives: `light_mode`, `blend_mode`, `preset`.
- Declarations: `uniform`, `varying`, `const`.
- Functions: regular helper functions + special entrypoint functions `vertex()` and `fragment()`.
- Function parameters: `in`, `out`, `inout` are supported.
- Preprocessor: `#include`, `#ifdef`, `#ifndef`, `#else`, `#endif`.
- `discard` works inside `fragment()`: internal stencil shaders rely on it to punch holes into the stencil buffer.

Critical:
- For numeric types (`float`, `int`, `vec*`, `mat*`) set the qualifier (`lowp`/`mediump`/`highp`); without it behavior is inconsistent between GPUs.
- Arrays are not supported for all types. Safe zone: `float[]`, `vec2[]`, `vec4[]`, `bool[]` (matching the array `SetParameter` overloads).

## Shader prototype YAML

A `type: shader` prototype selects how code is loaded and carries render state:

```yaml
- type: shader
  id: DisplacedStencilDraw
  kind: source            # source = parse a .swsl file; canvas = built-in default-sprite template
  path: "/Textures/Shaders/displacement.swsl"
  stencil:                # prototype-level stencil test/write state
    ref: 1                # reference value compared/written by the stencil test
    op: Keep              # what happens to the stencil value when the fragment passes
    func: NotEqual        # fragment passes only where stencil != ref
  params:                 # default uniform values for this instance
    displacementSize: 127
```
<!-- Verify: Resources/Prototypes/Shaders/displacement.yml; ShaderPrototype.cs AfterDeserialization -->

- `params` keys must exist as uniforms in the parsed source; unknown names log an error and are skipped.
- `light_mode` / `blend_mode` are declared inside the `.swsl`; `stencil` lives only in the prototype YAML.

## Built-in variables and functions

Available common uniforms:
- `TIME`, `SCREEN_PIXEL_SIZE`, `TEXTURE`, `TEXTURE_PIXEL_SIZE`, `projectionMatrix`, `viewMatrix`

Frequently used functions:
- `zTexture(uv)` and `zTextureSpec(tex, uv)` for correct sampling.
- `zFromSrgb(col)` / `zToSrgb(col)` for color space conversions.
- `zGrayscale(...)`, `zGrayscale_BT709(...)`, `zGrayscale_BT601(...)`.
- `zRandom(...)`, `zNoise(...)`, `zFBM(...)`.
- `zCircleGradient(...)`.
- `zClydeShadowDepthPack(...)` / `zClydeShadowDepthUnpack(...)`.

Critical about `zAdjustResult(col)`: both fragment templates already finish with `gl_FragColor = zAdjustResult(COLOR ...)`. Never call it manually on your final output color — double application corrupts colors. Use `zFromSrgb`/`zToSrgb` only for intermediate math.

Additionally in `preset raw`:
- `apply_mvp(vertex)` and `pixel_snap(vertex)` for the vertex stage.

## Example A: basic fullscreen fragment

```glsl
uniform sampler2D SCREEN_TEXTURE;

void fragment() {
    // Take the current screen honoring the engine's color-space rules.
    highp vec4 src = zTextureSpec(SCREEN_TEXTURE, UV);

    // Convert to grayscale with the engine function.
    highp float gray = zGrayscale(src.rgb);

    // Return the result with the original alpha.
    COLOR = vec4(vec3(gray), src.a);
}
// Verify: Resources/Textures/Shaders/greyscale_fullscreen.swsl — identical logic
```

## Example B: vertex deformation via varying

```glsl
uniform sampler2D displacementMap;
uniform highp float displacementSize;
uniform highp vec4 displacementUV;

varying highp vec2 displacementUVOut;

void vertex() {
    // Pass UVs for the displacement map from the vertex to the fragment stage.
    displacementUVOut = mix(displacementUV.xy, displacementUV.zw, tCoord2);
}

void fragment() {
    // Read the displacement map and shift main-texture sampling.
    highp vec4 disp = texture2D(displacementMap, displacementUVOut);
    highp vec2 offset = (disp.xy - vec2(128.0 / 255.0)) / (1.0 - 128.0 / 255.0);
    COLOR = zTexture(UV + offset * TEXTURE_PIXEL_SIZE * displacementSize * vec2(1.0, -1.0));
    COLOR.a *= disp.a; // The displacement map alpha acts like a mask.
}
// Verify: Resources/Textures/Shaders/displacement.swsl — grep displacementUVOut
```

## Example C: working correctly with ShaderInstance in C#

```csharp
// Take a unique instance to safely change uniforms.
var shader = proto.Index(shaderId).InstanceUnique();

// Set effect parameters as needed.
shader.SetParameter("Strength", strength);
shader.SetParameter("SCREEN_TEXTURE", screenTexture);

handle.UseShader(shader);
handle.DrawRect(bounds, Color.White); // A real draw-call where the shader applies.
handle.UseShader(null);               // Explicit state reset.
// Verify: ShaderPrototype.cs — InstanceUnique() is mutable; shared Instance() throws on SetParameter
```

## Patterns 🙂

- Use `InstanceUnique()` for mutable parameters; reserve shared `Instance()` for parameterless use (e.g., stencil pass shaders).
- Make heavy parameters (`strength`, `phase`, `count`) external uniforms, not baked-in `const`.
- For noise/iterations use lower precision where visually acceptable.
- Early-exit pixels that should not render: branch cheaply and output alpha 0, or use `discard`.
- Prefer engine helpers (`zTexture*`, `zGrayscale`, `zCircleGradient`) over reinventing them.
- For iterative debugging reload shaders via the built-in client console command instead of restarting the game.

## Anti-patterns ❌

- Mutating parameters of a shared `Instance()` — runtime exception.
- Trusting deprecated doc variable names instead of current templates.
- Expecting arbitrary types to support uniform arrays.
- Writing shaders without precision qualifiers and assuming "the same everywhere".
- Calling `zAdjustResult` on your final COLOR — templates already apply it, so colors break.
- Copying old examples blindly without checking their date and known issues.

## Mini checklist before use

- Correct `kind` (`canvas`/`source`) selected.
- Correct `preset` (`default`/`raw`) for the task.
- All important numeric types carry a precision qualifier.
- Runtime-changing parameters are supplied via `SetParameter` on a unique instance.
- Prototype-level state (`stencil`, `params`) matches what the shader expects.
- Fallback behavior exists for weak/problematic GPU configurations.

## SS14 dimension checklist

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating (`InPrediction` / `IsPredictionEnabled`) | N/A | client-only rendering path |
| Server / Client / Shared split + `[NetworkedComponent]` | N/A | `.swsl` sources and parser live in Robust.Client/Resources only |
| Event and `UpdatesBefore`/`UpdatesAfter` ordering | N/A | no entity-system hooks involved |
| Component lifecycle (Add/Remove/Initialize/Shutdown) | ☑ | owner code disposes `InstanceUnique()` results it creates |
| Hot path and allocations | ☑ | cache unique instances; call `SetParameter` only when a value actually changes |
| PVS / network visibility of client-side logic | N/A | rendering is never networked |

## Extension/change rule

Add new language/runtime facts under the existing sections; a whole new subsystem (post-processing chains, lighting internals) belongs to `ss14-graphics-overlays` or its own skill. Keep this file under 300 lines; move long catalogs into `references/`.

Verified against code state: 2026-08-23
