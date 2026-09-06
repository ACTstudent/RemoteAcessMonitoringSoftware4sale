using Microsoft.AspNetCore.Http;
using Server.Models;

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
        Sections: new[]
        {
            new NavSection(null, new[]
            {
                new NavItem("Dashboard", "speedometer2", "Dashboard", "Teacher")
            }),
            new NavSection("Laboratory Control", new[]
            {
                new NavItem("Session Management", "play-circle-fill", "Sessions", "Teacher"),
                new NavItem("Live Monitoring", "camera-video-fill", "Monitoring", "Teacher"),
                new NavItem("Remote-Control History", "terminal-fill", "RemoteHistory", "Teacher"),
                new NavItem("Workstations", "pc-display", "Computers", "Teacher")
            }),
            new NavSection("Classroom Management", new[]
            {
                new NavItem("Class Management", "folder-fill", "Classes", "Teacher",
                    AlsoActiveOn: new[] { "ClassDetails", "ClassAnalytics" }),
                new NavItem("Student Profiles", "people-fill", "Students", "Teacher",
                    AlsoActiveOn: new[] { "StudentDetails" }),
                new NavItem("Access Restrictions", "slash-circle-fill", "Restrictions", "Teacher"),
                new NavItem("Classroom Records", "journal-check", "Records", "Teacher"),
                new NavItem("Lab Utilization", "bar-chart-fill", "LabUtilization", "Teacher"),
                new NavItem("Unified Timeline", "clock-history", "UnifiedTimeline", "Teacher",
                    AlsoActiveOn: new[] { "ActivityTimeline" }),
                new NavItem("Browser Monitoring", "browser-chrome", "BrowserMonitoringHistory", "Teacher"),
                new NavItem("Monitoring Alerts", "bell-fill", "Alerts", "Teacher",
                    AlsoActiveOn: new[] { "AlertHistory" }, BadgeViewComponent: "OpenAlertCount"),
                new NavItem("Account Settings", "person-gear", "Settings", "Teacher")
            }),
            new NavSection("Global Operations", new[]
            {
                new NavItem("Dashboard & All Sessions", "globe2", "Index", "Admin"),
                new NavItem("Teachers", "person-badge-fill", "Teachers", "Admin"),
                new NavItem("Students", "mortarboard-fill", "Students", "Admin"),
                new NavItem("Classes, Student Profiles & Import", "folder-symlink-fill", "Classes", "Admin",
                    AlsoActiveOn: new[] { "ClassDetails" }),
                new NavItem("Computers, History & Mapping", "pc-display-horizontal", "Computers", "Admin",
                    AlsoActiveOn: new[] { "ComputerHistory" }),
                new NavItem("Rules & Categories", "shield-lock-fill", "Restrictions", "Admin"),
                new NavItem("Blacklists", "ban", "Blacklists", "Admin"),
                new NavItem("Whitelists", "check-circle-fill", "Whitelists", "Admin"),
                new NavItem("Session Rules", "hourglass-split", "SessionRules", "Admin")
            })
        });

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
        var isTeacherActor = context.User.IsInRole("Teacher") && !context.User.IsInRole("Admin");
        if (isTeacherActor)
        {
            return BuildTeacher(context);
        }

        var sections = new List<NavSection>();

        sections.Add(new NavSection("Global Operations", new[]
        {
            new NavItem("Global Dashboard & Sessions", "grid-fill", "Index", "Admin")
        }));
        sections.Add(new NavSection("Global People & Student Profiles", new[]
        {
            new NavItem("Teachers", "person-badge-fill", "Teachers", "Admin"),
            new NavItem("Students", "mortarboard-fill", "Students", "Admin"),
            new NavItem("Classes, Class Details & Import", "folder-fill", "Classes", "Admin",
                AlsoActiveOn: new[] { "ClassDetails" })
        }));
        sections.Add(new NavSection("Global Computers", new[]
        {
            new NavItem("Computers, History & Mapping", "pc-display", "Computers", "Admin",
                AlsoActiveOn: new[] { "ComputerHistory" })
        }));
        sections.Add(new NavSection("Global Restrictions", new[]
        {
            new NavItem("Rules & Categories", "slash-circle-fill", "Restrictions", "Admin"),
            new NavItem("Blacklist Directory", "ban", "Blacklists", "Admin"),
            new NavItem("Whitelist Directory", "check-circle-fill", "Whitelists", "Admin"),
            new NavItem("Session Rules", "hourglass-split", "SessionRules", "Admin")
        }));

        sections.Add(new NavSection("Administrator Only", new[]
        {
            new NavItem("Admin Accounts & Lockouts", "person-gear", "Settings", "Admin"),
            new NavItem("Roles & Permissions", "key-fill", "Roles", "Admin"),
            new NavItem("LAN Status", "router-fill", "LanConfig", "Admin"),
            new NavItem("Deployment Hub", "box-seam-fill", "Index", "AdminDeployment"),
            new NavItem("Database Maintenance", "database-gear", "Index", "AdminDatabase"),
            new NavItem("System Reports", "bar-chart-line-fill", "Reports", "Admin"),
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
                new NavItem("Session Information", "info-circle-fill", "Index", "Student"),
                new NavItem("Alert Center", "bell-fill", "Alerts", "Student"),
                new NavItem("Account Settings", "gear-fill", "Settings", "Student")
            })
        });
}
