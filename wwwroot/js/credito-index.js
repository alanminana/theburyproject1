/* credito-index.js — Tabs, filtros y affordance para Credito/Index */

document.addEventListener('DOMContentLoaded', function () {
    var creditoModule = window.TheBury && window.TheBury.CreditoModule;
    var tabButtons = Array.from(document.querySelectorAll('[data-credito-tab]'));
    var creditosPanel = document.getElementById('tab-creditos');
    var morasPanel = document.getElementById('tab-moras');
    var clientePanel = document.querySelector('[data-credito-cliente-panel]');
    var clientePanelContent = document.querySelector('[data-credito-cliente-panel-content]');
    var lastClientePanelTrigger = null;
    var selectedCuotas = new Map();
    var isPagoMultipleSubmitting = false;
    var isPagoMultipleLocked = false;
    var currencyFormatter = new Intl.NumberFormat('es-AR', {
        style: 'currency',
        currency: 'ARS',
        minimumFractionDigits: 2
    });

    function setTabButtonState(button, isActive) {
        button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
        button.classList.toggle('border-primary', isActive);
        button.classList.toggle('text-primary', isActive);
        button.classList.toggle('bg-primary/10', isActive);
        button.classList.toggle('border-transparent', !isActive);
        button.classList.toggle('text-slate-400', !isActive);
    }

    function activateTab(target) {
        tabButtons.forEach(function (button) {
            setTabButtonState(button, button.getAttribute('data-credito-tab') === target);
        });

        if (creditosPanel) {
            creditosPanel.classList.toggle('hidden', target !== 'creditos');
        }

        if (morasPanel) {
            morasPanel.classList.toggle('hidden', target !== 'moras');
        }

        if (creditoModule && typeof creditoModule.refreshScrollAffordance === 'function') {
            creditoModule.refreshScrollAffordance(document);
            window.setTimeout(function () {
                creditoModule.refreshScrollAffordance(document);
            }, 120);
        }
    }

    function getClienteTemplate(clienteId) {
        if (!clienteId) {
            return null;
        }

        return document.querySelector('template[data-credito-cliente-panel-template="' + clienteId + '"]');
    }

    function parseAmount(value) {
        var parsed = Number.parseFloat(value || '0');
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function formatCurrency(value) {
        return currencyFormatter.format(value || 0);
    }

    function setText(root, selector, value) {
        if (!root) {
            return;
        }

        var element = root.querySelector(selector);
        if (element) {
            element.textContent = value;
        }
    }

    function getPanelScope(element) {
        return element ? element.closest('[data-credito-cliente-panel-content]') : clientePanelContent;
    }

    function getAntiForgeryToken() {
        var tokenInput = document.querySelector('[data-credito-antiforgery] input[name="__RequestVerificationToken"]')
            || document.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput ? tokenInput.value : '';
    }

    function getSelectedMedioPago(scope) {
        var root = scope || clientePanelContent;
        var medioPago = root ? root.querySelector('[data-credito-medio-pago]') : null;
        return medioPago ? medioPago.value.trim() : '';
    }

    function setPagoStatus(scope, type, message) {
        var root = scope || clientePanelContent;
        var status = root ? root.querySelector('[data-credito-pago-status]') : null;
        if (!status) {
            return;
        }

        status.textContent = message || '';
        status.classList.toggle('hidden', !message);
        status.classList.toggle('text-emerald-400', type === 'success');
        status.classList.toggle('text-red-400', type === 'error');
        status.classList.toggle('text-amber-400', type !== 'success' && type !== 'error');
    }

    function setPagoPendienteVisible(scope, isVisible) {
        setPagoStatus(scope, 'warning', isVisible ? 'Pago múltiple pendiente de backend' : '');
    }

    function readCuotaSelection(input) {
        return {
            id: input.getAttribute('data-cuota-id'),
            creditoId: input.getAttribute('data-credito-id'),
            subtotal: parseAmount(input.getAttribute('data-cuota-subtotal')),
            mora: parseAmount(input.getAttribute('data-cuota-mora')),
            total: parseAmount(input.getAttribute('data-cuota-total'))
        };
    }

    function applyCuotaSelectedState(input, isSelected) {
        input.checked = isSelected;

        var row = input.closest('[data-credito-cliente-cuota]');
        if (row) {
            row.classList.toggle('bg-primary/5', isSelected);
        }
    }

    function restoreVisibleCuotas(scope) {
        var root = scope || clientePanelContent;
        if (!root) {
            return;
        }

        root.querySelectorAll('[data-credito-cuota-selector]').forEach(function (input) {
            if (input.disabled) {
                return;
            }

            applyCuotaSelectedState(input, selectedCuotas.has(input.getAttribute('data-cuota-id')));
        });
    }

    function updatePagoResumen(scope) {
        var root = scope || clientePanelContent;
        if (!root) {
            return;
        }

        var selectedItems = Array.from(selectedCuotas.values());
        var creditos = new Set();
        var subtotal = 0;
        var mora = 0;
        var total = 0;

        selectedItems.forEach(function (item) {
            if (item.creditoId) {
                creditos.add(item.creditoId);
            }

            subtotal += item.subtotal;
            mora += item.mora;
            total += item.total;
        });

        setText(root, '[data-credito-resumen-cuotas]', String(selectedItems.length));
        setText(root, '[data-credito-resumen-creditos]', String(creditos.size));
        setText(root, '[data-credito-resumen-subtotal]', formatCurrency(subtotal));
        setText(root, '[data-credito-resumen-mora]', formatCurrency(mora));
        setText(root, '[data-credito-resumen-total]', formatCurrency(total));

        var registerButton = root.querySelector('[data-credito-registrar-pago-multiple]');
        if (registerButton) {
            registerButton.disabled = isPagoMultipleSubmitting || isPagoMultipleLocked || selectedItems.length === 0;
        }

        if (selectedItems.length === 0) {
            setPagoPendienteVisible(root, false);
        }
    }

    function handleCuotaSelection(input) {
        if (!input || input.disabled) {
            return;
        }

        var scope = getPanelScope(input);
        var item = readCuotaSelection(input);
        if (!item.id) {
            return;
        }

        if (input.checked) {
            selectedCuotas.set(item.id, item);
        } else {
            selectedCuotas.delete(item.id);
        }

        applyCuotaSelectedState(input, input.checked);
        setPagoPendienteVisible(scope, false);
        updatePagoResumen(scope);
    }

    function openClientePanel(clienteId, trigger) {
        var template = getClienteTemplate(clienteId);
        if (!clientePanel || !clientePanelContent || !template) {
            return;
        }

        lastClientePanelTrigger = trigger || null;
        selectedCuotas.clear();
        isPagoMultipleSubmitting = false;
        isPagoMultipleLocked = false;
        clientePanel.removeAttribute('data-credito-panel-locked');
        clientePanelContent.innerHTML = template.innerHTML;
        clientePanelContent.setAttribute('data-credito-cliente-id', clienteId);
        clientePanel.classList.remove('hidden');
        clientePanel.setAttribute('aria-hidden', 'false');
        document.body.classList.add('overflow-hidden');
        updatePagoResumen(clientePanelContent);

        window.requestAnimationFrame(function () {
            var closeButton = clientePanel.querySelector('button[data-credito-cliente-panel-close]');
            if (closeButton) {
                closeButton.focus();
            }
        });
    }

    function closeClientePanel() {
        if (!clientePanel || clientePanel.classList.contains('hidden')) {
            return;
        }

        if (isPagoMultipleLocked) {
            return;
        }

        clientePanel.classList.add('hidden');
        clientePanel.setAttribute('aria-hidden', 'true');
        document.body.classList.remove('overflow-hidden');

        if (clientePanelContent) {
            clientePanelContent.innerHTML = '';
            clientePanelContent.removeAttribute('data-credito-cliente-id');
        }

        selectedCuotas.clear();
        isPagoMultipleSubmitting = false;
        isPagoMultipleLocked = false;

        if (clientePanel) {
            clientePanel.removeAttribute('data-credito-panel-locked');
        }

        if (lastClientePanelTrigger) {
            lastClientePanelTrigger.focus();
            lastClientePanelTrigger = null;
        }
    }

    function syncCreditoToggleAria(scope) {
        var root = scope || document;
        root.querySelectorAll('[data-credito-credit-toggle]').forEach(function (button) {
            button.setAttribute(
                'aria-expanded',
                button.getAttribute('data-credito-credit-expanded') === 'true' ? 'true' : 'false'
            );
        });
    }

    function toggleCreditoCuotas(button) {
        var creditBlock = button ? button.closest('[data-credito-cliente-credit]') : null;
        if (!creditBlock) {
            return;
        }

        var container = creditBlock.querySelector('[data-credito-cuotas-container]');
        var template = creditBlock.querySelector('template[data-credito-cuotas-template]');
        var label = button.querySelector('[data-credito-credit-toggle-label]');
        var icon = button.querySelector('[data-credito-credit-toggle-icon]');
        var isExpanded = button.getAttribute('data-credito-credit-expanded') === 'true';

        if (!container || !template) {
            return;
        }

        if (isExpanded) {
            container.innerHTML = '';
            container.classList.add('hidden');
            button.setAttribute('data-credito-credit-expanded', 'false');
            button.setAttribute('aria-expanded', 'false');
            if (label) label.textContent = 'Ver cuotas';
            if (icon) icon.textContent = 'expand_more';
            window.setTimeout(function () {
                syncCreditoToggleAria(creditBlock);
            }, 0);
            return;
        }

        if (!container.hasChildNodes()) {
            container.appendChild(template.content.cloneNode(true));
            restoreVisibleCuotas(container);
        }

        container.classList.remove('hidden');
        button.setAttribute('data-credito-credit-expanded', 'true');
        button.setAttribute('aria-expanded', 'true');
        if (label) label.textContent = 'Ocultar cuotas';
        if (icon) icon.textContent = 'expand_less';
        updatePagoResumen(getPanelScope(button));
        window.setTimeout(function () {
            syncCreditoToggleAria(creditBlock);
        }, 0);
    }

    function getSelectedCuotaIds() {
        return Array.from(selectedCuotas.keys())
            .map(function (id) { return Number.parseInt(id, 10); })
            .filter(function (id) { return Number.isInteger(id) && id > 0; });
    }

    function setRegisterButtonSubmitting(scope, isSubmitting) {
        var root = scope || clientePanelContent;
        var button = root ? root.querySelector('[data-credito-registrar-pago-multiple]') : null;
        var label = button ? button.querySelector('[data-credito-registrar-pago-label]') : null;
        var icon = button ? button.querySelector('[data-credito-registrar-pago-icon]') : null;

        isPagoMultipleSubmitting = isSubmitting;

        if (label) {
            label.textContent = isSubmitting ? 'Registrando...' : 'Registrar pago';
        }

        if (icon) {
            icon.textContent = isSubmitting ? 'progress_activity' : 'payments';
        }

        updatePagoResumen(root);
    }

    function setRegisterButtonResult(scope, labelText, iconText) {
        var root = scope || clientePanelContent;
        var button = root ? root.querySelector('[data-credito-registrar-pago-multiple]') : null;
        var label = button ? button.querySelector('[data-credito-registrar-pago-label]') : null;
        var icon = button ? button.querySelector('[data-credito-registrar-pago-icon]') : null;

        if (label) {
            label.textContent = labelText;
        }

        if (icon) {
            icon.textContent = iconText;
        }
    }

    function setPanelLocked(scope, isLocked) {
        var root = scope || clientePanelContent;

        isPagoMultipleLocked = isLocked;

        if (clientePanel) {
            if (isLocked) {
                clientePanel.setAttribute('data-credito-panel-locked', 'true');
            } else {
                clientePanel.removeAttribute('data-credito-panel-locked');
            }
        }

        if (!root) {
            return;
        }

        root.setAttribute('aria-busy', isLocked ? 'true' : 'false');
        root.querySelectorAll('button, input, select, textarea').forEach(function (control) {
            if (isLocked) {
                if (!control.hasAttribute('data-credito-locked-was-disabled')) {
                    control.setAttribute('data-credito-locked-was-disabled', control.disabled ? 'true' : 'false');
                }

                control.disabled = true;
                return;
            }

            var wasDisabled = control.getAttribute('data-credito-locked-was-disabled');
            if (wasDisabled !== null) {
                control.disabled = wasDisabled === 'true';
                control.removeAttribute('data-credito-locked-was-disabled');
            }
        });
    }

    function clearCuotaSelection(scope) {
        var root = scope || clientePanelContent;
        if (!root) {
            selectedCuotas.clear();
            return;
        }

        root.querySelectorAll('[data-credito-cuota-selector]').forEach(function (input) {
            applyCuotaSelectedState(input, false);
        });

        selectedCuotas.clear();
        updatePagoResumen(root);
    }

    function markPaidCuotas(scope, cuotaIds) {
        var root = scope || clientePanelContent;
        var paidIds = new Set((cuotaIds || []).map(function (id) { return String(id); }));
        if (!root || paidIds.size === 0) {
            return;
        }

        root.querySelectorAll('[data-credito-cuota-selector]').forEach(function (input) {
            if (!paidIds.has(input.getAttribute('data-cuota-id'))) {
                return;
            }

            applyCuotaSelectedState(input, false);
            input.disabled = true;

            var row = input.closest('[data-credito-cliente-cuota]');
            if (row) {
                row.classList.add('opacity-60');
                row.classList.remove('hover:bg-slate-800/40');
            }
        });
    }

    function getResponseErrors(payload) {
        if (!payload) {
            return ['No se pudo procesar la respuesta del servidor.'];
        }

        if (Array.isArray(payload.errors)) {
            return payload.errors;
        }

        if (Array.isArray(payload.Errors)) {
            return payload.Errors;
        }

        if (payload.error) {
            return [payload.error];
        }

        if (payload.message) {
            return [payload.message];
        }

        if (payload.title) {
            return [payload.title];
        }

        return ['No se pudo registrar el pago múltiple.'];
    }

    function getResultCuotaIds(payload, fallbackIds) {
        var data = payload ? (payload.data || payload.Data) : null;
        var cuotaIds = data ? (data.cuotaIds || data.CuotaIds) : null;
        return Array.isArray(cuotaIds) && cuotaIds.length > 0 ? cuotaIds : fallbackIds;
    }

    function isStalePagoError(message) {
        var normalized = (message || '').toLowerCase();
        return normalized.indexOf('ya está pagada') >= 0
            || normalized.indexOf('ya esta pagada') >= 0
            || normalized.indexOf('modificada') >= 0
            || normalized.indexOf('modificadas') >= 0
            || normalized.indexOf('concurrencia') >= 0
            || normalized.indexOf('no se encontraron cuotas') >= 0
            || normalized.indexOf('no tiene saldo pendiente') >= 0
            || normalized.indexOf('está cancelada') >= 0
            || normalized.indexOf('esta cancelada') >= 0;
    }

    async function submitPagoMultiple(button) {
        if (isPagoMultipleSubmitting || isPagoMultipleLocked) {
            return;
        }

        var scope = getPanelScope(button);
        var clienteId = scope ? Number.parseInt(scope.getAttribute('data-credito-cliente-id'), 10) : 0;
        var cuotaIds = getSelectedCuotaIds();
        var medioPago = getSelectedMedioPago(scope);

        if (!clienteId || cuotaIds.length === 0) {
            setPagoStatus(scope, 'error', 'Seleccioná al menos una cuota válida.');
            clearCuotaSelection(scope);
            return;
        }

        if (!medioPago) {
            setPagoStatus(scope, 'error', 'Seleccioná un medio de pago válido.');
            return;
        }

        var token = getAntiForgeryToken();
        if (!token) {
            setPagoStatus(scope, 'error', 'No se pudo validar la solicitud. Recargá la página e intentá nuevamente.');
            return;
        }

        var endpoint = button.getAttribute('data-credito-pago-url') || '/Credito/RegistrarPagoMultiple';

        setRegisterButtonSubmitting(scope, true);
        setPagoStatus(scope, 'warning', 'Registrando pago...');

        try {
            var response = await fetch(endpoint, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({
                    clienteId: clienteId,
                    cuotaIds: cuotaIds,
                    medioPago: medioPago,
                    observaciones: null
                })
            });

            var payload = null;
            try {
                payload = await response.json();
            } catch (_) {
                payload = null;
            }

            if (!response.ok || !payload || payload.success === false || payload.Success === false) {
                throw new Error(getResponseErrors(payload).join(' '));
            }

            markPaidCuotas(scope, getResultCuotaIds(payload, cuotaIds));
            selectedCuotas.clear();
            updatePagoResumen(scope);
            setRegisterButtonResult(scope, 'Registrado', 'check_circle');
            setPanelLocked(scope, true);
            setPagoStatus(scope, 'success', 'Pago registrado correctamente. Actualizando cartera...');

            window.setTimeout(function () {
                window.location.reload();
            }, 900);
        } catch (error) {
            var message = error && error.message ? error.message : 'No se pudo registrar el pago múltiple.';
            clearCuotaSelection(scope);
            setRegisterButtonSubmitting(scope, false);

            if (isStalePagoError(message)) {
                setPanelLocked(scope, true);
                setPagoStatus(scope, 'error', message + ' Recargando cartera para evitar un nuevo intento con datos desactualizados...');
                window.setTimeout(function () {
                    window.location.reload();
                }, 2400);
                return;
            }

            setPagoStatus(scope, 'error', message);
        }
    }

    tabButtons.forEach(function (button) {
        button.addEventListener('click', function () {
            activateTab(button.getAttribute('data-credito-tab'));
        });
    });

    document.addEventListener('click', function (event) {
        if (isPagoMultipleLocked && event.target.closest('[data-credito-cliente-panel]')) {
            event.preventDefault();
            event.stopPropagation();
            return;
        }

        var panelTrigger = event.target.closest('[data-credito-cliente-panel-open]');
        if (panelTrigger) {
            event.preventDefault();
            openClientePanel(panelTrigger.getAttribute('data-credito-cliente-id'), panelTrigger);
            return;
        }

        var closeTrigger = event.target.closest('[data-credito-cliente-panel-close]');
        if (closeTrigger) {
            event.preventDefault();
            closeClientePanel();
            return;
        }

        var cuotasTrigger = event.target.closest('[data-credito-credit-toggle]');
        if (cuotasTrigger) {
            event.preventDefault();
            toggleCreditoCuotas(cuotasTrigger);
            return;
        }

        var registerTrigger = event.target.closest('[data-credito-registrar-pago-multiple]');
        if (registerTrigger) {
            event.preventDefault();
            if (registerTrigger.disabled) {
                return;
            }

            submitPagoMultiple(registerTrigger);
        }
    });

    document.addEventListener('change', function (event) {
        if (isPagoMultipleLocked && event.target.closest('[data-credito-cliente-panel]')) {
            event.preventDefault();
            event.stopPropagation();
            return;
        }

        var cuotaSelector = event.target.closest('[data-credito-cuota-selector]');
        if (cuotaSelector) {
            handleCuotaSelection(cuotaSelector);
        }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && !isPagoMultipleLocked) {
            closeClientePanel();
        }
    });

    if (creditoModule && typeof creditoModule.initSharedUi === 'function') {
        creditoModule.initSharedUi();
    }
    activateTab('creditos');
});
