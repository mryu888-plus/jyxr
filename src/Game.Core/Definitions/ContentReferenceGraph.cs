using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Core.Abstractions;

namespace Game.Core.Definitions;

/// <summary>
/// 定义在引用解析阶段需要执行的自有步骤（例如按参数绑定自定义战斗效果处理器）。
/// 只能处理自身持有的数据；不得调用其它顶层定义的解析，以保证解析与注册顺序无关、允许定义间循环引用。
/// 自身数据里的 <see cref="IContentReference"/> 不需要在这里处理，由 <see cref="ContentReferenceGraph"/> 统一解析；
/// 但钩子在解析过程中新产生的对象（不在序列化成员里）必须自行交给 <see cref="ContentReferenceGraph.Resolve"/>。
/// </summary>
public interface IContentResolutionHook
{
    void OnResolve(IContentRepository contentRepository);
}

/// <summary>
/// 内容装配第二阶段的统一引用解析：所有定义先注册进仓库（第一阶段），再从每个顶层定义出发遍历其序列化成员，
/// 解析遇到的每个 <see cref="IContentReference"/>，并执行 <see cref="IContentResolutionHook"/>。
/// 新增定义类型或内嵌条目（affix、battle effect、自定义效果参数…）只要用引用类型声明字段就会被解析，不需要登记入口。
/// </summary>
public static class ContentReferenceGraph
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> MembersByType = new();

    public static void Resolve(object root, IContentRepository contentRepository) =>
        new Walker(contentRepository).Visit(root, Describe(root));

    /// <summary>以多个顶层定义为起点解析；共享对象只遍历一次。</summary>
    public static void ResolveAll(IEnumerable<object> roots, IContentRepository contentRepository)
    {
        var walker = new Walker(contentRepository);
        foreach (var root in roots)
        {
            walker.Visit(root, Describe(root));
        }
    }

    private static string Describe(object value)
    {
        var id = value.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value) as string;
        var kind = ContentReferenceLookup.KindName(value.GetType());
        return id is null ? kind : $"{kind} '{id}'";
    }

    private sealed class Walker(IContentRepository contentRepository)
    {
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        public void Visit(object? value, string path)
        {
            if (value is null || IsLeaf(value.GetType()))
            {
                return;
            }

            if (value is IContentReference reference)
            {
                try
                {
                    reference.Resolve(contentRepository);
                }
                catch (ContentReferenceException exception)
                {
                    throw new InvalidOperationException($"{path} references missing {exception.TargetKind} '{exception.ReferenceId}'.", exception);
                }

                return;
            }

            if (!_visited.Add(value))
            {
                return;
            }

            if (value is IContentResolutionHook hook)
            {
                try
                {
                    hook.OnResolve(contentRepository);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"{path}: {exception.Message}", exception);
                }
            }

            switch (value)
            {
                case IDictionary dictionary:
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        Visit(entry.Value, $"{path}[{entry.Key}]");
                    }

                    return;
                case IEnumerable sequence:
                    var index = 0;
                    foreach (var item in sequence)
                    {
                        Visit(item, $"{path}[{index++}]");
                    }

                    return;
            }

            foreach (var member in MembersByType.GetOrAdd(value.GetType(), FindDataMembers))
            {
                Visit(member.GetValue(value), $"{path}.{JsonMemberName(member)}");
            }
        }

        private static bool IsLeaf(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(JsonElement) || type == typeof(DateTime) || type == typeof(TimeSpan) ||
            Nullable.GetUnderlyingType(type) is { } underlying && IsLeaf(underlying);
    }

    private static string JsonMemberName(PropertyInfo member) =>
        member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(member.Name);

    /// <summary>序列化成员：可读、带 set/init、未标 JsonIgnore 的公开实例属性。计算出来的便捷属性不在其中。</summary>
    private static PropertyInfo[] FindDataMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property =>
                property.GetIndexParameters().Length == 0 &&
                property.GetMethod is { IsPublic: true } &&
                property.SetMethod is not null &&
                property.GetCustomAttribute<JsonIgnoreAttribute>() is null &&
                !IsLeafType(property.PropertyType))
            .ToArray();

    private static bool IsLeafType(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
        type == typeof(JsonElement) ||
        Nullable.GetUnderlyingType(type) is { } underlying && IsLeafType(underlying);
}
