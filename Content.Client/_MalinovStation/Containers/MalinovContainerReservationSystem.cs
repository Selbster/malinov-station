using Robust.Shared.Containers;

namespace Content.Client._MalinovStation.Containers;

/// <summary>
/// Keeps a single-item slot reserved for contents announced by the server but not yet visible locally.
/// </summary>
public sealed class MalinovContainerReservationSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
    }

    private void OnInsertAttempt(ContainerIsInsertingAttemptEvent args)
    {
        if (!args.AssumeEmpty && args.Container is ContainerSlot { ExpectedEntities.Count: > 0 })
            args.Cancel();
    }
}
