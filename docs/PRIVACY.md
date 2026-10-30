# سياسة الخصوصية — إتمام | Privacy Policy — Itmam

[العربية](#العربية) · [English](#english)

<div dir="rtl">

## العربية

**تاريخ السريان:** الإصدار 1.1 (29 أكتوبر 2026)

**إتمام نفسه لا يتصل بالإنترنت.** لا يرسل أي بيانات إلى أي جهة، ولا يجمع إحصاءات استخدام، ولا يرفع تقارير الأعطال. كل ما يفعله بما تكتبه يبقى على جهازك. الاستثناء الوحيد سؤال متجر Microsoft عن التحديثات (انظر «التحقق من التحديثات» أدناه).

### ما الذي يقرؤه التطبيق

- **ضغطات لوحة المفاتيح** أثناء الكتابة بلوحة مفاتيح عربية فقط، لمعرفة الكلمة التي تكتبها الآن واقتراح إكمالها. الكتابة بأي لغة أخرى (مثل الإنجليزية) لا تُتتبع.
- **الكلمة الحالية قبل المؤشر فقط:** عندما ينتقل المؤشر إلى نص موجود (Backspace، أو نقرة بالفأرة، أو الأسهم، أو الانتقال إلى حقل أو نافذة أخرى)، يقرأ التطبيق عبر خدمة UI Automation في Windows ما لا يزيد عن 49 حرفًا قبل المؤشر، ليكمل الكلمة التي يقف فيها المؤشر. يحتفظ في الذاكرة بهذه الكلمة وحدها ويتجاهل الباقي فورًا.
- **آخر الكلمات التي كتبتها متتالية** (8 كلمات على الأكثر)، لإكمال العبارات المتلازمة مثل «بسم الله الرحمن الرحيم». تبقى في الذاكرة فقط، وتُنسى عند أي علامة غير المسافة أو عند تحريك المؤشر أو تغيير النافذة. وما يُحفظ منها موصوف في «العبارات المتعلَّمة» أدناه.
- **اسم ملف البرنامج** الذي تكتب فيه (مثل `notepad.exe`)، لمعرفة هل هو من التطبيقات المستثناة. لا يُحفظ هذا الاسم.

### ما الذي لا يفعله

- لا يحفظ ما تكتبه، ولا يكتب أي حرف تكتبه في أي سجل. ولا يُحفظ من ترتيب الكلمات إلا التسلسلات القصيرة الموصوفة في «العبارات المتعلَّمة» أدناه، ويمكنك إيقاف ذلك.
- لا يعمل إطلاقًا ولا يقرأ أي نص في: حقول كلمات المرور، والتطبيقات المستثناة، والتطبيقات التي تعمل بصلاحيات المسؤول، والألعاب والعروض بملء الشاشة.
- لا يشارك أي بيانات مع أي طرف، ولا يعرض إعلانات.

### التحقق من التحديثات

- بعد بدء التشغيل بثوانٍ، يسأل إتمام **متجر Microsoft** عبر Windows: هل يوجد إصدار أحدث من إتمام؟ وإن وُجد يظهر إشعار، والنقر عليه يفتح صفحة إتمام في المتجر.
- خدمة المتجر في Windows هي التي تتصل، كما تفعل لتحديث كل تطبيقاتك. إتمام نفسه لا يملك صلاحية الاتصال بالإنترنت، ولا يُرسَل في هذا السؤال أي شيء عنك أو عما تكتبه.
- يمكنك إطفاؤه من الإعدادات ← «التحديثات» ← «البحث عن التحديثات عند بدء إتمام». ويبقى المتجر يحدّث التطبيق بطريقته المعتادة.

### ما الذي يُحفظ على جهازك

- **كلماتك:** لكل كلمة: الكلمة نفسها، وعدد مرات استعمالها، ووقت آخر استعمال، وهل حظرتها. لا شيء غير ذلك: لا جمل، ولا اسم التطبيق الذي كتبت فيه.
  المكان: `%LOCALAPPDATA%\Itmam\user.db` (عند التثبيت من المتجر يكون داخل المجلد الخاص بالتطبيق).
- **العبارات المتعلَّمة** (من الإصدار 1.1، مفعّلة افتراضيًا): من كلمتين إلى 5 كلمات عربية كتبتها متتالية، مع عدد مرات كتابتها ووقت آخرها، في الملف نفسه. التسلسل الذي تكتبه مرتين يُقترح عبارةً، وما لم يتكرر يُحذف بعد 30 يومًا من آخر كتابة له. لا يُحفظ معها اسم التطبيق ولا ما قبلها أو بعدها، ولا يُعدّ شيء في المواضع المذكورة في «ما لا يُتعلَّم». لإيقافها: الإعدادات ← «عباراتي» ← «تعلّم العبارات من كتابتي».
- **إعداداتك:** مثل الألوان والاختصارات وقائمة التطبيقات المستثناة، و«عباراتي» و«الاختصارات النصية» و«العبارات الطويلة» التي تضيفها بنفسك، في `%LOCALAPPDATA%\Itmam\settings.json`. لا تحتوي أي نص مما تكتبه في البرامج الأخرى.
- **ما لا يُتعلَّم:** الكلمات في حقول كلمات المرور، والتطبيقات المستثناة، والتطبيقات التي تعمل بصلاحيات المسؤول، والألعاب بملء الشاشة، والكلمات المحظورة وأي تسلسل فيه كلمة محظورة، وأي كلمة تمسحها مباشرة بعد كتابتها.

### تحكّمك في بياناتك

- **الحذف:** من الإعدادات ← «كلماتي»: حذف كلمة، أو «حذف كل كلماتي»، أو إطفاء «تعلّم الكلمات الجديدة». ومن «عباراتي»: حذف عبارة متعلَّمة، أو «حذف العبارات المتعلَّمة» مع كل ما عُدّ، أو إيقاف تعلّمها.
- **التصدير:** لا تُنسخ كلماتك إلى أي مكان إلا إذا اخترت «تصدير…» بنفسك، فتُكتب في الملف الذي تختاره أنت.
- **إلغاء التثبيت:** يحذف التطبيق وملفاته من جهازك.

### التواصل

لأي سؤال عن الخصوصية: ناصر محمد الماضي — nasmn77@gmail.com

الشيفرة المصدرية مفتوحة ويمكن مراجعتها: https://github.com/nasmn77/Itmam

إذا تغيّرت هذه السياسة، تُنشر النسخة الجديدة في هذه الصفحة مع تاريخ سريانها.

</div>

## English

**Effective date:** version 1.1 (October 29, 2026)

**Itmam itself never connects to the internet.** It sends no data to anyone, collects no usage statistics and does not upload crash reports. Everything it does with what you type stays on your device. The only exception is asking Microsoft Store about updates (see "Update check" below).

### What the app reads

- **Keystrokes**, only while you type with an Arabic keyboard layout, to know the word you are typing and suggest completions. Typing in any other language (e.g. English) is not tracked.
- **Only the current word before the caret:** when the caret moves into existing text (Backspace, a mouse click, arrow keys, or switching to another field or window), the app uses Windows UI Automation to read at most 49 characters before the caret so it can continue the word the caret is in. It keeps only that word, in memory, and discards the rest immediately.
- **The last words you typed in a row** (8 at most), to complete common phrases such as «بسم الله الرحمن الرحيم». They stay in memory only and are forgotten at any boundary other than a space, any caret move or window change. What is kept from them is described under "Learned phrases" below.
- **The program file name** of the app you type in (e.g. `notepad.exe`), to know whether it is an excluded app. This name is not stored.

### What it does not do

- It does not save what you type and never writes any typed character to a log. Word order is kept only in the short sequences described under "Learned phrases" below, and you can turn that off.
- It does nothing and reads no text in password fields, excluded apps, apps running as administrator, or full-screen games and presentations.
- It shares no data with anyone and shows no ads.

### Update check

- A few seconds after it starts, Itmam asks **Microsoft Store** through Windows whether a newer version of Itmam exists. If so, a notification appears; clicking it opens Itmam's page in the Store.
- The Store service in Windows does the network work, as it does to update all your apps. Itmam itself has no permission to connect to the internet, and nothing about you or what you type is sent with this question.
- You can turn it off in Settings → "Updates" → "Check for updates when Itmam starts". The Store keeps updating the app in its usual way.

### What is stored on your device

- **Your words:** for each word: the word itself, how many times you used it, when you last used it, and whether you blocked it. Nothing else: no sentences, not the app you typed in.
  Location: `%LOCALAPPDATA%\Itmam\user.db` (inside the app's private folder when installed from the Store).
- **Learned phrases** (from 1.1, on by default): sequences of 2 to 5 Arabic words you typed in a row, with how many times and when you last typed them, in the same file. A sequence typed twice is suggested as a phrase; one not repeated is deleted 30 days after it was last typed. The app and the text around it are not kept, and nothing is counted in the places listed under "Never learned". To turn it off: Settings → "My phrases" → "Learn phrases from my typing".
- **Your settings:** such as colors, shortcuts, the excluded apps list and the "My phrases", text shortcuts and long phrases you add yourself, in `%LOCALAPPDATA%\Itmam\settings.json`. They contain nothing you type in other apps.
- **Never learned:** words in password fields, excluded apps, apps running as administrator, full-screen games, blocked words and any sequence containing one, and any word you delete right after typing it.

### Your control over your data

- **Delete:** Settings → "My words": delete a word, "Delete all my words", or turn off "Learn new words". Settings → "My phrases": delete a learned phrase, "Delete learned phrases" (with everything counted so far), or stop learning them.
- **Export:** your words are copied nowhere unless you choose "Export…" yourself; they are then written to the file you pick.
- **Uninstall:** removes the app and its files from your device.

### Contact

Privacy questions: Naser Mohammad Almadi — nasmn77@gmail.com

The source code is open for review: https://github.com/nasmn77/Itmam

If this policy changes, the new version is published on this page with its effective date.
