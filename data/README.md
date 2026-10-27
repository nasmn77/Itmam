# Dictionary data

`itmam-ar.dict` is the dictionary shipped with Itmam. It is generated, not hand-written: every word
and frequency comes from a real corpus listed below. Nothing here is invented.

## Sources

| Name | What | License | Pinned version |
|---|---|---|---|
| `frequencywords` | [FrequencyWords](https://github.com/hermitdave/FrequencyWords) by Hermit Dave: word counts from the OpenSubtitles 2018 corpus ([OPUS](https://opus.nlpl.eu/OpenSubtitles/corpus/version/OpenSubtitles)), file `content/2018/ar/ar_full.txt`. Conversational / everyday Arabic. | Content: [CC-BY-SA-4.0](https://creativecommons.org/licenses/by-sa/4.0/) (code: MIT) | commit `525f9b560de45753a5ea01069454e72e9aa541c6`, SHA-256 `EBB36AEAE78E609ADC552B32EC91FF2490E1170F8B244D22EEF9952D2096D610` |

Planned: Arabic Wikipedia word counts (CC-BY-SA-4.0) for formal vocabulary, added after the MVP.

**The dictionary file `itmam-ar.dict` is a derived work of these sources and is distributed under
CC-BY-SA-4.0**, separately from the MIT-licensed code. See `THIRD_PARTY_NOTICES.md`.

## Rebuilding

```powershell
# 1. Download the pinned sources into data/raw/ (git-ignored). This is the only network step.
./tools/DictionaryBuilder/download-sources.ps1

# 2. Build the dictionary (offline).
dotnet run --project tools/DictionaryBuilder -c Release -- `
    --source frequencywords=data/raw/frequencywords-ar_full.txt `
    --min-count frequencywords=3 `
    --drop-bare-alef-variants `
    --blocklist data/blocklist.txt `
    --output data/itmam-ar.dict
```

Options: `--source <name>=<path>` (repeatable; the name must be a known source with attribution),
`--weight <name>=<w>` (relative weight when merging several sources, default 1),
`--min-count <name>=<n>` (drop tokens seen fewer than n times), `--max-words <n>`,
`--drop-bare-alef-variants` (see step 5).

## What the builder does

1. Reads `word count` lines from each source and drops tokens below `--min-count`
   (3 for FrequencyWords: rarer tokens are mostly typos).
2. Strips tashkeel and tatweel; keeps only words made of Arabic letters, 2–25 letters long,
   with no letter repeated 3+ times in a row (drops `ههههه`, mixed scripts, digits, punctuation).
3. Removes every word whose normalized form is in `blocklist.txt`.
4. Converts each source's counts to per-million and merges sources with their weights.
5. Groups spellings by normalized form (أ/إ/آ/ٱ→ا, ى→ي, ة→ه) and keeps **several spellings per
   form, each with its own frequency**, so distinct words survive (أن / إن, على / علي, إلى / إلي).
   Within a form it keeps at most 4 spellings and drops:
   - spellings below 5% of the form's most frequent spelling (rare typos);
   - with `--drop-bare-alef-variants`: a spelling that is exactly a more frequent spelling with
     the hamza left off its alef (ان for أن, انا for أنا, الى for إلى). Subtitles write these
     very often (25–29% of the correct form), so the 5% rule alone does not remove them. Correct
     bare-alef words (انتظار, استخدام) are kept because they are the more frequent spelling.
6. Writes a Brotli-compressed binary file: sorted normalized keys with front coding, display
   spellings and varint frequencies (format documented in `src/Itmam.Core/Dictionary/DictionaryFile.cs`).

Current build: **522,618 words** (spellings), 1.66 MB.

## Blocklist

`blocklist.txt` lists words that are never suggested (one per line, `#` comments). Contributions
are welcome; rebuild the dictionary after editing it. Users can also block words themselves.
