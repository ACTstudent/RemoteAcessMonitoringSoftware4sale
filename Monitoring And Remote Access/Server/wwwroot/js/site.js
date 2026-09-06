(() => {
    const modalElement = document.getElementById("confirmActionModal");
    if (!modalElement || typeof bootstrap === "undefined") return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const title = document.getElementById("confirmActionTitle");
    const message = document.getElementById("confirmActionMessage");
    const confirmButton = document.getElementById("confirmActionButton");
    let pendingConfirmation = null;

    modalElement.addEventListener("shown.bs.modal", () => {
        const backdrops = document.querySelectorAll(".modal-backdrop");
        if (backdrops.length > 1) {
            backdrops[backdrops.length - 1].style.zIndex = "1070";
        }
    });

    window.camsConfirm = ({
        title: promptTitle = "Confirm action",
        message: promptMessage = "Are you sure you want to continue?",
        confirmLabel = "Confirm",
        variant = "danger"
    } = {}) => {
        return new Promise(resolve => {
            if (pendingConfirmation) pendingConfirmation.resolve(false);
            pendingConfirmation = { resolve, confirmed: false };
            title.innerHTML = `<i class="bi bi-exclamation-circle me-2"></i>${escapeHtml(promptTitle)}`;
            message.textContent = promptMessage;
            confirmButton.textContent = confirmLabel;
            confirmButton.className = `btn btn-${variant} rounded-pill px-4`;
            modal.show();
        });
    };

    confirmButton.addEventListener("click", () => {
        if (pendingConfirmation) pendingConfirmation.confirmed = true;
        modal.hide();
    });

    modalElement.addEventListener("hidden.bs.modal", () => {
        const pending = pendingConfirmation;
        pendingConfirmation = null;
        if (document.querySelector(".modal.show")) {
            document.body.classList.add("modal-open");
        }
        if (!pending) return;
        pending.resolve(pending.confirmed);
    });

    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.dataset.confirm || form.dataset.confirmApproved === "true") return;

        event.preventDefault();
        const submitter = event.submitter;
        window.camsConfirm({
            title: form.dataset.confirmTitle || "Confirm action",
            message: form.dataset.confirm,
            confirmLabel: form.dataset.confirmLabel || "Confirm",
            variant: form.dataset.confirmVariant || "danger"
        }).then(confirmed => {
            if (!confirmed) return;
            form.dataset.confirmApproved = "true";
            if (submitter instanceof HTMLElement && submitter.form === form) form.requestSubmit(submitter);
            else form.requestSubmit();
        });
    });

    function escapeHtml(value) {
        const element = document.createElement("span");
        element.textContent = String(value);
        return element.innerHTML;
    }
})();

// Bulk entry tables start with a single row; "Add more rows" clones it on demand.
document.addEventListener('click', function (event) {
    var trigger = event.target.closest('[data-bulk-add]');
    if (!trigger) {
        return;
    }
    event.preventDefault();
    var body = document.querySelector(trigger.getAttribute('data-bulk-add'));
    if (!body || !body.rows.length) {
        return;
    }
    var row = body.rows[0].cloneNode(true);
    row.querySelectorAll('input').forEach(function (input) {
        input.value = '';
    });
    body.appendChild(row);
    var focusTarget = row.querySelector('input');
    if (focusTarget) {
        focusTarget.focus();
    }
});

// Bulk pickers: "Select all" toggle and type-to-filter over a checkbox list.
document.addEventListener('click', function (event) {
    var toggle = event.target.closest('[data-check-all]');
    if (!toggle) {
        return;
    }
    event.preventDefault();
    var list = document.querySelector(toggle.getAttribute('data-check-all'));
    if (!list) {
        return;
    }
    var visible = Array.prototype.filter.call(
        list.querySelectorAll('input[type="checkbox"]'),
        function (box) { return box.closest('label') && !box.closest('label').hidden; });
    var selectAll = visible.some(function (box) { return !box.checked; });
    visible.forEach(function (box) { box.checked = selectAll; });
    toggle.textContent = selectAll ? 'Clear all' : 'Select all';
});

document.addEventListener('input', function (event) {
    var field = event.target.closest('[data-filter-list]');
    if (!field) {
        return;
    }
    var list = document.querySelector(field.getAttribute('data-filter-list'));
    if (!list) {
        return;
    }
    var term = field.value.trim().toLowerCase();
    Array.prototype.forEach.call(list.querySelectorAll('label'), function (row) {
        row.hidden = term.length > 0 && row.textContent.toLowerCase().indexOf(term) === -1;
    });
});

// Show/hide toggle for password fields.
document.addEventListener('click', function (event) {
    var toggle = event.target.closest('[data-password-toggle]');
    if (!toggle) {
        return;
    }
    event.preventDefault();
    var field = document.querySelector(toggle.getAttribute('data-password-toggle'));
    if (!field) {
        return;
    }
    var reveal = field.type === 'password';
    field.type = reveal ? 'text' : 'password';
    toggle.setAttribute('aria-label', reveal ? 'Hide password' : 'Show password');
    var icon = toggle.querySelector('i');
    if (icon) {
        icon.className = reveal ? 'bi bi-eye-slash-fill' : 'bi bi-eye-fill';
    }
});

// Flash notifications: retire on their own, pause while the pointer is over
// them or focus is inside, so a message cannot vanish mid-read. Exposed as
// CamsToast so live events can raise one without a blocking dialog.
window.CamsToast = (function () {
    var DEFAULT_TIMEOUT = 5000;

    function region() {
        var existing = document.querySelector('.cams-toast-region');
        if (existing) {
            return existing;
        }
        var created = document.createElement('div');
        created.className = 'cams-toast-region';
        created.setAttribute('aria-live', 'polite');
        created.setAttribute('aria-atomic', 'true');
        document.body.appendChild(created);
        return created;
    }

    function setup(toast) {
        var lifetime = parseInt(toast.getAttribute('data-toast-timeout'), 10) || DEFAULT_TIMEOUT;
        var progress = toast.querySelector('.cams-toast-progress');
        var remaining = lifetime;
        var startedAt = Date.now();
        var timer = null;

        function dismiss() {
            if (toast.dataset.dismissed) {
                return;
            }
            toast.dataset.dismissed = 'true';
            window.clearTimeout(timer);
            toast.classList.add('is-leaving');
            var remove = function () {
                var host = toast.parentElement;
                toast.remove();
                if (host && host.children.length === 0) {
                    host.remove();
                }
            };
            toast.addEventListener('animationend', remove, { once: true });
            // Fallback for when the animation is suppressed.
            window.setTimeout(remove, 400);
        }

        function run(duration) {
            startedAt = Date.now();
            if (progress) {
                progress.style.transition = 'none';
                progress.style.transform = 'scaleX(1)';
                void progress.offsetWidth; // restart the transition from full width
                progress.style.transition = 'transform ' + duration + 'ms linear';
                progress.style.transform = 'scaleX(0)';
            }
            timer = window.setTimeout(dismiss, duration);
        }

        function hold() {
            window.clearTimeout(timer);
            remaining -= Date.now() - startedAt;
            if (progress) {
                var width = progress.getBoundingClientRect().width;
                var full = toast.getBoundingClientRect().width || 1;
                progress.style.transition = 'none';
                progress.style.transform = 'scaleX(' + (width / full) + ')';
            }
        }

        function resume() {
            run(Math.max(remaining, 1200));
        }

        toast.querySelectorAll('[data-toast-dismiss]').forEach(function (button) {
            button.addEventListener('click', dismiss);
        });
        toast.addEventListener('mouseenter', hold);
        toast.addEventListener('mouseleave', resume);
        toast.addEventListener('focusin', hold);
        toast.addEventListener('focusout', resume);

        run(lifetime);
        return dismiss;
    }

    // Builds a toast at runtime. Text is assigned, never parsed as markup.
    function show(message, options) {
        var settings = options || {};
        var type = settings.type === 'error' ? 'error' : 'success';
        var timeout = settings.timeout || (type === 'error' ? 9000 : DEFAULT_TIMEOUT);

        var toast = document.createElement('div');
        toast.className = 'cams-toast cams-toast-' + type;
        toast.setAttribute('role', type === 'error' ? 'alert' : 'status');
        toast.setAttribute('data-toast-timeout', String(timeout));

        var icon = document.createElement('i');
        icon.className = 'cams-toast-icon bi ' +
            (settings.icon || (type === 'error' ? 'bi-exclamation-triangle-fill' : 'bi-info-circle-fill'));
        icon.setAttribute('aria-hidden', 'true');

        var body = document.createElement('div');
        body.className = 'cams-toast-body';
        if (settings.title) {
            var heading = document.createElement('p');
            heading.className = 'cams-toast-title';
            heading.textContent = settings.title;
            body.appendChild(heading);
        }
        var text = document.createElement('p');
        text.className = 'cams-toast-text';
        text.textContent = message == null ? '' : String(message);
        body.appendChild(text);

        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'cams-toast-close';
        close.setAttribute('data-toast-dismiss', '');
        close.setAttribute('aria-label', 'Dismiss notification');
        close.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';

        var bar = document.createElement('span');
        bar.className = 'cams-toast-progress';
        bar.setAttribute('aria-hidden', 'true');

        toast.append(icon, body, close, bar);
        region().appendChild(toast);
        return setup(toast);
    }

    function init() {
        document.querySelectorAll('.cams-toast').forEach(setup);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    return { show: show };
})();

// Sidebar menu button. Below 992px it opens the sidebar as an overlay drawer;
// at wider sizes it collapses the sidebar out of the layout and back. The
// desktop choice is remembered, because every navigation is a full page load
// and the sidebar would otherwise reappear on each click.
(function () {
    var STORAGE_KEY = 'cams.sidebar.collapsed';
    var DESKTOP_QUERY = '(min-width: 992px)';

    function init() {
        var sidebar = document.getElementById('appSidebar');
        var toggle = document.getElementById('sidebarToggle');
        var backdrop = document.getElementById('sidebarBackdrop');
        if (!sidebar || !toggle) {
            return;
        }

        var desktop = window.matchMedia(DESKTOP_QUERY);
        var isDesktop = function () { return desktop.matches; };

        function readStored() {
            try {
                return window.localStorage.getItem(STORAGE_KEY) === 'true';
            } catch (error) {
                return false; // storage can be unavailable; default to visible
            }
        }

        function store(collapsed) {
            try {
                window.localStorage.setItem(STORAGE_KEY, String(collapsed));
            } catch (error) {
                // Not being able to remember the choice is not worth failing over.
            }
        }

        function setDrawerOpen(open) {
            sidebar.classList.toggle('show', open);
            if (backdrop) {
                backdrop.classList.toggle('show', open);
            }
            document.body.classList.toggle('sidebar-open', open);
            toggle.setAttribute('aria-expanded', String(open));
        }

        function setCollapsed(collapsed, remember) {
            document.body.classList.toggle('sidebar-collapsed', collapsed);
            toggle.setAttribute('aria-expanded', String(!collapsed));
            if (remember !== false) {
                store(collapsed);
            }
        }

        if (isDesktop()) {
            setCollapsed(readStored(), false);
        }

        toggle.addEventListener('click', function () {
            if (isDesktop()) {
                setCollapsed(!document.body.classList.contains('sidebar-collapsed'));
            } else {
                setDrawerOpen(!sidebar.classList.contains('show'));
            }
        });

        if (backdrop) {
            backdrop.addEventListener('click', function () { setDrawerOpen(false); });
        }

        // Following a link closes the drawer, but must not collapse the desktop
        // sidebar, or it would vanish on every navigation.
        sidebar.querySelectorAll('a').forEach(function (link) {
            link.addEventListener('click', function () {
                if (!isDesktop()) {
                    setDrawerOpen(false);
                }
            });
        });

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape' && !isDesktop()) {
                setDrawerOpen(false);
            }
        });

        var onBreakpointChange = function (event) {
            if (event.matches) {
                setDrawerOpen(false);
                setCollapsed(readStored(), false);
            } else {
                document.body.classList.remove('sidebar-collapsed');
                toggle.setAttribute('aria-expanded', 'false');
            }
        };

        if (typeof desktop.addEventListener === 'function') {
            desktop.addEventListener('change', onBreakpointChange);
        } else if (typeof desktop.addListener === 'function') {
            desktop.addListener(onBreakpointChange);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();

// Guards every state-changing form against a second submission while the first
// is still in flight. A double-click on "Create" used to create two students,
// two classes or two sessions, and the teacher had no way to tell which of the
// two was theirs.
(function () {
    'use strict';

    // Re-enabling after this long is a compromise: a submission that never
    // navigates (a blocked download, a dropped connection) must not leave a
    // permanently dead form, and a duplicate after this much time is a
    // deliberate second attempt rather than an impatient double-click.
    var RECOVERY_MS = 20000;

    function submitControls(form) {
        // form.elements covers controls associated by the form="" attribute,
        // which the bulk action buttons use from outside the form.
        return Array.prototype.filter.call(form.elements, function (element) {
            return element.type === 'submit';
        });
    }

    function release(form) {
        if (form.dataset.camsSubmitting !== 'true') return;
        delete form.dataset.camsSubmitting;
        form.removeAttribute('aria-busy');
        submitControls(form).forEach(function (control) {
            control.disabled = false;
            control.removeAttribute('aria-disabled');
        });
    }

    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement)) return;

        // The confirmation dialog cancels the first pass and re-issues the
        // submit once approved. That re-issue is the real submission.
        if (event.defaultPrevented) return;

        // GET forms are filters; re-running one is harmless and blocking it
        // would strand a teacher who wants to adjust a search.
        if ((form.getAttribute('method') || 'get').toLowerCase() !== 'post') return;
        if (form.dataset.noSubmitGuard === 'true') return;

        if (form.dataset.camsSubmitting === 'true') {
            event.preventDefault();
            return;
        }
        form.dataset.camsSubmitting = 'true';
        form.setAttribute('aria-busy', 'true');

        var controls = submitControls(form);
        // Disabled only after the browser has serialized this submission. A
        // submit button disabled during the event loses its own name and value,
        // and several forms here choose their action from exactly that.
        window.setTimeout(function () {
            controls.forEach(function (control) {
                control.disabled = true;
                control.setAttribute('aria-disabled', 'true');
            });
        }, 0);

        window.setTimeout(function () { release(form); }, RECOVERY_MS);
    });

    // Coming back with the browser's Back button restores the page from the
    // cache with the buttons still disabled, which would leave the form dead.
    window.addEventListener('pageshow', function (event) {
        if (!event.persisted) return;
        Array.prototype.forEach.call(document.querySelectorAll('form[aria-busy="true"]'), release);
    });
})();

// Connection status. Every portal page holds a SignalR connection - the teacher's
// carries the alert badge, the student's carries session state and any warning a
// teacher sends. When one dropped, nothing said so: the badge quietly stopped
// counting and a student stopped receiving messages while the page looked fine.
//
// CamsConnection attaches to a connection and reports its state in the header,
// staying out of the way while things are working.
window.CamsConnection = (function () {
    var indicator = null;

    function element() {
        if (indicator === null) {
            indicator = document.getElementById("connectionStatus") || false;
        }
        return indicator || null;
    }

    // "connected" hides the pill entirely; there is no news in working normally.
    function render(state, message) {
        var el = element();
        if (!el) {
            return;
        }
        if (state === "connected") {
            el.hidden = true;
            el.textContent = "";
            return;
        }
        el.hidden = false;
        el.className = "connection-status connection-status-" + state;
        el.textContent = message;
    }

    return {
        /**
         * Wires a SignalR connection to the header indicator.
         * Returns the connection so it can be chained.
         */
        watch: function (connection, options) {
            var settings = options || {};
            var label = settings.label || "connection";

            connection.onreconnecting(function () {
                render("waiting", "Reconnecting\u2026");
            });

            connection.onreconnected(function () {
                render("connected");
                if (window.CamsToast && settings.announceRecovery !== false) {
                    window.CamsToast.show("The " + label + " is back.", { timeout: 4000 });
                }
            });

            // Automatic reconnection has given up. A refresh is the recovery
            // path, so say so rather than leaving a dead page that looks live.
            connection.onclose(function () {
                render("lost", "Offline \u2014 refresh to reconnect");
            });

            return connection;
        },

        /** Called once the initial start() resolves or rejects. */
        started: function (promise, options) {
            var settings = options || {};
            render("waiting", "Connecting\u2026");
            return promise.then(function (value) {
                render("connected");
                return value;
            }).catch(function (error) {
                render("lost", "Offline \u2014 refresh to reconnect");
                if (settings.rethrow) {
                    throw error;
                }
            });
        }
    };
})();

// Puts a rejected submission back into its form.
//
// Validation failures redirect to the list with a message, which loses whatever
// was typed. PreserveSubmissionFilter carries the values across the redirect and
// this restores them, then reopens the dialog they came from - a form that is
// refilled but hidden behind a closed modal is no better than an empty one.
//
// Passwords are deliberately not carried, so any password field is left for the
// user to retype and is focused if there is one.
(function () {
    var holder = document.getElementById("preservedSubmission");
    if (!holder || !holder.textContent.trim()) {
        return;
    }

    var values;
    try {
        values = JSON.parse(holder.textContent);
    } catch (error) {
        return;   // Nothing sensible to restore; the error message still shows.
    }

    var names = Object.keys(values);
    if (!names.length) {
        return;
    }

    // The form holding the most of these fields is the one that was submitted.
    var best = null, bestScore = 0;
    Array.prototype.forEach.call(document.querySelectorAll("form"), function (form) {
        var score = 0;
        names.forEach(function (name) {
            if (form.querySelector("[name='" + CSS.escape(name) + "']")) {
                score++;
            }
        });
        if (score > bestScore) {
            bestScore = score;
            best = form;
        }
    });

    if (!best) {
        return;
    }

    var firstEmptyPassword = null;
    names.forEach(function (name) {
        var field = best.querySelector("[name='" + CSS.escape(name) + "']");
        if (!field) {
            return;
        }
        if (field.type === "checkbox" || field.type === "radio") {
            field.checked = field.value === values[name];
        } else {
            field.value = values[name];
        }
    });

    Array.prototype.forEach.call(best.querySelectorAll("input[type=password]"), function (field) {
        if (!firstEmptyPassword && !field.value) {
            firstEmptyPassword = field;
        }
    });

    // Reopen the dialog this form lives in, if it lives in one.
    var modal = best.closest(".modal");
    if (modal && window.bootstrap && window.bootstrap.Modal) {
        var instance = window.bootstrap.Modal.getOrCreateInstance(modal);
        modal.addEventListener("shown.bs.modal", function () {
            (firstEmptyPassword || best.querySelector("input, select, textarea")).focus();
        }, { once: true });
        instance.show();
    } else if (firstEmptyPassword) {
        firstEmptyPassword.focus();
    }
})();
