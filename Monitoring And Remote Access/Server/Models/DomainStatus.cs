namespace Server.Models;

/// <summary>
/// Whether a record is in use, retired, or filed away. Stored in
/// <c>Teacher.Status</c>, <c>Student.Status</c> and <c>Class.Status</c>, all of
/// which default to Active.
/// </summary>
public static class RecordStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Archived = "Archived";
}

/// <summary>
/// Where a workstation is in its own lifecycle, stored in
/// <c>Computer.Status</c>. Deliberately separate from <see cref="RecordStatus"/>
/// even though both spell "Archived" the same way: a workstation being archived
/// and a teacher being archived are different events with different rules, and
/// one shared constant would invite code that treats them as interchangeable.
/// The plan asks for consolidation only where meanings match, and here they do
/// not.
/// </summary>
public static class WorkstationStatus
{
    public const string Available = "Available";

    /// <summary>Note the space. This value is in the database; do not tidy it.</summary>
    public const string InUse = "In Use";

    public const string Assigned = "Assigned";
    public const string Maintenance = "Maintenance";
    public const string Archived = "Archived";
}
