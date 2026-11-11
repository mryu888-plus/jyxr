namespace Game.Application;

/// <summary>
/// 宿主 UI 流程（确认框、出战选择、商店、选择面板等）的会话作用域。
/// 读档会整体替换 <see cref="GameState"/>，此前仍在等待玩家输入的流程如果继续执行，
/// 就会把旧状态下的结果写进新状态（覆盖存档、带旧选择进入战斗等）。
/// 统一语义：读档发布 <see cref="SaveLoadedEvent"/> 时作废当前作用域，
/// 所有经 <see cref="Link"/> 关联的等待立即取消；宿主负责关闭对应界面，
/// 确认类流程把取消视为“未确认”，其余流程按取消（<see cref="OperationCanceledException"/>）结束。
/// </summary>
public sealed class SessionFlowScope : IDisposable
{
    private readonly IDisposable _subscription;
    private CancellationTokenSource _current = new();
    private bool _disposed;

    public SessionFlowScope(SessionEvents events)
    {
        ArgumentNullException.ThrowIfNull(events);
        _subscription = events.Subscribe<SaveLoadedEvent>(_ => Invalidate());
    }

    /// <summary>当前作用域的令牌；读档后失效并换新。</summary>
    public CancellationToken Token => _current.Token;

    /// <summary>每次作废递增，可用于判断某个结果是否产生于当前作用域。</summary>
    public int Generation { get; private set; }

    /// <summary>把调用方令牌与当前作用域关联；任一取消都会取消返回的令牌源。调用方负责释放。</summary>
    public CancellationTokenSource Link(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CancellationTokenSource.CreateLinkedTokenSource(_current.Token, cancellationToken);
    }

    /// <summary>作废当前作用域：取消所有关联的等待，并开始新的作用域。</summary>
    public void Invalidate()
    {
        if (_disposed)
        {
            return;
        }

        var previous = _current;
        _current = new CancellationTokenSource();
        Generation++;
        try
        {
            previous.Cancel();
        }
        finally
        {
            previous.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
        _current.Cancel();
        _current.Dispose();
    }
}
