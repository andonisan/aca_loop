namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>
/// The code-host half of a vendor: everything needed to clone and push to the target repository, and
/// to authenticate that vendor's CLI inside the run. One implementation per vendor that hosts git
/// (GitHub, GitLab, Azure DevOps).
/// <para>
/// Separate from <see cref="IBacklogAdapter"/> because tracker and code host are independent
/// coordinates on a dispatch: a Jira-tracked item can live in a GitHub repository, so Execution
/// resolves the tracker for gate re-validation and the failure contract, and the code host for
/// credentials and environment (jira-adapter design.md D6).
/// </para>
/// </summary>
public interface ICodeHostAdapter
{
    Vendor Vendor { get; }

    /// <summary>Mints short-lived, per-run credentials for cloning and pushing to the target repo.</summary>
    Task<CloneCredentials> GetCloneCredentialsAsync(
        string? project,
        string repo,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The vendor CLI authentication environment for a run against this code host, so the target
    /// repo's own command can drive whichever CLI its host needs. Owned by the code host rather than
    /// branched on inside Execution, which is what let Azure DevOps silently ship with no CLI token
    /// at all (jira-adapter design.md D6). Keys are environment variable names; values are secrets
    /// and MUST NOT be logged.
    /// </summary>
    IReadOnlyDictionary<string, string> GetCliEnvironment(CloneCredentials credentials);
}
