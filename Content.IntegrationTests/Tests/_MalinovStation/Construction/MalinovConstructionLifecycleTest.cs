using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.Server._MalinovStation.Construction;
using Content.Server.Construction;
using Content.Shared.GameTicking;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._MalinovStation.Construction;

public sealed class MalinovConstructionLifecycleTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private MalinovConstructionRequests GetRequests()
    {
        var system = SEntMan.System<ConstructionSystem>();
        return (MalinovConstructionRequests) typeof(ConstructionSystem)
            .GetField("_beingBuilt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(system)!;
    }

    [Test]
    public async Task DisconnectReleasesPendingSession()
    {
        ICommonSession session = null;
        MalinovConstructionRequests.Request pending = null;
        await Server.WaitAssertion(() =>
        {
            session = ServerSession!;
            pending = GetRequests().TryBegin(session, 1);
            Assert.That(pending, Is.Not.Null);
        });

        await Client.WaitPost(() => Client.ResolveDependency<IClientNetManager>().ClientDisconnect("Construction cleanup test"));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(session.Status, Is.EqualTo(SessionStatus.Disconnected));
            Assert.That(GetRequests().SessionCount, Is.Zero);
            Assert.That(pending.IsActive, Is.False);
            Assert.DoesNotThrow(pending.Dispose);
        });
    }

    [Test]
    public async Task RoundCleanupInvalidatesPendingRequests()
    {
        await Server.WaitAssertion(() =>
        {
            var requests = GetRequests();
            using var pending = requests.TryBegin(ServerSession!, 1);
            Assert.That(pending, Is.Not.Null);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
            Assert.That(requests.SessionCount, Is.Zero);
            Assert.That(pending.IsActive, Is.False);
            using var nextRound = requests.TryBegin(ServerSession!, 1);
            pending.Dispose();
            Assert.That(nextRound.IsActive, Is.True);
        });
    }
}
