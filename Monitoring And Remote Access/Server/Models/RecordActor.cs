using Shared.Contracts;

namespace Server.Models;

/// <summary>
/// The account types that can be recorded as having created or changed a row.
/// </summary>
/// <remarks>
/// The values match <see cref="RoleNames"/> so an audit entry, a workstation
/// status change and a created account all describe the same person the same
/// way. <see cref="System"/> has no role, because nobody is signed in when the
/// database seeds itself.
/// </remarks>
public static class ActorTypes
{
    public const string Admin = RoleNames.Admin;
    public const string Teacher = RoleNames.Teacher;
    public const string System = "System";
}

/// <summary>
/// Who did something, as the pair of columns the rows actually store.
/// </summary>
/// <remarks>
/// A foreign key cannot express this: the actor may be an administrator or a
/// teacher, and those live in different tables. The type says which table the
/// id belongs to, the same shape <c>ComputerStatusHistory</c> and
/// <c>AuditLog</c> already use. The database cannot enforce that the id exists,
/// which is the price of letting one column name three kinds of account.
/// </remarks>
public readonly record struct RecordActor(string Type, int? Id)
{
    /// <summary>Seeding and migrations, where no one is signed in.</summary>
    public static readonly RecordActor System = new(ActorTypes.System, null);

    public static RecordActor Admin(int? id) => new(ActorTypes.Admin, id);

    public static RecordActor Teacher(int? id) => new(ActorTypes.Teacher, id);
}
