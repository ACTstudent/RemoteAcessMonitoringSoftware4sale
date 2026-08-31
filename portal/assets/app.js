(() => {
  const menuButton = document.querySelector('[data-menu-button]');
  const menu = document.querySelector('[data-menu]');

  const closeMenu = () => {
    if (!menuButton || !menu) return;
    menuButton.setAttribute('aria-expanded', 'false');
    menuButton.querySelector('.sr-only').textContent = 'Open navigation';
    menu.classList.remove('is-open');
    document.body.classList.remove('menu-open');
  };

  menuButton?.addEventListener('click', () => {
    const isOpen = menuButton.getAttribute('aria-expanded') === 'true';
    if (isOpen) {
      closeMenu();
      return;
    }

    menuButton.setAttribute('aria-expanded', 'true');
    menuButton.querySelector('.sr-only').textContent = 'Close navigation';
    menu?.classList.add('is-open');
    document.body.classList.add('menu-open');
  });

  menu?.addEventListener('click', (event) => {
    if (event.target.closest('a')) closeMenu();
  });

  window.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') closeMenu();
  });

  document.querySelectorAll('[data-copy]').forEach((button) => {
    button.addEventListener('click', async () => {
      const originalLabel = button.dataset.copyLabel || 'Copy';
      try {
        await navigator.clipboard.writeText(button.dataset.copy);
        button.textContent = 'Copied';
      } catch {
        button.textContent = 'Copy unavailable';
      }
      window.setTimeout(() => { button.textContent = originalLabel; }, 1800);
    });
  });

  fetch('version.json', { cache: 'no-cache' })
    .then((response) => response.ok ? response.json() : Promise.reject())
    .then(({ version }) => {
      if (!/^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/.test(version)) return;
      document.querySelectorAll('[data-version]').forEach((element) => {
        element.textContent = version;
      });
    })
    .catch(() => {
      // The complete page, including its release version, works without fetch.
    });
})();
