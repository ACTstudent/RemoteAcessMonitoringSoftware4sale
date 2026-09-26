// Teacher live monitoring page.
// Extracted from Views/Teacher/Monitoring.cshtml, where it was 387 of the
// view's 483 lines. It contained no Razor interpolation, so it moves across
// unchanged. Depends on the SignalR client and Bootstrap, and on the shared
// teacherHubConnection the layout creates.
const hub = window.teacherHubConnection;

const activeUnits = new Map();
let selectedConnectionId = null;
let stationFilter = "all";
let restrictionAlertCount = 0;
let remoteSupportActive = false;
let lastMouseEventAt = 0;
let broadcastStream = null;
let broadcastTimer = null;
let broadcastBusy = false;
const remoteCanvas = document.getElementById("remoteCanvas");
const remoteContext = remoteCanvas.getContext("2d");

function valueOf(object, camelName, pascalName) {
    return object?.[camelName] ?? object?.[pascalName] ?? "";
}

function updateCount() {
    document.getElementById("lblConnectedCount").textContent = activeUnits.size;
    document.getElementById("lblActiveCount").textContent = [...activeUnits.values()].filter(unit => unit.activityKnown && !unit.isIdle).length;
    document.getElementById("lblIdleCount").textContent = [...activeUnits.values()].filter(unit => unit.isIdle).length;
    document.getElementById("lblAlertCount").textContent = restrictionAlertCount;
    applyStationFilters();
}

function applyStationFilters() {
    const query = document.getElementById("gridSearch").value.trim().toLowerCase();
    let visible = 0;
    activeUnits.forEach((unit, connectionId) => {
        const column = document.getElementById(`unit-card-${connectionId}`);
        if (!column) return;
        const matchesState = stationFilter === "all"
            || (stationFilter === "active" && unit.activityKnown && !unit.isIdle)
            || (stationFilter === "idle" && unit.isIdle)
            || (stationFilter === "attention" && unit.hasAlert);
        const matchesSearch = `${unit.pcName} ${unit.studentId} ${unit.applicationName}`.toLowerCase().includes(query);
        column.style.display = matchesState && matchesSearch ? "" : "none";
        if (matchesState && matchesSearch) visible++;
    });
    document.getElementById("lblVisibleCount").textContent = `${visible} of ${activeUnits.size} stations shown`;
    document.getElementById("stationFilterEmpty").hidden = visible > 0 || activeUnits.size === 0;
}

function appendMonitoringAlert(message, station) {
    document.getElementById("monitoringAlertsEmpty")?.remove();
    const feed = document.getElementById("monitoringAlertFeed");
    const item = document.createElement("li");
    const title = document.createElement("div");
    title.textContent = message;
    const context = document.createElement("small");
    context.textContent = `${station || "Workstation"} · ${new Date().toLocaleTimeString()}`;
    item.append(title, context);
    feed.prepend(item);
    while (feed.children.length > 20) feed.lastElementChild.remove();
}

function showError(message) {
    const error = document.getElementById("hubError");
    error.textContent = message;
    error.classList.remove("d-none");
}

// Who is at a station: their full name, with the student number kept for the
// tooltip. The name arrives with the connection; a card first drawn from a
// screen frame is named when it does.
function studentLabel(unit) {
    if (unit?.displayName) return unit.displayName;
    return unit?.studentId ? `Student #${unit.studentId}` : "Connected student";
}

function setStudentIdentity(connectionId, displayName) {
    const unit = activeUnits.get(connectionId);
    if (!unit || !displayName) return;
    unit.displayName = displayName;
    const label = document.getElementById(`student-${connectionId}`);
    if (label) label.textContent = displayName;
    document.getElementById(`unit-card-${connectionId}`)?.querySelector(".workstation-card")
        ?.setAttribute("aria-label", `View ${unit.pcName || "workstation"}, ${displayName}`);
    if (selectedConnectionId === connectionId) updateSelectedView();
}

function createWorkstationCard(connectionId, studentId, pcName, displayName) {
    if (!connectionId) return;
    if (document.getElementById(`unit-card-${connectionId}`)) {
        setStudentIdentity(connectionId, displayName);
        return;
    }

    document.getElementById("emptyState")?.remove();
    activeUnits.set(connectionId, { studentId, displayName, pcName, lastFrame: null, lastFrameAt: 0, applicationName: "Unknown", activityKnown: false, isIdle: false, hasAlert: false, browsers: {} });

    const column = document.createElement("div");
    column.className = "col-xl-4 col-sm-6 unit-col";
    column.id = `unit-card-${connectionId}`;

    const card = document.createElement("div");
    card.className = "workstation-card h-100 d-flex flex-column";
    card.setAttribute("role", "button");
    card.tabIndex = 0;
    card.addEventListener("click", () => selectWorkstation(connectionId));
    card.addEventListener("keydown", event => {
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            selectWorkstation(connectionId);
        }
    });

    const header = document.createElement("div");
    header.className = "p-3 border-bottom d-flex justify-content-between align-items-center";
    const name = document.createElement("span");
    name.className = "fw-bold text-dark font-monospace small text-truncate";
    name.textContent = pcName || "Unknown workstation";
    card.setAttribute("aria-label", `View ${pcName || "workstation"}, ${displayName || `student ${studentId || "unknown"}`}`);
    const online = document.createElement("span");
    online.className = "badge badge-active ms-2";
    online.id = `status-${connectionId}`;
    online.textContent = "Online";
    header.append(name, online);

    const stream = document.createElement("div");
    stream.className = "stream-container";
    const image = document.createElement("img");
    image.id = `img-${connectionId}`;
    image.className = "stream-img";
    image.alt = `Screen stream for ${pcName || "workstation"}`;
    stream.appendChild(image);

    const footer = document.createElement("div");
    footer.className = "workstation-card-footer p-3 bg-light";
    const activity = document.createElement("span");
    activity.id = `activity-${connectionId}`;
    activity.className = "workstation-card-activity small text-dark text-truncate";
    activity.textContent = "Activity unavailable";
    const timestamp = document.createElement("span");
    timestamp.id = `time-${connectionId}`;
    timestamp.className = "small text-muted";
    timestamp.textContent = "Waiting for live frame";
    const browser = document.createElement("span");
    browser.id = `browser-${connectionId}`;
    browser.className = "small text-muted";
    browser.textContent = "Browser monitoring pending";
    const viewButton = document.createElement("span");
    viewButton.className = "btn btn-sm btn-outline-primary rounded-pill px-3";
    viewButton.innerHTML = '<i class="bi bi-arrows-fullscreen me-1"></i> View';
    const student = document.createElement("span");
    student.id = `student-${connectionId}`;
    student.className = "workstation-card-student small fw-semibold text-truncate";
    student.textContent = studentLabel({ studentId, displayName });
    if (studentId) student.title = `Student number ${studentId}`;
    footer.append(student, activity, timestamp, browser, viewButton);

    card.append(header, stream, footer);
    column.appendChild(card);
    document.getElementById("workstationsGrid").appendChild(column);
    updateCount();
}

function removeWorkstationCard(connectionId) {
    document.getElementById(`unit-card-${connectionId}`)?.remove();
    const disconnected = activeUnits.get(connectionId);
    if (disconnected) appendMonitoringAlert("Station disconnected", disconnected.pcName);
    activeUnits.delete(connectionId);
    if (selectedConnectionId === connectionId) {
        selectedConnectionId = null;
        remoteSupportActive = false;
        bootstrap.Modal.getInstance(document.getElementById("liveViewModal"))?.hide();
    }
    if (activeUnits.size === 0) {
        document.getElementById("workstationsGrid").innerHTML = '<div class="col-12 text-center py-5 text-muted" id="emptyState"><i class="bi bi-display-slash display-3 d-block opacity-50 mb-3 text-emerald-800"></i><h5 class="fw-bold">Awaiting Workstation Connections...</h5><p class="small">Authenticated student clients will appear here when they connect.</p></div>';
    }
    updateCount();
}

function updateWorkstationCard(connectionId, frame) {
    const studentId = valueOf(frame, "studentId", "StudentId");
    const pcName = valueOf(frame, "pcName", "PcName");
    const base64Image = valueOf(frame, "frameBase64", "FrameBase64");
    if (!base64Image) return;

    createWorkstationCard(connectionId, studentId, pcName);
    const unit = activeUnits.get(connectionId);
    if (unit) {
        unit.lastFrame = base64Image;
        unit.lastFrameAt = Date.now();
    }
    const image = document.getElementById(`img-${connectionId}`);
    if (image) image.src = `data:image/jpeg;base64,${base64Image}`;
    const timestamp = document.getElementById(`time-${connectionId}`);
    if (timestamp) timestamp.textContent = `Live frame ${new Date().toLocaleTimeString()}`;
    if (selectedConnectionId === connectionId) updateSelectedView();
}

function selectWorkstation(connectionId) {
    selectedConnectionId = connectionId;
    updateSelectedView();
    bootstrap.Modal.getOrCreateInstance(document.getElementById("liveViewModal")).show();
}

function updateSelectedView() {
    const unit = activeUnits.get(selectedConnectionId);
    if (!unit) return;
    document.getElementById("liveViewTitle").textContent = `Live View - ${unit.pcName || "Workstation"} (${studentLabel(unit)})`;
    document.getElementById("liveViewImage").src = unit.lastFrame ? `data:image/jpeg;base64,${unit.lastFrame}` : "";
    if (remoteSupportActive && unit.lastFrame) drawRemoteFrame(unit.lastFrame);
}

function drawRemoteFrame(base64Image) {
    const image = new Image();
    image.onload = () => {
        remoteCanvas.width = image.width;
        remoteCanvas.height = image.height;
        remoteContext.drawImage(image, 0, 0);
    };
    image.src = `data:image/jpeg;base64,${base64Image}`;
}

// --- FLOW-03: the three states, kept apart ---

/** Session lifecycle, and only that. Never written by a transport event. */
// The server reports "None" before any lab session has been started, which
// read as a stray word under the Status caption.
const SESSION_STATE_TEXT = { None: "Not started" };

function renderSessionState(status) {
    const label = document.getElementById("lblSessionState");
    if (!label) return;
    label.textContent = SESSION_STATE_TEXT[status] ?? status;
    label.dataset.sessionState = status;
    markSessionStateStale(false);
}

/**
 * Marks the session state as possibly out of date, which is honest while the
 * connection is down: the lab may have been paused or ended since the last
 * message arrived. It does not overwrite the state with a transport word.
 */
function markSessionStateStale(stale) {
    const label = document.getElementById("lblSessionState");
    if (!label) return;
    label.classList.toggle("state-stale", stale);
    label.title = stale
        ? "The connection dropped, so this may be out of date. It will refresh when the connection returns."
        : "";
}

/** Remote control, which is neither the transport nor the session. */
function setRemoteControlState(active, reasonIfStopped) {
    remoteSupportActive = active;
    document.getElementById("liveViewImage")?.classList.toggle("d-none", active);
    remoteCanvas?.classList.toggle("d-none", !active);
    const button = document.getElementById("remoteSupportButton");
    if (button) button.textContent = active ? "Stop Remote Support" : "Start Remote Support";

    const indicator = document.getElementById("lblRemoteControlState");
    if (indicator) {
        indicator.textContent = active ? "Control active" : "Not in control";
        indicator.classList.toggle("text-danger", active);
        indicator.classList.toggle("text-muted", !active);
    }
    if (!active && reasonIfStopped) {
        showError(`Remote control stopped because ${reasonIfStopped}.`);
    }
}

/** True when the hub is connected well enough to carry a command. */
function canSendCommand() {
    return hub && hub.state === signalR.HubConnectionState.Connected;
}

async function toggleRemoteSupport() {
    if (!selectedConnectionId) return;
    const nextState = !remoteSupportActive;

    // Commands are guarded rather than fired into a dropped connection, where
    // they would fail with a transport error the teacher cannot act on.
    if (!canSendCommand()) {
        showError("Not connected to the server, so that cannot be sent yet. It will be possible again once the connection returns.");
        return;
    }

    try {
        await hub.invoke(nextState ? "StartRemoteControl" : "StopRemoteControl", selectedConnectionId);
    } catch (error) {
        showError(`Remote support could not ${nextState ? "start" : "stop"}: ${error.message || error}`);
        return;
    }

    setRemoteControlState(nextState);
    const unit = activeUnits.get(selectedConnectionId);
    if (remoteSupportActive && unit?.lastFrame) drawRemoteFrame(unit.lastFrame);
    if (remoteSupportActive) remoteCanvas.focus();
}

async function stopRemoteSupport() {
    if (!remoteSupportActive || !selectedConnectionId) return;
    const targetConnectionId = selectedConnectionId;
    // Through the shared setter, so the control indicator cannot say "Control
    // active" after control has stopped.
    setRemoteControlState(false);
    if (canSendCommand()) {
        try { await hub.invoke("StopRemoteControl", targetConnectionId); }
        catch (error) { showError(`Remote support could not stop: ${error.message || error}`); }
    }
}

function sendRemoteInput(eventType, event) {
    if (!remoteSupportActive || !selectedConnectionId) return;
    if (eventType === "mousemove" && Date.now() - lastMouseEventAt < 35) return;
    lastMouseEventAt = Date.now();
    const rect = remoteCanvas.getBoundingClientRect();
    sendWorkstationInput(eventType, {
        x: Math.max(0, Math.min(10000, Math.round((event.clientX - rect.left) / Math.max(rect.width, 1) * 10000))),
        y: Math.max(0, Math.min(10000, Math.round((event.clientY - rect.top) / Math.max(rect.height, 1) * 10000))),
        keyCode: event.button ?? 0,
        isShift: Boolean(event.shiftKey)
    });
}

function sendWorkstationInput(eventType, coordinates) {
    hub.invoke("SendRemoteInput", selectedConnectionId, { eventType, ...coordinates }).catch(error => showError(`Remote input failed: ${error.message || error}`));
}

remoteCanvas.addEventListener("mousemove", event => sendRemoteInput("mousemove", event));
remoteCanvas.addEventListener("mousedown", event => sendRemoteInput("mousedown", event));
remoteCanvas.addEventListener("mouseup", event => sendRemoteInput("mouseup", event));
remoteCanvas.addEventListener("wheel", event => {
    if (!remoteSupportActive || !selectedConnectionId) return;
    sendWorkstationInput("scroll", { x: 0, y: 0, keyCode: event.deltaY > 0 ? 1 : -1, isShift: Boolean(event.shiftKey) });
    event.preventDefault();
}, { passive: false });
remoteCanvas.addEventListener("keydown", event => {
    if (!remoteSupportActive || !selectedConnectionId) return;
    sendWorkstationInput("keydown", { x: 0, y: 0, keyCode: event.keyCode, isShift: Boolean(event.shiftKey) });
    event.preventDefault();
});

// --- FLOW-04: what happened to the command, and to which workstation ---

/**
 * Every command gets an entry naming its target, so a result can never be read
 * against the wrong workstation. Previously a bare message appeared with no
 * indication of which station it referred to; a teacher who selected another
 * station while waiting had no way to tell.
 */
function describeTarget(connectionId) {
    const unit = activeUnits.get(connectionId);
    if (!unit) return "a workstation that is no longer connected";
    return unit.pcName || unit.studentId || connectionId;
}

function trackCommand(label, target) {
    const feed = document.getElementById("commandFeed");
    const entry = document.createElement("li");
    entry.className = "command-entry command-pending";
    entry.textContent = `${label} on ${target}: sending…`;
    entry.setAttribute("role", "status");
    feed?.prepend(entry);

    // Keep the list short enough to read at a glance.
    while (feed && feed.children.length > 6) feed.lastElementChild.remove();

    const settle = (cls, text) => {
        entry.className = `command-entry ${cls}`;
        entry.textContent = `${label} on ${target}: ${text}`;
    };

    return {
        acknowledged: message => settle("command-ok", message || "done"),
        failed: message => settle("command-failed", `refused — ${message}`),
        // Deliberately its own state. A transport error after the server took
        // the command means nobody knows whether it ran, and calling that
        // "failed" invites a teacher to shut a machine down twice.
        unknown: message =>
            settle("command-unknown",
                `outcome unknown — ${message}. Check the workstation before sending it again.`)
    };
}

// What each command does to the student's computer, said before it is sent.
// Restart is forced (shutdown /r /f /t 10) and shutdown runs on a 15-second
// timer, so open work is not saved in either case.
const DANGEROUS_COMMANDS = {
    RestartStudent: {
        title: "Restart workstation",
        message: "Restart this computer in 10 seconds? **Open applications close and unsaved work is lost.**",
        confirmLabel: "Restart"
    },
    ShutdownStudent: {
        title: "Shut down workstation",
        message: "Shut down this computer in 15 seconds? **Unsaved work is lost**, and it stays off until someone turns it on.",
        confirmLabel: "Shut down"
    },
    ForceLogout: {
        title: "Log out student",
        message: "Log this student out? **The computer returns to the CAMS sign-in screen.**",
        confirmLabel: "Log out"
    }
};

async function sendWorkstationCommand(method, dangerous = false) {
    if (!selectedConnectionId) return;

    const label = method.replace("Student", "").replace(/([a-z])([A-Z])/g, "$1 $2");
    const target = describeTarget(selectedConnectionId);

    if (dangerous) {
        const unit = activeUnits.get(selectedConnectionId);
        const prompt = DANGEROUS_COMMANDS[method] ?? {
            title: "Confirm workstation command",
            message: `Run **${label.toLowerCase()}** on this workstation?`,
            confirmLabel: "Run command"
        };
        const confirmed = await window.camsConfirm({
            ...prompt,
            subject: unit ? [studentLabel(unit), unit.pcName].filter(Boolean).join(" · ") : target
        });
        if (!confirmed) return;
    }

    if (!canSendCommand()) {
        showError("Not connected to the server, so that command was not sent.");
        return;
    }

    const tracked = trackCommand(label, target);
    try {
        const result = await hub.invoke(method, selectedConnectionId);
        const message = valueOf(result, "message", "Message") || "done";
        if (Boolean(valueOf(result, "succeeded", "Succeeded"))) {
            tracked.acknowledged(message);
            document.getElementById("hubError").classList.add("d-none");
        } else {
            // The server answered and said no. That is a refusal, not a mystery.
            tracked.failed(message);
            showError(message);
        }
    } catch (error) {
        // No answer came back. The command may or may not have reached the
        // workstation, and there is no automatic retry for exactly that reason.
        tracked.unknown(error.message || String(error));
    }
}

function openWarning(allStudents) {
    if (!allStudents && !selectedConnectionId) return showError("Select a workstation before sending a warning.");
    document.getElementById("warningTarget").value = allStudents ? "" : selectedConnectionId;
    bootstrap.Modal.getOrCreateInstance(document.getElementById("warningModal")).show();
}

async function sendWarning() {
    const title = document.getElementById("warningTitle").value.trim();
    const message = document.getElementById("warningMessage").value.trim();
    if (!title || !message) return showError("A warning title and message are required.");
    try {
        await hub.invoke("SendWarningPopup", document.getElementById("warningTarget").value,
            { type: "Warning", title, message, timestamp: new Date().toISOString() });
        bootstrap.Modal.getInstance(document.getElementById("warningModal"))?.hide();
        document.getElementById("warningMessage").value = "";
    } catch (error) {
        showError(`Warning delivery failed: ${error.message || error}`);
    }
}

async function toggleBroadcast() {
    if (broadcastStream) {
        stopBroadcast();
        return;
    }
    try {
        broadcastStream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });
        const video = document.createElement("video");
        video.srcObject = broadcastStream;
        video.muted = true;
        await video.play();
        const canvas = document.createElement("canvas");
        const context = canvas.getContext("2d");
        const sendFrame = async () => {
            if (broadcastBusy || !broadcastStream || hub.state !== signalR.HubConnectionState.Connected) return;
            broadcastBusy = true;
            try {
                const scale = Math.min(1, 1280 / Math.max(video.videoWidth, 1));
                canvas.width = Math.max(1, Math.round(video.videoWidth * scale));
                canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
                context.drawImage(video, 0, 0, canvas.width, canvas.height);
                await hub.invoke("BroadcastScreen", canvas.toDataURL("image/jpeg", 0.65).split(",")[1]);
            } catch (error) {
                showError(`Screen broadcast failed: ${error.message || error}`);
            } finally {
                broadcastBusy = false;
            }
        };
        broadcastTimer = setInterval(sendFrame, 250);
        broadcastStream.getVideoTracks()[0].addEventListener("ended", stopBroadcast);
        document.getElementById("broadcastButton").innerHTML = '<i class="bi bi-stop-circle me-1"></i> Stop Broadcast';
    } catch (error) {
        stopBroadcast();
        showError(`Screen sharing was not started: ${error.message || error}`);
    }
}

async function stopBroadcast() {
    if (broadcastTimer) clearInterval(broadcastTimer);
    broadcastTimer = null;
    broadcastStream?.getTracks().forEach(track => track.stop());
    broadcastStream = null;
    document.getElementById("broadcastButton").innerHTML = '<i class="bi bi-broadcast me-1"></i> Broadcast Screen';
    if (hub.state === signalR.HubConnectionState.Connected) {
        try { await hub.invoke("StopBroadcast"); } catch { }
    }
}

async function bulkCommand(method, label) {
    const targets = [...document.querySelectorAll(".unit-col")]
        .filter(column => column.style.display !== "none")
        .map(column => column.id.replace("unit-card-", ""));
    if (!targets.length) return;

    const confirmed = await window.camsConfirm({
        title: "Confirm bulk command",
        subject: `${targets.length} visible workstation${targets.length === 1 ? "" : "s"}`,
        message: `Run **${label}** on every workstation shown on the monitoring wall?`,
        confirmLabel: "Run command"
    });
    if (!confirmed) return;

    if (!canSendCommand()) {
        showError("Not connected to the server, so that command was not sent.");
        return;
    }

    // Named by count rather than by station: this is one call covering many, and
    // pretending to report per-station would be a fiction the hub does not
    // support.
    const tracked = trackCommand(label, `${targets.length} visible workstations`);
    try {
        await hub.invoke(method, targets);
        tracked.acknowledged("the server accepted it for every listed workstation");
    } catch (error) {
        // Partial failure is the likely case here - some workstations may have
        // been reached before the error. Saying "failed" would be wrong.
        tracked.unknown(error.message || String(error));
    }
}

function updateActivity(connectionId, applicationName, isIdle) {
    const unit = activeUnits.get(connectionId);
    if (!unit) return;
    unit.activityKnown = true;
    if (applicationName) unit.applicationName = applicationName;
    if (typeof isIdle === "boolean") unit.isIdle = isIdle;
    const activity = document.getElementById(`activity-${connectionId}`);
    if (activity) activity.textContent = unit.isIdle ? "Idle" : unit.applicationName || "Active";
    const card = document.getElementById(`unit-card-${connectionId}`)?.querySelector(".workstation-card");
    card?.classList.toggle("station-idle", unit.isIdle);
    const status = document.getElementById(`status-${connectionId}`);
    if (status) {
        status.textContent = unit.isIdle ? "Idle" : "Active";
        status.className = `badge ms-2 ${unit.isIdle ? "bg-warning text-dark" : "badge-active"}`;
    }
    updateCount();
}

// Plain labels for the collector modes, rendered by the server from
// BrowserMonitoringDisplay so the tiles and the history page cannot drift into
// describing the same state differently.
const BROWSER_MODES = (() => {
    try {
        return JSON.parse(document.getElementById("browserModeLabels")?.textContent || "{}");
    } catch {
        return {};
    }
})();

// The hub sends the mode as either the enum name or its ordinal, depending on
// the serializer in play, so both are accepted.
const MODE_NAMES = ["ManagedProtocol", "WindowTitleFallback", "Unavailable"];

function modeNameOf(rawMode) {
    if (typeof rawMode === "number" || /^\d+$/.test(String(rawMode ?? ""))) {
        return MODE_NAMES[Number(rawMode)] ?? "Unavailable";
    }
    return MODE_NAMES.includes(rawMode) ? rawMode : "Unavailable";
}

function updateBrowserStatus(status) {
    const connectionId = valueOf(status, "connectionId", "ConnectionId");
    const unit = activeUnits.get(connectionId);
    if (!unit) return;
    const browserName = valueOf(status, "browser", "Browser") || "browser";
    const modeName = modeNameOf(valueOf(status, "mode", "Mode"));
    unit.browsers[browserName] = modeName;

    const label = document.getElementById(`browser-${connectionId}`);
    if (!label) return;

    const described = Object.entries(unit.browsers).map(([name, value]) => {
        const shown = BROWSER_MODES[value]?.label || value;
        return `${name}: ${shown}`;
    });
    label.textContent = described.join(" | ");

    // The difference between "titles only" and "not recorded" changes what a
    // teacher should do, so the reason is available on hover rather than lost.
    const explanations = Object.values(unit.browsers)
        .map(value => BROWSER_MODES[value]?.explanation)
        .filter(Boolean);
    label.title = Array.from(new Set(explanations)).join(" ");
}

async function loadLiveState() {
    try {
        const response = await fetch("/Teacher/LiveState", { credentials: "same-origin" });
        if (!response.ok) return;
        const state = await response.json();
        for (const student of (state.students ?? state.Students ?? [])) {
            const connectionId = valueOf(student, "connectionId", "ConnectionId");
            createWorkstationCard(connectionId, valueOf(student, "studentId", "StudentId"), valueOf(student, "pcName", "PcName"), valueOf(student, "displayName", "DisplayName"));
        }
        for (const app of (state.apps ?? state.Apps ?? [])) {
            updateActivity(valueOf(app, "connectionId", "ConnectionId"), valueOf(app, "applicationName", "ApplicationName"), false);
        }
        for (const idle of (state.idle ?? state.Idle ?? [])) {
            updateActivity(valueOf(idle, "connectionId", "ConnectionId"), null, Boolean(valueOf(idle, "isIdle", "IsIdle")));
        }
        for (const browser of (state.browsers ?? state.Browsers ?? [])) updateBrowserStatus(browser);
    } catch {
        showError("The initial monitoring state could not be loaded.");
    }
}

hub.on("ReceiveScreenFrame", (connectionId, frame) => updateWorkstationCard(connectionId, frame));

// A screen that cannot be captured sends nothing, so silence is the only signal
// available. Five seconds is well clear of the capture interval and of a slow
// frame, without letting a locked workstation sit unremarked.
const STALE_AFTER_MS = 5000;

function describeGap(milliseconds) {
    const seconds = Math.round(milliseconds / 1000);
    if (seconds < 60) return `${seconds}s ago`;
    const minutes = Math.floor(seconds / 60);
    return `${minutes}m ${seconds % 60}s ago`;
}

function reviewFrameFreshness() {
    const now = Date.now();
    activeUnits.forEach((unit, connectionId) => {
        const card = document.getElementById(`unit-card-${connectionId}`)?.querySelector(".workstation-card");
        const timestamp = document.getElementById(`time-${connectionId}`);
        if (!card || !timestamp) return;

        // A unit that has never sent a frame is still connecting, not stale.
        const gap = unit.lastFrameAt ? now - unit.lastFrameAt : 0;
        const stale = unit.lastFrameAt > 0 && gap > STALE_AFTER_MS;

        card.classList.toggle("workstation-stale", stale);
        card.setAttribute("aria-describedby", stale ? `time-${connectionId}` : "");
        if (stale) {
            timestamp.textContent = `Screen not updating - last frame ${describeGap(gap)}`;
            timestamp.classList.add("text-warning-emphasis", "fw-semibold");
        } else {
            timestamp.classList.remove("text-warning-emphasis", "fw-semibold");
        }
    });
}

setInterval(reviewFrameFreshness, 1000);
hub.on("ActiveAppReceived", app => updateActivity(valueOf(app, "connectionId", "ConnectionId"), valueOf(app, "applicationName", "ApplicationName"), false));
hub.on("WebsiteActivityReceived", website => updateActivity(valueOf(website, "connectionId", "ConnectionId"), `Web: ${valueOf(website, "domain", "Domain")}`, false));
hub.on("BrowserMonitoringStatusReceived", updateBrowserStatus);
hub.on("IdleStatusReceived", idle => updateActivity(valueOf(idle, "connectionId", "ConnectionId"), null, Boolean(valueOf(idle, "isIdle", "IsIdle"))));
hub.on("InfractionDetected", infraction => {
    restrictionAlertCount++;
    const connectionId = valueOf(infraction, "connectionId", "ConnectionId");
    const unit = activeUnits.get(connectionId);
    if (unit) unit.hasAlert = true;
    appendMonitoringAlert(`Restriction alert: ${valueOf(infraction, "target", "Target") || "restricted activity detected"}`, unit?.pcName);
    updateCount();
    const card = document.getElementById(`unit-card-${connectionId}`)?.querySelector(".workstation-card");
    card?.classList.add("station-alert");
    card?.classList.add("border", "border-danger", "border-3");
    if (card) setTimeout(() => card.classList.remove("border", "border-danger", "border-3"), 8000);
});
hub.on("StudentConnected", student => createWorkstationCard(valueOf(student, "connectionId", "ConnectionId"), valueOf(student, "studentId", "StudentId"), valueOf(student, "pcName", "PcName"), valueOf(student, "displayName", "DisplayName")));
hub.on("StudentDisconnected", connectionId => removeWorkstationCard(connectionId));
// The server sends the lab's elapsed time when it changes state, not every
// second, so a running lab is counted forward here between messages.
let labElapsedSeconds = 0;
let labRunning = false;

function renderLabTimer() {
    const timer = document.getElementById("lblSessionTimer");
    if (!timer) return;
    timer.textContent = `${String(Math.floor(labElapsedSeconds / 60)).padStart(2, "0")}:${String(labElapsedSeconds % 60).padStart(2, "0")}`;
}

setInterval(() => {
    if (!labRunning) return;
    labElapsedSeconds++;
    renderLabTimer();
}, 1000);

hub.on("GlobalSessionState", state => {
    const status = valueOf(state, "status", "Status") || "Ready";
    renderSessionState(status);
    labElapsedSeconds = Number(valueOf(state, "elapsedSeconds", "ElapsedSeconds")) || 0;
    labRunning = status === "Running";
    renderLabTimer();
});

// FLOW-03. These three are different things and used to share one label.
//
// The transport dropping was written into lblSessionState as "Reconnecting",
// and onreconnected then wrote "Ready" - so a paused session came back from any
// blip reading Ready, and a teacher had no way to tell a paused lab from a
// dropped connection. The transport now reports itself in the header, where
// CamsConnection already owns it; the session label only ever shows what the
// server said about the session.
hub.onreconnecting(() => {
    // The session state on screen is now only as good as the last message.
    markSessionStateStale(true);
});

hub.onreconnected(async () => {
    // Reconcile rather than guess. Whatever happened while the connection was
    // down, the server knows and this page does not.
    try {
        await loadLiveState();
        markSessionStateStale(false);
    } catch {
        markSessionStateStale(true);
    }
});

hub.onclose(() => {
    markSessionStateStale(true);
    setRemoteControlState(false, "the connection closed");
    showError("The monitoring connection closed. Refresh the page to reconnect.");
});

document.getElementById("gridSearch")?.addEventListener("input", applyStationFilters);
document.querySelectorAll("[data-station-filter]").forEach(button => {
    button.addEventListener("click", () => {
        stationFilter = button.dataset.stationFilter;
        document.querySelectorAll("[data-station-filter]").forEach(item => {
            const selected = item === button;
            item.classList.toggle("active", selected);
            item.setAttribute("aria-pressed", String(selected));
        });
        applyStationFilters();
    });
});
document.getElementById("liveViewModal")?.addEventListener("hide.bs.modal", stopRemoteSupport);

window.teacherHubStarted.then(loadLiveState).catch(error => showError(`Monitoring connection failed: ${error.message || error}`));
