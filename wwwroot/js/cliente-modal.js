/* cliente-modal.js — Modal de alta / edición de clientes (slide-in panel)
 *
 * El alta (isWizardMode, marcado por el propio HTML con
 * data-cliente-wizard="create" en #cliente-modal-form) se comporta como un
 * wizard real de pasos: navegación Anterior/Siguiente, validación del paso
 * actual antes de avanzar, bloqueo de saltos a pasos futuros sin validar los
 * necesarios, y "Crear cliente" únicamente en el último paso.
 *
 * La edición conserva exactamente el comportamiento anterior (tabs libres,
 * un solo botón de guardar siempre visible) — initWizard() nunca se llama
 * para ese caso.
 */
(function () {
    'use strict';

    var modal, backdrop, panel, loadingEl, contentEl;

    /* ── helpers compartidos (alta y edición) ─────────────────────── */

    function toggleSection(name, forceOpen) {
        var content = document.getElementById('section-' + name);
        var chevron = document.getElementById('chevron-' + name);
        if (!content || !chevron) {
            activateTab(name);
            return;
        }
        var isOpen = !content.classList.contains('hidden');
        var shouldOpen = forceOpen !== undefined ? forceOpen : !isOpen;
        content.classList.toggle('hidden', !shouldOpen);
        chevron.classList.toggle('rotate-180', !shouldOpen);
    }

    // Navegación libre entre tabs — solo se usa en el modal de edición.
    function activateTab(name) {
        var tabId = name === 'credito' || name === 'crediticio' ? 't-credito' : 't-' + name;
        var tab = document.querySelector('#modal-cliente [data-cliente-tab="' + tabId + '"]');
        var panel = document.getElementById(tabId);
        if (!tab || !panel) return;

        document.querySelectorAll('#modal-cliente #form-tabs .tab').forEach(function (item) {
            item.setAttribute('aria-selected', item === tab ? 'true' : 'false');
        });

        document.querySelectorAll('#modal-cliente .tab-panel').forEach(function (item) {
            item.classList.toggle('is-active', item === panel);
        });
    }

    function updatePreview() {
        var apellido = document.getElementById('Apellido');
        var nombre = document.getElementById('Nombre');
        var documento = document.getElementById('NumeroDocumento');
        var ap = apellido ? apellido.value.trim() : '';
        var no = nombre ? nombre.value.trim() : '';
        var full = (ap || no) ? (ap + (ap && no ? ', ' : '') + no) : 'Nuevo cliente';
        var avatar = ((ap.charAt(0) || '') + (no.charAt(0) || '')).toUpperCase() || '+';
        var nameEl = document.getElementById('pv-name');
        var avatarEl = document.getElementById('pv-avatar');
        var docEl = document.getElementById('pv-doc');
        if (nameEl) nameEl.textContent = full;
        if (avatarEl) avatarEl.textContent = avatar;
        if (docEl && documento) docEl.textContent = documento.value.trim() || '-';
    }

    function validarMontos() {
        var minInput = document.getElementById('montoMinimo');
        var maxInput = document.getElementById('montoMaximo');
        var errorEl  = document.getElementById('montoError');
        if (!minInput || !maxInput || !errorEl) return true;
        var min = parseFloat(minInput.value);
        var max = parseFloat(maxInput.value);
        if (!isNaN(min) && !isNaN(max) && min > max) {
            errorEl.classList.remove('hidden');
            maxInput.classList.add('border-red-500');
            return false;
        }
        errorEl.classList.add('hidden');
        maxInput.classList.remove('border-red-500');
        return true;
    }

    function showErrors(errors) {
        var box  = document.getElementById('cliente-modal-validation');
        var list = document.getElementById('cliente-modal-error-list');
        if (!box || !list) return;
        list.innerHTML = '';
        errors.forEach(function (msg) {
            var li = document.createElement('li');
            li.textContent = msg;
            list.appendChild(li);
        });
        box.classList.remove('hidden');
        box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function clearErrors() {
        var box = document.getElementById('cliente-modal-validation');
        if (box) box.classList.add('hidden');
    }

    /* ── open / close ───────────────────────────────────────────── */

    function openModal() {
        modal.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
    }

    function showLoading() {
        loadingEl.classList.remove('hidden');
        contentEl.classList.add('hidden');
    }

    function showContent() {
        loadingEl.classList.add('hidden');
        contentEl.classList.remove('hidden');
    }

    function close() {
        modal.classList.add('hidden');
        contentEl.innerHTML = '';
        contentEl.classList.add('hidden');
        loadingEl.classList.remove('hidden');
        document.body.style.overflow = '';
    }

    /* ── load partial ──────────────────────────────────────────── */

    function loadPartial(url) {
        openModal();
        showLoading();

        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (res) {
                if (!res.ok) throw new Error('Error ' + res.status);
                return res.text();
            })
            .then(function (html) {
                contentEl.innerHTML = html;
                showContent();
                if (window.ClienteCuilAutocomplete) {
                    window.ClienteCuilAutocomplete.init(contentEl);
                }
                initFormHandlers();
            })
            .catch(function (err) {
                console.error('ClienteModal: error cargando formulario', err);
                close();
            });
    }

    /* ── validación cliente-side: solo ayuda de UX ─────────────────
     * El servidor sigue siendo la autoridad final (CreateAjax/EditAjax
     * revalidan todo el ModelState). Los validadores de formato argentino
     * a medida del ViewModel no tienen adaptador client-side propio y por
     * lo tanto no generan atributos data-val-* de jQuery Validate, así que
     * acá solo se alcanza a comprobar [Required] (y el resto de reglas con
     * adaptador nativo) — el resto se sigue validando en el servidor, sin
     * reimplementar esas reglas de negocio en JS.
     */
    function patchValidatorAria(form) {
        var validator = window.jQuery && jQuery(form).data('validator');
        if (!validator) return;

        var originalHighlight = validator.settings.highlight;
        validator.settings.highlight = function (element, errorClass, validClass) {
            if (originalHighlight) originalHighlight.call(this, element, errorClass, validClass);
            jQuery(element).attr('aria-invalid', 'true');
        };

        var originalUnhighlight = validator.settings.unhighlight;
        validator.settings.unhighlight = function (element, errorClass, validClass) {
            if (originalUnhighlight) originalUnhighlight.call(this, element, errorClass, validClass);
            jQuery(element).removeAttr('aria-invalid');
        };

        var originalErrorPlacement = validator.settings.errorPlacement;
        validator.settings.errorPlacement = function (error, element) {
            if (originalErrorPlacement) originalErrorPlacement.call(this, error, element);
            var id = element.attr('id');
            if (!id) return;
            error.attr('id', id + '-error');
            element.attr('aria-describedby', id + '-error');
        };
    }

    /* ── wizard real (solo alta) ───────────────────────────────── */

    function initWizard(form) {
        if (!window.jQuery) return; // sin jQuery Validate no hay wizard seguro: se degrada a envío directo

        var steps = Array.prototype.slice.call(form.querySelectorAll('#form-tabs [data-cliente-tab]'))
            .filter(function (btn) { return !btn.hidden; })
            .map(function (btn) {
                return { button: btn, panel: document.getElementById(btn.getAttribute('data-cliente-tab')) };
            })
            .filter(function (step) { return !!step.panel; });

        if (!steps.length) return;

        // Fuente única de la cantidad de pasos: se deriva de los botones
        // realmente presentes en el DOM, nunca de un número fijo. Si un
        // futuro lote oculta un paso (ej. Referencias), steps.length baja
        // solo y el resto de la lógica (Paso X de N, último paso, etc.) se
        // ajusta sin tocar código.
        var stepState = steps.map(function () { return 'pending'; });
        var current = 0;
        var maxReached = 0;

        var prevBtn = document.getElementById('cliente-wizard-prev');
        var nextBtn = document.getElementById('cliente-wizard-next');
        var submitBtn = document.getElementById('cliente-wizard-submit');
        var progressEl = form.querySelector('[data-wizard-progress]');

        /* ── stepper compacto de mobile (Lote 3) ──────────────────────
         * Solo visible <=640px (cliente-module.css oculta .wizard-steps
         * y muestra este bloque en ese rango). Ícono/nombre se toman en
         * vivo del propio botón de tab del paso actual — nunca se
         * duplica el dato en una segunda lista. */
        var compactIconEl = form.querySelector('[data-wizard-compact-icon]');
        var compactLabelEl = form.querySelector('[data-wizard-compact-label]');
        var compactFillEl = form.querySelector('[data-wizard-compact-fill]');

        function plainIconOf(step) {
            var icons = step.button.querySelectorAll('.material-symbols-outlined');
            for (var i = 0; i < icons.length; i++) {
                if (!icons[i].hasAttribute('data-step-icon')) return icons[i].textContent;
            }
            return '';
        }

        /* ── resumen lateral dinámico (Lote 2, punto 3) ───────────────
         * Reemplaza, solo en modo wizard, a la updatePreview() genérica de
         * abajo (que sigue intacta para edición: no se toca su registro ni
         * su comportamiento). Una única función central lee los valores
         * actuales de Nombre/Apellido/TipoDocumento/NumeroDocumento/
         * Telefono/Email y actualiza los nodos del resumen sin reconstruir
         * el panel — nunca inventa datos ni muestra placeholders como
         * reales.
         */
        var pvNameEl = form.querySelector('#pv-name');
        var pvAvatarEl = form.querySelector('#pv-avatar');
        var pvDocEl = form.querySelector('#pv-doc');
        var pvContactEl = form.querySelector('#pv-contact');
        var pvContactPhoneEl = form.querySelector('#pv-contact-phone');
        var pvContactEmailEl = form.querySelector('#pv-contact-email');

        function fieldValue(id) {
            var el = document.getElementById(id);
            return el ? el.value.trim() : '';
        }

        // Formato visual únicamente (agrupa dígitos de a 3 con puntos, ej.
        // "35996614" -> "35.996.614"): no existe un helper reutilizable de
        // esto en el resto del JS del módulo (cliente-cuil.js solo limpia
        // dígitos, no los agrupa) y nunca toca el value real del input.
        function formatDocumentoNumero(raw) {
            var digits = String(raw || '').replace(/\D/g, '');
            if (!digits) return '';
            return digits.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
        }

        function setContactLine(li, value) {
            if (!li) return;
            var valueEl = li.querySelector('[data-pv-contact-value]');
            if (valueEl) valueEl.textContent = value;
            li.hidden = !value;
        }

        function updateClientSummary() {
            var nombre = fieldValue('Nombre');
            var apellido = fieldValue('Apellido');
            var tipoDoc = fieldValue('TipoDocumento');
            var numeroDoc = fieldValue('NumeroDocumento');
            var telefono = fieldValue('Telefono');
            var email = fieldValue('Email');

            // Identidad: iniciales y nombre completo derivados solo de
            // valores reales cargados. Sin nombre ni apellido se conserva el
            // estado inicial servido por el servidor ("Nuevo cliente" / "-").
            var iniciales = '';
            var nombreCompleto = null;
            if (nombre && apellido) {
                iniciales = (nombre.charAt(0) + apellido.charAt(0)).toUpperCase();
                nombreCompleto = nombre + ' ' + apellido;
            } else if (nombre) {
                iniciales = nombre.charAt(0).toUpperCase();
                nombreCompleto = nombre;
            } else if (apellido) {
                iniciales = apellido.charAt(0).toUpperCase();
                nombreCompleto = apellido;
            }
            if (pvNameEl) pvNameEl.textContent = nombreCompleto || 'Nuevo cliente';
            if (pvAvatarEl) pvAvatarEl.textContent = iniciales;

            // Documento: "TIPO 00.000.000" solo cuando hay número real
            // cargado; nunca un placeholder en su lugar.
            if (pvDocEl) {
                var numeroFormateado = formatDocumentoNumero(numeroDoc);
                pvDocEl.textContent = numeroFormateado
                    ? (tipoDoc ? tipoDoc + ' ' + numeroFormateado : numeroFormateado)
                    : '-';
            }

            // Contacto: bloque compacto, cada línea solo si tiene dato real;
            // el bloque entero queda oculto (sin reservar espacio) si ambos
            // están vacíos.
            setContactLine(pvContactPhoneEl, telefono);
            setContactLine(pvContactEmailEl, email);
            if (pvContactEl) pvContactEl.hidden = !telefono && !email;
        }

        ['Nombre', 'Apellido', 'NumeroDocumento', 'Telefono', 'Email'].forEach(function (id) {
            var el = document.getElementById(id);
            if (el) el.addEventListener('input', updateClientSummary);
        });
        var tipoDocumentoSelect = document.getElementById('TipoDocumento');
        if (tipoDocumentoSelect) tipoDocumentoSelect.addEventListener('change', updateClientSummary);

        updateClientSummary();

        // Cónyuge condicional (Lote 2): usa los valores reales de
        // DropdownConstants.EstadosCiviles (Views/Cliente/_ClienteFormCampos.cshtml
        // los renderiza en #f-civil). No es un enum nuevo ni una regla de negocio
        // agregada — solo decide si el paso se saltea al navegar.
        var ESTADOS_CIVILES_SIN_CONYUGE = ['Soltero/a', 'Divorciado/a', 'Viudo/a'];
        var conyugeIndex = steps.findIndex(function (step) {
            return step.button.getAttribute('data-cliente-tab') === 't-conyuge';
        });

        function isConyugeApplicable() {
            if (conyugeIndex === -1) return true;
            var civil = document.getElementById('f-civil');
            var value = civil ? civil.value : '';
            return ESTADOS_CIVILES_SIN_CONYUGE.indexOf(value) === -1;
        }

        // No aplica" nunca se guarda como dato del cliente ni borra lo cargado:
        // solo cambia si el paso se muestra/salta al navegar.
        function isStepApplicable(idx) {
            return idx !== conyugeIndex || isConyugeApplicable();
        }

        // Próximo índice navegable en una dirección, saltando pasos "no aplica".
        function stepAfter(idx, dir) {
            var target = idx + dir;
            while (target >= 0 && target <= steps.length - 1 && !isStepApplicable(target)) {
                target += dir;
            }
            return target;
        }

        function requiredFieldsOf(idx) {
            return Array.prototype.slice.call(steps[idx].panel.querySelectorAll('[data-val-required]'));
        }

        function validateStep(idx) {
            var fields = requiredFieldsOf(idx);
            if (!fields.length) return true;
            var validator = jQuery(form).data('validator');
            var ok = true;
            fields.forEach(function (field) {
                var fieldOk = validator ? !!validator.element(field) : !!field.value.trim();
                if (!fieldOk) ok = false;
            });
            return ok;
        }

        function firstInvalidFieldIn(idx) {
            var fields = requiredFieldsOf(idx);
            for (var i = 0; i < fields.length; i++) {
                if (fields[i].getAttribute('aria-invalid') === 'true' || fields[i].classList.contains('input-validation-error')) {
                    return fields[i];
                }
            }
            return fields[0] || null;
        }

        function iconFor(state) {
            switch (state) {
                case 'complete': return 'check_circle';
                case 'error': return 'error';
                case 'current': return 'radio_button_checked';
                case 'skip': return 'remove_circle';
                default: return 'radio_button_unchecked';
            }
        }

        function labelFor(state) {
            switch (state) {
                case 'complete': return 'Completado';
                case 'error': return 'Con errores';
                case 'current': return 'Paso actual';
                case 'skip': return 'No aplica';
                default: return 'Pendiente';
            }
        }

        function displayStateFor(i) {
            if (i === current) return stepState[i] === 'error' ? 'error' : 'current';
            if (!isStepApplicable(i)) return 'skip';
            return stepState[i];
        }

        // `options.skipFocus` evita robar el foco cuando el re-render lo dispara
        // un cambio de Estado civil (no una navegación real de paso) — si no,
        // cada vez que el usuario elige un Estado civil el foco saltaría del
        // <select> al panel activo.
        function render(focusEl, options) {
            options = options || {};
            steps.forEach(function (step, i) {
                var isCurrent = i === current;
                var state = displayStateFor(i);
                step.button.setAttribute('data-step-state', state);
                if (isCurrent) {
                    step.button.setAttribute('aria-current', 'step');
                } else {
                    step.button.removeAttribute('aria-current');
                }
                var icon = step.button.querySelector('[data-step-icon]');
                if (icon) icon.textContent = iconFor(state);
                var text = step.button.querySelector('[data-step-status-text]');
                if (text) text.textContent = labelFor(state);
                step.panel.classList.toggle('is-active', isCurrent);
            });

            // Mantiene visible el tab del paso actual dentro de la barra
            // scrolleable (Lote 3): sin esto, en anchos donde los 5 pasos no
            // entran completos (confirmado en vivo a 1024px y en mobile),
            // "Crédito" podía quedar totalmente fuera de vista y sin aviso al
            // llegar al último paso con Siguiente. No roba foco (block:
            // 'nearest' + la barra no scrollea verticalmente).
            if (steps[current] && typeof steps[current].button.scrollIntoView === 'function') {
                steps[current].button.scrollIntoView({ block: 'nearest', inline: 'nearest' });
            }

            // Stepper compacto de mobile (Lote 3): mismo dato que el tab
            // activo, nunca una copia independiente.
            if (compactIconEl) compactIconEl.textContent = plainIconOf(steps[current]);
            if (compactLabelEl) compactLabelEl.textContent = steps[current].button.getAttribute('data-step-label') || '';
            if (compactFillEl) compactFillEl.style.transform = 'scaleX(' + ((current + 1) / steps.length).toFixed(4) + ')';

            if (prevBtn) prevBtn.classList.toggle('hidden', current === 0);
            if (nextBtn) nextBtn.classList.toggle('hidden', current === steps.length - 1);
            if (submitBtn) submitBtn.classList.toggle('hidden', current !== steps.length - 1);
            // "Paso X de N" se recalcula desde steps.length (los pasos realmente
            // presentes en el DOM), nunca de un número fijo — ver stepAfter().
            if (progressEl) progressEl.textContent = 'Paso ' + (current + 1) + ' de ' + steps.length;

            if (options.skipFocus) return;

            var body = form.querySelector('.drawer-body');
            if (body) body.scrollTop = 0;

            if (focusEl) {
                focusEl.focus();
            } else {
                steps[current].panel.setAttribute('tabindex', '-1');
                steps[current].panel.focus({ preventScroll: true });
            }
        }

        function goTo(target) {
            if (target < 0 || target > steps.length - 1 || target === current) return;

            // Pasos ya alcanzados (completos o el actual) se pueden reabrir
            // libremente, sin repetir validación.
            if (target <= maxReached) {
                current = target;
                render();
                return;
            }

            // Paso futuro nunca alcanzado: hay que validar en orden todos los
            // pasos intermedios antes de llegar. Si alguno falla, nos
            // quedamos ahí marcado en error — nunca se llega directo al
            // destino (ej. no se puede llegar a Crédito con Personales
            // inválido).
            var idx = current;
            while (idx < target) {
                if (!validateStep(idx)) {
                    stepState[idx] = 'error';
                    current = idx;
                    render(firstInvalidFieldIn(idx));
                    return;
                }
                stepState[idx] = 'complete';
                maxReached = Math.max(maxReached, idx + 1);
                idx++;
            }
            current = target;
            render();
        }

        steps.forEach(function (step, i) {
            // Click directo en una tab: se deja igual que antes (incluye poder
            // reabrir Cónyuge "no aplica" a mano; ver isStepApplicable). El
            // salteo automático es solo para Anterior/Siguiente.
            step.button.addEventListener('click', function () { goTo(i); });
        });
        if (prevBtn) prevBtn.addEventListener('click', function () { goTo(stepAfter(current, -1)); });
        if (nextBtn) nextBtn.addEventListener('click', function () { goTo(stepAfter(current, 1)); });

        // Cambiar el Estado civil no borra datos de Cónyuge ya cargados ni
        // dispara validación: solo actualiza qué paso queda navegable/"no
        // aplica" (Personales sigue siendo el paso actual en este momento).
        var civilSelect = document.getElementById('f-civil');
        if (civilSelect) {
            civilSelect.addEventListener('change', function () {
                render(null, { skipFocus: true });
            });
        }

        // Enter dentro de un campo de texto avanza al siguiente paso en vez
        // de intentar un submit nativo prematuro (el botón "Crear cliente"
        // sigue existiendo en el DOM, aunque oculto, como submit implícito).
        form.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter' || e.target.tagName !== 'INPUT') return;
            if (current === steps.length - 1) return; // último paso: Enter sí puede enviar
            e.preventDefault();
            goTo(stepAfter(current, 1));
        });

        // Validación final completa antes de crear: revalida TODOS los pasos
        // (no solo los ya marcados "complete" por maxReached), por si el
        // usuario volvió a un paso ya visitado y rompió un dato obligatorio
        // antes de saltar directo a un paso más adelante ya desbloqueado.
        form.wizardValidateAll = function () {
            for (var i = 0; i < steps.length; i++) {
                if (!validateStep(i)) {
                    stepState[i] = 'error';
                    current = i;
                    render(firstInvalidFieldIn(i));
                    return false;
                }
                stepState[i] = 'complete';
            }
            render();
            return true;
        };

        render();
    }

    /* ── form handlers (re-init after each partial load) ────────── */

    function initFormHandlers() {
        var form = document.getElementById('cliente-modal-form');
        if (!form) return;

        var isWizardMode = form.getAttribute('data-cliente-wizard') === 'create';
        // El resumen dinámico propio del wizard (updateClientSummary, dentro
        // de initWizard) solo reemplaza a updatePreview() cuando el wizard
        // realmente se inicializa. Si falta jQuery Validate, initWizard() se
        // degrada (ver más abajo) y este fallback conserva exactamente el
        // comportamiento previo también en ese caso límite.
        var wizardReady = isWizardMode && !!(window.jQuery && jQuery.validator && jQuery.validator.unobtrusive);

        var cancelBtn = document.getElementById('modal-cliente-cancel');
        if (cancelBtn) cancelBtn.addEventListener('click', close);

        var closeBtn = document.getElementById('modal-cliente-close');
        if (closeBtn) closeBtn.addEventListener('click', close);

        var cancelBottomBtn = document.getElementById('modal-cliente-cancel-bottom');
        if (cancelBottomBtn) cancelBottomBtn.addEventListener('click', close);

        var wizardCancelBtn = document.getElementById('cliente-wizard-cancel');
        if (wizardCancelBtn) wizardCancelBtn.addEventListener('click', close);

        // updatePreview() es la vista previa genérica de edición (Apellido,
        // Nombre) — se preserva intacta para ese caso. El wizard de alta
        // tiene su propio resumen dinámico (updateClientSummary, con reglas
        // de iniciales/documento/contacto propias del punto 3 del Lote 2),
        // inicializado dentro de initWizard(); registrar también esta acá
        // duplicaría la lógica sobre los mismos nodos (#pv-name/#pv-avatar/
        // #pv-doc).
        if (!wizardReady) {
            ['Apellido', 'Nombre', 'NumeroDocumento'].forEach(function (id) {
                var el = document.getElementById(id);
                if (el) el.addEventListener('input', updatePreview);
            });
            updatePreview();
        }

        var minInput = document.getElementById('montoMinimo');
        var maxInput = document.getElementById('montoMaximo');
        if (minInput) minInput.addEventListener('input', validarMontos);
        if (maxInput) maxInput.addEventListener('input', validarMontos);

        if (wizardReady) {
            jQuery.validator.unobtrusive.parse(form);
            patchValidatorAria(form);
            initWizard(form);
        } else {
            // Edición (y cualquier fallback sin jQuery Validate disponible):
            // navegación libre entre tabs, como siempre.
            document.querySelectorAll('#modal-cliente [data-cliente-tab]').forEach(function (tab) {
                tab.addEventListener('click', function () {
                    activateTab(tab.getAttribute('data-cliente-tab').replace('t-', ''));
                });
            });
        }

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            if (form.dataset.submitting === '1') return; // evita doble submit
            clearErrors();

            if (isWizardMode && typeof form.wizardValidateAll === 'function') {
                if (!form.wizardValidateAll()) return;
            } else if (!validarMontos()) {
                toggleSection('credito', true);
                return;
            }

            submitForm(form, isWizardMode);
        });
    }

    function submitForm(form, isWizardMode) {
        var submitBtn = form.querySelector('button[type="submit"]');
        var originalHtml = submitBtn ? submitBtn.innerHTML : null;

        form.dataset.submitting = '1';
        if (submitBtn) {
            submitBtn.disabled = true;
            if (isWizardMode) {
                submitBtn.setAttribute('aria-busy', 'true');
                submitBtn.innerHTML = '<span class="material-symbols-outlined" aria-hidden="true">hourglass_empty</span>Creando cliente...';
            }
        }

        function restoreSubmitButton() {
            form.dataset.submitting = '0';
            if (!submitBtn) return;
            submitBtn.disabled = false;
            submitBtn.removeAttribute('aria-busy');
            if (originalHtml !== null) submitBtn.innerHTML = originalHtml;
        }

        var data = new URLSearchParams(new FormData(form));

        fetch(form.action, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: data
        })
        .then(function (res) { return res.json(); })
        .then(function (json) {
            if (json.success) {
                close();
                window.location.reload();
            } else {
                restoreSubmitButton();
                var fallback = isWizardMode
                    ? 'No pudimos crear el cliente. Revisá los datos e intentá nuevamente.'
                    : 'Error al guardar el cliente.';
                showErrors(json.errors || [fallback]);
            }
        })
        .catch(function (err) {
            console.error('ClienteModal: error en submit', err);
            restoreSubmitButton();
            showErrors(['Error de conexión. Intentá nuevamente.']);
        });
    }

    /* ── public API ─────────────────────────────────────────────── */

    window.ClienteModal = {
        openCreate: function () {
            loadPartial('/Cliente/ModalCreate');
        },
        openEdit: function (id) {
            loadPartial('/Cliente/ModalEdit/' + id);
        },
        close: close
    };

    /* ── init (delegated events, ESC, backdrop) ─────────────────── */

    function init() {
        modal     = document.getElementById('modal-cliente');
        backdrop  = document.getElementById('modal-cliente-backdrop');
        panel     = document.getElementById('modal-cliente-panel');
        loadingEl = document.getElementById('modal-cliente-loading');
        contentEl = document.getElementById('modal-cliente-content');

        if (!modal) return;

        /* Accordion delegated (inside modal content). En modo wizard la
           navegación la maneja initWizard(): este handler solo aplica a la
           edición, para no pisar aria-current/data-step-state reescribiendo
           aria-selected en cada click dentro del panel activo. */
        modal.addEventListener('click', function (e) {
            var currentForm = document.getElementById('cliente-modal-form');
            if (currentForm && currentForm.getAttribute('data-cliente-wizard') === 'create') return;
            var btn = e.target.closest('[data-cliente-section]');
            if (!btn) return;
            toggleSection(btn.getAttribute('data-cliente-section'));
        });

        /* Backdrop click */
        backdrop.addEventListener('click', close);

        /* ESC key */
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && !modal.classList.contains('hidden')) {
                close();
            }
        });

        /* Delegated open-create button */
        document.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-cliente-modal-open]');
            if (!btn) return;
            var mode = btn.getAttribute('data-cliente-modal-open');
            if (mode === 'create') {
                window.ClienteModal.openCreate();
            } else if (mode === 'edit') {
                var id = btn.getAttribute('data-cliente-id');
                if (id) window.ClienteModal.openEdit(id);
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
