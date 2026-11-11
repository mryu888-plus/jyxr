using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Core.Affix;

namespace Game.Core.Definitions;

/// <summary>
/// 人物定义里的一件初始装备。除了装备 id，还可以带只属于这件实例的额外词条
/// （对应 legacy 角色 items 下 item 的 addition_trigger），创建角色实例时写入 <c>EquipmentInstance.ExtraAffixes</c>。
/// JSON 里既可以写对象 <c>{"id": "...", "affixes": [...]}</c>，也可以只写装备 id 字符串。
/// </summary>
[JsonConverter(typeof(InitialEquipmentEntryDefinitionJsonConverter))]
public sealed record InitialEquipmentEntryDefinition(
    ContentReference<EquipmentDefinition> Reference,
    IReadOnlyList<AffixDefinition>? Affixes = null)
{
    public InitialEquipmentEntryDefinition(string id, IReadOnlyList<AffixDefinition>? affixes = null)
        : this(new ContentReference<EquipmentDefinition>(id), affixes)
    {
    }

    public InitialEquipmentEntryDefinition(EquipmentDefinition equipment, IReadOnlyList<AffixDefinition>? affixes = null)
        : this(new ContentReference<EquipmentDefinition>(equipment.Id, equipment), affixes)
    {
    }

    [JsonIgnore]
    public string Id => Reference.Id;

    [JsonIgnore]
    public EquipmentDefinition Equipment => Reference.Definition;

    [JsonIgnore]
    public IReadOnlyList<AffixDefinition> ExtraAffixes => Affixes ?? [];
}

internal sealed class InitialEquipmentEntryDefinitionJsonConverter : JsonConverter<InitialEquipmentEntryDefinition>
{
    public override InitialEquipmentEntryDefinition Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var id = reader.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new JsonException("Initial equipment id cannot be empty.");
            }

            return new InitialEquipmentEntryDefinition(id);
        }

        var entry = JsonSerializer.Deserialize<EntryObject>(ref reader, options)
            ?? throw new JsonException("Initial equipment entry cannot be null.");
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            throw new JsonException("Initial equipment entry requires an id.");
        }

        return new InitialEquipmentEntryDefinition(entry.Id, entry.Affixes);
    }

    public override void Write(
        Utf8JsonWriter writer,
        InitialEquipmentEntryDefinition value,
        JsonSerializerOptions options)
    {
        if (value.ExtraAffixes.Count == 0)
        {
            writer.WriteStringValue(value.Id);
            return;
        }

        JsonSerializer.Serialize(writer, new EntryObject(value.Id, value.Affixes), options);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record EntryObject(string Id, IReadOnlyList<AffixDefinition>? Affixes = null);
}
