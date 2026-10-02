namespace Server.Models;

/// <summary>
/// What a signed-in administrator or teacher may change about their own
/// account. An administrator has one name; a teacher has a first and a last
/// name, an email address and a contact number.
/// </summary>
public sealed class AccountProfileInput
{
    public string? FullName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? ContactNumber { get; set; }
}

/// <summary>
/// The Account Settings page: the account's own details and its password, the
/// same page for both portals.
///
/// The administrator had no such page. Changing their password sat at the foot
/// of the administrator accounts list, and a teacher's page changed a password
/// and nothing else, although the teacher list sends a teacher here to "edit
/// your own account".
/// </summary>
public sealed class AccountSettingsViewModel
{
    /// <summary>The portal the page belongs to, which is also the controller its forms post to.</summary>
    public bool IsTeacher { get; init; }

    public string RoleLabel => IsTeacher ? "Teacher" : "Administrator";

    public string Controller => IsTeacher ? "Teacher" : "Admin";

    public AccountProfileInput Profile { get; init; } = new();

    /// <summary>The password form, kept so a rejected change shows again with its messages.</summary>
    public PasswordChangeInput Password { get; init; } = new();
}
