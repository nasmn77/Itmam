using Itmam.Core.Input;

namespace Itmam.App.SettingsUi;

/// <summary>
/// Inserts a long phrase by pasting it (user decision 1.1): typing hundreds of characters as key presses
/// made Word stop responding. The user's clipboard is saved first and put back afterwards, and the
/// phrase is marked so that Windows keeps it out of the clipboard history (Win+V) and cloud sync.
/// UI thread (the clipboard needs an STA thread).
/// </summary>
internal static class ClipboardPaste
{
    // Registered by Windows for clipboard history; see "Clipboard formats" in the Windows docs.
    private const string ExcludeFromMonitoring = "ExcludeClipboardContentFromMonitorProcessing";
    private const string CanIncludeInHistory = "CanIncludeInClipboardHistory";
    private const string CanUploadToCloud = "CanUploadToCloudClipboard";

    /// <summary>How long the app gets to read the clipboard after Ctrl+V before it is restored.</summary>
    private static readonly TimeSpan ReadTime = TimeSpan.FromMilliseconds(700);

    /// <summary>Pastes <paramref name="text"/> (line breaks as <c>\n</c>) into the focused app.</summary>
    /// <returns>False when the clipboard could not be used (another app holds it).</returns>
    public static async Task<bool> PasteAsync(string text, ITextInjector injector)
    {
        var saved = Save();
        var data = new DataObject();
        data.SetText(text.Replace("\n", "\r\n", StringComparison.Ordinal), TextDataFormat.UnicodeText);
        data.SetData(ExcludeFromMonitoring, new MemoryStream([0, 0, 0, 0]));
        data.SetData(CanIncludeInHistory, new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData(CanUploadToCloud, new MemoryStream(BitConverter.GetBytes(0)));
        try
        {
            Clipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 50);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }

        var pasted = injector.Paste();
        await Task.Delay(ReadTime);
        Restore(saved);
        return pasted;
    }

    // A copy of every format on the clipboard that can be read now (text, HTML, images, files…).
    private static DataObject? Save()
    {
        try
        {
            if (Clipboard.GetDataObject() is not { } current)
            {
                return null;
            }

            var copy = new DataObject();
            foreach (var format in current.GetFormats(autoConvert: false))
            {
                try
                {
                    if (current.GetData(format, autoConvert: false) is { } value)
                    {
                        copy.SetData(format, value);
                    }
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or OutOfMemoryException)
                {
                    // A format that cannot be read now (delayed rendering): left out.
                }
            }

            return copy.GetFormats().Length > 0 ? copy : null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static void Restore(DataObject? saved)
    {
        try
        {
            if (saved is null)
            {
                Clipboard.Clear();
            }
            else
            {
                Clipboard.SetDataObject(saved, copy: true, retryTimes: 5, retryDelay: 50);
            }
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is busy: it keeps the phrase.
        }
    }
}
