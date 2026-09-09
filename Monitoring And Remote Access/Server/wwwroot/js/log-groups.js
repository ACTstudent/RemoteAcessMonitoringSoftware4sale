/* Grouped log rows: one user per row, their events folded inside it.

   A flat event list repeats the role and the user id on every line, so a busy
   day buries whatever an administrator came to find. Grouping trades that for
   one row per actor and a click to open the detail. */
(() => {
    'use strict';

    document.querySelectorAll('[data-log-group]').forEach(row => {
        const toggle = row.querySelector('.log-group-toggle');
        const detail = row.nextElementSibling;
        if (!toggle || !detail?.hasAttribute('data-log-detail')) return;

        // Selection controls inside a closed group are disabled, not merely
        // hidden. A disabled checkbox does not submit and cannot be reached by a
        // select-all, so a teacher can never bulk-acknowledge alerts that are
        // folded out of sight. Closing a group also clears what it had ticked.
        const selectors = detail.querySelectorAll('input[type="checkbox"]');

        function setOpen(open) {
            detail.hidden = !open;
            toggle.setAttribute('aria-expanded', String(open));
            row.classList.toggle('log-group-open', open);
            selectors.forEach(box => {
                if (!open) box.checked = false;
                box.disabled = !open;
            });
        }

        // The button is the accessible control, so it gets Enter and Space for
        // free. The row is a convenience target on top of it.
        toggle.addEventListener('click', event => {
            event.stopPropagation();
            setOpen(detail.hidden);
        });
        row.addEventListener('click', event => {
            if (event.target.closest('a, button, input, select, textarea, label')) return;
            setOpen(detail.hidden);
        });

        // The pager hides the rows that are not on the current page. The detail
        // is a sibling row rather than a child, so without this an open detail
        // would stay on screen underneath somebody else's row after a page turn
        // or a search. Collapsing with its own row is what keeps the two together.
        new MutationObserver(() => { if (row.hidden && !detail.hidden) setOpen(false); })
            .observe(row, { attributes: true, attributeFilter: ['hidden'] });
    });
})();
