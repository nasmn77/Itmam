# Itmam architecture

This document describes every code path that touches keyboard input or stored data, so the
app can be audited for privacy. It is updated at the end of each phase.

## Projects

| Project | Role | Windows APIs |
|---|---|---|
| `Itmam.Core` | Pure logic: normalization, dictionary, ranking, word buffer, key classification | none (`net10.0`) |
| `Itmam.Win32` | Hooks, keyboard layout detection (later: caret, injection, guards) | yes, via CsWin32 |
| `Itmam.App` | WinForms composition root (Phase 2: temporary debug window) | WinForms |
| `tools/DictionaryBuilder` | Offline build of `data/itmam-ar.dict` | none |

## Input path (Phase 2)

```
 physical key / click / focus change
        │
        ▼
 Itmam.Win32.InputHookService  ── dedicated thread "Itmam input hooks" with its own message loop
   • WH_KEYBOARD_LL  → KeyboardProc
   • WH_MOUSE_LL     → MouseProc
   • SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_FOCUS, out-of-context, skip own process)
        │  callbacks only: filter → classify → translate → enqueue; always CallNextHookEx
        ▼
 Channel<InputRecord>  (unbounded, single writer, single reader; in memory only)
        │
        ▼
 Itmam.Core.Input.InputPipeline (worker task) → InputTracker → WordBuffer
        │            │
        │            └─ after Backspace on an empty word: Itmam.Win32.UiaCaretContextSource
        │               (reads ≤ 49 characters before the caret, see "Caret context" below)
        ▼
 UI thread: debug window shows the current word (Phase 3+: suggestion popup)
```

### What the keyboard callback does

1. Ignores everything except `WM_KEYDOWN` / `WM_SYSKEYDOWN`, and ignores injected events
   (`LLKHF_INJECTED`), so text the app types itself (Phase 4, `SendInput`) never feeds back.
2. Reads modifier state (`GetAsyncKeyState`, `LLKHF_ALTDOWN`) and classifies the key with
   `Itmam.Core.Input.KeyClassifier` (pure, unit-tested): modifiers are ignored; Backspace,
   Ctrl+Backspace, Esc, navigation/Delete, Ctrl/Alt/Win shortcuts and paste become events.
3. Typing keys are translated with `ToUnicodeEx` using the layout of the window that has keyboard
   focus (`KeyboardLayouts.ForegroundLayout`, via `GetGUIThreadInfo` so UWP apps and consoles
   report the right layout), with flag `0x4` so the target app's keyboard state and dead keys are
   not changed.
4. Writes a `HookEvent` (event, layout, timestamp) to the channel and returns. No allocation per
   keystroke apart from the channel slot; nothing blocks; no key is swallowed in Phase 2.

The mouse callback only reports button-down clicks (and records that the hook is alive on every
mouse move). WinEvents report foreground and focus changes.

### Word tracking

`InputTracker` resets the word whenever the keyboard layout changes and tracks nothing at all
while the layout is not Arabic (primary language `0x01` of the HKL low word), so English typing is
never buffered. `WordBuffer` applies the rules in `PROMPT.md` §4 and emits *Committed* only on a
word boundary.

### Caret context (caret moved into existing text)

Whenever the caret may now be inside text the app did not see typed — Backspace on an empty
word, a mouse click, arrows/Home/End/Delete, a focus, foreground or layout change, paste or a
shortcut — `WordBuffer` becomes *detached*: letters are ignored, so no wrong suggestions are made
from a half-seen word.

To resume quickly, `InputPipeline` waits 40 ms after the last such event (so the app has applied
it; holding a key probes only once) and asks `UiaCaretContextSource` for the text before the
caret:

1. `IUIAutomation.GetFocusedElement()`; if `CurrentIsPassword` → nothing is read.
2. Caret range from `TextPattern2.GetCaretRange`, or the (empty) selection from `TextPattern`.
3. A copy of the range is extended **backwards by at most 49 characters** and `GetText(49)` is called.

Nothing is read in excluded apps (`Itmam.Core.Input.ExcludedApps`; defaults: remote desktop/VMs,
terminals, game launchers). This is checked twice: by `InputPipelineOptions.CanReadContext`
against the foreground process before every read, and by `UiaCaretContextSource` against the
process that owns the focused UI Automation element right before reading.

`WordBuffer.ResumeFromContext` keeps only the trailing Arabic word of that text (the word the
caret is in) as the current word, or resumes with an empty word if a boundary or the start of the
field precedes the caret. The rest of the text is discarded immediately; nothing is stored or
logged. While text is selected — with the keyboard (Shift+navigation, Ctrl+Shift+arrows, Ctrl+A:
a `Selection` event, after which nothing is read) or with the mouse (UI Automation reports a
non-empty selection) — nothing is tracked or suggested, because typing replaces the selection;
the next plain caret move, click or boundary resumes. If keys arrive while reading, the result is discarded and the text is read again once
they settle (it then includes them). If the app has no UI Automation text support (the call fails
or times out after 250 ms, on a dedicated thread, and once more after 100 ms): after a caret move,
tracking resumes with an empty word if no letter was typed meanwhile; after Backspace, or if
letters were typed, it waits for the next boundary.

### Robustness

- Every callback is wrapped in `try/catch`; an exception increments a counter and the input is
  still passed on.
- Windows silently removes a low-level hook that exceeds `LowLevelHooksTimeout`. Every 2 s the
  hook thread compares `GetLastInputInfo` with the last time its hooks ran; if the system saw
  input the hooks missed for > 1.5 s, both low-level hooks are reinstalled (at most once per 10 s).
- Hook delegates are held in fields so the GC cannot collect them while installed.
- Before installing, every keystroke code path is run once (warm-up) so JIT and P/Invoke stub
  generation never happen inside a real callback.
- Latency target for the keyboard callback: **p99 < 1 ms and max < 5 ms** (Windows removes a
  hook only after hundreds of ms). The hook thread runs at `AboveNormal` priority, and the
  foreground input thread is cached (`ForegroundLayoutCache`), so a keystroke costs one
  `GetForegroundWindow` and one `GetKeyboardLayout` call. The layout itself is read on every key
  because a layout switch changes no window and raises no event we receive.
- An unavailable caret context is retried once after 100 ms: Chromium/Electron enable their
  accessibility tree on the first UI Automation request.
- A garbage collection pauses all managed threads, the hook thread included. The app runs with
  `GCLatencyMode.SustainedLowLatency` and the worker avoids per-event allocations; the debug
  window reports callbacks slower than 500 µs and how many of them overlapped a GC.

## Suggestion bar (Phase 3: display only)

```
 InputPipeline.Updated (pipeline thread)
        │  Arabic layout, not detached, ≥ MinChars letters, foreground app not excluded
        ▼
 SuggestionEngine.Suggest(word)            (Core; < 0.1 ms with the prefix cache)
        │
        ▼
 SuggestionController (one request at a time; stale keystrokes are dropped)
        │  CaretLocator.LocateAsync:
        │    1. GetGUIThreadInfo → rcCaret + ClientToScreen          (caller thread)
        │    2. UI Automation caret range → GetBoundingRectangles    (UiaWorker, 150 ms timeout)
        │       empty range → rectangle of the previous character (caret on its left in RTL)
        │    3. MSAA AccessibleObjectFromWindow(OBJID_CARET).accLocation
        │    4. mouse pointer (bar shown below it)
        ▼
 SuggestionPopup (UI thread)
    PopupPlacement: above the caret, right edge at the caret (RTL), below if no room,
    clamped to the caret monitor's work area; sizes scaled by that monitor's DPI.
```

The popup never takes focus: `ShowWithoutActivation`, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW |
WS_EX_TOPMOST`, `WM_MOUSEACTIVATE → MA_NOACTIVATE`, shown with `SetWindowPos(SWP_NOACTIVATE)`.
Its look (35 px high, 14 px Segoe UI, 15 px padding per word, no dividers, `#2C2C2C` / white in
dark mode) was measured from a screenshot of the native Windows 11 bar; text uses grayscale
antialiasing like the native bar. The bar only displays suggestions; nothing is inserted yet.

UI Automation cost in Chromium (measured on an isolated Chrome instance with a small page):
the first request finds no TextPattern while Chrome enables its accessibility tree, the second
(100 ms later) succeeds; private memory 370 → 354 MB and idle CPU 1.9 % → 0.1 % (no measurable
increase; numbers dominated by Chrome finishing start-up). Large pages build larger trees.

## Accepting a suggestion (Phase 4)

```
 key-down in KeyboardProc (hook thread)
        │  HotkeyInterceptor.OnKeyDown(vk, modifiers, PopupState.Current)   (Core, pure)
        │    bar hidden            → Pass (every key reaches the app untouched)
        │    Tab                   → Swallow + AcceptSuggestion(0, word)
        │    Ctrl+1…9 (≤ shown)    → Swallow + AcceptSuggestion(n, word)
        │    Esc                   → Swallow + DismissSuggestions
        │    Ctrl+Alt+P (always)   → Swallow + ToggleEnabled
        │    native navigation (setting, off): Up enters, ←/→ select, Enter accepts, Esc/↓ leave
        │  a swallowed key's key-up is swallowed too
        ▼
 Channel<InputRecord> → InputPipeline (pipeline thread)
        AcceptSuggestion: TextEdit.Compute(typed, word, appendSpace) → ITextInjector.Send
                          → WordBuffer.AcceptWord (Committed: counts as used for learning)
        DismissSuggestions: suggestions suppressed until the word ends
        ▼
 SendInputInjector: one SendInput batch — Ctrl key-up if Ctrl is physically held (Ctrl+N),
 n × VK_BACK, then each UTF-16 unit as KEYEVENTF_UNICODE (+ space). Injected events carry
 LLKHF_INJECTED, so our hook ignores them.
```

Spaces: the caret-context read also returns the one character after the caret. It stays valid
while typing (text is inserted before it) and becomes unknown when the caret moves. On accept,
a following space is replaced (Delete, then word + space) so there are never two spaces, following
punctuation gets no space before it, and otherwise a space is appended.

The word in `AcceptSuggestion` is captured from the bar snapshot the hook saw, so exactly the
word on screen is inserted. `PopupState` is published by the UI thread on every show/hide and
read lock-free by the hook. Clicking a word on the bar posts the same event; clicks inside the
bar's bounds are not treated as caret moves. While disabled (Ctrl+Alt+P), the hooks publish
nothing and swallow nothing except the toggle key.

## Learning and storage (Phase 5)

```
 WordBuffer: word ended by a boundary, or accepted from the bar  → Committed
        ▼
 InputPipeline: the word is *pending*. The next event makes it final (PipelineUpdate.WordToLearn),
                except Backspace/Ctrl+Backspace right after it (the user is correcting it → dropped).
                On exit/log-off the pending word is final.
        ▼
 LearningService.Learn(word)    (Core)
    skip if paused, < 2 letters or not Arabic; tashkeel removed
    skip if on data/blocklist.txt and not already added by the user (manual adds are respected)
    UserWordIndex.RecordUse → count + 1, last-used = now
        ▼  (2 s after the last change, and on exit / log-off)
 SqliteUserWordStore  %LOCALAPPDATA%\Itmam\user.db   (MSIX: redirected to the package's private folder)
    table words(word TEXT PRIMARY KEY, count INTEGER, last_used INTEGER, blocked INTEGER)
    WAL journal: committed transactions survive crashes. A corrupt file is renamed
    user.db.corrupt-<timestamp> (kept) and a fresh database is created.
```

Stored per word: the word, how many times it was used, when it was last used, and whether the user
blocked it. Never sentences, word order, the app it was typed in, or anything typed in a silenced
window. A learned word is suggested after `MinUsesToSuggest` uses (default 2). "My words"
(Phase 6 UI) uses `LearningService`: add, edit, remove, block, import/export (`word<TAB>count`,
`!word` = blocked), pause, clear all. On the bar, right-click (or Ctrl+Delete while navigating)
forgets a learned word (`ForgetSuggestion` → `LearningService.Remove`); it is learned again if
used. Blocking for good is only offered in "My words".

## Where Itmam is silent (Phase 5)

`ForegroundGuard` (Win32, implements Core `IInputGuard`) is checked whenever the window, field or
caret may have changed. While it reports a reason, the pipeline tracks, reads, shows and learns
nothing:

| Reason | Detection |
|---|---|
| Password field | `ES_PASSWORD` on a focused Edit control; UI Automation `IsPassword` when the caret context is read |
| Elevated app | Mandatory integrity level of the foreground process above ours (or unreadable) |
| Excluded app | Process name in `ExcludedApps` |
| Full screen | `SHQueryUserNotificationState`: Direct3D exclusive full screen or presentation mode |
| IME / other languages | Only Arabic keyboard layouts are ever tracked |

## Privacy guarantees in the code

- Typed characters exist only in memory: in the channel, in `WordBuffer`, and on screen.
- The only text read from other apps is ≤ 49 characters before the caret, after the caret moved
  into existing text, never from password fields or excluded apps; only the current word is kept.
- No code path writes characters to a log, file, or the network. The debug window's event list
  shows event *kinds* and latencies only (letters are shown as `Character (letter)`).
- Hook diagnostics (`HookDiagnostics`) are counters and timings only.
- Storage of learned words (word → count/last-used only) is added in Phase 5 and will be
  documented here.
