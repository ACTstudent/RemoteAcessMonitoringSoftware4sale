/* Select several records in a directory and act on them at once: bulk delete
 * on Students and Student Profiles, bulk remove and delete on a class roster.
 *
 * Markup contract, inside one [data-crud-list][data-bulk-select] panel
 * (data-bulk-noun / data-bulk-nouns name a record: "student" / "students"):
 *   [data-bulk-item]      one checkbox per record: value is its id,
 *                         data-bulk-name what the confirmation calls it
 *   [data-bulk-all]       the header box: every record the search and filters
 *                         leave, on every page - not just the rows on screen
 *   [data-bulk-bar]       shown while anything is selected; [data-bulk-count]
 *                         inside it says how many
 *   [data-bulk-clear]     clears the selection
 *   form[data-bulk-form]  one per bulk action. On submit the selected ids are
 *                         added as data-bulk-field inputs, and data-bulk-confirm
 *                         becomes the confirmation, with {count} replaced.
 *
 * Runs after crud-list.js, which it relies on for camsMatchingItems.
 */
(() => {
    'use strict';

    document.querySelectorAll('[data-crud-list][data-bulk-select]').forEach(panel => {
        const boxes = Array.from(panel.querySelectorAll('[data-bulk-item]'));
        const all = panel.querySelector('[data-bulk-all]');
        const bar = panel.querySelector('[data-bulk-bar]');
        const count = bar?.querySelector('[data-bulk-count]');
        const noun = panel.dataset.bulkNoun || 'record';
        const nouns = panel.dataset.bulkNouns || `${noun}s`;
        const counted = n => `${n} ${n === 1 ? noun : nouns}`;
        const rowOf = box => box.closest('[data-crud-item]');

        // The boxes of the records still on the list after search and filters.
        const listed = () => {
            const matching = panel.camsMatchingItems;
            return boxes.filter(box => !box.disabled && (!matching || matching.includes(rowOf(box))));
        };
        const selected = () => boxes.filter(box => box.checked);

        function refresh() {
            const chosen = selected();
            if (bar) bar.hidden = chosen.length === 0;
            if (count) count.textContent = `${counted(chosen.length)} selected`;
            if (all) {
                const inList = listed();
                const ticked = inList.filter(box => box.checked).length;
                all.checked = inList.length > 0 && ticked === inList.length;
                all.indeterminate = ticked > 0 && ticked < inList.length;
                all.disabled = inList.length === 0;
            }
            boxes.forEach(box => rowOf(box)?.classList.toggle('is-selected', box.checked));
        }

        all?.addEventListener('change', () => {
            const check = all.checked;
            listed().forEach(box => { box.checked = check; });
            refresh();
        });
        boxes.forEach(box => box.addEventListener('change', refresh));
        panel.querySelectorAll('[data-bulk-clear]').forEach(button => button.addEventListener('click', () => {
            boxes.forEach(box => { box.checked = false; });
            refresh();
        }));

        // A record the search or a filter takes off the list is deselected, so
        // a bulk action never reaches a row that is no longer in the results.
        // Records on other pages of the same results stay selected.
        panel.addEventListener('cams:crud-render', () => {
            const inList = new Set(listed());
            boxes.forEach(box => { if (box.checked && !inList.has(box)) box.checked = false; });
            refresh();
        });

        panel.querySelectorAll('form[data-bulk-form]').forEach(form => {
            // Runs before site.js's confirmation, which listens on the document.
            form.addEventListener('submit', event => {
                const chosen = selected();
                if (chosen.length === 0) {
                    event.preventDefault();
                    event.stopImmediatePropagation();
                    return;
                }
                form.querySelectorAll('input[data-bulk-value]').forEach(input => input.remove());
                chosen.forEach(box => {
                    const input = document.createElement('input');
                    input.type = 'hidden';
                    input.name = form.dataset.bulkField;
                    input.value = box.value;
                    input.setAttribute('data-bulk-value', '');
                    form.append(input);
                });
                const names = chosen.map(box => box.dataset.bulkName || box.value);
                form.dataset.confirmSubject = names.length <= 3
                    ? names.join(', ')
                    : `${names.slice(0, 3).join(', ')} and ${names.length - 3} more`;
                form.dataset.confirm = (form.dataset.bulkConfirm || 'Apply this to {count}?')
                    .replaceAll('{count}', counted(chosen.length));
            });
        });

        refresh();
    });
})();
