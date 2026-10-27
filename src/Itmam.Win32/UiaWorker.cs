using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Win32.UI.Accessibility;

namespace Itmam.Win32;

/// <summary>
/// Runs UI Automation / MSAA work on one dedicated MTA thread. These calls are cross-process and
/// can hang on unresponsive apps, so callers wait with a timeout and get the fallback value; only
/// one request runs at a time and new requests are refused while one is stuck.
/// </summary>
public sealed class UiaWorker : IDisposable
{
    private readonly BlockingCollection<Action<IUIAutomation>> _requests = new(boundedCapacity: 1);
    private int _busy;

    public UiaWorker()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Itmam UI Automation" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    /// <summary>Runs <paramref name="work"/> on the UI Automation thread.</summary>
    /// <returns>The result, or <paramref name="fallback"/> on timeout, error, or when busy.</returns>
    internal async Task<T> RunAsync<T>(Func<IUIAutomation, T> work, T fallback, TimeSpan timeout, CancellationToken cancellation)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return fallback;
        }

        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Execute(IUIAutomation automation)
        {
            try
            {
                result.TrySetResult(work(automation));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException or ArgumentException)
            {
                result.TrySetResult(fallback); // no UI Automation support, elevated app, element gone…
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        if (!_requests.TryAdd(Execute))
        {
            Volatile.Write(ref _busy, 0);
            return fallback;
        }

        try
        {
            return await result.Task.WaitAsync(timeout, cancellation).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return fallback;
        }
    }

    public void Dispose() => _requests.CompleteAdding();

    private void Run()
    {
        IUIAutomation? automation = null;
        foreach (var request in _requests.GetConsumingEnumerable())
        {
            try
            {
                automation ??= (IUIAutomation)new CUIAutomation8();
            }
            catch (COMException)
            {
                // UI Automation unavailable on this system: every request gets its fallback.
                Volatile.Write(ref _busy, 0);
                continue;
            }

            request(automation);
        }
    }
}
