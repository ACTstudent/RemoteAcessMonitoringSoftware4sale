/* Keeps the student's own session card in step with the server.
 *
 * A poll rather than a hub subscription: the card shows remaining minutes, which
 * the server recalculates, and a 30s refresh is enough for a number counted in
 * minutes.
 *
 * Markup contract: #countdown, #sessionStatus, #assignedUnit.
 */
(() => {
    'use strict';

    const countdown = document.getElementById('countdown');
    const statusBox = document.getElementById('sessionStatus');
    const unitBox = document.getElementById('assignedUnit');
    if (!countdown && !statusBox && !unitBox) return;

    const badgeClass = status =>
        status === 'Running' ? 'badge-active'
            : status === 'Paused' ? 'bg-warning text-dark'
                : 'bg-secondary';

    const refresh = async () => {
        try {
            const response = await fetch('/Student/_SessionStatusJson');
            if (!response.ok) return;
            const data = await response.json();
            if (!data || !data.active) return;

            if (countdown) countdown.textContent = (data.remaining ?? '-') + ' min';
            if (statusBox) {
                const badge = document.createElement('span');
                badge.className = 'badge fs-6 px-3 py-2 ' + badgeClass(data.status);
                badge.textContent = data.status;
                statusBox.replaceChildren(badge);
            }
            if (unitBox && data.station) unitBox.textContent = data.station;
        } catch (error) {
            // A transient poll failure should not disturb the page.
        }
    };

    setInterval(refresh, 30000);
})();
