using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Core.Abstractions;
using Game.Core.Definitions.Skills;

namespace Game.Core.Definitions;

/// <summary>
/// 内容定义之间的引用。JSON 中只写稳定 ID；装配第二阶段由 <see cref="ContentReferenceGraph"/> 解析成已注册的定义。
/// 定义类型只保存这一个字段，不再同时保存原始 ID 与解析结果两份状态。
/// </summary>
public interface IContentReference
{
    string Id { get; }

    bool IsResolved { get; }

    /// <summary>引用目标的中文/类型说明，用于错误信息。</summary>
    string TargetKind { get; }

    void Resolve(IContentRepository contentRepository);
}

[JsonConverter(typeof(ContentReferenceJsonConverterFactory))]
public sealed class ContentReference<TDefinition> : IContentReference, IEquatable<ContentReference<TDefinition>>
    where TDefinition : class
{
    private TDefinition? _definition;

    public ContentReference(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public ContentReference(string id, TDefinition definition)
        : this(id)
    {
        _definition = definition;
    }

    public string Id { get; }

    public bool IsResolved => _definition is not null;

    public string TargetKind => ContentReferenceLookup.KindName(typeof(TDefinition));

    public TDefinition Definition => _definition ?? throw new InvalidOperationException(
        $"Content reference to {TargetKind} '{Id}' has not been resolved.");

    public void Resolve(IContentRepository contentRepository) =>
        _definition = ContentReferenceLookup.Find<TDefinition>(contentRepository, Id)
            ?? throw new ContentReferenceException(TargetKind, Id);

    public static implicit operator ContentReference<TDefinition>(string id) => new(id);

    public bool Equals(ContentReference<TDefinition>? other) =>
        other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ContentReference<TDefinition> other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

    public override string ToString() => Id;
}

/// <summary>"任意武学"引用：外功、内功或特殊技能之一（与战斗里 SkillInstance.SourceSkillId 的取值范围一致）。</summary>
[JsonConverter(typeof(ContentReferenceJsonConverterFactory))]
public sealed class SkillSourceReference : IContentReference, IEquatable<SkillSourceReference>
{
    public SkillSourceReference(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; }

    public bool IsResolved { get; private set; }

    public string TargetKind => "skill";

    public void Resolve(IContentRepository contentRepository)
    {
        IsResolved = contentRepository.TryGetExternalSkill(Id, out _) ||
            contentRepository.TryGetInternalSkill(Id, out _) ||
            contentRepository.TryGetSpecialSkill(Id, out _);
        if (!IsResolved)
        {
            throw new ContentReferenceException(TargetKind, Id);
        }
    }

    public static implicit operator SkillSourceReference(string id) => new(id);

    public bool Equals(SkillSourceReference? other) =>
        other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SkillSourceReference other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

    public override string ToString() => Id;
}

public sealed class ContentReferenceException(string targetKind, string id)
    : InvalidOperationException($"Missing {targetKind} '{id}'.")
{
    public string TargetKind { get; } = targetKind;

    public string ReferenceId { get; } = id;
}

internal static class ContentReferenceLookup
{
    public static TDefinition? Find<TDefinition>(IContentRepository repository, string id)
        where TDefinition : class
    {
        object? found = typeof(TDefinition) switch
        {
            var type when type == typeof(TalentDefinition) => repository.TryGetTalent(id, out var value) ? value : null,
            var type when type == typeof(BuffDefinition) => repository.TryGetBuff(id, out var value) ? value : null,
            var type when type == typeof(ExternalSkillDefinition) => repository.TryGetExternalSkill(id, out var value) ? value : null,
            var type when type == typeof(InternalSkillDefinition) => repository.TryGetInternalSkill(id, out var value) ? value : null,
            var type when type == typeof(SpecialSkillDefinition) => repository.TryGetSpecialSkill(id, out var value) ? value : null,
            var type when type == typeof(LegendSkillDefinition) => repository.GetLegendSkills()
                .FirstOrDefault(definition => string.Equals(definition.Id, id, StringComparison.Ordinal)),
            var type when type == typeof(CharacterDefinition) => repository.TryGetCharacter(id, out var value) ? value : null,
            var type when type == typeof(ItemDefinition) => repository.TryGetItem(id, out var value) ? value : null,
            var type when type == typeof(EquipmentDefinition) => repository.TryGetEquipment(id, out var value) ? value : null,
            var type when type == typeof(ItemTagDefinition) => repository.TryGetItemTag(id, out var value) ? value : null,
            var type when type == typeof(ScopedBattleEffectDefinition) => repository.TryGetScopedBattleEffect(id, out var value) ? value : null,
            _ => throw new NotSupportedException($"Content references to '{typeof(TDefinition).Name}' are not supported."),
        };
        return (TDefinition?)found;
    }

    public static string KindName(Type definitionType) => definitionType.Name.EndsWith("Definition", StringComparison.Ordinal)
        ? JsonNamingPolicy.CamelCase.ConvertName(definitionType.Name[..^"Definition".Length])
        : definitionType.Name;
}

internal sealed class ContentReferenceJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(SkillSourceReference) ||
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ContentReference<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<TReference> : JsonConverter<TReference>
        where TReference : class, IContentReference
    {
        public override TReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Content reference must be a string id, got {reader.TokenType}.");
            }

            var id = reader.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new JsonException("Content reference id cannot be empty.");
            }

            return (TReference)Activator.CreateInstance(typeToConvert, id)!;
        }

        public override void Write(Utf8JsonWriter writer, TReference value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Id);
    }
}
