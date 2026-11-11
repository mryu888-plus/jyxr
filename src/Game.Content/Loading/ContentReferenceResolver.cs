using System.Collections;
using System.Reflection;
using Game.Core.Definitions;

namespace Game.Content.Loading;

/// <summary>
/// 装配第二阶段入口：以仓库中每一个已注册定义为起点，交给 <see cref="ContentReferenceGraph"/> 解析引用。
/// 仓库新增的定义集合会被自动纳入，无需在这里登记。
/// </summary>
internal static class ContentReferenceResolver
{
    private static readonly PropertyInfo[] DefinitionCollections = typeof(InMemoryContentRepository)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(static property => property.GetIndexParameters().Length == 0 &&
            typeof(IEnumerable).IsAssignableFrom(property.PropertyType) &&
            property.PropertyType != typeof(string))
        .ToArray();

    public static void ResolveAll(InMemoryContentRepository repository) =>
        ContentReferenceGraph.ResolveAll(EnumerateDefinitions(repository), repository);

    private static IEnumerable<object> EnumerateDefinitions(InMemoryContentRepository repository)
    {
        foreach (var property in DefinitionCollections)
        {
            switch (property.GetValue(repository))
            {
                case IDictionary dictionary:
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (entry.Value is not null)
                        {
                            yield return entry.Value;
                        }
                    }

                    break;
                case IEnumerable sequence:
                    foreach (var definition in sequence)
                    {
                        if (definition is not null)
                        {
                            yield return definition;
                        }
                    }

                    break;
            }
        }
    }
}
