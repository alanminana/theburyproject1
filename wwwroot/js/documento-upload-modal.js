/**
 * documento-upload-modal.js
 * Lógica del modal "Subir / Reemplazar Documento" en DocumentoCliente/Index_tw.
 */
(function () {
    'use strict';

    var modal        = document.getElementById('modal-upload-documento');
    var backdrop     = document.getElementById('modal-upload-backdrop');
    var btnAbrir     = document.getElementById('btn-abrir-upload-modal');
    var btnCerrar    = document.getElementById('btn-cerrar-upload-modal');
    var btnCancelar  = document.getElementById('btn-cancelar-upload-modal');
    var form         = document.getElementById('form-upload-modal');
    var btnSubmit    = document.getElementById('btn-submit-upload-modal');
    var submitLabel  = document.getElementById('btn-submit-upload-label');
    var titulo       = document.getElementById('modal-upload-titulo');
    var tituloCrumb  = document.getElementById('modal-upload-titulo-breadcrumb');

    // Dropzone refs
    var dropZone        = document.getElementById('modal-dropZone');
    var archivoInput    = document.getElementById('modal-archivoInput');
    var dropContent     = document.getElementById('modal-dropZoneContent');
    var filePreview     = document.getElementById('modal-filePreview');
    var fileNameEl      = document.getElementById('modal-fileName');
    var fileSizeEl      = document.getElementById('modal-fileSize');

    // Reemplazo refs
    var reemplazarInput = document.getElementById('modal-upload-reemplazar');
    var replaceIdInput  = document.getElementById('modal-upload-replaceId');
    var reemplAv        = document.getElementById('modal-upload-reemplazo-aviso');
    var reemplNombre    = document.getElementById('modal-upload-reemplazo-nombre');

    if (!modal || !form) return;

    // ── Abrir / Cerrar ───────────────────────────────────────
    function open(opts) {
        opts = opts || {};

        // Resetear formulario
        form.reset();
        resetDropzone();
        hideValidation();

        // Modo reemplazo
        if (opts.replaceId) {
            if (reemplazarInput) reemplazarInput.value = 'true';
            if (replaceIdInput)  replaceIdInput.value  = opts.replaceId;
            if (reemplAv)        reemplAv.classList.remove('hidden');
            if (reemplNombre)    reemplNombre.textContent = opts.replaceNombre || 'documento existente';
            if (titulo)          titulo.textContent = 'Reemplazar Documento';
            if (tituloCrumb)     tituloCrumb.textContent = 'Reemplazar';
            if (submitLabel)     submitLabel.textContent  = 'Reemplazar Documento';
        } else {
            if (reemplazarInput) reemplazarInput.value = 'false';
            if (replaceIdInput)  replaceIdInput.value  = '';
            if (reemplAv)        reemplAv.classList.add('hidden');
            if (titulo)          titulo.textContent = 'Subir Documento';
            if (tituloCrumb)     tituloCrumb.textContent = 'Subir';
            if (submitLabel)     submitLabel.textContent  = 'Subir Documento';
        }

        modal.classList.remove('hidden');
        modal.classList.add('flex');
        document.body.style.overflow = 'hidden';
    }

    function close() {
        modal.classList.add('hidden');
        modal.classList.remove('flex');
        document.body.style.overflow = '';
    }

    // ── Dropzone ─────────────────────────────────────────────
    function showFilePreview(file) {
        if (!file) return;
        fileNameEl.textContent = file.name;
        var sizeMB = file.size / (1024 * 1024);
        fileSizeEl.textContent = sizeMB >= 1
            ? sizeMB.toFixed(2) + ' MB'
            : (file.size / 1024).toFixed(2) + ' KB';
        dropContent.classList.add('hidden');
        filePreview.classList.remove('hidden');
    }

    function resetDropzone() {
        if (dropContent) dropContent.classList.remove('hidden');
        if (filePreview) filePreview.classList.add('hidden');
        if (dropZone)    dropZone.classList.remove('border-primary', 'bg-primary/5');
    }

    if (archivoInput) {
        archivoInput.addEventListener('change', function () {
            if (this.files && this.files[0]) showFilePreview(this.files[0]);
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
                archivoInput.files = e.dataTransfer.files;
                showFilePreview(e.dataTransfer.files[0]);
            }
        });
    }

    // ── Validación ───────────────────────────────────────────
    function showValidation(text) {
        var box = document.getElementById('modal-upload-validation');
        var msg = document.getElementById('modal-upload-validation-text');
        if (box && msg) {
            msg.textContent = text;
            box.classList.remove('hidden');
            box.classList.add('flex');
        }
    }

    function hideValidation() {
        var box = document.getElementById('modal-upload-validation');
        if (box) {
            box.classList.add('hidden');
            box.classList.remove('flex');
        }
    }

    // ── Submit ───────────────────────────────────────────────
    form.addEventListener('submit', function (e) {
        hideValidation();

        var errors = [];

        var clienteEl = form.querySelector('[name="ClienteId"]');
        if (clienteEl && !clienteEl.value) errors.push('El cliente es obligatorio');

        var tipoEl = form.querySelector('[name="TipoDocumento"]');
        if (tipoEl && !tipoEl.value) errors.push('El tipo de documento es obligatorio');

        if (!archivoInput || !archivoInput.files || !archivoInput.files.length) {
            errors.push('Seleccioná un archivo');
        }

        if (errors.length) {
            e.preventDefault();
            showValidation(errors.join('. '));
            return;
        }

        // Deshabilitar botón para evitar doble submit
        if (btnSubmit) {
            btnSubmit.disabled = true;
            if (submitLabel) {
                submitLabel.textContent = (reemplazarInput && reemplazarInput.value === 'true')
                    ? 'Reemplazando...'
                    : 'Subiendo...';
            }
        }
    });

    // ── Eventos ──────────────────────────────────────────────
    if (btnAbrir)   btnAbrir.addEventListener('click', function () { open(); });
    if (btnCerrar)  btnCerrar.addEventListener('click', close);
    if (btnCancelar) btnCancelar.addEventListener('click', close);
    if (backdrop)   backdrop.addEventListener('click', close);

    // Botones "Subir Documento" del estado vacío
    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-documento-open-upload]')) {
            open();
        }
    });

    // Botones "Reemplazar" de la tabla → abrir modal en modo reemplazo
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-documento-replace]');
        if (!btn) return;
        e.preventDefault();
        open({
            replaceId:     btn.dataset.documentoReplaceId,
            replaceNombre: btn.dataset.documentoReplaceNombre
        });
    });

    // Esc para cerrar
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !modal.classList.contains('hidden')) {
            close();
        }
    });
})();
