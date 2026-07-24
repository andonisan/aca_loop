using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Execution.UnitTests;

/// <summary>Stands in for a vendor that is both tracker and code host, which is what every
/// currently-shipping vendor is. A tracker-only fake would implement <c>IBacklogAdapter</c> alone.</summary>
sealed class FakeVendorAdapter(Vendor vendor, string liveState) : IBacklogAdapter, ICodeHostAdapter
{
    /// <summary>A named record rather than a tuple: the inline tuple form pushed the declaration past
    /// the print width, and CSharpier's wrapping of it is rejected by this repo's IDE0055 rule.</summary>
    internal sealed record Transition(string? Project, string Repo, string ItemId, string NewState);

    public List<Transition> Transitions { get; } = [];
    public List<string> Comments { get; } = [];

    /// <summary>Simulates a workflow-constrained tracker where the requested state is not reachable
    /// from the item's current status — Jira's transition model makes this a legitimate outcome rather
    /// than an error (jira-adapter design.md D5).</summary>
    public string? TransitionFailure { get; init; }

    public Vendor Vendor { get; } = vendor;

    public bool VerifyWebhook(
        ReadOnlyMemory<byte> rawBody,
        IReadOnlyDictionary<string, string> headers
    ) => true;

    public CanonicalEvent? ParseEvent(
        ReadOnlyMemory<byte> rawBody,
        IReadOnlyDictionary<string, string> headers
    ) => null;

    public bool IsAutomationActor(string? actor) => false;

    public Task<string> ReadItemStateAsync(
        string? project,
        string repo,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(liveState);

    public Task TransitionStateAsync(
        string? project,
        string repo,
        string itemId,
        string newState,
        CancellationToken cancellationToken = default
    )
    {
        if (TransitionFailure is not null)
        {
            throw new InvalidOperationException(TransitionFailure);
        }

        Transitions.Add(new Transition(project, repo, itemId, newState));
        return Task.CompletedTask;
    }

    public Task PostCommentAsync(
        string? project,
        string repo,
        string itemId,
        string body,
        CancellationToken cancellationToken = default
    )
    {
        Comments.Add(body);
        return Task.CompletedTask;
    }

    public Task<CloneCredentials> GetCloneCredentialsAsync(
        string? project,
        string repo,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            new CloneCredentials(
                Token: "fake-token",
                CloneUrl: $"https://fake/{repo}.git",
                GitAuthorName: "fake-bot",
                GitAuthorEmail: "fake-bot@example.com"
            )
        );

    public IReadOnlyDictionary<string, string> GetCliEnvironment(CloneCredentials credentials) =>
        new Dictionary<string, string> { ["FAKE_TOKEN"] = credentials.Token };
}
