using Content.Shared.Body.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.Body.Components;

/// <summary>
/// Applied to a body with no brain organ. Mind-follows-brain (see <see cref="BrainSystem"/>) has already
/// moved any mind away to the extracted brain, so unlike a merely disconnected/asleep player this body can
/// never "wake back up" on its own - lets other systems (e.g. the SSD "Zz" indicator) tell the two apart.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(BrainSystem))]
public sealed partial class NoBrainComponent : Component;
