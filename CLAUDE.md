# CLAUDE.md — Itmam (إتمام)

Arabic as-you-type word prediction for the physical keyboard on Windows, shipped on the Microsoft Store as MSIX.
`PROMPT.md` is the full brief and the source of truth; this file is the short rule set. Read both at the start of a session.

## Current status
- **Phase 6** merged into main (tray + every settings page). Details kept below for reference: 6b on branch `phase-6-tray` (6a done: tray, menu, exit, single instance, settings.json, start with
  Windows, General/About/Exit pages, live ar/en switching). 6b done so far: **Appearance** page (theme, bar/text/selection
  colors with presets + custom picker, automatic WCAG text color, contrast warning, opacity 80–100 %, text size 12–20 with
  the bar height kept at 35:14, first-item highlight, number hints, reset, live preview drawn by the same `BarRenderer` as
  the bar); **Shortcuts** page (record by pressing keys; `HotkeyRules` refuses keys that type text, editing keys, common
  Windows shortcuts, duplicates, modifier+digit clashes, and a global toggle without Ctrl/Alt; Itmam's own shortcuts are
  suspended while recording; accept-N modifier Ctrl or Alt; arrow navigation toggle; reset); **Excluded apps** page (add from open apps or browse for an .exe, remove, default list, search, open apps first, confirmation before add/remove/reset); **My words** page (pause learning, add/block
  a word, import/export, sort, search, virtual word list with edit/block/delete, delete all with confirmation; index guarded by
  `LearningService.SyncRoot`, also held by `SuggestionController`). 6b complete: awaiting the user's review. Phases 0–5 merged. User decisions: phase 6 split in two; Windows 11 style settings (side navigation);
  diagnostics window in Debug builds only (tray → Diagnostics); "Itmam is running" notice on EVERY start, including start with
  Windows (user decision, overrides the brief's one-time notice); app icon = concept A (suggestion bar + «ت», tools/IconGenerator). Debug: `--demo-settings [general|appearance|shortcuts|excluded|words|phrases|textshortcuts|updates|about|exit] [en] [end]`, `--demo-popup x y [navy] [translucent] [large] [hints]`.
- Phase 5 decisions: learn words of ≥ 2 letters; silent full screen only for D3D exclusive + presentation mode (not F11
  browser/video); a finished word stays *pending* and is dropped if Backspace/Ctrl+Backspace follows it at once (user question);
  SQLite in Core (`SqliteUserWordStore`, WAL, corrupt file kept as `user.db.corrupt-*`); `ForegroundGuard` silences password
  fields (ES_PASSWORD + UIA IsPassword), elevated, excluded and full-screen apps; right-click or Ctrl+Delete (navigating)
  FORGETS a learned word (user decision: removed, re-learned if used again); permanent blocking only in "My words". Startup does one compacting GC after loading: working set ≈ 90 MB, private ≈ 38 MB.
- Phase 4 (approved after the user's tests in Notepad; other apps not tested yet): `HotkeyInterceptor` (Core) decides in the hook
  from `PopupState`; Tab/Ctrl+N/Esc only while the bar is visible, the toggle always (default Ctrl+Alt pressed and released alone, fired on key-up, nothing swallowed — user decision; Ctrl+Alt+P failed with the Arabic layout); `SendInputInjector` releases a held Ctrl,
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
- **Store submission 1 (v1.0.0.0) sent for certification on 2026-10-28, PUBLISHED by 2026-10-30** (the user confirmed). Partner Center:
  pricing free (0 USD, all markets), category Productivity, OneDrive backup and clip recording declarations turned off
  (privacy), age rating questionnaire all "No", listings ar + en (texts from STORE_LISTING.md, screenshots docs/store,
  logos docs/store/logos), certification notes in Additional Testing Information. Super hero art is ready in docs/store/logos
  (add it in the next submission).
- **1.1.0 (merged into main 2026-10-31, branch `release-1.1` deleted; site and privacy policy live; WACK same as 1.0; Store submission 2 (1.1.0.0) sent for certification on 2026-10-31: new package (1.0.0.0 removed), ar/en description, What's new, 20 features, screenshots 3–6, Super hero art 3840x2160, certification notes in Additional Testing Information):** user decisions 2026-10-29: update check through Microsoft Store (`StoreUpdates`, notification opens the Store page, Settings → Updates), on/off notifications «تم إيقاف إتمام» / «تم إيقاف إتمام مؤقتًا» / «تم تشغيل إتمام» (setting, default on), confirmation before exiting (not on sign-out); **common phrases** (user decision: whole phrase first, Tab inserts it, then word
  by word; 249 phrases in `data/phrases.txt`, MIT, compiled by hand; "My phrases" in settings.json; `RecentWords` = last 8
  words, memory only — privacy policy updated; no phrase starts from a short common word, one whole phrase at a start);
  **learned phrases** (user decisions: automatic, ON by default; counts kept on disk with expiry): 2–5-word sequences typed
  in a row are counted in user.db table `phrases` (schema 2), learned at 2 uses (user decision, was 3), not-learned ones deleted after 30 days,
  limits 20,000 / 2,000; order My phrases → learned → shipped; right-click forgets; Settings → «عباراتي» ("My phrases" page, renamed from «العبارات») lists them (all of them; only the bar leaves out phrases inside a longer one); **text shortcuts**
  (user decisions: shown first in the bar, Tab inserts — never replaced automatically; own page «الاختصارات النصية»;
  `TextShortcuts` in Core, kept in settings.json; digits after a letter stay in the word); tray icon: left or right
  click opens the menu (no double-click); «تعلّم الكلمات الجديدة» toggle (inverse of LearningPaused). Version shown is
  read from the package at runtime. Then (user decisions): Backspace right after an accept undoes it (`UndoAccept`,
  decided in the hook from `PopupState.CanUndoAccept`); import/export of My phrases and text shortcuts (text files, import
  only adds, an existing shortcut key keeps its text); right-click on the bar shows an `ActionStrip` over the clicked item (non-activating; for a phrase — the WHOLE phrase via `SuggestionController.PhraseSources` — "Delete" forgets it if learned, removes it from My phrases and hides it if built in (`HiddenPhrases` in settings.json, restorable on the Phrases page), plus "Add to My phrases"; a single word: "Delete" if learned and "Block" without confirmation — user decision, overrides "blocking only in My words"; a real ContextMenuStrip took the focus and stayed open — never use one there);
  "Turn off Itmam" in the tray menu and on the Exit page; About links to the website privacy page. Words typed while waiting for a context read are recorded and recovered (`WordBuffer` blind typing, `RecoveredWords`/`ResumeFromBlind`), so the first word after a focus/window change is not lost; pasted text never joins the phrase chain. **Long phrases** (user decisions: own page «العبارات الطويلة», name + multi-line text ≤ 4000, a shortcut opens a list of names to choose from — default Ctrl+Shift+Space, configurable; inserted by PASTING via `ClipboardPaste` (user decision: typing froze Word; clipboard saved/restored, excluded from history/cloud), typed with Shift+Enter line breaks only if the clipboard is busy; `LongPhrasePicker` takes the focus and gives it back; import/export with \n escapes). Pages renamed (user decisions): «الاختصارات» → «اختصارات المفاتيح», «العبارات» → «عباراتي» (its user list: «العبارات التي أضفتها»). The first suggestion is highlighted in the selection color, like the ↑ selection (user decision, replaces the subtle highlight). Then (user decisions, all in 1.1): tray "Turn off Itmam in <app>" (`ItmamApp.LastApp`, confirmation); statistics (completions + characters saved, `UsageDelta` from the pipeline, `stats` table in user.db schema 3, General page, reset). Full backup (`FullBackup`, one .itmam ZIP: settings + words + learned phrases + statistics; restore REPLACES everything after a confirmation, keeps this PC's start-with-Windows and on/off; General page). Common mistakes: `data/corrections.txt` (56 entries, MIT, by hand; exact typed word → corrections first in the bar; hamza/ة/ى endings already handled by the dictionary ranking, covered by a test). Also the two 1.0.1 fixes, verified by the user:
  (1) `CaretSource.FocusedField` fallback before the mouse pointer when a web control reports no caret (the bar had
  appeared at the top of Partner Center in Edge); (2) the caret is read again 60 ms after the bar is shown when no
  newer key arrived (the hook reads it before the app inserts the letter; an empty dir=auto field such as Google
  search flips to RTL on the first Arabic letter). Local test installs of the same version need Remove-AppxPackage first.
- **1.1 PUBLISHED (2026-10-31; the user now runs the Store install).** **1.1.1 (version 1.1.1.0) PUBLISHED 2026-10-31** (Submission 3: package + What's new ar/en only; the user's Store install updated to 1.1.1.0). `tools/Build-Package.ps1` now cleans the package project and checks every inner package version (an incremental build had put 1.1.0.0 packages in the 1.1.1.0 bundle; the Store refused it). 1.1.1 user decisions 2026-10-31: website link on the About page; **user guide** (PDF, ar + en, Arial): `docs/guide/guide.{ar,en}.html` + `guide.css` → `tools/Build-Guide.ps1` (headless Edge `--print-to-pdf`, temporary profile) → `docs/guide/itmam-guide-{ar,en}.pdf`, shipped in the package (`guide\`), opened by `UserGuide` from Settings → General (card at the top) and the tray menu ("User guide"); copied to `site/assets/guide/` with links in the site hero/footer and the READMEs. Screenshots in `docs/guide/img` from the Debug build with sample data (`ITMAM_DEMO_DATA`, captured with PrintWindow — Arabic windows come out mirrored and are flipped back; never CopyFromScreen, it caught the Start menu once).
- **Phase 7** merged into main. `src/Itmam.Package` (.wapproj, x64 + ARM64 bundle),
  identity from Partner Center product **9P08F7KGRJB0** (`NasserAlmadi.563063310BE3D`, `CN=9260B530-9E02-440E-8E99-B74323B1CE93`,
  PublisherDisplayName "Nasser Almadi"; reserved names «إتمام» + "Itmam"; a separate draft product «اتمام» without hamza
  also exists — unused, «اتمام» is a Store search term instead). Self-contained + ReadyToRun (~67 MB per architecture).
  `tools/Build-Package.ps1` (Store upload / signed test bundle), `tools/New-DevCertificate.ps1` (non-exportable cert in
  CurrentUser\My, no pfx; public part trusted in LocalMachine\TrustedPeople). Verified by the user: installs and runs.
  WACK 1.0.0.0 and 1.1.0.0 (2026-10-31, same result): "Blocked executables" (strings in .NET files/dictionary, Process.Start for About links) and
  DPI-awareness warning (set by WinForms at runtime) explained in STORE_LISTING.md certification notes.
  User decisions: category Productivity, free, full self-contained size (no trimming), version 1.0.0.0.
- Website (user decision: design "paper and ink", GitHub Pages): `site/` (ar default + `site/en/`, home + privacy policy),
  published by `.github/workflows/pages.yml` to https://nasmn77.github.io/Itmam/. No web fonts, no trackers, no external
  requests (Windows Arabic fonts with fallbacks). The Store privacy URL is https://nasmn77.github.io/Itmam/privacy.html;
  keep `site/privacy.html`, `site/en/privacy.html` and `docs/PRIVACY.md` in sync.
- MSIX tooling: VS 2026 → .NET desktop development → optional "MSIX Packaging Tools"
  (`Microsoft.VisualStudio.ComponentGroup.MSIX.Packaging`, not listed under Individual components) + Windows 11 SDK 10.0.26100.

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
  - **Wikipedia dictionary (user decisions 2026-10-31, 1.1):** both dictionaries are SHIPPED (no download button, no
    internetClient): `data/itmam-ar.dict` (basic) and `data/itmam-ar-wikipedia.dict` (DEFAULT, user decision 2026-10-31) (FrequencyWords + Arabic
    Wikipedia dump 2026-10-01, `wiki-counts.py`, `--min-count wikipedia=5`, `--weight frequencywords=2` (everyday words keep their rank), 881,541 spellings, 2.98 MB, ~36 MB in memory,
    load ~350 ms); setting `UseWikipediaDictionary`, Settings → General, switched live.
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
- README: `README.md` is Arabic (the default shown on GitHub, user decision), `README.en.md` English; both cross-linked.
- Terminals are excluded by default (user can re-enable in settings).
- Lookup structure: sorted normalized-key array + front coding + varint frequencies (compressed via BCL), not an object trie, to meet the memory target.
- The `.wapproj` is created in Phase 7: the MSIX tooling (DesktopBridge targets) is not installed in Visual Studio yet.

## Privacy rules (hard requirements)
- No network access at runtime: no `internetClient` capability, no telemetry, no crash upload. **Exception (user decision,
  2026-10-29, from 1.1):** `StoreUpdates` asks Microsoft Store through `StoreContext` whether a newer package exists
  (12 s after start-up, setting "CheckForUpdates", default on); the Store service does the network work, nothing else is
  sent. Documented in the privacy policy (docs/PRIVACY.md, site/) — keep it in sync.
- Never log typed characters. Debug logs (Debug builds only) contain event types and timings only.
- Learn only word → count/last-used. Never sentences. Never learn in password fields or excluded apps.
  **Exception (user decision, 2026-10-29, from 1.1):** 2–5-word sequences → count/last-used (learned phrases, setting
  "LearnPhrases", default on; not learned after 30 days → deleted). The last 8 words (RecentWords) stay in memory only.
- Hook and storage code paths must be documented in `docs/ARCHITECTURE.md`.

## Engineering rules
- Low-level hook callback: filter, translate, enqueue to a `Channel<T>`, decide swallow/pass — nothing else, no allocations, < 1 ms.
- Ignore `LLKHF_INJECTED` events. `ToUnicodeEx` with flag `0x4`.
- Hook/worker exceptions must never crash the app; reinstall the hook if Windows removes it.
- Code comments in English; all user-facing strings in `.resx` (Arabic + English). Public docs are bilingual, Arabic first (user decision): `README.md` (Arabic) + `README.en.md`; `docs/PRIVACY.md`, `docs/ARCHITECTURE.md`, `THIRD_PARTY_NOTICES.md`, `data/README.md`, `src/Itmam.Package/README.md` and `STORE_LISTING.md` hold an Arabic RTL section then English. Keep both languages in sync. Arabic UI name **إتمام**, English **Itmam**.
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
