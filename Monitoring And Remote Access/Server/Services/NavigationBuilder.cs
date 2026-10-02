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
///
/// A group holds the pages that do one kind of job, in the order the job is
/// done. People are accounts; classes are not people, so Classes stands on its
/// own. The records a teacher reads back - alerts, histories, the timeline -
/// are together rather than under "My Classes", and a person's own Account
/// Settings closes each menu, which the name in the page header also opens.
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
    private static readonly NavItem LabDashboard = new("Dashboard", "grid-fill", "Index", "Admin");

    /// <summary>
    /// The laboratory's computers, one page for both portals. A teacher used
    /// to have a "Workstations" page of their own beside this one - the same
    /// machines under a second name, listing only the ones in use.
    /// </summary>
    private static readonly NavItem LabComputers = new("Computers", "pc-display", "Computers", "Admin",
        AlsoActiveOn: new[] { "ComputerHistory" });

    private static readonly NavItem LabClasses = new("Classes", "folder-fill", "Classes", "Admin",
        AlsoActiveOn: new[] { "ClassDetails" });

    private static readonly NavItem Teachers = new("Teachers", "person-badge-fill", "Teachers", "Admin");
    private static readonly NavItem Students = new("Students", "mortarboard-fill", "Students", "Admin");

    private static NavSection PoliciesSection() => new("Policies", new[]
    {
        new NavItem("Restriction Rules", "slash-circle-fill", "Restrictions", "Admin"),
        new NavItem("Blacklist", "ban", "Blacklists", "Admin"),
        new NavItem("Whitelist", "check-circle-fill", "Whitelists", "Admin"),
        new NavItem("Session Rules", "hourglass-split", "SessionRules", "Admin")
    });

    private static NavItem AccountSettings(string controller) =>
        new("Account Settings", "person-gear", "Settings", controller);

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
        // Arranged as one menu rather than the teacher's sections with the
        // lab-wide ones appended: single links first, then the groups in the
        // order a lab period runs - control the lab, the teacher's own
        // classes, what was recorded - and then the lab-wide pages the
        // teacher shares with the administrator.
        Sections: new NavSection[]
        {
            // "My Classroom" rather than a bare "Dashboard": the lab-wide
            // overview beneath it is also a dashboard, and two links both
            // reading Dashboard told a teacher nothing about which one they
            // wanted.
            new NavSection(null, new[]
            {
                LabDashboard,
                new NavItem("My Classroom", "speedometer2", "Dashboard", "Teacher")
            }),
            new NavSection("Laboratory Control", new[]
            {
                new NavItem("Sessions", "play-circle-fill", "Sessions", "Teacher"),
                new NavItem("Live Monitoring", "camera-video-fill", "Monitoring", "Teacher"),
                LabComputers
            }),
            new NavSection("My Classes", new[]
            {
                // Its own icon: with the folder, this group and the lab-wide
                // Classes link were the same picture in the collapsed sidebar.
                new NavItem("My Class List", "collection-fill", "Classes", "Teacher",
                    AlsoActiveOn: new[] { "ClassDetails", "ClassAnalytics" }),
                new NavItem("My Students", "people-fill", "Students", "Teacher",
                    AlsoActiveOn: new[] { "StudentDetails" }),
                new NavItem("Class Restrictions", "slash-circle-fill", "Restrictions", "Teacher")
            }),
            new NavSection("Monitoring Records", new[]
            {
                new NavItem("Alerts", "bell-fill", "Alerts", "Teacher",
                    AlsoActiveOn: new[] { "AlertHistory" }, BadgeViewComponent: "OpenAlertCount"),
                new NavItem("Records", "journal-check", "Records", "Teacher"),
                new NavItem("Browser History", "browser-chrome", "BrowserMonitoringHistory", "Teacher"),
                new NavItem("Remote History", "terminal-fill", "RemoteHistory", "Teacher"),
                new NavItem("Timeline", "clock-history", "UnifiedTimeline", "Teacher",
                    AlsoActiveOn: new[] { "ActivityTimeline" }),
                new NavItem("Lab Utilization", "bar-chart-fill", "LabUtilization", "Teacher")
            }),
            new NavSection("People", new[] { Teachers, Students }),
            new NavSection(null, new[] { LabClasses }),
            PoliciesSection(),
            new NavSection(null, new[] { AccountSettings(Teacher) })
        })
    {
        SettingsController = Teacher
    };

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

        // Grouped by the job each page does rather than by who may open it.
        // "Administrator Only" used to hold six unrelated pages - accounts,
        // roles, the database, reports and two logs - because they shared an
        // audience, which told an administrator nothing about where to look.
        var sections = new List<NavSection>
        {
            new NavSection(null, new[] { LabDashboard }),
            new NavSection("People", new[]
            {
                Teachers,
                Students,
                new NavItem("Admin Accounts", "shield-lock-fill", "AdminAccounts", "Admin")
            }),
            new NavSection(null, new[] { LabClasses }),
            new NavSection("Laboratory", new[]
            {
                LabComputers,
                new NavItem("LAN Status", "router-fill", "LanConfig", "Admin"),
                new NavItem("Deployment", "box-seam-fill", "Index", "AdminDeployment")
            }),
            PoliciesSection(),
            new NavSection("Reports & Logs", new[]
            {
                new NavItem("Reports", "bar-chart-line-fill", "Reports", "Admin"),
                new NavItem("Audit Trail", "journal-text", "AuditLogs", "Admin"),
                new NavItem("System Logs", "bug-fill", "SystemLogs", "Admin")
            }),
            new NavSection("System", new[]
            {
                new NavItem("Roles", "key-fill", "Roles", "Admin"),
                new NavItem("Database", "database-gear", "Index", "AdminDatabase")
            }),
            new NavSection(null, new[] { AccountSettings(Admin) })
        };

        return new NavigationModel(
            BrandText: "CAMS Admin",
            BrandSubtitle: "Pardo Elementary School",
            NavAriaLabel: "Administrator navigation",
            TitleSuffix: "CAMS Admin",
            DisplayName: Name(context, "AdminName", "System Administrator"),
            RoleBadge: "Administrator",
            RoleBadgeCss: "bg-primary",
            AvatarIcon: "shield-check",
            Sections: sections)
        {
            SettingsController = Admin
        };
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
        })
    {
        SettingsController = Student
    };
}
