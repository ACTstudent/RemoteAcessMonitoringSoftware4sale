using Server.Models;
using Shared.Contracts;
using Xunit;

namespace Server.Tests.SharedTests;

/// <summary>
/// CODE-05. These constants replaced string literals, so their whole value is
/// that they are the same strings. Every one of them is either stored in the
/// database or carried in an authentication cookie, which means a rename is not
/// a refactor - it silently stops matching rows that already exist, or
/// invalidates every session already issued.
///
/// So these tests assert the literal text. They are deliberately the kind of
/// test that looks redundant: it is the redundancy that catches a well-meaning
/// tidy-up.
/// </summary>
public class DomainConstantTests
{
    [Fact]
    public void RoleNames_AreExactlyWhatTheCookieCarries()
    {
        Assert.Equal("Admin", RoleNames.Admin);
        Assert.Equal("Teacher", RoleNames.Teacher);
        Assert.Equal("Student", RoleNames.Student);
    }

    [Fact]
    public void AdminOrTeacher_IsTheCommaSeparatedFormAuthorizeExpects()
    {
        // [Authorize(Roles = "Admin,Teacher")] means "either", and the format is
        // the framework's, not ours. No space after the comma.
        Assert.Equal("Admin,Teacher", RoleNames.AdminOrTeacher);
    }

    [Fact]
    public void LabSessionStatus_AreExactlyWhatIsStoredAndSent()
    {
        Assert.Equal("Running", LabSessionStatus.Running);
        Assert.Equal("Paused", LabSessionStatus.Paused);
        Assert.Equal("Ended", LabSessionStatus.Ended);
        Assert.Equal("None", LabSessionStatus.None);
    }

    [Fact]
    public void RecordStatus_AreExactlyWhatIsStored()
    {
        Assert.Equal("Active", RecordStatus.Active);
        Assert.Equal("Inactive", RecordStatus.Inactive);
        Assert.Equal("Archived", RecordStatus.Archived);
    }

    [Fact]
    public void WorkstationStatus_AreExactlyWhatIsStored()
    {
        Assert.Equal("Available", WorkstationStatus.Available);
        Assert.Equal("Assigned", WorkstationStatus.Assigned);
        Assert.Equal("Maintenance", WorkstationStatus.Maintenance);
        Assert.Equal("Archived", WorkstationStatus.Archived);
    }

    [Fact]
    public void WorkstationInUse_KeepsItsSpace()
    {
        // The obvious "fix" is to make this match the others. Rows in every
        // deployed database say "In Use", and a workstation nobody can see as
        // busy is one a teacher hands to a second student.
        Assert.Equal("In Use", WorkstationStatus.InUse);
        Assert.Contains(' ', WorkstationStatus.InUse);
    }

    [Fact]
    public void ArchivedIsSpeltTheSameInBothDomains_ButRemainsTwoConstants()
    {
        // Same text, different events. The test records that this is intended,
        // so nobody deletes one and points the other at it.
        Assert.Equal(RecordStatus.Archived, WorkstationStatus.Archived);
    }

    [Fact]
    public void ModelDefaults_StillMatchTheConstants()
    {
        // The defaults were written as literals in the entity classes. If one
        // drifts, rows are created in a state nothing queries for.
        Assert.Equal(RecordStatus.Active, new Teacher().Status);
        Assert.Equal(RecordStatus.Active, new Student().Status);
        Assert.Equal(RecordStatus.Active, new Class().Status);
        Assert.Equal(WorkstationStatus.Available, new Computer().Status);
        Assert.Equal(LabSessionStatus.Running, new LabSession().Status);
    }
}
