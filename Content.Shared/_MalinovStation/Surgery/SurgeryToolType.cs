namespace Content.Shared._MalinovStation.Surgery;

/// <summary>
/// The kind of surgical instrument a <see cref="SurgeryStepPrototype"/> requires to be performed.
/// </summary>
public enum SurgeryToolType : byte
{
    Scalpel,
    Retractor,
    Hemostat,
    Cautery,
    Saw,
    Drill,
}
