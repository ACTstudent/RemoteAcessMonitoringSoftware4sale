using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Server.Models;
using Server.Services;
using Server.Tests.Controllers;

namespace Server.Tests.Services;

// The sidebar of each portal: which pages are grouped together, and where the
// name in the page header leads.
public class NavigationBuilderTests
{
    private static HttpContext SignedInAs(string role) => new DefaultHttpContext
    {
        Session = new FakeSession(),
        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"))
    };

    private static NavigationModel Menu(string portal) => NavigationBuilder.Build(portal, SignedInAs(portal));

    private static IEnumerable<NavItem> Links(NavigationModel menu) => menu.Sections.SelectMany(section => section.Items);

    public static TheoryData<string> Portals => new() { NavigationBuilder.Admin, NavigationBuilder.Teacher };

    // Classes are not people. The link used to sit in the People group beside
    // Teachers and Students.
    [Theory]
    [MemberData(nameof(Portals))]
    public void Classes_StandsOutsideThePeopleGroup(string portal)
    {
        var menu = Menu(portal);

        var people = Assert.Single(menu.Sections, section => section.Label == "People");
        Assert.DoesNotContain(people.Items, item => item.Text == "Classes");
        Assert.Contains(menu.Sections.Where(section => section.Label != "People").SelectMany(section => section.Items),
            item => item is { Text: "Classes", Action: "Classes", Controller: "Admin" });
    }

    [Fact]
    public void People_AreTheAccounts()
    {
        var admin = Assert.Single(Menu(NavigationBuilder.Admin).Sections, section => section.Label == "People");
        Assert.Equal(new[] { "Teachers", "Students", "Admin Accounts" }, admin.Items.Select(item => item.Text));
        Assert.Contains(admin.Items, item => item is { Text: "Admin Accounts", Action: "AdminAccounts", Controller: "Admin" });

        // A teacher manages teachers and students, never administrators.
        var teacher = Assert.Single(Menu(NavigationBuilder.Teacher).Sections, section => section.Label == "People");
        Assert.Equal(new[] { "Teachers", "Students" }, teacher.Items.Select(item => item.Text));
    }

    // A teacher used to have "Workstations" and "Computers": the same machines
    // under two names, on two pages.
    [Fact]
    public void TheTeacherMenu_HasOneComputersPage()
    {
        var links = Links(Menu(NavigationBuilder.Teacher)).ToList();

        var computers = Assert.Single(links, item => item.Action == "Computers");
        Assert.Equal("Computers", computers.Text);
        Assert.Equal("Admin", computers.Controller);
        Assert.DoesNotContain(links, item => item.Text.Contains("Workstation", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Portals))]
    public void TheNameInTheHeader_OpensThatPortalsAccountSettings(string portal)
    {
        var menu = Menu(portal);

        Assert.Equal("Settings", menu.SettingsAction);
        Assert.Equal(portal, menu.SettingsController);
        // The same page closes the sidebar.
        var last = menu.Sections[^1].Items[^1];
        Assert.Equal("Account Settings", last.Text);
        Assert.Equal("Settings", last.Action);
        Assert.Equal(portal, last.Controller);
    }

    // A teacher on a page shared with the administrator keeps the teacher's
    // menu, and so their own settings.
    [Fact]
    public void ATeacherOnASharedPage_IsLedToTheTeachersSettings()
    {
        var menu = NavigationBuilder.Build(NavigationBuilder.Admin, SignedInAs(NavigationBuilder.Teacher));

        Assert.Equal(NavigationBuilder.Teacher, menu.SettingsController);
        Assert.DoesNotContain(Links(menu), item => item.Action == "AdminAccounts");
    }

    // What a teacher reads back after a lab period is one group, not scattered
    // under "My Classes".
    [Fact]
    public void TheTeachersRecords_AreOneGroup()
    {
        var menu = Menu(NavigationBuilder.Teacher);

        var records = Assert.Single(menu.Sections, section => section.Label == "Monitoring Records");
        Assert.Equal(new[] { "Alerts", "Records", "Browser History", "Remote History", "Timeline", "Lab Utilization" },
            records.Items.Select(item => item.Text));
        var mine = Assert.Single(menu.Sections, section => section.Label == "My Classes");
        Assert.Equal(new[] { "My Class List", "My Students", "Class Restrictions" }, mine.Items.Select(item => item.Text));
    }

    [Theory]
    [MemberData(nameof(Portals))]
    public void NoPageIsListedTwice(string portal)
    {
        var links = Links(Menu(portal)).Select(item => $"{item.Controller}/{item.Action}").ToList();

        Assert.Equal(links.Count, links.Distinct().Count());
    }
}
