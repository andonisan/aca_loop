using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.Dispatch.Contracts;
using Shouldly;

namespace Orchestrator.Modules.Dispatch.UnitTests;

/// <summary>
/// The mapping loader can only check that a <c>codeHost</c> names a known vendor; whether that vendor
/// actually has a code-host adapter in *this* deployment is knowable only once the container exists.
/// These pin that the check happens at startup rather than surfacing as a failed run later — the case
/// it catches in practice is a target naming a vendor whose module was never composed, e.g.
/// <c>codeHost: GitLab</c> where <c>enable_gitlab</c> is false.
/// </summary>
public class CodeHostRegistration_Should_Constraint
{
    static MappingConfig ConfigWith(Vendor tracker, Vendor codeHost) =>
        new([
            new TargetMapping(
                tracker,
                Project: null,
                Repo: "acme/widgets",
                States: new Dictionary<string, StateMapping>
                {
                    ["status:ready-for-proposal"] = new(
                        "/ds:propose",
                        AgentRuntime.Claude,
                        null,
                        "status:blocked"
                    ),
                },
                CodeHost: codeHost,
                CodeRepo: "acme/widgets"
            ),
        ]);

    static IServiceProvider ProviderWith(MappingConfig config, params Vendor[] registeredCodeHosts)
    {
        var services = new ServiceCollection();
        services.AddSingleton(config);
        foreach (var vendor in registeredCodeHosts)
        {
            services.AddSingleton<ICodeHostAdapter>(new FakeCodeHostAdapter(vendor));
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InitializeAsync_Should_SucceedWhenTheCodeHostIsRegistered()
    {
        var provider = ProviderWith(ConfigWith(Vendor.GitHub, Vendor.GitHub), Vendor.GitHub);

        await new DispatchModule().InitializeAsync(provider);
    }

    [Fact]
    public async Task InitializeAsync_Should_ThrowWhenTheCodeHostHasNoRegisteredAdapter()
    {
        // A Jira-tracked target pointing at a GitLab repo, in a deployment where GitLab was not
        // composed. Nothing structural is wrong with the mapping, so only this check catches it.
        var provider = ProviderWith(ConfigWith(Vendor.Jira, Vendor.GitLab), Vendor.GitHub);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            new DispatchModule().InitializeAsync(provider)
        );

        exception.Message.ShouldContain("codeHost 'GitLab'");
        exception.Message.ShouldContain("no registered code-host adapter");
    }

    [Fact]
    public async Task InitializeAsync_Should_NotAcceptATrackerOnlyVendorAsACodeHost()
    {
        // Jira implements no code-host contract at all, so it can never be registered as one; a
        // mapping naming it must fail before traffic rather than at credential-minting time.
        var provider = ProviderWith(ConfigWith(Vendor.Jira, Vendor.Jira), Vendor.GitHub);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            new DispatchModule().InitializeAsync(provider)
        );

        exception.Message.ShouldContain("codeHost 'Jira'");
    }

    sealed class FakeCodeHostAdapter(Vendor vendor) : ICodeHostAdapter
    {
        public Vendor Vendor { get; } = vendor;

        public Task<CloneCredentials> GetCloneCredentialsAsync(
            string? project,
            string repo,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public IReadOnlyDictionary<string, string> GetCliEnvironment(
            CloneCredentials credentials
        ) => throw new NotSupportedException();
    }
}
