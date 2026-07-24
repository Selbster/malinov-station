using Content.Shared._MalinovStation.Surgery;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._MalinovStation.Surgery.Components;

/// <summary>
/// Tracks in-progress surgeries on a patient. Only holds a step cursor per active surgery - completion is
/// derived from organ presence instead of a separate "done" set, since <see cref="SurgeryPrototype"/> steps
/// are strictly linear.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurgeryProgressComponent : Component
{
    /// <summary>
    /// Maps an in-progress surgery to the index of its next incomplete step.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<SurgeryPrototype>, int> StepIndex = new();
}
