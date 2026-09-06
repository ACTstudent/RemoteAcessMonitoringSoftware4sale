/* The select-all box and the guard on the teacher's bulk alert actions.
 *
 * Markup contract:
 *   [data-alert-select-all]   the header checkbox
 *   [data-alert-select]       one checkbox per alert group
 *   [data-alert-bulk-form]    the form the bulk buttons submit
 */
(() => {
    'use strict';

    const form = document.querySelector('[data-alert-bulk-form]');
    const selectAll = document.querySelector('[data-alert-select-all]');
    const boxes = Array.from(document.querySelectorAll('[data-alert-select]'));

    if (selectAll) {
        selectAll.addEventListener('change', () => {
            boxes.forEach(box => { box.checked = selectAll.checked; });
        });
        // Clearing one row clears the header box too, otherwise it goes on
        // claiming everything is selected when it is not.
        boxes.forEach(box => box.addEventListener('change', () => {
            selectAll.checked = boxes.length > 0 && boxes.every(b => b.checked);
        }));
    }

    form?.addEventListener('submit', event => {
        if (boxes.some(box => box.checked)) return;
        event.preventDefault();
        window.camsConfirm({
            title: 'Select alerts',
            message: 'Select at least one alert group before using a bulk action.',
            confirmLabel: 'OK',
            variant: 'primary'
        });
    });
})();
