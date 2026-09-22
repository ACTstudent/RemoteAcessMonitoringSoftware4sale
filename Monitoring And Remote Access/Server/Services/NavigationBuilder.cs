using Microsoft.AspNetCore.Http;
using Server.Models;
using Shared.Contracts;

namespace Server.Services;

/// <summary>
/// The sidebar for each portal, in one place.
///
/// These lists used to live as markup inside three layout files. Holding them
/// as data means the shell can be written once, and it makes the link set for a
/// role something you can read in twenty lines instead of reconstructing from
/// Razor conditionals.
/// </summary>
public static class NavigationBuilder
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Student = "Student";

    public static NavigationModel Build(string variant, HttpContext context) => variant switch
    {
        Admin => BuildAdmin(context),
        Student => BuildStudent(context),
        _ => BuildTeacher(context)
    };

    private static string Name(HttpContext context, string key, string fallback) =>
        context.Session.GetString(key) is { Length: > 0 } value ? value : fallback;

    /// <summary>
    /// The lab-wide pages, declared once.
    ///
    /// Both portals reach these: an administrator owns them, and a teacher is
    /// admitted to them by <c>[TeacherSharedAction]</c>. They used to be written
    /// out twice, and the two copies had drifted - the same page carried a
    /// different label and a different icon depending on which sidebar you were
    /// looking at. <c>Admin/Index</c> was "Global Dashboard &amp; Sessions" to an
    /// administrator and "Dashboard &amp; All Sessions" to a teacher;
    /// <c>Admin/Classes</c> was "Classes, Class Details &amp; Import" to one and
    /// "Classes, Student Profiles &amp; Import" to the other; the blacklist and
    /// whitelist gained and lost the word "Directory". A page is one thing, so
    /// it gets one name and one icon wherever it is listed.
    /// </summary>
    private static IEnumerable<NavSection> GlobalSections() => new[]
    {
        new NavSection("Overview", new[]
        {
            new NavItem("Dashboard", "grid-fill", "Index", "Admin")
        }),
        new NavSection("People", new[]
        {
            new NavItem("Teachers", "person-badge-fill", "Teachers", "Admin"),
            new NavItem("Students", "mortarboard-fill", "Students", "Admin"),
            new NavItem("Classes", "folder-fill", "Classes", "Admin",
                AlsoActiveOn: new[] { "ClassDetails" })
        }),
        new NavSection("Laboratory", new[]
        {
            new NavItem("Computers", "pc-display", "Computers", "Admin",
                AlsoActiveOn: new[] { "ComputerHistory" })
        }),
        new NavSection("Policies", new[]
        {
            new NavItem("Restriction Rules", "slash-circle-fill", "Restrictions", "Admin"),
            new NavItem("Blacklist", "ban", "Blacklists", "Admin"),
            new NavItem("Whitelist", "check-circle-fill", "Whitelists", "Admin"),
            new NavItem("Session Rules", "hourglass-split", "SessionRules", "Admin")
        })
    };

    // ---------- Teacher ----------

    private static NavigationModel BuildTeacher(HttpContext context) => new(
        BrandText: "CAMS Teacher",
        BrandSubtitle: "Pardo Elementary School",
        NavAriaLabel: "Teacher navigation",
        TitleSuffix: "CAMS Teacher",
        DisplayName: Name(context, "TeacherName", "Teacher"),
        RoleBadge: "Instructor",
        RoleBadgeCss: "bg-success",
        AvatarIcon: "person-badge",
        ScriptPartial: "_TeacherAlertBadgeScript",
        Sections: new NavSection[]
        {
            // "My Classroom" rather than a bare "Dashboard": the lab-wide
            // overview further down is also a dashboard, and two links a few
            // rows apart both reading Dashboard told a teacher nothing about
            // which one they wanted.
            new NavSection(null, new[]
            {
                new NavItem("My Classroom", "speedometer2", "Dashboard", "Teacher")
            }),
            new NavSection("Laboratory Control", new[]
            {
                new NavItem("Sessions", "play-circle-fill", "Sessions", "Teacher"),
                new NavItem("Live Monitoring", "camera-video-fill", "Monitoring", "Teacher"),
                new NavItem("Remote History", "terminal-fill", "RemoteHistory", "Teacher"),
                new NavItem("Workstations", "pc-display", "Computers", "Teacher")
            }),
            new NavSection("My Classes", new[]
            {
                new NavItem("My Class List", "folder-fill", "Classes", "Teacher",
                    AlsoActiveOn: new[] { "ClassDetails", "ClassAnalytics" }),
                new NavItem("My Students", "people-fill", "Students", "Teacher",
                    AlsoActiveOn: new[] { "StudentDetails" }),
                new NavItem("Class Restrictions", "slash-circle-fill", "Restrictions", "Teacher"),
                new NavItem("Records", "journal-check", "Records", "Teacher"),
                new NavItem("Lab Utilization", "bar-chart-fill", "LabUtilization", "Teacher"),
                new NavItem("Timeline", "clock-history", "UnifiedTimeline", "Teacher",
                    AlsoActiveOn: new[] { "ActivityTimeline" }),
                new NavItem("Browser History", "browser-chrome", "BrowserMonitoringHistory", "Teacher"),
                new NavItem("Alerts", "bell-fill", "Alerts", "Teacher",
                    AlsoActiveOn: new[] { "AlertHistory" }, BadgeViewComponent: "OpenAlertCount"),
                new NavItem("Settings", "person-gear", "Settings", "Teacher")
            })
        }.Concat(GlobalSections()).ToArray());

    // ---------- Admin ----------
    //
    // A teacher reaches this portal too, for the lab-wide operations they share.
    // When they do, they keep their own menu: one list covering both their
    // classroom pages and the global ones, rather than a second menu with a
    // "back to my portal" link. A teacher moving between a class roster and the
    // global roster is doing one job, and should not have to notice that the
    // two pages belong to different controllers.
    //
    // An administrator still gets the administrator menu, which carries the
    // admin-only links a teacher would be refused.

    private static NavigationModel BuildAdmin(HttpContext context)
    {
        var isTeacherActor = context.User.IsInRole(RoleNames.Teacher) && !context.User.IsInRole(RoleNames.Admin);
        if (isTeacherActor)
        {
            return BuildTeacher(context);
        }

        var sections = new List<NavSection>(GlobalSections());

        sections.Add(new NavSection("Administrator Only", new[]
        {
            new NavItem("Admin Accounts", "person-gear", "Settings", "Admin"),
            new NavItem("Roles", "key-fill", "Roles", "Admin"),
            new NavItem("LAN Status", "router-fill", "LanConfig", "Admin"),
            new NavItem("Deployment", "box-seam-fill", "Index", "AdminDeployment"),
            new NavItem("Database", "database-gear", "Index", "AdminDatabase"),
            new NavItem("Reports", "bar-chart-line-fill", "Reports", "Admin"),
            new NavItem("Audit Trail", "journal-text", "AuditLogs", "Admin"),
            new NavItem("System Logs", "bug-fill", "SystemLogs", "Admin")
        }));

        return new NavigationModel(
            BrandText: "CAMS Admin",
            BrandSubtitle: "Pardo Elementary School",
            NavAriaLabel: "Administrator navigation",
            TitleSuffix: "CAMS Admin",
            DisplayName: Name(context, "AdminName", "System Administrator"),
            RoleBadge: "Administrator",
            RoleBadgeCss: "bg-primary",
            AvatarIcon: "shield-check",
            Sections: sections);
    }

    // ---------- Student ----------

    private static NavigationModel BuildStudent(HttpContext context) => new(
        BrandText: "CAMS Student",
        BrandSubtitle: "Pardo Elementary School",
        NavAriaLabel: "Student navigation",
        TitleSuffix: "CAMS Student",
        DisplayName: Name(context, "FullName", "Student"),
        RoleBadge: "Student",
        RoleBadgeCss: "bg-info text-dark",
        AvatarIcon: "mortarboard-fill",
        TopbarPartial: "_StudentTopbar",
        ScriptPartial: "_StudentSessionScript",
        Sections: new[]
        {
            new NavSection(null, new[]
            {
                new NavItem("My Session", "info-circle-fill", "Index", "Student"),
                new NavItem("Alerts", "bell-fill", "Alerts", "Student"),
                new NavItem("Settings", "gear-fill", "Settings", "Student")
            })
        });
}
