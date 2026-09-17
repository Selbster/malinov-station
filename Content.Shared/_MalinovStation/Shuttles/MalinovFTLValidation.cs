using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._MalinovStation.Shuttles;

/// <summary>
/// Validates FTL destinations before they enter transform and physics state.
/// </summary>
public static class MalinovFTLValidation
{
    public static bool IsFinite(Vector2 position, Angle angle)
    {
        return float.IsFinite(position.X) && float.IsFinite(position.Y) && double.IsFinite(angle.Theta);
    }

    /// <summary>
    /// Converts the requested center of mass position to the shuttle origin, rejecting arithmetic overflow.
    /// </summary>
    public static bool TryAdjustCoordinates(
        EntityCoordinates coordinates,
        Angle angle,
        Vector2 localCenter,
        out EntityCoordinates adjusted)
    {
        adjusted = default;
        if (!IsFinite(coordinates.Position, angle) || !IsFinite(localCenter, Angle.Zero))
            return false;

        var result = coordinates.Offset(angle.RotateVec(-localCenter));
        if (!IsFinite(result.Position, angle))
            return false;

        adjusted = result;
        return true;
    }
}
