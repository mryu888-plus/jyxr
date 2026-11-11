using System.Text;
using System.Text.Json;
using Game.Application;
using Game.Application.Mods;
using Game.Application.Persistence;
using Game.Core.Model;
using Game.Core.Persistence;
using Game.Core.Serialization;

namespace Game.Tests;

public sealed class LocalSaveEnvelopeTests
{
    [Fact]
    public void Create_WritesMetadataAndModsBeforeTheSaveGamePayload()
    {
        var envelope = LocalSaveEnvelope.Create(CreateSave(), DateTimeOffset.UnixEpoch, [new ModVersionReference("jyxr-base", "1.0.0", SaveImpact.Structural)], "打败梅超风前");

        var json = envelope.Serialize();

        var metadataIndex = json.IndexOf("\"Metadata\"", StringComparison.Ordinal);
        var modsIndex = json.IndexOf("\"Mods\"", StringComparison.Ordinal);
        var saveIndex = json.IndexOf("\"SaveGame\"", StringComparison.Ordinal);
        Assert.InRange(metadataIndex, 0, saveIndex);
        Assert.InRange(modsIndex, 0, saveIndex);
        Assert.Equal(LocalSaveEnvelope.CurrentVersion, envelope.Version);
        Assert.Equal("英雄", envelope.Metadata!.LeaderName);
        Assert.Equal(3, envelope.Metadata.Round);
        Assert.Equal("打败梅超风前", envelope.Metadata.Note);
    }

    [Fact]
    public void HeaderReader_ReadsMetadataWithoutConsumingTheSaveGame()
    {
        var metadata = SaveMetadata.FromSaveGame(CreateSave(), "备注");
        var padding = new string('x', 512 * 1024);
        var json = "{\"Version\":4,\"SavedAtUtc\":\"2026-11-12T00:00:00+00:00\",\"Mods\":[],\"Metadata\":" +
                   JsonSerializer.Serialize(metadata, GameJson.Default) +
                   ",\"SaveGame\":{\"Padding\":\"" + padding + "\"}}";
        using var stream = new CountingStream(Encoding.UTF8.GetBytes(json));

        var header = LocalSaveEnvelopeHeaderReader.TryRead(stream);

        Assert.NotNull(header);
        Assert.Equal(4, header.Version);
        Assert.Equal("备注", header.Metadata.Note);
        Assert.Equal(metadata.LeaderName, header.Metadata.LeaderName);
        Assert.True(stream.BytesRead < 64 * 1024, $"read {stream.BytesRead} bytes");
    }

    [Fact]
    public void HeaderReader_ReturnsNullForLegacyEnvelopeSoCallerFallsBackToFullRead()
    {
        // v3 及以前: 没有 Metadata, SaveGame 紧跟在 Version 之后.
        var legacy = JsonSerializer.Serialize(new
        {
            Version = 3,
            SaveGame = CreateSave(),
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Mods = Array.Empty<ModVersionReference>(),
        }, GameJson.Default);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy));

        Assert.Null(LocalSaveEnvelopeHeaderReader.TryRead(stream));

        var envelope = LocalSaveEnvelope.Deserialize(legacy)!;
        Assert.Null(envelope.Metadata);
        Assert.Equal("英雄", envelope.ResolveMetadata().LeaderName);
    }

    [Fact]
    public void WithNote_UpgradesLegacyEnvelopeAndNormalizesNote()
    {
        var save = CreateSave();
        var legacy = new LocalSaveEnvelope(3, DateTimeOffset.UnixEpoch, null, null, save);

        var noted = legacy.WithNote("  " + new string('长', 80) + "  ");
        var cleared = noted.WithNote("   ");

        Assert.Equal(LocalSaveEnvelope.CurrentVersion, noted.Version);
        Assert.Same(save, noted.SaveGame);
        Assert.Equal(DateTimeOffset.UnixEpoch, noted.SavedAtUtc);
        Assert.Equal(SaveMetadata.MaxNoteLength, noted.Metadata!.Note!.Length);
        Assert.Null(cleared.Metadata!.Note);
    }

    private static SaveGame CreateSave()
    {
        var state = new GameState();
        state.Adventure.SetRound(3);
        var hero = TestContentFactory.CreateCharacterInstance("主角", TestContentFactory.CreateCharacterDefinition("主角") with { Name = "英雄" });
        state.Party.AddMember(hero);
        return new GameSession(state, TestContentFactory.CreateRepository()).SaveGameService.CreateSave();
    }

    private sealed class CountingStream(byte[] data) : MemoryStream(data)
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
    }
}
