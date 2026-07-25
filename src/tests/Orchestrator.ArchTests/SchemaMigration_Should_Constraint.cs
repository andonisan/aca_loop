using Shouldly;

namespace Orchestrator.ArchTests;

/// <summary>
/// Exactly one host may migrate the configstore schema, and it is the Portal.
/// <para>
/// This is the rule design D8 rests on. Receivers compose the ConfigStore module to <i>read</i> a schema
/// they do not own, and the product runs one receiver per client stack — so if a receiver ever migrated,
/// every deploy would race hundreds of processes against one schema. The safety comes from migration
/// being a call a host makes explicitly rather than something <c>InitializeAsync</c> does for everyone,
/// which means it is also exactly one line away from being added to the wrong host by someone who does
/// not know why it is missing. Hence a test rather than a comment.
/// </para>
/// <para>
/// Source text, not IL: the thing worth catching is someone <i>writing</i> the call, and a host that fails
/// to compile has not been caught by anything else yet. It also keeps the check readable — matching a
/// method name is obvious in a way that walking call graphs is not.
/// </para>
/// <para>
/// The cost of matching text is that it cannot tell code from prose: a host comment that merely
/// <i>mentions</i> one of these names fails this test. That is confirmed behaviour, not a guess — it is
/// how this test was verified to fail at all. If you hit it while writing a comment, reword the comment;
/// the alternative is a call-graph walk that is far harder to read than the rule it protects.
/// </para>
/// </summary>
public class SchemaMigration_Should_Constraint
{
    const string PermittedHost = "Orchestrator.Portal";

    static readonly string[] MigrationCalls =
    [
        "MigrateConfigStoreAsync",
        "Database.MigrateAsync",
        "Database.Migrate(",
    ];

    [Fact]
    public void Migration_Should_HappenInThePortalAlone()
    {
        var hosts = SourceRoot();

        // Discovery finding nothing would make this pass while checking nothing -- the failure mode the
        // test-naming ArchTest already had once.
        hosts.ShouldNotBeNull("could not locate src/root from the test output directory");

        var hostDirectories = Directory
            .GetDirectories(hosts)
            .Select(d => new DirectoryInfo(d))
            .ToList();

        hostDirectories.ShouldNotBeEmpty("discovered no hosts under src/root");

        var offenders = hostDirectories
            .Where(host => host.Name != PermittedHost)
            .SelectMany(host =>
                Directory
                    .EnumerateFiles(host.FullName, "*.cs", SearchOption.AllDirectories)
                    .Where(IsNotBuildOutput)
                    .Where(file => MigrationCalls.Any(File.ReadAllText(file).Contains))
                    .Select(file => $"{host.Name}: {Path.GetFileName(file)}")
            )
            .Order()
            .ToList();

        offenders.ShouldBeEmpty(
            $"only {PermittedHost} may migrate the configstore schema (design D8)"
        );
    }

    /// <summary>Generated and copied files under bin/obj are not authored code and can legitimately
    /// mention anything.</summary>
    static bool IsNotBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    /// <summary>Walks up to the solution file, the same way <see cref="TestNaming_Should_Constraint"/>
    /// finds test assemblies, so neither test needs a path baked into it.</summary>
    static string? SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "orchestrator.slnx"))
        )
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return null;
        }

        var root = Path.Combine(directory.FullName, "root");

        return Directory.Exists(root) ? root : null;
    }
}
