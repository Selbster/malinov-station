namespace Content.Shared._MalinovStation.Surgery;

/// <summary>
/// What a <see cref="SurgeryStepPrototype"/> actually does when it completes successfully.
/// Incision/Retraction/ClampBleeders/Cauterize/Generic are all handled by the same code path
/// (sound/popup + optional bleed delta) - the distinct names exist for YAML/content readability.
/// </summary>
public enum SurgeryStepKind : byte
{
    /// <summary>
    /// No special effect beyond sounds/popups/bleed.
    /// </summary>
    Generic,

    /// <summary>
    /// Opens the patient up - typically raises bleeding.
    /// </summary>
    Incision,

    /// <summary>
    /// Holds an incision open.
    /// </summary>
    Retraction,

    /// <summary>
    /// Clamps bleeding vessels - typically lowers bleeding.
    /// </summary>
    ClampBleeders,

    /// <summary>
    /// Seals the incision closed.
    /// </summary>
    Cauterize,

    /// <summary>
    /// Removes the surgery's <see cref="SurgeryPrototype.TargetOrgan"/> organ from the patient into the surgeon's hand.
    /// </summary>
    ExtractOrgan,

    /// <summary>
    /// Inserts the organ held by the surgeon (matching the surgery's <see cref="SurgeryPrototype.TargetOrgan"/> category)
    /// into the patient.
    /// </summary>
    InsertOrgan,
}
