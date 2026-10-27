# Project Brief: Itmam (إتمام) — Arabic word prediction for Windows

> **App name (final):**
> - English: **Itmam** — Store title: *Itmam - Arabic Word Prediction*
> - Arabic: **إتمام** — Store title: *إتمام - التنبؤ بالكلمات العربية*
>
> Use `Itmam` for the solution, project names, namespaces, assembly names, mutex name, and the data folder (`%LOCALAPPDATA%\Itmam`). Use the Arabic name **إتمام** wherever the UI is shown in Arabic (tray tooltip, settings window title, About box, first-run notification, MSIX `ar` display name) and **Itmam** in the English UI.

You are a senior Windows/.NET engineer. Build a production-quality Windows desktop app that brings **as-you-type Arabic word suggestions on the physical keyboard**, a feature Windows 11 offers for English but that does not work for Arabic on many systems. The app will be **published publicly on the Microsoft Store as an MSIX package**, so code quality, privacy, stability and Store certification matter as much as features.

Before writing code: read this whole brief, then reply with (1) a short implementation plan, (2) any questions where the brief is ambiguous, (3) the risks you see. Wait for my answers, then work phase by phase (see "Phases"). After each phase: build, run tests, summarize what changed, and stop for my review.

---

## 1. Tech stack (fixed)

- **.NET 10 (LTS)** — the successor of ".NET Core"; use the .NET 10 SDK and pin it with a `global.json` (`"version": "10.0.100"`, `"rollForward": "latestFeature"`). All Microsoft packages must use their **10.x** versions to match (e.g. `Microsoft.Data.Sqlite` 10.x). Use Central Package Management (`Directory.Packages.props`) and a shared `Directory.Build.props` for common settings. C# latest, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, warnings as errors in Release.
- Target framework: `net10.0-windows10.0.19041.0`, `SupportedOSPlatformVersion` 10.0.19041.0 (Windows 10 2004+ and Windows 11).
- UI: **WinForms** (lightweight; good Arabic shaping and RTL support). Per-monitor DPI v2 (`ApplicationHighDpiMode=PerMonitorV2`).
- P/Invoke: use **Microsoft.Windows.CsWin32** source generator (`NativeMethods.txt`) instead of hand-written `DllImport` wherever possible.
- UI Automation: `UIAutomationClient` COM interop (or `Interop.UIAutomationClient`) for caret and password-field detection.
- Tests: **xUnit** + FluentAssertions (or Shouldly).
- Packaging: **Windows Application Packaging Project (.wapproj)** producing MSIX, `runFullTrust`.
- No third-party runtime dependencies unless clearly justified; list each one and its license.

## 2. Solution layout

```
Itmam.sln
src/
  Itmam.Core/        # pure logic, no Win32: normalization, trie, ranking, learning, word buffer state machine
  Itmam.Win32/       # hooks, caret locator, text injection, foreground/focus tracking, layout detection
  Itmam.App/         # WinForms: tray icon, suggestion popup, settings window, composition root
  Itmam.Package/     # .wapproj, Package.appxmanifest, Store assets
tools/
  DictionaryBuilder/         # console tool: builds the shipped dictionary from a source frequency list
tests/
  Itmam.Core.Tests/
data/
  README.md                  # where the source word list comes from + license
docs/
  PRIVACY.md  STORE_LISTING.md  ARCHITECTURE.md
README.md  README.ar.md  LICENSE  THIRD_PARTY_NOTICES.md
```

Keep `Core` 100% unit-testable with no Windows dependencies.

## 3. Core behaviour

1. **Background tray app, never in the taskbar.** The app has no main window: it starts with a custom `ApplicationContext` (not `Application.Run(new Form())`), lives only as an icon in the **system tray / notification area** (`NotifyIcon`), and never shows a taskbar button or Alt+Tab entry. Every form (popup, settings, about, debug window) sets `ShowInTaskbar = false`; the popup also uses `WS_EX_TOOLWINDOW`. Settings/About open from the tray menu (double-click the tray icon opens Settings) and closing them returns the app to the tray, it never exits. On first run, show a one-time tray balloon/toast explaining the app is running in the tray, and mention in README how to pin the icon so it is always visible (Windows 11 hides new tray icons in the overflow by default). It does nothing unless the **active keyboard layout of the foreground window is Arabic** (primary LANGID `0x01`, detect via `GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow()))`).
2. As the user types Arabic letters, the app tracks the **current word** (characters since the last word boundary).
3. When the current word has ≥ `MinChars` (default 2) letters, a small popup appears **just above the text caret** showing up to `N` suggestions (default 3), numbered, right-to-left.
4. Accepting a suggestion replaces the typed prefix with the full word, then (optional setting, default on) adds a space.
5. Default keys, **only intercepted while the popup is visible** (otherwise pass through untouched):
   - `Tab` → accept suggestion 1
   - `Ctrl+1`, `Ctrl+2`, `Ctrl+3` … → accept suggestion N
   - `Esc` → hide popup for the current word
   - All configurable in settings.
   - Mouse click on a suggestion also accepts it.
6. Global toggle hotkey `Ctrl+Alt+P` enables/disables the app (configurable). Tray menu mirrors it.

## 4. Keyboard hook (Itmam.Win32)

- `SetWindowsHookEx(WH_KEYBOARD_LL)` installed on a **dedicated thread with its own message loop**.
- The hook callback must return in well under 1 ms: Windows silently removes slow low-level hooks (`LowLevelHooksTimeout`). Do only: filter, translate, enqueue to a `Channel<T>`, decide swallow/pass. All other work happens on a worker / UI thread.
- **Ignore injected events** (`LLKHF_INJECTED`) so our own `SendInput` never feeds back into the buffer.
- Translate virtual keys to characters with `ToUnicodeEx` using the **foreground window's layout**, and pass flag `0x4` (do not change keyboard state) so dead keys and other apps' input are not corrupted.
- Word-buffer state machine (in Core, fed with abstract events so it is testable):
  - Arabic letter (U+0621–U+064A, U+0671–U+06D3 as needed, plus hamza forms) → append.
  - Tashkeel (U+064B–U+0652) and tatweel (U+0640) → append to buffer but ignored for matching.
  - Backspace → remove last char (empty → reset). `Ctrl+Backspace` → reset.
  - Space, Enter, punctuation (Arabic and Latin, including `،` `؛` `؟`), digits → **commit word** (for learning) and reset.
  - Arrows, Home/End, PageUp/Down, Delete, Esc, any Ctrl/Alt/Win shortcut, clipboard paste → reset without committing.
  - Foreground window change, focus change (`SetWinEventHook` `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_FOCUS`), any mouse click (`WH_MOUSE_LL`, cheap) → reset.
  - Keyboard layout switched mid-word → reset.
  - Auto-repeat keys behave like repeated presses.

## 5. Where the app must stay silent

- **Password fields**: check the focused element with UI Automation `IsPassword`; also treat `ES_PASSWORD` edit controls as passwords. Never buffer, show, or learn anything there.
- **Elevated (admin) windows**: UIPI blocks injection into higher-integrity processes. Detect (compare integrity levels / attempt check) and stay silent instead of half-working.
- **Excluded apps**: user-editable list of process names (defaults: common games launchers, `mstsc.exe`, `vmconnect.exe`, terminals optional).
- **Full-screen exclusive apps / games**: stay silent.
- IME composition active, or non-Arabic layout: silent.

## 6. Caret location

Try in order, use the first that returns a valid on-screen rectangle:
1. `GetGUIThreadInfo` → `rcCaret` + `ClientToScreen` (classic Win32 edits, Notepad, Office in many cases).
2. UI Automation: `TextPattern2.GetCaretRange` → `GetBoundingRectangles`; fallback `TextPattern.GetSelection`.
3. MSAA: `AccessibleObjectFromWindow(OBJID_CARET)` → `accLocation` (Chromium/Electron/Edge/Chrome usually respond here).
4. Fallback: just below the mouse cursor.

Cache per focused element briefly; locator calls run off the hook thread with a short timeout (UIA can hang on unresponsive apps).

## 7. Suggestion popup

- Borderless, topmost, **never takes focus**: override `ShowWithoutActivation => true`, `CreateParams` with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`; handle `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`.
- **Look and feel must match the native Windows 11 text-suggestions bar** (the one Windows shows for English when typing on a physical keyboard), so it feels like part of the OS:
  - A small **horizontal** bar just above the caret with the suggestions **side by side** (not a vertical list), separated by thin dividers, ordered right-to-left for Arabic (best suggestion on the right).
  - Same proportions: compact height, generous horizontal padding, Segoe UI Variable / Segoe UI, Windows 11 rounded corners (`DwmSetWindowAttribute DWMWA_WINDOW_CORNER_PREFERENCE`), subtle 1px border and soft shadow, selected item highlighted with the **Windows accent color**.
  - Small hotkey hints (e.g. a tiny `1 2 3` badge or `Tab` on the first item) are allowed but must stay subtle and can be turned off in settings.
  - Before building it, look at how the native bar appears (I will give you a screenshot if you ask) and match it as closely as possible.
  - Optional "native navigation" mode (setting, off by default): while the bar is visible, `Up` arrow moves focus into the bar, `Left`/`Right` select, `Enter` inserts, `Esc` leaves, exactly like Windows does for English.
- `RightToLeft = Yes`, `RightToLeftLayout = true`.
- Positioned above the caret (below if no room), clamped to the monitor work area, correct on mixed-DPI multi-monitor setups.
- **Themes / colors** (all applied live, no restart):
  - **"System" (default):** follows Windows automatically — light when Windows is in light mode, dark when in dark mode (`AppsUseLightTheme` registry value; switch instantly on `WM_SETTINGCHANGE` "ImmersiveColorSet"), and uses the current Windows accent color for the highlight (`DwmGetColorizationColor` / UISettings accent). In this mode it should look identical to the native bar.
  - **Light** and **Dark** fixed modes.
  - **Multiple background color presets** (at least 8 tasteful ones, e.g. white, light gray, charcoal, black, navy, dark green, warm beige, soft blue) plus a **custom color picker** for background and highlight.
  - Text color is chosen automatically for readable contrast (WCAG AA, ≥ 4.5:1) against whatever background is selected; the user can override it.
  - Optional transparency/opacity slider (80–100%).
- Font: Segoe UI Variable / Segoe UI (good Arabic support), size configurable.
- Updates without flicker (double buffering); never steals keystrokes.

## 8. Text insertion

- Use `SendInput` with `KEYEVENTF_UNICODE` for each UTF-16 code unit.
- Compute the minimal edit: if the suggestion starts with exactly what was typed, send only the **remaining suffix**. If normalization made them differ (e.g. user typed `ا` and suggestion is `أحمد`), send `prefixLength` backspaces then the full word.
- Send as one batched `SendInput` call. Then optional trailing space.
- After insertion, reset the buffer; count the accepted word as committed for learning.

## 9. Dictionary & matching (Itmam.Core)

**Normalization for matching only** (the displayed/inserted word keeps its original spelling):
- `أ إ آ ٱ` → `ا`
- `ى` → `ي` (matching only), `ة` ↔ `ه` treated as equal at word end
- Strip tashkeel and tatweel
- Keep `ء ؤ ئ` as-is for matching but allow `ؤ/ئ` to match `ء` as a lower-ranked fallback

**Data structure**: compact prefix trie (or sorted array + binary search on normalized keys) built at startup from a binary/compact file. Targets: startup load < 500 ms, lookup < 2 ms, dictionary memory < 80 MB (for 500k words).

**Source word list — the dictionary must be LARGE and rich** (do NOT invent or hallucinate words or frequencies; every word comes from a real corpus):
- Target: **at least 300,000 distinct words, ideally 500,000+**, covering Modern Standard Arabic plus common everyday/Gulf usage, with real frequencies.
- **Merge several openly licensed sources** rather than relying on one. Candidates to evaluate: Hermit Dave's *FrequencyWords* `ar_full` (OpenSubtitles, conversational), **Arabic Wikipedia** word counts computed from the official dump (formal/encyclopedic), and other open Arabic corpora you can find. For each source: verify the license yourself (avoid "non-commercial only" licenses unless I approve), document it in `data/README.md` and `THIRD_PARTY_NOTICES.md`, and **tell me before using it**.
- If a source needs downloading or processing (e.g. counting words from the Wikipedia dump), write that as a reproducible script in `tools/DictionaryBuilder` (with `--source` options) so anyone can regenerate the dictionary; ask me before any network download.
- Merging: normalize each source's frequencies to a common scale (e.g. per-million), then combine with configurable weights per source so both formal and conversational words rank well.
- `tools/DictionaryBuilder` outputs the shipped file: keep only Arabic letters, length 2–25, drop obvious junk (a letter repeated 3+ times in a row, mixed scripts), de-duplicate by normalized form keeping the most frequent spelling, apply a blocklist file (`data/blocklist.txt`, profanity/offensive words) so they are never suggested. Ship it as a compact binary file (e.g. sorted normalized keys + front-coding + varint frequencies), compressed, loaded fast (see performance targets; with 500k words aim for load < 500 ms and memory < 80 MB).
- **Clitic-aware prediction (important for Arabic):** many typed words start with attached prefixes (`و`, `ف`, `ب`, `ك`, `ل`, `ال`, `وال`, `بال`, `فال`, `كال`, `لل`, `س`). When the full typed prefix has few matches, also try stripping a known prefix, look up the stem, and re-attach the prefix to the suggestions (e.g. typing `والمد` can suggest `والمدرسة`). Rank these slightly below direct matches. Optional suffix awareness (`ها`, `هم`, `كم`, `ات`, `ين`, `ون`) later.
- Expose the dictionary size and source attribution in the About window.

**Ranking**: `score = log(baseFrequency + 1) + w_user * log(userCount + 1) * recencyDecay`. Exact-prefix matches rank above normalization-only matches. Never suggest the word the user has already fully typed.

**Learning & saving new words (local only) — the app must remember every new word the user types:**
- On every word commit, if the word is **not in the dictionary, save it immediately** to the user dictionary (with count = 1 and timestamp); if it already exists, increment its user count and last-used time.
- New words become suggestible after being typed **2 times by default** (setting: 1–5; 1 = suggest after first use). This protects against saving typos as suggestions while still learning fast. Words accepted from a suggestion count as used.
- User words are merged into lookups at runtime (same trie/index or a second small index), ranked with the formula above so frequently used personal words rise to the top.
- Store **only word → count/timestamp**, never sentences, never word order, never anything typed in excluded apps or password fields.
- Persistence must be **durable and scalable** (tens of thousands of words): propose using SQLite (`Microsoft.Data.Sqlite`, MIT) at `%LOCALAPPDATA%\Itmam\user.db` (MSIX redirects this correctly) and ask me to confirm; otherwise a JSON/binary file with debounced writes and atomic replace (write temp + move). Data must survive crashes, updates and reinstalls of new versions (not uninstall).
- SQLite here means the **embedded** library only: the NuGet package `Microsoft.Data.Sqlite` ships the native SQLite engine inside the app (via `SQLitePCLRaw.bundle_e_sqlite3`). Nothing is installed on the developer's or users' machines, no server, no service; the database is just one file. Verify the bundled native DLL is included in the MSIX for x64 and ARM64.
- Save within a few seconds of learning (debounced) and always on Exit/logoff.
- Settings → "My words" page: list learned words with counts and search; **add a word manually**, edit, delete, block a word (never suggest it again); pause learning; clear all learned data; **import/export** the personal dictionary as a UTF-8 `.txt` (one word per line, optional count) so users can back it up or move it to another PC.
- In the suggestion bar, a context action (e.g. `Ctrl+Delete` on a highlighted suggestion, or right-click) removes/blocks that suggestion.
- Tests: new word saved on first commit, becomes suggestible after the threshold, survives restart, import/export round-trip, blocked words never suggested.

Phase 2 (optional, after MVP): next-word prediction using bigrams learned only from the user's own commits, same privacy rules.

## 10. Tray icon & settings

Tray menu: Enable/Disable · Pause for 1 hour · Settings · About · Exit. Tooltip shows state.

**Exit** (tray menu → خروج / Exit) is the only way to close the app from the UI. It must shut down cleanly: unhook keyboard/mouse/WinEvent hooks, flush learned data to disk, dispose the `NotifyIcon` (so no ghost icon remains), release the single-instance mutex, then end the `ApplicationContext`. Also handle `SystemEvents.SessionEnding` / `WM_ENDSESSION` (logoff/shutdown) with the same cleanup. Exiting does not change the "start with Windows" setting; README explains the difference between Disable (stays in tray, does nothing) and Exit (fully closed until next login or manual launch), and how to stop auto-start (app Settings, or Windows Settings → Apps → Startup).

Settings window (Arabic UI by default, English available, RTL layout when Arabic):
- Enable on startup (MSIX `desktop:StartupTask`; in unpackaged debug builds use `HKCU\...\Run`)
- Number of suggestions (1–5), minimum letters (1–4), add space after accept
- Hotkeys (accept first, accept N, hide, global toggle) with conflict detection
- **Appearance page**: theme (System — follows Windows light/dark automatically [default] / Light / Dark), background color presets + custom color picker, highlight color (Windows accent [default] or custom), text color (auto contrast or custom), opacity, font size, show/hide hotkey hints, "native navigation" mode. Include a **live preview** of the suggestion bar showing sample Arabic words, and a "Reset to system look" button.
- The settings window itself also follows the System/Light/Dark choice.
- Excluded apps (pick from running processes or type name)
- Learning on/off, manage learned words, clear data
- Language of UI (ar / en) via `.resx` resources

Single instance via named mutex; a second launch opens Settings of the running instance.

## 11. Privacy & security (hard requirements; the Store review will check these)

- **No network access at all.** Do not declare `internetClient` in the manifest. No telemetry, no update checks, no crash upload.
- Never log typed text. Debug logs (Debug builds only) may contain event types and timings, never characters.
- `docs/PRIVACY.md` (Arabic + English) explaining exactly what is stored, where, and how to delete it.
- Code must be easy to audit: the hook and storage code paths documented in `docs/ARCHITECTURE.md`.
- Handle exceptions in hook/worker threads without crashing the whole app; if the hook is removed by Windows, detect and reinstall.

## 12. MSIX packaging

- `Package.appxmanifest`: placeholder Identity/Publisher (I will fill from Partner Center), display names in `ar` and `en`, `runFullTrust` only, `desktop:StartupTask`, all required Store logo sizes (generate a simple original placeholder icon, no third-party logos).
- Document in README how to build the MSIX in Visual Studio and how to test-install it locally with a self-signed certificate.
- Known limitation to document: cannot assist inside apps running as administrator.

## 13. Tests (must pass before each phase is "done")

- Normalization (all alef/ya/ta-marbuta/tashkeel cases)
- Trie/lookup correctness and ordering, performance test on full dictionary
- Ranking with user counts and decay
- Word-buffer state machine: scripted key sequences → expected buffer/commit events (backspace, punctuation, layout switch, Ctrl shortcuts, focus change)
- Minimal-edit computation for insertion
- Learning persistence round-trip and corruption recovery (corrupt store → keep a backup copy, start fresh, never crash)
- Clitic-aware lookup (prefix stripping + re-attachment)

## 14. Phases (stop for review after each)

0. **First, create `CLAUDE.md`** (see section 17). Then connect to the GitHub repo (section 16), scaffold solution, projects, CsWin32, test project, `.gitignore`/`.gitattributes`, CI workflow, CI-friendly `dotnet build` / `dotnet test`. Confirm the word-list source and license with me.
1. Core: normalization, dictionary builder, trie, ranking, word-buffer state machine + full tests.
2. Win32: keyboard hook thread, layout detection, focus/foreground tracking, a temporary debug window showing the live buffer (no popup yet).
3. Caret locator + suggestion popup (no insertion yet). Test in Notepad, Word, Edge/Chrome, VS Code, Teams/WhatsApp desktop.
4. Hotkey interception + text insertion.
5. Learning + persistence + password/elevated/excluded-app guards.
6. Tray, settings window, localization (ar/en), startup task, single instance.
7. MSIX packaging, Store assets, README (ar/en), PRIVACY, STORE_LISTING draft (ar/en), THIRD_PARTY_NOTICES.

## 15. Acceptance criteria

- Typing Arabic in Notepad, Word, Edge, Chrome and VS Code shows suggestions above the caret within ~50 ms of each keystroke.
- No noticeable typing lag; CPU idle < 1% when not typing; total memory < 120 MB.
- The shipped dictionary has 300k+ real words; a new word typed twice (default) is suggested next time and is still there after restarting the app or the PC.
- Accepting a suggestion inserts the correct word with correct hamza/ta-marbuta spelling, in every app above.
- Nothing appears or is learned in password fields, excluded apps, or with an English layout.
- English typing is completely unaffected.
- The app never appears in the taskbar or Alt+Tab; it is reachable only from its tray icon.
- With the default "System" theme, the suggestion bar is visually close to the native Windows 11 English suggestion bar and switches light/dark instantly when Windows does; every color preset keeps text readable.
- App survives sleep/resume, layout switching, monitor changes, and Explorer restarts (tray icon re-added on `TaskbarCreated`).
- All tests pass; Release build has zero warnings.

## 16. Source control & GitHub

- The project lives in the GitHub repository: **https://github.com/nasmn77/Itmam**
- In Phase 0: if the folder is not already a clone of that repo, run `git init`, set the default branch to `main`, and add the remote: `git remote add origin https://github.com/nasmn77/Itmam.git`. If the remote already has commits (e.g. a README or LICENSE created on GitHub), `git pull origin main --allow-unrelated-histories` first and resolve conflicts instead of overwriting them.
- Add a proper `.gitignore` for .NET / Visual Studio (`bin/`, `obj/`, `.vs/`, `*.user`, `AppPackages/`, `BundleArtifacts/`, `TestResults/`). **Never commit** signing certificates (`*.pfx`, `*.cer` private keys), Partner Center secrets, or local user data (`user.json`).
- Add `.gitattributes` (`* text=auto`, and make sure `.md`, `.cs`, `.resx`, `.txt` dictionary files are UTF-8 so Arabic text is never corrupted).
- Commit after each completed step with clear messages (Conventional Commits style, e.g. `feat(core): add Arabic normalizer`, `test(core): trie lookup cases`, `fix(win32): reinstall hook after timeout`). Small, focused commits; never commit a broken build.
- Work on a branch per phase (`phase-1-core`, `phase-2-hook`, …) and merge into `main` after I approve the phase.
- **Ask me before every `git push`** and before creating releases, tags, or anything public on GitHub. Never force-push to `main`.
- Add a GitHub Actions workflow `.github/workflows/build.yml` running on `windows-latest`: setup .NET 10, `dotnet restore`, `dotnet build -c Release`, `dotnet test`. (MSIX packaging can stay local/Visual Studio.)
- README (ar + en) links to the repository, explains how to build from source, and includes the license and a short contribution guide (especially for improving the dictionary and blocklist). Suggested license: MIT (confirm with me), with third-party data licenses listed separately in `THIRD_PARTY_NOTICES.md`.
- The privacy policy (`docs/PRIVACY.md`) must be reachable via a public URL for the Store listing; use the GitHub file URL in this repo (or GitHub Pages if I ask for it).

## 17. Working rules for you (Claude)

- **CLAUDE.md (required, first step of Phase 0):**
  > أنشئ ملف CLAUDE.md في جذر المشروع يلخّص القواعد الأساسية من PROMPT.md (التقنيات، الخصوصية، قواعد Git، طريقة العمل بالمراحل) حتى تلتزم بها في كل جلسة جديدة.

  i.e. create `CLAUDE.md` at the repository root summarizing the essential rules from this PROMPT.md: tech stack and versions, privacy rules, Git/GitHub rules, the phase-by-phase workflow, plus the current phase and how to build/test. Keep it concise (rules, not the full brief), reference `PROMPT.md` for details, commit both files, and **update CLAUDE.md at the end of every phase** (current phase, decisions made, open questions) so every new session continues correctly.

- Plan before coding; keep changes small and reviewable; explain any Win32 trick you use.
- Run `dotnet build` and `dotnet test` after every meaningful change and fix failures before moving on.
- Never fabricate APIs, package names or licenses; if unsure, say so and propose how to verify.
- Ask me before adding any dependency, before choosing the word-list source, and before anything that touches the network.
- Write code comments and docs in English; all user-facing strings in `.resx` with Arabic and English.
