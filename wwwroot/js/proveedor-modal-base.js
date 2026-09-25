/**
 * proveedor-modal-base.js
 *
 * Comportamiento común de los drawers de proveedor (alta y edición):
 * abrir/cerrar con foco atrapado y devuelto, aviso de cambios sin guardar, errores del servidor
 * junto a cada campo, botón de guardar bloqueado mientras se envía y recarga de la página al
 * guardar (el aviso de éxito viaja por TempData y lo dibuja el servidor).
 *
 * Expone: window.TheBury.createProveedorModal(config)
 *   config: { modalId, formId, summaryId, listId, url }
 */
(() => {
    window.TheBury = window.TheBury || {};

    if (window.TheBury.createProveedorModal) {
        return;
    }

    const MSG_FORBIDDEN = 'No tenés permiso para esta acción.';
    const MSG_SESSION = 'Tu sesión venció. Recargá la página e iniciá sesión de nuevo.';
    const MSG_NETWORK = 'No se pudo conectar con el servidor. Revisá tu conexión e intentá de nuevo.';
    const MSG_DISCARD = 'Hay cambios sin guardar. Si cerrás ahora, se pierden.';
    const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    // Convierte una respuesta fetch en JSON o en un error con mensaje entendible.
    async function readJson(res) {
        if (res.status === 403) throw new Error(MSG_FORBIDDEN);
        const contentType = res.headers.get('content-type') || '';
        if (res.status === 401 || res.redirected || !contentType.includes('json')) throw new Error(MSG_SESSION);
        return res.json();
    }

    function describeFailure(error) {
        return error && error.message && error.message !== 'Failed to fetch' ? error.message : MSG_NETWORK;
    }

    function confirmDiscard(onConfirm) {
        const ui = window.TheBury;
        if (typeof ui.confirmAction === 'function') {
            ui.confirmAction(MSG_DISCARD, onConfirm);
            return;
        }
        if (window.confirm(MSG_DISCARD)) onConfirm();
    }

    function createProveedorModal(config) {
        const modal = () => document.getElementById(config.modalId);
        const form = () => document.getElementById(config.formId);
        const summary = () => document.getElementById(config.summaryId);
        const errorList = () => document.getElementById(config.listId);
        const submitButton = () => modal()?.querySelector('[data-proveedor-modal-action="submit"]');

        let opener = null;
        let snapshot = '';
        let busy = false;
        let reloading = false;
        let submitLabel = '';

        const isOpen = () => !!modal() && !modal().classList.contains('hidden');
        const serialize = () => new URLSearchParams(new FormData(form())).toString();
        const isDirty = () => serialize() !== snapshot;

        function takeSnapshot() {
            snapshot = serialize();
        }

        function setBusy(value) {
            busy = value;
            const button = submitButton();
            if (!button) return;
            if (value) {
                submitLabel = button.textContent;
                button.textContent = 'Guardando…';
            } else if (submitLabel) {
                button.textContent = submitLabel;
            }
            button.disabled = value;
            button.setAttribute('aria-busy', value ? 'true' : 'false');
        }

        // Bloquea el guardado sin cambiar el texto del botón (p. ej. mientras se cargan los datos).
        function setLocked(value) {
            const button = submitButton();
            if (button) button.disabled = value;
        }

        // ── Errores ─────────────────────────────────────────────────────────────
        function labelText(input) {
            const label = input.id ? form().querySelector(`label[for="${input.id}"]`) : null;
            const text = label ? label.textContent : input.name;
            return text.replace(/\*/g, '').replace(/\(opcional\)/i, '').trim();
        }

        function clearFieldError(input) {
            input.removeAttribute('aria-invalid');
            input.classList.remove('prov-invalid');
            const holder = input.closest('.prov-field');
            const message = holder && holder.querySelector('[data-field-error]');
            if (message) {
                message.hidden = true;
                message.textContent = '';
            }
        }

        function clearErrors() {
            summary().hidden = true;
            errorList().innerHTML = '';
            form().querySelectorAll('[aria-invalid="true"]').forEach(clearFieldError);
        }

        function showErrors(errors) {
            clearErrors();
            const items = [];
            let firstInvalid = null;

            Object.entries(errors).forEach(([field, messages]) => {
                const input = field ? form().querySelector(`[name="${field}"]:not([type="hidden"])`) : null;
                messages.forEach(text => {
                    items.push(input ? `${labelText(input)}: ${text}` : text);
                });
                if (!input) return;

                input.setAttribute('aria-invalid', 'true');
                input.classList.add('prov-invalid');
                const message = input.closest('.prov-field')?.querySelector('[data-field-error]');
                if (message) {
                    message.id = message.id || `${input.id}-error`;
                    message.textContent = messages.join(' ');
                    message.hidden = false;
                    const described = (input.getAttribute('aria-describedby') || '').split(' ').filter(Boolean);
                    if (!described.includes(message.id)) described.push(message.id);
                    input.setAttribute('aria-describedby', described.join(' '));
                }
                firstInvalid = firstInvalid || input;
            });

            items.forEach(text => {
                const li = document.createElement('li');
                li.textContent = text;
                errorList().appendChild(li);
            });
            summary().hidden = false;
            (firstInvalid || summary()).focus({ preventScroll: false });
            if (!firstInvalid) summary().scrollIntoView({ block: 'nearest' });
        }

        // ── Abrir / cerrar ──────────────────────────────────────────────────────
        function show() {
            opener = document.activeElement;
            modal().classList.remove('hidden');
            document.body.style.overflow = 'hidden';
            requestAnimationFrame(() => {
                const first = form().querySelector('input:not([type="hidden"]):not([disabled]), select, textarea');
                if (first) first.focus({ preventScroll: true });
            });
        }

        function hide() {
            modal().classList.add('hidden');
            document.body.style.overflow = '';
            if (opener && document.contains(opener)) opener.focus({ preventScroll: true });
            opener = null;
        }

        function requestClose() {
            if (!isOpen() || busy) return;
            if (isDirty()) {
                confirmDiscard(hide);
                return;
            }
            hide();
        }

        // ── Guardar ─────────────────────────────────────────────────────────────
        async function submit() {
            if (busy) return;
            clearErrors();

            const params = new URLSearchParams(new FormData(form()));
            // Un checkbox desmarcado no viaja: sin esto no habría forma de desactivar un proveedor.
            if (!params.has('Activo')) params.append('Activo', 'false');

            setBusy(true);
            try {
                const res = await fetch(config.url, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'X-Requested-With': 'XMLHttpRequest'
                    },
                    body: params.toString()
                });
                const data = await readJson(res);

                if (data.success) {
                    reloading = true;
                    takeSnapshot();
                    window.location.reload();
                    return;
                }
                showErrors(data.errors || { '': ['No se pudo guardar el proveedor.'] });
            } catch (error) {
                showErrors({ '': [describeFailure(error)] });
            } finally {
                if (!reloading) setBusy(false);
            }
        }

        // ── Teclado: ESC cierra, Tab queda dentro del drawer ────────────────────
        // El modal de confirmación compartido cierra con ESC en un listener propio (shared-ui.js, registrado
        // en DOMContentLoaded, o sea después de este). Por eso: (1) se anota en captura si estaba abierto, para
        // que ese ESC solo lo cierre; (2) el pedido de cierre del drawer se difiere, para que la confirmación
        // se abra cuando ese listener ya corrió y no la cierre en el mismo evento.
        let confirmWasOpen = false;
        document.addEventListener('keydown', () => {
            const confirmModal = document.getElementById('confirmModal');
            confirmWasOpen = !!confirmModal && !confirmModal.classList.contains('hidden');
        }, true);

        document.addEventListener('keydown', event => {
            if (!isOpen() || confirmWasOpen) return;

            if (event.key === 'Escape') {
                // El primer ESC cierra la lista de productos del buscador, no el drawer.
                if (document.querySelector('.picker-dropdown:not([hidden])')) return;
                window.setTimeout(requestClose, 0);
                return;
            }

            if (event.key !== 'Tab') return;
            const items = [...modal().querySelectorAll(FOCUSABLE)].filter(el => el.offsetParent !== null);
            if (!items.length) return;
            const first = items[0];
            const last = items[items.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        });

        // Al corregir un campo, su error deja de mostrarse.
        document.addEventListener('input', event => {
            if (!form() || !form().contains(event.target)) return;
            if (event.target.getAttribute && event.target.getAttribute('aria-invalid') === 'true') {
                clearFieldError(event.target);
            }
        });

        return {
            show, hide, requestClose, submit, clearErrors, showErrors, takeSnapshot, setBusy, setLocked, readJson, describeFailure,
            form, modal
        };
    }

    window.TheBury.createProveedorModal = createProveedorModal;
})();
