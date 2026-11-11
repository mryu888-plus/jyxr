using Game.Content.Loading;

namespace Game.Tests;

/// <summary>内容装配与校验：两阶段引用解析、自定义效果参数引用、hook 时机约束。</summary>
public sealed class ContentAssemblyTests
{
    [Theory]
    [InlineData("""[{"type":"context_hit_state","state":"hit"}]""")]
    [InlineData("""[{"type":"context_unit_role","role":"source"},{"type":"context_hit_state","state":"hit"}]""")]
    public void Loader_RejectsAttackerSideHitStateHookBeforeHitResolved(string conditions)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => LoadTalentHook("BeforeHitResolved", conditions));
        Assert.Contains("OnHitConfirmed", exception.Message);
    }

    [Theory]
    [InlineData("""[{"type":"context_unit_role","role":"target"},{"type":"context_hit_state","state":"hit"}]""")]
    [InlineData("""[{"type":"context_unit_role","role":"source"},{"type":"context_hit_state","state":"miss"}]""")]
    public void Loader_AcceptsHitResolutionHooksThatCanRun(string conditions)
    {
        var repository = LoadTalentHook("BeforeHitResolved", conditions);
        Assert.True(repository.Talents.ContainsKey("hook_talent"));
    }

    private static InMemoryContentRepository LoadTalentHook(string timing, string conditions) =>
        Load(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["talents.json"] =
                $$"""
                [{
                  "id": "hook_talent",
                  "name": "hook_talent",
                  "affixes": [{
                    "type": "hook",
                    "timing": "{{timing}}",
                    "conditions": {{conditions}},
                    "effects": [{ "type": "cancel_hit" }]
                  }]
                }]
                """,
        });

    internal static InMemoryContentRepository Load(IReadOnlyDictionary<string, string> overrides)
    {
        var directoryPath = CreateContentDirectory(overrides);
        try
        {
            return new JsonContentLoader().LoadFromDirectory(directoryPath);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static string CreateContentDirectory(IReadOnlyDictionary<string, string> overrides)
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["battles.json"] = "[]",
            ["scoped-battle-effects.json"] = "[]",
            ["characters.json"] = "[]",
            ["external-skills.json"] = "[]",
            ["game-tips.json"] = "[]",
            ["grow-templates.json"] = "[]",
            ["internal-skills.json"] = "[]",
            ["legend-skills.json"] = "[]",
            ["maps/empty.json"] = "[]",
            ["world-triggers.json"] = "[]",
            ["resources.json"] = "[]",
            ["sects.json"] = "[]",
            ["shops.json"] = "[]",
            ["special-skills.json"] = "[]",
            ["items.json"] = "[]",
            ["item-tags.json"] = "[]",
            ["random-affix-tables.json"] = "[]",
            ["buffs.json"] = "[]",
            ["talents.json"] = "[]",
            ["towers.json"] = "[]",
        };
        foreach (var (relativePath, content) in overrides)
        {
            files[relativePath] = content;
        }

        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(directoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        return directoryPath;
    }
}
