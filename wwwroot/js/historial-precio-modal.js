/**
 * historial-precio-modal.js – Historial de cambios de precio por producto.
 * La confirmación de la reversión usa InvUI.confirmar.
 */
const HistorialPrecioModal = (() => {
    const catalogoModule = typeof CatalogoModule !== 'undefined' ? CatalogoModule : null;
    const modal = () => document.getElementById('modal-historial-precio');
    const tbody = () => document.getElementById('historial-precio-tbody');
    const loading = () => document.getElementById('historial-precio-loading');
    const empty = () => document.getElementById('historial-precio-empty');
    const nombre = () => document.getElementById('historial-producto-nombre');
    const codigo = () => document.getElementById('historial-producto-codigo');
    const feedback = () => document.getElementById('historial-precio-feedback');
    const count = () => document.getElementById('historial-precio-count');

    let productoIdActual = null;

    function dispatchScrollRefresh() {
        if (catalogoModule && typeof catalogoModule.requestScrollRefresh === 'function') {
            catalogoModule.requestScrollRefresh();
            return;
        }

        document.dispatchEvent(new CustomEvent('catalogo:refresh-scroll'));
    }

    function show(element) {
        if (element) {
            element.classList.remove('hidden');
        }
    }

    function hide(element) {
        if (element) {
            element.classList.add('hidden');
        }
    }

    function clearFeedback() {
        const box = feedback();
        if (!box) return;

        box.textContent = '';
        box.className = 'hidden';
    }

    function updateCount(total) {
        const badge = count();
        if (!badge) return;
        badge.textContent = total + ' evento' + (total === 1 ? '' : 's');
    }

    function showFeedback(message, type) {
        const box = feedback();
        if (!box) return;

        box.textContent = message;
        box.className = 'inv-aviso inv-drawer__aviso ' + (type === 'success' ? 'inv-aviso--ok' : 'inv-aviso--error');
    }

    function fmt(n) {
        return '$' + Number(n).toLocaleString('es-AR', {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    }

    // Los datos del historial (motivo, usuario, fecha) son texto libre persistido: se renderizan siempre
    // como nodos DOM con textContent, nunca concatenados a HTML (COM-S-001).
    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = String(text);
        return node;
    }

    function cell(className, ...children) {
        const td = el('td', className);
        children.forEach((child) => child && td.appendChild(child));
        return td;
    }

    function variacionNode(anterior, nuevo) {
        if (!anterior) return el('span', '', '—');

        const diff = nuevo - anterior;
        if (!diff) return el('span', '', '0%');

        const signo = diff > 0 ? '+' : '−';
        const wrap = el('span', diff > 0 ? 'inv-text-danger' : 'inv-text-ok');
        wrap.appendChild(el('strong', '', signo + Math.abs((diff / anterior) * 100).toFixed(1) + '%'));
        wrap.appendChild(el('span', 'inv-ayuda-texto inv-bloque', signo + fmt(Math.abs(diff))));
        return wrap;
    }

    function getToken() {
        const element = document.querySelector('input[name="__RequestVerificationToken"]');
        return element ? element.value : '';
    }

    // Datos de cada evento para describir en la confirmación qué se va a restaurar.
    const eventos = new Map();

    function renderRows(items) {
        const tableBody = tbody();
        const tabla = document.getElementById('historial-precio-tabla');
        tableBody.innerHTML = '';
        eventos.clear();
        updateCount(items ? items.length : 0);

        if (!items || items.length === 0) {
            tabla.hidden = true;
            show(empty());
            dispatchScrollRefresh();
            return;
        }

        hide(empty());
        tabla.hidden = false;

        items.forEach((item) => {
            const partes = (item.fecha || '').split('·');
            const fechaStr = (partes[0] || '').trim();
            const horaStr = (partes[1] || '').trim();
            const eventoId = parseInt(item.eventoId, 10);
            eventos.set(eventoId, item);

            let accion = null;
            if (item.puedeRevertir) {
                accion = el('button', 'inv-btn inv-btn--ghost inv-btn--sm', 'Revertir');
                accion.type = 'button';
                accion.setAttribute('data-catalogo-action', 'historial-revert');
                accion.setAttribute('data-catalogo-evento-id', String(eventoId));
                accion.setAttribute('aria-label', 'Revertir el cambio del ' + fechaStr + ' ' + horaStr);
            }

            const fecha = el('th', '', fechaStr);
            fecha.scope = 'row';
            fecha.appendChild(el('p', 'inv-ayuda-texto', horaStr));

            const row = el('tr');
            row.appendChild(fecha);
            row.appendChild(celda('Motivo y usuario', 'inv-celda-detalle',
                el('span', '', item.motivo || 'Sin motivo'),
                el('p', 'inv-ayuda-texto', item.usuario ? String(item.usuario) : '—')));
            row.appendChild(celda('Precio anterior', 'inv-col-num', el('span', '', fmt(item.precioAnterior))));
            row.appendChild(celda('Precio nuevo', 'inv-col-num', el('span', 'inv-importe', fmt(item.precioNuevo))));
            row.appendChild(celda('Variación', 'inv-col-num', variacionNode(item.precioAnterior, item.precioNuevo)));
            row.appendChild(cell('inv-col-actions', accion));
            tableBody.appendChild(row);
        });

        dispatchScrollRefresh();
    }

    function celda(rotulo, clase, ...children) {
        const td = cell(clase, ...children);
        td.setAttribute('data-label', rotulo);
        return td;
    }

    async function fetchHistorial(productoId) {
        clearFeedback();
        hide(empty());
        document.getElementById('historial-precio-tabla').hidden = true;
        tbody().innerHTML = '';
        show(loading());

        try {
            const response = await fetch('/Catalogo/HistorialPrecioProductoApi?productoId=' + encodeURIComponent(productoId));
            const data = await response.json();
            hide(loading());

            if (data.success === false) {
                show(empty());
                return;
            }

            nombre().textContent = data.producto?.nombre || '';
            codigo().textContent = data.producto?.codigo || '';
            renderRows(data.items);
        } catch {
            hide(loading());
            show(empty());
            showFeedback('No se pudo cargar el historial en este momento.', 'error');
        }
    }

    async function executeRevertir(eventoId) {
        clearFeedback();

        try {
            const formData = new URLSearchParams();
            formData.append('eventoId', eventoId);
            formData.append('__RequestVerificationToken', getToken());

            const response = await fetch('/Catalogo/RevertirCambioPrecioApi', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: formData.toString()
            });
            const data = await response.json();

            if (data.success) {
                if (productoIdActual) {
                    await fetchHistorial(productoIdActual);
                }
                showFeedback(data.mensaje || 'Cambio de precio revertido correctamente.', 'success');
            } else {
                showFeedback(data.mensaje || 'No se pudo revertir el cambio.', 'error');
            }
        } catch {
            showFeedback('Error de conexión al intentar revertir.', 'error');
        }
    }

    // El historial es un <dialog> modal: la confirmación tiene que abrirse en la capa superior,
    // si no queda detrás del panel y no se puede pulsar.
    async function revertir(eventoId) {
        const item = eventos.get(eventoId);
        const respuesta = await window.InvUI.confirmar({
            titulo: '¿Revertir este ajuste de precios?',
            datos: item
                ? [['Producto', nombre().textContent], ['Aplicado', item.fecha],
                    ['Precio del ajuste', fmt(item.precioNuevo)], ['Vuelve a', fmt(item.precioAnterior)]]
                : [],
            avisos: ['Se revierte el ajuste completo: se restaura el precio anterior de todos los productos incluidos en ese ajuste, no solo de este.'],
            consecuencias: ['Si alguno cambió de precio después, no se revierte ninguno.'],
            etiquetaConfirmar: 'Revertir ajuste completo'
        });
        if (respuesta.ok) await executeRevertir(eventoId);
    }

    var _panel = null;
    function panel() {
        if (!_panel) {
            _panel = window.InvUI.panelFormulario(document.getElementById('modal-historial-precio'), {
                soloLectura: true,
                cierres: '[data-catalogo-modal-close="historial-precio"]',
                alCerrar: limpiar
            });
        }
        return _panel;
    }

    function open(productoId) {
        productoIdActual = productoId;
        clearFeedback();
        nombre().textContent = '';
        codigo().textContent = '';
        updateCount(0);
        panel().abrir();
        dispatchScrollRefresh();
        fetchHistorial(productoId);
    }

    function limpiar() {
        tbody().innerHTML = '';
        clearFeedback();
        productoIdActual = null;
        updateCount(0);
    }

    function close() { panel().cerrar(); }

    document.addEventListener('click', function (event) {
        const revertButton = event.target.closest('[data-catalogo-action="historial-revert"]');
        if (!revertButton) return;

        event.preventDefault();

        const eventoId = parseInt(revertButton.getAttribute('data-catalogo-evento-id'), 10);
        if (!isNaN(eventoId)) {
            revertir(eventoId);
        }
    });

    return { open, close, revertir };
})();

if (typeof CatalogoModule !== 'undefined' && typeof CatalogoModule.registerModalApi === 'function') {
    CatalogoModule.registerModalApi('historial-precio', HistorialPrecioModal);
}
