namespace Itmam.Core.Input;

/// <summary>Helpers for keyboard layout handles (HKL); the low word is the input language LANGID.</summary>
public static class KeyboardLayoutId
{
    private const int PrimaryLanguageMask = 0x3FF;
    private const int LangArabic = 0x01;

    /// <summary>True for every Arabic input language (all regions: 0x0401 Saudi, 0x0C01 Egypt, 0x1401 Algeria, …).</summary>
    public static bool IsArabic(nint keyboardLayout) => ((int)(keyboardLayout & 0xFFFF) & PrimaryLanguageMask) == LangArabic;
}
