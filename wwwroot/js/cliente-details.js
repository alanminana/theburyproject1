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
        var descEl = document.getElementById('bcra-desc');
        var dotEl = document.getElementById('bcra-dot');
        var metaEl = document.getElementById('bcra-meta');

        if (!btn || !descEl) return;

        btn.disabled = true;
        if (icon) icon.classList.add('animate-spin');

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

                if (!data.ok) {
                    color = 'text-slate-400';
                    dotColor = 'bg-slate-400';
                } else if (data.situacion === 0 || data.situacion === 1) {
                    color = 'text-green-500';
                    dotColor = 'bg-green-500';
                } else if (data.situacion === 2) {
                    color = 'text-amber-500';
                    dotColor = 'bg-amber-500';
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
            })
            .catch(function () {
                if (descEl) descEl.textContent = 'Error al consultar';
            })
            .finally(function () {
                btn.disabled = false;
                if (icon) icon.classList.remove('animate-spin');
            });
    }

    function initClienteDetails() {
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
                } else if (archivo.files[0].size > 10 * 1024 * 1024) {
                    messages.push('El archivo no puede superar los 10 MB.');
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
