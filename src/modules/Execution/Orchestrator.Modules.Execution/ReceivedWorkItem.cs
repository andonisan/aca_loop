using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Execution;

public sealed record ReceivedWorkItem(
    string MessageId,
    string PopReceipt,
    long DequeueCount,
    CanonicalMessage Payload
);
