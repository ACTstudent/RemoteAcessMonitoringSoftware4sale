namespace Server.Models;

/// <summary>
/// The alert list's display state, exactly as it appears in the query string.
///
/// Alert actions used to redirect to a bare <c>Alerts</c> URL with
/// <c>includeAcknowledged=true</c> pinned on. A teacher working through, say,
/// critical open alerts for one station was thrown back to an unfiltered list
/// containing already-handled alerts after every single acknowledge, and had to
/// rebuild the filter each time. Posting this object with the action and handing
/// it back to the redirect keeps the list where the teacher left it.
///
/// It is also the one place that decides which status a filter selects, so the
/// list, the paging links and the CSV export cannot disagree about what is on
/// screen.
/// </summary>
public sealed class AlertListFilter
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;

    /// <summary>Shows every status rather than open alerts only. Ignored when <see cref="Status"/> is set.</summary>
    public bool IncludeAcknowledged { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Severity { get; set; }
    public string? StudentId { get; set; }
    public string? Station { get; set; }

    /// <summary>An explicit <see cref="MonitoringAlertStatus"/> name, or blank to let <see cref="IncludeAcknowledged"/> decide.</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>A date range a teacher can produce from the filter form by picking an end date before the start date.</summary>
    public bool HasUsableDateRange => !(From.HasValue && To.HasValue && To.Value.Date < From.Value.Date);

    /// <summary>
    /// Resolves the status this filter displays. Returns false for a status name
    /// that is not a known value, which only a hand-edited URL can produce.
    /// </summary>
    public bool TryResolveStatus(out MonitoringAlertStatus? status)
    {
        if (!string.IsNullOrWhiteSpace(Status))
        {
            if (Enum.TryParse<MonitoringAlertStatus>(Status, true, out var parsed))
            {
                status = parsed;
                return true;
            }

            status = null;
            return false;
        }

        status = IncludeAcknowledged ? null : MonitoringAlertStatus.Open;
        return true;
    }

    /// <summary>The status name the filter form should show as selected; blank means "All statuses".</summary>
    public string ResolvedStatusName =>
        TryResolveStatus(out var status) && status.HasValue ? status.Value.ToString() : string.Empty;

    /// <summary>Pulls page and size into range instead of rejecting a shareable link outright.</summary>
    public void ClampPaging()
    {
        if (Page < 1) Page = 1;
        if (PageSize < 1 || PageSize > MaxPageSize) PageSize = DefaultPageSize;
    }

    public MonitoringAlertFilter ToQueryFilter(MonitoringAlertStatus? status) =>
        new(From, To, Trimmed(Severity), Trimmed(StudentId), Trimmed(Station), status, Page, PageSize);

    /// <summary>
    /// The filter as route values. Defaults are emitted as null so the framework
    /// drops them, which keeps a plain list on a plain URL and makes a filtered
    /// one copyable.
    /// </summary>
    public object ToRouteValues(int? page = null)
    {
        var effectivePage = page ?? Page;
        return new
        {
            includeAcknowledged = IncludeAcknowledged ? "true" : null,
            from = From?.ToString("yyyy-MM-dd"),
            to = To?.ToString("yyyy-MM-dd"),
            severity = Trimmed(Severity),
            studentId = Trimmed(StudentId),
            station = Trimmed(Station),
            status = Trimmed(Status),
            page = effectivePage > 1 ? effectivePage : (int?)null,
            pageSize = PageSize != DefaultPageSize ? PageSize : (int?)null
        };
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>What the alerts page renders: the current page of groups, the filter that produced it, and any filter problem worth telling the teacher about.</summary>
public sealed record AlertListViewModel(
    PagedResult<MonitoringAlert> Alerts,
    AlertListFilter Filter,
    string? FilterWarning = null);
