using System.Linq;
using System.Text.Json;
using Game.Application;
using Game.Application.Mods;
using Game.Application.Persistence;
using Game.Core.Model;
using Game.Core.Persistence;
using Game.Core.Serialization;

namespace Game.Godot.Persistence;

public enum LocalSaveReadFailureReason
{
	None = 0,
	MissingFile,
	InvalidFormat,
	EnvelopeVersionMismatch,
	SaveVersionMismatch,
}

public enum LocalSaveKind
{
	Auto,
	Quick,
	Manual,
}

public sealed record LocalSaveId
{
	private LocalSaveId(LocalSaveKind kind, int slotIndex)
	{
		Kind = kind;
		SlotIndex = slotIndex;
	}

	public static LocalSaveId Auto { get; } = new(LocalSaveKind.Auto, 0);
	public static LocalSaveId Quick { get; } = new(LocalSaveKind.Quick, 0);

	public LocalSaveKind Kind { get; }
	public int SlotIndex { get; }
	public string Title => Kind switch
	{
		LocalSaveKind.Auto => "自动存档",
		LocalSaveKind.Quick => "快速存档",
		LocalSaveKind.Manual => $"存档{SlotIndex}",
		_ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unsupported save kind."),
	};

	public static LocalSaveId Manual(int slotIndex)
	{
		LocalSaveStore.ValidateSlotIndex(slotIndex);
		return new LocalSaveId(LocalSaveKind.Manual, slotIndex);
	}
}

public sealed class LocalSaveStore
{
	public const int SlotCount = 20;
	private readonly ModStoragePaths? _storagePaths;

	public LocalSaveStore(ModStoragePaths? storagePaths = null)
	{
		_storagePaths = storagePaths;
	}

	public string SaveCurrentSession(LocalSaveId saveId)
	{
		if (!CanWriteCurrentSession(saveId))
		{
			throw new InvalidOperationException("无悔周目只允许自动存档。");
		}

		var absolutePath = SaveCurrentSession(ResolveSavePath(saveId), GetLogName(saveId));
		if (saveId.Kind is LocalSaveKind.Quick or LocalSaveKind.Manual)
		{
			Game.ProfileService.AddSaves();
		}

		return absolutePath;
	}

	private static bool CanWriteCurrentSession(LocalSaveId saveId) =>
		!Game.State.Adventure.NoRegret || saveId.Kind == LocalSaveKind.Auto;

	private static string SaveCurrentSession(string savePath, string logName)
	{
		var envelope = LocalSaveEnvelope.Create(
			Game.SaveGameService.CreateSave(),
			DateTimeOffset.UtcNow,
			Game.ActiveModLoadout.CreateVersionReferences());
		WriteEnvelope(savePath, envelope);
		Game.Logger.Info($"Saved {logName} to '{savePath}'.");
		return savePath;
	}

	/// <summary>修改存档备注 (只改信封 metadata, 不动存档内容和保存时间)。备注为空表示清除。</summary>
	public bool SetNote(LocalSaveId saveId, string? note)
	{
		var absolutePath = ResolveSavePath(saveId);
		if (!TryReadEnvelope(absolutePath, out var envelope, out _) || envelope is null)
		{
			return false;
		}

		WriteEnvelope(absolutePath, envelope.WithNote(note));
		Game.Logger.Info($"Updated note of {GetLogName(saveId)}.");
		return true;
	}

	private static void WriteEnvelope(string savePath, LocalSaveEnvelope envelope)
	{
		var absolutePath = savePath;
		var directoryPath = Path.GetDirectoryName(absolutePath);
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			throw new InvalidOperationException($"Invalid save path: {savePath}");
		}

		Directory.CreateDirectory(directoryPath);
		File.WriteAllText(absolutePath, envelope.Serialize());
	}

	public bool TryLoad(LocalSaveId saveId, out LocalSaveEnvelope? envelope, out LocalSaveReadFailureReason failureReason)
	{
		var absolutePath = ResolveSavePath(saveId);
		if (!TryReadEnvelope(absolutePath, out envelope, out failureReason))
		{
			return false;
		}

		Game.Logger.Info($"Loaded {GetLogName(saveId)} from '{absolutePath}'.");
		return true;
	}

	public bool Delete(LocalSaveId saveId)
	{
		var absolutePath = ResolveSavePath(saveId);
		if (!File.Exists(absolutePath))
		{
			return false;
		}

		File.Delete(absolutePath);
		Game.Logger.Info($"Deleted {GetLogName(saveId)} at '{absolutePath}'.");
		return true;
	}

	public bool Exists(LocalSaveId saveId)
	{
		return File.Exists(ResolveSavePath(saveId));
	}

	public LocalSaveSummary GetSummary(LocalSaveId saveId)
	{
		if (!Exists(saveId))
		{
			return new LocalSaveSummary(saveId);
		}

		var absolutePath = ResolveSavePath(saveId);
		if (TryReadHeader(absolutePath) is { } header)
		{
			return BuildSummary(saveId, header.Metadata, header.SavedAtUtc, header.Mods);
		}

		// 旧格式 (v1~v3) 没有 metadata: 完整读取一次, 从 SaveGame 现算摘要.
		if (!TryReadEnvelope(absolutePath, out var envelope, out var failureReason) || envelope is null)
		{
			return new LocalSaveSummary(saveId, HasSave: true, FailureReason: failureReason);
		}

		return BuildSummary(saveId, envelope.ResolveMetadata(), envelope.SavedAtUtc, envelope.Mods);
	}

	private static LocalSaveEnvelopeHeader? TryReadHeader(string absolutePath)
	{
		try
		{
			using var stream = File.OpenRead(absolutePath);
			var header = LocalSaveEnvelopeHeaderReader.TryRead(stream);
			return header is { Version: >= 1 and <= LocalSaveEnvelope.CurrentVersion } ? header : null;
		}
		catch (IOException exception)
		{
			Game.Logger.Warning($"Save header read failed: {absolutePath}. {exception.Message}");
			return null;
		}
	}

	private static LocalSaveSummary BuildSummary(
		LocalSaveId saveId,
		SaveMetadata metadata,
		DateTimeOffset? savedAtUtc,
		IReadOnlyList<ModVersionReference>? mods)
	{
		var modComparison = Game.ActiveModLoadout.Compare(mods);
		return new LocalSaveSummary(
			saveId,
			HasSave: true,
			LeaderName: metadata.LeaderName,
			LeaderPortrait: metadata.LeaderPortrait,
			PartyMemberCount: metadata.PartyMemberCount,
			Round: metadata.Round,
			Difficulty: metadata.Difficulty,
			Clock: metadata.Clock,
			CurrentMapId: metadata.CurrentMapId,
			SavedAtUtc: savedAtUtc,
			NoRegret: metadata.NoRegret,
			PlayTimeSeconds: metadata.PlayTimeSeconds,
			Note: metadata.Note,
			ModWarningImpact: modComparison.WarningImpact,
			ModDifferences: modComparison.Differences);
	}

	internal static void ValidateSlotIndex(int slotIndex)
	{
		if (slotIndex < 1 || slotIndex > SlotCount)
		{
			throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, $"存档槽必须在 1 到 {SlotCount} 之间。");
		}
	}

	private string ResolveSavePath(LocalSaveId saveId) => saveId.Kind switch
	{
		LocalSaveKind.Auto => StoragePaths.AutoSavePath,
		LocalSaveKind.Quick => StoragePaths.QuickSavePath,
		LocalSaveKind.Manual => StoragePaths.GetSaveSlotPath(saveId.SlotIndex),
		_ => throw new ArgumentOutOfRangeException(nameof(saveId), saveId, "Unsupported save kind."),
	};

	private static string GetLogName(LocalSaveId saveId) => saveId.Kind switch
	{
		LocalSaveKind.Auto => "autosave",
		LocalSaveKind.Quick => "quicksave",
		LocalSaveKind.Manual => $"slot {saveId.SlotIndex}",
		_ => throw new ArgumentOutOfRangeException(nameof(saveId), saveId, "Unsupported save kind."),
	};

	private ModStoragePaths StoragePaths => _storagePaths ?? Game.ActiveModLoadout.StoragePaths;

	private static bool TryReadEnvelope(string savePath, out LocalSaveEnvelope? envelope, out LocalSaveReadFailureReason failureReason)
	{
		var absolutePath = savePath;
		if (!File.Exists(absolutePath))
		{
			return Fail(out envelope, out failureReason, LocalSaveReadFailureReason.MissingFile);
		}

		try
		{
			var json = File.ReadAllText(absolutePath);
			var rawEnvelope = LocalSaveEnvelope.Deserialize(json);
			if (rawEnvelope is null)
			{
				Game.Logger.Warning($"Save file could not be deserialized: {absolutePath}");
				return Fail(out envelope, out failureReason, LocalSaveReadFailureReason.InvalidFormat);
			}

			if (rawEnvelope.Version is < 1 or > LocalSaveEnvelope.CurrentVersion)
			{
				Game.Logger.Warning(
					$"Save file envelope version mismatch: {rawEnvelope.Version}, supported 1..{LocalSaveEnvelope.CurrentVersion}. Path: {absolutePath}");
				return Fail(out envelope, out failureReason, LocalSaveReadFailureReason.EnvelopeVersionMismatch);
			}

			if (rawEnvelope.SaveGame.Version is < SaveGame.MinSupportedVersion or > SaveGame.CurrentVersion)
			{
				Game.Logger.Warning(
					$"Save file save version mismatch: {rawEnvelope.SaveGame.Version}, supported {SaveGame.MinSupportedVersion}..{SaveGame.CurrentVersion}. Path: {absolutePath}");
				return Fail(out envelope, out failureReason, LocalSaveReadFailureReason.SaveVersionMismatch);
			}

			var modComparison = AssessCompatibility(rawEnvelope);
			if (modComparison.HasWarning)
			{
				Game.Logger.Warning(
					$"Save file mod loadout differs from the active loadout with {modComparison.WarningImpact} impact: {absolutePath}");
			}

			envelope = rawEnvelope;
			failureReason = LocalSaveReadFailureReason.None;
			return true;
		}
		catch (Exception exception)
		{
			Game.Logger.Warning($"Save file read failed: {absolutePath}. {exception.Message}");
			return Fail(out envelope, out failureReason, LocalSaveReadFailureReason.InvalidFormat);
		}
	}

	public static ModLoadoutComparison AssessCompatibility(LocalSaveEnvelope envelope)
	{
		ArgumentNullException.ThrowIfNull(envelope);
		return Game.ActiveModLoadout.Compare(envelope.Mods);
	}

	private static bool Fail(
		out LocalSaveEnvelope? envelope,
		out LocalSaveReadFailureReason failureReason,
		LocalSaveReadFailureReason reason)
	{
		envelope = null;
		failureReason = reason;
		return false;
	}
}

public sealed record LocalSaveSummary(
	LocalSaveId SaveId,
	bool HasSave = false,
	string? LeaderName = null,
	string? LeaderPortrait = null,
	int PartyMemberCount = 0,
	int Round = 1,
	GameDifficulty Difficulty = GameDifficulty.Normal,
	ClockRecord? Clock = null,
	string? CurrentMapId = null,
	DateTimeOffset? SavedAtUtc = null,
	bool NoRegret = false,
	long PlayTimeSeconds = 0,
	string? Note = null,
	SaveImpact ModWarningImpact = SaveImpact.None,
	IReadOnlyList<ModLoadoutDifference>? ModDifferences = null,
	LocalSaveReadFailureReason FailureReason = LocalSaveReadFailureReason.None)
{
	public bool CanLoad => HasSave && FailureReason == LocalSaveReadFailureReason.None;
}

