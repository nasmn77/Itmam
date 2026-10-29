using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Itmam.Core.Input;
using Itmam.Core.Presentation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Itmam.Win32;


/// <summary>
/// Global keyboard and mouse low-level hooks plus foreground/focus WinEvent hooks, running on a
/// dedicated thread with its own message loop. Events are published to <see cref="Events"/>;
/// all real work happens on the reader's thread.
/// </summary>
/// <remarks>
/// Windows silently removes a low-level hook whose callback exceeds LowLevelHooksTimeout, so the
/// callbacks only filter, translate and enqueue, never block. Keys pass to the app untouched
/// unless <see cref="HotkeyInterceptor"/> decides they act on the visible suggestion bar (or are
/// the global toggle); such a key, and its matching key-up, is swallowed. Injected input
/// (LLKHF_INJECTED, including our own SendInput) is ignored. A timer compares the system's
/// last-input time with the last time our hooks ran; if the system saw input we did not, the
/// hooks are reinstalled.
/// </remarks>
public sealed unsafe class InputHookService : IDisposable
{
    private const uint ToUnicodeNoStateChange = 0x4;
    private const uint HealthTimerMs = 2000;
    private const uint MissedInputThresholdMs = 1500;
    private const long ReinstallCooldownMs = 10_000;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYUP = 0x0105;

    // Multiple writers: the hook thread, and the UI thread for clicks on the suggestion bar.
    private readonly Channel<InputRecord> _channel =
        Channel.CreateUnbounded<InputRecord>(new UnboundedChannelOptions { SingleReader = true });

    private readonly PopupState _popup;
    private volatile HotkeyInterceptor _interceptor;
    private readonly bool[] _swallowedKeys = new bool[256];
    private volatile bool _enabled = true;

    // Delegates must stay referenced for as long as the hooks are installed.
    private readonly HOOKPROC _keyboardProc;
    private readonly HOOKPROC _mouseProc;
    private readonly WINEVENTPROC _winEventProc;

    // Reused by the hook thread only; no allocation per keystroke.
    private readonly byte[] _keyState = new byte[256];
    private readonly char[] _chars = new char[8];

    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private uint _threadId;
    private HHOOK _keyboardHook;
    private HHOOK _mouseHook;
    private HWINEVENTHOOK _foregroundHook;
    private HWINEVENTHOOK _focusHook;
    private readonly ForegroundLayoutCache _layouts = new();
    private (HWND Window, int Object, int Child) _lastFocus;
    private uint _lastHookTick;
    private long _lastReinstallTick;
    private Exception? _startError;

    /// <param name="popup">What the suggestion bar shows (read on every key-down).</param>
    /// <param name="hotkeys">Keys that act on the bar.</param>
    public InputHookService(PopupState popup, HotkeySettings hotkeys)
    {
        _popup = popup;
        _interceptor = new HotkeyInterceptor(hotkeys);
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
        _winEventProc = WinEventProc;
    }

    public ChannelReader<InputRecord> Events => _channel.Reader;

    /// <summary>Toggled by the global hotkey; while false nothing is tracked or intercepted.</summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                Publish(InputEvent.Of(InputEventKind.ToggleEnabled), 0);
            }
        }
    }

    /// <summary>Keys acting on the bar; replaced live from Settings (the hook reads the new instance on the next key).</summary>
    public HotkeySettings Hotkeys
    {
        get => _interceptor.Settings;
        set => _interceptor = new HotkeyInterceptor(value);
    }

    /// <summary>Publishes an event from another thread (e.g. a click on a suggestion).</summary>
    public void Post(InputEvent input) => Publish(input, KeyboardLayouts.ForegroundLayout());

    public HookDiagnostics Diagnostics { get; } = new();

    /// <summary>Starts the hook thread and waits until the hooks are installed.</summary>
    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Itmam input hooks", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _started.Wait();
        if (_startError is not null)
        {
            throw new InvalidOperationException("Could not install the input hooks.", _startError);
        }
    }

    public void Dispose()
    {
        if (_thread is { IsAlive: true })
        {
            PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _channel.Writer.TryComplete();
        _started.Dispose();
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        try
        {
            WarmUp();
            InstallLowLevelHooks();
            const uint flags = PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS;
            _foregroundHook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, default(HMODULE), _winEventProc, 0, 0, flags);
            _focusHook = PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_FOCUS, PInvoke.EVENT_OBJECT_FOCUS, default(HMODULE), _winEventProc, 0, 0, flags);
            PInvoke.SetTimer(default, 0, HealthTimerMs, null);
        }
        catch (Exception ex)
        {
            _startError = ex;
        }
        finally
        {
            _started.Set();
        }

        if (_startError is not null)
        {
            UninstallAll();
            return;
        }

        // Message loop: delivers hook callbacks, WinEvents and the health timer to this thread.
        MSG msg;
        while (PInvoke.GetMessage(&msg, default, 0, 0).Value > 0)
        {
            if (msg.message == PInvoke.WM_TIMER)
            {
                CheckHookHealth();
            }

            PInvoke.DispatchMessage(&msg);
        }

        UninstallAll();
    }

    /// <summary>
    /// Runs every code path a real keystroke takes before the hooks are installed, so JIT
    /// compilation and P/Invoke stub generation do not happen inside the first real callback
    /// (a first callback measured ~4.6 ms without this, against a 1 ms budget).
    /// </summary>
    private void WarmUp()
    {
        // Native → managed thunks of the hook delegates. code = -1 means "pass on, do nothing".
        var keyboard = (delegate* unmanaged<int, WPARAM, LPARAM, LRESULT>)Marshal.GetFunctionPointerForDelegate(_keyboardProc);
        var mouse = (delegate* unmanaged<int, WPARAM, LPARAM, LRESULT>)Marshal.GetFunctionPointerForDelegate(_mouseProc);
        var winEvent = (delegate* unmanaged<HWINEVENTHOOK, uint, HWND, int, int, uint, uint, void>)Marshal.GetFunctionPointerForDelegate(_winEventProc);
        keyboard(-1, default, default);
        mouse(-1, default, default);
        winEvent(default, 0, default, 0, 0, 0, 0); // event 0 is ignored

        // Classification, layout lookup and ToUnicodeEx for a letter, an event key and a modifier.
        var letter = new KBDLLHOOKSTRUCT { vkCode = 0x41, scanCode = 0x1E };
        var backspace = new KBDLLHOOKSTRUCT { vkCode = 0x08, scanCode = 0x0E };
        var shift = new KBDLLHOOKSTRUCT { vkCode = 0xA0, scanCode = 0x2A };
        OnKeyDown(&letter, publish: false);
        OnKeyDown(&backspace, publish: false);
        OnKeyDown(&shift, publish: false);

        // Hotkey decisions with a visible bar, on a separate interceptor (no state change here).
        var warmInterceptor = new HotkeyInterceptor(_interceptor.Settings);
        var bar = new PopupSnapshot(["ا", "ب"], System.Drawing.Rectangle.Empty);
        warmInterceptor.OnKeyDown(0x09, KeyModifiers.None, bar);
        warmInterceptor.OnKeyDown(0x32, KeyModifiers.Ctrl, bar);
        warmInterceptor.OnKeyDown(0x41, KeyModifiers.None, _popup.Current);
        warmInterceptor.OnKeyDown(0xA2, KeyModifiers.Alt, _popup.Current);
        warmInterceptor.OnKeyUp(0xA2);
        warmInterceptor.CancelChord();

        // Channel write/read code for InputRecord, on a throwaway channel so no fake event is published.
        var probe = Channel.CreateUnbounded<InputRecord>(new UnboundedChannelOptions { SingleReader = true });
        probe.Writer.TryWrite(new InputRecord(InputEvent.Char('ا'), KeyboardLayouts.ForegroundLayout(), Stopwatch.GetTimestamp()));
        probe.Reader.TryRead(out _);

        Diagnostics.RecordCallback(Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp()));
        Diagnostics.ResetMax();
        _ = Volatile.Read(ref _lastHookTick);
    }

    private void InstallLowLevelHooks()
    {
        var module = new HINSTANCE((nint)PInvoke.GetModuleHandle((PCWSTR)null).Value);
        _keyboardHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboardHook.IsNull || _mouseHook.IsNull)
        {
            throw new System.ComponentModel.Win32Exception();
        }

        _lastHookTick = (uint)Environment.TickCount;
    }

    private void UninstallLowLevelHooks()
    {
        if (!_keyboardHook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = default;
        }

        if (!_mouseHook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = default;
        }
    }

    private void UninstallAll()
    {
        UninstallLowLevelHooks();
        if (!_foregroundHook.IsNull)
        {
            PInvoke.UnhookWinEvent(_foregroundHook);
        }

        if (!_focusHook.IsNull)
        {
            PInvoke.UnhookWinEvent(_focusHook);
        }
    }

    // If the system received input after the last time our hooks ran, Windows probably removed
    // them (callback timeout, sleep/resume): reinstall, at most once per cooldown period.
    private void CheckHookHealth()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        if (!PInvoke.GetLastInputInfo(&info))
        {
            return;
        }

        var missedMs = (int)(info.dwTime - Volatile.Read(ref _lastHookTick));
        var now = Environment.TickCount64;
        if (missedMs > MissedInputThresholdMs && now - _lastReinstallTick > ReinstallCooldownMs)
        {
            _lastReinstallTick = now;
            try
            {
                UninstallLowLevelHooks();
                InstallLowLevelHooks();
                Diagnostics.Reinstalls++;
            }
            catch (Exception)
            {
                Diagnostics.CallbackErrors++;
            }
        }
    }

    private LRESULT KeyboardProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            // Measured from inside managed code: a GC already running when Windows calls us delays
            // the entry itself and is not included. A GC during the body is detected by the count.
            var start = Stopwatch.GetTimestamp();
            var gcCount = GC.CollectionCount(0);
            var swallow = false;
            try
            {
                Volatile.Write(ref _lastHookTick, (uint)Environment.TickCount);
                var data = (KBDLLHOOKSTRUCT*)lParam.Value;
                var message = (uint)wParam.Value;
                var injected = (data->flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED) != 0;
                if (!injected)
                {
                    if (message == PInvoke.WM_KEYDOWN || message == PInvoke.WM_SYSKEYDOWN)
                    {
                        Diagnostics.KeyEvents++;
                        swallow = OnKeyDownIntercept(data);
                    }
                    else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                    {
                        if (data->vkCode < 256 && _swallowedKeys[data->vkCode])
                        {
                            // The app never saw this key go down, so it must not see it go up either.
                            _swallowedKeys[data->vkCode] = false;
                            swallow = true;
                        }
                        else if (_interceptor.OnKeyUp((int)data->vkCode))
                        {
                            // Modifiers-only toggle (Ctrl+Alt released): the key-up still reaches the app.
                            _enabled = !_enabled;
                            Publish(InputEvent.Of(InputEventKind.ToggleEnabled), 0);
                        }
                    }
                }
            }
            catch (Exception)
            {
                Diagnostics.CallbackErrors++;
            }

            Diagnostics.RecordKeyCallback(Stopwatch.GetElapsedTime(start), GC.CollectionCount(0) != gcCount);
            if (swallow)
            {
                return new LRESULT(1); // the key does not reach the app (nor later hooks)
            }
        }

        return PInvoke.CallNextHookEx(default, code, wParam, lParam);
    }

    /// <returns>True when the key must be swallowed.</returns>
    private bool OnKeyDownIntercept(KBDLLHOOKSTRUCT* data)
    {
        var modifiers = CurrentModifiers(data);
        var decision = _interceptor.OnKeyDown((int)data->vkCode, modifiers, _enabled ? _popup.Current : PopupSnapshot.Hidden, _enabled && _popup.CanUndoAccept);
        if (HotkeyRules.ModifierOf((int)data->vkCode) == KeyModifiers.None)
        {
            _popup.SetUndoAccept(false); // only the very next key can undo an accept
        }

        if (decision.Command is { Kind: InputEventKind.ToggleEnabled })
        {
            _enabled = !_enabled;
            _swallowedKeys[data->vkCode & 0xFF] = true;
            Publish(InputEvent.Of(InputEventKind.ToggleEnabled), 0);
            return true;
        }

        if (!_enabled)
        {
            return false; // disabled: nothing is tracked, every key passes
        }

        if (decision.Command is { } command)
        {
            Publish(command, _layouts.Current());
        }

        if (decision.Action == InterceptAction.Swallow)
        {
            _swallowedKeys[data->vkCode & 0xFF] = true;
            return true;
        }

        OnKeyDown(data, publish: true);
        return false;
    }

    private void OnKeyDown(KBDLLHOOKSTRUCT* data, bool publish)
    {
        var modifiers = CurrentModifiers(data);
        var classification = KeyClassifier.Classify((int)data->vkCode, modifiers);
        if (classification.Action == KeyAction.Ignore)
        {
            return;
        }

        var layout = _layouts.Current();
        if (classification.Action == KeyAction.Event)
        {
            if (publish)
            {
                Publish(InputEvent.Of(classification.Kind), layout);
            }

            return;
        }

        var count = Translate(data, modifiers, layout);
        if (!publish || count < 0)
        {
            return; // warm-up, or a dead key waiting for the next key
        }

        if (count == 0)
        {
            Publish(InputEvent.Of(InputEventKind.Navigation), layout); // F-keys etc.
            return;
        }

        for (var i = 0; i < count; i++)
        {
            Publish(InputEvent.Char(_chars[i]), layout);
        }
    }

    // Translates with the foreground window's layout. Flag 0x4 keeps the system keyboard state
    // (and pending dead keys) of the app being typed in untouched.
    private int Translate(KBDLLHOOKSTRUCT* data, KeyModifiers modifiers, nint layout)
    {
        Array.Clear(_keyState);
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            _keyState[0x10] = 0x80;
        }

        if (modifiers.HasFlag(KeyModifiers.Ctrl) && modifiers.HasFlag(KeyModifiers.Alt))
        {
            _keyState[0x11] = 0x80;
            _keyState[0x12] = 0x80;
        }

        if ((PInvoke.GetKeyState(0x14) & 1) != 0)
        {
            _keyState[0x14] = 0x01; // Caps Lock toggled
        }

        fixed (byte* state = _keyState)
        fixed (char* chars = _chars)
        {
            return PInvoke.ToUnicodeEx(data->vkCode, data->scanCode, state, chars, _chars.Length, ToUnicodeNoStateChange, new HKL(layout));
        }
    }

    private static KeyModifiers CurrentModifiers(KBDLLHOOKSTRUCT* data)
    {
        static bool Down(int vk) => PInvoke.GetAsyncKeyState(vk) < 0;

        var modifiers = KeyModifiers.None;
        if (Down(0x10))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (Down(0x11))
        {
            modifiers |= KeyModifiers.Ctrl;
        }

        if ((data->flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_ALTDOWN) != 0)
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (Down(0x5B) || Down(0x5C))
        {
            modifiers |= KeyModifiers.Win;
        }

        return modifiers;
    }

    private LRESULT MouseProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            try
            {
                Volatile.Write(ref _lastHookTick, (uint)Environment.TickCount);
                var message = (uint)wParam.Value;
                if (message is PInvoke.WM_LBUTTONDOWN or PInvoke.WM_RBUTTONDOWN or PInvoke.WM_MBUTTONDOWN or PInvoke.WM_XBUTTONDOWN)
                {
                    _interceptor.CancelChord(); // Ctrl+Alt+click is not the toggle
                    _popup.SetUndoAccept(false);
                }

                if (_enabled && message is PInvoke.WM_LBUTTONDOWN or PInvoke.WM_RBUTTONDOWN or PInvoke.WM_MBUTTONDOWN or PInvoke.WM_XBUTTONDOWN)
                {
                    // A click on the suggestion bar is handled by the bar (accepts a word); the
                    // caret in the app does not move, so it is not a context change.
                    var point = ((MSLLHOOKSTRUCT*)lParam.Value)->pt;
                    var bar = _popup.Current;
                    if (!((bar.IsVisible && bar.Bounds.Contains(point.X, point.Y)) || bar.Actions.Contains(point.X, point.Y)))
                    {
                        Diagnostics.MouseClicks++;
                        Publish(InputEvent.Of(InputEventKind.MouseClick), _layouts.Current());
                    }
                }
            }
            catch (Exception)
            {
                Diagnostics.CallbackErrors++;
            }
        }

        return PInvoke.CallNextHookEx(default, code, wParam, lParam);
    }

    private void WinEventProc(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        try
        {
            if (!_enabled)
            {
                return;
            }

            InputEventKind kind;
            if (eventType == PInvoke.EVENT_SYSTEM_FOREGROUND)
            {
                kind = InputEventKind.ForegroundChanged;
            }
            else if (eventType == PInvoke.EVENT_OBJECT_FOCUS)
            {
                // Some apps re-announce focus on the element that already has it while typing;
                // only a real move to another element resets the word.
                var focused = (hwnd, idObject, idChild);
                if (focused == _lastFocus)
                {
                    Diagnostics.RepeatedFocusEvents++;
                    return;
                }

                _lastFocus = focused;
                kind = InputEventKind.FocusChanged;
            }
            else
            {
                return;
            }

            Diagnostics.FocusEvents++;
            _layouts.Invalidate();
            Publish(InputEvent.Of(kind), _layouts.Current());
        }
        catch (Exception)
        {
            Diagnostics.CallbackErrors++;
        }
    }

    private void Publish(InputEvent input, nint layout) =>
        _channel.Writer.TryWrite(new InputRecord(input, layout, Stopwatch.GetTimestamp()));
}
