using Robust.Shared.GameStates;

namespace Content.Shared._MalinovStation.Surgery.OrganEffects;

/// <summary>
/// Marks an organ as a tongue: removing it from a body mutes that body, using the existing
/// <see cref="Content.Shared.Speech.Muting.MutedComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(OrganEffectsSystem))]
public sealed partial class TongueOrganComponent : Component
{
    /// <summary>Whether the body was already mute for an unrelated reason before the tongue was removed,
    /// so re-implanting a tongue doesn't undo e.g. a "Muted" trait.</summary>
    [DataField]
    public bool WasMuted;
}
