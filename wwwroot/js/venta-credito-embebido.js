/**
 * venta-credito-embebido.js
 * Orquesta el configurador canónico de Crédito Personal embebido dentro del paso
 * "Crédito" del wizard de Venta (Crear y Editar). No calcula nada: delega todo en
 * /Credito/ConfigurarVenta (embedded=true) y /ContratoVentaCredito/Generar, los mismos
 * endpoints server-authoritative que usa la página standalone ConfigurarVenta_tw (ver
 * configurar-venta-credito.js, reutilizado tal cual sobre el fragmento inyectado).
 *
 * Responsabilidades:
 *  - En Crear, persistir el borrador (venta + crédito pendiente) la primera vez que hace
 *    falta, vía /Venta/CreateAjax, y desde ahí mutar el wizard a modo Editar (mismo
 *    borrador) para que el submit final no cree una segunda venta.
 *  - Cargar el fragmento embebido (GET ConfigurarVenta?embedded=true) e inicializarlo.
 *  - "Confirmar crédito" y "Generar contrato" por fetch (sin submits de página completa).
 *  - Emitir venta:credito-validado {configurado:true} sólo tras una configuración
 *    server-side válida, y volver a false ante cualquier cambio relevante — reutilizando
 *    el mismo evento que venta-create.js ya dispara al invalidar cliente/productos/pago.
 */
(function () {
    'use strict';

    const root = document.getElementById('venta-create-page') || document.getElementById('venta-edit-page');
    if (!root) return;

    const ventaForm = document.getElementById('venta-form');
    const contenedor = document.getElementById('credito-embebido-contenedor');
    const cargandoEl = document.getElementById('credito-embebido-cargando');
    const errorBox = document.getElementById('credito-embebido-error');
    const pagoSelect = document.getElementById('select-tipo-pago');
    const stepBtnCredito = document.getElementById('step-btn-credito');

    if (!ventaForm || !contenedor) return;

    function requiereCredito() {
        return pagoSelect?.value === pagoSelect?.dataset.creditoPersonalValue;
    }

    function show(el) { el?.classList.remove('hidden'); }
    function hide(el) { el?.classList.add('hidden'); }

    let ventaId = window.ventaInicial?.id || null;
    let creditoId = window.ventaInicial?.creditoId || null;
    let configurado = window.ventaInicial?.creditoConfigurado === true;
    let cargado = false;   // el fragmento inyectado refleja el estado vigente del servidor
    let enCurso = false;   // fetch de carga en curso (evita solicitudes superpuestas)
    // Aunque ventaId/creditoId ya existan (se creó el borrador la primera vez que se
    // entró al paso Crédito), un cambio posterior de cliente/productos/pago sólo vive en
    // el JS del wizard hasta el submit final: el total que lee el servidor sigue siendo
    // el viejo. Sin volver a sincronizar el borrador antes de recargar el fragmento, el
    // configurador mostraría un monto/plan desactualizado aunque "configurado" ya esté
    // correctamente en false.
    let debeResincronizarBorrador = false;

    function dispatchConfigurado(valor) {
        configurado = valor;
        document.dispatchEvent(new CustomEvent('venta:credito-validado', {
            detail: { configurado: valor, origenEmbebido: true }
        }));
    }

    // El mismo evento que venta-create.js dispara al invalidar elegibilidad ante cambios
    // de cliente/productos/pago (invalidarVerificacionCrediticia) también nos avisa a
    // nosotros: el fragmento cacheado quedó desactualizado (total/productos distintos) y
    // hay que recargarlo (con el borrador resincronizado) la próxima vez que el operador
    // entre al paso Crédito. Se ignoran los eventos que originamos nosotros mismos (editar
    // anticipo/cuotas no invalida el fragmento en sí, sólo el "configurado").
    document.addEventListener('venta:credito-validado', (event) => {
        if (event.detail && event.detail.configurado === false && !event.detail.origenEmbebido) {
            console.debug('[venta-credito-embebido] Fragmento invalidado por evento externo', event.detail);
            cargado = false;
            if (ventaId && creditoId) debeResincronizarBorrador = true;

            // BUG reportado: confirmar la excepción documental (u otro evento externo que
            // invalide el fragmento) dispara este mismo evento SIN que el operador cambie
            // de pestaña. El único disparador de recarga hasta ahora era "llegar" al paso
            // Crédito (click de tab o venta:wizard-paso-activo) — si ya estábamos parados
            // en él, marcar cargado=false no alcanza: el panel queda vacío hasta que el
            // operador se va a otra pestaña y vuelve. Si el paso Crédito ya está activo,
            // recargar ahora mismo en vez de esperar una llegada que no va a ocurrir.
            if (stepBtnCredito?.getAttribute('aria-selected') === 'true' && !stepBtnCredito.disabled) {
                cargarConfigurador();
            }
        }
    });

    function mostrarError(mensaje) {
        if (!errorBox) return;
        errorBox.textContent = mensaje;
        show(errorBox);
    }

    function ocultarError() {
        hide(errorBox);
    }

    // ── Persistir el borrador la primera vez que hace falta ────────────────
    // Dos casos posibles al llegar acá sin creditoId:
    //  - Crear: la venta ni siquiera existe todavía → /Venta/CreateAjax.
    //  - Editar: la venta ya existe (venía de antes, o recién se le cambió el medio de
    //    pago a Crédito Personal en este mismo edit) pero todavía no tiene crédito
    //    pendiente → /Venta/EditAjax/{id}, NUNCA CreateAjax (crearía una segunda venta).
    async function asegurarBorradorPersistido() {
        if (ventaId && creditoId && !debeResincronizarBorrador) return true;

        const formData = new FormData(ventaForm);
        const params = new URLSearchParams();
        for (const [key, value] of formData.entries()) {
            if (value instanceof File) continue;
            params.append(key, value);
        }

        const esActualizacion = Boolean(ventaId);
        const url = esActualizacion ? `/Venta/EditAjax/${ventaId}` : '/Venta/CreateAjax';

        let resp;
        try {
            resp = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: params.toString()
            });
        } catch {
            mostrarError('No se pudo contactar al servidor para preparar la venta.');
            return false;
        }

        const data = await resp.json().catch(() => null);
        if (!data || !data.success || !data.creditoId) {
            const detalle = data?.errors ? Object.values(data.errors).flat().join(' ') : data?.message;
            mostrarError(detalle || 'No se pudo preparar la venta para configurar el crédito.');
            return false;
        }

        ventaId = data.ventaId;
        creditoId = data.creditoId;

        if (!esActualizacion) {
            // Recién se creó: a partir de acá el wizard sigue como si estuviera editando
            // este borrador. El submit final ("Confirmar operación") debe actualizar la
            // misma venta, nunca crear una segunda (ver comentario de CreateAjax). El id
            // va explícito en la URL: a diferencia de un Edit real, acá no hay route value
            // ambiente ("/Venta/Create" no trae id) del que asp-action pudiera inferirlo.
            ventaForm.setAttribute('action', `/Venta/Edit/${ventaId}`);
            let hdnId = ventaForm.querySelector('input[name="Id"]');
            if (!hdnId) {
                hdnId = document.createElement('input');
                hdnId.type = 'hidden';
                hdnId.name = 'Id';
                ventaForm.appendChild(hdnId);
            }
            hdnId.value = String(ventaId);
        }

        const hdnRowVersion = ventaForm.querySelector('input[name="RowVersion"]');
        if (hdnRowVersion && data.rowVersion) {
            console.debug('[venta-credito-embebido] RowVersion resincronizado desde ' + url, { anterior: hdnRowVersion.value, nuevo: data.rowVersion });
            hdnRowVersion.value = data.rowVersion;
        }

        debeResincronizarBorrador = false;
        return true;
    }

    // ── Cargar / recargar el fragmento embebido ────────────────────────────
    async function cargarConfigurador() {
        if (enCurso || !requiereCredito()) return;
        enCurso = true;
        ocultarError();
        show(cargandoEl);

        try {
            const ok = await asegurarBorradorPersistido();
            if (!ok) return;

            const params = new URLSearchParams({ id: String(creditoId), ventaId: String(ventaId), embedded: 'true' });
            const resp = await fetch(`/Credito/ConfigurarVenta?${params}`);

            if (!resp.ok) {
                const data = await resp.json().catch(() => null);
                mostrarError(data?.message || 'No se pudo cargar la configuración del crédito.');
                cargado = false;
                return;
            }

            const html = await resp.text();
            contenedor.innerHTML = html;

            // El fragmento vive dentro de #venta-form (para compartir layout con el paso
            // Crédito), pero sus campos (CreditoId, ClienteId, Anticipo, el propio
            // __RequestVerificationToken, ...) NO deben viajar en el submit real del
            // wizard: varios nombres colisionan con propiedades de VentaViewModel
            // (ej. "ClienteId") y duplicar el antiforgery token rompe esa validación.
            // El atributo form="" apuntando a un id inexistente desasocia cada campo de
            // cualquier <form> ancestro sin afectar en nada su lectura/escritura por JS
            // (nosotros los leemos y posteamos por fetch, nunca por submit nativo).
            contenedor.querySelectorAll('input, select, textarea').forEach((el) => {
                el.setAttribute('form', 'credito-embebido-sin-form');
            });

            // Confirmar el crédito y generar el contrato modifican la venta server-side
            // (bumpean su RowVersion). Sin esto, el RowVersion sembrado al cargar la
            // página queda desactualizado y el submit final del wizard rebota con "La
            // venta fue modificada por otro usuario" — un 409 de concurrencia legítimo
            // por diseño, pero falso en este caso: el "otro usuario" es este mismo
            // configurador.
            const rowVersionActual = contenedor.querySelector('#hdn-venta-row-version')?.value;
            const hdnRowVersion = ventaForm.querySelector('input[name="RowVersion"]');
            if (rowVersionActual && hdnRowVersion) {
                console.debug('[venta-credito-embebido] RowVersion resincronizado desde fragmento ConfigurarVenta', { anterior: hdnRowVersion.value, nuevo: rowVersionActual });
                hdnRowVersion.value = rowVersionActual;
            } else if (!rowVersionActual) {
                console.warn('[venta-credito-embebido] El fragmento de ConfigurarVenta no trajo #hdn-venta-row-version: el RowVersion del wizard puede quedar desactualizado.');
            }

            window.TheBury.initConfigurarVentaCredito(contenedor, {
                embedded: true,
                onCambioPlan: () => { if (configurado) dispatchConfigurado(false); }
            });
            wireBotonesEmbebidos();
            cargado = true;

            // Sincronizar "configurado" con lo que el servidor efectivamente refleja en
            // este fragmento recién renderizado, no con lo que asumíamos antes de pedirlo.
            const yaConfigurado = contenedor.querySelector('[data-credito-embebido-ya-configurado]') != null;
            if (yaConfigurado !== configurado) dispatchConfigurado(yaConfigurado);
        } catch {
            mostrarError('No se pudo contactar al servidor para cargar la configuración del crédito.');
            cargado = false;
        } finally {
            enCurso = false;
            hide(cargandoEl);
        }
    }

    function wireBotonesEmbebidos() {
        const btnConfirmar = contenedor.querySelector('[data-credito-embebido-confirmar]');
        const feedback = contenedor.querySelector('#credito-embebido-confirmar-feedback');

        btnConfirmar?.addEventListener('click', async () => {
            btnConfirmar.disabled = true;
            if (feedback) {
                feedback.textContent = 'Guardando configuración…';
                show(feedback);
            }
            ocultarError();

            const ok = await confirmarCreditoEmbebido();
            if (!ok) {
                btnConfirmar.disabled = false;
                if (feedback) hide(feedback);
                return;
            }

            // Recarga fresca: el servidor decide si el crédito quedó Configurado y si el
            // contrato ya puede generarse. No se asume nada del lado del navegador.
            await cargarConfigurador();
        });

        const btnContrato = contenedor.querySelector('[data-credito-embebido-generar-contrato]');
        btnContrato?.addEventListener('click', async () => {
            btnContrato.disabled = true;
            ocultarError();
            await generarContratoEmbebido();
            await cargarConfigurador();
        });
    }

    async function confirmarCreditoEmbebido() {
        const tokenInput = contenedor.querySelector('input[name="__RequestVerificationToken"]');
        const campos = [
            'CreditoId', 'VentaId', 'ClienteId', 'Monto', 'MontoFinanciado', 'FuenteConfiguracion',
            'MetodoCalculo', 'PerfilCreditoSeleccionadoId', 'CantidadCuotas', 'Anticipo', 'TasaMensual',
            'GastosAdministrativos', 'FechaPrimeraCuota', 'MedioPagoPrimeraCuota'
        ];
        const params = new URLSearchParams();
        campos.forEach((nombre) => {
            const el = contenedor.querySelector(`[name="${nombre}"]`);
            if (el) params.append(nombre, el.value ?? '');
        });
        const chkCobrar = contenedor.querySelector('[data-primera-cuota-cobrar]');
        params.append('CobrarPrimeraCuota', chkCobrar?.checked ? 'true' : 'false');
        if (tokenInput) params.append('__RequestVerificationToken', tokenInput.value);
        params.append('embedded', 'true');

        try {
            const resp = await fetch('/Credito/ConfigurarVenta', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: params.toString()
            });
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data?.success) {
                mostrarError(data?.message || (data?.errors && Object.values(data.errors).flat().join(' ')) || 'No se pudo confirmar el crédito. Revisá los valores.');
                return false;
            }
            return true;
        } catch {
            mostrarError('No se pudo contactar al servidor para confirmar el crédito.');
            return false;
        }
    }

    async function generarContratoEmbebido() {
        const tokenInput = contenedor.querySelector('input[name="__RequestVerificationToken"]');
        const params = new URLSearchParams({ ventaId: String(ventaId) });
        if (tokenInput) params.append('__RequestVerificationToken', tokenInput.value);

        try {
            const resp = await fetch('/ContratoVentaCredito/Generar', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Requested-With': 'XMLHttpRequest' },
                body: params.toString()
            });
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data?.success) {
                mostrarError(data?.message || 'No se pudo generar el contrato.');
                return false;
            }
            return true;
        } catch {
            mostrarError('No se pudo contactar al servidor para generar el contrato.');
            return false;
        }
    }

    stepBtnCredito?.addEventListener('click', () => {
        if (stepBtnCredito.disabled) return;
        if (!cargado) cargarConfigurador();
    });

    // Cubre llegar al paso "Crédito" sin clickear la pestaña (p.ej. el botón primario
    // "Continuar a crédito" desde Pago, que navega vía avanzar()/setActiveStep()): sin
    // este listener el fragmento nunca se pedía y el panel quedaba vacío.
    document.addEventListener('venta:wizard-paso-activo', (event) => {
        if (event.detail?.step !== 'credito') return;
        if (stepBtnCredito?.disabled) return;
        if (!cargado) cargarConfigurador();
    });
})();
