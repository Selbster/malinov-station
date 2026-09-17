using System.Numerics;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Robust.Client.GameStates;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._MalinovStation.Fluids;

[TestFixture]
public sealed class PuddleLifecycleTest
{
    [Test]
    public async Task PuddleLeavingPvsDoesNotBecomePredictedDeletion()
    {
        // This test flushes the client's entities just like shutdown, so the pair cannot be reused.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Destructive = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var transforms = server.System<SharedTransformSystem>();
        EntityUid observer = default;
        EntityUid puddle = default;

        await server.WaitPost(() =>
        {
            observer = server.EntMan.SpawnEntity(null, map.GridCoords);
            puddle = server.EntMan.SpawnEntity("PuddleBlood", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, observer);
            server.PlayerMan.JoinGame(pair.Player!);
            server.CfgMan.SetCVar(CVars.NetPVS, true);
        });
        await pair.RunTicksSync(10);
        await pair.SyncTicks();
        var cPuddle = pair.ToClientUid(puddle);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.Transform(cPuddle).Anchored, Is.True);
            Assert.That(client.Transform(cPuddle).ParentUid, Is.EqualTo(map.CGridUid));
            Assert.That(client.MetaData(cPuddle).Flags.HasFlag(MetaDataFlags.Detached), Is.False);
        });

        // Moving the observer leaves the authoritative puddle unchanged, but exercises the full PVS detach path.
        var farAway = new EntityCoordinates(map.MapUid, new Vector2(100, 100));
        await server.WaitPost(() => transforms.SetCoordinates(observer, farAway));
        await pair.RunTicksSync(10);
        await pair.SyncTicks();

        await client.WaitAssertion(() =>
        {
            Assert.That(client.MetaData(cPuddle).Flags.HasFlag(MetaDataFlags.Detached), Is.True);
            Assert.That(client.Transform(cPuddle).ParentUid, Is.EqualTo(EntityUid.Invalid));
            Assert.That(client.EntMan.IsQueuedForDeletion(cPuddle), Is.False);
        });

        // Further state updates and prediction rollbacks must leave the unseen puddle detached.
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.MetaData(cPuddle).Flags.HasFlag(MetaDataFlags.Detached), Is.True);
            Assert.That(client.Transform(cPuddle).ParentUid, Is.EqualTo(EntityUid.Invalid));
            Assert.That(client.EntMan.IsQueuedForDeletion(cPuddle), Is.False);
        });

        await server.WaitPost(() => transforms.SetCoordinates(observer, map.GridCoords));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.Transform(cPuddle).Anchored, Is.True);
            Assert.That(client.Transform(cPuddle).ParentUid, Is.EqualTo(map.CGridUid));
            Assert.That(client.MetaData(cPuddle).Flags.HasFlag(MetaDataFlags.Detached), Is.False);
            var lookup = client.System<EntityLookupSystem>();
            Assert.That(lookup.GetEntitiesInRange(map.MapCoords, 1f, LookupFlags.All), Does.Contain(cPuddle));
        });

        await client.WaitPost(() => client.EntMan.FlushEntities());
        await client.WaitAssertion(() => Assert.That(client.EntMan.EntityExists(cPuddle), Is.False));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovingLastEvaporatingReagentStopsEvaporation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var solutions = server.System<SharedSolutionContainerSystem>();
        EntityUid puddle = default;
        Entity<SolutionComponent> solution = default;
        var resolved = false;
        await server.WaitPost(() =>
        {
            puddle = server.EntMan.SpawnEntity("PuddleBlood", map.GridCoords);
            var puddleComp = server.EntMan.GetComponent<PuddleComponent>(puddle);
            resolved = solutions.TryGetSolution(puddle, puddleComp.SolutionName, out var solutionEnt);
            if (resolved)
                solution = solutionEnt!.Value;
        });
        await server.WaitAssertion(() => Assert.That(resolved, Is.True));
        await server.WaitPost(() => solutions.TryAddReagent(solution, "Water", FixedPoint2.New(10)));
        await server.WaitAssertion(() => Assert.That(server.EntMan.HasComponent<EvaporationComponent>(puddle), Is.True));

        await server.WaitPost(() => solutions.RemoveReagent(solution, "Water", FixedPoint2.New(10)));
        await server.WaitAssertion(() =>
        {
            Assert.That(solution.Comp.Solution.Volume, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(server.EntMan.HasComponent<EvaporationComponent>(puddle), Is.False);
        });

        await server.WaitPost(() => solutions.TryAddReagent(solution, "Water", FixedPoint2.New(10)));
        await server.WaitAssertion(() => Assert.That(server.EntMan.HasComponent<EvaporationComponent>(puddle), Is.True));
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task QueuedPuddleDeletionDiscardsStaleRequest(bool leavesPvs)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        EntityUid puddle = default;
        await server.WaitPost(() => puddle = server.EntMan.SpawnEntity("PuddleBlood", map.GridCoords));
        await pair.RunTicksSync(10);
        await pair.SyncTicks();
        var cPuddle = pair.ToClientUid(puddle);
        var solutions = client.System<SharedSolutionContainerSystem>();
        var gameStates = (ClientGameStateManager) client.Resolve<IClientGameStateManager>();
        Entity<SolutionComponent> solution = default;
        var resolved = false;
        await client.WaitPost(() =>
        {
            var puddleComp = client.EntMan.GetComponent<PuddleComponent>(cPuddle);
            resolved = solutions.TryGetSolution(cPuddle, puddleComp.SolutionName, out var solutionEnt);
            if (resolved)
                solution = solutionEnt!.Value;
        });
        await client.WaitAssertion(() => Assert.That(resolved, Is.True));

        await client.WaitPost(() =>
        {
            // Emptying the solution raises the real event that queues deletion in SharedPuddleSystem.
            solutions.RemoveAllSolution(solution);
            if (leavesPvs)
                gameStates.DetachImmediate([client.EntMan.GetNetEntity(cPuddle)]);
            else
                gameStates.ResetPredictedEntities();

            client.System<SharedPuddleSystem>().Update(0);
        });
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.IsQueuedForDeletion(cPuddle), Is.False);
            Assert.That(client.MetaData(cPuddle).Flags.HasFlag(MetaDataFlags.Detached), Is.EqualTo(leavesPvs));
            if (!leavesPvs)
                Assert.That(solution.Comp.Solution.Volume, Is.GreaterThan(FixedPoint2.Zero));
        });

        // The authoritative puddle was never emptied. Its next state must restore visibility without a stale delete.
        await server.WaitPost(() => server.EntMan.Dirty(puddle, server.Transform(puddle)));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.Transform(cPuddle).ParentUid, Is.EqualTo(map.CGridUid));
            Assert.That(client.MetaData(cPuddle).Flags.HasFlag(MetaDataFlags.Detached), Is.False);
            Assert.That(client.EntMan.IsQueuedForDeletion(cPuddle), Is.False);
            Assert.That(solution.Comp.Solution.Volume, Is.GreaterThan(FixedPoint2.Zero));
        });
        await pair.CleanReturnAsync();
    }
}
