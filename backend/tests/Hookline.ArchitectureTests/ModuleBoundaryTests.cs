using System.Reflection;

using Hookline.Modules.Sample;
using Hookline.SharedKernel.Modules;

using NetArchTest.Rules;

namespace Hookline.ArchitectureTests;

/// <summary>
/// Enforces the modular-monolith boundaries from the architecture guide. An illegal
/// reference fails CI rather than slipping in. As real modules land, add their
/// assemblies to <see cref="ModuleAssemblies"/> and these rules cover them automatically.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly SharedKernel = typeof(IModule).Assembly;
    private static readonly Assembly Infrastructure = typeof(Hookline.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(SampleModule).Assembly,
    ];

    [Fact]
    public void SharedKernel_does_not_depend_on_infrastructure_host_or_modules()
    {
        var result = Types.InAssembly(SharedKernel)
            .ShouldNot()
            .HaveDependencyOnAny("Hookline.Infrastructure", "Hookline.Host", "Hookline.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe("SharedKernel leaks a dependency", result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_modules_or_host()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny("Hookline.Host", "Hookline.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe("Infrastructure leaks a dependency", result));
    }

    [Fact]
    public void Modules_do_not_reference_the_host()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("Hookline.Host")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe($"{Name(assembly)} references the host", result));
        }
    }

    [Fact]
    public void Modules_do_not_reference_other_modules()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var otherModules = ModuleAssemblies
                .Where(a => a != assembly)
                .Select(Name)
                .ToArray();

            if (otherModules.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(otherModules)
                .GetResult();

            Assert.True(result.IsSuccessful, Describe($"{Name(assembly)} references another module", result));
        }
    }

    [Fact]
    public void Domain_types_do_not_depend_on_infrastructure()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .ResideInNamespaceContaining(".Domain")
                .ShouldNot()
                .HaveDependencyOn("Hookline.Infrastructure")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe($"{Name(assembly)} Domain depends on Infrastructure", result));
        }
    }

    private static string Name(Assembly assembly) => assembly.GetName().Name!;

    private static string Describe(string message, TestResult result) =>
        $"{message}: {string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}";
}
