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
