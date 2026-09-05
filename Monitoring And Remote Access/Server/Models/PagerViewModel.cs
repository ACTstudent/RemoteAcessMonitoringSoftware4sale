namespace Server.Models;

/// <summary>
/// One pager for every paged list, so page controls sit in the same place and
/// behave the same way wherever a teacher meets them.
///
/// The remote-control history had no pager at all: the action accepted a page
/// number and the query returned a paged result, but the view rendered only the
/// first page, so a command audit longer than one page was unreachable through
/// the interface.
/// </summary>
/// <param name="Page">The page being shown, 1-based.</param>
/// <param name="PageCount">Total pages available.</param>
/// <param name="TotalCount">Total items across all pages.</param>
/// <param name="ItemNoun">Plural noun for the items, used in the count and the landmark label.</param>
/// <param name="Action">The action the page links point at.</param>
/// <param name="RouteValues">The current filter, so paging does not drop it. Any <c>page</c> entry is replaced.</param>
public sealed record PagerViewModel(
    int Page,
    int PageCount,
    int TotalCount,
    string ItemNoun,
    string Action,
    object? RouteValues = null)
{
    public static PagerViewModel For<T>(
        PagedResult<T> result, string itemNoun, string action, object? routeValues = null) =>
        new(result.Page, result.PageCount, result.TotalCount, itemNoun, action, routeValues);
}
