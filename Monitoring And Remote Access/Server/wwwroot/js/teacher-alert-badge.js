/* Keeps the sidebar's open-alert count current, and owns the teacher hub connection.
 *
 * Runs immediately rather than on DOMContentLoaded, and that is load-bearing:
 * teacher-monitoring.js reads window.teacherHubConnection at its top level, and
 * the layout renders this script before the Scripts section. Deferring it would
 * hand that file an undefined connection.
 *
 * The count itself is rendered server-side by OpenAlertCountViewComponent. This
 * only refreshes it when the hub reports a new alert, so a teacher watching the
 * dashboard sees one arrive without reloading.
 */
(() => {
    'use strict';

    if (typeof signalR === 'undefined') return;

    const badge = document.getElementById('monitoringAlertBadge');

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/remoteMonitoringHub')
        .withAutomaticReconnect()
        .build();

    const refreshAlertCount = async () => {
        if (!badge) return;
        const response = await fetch('/Teacher/OpenAlertCount', { credentials: 'same-origin' });
        if (!response.ok) return;
        const count = Number((await response.json()).count) || 0;
        badge.textContent = String(count);
        badge.classList.toggle('d-none', count === 0);
    };

    connection.on('MonitoringAlertReceived', () => refreshAlertCount().catch(() => {}));

    // Three times the server keep-alive of 5s, so one missed ping on a busy link
    // is not read as a disconnection.
    connection.serverTimeoutInMilliseconds = 15000;

    window.teacherHubConnection = connection;
    // The badge stops counting if this drops, so the header says so.
    CamsConnection.watch(connection, { label: 'notifications connection' });
    window.teacherHubStarted = CamsConnection.started(connection.start());
})();
