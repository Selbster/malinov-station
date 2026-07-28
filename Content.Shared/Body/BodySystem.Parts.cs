using Robust.Shared.Containers;

namespace Content.Shared.Body;

public sealed partial class BodySystem
{
    [Dependency] private EntityQuery<BodyPartComponent> _partQuery = default!;

    private void InitializeParts()
    {
        SubscribeLocalEvent<BodyPartComponent, ComponentInit>(OnPartInit);
        SubscribeLocalEvent<BodyPartComponent, ComponentShutdown>(OnPartShutdown);

        SubscribeLocalEvent<BodyPartComponent, EntInsertedIntoContainerMessage>(OnPartEntInserted);
        SubscribeLocalEvent<BodyPartComponent, EntRemovedFromContainerMessage>(OnPartEntRemoved);
    }

    private void OnPartInit(Entity<BodyPartComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Organs =
            _container.EnsureContainer<Container>(ent, BodyPartComponent.ContainerID);
    }

    private void OnPartShutdown(Entity<BodyPartComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Organs is { } organs)
            _container.ShutdownContainer(organs);
    }

    /// <summary>
    /// Handles an organ being inserted directly into an already-attached part (e.g. installing a heart into
    /// a torso that's already part of a body). Assembling a part off-body and inserting the whole thing at
    /// once is handled by <see cref="OnBodyEntInserted"/>'s cascade instead.
    /// </summary>
    private void OnPartEntInserted(Entity<BodyPartComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != BodyPartComponent.ContainerID)
            return;

        if (ent.Comp.Body is not { } bodyId || !_bodyQuery.TryComp(bodyId, out var bodyComp))
            return;

        if (!_organQuery.TryComp(args.Entity, out var organ))
            return;

        FireOrganInserted((bodyId, bodyComp), args.Entity, organ);
    }

    private void OnPartEntRemoved(Entity<BodyPartComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != BodyPartComponent.ContainerID)
            return;

        if (ent.Comp.Body is not { } bodyId || !_bodyQuery.TryComp(bodyId, out var bodyComp))
            return;

        if (!_organQuery.TryComp(args.Entity, out var organ))
            return;

        FireOrganRemoved((bodyId, bodyComp), args.Entity, organ);
    }

    /// <summary>
    /// Every organ transitively reachable from this body - both direct children (including part entities
    /// themselves, e.g. the torso) and organs nested one level deeper inside a part's own container
    /// (e.g. the heart inside the torso). The single choke point every flat "all organs on this body"
    /// consumer should use instead of walking <see cref="BodyComponent.Organs"/> directly.
    /// </summary>
    public IEnumerable<EntityUid> EnumerateOrgans(Entity<BodyComponent?> body)
    {
        if (!_bodyQuery.Resolve(body, ref body.Comp, logMissing: false))
            yield break;

        foreach (var organ in body.Comp.Organs?.ContainedEntities ?? [])
        {
            yield return organ;

            if (_partQuery.TryComp(organ, out var part))
            {
                foreach (var nested in part.Organs?.ContainedEntities ?? [])
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Direct children of the body that are themselves parts (e.g. Torso/Head/Arm/Leg) - no flattening into
    /// their nested organs, unlike <see cref="EnumerateOrgans"/>.
    /// </summary>
    public IEnumerable<EntityUid> EnumerateParts(Entity<BodyComponent?> body)
    {
        if (!_bodyQuery.Resolve(body, ref body.Comp, logMissing: false))
            yield break;

        foreach (var organ in body.Comp.Organs?.ContainedEntities ?? [])
        {
            if (_partQuery.HasComp(organ))
                yield return organ;
        }
    }
}
