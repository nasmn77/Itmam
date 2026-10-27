# Third-party notices

Itmam's source code is licensed under the MIT License (see `LICENSE`). It includes or is built
with the following third-party components and data, each under its own license.

## Data

### FrequencyWords — Arabic word frequencies (shipped in `data/itmam-ar.dict`)

- Author: Hermit Dave — https://github.com/hermitdave/FrequencyWords
- Derived from the OpenSubtitles 2018 corpus (http://www.opensubtitles.org/), distributed via
  OPUS (P. Lison and J. Tiedemann, 2016, *OpenSubtitles2016: Extracting Large Parallel Corpora from
  Movie and TV Subtitles*, LREC 2016).
- License of the content: Creative Commons Attribution-ShareAlike 4.0 International
  (CC-BY-SA-4.0) — https://creativecommons.org/licenses/by-sa/4.0/
- Changes made: filtered (Arabic letters only, length 2–25, minimum count 3, junk and blocklisted
  words removed), tashkeel removed, spelling variants merged, frequencies converted to per-million,
  re-encoded in a compact binary format. See `data/README.md`.

The dictionary file `data/itmam-ar.dict` is an adaptation of this material and is therefore
licensed under **CC-BY-SA-4.0**. The Itmam program code is not affected and remains MIT.

## Build-time and test packages (not redistributed in the app unless noted)

| Package | License | Notes |
|---|---|---|
| Microsoft.Windows.CsWin32 | MIT | Source generator; generated code is compiled into Itmam |
| xunit.v3 | Apache-2.0 | Tests only |
| Shouldly | BSD-3-Clause | Tests only |
