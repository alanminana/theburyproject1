/* cliente-details.js — Lógica para Details_tw de Cliente */
(function () {
    'use strict';

    var tipoDocNombreMap = {
        'DNI': '1',
        'Recibo de Sueldo': '2',
        'ReciboSueldo': '2',
        'Servicio': '3',
        'Constancia CUIL': '6',
        'ConstanciaCUIL': '6',
        'Veraz': '8',
        'Otro': '99'
    };

    function setSectionState(button, expanded) {
        if (!button) return;

        var sectionName = button.getAttribute('data-cliente-details-toggle');
        var content = document.getElementById('content-' + sectionName);
        var chevron = document.getElementById('chevron-' + sectionName);

        if (!content || !chevron) return;

        content.classList.toggle('hidden', !expanded);
        chevron.textContent = expanded ? 'expand_less' : 'expand_more';
        button.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    }

    function openUploadModal(tipoDocValue, tipoDocLabel, replaceId) {
        var modal = document.getElementById('uploadDocModal');
        var select = document.getElementById('modalTipoDocumento');
        var subtitle = document.getElementById('uploadModalSubtitle');
        var errors = document.getElementById('uploadModalErrors');
        var replaceField = document.getElementById('modalReplaceId');
        var replaceFlag = document.getElementById('modalReemplazar');
        var submitBtn = document.getElementById('btnSubirDoc');
        var form = document.getElementById('uploadDocForm');
        var dropContent = document.getElementById('modalDropContent');
        var filePreview = document.getElementById('modalFilePreview');

        if (!modal) return;

        if (form) {
            form.querySelectorAll('input[type="file"]').forEach(function (fileInput) {
                fileInput.value = '';
            });
            var fecha = document.getElementById('modalFechaVencimiento');
            var obs = document.getElementById('modalObservaciones');
            if (fecha) fecha.value = '';
            if (obs) obs.value = '';
        }

        if (dropContent) dropContent.classList.remove('hidden');
        if (filePreview) filePreview.classList.add('hidden');
        if (errors) {
            errors.classList.add('hidden');
            errors.textContent = '';
        }

        if (select) {
            var value = tipoDocValue ? String(tipoDocValue) : '';
            if (!value && tipoDocLabel && tipoDocNombreMap[tipoDocLabel]) {
                value = tipoDocNombreMap[tipoDocLabel];
            }
            select.value = value;
        }

        if (replaceField && replaceFlag) {
            if (replaceId) {
                replaceField.value = replaceId;
                replaceFlag.value = 'true';
            } else {
                replaceField.value = '';
                replaceFlag.value = 'false';
            }
        }

        var isReplace = !!replaceId;
        if (subtitle) {
            subtitle.textContent = isReplace
                ? 'Reemplazar: ' + (tipoDocLabel || 'documento')
                : 'Subir: ' + (tipoDocLabel || 'documento');
        }

        if (submitBtn) {
            submitBtn.innerHTML = isReplace
                ? '<span class="material-symbols-outlined text-lg">swap_horiz</span> Reemplazar Documento'
                : '<span class="material-symbols-outlined text-lg">cloud_upload</span> Subir Documento';
        }

        modal.classList.remove('hidden');
        modal.classList.add('flex');
    }

    function closeUploadModal() {
        var modal = document.getElementById('uploadDocModal');
        if (!modal) return;
        modal.classList.add('hidden');
        modal.classList.remove('flex');
    }

    function actualizarBcra(clienteId) {
        var btn = document.getElementById('bcra-btn');
        var icon = document.getElementById('bcra-icon');
        var btnLabel = document.getElementById('bcra-btn-label');
        var descEl = document.getElementById('bcra-desc');
        var dotEl = document.getElementById('bcra-dot');
        var metaEl = document.getElementById('bcra-meta');
        var chipEl = document.getElementById('bcra-chip');
        var chipIconEl = document.getElementById('bcra-chip-icon');
        var chipLabelEl = document.getElementById('bcra-chip-label');
        var avisoEl = document.getElementById('bcra-aviso');
        var errorEl = document.getElementById('bcra-error');

        if (!btn || !descEl) return;

        btn.disabled = true;
        if (icon) icon.classList.add('animate-spin');
        if (btnLabel) btnLabel.textContent = 'Actualizando...';
        if (errorEl) errorEl.classList.add('hidden');

        fetch('/Cliente/ActualizarBcra', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
            },
            body: 'clienteId=' + encodeURIComponent(clienteId)
        })
            .then(function (response) {
                return response.json();
            })
            .then(function (data) {
                if (descEl) descEl.textContent = data.descripcion || 'Sin información';

                var color;
                var dotColor;

                if (data.situacion === 0 || data.situacion === 1) {
                    color = 'text-green-500';
                    dotColor = 'bg-green-500';
                } else if (data.situacion === 2) {
                    color = 'text-amber-500';
                    dotColor = 'bg-amber-500';
                } else if (data.situacion === null || data.situacion === undefined) {
                    color = 'text-slate-400';
                    dotColor = 'bg-slate-400';
                } else {
                    color = 'text-red-500';
                    dotColor = 'bg-red-500';
                }

                if (descEl) descEl.className = 'font-bold ' + color;
                if (dotEl) dotEl.className = 'h-2 w-2 rounded-full ' + dotColor;
                if (metaEl) {
                    var text = data.ultimaConsulta ? 'Consulta: ' + data.ultimaConsulta : '';
                    if (data.periodo) text += ' · Período: ' + data.periodo;
                    metaEl.textContent = text || 'Actualizado';
                }

                // Chip de cabecera: sincroniza el mismo estado que Details_tw.cshtml
                // (nunca consultado / consulta OK / usando ultima consulta valida / error).
                if (chipEl) {
                    var chipClass, chipIcon, chipLabel;
                    if (!data.tieneCuil) {
                        chipClass = 'chip-neutral'; chipIcon = 'remove'; chipLabel = 'Sin CUIL';
                    } else if (data.nuncaConsultado) {
                        chipClass = 'chip-neutral'; chipIcon = 'schedule'; chipLabel = 'Sin consultar';
                    } else if (data.ok) {
                        chipClass = 'chip-ok'; chipIcon = 'check'; chipLabel = 'Consulta OK';
                    } else if (data.usandoUltimoExito) {
                        chipClass = 'chip-warn'; chipIcon = 'history'; chipLabel = 'Usando ultima consulta valida';
                    } else {
                        chipClass = 'chip-bad'; chipIcon = 'priority_high'; chipLabel = 'Error BCRA';
                    }
                    chipEl.className = 'chip ' + chipClass;
                    if (chipIconEl) chipIconEl.textContent = chipIcon;
                    if (chipLabelEl) chipLabelEl.textContent = chipLabel;
                }

                if (avisoEl) {
                    if (data.usandoUltimoExito && data.mensaje) {
                        avisoEl.textContent = data.mensaje;
                        avisoEl.classList.remove('hidden');
                    } else {
                        avisoEl.textContent = '';
                        avisoEl.classList.add('hidden');
                    }
                }
            })
            .catch(function () {
                // No pisar descEl: mantiene el ultimo dato real conocido en pantalla
                // en vez de reemplazarlo por un texto generico de error (perderia
                // contexto real, ej. "Usando ultima consulta valida"). El error se
                // muestra aparte, cerca del boton, y reintentar es volver a tocarlo.
                if (errorEl) errorEl.classList.remove('hidden');
            })
            .finally(function () {
                btn.disabled = false;
                if (icon) icon.classList.remove('animate-spin');
                if (btnLabel) btnLabel.textContent = 'Actualizar BCRA';
            });
    }

    // Solapas de la ficha (ERP-UI-STANDARD.md §5): patron ARIA completo con roving
    // tabindex, mismo enfoque que wwwroot/js/dashboard-index.js y el tab-btn de
    // Views/Venta/Index_tw.cshtml. Paneles ya vienen server-renderizados completos;
    // esto solo alterna que panel queda visible, sin fetch ni reload.
    function setActiveClienteTab(tabName, updateHash) {
        var buttons = Array.prototype.slice.call(document.querySelectorAll('[data-cliente-tab]'));
        var panels = Array.prototype.slice.call(document.querySelectorAll('[data-cliente-tab-panel]'));
        if (!buttons.length) return;

        buttons.forEach(function (button) {
            var isActive = button.getAttribute('data-cliente-tab') === tabName;
            button.classList.toggle('is-active', isActive);
            button.setAttribute('aria-selected', isActive ? 'true' : 'false');
            button.tabIndex = isActive ? 0 : -1;
        });

        panels.forEach(function (panel) {
            var isActive = panel.getAttribute('data-cliente-tab-panel') === tabName;
            panel.classList.toggle('is-active', isActive);
            panel.hidden = !isActive;
        });

        // Persistencia de solapa activa (sin SPA, sin backend): el hash de la URL
        // refleja la solapa visible. replaceState (nunca pushState) para no llenar
        // el historial de "Atras" con un paso por cada solapa — cambiar de solapa
        // sigue sintiendose como una sola pantalla, no una navegacion nueva.
        if (updateHash !== false && window.history && window.history.replaceState) {
            var nuevoHash = '#' + tabName;
            if (window.location.hash !== nuevoHash) {
                window.history.replaceState(null, '', nuevoHash);
            }
        }
    }

    function moveClienteTabFocus(current, direction) {
        var buttons = Array.prototype.slice.call(document.querySelectorAll('[data-cliente-tab]'));
        if (!buttons.length) return;

        var index = buttons.indexOf(current);
        if (index < 0) return;

        var nextIndex = (index + direction + buttons.length) % buttons.length;
        buttons[nextIndex].focus();
        setActiveClienteTab(buttons[nextIndex].getAttribute('data-cliente-tab'));
    }

    function initClienteTabs() {
        var buttons = document.querySelectorAll('[data-cliente-tab]');
        if (!buttons.length) return;

        buttons.forEach(function (button) {
            button.addEventListener('click', function () {
                setActiveClienteTab(button.getAttribute('data-cliente-tab'));
            });

            button.addEventListener('keydown', function (event) {
                if (event.key === 'ArrowRight') {
                    event.preventDefault();
                    moveClienteTabFocus(button, 1);
                } else if (event.key === 'ArrowLeft') {
                    event.preventDefault();
                    moveClienteTabFocus(button, -1);
                } else if (event.key === 'Home') {
                    event.preventDefault();
                    var first = document.querySelector('[data-cliente-tab]');
                    if (first) {
                        first.focus();
                        setActiveClienteTab(first.getAttribute('data-cliente-tab'));
                    }
                } else if (event.key === 'End') {
                    event.preventDefault();
                    var all = document.querySelectorAll('[data-cliente-tab]');
                    var last = all[all.length - 1];
                    if (last) {
                        last.focus();
                        setActiveClienteTab(last.getAttribute('data-cliente-tab'));
                    }
                }
            });
        });

        // Persistencia de solapa activa via hash (#credito, #documentacion, etc.):
        // si la URL trae un hash valido se abre esa solapa; si no hay hash o no
        // coincide con ninguna de las 5, cae a Resumen (nunca una pantalla en
        // blanco). updateHash:false porque esto es solo reflejar el estado, no
        // una interaccion que deba tocar el historial.
        function activarTabDesdeHash() {
            var tabNames = Array.prototype.map.call(
                document.querySelectorAll('[data-cliente-tab]'),
                function (b) { return b.getAttribute('data-cliente-tab'); }
            );
            var hashTab = (window.location.hash || '').replace('#', '');
            var tab = tabNames.indexOf(hashTab) !== -1 ? hashTab : 'resumen';
            setActiveClienteTab(tab, false);
        }

        activarTabDesdeHash();

        // Cubre tambien la navegacion "en la misma pagina" (un link a #documentacion
        // mientras ya se esta en #credito, o editar el hash a mano): el navegador no
        // recarga, solo dispara hashchange — sin este listener la solapa quedaria
        // desincronizada de la URL hasta el proximo reload real.
        window.addEventListener('hashchange', activarTabDesdeHash);

        // Al volver de una accion secundaria que redirige directo al "returnUrl"
        // recibido (Verificar/Rechazar/Subir documento — DocumentoClienteController
        // usa RedirectToReturnUrlOrIndex, que hace LocalRedirect literal a esa URL),
        // se le agrega el hash de la solapa activa al campo oculto antes de enviar
        // el form: el redirect llega con "#documentacion" y esta misma logica de
        // arriba vuelve a abrir esa solapa. Sin tocar el controller ni el contrato
        // de returnUrl (el backend sigue redirigiendo al string que ya recibia).
        // No aplica a acciones que redirigen con RedirectToAction(Details, ...)
        // (Recalcular aptitud/scoring, asignar/limpiar puntaje manual): esas arman
        // una URL nueva por routing y no preservan un fragmento — quedan en Resumen
        // tras usarse, igual que antes de este cambio (deuda documentada).
        document.addEventListener('submit', function (event) {
            var form = event.target;
            if (!form || typeof form.querySelector !== 'function') return;
            var returnUrlField = form.querySelector('input[name="returnUrl"]');
            if (!returnUrlField || !returnUrlField.value) return;
            if (returnUrlField.value.indexOf('#') !== -1) return;
            var activo = document.querySelector('[data-cliente-tab].is-active');
            if (!activo) return;
            returnUrlField.value += '#' + activo.getAttribute('data-cliente-tab');
        }, true);
    }

    function initClienteScrollAffordances() {
        var roots = Array.prototype.slice.call(document.querySelectorAll('[data-oc-scroll]'));
        roots.forEach(function (root) {
            if (window.TheBury && typeof window.TheBury.initHorizontalScrollAffordance === 'function') {
                window.TheBury.initHorizontalScrollAffordance(root);
            }
        });
    }

    function initClienteDetails() {
        initClienteTabs();
        initClienteScrollAffordances();

        document.querySelectorAll('[data-cliente-details-toggle]').forEach(function (button) {
            var sectionName = button.getAttribute('data-cliente-details-toggle');
            var content = document.getElementById('content-' + sectionName);
            setSectionState(button, !(content && content.classList.contains('hidden')));

            button.addEventListener('click', function () {
                var expanded = button.getAttribute('aria-expanded') === 'true';
                setSectionState(button, !expanded);
            });
        });

        document.querySelectorAll('[data-cliente-upload-open]').forEach(function (button) {
            button.addEventListener('click', function () {
                openUploadModal(
                    button.getAttribute('data-tipo-documento-value'),
                    button.getAttribute('data-tipo-documento-label'),
                    button.getAttribute('data-replace-id')
                );
            });
        });

        document.querySelectorAll('[data-cliente-upload-close]').forEach(function (button) {
            button.addEventListener('click', function () {
                closeUploadModal();
            });
        });

        var nivelManualModal = document.getElementById('nivelManualModal');
        var nivelManualForm = document.getElementById('nivelManualForm');
        var nivelManualSelect = document.getElementById('nivelCreditoManual');
        var nivelManualNuevoLimite = document.getElementById('nivelManualNuevoLimite');
        var nivelManualNuevoDisponible = document.getElementById('nivelManualNuevoDisponible');

        function formatMoney(value) {
            var amount = Number.isFinite(value) ? value : 0;
            return amount.toLocaleString('es-AR', {
                style: 'currency',
                currency: 'ARS',
                maximumFractionDigits: 0
            });
        }

        function updateNivelManualPreview() {
            if (!nivelManualSelect) return;

            var selected = nivelManualSelect.options[nivelManualSelect.selectedIndex];
            var limite = selected ? parseFloat(selected.getAttribute('data-limit') || '0') : 0;
            var saldoUsado = nivelManualForm
                ? parseFloat(nivelManualForm.getAttribute('data-saldo-usado') || '0')
                : 0;
            var disponible = Math.max(0, (Number.isFinite(limite) ? limite : 0) - (Number.isFinite(saldoUsado) ? saldoUsado : 0));

            if (nivelManualNuevoLimite) nivelManualNuevoLimite.textContent = formatMoney(limite);
            if (nivelManualNuevoDisponible) nivelManualNuevoDisponible.textContent = formatMoney(disponible);
        }

        function openNivelManualModal() {
            if (!nivelManualModal) return;
            updateNivelManualPreview();
            nivelManualModal.classList.remove('hidden');
            nivelManualModal.classList.add('flex');
            if (nivelManualSelect) nivelManualSelect.focus();
        }

        function closeNivelManualModal() {
            if (!nivelManualModal) return;
            nivelManualModal.classList.add('hidden');
            nivelManualModal.classList.remove('flex');
        }

        document.querySelectorAll('[data-cliente-open-nivel-manual]').forEach(function (button) {
            button.addEventListener('click', openNivelManualModal);
        });

        document.querySelectorAll('[data-cliente-close-nivel-manual]').forEach(function (button) {
            button.addEventListener('click', closeNivelManualModal);
        });

        if (nivelManualSelect) {
            nivelManualSelect.addEventListener('change', updateNivelManualPreview);
            updateNivelManualPreview();
        }

        if (nivelManualModal) {
            nivelManualModal.addEventListener('click', function (event) {
                if (event.target === nivelManualModal) closeNivelManualModal();
            });
        }

        var bcraButton = document.querySelector('[data-cliente-bcra-refresh]');
        if (bcraButton) {
            bcraButton.addEventListener('click', function () {
                actualizarBcra(bcraButton.getAttribute('data-cliente-id'));
            });
        }

        var rejectModal       = document.getElementById('rejectDocModal');
        var rejectMotivo      = document.getElementById('rejectDocMotivo');
        var rejectError       = document.getElementById('rejectDocMotivoError');
        var rejectConfirmBtn  = document.getElementById('rejectDocModalConfirmBtn');
        var rejectCancelBtn   = document.getElementById('rejectDocModalCancelBtn');
        var rejectCloseBtn    = document.getElementById('rejectDocModalClose');
        var pendingRejectForm = null;

        function openRejectModal(form) {
            if (!rejectModal) return false;
            pendingRejectForm = form;
            if (rejectMotivo) rejectMotivo.value = '';
            if (rejectError) rejectError.classList.add('hidden');
            rejectModal.classList.remove('hidden');
            rejectModal.classList.add('flex');
            if (rejectMotivo) rejectMotivo.focus();
            return true;
        }

        function closeRejectModal() {
            if (!rejectModal) return;
            rejectModal.classList.add('hidden');
            rejectModal.classList.remove('flex');
            pendingRejectForm = null;
        }

        if (rejectConfirmBtn) {
            rejectConfirmBtn.addEventListener('click', function () {
                var motivo = rejectMotivo ? rejectMotivo.value.trim() : '';
                if (!motivo) {
                    if (rejectError) rejectError.classList.remove('hidden');
                    if (rejectMotivo) rejectMotivo.focus();
                    return;
                }
                if (!pendingRejectForm) return;
                var hiddenInput = pendingRejectForm.querySelector('.motivo-hidden');
                if (hiddenInput) hiddenInput.value = motivo;
                closeRejectModal();
                pendingRejectForm.submit();
            });
        }

        if (rejectCancelBtn) rejectCancelBtn.addEventListener('click', closeRejectModal);
        if (rejectCloseBtn)  rejectCloseBtn.addEventListener('click', closeRejectModal);

        if (rejectModal) {
            rejectModal.addEventListener('click', function (event) {
                if (event.target === rejectModal) closeRejectModal();
            });
        }

        document.querySelectorAll('[data-cliente-reject-form]').forEach(function (form) {
            form.addEventListener('submit', function (event) {
                event.preventDefault();
                openRejectModal(form);
            });
        });

        var modal = document.getElementById('uploadDocModal');
        if (modal) {
            modal.addEventListener('click', function (event) {
                if (event.target === modal) {
                    closeUploadModal();
                }
            });
        }

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') {
                closeUploadModal();
                closeRejectModal();
                closeNivelManualModal();
            }
        });

        var fileInput = document.getElementById('modalArchivoInput');
        var dropZone = document.getElementById('modalDropZone');
        var dropContent = document.getElementById('modalDropContent');
        var filePreview = document.getElementById('modalFilePreview');
        var fileNameEl = document.getElementById('modalFileName');
        var fileSizeEl = document.getElementById('modalFileSize');

        function showModalFilePreview(file) {
            if (!file || !fileNameEl || !fileSizeEl || !dropContent || !filePreview) return;

            fileNameEl.textContent = file.name;
            var sizeMB = file.size / (1024 * 1024);
            fileSizeEl.textContent = sizeMB >= 1
                ? sizeMB.toFixed(2) + ' MB'
                : (file.size / 1024).toFixed(2) + ' KB';
            dropContent.classList.add('hidden');
            filePreview.classList.remove('hidden');
        }

        if (fileInput) {
            fileInput.addEventListener('change', function () {
                if (this.files && this.files[0]) {
                    showModalFilePreview(this.files[0]);
                }
            });
        }

        if (dropZone && fileInput) {
            dropZone.addEventListener('dragover', function (event) {
                event.preventDefault();
                dropZone.classList.add('border-primary', 'bg-primary/5');
            });
            dropZone.addEventListener('dragleave', function () {
                dropZone.classList.remove('border-primary', 'bg-primary/5');
            });
            dropZone.addEventListener('drop', function (event) {
                event.preventDefault();
                dropZone.classList.remove('border-primary', 'bg-primary/5');
                if (event.dataTransfer.files && event.dataTransfer.files[0]) {
                    fileInput.files = event.dataTransfer.files;
                    showModalFilePreview(event.dataTransfer.files[0]);
                }
            });
        }

        var uploadForm = document.getElementById('uploadDocForm');
        if (uploadForm) {
            uploadForm.addEventListener('submit', function (event) {
                var errorsEl = document.getElementById('uploadModalErrors');
                var messages = [];
                var tipo = document.getElementById('modalTipoDocumento');
                var archivo = document.getElementById('modalArchivoInput');

                if (!tipo || !tipo.value) messages.push('Seleccione un tipo de documento.');
                if (!archivo || !archivo.files || archivo.files.length === 0) {
                    messages.push('Debe seleccionar un archivo.');
                } else if (archivo.files[0].size > 5 * 1024 * 1024) {
                    messages.push('El archivo no puede superar los 5 MB.');
                }

                if (messages.length > 0) {
                    event.preventDefault();
                    if (errorsEl) {
                        errorsEl.innerHTML = messages.join('<br>');
                        errorsEl.classList.remove('hidden');
                    }
                }
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initClienteDetails);
    } else {
        initClienteDetails();
    }
})();
