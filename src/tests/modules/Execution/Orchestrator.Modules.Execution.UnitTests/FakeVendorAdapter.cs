using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Execution.UnitTests;

sealed class FakeVendorAdapter(Vendor vendor, string liveState) : IVendorAdapter
{
    public List<(string? Project, string Repo, string ItemId, string NewState)> Transitions { get; } = [];
    public List<string> Comments { get; } = [];

    public Vendor Vendor { get; } = vendor;

    public bool VerifyWebhook(ReadOnlyMemory<byte> rawBody, IReadOnlyDictionary<string, string> headers) =>
        true;

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
        Transitions.Add((project, repo, itemId, newState));
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
}
