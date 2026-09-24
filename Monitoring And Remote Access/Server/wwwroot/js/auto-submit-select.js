/* Saves a select the moment a choice is made, in place of a separate
 * Assign or Map button beside it.
 *
 * Markup contract: <select data-auto-submit> inside the form it saves.
 *
 * A mouse or touch pick saves at once. The keyboard is handled differently on
 * purpose: in Chrome and Edge an arrow key on a closed select changes the value
 * and fires "change" immediately, so saving on every change would submit - and
 * reload the page - on the first arrow press, long before the option that was
 * wanted. Keyboard browsing therefore saves on Enter, or when focus leaves the
 * select with a different value showing.
 *
 * Other submit handlers on the same form still run. The class select's move
 * confirmation, for one, holds the submit back until it is answered.
 */
(() => {
    'use strict';

    const browseKeys = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown']);

    // Selects whose current value came from the keyboard and is not saved yet.
    const browsing = new WeakSet();
    const wired = new WeakSet();

    const save = select => {
        browsing.delete(select);
        if (!select.form || select.value === select.dataset.savedValue) return;
        select.form.requestSubmit();
    };

    const wire = select => {
        if (wired.has(select)) return;
        wired.add(select);
        select.dataset.savedValue = select.value;

        select.addEventListener('pointerdown', () => browsing.delete(select));

        select.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                save(select);
                return;
            }
            // Alt+Down and Space open the list rather than stepping through it;
            // a pick made in the open list arrives as an ordinary change.
            const steps = browseKeys.has(event.key) && !event.altKey;
            const typeAhead = event.key.length === 1 && event.key !== ' ' && !event.ctrlKey && !event.metaKey;
            if (steps || typeAhead) browsing.add(select);
        });

        select.addEventListener('change', () => {
            if (!browsing.has(select)) save(select);
        });

        select.addEventListener('blur', () => {
            if (browsing.has(select)) save(select);
        });

        // Dim the control while the page saves. Checked after every handler has
        // run, so a submit another handler held back does not look busy.
        select.form?.addEventListener('submit', event => {
            setTimeout(() => {
                if (event.defaultPrevented) return;
                select.classList.add('is-saving');
                select.setAttribute('aria-busy', 'true');
            }, 0);
        });
    };

    const init = (root = document) => root.querySelectorAll('select[data-auto-submit]').forEach(wire);

    window.CamsAutoSubmit = { init };
    init();
})();
