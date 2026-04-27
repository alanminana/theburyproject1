document.addEventListener('DOMContentLoaded', function () {
    const module = window.TheBury && window.TheBury.DocumentoClienteModule;
    if (module && typeof module.initUpload === 'function') {
        module.initUpload(document);
    }

    const form = document.querySelector('[data-documento-upload-form]') || document.getElementById('uploadForm');
    const archivoInput = document.getElementById('archivoInput');
    const dropZone = document.getElementById('dropZone');
    const dropZoneContent = document.getElementById('dropZoneContent');
    const filePreview = document.getElementById('filePreview');
    const fileNameEl = document.getElementById('fileName');
    const fileSizeEl = document.getElementById('fileSize');

    if (!archivoInput) return;

    function setArchivoError(message) {
        let errorEl = document.getElementById('archivoInput-error');
        if (!errorEl) {
            errorEl = document.createElement('p');
            errorEl.id = 'archivoInput-error';
            errorEl.className = 'mt-1 text-xs text-red-500';
            dropZone.insertAdjacentElement('afterend', errorEl);
        }

        errorEl.textContent = message;
        errorEl.classList.remove('hidden');
        dropZone.classList.add('border-red-500', 'bg-red-500/5');
        archivoInput.setAttribute('aria-invalid', 'true');
    }

    function clearArchivoError() {
        const errorEl = document.getElementById('archivoInput-error');
        if (errorEl) {
            errorEl.textContent = '';
            errorEl.classList.add('hidden');
        }

        dropZone.classList.remove('border-red-500', 'bg-red-500/5');
        archivoInput.removeAttribute('aria-invalid');
    }

    function showPreview(file) {
        if (!file) return;
        clearArchivoError();
        fileNameEl.textContent = file.name;
        const sizeMB = file.size / (1024 * 1024);
        fileSizeEl.textContent = sizeMB >= 1
            ? sizeMB.toFixed(2) + ' MB'
            : (file.size / 1024).toFixed(2) + ' KB';
        dropZoneContent.classList.add('hidden');
        filePreview.classList.remove('hidden');
    }

    archivoInput.addEventListener('change', function () {
        if (this.files && this.files[0]) {
            showPreview(this.files[0]);
        }
    });

    // Drag and drop visual feedback
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
            showPreview(e.dataTransfer.files[0]);
        }
    });

    // Pre-select TipoDocumento when the page receives an initial type from the server.
    var tipoParam = form ? form.getAttribute('data-documento-initial-tipo') : null;
    if (tipoParam) {
        var tipoSelect = document.getElementById('TipoDocumento');
        if (tipoSelect) {
            for (var i = 0; i < tipoSelect.options.length; i++) {
                if (tipoSelect.options[i].value === tipoParam) {
                    tipoSelect.value = tipoParam;
                    break;
                }
            }
        }
    }

    if (form) {
        form.addEventListener('submit', function (event) {
            if (!archivoInput.files || archivoInput.files.length === 0) {
                event.preventDefault();
                setArchivoError('Debe seleccionar un archivo');
                dropZone.scrollIntoView({ behavior: 'smooth', block: 'center' });
                return;
            }

            if (archivoInput.files[0].size === 0) {
                event.preventDefault();
                setArchivoError('El archivo seleccionado esta vacio');
            }
        });
    }

    // Hide empty validation summary
    var summary = document.querySelector('.hidden-when-empty');
    if (summary && summary.querySelector('ul') && summary.querySelectorAll('li').length === 0) {
        summary.style.display = 'none';
    }
});
