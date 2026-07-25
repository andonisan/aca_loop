using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;

namespace Orchestrator.ArchTests;

/// <summary>
/// Enforces the repo-wide <c>Subject_Should_Constraint</c> test-naming convention across <b>every</b> test
/// assembly, not just this one.
/// <para>
/// It used to call <c>Assembly.GetExecutingAssembly()</c>, which meant it only ever checked ArchTests' own
/// handful of methods — the convention was unenforced everywhere it actually mattered, while a green test
/// suggested otherwise. Assemblies are discovered from disk instead, the same way
/// <see cref="ModuleAssemblies"/> finds modules at runtime, so a new test project is covered without
/// editing this file and without ArchTests taking a project reference on every suite (which would make
/// xunit try to run their tests from here too).
/// </para>
/// <para>
/// The cost of reading from disk: this sees whatever was last <i>built</i>, not what is currently on disk
/// as source. A full <c>dotnet test</c> over the solution builds everything first so they agree, but
/// running this project alone right after editing another suite can report an offender that no longer
/// exists — or miss one that does. If a failure here names something you cannot find, rebuild before
/// believing it.
/// </para>
/// </summary>
public partial class TestNaming_Should_Constraint
{
    [Fact]
    public void TestMethods_Should_FollowSubjectShouldConstraintNaming()
    {
        var assemblies = TestAssemblies();

        // If discovery finds nothing, the test would pass vacuously and be exactly as useless as the
        // version this replaced. Fail instead.
        assemblies.ShouldNotBeEmpty("discovered no test assemblies to check");

        var offenders = assemblies
            .SelectMany(SafeGetTypes)
            .SelectMany(t =>
                t.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            .Where(m =>
                m.GetCustomAttributes()
                    .Any(a => a.GetType().Name is "FactAttribute" or "TheoryAttribute")
            )
            .Where(m => !NamingPattern().IsMatch(m.Name))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .Order()
            .ToList();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// Every built test assembly, found by walking up to the solution file and then looking through each
    /// test project's output. Reading from disk rather than referencing the projects keeps the suites
    /// independent of each other.
    /// </summary>
    static IReadOnlyList<Assembly> TestAssemblies()
    {
        var src = new DirectoryInfo(AppContext.BaseDirectory);
        while (src is not null && !File.Exists(Path.Combine(src.FullName, "orchestrator.slnx")))
        {
            src = src.Parent;
        }

        if (src is null)
        {
            return [];
        }

        // BaseDirectory is <Project>/bin/<Configuration>/<tfm>/, so the configuration is two levels up.
        var tfm = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = tfm.Parent?.Name ?? "Debug";

        return
        [
            .. Directory
                .GetFiles(
                    Path.Combine(src.FullName, "tests"),
                    "Orchestrator.*Tests.dll",
                    SearchOption.AllDirectories
                )
                // This build configuration only, and only each assembly in its *own* project's output --
                // test assemblies are also copied into other projects' output when referenced, and
                // loading those duplicates would double-report every offender.
                .Where(path => IsOwnOutput(path, configuration))
                .Select(TryLoad)
                .OfType<Assembly>(),
        ];
    }

    /// <summary>True when the dll sits at <c>&lt;Project&gt;/bin/&lt;Configuration&gt;/&lt;tfm&gt;/&lt;Project&gt;.dll</c>.</summary>
    static bool IsOwnOutput(string assemblyPath, string configuration)
    {
        var tfmDirectory = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);
        var configurationDirectory = tfmDirectory.Parent;
        var binDirectory = configurationDirectory?.Parent;
        var projectDirectory = binDirectory?.Parent;

        return configurationDirectory?.Name == configuration
            && binDirectory?.Name == "bin"
            && projectDirectory?.Name == Path.GetFileNameWithoutExtension(assemblyPath);
    }

    static Assembly? TryLoad(string path)
    {
        try
        {
            return Assembly.LoadFrom(path);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>A test assembly can reference something not present here; the types that did load are
    /// still worth checking.</summary>
    static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    [GeneratedRegex("^[A-Za-z0-9]+_Should_[A-Za-z0-9]+$")]
    private static partial Regex NamingPattern();
}
