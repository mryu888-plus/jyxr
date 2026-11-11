using Game.Content.Loading;
using Game.Core.Affix;
using Game.Core.Battle;

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

    [Fact]
    public void Loader_ResolvesCyclicReferencesRegardlessOfOrder()
    {
        var repository = Load(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["talents.json"] =
                """
                [
                  { "id": "yin", "name": "yin", "affixes": [{ "type": "grant_talent", "talentId": "yang" }] },
                  { "id": "yang", "name": "yang", "affixes": [{ "type": "grant_talent", "talentId": "yin" }] }
                ]
                """,
            ["buffs.json"] =
                """
                [{
                  "id": "echo", "name": "echo", "isDebuff": false,
                  "affixes": [{
                    "type": "hook", "timing": "AfterBuffRound",
                    "effects": [{ "type": "apply_buff", "target": { "type": "self" }, "buffId": "echo", "level": 1, "duration": 1 }]
                  }]
                }]
                """,
        });

        var yinGrant = Assert.IsType<GrantTalentAffix>(Assert.Single(repository.Talents["yin"].Affixes));
        var yangGrant = Assert.IsType<GrantTalentAffix>(Assert.Single(repository.Talents["yang"].Affixes));
        Assert.Same(repository.Talents["yang"], yinGrant.Talent);
        Assert.Same(repository.Talents["yin"], yangGrant.Talent);
        var hook = Assert.IsType<HookAffix>(Assert.Single(repository.Buffs["echo"].Affixes));
        var apply = Assert.IsType<ApplyBuffBattleEffectDefinition>(Assert.Single(hook.Effects));
        Assert.Same(repository.Buffs["echo"], apply.Buff);
    }

    [Fact]
    public void Loader_ReportsMissingNestedReferenceWithPath()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["talents.json"] =
                """
                [{
                  "id": "curse", "name": "curse",
                  "affixes": [{
                    "type": "hook", "timing": "OnHitConfirmed",
                    "effects": [{ "type": "apply_buff", "target": { "type": "target" }, "buffId": "nope", "level": 1, "duration": 1 }]
                  }]
                }]
                """,
        }));

        Assert.Contains("talent 'curse'.affixes[0].effects[0].buffId", exception.Message);
        Assert.Contains("missing buff 'nope'", exception.Message);
    }

    [Theory]
    [InlineData("random_debuff", """{ "buffIds": ["nope"], "level": 1, "duration": 2 }""", "OnHitConfirmed", "missing buff 'nope'")]
    [InlineData("reset_skill_cooldowns", """{ "talentId": "ghost" }""", "BeforeHitResolved", "missing talent 'ghost'")]
    [InlineData("illness", """{ "protectorCharacterId": "nobody" }""", "BeforeActionStart", "missing character 'nobody'")]
    [InlineData("formless_sword_qi", """{ "sourceSkillId": "nothing", "mpDifferenceCap": 1, "minimumFactor": 0, "maximumFactor": 1 }""", "BeforeDamageCalculation", "missing skill 'nothing'")]
    public void Loader_RejectsMissingReferencesInsideCustomEffectParameters(
        string effectId, string parameters, string timing, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["talents.json"] =
                $$"""
                [{
                  "id": "carrier", "name": "carrier",
                  "affixes": [{
                    "type": "hook", "timing": "{{timing}}",
                    "effects": [{ "type": "custom", "effectId": "{{effectId}}", "parameters": {{parameters}} }]
                  }]
                }]
                """,
        }));

        Assert.Contains(expected, exception.Message);
        Assert.Contains("talent 'carrier'", exception.Message);
    }

    [Fact]
    public void Loader_ResolvesCustomEffectParameterReferences()
    {
        var repository = Load(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["buffs.json"] = """[{ "id": "poison", "name": "poison", "isDebuff": true }]""",
            ["talents.json"] =
                """
                [{
                  "id": "venom", "name": "venom",
                  "affixes": [{
                    "type": "hook", "timing": "OnHitConfirmed",
                    "effects": [{ "type": "custom", "effectId": "random_debuff", "parameters": { "buffIds": ["poison"], "level": 1, "duration": 2 } }]
                  }]
                }]
                """,
        });

        var hook = Assert.IsType<HookAffix>(Assert.Single(repository.Talents["venom"].Affixes));
        var custom = Assert.IsType<CustomBattleEffectDefinition>(Assert.Single(hook.Effects));
        var parameters = Assert.IsType<Game.Core.Battle.Talents.RandomDebuffBattleEffectParameters>(custom.BoundParameters);
        Assert.Same(repository.Buffs["poison"], Assert.Single(parameters.BuffReferences).Definition);
    }

    /// <summary>
    /// 自定义效果参数对内容校验是不透明的，只有声明成引用类型才能在加载期检查；
    /// 这里保证参数里不再出现以字符串保存的天赋/Buff/武学/角色/物品 ID。
    /// </summary>
    [Fact]
    public void CustomEffectParameters_DeclareContentIdsAsReferences()
    {
        var contentIdName = new System.Text.RegularExpressions.Regex("(Talent|Buff|Skill|Character|Item)Ids?$");
        var offenders = typeof(Game.Core.Battle.CustomBattleEffectRegistry).Assembly.GetTypes()
            .Where(static type => type.Name.EndsWith("Parameters", StringComparison.Ordinal))
            .SelectMany(static type => type.GetProperties())
            .Where(property => property.SetMethod is not null &&
                (property.PropertyType == typeof(string) || property.PropertyType == typeof(IReadOnlyList<string>)) &&
                contentIdName.IsMatch(property.Name))
            .Select(static property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>旧的逐类型 Resolve 协议已收敛到 ContentReferenceGraph，定义类型不应再各自实现。</summary>
    [Fact]
    public void ContentDefinitions_DoNotDeclareAdHocResolveMethods()
    {
        var offenders = typeof(Game.Core.Definitions.ContentReferenceGraph).Assembly.GetTypes()
            .Where(static type => type.Namespace?.StartsWith("Game.Core", StringComparison.Ordinal) == true &&
                !typeof(Game.Core.Definitions.IContentReference).IsAssignableFrom(type))
            .SelectMany(static type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(static method => method.Name.StartsWith("Resolve", StringComparison.Ordinal) &&
                method.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == typeof(Game.Core.Abstractions.IContentRepository))
            .Select(static method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();

        Assert.Empty(offenders);
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
