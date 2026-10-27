namespace Itmam.Core.Input;

/// <summary>
/// Apps where Itmam stays completely silent: nothing is tracked, shown, learned or read.
/// Matched by process file name, case-insensitively, with or without ".exe".
/// The user edits the list in Settings (Phase 6); these are the defaults.
/// </summary>
public sealed class ExcludedApps
{
    /// <summary>Remote desktops and VMs (keys go to another machine), terminals, game launchers.</summary>
    public static readonly IReadOnlyList<string> Defaults =
    [
        // Remote desktop / virtual machines
        "mstsc.exe", "vmconnect.exe", "VirtualBoxVM.exe", "vmware.exe", "vmware-vmx.exe",
        // Terminals (excluded by default; the user can re-enable them)
        "WindowsTerminal.exe", "OpenConsole.exe", "conhost.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "mintty.exe", "alacritty.exe", "wezterm-gui.exe",
        // Game launchers
        "steam.exe", "EpicGamesLauncher.exe", "Battle.net.exe", "GalaxyClient.exe",
    ];

    private readonly HashSet<string> _names;

    public ExcludedApps(IEnumerable<string>? processNames = null)
    {
        _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in processNames ?? Defaults)
        {
            if (Normalize(name) is { Length: > 0 } normalized)
            {
                _names.Add(normalized);
            }
        }
    }

    public IReadOnlyCollection<string> Names => _names;

    /// <param name="processName">File name or full path of the process, e.g. "C:\…\WindowsTerminal.exe".</param>
    public bool IsExcluded(string? processName) =>
        processName is not null && _names.Contains(Normalize(processName));

    private static string Normalize(string name)
    {
        var trimmed = name.Trim();
        var fileName = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".exe";
    }
}
