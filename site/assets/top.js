// "Back to top" button: shown once the visitor has scrolled down a screen. Without this script the
// button is simply always shown, and it still works (it is a link to the top of the page).
(() => {
  document.addEventListener('DOMContentLoaded', () => {
    const button = document.querySelector('.to-top');
    if (!button) {
      return;
    }

    const update = () => button.classList.toggle('away', window.scrollY < window.innerHeight * 0.8);
    update();
    window.addEventListener('scroll', update, { passive: true });
    button.addEventListener('click', event => {
      event.preventDefault();
      window.scrollTo({ top: 0 });
      history.replaceState(null, '', location.pathname + location.search);
    });
  });
})();
