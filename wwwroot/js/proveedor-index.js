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

        if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
            window.TheBury.autoDismissToasts();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();

document.addEventListener('proveedor:toast', function (e) {
    var detail  = e.detail || {};
    var message = detail.message || '';
    var type    = detail.type || 'success';
    var isError = type === 'error';

    var div = document.createElement('div');
    div.className = 'toast-msg fixed bottom-4 right-4 z-[60] flex items-center gap-3 rounded-xl border px-4 py-3 shadow-lg text-sm font-semibold ' +
        (isError
            ? 'border-red-500/20 bg-red-500/10 text-red-600 dark:text-red-400'
            : 'border-emerald-500/20 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300');
    div.innerHTML = '<span class="material-symbols-outlined text-base">' + (isError ? 'error' : 'check_circle') + '</span>' +
        '<span>' + message.replace(/</g, '&lt;').replace(/>/g, '&gt;') + '</span>';
    document.body.appendChild(div);

    if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
        window.TheBury.autoDismissToasts(4000);
    }
});
