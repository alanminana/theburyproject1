/**
 * configurar-venta-credito.js
 * Lógica de la vista ConfigurarVenta_tw de Crédito. No calcula saldo, recargo, total
 * financiado ni cuotas: eso es exclusivo del servidor (FinancialCalculationService vía
 * /Credito/SimularPlanVenta, server-authoritative con ventaId). Este script solo:
 *  - Cantidad de cuotas → si hay planes configurados, ajusta a la cantidad válida más
 *    cercana (dato, no cálculo: la lista de planes ya viene resuelta del servidor).
 *  - Dispara la simulación en tiempo real del plan (AJAX → /Credito/SimularPlanVenta,
 *    con ventaId) y pinta la respuesta tal cual, incluido el porcentaje aplicado.
 *  - Semáforo de evaluación preliminar.
 *
 * ML6: el método de cálculo y la fuente de configuración (Global/Manual/Cliente/Perfil/
 * Producto) ya no son elegibles acá — el porcentaje sale siempre del plan de cuotas
 * resuelto por el servidor. No hay ningún campo editable ni ningún dato local que decida
 * o precargue una tasa: el input de porcentaje es de solo lectura y se pinta con el valor
 * que devuelve la simulación del servidor.
 *
 * Se usa tanto en la página standalone (ConfigurarVenta_tw, auto-init sobre document)
 * como embebido dentro del wizard de Venta (venta-credito-embebido.js inyecta el
 * fragmento _ConfigurarVentaEmbebida y llama a initConfigurarVentaCredito(root, opts)
 * explícitamente). Por eso todas las búsquedas de nodos están scopeadas a `root` en vez
 * de a `document`: permite reinicializar sobre un fragmento reinyectado sin tocar ni
 * duplicar listeners de una instancia anterior (el fragmento viejo se descarta entero
 * vía innerHTML, así que sus listeners mueren con él).
 */
(function () {
    'use strict';

    function initConfigurarVentaCredito(root, opts) {
        root = root || document;
        opts = opts || {};
        const embebido = Boolean(opts.embedded);

        const creditoModule = window.TheBury && window.TheBury.CreditoModule;

        // ── DOM ────────────────────────────────────────────────────────────
        const $ = (sel) => root.querySelector(sel);

        const hdnMontoVenta    = $('#hdn-monto-venta');
        const hdnMontoFin      = $('#hdn-monto-financiado');
        const hdnVentaId       = $('#hdn-venta-id');
        const txtAnticipo      = $('#txt-anticipo');
        const txtMontoFin      = $('#txt-monto-financiado');

        const txtCuotas        = $('#txt-cuotas');
        // Read-only (ML6): pintado exclusivamente desde la respuesta del servidor, nunca
        // editable ni enviado en el POST (sin asp-for, sin atributo name).
        const txtTasa          = $('#txt-tasa');
        const txtGastos        = $('#txt-gastos');
        const txtFecha         = $('#txt-fecha-primera-cuota');

        const cuotasRangoInfo  = $('#cuotas-rango-info');
        const badgeTasaFuente  = $('#badge-tasa-fuente');
        const btnCancelar      = $('#btn-cancelar-credito');
        const btnGenerarContrato = $('#btn-generar-contrato');
        const btnConfirmar     = $('#btn-confirmar-credito');

        // Plan summary
        const planCuotaDetalle = $('#plan-cuota-detalle');
        const planCuotasLabel  = $('#plan-cuotas-label');
        const planCuotaEstimada = $('#plan-cuota-estimada');
        const planPrecioFinal  = $('#plan-precio-final');
        const planAnticipo     = $('#plan-anticipo');
        const planTasa         = $('#plan-tasa');
        const planInteres      = $('#plan-interes');
        const planCapital      = $('#plan-capital');
        const planGastos       = $('#plan-gastos');
        const planTotal        = $('#plan-total');
        const planFechaContainer = $('#plan-fecha-container');
        const planFechaPago    = $('#plan-fecha-pago');
        const planSimulando    = $('#plan-simulando');
        const planError        = $('#plan-error');
        // CSR-ML6: metadata del plan + tabla completa por cuota (vector autoritativo del servidor).
        const planCuotasSinRecargo = $('#plan-cuotas-sin-recargo');
        const planCuotasTablaBody  = $('#plan-cuotas-tabla-body');
        // CREDITO-VISUAL-02B: texto del <summary> de "Detalle por cuota" (details/summary
        // nativo, colapsado por defecto). El nodo <details> nunca se reemplaza acá — sólo
        // se actualiza este textContent y el tbody de la tabla (ver renderTablaCuotas) — así
        // que si el usuario ya lo abrió, una recalculación en vivo no lo vuelve a cerrar.
        const planCuotasDetalleSummary = $('[data-plan-cuotas-detalle-summary]');

        // Semáforo
        const semaforoPanel    = $('#semaforo-panel');
        const semaforoVacio    = $('#semaforo-vacio');
        const semaforoBadge    = $('#semaforo-badge');
        const semaforoDot      = $('#semaforo-dot');
        const semaforoLabel    = $('#semaforo-label');
        const semaforoTag      = $('#semaforo-tag');
        const semaforoMensaje  = $('#semaforo-mensaje');
        const semaforoAlertas  = $('#semaforo-alertas');

        // ── Config from server ─────────────────────────────────────────────
        let clienteConfig = creditoModule && typeof creditoModule.parseJsonScript === 'function'
            ? creditoModule.parseJsonScript('[data-credito-json="cliente-config"]', {})
            : {};
        clienteConfig = clienteConfig || {};

        // ── Helpers ────────────────────────────────────────────────────────
        const formatCurrency = TheBury.formatCurrency;

        function show(el) { el?.classList.remove('hidden'); }
        function hide(el) { el?.classList.add('hidden'); }

        let simulacionTimer = null;

        // CSR-ML6: números de cuota (1-based) marcados "sin recargo" en la metadata del plan
        // (data.cuotasSinRecargo), la misma autoridad que ya pinta #plan-cuotas-sin-recargo.
        // renderTablaCuotas la lee de acá en vez de recibirla por parámetro para no tocar su
        // firma (contrato congelado, ver CreditoPersonalCuotasTablaUiContractTests).
        let cuotasSinRecargoVigentes = [];

        function formatDateDisplay(dateStr) {
            if (!dateStr) return '-';
            const d = new Date(dateStr + 'T00:00:00');
            if (isNaN(d)) return dateStr;
            const meses = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
                'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];
            return `${String(d.getDate()).padStart(2, '0')} de ${meses[d.getMonth()]}, ${d.getFullYear()}`;
        }

        // ── 1. Saldo a financiar ──────────────────────────────────────────
        // El saldo, el recargo y el total financiado los calcula exclusivamente el
        // servidor (FinancialCalculationService.SimularPlanCredito). Mientras se espera
        // la respuesta de /Credito/SimularPlanVenta se muestra un estado "calculando",
        // nunca un valor derivado en el navegador.
        function marcarSimulacionPendiente() {
            if (txtMontoFin) txtMontoFin.textContent = 'Calculando…';
            programarSimulacion();
        }

        txtAnticipo?.addEventListener('input', marcarSimulacionPendiente);

        // BUG reportado: la numeración de las secciones (1 Método, 2 Perfil, 3 Cuotas,
        // 4 Valores) está hardcodeada en el Razor. La sección 2 ("Perfil de crédito")
        // sólo se muestra cuando el método es "Usar perfil" — en cualquier otro caso
        // (el default, "Global") queda oculta y la numeración visible salta 1→3→4. Se
        // renumeran acá las secciones realmente visibles en cada cambio de método.
        function renumerarSecciones() {
            const visibles = Array.from(root.querySelectorAll('.sec-num'))
                .filter((num) => !num.closest('section')?.classList.contains('hidden'));
            visibles.forEach((num, index) => { num.textContent = String(index + 1); });
        }

        // ── 2. Planes de cuotas ────────────────────────────────────────────
        // ML6: único origen de "cuotas seleccionables". Cuando hay planes activos
        // configurados, las cantidades surgen de esos planes; el porcentaje de cada uno
        // es sólo informativo en el datalist (Razor) — nunca se copia acá a txtTasa, que
        // se pinta exclusivamente con la respuesta de /Credito/SimularPlanVenta.

        function cuotasHabilitadasGlobal() {
            const lista = Array.isArray(clienteConfig.cuotasHabilitadas) ? clienteConfig.cuotasHabilitadas : [];
            const maxProducto = parseInt(clienteConfig.maxCuotasCreditoProducto) || null;
            return lista
                .filter(c => !maxProducto || c.cantidadCuotas <= maxProducto)
                .sort((a, b) => a.cantidadCuotas - b.cantidadCuotas);
        }

        function aplicarCuotasHabilitadas() {
            const planes = cuotasHabilitadasGlobal();
            if (!planes.length) return false;

            const cantidades = planes.map(c => c.cantidadCuotas);
            txtCuotas.min = cantidades[0];
            txtCuotas.max = cantidades[cantidades.length - 1];
            cuotasRangoInfo.textContent = `Cuotas disponibles según planes configurados: ${cantidades.join(', ')}.`;

            ajustarCuotaAPlanHabilitado();
            return true;
        }

        // Ajusta la cantidad tipeada a la más cercana entre las cantidades habilitadas por
        // los planes (dato del servidor, no un cálculo): nunca toca txtTasa.
        function ajustarCuotaAPlanHabilitado() {
            const planes = cuotasHabilitadasGlobal();
            if (!planes.length) return;

            const cantidades = planes.map(c => c.cantidadCuotas);
            const actual = parseInt(txtCuotas.value) || 0;
            if (!cantidades.includes(actual)) {
                const cercana = cantidades.reduce((p, c) => Math.abs(c - actual) < Math.abs(p - actual) ? c : p);
                txtCuotas.value = cercana;
            }
        }

        // ── 3. Simulación de Plan (AJAX) ──────────────────────────────────
        function programarSimulacion() {
            // Ningún cambio de entrada deja el botón de confirmar habilitado con un plan
            // desactualizado: se deshabilita apenas se agenda una nueva simulación.
            deshabilitarConfirmar(true);
            opts.onCambioPlan?.();
            clearTimeout(simulacionTimer);
            simulacionTimer = setTimeout(simularPlan, 400);
        }

        // Token de la simulación en curso: si llega una respuesta tardía de una simulación
        // ya superada por una más nueva, se descarta (evita pintar un plan desactualizado).
        let simulacionToken = 0;

        async function simularPlan() {
            const totalVenta = parseFloat(hdnMontoVenta?.value) || 0;
            const anticipo = parseFloat(txtAnticipo?.value) || 0;
            const cuotas = parseInt(txtCuotas?.value) || 0;
            const gastos = parseFloat(txtGastos?.value) || 0;
            const fecha = txtFecha?.value || '';
            const ventaId = hdnVentaId?.value || '';

            if (totalVenta <= 0 || cuotas <= 0) {
                resetPlanResumen();
                return;
            }

            const token = ++simulacionToken;
            hide(planError);
            show(planSimulando);
            deshabilitarConfirmar(true);

            try {
                const params = new URLSearchParams({
                    totalVenta: totalVenta.toString(),
                    anticipo: anticipo.toString(),
                    cuotas: cuotas.toString(),
                    gastosAdministrativos: gastos.toString(),
                    fechaPrimeraCuota: fecha
                });
                // Server-authoritative: con ventaId el backend ignora totalVenta y resuelve el
                // porcentaje/planes efectivos de los productos de la venta. ML6: no se envía
                // metodoCalculo/fuenteConfiguracion/tasaMensual — el porcentaje sale siempre del
                // plan de cuotas, nunca de un valor local (ver CreditoController.SimularPlanVenta).
                if (ventaId) params.set('ventaId', ventaId);

                const resp = await fetch(`/Credito/SimularPlanVenta?${params}`);
                if (token !== simulacionToken) return; // superada por una simulación más nueva

                if (!resp.ok) {
                    const body = await resp.json().catch(() => null);
                    mostrarErrorPlan(body?.error || 'No se pudo calcular el plan. Revisá los valores ingresados.');
                    return;
                }

                const data = await resp.json();
                if (token !== simulacionToken) return;
                actualizarPlanResumen(data, cuotas);
                actualizarSemaforo(data);
                deshabilitarConfirmar(false);

            } catch {
                if (token !== simulacionToken) return;
                mostrarErrorPlan('No se pudo contactar al servidor para calcular el plan. Reintentá.');
            } finally {
                if (token === simulacionToken) hide(planSimulando);
            }
        }

        function mostrarErrorPlan(mensaje) {
            if (!planError) return;
            planError.textContent = mensaje;
            show(planError);
            planError.focus();
            deshabilitarConfirmar(true);
        }

        function deshabilitarConfirmar(deshabilitado) {
            if (!btnConfirmar) return;
            // Sin planes compatibles el botón ya viene deshabilitado desde el servidor
            // (Model.SinPlanesCompatibles); no reactivarlo aunque la simulación se resuelva.
            if (clienteConfig.sinPlanesCompatibles) return;
            btnConfirmar.disabled = deshabilitado;
        }

        // Formatea el vector de cuotas devuelto por el servidor (ML3) como resumen corto:
        // cuotas iguales → "N cuotas de $X"; con importes distintos (residuo de redondeo Y/O
        // cuotas sin recargo, CSR-ML6) → "Ver detalle por cuota" (la tabla completa, siempre
        // visible, es la fuente visual final — no se resume comparando sólo primera vs última:
        // ver #plan-cuotas-tabla).
        function formatearDetalleCuotas(cuotas, cantidad) {
            if (!Array.isArray(cuotas) || cuotas.length === 0) return `${cantidad} cuotas`;
            if (cuotas.length === 1) return `1 cuota de ${formatCurrency(cuotas[0].total)}`;

            const primera = cuotas[0].total;
            const todasIguales = cuotas.every((c) => Math.abs(Number(c.total) - Number(primera)) < 0.005);

            return todasIguales
                ? `${cuotas.length} cuotas de ${formatCurrency(primera)}`
                : 'Ver detalle por cuota';
        }

        // CSR-ML6: "N°, N°, …" a partir de la metadata del plan (data.cuotasSinRecargo), nunca
        // inferido de interes === 0 (con un plan 0% todas las cuotas tendrían interés 0 sin estar
        // necesariamente marcadas como "sin recargo").
        function formatearCuotasSinRecargo(lista) {
            if (!Array.isArray(lista) || lista.length === 0) return 'Ninguna';
            return lista.slice().sort((a, b) => a - b).join(', ');
        }

        // CREDITO-VISUAL-02B: texto corto del <summary> de "Detalle por cuota" — misma
        // cantidad de cuotas y mismo cuotaEstimada que ya pinta el Resumen del plan
        // (#plan-cuota-estimada), nunca un cálculo propio. Sin simulación válida todavía
        // usa un fallback neutral (nunca cero cuotas con importe cero, que sería engañoso).
        function formatearResumenDetalleCuotas(cantidad, cuotaEstimada) {
            if (!cantidad || cantidad <= 0 || !Number.isFinite(cuotaEstimada)) return 'Detalle por cuota';
            const plural = cantidad === 1 ? 'cuota' : 'cuotas';
            return `${cantidad} ${plural} de ${formatCurrency(cuotaEstimada)} · Ver detalle`;
        }

        // Pinta la tabla completa por cuota tal cual el vector del servidor: no recalcula capital,
        // recargo ni total. VENTA-FORM-PAGO-CREDITO-02A: el badge "Sin recargo" se pintaba antes
        // por el propio interes de la fila (0), lo que contradecía #plan-cuotas-sin-recargo con
        // un plan 0% (todas las cuotas con interés 0 sin estar marcadas como "sin recargo" en la
        // metadata — exactamente el caso que formatearCuotasSinRecargo ya evita, ver su comentario
        // arriba). Ahora usa la misma metadata (cuotasSinRecargoVigentes) como única autoridad.
        function renderTablaCuotas(cuotas) {
            if (!planCuotasTablaBody) return;
            planCuotasTablaBody.innerHTML = '';
            if (!Array.isArray(cuotas)) return;

            cuotas.forEach((c) => {
                const sinRecargo = cuotasSinRecargoVigentes.includes(c.numeroCuota);
                const tr = document.createElement('tr');

                const tdNumero = document.createElement('td');
                tdNumero.textContent = c.numeroCuota;
                tr.appendChild(tdNumero);

                const tdCapital = document.createElement('td');
                tdCapital.className = 'num';
                tdCapital.style.textAlign = 'right';
                tdCapital.textContent = formatCurrency(c.capital);
                tr.appendChild(tdCapital);

                const tdRecargo = document.createElement('td');
                tdRecargo.className = 'num';
                tdRecargo.style.textAlign = 'right';
                tdRecargo.textContent = formatCurrency(c.interes);
                if (sinRecargo) {
                    const badge = document.createElement('span');
                    badge.className = 'chip chip-neutral';
                    badge.style.marginLeft = '.4rem';
                    badge.textContent = 'Sin recargo';
                    tdRecargo.appendChild(badge);
                }
                tr.appendChild(tdRecargo);

                const tdTotal = document.createElement('td');
                tdTotal.className = 'num';
                tdTotal.style.textAlign = 'right';
                tdTotal.style.fontWeight = '600';
                tdTotal.textContent = formatCurrency(c.total);
                tr.appendChild(tdTotal);

                planCuotasTablaBody.appendChild(tr);
            });
        }

        function actualizarPlanResumen(data, cuotas) {
            planCuotasLabel.textContent = cuotas;
            planCuotaEstimada.textContent = formatCurrency(data.cuotaEstimada);
            if (planCuotaDetalle) planCuotaDetalle.textContent = formatearDetalleCuotas(data.cuotas, cuotas);
            planTasa.textContent = `${data.tasaAplicada?.toFixed(2) ?? '0'}%`;
            // ML6: única fuente del porcentaje mostrado en el form — nunca un valor local.
            if (txtTasa) txtTasa.value = (data.tasaAplicada ?? 0).toFixed(2);
            planInteres.textContent = formatCurrency(data.interesTotal);
            if (planPrecioFinal) planPrecioFinal.textContent = formatCurrency(data.totalVenta ?? 0);
            if (planAnticipo) planAnticipo.textContent = formatCurrency(data.anticipo ?? 0);
            planCapital.textContent = formatCurrency(data.montoFinanciado);
            if (txtMontoFin) txtMontoFin.textContent = formatCurrency(data.montoFinanciado);
            if (hdnMontoFin) hdnMontoFin.value = (data.montoFinanciado ?? 0).toFixed(2);
            planGastos.textContent = formatCurrency(data.gastosAdministrativos);
            // Total financiado = saldo a financiar + recargo (totalAPagar). Los gastos
            // administrativos son informativos aparte, nunca se suman a este total.
            planTotal.textContent = formatCurrency(data.totalAPagar);
            // CSR-ML6: metadata del plan + tabla completa, pintadas tal cual las manda el servidor.
            // Misma lista para el resumen y para el badge por fila (renderTablaCuotas): una sola
            // autoridad, nunca pueden contradecirse.
            cuotasSinRecargoVigentes = Array.isArray(data.cuotasSinRecargo) ? data.cuotasSinRecargo : [];
            if (planCuotasSinRecargo) planCuotasSinRecargo.textContent = formatearCuotasSinRecargo(data.cuotasSinRecargo);
            renderTablaCuotas(data.cuotas);
            if (planCuotasDetalleSummary) planCuotasDetalleSummary.textContent = formatearResumenDetalleCuotas(cuotas, data.cuotaEstimada);

            if (data.fechaPrimerPago) {
                planFechaPago.textContent = formatDateDisplay(data.fechaPrimerPago);
                show(planFechaContainer);
            } else {
                hide(planFechaContainer);
            }

            // ML6.1: el badge junto al campo de porcentaje pinta data.fuentePorcentaje tal cual la
            // manda el servidor. Contrato congelado: el servidor siempre responde "Plan" (el plan
            // de cuotas es la única fuente del %) — nunca "Producto"/"Cliente"/"Manual"/"Global".
            if (badgeTasaFuente && data.fuentePorcentaje) {
                badgeTasaFuente.textContent = data.fuentePorcentaje;
                show(badgeTasaFuente);
            }
        }

        function resetPlanResumen() {
            planCuotasLabel.textContent = '0';
            planCuotaEstimada.textContent = '$ 0,00';
            if (planCuotaDetalle) planCuotaDetalle.textContent = 'Cuota';
            planTasa.textContent = '0%';
            if (txtTasa) txtTasa.value = '';
            planInteres.textContent = '$ 0,00';
            if (planPrecioFinal) planPrecioFinal.textContent = '$ 0,00';
            if (planAnticipo) planAnticipo.textContent = '$ 0,00';
            planCapital.textContent = '$ 0,00';
            if (txtMontoFin) txtMontoFin.textContent = '$ 0,00';
            planGastos.textContent = '$ 0,00';
            planTotal.textContent = '$ 0,00';
            cuotasSinRecargoVigentes = [];
            if (planCuotasSinRecargo) planCuotasSinRecargo.textContent = 'Ninguna';
            if (planCuotasTablaBody) planCuotasTablaBody.innerHTML = '';
            if (planCuotasDetalleSummary) planCuotasDetalleSummary.textContent = 'Detalle por cuota';
            hide(planFechaContainer);
            hide(planSimulando);
            hide(planError);
            deshabilitarConfirmar(true);
        }

        // ── 4. Semáforo de Evaluación ─────────────────────────────────────
        function actualizarSemaforo(data) {
            const estado = data.semaforoEstado;
            const mensaje = data.semaforoMensaje;

            if (!estado || estado === 'sinDatos') {
                hide(semaforoPanel);
                show(semaforoVacio);
                return;
            }

            hide(semaforoVacio);
            show(semaforoPanel);

            // VENTA-FORM-RIESGO-01: el semáforo es asesorio, no una decisión de
            // aprobación/rechazo (esa elegibilidad ya la resuelven los bloqueantes y el
            // cupo mostrados más arriba). "tag" ya no usa lenguaje de aprobación/rechazo
            // ("Aprobado"/"A revisar"/"Rechazado") para no contradecir visualmente que
            // "Confirmar crédito" sigue habilitado en Riesgo Alto: ver deshabilitarConfirmar,
            // que nunca depende de `estado`.
            //
            // CREDITO-VISUAL-02A: el contenedor (badgeClass) ya no lleva fondo/borde rojo/
            // amber/verde dominante — esa superficie competía visualmente con bloqueantes
            // reales (ej. "Cupo insuficiente — operación bloqueada"), haciendo que "Riesgo
            // alto" pareciera un rechazo. El color del nivel se conserva en el dot y en el
            // texto (labelClass/tagClass), que siguen distinguiendo bajo/moderado/alto; el
            // contenedor usa el mismo neutro en los tres niveles (mismo tono que el fallback
            // de "Otros motivos", ver mostrarMotivos en venta-create.js).
            const BADGE_NEUTRO = 'bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700';
            const estados = {
                verde: {
                    dotClass: 'bg-green-500',
                    badgeClass: BADGE_NEUTRO,
                    labelClass: 'text-green-700 dark:text-green-400',
                    tagClass: 'text-green-600 dark:text-green-500',
                    label: 'Riesgo bajo',
                    tag: 'No bloquea'
                },
                amarillo: {
                    dotClass: 'bg-yellow-500',
                    badgeClass: BADGE_NEUTRO,
                    labelClass: 'text-yellow-700 dark:text-yellow-400',
                    tagClass: 'text-yellow-600 dark:text-yellow-500',
                    label: 'Riesgo moderado',
                    tag: 'No bloquea'
                },
                rojo: {
                    dotClass: 'bg-red-500',
                    badgeClass: BADGE_NEUTRO,
                    labelClass: 'text-red-700 dark:text-red-400',
                    tagClass: 'text-red-600 dark:text-red-500',
                    label: 'Riesgo alto',
                    tag: 'No bloquea'
                }
            };

            const config = estados[estado] || estados.amarillo;

            semaforoDot.className = `size-4 rounded-full animate-pulse ${config.dotClass}`;
            semaforoBadge.className = `flex items-center justify-between p-3 rounded-lg ${config.badgeClass}`;
            semaforoLabel.className = `font-bold ${config.labelClass}`;
            semaforoLabel.textContent = config.label;
            semaforoTag.className = `text-[10px] uppercase font-black ${config.tagClass}`;
            semaforoTag.textContent = config.tag;
            semaforoMensaje.textContent = `"${mensaje}"`;

            // Alertas
            semaforoAlertas.innerHTML = '';
            if (data.mostrarMsgIngreso) {
                semaforoAlertas.innerHTML += `
                <div class="flex items-start gap-2 text-yellow-600 dark:text-yellow-500 text-sm">
                    <span class="material-symbols-outlined text-[18px]">warning</span>
                    <span>Verificar ingresos declarados del cliente.</span>
                </div>`;
            }
            if (data.mostrarMsgAntiguedad) {
                semaforoAlertas.innerHTML += `
                <div class="flex items-start gap-2 text-red-500 dark:text-red-400 text-sm">
                    <span class="material-symbols-outlined text-[18px]">error</span>
                    <span>Antigüedad laboral insuficiente.</span>
                </div>`;
            }
        }

        // ── 5. Event Listeners ────────────────────────────────────────────
        txtCuotas?.addEventListener('input', programarSimulacion);
        txtCuotas?.addEventListener('change', function () {
            ajustarCuotaAPlanHabilitado();
            programarSimulacion();
        });
        txtGastos?.addEventListener('input', programarSimulacion);
        txtFecha?.addEventListener('change', programarSimulacion);

        if (btnCancelar) {
            btnCancelar.addEventListener('click', function () {
                const cancelUrl = btnCancelar.getAttribute('data-credito-cancel-url') || '/Credito';
                const message = '¿Desea cancelar la configuración del crédito? Se perderán los cambios no guardados.';

                if (window.TheBury && typeof window.TheBury.confirmAction === 'function') {
                    window.TheBury.confirmAction(message, function () {
                        window.location.href = cancelUrl;
                    });
                } else {
                    window.location.href = cancelUrl;
                }
            });
        }

        // En la página standalone, generar el contrato es un submit de página completa
        // (formtarget=_blank + reload). Embebido en el wizard, venta-credito-embebido.js
        // ata su propio listener a este mismo botón (fetch + recarga del fragmento), así
        // que acá no hay que hacer nada más.
        if (btnGenerarContrato && !embebido) {
            btnGenerarContrato.addEventListener('click', function () {
                window.setTimeout(function () {
                    window.location.reload();
                }, 2500);
            });
        }

        // ── 6. Primera cuota: cobro inmediato (F2, Micro-lote 6) ──────────
        // El cobro de la 1ª cuota al confirmar solo se ofrece cuando la primera cuota
        // vence HOY. La autoridad final es el servidor; esto es únicamente UX.
        const pcAplica       = $('[data-primera-cuota-aplica]');
        const pcNoAplica     = $('[data-primera-cuota-no-aplica]');
        const pcCobrar       = $('[data-primera-cuota-cobrar]');
        const pcMedioBox     = $('[data-primera-cuota-medio-container]');

        function fechaEsHoy(valor) {
            if (!valor) return false;
            const hoy = new Date();
            const hoyIso = `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}-${String(hoy.getDate()).padStart(2, '0')}`;
            return valor === hoyIso;
        }

        function actualizarMedioVisibilidad() {
            if (pcCobrar && pcCobrar.checked) {
                show(pcMedioBox);
            } else {
                hide(pcMedioBox);
            }
        }

        function actualizarPrimeraCuota() {
            const venceHoy = fechaEsHoy(txtFecha?.value);
            if (venceHoy) {
                show(pcAplica);
                hide(pcNoAplica);
            } else {
                hide(pcAplica);
                show(pcNoAplica);
                // Fuera de "vence hoy" no puede quedar marcada la opción (el server también lo rechaza).
                if (pcCobrar) pcCobrar.checked = false;
            }
            actualizarMedioVisibilidad();
        }

        pcCobrar?.addEventListener('change', actualizarMedioVisibilidad);
        txtFecha?.addEventListener('change', actualizarPrimeraCuota);
        txtFecha?.addEventListener('input', actualizarPrimeraCuota);

        if (creditoModule && typeof creditoModule.initSharedUi === 'function') {
            creditoModule.initSharedUi();
        }

        // ── Init ──────────────────────────────────────────────────────────
        // Set default date if empty
        if (txtFecha && !txtFecha.value) {
            const d = new Date();
            d.setMonth(d.getMonth() + 1);
            txtFecha.value = d.toISOString().split('T')[0];
        }

        // Si no hay cuotas definidas aún, usar 12 como valor inicial para mostrar la simulación
        if (txtCuotas && (parseInt(txtCuotas.value) || 0) <= 0) {
            txtCuotas.value = 12;
        }

        // Initial state
        // ML6: no hay método/perfil que aplicar; sólo ajustar cuotas al plan habilitado
        // (dato del servidor) y numerar las secciones visibles (WIP: renumerarSecciones).
        aplicarCuotasHabilitadas();
        renumerarSecciones();
        marcarSimulacionPendiente();
        actualizarPrimeraCuota();

        return {
            root,
            getBtnConfirmar: () => btnConfirmar,
            getBtnGenerarContrato: () => btnGenerarContrato
        };
    }

    window.TheBury = window.TheBury || {};
    window.TheBury.initConfigurarVentaCredito = initConfigurarVentaCredito;

    // Auto-init para la página standalone (ConfigurarVenta_tw). El fragmento embebido
    // en el wizard de Venta se inicializa explícitamente desde venta-credito-embebido.js
    // después de inyectar el HTML, así que no debe auto-ejecutarse acá.
    if (document.querySelector('[data-credito-config]') && !document.querySelector('[data-credito-config-embedded]')) {
        initConfigurarVentaCredito(document, { embedded: false });
    }
})();
