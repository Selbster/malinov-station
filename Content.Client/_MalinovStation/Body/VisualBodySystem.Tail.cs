using Content.Client._MalinovStation.Body;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;

namespace Content.Client.Body;

public sealed partial class VisualBodySystem
{
    // Connects organ marking layers to directional tail ordering.
    [Dependency] private DirectionalTailSystem _directionalTail = default!;

    private void TrackDirectionalTail(Entity<VisualOrganMarkingsComponent> ent, EntityUid target,
        MarkingPrototype marking, string layerKey)
    {
        if (marking.BodyPart != HumanoidVisualLayers.Tail || !ProtoMan.Index(ent.Comp.MarkingData.Group).DirectionalTail)
            return;

        _directionalTail.Track(target, layerKey);
    }
}
