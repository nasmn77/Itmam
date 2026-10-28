# سياسة الخصوصية — إتمام | Privacy Policy — Itmam

> Draft (Phase 2). It is completed with the storage details in Phase 5 and published for the
> Microsoft Store listing in Phase 7.

## العربية

**إتمام لا يتصل بالإنترنت إطلاقًا.** لا يرسل أي بيانات، ولا يجمع إحصاءات استخدام، ولا يتحقق من التحديثات، ولا يرفع تقارير الأعطال.

### ما الذي يقرؤه التطبيق
- **ضغطات لوحة المفاتيح** أثناء الكتابة بتخطيط عربي فقط، لمعرفة الكلمة التي تكتبها الآن واقتراح إكمالها. الكتابة بأي تخطيط آخر (مثل الإنجليزية) لا تُتتبع.
- **الكلمة الحالية قبل المؤشر فقط:** عندما ينتقل المؤشر إلى نص موجود (Backspace، أو نقرة بالفأرة، أو الأسهم، أو الانتقال إلى حقل أو نافذة أخرى)، يقرأ التطبيق عبر UI Automation في Windows ما لا يزيد عن 49 حرفًا قبل المؤشر، ليكمل الكلمة التي يقف فيها المؤشر. يحتفظ في الذاكرة بهذه الكلمة فقط ويتجاهل الباقي فورًا. لا يُقرأ أي شيء من حقول كلمات المرور ولا من التطبيقات المستثناة.

### ما الذي لا يفعله
- لا يحفظ ما تكتبه، ولا الجمل، ولا ترتيب الكلمات، ولا يكتبه في أي سجل.
- لا يعمل في حقول كلمات المرور، ولا في التطبيقات المستثناة، ولا يقرأ منها أي نص.

### ما الذي يُحفظ على جهازك
- **الكلمات التي تكتبها:** لكل كلمة: الكلمة نفسها، وعدد مرات استخدامها، ووقت آخر استخدام، وهل حظرتها. لا شيء غير ذلك: لا جمل، ولا ترتيب كلمات، ولا اسم التطبيق الذي كتبت فيه.
- **المكان:** ملف واحد على جهازك فقط: `%LOCALAPPDATA%\Itmam\user.db` (عند التثبيت من المتجر يكون داخل المجلد الخاص بالتطبيق).
- **ما لا يُتعلم:** الكلمات في حقول كلمات المرور، والتطبيقات المستثناة، والتطبيقات التي تعمل بصلاحيات المسؤول، والألعاب بملء الشاشة، والكلمات المحظورة، وأي كلمة تمسحها مباشرة بعد كتابتها.
- **الحذف:** من الإعدادات ← "كلماتي": حذف كلمة، أو "مسح كل البيانات المتعلَّمة"، أو إيقاف التعلم مؤقتًا. ويُحذف الملف نهائيًا بإلغاء تثبيت التطبيق.

## English

**Itmam never connects to the internet.** It sends no data, collects no usage statistics, does not check for updates and does not upload crash reports.

### What the app reads
- **Keystrokes**, only while you type with an Arabic keyboard layout, to know the word you are typing and suggest completions. Typing with any other layout (e.g. English) is not tracked.
- **Only the current word before the caret:** when the caret moves into existing text (Backspace, a mouse click, arrow keys, or switching to another field or window), the app uses Windows UI Automation to read at most 49 characters before the caret so it can continue the word the caret is in. It keeps only that word (in memory) and discards the rest immediately. Nothing is ever read from password fields or excluded apps.

### What it does not do
- It does not save what you type, sentences or word order, and never writes it to any log.
- It does nothing in password fields or excluded apps, and never reads any text from them.

### What is stored on your device
- **Words you type:** for each word: the word itself, how many times you used it, when you last used it, and whether you blocked it. Nothing else: no sentences, no word order, not the app you typed in.
- **Where:** one file on your device only: `%LOCALAPPDATA%\Itmam\user.db` (inside the app's private folder when installed from the Store).
- **Never learned:** words in password fields, excluded apps, apps running as administrator, full-screen games, blocked words, and any word you delete right after typing it.
- **Deleting it:** Settings → "My words": delete a word, "Clear all learned data", or pause learning. Uninstalling the app removes the file.
