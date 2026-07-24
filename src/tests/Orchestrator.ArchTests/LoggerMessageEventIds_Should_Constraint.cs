using System.Reflection;
using Orchestrator.BuildingBlocks.CQS;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Orchestrator.ArchTests;

public class LoggerMessageEventIds_Should_Constraint
{
    [Fact]
    public void LoggerMessageMethods_Should_HaveUniqueEventIdsAcrossTheSolution()
    {
        Assembly[] assemblies = [typeof(ISender).Assembly, .. ModuleAssemblies.Implementations];

        var eventIds = assemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .SelectMany(t =>
                t.GetMethods(
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Static
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly
                )
            )
            .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.EventId)
            .ToList();

        var duplicates = eventIds
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        duplicates.ShouldBeEmpty();
    }
}
