# بيانات القاموس | Dictionary data

[العربية](#العربية) · [English](#english)

<div dir="rtl">

## العربية

`itmam-ar.dict` هو القاموس الذي يُشحن مع إتمام. وهو مُولَّد لا مكتوب باليد: كل كلمة وكل تكرار فيه مأخوذ من مدوّنة نصوص حقيقية مذكورة أدناه. لا شيء هنا مُختلَق.

### المصادر

| الاسم | المحتوى | الرخصة | الإصدار المثبَّت |
|---|---|---|---|
| `frequencywords` | [FrequencyWords](https://github.com/hermitdave/FrequencyWords) من Hermit Dave: عدد مرات ورود الكلمات في مدوّنة ترجمات OpenSubtitles لعام 2018 ([OPUS](https://opus.nlpl.eu/OpenSubtitles/corpus/version/OpenSubtitles))، الملف `content/2018/ar/ar_full.txt`. عربية المحادثة والاستعمال اليومي. | المحتوى: [CC-BY-SA-4.0](https://creativecommons.org/licenses/by-sa/4.0/) (الشيفرة: MIT) | الـ commit ‏`525f9b560de45753a5ea01069454e72e9aa541c6`، وبصمة SHA-256 ‏`EBB36AEAE78E609ADC552B32EC91FF2490E1170F8B244D22EEF9952D2096D610` |

مخطَّط لاحقًا: تكرار الكلمات في ويكيبيديا العربية (CC-BY-SA-4.0) لمفردات الفصحى، بعد اكتمال النسخة الأولى.

**ملف القاموس `itmam-ar.dict` عمل مشتق من هذه المصادر، ويُوزَّع برخصة CC-BY-SA-4.0** مستقلًا عن الشيفرة المرخّصة بـ MIT. انظر `THIRD_PARTY_NOTICES.md`.

### إعادة البناء

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

الخطوة الأولى تنزّل المصادر المثبَّتة إلى `data/raw/` (مستبعد من git)، وهي الخطوة الوحيدة التي تتصل بالشبكة. والثانية تبني القاموس دون اتصال.

الخيارات:
- `--source <name>=<path>`: مصدر (يتكرر؛ ويجب أن يكون الاسم مصدرًا معروفًا له إسناد).
- `--weight <name>=<w>`: الوزن النسبي عند دمج عدة مصادر (الافتراضي 1).
- `--min-count <name>=<n>`: إسقاط الكلمات التي وردت أقل من n مرة.
- `--max-words <n>`: الحد الأقصى لعدد الكلمات.
- `--drop-bare-alef-variants`: انظر الخطوة 5.

### ما الذي تفعله أداة البناء

1. تقرأ أسطر `word count` من كل مصدر، وتُسقط ما ورد أقل من `--min-count` (3 في FrequencyWords: الأندر غالبًا أخطاء كتابة).
2. تحذف التشكيل والتطويل؛ وتُبقي الكلمات المكوّنة من حروف عربية فقط، بطول من 2 إلى 25 حرفًا، ولا يتكرر فيها حرف 3 مرات متتالية أو أكثر (فتُسقط `ههههه`، والكتابة المختلطة، والأرقام، وعلامات الترقيم).
3. تحذف كل كلمة تقع صورتها الموحَّدة في `blocklist.txt`.
4. تحوّل تكرار كل مصدر إلى نسبة لكل مليون، وتدمج المصادر بأوزانها.
5. تجمع صور الكتابة حسب الصورة الموحَّدة (أ/إ/آ/ٱ←ا، ى←ي، ة←ه)، وتحتفظ **بعدة صور كتابة للصورة الواحدة، لكل منها تكراره**، فتبقى الكلمات المختلفة (أن / إن، على / علي، إلى / إلي). وفي الصورة الواحدة تحتفظ بأربع صور على الأكثر، وتُسقط:
   - الصور التي يقل تكرارها عن 5% من أكثر صورة تكرارًا (أخطاء كتابة نادرة)؛
   - ومع `--drop-bare-alef-variants`: الصورة التي هي بالضبط صورة أكثر تكرارًا بلا همزة على ألفها (ان بدل أن، انا بدل أنا، الى بدل إلى). الترجمات تكتبها كثيرًا جدًا (25–29% من الصورة الصحيحة)، فلا تكفي قاعدة الـ 5% لحذفها. أما الكلمات الصحيحة بألف بلا همزة (انتظار، استخدام) فتبقى لأنها هي الصورة الأكثر تكرارًا.
6. تكتب ملفًا ثنائيًا مضغوطًا بـ Brotli: مفاتيح موحَّدة مرتّبة بترميز البادئات (front coding)، وصور الكتابة المعروضة، والتكرار بترميز varint (الصيغة موثّقة في `src/Itmam.Core/Dictionary/DictionaryFile.cs`).

البناء الحالي: **522,510 كلمة** (صورة كتابة)، 1.66 ميغابايت؛ وحذفت قائمة الحظر 108 مدخلات.

### العبارات المتلازمة (`phrases.txt`)

عبارات عربية شائعة يكملها إتمام كاملة أو كلمة بعد كلمة (التحية، والذكر، والدعاء، والمناسبات، والرسائل الرسمية). جُمعت يدويًا لإتمام وتصدر برخصة MIT كالشيفرة. عبارة في كل سطر، كلمتان على الأقل، دون تشكيل؛ والترتيب مهم، فالأشهر أولًا داخل كل فئة. المساهمات مرحَّب بها.

### قائمة الحظر

`blocklist.txt` يضم الكلمات التي لا تُقترح أبدًا (كلمة في كل سطر، والسطر الذي يبدأ بـ `#` تعليق). المساهمات مرحَّب بها؛ أعد بناء القاموس بعد تعديلها. ويستطيع المستخدمون أيضًا حظر الكلمات بأنفسهم.

- **المصادر:** 21 كلمة اختيرت يدويًا من قائمة LDNOOBW العربية `ar` (CC-BY-4.0) والقائمة العربية من uxbertlabs (MIT)؛ انظر `THIRD_PARTY_NOTICES.md`. ورُفض مصدر ثالث (AhmedRedaAmin/ArabicSlangProfanity) لأنه مكتوب بحروف لاتينية (عربيزي).
- **لا يُحظر عمدًا:** كلمات من تلك القوائم عادية أو لها معنى شائع غير مسيء، أو من مفردات الطب أو القانون والدين أو الأخبار: أمك، أختك، نيك (غالبًا اسم Nick)، شاذ، قضيب، قواد، فرج، حلمه، خول، لبوة، مبادل، زق، بز، ثدي، شرج، بظر، احتلام، خنثى، جماع، شهوة، اغتصاب، لواط، سحاق، لعق، لحس، مص، تمص، بيضان.
- **المطابقة:** بالصورة الموحَّدة مع توحيد مقاعد الهمزة (ؤ/ئ = ء)، فتُلتقط صور التشكيل والألف والياء والتاء المربوطة والهمزة كلها. وصور اللواصق: الكلمات من 4 حروف فأكثر مع أي لاصقة، والكلمات من 3 حروف بعد ال/وال/بال/فال/كال/لل فقط، والكلمات من حرفين لا تُطابق مع اللواصق أبدًا (فلا تحظر كس كلمتي ألكس وكسكس، ولا تحظر خرا كلمة فخرا).

</div>

## English

`itmam-ar.dict` is the dictionary shipped with Itmam. It is generated, not hand-written: every word
and frequency comes from a real corpus listed below. Nothing here is invented.

### Sources

| Name | What | License | Pinned version |
|---|---|---|---|
| `frequencywords` | [FrequencyWords](https://github.com/hermitdave/FrequencyWords) by Hermit Dave: word counts from the OpenSubtitles 2018 corpus ([OPUS](https://opus.nlpl.eu/OpenSubtitles/corpus/version/OpenSubtitles)), file `content/2018/ar/ar_full.txt`. Conversational / everyday Arabic. | Content: [CC-BY-SA-4.0](https://creativecommons.org/licenses/by-sa/4.0/) (code: MIT) | commit `525f9b560de45753a5ea01069454e72e9aa541c6`, SHA-256 `EBB36AEAE78E609ADC552B32EC91FF2490E1170F8B244D22EEF9952D2096D610` |

Planned: Arabic Wikipedia word counts (CC-BY-SA-4.0) for formal vocabulary, added after the MVP.

**The dictionary file `itmam-ar.dict` is a derived work of these sources and is distributed under
CC-BY-SA-4.0**, separately from the MIT-licensed code. See `THIRD_PARTY_NOTICES.md`.

### Rebuilding

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

### What the builder does

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

Current build: **522,510 words** (spellings), 1.66 MB; the blocklist removed 108 entries.

### Common phrases (`phrases.txt`)

Common Arabic phrases Itmam completes whole or word by word (greetings, remembrance, prayers, occasions, formal letters). Compiled by hand for Itmam and released under the MIT license like the code. One phrase per line, at least two words, without tashkeel; order matters, the more common phrase first within each category. Contributions are welcome.

### Blocklist

`blocklist.txt` lists words that are never suggested (one per line, `#` comments). Contributions
are welcome; rebuild the dictionary after editing it. Users can also block words themselves.

- **Sources:** 21 words selected by hand from LDNOOBW `ar` (CC-BY-4.0) and uxbertlabs'
  Arabic list (MIT); see `THIRD_PARTY_NOTICES.md`. A third candidate (AhmedRedaAmin/ArabicSlangProfanity)
  was rejected because it is written in Latin script (Arabizi).
- **Deliberately not blocked:** words from those lists that are ordinary or have a common
  non-offensive meaning, or are medical, legal/religious or news vocabulary: أمك، أختك، نيك (mostly
  the name Nick)، شاذ، قضيب، قواد، فرج، حلمه، خول، لبوة، مبادل، زق، بز، ثدي، شرج، بظر، احتلام، خنثى،
  جماع، شهوة، اغتصاب، لواط، سحاق، لعق، لحس، مص، تمص، بيضان.
- **Matching:** normalized form with hamza seats folded (ؤ/ئ = ء), so tashkeel, alef, ya,
  ta marbuta and hamza variants are all caught. Clitic forms: 4+ letter words with any clitic,
  3-letter words only after ال/وال/بال/فال/كال/لل, 2-letter words never (so كس does not block ألكس
  or كسكس, and خرا does not block فخرا).
