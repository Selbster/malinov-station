using Content.Client._MalinovStation.Body;
using Robust.Client.Graphics;

namespace Content.Client.Lobby.UI.ProfileEditorControls;

public sealed partial class ProfilePreviewSpriteView
{
    // Applies the preview's viewing direction before SpriteView renders the tail.
    protected override void Draw(IRenderHandle renderHandle)
    {
        if (Entity is { } entity)
        {
            var rotation = WorldRotation ?? EntMan.System<SharedTransformSystem>().GetWorldRotation(entity.Comp2);
            EntMan.System<DirectionalTailSystem>().UpdateOrder(entity.Owner, rotation, EyeRotation, OverrideDirection);
        }
        base.Draw(renderHandle);
    }
}
