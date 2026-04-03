/**
 * proveedor-index.js
 *
 * Thin page wrapper for the shared Proveedor module.
 */
(() => {
    function init() {
        const moduleApi = window.TheBury && window.TheBury.ProveedorModule;
        if (moduleApi && typeof moduleApi.initIndex === 'function') {
            moduleApi.initIndex();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
