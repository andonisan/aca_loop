using Orchestrator.Constants;
using Orchestrator.Portal.Presentation;
using Shouldly;

namespace Orchestrator.Portal.UnitTests;

/// <summary>
/// The design system's contract is that one domain state has exactly one look, defined once. These
/// tests exist to make that contract fail loudly rather than drift: the interesting ones are the
/// coverage tests, which fail when someone adds a state without deciding how it looks.
/// </summary>
public class StatusTreatment_Should_Constraint
{
    /// <summary>
    /// The important test in this file. Every outcome the stacks can emit must have a treatment, and
    /// crucially must not fall through to the unknown-skew treatment — which is what a missing entry
    /// would otherwise look like. Driving it off <see cref="LifecycleOutcomes.All"/> means adding an
    /// outcome to the shared contract without handling it here fails, rather than shipping and
    /// rendering as "unknown outcome" in production.
    /// </summary>
    [Fact]
    public void EveryEmittableOutcome_Should_HaveItsOwnTreatment()
    {
        var unknown = StatusTreatment.ForOutcome(RunOutcome.Unknown);

        foreach (var wireValue in LifecycleOutcomes.All)
        {
            var badge = StatusTreatment.ForOutcome(wireValue);

            badge.CssClass.ShouldNotBeNullOrWhiteSpace($"'{wireValue}' has no badge class");
            badge.Label.ShouldNotBeNullOrWhiteSpace($"'{wireValue}' has no label");
            badge.ShouldNotBe(
                unknown,
                $"'{wireValue}' falls through to the unknown-skew treatment"
            );
        }
    }

    /// <summary>Same guarantee from the enum side, so a member added to <see cref="RunOutcome"/> without
    /// a switch arm fails here instead of throwing at render time.</summary>
    [Fact]
    public void EveryModelledOutcome_Should_MapWithoutThrowing()
    {
        foreach (var outcome in Enum.GetValues<RunOutcome>())
        {
            var badge = Should.NotThrow(() => StatusTreatment.ForOutcome(outcome));

            badge.Label.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void EveryExecutionState_Should_MapWithoutThrowing()
    {
        foreach (var state in Enum.GetValues<PodExecutionState>())
        {
            var badge = Should.NotThrow(() => StatusTreatment.ForExecution(state));

            badge.Label.ShouldNotBeNullOrWhiteSpace();
        }
    }

    /// <summary>
    /// The judgement from design D4 that is most likely to be "tidied up" by someone who reasons that a
    /// run which did not do its work is a failure. It is not: the item left its gate state before the pod
    /// started, so declining was correct. Pinned explicitly so the reasoning has to be argued with rather
    /// than quietly reverted.
    /// </summary>
    [Fact]
    public void StaleGate_Should_NotLookLikeFailure()
    {
        var staleGate = StatusTreatment.ForOutcome(LifecycleOutcomes.StaleGate);
        var failed = StatusTreatment.ForOutcome(LifecycleOutcomes.Failed);

        staleGate.CssClass.ShouldNotBe(failed.CssClass);
        failed.CssClass.ShouldBe("danger");
        staleGate.CssClass.ShouldBe("warn");
    }

    /// <summary>Poison means redelivery gave up and the work was dropped unrun. Sharing the failure hue
    /// would bury it in the failure count, which is the opposite of what a dropped message needs.</summary>
    [Fact]
    public void Poison_Should_BeDistinctFromFailure()
    {
        StatusTreatment
            .ForOutcome(LifecycleOutcomes.Poison)
            .CssClass.ShouldNotBe(StatusTreatment.ForOutcome(LifecycleOutcomes.Failed).CssClass);
    }

    /// <summary>A schema-skewed stack must be visible. Returning the same treatment as a real outcome
    /// would let an unrecognised value pass for a known one.</summary>
    [Theory]
    [InlineData("cancelled")]
    [InlineData("Succeeded")] // Case differs: same-constants emitters make this a contract violation.
    [InlineData("")]
    [InlineData(null)]
    public void AnUnrecognisedOutcome_Should_RenderAsSkew(string? wireValue)
    {
        var badge = StatusTreatment.ForOutcome(wireValue);

        badge.ShouldBe(StatusTreatment.ForOutcome(RunOutcome.Unknown));
        badge.Label.ShouldContain("unknown");
    }

    /// <summary>Colour is never the only signal, so no treatment may carry an empty label.</summary>
    [Fact]
    public void NoTreatment_Should_HaveAnEmptyLabel()
    {
        var all = Enum.GetValues<RunOutcome>()
            .Select(StatusTreatment.ForOutcome)
            .Concat(Enum.GetValues<PodExecutionState>().Select(StatusTreatment.ForExecution))
            .Append(StatusTreatment.ForQueueDepth(0, 10, 20));

        foreach (var badge in all)
        {
            badge.Label.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData(0, "ok")]
    [InlineData(9, "ok")]
    [InlineData(10, "warn")] // At the threshold, not merely above it.
    [InlineData(19, "warn")]
    [InlineData(20, "danger")]
    [InlineData(500, "danger")]
    public void QueueDepth_Should_BandAgainstTheSuppliedThresholds(int depth, string expected) =>
        StatusTreatment.ForQueueDepth(depth, warnAt: 10, dangerAt: 20).CssClass.ShouldBe(expected);

    [Fact]
    public void QueueDepth_Should_IncludeTheCountInItsLabel() =>
        StatusTreatment.ForQueueDepth(42, 10, 20).Label.ShouldBe("42 queued");

    /// <summary>Inverted thresholds would silently disable the danger band. They arrive from
    /// configuration, so a swapped pair is a config bug and is rejected rather than normalised.</summary>
    [Fact]
    public void QueueDepth_Should_RejectInvertedThresholds() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            StatusTreatment.ForQueueDepth(5, warnAt: 20, dangerAt: 10)
        );

    [Fact]
    public void QueueDepth_Should_RejectANegativeDepth() =>
        Should.Throw<ArgumentOutOfRangeException>(() => StatusTreatment.ForQueueDepth(-1, 10, 20));

    // The expected value is passed as a name rather than as RunOutcome itself: xUnit requires theory
    // methods to be public, and a public signature cannot take an internal enum. Widening the enum to
    // public purely to satisfy the test framework is the wrong trade, so the name is resolved inside.
    [Theory]
    [InlineData(LifecycleOutcomes.Succeeded, nameof(RunOutcome.Succeeded))]
    [InlineData(LifecycleOutcomes.Failed, nameof(RunOutcome.Failed))]
    [InlineData(LifecycleOutcomes.StaleGate, nameof(RunOutcome.StaleGate))]
    [InlineData(LifecycleOutcomes.Poison, nameof(RunOutcome.Poison))]
    [InlineData(LifecycleOutcomes.WipRefused, nameof(RunOutcome.WipRefused))]
    public void TheParser_Should_RoundTripEveryWireValue(string wireValue, string expectedName) =>
        RunOutcomeParser.Parse(wireValue).ShouldBe(Enum.Parse<RunOutcome>(expectedName));
}
