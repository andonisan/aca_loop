namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>
/// Short-lived, per-run credentials for cloning the target repo and pushing back to it. Never
/// persisted, never logged — minted fresh by the adapter for each execution (see design.md D6).
/// </summary>
public sealed record CloneCredentials(
    string Token,
    string CloneUrl,
    string GitAuthorName,
    string GitAuthorEmail
);
