using System.Collections.Frozen;

namespace Itmam.Core.Input;

/// <summary>
/// Apps where Itmam stays completely silent: nothing is tracked, shown, learned or read.
/// Matched by process file name, case-insensitively, with or without ".exe".
/// The user edits the list in Settings; <see cref="Replace"/> applies it live (thread-safe).
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

    private volatile FrozenSet<string> _names;

    public ExcludedApps(IEnumerable<string>? processNames = null) => _names = Build(processNames ?? Defaults);

    public IReadOnlyCollection<string> Names => _names;

    /// <param name="processName">File name or full path of the process, e.g. "C:\…\WindowsTerminal.exe".</param>
    public bool IsExcluded(string? processName) =>
        processName is not null && _names.Contains(Normalize(processName));

    /// <summary>Applies an edited list (Settings).</summary>
    public void Replace(IEnumerable<string> processNames) => _names = Build(processNames);

    private static FrozenSet<string> Build(IEnumerable<string> processNames) =>
        processNames
            .Select(Normalize)
            .Where(name => name.Length > ".exe".Length)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string name)
    {
        var trimmed = name.Trim();
        var fileName = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".exe";
    }
}
