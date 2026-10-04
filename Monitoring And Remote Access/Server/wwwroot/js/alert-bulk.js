/* Selecting alerts for the teacher's bulk actions: Acknowledge, Dismiss, Reopen.
 *
 * The alerts are folded inside one row per student. Selecting works at three
 * levels, and each keeps the other two in step:
 *   - the header box selects every alert on the page;
 *   - a student's box selects every alert of that student;
 *   - an alert's own box, seen once the student's row is opened.
 *
 * A folded alert stays selectable. It used to be disabled until its student's
 * row was opened, so the header box looked ticked while nothing was selected,
 * and Acknowledge and Dismiss answered "Select at least one alert group".
 *
 * Markup contract:
 *   [data-alert-select-all]       the header checkbox
 *   [data-alert-select-student]   one checkbox per student row
 *   [data-alert-select]           one checkbox per alert group
 *   [data-alert-selected-count]   where the number selected is shown
 *   [data-alert-bulk-form]        the form the bulk buttons submit
 */
(() => {
    'use strict';

    const form = document.querySelector('[data-alert-bulk-form]');
    const selectAll = document.querySelector('[data-alert-select-all]');
    const boxes = Array.from(document.querySelectorAll('[data-alert-select]'));
    const count = document.querySelector('[data-alert-selected-count]');

    // A student's box, with the alert boxes folded in the row beneath it.
    const students = Array.from(document.querySelectorAll('[data-alert-select-student]')).map(box => {
        const detail = box.closest('tr')?.nextElementSibling;
        return { box, alerts: Array.from(detail?.querySelectorAll('[data-alert-select]') ?? []) };
    });

    function setState(box, ticked, total) {
        box.checked = total > 0 && ticked === total;
        // Some but not all: shown as a dash, so a part selection is not mistaken for none.
        box.indeterminate = ticked > 0 && ticked < total;
    }

    function refresh() {
        students.forEach(({ box, alerts }) => setState(box, alerts.filter(alert => alert.checked).length, alerts.length));
        const ticked = boxes.filter(box => box.checked).length;
        if (selectAll) setState(selectAll, ticked, boxes.length);
        if (count) {
            count.textContent = ticked === 0 ? 'No alerts selected' : ticked === 1 ? '1 alert selected' : `${ticked} alerts selected`;
        }
    }

    selectAll?.addEventListener('change', () => {
        boxes.forEach(box => { box.checked = selectAll.checked; });
        refresh();
    });
    students.forEach(({ box, alerts }) => box.addEventListener('change', () => {
        alerts.forEach(alert => { alert.checked = box.checked; });
        refresh();
    }));
    boxes.forEach(box => box.addEventListener('change', refresh));
    refresh();

    form?.addEventListener('submit', event => {
        if (boxes.some(box => box.checked)) return;
        event.preventDefault();
        window.camsConfirm({
            title: 'Select alerts',
            message: 'Tick a student, or open a student and tick an alert, before using Acknowledge, Dismiss or Reopen.',
            confirmLabel: 'OK',
            variant: 'primary'
        });
    });
})();
