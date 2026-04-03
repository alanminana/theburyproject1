document.addEventListener('DOMContentLoaded', function () {
    var module = window.TheBury && window.TheBury.DocumentoClienteModule;
    if (!module || typeof module.initDetails !== 'function') return;

    module.initDetails(document);
});
