namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Marker for an AI player running the Milestone 1 "Cognitive Prototype" loop (spawned via the
/// <c>aiplayer_spawn_cognitive</c> debug command, not automatically) - presence of this component is what
/// every cognitive-only code path (<see cref="Systems.EmotionSystem"/>, <see cref="Systems.BeliefSystem"/>,
/// <see cref="Systems.DesireSystem"/>, <see cref="IntentComponent"/>, the cognitive half of
/// <see cref="Systems.LlmGatewaySystem"/>, the outcome-memory writes in <see cref="Systems.AiTraceSystem"/>,
/// the Belief-vs-Memory branch in <see cref="Systems.SocialSystem.ShareRumor"/>) gates on. A legacy AI player
/// never has this component, so none of that new behaviour ever runs for it.
/// </summary>
[RegisterComponent]
public sealed partial class CognitiveModeComponent : Component
{
    /// <summary>How often (in seconds) a periodic "reflection" LLM decision is requested, on top of the
    /// existing event-driven triggers (see <see cref="Systems.LlmGatewaySystem"/>).</summary>
    [DataField]
    public float ReflectionCooldown = 45f;

    [ViewVariables]
    public float ReflectionAccumulator;
}
