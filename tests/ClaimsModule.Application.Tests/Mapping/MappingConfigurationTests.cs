using System.Collections;
using System.Reflection;
using AutoMapper;
using AutoMapper.Internal;

namespace ClaimsModule.Application.Tests.Mapping;

/// <summary>
/// The AutoMapper configuration is valid, and no map is recursive (D-67). AutoMapper 14.0.0 has
/// three DoS advisories, all of which need a map that recurses through nested or self-referential
/// types. The guard fails the build as soon as any map could recurse, which is when the "not
/// affected" analysis would stop being true.
/// </summary>
public sealed class MappingConfigurationTests
{
    private const int MaxMapDepth = 3;

    private static MapperConfiguration ApplicationConfiguration() =>
        new(cfg => cfg.AddMaps(typeof(AssemblyReference).Assembly));

    [Fact]
    public void Configuration_is_valid()
    {
        ApplicationConfiguration().AssertConfigurationIsValid();
    }

    [Fact]
    public void No_map_is_recursive_and_nesting_stays_shallow()
    {
        var problems = RecursionProblems(ApplicationConfiguration());

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Recursion_guard_detects_a_self_referential_map()
    {
        var recursive = new MapperConfiguration(cfg => cfg.CreateMap<Node, NodeDto>());

        RecursionProblems(recursive).ShouldContain(p => p.Contains(nameof(NodeDto), StringComparison.Ordinal));
    }

    /// <summary>Depth-first walk over destination types that are themselves map targets.</summary>
    internal static List<string> RecursionProblems(MapperConfiguration configuration)
    {
        var destinations = configuration.Internal().GetAllTypeMaps().Select(m => m.DestinationType).ToHashSet();
        var problems = new List<string>();

        foreach (var root in destinations)
        {
            Walk(root, [root]);
        }

        return problems.Distinct().ToList();

        void Walk(Type type, List<Type> path)
        {
            foreach (var child in MemberTypes(type).Where(destinations.Contains))
            {
                if (path.Contains(child))
                {
                    problems.Add($"Recursive map: {string.Join(" -> ", path.Append(child).Select(t => t.Name))}");
                    continue;
                }

                if (path.Count >= MaxMapDepth)
                {
                    problems.Add($"Map nesting deeper than {MaxMapDepth}: {string.Join(" -> ", path.Append(child).Select(t => t.Name))}");
                    continue;
                }

                Walk(child, [.. path, child]);
            }
        }
    }

    private static IEnumerable<Type> MemberTypes(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.PropertyType);
        var constructorParameters = type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);
        return properties.Concat(constructorParameters).Select(ElementType).Distinct();
    }

    private static Type ElementType(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }

        return type.IsArray
            ? type.GetElementType()!
            : type.GetGenericArguments().FirstOrDefault() ?? type;
    }

    private sealed class Node
    {
        public List<Node> Children { get; init; } = [];
    }

    private sealed class NodeDto
    {
        public List<NodeDto> Children { get; init; } = [];
    }
}
