using SmartReport.Core.Catalog;
using SmartReport.Core.Specs;

namespace SmartReport.Core.Policy;

/// <summary>
/// Authorizes a validated spec for one principal, before anything is compiled.
///
/// <para>
/// Three kinds of boundary are enforced here, and each produces a decision the UI can show:
/// </para>
/// <list type="bullet">
/// <item><b>Role.</b> Billing belongs to the account owner — the refusal names them.</item>
/// <item><b>Northpoint's own data.</b> Present in the database, unreachable from the portal, and
/// the refusal redirects to the client's own equivalent figure.</item>
/// <item><b>Disclosure.</b> Benchmarks carry a minimum-cohort floor, stated rather than silent.</item>
/// </list>
/// <para>
/// Row scoping to the client is <i>not</i> done here. That is Postgres row-level security, which
/// cannot be skipped by a bug in this file; the engine only mentions it so the user understands a
/// smaller number.
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
            var model = catalog.Model(query.Model);
            if (model is null)
            {
                continue;
            }

            // A whole model can be out of bounds — Northpoint's contract rates, or billing for
            // anyone but the owner. Refuse once, at the model, rather than metric by metric.
            if (!model.IsAllowedFor(principal.Role))
            {
                decisions.Add(PolicyDecision.Deny(
                    $"model:{model.Key}",
                    model.RestrictionNote ?? $"{model.Label} is not available to {Roles.Label(principal.Role)}.",
                    OwnerRemedy(model, principal)
                        ?? Remedy(model.Metrics.Values.FirstOrDefault()?.Alternative, principal)));

                withheld.Add(model.Label);
                continue;
            }

            var metrics = new List<string>();
            foreach (var key in query.Metrics)
            {
                var metric = model.Metric(key)!;
                if (metric.IsAllowedFor(principal.Role))
                {
                    decisions.Add(PolicyDecision.Allow($"metric:{key}", $"{metric.Label} from {model.Label}."));
                    metrics.Add(key);
                }
                else
                {
                    decisions.Add(PolicyDecision.Deny(
                        $"metric:{key}",
                        metric.RestrictionNote ?? $"{metric.Label} is restricted.",
                        // Access that can be granted is a better answer than a substitute, so
                        // the named owner comes first where there is one.
                        OwnerRemedy(model, principal) ?? Remedy(metric.Alternative, principal)));
                    withheld.Add(metric.Label);
                }
            }

            if (metrics.Count == 0)
            {
                continue;
            }

            var dimensions = query.Dimensions
                .Where(key => model.Dimension(key)!.IsAllowedFor(principal.Role))
                .ToList();

            var filters = query.Filters
                .Where(f => model.Filter(f.Field)!.IsAllowedFor(principal.Role))
                .ToList();

            // Safeguards are not optional and not conditional — state each one so the user can
            // see the figure is bounded rather than wonder why it looks odd.
            foreach (var safeguard in model.Safeguards)
            {
                decisions.Add(PolicyDecision.Aggregate($"safeguard:{model.Key}", safeguard.Reason));
            }

            governed.Add(new GovernedQuery(
                new SpecQuery(model.Key, metrics, dimensions, filters, query.Limit, query.Label),
                Math.Clamp(query.Limit ?? SpecLimits.DefaultRowLimit, 1, SpecLimits.MaxRowLimit)));

            foreach (var table in model.Tables)
            {
                if (!tablesRead.Contains(table, StringComparer.Ordinal))
                {
                    tablesRead.Add(table);
                }
            }
        }

        if (governed.Count > 0)
        {
            decisions.Add(PolicyDecision.Scope(
                "rows:client",
                $"Rows are limited to {principal.ClientName}'s own data by database policy."));
        }

        return new GovernedSpec(spec.Title, governed, decisions, tablesRead, withheld);
    }

    /// <summary>
    /// Turns an <c>Alternative</c> into an offer — but only after checking the asker may use it.
    /// Suggesting something that leads to a second refusal is worse than suggesting nothing.
    /// </summary>
    private string? Remedy(string? alternative, Principal principal)
    {
        if (alternative is null || catalog.ResolveMetric(alternative) is not var (model, metric))
        {
            return null;
        }

        return model.IsAllowedFor(principal.Role) && metric.IsAllowedFor(principal.Role)
            ? $"You can see {metric.Label.ToLowerInvariant()} instead."
            : null;
    }

    /// <summary>Where no substitute exists, name who can grant access — if anyone can.</summary>
    private static string? OwnerRemedy(QueryModel model, Principal principal) =>
        model.RequiredRoles is not null
        && model.RequiredRoles.Contains(Roles.AccountOwner)
        && principal.AccountOwnerName is not null
            ? $"Ask {principal.AccountOwnerName}, {principal.ClientName}'s account owner, for access."
            : null;
}
