/**
 * ajuste-stock-modal.js
 * Diálogo «Registrar ajuste de stock» (_AjusteStockModal.cshtml). El envío es un POST real de formulario: este script
 * abre y cierra el diálogo, explica el efecto del tipo elegido, arma la confirmación (stock actual → resultante) y,
 * si el servidor rechazó el envío, lo reabre con lo escrito.
 */

/** Confirmación de un movimiento de stock (qué cambia y cómo queda). La usan el diálogo y el formulario de la ficha. */
window.InvAjusteStock = (() => {
    const numero = (valor) => Number(valor).toLocaleString('es-AR', { maximumFractionDigits: 2 });

    function resultante(tipo, cantidad, actual) {
        const valor = Number(cantidad);
        if (actual === null || cantidad === '' || Number.isNaN(valor)) return null;
        if (tipo === 'Entrada') return actual + valor;
        if (tipo === 'Salida') return actual - valor;
        return valor;
    }

    /** datos: { producto, tipo: 'Entrada'|'Salida'|'Ajuste', cantidad: string, actual: number|null } */
    function configurar(formulario, datos) {
        const nuevo = resultante(datos.tipo, datos.cantidad, datos.actual);
        const filas = [
            ['Producto', datos.producto || '—'],
            ['Movimiento', datos.tipo],
            ['Cantidad', datos.cantidad === '' ? '—' : numero(datos.cantidad)]
        ];
        if (nuevo !== null) filas.push(['Stock', numero(datos.actual) + ' → ' + numero(nuevo)]);
        filas.push({ etiqueta: 'Motivo', campo: 'Motivo' });
        formulario.setAttribute('data-inv-confirm', JSON.stringify({
            titulo: '¿Registrar este movimiento de stock?',
            datos: filas,
            avisos: [nuevo !== null && nuevo < 0 ? 'No hay stock suficiente: el servidor va a rechazar este movimiento.' : ''],
            consecuencias: [
                datos.tipo === 'Ajuste' ? 'El stock queda fijado en la cantidad indicada, sin sumar ni restar.' : 'El stock del producto cambia de inmediato.',
                'Queda un movimiento registrado con tu usuario. No modifica unidades con número de serie.'
            ],
            etiquetaConfirmar: 'Registrar movimiento',
            tono: 'primario'
        }));
    }

    // Formulario en página (ficha de inventario): <form data-ajuste-inline data-producto data-stock-actual>.
    document.querySelectorAll('form[data-ajuste-inline]').forEach((formulario) => {
        const actualizar = () => configurar(formulario, {
            producto: formulario.dataset.producto,
            tipo: formulario.elements.namedItem('Tipo').value,
            cantidad: formulario.elements.namedItem('Cantidad').value,
            actual: Number(formulario.dataset.stockActual)
        });
        formulario.addEventListener('input', actualizar);
        formulario.addEventListener('change', actualizar);
        actualizar();
    });

    return { configurar };
})();

(() => {
    const dialogo = document.getElementById('modal-ajuste-stock');
    if (!dialogo) return;

    const form = document.getElementById('form-ajuste-stock');
    const tipo = document.getElementById('ajuste-stock-tipo');
    const cantidad = document.getElementById('ajuste-stock-cantidad');
    const producto = document.getElementById('ajuste-stock-producto');
    const ayudaStock = document.getElementById('ajuste-stock-actual');
    const ayudaTipo = document.getElementById('ajuste-stock-hint');
    const BORRADOR = 'erp:ajuste-stock:borrador';
    const CAMPOS = ['ProductoId', 'Tipo', 'Cantidad', 'Motivo', 'Referencia'];

    const AYUDAS = {
        Entrada: 'Suma la cantidad al stock actual.',
        Salida: 'Resta la cantidad del stock actual.',
        Ajuste: 'Fija el stock en el valor exacto indicado (inventario físico).'
    };

    let stockActual = form.dataset.stockActual === '' ? null : Number(form.dataset.stockActual);

    const panel = window.InvUI.panelFormulario(dialogo, {
        cierres: '[data-ajuste-stock-close]',
        alCerrar: () => {
            form.reset();
            stockActual = form.dataset.stockActual === '' ? null : Number(form.dataset.stockActual);
            if (ayudaStock) ayudaStock.textContent = '';
            mostrarError('');
            actualizar();
        }
    });

    function numero(valor) {
        return Number(valor).toLocaleString('es-AR', { maximumFractionDigits: 2 });
    }

    function nombreTipo() {
        return tipo.options[tipo.selectedIndex]?.text || '';
    }

    function nombreProducto() {
        if (producto) return producto.value ? producto.options[producto.selectedIndex].text : '';
        return dialogo.querySelector('[data-ajuste-producto-nombre]')?.textContent.trim() || '';
    }

    // La confirmación compartida lee data-inv-confirm al enviar: se mantiene al día con lo que está en el formulario.
    function actualizar() {
        ayudaTipo.textContent = AYUDAS[nombreTipo()] || '';
        window.InvAjusteStock.configurar(form, {
            producto: nombreProducto(), tipo: nombreTipo(), cantidad: cantidad.value, actual: stockActual
        });
    }

    function cargarStock() {
        stockActual = null;
        if (ayudaStock) ayudaStock.textContent = '';
        actualizar();
        const id = producto.value;
        if (!id) return Promise.resolve();
        return fetch('/MovimientoStock/GetProductoInfo/' + encodeURIComponent(id), { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then((r) => (r.ok ? r.json() : null))
            .then((info) => {
                if (producto.value !== id || !info) return;
                const valor = info.stockActual ?? info.StockActual;
                if (valor === undefined || valor === null) return;
                stockActual = Number(valor);
                ayudaStock.textContent = 'Stock actual: ' + numero(stockActual);
                actualizar();
            })
            .catch(() => { /* sin dato de stock la confirmación omite el resultado; el servidor valida igual */ });
    }

    function mostrarError(texto) {
        const caja = dialogo.querySelector('[data-ajuste-error]');
        caja.textContent = texto;
        caja.hidden = !texto;
    }

    function abrir() {
        panel.abrir();
        actualizar();
        (producto || tipo).focus();
    }

    document.addEventListener('click', (e) => {
        if (e.target.closest('[data-ajuste-stock-open]')) abrir();
    });
    if (producto) producto.addEventListener('change', cargarStock);
    form.addEventListener('input', actualizar);
    form.addEventListener('change', actualizar);

    // Se guarda recién cuando el envío sigue adelante (ya confirmado).
    form.addEventListener('submit', (e) => {
        if (e.defaultPrevented) return;
        const datos = { ruta: window.location.pathname };
        CAMPOS.forEach((nombre) => { datos[nombre] = form.elements.namedItem(nombre)?.value ?? ''; });
        try { sessionStorage.setItem(BORRADOR, JSON.stringify(datos)); } catch (_) { /* sin almacenamiento: no se restaura */ }
    });

    actualizar();

    let borrador = null;
    try {
        borrador = JSON.parse(sessionStorage.getItem(BORRADOR) || 'null');
        sessionStorage.removeItem(BORRADOR);
    } catch (_) { borrador = null; }

    const errorServidor = dialogo.dataset.error;
    if (borrador && errorServidor && borrador.ruta === window.location.pathname) {
        abrir();
        CAMPOS.forEach((nombre) => {
            const campo = form.elements.namedItem(nombre);
            if (campo && campo.type !== 'hidden') campo.value = borrador[nombre];
        });
        mostrarError(errorServidor);
        if (producto) cargarStock(); else actualizar();
        cantidad.focus();
    }
})();
