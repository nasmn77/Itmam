# Itmam (إتمام)

[العربية](README.md) · **English**

**Itmam** suggests Arabic words as you type on your physical keyboard, in any Windows app, like the suggestions on a phone: a small bar above the caret shows the closest words to what you started typing, and one key inserts the one you want.

It works fully offline, learns your own words, and never saves what you type.

Website: https://nasmn77.github.io/Itmam/en/ · Repository: https://github.com/nasmn77/Itmam

## Features

- **Instant suggestions** in Notepad, Word, browsers and most apps, from an Arabic dictionary of more than 500,000 words.
- **Learns your words:** a word you use twice becomes a suggestion, and the words you use most come first.
- **Understands Arabic writing:** «احمد» finds «أحمد», «ان» suggests «أن», «إن» and «أنا», and the article «ال» and attached prepositions and conjunctions are handled.
- **A bar that looks like Windows 11's own**, following light or dark mode, with the colors, size and opacity you choose.
- **Completes common phrases:** «بسم الله الرحمن الرحيم», «السلام عليكم ورحمة الله وبركاته», «جزاك الله خيرا» and about 250 others, whole with Tab or word by word, plus "My phrases" you add yourself.
- **Text shortcuts:** type «ع1» and the bar offers «السلام عليكم ورحمة الله وبركاته»; Tab puts it in place, with the shortcuts you choose.
- **Learns your phrases:** words you type in a row 3 times are then suggested as a whole phrase (can be turned off).
- **Completes a word from the middle:** go back into a word with a click or Backspace and it continues from the caret.
- **Silent where it should be:** password fields, excluded apps (such as terminals and remote desktop) and full-screen games.
- **Arabic and English interface**, switched instantly, right to left in Arabic.

## Shortcuts

| Shortcut | What it does |
|---|---|
| <kbd>Tab</kbd> | Insert the first suggestion |
| <kbd>Ctrl</kbd>+<kbd>1</kbd>…<kbd>9</kbd> | Insert suggestion by number |
| <kbd>↑</kbd>, then <kbd>←</kbd> <kbd>→</kbd>, then <kbd>Enter</kbd> | Choose with the arrow keys, then insert |
| <kbd>Esc</kbd> | Hide the suggestions until the next word |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd> (press and release) | Turn Itmam off or on from anywhere |
| <kbd>Backspace</kbd> right after inserting | Undo: puts back what you had typed |
| Right-click a suggestion | Menu: delete a learned word or phrase, or add the phrase to "My phrases" |
| <kbd>Ctrl</kbd>+<kbd>Delete</kbd> while choosing with the arrows | Forget a learned word |

Shortcuts can be changed in Settings → "Shortcuts".

## Settings

Click the Itmam icon next to the clock (right or left button) and choose "Settings":

- **General:** start with Windows, number of suggestions, letters before they appear, interface language.
- **Appearance:** theme, bar, text and selection colors, opacity, text size, with a live preview.
- **Shortcuts:** record a new shortcut by pressing it; shortcuts that would break typing or Windows shortcuts are refused.
- **Excluded apps:** programs where Itmam stays completely silent.
- **My words:** add, edit, block and delete words, import and export, delete all.
- **Phrases:** turn phrase suggestions and phrase learning on or off, "My phrases" (with import and export), and the learned phrases (delete, move to "My phrases", delete all).
- **Text shortcuts:** add a shortcut («ع1») and its text, edit, delete, import and export.
- **Updates:** the version, and checking for an update through Microsoft Store.

## The icon next to the clock

Itmam has no main window and no taskbar button; it runs from an icon next to the clock. Windows 11 hides new icons in the overflow menu (the ^ arrow). To keep it always visible:

- drag the Itmam icon from the overflow menu next to the clock, or
- in Windows Settings → Personalization → Taskbar, turn Itmam on under the other system tray icons.

## Turning off vs. exiting

- **Turn off** (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>, "Turn off Itmam" in the icon's menu or in Settings → "Exit", or "Pause for 1 hour"): Itmam tracks nothing and intercepts no key until you turn it on again.
- **Exit** (from the icon's menu or the "Exit" page in Settings): closes completely until your next sign-in or until you start it yourself.
- **To stop it starting with Windows:** Settings → General → "Start with Windows", or Windows Settings → Apps → Startup.

## Privacy

- Itmam itself never connects to the internet: no statistics, no crash reports. It only asks Microsoft Store about updates at start-up, which can be turned off in Settings → "Updates".
- For each word only the word, how many times it was used and when are kept, in a file on your device. No sentences.
- To learn phrases, short sequences (2 to 5 words) are kept with their counts; one not typed 3 times is deleted after 30 days. This can be turned off in Settings.
- Nothing is read or learned in password fields or excluded apps.

Full details in the [privacy policy](docs/PRIVACY.md).

## Requirements

- Windows 10 version 2004 (19041) or later, or Windows 11.
- x64 or ARM64 processor.
- An Arabic keyboard layout added in Windows.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```
dotnet build Itmam.CI.slnf -c Release
dotnet test --solution Itmam.CI.slnf -c Release
```

Then run `src\Itmam.App\bin\Release\net10.0-windows10.0.19041.0\Itmam.exe`.

**Store package (MSIX):** needs Visual Studio 2026 with "MSIX Packaging Tools" and a Windows 11 SDK, then `./tools/Build-Package.ps1`. Building and testing locally with a self-signed certificate: [src/Itmam.Package/README.md](src/Itmam.Package/README.md).
How it works and how the code is organized: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Contributing

- **Bugs and ideas:** open an [issue](https://github.com/nasmn77/Itmam/issues) describing what happened and in which app.
- **Dictionary:** built by `tools/DictionaryBuilder` from openly licensed sources, as described in [data/README.md](data/README.md). To propose a new word source, open an issue with its link and license; no source is added before its license is checked.
- **Blocklist** (`data/blocklist.txt`): one word per line. Send a pull request with the reason for adding or removing a word; ordinary, medical, legal and news vocabulary is deliberately not blocked.
- **Code:** before a pull request, the build must have zero warnings and all tests must pass (commands above). Code comments and docs are in English; interface text lives in `.resx` files in Arabic and English.

## License and sources

- Source code: [MIT](LICENSE).
- The dictionary (`data/itmam-ar.dict`) is derived from the [FrequencyWords](https://github.com/hermitdave/FrequencyWords) word frequency lists (built from OpenSubtitles subtitles) and is licensed [CC-BY-SA-4.0](https://creativecommons.org/licenses/by-sa/4.0/).
- All components and their licenses: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Developer

Naser Mohammad Almadi — nasmn77@gmail.com
