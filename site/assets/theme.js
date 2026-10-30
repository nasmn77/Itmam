// Light / dark theme button. The choice is kept in this browser only (localStorage): no cookie,
// nothing sent anywhere. Without a choice, the page follows the system's light or dark mode.
(() => {
  const root = document.documentElement;
  const media = window.matchMedia('(prefers-color-scheme: dark)');
  const current = () => root.dataset.theme || (media.matches ? 'dark' : 'light');

  function label(button) {
    const dark = current() === 'dark';
    const arabic = root.lang === 'ar';
    button.setAttribute('aria-label', dark
      ? (arabic ? 'الوضع الفاتح' : 'Light mode')
      : (arabic ? 'الوضع الداكن' : 'Dark mode'));
    button.setAttribute('aria-pressed', String(dark));
  }

  document.addEventListener('DOMContentLoaded', () => {
    const button = document.querySelector('[data-theme-toggle]');
    if (!button) {
      return;
    }

    label(button);
    button.addEventListener('click', () => {
      const next = current() === 'dark' ? 'light' : 'dark';
      root.dataset.theme = next;
      try {
        localStorage.setItem('theme', next);
      } catch {
        // Storage blocked: the choice lasts until the page is closed.
      }

      label(button);
    });
    media.addEventListener('change', () => label(button));
  });
})();
