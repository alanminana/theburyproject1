// Modal "Completar datos del cliente" (ver Views/Shared/_CompletarDatosClienteModal.cshtml).
// El servidor decide qué falta y valida; este script solo lo presenta y envía el formulario.
//   window.ClienteDatosContractuales.abrir(clienteId) → Promise<boolean> (true si se guardó)
//   [data-completar-datos-cliente="<id>"] (delegado) → abre el modal y dispara el evento
//   'cliente:datos-contractuales-guardados' { clienteId } en document.
(function () {
    'use strict';

    const dialog = document.getElementById('cliente-datos-contractuales-modal');
    if (!dialog || typeof dialog.showModal !== 'function') return;

    const form = dialog.querySelector('#cdc-form');
    const errores = dialog.querySelector('#cdc-errors');
    const btnGuardar = dialog.querySelector('[data-cdc-save]');
    const btnCancelar = dialog.querySelector('[data-cdc-cancel]');
    const campos = ['Nombre', 'Apellido', 'TipoDocumento', 'NumeroDocumento', 'Domicilio', 'Localidad', 'Telefono'];

    let resolver = null;
    let guardado = false;
    let clienteIdActual = null;

    function campo(nombre) { return form.elements[nombre]; }

    function mostrarErrores(lista) {
        errores.replaceChildren();
        if (!lista || lista.length === 0) { errores.hidden = true; return; }
        const ul = document.createElement('ul');
        lista.forEach((texto) => {
            const li = document.createElement('li');
            li.textContent = texto;
            ul.appendChild(li);
        });
        errores.appendChild(ul);
        errores.hidden = false;
    }

    function marcarFaltantes(faltantes) {
        campos.forEach((nombre) => {
            const falta = faltantes.includes(nombre);
            const input = campo(nombre);
            input.setAttribute('aria-invalid', falta ? 'true' : 'false');
            const aviso = input.closest('.cdc-field').querySelector('.cdc-miss');
            if (aviso) aviso.hidden = !falta;
        });
    }

    async function abrir(clienteId) {
        if (!clienteId) return false;
        clienteIdActual = clienteId;
        guardado = false;
        mostrarErrores([]);

        let data;
        try {
            const resp = await fetch('/ClienteDatosContractuales/Obtener?clienteId=' + encodeURIComponent(clienteId),
                { headers: { 'Accept': 'application/json' } });
            data = await resp.json().catch(() => null);
            if (!resp.ok || !data?.success) throw new Error(data?.message || 'No se pudieron cargar los datos del cliente.');
        } catch (e) {
            mostrarErrores([e.message || 'No se pudieron cargar los datos del cliente.']);
            data = null;
        }

        form.elements.ClienteId.value = String(clienteId);
        if (data) {
            campos.forEach((nombre) => { campo(nombre).value = data.datos[nombre.charAt(0).toLowerCase() + nombre.slice(1)] ?? ''; });
            marcarFaltantes(data.faltantes || []);

            // El documento es la identidad del cliente: sólo se completa si falta (el servidor también lo respeta).
            const documentoCargado = !(data.faltantes || []).some((n) => n === 'TipoDocumento' || n === 'NumeroDocumento');
            campo('TipoDocumento').disabled = documentoCargado;
            campo('NumeroDocumento').readOnly = documentoCargado;
        }
        btnGuardar.disabled = !data;

        if (dialog.open) dialog.close();
        dialog.showModal();

        const primero = data && campos.map(campo).find((el) => el.getAttribute('aria-invalid') === 'true');
        (primero || btnCancelar).focus();

        return new Promise((resolve) => { resolver = resolve; });
    }

    form.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (btnGuardar.disabled) return;
        btnGuardar.disabled = true;
        mostrarErrores([]);

        try {
            const resp = await fetch('/ClienteDatosContractuales/Guardar', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'Accept': 'application/json' },
                body: new URLSearchParams(new FormData(form)).toString()
            });
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data?.success) {
                mostrarErrores(data?.errors || ['No se pudieron guardar los datos del cliente.']);
                btnGuardar.disabled = false;
                return;
            }
            guardado = true;
            dialog.close();
            document.dispatchEvent(new CustomEvent('cliente:datos-contractuales-guardados', { detail: { clienteId: clienteIdActual } }));
        } catch {
            mostrarErrores(['No se pudo contactar al servidor para guardar los datos.']);
            btnGuardar.disabled = false;
        }
    });

    btnCancelar.addEventListener('click', () => dialog.close());

    dialog.addEventListener('close', () => {
        if (resolver) { resolver(guardado); resolver = null; }
    });

    document.addEventListener('click', (event) => {
        const disparador = event.target.closest('[data-completar-datos-cliente]');
        if (!disparador) return;
        event.preventDefault();
        abrir(disparador.getAttribute('data-completar-datos-cliente'));
    });

    window.ClienteDatosContractuales = { abrir: abrir };
})();
