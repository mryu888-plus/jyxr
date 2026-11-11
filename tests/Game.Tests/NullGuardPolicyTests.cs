namespace Game.Tests;

/// <summary>
/// 空值约定：内部层（Game.Core、Game.Presentation）不写 ArgumentNullException.ThrowIfNull，依靠可空引用类型（nullable 警告视为错误）；
/// 显式运行时校验只留在边界层（Godot 宿主、Game.Application 用例入口、Game.Content 内容加载、Game.Expressions 外部表达式解析）。
/// 合并新代码后若失败，运行 tools/StripInternalNullGuards.ps1。
/// </summary>
public sealed class NullGuardPolicyTests
{
    [Theory]
    [InlineData("src/Game.Core")]
    [InlineData("src/Game.Presentation")]
    public void InternalLayers_DoNotUseRuntimeNullGuards(string relativeRoot)
    {
        var root = Path.Combine(FindRepositoryRoot(), relativeRoot.Replace('/', Path.DirectorySeparatorChar));
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (path, line, index))
                .Where(static entry => entry.line.Contains("ArgumentNullException.ThrowIfNull", StringComparison.Ordinal))
                .Select(entry => $"{Path.GetRelativePath(root, entry.path)}:{entry.index + 1}"))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "engine-free-rpg.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
