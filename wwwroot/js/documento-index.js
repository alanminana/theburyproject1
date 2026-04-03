document.addEventListener('DOMContentLoaded', function () {
    var module = window.TheBury && window.TheBury.DocumentoClienteModule;
    if (!module || typeof module.initIndex !== 'function') return;

    module.initIndex(document);
});
