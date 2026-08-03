using Aegis.Domain.Assets.ValueObjects;
using Aegis.Domain.Incidents;
using Aegis.Domain.Incidents.Events;

namespace Aegis.Domain.UnitTests.Incidents;

public sealed class IncidentTests
{
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Dispatcher = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);

    private static Incident Report(
        ClassificationMethod method = ClassificationMethod.Model,
        double? confidence = 0.93,
        bool safetyRisk = false,
        IncidentCategory category = IncidentCategory.Leak,
        IncidentSeverity severity = IncidentSeverity.Moderate)
    {
        var incident = Incident.Report(
            Organization,
            "Water is coming up through the pavement outside number 14 Northgate Road.",
            "Reported leak on Northgate Road",
            category,
            severity,
            method,
            confidence,
            safetyRisk,
            "Northgate Road",
            Now).Value;

        incident.ClearDomainEvents();

        return incident;
    }

    [Fact]
    public void A_new_report_awaits_triage()
    {
        var incident = Report();

        incident.Status.ShouldBe(IncidentStatus.Reported);
        incident.AwaitingTriage.ShouldBeTrue();
        incident.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void The_reference_is_quotable_and_carries_the_year()
    {
        Report().Reference.ShouldStartWith("INC-2026-");
    }

    [Fact]
    public void References_are_unique_across_incidents_reported_together()
    {
        // Derived from a UUIDv7 rather than a per-tenant counter, so uniqueness holds without a
        // serialised sequence on the hottest insert path.
        var references = Enumerable.Range(0, 200).Select(_ => Report().Reference).ToArray();

        references.Distinct(StringComparer.Ordinal).Count().ShouldBe(references.Length);
    }

    [Fact]
    public void An_empty_report_is_rejected()
    {
        Incident.Report(
            Organization, "  ", null, IncidentCategory.Other, IncidentSeverity.Low,
            ClassificationMethod.Manual, null, false, null, Now)
            .Error.Code.ShouldBe("Incident.ReportEmpty");
    }

    // ---- What the classifier proposed is never lost ----

    [Fact]
    public void The_reporters_words_survive_triage_unchanged()
    {
        // The question "what did they actually say?" is asked constantly during an investigation.
        // An interface that has replaced the answer with a machine's paraphrase cannot answer it.
        var incident = Report();
        var original = incident.ReportText;

        incident.Triage(IncidentCategory.Blockage, IncidentSeverity.High, "Corrected summary", Dispatcher, Now);

        incident.ReportText.ShouldBe(original);
        incident.Summary.ShouldBe("Corrected summary");
    }

    [Fact]
    public void A_correction_retains_what_was_proposed()
    {
        // The gap between proposal and confirmation is the only honest measure of whether the
        // classifier is any good, and it cannot be reconstructed later if only the final value
        // is kept.
        var incident = Report(category: IncidentCategory.Leak, severity: IncidentSeverity.Low);

        incident.Triage(IncidentCategory.Blockage, IncidentSeverity.High, null, Dispatcher, Now);

        incident.ProposedCategory.ShouldBe(IncidentCategory.Leak);
        incident.ProposedSeverity.ShouldBe(IncidentSeverity.Low);
        incident.Category.ShouldBe(IncidentCategory.Blockage);
        incident.Severity.ShouldBe(IncidentSeverity.High);
    }

    [Fact]
    public void Triage_raises_an_event_carrying_both_the_proposal_and_the_decision()
    {
        var incident = Report(category: IncidentCategory.Leak, severity: IncidentSeverity.Low);

        incident.Triage(IncidentCategory.WaterQuality, IncidentSeverity.High, null, Dispatcher, Now);

        var raised = incident.DomainEvents.OfType<IncidentTriaged>().ShouldHaveSingleItem();
        raised.ProposedCategory.ShouldBe(IncidentCategory.Leak);
        raised.ConfirmedCategory.ShouldBe(IncidentCategory.WaterQuality);
        raised.TriagedBy.ShouldBe(Dispatcher);
    }

    [Fact]
    public void A_manually_entered_incident_records_no_proposal()
    {
        var incident = Incident.Report(
            Organization, "Entered by phone operator", "Burst main", IncidentCategory.Leak,
            IncidentSeverity.High, ClassificationMethod.Manual, null, false, null, Now).Value;

        incident.ProposedCategory.ShouldBeNull();
        incident.ProposedSeverity.ShouldBeNull();
        incident.RequiresReview().ShouldBeFalse();
    }

    // ---- Review rules ----

    [Fact]
    public void A_confident_model_classification_does_not_require_review()
    {
        Report(ClassificationMethod.Model, confidence: 0.94).RequiresReview().ShouldBeFalse();
    }

    [Fact]
    public void A_low_confidence_classification_requires_review()
    {
        Report(ClassificationMethod.Model, confidence: 0.6).RequiresReview().ShouldBeTrue();
    }

    [Fact]
    public void A_safety_risk_requires_review_however_confident_the_classifier_was()
    {
        // A model confidently mislabelling a gas smell as a routine leak is exactly the failure
        // that must not pass silently. Confidence measures self-consistency, not being right about
        // consequences.
        Report(ClassificationMethod.Model, confidence: 0.99, safetyRisk: true)
            .RequiresReview().ShouldBeTrue();
    }

    [Fact]
    public void A_heuristic_classification_always_requires_review()
    {
        Report(ClassificationMethod.Heuristic, confidence: 0.55).RequiresReview().ShouldBeTrue();
    }

    // ---- Lifecycle ----

    [Fact]
    public void Resolving_records_who_when_and_how_long_it_took()
    {
        var incident = Report();
        var resolvedAt = Now.AddHours(4);

        incident.Resolve("Clamp fitted", Dispatcher, resolvedAt).IsSuccess.ShouldBeTrue();

        incident.Status.ShouldBe(IncidentStatus.Resolved);
        incident.ResolvedBy.ShouldBe(Dispatcher);
        incident.Age(resolvedAt).ShouldBe(TimeSpan.FromHours(4));

        incident.DomainEvents.OfType<IncidentResolved>()
            .ShouldHaveSingleItem().TimeToResolve.ShouldBe(TimeSpan.FromHours(4));
    }

    [Fact]
    public void An_incident_cannot_be_resolved_before_it_was_reported()
    {
        Report().Resolve(null, Dispatcher, Now.AddHours(-1))
            .Error.Code.ShouldBe("Incident.ResolvedBeforeReported");
    }

    [Fact]
    public void A_closed_incident_cannot_be_triaged_or_resolved_again()
    {
        var incident = Report();
        incident.Resolve(null, Dispatcher, Now);

        incident.Triage(IncidentCategory.Leak, IncidentSeverity.Low, null, Dispatcher, Now)
            .Error.Code.ShouldBe("Incident.NotOpen");
        incident.Resolve(null, Dispatcher, Now).Error.Code.ShouldBe("Incident.NotOpen");
    }

    [Fact]
    public void Only_a_resolved_incident_can_be_closed()
    {
        var incident = Report();

        incident.Close().Error.Code.ShouldBe("Incident.NotResolved");

        incident.Resolve(null, Dispatcher, Now);
        incident.Close().IsSuccess.ShouldBeTrue();
        incident.Status.ShouldBe(IncidentStatus.Closed);
    }

    [Fact]
    public void Escalation_must_raise_the_severity()
    {
        // Lowering severity goes through triage, which records who decided that and when.
        // Allowing Escalate to do it quietly would lose that accountability.
        var incident = Report(severity: IncidentSeverity.High);

        incident.Escalate(IncidentSeverity.Moderate, "calmed down").Error.Code
            .ShouldBe("Incident.NotAnEscalation");
        incident.Escalate(IncidentSeverity.Critical, "spreading").IsSuccess.ShouldBeTrue();

        incident.DomainEvents.OfType<IncidentEscalated>()
            .ShouldHaveSingleItem().ToSeverity.ShouldBe(IncidentSeverity.Critical);
    }

    [Fact]
    public void An_incident_cannot_duplicate_itself()
    {
        var incident = Report();

        incident.MarkDuplicateOf(incident.Id).Error.Code.ShouldBe("Incident.SelfDuplicate");
    }

    [Fact]
    public void Marking_a_duplicate_closes_it_and_records_the_original()
    {
        var incident = Report();
        var original = Guid.CreateVersion7();

        incident.MarkDuplicateOf(original).IsSuccess.ShouldBeTrue();

        incident.Status.ShouldBe(IncidentStatus.Duplicate);
        incident.DuplicateOfIncidentId.ShouldBe(original);
        incident.IsOpen.ShouldBeFalse();
    }

    // ---- Location and asset ----

    [Fact]
    public void An_asset_link_raises_an_event_only_when_it_changes()
    {
        var incident = Report();
        var assetId = Guid.CreateVersion7();

        incident.LinkToAsset(assetId);
        incident.ClearDomainEvents();
        incident.LinkToAsset(assetId);

        incident.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_position_is_stored_when_supplied()
    {
        var incident = Report();

        incident.SetLocation(GeoCoordinate.Create(51.5341, -0.1352).Value);

        incident.Location.ShouldNotBeNull();
        incident.Location.Latitude.ShouldBe(51.5341, 0.0001);
    }

    [Fact]
    public void An_anonymous_report_is_accepted()
    {
        // Anonymity is normal for public reporting and must not feel like an omission.
        var incident = Report();

        incident.RecordReporter(null, null);

        incident.ReporterName.ShouldBeNull();
        incident.ReporterContact.ShouldBeNull();
    }
}
