using Itmam.Core.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Itmam.Win32;

/// <summary>
/// Types a <see cref="TextEdit"/> into the focused app with one batched <c>SendInput</c> call:
/// Backspace key presses, then each UTF-16 code unit as a <c>KEYEVENTF_UNICODE</c> press.
/// </summary>
/// <remarks>
/// If Ctrl is physically held (Ctrl+N accepted the suggestion) a Ctrl key-up is injected first;
/// otherwise the app would see Ctrl+Backspace (delete a word) and Ctrl+letters (shortcuts).
/// All these events carry LLKHF_INJECTED, so our own keyboard hook ignores them.
/// UIPI blocks injection into elevated windows: then fewer events are inserted than sent and
/// the edit is reported as failed (the app stays silent there from Phase 5 on).
/// </remarks>
public sealed class SendInputInjector : ITextInjector
{
    public unsafe bool Send(TextEdit edit)
    {
        var inputs = new List<INPUT>((edit.Backspaces + edit.DeleteForward + edit.Text.Length + 2) * 2);

        foreach (var ctrl in new[] { VIRTUAL_KEY.VK_LCONTROL, VIRTUAL_KEY.VK_RCONTROL })
        {
            if (PInvoke.GetAsyncKeyState((int)ctrl) < 0)
            {
                inputs.Add(Key(ctrl, '\0', KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
            }
        }

        for (var i = 0; i < edit.Backspaces; i++)
        {
            inputs.Add(Key(VIRTUAL_KEY.VK_BACK, '\0', 0));
            inputs.Add(Key(VIRTUAL_KEY.VK_BACK, '\0', KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
        }

        // Delete is logical (the next character in reading order), so it works in RTL text too.
        // KEYEVENTF_EXTENDEDKEY: the navigation-block Delete, not the numpad one.
        for (var i = 0; i < edit.DeleteForward; i++)
        {
            inputs.Add(Key(VIRTUAL_KEY.VK_DELETE, '\0', KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY));
            inputs.Add(Key(VIRTUAL_KEY.VK_DELETE, '\0', KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
        }

        foreach (var unit in edit.Text)
        {
            // A line break (long phrases) is Shift+Enter (user decision): a new line in chat apps
            // (WhatsApp, Teams), where Enter sends the message, and in Notepad, mail and Word (a line
            // break there). A typed U+000A is ignored or shown as a box by many apps.
            if (unit == '\n')
            {
                inputs.Add(Key(VIRTUAL_KEY.VK_SHIFT, '\0', 0));
                inputs.Add(Key(VIRTUAL_KEY.VK_RETURN, '\0', 0));
                inputs.Add(Key(VIRTUAL_KEY.VK_RETURN, '\0', KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
                inputs.Add(Key(VIRTUAL_KEY.VK_SHIFT, '\0', KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
                continue;
            }

            inputs.Add(Key(0, unit, KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE));
            inputs.Add(Key(0, unit, KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
        }

        if (inputs.Count == 0)
        {
            return true;
        }

        var array = inputs.ToArray();
        var sent = PInvoke.SendInput(array, sizeof(INPUT));
        return sent == array.Length;
    }

    private static INPUT Key(VIRTUAL_KEY virtualKey, char scan, KEYBD_EVENT_FLAGS flags)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.ki.wVk = virtualKey;
        input.ki.wScan = scan;
        input.ki.dwFlags = flags;
        return input;
    }
}
