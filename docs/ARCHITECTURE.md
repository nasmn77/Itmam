# بنية إتمام | Itmam architecture

[العربية](#العربية) · [English](#english)

<div dir="rtl">

## العربية

تصف هذه الوثيقة كل مسار في الشيفرة يتعامل مع إدخال لوحة المفاتيح أو مع البيانات المحفوظة، حتى يمكن مراجعة التطبيق من ناحية الخصوصية. تُحدَّث في نهاية كل مرحلة. المخططات مكتوبة بأسماء الأصناف والدوال كما هي في الشيفرة.

### المشاريع

| المشروع | الدور | واجهات Windows |
|---|---|---|
| `Itmam.Core` | المنطق الخالص: التطبيع، والقاموس، والترتيب، ومخزن الكلمة، وتصنيف المفاتيح، والتعلّم، والإعدادات | لا شيء (`net10.0`) |
| `Itmam.Win32` | الخطافات، واكتشاف لغة لوحة المفاتيح، وموضع المؤشر، والإدراج، والحمايات | نعم، عبر CsWin32 |
| `Itmam.App` | تطبيق WinForms: أيقونة قرب الساعة، وشريط الاقتراحات، ونافذة الإعدادات | WinForms |
| `tools/DictionaryBuilder` | بناء `data/itmam-ar.dict` دون اتصال | لا شيء |
| `tools/IconGenerator` | توليد أيقونة التطبيق `src/Itmam.App/Assets/Itmam.ico` | لا شيء |

### مسار الإدخال

```
 physical key / click / focus change
        │
        ▼
 Itmam.Win32.InputHookService  ── dedicated thread "Itmam input hooks" with its own message loop
   • WH_KEYBOARD_LL  → KeyboardProc
   • WH_MOUSE_LL     → MouseProc
   • SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_FOCUS, out-of-context, skip own process)
        │  callbacks only: filter → classify → translate → enqueue / swallow; always CallNextHookEx
        ▼
 Channel<InputRecord>  (unbounded, single writer, single reader; in memory only)
        │
        ▼
 Itmam.Core.Input.InputPipeline (worker task) → InputTracker → WordBuffer
        │            │
        │            └─ caret moved into existing text: Itmam.Win32.UiaCaretContextSource
        │               (reads ≤ 49 characters before the caret, see "Caret context")
        ▼
 SuggestionController → SuggestionPopup (UI thread)
```

#### ما الذي تفعله دالة لوحة المفاتيح

1. تتجاهل الأحداث المُرسَلة برمجيًا (`LLKHF_INJECTED`)، فالنص الذي يكتبه إتمام نفسه عبر `SendInput` لا يعود إليه أبدًا. تعالج ضغط المفتاح (`WM_KEYDOWN` / `WM_SYSKEYDOWN`)، وإفلاته فقط لسببين: إخفاء إفلات مفتاح ابتُلع ضغطه، واختصار المفاتيح المعدِّلة وحدها (Ctrl+Alt).
2. تقرأ حالة المفاتيح المعدِّلة (`GetAsyncKeyState`، `LLKHF_ALTDOWN`)، ثم تقرر عبر `HotkeyInterceptor` هل المفتاح اختصار لإتمام (انظر «قبول اقتراح»)، ثم تصنّف المفتاح عبر `KeyClassifier` (منطق خالص مختبَر): المفاتيح المعدِّلة تُتجاهل، وBackspace وCtrl+Backspace وEsc والتنقل وDelete واختصارات Ctrl/Alt/Win واللصق تصبح أحداثًا.
3. مفاتيح الكتابة تُترجم بـ `ToUnicodeEx` بلغة النافذة التي لها تركيز لوحة المفاتيح (`KeyboardLayouts.ForegroundLayout` عبر `GetGUIThreadInfo`، لتصحّ في تطبيقات UWP والطرفيات)، مع العلَم `0x4` حتى لا تتغير حالة لوحة المفاتيح في التطبيق المستهدف ولا المفاتيح الميتة.
4. تكتب حدثًا (النوع، واللغة، والوقت) في القناة وتعود. لا تخصيص ذاكرة لكل ضغطة سوى خانة القناة، ولا انتظار. لا يُبتلع إلا اختصار لإتمام والشريط ظاهر، أو اختصار التشغيل والإيقاف إن كان بمفتاح.

دالة الفأرة تبلّغ عن النقرات فقط (وتسجّل أن الخطاف حيّ مع كل حركة). وأحداث WinEvent تبلّغ عن تغيّر النافذة الأمامية والتركيز.

#### تتبّع الكلمة

`InputTracker` يعيد ضبط الكلمة عند تغيّر لغة لوحة المفاتيح، ولا يتتبع شيئًا إطلاقًا ما دامت اللغة غير عربية (اللغة الأساسية `0x01` في الكلمة الدنيا من HKL)، فالكتابة بالإنجليزية لا تُخزَّن أبدًا. الأحداث التي تأتي بلغة «غير معروفة» (القيمة 0، مثل إعادة تشغيل إتمام) تُبقي اللغة الحالية. و`WordBuffer` يطبّق قواعد `PROMPT.md` §4 ولا يُصدر *Committed* إلا عند حدّ كلمة.

#### سياق المؤشر (انتقل المؤشر إلى نص موجود)

كلما احتمل أن المؤشر صار داخل نص لم يرَ التطبيق كتابته — Backspace على كلمة فارغة، أو نقرة، أو الأسهم وHome وEnd وDelete، أو تغيّر التركيز أو النافذة أو اللغة، أو اللصق، أو اختصار، أو إعادة تشغيل إتمام — يصبح `WordBuffer` *منفصلًا*: تُتجاهل الحروف، فلا تُقترح كلمات خاطئة من كلمة رآها نصفها.

ليستأنف بسرعة، ينتظر `InputPipeline` ‏40 ملّي ثانية بعد آخر حدث من هذا النوع (ليكون التطبيق قد طبّقه؛ وإبقاء مفتاح مضغوطًا يقرأ مرة واحدة)، ثم يطلب من `UiaCaretContextSource` النص الذي قبل المؤشر:

1. `IUIAutomation.GetFocusedElement()`؛ وإذا كان `CurrentIsPassword` فلا يُقرأ شيء.
2. نطاق المؤشر من `TextPattern2.GetCaretRange`، أو التحديد (الفارغ) من `TextPattern`.
3. تُمدّ نسخة من النطاق **إلى الخلف بما لا يزيد عن 49 حرفًا** ويُستدعى `GetText(49)`.

لا يُقرأ شيء في التطبيقات المستثناة (`Itmam.Core.Input.ExcludedApps`؛ افتراضيًا: سطح المكتب البعيد والأجهزة الافتراضية والطرفيات ومشغّلات الألعاب). يُتحقق من ذلك مرتين: عبر `InputPipelineOptions.CanReadContext` على العملية الأمامية قبل كل قراءة، وعبر `UiaCaretContextSource` على العملية المالكة لعنصر UI Automation المركَّز قبل القراءة مباشرة.

`WordBuffer.ResumeFromContext` يحتفظ من هذا النص بالكلمة العربية الأخيرة فقط (الكلمة التي فيها المؤشر) كلمةً حالية، أو يستأنف بكلمة فارغة إذا سبق المؤشرَ حدُّ كلمة أو بداية الحقل. ويُتجاهل باقي النص فورًا؛ لا شيء يُحفظ أو يُسجَّل. وما دام هناك نص محدد — بلوحة المفاتيح (Shift مع التنقل، وCtrl+Shift+الأسهم، وCtrl+A: حدث `Selection` لا يُقرأ بعده شيء) أو بالفأرة (يبلّغ UI Automation عن تحديد غير فارغ) — لا يُتتبع شيء ولا يُقترح، لأن الكتابة تستبدل التحديد؛ ويستأنف بعد أول حركة مؤشر عادية أو نقرة أو حدّ كلمة. وإذا وصلت مفاتيح أثناء القراءة تُهمل النتيجة ويُقرأ النص من جديد بعد أن تهدأ (فيشملها). وإذا لم يدعم التطبيق نصوص UI Automation (فشل الاستدعاء أو تجاوز 250 ملّي ثانية، على خيط مخصص، ثم مرة أخرى بعد 100 ملّي ثانية): بعد حركة مؤشر يستأنف بكلمة فارغة إن لم تُكتب حروف في الأثناء؛ وبعد Backspace أو إن كُتبت حروف ينتظر حدّ الكلمة التالي.

#### المتانة

- كل دالة استدعاء محاطة بـ `try/catch`؛ الاستثناء يزيد عدّادًا ويُمرَّر الإدخال كما هو.
- يزيل Windows بصمت أي خطاف منخفض المستوى يتجاوز `LowLevelHooksTimeout`. كل ثانيتين يقارن خيط الخطافات `GetLastInputInfo` بآخر مرة عملت فيها خطافاته؛ وإذا رأى النظام إدخالًا فاتته الخطافات لأكثر من 1.5 ثانية، يُعاد تثبيت الخطافين (مرة واحدة كل 10 ثوانٍ على الأكثر).
- مندوبات الخطافات محفوظة في حقول حتى لا يجمعها جامع القمامة وهي مثبّتة.
- قبل التثبيت يُشغَّل كل مسار للضغطات مرة واحدة (إحماء)، فلا يحدث JIT ولا توليد P/Invoke داخل استدعاء حقيقي.
- هدف زمن دالة لوحة المفاتيح: **p99 أقل من 1 ملّي ثانية، والأقصى أقل من 5**. خيط الخطافات بأولوية `AboveNormal`، وخيط الإدخال الأمامي محفوظ (`ForegroundLayoutCache`)، فتكلّف الضغطة استدعاء `GetForegroundWindow` واستدعاء `GetKeyboardLayout`. تُقرأ اللغة مع كل مفتاح لأن تبديلها لا يغيّر النافذة ولا يرسل حدثًا نستقبله.
- إذا لم يتوفر سياق المؤشر يُعاد المحاولة مرة بعد 100 ملّي ثانية: Chromium وElectron يفعّلان شجرة الوصول عند أول طلب UI Automation.
- جمع القمامة يوقف كل الخيوط المُدارة ومنها خيط الخطافات. يعمل التطبيق بوضع `GCLatencyMode.SustainedLowLatency`، ويتجنب الخيط العامل التخصيص لكل حدث؛ ونافذة التشخيص (في نسخ Debug فقط) تبلّغ عن الاستدعاءات الأبطأ من 500 ميكروثانية وكم منها تزامن مع جمع القمامة.

### شريط الاقتراحات

```
 InputPipeline.Updated (pipeline thread)
        │  Arabic layout, not detached, ≥ MinChars letters, not silenced, Itmam enabled
        ▼
 SuggestionEngine.Suggest(word)            (Core; < 0.1 ms with the prefix cache; under LearningService.SyncRoot)
        │
        ▼
 SuggestionController (one request at a time; stale keystrokes are dropped)
        │  CaretLocator.LocateAsync:
        │    1. GetGUIThreadInfo → rcCaret + ClientToScreen          (caller thread)
        │    2. UI Automation caret range → GetBoundingRectangles    (UiaWorker, 150 ms timeout)
        │       empty range → rectangle of the previous character (caret on its left in RTL)
        │    3. MSAA AccessibleObjectFromWindow(OBJID_CARET).accLocation
        │    4. focused field's bounds (UIA CurrentBoundingRectangle, field-sized only): its start edge
        │    5. mouse pointer (bar shown below it)
        ▼
 SuggestionPopup (UI thread)
    PopupPlacement: above the caret, right edge at the caret (RTL), below if no room,
    clamped to the caret monitor's work area; sizes scaled by that monitor's DPI.
```

يُقرأ موضع المؤشر لحظة ضغط المفتاح، أي قبل أن يُدرج التطبيق الحرف. لذلك إن لم يصل مفتاح جديد خلال 60 ملّي ثانية، يُقرأ الموضع مرة أخرى ويُنقل الشريط إن تحرك المؤشر؛ فالحقل الفارغ ذو الاتجاه التلقائي (مثل بحث Google) يتحول إلى اليمين لليسار مع أول حرف عربي فيقفز مؤشره إلى الجهة الأخرى.

الشريط لا يأخذ التركيز أبدًا: `ShowWithoutActivation`، و`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`، و`WM_MOUSEACTIVATE → MA_NOACTIVATE`، ويُعرض بـ `SetWindowPos(SWP_NOACTIVATE)`. شكله الافتراضي (ارتفاع 35 بكسل، وخط Segoe UI بحجم 14، و15 بكسل حول كل كلمة، بلا فواصل، و`#2C2C2C` مع نص أبيض في الوضع الداكن) مقيس من لقطة لشريط Windows 11 الأصلي، والنص بتنعيم رمادي مثله. يمكن تغيير الألوان والعتامة والحجم من صفحة «المظهر».

تكلفة UI Automation في Chromium (قيست على نسخة Chrome معزولة بصفحة صغيرة): الطلب الأول لا يجد TextPattern بينما يفعّل Chrome شجرة الوصول، والثاني (بعد 100 ملّي ثانية) ينجح؛ الذاكرة الخاصة 370 ← 354 ميغابايت، والمعالج في الخمول 1.9% ← 0.1% (لا زيادة ملموسة؛ الأرقام تغلب عليها نهاية تشغيل Chrome). الصفحات الكبيرة تبني أشجارًا أكبر.

### قبول اقتراح

```
 key-down in KeyboardProc (hook thread)
        │  HotkeyInterceptor.OnKeyDown(vk, modifiers, PopupState.Current)   (Core, pure)
        │    bar hidden            → Pass (every key reaches the app untouched)
        │    Tab                   → Swallow + AcceptSuggestion(0, word)
        │    Ctrl+1…9 (≤ shown)    → Swallow + AcceptSuggestion(n, word)
        │    Esc                   → Swallow + DismissSuggestions
        │    native navigation (on by default): Up enters, ←/→ select, Enter accepts, Esc/↓ leave,
        │                          Ctrl+Delete forgets the selected learned word
        │  a swallowed key's key-up is swallowed too
        │  key-up: Ctrl+Alt released alone (always) → ToggleEnabled (nothing swallowed)
        ▼
 Channel<InputRecord> → InputPipeline (pipeline thread)
        AcceptSuggestion: TextEdit.Compute(typed, word, appendSpace) → ITextInjector.Send
                          → WordBuffer.AcceptWord (Committed: counts as used for learning)
        DismissSuggestions: suggestions suppressed until the word ends
        ▼
 SendInputInjector: one SendInput batch — Ctrl key-up if Ctrl is physically held (Ctrl+N),
 n × VK_BACK, VK_DELETE when a following space is replaced, then each UTF-16 unit as
 KEYEVENTF_UNICODE (+ space). Injected events carry LLKHF_INJECTED, so our hook ignores them.
```

المسافات: قراءة سياق المؤشر تعيد أيضًا الحرف الواحد الذي بعد المؤشر. يبقى صحيحًا أثناء الكتابة (النص يُدرج قبله) ويصبح مجهولًا عند تحرّك المؤشر. عند القبول، تُستبدل المسافة التالية (Delete ثم الكلمة والمسافة) فلا تتكرر مسافتان، ولا توضع مسافة قبل علامة ترقيم تالية، وفي غير ذلك تُضاف مسافة.

الكلمة في `AcceptSuggestion` مأخوذة من لقطة الشريط التي رآها الخطاف، فتُدرج الكلمة الظاهرة على الشاشة بالضبط. ينشر خيط الواجهة `PopupState` مع كل إظهار وإخفاء، ويقرؤه الخطاف دون قفل. النقر على كلمة في الشريط يرسل الحدث نفسه، والنقرات داخل حدود الشريط لا تُعدّ حركة مؤشر. وأثناء الإيقاف (Ctrl+Alt افتراضيًا) لا تنشر الخطافات شيئًا ولا تبتلع شيئًا، إلا اختصار التشغيل إن كان بمفتاح.

الاختصارات قابلة للتغيير من صفحة «الاختصارات»، ويتحقق `HotkeyRules` (في Core) منها قبل الحفظ: يرفض ما يكتب نصًا، ومفاتيح التحرير، واختصارات Windows الشائعة، والتكرار، والتعارض مع «المفتاح + رقم». وأثناء تسجيل اختصار يعطي `ItmamApp.SuspendHotkeys` الخطافَ إعدادات فارغة، فتصل المفاتيح إلى نافذة الإعدادات بدل أن تعمل على إتمام.

### التعلّم والتخزين

```
 WordBuffer: word ended by a boundary, or accepted from the bar  → Committed
        ▼
 InputPipeline: the word is *pending*. The next event makes it final (PipelineUpdate.WordToLearn),
                except Backspace/Ctrl+Backspace right after it (the user is correcting it → dropped).
                On exit/log-off the pending word is final.
        ▼
 LearningService.Learn(word)    (Core)
    skip if paused, < 2 letters or not Arabic; tashkeel removed
    skip if on data/blocklist.txt and not already added by the user (manual adds are respected)
    UserWordIndex.RecordUse → count + 1, last-used = now
        ▼  (2 s after the last change, and on exit / log-off)
 SqliteUserWordStore  %LOCALAPPDATA%\Itmam\user.db   (MSIX: redirected to the package's private folder)
    table words(word TEXT PRIMARY KEY, count INTEGER, last_used INTEGER, blocked INTEGER)
    WAL journal: committed transactions survive crashes. A corrupt file is renamed
    user.db.corrupt-<timestamp> (kept) and a fresh database is created.
```

ما يُحفظ لكل كلمة: الكلمة، وعدد مرات استعمالها، ووقت آخر استعمال، وهل حظرها المستخدم. لا جمل أبدًا، ولا اسم التطبيق الذي كُتبت فيه، ولا أي شيء كُتب في نافذة صامتة. الكلمة المتعلَّمة تُقترح بعد `MinUsesToSuggest` استعمالًا (افتراضيًا 2). صفحة «كلماتي» تستخدم `LearningService`: إضافة، وتعديل، وحذف، وحظر وإلغاؤه، واستيراد وتصدير (`word<TAB>count`، و`!word` للمحظورة)، وإيقاف مؤقت، وحذف الكل؛ والحظر وإلغاؤه والحذف تطلب تأكيدًا. وفي الشريط، النقر بالزر الأيمن يعرض في الشريط نفسه أزرارًا بدل الاقتراحات، دون أن يأخذ التركيز (من الإصدار 1.1: «حذف» للكلمة أو العبارة المتعلَّمة، و«إضافة إلى عباراتي» للعبارات)، وCtrl+Delete أثناء الاختيار بالأسهم ينسى كلمة متعلَّمة (`ForgetSuggestion` ← `LearningService.Remove`)، ويتعلّمها من جديد إن استُعملت. الحظر الدائم لا يُتاح إلا في «كلماتي».

أمان التزامن: خيط المعالجة يتعلّم الكلمات بينما تعدّلها «كلماتي» من خيط الواجهة. كل تغيير يمر عبر `LearningService` تحت قفله (`SyncRoot`)؛ و`SuggestionController` يمسك القفل نفسه بينما يقرأ `SuggestionEngine` الفهرس؛ والصفحة تعرض نسخة (`LearningService.Snapshot`).

### العبارات المتلازمة (من الإصدار 1.1)

`PhraseIndex` (في Core) يحمّل `data/phrases.txt` و«عباراتي» من الإعدادات (عبارات المستخدم أولًا). ويحتفظ `InputPipeline` في `RecentWords` بآخر 8 كلمات أنهتها مسافة، مطبّعةً وفي الذاكرة فقط، ويمسحها عند أي علامة غير المسافة وعند أي حدث غير الكتابة (Backspace، حركة المؤشر، تغيير التركيز أو اللغة…) وفي النوافذ الصامتة؛ وترسلها `PipelineUpdate.RecentWords`. يقترح `SuggestionController` أولًا بقية العبارة التي يكتبها المستخدم (يُدرجها Tab كاملة) ثم الكلمة التالية فيها، ثم كلمات القاموس. ويظهر الشريط بعد المسافة مباشرة إن كانت هناك كلمة تالية. ولكي يبقى Tab متوقعًا: لا تبدأ عبارة من كلمة شائعة قصيرة (في، على، من…)، ولا تُقترح عند البدء إلا عبارة واحدة حتى تبقى كلمات القاموس في الشريط، وتُعتمد العبارات المطابقة لأكبر عدد من الكلمات فقط. القبول يُدرج العبارة دفعة واحدة، والقبول بلا حروف مكتوبة مسموح.

### التراجع عن القبول (من الإصدار 1.1)

بعد إدراج اقتراح يحفظ `InputPipeline` في الذاكرة ما كُتب قبله وما أُدرج (`UndoableAccept`) ويرفع `PopupState.CanUndoAccept`. في الخطاف، Backspace بلا معدِّلات والعلم مرفوع يُبتلع ويصبح `UndoAccept`، وأي مفتاح آخر (غير المعدِّلات) أو نقرة يُنزل العلم، وكذلك الحدث التالي في المسار. يحذف المسار ما أُدرج ويكتب ما كان قد حُذف دفعة واحدة، ويعيد الكلمة والكلمات المتتالية كما كانت، ولا يتعلّم الكلمة المقبولة ولا تسلسلاتها. وإن لم يبقَ ما يُتراجع عنه يرسل Backspace عاديًا، فلا تضيع الضغطة. لا تراجع حين استُبدلت مسافة بعد المؤشر (`DeleteForward`).

### الاختصارات النصية (من الإصدار 1.1)

`TextShortcuts` (في Core) يحمل اختصارات المستخدم من `settings.json` (مثل «ع1» ← «السلام عليكم ورحمة الله وبركاته»). الاختصار حرف عربي ثم حروف عربية أو أرقام؛ ولذلك يُبقي `WordBuffer` الأرقام التي تأتي بعد حرف داخل الكلمة (الرقم في أول الكلمة ما زال حدًّا)، ولا تُتعلَّم كلمة فيها رقم. يضع `SuggestionController` عبارة الاختصار المكتوب بالضبط أولًا في الشريط، مهما كان الحد الأدنى للحروف، ويضعها Tab مكان الاختصار بالطريقة نفسها التي تُقبل بها الاقتراحات. المطابقة تتجاهل التشكيل والهمزات، والأرقام الهندية تساوي الأرقام العربية الغربية.

### العبارات المتعلَّمة (من الإصدار 1.1، قرار المستخدم: مفعّلة افتراضيًا)

حين تنتهي كلمة، يكوّن `RecentWords.SequencesEndingWith` التسلسلات من كلمتين إلى 5 التي تنتهي بها (بالتهجئة المكتوبة، بلا تشكيل). وتبقى معلّقة مع الكلمة: إن تلاها Backspace أُسقطت معها، وإلا أرسلتها `PipelineUpdate.PhrasesToLearn`. ثم يعدّها `LearningService.LearnPhrases` في جدول `phrases` في `user.db` (المفتاح المطبّع ← آخر تهجئة، والعدد، ووقت آخر كتابة). وما بلغ 3 مرات يصير عبارة متعلَّمة. لا يُعدّ شيء أثناء إيقاف التعلّم أو إطفاء «تعلّم العبارات»، ولا ما فيه كلمة أقل من حرفين أو كلمة محظورة، ولا شيء في النوافذ الصامتة (لا يرى المسار فيها شيئًا). التنظيف عند البدء ومرة كل يوم: يُحذف ما لم يُتعلَّم ولم يُكتب منذ 30 يومًا. والحدود 20,000 تسلسل غير متعلَّم و2,000 عبارة متعلَّمة، ويُحذف الأقدم استعمالًا أولًا. `LearnedPhrases()` يرتّبها بالأكثر استعمالًا، ويترك العبارة التي تحتويها عبارة متعلَّمة أطول مكتوبة بقدرها تقريبًا (¾ عددها على الأقل). يضع `ItmamApp.UpdatePhrases` في `PhraseIndex` «عباراتي» ثم المتعلَّمة ثم المضمّنة، ويعيد بناءه عند `LearnedPhrasesChanged` على خيط المسار قبل حساب الشريط. والنقر بالزر الأيمن على عبارة في الشريط ينساها (`ForgetPhrase`). وفي صفحة «العبارات»: القائمة، والحذف، والنقل إلى «عباراتي»، و«حذف العبارات المتعلَّمة» (مع كل التسلسلات المعدودة). وحين يُقرأ السياق قبل المؤشر (بعد حدث تركيز أو نقرة…) يعيد `RecentWords.ResumeFrom` بناء الكلمات المتتالية من النص المقروء، فلا تضيع عبارة كُتبت قبل اكتمال القراءة.

### أين يصمت إتمام

يُفحص `ForegroundGuard` (في Win32، وينفّذ `IInputGuard` من Core) كلما احتمل تغيّر النافذة أو الحقل أو المؤشر. وما دام يبلّغ عن سبب، لا يتتبع خط المعالجة شيئًا ولا يقرأ ولا يعرض ولا يتعلّم:

| السبب | طريقة الاكتشاف |
|---|---|
| حقل كلمة مرور | `ES_PASSWORD` على عنصر Edit مركَّز؛ و`IsPassword` من UI Automation عند قراءة سياق المؤشر |
| تطبيق بصلاحيات مسؤول | مستوى السلامة الإلزامي للعملية الأمامية أعلى من مستوانا (أو تعذّرت قراءته) |
| تطبيق مستثنى | اسم العملية في `ExcludedApps` |
| ملء الشاشة | `SHQueryUserNotificationState`: ملء شاشة حصري لـ Direct3D أو وضع العرض التقديمي |
| محررات الإدخال واللغات الأخرى | لا يُتتبع إلا تخطيط لوحة مفاتيح عربي |

### التطبيق قرب الساعة والإعدادات

- `Program`: نسخة واحدة فقط عبر الكائن `Local\Itmam.SingleInstance`؛ التشغيل الثاني يستدعي `AllowSetForegroundWindow(ASFW_ANY)` ويرسل الإشارة `Local\Itmam.ShowSettings`، فتفتح النسخة العاملة الإعدادات في المقدمة، ثم يخرج. لغة الواجهة (عربية/إنجليزية) من الإعدادات.
- `ItmamApp` (جذر التركيب) يبني كل المكوّنات، ويطبّق `AppSettings` فورًا (الاختصارات، وعدد الاقتراحات، والمسافة بعد القبول، والتعلّم، والتطبيقات المستثناة، ومظهر الشريط)، ويُغلق مرة واحدة: إزالة الخطافات ← إيقاف خط المعالجة ← تعلّم الكلمة المعلّقة ← حفظ user.db وإغلاقه ← التخلص من الموارد.
- `TrayApplication` (`ApplicationContext`، بلا نافذة رئيسية، ولا شيء في شريط المهام أو Alt+Tab): قائمة الأيقونة «مفعّل» · «إيقاف مؤقت لساعة» · «الإعدادات» · «حول» · («التشخيص» في نسخ Debug) · «خروج». الخروج وتسجيل الخروج من Windows (`SystemEvents.SessionEnding`) يمرّان بالإغلاق النظيف نفسه ويزيلان الأيقونة. وتعيد NotifyIcon إضافة نفسها عند إعادة تشغيل Explorer (TaskbarCreated).
- الإعدادات: `%LOCALAPPDATA%\Itmam\settings.json` (‏`SettingsStore`: ملف مؤقت ثم استبدال؛ الملف التالف يُحفظ باسم `settings.json.corrupt-*` وتُستخدم القيم الافتراضية). تفضيلات فقط، بلا أي نص مكتوب.
- المظهر: `PopupAppearance.From(AppSettings)` ← `PopupColors.For(appearance)` (ألوان السمة، أو خلفية مخصصة مع نص أسود أو أبيض يُختار بتباين WCAG عبر `ColorContrast`). `BarRenderer` يرسم عناصر الشريط الحقيقي والمعاينة الحية في صفحة «المظهر» معًا. العتامة بنافذة طبقية (`Form.Opacity`) لا تفعّل الشريط ولا تُظهره.
- التشغيل مع Windows (`StartupRegistration`): ‏`StartupTask` باسم "ItmamStartup" في حزمة MSIX (يُطابَق مع الإعداد عند البدء)؛ و`HKCU\…\Run` دون حزمة، فقط حين يفعّله المستخدم من الإعدادات (نسخ التطوير لا تُسجَّل تلقائيًا أبدًا).
- التحديثات (من الإصدار 1.1، قرار المستخدم): بعد 12 ثانية من البدء، وإن كان «البحث عن التحديثات» مفعّلًا، يستدعي `StoreUpdates.CheckAsync` الدالة `StoreContext.GetAppAndOptionalStorePackageUpdatesAsync`. خدمة المتجر في Windows هي التي تتصل، والحزمة بلا صلاحية `internetClient`. إن وُجد تحديث يظهر إشعار يفتح النقرُ عليه `ms-windows-store://pdp/?productid=9P08F7KGRJB0`. لا يعمل إلا في نسخة المتجر؛ ونسخ التطوير والتجربة تعطي «غير متاح». صفحة «التحديثات» تعرض الإصدار وتبحث عند الطلب.
- الإشعارات: عند تغيّر حالة التشغيل (القائمة، أو اختصار التشغيل، أو الإيقاف المؤقت وانتهاؤه) يظهر إشعار من الأيقونة، ما دام «إشعار عند إيقاف إتمام واستئنافه» مفعّلًا. الخروج من القائمة أو من الإعدادات يطلب تأكيدًا؛ وتسجيل الخروج من Windows لا يطلبه.

### ضمانات الخصوصية في الشيفرة

- الحروف المكتوبة توجد في الذاكرة فقط: في القناة، وفي `WordBuffer`، وعلى الشاشة.
- النص الوحيد الذي يُقرأ من التطبيقات الأخرى هو ما لا يزيد عن 49 حرفًا قبل المؤشر، بعد انتقال المؤشر إلى نص موجود، وأبدًا من حقول كلمات المرور أو التطبيقات المستثناة؛ ولا يُحتفظ إلا بالكلمة الحالية.
- آخر 8 كلمات متتالية (للعبارات) تبقى في الذاكرة فقط وتُنسى عند أي حركة للمؤشر. ولا يُحفظ من ترتيب الكلمات إلا تسلسلات من كلمتين إلى 5 مع عددها (العبارات المتعلَّمة، قابلة للإطفاء)، وما لم يتكرر يُحذف بعد 30 يومًا.
- السؤال الوحيد الذي يخرج من الجهاز هو سؤال متجر Microsoft عن وجود إصدار أحدث (`StoreUpdates`، قابل للإطفاء)، ولا يحمل شيئًا عن المستخدم أو عما يكتبه.
- لا يكتب أي مسار في الشيفرة حروفًا في سجل أو ملف أو شبكة. قائمة الأحداث في نافذة التشخيص تعرض *أنواع* الأحداث وأزمنتها فقط (الحروف تظهر بصيغة `Character (letter)`).
- تشخيص الخطافات (`HookDiagnostics`) عدّادات وأزمنة فقط.
- الكلمات المتعلَّمة تُحفظ بصيغة «الكلمة ← العدد / آخر استعمال» فقط (انظر «التعلّم والتخزين»)، ولا تُنسخ إلى أي مكان إلا بتصدير يختاره المستخدم إلى ملف يختاره.

</div>

## English

This document describes every code path that touches keyboard input or stored data, so the
app can be audited for privacy. It is updated at the end of each phase.

### Projects

| Project | Role | Windows APIs |
|---|---|---|
| `Itmam.Core` | Pure logic: normalization, dictionary, ranking, word buffer, key classification, learning, settings | none (`net10.0`) |
| `Itmam.Win32` | Hooks, keyboard layout detection, caret location, text injection, guards | yes, via CsWin32 |
| `Itmam.App` | WinForms app: tray icon, suggestion bar, settings window | WinForms |
| `tools/DictionaryBuilder` | Offline build of `data/itmam-ar.dict` | none |
| `tools/IconGenerator` | Generates the app icon `src/Itmam.App/Assets/Itmam.ico` | none |

### Input path

```
 physical key / click / focus change
        │
        ▼
 Itmam.Win32.InputHookService  ── dedicated thread "Itmam input hooks" with its own message loop
   • WH_KEYBOARD_LL  → KeyboardProc
   • WH_MOUSE_LL     → MouseProc
   • SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_FOCUS, out-of-context, skip own process)
        │  callbacks only: filter → classify → translate → enqueue / swallow; always CallNextHookEx
        ▼
 Channel<InputRecord>  (unbounded, single writer, single reader; in memory only)
        │
        ▼
 Itmam.Core.Input.InputPipeline (worker task) → InputTracker → WordBuffer
        │            │
        │            └─ caret moved into existing text: Itmam.Win32.UiaCaretContextSource
        │               (reads ≤ 49 characters before the caret, see "Caret context")
        ▼
 SuggestionController → SuggestionPopup (UI thread)
```

#### What the keyboard callback does

1. Ignores injected events (`LLKHF_INJECTED`), so text the app types itself (`SendInput`) never
   feeds back. It handles key-down (`WM_KEYDOWN` / `WM_SYSKEYDOWN`), and key-up only to hide the
   release of a swallowed key and for a modifiers-only shortcut (Ctrl+Alt).
2. Reads modifier state (`GetAsyncKeyState`, `LLKHF_ALTDOWN`), lets `HotkeyInterceptor` decide
   whether the key is an Itmam shortcut (see "Accepting a suggestion"), then classifies it with
   `Itmam.Core.Input.KeyClassifier` (pure, unit-tested): modifiers are ignored; Backspace,
   Ctrl+Backspace, Esc, navigation/Delete, Ctrl/Alt/Win shortcuts and paste become events.
3. Typing keys are translated with `ToUnicodeEx` using the layout of the window that has keyboard
   focus (`KeyboardLayouts.ForegroundLayout`, via `GetGUIThreadInfo` so UWP apps and consoles
   report the right layout), with flag `0x4` so the target app's keyboard state and dead keys are
   not changed.
4. Writes an event (kind, layout, timestamp) to the channel and returns. No allocation per
   keystroke apart from the channel slot; nothing blocks. Only an Itmam shortcut while the bar is
   shown, or a key-based on/off shortcut, is swallowed.

The mouse callback only reports button-down clicks (and records that the hook is alive on every
mouse move). WinEvents report foreground and focus changes.

#### Word tracking

`InputTracker` resets the word whenever the keyboard layout changes and tracks nothing at all
while the layout is not Arabic (primary language `0x01` of the HKL low word), so English typing is
never buffered. Events raised with an unknown layout (0, e.g. Itmam turned back on) keep the
current layout. `WordBuffer` applies the rules in `PROMPT.md` §4 and emits *Committed* only on a
word boundary.

#### Caret context (caret moved into existing text)

Whenever the caret may now be inside text the app did not see typed — Backspace on an empty
word, a mouse click, arrows/Home/End/Delete, a focus, foreground or layout change, paste, a
shortcut, or Itmam being turned back on — `WordBuffer` becomes *detached*: letters are ignored, so
no wrong suggestions are made from a half-seen word.

To resume quickly, `InputPipeline` waits 40 ms after the last such event (so the app has applied
it; holding a key probes only once) and asks `UiaCaretContextSource` for the text before the
caret:

1. `IUIAutomation.GetFocusedElement()`; if `CurrentIsPassword` → nothing is read.
2. Caret range from `TextPattern2.GetCaretRange`, or the (empty) selection from `TextPattern`.
3. A copy of the range is extended **backwards by at most 49 characters** and `GetText(49)` is called.

Nothing is read in excluded apps (`Itmam.Core.Input.ExcludedApps`; defaults: remote desktop/VMs,
terminals, game launchers). This is checked twice: by `InputPipelineOptions.CanReadContext`
against the foreground process before every read, and by `UiaCaretContextSource` against the
process that owns the focused UI Automation element right before reading.

`WordBuffer.ResumeFromContext` keeps only the trailing Arabic word of that text (the word the
caret is in) as the current word, or resumes with an empty word if a boundary or the start of the
field precedes the caret. The rest of the text is discarded immediately; nothing is stored or
logged. While text is selected — with the keyboard (Shift+navigation, Ctrl+Shift+arrows, Ctrl+A:
a `Selection` event, after which nothing is read) or with the mouse (UI Automation reports a
non-empty selection) — nothing is tracked or suggested, because typing replaces the selection;
the next plain caret move, click or boundary resumes. If keys arrive while reading, the result is
discarded and the text is read again once they settle (it then includes them). If the app has no
UI Automation text support (the call fails or times out after 250 ms, on a dedicated thread, and
once more after 100 ms): after a caret move, tracking resumes with an empty word if no letter was
typed meanwhile; after Backspace, or if letters were typed, it waits for the next boundary.

#### Robustness

- Every callback is wrapped in `try/catch`; an exception increments a counter and the input is
  still passed on.
- Windows silently removes a low-level hook that exceeds `LowLevelHooksTimeout`. Every 2 s the
  hook thread compares `GetLastInputInfo` with the last time its hooks ran; if the system saw
  input the hooks missed for > 1.5 s, both low-level hooks are reinstalled (at most once per 10 s).
- Hook delegates are held in fields so the GC cannot collect them while installed.
- Before installing, every keystroke code path is run once (warm-up) so JIT and P/Invoke stub
  generation never happen inside a real callback.
- Latency target for the keyboard callback: **p99 < 1 ms and max < 5 ms** (Windows removes a
  hook only after hundreds of ms). The hook thread runs at `AboveNormal` priority, and the
  foreground input thread is cached (`ForegroundLayoutCache`), so a keystroke costs one
  `GetForegroundWindow` and one `GetKeyboardLayout` call. The layout itself is read on every key
  because a layout switch changes no window and raises no event we receive.
- An unavailable caret context is retried once after 100 ms: Chromium/Electron enable their
  accessibility tree on the first UI Automation request.
- A garbage collection pauses all managed threads, the hook thread included. The app runs with
  `GCLatencyMode.SustainedLowLatency` and the worker avoids per-event allocations; the diagnostics
  window (Debug builds only) reports callbacks slower than 500 µs and how many of them overlapped a GC.

### Suggestion bar

```
 InputPipeline.Updated (pipeline thread)
        │  Arabic layout, not detached, ≥ MinChars letters, not silenced, Itmam enabled
        ▼
 SuggestionEngine.Suggest(word)            (Core; < 0.1 ms with the prefix cache; under LearningService.SyncRoot)
        │
        ▼
 SuggestionController (one request at a time; stale keystrokes are dropped)
        │  CaretLocator.LocateAsync:
        │    1. GetGUIThreadInfo → rcCaret + ClientToScreen          (caller thread)
        │    2. UI Automation caret range → GetBoundingRectangles    (UiaWorker, 150 ms timeout)
        │       empty range → rectangle of the previous character (caret on its left in RTL)
        │    3. MSAA AccessibleObjectFromWindow(OBJID_CARET).accLocation
        │    4. focused field's bounds (UIA CurrentBoundingRectangle, field-sized only): its start edge
        │    5. mouse pointer (bar shown below it)
        ▼
 SuggestionPopup (UI thread)
    PopupPlacement: above the caret, right edge at the caret (RTL), below if no room,
    clamped to the caret monitor's work area; sizes scaled by that monitor's DPI.
```

The caret is read when the key goes down, before the app inserts the letter. So if no newer key arrives within
60 ms, it is read again and the bar moves if the caret did: an empty field with automatic direction (Google
search) turns right-to-left on the first Arabic letter and its caret jumps to the other side.

The popup never takes focus: `ShowWithoutActivation`, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW |
WS_EX_TOPMOST`, `WM_MOUSEACTIVATE → MA_NOACTIVATE`, shown with `SetWindowPos(SWP_NOACTIVATE)`.
Its default look (35 px high, 14 px Segoe UI, 15 px padding per word, no dividers, `#2C2C2C` /
white in dark mode) was measured from a screenshot of the native Windows 11 bar; text uses
grayscale antialiasing like the native bar. Colors, opacity and size can be changed on the
Appearance page.

UI Automation cost in Chromium (measured on an isolated Chrome instance with a small page):
the first request finds no TextPattern while Chrome enables its accessibility tree, the second
(100 ms later) succeeds; private memory 370 → 354 MB and idle CPU 1.9 % → 0.1 % (no measurable
increase; numbers dominated by Chrome finishing start-up). Large pages build larger trees.

### Accepting a suggestion

```
 key-down in KeyboardProc (hook thread)
        │  HotkeyInterceptor.OnKeyDown(vk, modifiers, PopupState.Current)   (Core, pure)
        │    bar hidden            → Pass (every key reaches the app untouched)
        │    Tab                   → Swallow + AcceptSuggestion(0, word)
        │    Ctrl+1…9 (≤ shown)    → Swallow + AcceptSuggestion(n, word)
        │    Esc                   → Swallow + DismissSuggestions
        │    native navigation (on by default): Up enters, ←/→ select, Enter accepts, Esc/↓ leave,
        │                          Ctrl+Delete forgets the selected learned word
        │  a swallowed key's key-up is swallowed too
        │  key-up: Ctrl+Alt released alone (always) → ToggleEnabled (nothing swallowed)
        ▼
 Channel<InputRecord> → InputPipeline (pipeline thread)
        AcceptSuggestion: TextEdit.Compute(typed, word, appendSpace) → ITextInjector.Send
                          → WordBuffer.AcceptWord (Committed: counts as used for learning)
        DismissSuggestions: suggestions suppressed until the word ends
        ▼
 SendInputInjector: one SendInput batch — Ctrl key-up if Ctrl is physically held (Ctrl+N),
 n × VK_BACK, VK_DELETE when a following space is replaced, then each UTF-16 unit as
 KEYEVENTF_UNICODE (+ space). Injected events carry LLKHF_INJECTED, so our hook ignores them.
```

Spaces: the caret-context read also returns the one character after the caret. It stays valid
while typing (text is inserted before it) and becomes unknown when the caret moves. On accept,
a following space is replaced (Delete, then word + space) so there are never two spaces, following
punctuation gets no space before it, and otherwise a space is appended.

The word in `AcceptSuggestion` is captured from the bar snapshot the hook saw, so exactly the
word on screen is inserted. `PopupState` is published by the UI thread on every show/hide and
read lock-free by the hook. Clicking a word on the bar posts the same event; clicks inside the
bar's bounds are not treated as caret moves. While disabled (Ctrl+Alt by default), the hooks
publish nothing and swallow nothing, except a key-based on/off shortcut.

Shortcuts can be changed on the Shortcuts page; `HotkeyRules` (Core) checks them before saving and
refuses keys that type text, editing keys, common Windows shortcuts, duplicates and clashes with
"modifier + digit". While a shortcut is recorded, `ItmamApp.SuspendHotkeys` gives the hook an empty
`HotkeySettings`, so the keys reach the settings window instead of acting on Itmam.

### Learning and storage

```
 WordBuffer: word ended by a boundary, or accepted from the bar  → Committed
        ▼
 InputPipeline: the word is *pending*. The next event makes it final (PipelineUpdate.WordToLearn),
                except Backspace/Ctrl+Backspace right after it (the user is correcting it → dropped).
                On exit/log-off the pending word is final.
        ▼
 LearningService.Learn(word)    (Core)
    skip if paused, < 2 letters or not Arabic; tashkeel removed
    skip if on data/blocklist.txt and not already added by the user (manual adds are respected)
    UserWordIndex.RecordUse → count + 1, last-used = now
        ▼  (2 s after the last change, and on exit / log-off)
 SqliteUserWordStore  %LOCALAPPDATA%\Itmam\user.db   (MSIX: redirected to the package's private folder)
    table words(word TEXT PRIMARY KEY, count INTEGER, last_used INTEGER, blocked INTEGER)
    WAL journal: committed transactions survive crashes. A corrupt file is renamed
    user.db.corrupt-<timestamp> (kept) and a fresh database is created.
```

Stored per word: the word, how many times it was used, when it was last used, and whether the user
blocked it. Never sentences, the app it was typed in, or anything typed in a silenced
window. A learned word is suggested after `MinUsesToSuggest` uses (default 2). "My words" uses
`LearningService`: add, edit, remove, block/unblock, import/export (`word<TAB>count`,
`!word` = blocked), pause, delete all; blocking, unblocking and deleting ask for confirmation. On the bar, right-click shows actions in
the bar itself, without taking the focus (from 1.1: "Delete" for a learned word or phrase, "Add to My phrases" for phrases), and Ctrl+Delete while
navigating forgets a learned word (`ForgetSuggestion` →
`LearningService.Remove`); it is learned again if used. Blocking for good is only offered in "My words".

Thread safety: the pipeline thread learns words while "My words" edits them from the UI thread.
Every change goes through `LearningService` under its lock (`SyncRoot`); `SuggestionController`
holds the same lock while `SuggestionEngine` reads the index, and the page lists a copy
(`LearningService.Snapshot`).

### Common phrases (from 1.1)

`PhraseIndex` (Core) loads `data/phrases.txt` and "My phrases" from the settings (the user's first). `InputPipeline`
keeps in `RecentWords` the last 8 words ended by a space, normalized and in memory only, cleared at any other boundary,
any non-typing event (Backspace, caret move, focus or layout change…) and in silenced windows; `PipelineUpdate.RecentWords`
carries them. `SuggestionController` offers first the rest of the phrase being typed (Tab inserts it whole), then its next
word, then dictionary words; the bar can appear right after a space when a next word exists. To keep Tab predictable, no
phrase starts from a short common word (في، على، من…), only one whole phrase is offered at a phrase start so dictionary
words keep their place, and only the phrases matching the most words count. Accepting inserts the phrase in one batch,
and accepting with nothing typed yet is allowed.

### Undoing an accept (from 1.1)

After inserting a suggestion, `InputPipeline` keeps in memory what had been typed and what was inserted
(`UndoableAccept`) and raises `PopupState.CanUndoAccept`. In the hook, Backspace without modifiers while it is raised is
swallowed as `UndoAccept`; any other non-modifier key or a click lowers it, and so does the pipeline's next event. The
pipeline deletes what was inserted and types back what had been deleted in one batch, restores the word and the words in
a row, and learns neither the accepted word nor its sequences. If there is nothing left to undo it sends a plain
Backspace, so the key press is never lost. No undo when a space after the caret was replaced (`DeleteForward`).

### Text shortcuts (from 1.1)

`TextShortcuts` (Core) holds the user's shortcuts from `settings.json` (e.g. «ع1» → «السلام عليكم ورحمة الله وبركاته»). A key
is an Arabic letter followed by Arabic letters or digits, so `WordBuffer` keeps digits typed after a letter inside the
word (a digit at the start of a word is still a boundary), and a word containing a digit is never learned.
`SuggestionController` puts the text of a key typed exactly first in the bar, whatever the minimum letters setting, and
Tab inserts it in place of the key like any accepted suggestion. Matching ignores tashkeel and hamza seats, and
Arabic-Indic digits equal Western ones.

### Learned phrases (from 1.1, user decision: on by default)

When a word is finished, `RecentWords.SequencesEndingWith` forms the 2–5-word sequences ending with it (as typed,
without tashkeel). They stay pending with the word: dropped with it if Backspace follows, otherwise carried by
`PipelineUpdate.PhrasesToLearn`. `LearningService.LearnPhrases` counts them in the `phrases` table of `user.db`
(normalized key → spelling typed last, count, last time typed); 3 times makes a learned phrase. Nothing is counted while
learning is paused or "Learn phrases" is off, nothing with a word under 2 letters or a blocked word, and nothing in
silenced windows (the pipeline sees nothing there). Clean-up at start and once a day: a sequence not learned and not
typed for 30 days is deleted; limits are 20,000 sequences not learned and 2,000 learned phrases, the least recently
typed going first. `LearnedPhrases()` orders them by use and leaves out a phrase contained in a longer learned one typed
nearly as often (at least ¾ of its count). `ItmamApp.UpdatePhrases` builds the `PhraseIndex` from "My phrases", then the
learned ones, then the shipped list, and rebuilds it on `LearnedPhrasesChanged` on the pipeline thread before the bar is
recomputed. Right-click on a phrase in the bar forgets it (`ForgetPhrase`). The "Phrases" page lists them: delete,
move to "My phrases", and "Delete learned phrases" (with every sequence counted). When the context before the caret is
read (after a focus event, a click…), `RecentWords.ResumeFrom` rebuilds the words in a row from that text, so a phrase
typed before the read finished still counts.

### Where Itmam is silent

`ForegroundGuard` (Win32, implements Core `IInputGuard`) is checked whenever the window, field or
caret may have changed. While it reports a reason, the pipeline tracks, reads, shows and learns
nothing:

| Reason | Detection |
|---|---|
| Password field | `ES_PASSWORD` on a focused Edit control; UI Automation `IsPassword` when the caret context is read |
| Elevated app | Mandatory integrity level of the foreground process above ours (or unreadable) |
| Excluded app | Process name in `ExcludedApps` |
| Full screen | `SHQueryUserNotificationState`: Direct3D exclusive full screen or presentation mode |
| IME / other languages | Only Arabic keyboard layouts are ever tracked |

### Tray app and settings

- `Program`: single instance via the `Local\Itmam.SingleInstance` mutex; a second launch calls
  `AllowSetForegroundWindow(ASFW_ANY)` and signals `Local\Itmam.ShowSettings`, so the running copy
  opens Settings in front, then exits. The UI culture (ar/en) comes from the settings.
- `ItmamApp` (composition root) builds every component, applies `AppSettings` live (hotkeys,
  suggestion counts, space after accept, learning, excluded apps, bar look) and shuts down once:
  unhook → stop pipeline → learn the pending word → flush and close user.db → dispose.
- `TrayApplication` (`ApplicationContext`, no main window, nothing in the taskbar or Alt+Tab):
  NotifyIcon menu Enabled · Pause for 1 hour · Settings · About · (Diagnostics, Debug builds) · Exit.
  Exit and Windows log-off (`SystemEvents.SessionEnding`) run the same clean shutdown and remove the
  icon. NotifyIcon re-adds itself when Explorer restarts (TaskbarCreated).
- Settings: `%LOCALAPPDATA%\Itmam\settings.json` (`SettingsStore`: temp file + replace; a corrupt file
  is kept as `settings.json.corrupt-*` and defaults are used). Preferences only — no typed text.
- Appearance: `PopupAppearance.From(AppSettings)` → `PopupColors.For(appearance)` (theme colors, or a custom
  background with black/white text chosen by WCAG contrast, `ColorContrast`). `BarRenderer` paints the bar's
  items for both the real bar and the live preview on the Appearance page. Opacity uses a layered window
  (`Form.Opacity`), which never activates or shows the bar.
- Start with Windows (`StartupRegistration`): MSIX `StartupTask` "ItmamStartup" when packaged
  (synced with the setting at start-up); `HKCU\…\Run` when unpackaged, only when the user switches it
  on in Settings (development builds are never registered automatically).
- Updates (from 1.1, user decision): 12 s after start-up, if "Check for updates" is on, `StoreUpdates.CheckAsync` calls
  `StoreContext.GetAppAndOptionalStorePackageUpdatesAsync`. The Store service in Windows does the network work; the
  package has no `internetClient` capability. If an update exists, a notification opens
  `ms-windows-store://pdp/?productid=9P08F7KGRJB0` when clicked. Store installations only; development and test builds
  report "unavailable". The Updates page shows the version and checks on demand.
- Notifications: when the on/off state changes (menu, toggle shortcut, a pause and its end) the tray shows a notification,
  unless "Notify when Itmam is turned off or on" is off. Exiting from the menu or Settings asks for confirmation; Windows
  sign-out never asks.

### Privacy guarantees in the code

- Typed characters exist only in memory: in the channel, in `WordBuffer`, and on screen.
- The only text read from other apps is ≤ 49 characters before the caret, after the caret moved
  into existing text, never from password fields or excluded apps; only the current word is kept.
- The last 8 words in a row (for phrases) stay in memory only and are forgotten on any caret move. Word order is
  stored only as 2–5-word sequences with their counts (learned phrases, can be turned off); one not repeated is
  deleted after 30 days.
- The only question that leaves the device is asking Microsoft Store whether a newer version exists (`StoreUpdates`,
  can be turned off); it carries nothing about the user or what they type.
- No code path writes characters to a log, file, or the network. The diagnostics window's event list
  shows event *kinds* and latencies only (letters are shown as `Character (letter)`).
- Hook diagnostics (`HookDiagnostics`) are counters and timings only.
- Learned words are stored as word → count / last use only (see "Learning and storage") and are
  copied nowhere except by an export the user chooses, to a file the user picks.
