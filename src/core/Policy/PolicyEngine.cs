using SmartReport.Core.Catalog;
using SmartReport.Core.Specs;

namespace SmartReport.Core.Policy;

/// <summary>
/// Authorizes a validated spec for one principal, column by column.
///
/// <para>
/// This runs <i>before</i> compilation, so a measure the principal may not use is never turned
/// into SQL at all. Postgres row-level security still sits underneath as the backstop, but the
/// two answer different questions: RLS decides which <i>rows</i> come back, this decides which
/// <i>measures</i> may be asked for — and, unlike RLS, it can say why.
/// </para>
/// </summary>
public sealed class PolicyEngine(DataCatalog catalog)
{
    public GovernedSpec Govern(ReportSpec spec, Principal principal)
    {
        var decisions = new List<PolicyDecision>();
        var governed = new List<GovernedQuery>();
        var tablesRead = new List<string>();
        var withheld = new List<string>();

        foreach (var query in spec.Queries)
        {
            // A validated spec only ever names models that exist; this is belt and braces.
            var model = catalog.Model(query.Model);
            if (model is null)
            {
                continue;
            }

            var metrics = Keep(
                query.Metrics,
                key => model.Metric(key)!.IsAllowedFor(principal.Role),
                key => $"metric:{key}",
                key => $"{model.Metric(key)!.Label} is restricted to "
                       + DescribeRoles(model.Metric(key)!.RequiredRoles)
                       + $"; you are signed in as {Roles.Label(principal.Role)}.",
                key => $"{model.Metric(key)!.Label} from {model.Label}.",
                decisions, withheld);

            if (metrics.Count == 0)
            {
                // Every measure was denied, so there is nothing left to ask. Skip the query
                // rather than compiling one that would return bare groupings.
                continue;
            }

            var dimensions = Keep(
                query.Dimensions,
                key => model.Dimension(key)!.IsAllowedFor(principal.Role),
                key => $"dimension:{key}",
                key => $"Grouping by {model.Dimension(key)!.Label} is restricted to "
                       + DescribeRoles(model.Dimension(key)!.RequiredRoles) + ".",
                _ => null,
                decisions, withheld);

            var filters = new List<SpecFilter>(query.Filters.Count);
            foreach (var filter in query.Filters)
            {
                var field = model.Filter(filter.Field)!;
                if (field.IsAllowedFor(principal.Role))
                {
                    filters.Add(filter);
                }
                else
                {
                    decisions.Add(PolicyDecision.Deny($"filter:{filter.Field}",
                        $"Filtering on {field.Label} is restricted to {DescribeRoles(field.RequiredRoles)}."));
                    withheld.Add(field.Label);
                }
            }

            governed.Add(new GovernedQuery(
                new SpecQuery(model.Key, metrics, dimensions, filters, query.Limit),
                Math.Clamp(query.Limit ?? SpecLimits.DefaultRowLimit, 1, SpecLimits.MaxRowLimit)));

            foreach (var table in model.Tables)
            {
                if (!tablesRead.Contains(table, StringComparer.Ordinal))
                {
                    tablesRead.Add(table);
                }
            }
        }

        // Surface the row scoping the database is about to apply, so the user understands a
        // smaller number rather than quietly receiving one. The enforcement is RLS, not this line.
        if (governed.Count > 0 && principal.IsRegionScoped)
        {
            decisions.Add(PolicyDecision.Scope("rows:region",
                $"Rows are limited to the {principal.RegionName ?? "assigned"} region by database policy."));
        }

        return new GovernedSpec(spec.Title, governed, decisions, tablesRead, withheld);
    }

    private static List<string> Keep(
        IReadOnlyList<string> keys,
        Func<string, bool> isAllowed,
        Func<string, string> subject,
        Func<string, string> denyReason,
        Func<string, string?> allowReason,
        List<PolicyDecision> decisions,
        List<string> withheld)
    {
        var kept = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (isAllowed(key))
            {
                var reason = allowReason(key);
                if (reason is not null)
                {
                    decisions.Add(PolicyDecision.Allow(subject(key), reason));
                }
                kept.Add(key);
            }
            else
            {
                decisions.Add(PolicyDecision.Deny(subject(key), denyReason(key)));
                withheld.Add(key);
            }
        }
        return kept;
    }

    private static string DescribeRoles(IReadOnlyList<string>? roles) =>
        roles is null or { Count: 0 }
            ? "no one"
            : string.Join(" or ", roles.Select(Roles.Label));
}
