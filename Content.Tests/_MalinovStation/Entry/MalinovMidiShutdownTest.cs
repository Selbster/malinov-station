using Content.Client._MalinovStation.Entry;
using Moq;
using NUnit.Framework;
using Robust.Client.Audio.Midi;

namespace Content.Tests._MalinovStation.Entry;

[TestFixture]
public sealed class MalinovMidiShutdownTest
{
    [Test]
    public void ClosesActivePlayersAndLeavesRendererLifetimeToTheEngine()
    {
        var fileStatus = MidiRendererStatus.File;
        var inputStatus = MidiRendererStatus.Input;
        var file = new Mock<IMidiRenderer>(MockBehavior.Strict);
        file.SetupGet(renderer => renderer.Disposed).Returns(false);
        file.SetupGet(renderer => renderer.Status).Returns(() => fileStatus);
        file.Setup(renderer => renderer.CloseMidi()).Returns(() =>
        {
            fileStatus = MidiRendererStatus.None;
            return true;
        });

        var input = new Mock<IMidiRenderer>(MockBehavior.Strict);
        input.SetupGet(renderer => renderer.Disposed).Returns(false);
        input.SetupGet(renderer => renderer.Status).Returns(() => inputStatus);
        input.Setup(renderer => renderer.CloseInput()).Returns(() =>
        {
            inputStatus = MidiRendererStatus.None;
            return true;
        });

        var idle = new Mock<IMidiRenderer>(MockBehavior.Strict);
        idle.SetupGet(renderer => renderer.Disposed).Returns(false);
        idle.SetupGet(renderer => renderer.Status).Returns(MidiRendererStatus.None);

        var disposed = new Mock<IMidiRenderer>(MockBehavior.Strict);
        disposed.SetupGet(renderer => renderer.Disposed).Returns(true);

        var manager = new Mock<IMidiManager>(MockBehavior.Strict);
        manager.SetupGet(value => value.Renderers)
            .Returns(new[] { file.Object, input.Object, idle.Object, disposed.Object });

        MalinovMidiShutdown.ClosePlayers(manager.Object);
        MalinovMidiShutdown.ClosePlayers(manager.Object);

        Assert.Multiple(() =>
        {
            Assert.That(fileStatus, Is.EqualTo(MidiRendererStatus.None));
            Assert.That(inputStatus, Is.EqualTo(MidiRendererStatus.None));
            file.Verify(renderer => renderer.CloseMidi(), Times.Once);
            input.Verify(renderer => renderer.CloseInput(), Times.Once);
            file.Verify(renderer => renderer.Dispose(), Times.Never);
            input.Verify(renderer => renderer.Dispose(), Times.Never);
            manager.VerifyGet(value => value.IsAvailable, Times.Never);
        });
    }
}
