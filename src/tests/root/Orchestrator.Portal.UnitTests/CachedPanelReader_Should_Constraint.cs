using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Orchestrator.Portal.ReadModel;
using Orchestrator.Portal.Registry;
using Shouldly;

namespace Orchestrator.Portal.UnitTests;

/// <summary>
/// The read-model's two cross-cutting guarantees live here rather than in each source, so these tests
/// are what make them true everywhere: a failing source degrades its own panel instead of the page, and
/// repeated views do not become repeated Azure calls.
/// </summary>
public class CachedPanelReader_Should_Constraint
{
    static readonly RegisteredStack Stack = new(
        "dsconnect-dev",
        "ds-connect (dev)",
        "mappings/dsconnect-dev.mapping.yaml",
        new StackAzureCoordinates(
            "00000000-0000-0000-0000-000000000000",
            "rg",
            new Uri("https://acct.queue.core.windows.net"),
            "ai-jobs",
            "agent-job",
            "11111111-1111-1111-1111-111111111111"
        )
    );

    static (CachedPanelReader Reader, FakeTimeProvider Time) Build()
    {
        var time = new FakeTimeProvider();
        // One clock for both the RetrievedAt stamp and TTL expiry, so these tests drive staleness
        // with time.Advance(...) rather than by sleeping.
        var reader = new CachedPanelReader(time, NullLogger<CachedPanelReader>.Instance);

        return (reader, time);
    }

    sealed class CountingSource(TimeSpan ttl, Func<int, int> read) : IStackSource<int>
    {
        public int Calls { get; private set; }

        public string Name => "counting";

        public TimeSpan Ttl { get; } = ttl;

        public Task<int> ReadAsync(
            RegisteredStack stack,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return Task.FromResult(read(Calls));
        }
    }

    [Fact]
    public async Task ReadAsync_Should_ReturnTheSourceValue()
    {
        var (reader, _) = Build();
        var source = new CountingSource(TimeSpan.FromSeconds(10), _ => 42);

        var panel = await reader.ReadAsync(source, Stack);

        panel.IsAvailable.ShouldBeTrue();
        panel.Value.ShouldBe(42);
        panel.Unavailable.ShouldBeNull();
    }

    [Fact]
    public async Task ReadAsync_Should_ServeFromCacheWithinTheSourcesTtl()
    {
        var (reader, time) = Build();
        var source = new CountingSource(TimeSpan.FromSeconds(10), calls => calls);

        await reader.ReadAsync(source, Stack);
        time.Advance(TimeSpan.FromSeconds(9));
        var second = await reader.ReadAsync(source, Stack);

        // A dashboard polling faster than the TTL must not turn into one Azure call per poll.
        source.Calls.ShouldBe(1);
        second.Value.ShouldBe(1);
    }

    [Fact]
    public async Task ReadAsync_Should_ReadAgainOnceTheTtlHasElapsed()
    {
        var (reader, time) = Build();
        var source = new CountingSource(TimeSpan.FromSeconds(10), calls => calls);

        await reader.ReadAsync(source, Stack);
        time.Advance(TimeSpan.FromSeconds(11));
        var second = await reader.ReadAsync(source, Stack);

        source.Calls.ShouldBe(2);
        second.Value.ShouldBe(2);
    }

    [Fact]
    public async Task ReadAsync_Should_CachePerStackNotGlobally()
    {
        var (reader, _) = Build();
        var source = new CountingSource(TimeSpan.FromSeconds(10), calls => calls);
        var other = Stack with { Id = "other-client" };

        var first = await reader.ReadAsync(source, Stack);
        var second = await reader.ReadAsync(source, other);

        // Two stacks must never share a cache entry -- that would show one client's queue depth under
        // another client's name.
        source.Calls.ShouldBe(2);
        first.Value.ShouldBe(1);
        second.Value.ShouldBe(2);
    }

    [Fact]
    public async Task ReadAsync_Should_ReportAFailingSourceAsUnavailableRatherThanThrowing()
    {
        var (reader, _) = Build();
        var source = new ThrowingSource("the ACA API is unreachable");

        var panel = await reader.ReadAsync(source, Stack);

        // The page renders; only this panel is marked unavailable (portal-read-model spec).
        panel.IsAvailable.ShouldBeFalse();
        panel.Value.ShouldBe(default);
        panel.Unavailable.ShouldBe("the ACA API is unreachable");
    }

    [Fact]
    public async Task ReadAsync_Should_NotHammerASourceThatIsFailing()
    {
        var (reader, time) = Build();
        var source = new ThrowingSource("timed out");

        await reader.ReadAsync(source, Stack);
        time.Advance(TimeSpan.FromSeconds(5));
        await reader.ReadAsync(source, Stack);

        // Failures are cached on the same TTL as successes: a source that is timing out is exactly the
        // one a refreshing dashboard must not pile onto.
        source.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ReadAsync_Should_RecoverWhenTheSourceComesBack()
    {
        var (reader, time) = Build();
        var source = new FlakySource(failFirstCalls: 1);

        var failed = await reader.ReadAsync(source, Stack);
        time.Advance(TimeSpan.FromSeconds(11));
        var recovered = await reader.ReadAsync(source, Stack);

        // Recovery needs no eviction or restart -- the cached failure simply expires.
        failed.IsAvailable.ShouldBeFalse();
        recovered.IsAvailable.ShouldBeTrue();
        recovered.Value.ShouldBe(7);
    }

    [Fact]
    public async Task ReadAsync_Should_StampTheTimeOfTheReadNotOfTheRender()
    {
        var (reader, time) = Build();
        var source = new CountingSource(TimeSpan.FromSeconds(10), _ => 1);
        var readAt = time.GetUtcNow();

        await reader.ReadAsync(source, Stack);
        time.Advance(TimeSpan.FromSeconds(5));
        var cached = await reader.ReadAsync(source, Stack);

        // So the UI can honestly show how old the data it is displaying actually is.
        cached.RetrievedAt.ShouldBe(readAt);
    }

    [Fact]
    public async Task ReadAsync_Should_PropagateCallerCancellationRatherThanCachingItAsAFailure()
    {
        var (reader, _) = Build();
        var source = new CancelingSource();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            reader.ReadAsync(source, Stack, cts.Token)
        );

        // A navigated-away page is not a broken source; caching it would misreport a healthy source as
        // unavailable for a full TTL.
        source.Calls.ShouldBe(1);
    }

    sealed class ThrowingSource(string message) : IStackSource<int>
    {
        public int Calls { get; private set; }

        public string Name => "throwing";

        public TimeSpan Ttl => TimeSpan.FromSeconds(10);

        public Task<int> ReadAsync(
            RegisteredStack stack,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            throw new InvalidOperationException(message);
        }
    }

    sealed class FlakySource(int failFirstCalls) : IStackSource<int>
    {
        int _calls;

        public string Name => "flaky";

        public TimeSpan Ttl => TimeSpan.FromSeconds(10);

        public Task<int> ReadAsync(
            RegisteredStack stack,
            CancellationToken cancellationToken = default
        )
        {
            _calls++;
            if (_calls <= failFirstCalls)
            {
                throw new InvalidOperationException("not yet");
            }

            return Task.FromResult(7);
        }
    }

    sealed class CancelingSource : IStackSource<int>
    {
        public int Calls { get; private set; }

        public string Name => "canceling";

        public TimeSpan Ttl => TimeSpan.FromSeconds(10);

        public Task<int> ReadAsync(
            RegisteredStack stack,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
    }
}
