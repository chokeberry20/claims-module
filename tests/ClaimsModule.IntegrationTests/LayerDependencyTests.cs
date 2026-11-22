using System.Reflection;

namespace ClaimsModule.IntegrationTests;

/// <summary>
/// Lightweight Clean Architecture guard: checks the compiled assembly references.
/// (The NetArchTest suite is Stretch, see PLAN.md P0 task 3. This covers the project-level rule.)
/// </summary>
public sealed class LayerDependencyTests
{
    private const string Domain = "ClaimsModule.Domain";
    private const string Application = "ClaimsModule.Application";
    private const string Persistence = "ClaimsModule.Persistence";
    private const string Infrastructure = "ClaimsModule.Infrastructure";
    private const string Api = "ClaimsModule.API";

    public static TheoryData<Assembly, string[]> ForbiddenReferences => new()
    {
        { typeof(global::ClaimsModule.Domain.AssemblyReference).Assembly, [Application, Persistence, Infrastructure, Api] },
        { typeof(global::ClaimsModule.Application.AssemblyReference).Assembly, [Persistence, Infrastructure, Api] },
        { typeof(global::ClaimsModule.Persistence.AssemblyReference).Assembly, [Infrastructure, Api] },
        { typeof(global::ClaimsModule.Infrastructure.AssemblyReference).Assembly, [Persistence, Api] },
    };

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void Layer_does_not_reference_outer_layers(Assembly layer, string[] forbidden)
    {
        var referenced = layer.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        referenced.Intersect(forbidden).ShouldBeEmpty(
            $"{layer.GetName().Name} must not reference: {string.Join(", ", forbidden)}");
    }

    [Fact]
    public void Domain_has_no_project_or_package_dependencies()
    {
        var referenced = typeof(global::ClaimsModule.Domain.AssemblyReference).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal)
                        && n is not "netstandard" and not "mscorlib");

        referenced.ShouldBeEmpty();
    }
}
