# Itmam (إتمام)

[العربية](README.md) · **English**

**Itmam** suggests words as you type on your physical keyboard, in any Windows app, in Arabic, English and other languages, like the suggestions on a phone: a small bar above the caret shows the closest words to what you started typing, and one key inserts the one you want.

It sends no data, learns your own words, and never saves your text.

Website: https://nasmn77.github.io/Itmam/en/ · Repository: https://github.com/nasmn77/Itmam · User guide (PDF): https://nasmn77.github.io/Itmam/assets/guide/itmam-guide-en.pdf

## Features

- **Instant suggestions** in Notepad, Word, browsers and most apps, from an Arabic dictionary of about 880,000 words (or the lighter basic one, more than 500,000).
- **Learns your words:** a word you use twice becomes a suggestion, and the words you use most come first.
- **Understands Arabic writing:** «احمد» finds «أحمد», «ان» suggests «أن», «إن» and «أنا», and the article «ال» and attached prepositions and conjunctions are handled.
- **A bar that looks like Windows 11's own**, following light or dark mode, with the colors, size and opacity you choose.
- **Completes common phrases:** «بسم الله الرحمن الرحيم», «السلام عليكم ورحمة الله وبركاته», «جزاك الله خيرا» and about 250 others, whole with Tab or word by word, plus "My phrases" you add yourself.
- **Text shortcuts:** type «ع1» and the bar offers «السلام عليكم ورحمة الله وبركاته»; Tab puts it in place, with the shortcuts you choose.
- **Long texts of several lines** (such as technical support replies): press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Space</kbd> in any program and choose by name from the list. With **variables**: `{customer name}` is asked for when inserting, and `{{date}}` is written by Itmam (the date, the Hijri date, the day and the time).
- **Fixes common mistakes:** «لاكن» → «لكن», «انشاءالله» → «إن شاء الله», «مدرسه» → «مدرسة», with Tab.
- **Learns your phrases:** words you type in a row twice are then suggested as a whole phrase (can be turned off).
- **Completes a word from the middle:** go back into a word with a click or Backspace and it continues from the caret.
- **Several languages (from 2.0):** it follows your keyboard layout and suggests in the language you are typing.
  Arabic and English have a full dictionary (English: about 600,000 words from film subtitles and English Wikipedia, or
  the basic one of about 300,000; with `don't`, `I'm` and letter case:
  «Lon» → «London»). Any other alphabetic language (Hebrew, French, Russian …) can be turned on in Settings →
  Languages: Itmam then learns its words and phrases from your typing. It stays silent with Chinese, Japanese and Korean.
- **Silent where it should be:** password fields, excluded apps (such as terminals and remote desktop) and full-screen games.
- **Arabic and English interface**, switched instantly, right to left in Arabic.

## Shortcuts

| Shortcut | What it does |
|---|---|
| <kbd>Tab</kbd> | Insert the first suggestion |
| <kbd>Ctrl</kbd>+<kbd>1</kbd>…<kbd>9</kbd> | Insert suggestion by number |
| <kbd>↑</kbd>, then <kbd>←</kbd> <kbd>→</kbd>, then <kbd>Enter</kbd> | Choose with the arrow keys, then insert |
| <kbd>Esc</kbd> | Hide the suggestions until the next letter |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd> (press and release) | Turn Itmam off or on from anywhere |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Space</kbd> | The "Long texts" list: choose by name and it is typed where you write |
| <kbd>Backspace</kbd> right after inserting | Undo: puts back what you had typed |
| Right-click a suggestion | Actions over the suggestion: "Delete" a learned word or a phrase, "Block" a word, "Add to My phrases" |
| <kbd>Ctrl</kbd>+<kbd>Delete</kbd> while choosing with the arrows | Forget a learned word |

Shortcuts can be changed in Settings → "Keyboard shortcuts".

## Settings

Click the Itmam icon next to the clock (right or left button) and choose "Settings":

- **General:** start with Windows, number of suggestions, letters before they appear.
- **Languages:** the interface language (it starts in Arabic if Windows is displayed in Arabic and in English otherwise, and the tray menu switches it too), the keyboard languages on your PC and what Itmam does with each, turning them on or off (a
  language turned off frees its dictionary from memory at once), and the Arabic and English dictionaries (basic or Wikipedia).
- **Appearance:** theme, bar, text and selection colors, opacity, text size, with a live preview.
- **Shortcuts:** record a new shortcut by pressing it; shortcuts that would break typing or Windows shortcuts are refused.
- **Excluded apps:** programs where Itmam stays completely silent.
- **My words:** add, edit, block and delete words, import from any text file (its words are taken and suggested at once; the text is not kept), export, delete all, one language at a time.
- **My phrases:** turn phrase suggestions and phrase learning on or off, "My phrases" (with import and export), and the learned phrases (delete, move to "My phrases", delete all).
- **Text shortcuts:** add a shortcut («ع1») and its text, edit, delete, import and export.
- **Long texts:** add a text with its name and content (up to 4,000 characters, several lines), "Insert variable…", edit, delete, reorder by dragging, import and export. Texts can also be added from the shortcut's list itself.
- **Phrases in the bar** (My phrases page): at its start or at its end, after the words.
- **Updates:** the version, checking for an update through Microsoft Store, and "Rate and review" (also on the About page).

## The icon next to the clock

Itmam has no main window and no taskbar button; it runs from an icon next to the clock. Windows 11 hides new icons in the overflow menu (the ^ arrow). To keep it always visible:

- drag the Itmam icon from the overflow menu next to the clock, or
- in Windows Settings → Personalization → Taskbar, turn Itmam on under the other system tray icons.

## Turning off vs. exiting

- **Turn off** (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>, "Turn off Itmam" in the icon's menu or in Settings → "Exit", or "Pause for 1 hour"): Itmam tracks nothing and intercepts no key until you turn it on again.
- **Exit** (from the icon's menu or the "Exit" page in Settings): closes completely until your next sign-in or until you start it yourself.
- **To stop it starting with Windows:** Settings → General → "Start with Windows", or Windows Settings → Apps → Startup.

## Privacy

- Itmam sends no data: no statistics, no crash reports. Its only automatic connection is asking Microsoft Store about updates, and it can be turned off in Settings → "Updates". "Rate and review" opens the Store's window only when you press it.
- For each word only the word, how many times it was used and when are kept, in a file on your device. No sentences.
- To learn phrases, short sequences (2 to 5 words) are kept with their counts; one not typed twice is deleted after 30 days. This can be turned off in Settings.
- Nothing is read or learned in password fields or excluded apps.

Full details in the [privacy policy](docs/PRIVACY.md).

## Requirements

- Windows 10 version 2004 (19041) or later, or Windows 11.
- x64 or ARM64 processor.
- An Arabic or English keyboard layout (or another language you turn on) added in Windows.

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
- The Arabic (`data/itmam-ar*.dict`) and English (`data/itmam-en*.dict`) dictionaries are derived from the [FrequencyWords](https://github.com/hermitdave/FrequencyWords) word frequency lists (built from OpenSubtitles subtitles), and the Wikipedia ones from Arabic and English Wikipedia text too, and are licensed [CC-BY-SA-4.0](https://creativecommons.org/licenses/by-sa/4.0/).
- All components and their licenses: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Developer

Naser Mohammad Almadi — nasmn77@gmail.com
