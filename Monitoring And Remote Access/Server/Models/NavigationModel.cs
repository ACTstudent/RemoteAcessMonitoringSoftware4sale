namespace Server.Models;

/// <summary>One link in the sidebar.</summary>
/// <param name="Text">The label a person reads.</param>
/// <param name="Icon">A Bootstrap icon class, without the leading <c>bi </c>.</param>
/// <param name="Action">The action this link points at.</param>
/// <param name="Controller">The controller this link points at. Matching on both is what keeps
/// <c>/Admin/Students</c> from lighting up the teacher's own Students link, which shares its action name.</param>
/// <param name="AlsoActiveOn">Further actions that should still show this link as the current page,
/// for detail pages that have no sidebar entry of their own.</param>
/// <param name="BadgeViewComponent">A view component rendered inside the link, for counts.</param>
public sealed record NavItem(
    string Text,
    string Icon,
    string Action,
    string Controller,
    IReadOnlyList<string>? AlsoActiveOn = null,
    string? BadgeViewComponent = null);

/// <summary>A group of links under an optional heading.</summary>
public sealed record NavSection(string? Label, IReadOnlyList<NavItem> Items);

/// <summary>
/// Everything that differs between the three portals.
///
/// The Admin, Teacher and Student portals each had their own full copy of the
/// page shell — sidebar, header, scripts, the lot — so a change to any of it had
/// to be made three times and stayed consistent only by care. What actually
/// varies is this record; the shell itself is now written once.
/// </summary>
public sealed record NavigationModel(
    string BrandText,
    string BrandSubtitle,
    string NavAriaLabel,
    string TitleSuffix,
    string DisplayName,
    string RoleBadge,
    string RoleBadgeCss,
    string AvatarIcon,
    IReadOnlyList<NavSection> Sections,
    string? TopbarPartial = null,
    string? ScriptPartial = null)
{
    /// <summary>
    /// Whether <paramref name="item"/> is the page being shown.
    ///
    /// Both halves of the route have to match. The old layouts compared the
    /// action alone, so opening <c>/Admin/Students</c> from the teacher portal
    /// highlighted the teacher's "Student Profiles" link, because both actions
    /// are called Students.
    /// </summary>
    public bool IsActive(NavItem item, string controller, string action) =>
        string.Equals(item.Controller, controller, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(item.Action, action, StringComparison.OrdinalIgnoreCase) ||
         (item.AlsoActiveOn?.Any(other => string.Equals(other, action, StringComparison.OrdinalIgnoreCase)) ?? false));
}
