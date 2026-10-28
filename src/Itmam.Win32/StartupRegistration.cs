using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Itmam.Win32;

/// <summary>
/// "Start with Windows". Installed from the Store (MSIX) it uses the package's
/// <c>desktop:StartupTask</c> (<see cref="TaskId"/>, declared in Package.appxmanifest), which the
/// user can also switch off in Windows Settings → Apps → Startup. Unpackaged (development builds)
/// it uses the <c>HKCU\…\Run</c> registry value.
/// </summary>
public static class StartupRegistration
{
    /// <summary>Must match the StartupTask TaskId in Package.appxmanifest.</summary>
    public const string TaskId = "ItmamStartup";

    /// <summary>Command-line flag passed when started at sign-in.</summary>
    public const string StartupArgument = "--startup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Itmam";

    /// <summary>Running from an MSIX package (installed from the Store or test-installed).</summary>
    public static unsafe bool IsPackaged
    {
        get
        {
            uint length = 0;
            return PInvoke.GetCurrentPackageFullName(&length, default) != WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE;
        }
    }

    public static async Task<bool> IsEnabledAsync()
    {
        if (IsPackaged)
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    /// <summary>Turns start-up on or off; returns the resulting state (the user or a policy may refuse).</summary>
    public static async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (IsPackaged)
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (!enabled)
            {
                task.Disable();
                return false;
            }

            var state = task.State == StartupTaskState.Disabled ? await task.RequestEnableAsync() : task.State;
            return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" {StartupArgument}");
        }
        else
        {
            key.DeleteValue(RunValue, throwOnMissingValue: false);
        }

        return enabled;
    }
}
