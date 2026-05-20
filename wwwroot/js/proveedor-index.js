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

    var typeMap = {
        success: { cls: 'alert-erp-success', icon: 'check_circle', role: 'status' },
        error:   { cls: 'alert-erp-error',   icon: 'error',        role: 'alert'  },
        warning: { cls: 'alert-erp-warning', icon: 'warning',      role: 'alert'  }
    };
    var v = typeMap[type] || typeMap.success;

    var div = document.createElement('div');
    div.className = 'toast-msg alert-erp ' + v.cls + ' fixed bottom-4 right-4 z-[60] shadow-lg';
    div.setAttribute('role', v.role);
    var iconSpan = document.createElement('span');
    iconSpan.className = 'material-symbols-outlined';
    iconSpan.textContent = v.icon;
    div.appendChild(iconSpan);
    div.appendChild(document.createTextNode(message));
    document.body.appendChild(div);

    if (window.TheBury && typeof window.TheBury.autoDismissToasts === 'function') {
        window.TheBury.autoDismissToasts(4000);
    }
});
