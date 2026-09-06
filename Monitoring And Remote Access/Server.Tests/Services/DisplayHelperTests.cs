using Server.Extensions;
using Server.Models;

namespace Server.Tests.Services;

/// <summary>
/// SQLite hands timestamps back with <see cref="DateTimeKind.Unspecified"/>.
/// Calling ToLocalTime on those leaves them untouched, so a UTC value would be
/// printed as though it were already local - an hour-shifted timestamp on every
/// history and export screen. These tests pin the conversion.
/// </summary>
public class DateTimeDisplayExtensionsTests
{
    [Fact]
    public void AnUnspecifiedTimestampIsReadAsUtcAndThenConverted()
    {
        var stored = new DateTime(2026, 3, 14, 1, 30, 0, DateTimeKind.Unspecified);

        var shown = stored.ToDisplayLocal();

        Assert.Equal(DateTimeKind.Local, shown.Kind);
        Assert.Equal(DateTime.SpecifyKind(stored, DateTimeKind.Utc).ToLocalTime(), shown);
    }

    [Fact]
    public void AUtcTimestampIsConvertedOnce()
    {
        var stored = new DateTime(2026, 3, 14, 1, 30, 0, DateTimeKind.Utc);

        var shown = stored.ToDisplayLocal();

        Assert.Equal(DateTimeKind.Local, shown.Kind);
        Assert.Equal(stored.ToLocalTime(), shown);
    }

    [Fact]
    public void AnAlreadyLocalTimestampIsLeftAloneSoItIsNotShiftedTwice()
    {
        var local = new DateTime(2026, 3, 14, 9, 30, 0, DateTimeKind.Local);

        Assert.Equal(local, local.ToDisplayLocal());
    }

    [Fact]
    public void ConvertingTwiceGivesTheSameAnswerAsConvertingOnce()
    {
        var stored = new DateTime(2026, 7, 1, 16, 45, 0, DateTimeKind.Unspecified);

        Assert.Equal(stored.ToDisplayLocal(), stored.ToDisplayLocal().ToDisplayLocal());
    }

    [Fact]
    public void AMissingTimestampStaysMissing()
    {
        DateTime? none = null;
        Assert.Null(none.ToDisplayLocal());
    }

    [Fact]
    public void APresentNullableTimestampConvertsLikeAPlainOne()
    {
        DateTime? stored = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        Assert.Equal(stored!.Value.ToDisplayLocal(), stored.ToDisplayLocal());
    }
}

/// <summary>
/// The shared pager. Its job is to carry the current filter into the page links,
/// which is the part that used to be missing: the remote-control history paged
/// on the server but rendered only page one, so a long command audit had no way
/// to reach its second page.
/// </summary>
public class PagerViewModelTests
{
    private static PagedResult<string> Page(int page, int pageSize, int total) =>
        new(Enumerable.Range(0, Math.Min(pageSize, Math.Max(0, total - (page - 1) * pageSize)))
                .Select(i => $"row {i}").ToList(),
            page, pageSize, total);

    [Fact]
    public void ForCopiesThePagingNumbersOffThePagedResult()
    {
        var model = PagerViewModel.For(Page(2, 25, 130), "commands", "RemoteHistory");

        Assert.Equal(2, model.Page);
        Assert.Equal(6, model.PageCount);
        Assert.Equal(130, model.TotalCount);
        Assert.Equal("commands", model.ItemNoun);
        Assert.Equal("RemoteHistory", model.Action);
    }

    [Fact]
    public void ForCarriesTheCurrentFilterSoPagingDoesNotDropIt()
    {
        var filter = new { from = "2026-01-01", severity = "Critical" };

        var model = PagerViewModel.For(Page(1, 50, 200), "alerts", "Alerts", filter);

        Assert.Same(filter, model.RouteValues);
    }

    [Fact]
    public void AListThatFitsOnOnePageReportsOnePage()
    {
        var model = PagerViewModel.For(Page(1, 100, 12), "alerts", "Alerts");

        Assert.Equal(1, model.PageCount);
    }

    [Fact]
    public void AnEmptyListDoesNotClaimAPageOfResults()
    {
        var model = PagerViewModel.For(Page(1, 100, 0), "alerts", "Alerts");

        Assert.Equal(0, model.TotalCount);
        Assert.InRange(model.PageCount, 0, 1);
    }

    [Fact]
    public void APartialFinalPageStillCountsAsAPage()
    {
        var model = PagerViewModel.For(Page(3, 25, 51), "commands", "RemoteHistory");

        Assert.Equal(3, model.PageCount);
    }
}
