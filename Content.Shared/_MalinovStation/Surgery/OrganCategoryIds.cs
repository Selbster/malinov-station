namespace Content.Shared._MalinovStation.Surgery;

/// <summary>
/// Canonical <see cref="Content.Shared.Body.OrganCategoryPrototype"/> IDs that C# surgery logic (not just
/// YAML content) needs to name directly - e.g. to decide which extremity cascades with which limb, or how
/// many legs a body has left. Centralized so a rename only needs updating in one place instead of silently
/// desyncing scattered string literals.
/// </summary>
public static class OrganCategoryIds
{
    public const string Head = "Head";
    public const string Torso = "Torso";
    public const string Brain = "Brain";
    public const string Eyes = "Eyes";
    public const string Tongue = "Tongue";
    public const string Ears = "Ears";
    public const string Heart = "Heart";
    public const string Lungs = "Lungs";
    public const string Stomach = "Stomach";
    public const string Liver = "Liver";
    public const string Kidneys = "Kidneys";
    public const string Appendix = "Appendix";
    public const string ArmLeft = "ArmLeft";
    public const string ArmRight = "ArmRight";
    public const string HandLeft = "HandLeft";
    public const string HandRight = "HandRight";
    public const string LegLeft = "LegLeft";
    public const string LegRight = "LegRight";
    public const string FootLeft = "FootLeft";
    public const string FootRight = "FootRight";
}
