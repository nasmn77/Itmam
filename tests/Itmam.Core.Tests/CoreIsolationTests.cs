using System.Reflection;
using System.Runtime.Versioning;

namespace Itmam.Core.Tests;

/// <summary>
/// Guards the rule that Itmam.Core stays free of Windows dependencies.
/// </summary>
public class CoreIsolationTests
{
    private static readonly Assembly Core = Assembly.Load("Itmam.Core");

    [Fact]
    public void Core_targets_platform_neutral_framework()
    {
        var framework = Core.GetCustomAttribute<TargetFrameworkAttribute>();

        framework.ShouldNotBeNull();
        framework.FrameworkName.ShouldBe(".NETCoreApp,Version=v10.0");
    }

    [Theory]
    [InlineData("System.Windows.Forms")]
    [InlineData("Microsoft.Win32")]
    [InlineData("Itmam.Win32")]
    [InlineData("Itmam.App")]
    public void Core_does_not_reference_windows_assemblies(string forbiddenPrefix)
    {
        var references = Core.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        references.ShouldNotContain(name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
    }
}
