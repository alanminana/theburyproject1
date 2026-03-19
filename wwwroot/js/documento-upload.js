document.addEventListener('DOMContentLoaded', function () {
    const archivoInput = document.getElementById('archivoInput');
    const dropZone = document.getElementById('dropZone');
    const dropZoneContent = document.getElementById('dropZoneContent');
    const filePreview = document.getElementById('filePreview');
    const fileNameEl = document.getElementById('fileName');
    const fileSizeEl = document.getElementById('fileSize');

    if (!archivoInput) return;

    function showPreview(file) {
        if (!file) return;
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

    // Pre-select TipoDocumento from query string
    var params = new URLSearchParams(window.location.search);
    var tipoParam = params.get('tipoDocumento');
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

    // Hide empty validation summary
    var summary = document.querySelector('.hidden-when-empty');
    if (summary && summary.querySelector('ul') && summary.querySelectorAll('li').length === 0) {
        summary.style.display = 'none';
    }
});
