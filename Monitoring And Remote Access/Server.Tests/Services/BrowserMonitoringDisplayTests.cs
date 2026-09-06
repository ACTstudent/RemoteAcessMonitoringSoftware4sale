using Server.Extensions;
using Shared.Contracts;
using Xunit;

namespace Server.Tests.Services;

/// <summary>
/// FLOW-06. These guard the property that matters: a teacher never sees the
/// enum name, and the three states stay distinguishable. A new mode added to
/// the contract without a label here fails the first test rather than reaching
/// a classroom as "SomeNewMode".
/// </summary>
public class BrowserMonitoringDisplayTests
{
    public static TheoryData<BrowserMonitoringMode> AllModes()
    {
        var data = new TheoryData<BrowserMonitoringMode>();
        foreach (var mode in Enum.GetValues<BrowserMonitoringMode>()) data.Add(mode);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void EveryMode_HasALabelThatIsNotTheEnumName(BrowserMonitoringMode mode)
    {
        var label = BrowserMonitoringDisplay.Label(mode);

        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.NotEqual(mode.ToString(), label);
        Assert.NotEqual("Unknown", label);
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void EveryMode_ExplainsItself(BrowserMonitoringMode mode)
    {
        var explanation = BrowserMonitoringDisplay.Explanation(mode);

        Assert.False(string.IsNullOrWhiteSpace(explanation));
        Assert.DoesNotContain("does not recognise", explanation);
    }

    [Fact]
    public void TheThreeStates_ReadDifferently()
    {
        // The whole point of the item: degraded collection must not look like
        // no collection, and neither must look like full collection.
        var labels = Enum.GetValues<BrowserMonitoringMode>()
            .Select(BrowserMonitoringDisplay.Label)
            .ToList();

        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Fact]
    public void DegradedCollection_IsNotDescribedAsNoActivity()
    {
        var fallback = BrowserMonitoringDisplay.Explanation(BrowserMonitoringMode.WindowTitleFallback);

        // A teacher reading this must not conclude the student is doing nothing.
        Assert.Contains("still being recorded", fallback);
        Assert.Contains("not idle", fallback);
    }

    [Fact]
    public void UnavailableCollection_SaysWhatToDoAboutIt()
    {
        var unavailable = BrowserMonitoringDisplay.Explanation(BrowserMonitoringMode.Unavailable);

        Assert.Contains("Student Client", unavailable);
    }

    [Fact]
    public void ForScript_CarriesEveryModeWithBothStrings()
    {
        var map = BrowserMonitoringDisplay.ForScript();

        Assert.Equal(Enum.GetValues<BrowserMonitoringMode>().Length, map.Count);
        foreach (var mode in Enum.GetValues<BrowserMonitoringMode>())
        {
            Assert.True(map.ContainsKey(mode.ToString()),
                $"the monitoring page would have no label for {mode}");
        }
    }
}
