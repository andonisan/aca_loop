using NetArchTest.Rules;
using Shouldly;

namespace Orchestrator.ArchTests;

public class ModuleBoundary_Should_Constraint
{
    [Fact]
    public void Modules_Should_NotReferenceAnotherModuleImplementationDirectly()
    {
        // An actual assembly-reference check, not a namespace-string prefix match: a module's
        // .Contracts project (e.g. "Orchestrator.Modules.Backlog.Contracts") is a separate assembly
        // whose name happens to start with its owning module's name, so a naive string-prefix check
        // against the bare module name would also flag the legitimate, allowed dependency on that
        // sibling .Contracts assembly.
        var failures = new List<string>();

        foreach (var module in ModuleAssemblies.Implementations)
        {
            var referencedAssemblyNames = module
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToHashSet();

            foreach (var otherModule in ModuleAssemblies.Implementations)
            {
                if (ReferenceEquals(module, otherModule))
                {
                    continue;
                }

                if (referencedAssemblyNames.Contains(otherModule.GetName().Name))
                {
                    failures.Add(
                        $"{module.GetName().Name} references {otherModule.GetName().Name} directly"
                    );
                }
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Modules_Should_NotDefineMvcControllers()
    {
        var offenders = ModuleAssemblies
            .Implementations.SelectMany(a => a.GetTypes())
            .Where(IsMvcController)
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    static bool IsMvcController(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (
                current.FullName
                is "Microsoft.AspNetCore.Mvc.Controller"
                    or "Microsoft.AspNetCore.Mvc.ControllerBase"
            )
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Interfaces_Should_HaveIPrefix()
    {
        foreach (var module in ModuleAssemblies.Implementations)
        {
            var result = Types
                .InAssembly(module)
                .That()
                .AreInterfaces()
                .Should()
                .HaveNameStartingWith("I")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue();
        }
    }
}
