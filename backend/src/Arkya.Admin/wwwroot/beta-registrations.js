(() => {
  const dialog = document.querySelector('#beta-draw');
  const opener = document.querySelector('[data-open-draw]');
  if (!dialog || !opener || typeof dialog.showModal !== 'function') return;
  opener.addEventListener('click', event => { event.preventDefault(); dialog.showModal(); });
  for (const closer of dialog.querySelectorAll('[data-close-draw]')) {
    closer.addEventListener('click', event => { event.preventDefault(); dialog.close(); opener.focus(); });
  }
  dialog.addEventListener('close', () => opener.focus());
  if (dialog.open) { dialog.close(); dialog.showModal(); }
  for (const form of document.querySelectorAll('form[method="post"]')) {
    form.addEventListener('submit', () => {
      for (const button of form.querySelectorAll('button[type="submit"]')) button.disabled = true;
    });
  }
})();
