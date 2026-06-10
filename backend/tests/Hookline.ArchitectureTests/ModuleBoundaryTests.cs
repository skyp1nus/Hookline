using System.Reflection;

using Hookline.SharedKernel.Modules;

using NetArchTest.Rules;

namespace Hookline.ArchitectureTests;

/// <summary>
/// Enforces the modular-monolith boundaries from the architecture guide. An illegal
/// reference fails CI rather than slipping in. Module assemblies are reflection-discovered
/// (see <see cref="DiscoverModuleAssemblies"/>), so every <c>Hookline.Modules.*</c> the host
/// pulls in is covered automatically — there is no hand-maintained list to forget.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly SharedKernel = typeof(IModule).Assembly;
    private static readonly Assembly Infrastructure = typeof(Hookline.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly[] ModuleAssemblies = DiscoverModuleAssemblies();

    /// <summary>
    /// Loads every <c>Hookline.Modules.*</c> assembly sitting next to the test binary (they land
    /// there transitively through the Host reference) and returns them. A newly registered module
    /// is picked up with zero edits here.
    /// </summary>
    private static Assembly[] DiscoverModuleAssemblies()
    {
        var binDir = Path.GetDirectoryName(typeof(ModuleBoundaryTests).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(binDir, "Hookline.Modules.*.dll"))
        {
            try
            {
                Assembly.LoadFrom(dll);
            }
            catch (BadImageFormatException)
            {
                // Not a managed module assembly — skip.
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name is { } n
                && n.StartsWith("Hookline.Modules.", StringComparison.Ordinal)
                && !n.EndsWith(".Tests", StringComparison.Ordinal))
            .DistinctBy(a => a.GetName().Name)
            .OrderBy(a => a.GetName().Name)
            .ToArray();
    }

    [Fact]
    public void Module_discovery_covers_the_registered_modules()
    {
        // Guards against the rules silently going vacuous (e.g. discovery breaking or a module
        // dropping out of the build): if nothing is found, the boundary tests below assert nothing.
        Assert.NotEmpty(ModuleAssemblies);
        Assert.Contains(ModuleAssemblies, a => a.GetName().Name == "Hookline.Modules.YouTubeUploads");
    }

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
