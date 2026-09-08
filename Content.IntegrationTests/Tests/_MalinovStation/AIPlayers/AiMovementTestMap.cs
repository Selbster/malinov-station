using Content.IntegrationTests.Pair;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

internal static class AiMovementTestMap
{
    /// <summary>Floors the northbound door-test corridor on empty.yml, whose surrounding tiles are space.</summary>
    public static void PaintNorthCorridor(TestPair pair, EntityUid actor)
    {
        var server = pair.Server;
        var xform = server.EntMan.GetComponent<TransformComponent>(actor);
        var gridUid = xform.GridUid!.Value;
        var grid = server.EntMan.GetComponent<MapGridComponent>(gridUid);
        grid.CanSplit = false;
        var maps = server.System<SharedMapSystem>();
        var origin = maps.CoordinatesToTile(gridUid, grid, xform.Coordinates);
        var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 7; y++)
            maps.SetTile(gridUid, grid, origin + new Vector2i(x, y), tile);
    }
}
