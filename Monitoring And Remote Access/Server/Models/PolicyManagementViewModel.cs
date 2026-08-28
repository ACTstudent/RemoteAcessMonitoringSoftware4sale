namespace Server.Models;

public sealed class PolicyManagementViewModel
{
    public IReadOnlyList<RestrictionRule> Restrictions { get; init; } = Array.Empty<RestrictionRule>();
    public IReadOnlyList<BlacklistItem> Blacklist { get; init; } = Array.Empty<BlacklistItem>();
    public IReadOnlyList<ApplicationCategory> ApplicationCategories { get; init; } = Array.Empty<ApplicationCategory>();
    public IReadOnlyList<WebsiteCategory> WebsiteCategories { get; init; } = Array.Empty<WebsiteCategory>();
}
