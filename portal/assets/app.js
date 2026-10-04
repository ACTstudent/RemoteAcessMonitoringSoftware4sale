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
    // Only the label changes, so the button's icon survives the feedback.
    const label = button.querySelector('[data-copy-text]') || button;
    button.addEventListener('click', async () => {
      const originalLabel = button.dataset.copyLabel || 'Copy';
      try {
        await navigator.clipboard.writeText(button.dataset.copy);
        label.textContent = 'Copied';
      } catch {
        label.textContent = 'Copy unavailable';
      }
      window.setTimeout(() => { label.textContent = originalLabel; }, 1800);
    });
  });

  // The header gains its bottom rule once the hero heading has scrolled away.
  const header = document.querySelector('[data-header]');
  const heroTitle = document.getElementById('hero-title');
  if (header && heroTitle && 'IntersectionObserver' in window) {
    new IntersectionObserver(([entry]) => {
      header.classList.toggle('is-scrolled', !entry.isIntersecting);
    }).observe(heroTitle);
  }

  // The wireframe reel moves on its own, so it gets a pause control (WCAG 2.2.2).
  // With reduced motion it never moves and the control is hidden.
  const reel = document.querySelector('[data-reel]');
  const reelToggle = document.querySelector('[data-reel-toggle]');
  if (reel && reelToggle) {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      reelToggle.hidden = true;
    } else {
      const reelLabel = reelToggle.querySelector('[data-reel-label]');
      reelToggle.addEventListener('click', () => {
        const paused = reel.classList.toggle('is-paused');
        reelToggle.setAttribute('aria-pressed', String(paused));
        reelLabel.textContent = paused ? 'Play' : 'Pause';
      });
    }
  }

  // Sections ease in as they enter the viewport. Content is visible by default;
  // it is only hidden first when motion is allowed and the observer exists.
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (!reduceMotion && 'IntersectionObserver' in window) {
    const revealObserver = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('is-visible');
        revealObserver.unobserve(entry.target);
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.12 });

    document.querySelectorAll('[data-reveal]').forEach((element) => {
      // Siblings that enter together arrive in sequence rather than all at once.
      const siblings = [...element.parentElement.children].filter((child) => child.hasAttribute('data-reveal'));
      const index = siblings.indexOf(element);
      if (index > 0) element.style.setProperty('--reveal-delay', `${Math.min(index, 5) * 70}ms`);
      revealObserver.observe(element);
    });
    document.documentElement.classList.add('has-reveal');
  }

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
