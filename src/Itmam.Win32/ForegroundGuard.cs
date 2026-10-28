using Itmam.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Itmam.Win32;

/// <summary>
/// Decides whether Itmam may work in the foreground window (see <see cref="IInputGuard"/>):
/// silent in excluded apps, apps running at a higher integrity level (administrator — Windows
/// blocks our input there anyway), Direct3D full-screen games and presentation mode, and classic
/// Win32 password edits (ES_PASSWORD). Password fields of other frameworks are detected with
/// UI Automation when the caret context is read.
/// </summary>
/// <remarks>Called on the pipeline thread only. Every check is a few cheap system calls; per-process results are cached.</remarks>
public sealed class ForegroundGuard(ExcludedApps excludedApps) : IInputGuard
{
    private const int EditPasswordStyle = 0x20; // ES_PASSWORD
    private const int CacheLimit = 256;

    private readonly uint _ownIntegrity = IntegrityOf(PInvoke.GetCurrentProcess()) ?? 0x2000; // medium
    private readonly Dictionary<uint, bool> _higherIntegrity = [];

    public unsafe SilenceReason Check()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return SilenceReason.None;
        }

        uint processId;
        var threadId = PInvoke.GetWindowThreadProcessId(foreground, &processId);

        if (excludedApps.IsExcluded(ProcessNames.Of(processId)))
        {
            return SilenceReason.ExcludedApp;
        }

        if (IsHigherIntegrity(processId))
        {
            return SilenceReason.ElevatedApp;
        }

        if (PInvoke.SHQueryUserNotificationState(out var state).Succeeded
            && state is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
        {
            return SilenceReason.FullScreen;
        }

        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        if (PInvoke.GetGUIThreadInfo(threadId, &info) && !info.hwndFocus.IsNull && IsPasswordEdit(info.hwndFocus))
        {
            return SilenceReason.PasswordField;
        }

        return SilenceReason.None;
    }

    private static bool IsPasswordEdit(HWND window)
    {
        Span<char> className = stackalloc char[64];
        var length = PInvoke.GetClassName(window, className);
        return length > 0
            && className[..length].Contains("Edit", StringComparison.OrdinalIgnoreCase)
            && (PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE) & EditPasswordStyle) != 0;
    }

    private bool IsHigherIntegrity(uint processId)
    {
        if (_higherIntegrity.TryGetValue(processId, out var higher))
        {
            return higher;
        }

        // A process whose token we cannot even query (elevated or protected) is treated as higher.
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        uint? integrity = null;
        if (!process.IsNull)
        {
            integrity = IntegrityOf(process);
            PInvoke.CloseHandle(process);
        }

        higher = integrity is not { } level || level > _ownIntegrity;
        if (_higherIntegrity.Count >= CacheLimit)
        {
            _higherIntegrity.Clear(); // process ids are reused: keep the cache short-lived
        }

        _higherIntegrity[processId] = higher;
        return higher;
    }

    // Mandatory integrity level RID: 0x1000 low, 0x2000 medium, 0x3000 high (administrator), 0x4000 system.
    private static unsafe uint? IntegrityOf(HANDLE process)
    {
        HANDLE token;
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, &token))
        {
            return null;
        }

        try
        {
            const int size = 64;
            var buffer = stackalloc byte[size];
            uint returned;
            if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, buffer, size, &returned))
            {
                return null;
            }

            var sid = ((TOKEN_MANDATORY_LABEL*)buffer)->Label.Sid;
            var count = *PInvoke.GetSidSubAuthorityCount(sid);
            return count == 0 ? null : *PInvoke.GetSidSubAuthority(sid, (uint)(count - 1));
        }
        finally
        {
            PInvoke.CloseHandle(token);
        }
    }
}
