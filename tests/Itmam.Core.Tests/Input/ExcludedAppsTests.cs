using Itmam.Core.Input;

namespace Itmam.Core.Tests.Input;

public class ExcludedAppsTests
{
    private readonly ExcludedApps _defaults = new();

    [Theory]
    [InlineData("mstsc.exe")]
    [InlineData("MSTSC.EXE")]
    [InlineData("mstsc")]
    [InlineData(@"C:\Windows\System32\mstsc.exe")]
    [InlineData("WindowsTerminal.exe")]
    [InlineData("pwsh.exe")]
    [InlineData("vmconnect.exe")]
    public void Default_list_excludes_remote_desktops_vms_and_terminals(string process) =>
        _defaults.IsExcluded(process).ShouldBeTrue();

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData("WINWORD.EXE")]
    [InlineData("chrome.exe")]
    [InlineData(null)]
    public void Ordinary_apps_are_not_excluded(string? process) => _defaults.IsExcluded(process).ShouldBeFalse();

    [Fact]
    public void Edited_list_applies_live()
    {
        var apps = new ExcludedApps();

        apps.Replace(["game", "  ", ""]);

        apps.IsExcluded("game.exe").ShouldBeTrue();
        apps.IsExcluded("mstsc.exe").ShouldBeFalse();
        apps.Names.Count.ShouldBe(1);
    }

    [Fact]
    public void Custom_list_replaces_the_defaults()
    {
        var custom = new ExcludedApps(["game"]);

        custom.IsExcluded("game.exe").ShouldBeTrue();
        custom.IsExcluded("mstsc.exe").ShouldBeFalse();
    }

    [Theory]
    [InlineData(@"C:\Program Files\Code\Code.exe", "Code.exe")]
    [InlineData("code", "code.exe")]
    [InlineData(" Notepad.EXE ", "Notepad.EXE")]
    public void Names_are_normalized(string input, string expected) =>
        ExcludedApps.NormalizeName(input).ShouldBe(expected);

    [Fact]
    public void Adding_normalizes_sorts_and_skips_duplicates()
    {
        var list = ExcludedApps.With(["zoom.exe", "cmd.exe"], @"C:\Windows\System32\CMD.EXE");
        list.ShouldBe(["cmd.exe", "zoom.exe"]);

        ExcludedApps.With(list, "Code").ShouldBe(["cmd.exe", "Code.exe", "zoom.exe"]);
    }

    [Fact]
    public void Removing_ignores_case_and_extension() =>
        ExcludedApps.Without(["cmd.exe", "Code.exe"], "code").ShouldBe(["cmd.exe"]);
}
