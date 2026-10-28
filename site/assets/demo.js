// The hero demo: a sentence types itself, the suggestion bar appears above the caret, Tab completes
// the word. Illustrative only (no dictionary here). Stays on a finished frame for reduced motion.
(() => {
  const root = document.querySelector('[data-demo]');
  if (!root) return;
  const typed = root.querySelector('.typed');
  const bar = root.querySelector('.bar');
  const key = root.querySelector('.key');

  const scenes = [
    { before: 'مع أطيب التحيات ', part: 'والسلا', words: ['والسلام', 'والسلامة', 'والسلاح'] },
    { before: 'ذهبت صباح اليوم إلى ', part: 'المدر', words: ['المدرسة', 'المدرسين', 'المدرب'] },
    { before: 'شكرًا لكم على ', part: 'التعا', words: ['التعاون', 'التعامل', 'التعاطف'] },
  ];

  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const showBar = words => {
    bar.replaceChildren(...words.map(w => Object.assign(document.createElement('span'), { textContent: w })));
    bar.classList.add('show');
  };

  if (matchMedia('(prefers-reduced-motion: reduce)').matches) {
    typed.textContent = scenes[0].before + scenes[0].part;
    showBar(scenes[0].words);
    return;
  }

  (async function play() {
    for (let i = 0; ; i = (i + 1) % scenes.length) {
      const { before, part, words } = scenes[i];
      typed.textContent = '';
      bar.classList.remove('show');
      for (const ch of before + part) {
        typed.textContent += ch;
        if (typed.textContent.length > before.length + 1) showBar(words);
        await wait(ch === ' ' ? 140 : 85 + Math.random() * 70);
      }
      await wait(1100);
      key.classList.add('on');
      bar.firstElementChild.classList.add('picked');
      await wait(260);
      key.classList.remove('on');
      bar.classList.remove('show');
      typed.textContent = before + words[0] + ' ';
      await wait(2200);
    }
  })();
})();
