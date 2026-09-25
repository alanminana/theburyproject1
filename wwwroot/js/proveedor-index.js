/**
 * proveedor-index.js
 *
 * Thin page wrapper for the shared Proveedor module + filtros automáticos del listado.
 */
(() => {
    const SEARCH_FOCUS_KEY = 'proveedor-index:search-focus';
    const SEARCH_DEBOUNCE_MS = 500;

    // Los filtros se aplican solos: al cambiar un select o el check, y al dejar de escribir en la búsqueda.
    function initAutoFilters() {
        const form = document.querySelector('[data-proveedor-auto-filter]');
        if (!form) return;

        const search = form.querySelector('input[type="search"]');
        const apply = () => (form.requestSubmit ? form.requestSubmit() : form.submit());

        form.addEventListener('change', event => {
            if (event.target.matches('select, input[type="checkbox"]')) apply();
        });

        if (!search) return;

        let timer = 0;
        search.addEventListener('input', () => {
            window.clearTimeout(timer);
            timer = window.setTimeout(() => {
                try { sessionStorage.setItem(SEARCH_FOCUS_KEY, '1'); } catch { /* sin storage: solo se pierde el foco */ }
                apply();
            }, SEARCH_DEBOUNCE_MS);
        });

        // La página se recarga al filtrar: se devuelve el foco (y el cursor al final) para seguir escribiendo.
        let restore = false;
        try {
            restore = sessionStorage.getItem(SEARCH_FOCUS_KEY) === '1';
            sessionStorage.removeItem(SEARCH_FOCUS_KEY);
        } catch { /* sin storage */ }
        if (restore) {
            search.focus({ preventScroll: true });
            const end = search.value.length;
            search.setSelectionRange(end, end);
        }
    }

    function init() {
        const moduleApi = window.TheBury && window.TheBury.ProveedorModule;
        if (moduleApi && typeof moduleApi.initIndex === 'function') {
            moduleApi.initIndex();
        }

        initAutoFilters();

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
