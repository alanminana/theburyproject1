/**
 * proveedor-detalles.js  –  Detalle de Proveedor
 *
 * - Carga productos asociados vía AJAX
 */
(() => {
    const fmt = n => n.toLocaleString('es-AR', { minimumFractionDigits: 0 });

    async function loadProductos() {
        const tbody = document.getElementById('productos-tbody');
        const footer = document.getElementById('productos-footer');
        const count = document.getElementById('productos-count');

        try {
            const res = await fetch(`/Proveedor/GetProductos/${proveedorId}`);
            const data = await res.json();

            if (!data.length) {
                tbody.innerHTML = `
                    <tr>
                        <td colspan="4" class="px-6 py-12 text-center">
                            <div class="flex flex-col items-center gap-2 text-slate-400">
                                <span class="material-symbols-outlined text-3xl">inventory_2</span>
                                <span class="text-sm">No hay productos asociados</span>
                            </div>
                        </td>
                    </tr>`;
                return;
            }

            tbody.innerHTML = data.map(p => `
                <tr class="hover:bg-slate-50 dark:hover:bg-slate-800/50 transition-colors">
                    <td class="px-6 py-4 font-medium">${p.nombre}</td>
                    <td class="px-6 py-4 text-center font-mono text-xs">${p.codigo || '—'}</td>
                    <td class="px-6 py-4 text-center">${fmt(p.stock)}</td>
                    <td class="px-6 py-4 text-right">$${fmt(p.precio)}</td>
                </tr>`).join('');

            footer.classList.remove('hidden');
            count.textContent = `${data.length} producto${data.length !== 1 ? 's' : ''} asociado${data.length !== 1 ? 's' : ''}`;
        } catch {
            tbody.innerHTML = `
                <tr>
                    <td colspan="4" class="px-6 py-8 text-center text-sm text-red-500">
                        Error al cargar productos. Intente recargar la página.
                    </td>
                </tr>`;
        }
    }

    document.addEventListener('DOMContentLoaded', loadProductos);
})();
