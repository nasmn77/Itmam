using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Itmam.Win32;

/// <summary>Executable file names of processes, used to apply the excluded-apps list.</summary>
public static class ProcessNames
{
    private const int CacheLimit = 256;
    private static readonly Dictionary<uint, string?> Cache = [];
    private static readonly Lock CacheLock = new();

    /// <summary>Executable name of the process owning the foreground window, or null.</summary>
    public static string? ForegroundProcessName()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return null;
        }

        unsafe
        {
            uint processId;
            PInvoke.GetWindowThreadProcessId(foreground, &processId);
            return Of(processId);
        }
    }

    /// <summary>
    /// The app the user is working in: the foreground window's executable name, or null while the
    /// foreground is the taskbar, the notification area or Itmam itself (opening the tray menu).
    /// </summary>
    public static unsafe string? ForegroundAppName()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[64];
        int length;
        fixed (char* chars = buffer)
        {
            length = PInvoke.GetClassName(foreground, chars, buffer.Length);
        }

        var className = buffer[..Math.Max(0, length)];
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland")
        {
            return null;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(foreground, &processId);
        return processId == (uint)Environment.ProcessId ? null : Of(processId);
    }

    /// <summary>Executable file name (e.g. "WindowsTerminal.exe") or null if it cannot be read (e.g. protected process).</summary>
    public static string? Of(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        lock (CacheLock)
        {
            if (Cache.TryGetValue(processId, out var cached))
            {
                return cached;
            }
        }

        var path = PathOf(processId);
        var name = path?[(path.LastIndexOf('\\') + 1)..];
        lock (CacheLock)
        {
            // Process ids are reused; a small bounded cache keeps stale entries short-lived.
            if (Cache.Count >= CacheLimit)
            {
                Cache.Clear();
            }

            Cache[processId] = name;
        }

        return name;
    }

    /// <summary>Full path of the process's executable, or null if it cannot be read (e.g. protected process).</summary>
    public static unsafe string? PathOf(uint processId)
    {
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
        {
            return null;
        }

        try
        {
            const int capacity = 1024;
            var buffer = stackalloc char[capacity];
            var length = (uint)capacity;
            if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, &length))
            {
                return null;
            }

            return new string(buffer, 0, (int)length);
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }
}
