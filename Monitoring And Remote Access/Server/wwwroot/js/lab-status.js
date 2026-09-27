/* Keeps the Dashboard's lab session current.
 *
 * The page is rendered with the lab's state ([data-lab-status]). When a
 * teacher starts, pauses or ends the lab the hub sends GlobalSessionState, and
 * the page reloads to show it, so an administrator watching the Dashboard
 * sees a teacher's lab appear without refreshing. It does not reload over an
 * open dialog or a form being sent: the form's own redirect brings the new
 * state. While the lab runs, the elapsed minutes count up on their own.
 */
(() => {
    'use strict';

    const card = document.querySelector('[data-lab-status]');
    if (!card) return;

    // Ended and None both mean no lab is open.
    const openState = status => (status === 'Running' || status === 'Paused' ? status : 'None');
    const rendered = openState(card.dataset.labStatus);

    const elapsedLabel = document.querySelector('[data-lab-elapsed]');
    let elapsed = Number(card.dataset.labElapsed) || 0;
    if (rendered === 'Running' && elapsedLabel) {
        setInterval(() => {
            elapsed += 15;
            const minutes = Math.floor(elapsed / 60);
            elapsedLabel.textContent = minutes < 1 ? 'started just now' : `${minutes} min elapsed`;
        }, 15000);
    }

    const onState = state => {
        const status = state?.status ?? state?.Status;
        if (!status || openState(status) === rendered) return;
        if (document.querySelector('.modal.show, form[data-cams-submitting="true"]')) return;
        location.reload();
    };

    // A teacher viewing the Dashboard already has the portal's connection
    // (teacher-alert-badge.js); an administrator's pages have none, so one is
    // opened here. The hub puts administrators in the group that hears this.
    let connection = window.teacherHubConnection;
    if (!connection) {
        if (typeof signalR === 'undefined') return;
        connection = new signalR.HubConnectionBuilder()
            .withUrl('/remoteMonitoringHub')
            .withAutomaticReconnect()
            .build();
        // The shared heartbeat (HubHeartbeat on the server; a test holds these to it).
        connection.keepAliveIntervalInMilliseconds = 5000;
        connection.serverTimeoutInMilliseconds = 15000;
        connection.start().catch(() => { /* the page still shows the state it was rendered with */ });
    }
    connection.on('GlobalSessionState', onState);
})();
