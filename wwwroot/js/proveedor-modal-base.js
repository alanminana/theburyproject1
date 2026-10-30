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

        // Varias reglas de un mismo campo pueden decir casi lo mismo ("…11 dígitos" / "…11 dígitos numéricos"):
        // se descarta el mensaje que es el comienzo de otro más específico.
        function distinctMessages(messages) {
            const unique = [...new Set(messages)];
            return unique.filter(m => !unique.some(o => o !== m && o.startsWith(m)));
        }

        // ── Contadores de categorías/marcas marcadas ────────────────────────────
        function refreshCounts() {
            form().querySelectorAll('[data-prov-check-group]').forEach(group => {
                const total = group.querySelectorAll('input[type="checkbox"]:checked').length;
                const counter = group.closest('fieldset')?.querySelector('[data-prov-count]');
                if (counter) counter.textContent = total ? `· ${total} ${total === 1 ? 'seleccionada' : 'seleccionadas'}` : '';
            });
        }

        // ── Filtro de categorías/marcas: texto + "solo marcadas" (sin name: no viaja en el formulario) ──
        const normalize = text => text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

        function applyCheckFilter(fieldset) {
            const search = fieldset.querySelector('[data-prov-check-search]');
            const onlyChecked = fieldset.querySelector('[data-prov-check-only]');
            const list = fieldset.querySelector('[data-prov-check-group]');
            if (!search || !list) return;
            const term = normalize(search.value.trim());
            let visible = 0;
            list.querySelectorAll('.prov-check').forEach(label => {
                const matches = !term || normalize(label.textContent).includes(term);
                const keep = matches && (!onlyChecked.checked || label.querySelector('input').checked);
                label.hidden = !keep;
                if (keep) visible++;
            });
            const empty = fieldset.querySelector('[data-prov-check-empty]');
            if (empty) empty.hidden = visible > 0;
        }

        function resetCheckFilters() {
            form().querySelectorAll('fieldset').forEach(fieldset => {
                const search = fieldset.querySelector('[data-prov-check-search]');
                if (!search) return;
                search.value = '';
                fieldset.querySelector('[data-prov-check-only]').checked = false;
                applyCheckFilter(fieldset);
            });
        }

        function showErrors(errors) {
            clearErrors();
            const items = [];
            let firstInvalid = null;

            Object.entries(errors).forEach(([field, allMessages]) => {
                const messages = distinctMessages(allMessages);
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
            refreshCounts();
            resetCheckFilters();
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
                // En el filtro de categorías/marcas, el primer ESC borra el texto en vez de cerrar el drawer.
                const target = event.target;
                if (target && target.matches && target.matches('[data-prov-check-search]') && target.value) {
                    target.value = '';
                    applyCheckFilter(target.closest('fieldset'));
                    return;
                }
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

        document.addEventListener('change', event => {
            if (!form() || !form().contains(event.target)) return;
            if (event.target.matches('[data-prov-check-only]')) {
                applyCheckFilter(event.target.closest('fieldset'));
                return;
            }
            if (event.target.type === 'checkbox') refreshCounts();
        });

        document.addEventListener('input', event => {
            if (form() && form().contains(event.target) && event.target.matches('[data-prov-check-search]')) {
                applyCheckFilter(event.target.closest('fieldset'));
            }
        });

        // El CUIT se muestra como 30-12345678-9 en todo el sistema: se acepta pegado con guiones o espacios
        // y se guarda solo con dígitos (maxlength=11 cortaría el texto pegado antes de poder limpiarlo).
        const onlyDigits = text => text.replace(/\D/g, '');
        document.addEventListener('paste', event => {
            const target = event.target;
            if (!form() || !form().contains(target) || target.name !== 'Cuit') return;
            event.preventDefault();
            const pasted = onlyDigits((event.clipboardData || window.clipboardData).getData('text'));
            const start = target.selectionStart ?? target.value.length;
            const end = target.selectionEnd ?? target.value.length;
            target.value = (target.value.slice(0, start) + pasted + target.value.slice(end)).slice(0, 11);
            target.dispatchEvent(new Event('input', { bubbles: true }));
        });
        document.addEventListener('input', event => {
            const target = event.target;
            if (form() && form().contains(target) && target.name === 'Cuit') {
                if (/\D/.test(target.value)) target.value = onlyDigits(target.value);
                refreshCuitAdvice(target);
            }
        });

        // Aviso anticipado: el servidor rechaza un CUIT nuevo o modificado cuyo dígito verificador no coincide (casi
        // seguro es un error de tipeo). Un CUIT antiguo que no se toca se puede seguir guardando.
        const CUIT_WEIGHTS = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
        function cuitVerifierOk(digits) {
            const sum = CUIT_WEIGHTS.reduce((acc, w, i) => acc + w * Number(digits[i]), 0);
            let check = 11 - (sum % 11);
            if (check === 11) check = 0;
            else if (check === 10) check = 9;
            return check === Number(digits[10]);
        }

        function refreshCuitAdvice(input) {
            const hint = document.getElementById(`${input.id}-hint`);
            if (!hint) return;
            if (!hint.dataset.defaultText) hint.dataset.defaultText = hint.textContent;
            const warn = input.value.length === 11 && !cuitVerifierOk(input.value);
            hint.textContent = warn
                ? 'El dígito verificador no coincide: revisá que el CUIT esté bien escrito.'
                : hint.dataset.defaultText;
            hint.classList.toggle('prov-hint--warn', warn);
        }

        // Al corregir un campo, su error deja de mostrarse.
        document.addEventListener('input', event => {
            if (!form() || !form().contains(event.target)) return;
            if (event.target.getAttribute && event.target.getAttribute('aria-invalid') === 'true') {
                clearFieldError(event.target);
            }
        });

        return {
            show, hide, requestClose, submit, clearErrors, showErrors, takeSnapshot, setBusy, setLocked, readJson, describeFailure, refreshCounts, refreshCuitAdvice,
            form, modal
        };
    }

    window.TheBury.createProveedorModal = createProveedorModal;
})();
