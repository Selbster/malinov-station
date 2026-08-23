#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.5: Russian is the default cognitive language. English technical identifiers (action names,
/// category names, JSON keys, prototype/component/entity ids) must stay exactly as spelled per spec section 6 -
/// these tests check both halves of that split: the instructional/narrative text is Russian, and the technical
/// tokens embedded in it survive untouched. Also covers the other half of "Russian by default" that isn't LLM
/// prompt text at all: the hardcoded memory content <see cref="AiTraceSystem"/> writes on the AI's behalf.
/// </summary>
[TestFixture]
public sealed class RussianLocalizationTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "RussianLocalizationTestMap";

    [TestPrototypes]
    private static readonly string RussianLocalizationTestMap = @$"
- type: gameMap
  id: {Map}
  mapName: {Map}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          availableJobs:
            {Passenger}: [ -1, -1 ]
";

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        Dirty = true,
    };

    private async Task<EntityUid> StartRoundAndGetStation(TestPair pair)
    {
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.GameMap, Map);
        var ticker = server.System<GameTicker>();

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        var station = server.EntMan.EntityQuery<StationDataComponent>().Select(x => x.Owner).FirstOrDefault();
        Assert.That(server.EntMan.EntityExists(station), "Expected a station to exist after round start.");
        return station;
    }

    /// <summary>Cyrillic occupies U+0400-U+04FF - a cheap, dependency-free way to assert "this text is
    /// actually Russian" without hardcoding a specific substring that would make the test brittle to minor
    /// wording changes.</summary>
    private static bool ContainsCyrillic(string text) => text.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    [Test]
    public void BuildIntentSystemPrompt_IsRussian_ButKeepsEnglishTechnicalTokensIntact()
    {
        var prompt = PromptBuilder.BuildIntentSystemPrompt(new[] { "Rest" }, new[] { AiActionCategories.Work });

        Assert.Multiple(() =>
        {
            Assert.That(ContainsCyrillic(prompt), Is.True, "The instructional prose should be Russian.");

            // JSON keys and category/action-name-shaped tokens are technical identifiers (spec section 6) -
            // they must survive verbatim, not get translated or transliterated.
            Assert.That(prompt, Does.Contain("\"desire\""));
            Assert.That(prompt, Does.Contain("\"category\""));
            Assert.That(prompt, Does.Contain(AiActionCategories.Work));
            Assert.That(prompt, Does.Contain(AiActionCategories.Movement));
            Assert.That(prompt, Does.Contain("Rest"));
        });
    }

    [Test]
    public void BuildActionSelectionSystemPrompt_IsRussian_ButKeepsActionNamesIntact()
    {
        IReadOnlyList<IAiAction> eligible = new List<IAiAction> { new ContinueActivityAction() };
        var prompt = PromptBuilder.BuildActionSelectionSystemPrompt(AiActionCategories.General, eligible);

        Assert.Multiple(() =>
        {
            Assert.That(ContainsCyrillic(prompt), Is.True, "The instructional prose should be Russian.");
            Assert.That(prompt, Does.Contain($"\"{ContinueActivityAction.ActionName}\""),
                "The action's technical name must stay in English, unmodified.");
            Assert.That(ContainsCyrillic(new ContinueActivityAction().Description), Is.True,
                "The action's human-readable description (embedded in this same prompt) should be Russian.");
        });
    }

    [Test]
    public void BuildDialogueSystemPrompt_IsRussian_ButKeepsTheJsonKeyIntact()
    {
        var prompt = PromptBuilder.BuildDialogueSystemPrompt();

        Assert.Multiple(() =>
        {
            Assert.That(ContainsCyrillic(prompt), Is.True, "The instructional prose should be Russian.");
            Assert.That(prompt, Does.Contain("\"line\""), "The output JSON key is a technical identifier and must stay in English.");
        });
    }

    [Test]
    public async Task AllRegisteredActions_HaveARussianDescription()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var registry = server.System<AiActionRegistrySystem>();
            foreach (var action in registry.AllActions)
            {
                Assert.That(ContainsCyrillic(action.Description), Is.True,
                    $"{action.Name}'s Description should be Russian (technical Name/registry key stays English).");
            }
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// AI Players 0.5: memory content written directly by game code (as opposed to LLM-generated free text)
    /// used to be hardcoded English C# literals (<see cref="AiTraceSystem.ActionFailed"/> et al.) - now Russian.
    /// Structured fields (Source, Importance, EmotionalWeight) stay technical/untouched, matching spec
    /// section 20's "structured metadata remains technical" split.
    /// </summary>
    [Test]
    public async Task ActionFailed_WritesARussianOutcomeMemory_WithUntranslatedStructuredFields()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            server.System<AiTraceSystem>().ActionFailed(aiPlayer, "SearchArea", "Kitchen inaccessible");

            var memory = server.EntMan.GetComponent<MemoryComponent>(aiPlayer).Memories.Last();
            Assert.Multiple(() =>
            {
                Assert.That(ContainsCyrillic(memory.Content), Is.True, "The memory's narrative content should be Russian.");
                Assert.That(memory.Source, Is.EqualTo("outcome"), "Structured metadata (Source) stays an untranslated technical tag.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
