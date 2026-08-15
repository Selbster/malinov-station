using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Shared.Random;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Generates a random personality for an AI player. There is no editing logic here beyond generation:
/// traits are read directly off <see cref="PersonalityComponent"/> by whichever system needs them.
/// </summary>
public sealed partial class PersonalitySystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>
    /// Rolls a fresh, random personality onto the entity and returns it.
    /// </summary>
    public PersonalityComponent RandomizePersonality(EntityUid uid)
    {
        var personality = EnsureComp<PersonalityComponent>(uid);

        personality.Sociability = RandomTrait();
        personality.Courage = RandomTrait();
        personality.Curiosity = RandomTrait();
        personality.Laziness = RandomTrait();
        personality.Greed = RandomTrait();
        personality.Aggression = RandomTrait();
        personality.Loyalty = RandomTrait();
        personality.RiskTolerance = RandomTrait();
        personality.AuthorityRespect = RandomTrait();
        personality.Professionalism = RandomTrait();
        personality.Empathy = RandomTrait();
        personality.Honesty = RandomTrait();
        personality.Impulsiveness = RandomTrait();

        return personality;
    }

    /// <summary>
    /// Averages two rolls so most AI players land near 0.5, with occasional extremes, instead of a flat
    /// distribution where "very greedy" and "perfectly average" are equally likely.
    /// </summary>
    private float RandomTrait()
    {
        return (_random.NextFloat() + _random.NextFloat()) / 2f;
    }
}
