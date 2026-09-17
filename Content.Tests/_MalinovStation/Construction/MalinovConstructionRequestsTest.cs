using System;
using Content.Server._MalinovStation.Construction;
using Moq;
using NUnit.Framework;
using Robust.Shared.Player;

namespace Content.Tests._MalinovStation.Construction;

[TestFixture]
public sealed class MalinovConstructionRequestsTest
{
    [Test]
    public void LastCompletedRequestReleasesSession()
    {
        var requests = new MalinovConstructionRequests();
        var session = Mock.Of<ICommonSession>();
        using var first = requests.TryBegin(session, 1);
        using var second = requests.TryBegin(session, 2);
        Assert.That(requests.TryBegin(session, 1), Is.Null, "Duplicate ACK must remain blocked while its request is active.");
        first.Dispose();
        Assert.That(requests.SessionCount, Is.EqualTo(1));
        Assert.That(second.IsActive, Is.True);
        second.Dispose();
        Assert.That(requests.SessionCount, Is.Zero);
    }

    [Test]
    public void DifferentSessionsMayUseTheSameAck()
    {
        var requests = new MalinovConstructionRequests();
        using var first = requests.TryBegin(Mock.Of<ICommonSession>(), 1);
        using var second = requests.TryBegin(Mock.Of<ICommonSession>(), 1);
        Assert.That(requests.SessionCount, Is.EqualTo(2));
        first.Dispose();
        Assert.That(second.IsActive, Is.True);
        second.Dispose();
        Assert.That(requests.SessionCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LateCompletionAfterDisconnectOrRoundCleanupDoesNotReleaseNewRequest(bool roundCleanup)
    {
        var requests = new MalinovConstructionRequests();
        var session = Mock.Of<ICommonSession>();
        using var oldRequest = requests.TryBegin(session, 1);
        if (roundCleanup)
            requests.Clear();
        else
            requests.Remove(session);

        Assert.That(requests.SessionCount, Is.Zero);
        Assert.That(oldRequest.IsActive, Is.False, "Cleared requests must not send late ACKs.");
        using var newRequest = requests.TryBegin(session, 1);
        oldRequest.Dispose();
        Assert.That(newRequest.IsActive, Is.True);
        Assert.That(requests.TryBegin(session, 1), Is.Null);
        newRequest.Dispose();
        Assert.That(requests.SessionCount, Is.Zero);
    }

    [Test]
    public void ExceptionalCompletionReleasesSessionAndAllowsRetry()
    {
        var requests = new MalinovConstructionRequests();
        var session = Mock.Of<ICommonSession>();
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var request = requests.TryBegin(session, 1);
            throw new InvalidOperationException("Construction failed after acquiring the ACK.");
        });
        Assert.That(requests.SessionCount, Is.Zero);
        using var retry = requests.TryBegin(session, 1);
        Assert.That(retry.IsActive, Is.True);
    }
}
