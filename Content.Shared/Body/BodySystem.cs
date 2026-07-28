using Content.Shared.DragDrop;
using Robust.Shared.Containers;

namespace Content.Shared.Body;

/// <summary>
/// System responsible for coordinating entities with <see cref="BodyComponent" /> and their entities with <see cref="OrganComponent" />.
/// This system is primarily responsible for event relaying and the relationships between a body and its organs.
/// It is not responsible for player-facing body features, e.g. "blood" or "breathing."
/// Such features should be implemented in systems relying on the various events raised by this class.
/// </summary>
/// <seealso cref="OrganGotInsertedEvent" />
/// <seealso cref="OrganGotRemovedEvent" />
/// <seealso cref="OrganInsertedIntoEvent" />
/// <seealso cref="OrganRemovedFromEvent" />
/// <seealso cref="BodyRelayedEvent{TEvent}" />
public sealed partial class BodySystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    [Dependency] private EntityQuery<BodyComponent> _bodyQuery = default!;
    [Dependency] private EntityQuery<OrganComponent> _organQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, ComponentInit>(OnBodyInit);
        SubscribeLocalEvent<BodyComponent, ComponentShutdown>(OnBodyShutdown);

        SubscribeLocalEvent<BodyComponent, CanDragEvent>(OnCanDrag);

        SubscribeLocalEvent<BodyComponent, EntInsertedIntoContainerMessage>(OnBodyEntInserted);
        SubscribeLocalEvent<BodyComponent, EntRemovedFromContainerMessage>(OnBodyEntRemoved);

        InitializeRelay();
        InitializeParts();
    }

    private void OnBodyInit(Entity<BodyComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Organs =
            _container.EnsureContainer<Container>(ent, BodyComponent.ContainerID);
    }

    private void OnBodyShutdown(Entity<BodyComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Organs is { } organs)
            _container.ShutdownContainer(organs);
    }

    private void OnBodyEntInserted(Entity<BodyComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != BodyComponent.ContainerID)
            return;

        // The inserted entity may be a part (e.g. an arm, possibly already carrying its own hand) as well
        // as an organ in its own right - both branches can fire for the same entity, not exclusively.
        if (_partQuery.TryComp(args.Entity, out var part))
        {
            if (part.Body != ent.Owner)
            {
                part.Body = ent.Owner;
                Dirty(args.Entity, part);
            }

            var partIntoEv = new PartInsertedIntoEvent(args.Entity);
            RaiseLocalEvent(ent.Owner, ref partIntoEv);

            var partGotEv = new PartGotInsertedEvent(ent.Owner);
            RaiseLocalEvent(args.Entity, ref partGotEv);

            foreach (var nested in part.Organs?.ContainedEntities ?? [])
            {
                if (_organQuery.TryComp(nested, out var nestedOrgan))
                    FireOrganInserted(ent, nested, nestedOrgan);
            }
        }

        if (!_organQuery.TryComp(args.Entity, out var organ))
            return;

        FireOrganInserted(ent, args.Entity, organ);
    }

    private void OnBodyEntRemoved(Entity<BodyComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != BodyComponent.ContainerID)
            return;

        if (_partQuery.TryComp(args.Entity, out var part))
        {
            if (part.Body != null)
            {
                part.Body = null;
                Dirty(args.Entity, part);
            }

            var partFromEv = new PartRemovedFromEvent(args.Entity);
            RaiseLocalEvent(ent.Owner, ref partFromEv);

            var partGotEv = new PartGotRemovedEvent(ent.Owner);
            RaiseLocalEvent(args.Entity, ref partGotEv);

            foreach (var nested in part.Organs?.ContainedEntities ?? [])
            {
                if (_organQuery.TryComp(nested, out var nestedOrgan))
                    FireOrganRemoved(ent, nested, nestedOrgan);
            }
        }

        if (!_organQuery.TryComp(args.Entity, out var organ))
            return;

        FireOrganRemoved(ent, args.Entity, organ);
    }

    /// <summary>Raises the organ-inserted events and updates <see cref="OrganComponent.Body"/>. Shared by
    /// organs inserted directly into a body and organs inserted into a part that's already part of a body.</summary>
    private void FireOrganInserted(Entity<BodyComponent> body, EntityUid organEntity, OrganComponent organ)
    {
        var bodyEv = new OrganInsertedIntoEvent(organEntity);
        RaiseLocalEvent(body.Owner, ref bodyEv);

        var organEv = new OrganGotInsertedEvent(body.Owner);
        RaiseLocalEvent(organEntity, ref organEv);

        if (organ.Body != body.Owner)
        {
            organ.Body = body.Owner;
            Dirty(organEntity, organ);
        }
    }

    /// <summary>Raises the organ-removed events and clears <see cref="OrganComponent.Body"/>. Shared by
    /// organs removed directly from a body and organs removed from a part that's already part of a body.</summary>
    private void FireOrganRemoved(Entity<BodyComponent> body, EntityUid organEntity, OrganComponent organ)
    {
        var bodyEv = new OrganRemovedFromEvent(organEntity);
        RaiseLocalEvent(body.Owner, ref bodyEv);

        var organEv = new OrganGotRemovedEvent(body.Owner);
        RaiseLocalEvent(organEntity, ref organEv);

        if (organ.Body == null)
            return;

        organ.Body = null;
        Dirty(organEntity, organ);
    }

    private void OnCanDrag(Entity<BodyComponent> ent, ref CanDragEvent args)
    {
        args.Handled = true;
    }
}
