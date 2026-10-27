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

### Caret context (Backspace into existing text)

Backspace on an empty word moves the caret into text the app did not see typed (the previous
word, or text that existed before). `WordBuffer` then becomes *detached*: letters are ignored, so
no wrong suggestions are made from a half-seen word.

To resume quickly, `InputPipeline` waits 40 ms after the last such Backspace (so the app has
applied it; holding Backspace probes only once) and asks `UiaCaretContextSource` for the text
before the caret:

1. `IUIAutomation.GetFocusedElement()`; if `CurrentIsPassword` → nothing is read.
2. Caret range from `TextPattern2.GetCaretRange`, or the (empty) selection from `TextPattern`.
3. A copy of the range is extended **backwards by at most 49 characters** and `GetText(49)` is called.

`WordBuffer.ResumeFromContext` keeps only the trailing Arabic word of that text (the word the
caret is in) as the current word, or resumes with an empty word if a boundary or the start of the
field precedes the caret. The rest of the text is discarded immediately; nothing is stored or
logged. If keys arrive while reading, the result is discarded. If the app has no UI Automation
text support, the call times out (250 ms, on a dedicated MTA thread) or fails, tracking stays
paused and resumes at the next boundary, click, navigation key, shortcut (e.g. Ctrl+A) or focus
change.

### Robustness

- Every callback is wrapped in `try/catch`; an exception increments a counter and the input is
  still passed on.
- Windows silently removes a low-level hook that exceeds `LowLevelHooksTimeout`. Every 2 s the
  hook thread compares `GetLastInputInfo` with the last time its hooks ran; if the system saw
  input the hooks missed for > 1.5 s, both low-level hooks are reinstalled (at most once per 10 s).
- Hook delegates are held in fields so the GC cannot collect them while installed.

## Privacy guarantees in the code

- Typed characters exist only in memory: in the channel, in `WordBuffer`, and on screen.
- The only text read from other apps is ≤ 49 characters before the caret, after Backspace on an
  empty word, never from password fields; only the current word is kept from it.
- No code path writes characters to a log, file, or the network. The debug window's event list
  shows event *kinds* and latencies only (letters are shown as `Character (letter)`).
- Hook diagnostics (`HookDiagnostics`) are counters and timings only.
- Storage of learned words (word → count/last-used only) is added in Phase 5 and will be
  documented here.
