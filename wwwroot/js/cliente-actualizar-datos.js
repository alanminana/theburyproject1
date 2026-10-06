// Aviso periódico "actualizar datos del cliente" (configuración global en Configuración > Datos de cliente).
// El servidor decide si corresponde (requiereActualizacion); este script solo lo presenta.
(function () {
    'use strict';

    const dialog = document.getElementById('cliente-actualizar-datos-modal');
    if (!dialog || typeof dialog.showModal !== 'function') return;

    const texto = dialog.querySelector('#cad-text');
    const btnEditar = dialog.querySelector('[data-cad-edit]');
    const btnLuego = dialog.querySelector('[data-cad-later]');
    const SNOOZE_PREFIX = 'cliente-actualizar-datos:';

    function pospuesto(id) {
        try { return sessionStorage.getItem(SNOOZE_PREFIX + id) === '1'; } catch { return false; }
    }

    function posponer(id) {
        try { sessionStorage.setItem(SNOOZE_PREFIX + id, '1'); } catch { /* sin storage: se vuelve a preguntar */ }
    }

    function descripcionDias(dias) {
        if (!dias || dias < 1) return 'hace tiempo';
        return dias === 1 ? 'hace 1 día' : 'hace ' + dias + ' días';
    }

    // detalle: { id, nombre, dias, nuevaPestana }
    // "Más tarde" lo silencia durante la sesión del navegador para ese cliente: así buscarlo
    // varias veces en el mismo turno no repite el aviso, pero vuelve a salir al día siguiente.
    function mostrar(detalle) {
        if (!detalle || !detalle.id || pospuesto(detalle.id)) return;
        if (dialog.open) dialog.close();

        const nombre = detalle.nombre ? detalle.nombre : 'Este cliente';
        texto.textContent = nombre + ' no tiene sus datos actualizados (' + descripcionDias(detalle.dias) +
            '). Revisá teléfono, domicilio, ocupación e ingresos con el cliente antes de continuar.';

        btnEditar.href = '/Cliente/Edit/' + encodeURIComponent(detalle.id);
        if (detalle.nuevaPestana) {
            // En Venta se abre aparte para no perder el carrito del wizard.
            btnEditar.target = '_blank';
            btnEditar.rel = 'noopener';
        } else {
            btnEditar.removeAttribute('target');
            btnEditar.removeAttribute('rel');
        }

        btnEditar.onclick = function () {
            posponer(detalle.id);
            dialog.close();
        };
        btnLuego.onclick = function () {
            posponer(detalle.id);
            dialog.close();
        };

        dialog.showModal();
    }

    window.ClienteActualizarDatos = { mostrar: mostrar };

    // Cliente/Details: el servidor deja los datos en data-* del propio diálogo.
    const auto = dialog.dataset.autoId;
    if (auto) {
        mostrar({ id: auto, nombre: dialog.dataset.autoNombre, dias: Number(dialog.dataset.autoDias) || 0, nuevaPestana: false });
    }
})();
