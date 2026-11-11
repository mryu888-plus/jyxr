using Game.Application;

namespace Game.Tests;

public sealed class SessionFlowScopeTests
{
    [Fact]
    public async Task SaveLoaded_CancelsPendingLinkedFlowAndStartsNewScope()
    {
        var events = new SessionEvents();
        using var scope = new SessionFlowScope(events);
        using var flow = scope.Link();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = flow.Token.Register(() => pending.TrySetCanceled(flow.Token));

        events.Publish(new SaveLoadedEvent());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Task);
        Assert.Equal(1, scope.Generation);
        Assert.False(scope.Token.IsCancellationRequested);
        using var next = scope.Link();
        Assert.False(next.Token.IsCancellationRequested);
    }

    [Fact]
    public void Link_PropagatesCallerCancellationWithoutInvalidatingScope()
    {
        var events = new SessionEvents();
        using var scope = new SessionFlowScope(events);
        using var caller = new CancellationTokenSource();
        using var flow = scope.Link(caller.Token);

        caller.Cancel();

        Assert.True(flow.Token.IsCancellationRequested);
        Assert.False(scope.Token.IsCancellationRequested);
        Assert.Equal(0, scope.Generation);
    }

    [Fact]
    public void Dispose_StopsListeningToSaveLoaded()
    {
        var events = new SessionEvents();
        var scope = new SessionFlowScope(events);
        scope.Dispose();

        events.Publish(new SaveLoadedEvent());

        Assert.Equal(0, scope.Generation);
        Assert.Throws<ObjectDisposedException>(() => scope.Link());
    }
}
