namespace Shared.Contracts;

public sealed record RestrictionRuleMessage(
    int Id,
    string RuleType,   // Application | Website
    string Target,
    string Mode);      // Block | Allow
