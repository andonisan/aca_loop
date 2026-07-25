using Azure.Storage.Queues;
using Testcontainers.Azurite;

namespace Orchestrator.Portal.FunctionalTests;

/// <summary>
/// One Azurite container shared by a whole test class. A fixture rather than <c>IAsyncLifetime</c> on the
/// test class itself, because xUnit constructs a new class instance per test method — which would start and
/// tear down a container per test.
/// <para>
/// Isolation comes from a fresh, uniquely-named queue per test instead, which is both faster and stronger:
/// sharing one queue across tests would make each test's message count depend on the ones before it.
/// </para>
/// <para>
/// <b>Running these needs two environment variables on a Docker Desktop / macOS host</b>, and the failure
/// mode of each is a hang rather than an error, so they are worth knowing before losing ten minutes to it:
/// <list type="bullet">
/// <item><description><c>DOCKER_HOST=unix://$HOME/.docker/run/docker.sock</c> — Docker Desktop does not
/// put the socket where Testcontainers looks by default.</description></item>
/// <item><description><c>TESTCONTAINERS_RYUK_DISABLED=true</c> — Testcontainers otherwise starts a
/// <c>testcontainers/ryuk</c> reaper container first, pulled from Docker Hub. Where Docker Hub is slow or
/// unreachable (it was, from the machine this was written on, while Microsoft's MCR was fine) the pull
/// blocks before any test runs. Disabling it is safe here because this fixture disposes its container
/// explicitly; the reaper only matters when a test process dies without cleaning up.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    // Pinned: a floating emulator tag would let this suite's meaning drift with no edit to it.
    readonly AzuriteContainer _azurite = new AzuriteBuilder(
        "mcr.microsoft.com/azure-storage/azurite:3.35.0"
    ).Build();

    public async Task InitializeAsync() => await _azurite.StartAsync();

    public async Task DisposeAsync() => await _azurite.DisposeAsync();

    /// <summary>A created, empty queue with a name no other test uses.</summary>
    public async Task<QueueClient> CreateQueueAsync()
    {
        var client = new QueueClient(
            _azurite.GetConnectionString(),
            $"q{Guid.NewGuid():N}",
            // Two settings, for two different reasons.
            //
            // MessageEncoding matches the dispatch pipeline's, so what this reads is shaped like what the
            // receiver writes.
            //
            // ServiceVersion is pinned back because the SDK defaults to a newer x-ms-version than this
            // Azurite build accepts -- it answers InvalidHeaderValue. That is an emulator limitation, not
            // a product constraint: real Azure accepts the SDK default, so this is deliberately set here
            // in the test harness rather than on the production client.
            new QueueClientOptions(QueueClientOptions.ServiceVersion.V2024_11_04)
            {
                MessageEncoding = QueueMessageEncoding.None,
            }
        );

        await client.CreateIfNotExistsAsync();
        return client;
    }
}
