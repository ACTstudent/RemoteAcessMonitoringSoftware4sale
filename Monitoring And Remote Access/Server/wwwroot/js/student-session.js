/* The student toolbar: live session status, the running timer, and the messages
 * a teacher sends.
 *
 * Previously an inline block whose variables were true globals. Wrapped now -
 * nothing outside referenced them.
 *
 * Markup contract: #toolbarTimer, #toolbarSession, and the #alertModal partial
 * with #alertModalHeader / #alertModalTitle / #alertModalBody.
 */
(() => {
    'use strict';

    if (typeof signalR === 'undefined') return;

    const timerEl = document.getElementById('toolbarTimer');
    const sessionEl = document.getElementById('toolbarSession');
    const alertModalEl = document.getElementById('alertModal');
    if (!timerEl || !sessionEl) return;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/remoteMonitoringHub')
        .withAutomaticReconnect()
        .build();

    let status = 'None';
    let elapsedSeconds = 0;

    const format = seconds => {
        const m = Math.floor(seconds / 60);
        const s = seconds % 60;
        return String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0');
    };

    const badgeClass = value =>
        value === 'Running' ? 'badge-active'
            : value === 'Paused' ? 'bg-warning text-dark'
                : 'bg-secondary';

    const render = () => {
        timerEl.textContent = format(Math.max(0, elapsedSeconds));
        timerEl.classList.remove('expired', 'warn');
        sessionEl.textContent = status;
        sessionEl.className = 'badge rounded-pill px-3 py-2 ' + badgeClass(status);
    };

    setInterval(() => {
        if (status !== 'Running') return;
        elapsedSeconds++;
        render();
    }, 1000);
    render();

    connection.on('GlobalSessionState', state => {
        status = state.status;
        if (state.status === 'Running') elapsedSeconds = state.elapsedSeconds;
        render();
    });

    connection.on('SessionEnded', () => {
        status = 'Ended';
        elapsedSeconds = 0;
        render();
        if (!alertModalEl) return;
        document.getElementById('alertModalHeader').style.backgroundColor = 'var(--cams-danger)';
        document.getElementById('alertModalTitle').textContent = 'Session ended';
        document.getElementById('alertModalBody').textContent =
            'The laboratory session has been ended by the teacher. You will now be logged out.';
        alertModalEl.addEventListener('hidden.bs.modal', () => { location.href = '/Account/Logout'; }, { once: true });
        bootstrap.Modal.getOrCreateInstance(alertModalEl).show();
    });

    // Messages from the teacher surface as toasts that fade on their own. Only
    // the end-of-session notice still blocks, because it logs the student out
    // and must be acknowledged.
    connection.on('SendWarningPopup', warning => {
        CamsToast.show(warning.message || '', {
            type: 'error', title: warning.title || 'Warning', timeout: 10000
        });
    });

    connection.on('SendNotification', notification => {
        CamsToast.show(notification.message || '', {
            title: notification.title || 'Notification', timeout: 7000
        });
    });

    let dismissBroadcastToast = null;
    connection.on('BroadcastScreen', () => {
        if (dismissBroadcastToast) dismissBroadcastToast();
        dismissBroadcastToast = CamsToast.show(
            'Your teacher is sharing their screen. Watch the broadcast window on the Student Client.',
            { title: 'Screen broadcast', icon: 'bi-broadcast', timeout: 8000 });
    });
    connection.on('BroadcastStopped', () => {
        if (!dismissBroadcastToast) return;
        dismissBroadcastToast();
        dismissBroadcastToast = null;
    });

    // Three times the 5s server keep-alive.
    connection.serverTimeoutInMilliseconds = 15000;

    // A student whose connection drops stops receiving warnings and the
    // end-of-session notice, so this must be visible rather than silent.
    CamsConnection.watch(connection, { label: 'connection to your teacher' });
    CamsConnection.started(connection.start());
})();
