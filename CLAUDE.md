# CLAUDE.md — Itmam (إتمام)

As-you-type word prediction for the physical keyboard on Windows (Arabic first, English, any alphabet by learning), on the
Microsoft Store as MSIX. `PROMPT.md` is the full brief; this file is the short rule set. Read both at the start of a session.
History of every release and the detailed user decisions: [docs/HISTORY.md](docs/HISTORY.md) — read it before changing a feature.

## Current status
- **Latest published: 2.3.0.0** (Submission 11, published 2026-11-09). Everything is merged into `main`; no open branch.
- Store product **9P08F7KGRJB0**. A version number already uploaded is never rebuilt: any change needs a new number.
- A release: version in `Package.appxmanifest`, `tools/Build-Package.ps1`, What's new + certification notes in `STORE_LISTING.md`.
- Site: `site/` → GitHub Pages (https://nasmn77.github.io/Itmam/). Keep `site/privacy.html`, `site/en/privacy.html`, `docs/PRIVACY.md` in sync.
- Guide: `docs/guide/guide.{ar,en}.html` → `tools/Build-Guide.ps1` → PDFs (shipped in the package, copied to `site/assets/guide/`).
- Settings UI: `SettingsWindow` is the shell; one class per page in `src/Itmam.App/SettingsUi/Pages` (`SettingsPageBase`).
- Debug only: `--demo-settings [page] [en] [end]`, `--demo-popup x y …`, `--demo-longphrases …`, `--demo-longphrase-dialog …`;
  sample data folder via `ITMAM_DEMO_DATA`. Screenshots with PrintWindow (Arabic windows come out mirrored), never CopyFromScreen.

## Build & test
```
dotnet build Itmam.CI.slnf -c Release   # must be zero warnings (warnings are errors in Release)
dotnet test --solution Itmam.CI.slnf -c Release
```
Tests run on Microsoft.Testing.Platform (`global.json` → `test.runner`), not VSTest. `Itmam.CI.slnf` leaves out the MSIX
project (`.wapproj` builds only with Visual Studio's MSIX Packaging Tools + Windows 11 SDK 10.0.26100).

## Tech stack (fixed)
- .NET 10 SDK pinned by `global.json`. Central Package Management, shared settings in `Directory.Build.props`.
- C# latest, nullable on, implicit usings on. `Itmam.Core` targets `net10.0` (no Windows APIs, fully unit-testable);
  everything else `net10.0-windows10.0.19041.0`.
- WinForms, PerMonitorV2 DPI. P/Invoke via **CsWin32** (`NativeMethods.txt`), never hand-written `DllImport`.
- Tests: xUnit v3 + **Shouldly** (not FluentAssertions). Packaging: MSIX, `runFullTrust` only, **x64 and ARM64 only**.
- Approved dependencies (ask before adding any other): Microsoft.Windows.CsWin32 (MIT), Microsoft.Data.Sqlite 10.x
  (MIT / SQLite public domain), xunit.v3 (Apache-2.0), Shouldly (BSD-3-Clause).

## Decisions in force (details and reasons in docs/HISTORY.md)
- Data: `%LOCALAPPDATA%\Itmam\user.db` (SQLite, schema 4, words and phrases per language) and `settings.json`.
- Dictionaries are shipped, never downloaded: Arabic and English, basic + Wikipedia (the default). Derived files are
  CC-BY-SA-4.0, code is MIT; attribution in `THIRD_PARTY_NOTICES.md`. Verify a source's license and tell the user before downloading.
- Never merge distinct spellings (أن/إن, على/علي, إلى/إلي). Committed words are stored without tashkeel/tatweel.
- Blocklists are the user's choice word by word; ordinary, medical, legal and news words stay unblocked. A blocked word is
  never learned from typing, but one the user adds by hand is accepted.
- Languages: Arabic and English fully; any other alphabetic layout learns only; IME languages are silent. The golden
  snapshots in `tests/Itmam.Core.Tests/Golden` must stay unchanged (`ITMAM_UPDATE_GOLDEN=1` only for an intended change).
- Suggestions are offered, never applied automatically (text shortcuts, corrections, typing mistakes included).
- Bar: non-activating; never a real `ContextMenuStrip` on it. Long texts are inserted by pasting (clipboard saved and restored).
- Code names keep "LongPhrase(s)" although the UI says «النصوص الطويلة» / "Long texts" (settings and backups depend on them).
- Hook latency: p99 < 1 ms and max < 5 ms. Injected keys are ignored, so automated checks post events instead.
- Terminals are excluded by default. Silent in password fields, elevated, excluded and exclusive full-screen apps, read-only text.
- "Itmam is running" notice on every start. Diagnostics window in Debug builds only. Interface: Arabic or English only for now.
- Local test installs of the same version need `Remove-AppxPackage` first.

## Privacy rules (hard requirements)
- No network access at runtime: no `internetClient`, no telemetry, no crash upload. Two exceptions decided by the user:
  the Store update check through `StoreContext` (setting `CheckForUpdates`, default on) and the «تقييم ومراجعة» button,
  which opens the Store app's review page only when pressed. Published: "its only AUTOMATIC connection is the Store update check".
- Never log typed characters. Debug logs (Debug builds only) hold event types and timings only.
- Learn only word → count/last-used, and 2–5-word sequences → count/last-used (setting `LearnPhrases`; not learned after
  30 days → deleted). Never sentences. The last 8 words stay in memory only. Never learn in password fields or excluded apps.
- Caret context: at most 49 characters before the caret and 30 after it, never stored, never in password fields.
- Hook and storage code paths must be documented in `docs/ARCHITECTURE.md`; the privacy policy must match the code.

## Engineering rules
- Low-level hook callback: filter, translate, enqueue to a `Channel<T>`, decide swallow/pass — nothing else, no allocations, < 1 ms.
- Ignore `LLKHF_INJECTED` events. `ToUnicodeEx` with flag `0x4`. Hook/worker exceptions must never crash the app;
  reinstall the hook if Windows removes it.
- Code comments in English; all user-facing strings in `.resx` (Arabic + English). Arabic UI name **إتمام**, English **Itmam**.
- Public docs are bilingual, Arabic first, kept in sync: `README.md` (Arabic) + `README.en.md`; one file with an Arabic RTL
  section then English for `docs/*.md`, `THIRD_PARTY_NOTICES.md`, `data/README.md`, `src/Itmam.Package/README.md`, `STORE_LISTING.md`.
- Never fabricate APIs, package names or licenses; say when unsure and propose how to verify.
- Ask before: adding a dependency, choosing/downloading a word-list source, anything touching the network.

## Git rules
- Remote: https://github.com/nasmn77/Itmam (`origin`), default branch `main`.
- One branch per piece of work, merged into `main` only after the user approves; delete it (local and on GitHub) right after.
- Conventional Commits (`feat(core): …`, `fix(win32): …`), small and focused; never commit a broken build.
- **Ask before every `git push`**, tag, release or anything public. Never force-push `main`.
- Never commit certificates (`*.pfx`, private keys), Partner Center secrets, or user data (`user.db`, `user.json`, `settings.json`).

## Workflow
After each piece of work: build, run the tests, summarize the changes, add the release notes and decisions to
`docs/HISTORY.md` (Arabic and English), keep "Current status" here short, then **stop for review**.
