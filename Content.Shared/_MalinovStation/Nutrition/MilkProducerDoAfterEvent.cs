using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._MalinovStation.Nutrition;

/// <summary>
/// Completes a milk producer's transfer into the container held by the user.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class MilkProducerDoAfterEvent : SimpleDoAfterEvent;
