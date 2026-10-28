# CLAUDE.md — Itmam (إتمام)

Arabic as-you-type word prediction for the physical keyboard on Windows, shipped on the Microsoft Store as MSIX.
`PROMPT.md` is the full brief and the source of truth; this file is the short rule set. Read both at the start of a session.

## Current status
- **Current phase:** 6b on branch `phase-6-tray` (6a done: tray, menu, exit, single instance, settings.json, start with
  Windows, General/About/Exit pages, live ar/en switching). 6b done so far: **Appearance** page (theme, bar/text/selection
  colors with presets + custom picker, automatic WCAG text color, contrast warning, opacity 80–100 %, text size 12–20 with
  the bar height kept at 35:14, first-item highlight, number hints, reset, live preview drawn by the same `BarRenderer` as
  the bar). Next: Shortcuts, Excluded apps, My words pages. Phases 0–5 merged. User decisions: phase 6 split in two; Windows 11 style settings (side navigation);
  diagnostics window in Debug builds only (tray → Diagnostics); "Itmam is running" notice on EVERY start, including start with
  Windows (user decision, overrides the brief's one-time notice); app icon = concept A (suggestion bar + «ت», tools/IconGenerator). Debug: `--demo-settings [general|appearance|about|exit] [en] [end]`, `--demo-popup x y [navy] [translucent] [large] [hints]`.
- Phase 5 decisions: learn words of ≥ 2 letters; silent full screen only for D3D exclusive + presentation mode (not F11
  browser/video); a finished word stays *pending* and is dropped if Backspace/Ctrl+Backspace follows it at once (user question);
  SQLite in Core (`SqliteUserWordStore`, WAL, corrupt file kept as `user.db.corrupt-*`); `ForegroundGuard` silences password
  fields (ES_PASSWORD + UIA IsPassword), elevated, excluded and full-screen apps; right-click or Ctrl+Delete (navigating)
  FORGETS a learned word (user decision: removed, re-learned if used again); permanent blocking only in "My words". Startup does one compacting GC after loading: working set ≈ 90 MB, private ≈ 38 MB.
- Phase 4 (approved after the user's tests in Notepad; other apps not tested yet): `HotkeyInterceptor` (Core) decides in the hook
  from `PopupState`; Tab/Ctrl+N/Esc only while the bar is visible, Ctrl+Alt+P always; `SendInputInjector` releases a held Ctrl,
  sends backspaces + Delete + unicode in one batch. Native navigation (Up/Left/Right/Enter/Esc) ON by default (user decision).
  No suggestions while text is selected (Shift+arrows, Ctrl+A, mouse drag). Accepting mid-line never doubles a space: the char
  after the caret is read with the context (space → Delete + word + space; punctuation → no space).
- Phase 3 decisions: bar look measured from the user's screenshot (no dividers, 35 px, 14 px Segoe UI, `#2C2C2C`/white dark,
  grayscale text AA, hints off, subtle first-item highlight with a setting to turn it off). Placement mirrors the native bar:
  right edge at the caret for Arabic. Debug builds: `Itmam.exe --demo-popup <x> <y> [light|dark] [nohighlight] [hints]`.
- Caret moves (click, arrows, focus/foreground/layout change, paste, shortcut, Ctrl+Backspace) also detach and read the
  context (user decision after "مد" + click + "ر" started a new word). Without UIA: empty word assumed after a caret move only
  if no letter was typed meanwhile. Esc resets without detaching.
- Backspace on an empty word (user decision): buffer becomes *detached*; `InputPipeline` reads ≤ 49 chars before the caret via
  UI Automation (`UiaCaretContextSource`, never in password fields, nothing stored) and resumes with the word the caret is in,
  or empty. Without UIA: resume at boundary, click, navigation, shortcut (Ctrl+A) or focus change. Documented in PRIVACY.md.
- Hook latency criterion (user decision): **p99 < 1 ms and max < 5 ms** (not a hard 1 ms max). Warm-up before install,
  AboveNormal hook thread, cached foreground input thread. Measured by the user: max ≈ 1.07 ms, no GC involvement.
- Unavailable caret context is retried once after 100 ms (Chromium/Electron wake up their accessibility tree); verified by the
  user in Notepad, Word and Chrome.
- Phase 2 notes: input path documented in `docs/ARCHITECTURE.md`; `Program.cs` temporarily runs `DebugWindow` (replaced in
  Phase 6). Key classification (`KeyClassifier`) and layout gating (`InputTracker`) live in Core and are unit-tested.
- **Open questions:** MSIX tooling must be installed in Visual Studio before Phase 7.

## Build & test
```
dotnet build Itmam.CI.slnf -c Release   # must be zero warnings (warnings are errors in Release)
dotnet test --solution Itmam.CI.slnf -c Release
```
Tests run on Microsoft.Testing.Platform (opted in via `global.json` → `test.runner`), not VSTest.
`Itmam.CI.slnf` excludes the MSIX packaging project (`.wapproj` builds only in Visual Studio / MSBuild with the MSIX tooling).

## Tech stack (fixed)
- .NET 10 SDK pinned by `global.json` (`10.0.100`, `latestFeature`). Microsoft packages on 10.x.
- Central Package Management (`Directory.Packages.props`), shared settings in `Directory.Build.props`.
- C# latest, nullable on, implicit usings on, warnings as errors in Release.
- `Itmam.Core` targets `net10.0` (no Windows APIs, 100% unit-testable). Everything else targets `net10.0-windows10.0.19041.0`, min OS 10.0.19041.0.
- WinForms, PerMonitorV2 DPI. P/Invoke via **CsWin32** (`NativeMethods.txt`), not hand-written `DllImport`.
- Tests: xUnit v3 + **Shouldly** (not FluentAssertions: v8+ is commercially licensed).
- Packaging: `.wapproj` → MSIX, `runFullTrust` only. Platforms: **x64 and ARM64 only**.

## Approved dependencies (ask before adding any other)
| Package | Use | License |
|---|---|---|
| Microsoft.Windows.CsWin32 | P/Invoke source generator (build-time) | MIT |
| Microsoft.Data.Sqlite 10.x (bundles SQLitePCLRaw/e_sqlite3) | user word store (Phase 5) | MIT / SQLite public domain |
| xunit.v3 | tests (Microsoft.Testing.Platform runner) | Apache-2.0 |
| Shouldly | test assertions | BSD-3-Clause |

## Decisions made
- User words: embedded SQLite at `%LOCALAPPDATA%\Itmam\user.db`.
- Dictionary sources: **FrequencyWords `ar_full` first (Phase 1)**; Arabic Wikipedia counts added later once everything works. CC-BY-SA on the derived dictionary file is accepted. Verify each license and tell the user **before** downloading.
  - FrequencyWords (github.com/hermitdave/FrequencyWords, `content/2018/ar/ar_full.txt`, ~42 MB): README states
    "MIT License for code, CC-by-sa-4.0 for content"; data derived from OpenSubtitles 2018 (OPUS). Approved; pinned commit +
    SHA-256 in `tools/DictionaryBuilder/download-sources.ps1`. Built with `--min-count 3 --drop-bare-alef-variants`
    → 522,510 spellings, 1.66 MB file, ~20 MB in memory, load ~200 ms.
- Lookup speed: `PrefixCandidateCache` precomputes the top 64 entries per 1–3 letter prefix with ≥ 512 entries (by key and by
  literal spelling); a test proves results equal a full scan for every cached prefix. Lookups ≈ 0.01–0.03 ms.
- Blocklist (`data/blocklist.txt`, 21 words hand-picked by the user from LDNOOBW `ar` CC-BY-4.0 + uxbertlabs MIT;
  AhmedRedaAmin rejected — Latin script). Ordinary/medical/legal/news words (أمك، أختك، قضيب، اغتصاب، لواط، سحاق …) are
  deliberately NOT blocked. Matching: normalized + hamza seats folded; clitic forms for 4+ letters (any clitic), 3 letters
  (article clitics only), 2 letters never.
- **Phase 5 rule (user decision):** ship `blocklist.txt` with the app and check it at runtime: never auto-learn a blocked
  word from typing; but if the user adds a blocked word manually in "My words", accept it and respect that choice.
  - `data/itmam-ar.dict` is CC-BY-SA-4.0 (derived data); code stays MIT. Attribution in `THIRD_PARTY_NOTICES.md`.
- Ranking: literal-prefix matches get a score bonus (`LiteralMatchBonus`, default ln 10) instead of an absolute tier, because
  users often type ا for أ/إ; hamza-fallback and clitic matches are strict lower tiers. Setting the bonus to +∞ restores the strict rule.
- `CompleteWordBonus` (default ln 10): spellings whose normalized form equals the typed text get a bonus, so typing ان
  suggests أن | إن | أنا.
- **Several spellings per normalized key** (user decision: never merge distinct words like أن/إن, على/علي, إلى/إلي).
  Each spelling keeps its own frequency; per key: max 4 spellings, drop spellings < 5% of the top spelling, and drop a
  spelling that is exactly a more frequent spelling minus the hamza on its alef (ان/انا/الى). Tests assert أن، إن، على، علي، إلى
  are in the shipped dictionary.
- Committed words are stored without tashkeel/tatweel.
- Code license: MIT.
- Terminals are excluded by default (user can re-enable in settings).
- Lookup structure: sorted normalized-key array + front coding + varint frequencies (compressed via BCL), not an object trie, to meet the memory target.
- The `.wapproj` is created in Phase 7: the MSIX tooling (DesktopBridge targets) is not installed in Visual Studio yet.

## Privacy rules (hard requirements)
- No network access at runtime: no `internetClient` capability, no telemetry, no update checks, no crash upload.
- Never log typed characters. Debug logs (Debug builds only) contain event types and timings only.
- Learn only word → count/last-used. Never sentences or word order. Never learn in password fields or excluded apps.
- Hook and storage code paths must be documented in `docs/ARCHITECTURE.md`.

## Engineering rules
- Low-level hook callback: filter, translate, enqueue to a `Channel<T>`, decide swallow/pass — nothing else, no allocations, < 1 ms.
- Ignore `LLKHF_INJECTED` events. `ToUnicodeEx` with flag `0x4`.
- Hook/worker exceptions must never crash the app; reinstall the hook if Windows removes it.
- Comments and docs in English; all user-facing strings in `.resx` (Arabic + English). Arabic UI name **إتمام**, English **Itmam**.
- Never fabricate APIs, package names or licenses; say when unsure and propose how to verify.
- Ask before: adding a dependency, choosing/downloading a word-list source, anything touching the network.

## Git rules
- Remote: https://github.com/nasmn77/Itmam (`origin`), default branch `main`.
- One branch per phase (`phase-1-core`, `phase-2-hook`, …), merged into `main` only after the user approves;
  **delete the branch (local and on GitHub) right after merging.**
- Conventional Commits (`feat(core): …`, `test(core): …`, `fix(win32): …`), small and focused; never commit a broken build.
- **Ask before every `git push`**, tag, release or anything public. Never force-push `main`.
- Never commit certificates (`*.pfx`, private keys), Partner Center secrets, or user data (`user.db`, `user.json`).

## Phase workflow
Phases 0–7 are listed in `PROMPT.md` §14. After each phase: build, run tests, summarize the changes, update the "Current status" and "Decisions made" sections here, then **stop for review**.
