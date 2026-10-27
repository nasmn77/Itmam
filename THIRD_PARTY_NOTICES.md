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

### Blocklist sources (`data/blocklist.txt`)

`data/blocklist.txt` contains a hand-reviewed selection of words from the two lists below.

**List of Dirty, Naughty, Obscene, and Otherwise Bad Words — Arabic (`ar`)**
- LDNOOBW project contributors — https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words
  (commit `5faf2ba42d7b1c0977169ec3611df25a3c08eb13`)
- License: Creative Commons Attribution 4.0 International (CC-BY-4.0) — https://creativecommons.org/licenses/by/4.0/
- Changes made: a subset of entries was selected; ordinary, medical, legal and news vocabulary was removed.

**Arabic Bad & Dirty Word Filter List**
- UXBERT — https://github.com/uxbertlabs/arabic_bad_dirty_word_filter_list
  (commit `15f64b425d0f34c0be3e3809f5eb0002eaf6e723`)
- License: MIT. Copyright (c) 2017 UXBERT.

  Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
  associated documentation files (the "Software"), to deal in the Software without restriction,
  including without limitation the rights to use, copy, modify, merge, publish, distribute,
  sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
  furnished to do so, subject to the following conditions: The above copyright notice and this
  permission notice shall be included in all copies or substantial portions of the Software.
  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

## Build-time and test packages (not redistributed in the app unless noted)

| Package | License | Notes |
|---|---|---|
| Microsoft.Windows.CsWin32 | MIT | Source generator; generated code is compiled into Itmam |
| xunit.v3 | Apache-2.0 | Tests only |
| Shouldly | BSD-3-Clause | Tests only |
