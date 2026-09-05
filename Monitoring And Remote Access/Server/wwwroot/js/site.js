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
