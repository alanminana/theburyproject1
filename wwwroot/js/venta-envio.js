// ENVIO-ML4: paso "Envío" del wizard de Venta (Create/Edit). Muestra/oculta la
// sección de datos de entrega según el checkbox "Tiene envío" y la precarga con el
// domicilio del cliente seleccionado (editable después). No calcula ni toca totales:
// el costo de envío es puramente informativo.
(function () {
    'use strict';

    const root = document.getElementById('venta-create-page') || document.getElementById('venta-edit-page');
    if (!root) return;

    const $ = (sel) => root.querySelector(sel);

    const chkTieneEnvio = $('#chk-tiene-envio');
    const bloqueDatos = $('#envio-datos');
    const btnUsarCliente = $('#btn-envio-usar-cliente');
    if (!chkTieneEnvio || !bloqueDatos) return;

    const campos = {
        destinatario: $('#envio-destinatario'),
        telefono: $('#envio-telefono'),
        domicilio: $('#envio-domicilio'),
        localidad: $('#envio-localidad'),
        provincia: $('#envio-provincia'),
        codigoPostal: $('#envio-cp'),
        transportista: $('#envio-transportista'),
        costoEnvio: $('#envio-costo'),
        fechaProgramada: $('#envio-fecha'),
        observaciones: $('#envio-observaciones')
    };

    // Último cliente elegido en vivo (evento disparado por venta-create.js al
    // seleccionar de #dropdown-clientes). Si todavía no hubo selección en esta carga
    // de página (p. ej. Edit, donde el cliente ya venía seteado desde el servidor),
    // se usa window.ventaInicial.cliente como fallback.
    let ultimoClienteSeleccionado = null;
    document.addEventListener('venta:cliente-seleccionado', function (e) {
        ultimoClienteSeleccionado = e.detail || null;
    });

    function campoVacio(input) {
        return !input || !input.value || !input.value.trim();
    }

    function todosLosCamposVacios() {
        return campoVacio(campos.destinatario) && campoVacio(campos.domicilio);
    }

    function precargarDesdeCliente(cliente, forzar) {
        if (!cliente) return;

        if (forzar || campoVacio(campos.destinatario)) {
            const nombreCompleto = [cliente.apellido, cliente.nombre].filter(Boolean).join(', ')
                || cliente.nombre || cliente.display || '';
            if (campos.destinatario) campos.destinatario.value = nombreCompleto;
        }
        if ((forzar || campoVacio(campos.telefono)) && cliente.telefono) {
            campos.telefono.value = cliente.telefono;
        }
        if ((forzar || campoVacio(campos.domicilio)) && cliente.domicilio) {
            campos.domicilio.value = cliente.domicilio;
        }
        if ((forzar || campoVacio(campos.localidad)) && cliente.localidad) {
            campos.localidad.value = cliente.localidad;
        }
        if ((forzar || campoVacio(campos.provincia)) && cliente.provincia) {
            campos.provincia.value = cliente.provincia;
        }
        if ((forzar || campoVacio(campos.codigoPostal)) && cliente.codigoPostal) {
            campos.codigoPostal.value = cliente.codigoPostal;
        }
    }

    function clienteDisponible() {
        if (ultimoClienteSeleccionado) return ultimoClienteSeleccionado;
        if (window.ventaInicial && window.ventaInicial.cliente && window.ventaInicial.cliente.id) {
            return window.ventaInicial.cliente;
        }
        return null;
    }

    function actualizarVisibilidad() {
        const activo = !!chkTieneEnvio.checked;
        bloqueDatos.classList.toggle('hidden', !activo);
        bloqueDatos.hidden = !activo;

        if (activo && todosLosCamposVacios()) {
            precargarDesdeCliente(clienteDisponible(), false);
        }

        document.dispatchEvent(new CustomEvent('venta:envio-toggle', { detail: { activo } }));
    }

    chkTieneEnvio.addEventListener('change', actualizarVisibilidad);

    btnUsarCliente?.addEventListener('click', function () {
        precargarDesdeCliente(clienteDisponible(), true);
    });

    // Hidrata los datos ya persistidos (Edit, o Create con envío ya adjuntado desde
    // una cotización convertida) — sólo valores, el checked/hidden ya vino del server.
    if (window.ventaInicial && window.ventaInicial.envio) {
        const e = window.ventaInicial.envio;
        if (campos.destinatario) campos.destinatario.value = e.destinatario || '';
        if (campos.telefono) campos.telefono.value = e.telefono || '';
        if (campos.domicilio) campos.domicilio.value = e.domicilio || '';
        if (campos.localidad) campos.localidad.value = e.localidad || '';
        if (campos.provincia) campos.provincia.value = e.provincia || '';
        if (campos.codigoPostal) campos.codigoPostal.value = e.codigoPostal || '';
        if (campos.transportista) campos.transportista.value = e.transportista || '';
        if (campos.costoEnvio && e.costoEnvio != null) campos.costoEnvio.value = e.costoEnvio;
        if (campos.fechaProgramada) campos.fechaProgramada.value = e.fechaProgramada || '';
        if (campos.observaciones) campos.observaciones.value = e.observaciones || '';
    }

    actualizarVisibilidad();
})();
