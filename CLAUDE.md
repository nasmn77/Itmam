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
- **1.1.2 (version 1.1.2.0) PUBLISHED 2026-11-01; the user's Store install updated (Submission 4: package + Description privacy paragraph + What's new, ar and en):** one privacy wording everywhere (user decision after a review): "sends no data; its only connection is the Store update check, can be turned off" and "does not save your text: only the words and short phrases you repeat, with their counts" (site, privacy policy, README, guide, About page, STORE_LISTING description); GitHub issue form for bug reports only (word suggestion form removed by the user; label `dictionary` unused), links on the site/guide. Partner Center: package 1.1.2.0 + Description privacy paragraph + What's new, ar and en.
- **1.2 (version 1.2.0.0; merged into main 2026-11-01, branch deleted; PUBLISHED 2026-11-02, the user's Store install updated — Submission 5: package + What's new + long phrases feature line, ar and en): long phrase variables**, plus two first-word fixes (a letter that brings a layout switch starts a read; up to 3 more reads while typing when Chromium/Electron wakes up late, `ReadsWhileTyping`) (user decisions): `{name}` asked for (free name ≤ 30 chars, typed by hand or via the button), `{{name}}` automatic, only four: التاريخ / التاريخ الهجري (Umm al-Qura) / اليوم / الوقت (English aliases date / hijri date / day / time; no clipboard, no caret); automatic ones are inserted from the button (a list, never typed) and an unknown `{{name}}` is refused on save. "Insert variable…" button in the long phrase editor → `VariableDialog` (two radio buttons: a text box or a drop-down list). The long phrases list shows a field per asked variable in place of the list with a live preview (Tab, Enter in the last inserts, Esc back); phrases without variables unchanged. Automatic values follow the phrase text's language (Arabic letters → «1 نوفمبر 2026», الأحد, «10:30 ص»; else English); Gregorian months يناير/فبراير…; values never saved. Long phrases only (not text shortcuts). Code: `LongPhraseVariables` (Core, tested), `VariableDialog`, `LongPhraseDialog`, `LongPhrasePicker`; Debug `--demo-longphrases [en] [fill]`, `--demo-longphrase-dialog [en] [variable] [auto]`.
- **1.3 (version 1.3.0.0; merged into main 2026-11-02, branch deleted; Submission 6 (package + What's new ar/en) PUBLISHED 2026-11-02, the user's Store install updated):** (user decisions 2026-11-02) the first suggestion is a light
  shade of the selection color (alpha 0x2C) and the arrow-key selection the full one (0x78), so ↑ shows a change; Esc hides the
  bar until the next LETTER (was: next word); long phrases list 640 wide, centred on the screen, Insert/Cancel buttons, the
  phrase text is editable (Enter = new line, Ctrl+Enter inserts) and an edit asks on insert «حفظ التعديل؟» (Yes saves to the
  phrase via `TrayApplication.SaveLongPhraseText`, No = this time only, Cancel = back; changing selection/closing drops it
  silently); in the fill window the reply is editable for this time only — an edit locks the fields, and variables still
  written as `{name}` get the empty-fields question (No selects the first one, Yes removes them). Debug:
  `--demo-longphrases [edit [save]]`. Guide (new picker/fill screenshots, `figure.tall`), site, ARCHITECTURE and STORE_LISTING "What's new in 1.3" updated; version 1.3.0.0, Store package built.
- **2.0 multi-language (MERGED into main and pushed 2026-11-03, branch `release-2.0` deleted — it was never on GitHub; GitHub Pages deployed, CI build green; Submission 7 (package 2.0.0.0 + ar/en Description, What's new, two feature lines, certification notes in Additional Testing Information) SENT FOR CERTIFICATION 2026-11-03, PUBLISHED 2026-11-04 (the user confirmed); plan approved 2026-11-02; user decisions: one big 2.0 release, English the only new fully supported language; any other alphabetic layout = learning only; IME languages (zh/ja/ko) silenced).** M0 done: golden snapshots of every Arabic path in `tests/Itmam.Core.Tests/Golden` (suggestions of both dictionaries, normalization/cleaning, blocklist hash, phrases, corrections, `WordBuffer`); they must stay unchanged through 2.0 (`ITMAM_UPDATE_GOLDEN=1` rewrites them, only for an intended change). M1 done: `Itmam.Core/Languages` — `LanguageProfile` (letters, ignorable marks, 1:1 `Fold`, clitics, excluded stem start, fallback variants, common phrase starts, blocklist loose key/stems), `ArabicLanguage` (delegates to `ArabicText`/`ArabicNormalizer`, unchanged), `LearningOnlyLanguage` (char.IsLetter, non-spacing marks ignored, lower-case fold). `WordBuffer`, `RecentWords`, `UserWordIndex`, `WordDictionary`, `SuggestionEngine`, `PhraseIndex`, `Corrections`, `Blocklist`, `WordFilter` take a profile (default Arabic); a test forbids `ArabicText.`/`ArabicNormalizer.` elsewhere in Core (allowed: LongPhrases search, LongPhraseVariables date language). M2 done: `LanguageRegistry` (HKL/LANGID → profile; zh/ja/ko/am/ti/vi, th/lo/km/my/bo and Indic = Unsupported, silent; tag via CultureInfo; RTL via TextInfo; one profile per tag; `AnyScript` for My phrases/shortcut keys of no particular language); `InputTracker.Language` + `EnabledLanguages` (setting `EnabledLanguages`, default ar+en); `PipelineUpdate.Language` + `LearnLanguage` (pending word keeps its language across a layout switch); per-language `LanguageSuggestions` in `SuggestionController` (built on first use by `ItmamApp.SuggestionsFor`), bar direction from the language; `LearningService` per-language states (optional language parameter, primary = Arabic; Export/Import `# language: tag` sections); user.db schema 4 (`(lang, word)`/`(lang, key)`, 1.x file → `user.db.v3-backup` via VACUUM INTO, then migrated to ar in one transaction); FullBackup format 2 (`learned-phrases-<tag>.txt`); text shortcut keys and My phrases accept any script (case-insensitive). IME (user decision 2026-11-02, replaces the planned IMM query: it goes through the other app and could hang, and TSF IMEs answer it badly): no IMM query; VK_PROCESSKEY → `InputEventKind.InputMethod` → `SilenceReason.InputMethod` until the context changes. Dead keys → `InputEventKind.DeadKey` → detach + context read (no composing). Settings pages still show the primary (Arabic) language only (M4). M3 done (user approved the sources and downloads 2026-11-02): `EnglishLanguage` (Latin letters < U+0250, apostrophe = inner mark `IsInnerMark` — inside a word only, `IsWordPrefix` lets a typed prefix end with it; fold = lower case + no accents, 1:1 table; `MatchCase`: typed all caps → all caps, first capital → capitalised unless the word has capitals of its own; `DisplayKey` lower case dedupes learned «Hello»/dictionary «hello»); `data/itmam-en.dict` (FrequencyWords en_full, same pinned commit 525f9b5…, `--language en --min-count frequencywords-en=5`, `EnglishContractions.Rebuild` puts split contractions back — stems before n't keep their counts, other suffixes shared by the counts of the words that take them (estimate), can't/won't share the rest, «i» → «I»; 298,335 words, 978 KB, 13 MB in memory, load 125 ms; single letters not kept); dictionary file format 2 (language tag, chars not XORed) for non-Arabic, Arabic files stay format 1; `data/blocklist.en.txt` (181 words from LDNOOBW en CC-BY-4.0, user decision: slurs, profanity and porn slang blocked; ordinary/medical/legal/news words kept; dick, cock, pussy, tit, hooker, horny, threesome NOT blocked; also kept: cocks, fingering, twinkie, bastinado, octopussy, smut, nsfw, masturbating); `data/corrections.en.txt` (by hand, MIT); `data/phrases.en.txt` (172 phrases by hand, MIT, user decision); phrase suggestions follow the typed case too. User decisions 2026-11-02: the seven borderline words and the extra kept words stay unblocked; glued junk such as «becauseyou» left in (ranks low). The app loads English at start-up when it is on. M4 done: Settings → «اللغات» / Languages (`SettingsPage.Languages`, after General): one card per installed keyboard language (`KeyboardLayouts.Installed` = GetKeyboardLayoutList) plus enabled ones without a keyboard, toggle = `EnabledLanguages` (disabled for unsupported), status Full / LearningOnly / Unsupported; the Arabic dictionary choice moved here from General («القاموس العربي»); a «شريطا اقتراحات؟» card opens ms-settings:typing. My words and the learned phrases on My phrases have a language choice (`AddLanguageChoice`, shown with 2+ languages: learned ones + enabled supported ones); every action uses it; alphabetical sort by the language culture. Arabic-only UI texts made general (NotAnArabicWord, NotAPhrase, shortcut key texts, FirstRunText, AboutTagline). About lists the sources of every loaded dictionary and the words of every language. Debug: `--demo-settings languages`. M5 done: ARCHITECTURE (Multiple languages section, dead keys/IME, schema 4), PRIVACY + site privacy pages (effective 2.0, 2026-11-02: keystrokes only in languages turned on, words stored with their language), THIRD_PARTY_NOTICES + data/README (English dictionary and blocklist), README ar/en, site feature 16, guide 2.0 (new chapter 10 «اللغات», chapters 10–19 renumbered 11–20, every settings screenshot retaken with recreated sample data: 8 Arabic + 3 English words, stats 1284/7931 since 2026-10-17, 4 My phrases, 3 text shortcuts, 5 long phrases; English shots with the interface language set to English), STORE_LISTING (What's new in 2.0, 20 features each, certification notes), version 2.0.0.0. Store package built (artifacts/msix/Itmam.Package_2.0.0.0_x64_arm64_bundle.msixupload; the English dictionary, lists and guides are in it). Timing tests run alone (`TimingCollection`, DisableParallelization). User testing 2026-11-02 (Debug build, real data migrated to schema 4 with user.db.v3-backup): fixed the English bar's arrows (`PopupSnapshot.RightToLeft`: Right = next in an English bar) and a stale layout in the hook (`ForegroundLayoutCache` now checks the focused window on every key; English typed after toggling languages was translated as Arabic). User decisions: two phrases at once when one continues the other stay as in Arabic. **English Wikipedia dictionary DONE (user decisions 2026-11-02/03: in 2.0, the DEFAULT for English):** enwiki-20261001 multistream dump (25.1 GB, SHA-1 verified, user approved the download) counted by `tools/DictionaryBuilder/wiki-counts-en.py` (multiprocessing over the index, bulk findall; case kept except the capitalised word starting a sentence; possessives dropped) → `data/raw/wikipedia-en.txt` → `data/itmam-en-wikipedia.dict` (FrequencyWords en weight 2 + wikipedia-en min count 100: 605,709 words, 2.7 MB, ~27 MB in memory, load ~210 ms); setting `UseEnglishWikipediaDictionary` (default true), «القاموس الإنجليزي» choice on the Languages page, switched live, falls back to the basic file. Docs, guide (new Languages screenshots), Store notes updated; package rebuilt. The 25 GB dump was deleted 2026-11-03 after the user approved the dictionary; `data/raw/wikipedia-en.txt` (counts, 37 MB, git-ignored) is kept to rebuild it. User decision 2026-11-03: a language turned off leaves memory at once (Arabic too) and its dictionary is loaded again as soon as it is turned on (`ItmamApp.LoadEnabledLanguages` from `Apply`, compacting GC; start-up loads only the languages that are on; `_engineOptions` replaces the stored Arabic Engine/Dictionary; About sums the loaded dictionaries). Measured (Debug, private bytes): ar+en 81 MB, ar 54 MB, en 43 MB, none 16 MB; the user confirmed it live in Task Manager (a compacting GC also after a load: loading buffers had doubled it). Import from any text (user decisions 2026-11-03: file only, every word added and suggested at once): `TextWords` (Decode: BOM/UTF-8/else the language's ANSI code page; IsWordList: the Export format or a plain list is imported as before, plain words get MinUsesToSuggest uses; Count: the language's words with occurrences, a sentence capital joins its lower-case form) + `LearningService.ImportCounts` (blocklist skipped, user-blocked stays blocked); confirmation with the counts; privacy policy (ar/en/site) says only words and counts are kept; verified by the user with a Persian text. The interface language choice moved from General to the top of the Languages page (section «الواجهة», user decision 2026-11-03); more interface languages postponed until a native reviewer is available. WACK 2.0.0.0 (2026-11-03, run from the command line with appcert.exe, report artifacts/wack-2.0.0.0.xml): OVERALL WARNING — the same two items as 1.x (Blocked executables: .NET files, dictionary data incl. itmam-en-wikipedia.dict, ShellExecute; DPIAwarenessValidation); certification notes now also name ms-settings:typing.
- **Fixes and features after 2.0 (branch `fix-readonly-context` MERGED into main 2026-11-04 and deleted; PUSHED 2026-11-04 with new guide/site screenshots and rebuilt guide PDFs (site and privacy page live, CI green); released as **2.1 (version 2.1.0.0)**: package built 2026-11-04 (artifacts/msix/Itmam.Package_2.1.0.0_x64_arm64_bundle.msixupload, built with the user's uncommitted WIP stashed), privacy effective date «الإصدار 2.1 (4 نوفمبر 2026)», What's new in 2.1 ar/en and certification notes (49 before / 30 after the caret, not read-only text) in STORE_LISTING.md, guide cover 2.1; **Submission 8 (package 2.1.0.0 + What's new ar/en, 7 lines) SENT FOR CERTIFICATION 2026-11-04, PUBLISHED 2026-11-05 (the user confirmed)**). The user's two 2.0-testing fixes left uncommitted were then merged in (user decision 2026-11-04: they came first): `KeyboardLayouts.Installed` leaves out layouts no longer under HKCU\Keyboard Layout\Preload (removed in Windows Settings, still loaded until sign-out), and `LearningService.LanguagesWithLearned` (only languages with learned words or phrases that reached PhraseMinUses) feeds the language choice in My words / My phrases; package rebuilt as 2.1.0.0 with them. Then (user decision, branch `picker-resize`, merged into 2.1 and the 2.1.0.0 package rebuilt with it): the long texts list window is resizable with a maximize button (`Sizable`, minimum 420 × 360 client), the layout follows the size in list and fill modes (`ArrangeList`/`ArrangeFill`, list = 2/5 of the free height), and a size chosen by the user (drag or maximize) is kept when switching between the list and the fields; it is also remembered for the next opening (user decision): setting `LongTextsWindow` (`WindowSize`: client size at 96 DPI + Maximized, the normal size kept when maximized; damaged values → null = default size), saved on closing only if the user resized or maximized, the window still centred on the typing screen:** reported by a user: picking an item in a
  web dropdown (select2-like) or clicking page text showed the bar with completions of the page's words (browsers put a
  caret at the click point in non-editable text). `UiaCaretContextSource` now checks `UIA_IsReadOnlyAttributeId` at the
  caret: true → `CaretContextStatus.ReadOnly` → `SilenceReason.ReadOnlyText` until the context changes; mixed/unsupported/
  error = editable. Verified by the user with the Debug build in Chrome, Edge and Firefox on a local test page.
  Then (user decision 2026-11-04): accepting with the caret inside a word replaces the WHOLE word and adds no space
  («محم|د» → «محمود»): the context read also returns ≤ 30 chars after the caret (`CaretContext.TextAfter`, clamped to the
  field end), the pipeline keeps only the count of the word's letters after the caret + the next char
  (`_wordAfterCaret`, `DeleteForward`; no undo then). Privacy policy (docs + site, ar/en) says «و30 حرفًا بعده»;
  effective from 2.1.
  Then (user decisions 2026-11-04): (1) setting `PhrasesLast` («مكان العبارات في الشريط»: في البداية/في النهاية, default
  start, on the «عباراتي» page): only phrases move after the dictionary words — the SAME suggestions are shown (phrases
  are not pushed out), text shortcuts and corrections stay first (`SuggestionController.PhrasesLast`). (2) «العبارات
  الطويلة» renamed «النصوص الطويلة» / "Long texts" in the UI, guide, site, READMEs, privacy policy, Store feature line
  and certification notes (the What's new history stays as published); code names (`LongPhrase…`, setting
  `LongPhrases`, `SettingsPage.LongPhrases`) unchanged so settings.json/backups keep working; guide/site screenshots
  retaken (sample data from the 2.0 session copied to the scratchpad, PrintWindow, Arabic flipped, dialog borders cropped 7 px). Long texts are reordered by dragging their cards
  (`CardReorder`, accent insertion line, auto-scroll near the edges; the picker follows the settings order).
  (3) «إضافة نص…» button in the shortcut's list (`LongPhrasePicker` `addNew` → `TrayApplication.AddLongPhrase`, same
  dialog as Settings, a used name replaced after a confirmation, the new text shown SELECTED, not inserted; hidden
  while filling variables).
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
