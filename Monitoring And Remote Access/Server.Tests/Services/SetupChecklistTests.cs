using Server.Services;
using Xunit;

namespace Server.Tests.Services;

/// <summary>
/// FLOW-01. The acceptance is that a clean setup can be completed without
/// editing source or config, and that a failure identifies the next action. So
/// these test the two things a checklist can get wrong: reporting complete when
/// it is not, and telling someone what is missing without telling them what to
/// do about it.
/// </summary>
public class SetupChecklistTests
{
    private static IReadOnlyList<SetupStep> Fresh() =>
        SetupChecklist.Build(0, 0, 0, false, false, false);

    private static IReadOnlyList<SetupStep> Ready() =>
        SetupChecklist.Build(1, 1, 1, true, true, true);

    [Fact]
    public void AFreshInstall_HasEverythingOutstanding()
    {
        var steps = Fresh();

        Assert.NotEmpty(steps);
        Assert.All(steps, step => Assert.False(step.Done));
        Assert.False(SetupChecklist.IsComplete(steps));
    }

    [Fact]
    public void AReadyInstall_IsComplete()
    {
        Assert.True(SetupChecklist.IsComplete(Ready()));
    }

    [Fact]
    public void EveryStep_SaysWhatDoesNotWorkUntilItIsDone()
    {
        // A checklist that says "Add a teacher" without saying why is a chore.
        // Saying what stops working makes it a reason.
        Assert.All(Fresh(), step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Why));
            Assert.True(step.Why.Length > 40, $"'{step.Title}' does not explain itself");
        });
    }

    [Fact]
    public void EveryStep_NamesItsNextAction()
    {
        Assert.All(Fresh(), step => Assert.False(string.IsNullOrWhiteSpace(step.NextAction)));
    }

    [Fact]
    public void EveryStepThePortalCanDo_LinksToWhereItIsDone()
    {
        var linkable = Fresh().Where(step => step.Href is not null).ToList();

        // All but the last: installing an agent on a workstation is not
        // something the portal can do, and pretending otherwise with a link to
        // nowhere would be worse than saying so.
        Assert.Equal(Fresh().Count - 1, linkable.Count);
        Assert.All(linkable, step => Assert.StartsWith("/Admin/", step.Href!));
    }

    [Fact]
    public void NextStep_IsTheFirstOutstandingOne()
    {
        // Six red items is not an instruction. One is.
        var steps = SetupChecklist.Build(1, 0, 0, false, false, false);

        var next = SetupChecklist.NextStep(steps);

        Assert.NotNull(next);
        Assert.Equal("Create a class", next!.Title);
    }

    [Fact]
    public void NextStep_IsNothingWhenEverythingIsDone()
    {
        Assert.Null(SetupChecklist.NextStep(Ready()));
    }

    [Fact]
    public void ConnectingAWorkstation_IsTheLastStep()
    {
        // Everything else can be true while a student still cannot reach the
        // server, so this has to be its own step and it has to come last.
        var steps = SetupChecklist.Build(1, 1, 1, true, true, false);

        Assert.False(SetupChecklist.IsComplete(steps));
        Assert.Equal("Connect the first workstation", SetupChecklist.NextStep(steps)!.Title);
        Assert.Equal(steps[^1].Title, SetupChecklist.NextStep(steps)!.Title);
    }

    [Fact]
    public void TheCertificateStep_SaysWhyTheFailureLooksLikeSomethingElse()
    {
        var certificate = Fresh().Single(step => step.Title.Contains("root certificate"));

        // The whole point of naming it: an untrusted certificate presents as a
        // network fault, and an administrator will chase the wrong thing.
        Assert.Contains("network fault", certificate.Why);
    }

    [Fact]
    public void OnlyThePublicCertificateIsMentioned()
    {
        var certificate = Fresh().Single(step => step.Title.Contains("root certificate"));

        Assert.Contains("public", certificate.Why);
        Assert.DoesNotContain("pfx", certificate.Why, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private key", certificate.Why, StringComparison.OrdinalIgnoreCase);
    }
}
