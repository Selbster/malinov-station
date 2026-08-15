using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.AIPlayers.Prototypes;

/// <summary>
/// A job-specific "professional" goal (spec section 12), separate from the fixed routine/situational
/// vocabulary in <see cref="Components.AIGoals"/>. Data-driven so new job-specific work can be added without
/// touching C# (spec section 4's original Data/GoalPrototype.cs intent) - see
/// <see cref="Systems.GoalSystem"/> (candidate consideration, job-filtered) and
/// <see cref="Systems.LlmGatewaySystem"/> (LLM decision whitelist, alongside <see cref="Components.AIGoals.All"/>).
/// </summary>
[Prototype]
public sealed partial class AiProfessionalGoalPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The HTN compound that actually executes this goal once selected.</summary>
    [DataField(required: true)]
    public string HtnCompound = default!;

    /// <summary>Which jobs may ever consider pursuing this goal.</summary>
    [DataField(required: true)]
    public HashSet<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>Baseline priority before personality modifiers, comparable to the fixed goals' priorities.</summary>
    [DataField]
    public float BasePriority = 0.5f;
}

/// <summary>
/// Well-known professional goal IDs, mirroring <see cref="Components.AIGoals"/>'s constant-holder shape.
/// Unlike <see cref="Components.AIGoals.All"/>, these are validated against loaded
/// <see cref="AiProfessionalGoalPrototype"/>s rather than a fixed set - see callers for how the two
/// whitelists combine.
/// </summary>
public static class ProfessionalGoals
{
    /// <summary>Milestone 11: walk to a nearby damaged machine and fix it with a held welding tool.</summary>
    public const string RepairMachine = "RepairMachine";
}
