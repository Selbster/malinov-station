using Content.Shared._MalinovStation.Surgery;

namespace Content.Client._MalinovStation.Surgery;

/// <summary>
/// Enables <see cref="SharedSurgerySystem"/>'s logic (IsLyingDown, TryValidateStep, ...) on the client,
/// so the surgery window can show live tool/posture feedback without waiting on a server round-trip.
/// </summary>
public sealed class SurgerySystem : SharedSurgerySystem
{
}
