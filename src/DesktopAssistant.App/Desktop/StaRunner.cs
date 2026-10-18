namespace DesktopAssistant.Desktop;

/// <summary>Runs code on a fresh STA thread (needed for Shell / Office COM objects and the clipboard).</summary>
public static class StaRunner
{
    public static Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }) { IsBackground = true, Name = "STA worker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return tcs.Task;
    }

    public static Task RunAsync(Action work, CancellationToken cancellationToken = default) => RunAsync(() => { work(); return true; }, cancellationToken);
}
