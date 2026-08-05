/* credito-details.js — Modales, toasts y detalle read-only de punitorios */

(function () {
    'use strict';

    function initSharedUi() {
        var creditoModule = window.TheBury && window.TheBury.CreditoModule;
        if (creditoModule && typeof creditoModule.initSharedUi === 'function') {
            creditoModule.initSharedUi();
        }
        if (creditoModule && typeof creditoModule.bindModalController === 'function') {
            creditoModule.bindModalController();
        }
        if (creditoModule && typeof creditoModule.initScrollAffordance === 'function') {
            creditoModule.initScrollAffordance(document);
        }
    }

    function findToggle(container, panel) {
        var toggles = container.querySelectorAll('[data-punitorio-toggle]');
        for (var index = 0; index < toggles.length; index += 1) {
            if (toggles[index].getAttribute('aria-controls') === panel.id) {
                return toggles[index];
            }
        }
        return null;
    }

    function setExpanded(toggle, panel, expanded) {
        toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        panel.hidden = !expanded;

        var chevron = toggle.querySelector('[data-punitorio-chevron]');
        if (chevron) {
            chevron.textContent = expanded ? 'expand_less' : 'expand_more';
        }
    }

    function errorMessage(status) {
        if (status === 403) {
            return 'No tenés permiso para consultar este detalle.';
        }
        if (status === 404) {
            return 'La cuota ya no está disponible en este crédito.';
        }
        if (status === 409) {
            return 'El detalle cambió mientras se consultaba. Intentá nuevamente.';
        }
        return 'No se pudo cargar el detalle de punitorios. Intentá nuevamente.';
    }

    function showLoading(panel) {
        panel.setAttribute('aria-busy', 'true');
        panel.innerHTML = [
            '<div class="punitorio-loading" role="status">',
            '<span class="material-symbols-outlined punitorio-loading__icon" aria-hidden="true">progress_activity</span>',
            '<span>Cargando resumen e historial de punitorios…</span>',
            '</div>'
        ].join('');
    }

    function showError(panel, status) {
        panel.innerHTML = [
            '<div class="alert alert-bad punitorio-load-error" role="alert">',
            '<span class="material-symbols-outlined" aria-hidden="true">error</span>',
            '<div><strong>No se pudo cargar</strong><p>',
            errorMessage(status),
            '</p><button type="button" class="btn btn-ghost btn-sm" data-punitorio-retry>',
            '<span class="material-symbols-outlined" aria-hidden="true">refresh</span>Reintentar',
            '</button></div></div>'
        ].join('');
    }

    async function loadPanel(toggle, panel, forceReload) {
        if (panel.dataset.loading === 'true') {
            return false;
        }
        if (!forceReload && panel.dataset.loaded === 'true') {
            return true;
        }

        var url = toggle.dataset.punitorioUrl;
        if (!url) {
            showError(panel, 500);
            return false;
        }

        panel.dataset.loading = 'true';
        toggle.disabled = true;
        showLoading(panel);

        var loaded = false;
        try {
            var response = await fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                headers: {
                    Accept: 'text/html',
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            if (response.redirected && /\/Identity\/Account\/Login/i.test(response.url)) {
                throw { status: 403 };
            }
            if (!response.ok) {
                throw { status: response.status };
            }

            panel.innerHTML = await response.text();
            panel.dataset.loaded = 'true';
            loaded = true;
        } catch (error) {
            delete panel.dataset.loaded;
            showError(panel, error && error.status ? error.status : 500);
        } finally {
            panel.dataset.loading = 'false';
            panel.setAttribute('aria-busy', 'false');
            toggle.disabled = false;
        }
        return loaded;
    }

    function setFeedback(panel, message, isError, moveFocus) {
        var feedback = panel.querySelector('[data-punitorio-feedback]');
        if (!feedback) {
            return;
        }

        feedback.textContent = message;
        feedback.hidden = false;
        feedback.classList.toggle('is-error', isError);
        feedback.classList.toggle('is-success', !isError);
        if (moveFocus) {
            feedback.focus({ preventScroll: false });
        }
    }

    function clearFieldErrors(form) {
        form.querySelectorAll('[data-punitorio-field-error]').forEach(function (element) {
            element.textContent = '';
        });
        form.querySelectorAll('[aria-invalid="true"]').forEach(function (element) {
            element.removeAttribute('aria-invalid');
            element.setCustomValidity('');
        });
    }

    function findErrorElement(form, fieldName) {
        var candidates = form.querySelectorAll('[data-valmsg-for]');
        for (var index = 0; index < candidates.length; index += 1) {
            if (candidates[index].getAttribute('data-valmsg-for') === fieldName) {
                return candidates[index];
            }
        }
        return null;
    }

    function applyFieldErrors(form, errors) {
        var firstInvalid = null;
        Object.keys(errors || {}).forEach(function (fieldName) {
            var messages = Array.isArray(errors[fieldName]) ? errors[fieldName] : [errors[fieldName]];
            var message = messages.filter(Boolean).join(' ');
            var field = form.elements.namedItem(fieldName);
            var errorElement = findErrorElement(form, fieldName);

            if (errorElement) {
                errorElement.textContent = message;
            }
            if (field && typeof field.setAttribute === 'function') {
                field.setAttribute('aria-invalid', 'true');
                field.setCustomValidity(message);
                firstInvalid = firstInvalid || field;
            }
        });
        return firstInvalid;
    }

    function focusFirstFieldError(form, preferredField) {
        var field = preferredField || form.querySelector('[aria-invalid="true"]');
        if (field && typeof field.focus === 'function') {
            field.focus();
        }
    }

    function setFormPending(form, pending) {
        form.dataset.inFlight = pending ? 'true' : 'false';
        var submit = form.querySelector('[data-punitorio-submit]');
        if (submit) {
            submit.disabled = pending;
            submit.setAttribute('aria-disabled', pending ? 'true' : 'false');
        }
    }

    async function readJsonResponse(response) {
        var contentType = response.headers.get('content-type') || '';
        if (!contentType.includes('application/json')) {
            return null;
        }
        try {
            return await response.json();
        } catch (_) {
            return null;
        }
    }

    async function submitPunitorioOperation(container, form) {
        if (form.dataset.inFlight === 'true') {
            return;
        }

        var panel = form.closest('[data-punitorio-panel]');
        var toggle = panel ? findToggle(container, panel) : null;
        if (!panel || !toggle) {
            return;
        }

        clearFieldErrors(form);
        var motivo = form.querySelector('textarea[name$=".Motivo"]');
        if (motivo) {
            motivo.value = motivo.value.trim();
            if (!motivo.value) {
                var requiredMessage = 'El motivo es obligatorio.';
                motivo.setCustomValidity(requiredMessage);
                motivo.setAttribute('aria-invalid', 'true');
                var motiveError = findErrorElement(form, motivo.name);
                if (motiveError) {
                    motiveError.textContent = requiredMessage;
                }
                setFeedback(panel, requiredMessage, true, false);
                motivo.reportValidity();
                focusFirstFieldError(form, motivo);
                return;
            }
        }

        setFormPending(form, true);
        panel.setAttribute('aria-busy', 'true');

        try {
            var response = await fetch(form.action, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: new FormData(form)
            });
            var payload = await readJsonResponse(response);
            var message = payload && payload.message
                ? payload.message
                : errorMessage(response.status);

            if (response.ok && payload && payload.success) {
                delete panel.dataset.loaded;
                setExpanded(toggle, panel, true);
                if (await loadPanel(toggle, panel, true)) {
                    setFeedback(panel, message, false, true);
                }
                return;
            }

            if (response.status === 409) {
                delete panel.dataset.loaded;
                setExpanded(toggle, panel, true);
                if (await loadPanel(toggle, panel, true)) {
                    setFeedback(panel, message, true, true);
                }
                return;
            }

            var firstInvalid = response.status === 400 && payload
                ? applyFieldErrors(form, payload.errors)
                : null;
            setFeedback(panel, message, true, false);
            if (response.status === 400) {
                focusFirstFieldError(form, firstInvalid || motivo);
            }
        } catch (_) {
            setFeedback(panel, 'No se pudo completar la operacion. Intenta nuevamente.', true, false);
        } finally {
            panel.setAttribute('aria-busy', 'false');
            setFormPending(form, false);
        }
    }

    function initPunitorioPanels() {
        var container = document.querySelector('[data-credito-details]');
        if (!container || container.dataset.punitorioPanelsBound === 'true') {
            return;
        }

        container.dataset.punitorioPanelsBound = 'true';
        container.addEventListener('click', function (event) {
            var toggle = event.target.closest('[data-punitorio-toggle]');
            if (toggle && container.contains(toggle)) {
                var panelId = toggle.getAttribute('aria-controls');
                var panel = panelId ? document.getElementById(panelId) : null;
                if (!panel) {
                    return;
                }

                var expanded = toggle.getAttribute('aria-expanded') === 'true';
                setExpanded(toggle, panel, !expanded);
                if (!expanded) {
                    loadPanel(toggle, panel, false);
                }
                return;
            }

            var reload = event.target.closest('[data-punitorio-reload], [data-punitorio-retry]');
            if (!reload || !container.contains(reload)) {
                return;
            }

            var reloadPanel = reload.closest('[data-punitorio-panel]');
            var reloadToggle = reloadPanel ? findToggle(container, reloadPanel) : null;
            if (!reloadPanel || !reloadToggle) {
                return;
            }

            setExpanded(reloadToggle, reloadPanel, true);
            loadPanel(reloadToggle, reloadPanel, true);
        });

        container.addEventListener('submit', function (event) {
            var form = event.target.closest('[data-punitorio-operation-form]');
            if (!form || !container.contains(form)) {
                return;
            }

            event.preventDefault();
            submitPunitorioOperation(container, form);
        });
    }

    function init() {
        initSharedUi();
        initPunitorioPanels();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
