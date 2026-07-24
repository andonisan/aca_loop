namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>
/// The canonical, vendor-agnostic queue message. Carries everything the Agent needs to select an
/// adapter, mint credentials, run the mapped command, pick a runtime, and write back a failure
/// transition — without the agent consulting any vendor-specific receiver logic or loading the
/// mapping config itself. <see cref="OnFailureState"/> is resolved once, by Dispatch, from the
/// state-action-config mapping (the mapping is a Receiver-only concern — see design.md D2); a
/// successful run's write-back is owned by the target repo's own command, never this message.
/// <see cref="Runtime"/> is the raw name (e.g. "Claude"), not the <see cref="AgentRuntime"/> enum
/// itself: Dispatch always writes a value it already validated against the mapping, but the Agent
/// parses it defensively on the way in (agent-runtime-selection spec — an unparseable value must
/// fail the run through the normal failure contract, never crash message deserialization outright).
/// </summary>
public sealed record CanonicalMessage(
    Vendor Vendor,
    string? Project,
    string Repo,
    string ItemId,
    string State,
    string Action,
    string Runtime,
    string OnFailureState
);
