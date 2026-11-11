using System.Globalization;
using System.Text.RegularExpressions;

namespace Game.Application;

/// <summary>
/// 剧情对白/选项里的 <c>$name$</c> 占位符。名字按剧情表达式的变量规则解析：先找内建变量
/// (<c>hero_name</c>、<c>heroine_name</c>、<c>zhenlong_level</c> 以及条件里能用的 <c>silver</c>、<c>round</c> 等)，
/// 再找当前执行上下文和剧情动态变量。legacy 的 <c>$MALE$</c>、<c>$FEMALE$</c>、<c>$ZHENLONG_LEVEL$</c> 是对应内建变量的别名。
/// 解析不到的占位符原样保留。
/// </summary>
internal sealed class StoryTextInterpolator
{
    private static readonly Regex PlaceholderPattern = new(@"\$([A-Za-z_][A-Za-z0-9_]*)\$", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> LegacyAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MALE"] = GameExpressionSymbols.HeroNameVariable,
        ["FEMALE"] = GameExpressionSymbols.HeroineNameVariable,
        ["ZHENLONG_LEVEL"] = GameExpressionSymbols.ZhenlongLevelVariable,
    };

    private readonly GameSession _session;
    private readonly StoryExecutionContext _context;

    public StoryTextInterpolator(GameSession session, StoryExecutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _context = context ?? StoryExecutionContext.Empty;
    }

    public string Interpolate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return text;
        }

        var resolver = new GameExpressionVariableResolver(_session, _context);
        return PlaceholderPattern.Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            name = LegacyAliases.GetValueOrDefault(name, name);
            return resolver.TryResolve(name, out var value) && TryFormat(value, out var formatted)
                ? formatted
                : match.Value;
        });
    }

    private static bool TryFormat(ExpressionValue value, out string formatted)
    {
        switch (value.Kind)
        {
            case ExpressionValueKind.String:
                formatted = value.AsString("story text placeholder");
                return true;
            case ExpressionValueKind.Number:
                formatted = value.AsNumber("story text placeholder").ToString(CultureInfo.InvariantCulture);
                return true;
            case ExpressionValueKind.Boolean:
                formatted = value.AsBoolean("story text placeholder") ? "true" : "false";
                return true;
            default:
                formatted = string.Empty;
                return false;
        }
    }
}
