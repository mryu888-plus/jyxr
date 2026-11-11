using System.Text.Json;
using Game.Application.Mods;
using Game.Core.Model;
using Game.Core.Persistence;
using Game.Core.Serialization;

namespace Game.Application.Persistence;

/// <summary>
/// 本地存档文件的外层信封。v4 起把列表页需要的摘要放进 <see cref="Metadata"/>，并在序列化时
/// 排在体积大的 <see cref="SaveGame"/> 之前，存档列表只需流式读取文件开头。v1~v3 没有 metadata，列表回退到完整读取。
/// </summary>
public sealed record LocalSaveEnvelope(
    int Version,
    DateTimeOffset SavedAtUtc,
    IReadOnlyList<ModVersionReference>? Mods,
    SaveMetadata? Metadata,
    SaveGame SaveGame)
{
    public const int CurrentVersion = 4;

    public static LocalSaveEnvelope Create(
        SaveGame saveGame,
        DateTimeOffset savedAtUtc,
        IReadOnlyList<ModVersionReference>? mods,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(saveGame);
        return new LocalSaveEnvelope(
            CurrentVersion,
            savedAtUtc,
            mods,
            SaveMetadata.FromSaveGame(saveGame, note),
            saveGame);
    }

    /// <summary>当前信封的摘要：有 metadata 用 metadata，旧存档从 SaveGame 现算。</summary>
    public SaveMetadata ResolveMetadata() => Metadata ?? SaveMetadata.FromSaveGame(SaveGame, note: null);

    public LocalSaveEnvelope WithNote(string? note) => this with
    {
        Version = CurrentVersion,
        Metadata = ResolveMetadata() with { Note = SaveMetadata.NormalizeNote(note) },
    };

    public string Serialize() => JsonSerializer.Serialize(this, GameJson.Default);

    public static LocalSaveEnvelope? Deserialize(string json) =>
        JsonSerializer.Deserialize<LocalSaveEnvelope>(json, GameJson.Default);
}

/// <summary>存档列表展示用的摘要，以及玩家写的备注。</summary>
public sealed record SaveMetadata(
    string? LeaderName,
    string? LeaderPortrait,
    int PartyMemberCount,
    int Round,
    GameDifficulty Difficulty,
    ClockRecord? Clock,
    string? CurrentMapId,
    bool NoRegret,
    long PlayTimeSeconds,
    string? Note = null)
{
    public const int MaxNoteLength = 60;

    public static SaveMetadata FromSaveGame(SaveGame saveGame, string? note)
    {
        ArgumentNullException.ThrowIfNull(saveGame);
        var leaderId = saveGame.Party.MemberIds.FirstOrDefault();
        var leader = leaderId is null
            ? saveGame.Characters.FirstOrDefault()
            : saveGame.Characters.FirstOrDefault(character => string.Equals(character.Id, leaderId, StringComparison.Ordinal))
              ?? saveGame.Characters.FirstOrDefault();

        return new SaveMetadata(
            leader?.Name,
            leader?.Portrait,
            saveGame.Party.MemberIds.Count,
            saveGame.Adventure.Round,
            saveGame.Adventure.Difficulty,
            saveGame.Clock,
            saveGame.Location.CurrentMapId,
            saveGame.Adventure.NoRegret,
            saveGame.PlayTimeSeconds,
            NormalizeNote(note));
    }

    public static string? NormalizeNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaxNoteLength ? trimmed : trimmed[..MaxNoteLength];
    }
}

/// <summary>流式读取得到的信封开头部分；不包含 SaveGame。</summary>
public sealed record LocalSaveEnvelopeHeader(
    int Version,
    DateTimeOffset? SavedAtUtc,
    IReadOnlyList<ModVersionReference>? Mods,
    SaveMetadata Metadata);
