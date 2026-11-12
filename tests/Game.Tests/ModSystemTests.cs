using System.Text.Json;
using System.Text.Json.Nodes;
using Game.Application.Mods;
using Game.Content.Loading;
using Game.Core.Model;
using Game.Core.Serialization;

namespace Game.Tests;

public sealed class ModSystemTests
{
    private static readonly Lazy<string> PrimaryModDirectory = new(CreateTestPrimaryModDirectory);

    private static string SampleContentDirectoryPath =>
        Path.Combine(AppContext.BaseDirectory, "SampleData", "sample-content");

    private static string PrimaryModDirectoryPath => PrimaryModDirectory.Value;

    private static string PrimaryModDataPath => Path.Combine(PrimaryModDirectoryPath, "data");

    [Fact]
    public void ModRegistry_DiscoversBaseModFromProjectDataRoot()
    {
        var root = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(root.ModsDirectoryPath, "test-base");
        Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            """{"id":"test-base","name":"Test Base","version":"1","type":"game","dependencies":[],"saveImpact":"structural"}""");
        var mods = new ModRegistry(root).DiscoverMods();

        var mod = Assert.Single(mods);
        Assert.Equal("test-base", mod.ModId);
        Assert.Equal(Path.Combine(modDirectory, "data"), mod.DataDirectoryPath);
    }

    [Fact]
    public void ModRegistry_RejectsInvalidManifest()
    {
        var tempRoot = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(tempRoot.Path, "mods", "bad mod");
        Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
        File.WriteAllText(Path.Combine(modDirectory, "mod.json"), """{"id":"bad mod","name":"Bad","version":"1","type":"game","dependencies":[],"saveImpact":"structural"}""");

        Assert.Throws<InvalidOperationException>(() => ModRegistry.LoadMod(tempRoot, modDirectory));
    }

    [Fact]
    public void ModRegistry_RejectsMissingSaveImpact()
    {
        var tempRoot = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(tempRoot.Path, "mods", "missing-save-impact");
        Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            """{"id":"missing-save-impact","name":"Missing Save Impact","version":"1","type":"game","dependencies":[]}""");

        var exception = Assert.Throws<InvalidOperationException>(
            () => ModRegistry.LoadMod(tempRoot, modDirectory));

        Assert.Contains("Required mod manifest field 'saveImpact' is missing", exception.Message);
    }

    [Fact]
    public void ModRegistry_RejectsLooseAssetManifestField()
    {
        var tempRoot = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(tempRoot.Path, "mods", "loose-assets");
        Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            """{"id":"loose-assets","name":"Loose Assets","version":"1","type":"game","dependencies":[],"saveImpact":"structural","assetsPath":"assets"}""");

        Assert.Throws<InvalidOperationException>(() => ModRegistry.LoadMod(tempRoot, modDirectory));
    }

    [Fact]
    public void ModRegistry_ResolvesPackPathsInManifestOrder()
    {
        var tempRoot = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(tempRoot.Path, "mods", "packed");
        Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
        Directory.CreateDirectory(Path.Combine(modDirectory, "packs"));
        File.WriteAllText(Path.Combine(modDirectory, "packs", "base.pck"), "");
        File.WriteAllText(Path.Combine(modDirectory, "packs", "ui.pck"), "");
        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            """{"id":"packed","name":"Packed","version":"1","type":"game","dependencies":[],"saveImpact":"structural","packs":["packs/base.pck","packs/ui.pck"]}""");

        var context = ModRegistry.LoadMod(tempRoot, modDirectory);

        Assert.Equal(
            [
                Path.Combine(modDirectory, "packs", "base.pck"),
                Path.Combine(modDirectory, "packs", "ui.pck"),
            ],
            context.PackFilePaths);
    }

    [Fact]
    public void ModRegistry_RejectsMissingPack()
    {
        var tempRoot = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(tempRoot.Path, "mods", "missing-pack");
        Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            """{"id":"missing-pack","name":"Missing Pack","version":"1","type":"game","dependencies":[],"saveImpact":"structural","packs":["packs/missing.pck"]}""");

        Assert.Throws<FileNotFoundException>(() => ModRegistry.LoadMod(tempRoot, modDirectory));
    }

    [Fact]
    public void ModRegistry_RejectsUnsupportedPackExtension()
    {
        var tempRoot = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(tempRoot.Path, "mods", "bad-pack");
        Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
        Directory.CreateDirectory(Path.Combine(modDirectory, "packs"));
        File.WriteAllText(Path.Combine(modDirectory, "packs", "bad.txt"), "");
        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            """{"id":"bad-pack","name":"Bad Pack","version":"1","type":"game","dependencies":[],"saveImpact":"structural","packs":["packs/bad.txt"]}""");

        Assert.Throws<InvalidOperationException>(() => ModRegistry.LoadMod(tempRoot, modDirectory));
    }

    [Fact]
    public void ModStoragePaths_IsolateUserDataByModId()
    {
        var alpha = new ModStoragePaths("C:\\project-data", "alpha");
        var beta = new ModStoragePaths("C:\\project-data", "beta");

        Assert.NotEqual(alpha.GetSaveSlotPath(1), beta.GetSaveSlotPath(1));
        Assert.NotEqual(alpha.ProfilePath, beta.ProfilePath);
        Assert.NotEqual(alpha.SettingsPath, beta.SettingsPath);
        Assert.EndsWith(Path.Combine("userdata", "alpha", "saves", "save-slot-1.json"), alpha.GetSaveSlotPath(1));
        Assert.EndsWith(Path.Combine("userdata", "alpha", "saves", "quicksave.json"), alpha.QuickSavePath);
    }

    [Fact]
    public void ModRegistry_AllowsAddonWithoutDataDirectory()
    {
        var tempRoot = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(tempRoot.Path, "mods", "resource-addon");
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            """{"id":"resource-addon","name":"Resource Addon","version":"1","type":"addon","dependencies":["jyxr-base"],"saveImpact":"gameplay"}""");

        var context = ModRegistry.LoadMod(tempRoot, modDirectory);

        Assert.Equal(ModType.Addon, context.Manifest.Type);
        Assert.Equal(SaveImpact.Gameplay, context.Manifest.SaveImpact);
        Assert.False(Directory.Exists(context.DataDirectoryPath));
    }

    [Fact]
    public void ModRegistry_LoadsAddonWithNoSaveImpact()
    {
        var tempRoot = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(tempRoot.Path, "mods", "ui-addon");
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            """{"id":"ui-addon","name":"UI Addon","version":"1","type":"addon","dependencies":["jyxr-base"],"saveImpact":"none"}""");

        var context = ModRegistry.LoadMod(tempRoot, modDirectory);

        Assert.Equal(SaveImpact.None, context.Manifest.SaveImpact);
    }

    [Fact]
    public void ModRegistry_RejectsGameWithoutStructuralSaveImpact()
    {
        var tempRoot = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(tempRoot.Path, "mods", "unsafe-game");
        Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
        File.WriteAllText(
            Path.Combine(modDirectory, "mod.json"),
            """{"id":"unsafe-game","name":"Unsafe Game","version":"1","type":"game","dependencies":[],"saveImpact":"gameplay"}""");

        var exception = Assert.Throws<InvalidOperationException>(
            () => ModRegistry.LoadMod(tempRoot, modDirectory));

        Assert.Contains("must declare structural save impact", exception.Message);
    }

    [Fact]
    public void ModLoadoutResolver_AddsTransitiveDependenciesBeforeDependents()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var dependency = CreateContext(root, "dependency", ModType.Addon, ["base"]);
        var addon = CreateContext(root, "addon", ModType.Addon, ["dependency"]);

        var loadout = new ModLoadoutResolver([addon, primary, dependency]).Resolve("base", ["addon"]);

        Assert.Equal(["base", "dependency", "addon"], loadout.ModsInLoadOrder.Select(static mod => mod.ModId));
    }

    [Fact]
    public void ModLoadoutResolver_RejectsAddonForDifferentGame()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var otherGame = CreateContext(root, "other", ModType.Game);
        var addon = CreateContext(root, "addon", ModType.Addon, ["other"]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ModLoadoutResolver([primary, otherGame, addon]).Resolve("base", ["addon"]));

        Assert.Contains("requires game mod 'other'", exception.Message);
    }

    [Fact]
    public void ModLoadoutResolver_RejectsCircularDependencies()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var alpha = CreateContext(root, "alpha", ModType.Addon, ["beta"]);
        var beta = CreateContext(root, "beta", ModType.Addon, ["alpha"]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ModLoadoutResolver([primary, alpha, beta]).Resolve("base", ["alpha"]));

        Assert.Contains("Circular mod dependency", exception.Message);
    }

    [Fact]
    public void ModLoadoutResolver_OnlyMovesAddonWhenDependencyOrderRemainsValid()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var dependency = CreateContext(root, "dependency", ModType.Addon, ["base"]);
        var addon = CreateContext(root, "addon", ModType.Addon, ["dependency"]);
        var resolver = new ModLoadoutResolver([primary, dependency, addon]);
        var loadout = resolver.Resolve("base", ["addon"]);

        Assert.False(resolver.CanMove(loadout.AddonMods, 1, 0));
        Assert.False(resolver.CanMove(loadout.AddonMods, 0, 1));
    }

    [Fact]
    public void ModLoadoutComparison_AssessesVersionAndOrderRisk()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var addon = CreateContext(root, "addon", ModType.Addon, ["base"]);
        var loadout = new ModLoadout(primary, [addon]);

        var matching = loadout.Compare(
        [
            new ModVersionReference("base", "1", SaveImpact.Structural),
            new ModVersionReference("addon", "1", SaveImpact.Gameplay),
        ]);
        var versionChanged = loadout.Compare(
        [
            new ModVersionReference("base", "1", SaveImpact.Structural),
            new ModVersionReference("addon", "2", SaveImpact.Gameplay),
        ]);
        var reordered = loadout.Compare(
        [
            new ModVersionReference("addon", "1", SaveImpact.Gameplay),
            new ModVersionReference("base", "1", SaveImpact.Structural),
        ]);

        Assert.False(matching.HasWarning);
        Assert.Equal(SaveImpact.Gameplay, versionChanged.WarningImpact);
        Assert.Equal(SaveImpact.Structural, reordered.WarningImpact);
    }

    [Fact]
    public void ModLoadoutComparison_ReportsAddedAndRemovedMods()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var addon = CreateContext(root, "addon", ModType.Addon, ["base"]);

        var added = new ModLoadout(primary, [addon]).Compare(
        [
            new ModVersionReference("base", "1", SaveImpact.Structural),
        ]);
        var removed = new ModLoadout(primary, []).Compare(
        [
            new ModVersionReference("base", "1", SaveImpact.Structural),
            new ModVersionReference("addon", "1", SaveImpact.Gameplay),
        ]);

        Assert.Equal(ModLoadoutDifferenceKind.Added, Assert.Single(added.Differences).Kind);
        Assert.Equal(ModLoadoutDifferenceKind.Removed, Assert.Single(removed.Differences).Kind);
    }

    [Fact]
    public void ModLoadoutComparison_TreatsMissingModsAsEmpty()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var ui = CreateContext(root, "ui", ModType.Addon, ["base"], SaveImpact.None);
        var gameplay = CreateContext(root, "gameplay", ModType.Addon, ["base"], SaveImpact.Gameplay);

        var baseOnly = new ModLoadout(primary, []).Compare(null);
        var withUi = new ModLoadout(primary, [ui]).Compare(null);
        var withGameplay = new ModLoadout(primary, [gameplay]).Compare(null);

        Assert.Equal(SaveImpact.Structural, baseOnly.WarningImpact);
        Assert.Equal(SaveImpact.Structural, withUi.WarningImpact);
        Assert.Equal(SaveImpact.Structural, withGameplay.WarningImpact);
        Assert.All(baseOnly.Differences, static difference => Assert.Equal(ModLoadoutDifferenceKind.Added, difference.Kind));
    }

    [Fact]
    public void ModLoadoutComparison_IgnoresNonSaveAddonIdentityOrderAndVersion()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var content = CreateContext(root, "content", ModType.Addon, ["base"]);
        var ui = CreateContext(root, "ui", ModType.Addon, ["base"], SaveImpact.None);
        var loadout = new ModLoadout(primary, [ui, content]);

        var comparison = loadout.Compare(
        [
            new ModVersionReference("base", "1", SaveImpact.Structural),
            new ModVersionReference("content", "1", SaveImpact.Gameplay),
            new ModVersionReference("different-ui", "99", SaveImpact.None),
        ]);

        Assert.False(comparison.HasWarning);
    }

    [Fact]
    public void ModLoadoutComparison_DoesNotLetUnchangedBaseRaiseAddonReorderRisk()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var alpha = CreateContext(root, "alpha", ModType.Addon, ["base"]);
        var beta = CreateContext(root, "beta", ModType.Addon, ["base"]);

        var comparison = new ModLoadout(primary, [alpha, beta]).Compare(
        [
            new ModVersionReference("base", "1", SaveImpact.Structural),
            new ModVersionReference("beta", "1", SaveImpact.Gameplay),
            new ModVersionReference("alpha", "1", SaveImpact.Gameplay),
        ]);

        Assert.Equal(SaveImpact.Gameplay, comparison.WarningImpact);
        Assert.DoesNotContain(comparison.Differences, static difference => difference.Id == "base");
    }

    [Fact]
    public void ModLoadoutComparison_UsesHigherSavedImpactWhenVersionChanges()
    {
        var root = CreateTempProjectDataRoot();
        var primary = CreateContext(root, "base", ModType.Game);
        var addon = CreateContext(root, "addon", ModType.Addon, ["base"], SaveImpact.Gameplay);

        var comparison = new ModLoadout(primary, [addon]).Compare(
        [
            new ModVersionReference("base", "1", SaveImpact.Structural),
            new ModVersionReference("addon", "0", SaveImpact.Structural),
        ]);

        Assert.Equal(SaveImpact.Structural, comparison.WarningImpact);
        Assert.Equal(
            SaveImpact.Structural,
            Assert.Single(comparison.Differences, static difference => difference.Id == "addon").Impact);
    }

    [Fact]
    public void ModVersionReference_RoundTripsSaveImpact()
    {
        var json = JsonSerializer.Serialize(
            new ModVersionReference("addon", "1", SaveImpact.Structural),
            GameJson.Default);

        var reference = JsonSerializer.Deserialize<ModVersionReference>(json, GameJson.Default);

        Assert.NotNull(reference);
        Assert.Equal(SaveImpact.Structural, reference.SaveImpact);
    }

    [Fact]
    public void LauncherSettingsStore_RoundTripsOrderedLoadout()
    {
        var root = CreateTempProjectDataRoot();
        var store = new LauncherSettingsStore(root.LauncherSettingsPath);
        var expected = new LauncherSettingsRecord(
            LauncherSettingsRecord.CurrentVersion,
            "base",
            ["dependency", "addon"]);

        store.Save(expected);
        var actual = store.LoadOrEmpty();

        Assert.Equal(expected.PrimaryModId, actual.PrimaryModId);
        Assert.Equal(expected.EnabledAddonIds, actual.EnabledAddonIds);
    }

    [Fact]
    public void JsonContentLoader_LoadsBaseModDataDirectory()
    {
        var repository = new JsonContentLoader().LoadFromDirectory(SampleContentDirectoryPath);

        Assert.NotNull(repository.GetCharacter("ally_warrior"));
        Assert.NotNull(repository.GetMap("sample_map"));
    }

    [Fact]
    public void JsonContentLoader_LoadsMapObjectsAndArraysFromNestedDirectories()
    {
        var primaryDirectory = CreateTestPrimaryModDirectory();
        var nestedDirectory = Path.Combine(primaryDirectory, "data", "maps", "regions", "north");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(
            Path.Combine(nestedDirectory, "single.json"),
            """{"id":"nested_single_map","name":"Nested Single Map","locations":[]}""");
        File.WriteAllText(
            Path.Combine(primaryDirectory, "data", "maps", "batch.json"),
            """[{"id":"nested_array_map","name":"Nested Array Map","locations":[]}]""");

        var repository = new JsonContentLoader().LoadFromMods(
        [
            new ModContentInput("test-base", primaryDirectory, Required: true),
        ]);

        Assert.NotNull(repository.GetMap("nested_single_map"));
        Assert.NotNull(repository.GetMap("nested_array_map"));
    }

    [Fact]
    public void JsonContentLoader_AllowsEmptyPrimaryMapDirectory()
    {
        var primaryDirectory = CreateTestPrimaryModDirectory();
        var mapDirectory = Path.Combine(primaryDirectory, "data", "maps");
        foreach (var filePath in Directory.EnumerateFiles(mapDirectory, "*.json", SearchOption.AllDirectories))
        {
            File.Delete(filePath);
        }

        var repository = new JsonContentLoader().LoadFromMods(
        [
            new ModContentInput("test-base", primaryDirectory, Required: true),
        ]);

        Assert.False(repository.TryGetMap("sample_map", out _));
    }

    [Fact]
    public void JsonContentLoader_RejectsLegacyPrimaryMapFileWithoutMapDirectory()
    {
        var primaryDirectory = CreateTestPrimaryModDirectory();
        var dataDirectory = Path.Combine(primaryDirectory, "data");
        var mapDirectory = Path.Combine(dataDirectory, "maps");
        foreach (var filePath in Directory.EnumerateFiles(mapDirectory, "*.json", SearchOption.AllDirectories))
        {
            File.Delete(filePath);
        }

        Directory.Delete(mapDirectory, recursive: true);
        File.WriteAllText(Path.Combine(dataDirectory, "maps.json"), "[]");

        var exception = Assert.Throws<ContentLoadException>(() => new JsonContentLoader().LoadFromMods(
        [
            new ModContentInput("test-base", primaryDirectory, Required: true),
        ]));

        Assert.Contains("maps", exception.Message);
    }

    [Fact]
    public void JsonContentLoader_LoadsMapFromSparseAddonDirectory()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        var mapDirectory = Path.Combine(addonDirectory, "data", "maps", "nested");
        Directory.CreateDirectory(mapDirectory);
        File.WriteAllText(
            Path.Combine(mapDirectory, "addon.json"),
            """{"id":"addon_map","name":"Addon Map","locations":[]}""");

        var repository = LoadWithAddon(addonDirectory).Repository;

        Assert.NotNull(repository.GetMap("addon_map"));
    }

    [Fact]
    public void JsonContentLoader_ReportsBothFilesForDuplicateMapId()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        var mapDirectory = Path.Combine(addonDirectory, "data", "maps", "nested");
        Directory.CreateDirectory(mapDirectory);
        var duplicatePath = Path.Combine(mapDirectory, "duplicate.json");
        File.WriteAllText(
            duplicatePath,
            """{"id":"sample_map","name":"Duplicate Map","locations":[]}""");

        var exception = Assert.Throws<ContentLoadException>(() => LoadWithAddon(addonDirectory));

        Assert.Contains("sample.json", exception.Message);
        Assert.Contains("duplicate.json", exception.Message);
    }

    [Fact]
    public void JsonContentLoader_ReportsBothFilesForDuplicateMapIdWithinDirectory()
    {
        var primaryDirectory = CreateTestPrimaryModDirectory();
        var duplicatePath = Path.Combine(primaryDirectory, "data", "maps", "nested", "duplicate.json");
        Directory.CreateDirectory(Path.GetDirectoryName(duplicatePath)!);
        File.WriteAllText(
            duplicatePath,
            """{"id":"sample_map","name":"Duplicate Map","locations":[]}""");

        var exception = Assert.Throws<ContentLoadException>(() => new JsonContentLoader().LoadFromMods(
        [
            new ModContentInput("test-base", primaryDirectory, Required: true),
        ]));

        Assert.Contains("sample.json", exception.Message);
        Assert.Contains("duplicate.json", exception.Message);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("true")]
    [InlineData("[1]")]
    public void JsonContentLoader_ReportsNestedMapSourceForInvalidDocument(string content)
    {
        var addonDirectory = CreateSparseAddonDirectory();
        var invalidPath = Path.Combine(addonDirectory, "data", "maps", "nested", "invalid.json");
        Directory.CreateDirectory(Path.GetDirectoryName(invalidPath)!);
        File.WriteAllText(invalidPath, content);

        var exception = Assert.Throws<ContentLoadException>(() => LoadWithAddon(addonDirectory));

        Assert.Contains("invalid.json", exception.Message);
    }

    [Fact]
    public void JsonContentLoader_LoadsSparseAddonAndMergesDefinitionById()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "merge",
                  "target": {"kind": "character", "id": "ally_warrior"},
                  "value": {"name": "Extended Warrior", "stats": {"bili": 77}}
                }
              ]
            }
            """);

        var repository = new JsonContentLoader().LoadFromMods(
        [
            new ModContentInput("test-base", PrimaryModDirectoryPath, Required: true),
            new ModContentInput("test-addon", addonDirectory, Required: false),
        ]);

        Assert.Equal("Extended Warrior", repository.GetCharacter("ally_warrior").Name);
        Assert.Equal(77, repository.GetCharacter("ally_warrior").Stats[StatType.Bili]);
        Assert.NotNull(repository.GetMap("sample_map"));
    }

    [Fact]
    public void JsonContentLoader_MaterializesRootDefinitionReplacementWithoutCloning()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "set",
                  "target": {"kind": "character", "id": "ally_warrior"},
                  "value": {
                    "id": "ally_warrior",
                    "name": "Replacement Warrior",
                    "stats": {"max_hp": 44, "speed": 35, "movement": 3, "max_mp": 10},
                    "externalSkills": [{"level": 1, "id": "basic_attack"}],
                    "internalSkills": [],
                    "talentIds": [],
                    "equipmentIds": ["iron_blade"],
                    "specialSkillIds": []
                  }
                }
              ]
            }
            """);

        var repository = LoadWithAddon(addonDirectory).Repository;

        Assert.Equal("Replacement Warrior", repository.GetCharacter("ally_warrior").Name);
    }

    [Fact]
    public void JsonContentLoader_LoadsGeneratedAddonDataAndPatch()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        File.WriteAllText(
            Path.Combine(addonDirectory, "data", "game-tips.json"),
            """[{"id":"addon_tip","text":"Generated by the test addon."}]""");
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "merge",
                  "target": {"kind": "externalSkill", "id": "basic_attack"},
                  "value": {"powerBase": 9}
                }
              ]
            }
            """);
        var repository = new JsonContentLoader().LoadFromMods(
        [
            new ModContentInput("test-base", PrimaryModDirectoryPath, Required: true),
            new ModContentInput("test-addon", addonDirectory, Required: false),
        ]);

        Assert.Equal("Generated by the test addon.", repository.GetGameTip("addon_tip").Text);
        Assert.Equal(9, repository.GetExternalSkill("basic_attack").PowerBase);
    }

    [Fact]
    public void JsonContentLoader_ReplacesStorySegmentWithPatch()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "set",
                  "target": {"kind": "storySegment", "id": "test_story"},
                  "value": {
                    "name": "test_story",
                    "steps": [{"kind":"command","call":"change_yuanbao(99)"}]
                  }
                }
              ]
            }
            """);

        var repository = new JsonContentLoader().LoadFromMods(
        [
            new ModContentInput("test-base", PrimaryModDirectoryPath, Required: true),
            new ModContentInput("test-addon", addonDirectory, Required: false),
        ]);

        var segment = repository.GetStorySegment("test_story");
        var command = Assert.IsType<Game.Core.Story.CommandStep>(Assert.Single(segment.Segment.Steps));
        Assert.Equal("change_yuanbao", command.Call.Root.Name);
    }

    [Fact]
    public void GameConfig_LoadsFromBaseModDataDirectory()
    {
        var configPath = Path.Combine(PrimaryModDataPath, "game-config.json");
        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<GameConfig>(json, GameJson.Default);

        Assert.NotNull(config);
        Assert.NotEmpty(config.InitialPartyCharacterIds);
        Assert.False(string.IsNullOrWhiteSpace(config.InitialStorySegmentId));
    }

    [Fact]
    public void JsonContentLoader_LoadModContentPatchesGameConfigAndStorySteps()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "merge",
                  "target": {"kind": "gameConfig"},
                  "value": {"maxLevel": 42}
                },
                {
                  "op": "append",
                  "target": {"kind": "gameConfig"},
                  "path": ["initialPartyCharacterIds"],
                  "values": ["ally_mage"]
                },
                {
                  "op": "prepend",
                  "target": {"kind": "storySegment", "id": "test_story"},
                  "path": ["steps"],
                  "values": [{"kind":"command","call":"change_silver(1)"}]
                }
              ]
            }
            """);

        var loaded = new JsonContentLoader().LoadModContent(
        [
            new ModContentInput("test-base", PrimaryModDirectoryPath, Required: true),
            new ModContentInput("test-addon", addonDirectory, Required: false),
        ]);

        Assert.Equal(42, loaded.Config.MaxLevel);
        Assert.Equal(["ally_warrior", "ally_mage"], loaded.Config.InitialPartyCharacterIds);
        var steps = loaded.Repository.GetStorySegment("test_story").Segment.Steps;
        Assert.Equal("change_silver", Assert.IsType<Game.Core.Story.CommandStep>(steps[0]).Call.Root.Name);
    }

    [Theory]
    [InlineData("legendFemaleVoiceSfxIds")]
    [InlineData("legendMaleVoiceSfxIds")]
    [InlineData("legendEffectSfxIds")]
    public void JsonContentLoader_RejectsEmptyLegendSfxList(string field)
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            $$"""
            {
              "format": 2,
              "operations": [
                {"op": "set", "target": {"kind": "gameConfig"}, "path": ["{{field}}"], "value": []}
              ]
            }
            """);

        var exception = Assert.ThrowsAny<Exception>(() => new JsonContentLoader().LoadModContent(
        [
            new ModContentInput("test-base", PrimaryModDirectoryPath, Required: true),
            new ModContentInput("test-addon", addonDirectory, Required: false),
        ]));
        Assert.Contains(field, exception.Message);
    }

    [Fact]
    public void JsonContentLoader_PatchesKeyedMapLocationById()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "merge",
                  "target": {"kind": "map", "id": "sample_map"},
                  "path": ["locations", {"id": "sample_location"}],
                  "value": {"description": "已修改"}
                }
              ]
            }
            """);

        var repository = LoadWithAddon(addonDirectory).Repository;

        Assert.Equal("已修改", repository.GetMap("sample_map").Locations.Single(location => location.Id == "sample_location").Description);
    }

    [Fact]
    public void JsonContentLoader_InsertsAndMovesIdListItems()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "insertAfter",
                  "target": {"kind": "map", "id": "sample_map"},
                  "path": ["locations"],
                  "anchor": {"id": "sample_location"},
                  "value": {
                    "id": "测试地点",
                    "position": {"x": 1, "y": 2},
                    "description": "测试",
                    "picture": null,
                    "events": []
                  }
                },
                {
                  "op": "moveBefore",
                  "target": {"kind": "map", "id": "sample_map"},
                  "path": ["locations"],
                  "item": {"id": "测试地点"},
                  "anchor": {"id": "sample_location"}
                }
              ]
            }
            """);

        var locations = LoadWithAddon(addonDirectory).Repository.GetMap("sample_map").Locations;

        Assert.Equal("测试地点", locations[0].Id);
        Assert.Equal("sample_location", locations[1].Id);
    }

    [Fact]
    public void JsonContentLoader_RemovesIdListItem()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "remove",
                  "target": {"kind": "map", "id": "sample_map"},
                  "path": ["locations", {"id": "sample_location"}]
                }
              ]
            }
            """);

        var locations = LoadWithAddon(addonDirectory).Repository.GetMap("sample_map").Locations;

        Assert.DoesNotContain(locations, static location => location.Id == "sample_location");
    }

    [Fact]
    public void JsonContentLoader_RejectsLegacyPatchFormat()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 1,
              "operations": []
            }
            """);

        var exception = Assert.Throws<ContentLoadException>(() => LoadWithAddon(addonDirectory));

        Assert.Contains("unsupported format '1'", exception.Message);
    }

    [Fact]
    public void JsonContentLoader_RejectsIdSelectorOnUnkeyedStorySteps()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "remove",
                  "target": {"kind": "storySegment", "id": "test_story"},
                  "path": ["steps", {"id": "step-1"}]
                }
              ]
            }
            """);

        var exception = Assert.Throws<ContentLoadException>(() => LoadWithAddon(addonDirectory));

        Assert.Contains("expected one 'id=step-1'", exception.Message);
    }

    [Fact]
    public void JsonContentLoader_RejectsCompleteDefinitionOverride()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        File.WriteAllText(
            Path.Combine(addonDirectory, "data", "characters.json"),
            """[{"id":"ally_warrior"}]""");

        var exception = Assert.Throws<ContentLoadException>(() => LoadWithAddon(addonDirectory));

        Assert.Contains("Use a patch", exception.Message);
    }

    [Fact]
    public void JsonContentLoader_LaterAddonWinsAndReportsFieldConflict()
    {
        var firstAddon = CreateSparseAddonDirectory();
        var secondAddon = CreateSparseAddonDirectory();
        WriteCharacterNamePatch(firstAddon, "第一个");
        WriteCharacterNamePatch(secondAddon, "第二个");

        var loaded = new JsonContentLoader().LoadModContent(
        [
            new ModContentInput("test-base", PrimaryModDirectoryPath, Required: true),
            new ModContentInput("first", firstAddon, Required: false),
            new ModContentInput("second", secondAddon, Required: false),
        ]);

        Assert.Equal("第二个", loaded.Repository.GetCharacter("ally_warrior").Name);
        var warning = Assert.Single(loaded.Report.Warnings);
        Assert.Equal("first", warning.PreviousModId);
        Assert.Equal("second", warning.CurrentModId);
    }

    [Fact]
    public void JsonContentLoader_TestOperationGuardsExpectedBaseValue()
    {
        var addonDirectory = CreateSparseAddonDirectory();
        WritePatch(
            addonDirectory,
            """
            {
              "format": 2,
              "operations": [
                {
                  "op": "test",
                  "target": {"kind": "character", "id": "ally_warrior"},
                  "path": ["name"],
                  "value": "不是小虾米"
                }
              ]
            }
            """);

        var exception = Assert.Throws<ContentLoadException>(() => LoadWithAddon(addonDirectory));

        Assert.Contains("test failed", exception.Message);
    }

    private static ProjectDataRoot CreateTempProjectDataRoot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestScratch", "jyxr-mod-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return ProjectDataRoot.FromPath(path);
    }

    private static ModContext CreateContext(
        ProjectDataRoot root,
        string id,
        ModType type,
        IReadOnlyList<string>? dependencies = null,
        SaveImpact saveImpact = SaveImpact.Gameplay)
    {
        var directory = Path.Combine(root.ModsDirectoryPath, id);
        Directory.CreateDirectory(directory);
        return new ModContext(
            root,
            directory,
            new ModManifest(
                id,
                id,
                "1",
                type,
                Dependencies: dependencies,
                SaveImpact: type == ModType.Game ? SaveImpact.Structural : saveImpact));
    }

    private static string CreateTestPrimaryModDirectory()
    {
        var root = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(root.ModsDirectoryPath, "test-base");
        var dataDirectory = Path.Combine(modDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(
                     SampleContentDirectoryPath,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(SampleContentDirectoryPath, sourcePath);
            var targetPath = Path.Combine(dataDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath);
        }

        File.WriteAllText(
            Path.Combine(dataDirectory, "game-config.json"),
            """
            {
              "initialStorySegmentId": "test_story",
              "initialPartyCharacterIds": ["ally_warrior"],
              "mainMenuMusic": "audio/test_theme",
              "mainMenuBackground": "map/test_background",
              "selectablePortraitIds": ["head/test_portrait"],
              "randomBattleMusics": ["audio/test_battle"],
              "equipmentRandomAffixCountWeights": [{"count": 1, "weight": 1}]
            }
            """);
        var storyDirectory = Path.Combine(dataDirectory, "stories");
        Directory.CreateDirectory(storyDirectory);
        File.WriteAllText(
            Path.Combine(storyDirectory, "test.story.json"),
            """
            {
              "version": 3,
              "segments": [
                {
                  "name": "test_story",
                  "steps": [
                    {"kind": "command", "call": "change_silver(1)"}
                  ]
                }
              ]
            }
            """);
        return modDirectory;
    }

    private static string CreateSparseAddonDirectory()
    {
        var root = CreateTempProjectDataRoot();
        var modDirectory = Path.Combine(root.ModsDirectoryPath, "test-addon");
        Directory.CreateDirectory(Path.Combine(modDirectory, "data"));
        return modDirectory;
    }

    private static LoadedModContent LoadWithAddon(string addonDirectory) =>
        new JsonContentLoader().LoadModContent(
        [
            new ModContentInput("test-base", PrimaryModDirectoryPath, Required: true),
            new ModContentInput("test-addon", addonDirectory, Required: false),
        ]);

    private static void WriteCharacterNamePatch(string addonDirectory, string name) =>
        WritePatch(
            addonDirectory,
            $$"""
            {
              "format": 2,
              "operations": [
                {
                  "op": "merge",
                  "target": {"kind": "character", "id": "ally_warrior"},
                  "value": {"name": "{{name}}"}
                }
              ]
            }
            """);

    private static void WritePatch(string addonDirectory, string json)
    {
        var patchDirectory = Path.Combine(addonDirectory, "patches");
        Directory.CreateDirectory(patchDirectory);
        File.WriteAllText(Path.Combine(patchDirectory, "010-test.patch.json"), json);
    }
}
