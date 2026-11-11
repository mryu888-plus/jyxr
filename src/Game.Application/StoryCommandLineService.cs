using System.Globalization;
using System.Text;

namespace Game.Application;

public sealed class StoryCommandLineService
{
    private readonly StoryCommandDispatcher _dispatcher;
    private readonly ExpressionParser _parser = new();

    public StoryCommandLineService(StoryCommandDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public StoryCommandInvocation Parse(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        if (IsFullDslCall(line))
        {
            var call = _parser.ParseCall(line, "debug console");
            if (call.Root.Arguments.Any(argument => argument is not LiteralExpressionSyntax))
            {
                throw new InvalidOperationException("Console Parse returns literal arguments only; execute the full DSL call directly.");
            }
            return new StoryCommandInvocation(call.Root.Name, call.Root.Arguments.Cast<LiteralExpressionSyntax>().Select(x => x.Value).ToArray());
        }

        var tokens = Tokenize(line);
        if (tokens.Count == 0) throw new InvalidOperationException("请输入有效指令。");
        return new StoryCommandInvocation(tokens[0].Text, tokens.Skip(1).Select(ParseToken).ToArray());
    }

    public async ValueTask<StoryCommandInvocation> ExecuteAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        if (IsFullDslCall(line))
        {
            var call = _parser.ParseCall(line, "debug console");
            await _dispatcher.ExecuteCallAsync(call, cancellationToken);
            return new StoryCommandInvocation(call.Root.Name, []);
        }

        var invocation = Parse(line);
        await _dispatcher.ExecuteCommandAsync(invocation.Name, invocation.Arguments, cancellationToken);
        return invocation;
    }

    private static bool IsFullDslCall(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index])) index++;
        if (index >= line.Length || line[index] != '_' && (line[index] < 'a' || line[index] > 'z')) return false;
        index++;
        while (index < line.Length && (line[index] == '_' || line[index] is >= 'a' and <= 'z' || char.IsDigit(line[index]))) index++;
        while (index < line.Length && char.IsWhiteSpace(line[index])) index++;
        return index < line.Length && line[index] == '(';
    }

    private static IReadOnlyList<ConsoleToken> Tokenize(string line)
    {
        // 空白分隔参数; 引号包住的是字符串; [ ... ] 是列表参数, 内部保留原文 (含引号和空白), 交给表达式解析器.
        var tokens = new List<ConsoleToken>();
        var current = new StringBuilder();
        char quote = '\0';
        var wasQuoted = false;
        var listDepth = 0;
        foreach (var ch in line)
        {
            if (listDepth > 0)
            {
                current.Append(ch);
                if (quote != '\0')
                {
                    if (ch == quote) quote = '\0';
                    continue;
                }

                if (ch is '\'' or '"') quote = ch;
                else if (ch == '[') listDepth++;
                else if (ch == ']' && --listDepth == 0) Flush(tokens, current, ref wasQuoted, isList: true);
                continue;
            }

            if (ch is '\'' or '"')
            {
                if (quote == '\0') { quote = ch; wasQuoted = true; continue; }
                if (quote == ch) { quote = '\0'; continue; }
            }
            if (ch == '[' && quote == '\0' && current.Length == 0 && !wasQuoted)
            {
                listDepth = 1;
                current.Append(ch);
                continue;
            }
            if (char.IsWhiteSpace(ch) && quote == '\0') { Flush(tokens, current, ref wasQuoted); continue; }
            current.Append(ch);
        }
        if (quote != '\0') throw new InvalidOperationException("命令行引号未闭合。");
        if (listDepth > 0) throw new InvalidOperationException("命令行列表括号未闭合。");
        Flush(tokens, current, ref wasQuoted);
        return tokens;
    }

    private static void Flush(List<ConsoleToken> tokens, StringBuilder current, ref bool wasQuoted, bool isList = false)
    {
        if (current.Length == 0 && !wasQuoted) return;
        tokens.Add(new ConsoleToken(current.ToString(), wasQuoted, isList));
        current.Clear();
        wasQuoted = false;
    }

    private ExpressionValue ParseToken(ConsoleToken token)
    {
        if (token.IsList) return ParseListLiteral(token.Text);
        if (token.WasQuoted) return ExpressionValue.FromString(token.Text);
        if (bool.TryParse(token.Text, out var boolean)) return ExpressionValue.FromBoolean(boolean);
        if (double.TryParse(token.Text, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)) return ExpressionValue.FromNumber(number);
        return ExpressionValue.FromString(token.Text);
    }

    private ExpressionValue ParseListLiteral(string text)
    {
        var expression = _parser.ParseExpression(text, "debug console list");
        return ToLiteralValue(expression.Root);
    }

    private static ExpressionValue ToLiteralValue(ExpressionSyntax syntax) => syntax switch
    {
        LiteralExpressionSyntax literal => literal.Value,
        ListExpressionSyntax list => ExpressionValue.FromList(list.Items.Select(ToLiteralValue).ToArray()),
        _ => throw new InvalidOperationException("命令行列表只能包含字面量，例如 [\"小还丹\", \"大还丹\"]。"),
    };

    private readonly record struct ConsoleToken(string Text, bool WasQuoted, bool IsList = false);
}

public sealed record StoryCommandInvocation(string Name, IReadOnlyList<ExpressionValue> Arguments);
