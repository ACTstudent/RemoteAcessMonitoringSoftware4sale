/* Confirms moving a student who is already in a class into a different one.
 *
 * This ran as three near-identical inline blocks - the admin class page, the
 * admin roster and the teacher class page - which had already drifted apart in
 * small ways. One copy driven by markup.
 *
 * Markup contract, on the form:
 *   data-move-confirm             marks the form
 *   data-move-target="Class 1A"   the destination, when the page is about one
 *                                 class. Omitted on the roster, where the
 *                                 destination is whatever the select shows.
 * Inside it:
 *   [data-move-select]            the class or student select
 *   [data-move-flag]              the hidden input the server reads
 *
 * Where the student's *current* class is recorded differs by page and does not
 * unify away: on a class page one option per student carries it, on the roster
 * the row is a single student so the select carries it.
 */
(() => {
    'use strict';

    // Forms already wired, so re-initialising cannot attach a second handler
    // and ask twice.
    const wired = new WeakSet();

    const currentClassOf = (select, option) =>
        (option && option.dataset.currentClass) || select.dataset.currentClass || '';

    const onSubmit = async event => {
        const form = event.currentTarget;
        const select = form.querySelector('[data-move-select]');
        const flag = form.querySelector('[data-move-flag]');
        if (!select || !flag) return;

        const option = select.selectedOptions[0];
        const currentClass = currentClassOf(select, option);

        // Not in a class yet: this is an enrolment, not a move.
        if (!currentClass) return;

        // Roster mode, identified by the select carrying the current class id.
        // Reproduced exactly as it was: choosing the same class again is not a
        // move, and neither is choosing "Unassigned" - removing a student from
        // a class went through without asking, and this change is not the place
        // to start asking.
        if (select.hasAttribute('data-current-class-id')) {
            const currentClassId = select.dataset.currentClassId || '';
            const selected = select.value || '';
            if (!currentClassId || !selected || selected === currentClassId) return;
        }

        // Second pass, after it was confirmed. Let it through.
        if (flag.value === 'true') return;

        event.preventDefault();

        const target = form.dataset.moveTarget ||
            (option && option.textContent.trim()) || 'the selected class';
        const confirmed = await window.camsConfirm({
            title: 'Move student',
            message: `Move this student from ${currentClass} to ${target}?`,
            confirmLabel: 'Move student',
            variant: 'warning'
        });
        if (!confirmed) return;

        flag.value = 'true';
        form.requestSubmit();
    };

    const init = (root = document) => {
        root.querySelectorAll('form[data-move-confirm]').forEach(form => {
            if (wired.has(form)) return;
            wired.add(form);
            form.addEventListener('submit', onSubmit);
        });
    };

    window.CamsStudentMove = { init };
    init();
})();
