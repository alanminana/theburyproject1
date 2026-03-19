/* cliente-details.js — Lógica para Details_tw de Cliente */

/** Expande o colapsa una sección del detalle */
function toggleDetailsSection(name) {
    var content = document.getElementById('content-' + name);
    var chevron = document.getElementById('chevron-' + name);
    if (!content || !chevron) return;

    var isHidden = content.classList.contains('hidden');
    content.classList.toggle('hidden', !isHidden);
    chevron.textContent = isHidden ? 'expand_less' : 'expand_more';
}

/* ═══════ Modal: Subir Documento ═══════ */

// Map de nombre legible → valor enum (incluye variantes con y sin espacios)
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

function abrirUploadModal(tipoDocValue, tipoDocLabel, replaceId) {
    var modal = document.getElementById('uploadDocModal');
    var select = document.getElementById('modalTipoDocumento');
    var subtitle = document.getElementById('uploadModalSubtitle');
    var errors = document.getElementById('uploadModalErrors');
    var replaceField = document.getElementById('modalReplaceId');
    var replaceFlag = document.getElementById('modalReemplazar');
    var submitBtn = document.getElementById('btnSubirDoc');

    if (!modal) return;

    // Reset form
    var form = document.getElementById('uploadDocForm');
    if (form) {
        form.querySelectorAll('input[type="file"]').forEach(function (f) { f.value = ''; });
        document.getElementById('modalFechaVencimiento').value = '';
        document.getElementById('modalObservaciones').value = '';
    }

    // Reset file preview
    var dropContent = document.getElementById('modalDropContent');
    var filePreview = document.getElementById('modalFilePreview');
    if (dropContent) dropContent.classList.remove('hidden');
    if (filePreview) filePreview.classList.add('hidden');

    // Hide errors
    if (errors) { errors.classList.add('hidden'); errors.textContent = ''; }

    // Pre-select tipo documento
    if (select) {
        var val = tipoDocValue ? String(tipoDocValue) : '';
        if (!val && tipoDocLabel && tipoDocNombreMap[tipoDocLabel]) {
            val = tipoDocNombreMap[tipoDocLabel];
        }
        select.value = val;
    }

    // Replace mode
    if (replaceField && replaceFlag) {
        if (replaceId) {
            replaceField.value = replaceId;
            replaceFlag.value = 'true';
        } else {
            replaceField.value = '';
            replaceFlag.value = 'false';
        }
    }

    // Update subtitle & button
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

    // Show modal
    modal.classList.remove('hidden');
    modal.classList.add('flex');
}

function cerrarUploadModal() {
    var modal = document.getElementById('uploadDocModal');
    if (!modal) return;
    modal.classList.add('hidden');
    modal.classList.remove('flex');
}

/** Solicita motivo antes de enviar el form de rechazo */
function solicitarMotivoRechazo(form) {
    var motivo = prompt('Indique el motivo del rechazo:');
    if (!motivo || !motivo.trim()) return false;
    form.querySelector('.motivo-hidden').value = motivo.trim();
    return true;
}

document.addEventListener('DOMContentLoaded', function () {
    var modal = document.getElementById('uploadDocModal');
    if (!modal) return;

    // Close on backdrop click
    modal.addEventListener('click', function (e) {
        if (e.target === modal) cerrarUploadModal();
    });

    // Close on Escape
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !modal.classList.contains('hidden')) {
            cerrarUploadModal();
        }
    });

    // File preview in modal
    var fileInput = document.getElementById('modalArchivoInput');
    var dropZone = document.getElementById('modalDropZone');
    var dropContent = document.getElementById('modalDropContent');
    var filePreview = document.getElementById('modalFilePreview');
    var fileNameEl = document.getElementById('modalFileName');
    var fileSizeEl = document.getElementById('modalFileSize');

    function showModalFilePreview(file) {
        if (!file) return;
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
            if (this.files && this.files[0]) showModalFilePreview(this.files[0]);
        });
    }

    if (dropZone) {
        dropZone.addEventListener('dragover', function (e) {
            e.preventDefault();
            dropZone.classList.add('border-primary', 'bg-primary/5');
        });
        dropZone.addEventListener('dragleave', function () {
            dropZone.classList.remove('border-primary', 'bg-primary/5');
        });
        dropZone.addEventListener('drop', function (e) {
            e.preventDefault();
            dropZone.classList.remove('border-primary', 'bg-primary/5');
            if (e.dataTransfer.files && e.dataTransfer.files[0]) {
                fileInput.files = e.dataTransfer.files;
                showModalFilePreview(e.dataTransfer.files[0]);
            }
        });
    }

    // Client-side validation before submit
    var form = document.getElementById('uploadDocForm');
    if (form) {
        form.addEventListener('submit', function (e) {
            var errorsEl = document.getElementById('uploadModalErrors');
            var msgs = [];

            var tipo = document.getElementById('modalTipoDocumento');
            if (!tipo || !tipo.value) msgs.push('Seleccione un tipo de documento.');

            var archivo = document.getElementById('modalArchivoInput');
            if (!archivo || !archivo.files || archivo.files.length === 0) {
                msgs.push('Debe seleccionar un archivo.');
            } else if (archivo.files[0].size > 10 * 1024 * 1024) {
                msgs.push('El archivo no puede superar los 10 MB.');
            }

            if (msgs.length > 0) {
                e.preventDefault();
                if (errorsEl) {
                    errorsEl.innerHTML = msgs.join('<br>');
                    errorsEl.classList.remove('hidden');
                }
            }
        });
    }
});

/* ═══════ BCRA: Actualizar Situación Crediticia ═══════ */

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
    .then(function (r) { return r.json(); })
    .then(function (data) {
        if (descEl) descEl.textContent = data.descripcion || 'Sin información';

        var color, dotColor;
        if (!data.ok) {
            color = 'text-slate-400'; dotColor = 'bg-slate-400';
        } else if (data.situacion === 0 || data.situacion === 1) {
            color = 'text-green-500'; dotColor = 'bg-green-500';
        } else if (data.situacion === 2) {
            color = 'text-amber-500'; dotColor = 'bg-amber-500';
        } else {
            color = 'text-red-500'; dotColor = 'bg-red-500';
        }

        if (descEl) {
            descEl.className = 'font-bold ' + color;
        }
        if (dotEl) {
            dotEl.className = 'h-2 w-2 rounded-full ' + dotColor;
        }
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
