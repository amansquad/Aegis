using System.Text.RegularExpressions;
using Aegis.Application.Abstractions.Ai;
using Aegis.Domain.Common;

namespace Aegis.Infrastructure.Ai;

/// <summary>
/// A rule-based extractor used when no language model is configured.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> It keeps the intake form working, and the test suite green, on a
/// machine with no API key and no network. It is not a substitute for the model and does not
/// pretend to be one: every result it produces is marked <see cref="ExtractionMethod.Heuristic"/>,
/// which forces human review regardless of the score it assigns. A dispatcher is always told a
/// human classified this, not a model.
/// </para>
/// <para>
/// <b>What it cannot do.</b> Keyword matching has no understanding of negation, sarcasm or
/// context. "No leak, just checking the hydrant is accessible" scores as a leak here. That is
/// exactly why its output is never auto-accepted, and why the confidence it reports is capped well
/// below the threshold that would allow it to be.
/// </para>
/// </remarks>
public sealed partial class HeuristicIncidentExtractor : IIncidentExtractor
{
    /// <summary>
    /// The highest confidence this extractor will ever claim.
    /// </summary>
    /// <remarks>
    /// Below <see cref="ExtractedIncident.AutoAcceptThreshold"/> by construction. Keyword matching
    /// that is sure of itself is keyword matching that has not met a negation yet.
    /// </remarks>
    private const double MaxConfidence = 0.55;

    /// <summary>
    /// Keywords per category, in the order they are tested.
    /// </summary>
    /// <remarks>
    /// Order matters and is not alphabetical: the more specific and more dangerous categories are
    /// tested first, so a report mentioning both "flooding" and "smell of gas" classifies on the
    /// gas. Ties resolved toward the outcome that gets a human looking sooner.
    /// </remarks>
    private static readonly (IncidentCategory Category, string[] Keywords)[] CategoryRules =
    [
        (IncidentCategory.PowerFault, ["power cut", "no power", "electric", "sparking", "cable", "substation", "transformer"]),
        (IncidentCategory.StructuralDamage, ["collapse", "collapsed", "sinkhole", "subsidence", "crack", "struck", "hit the", "damaged"]),
        (IncidentCategory.WaterQuality, ["brown water", "discolour", "discolor", "smell", "smells", "taste", "cloudy", "contaminat"]),
        (IncidentCategory.SupplyLoss, ["no water", "no supply", "lost supply", "supply is off", "nothing coming"]),
        (IncidentCategory.Blockage, ["blocked", "blockage", "drain", "sewer", "backing up", "overflow", "gully"]),
        (IncidentCategory.PressureProblem, ["pressure", "trickle", "weak flow", "low flow"]),
        (IncidentCategory.Leak, ["leak", "leaking", "burst", "water coming", "flooding", "flood", "gushing", "seeping"]),
        (IncidentCategory.RoadDefect, ["pothole", "road surface", "pavement", "street light", "streetlight", "signage"]),
    ];

    /// <summary>
    /// Phrases that indicate danger to people.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. A false positive here costs a dispatcher a glance; a false negative
    /// costs considerably more, and this list is the only judgement about consequence that a
    /// keyword matcher is qualified to make.
    /// </remarks>
    private static readonly string[] SafetyPhrases =
    [
        "gas", "smell of gas", "electric", "electrical", "sparking", "live wire", "exposed",
        "collapse", "sinkhole", "injured", "hurt", "danger", "dangerous", "hazard",
        "flooding the", "into the house", "into my house", "basement", "cellar",
        "school", "hospital", "child", "children", "elderly", "car has", "traffic",
    ];

    private static readonly string[] HighSeverityPhrases =
    [
        "burst", "gushing", "collapse", "sinkhole", "no water", "no supply", "flooding",
        "whole street", "entire street", "many houses", "hospital", "school", "urgent",
    ];

    private static readonly string[] LowSeverityPhrases =
    [
        "dripping", "slight", "minor", "small", "slowly", "occasionally", "cosmetic", "not urgent",
    ];

    /// <inheritdoc />
    public Task<Result<ExtractedIncident>> ExtractAsync(
        string report,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(report))
        {
            return Task.FromResult(Result.Failure<ExtractedIncident>(Error.Validation(
                "Incident.ReportEmpty",
                "Describe the problem before submitting.")));
        }

        var text = report.ToLowerInvariant();

        var matchIndex = Array.FindIndex(
            CategoryRules,
            rule => rule.Keywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)));

        var category = matchIndex < 0 ? IncidentCategory.Other : CategoryRules[matchIndex].Category;

        var safetyRisk = SafetyPhrases.Any(phrase => text.Contains(phrase, StringComparison.Ordinal));

        var severity = ResolveSeverity(text, safetyRisk);

        // Confidence rises only with corroborating signal, and is capped regardless. An unmatched
        // report is close to a guess and says so.
        var corroboration = matchIndex < 0
            ? 0
            : CategoryRules[matchIndex].Keywords.Count(k => text.Contains(k, StringComparison.Ordinal));

        var confidence = matchIndex < 0
            ? 0.2
            : Math.Min(MaxConfidence, 0.35 + (corroboration * 0.08));

        return Task.FromResult(Result.Success(new ExtractedIncident(
            category,
            severity,
            BuildSummary(report, category),
            ExtractLocation(report),
            ExtractAssetCode(report),
            safetyRisk,
            confidence,
            ExtractionMethod.Heuristic)));
    }

    private static IncidentSeverity ResolveSeverity(string text, bool safetyRisk)
    {
        // A safety risk floors severity at High. Nothing a keyword matcher reads should be able to
        // classify a described danger as routine.
        if (safetyRisk)
        {
            return HighSeverityPhrases.Any(phrase => text.Contains(phrase, StringComparison.Ordinal))
                ? IncidentSeverity.Critical
                : IncidentSeverity.High;
        }

        if (HighSeverityPhrases.Any(phrase => text.Contains(phrase, StringComparison.Ordinal)))
        {
            return IncidentSeverity.High;
        }

        if (LowSeverityPhrases.Any(phrase => text.Contains(phrase, StringComparison.Ordinal)))
        {
            return IncidentSeverity.Low;
        }

        return IncidentSeverity.Moderate;
    }

    /// <summary>
    /// Builds a summary by labelling the report rather than rewriting it.
    /// </summary>
    /// <remarks>
    /// A rule-based summariser that tried to paraphrase would produce something worse than the
    /// original and hide what the reporter actually said. Prefixing the category and truncating is
    /// honest about how little processing happened.
    /// </remarks>
    private static string BuildSummary(string report, IncidentCategory category)
    {
        var condensed = Whitespace().Replace(report.Trim(), " ");
        var body = condensed.Length <= 180 ? condensed : $"{condensed[..177]}…";

        return $"{Humanise(category)}: {body}";
    }

    private static string Humanise(IncidentCategory category) => category switch
    {
        IncidentCategory.Leak => "Reported leak",
        IncidentCategory.SupplyLoss => "Reported loss of supply",
        IncidentCategory.WaterQuality => "Reported water quality problem",
        IncidentCategory.PressureProblem => "Reported pressure problem",
        IncidentCategory.Blockage => "Reported blockage",
        IncidentCategory.StructuralDamage => "Reported structural damage",
        IncidentCategory.PowerFault => "Reported power fault",
        IncidentCategory.RoadDefect => "Reported road defect",
        _ => "Unclassified report",
    };

    /// <summary>Pulls a street-like phrase out of the report, if one is present.</summary>
    private static string? ExtractLocation(string report)
    {
        var match = StreetPattern().Match(report);

        return match.Success ? match.Value.Trim() : null;
    }

    /// <summary>
    /// Pulls anything shaped like an asset code out of the report.
    /// </summary>
    /// <remarks>
    /// A hint only. It is resolved through the ordinary tenant-scoped lookup, so a code invented by
    /// the reporter, or belonging to another organization, simply fails to match.
    /// </remarks>
    private static string? ExtractAssetCode(string report)
    {
        var match = AssetCodePattern().Match(report);

        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Whitespace();

    [GeneratedRegex(
        @"\b\d{0,4}\s?[A-Z][a-z]+(?:\s[A-Z][a-z]+)*\s(?:Road|Street|Lane|Avenue|Close|Way|Drive|Crescent|Hill|Gardens|Square)\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex StreetPattern();

    [GeneratedRegex(
        @"\b[A-Za-z]{2,4}-[A-Za-z0-9]{1,4}-\d{2,6}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex AssetCodePattern();
}
