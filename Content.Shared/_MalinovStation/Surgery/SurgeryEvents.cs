using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._MalinovStation.Surgery;

[Serializable, NetSerializable]
public enum SurgeryUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum SurgeryStepStatus : byte
{
    /// <summary>Not the current step of its surgery yet - shown greyed out.</summary>
    Locked,

    /// <summary>The current step, but can't be performed right now (missing tool, patient stood up, etc).</summary>
    Blocked,

    /// <summary>The current step and can be performed right now.</summary>
    Available,

    /// <summary>Already performed.</summary>
    Completed,
}

/// <summary>
/// Client-facing, pre-resolved display state for a single step, as shown in the surgery window.
/// </summary>
[Serializable, NetSerializable]
public sealed class SurgeryStepDisplay
{
    public readonly ProtoId<SurgeryStepPrototype> Step;
    public readonly SurgeryStepStatus Status;

    /// <summary>Loc key describing why this step is currently blocked, if <see cref="Status"/> is Blocked.</summary>
    public readonly string? BlockedReason;

    public SurgeryStepDisplay(ProtoId<SurgeryStepPrototype> step, SurgeryStepStatus status, string? blockedReason)
    {
        Step = step;
        Status = status;
        BlockedReason = blockedReason;
    }
}

/// <summary>
/// Client-facing, pre-resolved display state for a single available surgery and its steps.
/// </summary>
[Serializable, NetSerializable]
public sealed class SurgeryDisplay
{
    public readonly ProtoId<SurgeryPrototype> Surgery;
    public readonly List<SurgeryStepDisplay> Steps;

    public SurgeryDisplay(ProtoId<SurgeryPrototype> surgery, List<SurgeryStepDisplay> steps)
    {
        Surgery = surgery;
        Steps = steps;
    }
}

[Serializable, NetSerializable]
public sealed class SurgeryBuiState : BoundUserInterfaceState
{
    public required List<SurgeryDisplay> Surgeries { get; init; }
}

[Serializable, NetSerializable]
public sealed class SurgeryStepChosenBuiMsg : BoundUserInterfaceMessage
{
    public required ProtoId<SurgeryPrototype> Surgery { get; init; }
    public required ProtoId<SurgeryStepPrototype> Step { get; init; }
}

/// <summary>
/// Raised as the DoAfter event for performing a single surgery step.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SurgeryStepDoAfterEvent : DoAfterEvent
{
    public readonly ProtoId<SurgeryPrototype> Surgery;
    public readonly ProtoId<SurgeryStepPrototype> Step;

    /// <summary>Chance in [0,1] of success, resolved once at DoAfter-start time from tool/table stats.</summary>
    public readonly float SuccessRate;

    /// <summary>
    /// The surgery's step cursor at the moment this DoAfter started. Someone else may have advanced (or
    /// otherwise changed) it by the time this DoAfter finishes - e.g. a second surgeon completing the same
    /// step concurrently - so the step-completion handler re-checks this against the live cursor before
    /// applying any effect, instead of trusting that finishing always means "the current step".
    /// </summary>
    public readonly int Cursor;

    public SurgeryStepDoAfterEvent(ProtoId<SurgeryPrototype> surgery, ProtoId<SurgeryStepPrototype> step, float successRate, int cursor)
    {
        Surgery = surgery;
        Step = step;
        SuccessRate = successRate;
        Cursor = cursor;
    }

    public override DoAfterEvent Clone() => this;
}
