/**
 * proveedor-module.js
 *
 * Shared frontend module for Proveedor pages.
 * Keeps page-specific wrappers small while centralizing:
 * - delegated modal actions
 * - delete confirmation
 * - horizontal scroll affordance init
 * - details AJAX loading
 */
(() => {
    window.TheBury = window.TheBury || {};

    if (window.TheBury.ProveedorModule) {
        return;
    }

    let eventsBound = false;

    const getCreateModalApi = () => typeof ProveedorCrearModal !== 'undefined' ? ProveedorCrearModal : null;
    const getEditModalApi = () => typeof ProveedorEditarModal !== 'undefined' ? ProveedorEditarModal : null;
    const formatInteger = value => Number(value || 0).toLocaleString('es-AR', { minimumFractionDigits: 0 });

    function getUi() {
        return globalThis.TheBury;
    }

    function getModalApi(target) {
        if (target === 'create') return getCreateModalApi();
        if (target === 'edit') return getEditModalApi();
        return null;
    }

    function invokeModalAction(target, action, proveedorId) {
        const api = getModalApi(target);
        if (!api) return;

        if (action === 'open' && typeof api.open === 'function') {
            if (target === 'edit') {
                if (Number.isFinite(proveedorId) && proveedorId > 0) {
                    api.open(proveedorId);
                }
                return;
            }

            api.open();
            return;
        }

        if (action === 'close' && typeof api.close === 'function') {
            api.close();
            return;
        }

        if (action === 'submit' && typeof api.submit === 'function') {
            api.submit();
        }
    }

    function initToastDismiss() {
        const ui = getUi();
        if (ui && typeof ui.autoDismissToasts === 'function') {
            ui.autoDismissToasts(4000);
        }
    }

    function initScrollAffordances() {
        const ui = getUi();
        if (!ui || typeof ui.initHorizontalScrollAffordance !== 'function') {
            return [];
        }

        return [...document.querySelectorAll('[data-oc-scroll]')]
            .map(root => ui.initHorizontalScrollAffordance(root))
            .filter(Boolean);
    }

    function bindModuleEvents() {
        if (eventsBound) {
            return;
        }

        eventsBound = true;

        document.addEventListener('click', event => {
            const modalTrigger = event.target.closest('[data-proveedor-modal-action]');
            if (modalTrigger) {
                event.preventDefault();

                const action = modalTrigger.getAttribute('data-proveedor-modal-action');
                const target = modalTrigger.getAttribute('data-proveedor-modal');
                const proveedorId = Number(modalTrigger.getAttribute('data-proveedor-id'));

                invokeModalAction(target, action, proveedorId);
                return;
            }
        });

        document.addEventListener('submit', event => {
            const form = event.target.closest('[data-proveedor-delete-form]');
            if (!form || form.dataset.confirmed === 'true') {
                return;
            }

            event.preventDefault();

            const proveedorNombre = form.getAttribute('data-proveedor-name') || 'este proveedor';
            const message = `¿Estás seguro de eliminar "${proveedorNombre}"? Esta acción no se puede deshacer.`;
            const ui = getUi();

            if (ui && typeof ui.confirmAction === 'function') {
                ui.confirmAction(message, () => {
                    form.dataset.confirmed = 'true';
                    form.submit();
                });
                return;
            }

            form.dataset.confirmed = 'true';
            form.submit();
        });
    }

    function getDetailsRoot() {
        return document.querySelector('[data-proveedor-details]');
    }

    function getProveedorId() {
        const root = getDetailsRoot();
        if (!root) {
            return null;
        }

        const proveedorId = Number(root.getAttribute('data-proveedor-id'));
        return Number.isFinite(proveedorId) ? proveedorId : null;
    }

    // El badge tiene un ícono fijo: solo se reemplaza el texto.
    function setBadge(badge, text) {
        const target = badge.querySelector('[data-productos-badge-text]') || badge;
        target.textContent = text;
    }

    function setProductosLoadingState(tbody) {
        tbody.innerHTML = `
            <tr>
                <td colspan="4" class="px-6 py-8 text-center text-sm text-slate-400">
                    <div class="flex items-center justify-center gap-2">
                        <div class="w-5 h-5 border-2 border-primary border-t-transparent rounded-full animate-spin"></div>
                        Cargando productos…
                    </div>
                </td>
            </tr>`;
    }

    function renderProductosEmptyState(tbody, footer, badge) {
        tbody.innerHTML = `
            <tr>
                <td colspan="4" class="px-6 py-12 text-center">
                    <div class="flex flex-col items-center gap-2 text-slate-400">
                        <span class="material-symbols-outlined text-3xl">inventory_2</span>
                        <span class="text-sm">No hay productos asociados</span>
                    </div>
                </td>
            </tr>`;

        footer.classList.add('hidden');
        setBadge(badge, 'Sin productos');
    }

    function renderProductos(data, tbody, footer, count, badge) {
        tbody.replaceChildren();

        data.forEach(producto => {
            const tr = document.createElement('tr');
            tr.className = 'hover:bg-white/5 transition-colors';

            const tdNombre = document.createElement('td');
            tdNombre.className = 'px-6 py-4 font-medium text-white';
            tdNombre.textContent = producto.nombre;

            const tdCodigo = document.createElement('td');
            tdCodigo.className = 'px-6 py-4 text-center font-mono text-xs';
            tdCodigo.textContent = producto.codigo || '—';

            const tdStock = document.createElement('td');
            tdStock.className = 'px-6 py-4 text-center';
            tdStock.textContent = formatInteger(producto.stock);

            const tdPrecio = document.createElement('td');
            tdPrecio.className = 'px-6 py-4 text-right';
            tdPrecio.textContent = '$' + formatInteger(producto.precio);

            tr.appendChild(tdNombre);
            tr.appendChild(tdCodigo);
            tr.appendChild(tdStock);
            tr.appendChild(tdPrecio);
            tbody.appendChild(tr);
        });

        const label = `${data.length} producto${data.length !== 1 ? 's' : ''} asociado${data.length !== 1 ? 's' : ''}`;
        footer.classList.remove('hidden');
        count.textContent = label;
        setBadge(badge, label);
    }

    async function loadProductos(scrollAffordance) {
        const proveedorId = getProveedorId();
        const tbody = document.getElementById('productos-tbody');
        const footer = document.getElementById('productos-footer');
        const count = document.getElementById('productos-count');
        const badge = document.getElementById('productos-badge');

        if (!proveedorId || !tbody || !footer || !count || !badge) {
            return;
        }

        setProductosLoadingState(tbody);

        try {
            const response = await fetch(`/Proveedor/GetProductos/${proveedorId}`);
            // Un 500 no es "sin productos": tiene que verse como error, no como lista vacía.
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const data = await response.json();

            if (!Array.isArray(data) || !data.length) {
                renderProductosEmptyState(tbody, footer, badge);
            } else {
                renderProductos(data, tbody, footer, count, badge);
            }
        } catch {
            tbody.innerHTML = `
                <tr>
                    <td colspan="4" class="px-6 py-8 text-center text-sm text-red-400">
                        No se pudieron cargar los productos. Recargá la página para reintentar.
                    </td>
                </tr>`;
            footer.classList.add('hidden');
            setBadge(badge, 'Error');
        } finally {
            if (scrollAffordance && typeof scrollAffordance.update === 'function') {
                scrollAffordance.update();
            }
        }
    }

    function initIndex() {
        bindModuleEvents();
        initToastDismiss();
        initScrollAffordances();
        if (typeof ProveedorProductPicker !== 'undefined') {
            ProveedorProductPicker.init();
        }
    }

    function initDetails() {
        bindModuleEvents();
        initToastDismiss();
        // El drawer de edición de Details necesita el picker: sin él no carga los productos del proveedor
        // y al guardar se borrarían.
        if (typeof ProveedorProductPicker !== 'undefined') {
            ProveedorProductPicker.init();
        }
        const [scrollAffordance] = initScrollAffordances();
        loadProductos(scrollAffordance);
    }

    window.TheBury.ProveedorModule = {
        initIndex,
        initDetails
    };
})();
