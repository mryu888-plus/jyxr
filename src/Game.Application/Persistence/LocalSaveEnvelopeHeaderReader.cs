using System.Text.Json;
using Game.Application.Mods;
using Game.Core.Serialization;

namespace Game.Application.Persistence;

/// <summary>
/// 只读存档文件开头的信封字段（版本、保存时间、MOD 组合、metadata），读到 SaveGame 属性名就停，
/// 不反序列化完整存档。v1~v3 的旧信封没有 metadata，或 metadata 排在 SaveGame 之后时返回 null，调用方回退到完整读取。
/// </summary>
public static class LocalSaveEnvelopeHeaderReader
{
    private const int InitialChunkSize = 16 * 1024;
    private const int MaxHeaderBytes = 4 * 1024 * 1024;

    public static LocalSaveEnvelopeHeader? TryRead(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var buffer = new byte[InitialChunkSize];
        var filled = 0;
        var endOfStream = false;
        while (true)
        {
            if (filled == buffer.Length)
            {
                if (buffer.Length >= MaxHeaderBytes)
                {
                    return null;
                }

                Array.Resize(ref buffer, buffer.Length * 2);
            }

            var read = stream.Read(buffer, filled, buffer.Length - filled);
            if (read == 0)
            {
                endOfStream = true;
            }

            filled += read;
            var result = TryParse(buffer.AsSpan(0, filled), endOfStream);
            if (result.Complete || endOfStream)
            {
                return result.Header;
            }
        }
    }

    private static (bool Complete, LocalSaveEnvelopeHeader? Header) TryParse(ReadOnlySpan<byte> data, bool isFinalBlock)
    {
        var reader = new Utf8JsonReader(data, isFinalBlock, default);
        int? version = null;
        DateTimeOffset? savedAtUtc = null;
        IReadOnlyList<ModVersionReference>? mods = null;
        SaveMetadata? metadata = null;
        try
        {
            if (!reader.Read())
            {
                return (false, null);
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return (true, null);
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
                {
                    return (true, Build(version, savedAtUtc, mods, metadata));
                }

                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                {
                    continue;
                }

                var name = reader.GetString() ?? string.Empty;
                if (string.Equals(name, nameof(LocalSaveEnvelope.SaveGame), StringComparison.OrdinalIgnoreCase))
                {
                    // 摘要字段都排在 SaveGame 之前; 到这里还没有 metadata 就是旧格式.
                    return (true, Build(version, savedAtUtc, mods, metadata));
                }

                if (!reader.Read())
                {
                    return (false, null);
                }

                var valueStart = (int)reader.TokenStartIndex;
                if (!reader.TrySkip())
                {
                    return (false, null);
                }

                var value = data[valueStart..(int)reader.BytesConsumed];
                if (string.Equals(name, nameof(LocalSaveEnvelope.Version), StringComparison.OrdinalIgnoreCase))
                {
                    version = JsonSerializer.Deserialize<int>(value, GameJson.Default);
                }
                else if (string.Equals(name, nameof(LocalSaveEnvelope.SavedAtUtc), StringComparison.OrdinalIgnoreCase))
                {
                    savedAtUtc = JsonSerializer.Deserialize<DateTimeOffset>(value, GameJson.Default);
                }
                else if (string.Equals(name, nameof(LocalSaveEnvelope.Mods), StringComparison.OrdinalIgnoreCase))
                {
                    mods = JsonSerializer.Deserialize<List<ModVersionReference>>(value, GameJson.Default);
                }
                else if (string.Equals(name, nameof(LocalSaveEnvelope.Metadata), StringComparison.OrdinalIgnoreCase))
                {
                    metadata = JsonSerializer.Deserialize<SaveMetadata>(value, GameJson.Default);
                }
            }

            return (false, null);
        }
        catch (JsonException) when (!isFinalBlock)
        {
            return (false, null);
        }
        catch (JsonException)
        {
            return (true, null);
        }
    }

    private static LocalSaveEnvelopeHeader? Build(
        int? version,
        DateTimeOffset? savedAtUtc,
        IReadOnlyList<ModVersionReference>? mods,
        SaveMetadata? metadata) =>
        version is { } resolvedVersion && metadata is not null
            ? new LocalSaveEnvelopeHeader(resolvedVersion, savedAtUtc, mods, metadata)
            : null;
}
