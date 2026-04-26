(function () {
    'use strict';

    var activeStatusIds = [];

    function openTicketPanel(ticketId) {
        if (!window.TicketPanel || typeof window.TicketPanel.open !== 'function') {
            return;
        }

        if (Number.isFinite(ticketId) && ticketId > 0) {
            window.TicketPanel.open(ticketId);
            return;
        }

        window.TicketPanel.open();
    }

    function getStatusModal() {
        return document.getElementById('ticketStatusModal');
    }

    function getSelectedTickets() {
        return Array.prototype.slice
            .call(document.querySelectorAll('[data-ticket-checkbox]:checked'))
            .map(function (checkbox) {
                var row = checkbox.closest('[data-ticket-row]');
                return {
                    id: checkbox.value,
                    title: row ? row.dataset.ticketTitle || '' : ''
                };
            })
            .filter(function (ticket) { return ticket.id; });
    }

    function updateBulkSelection() {
        var checkboxes = Array.prototype.slice.call(document.querySelectorAll('[data-ticket-checkbox]'));
        var selected = getSelectedTickets();
        var bulkBar = document.querySelector('[data-ticket-bulk-bar]');
        var count = document.getElementById('ticketSelectedCount');
        var selectAll = document.querySelector('[data-ticket-select-all]');

        if (count) count.textContent = selected.length.toString();

        if (bulkBar) {
            bulkBar.classList.toggle('hidden', selected.length === 0);
        }

        if (selectAll) {
            selectAll.checked = checkboxes.length > 0 && selected.length === checkboxes.length;
            selectAll.indeterminate = selected.length > 0 && selected.length < checkboxes.length;
        }
    }

    function clearSelection() {
        document.querySelectorAll('[data-ticket-checkbox]').forEach(function (checkbox) {
            checkbox.checked = false;
        });
        updateBulkSelection();
    }

    function setStatusError(message) {
        var error = document.getElementById('ticketStatusError');
        if (!error) return;

        if (!message) {
            error.textContent = '';
            error.classList.add('hidden');
            return;
        }

        error.textContent = message;
        error.classList.remove('hidden');
    }

    function selectedStatusRequiresDescription() {
        var select = document.querySelector('[data-ticket-status-select]');
        if (!select || !select.selectedOptions || select.selectedOptions.length === 0) {
            return false;
        }

        return select.selectedOptions[0].dataset.requiresDescription === 'true';
    }

    function updateDescriptionHint() {
        var hint = document.getElementById('ticketStatusDescriptionHint');
        var textarea = document.querySelector('[data-ticket-status-description-input]');
        var requiresDescription = selectedStatusRequiresDescription();

        if (hint) {
            hint.textContent = requiresDescription ? 'Obligatoria para resolver' : 'Opcional salvo resolución';
            hint.classList.toggle('text-rose-300', requiresDescription);
            hint.classList.toggle('text-slate-500', !requiresDescription);
        }

        if (textarea) {
            textarea.required = requiresDescription;
        }
    }

    function openStatusModal(options) {
        var modal = getStatusModal();
        if (!modal) return;

        var ids = options.ids || [];
        activeStatusIds = ids.filter(Boolean);

        var mode = document.getElementById('ticketStatusModalMode');
        var title = document.getElementById('ticketStatusModalTitle');
        var subtitle = document.getElementById('ticketStatusModalSubtitle');
        var currentWrap = document.getElementById('ticketStatusCurrentWrap');
        var currentLabel = document.getElementById('ticketStatusCurrentLabel');
        var select = document.querySelector('[data-ticket-status-select]');
        var description = document.querySelector('[data-ticket-status-description-input]');
        var confirmButton = document.querySelector('[data-ticket-status-confirm]');

        if (mode) mode.textContent = ids.length > 1 ? 'Cambio masivo de estado' : 'Cambio de estado';
        if (title) title.textContent = ids.length > 1 ? ids.length + ' tickets seleccionados' : (options.title || 'Actualizar ticket');
        if (subtitle) {
            subtitle.textContent = ids.length > 1
                ? 'La acción se aplicará a todos los tickets seleccionados.'
                : 'Seleccioná el nuevo estado y confirmá la acción.';
        }

        if (currentWrap && currentLabel) {
            currentWrap.classList.toggle('hidden', ids.length !== 1 || !options.currentLabel);
            currentLabel.textContent = options.currentLabel || '';
        }

        if (select) {
            select.value = options.newStatus || '';
        }

        if (description) {
            description.value = '';
        }

        if (confirmButton) {
            confirmButton.disabled = false;
        }

        setStatusError('');
        updateDescriptionHint();
        modal.classList.remove('hidden');
        modal.classList.add('flex');

        window.setTimeout(function () {
            if (select) select.focus();
        }, 0);
    }

    function closeStatusModal() {
        var modal = getStatusModal();
        if (!modal) return;

        modal.classList.add('hidden');
        modal.classList.remove('flex');
        activeStatusIds = [];
        setStatusError('');
    }

    function submitStatusForm() {
        var form = document.getElementById('ticketStatusForm');
        var idsInput = document.querySelector('[data-ticket-status-ids]');
        var valueInput = document.querySelector('[data-ticket-status-value]');
        var descriptionInput = document.querySelector('[data-ticket-status-description]');
        var select = document.querySelector('[data-ticket-status-select]');
        var textarea = document.querySelector('[data-ticket-status-description-input]');
        var confirmButton = document.querySelector('[data-ticket-status-confirm]');

        if (!form || !idsInput || !valueInput || !descriptionInput || !select) return;

        var selectedStatus = select.value;
        var description = textarea ? textarea.value.trim() : '';

        if (activeStatusIds.length === 0) {
            setStatusError('Seleccioná al menos un ticket.');
            return;
        }

        if (!selectedStatus) {
            setStatusError('Seleccioná el nuevo estado.');
            return;
        }

        if (selectedStatusRequiresDescription() && !description) {
            setStatusError('La descripción es obligatoria para marcar tickets como resueltos.');
            return;
        }

        idsInput.value = activeStatusIds.join(',');
        valueInput.value = selectedStatus;
        descriptionInput.value = description;

        if (confirmButton) {
            confirmButton.disabled = true;
        }

        form.submit();
    }

    function submitDeleteForm(ids) {
        var form = document.getElementById('ticketDeleteForm');
        var input = document.querySelector('[data-ticket-delete-ids]');
        if (!form || !input) return;

        input.value = ids.filter(Boolean).join(',');
        if (!input.value) return;

        form.submit();
    }

    function confirmDelete(ids, title) {
        var validIds = ids.filter(Boolean);
        if (validIds.length === 0) return;

        var message = validIds.length === 1
            ? '¿Eliminar el ticket #' + validIds[0] + (title ? ' - ' + title : '') + '? Esta acción lo ocultará del listado.'
            : '¿Eliminar ' + validIds.length + ' tickets seleccionados? Esta acción los ocultará del listado.';

        if (typeof window.openConfirmModal === 'function') {
            window.openConfirmModal(message, function () {
                submitDeleteForm(validIds);
            });
            return;
        }

        console.error('No se encontró el modal global de confirmación. Se canceló la eliminación.');
    }

    function initHorizontalScroll() {
        if (!window.TheBury || typeof window.TheBury.initHorizontalScrollAffordance !== 'function') {
            return;
        }

        document.querySelectorAll('[data-oc-scroll]').forEach(function (root) {
            window.TheBury.initHorizontalScrollAffordance(root);
        });
    }

    document.addEventListener('click', function (event) {
        if (!(event.target instanceof Element)) return;

        var panelTrigger = event.target.closest('[data-ticket-panel-open]');
        if (panelTrigger) {
            event.preventDefault();
            var ticketId = parseInt(panelTrigger.dataset.ticketPanelOpen || '', 10);
            openTicketPanel(ticketId);
            return;
        }

        var statusTrigger = event.target.closest('[data-ticket-status-action]');
        if (statusTrigger) {
            event.preventDefault();
            openStatusModal({
                ids: [statusTrigger.dataset.ticketId],
                title: 'Ticket #' + statusTrigger.dataset.ticketId,
                currentLabel: statusTrigger.dataset.ticketCurrentLabel,
                newStatus: statusTrigger.dataset.ticketNewStatus,
                newLabel: statusTrigger.dataset.ticketNewLabel,
                requiresDescription: statusTrigger.dataset.ticketRequiresDescription === 'true'
            });
            return;
        }

        var bulkStatusTrigger = event.target.closest('[data-ticket-bulk-status]');
        if (bulkStatusTrigger) {
            event.preventDefault();
            var selected = getSelectedTickets();
            openStatusModal({
                ids: selected.map(function (ticket) { return ticket.id; })
            });
            return;
        }

        var deleteTrigger = event.target.closest('[data-ticket-delete-action]');
        if (deleteTrigger) {
            event.preventDefault();
            confirmDelete([deleteTrigger.dataset.ticketId], deleteTrigger.dataset.ticketTitle || '');
            return;
        }

        var bulkDeleteTrigger = event.target.closest('[data-ticket-bulk-delete]');
        if (bulkDeleteTrigger) {
            event.preventDefault();
            confirmDelete(getSelectedTickets().map(function (ticket) { return ticket.id; }));
            return;
        }

        if (event.target.closest('[data-ticket-selection-clear]')) {
            event.preventDefault();
            clearSelection();
            return;
        }

        if (event.target.closest('[data-ticket-status-close]')) {
            event.preventDefault();
            closeStatusModal();
            return;
        }

        if (event.target.closest('[data-ticket-status-confirm]')) {
            event.preventDefault();
            submitStatusForm();
        }
    });

    document.addEventListener('change', function (event) {
        if (!(event.target instanceof Element)) return;

        if (event.target.matches('[data-ticket-select-all]')) {
            var checked = event.target.checked;
            document.querySelectorAll('[data-ticket-checkbox]').forEach(function (checkbox) {
                checkbox.checked = checked;
            });
            updateBulkSelection();
            return;
        }

        if (event.target.matches('[data-ticket-checkbox]')) {
            updateBulkSelection();
            return;
        }

        if (event.target.matches('[data-ticket-status-select]')) {
            updateDescriptionHint();
            setStatusError('');
        }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') {
            closeStatusModal();
        }
    });

    document.addEventListener('DOMContentLoaded', function () {
        var modal = getStatusModal();
        if (modal) {
            modal.addEventListener('click', function (event) {
                if (event.target === modal) {
                    closeStatusModal();
                }
            });
        }

        updateBulkSelection();
        initHorizontalScroll();

        if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
            window.TheBury.autoDismissToasts(5000);
        }
    });
})();
