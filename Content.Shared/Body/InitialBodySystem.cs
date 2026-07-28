using System.Numerics;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Shared.Body;

public sealed partial class InitialBodySystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InitialBodyComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<InitialBodyComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<ContainerManagerComponent>(ent, out var containerComp))
            return;

        if (TerminatingOrDeleted(ent) || !Exists(ent))
            return;

        if (!_container.TryGetContainer(ent, BodyComponent.ContainerID, out var container, containerComp))
        {
            Log.Error($"Entity {ToPrettyString(ent)} with a {nameof(InitialBodyComponent)} is missing a container ({BodyComponent.ContainerID}).");
            return;
        }

        var xform = Transform(ent);
        var coords = new EntityCoordinates(ent, Vector2.Zero);

        foreach (var (category, proto) in ent.Comp.Organs)
        {
            // TODO: When e#6192 is merged replace this all with TrySpawnInContainer...
            var spawn = Spawn(proto, coords);

            if (!_container.Insert(spawn, container, containerXform: xform))
            {
                Log.Error($"Entity {ToPrettyString(ent)} with a {nameof(InitialBodyComponent)} failed to insert an entity: {ToPrettyString(spawn)}.\n");
                Del(spawn);
                continue;
            }

            if (!ent.Comp.PartOrgans.TryGetValue(category, out var childOrgans) || childOrgans.Count == 0)
                continue;

            if (!TryComp<BodyPartComponent>(spawn, out var part) || part.Organs is not { } partContainer)
            {
                Log.Error($"Entity {ToPrettyString(ent)} with a {nameof(InitialBodyComponent)} has partOrgans for category '{category}', but the spawned '{proto}' isn't a body part.");
                continue;
            }

            foreach (var childProto in childOrgans.Values)
            {
                var childSpawn = Spawn(childProto, coords);

                // No cached TransformComponent for the just-spawned part entity (unlike `xform`, which is
                // the body's) - let Insert resolve the part's own transform instead of reusing the body's.
                if (!_container.Insert(childSpawn, partContainer))
                {
                    Log.Error($"Entity {ToPrettyString(ent)} with a {nameof(InitialBodyComponent)} failed to insert a part-organ entity: {ToPrettyString(childSpawn)}.\n");
                    Del(childSpawn);
                }
            }
        }
    }
}
