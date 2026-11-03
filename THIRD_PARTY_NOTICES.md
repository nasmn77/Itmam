# إشعارات الأطراف الثالثة | Third-party notices

[العربية](#العربية) · [English](#english)

<div dir="rtl">

## العربية

الشيفرة المصدرية لإتمام مرخّصة برخصة MIT (انظر `LICENSE`). ويتضمن إتمام، أو يُبنى باستخدام، المكوّنات والبيانات التالية من أطراف أخرى، ولكل منها رخصته. هذا القسم ترجمة للتسهيل؛ والنص الإنجليزي أدناه ونصوص الرخص الأصلية هي المرجع.

### البيانات

#### FrequencyWords — تكرار الكلمات العربية (داخل `data/itmam-ar.dict`)

- المؤلف: Hermit Dave — https://github.com/hermitdave/FrequencyWords
- مشتقة من مدوّنة ترجمات OpenSubtitles لعام 2018 (http://www.opensubtitles.org/)، الموزّعة عبر مشروع OPUS (P. Lison و J. Tiedemann، 2016، *OpenSubtitles2016: Extracting Large Parallel Corpora from Movie and TV Subtitles*، مؤتمر LREC 2016).
- رخصة المحتوى: المشاع الإبداعي، نسب المصنف – الترخيص بالمثل 4.0 الدولية (CC-BY-SA-4.0) — https://creativecommons.org/licenses/by-sa/4.0/
- التعديلات: تصفية (حروف عربية فقط، وطول من 2 إلى 25 حرفًا، وتكرار 3 على الأقل، وحذف الكلمات غير الصالحة والمحظورة)، وحذف التشكيل، ودمج صور الكتابة المختلفة للكلمة، وتحويل التكرار إلى نسبة لكل مليون، وإعادة الترميز بصيغة ثنائية مضغوطة. التفاصيل في `data/README.md`.

ملف القاموس `data/itmam-ar.dict` مُعدَّل عن هذه المادة، ولذلك رخصته **CC-BY-SA-4.0**. ولا يتأثر بذلك برنامج إتمام نفسه، فيبقى برخصة MIT.

#### ويكيبيديا العربية (القاموس الثاني `data/itmam-ar-wikipedia.dict`)

- المصدر: نصوص مقالات ويكيبيديا العربية من ملف النسخ العام (https://dumps.wikimedia.org/arwiki/)، من تأليف محرري ويكيبيديا.
- الرخصة: المشاع الإبداعي، نسب المصنف – الترخيص بالمثل 4.0 الدولية (CC-BY-SA-4.0).
- التعديلات: عُدّت الكلمات العربية في نص المقالات بعد حذف القوالب والمراجع والجداول والوسوم والتشكيل (الأداة `tools/DictionaryBuilder/wiki-counts.py`)، ثم دُمجت مع تكرارات FrequencyWords بالأداة نفسها التي تبني القاموس المضمّن.

يُضمَّن هذا القاموس في التطبيق بجانب الأساسي، ويختار المستخدم أحدهما من الإعدادات ← «اللغات». وهو مُعدَّل عن هذه المادة وعن FrequencyWords، ولذلك رخصته **CC-BY-SA-4.0**.

#### FrequencyWords — تكرار الكلمات الإنجليزية (داخل `data/itmam-en.dict`، من الإصدار 2.0)

- المؤلف والمصدر والرخصة كما في القسم العربي أعلاه: القائمة الإنجليزية `content/2018/en/en_full.txt` من المشروع نفسه (الـ commit `525f9b560de45753a5ea01069454e72e9aa541c6`)، برخصة CC-BY-SA-4.0.
- التعديلات: أُعيد تركيب الاختصارات التي فصلها المصدر عند الفاصلة العليا (`don` + `'t` ← `don't`، وتكرارات بعضها تقديرية)، وكُتب `i` بحرف كبير، ثم تصفية (حروف لاتينية فقط، وطول من 2 إلى 25، وتكرار 5 على الأقل، وحذف الكلمات المحظورة)، ودمج الصور المختلفة للكلمة، وإعادة الترميز. التفاصيل في `data/README.md`.

ملف القاموس `data/itmam-en.dict` مُعدَّل عن هذه المادة، ولذلك رخصته **CC-BY-SA-4.0**.

#### ويكيبيديا الإنجليزية (القاموس الإنجليزي الثاني `data/itmam-en-wikipedia.dict`، من الإصدار 2.0)

- المصدر: نصوص مقالات ويكيبيديا الإنجليزية من ملف النسخ العام (https://dumps.wikimedia.org/enwiki/، نسخة 2026-10-01)، من تأليف محرري ويكيبيديا.
- الرخصة: المشاع الإبداعي، نسب المصنف – الترخيص بالمثل 4.0 الدولية (CC-BY-SA-4.0).
- التعديلات: عُدّت الكلمات في نص المقالات بعد حذف القوالب والمراجع والجداول والوسوم (الأداة `tools/DictionaryBuilder/wiki-counts-en.py`)، دون صيغ الملكية، ثم دُمجت مع تكرارات FrequencyWords الإنجليزية.

يُضمَّن بجانب القاموس الإنجليزي الأساسي وهو الافتراضي، ويختار المستخدم أحدهما من الإعدادات ← «اللغات». رخصته **CC-BY-SA-4.0**.

#### مصادر قائمة الحظر (`data/blocklist.txt`)

يحتوي `data/blocklist.txt` على مختارات راجعها المطوّر يدويًا من القائمتين التاليتين:

- **List of Dirty, Naughty, Obscene, and Otherwise Bad Words — القائمة العربية (`ar`)**: من المساهمين في مشروع LDNOOBW، برخصة المشاع الإبداعي نسب المصنف 4.0 الدولية (CC-BY-4.0). التعديلات: اختيرت بعض الكلمات فقط، واستُبعدت المفردات العادية والطبية والقانونية والإخبارية.
- **Arabic Bad & Dirty Word Filter List**: من UXBERT، برخصة MIT. حقوق النشر (c) 2017 UXBERT. نص الرخصة كاملًا في القسم الإنجليزي.

#### مصدر قائمة الحظر الإنجليزية (`data/blocklist.en.txt`، من الإصدار 2.0)

- **List of Dirty, Naughty, Obscene, and Otherwise Bad Words — القائمة الإنجليزية (`en`)**: من المساهمين في مشروع LDNOOBW، © 2012–2020 Shutterstock, Inc.، برخصة CC-BY-4.0 (الـ commit `5faf2ba42d7b1c0977169ec3611df25a3c08eb13`). التعديلات: اختار المطوّر الشتائم العنصرية والألفاظ النابية والمصطلحات الإباحية فقط، واستُبعدت المفردات العادية والطبية والقانونية والإخبارية والكلمات التي لها معنى عادي شائع.

### المكتبات المضمَّنة في التطبيق

| المكتبة | الرخصة | الاستخدام |
|---|---|---|
| Microsoft.Data.Sqlite 10.0.14 | MIT — © Microsoft Corporation | قراءة ملف كلماتك وحفظه |
| SQLitePCLRaw 2.1.13 (bundle_e_sqlite3) | Apache-2.0 — © 2014-2024 SourceGear, LLC | ربط .NET بمحرك SQLite |
| SQLite (مكتبة e_sqlite3 الأصلية) | ملكية عامة | قاعدة البيانات على جهازك |

### حزم البناء والاختبار (لا تُوزَّع مع التطبيق إلا حيث ذُكر)

| الحزمة | الرخصة | ملاحظات |
|---|---|---|
| Microsoft.Windows.CsWin32 | MIT | مولّد شيفرة؛ الشيفرة المولَّدة تُترجم داخل إتمام |
| xunit.v3 | Apache-2.0 | للاختبارات فقط |
| Shouldly | BSD-3-Clause | للاختبارات فقط |

</div>

## English

Itmam's source code is licensed under the MIT License (see `LICENSE`). It includes or is built
with the following third-party components and data, each under its own license.

### Data

#### FrequencyWords — Arabic word frequencies (shipped in `data/itmam-ar.dict`)

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

#### Arabic Wikipedia (second dictionary `data/itmam-ar-wikipedia.dict`)

- Source: the text of Arabic Wikipedia articles from the public dumps (https://dumps.wikimedia.org/arwiki/), by
  Wikipedia contributors.
- License: Creative Commons Attribution-ShareAlike 4.0 International (CC-BY-SA-4.0).
- Changes made: Arabic words counted in the article text after removing templates, references, tables, tags and
  tashkeel (`tools/DictionaryBuilder/wiki-counts.py`), then merged with the FrequencyWords counts by the same tool that
  builds the shipped dictionary.

This dictionary ships with the app next to the basic one; the user chooses one in Settings → Languages. It is
adapted from this material and FrequencyWords, so it is licensed under **CC-BY-SA-4.0**.

#### FrequencyWords — English word frequencies (shipped in `data/itmam-en.dict`, from 2.0)

- Author, source and license as for the Arabic list above: the English list `content/2018/en/en_full.txt` of the same
  project (commit `525f9b560de45753a5ea01069454e72e9aa541c6`), CC-BY-SA-4.0.
- Changes made: contractions the source split at the apostrophe were put back together (`don` + `'t` → `don't`; some
  counts are estimates), `i` written `I`, then filtered (Latin letters only, length 2–25, minimum count 5, blocklisted
  words removed), spelling variants merged, re-encoded. See `data/README.md`.

The dictionary file `data/itmam-en.dict` is an adaptation of this material and is therefore licensed under
**CC-BY-SA-4.0**.

#### English Wikipedia (second English dictionary `data/itmam-en-wikipedia.dict`, from 2.0)

- Source: the text of English Wikipedia articles from the public dumps (https://dumps.wikimedia.org/enwiki/, dump of
  2026-10-01), by Wikipedia contributors.
- License: Creative Commons Attribution-ShareAlike 4.0 International (CC-BY-SA-4.0).
- Changes made: words counted in the article text after removing templates, references, tables and tags
  (`tools/DictionaryBuilder/wiki-counts-en.py`), possessives left out, then merged with the English FrequencyWords counts.

It ships next to the basic English dictionary and is the default; the user chooses one in Settings → Languages. It is
licensed under **CC-BY-SA-4.0**.

#### Blocklist sources (`data/blocklist.txt`)

`data/blocklist.txt` contains a hand-reviewed selection of words from the two lists below.

**List of Dirty, Naughty, Obscene, and Otherwise Bad Words — Arabic (`ar`)**
- LDNOOBW project contributors — https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words
  (commit `5faf2ba42d7b1c0977169ec3611df25a3c08eb13`)
- License: Creative Commons Attribution 4.0 International (CC-BY-4.0) — https://creativecommons.org/licenses/by/4.0/
- Changes made: a subset of entries was selected; ordinary, medical, legal and news vocabulary was removed.

**Arabic Bad & Dirty Word Filter List**
- UXBERT — https://github.com/uxbertlabs/arabic_bad_dirty_word_filter_list
  (commit `15f64b425d0f34c0be3e3809f5eb0002eaf6e723`)
- License: MIT.

```
Copyright (c) 2017 UXBERT

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

#### English blocklist source (`data/blocklist.en.txt`, from 2.0)

**List of Dirty, Naughty, Obscene, and Otherwise Bad Words — English (`en`)**
- LDNOOBW project contributors, © 2012–2020 Shutterstock, Inc. — https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words
  (commit `5faf2ba42d7b1c0977169ec3611df25a3c08eb13`)
- License: Creative Commons Attribution 4.0 International (CC-BY-4.0) — https://creativecommons.org/licenses/by/4.0/
- Changes made: the maintainer kept only slurs, profanity and porn slang; ordinary, medical, legal and news vocabulary
  and words with a common ordinary meaning were removed.

### Libraries shipped in the app

| Package | License | Use |
|---|---|---|
| Microsoft.Data.Sqlite 10.0.14 | MIT — © Microsoft Corporation | Reads and saves your words file |
| SQLitePCLRaw 2.1.13 (bundle_e_sqlite3) | Apache-2.0 — © 2014-2024 SourceGear, LLC — https://www.apache.org/licenses/LICENSE-2.0 | Connects .NET to the SQLite engine |
| SQLite (native e_sqlite3 library) | Public domain — https://www.sqlite.org/copyright.html | The database on your device |

### Build-time and test packages (not redistributed in the app unless noted)

| Package | License | Notes |
|---|---|---|
| Microsoft.Windows.CsWin32 | MIT | Source generator; generated code is compiled into Itmam |
| xunit.v3 | Apache-2.0 | Tests only |
| Shouldly | BSD-3-Clause | Tests only |
