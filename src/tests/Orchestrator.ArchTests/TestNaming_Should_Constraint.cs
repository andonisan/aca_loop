using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;

namespace Orchestrator.ArchTests;

/// <summary>Enforces the repo-wide <c>Subject_Should_Constraint</c> test-naming convention.</summary>
public partial class TestNaming_Should_Constraint
{
    [Fact]
    public void TestMethods_Should_FollowSubjectShouldConstraintNaming()
    {
        var offenders = Assembly
            .GetExecutingAssembly()
            .GetTypes()
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
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [GeneratedRegex("^[A-Za-z0-9]+_Should_[A-Za-z0-9]+$")]
    private static partial Regex NamingPattern();
}
