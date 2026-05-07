/**
 * configurar-venta-credito.js
 * Lógica de la vista ConfigurarVenta_tw de Crédito:
 *  - Cálculo de monto financiado (monto - anticipo)
 *  - Cambio de método de cálculo → precarga de valores
 *  - Selección de perfil de crédito → aplica tasa/gastos/rango cuotas
 *  - Simulación en tiempo real del plan (AJAX → /Credito/SimularPlanVenta)
 *  - Semáforo de evaluación preliminar
 */
(function () {
    'use strict';

    const creditoModule = window.TheBury && window.TheBury.CreditoModule;

    // ── DOM ────────────────────────────────────────────────────────────
    const $ = (sel) => document.querySelector(sel);

    const hdnMontoVenta    = $('#hdn-monto-venta');
    const hdnMontoFin      = $('#hdn-monto-financiado');
    const hdnFuente        = $('#hdn-fuente-configuracion');
    const txtAnticipo      = $('#txt-anticipo');
    const txtMontoFin      = $('#txt-monto-financiado');

    const selectMetodo     = $('#select-metodo-calculo');
    const selectPerfil     = $('#select-perfil-credito');
    const panelPerfil      = $('#panel-perfil-credito');
    const txtCuotas        = $('#txt-cuotas');
    const txtTasa          = $('#txt-tasa');
    const txtGastos        = $('#txt-gastos');
    const txtFecha         = $('#txt-fecha-primera-cuota');

    const metodoInfo       = $('#metodo-info');
    const metodoInfoTexto  = $('#metodo-info-texto');
    const cuotasRangoInfo  = $('#cuotas-rango-info');
    const badgeTasaFuente  = $('#badge-tasa-fuente');
    const heroMontoFin     = $('#hero-monto-financiado');
    const heroCuotas       = $('#hero-cuotas');
    const heroMetodo       = $('#hero-metodo');
    const btnCancelar      = $('#btn-cancelar-credito');
    const btnGenerarContrato = $('#btn-generar-contrato');

    // Plan summary
    const planCuotasLabel  = $('#plan-cuotas-label');
    const planCuotaEstimada = $('#plan-cuota-estimada');
    const planTasa         = $('#plan-tasa');
    const planInteres      = $('#plan-interes');
    const planCapital      = $('#plan-capital');
    const planGastos       = $('#plan-gastos');
    const planTotal        = $('#plan-total');
    const planFechaContainer = $('#plan-fecha-container');
    const planFechaPago    = $('#plan-fecha-pago');

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

    const METODO = { AutomaticoPorCliente: '0', UsarPerfil: '1', UsarCliente: '2', Global: '3', Manual: '4' };

    // ── Helpers ────────────────────────────────────────────────────────
    const formatCurrency = TheBury.formatCurrency;

    function show(el) { el?.classList.remove('hidden'); }
    function hide(el) { el?.classList.add('hidden'); }

    let simulacionTimer = null;

    function formatDateDisplay(dateStr) {
        if (!dateStr) return '-';
        const d = new Date(dateStr + 'T00:00:00');
        if (isNaN(d)) return dateStr;
        const meses = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
            'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];
        return `${String(d.getDate()).padStart(2, '0')} de ${meses[d.getMonth()]}, ${d.getFullYear()}`;
    }

    // ── 1. Monto Financiado ────────────────────────────────────────────
    function recalcularMontoFinanciado() {
        const monto = parseFloat(hdnMontoVenta?.value) || 0;
        const anticipo = parseFloat(txtAnticipo?.value) || 0;
        const financiado = Math.max(0, monto - anticipo);

        if (hdnMontoFin) hdnMontoFin.value = financiado.toFixed(2);
        if (txtMontoFin) txtMontoFin.textContent = formatCurrency(financiado);
        if (heroMontoFin) heroMontoFin.textContent = formatCurrency(financiado);

        programarSimulacion();
    }

    txtAnticipo?.addEventListener('input', recalcularMontoFinanciado);

    // ── 2. Método de Cálculo ───────────────────────────────────────────
    const metodoDescripciones = {
        [METODO.AutomaticoPorCliente]: 'Usa la mejor configuración disponible del cliente, perfil preferido o sistema.',
        [METODO.UsarPerfil]: 'Aplica los valores de un perfil de crédito predefinido.',
        [METODO.UsarCliente]: 'Usa la configuración personalizada del cliente.',
        [METODO.Global]: 'Utiliza los valores globales del sistema.',
        [METODO.Manual]: 'Permite edición libre de todos los campos.'
    };

    function onMetodoChange() {
        const val = selectMetodo?.value;

        // Info text
        if (val && metodoDescripciones[val]) {
            metodoInfoTexto.textContent = metodoDescripciones[val];
            show(metodoInfo);
        } else {
            hide(metodoInfo);
        }

        // Show/hide perfil selector
        if (val === METODO.UsarPerfil) {
            show(panelPerfil);
        } else {
            hide(panelPerfil);
        }

        // Apply values based on method
        aplicarValoresMetodo(val);

        // Field editability
        const esManual = val === METODO.Manual;
        const esReadonly = !esManual && val !== '';
        txtTasa.readOnly = esReadonly;
        txtGastos.readOnly = esReadonly;

        if (esReadonly) {
            txtTasa.classList.add('bg-slate-100', 'dark:bg-slate-800/50', 'cursor-not-allowed');
            txtTasa.classList.remove('bg-slate-50', 'dark:bg-slate-800');
            txtGastos.classList.add('bg-slate-100', 'dark:bg-slate-800/50', 'cursor-not-allowed');
            txtGastos.classList.remove('bg-slate-50', 'dark:bg-slate-800');
        } else {
            txtTasa.classList.remove('bg-slate-100', 'dark:bg-slate-800/50', 'cursor-not-allowed');
            txtTasa.classList.add('bg-slate-50', 'dark:bg-slate-800');
            txtGastos.classList.remove('bg-slate-100', 'dark:bg-slate-800/50', 'cursor-not-allowed');
            txtGastos.classList.add('bg-slate-50', 'dark:bg-slate-800');
        }

        // Update fuente hidden
        if (val === METODO.Manual) {
            if (hdnFuente) hdnFuente.value = '2'; // Manual
        } else if (val === METODO.UsarCliente) {
            if (hdnFuente) hdnFuente.value = '1'; // PorCliente
        } else {
            if (hdnFuente) hdnFuente.value = '0'; // Global
        }

        // Badge
        actualizarBadgeTasa(val);
        actualizarHeroMetodo(val);

        programarSimulacion();
    }

    function aplicarValoresMetodo(metodo) {
        switch (metodo) {
            case METODO.AutomaticoPorCliente:
                // Priority: client custom > preferred profile > global
                if (clienteConfig.tieneConfiguracionCliente) {
                    txtTasa.value = clienteConfig.tasaPersonalizada ?? clienteConfig.perfilTasa ?? clienteConfig.tasaGlobal ?? '';
                    txtGastos.value = clienteConfig.gastosPersonalizados ?? clienteConfig.perfilGastos ?? clienteConfig.gastosGlobales ?? 0;
                    actualizarRangoCuotas(
                        clienteConfig.perfilMinCuotas ?? 1,
                        clienteConfig.cuotasMaximas ?? clienteConfig.perfilMaxCuotas ?? 24
                    );
                } else if (clienteConfig.tienePerfilPreferido) {
                    txtTasa.value = clienteConfig.perfilTasa ?? clienteConfig.tasaGlobal ?? '';
                    txtGastos.value = clienteConfig.perfilGastos ?? clienteConfig.gastosGlobales ?? 0;
                    actualizarRangoCuotas(clienteConfig.perfilMinCuotas ?? 1, clienteConfig.perfilMaxCuotas ?? 24);
                } else {
                    txtTasa.value = clienteConfig.tasaGlobal ?? '';
                    txtGastos.value = clienteConfig.gastosGlobales ?? 0;
                    actualizarRangoCuotas(1, 24);
                }
                break;

            case METODO.UsarPerfil:
                onPerfilChange();
                break;

            case METODO.UsarCliente:
                txtTasa.value = clienteConfig.tasaPersonalizada ?? clienteConfig.tasaGlobal ?? '';
                txtGastos.value = clienteConfig.gastosPersonalizados ?? 0;
                actualizarRangoCuotas(1, clienteConfig.cuotasMaximas ?? 24);
                break;

            case METODO.Global:
                txtTasa.value = clienteConfig.tasaGlobal ?? '';
                txtGastos.value = clienteConfig.gastosGlobales ?? 0;
                actualizarRangoCuotas(1, 24);
                break;

            case METODO.Manual:
                // Don't change values, let user edit freely
                actualizarRangoCuotas(1, 120);
                break;
        }
    }

    function actualizarBadgeTasa(metodo) {
        if (!metodo || metodo === '') {
            hide(badgeTasaFuente);
            return;
        }
        const labels = {
            [METODO.AutomaticoPorCliente]: 'Auto',
            [METODO.UsarPerfil]: 'Perfil',
            [METODO.UsarCliente]: 'Cliente',
            [METODO.Global]: 'Global',
            [METODO.Manual]: 'Manual'
        };
        badgeTasaFuente.textContent = labels[metodo] || '';
        show(badgeTasaFuente);
    }

    function actualizarHeroMetodo(metodo) {
        if (!heroMetodo) return;

        const labels = {
            [METODO.AutomaticoPorCliente]: 'Automático',
            [METODO.UsarPerfil]: 'Perfil',
            [METODO.UsarCliente]: 'Cliente',
            [METODO.Global]: 'Global',
            [METODO.Manual]: 'Manual'
        };

        heroMetodo.textContent = labels[metodo] || '—';
    }

    function actualizarRangoCuotas(min, max) {
        const maxProducto = parseInt(clienteConfig.maxCuotasCreditoProducto) || null;
        const maxEfectivo = maxProducto ? Math.min(max, maxProducto) : max;

        txtCuotas.min = min;
        txtCuotas.max = maxEfectivo;
        cuotasRangoInfo.textContent = `Rango permitido: ${min} a ${maxEfectivo} cuotas.`;
        if (maxProducto) {
            cuotasRangoInfo.textContent += ` ${clienteConfig.restriccionCreditoProductoDescripcion || `Límite por producto: hasta ${maxProducto} cuotas.`}`;
        }

        // Clamp current value
        const current = parseInt(txtCuotas.value) || 0;
        if (current < min) txtCuotas.value = min;
        if (current > maxEfectivo) txtCuotas.value = maxEfectivo;
        if (heroCuotas) heroCuotas.textContent = txtCuotas.value || '0';
    }

    selectMetodo?.addEventListener('change', onMetodoChange);

    // ── 3. Perfil de Crédito ───────────────────────────────────────────
    function onPerfilChange() {
        const opt = selectPerfil?.selectedOptions[0];
        if (!opt || !opt.value) return;

        const tasa = opt.dataset.tasa;
        const gastos = opt.dataset.gastos;
        const minCuotas = parseInt(opt.dataset.minCuotas) || 1;
        const maxCuotas = parseInt(opt.dataset.maxCuotas) || 24;

        if (tasa) txtTasa.value = tasa;
        if (gastos) txtGastos.value = gastos;
        actualizarRangoCuotas(minCuotas, maxCuotas);

        programarSimulacion();
    }

    selectPerfil?.addEventListener('change', function () {
        onPerfilChange();
        programarSimulacion();
    });

    // ── 4. Simulación de Plan (AJAX) ──────────────────────────────────
    function programarSimulacion() {
        clearTimeout(simulacionTimer);
        simulacionTimer = setTimeout(simularPlan, 400);
    }

    async function simularPlan() {
        const montoFinanciado = parseFloat(hdnMontoFin?.value) || 0;
        const totalVenta = parseFloat(hdnMontoVenta?.value) || 0;
        const anticipo = parseFloat(txtAnticipo?.value) || 0;
        const cuotas = parseInt(txtCuotas?.value) || 0;
        const tasa = parseFloat(txtTasa?.value);
        const gastos = parseFloat(txtGastos?.value) || 0;
        const fecha = txtFecha?.value || '';

        if (totalVenta <= 0 || cuotas <= 0) {
            resetPlanResumen();
            return;
        }

        try {
            const params = new URLSearchParams({
                totalVenta: totalVenta.toString(),
                anticipo: anticipo.toString(),
                cuotas: cuotas.toString(),
                gastosAdministrativos: gastos.toString(),
                fechaPrimeraCuota: fecha
            });
            if (!isNaN(tasa) && tasa > 0) {
                params.set('tasaMensual', tasa.toString());
            }

            const resp = await fetch(`/Credito/SimularPlanVenta?${params}`);
            if (!resp.ok) {
                await resp.json().catch(() => null);
                return;
            }

            const data = await resp.json();
            actualizarPlanResumen(data, cuotas);
            actualizarSemaforo(data);

        } catch {
            // simulación falló silenciosamente
        }
    }

    function actualizarPlanResumen(data, cuotas) {
        planCuotasLabel.textContent = cuotas;
        planCuotaEstimada.textContent = formatCurrency(data.cuotaEstimada);
        planTasa.textContent = `${data.tasaAplicada?.toFixed(2) ?? '0'}% Mensual`;
        planInteres.textContent = formatCurrency(data.interesTotal);
        planCapital.textContent = formatCurrency(data.montoFinanciado);
        planGastos.textContent = formatCurrency(data.gastosAdministrativos);
        planTotal.textContent = formatCurrency(data.totalPlan);

        if (data.fechaPrimerPago) {
            planFechaPago.textContent = formatDateDisplay(data.fechaPrimerPago);
            show(planFechaContainer);
        } else {
            hide(planFechaContainer);
        }
    }

    function resetPlanResumen() {
        planCuotasLabel.textContent = '0';
        planCuotaEstimada.textContent = '$ 0,00';
        planTasa.textContent = '0% Mensual';
        planInteres.textContent = '$ 0,00';
        planCapital.textContent = '$ 0,00';
        planGastos.textContent = '$ 0,00';
        planTotal.textContent = '$ 0,00';
        hide(planFechaContainer);
    }

    // ── 5. Semáforo de Evaluación ─────────────────────────────────────
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

        const estados = {
            verde: {
                dotClass: 'bg-green-500',
                badgeClass: 'bg-green-500/10 border border-green-500/20',
                labelClass: 'text-green-700 dark:text-green-400',
                tagClass: 'text-green-600 dark:text-green-500',
                label: 'Riesgo Bajo',
                tag: 'Approved'
            },
            amarillo: {
                dotClass: 'bg-yellow-500',
                badgeClass: 'bg-yellow-500/10 border border-yellow-500/20',
                labelClass: 'text-yellow-700 dark:text-yellow-400',
                tagClass: 'text-yellow-600 dark:text-yellow-500',
                label: 'Riesgo Moderado',
                tag: 'Caution'
            },
            rojo: {
                dotClass: 'bg-red-500',
                badgeClass: 'bg-red-500/10 border border-red-500/20',
                labelClass: 'text-red-700 dark:text-red-400',
                tagClass: 'text-red-600 dark:text-red-500',
                label: 'Riesgo Alto',
                tag: 'Rejected'
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

    // ── 6. Event Listeners ────────────────────────────────────────────
    txtCuotas?.addEventListener('input', programarSimulacion);
    txtCuotas?.addEventListener('input', function () {
        if (heroCuotas) heroCuotas.textContent = txtCuotas.value || '0';
    });
    txtTasa?.addEventListener('input', programarSimulacion);
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

    if (btnGenerarContrato) {
        btnGenerarContrato.addEventListener('click', function () {
            window.setTimeout(function () {
                window.location.reload();
            }, 2500);
        });
    }

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
    if (heroCuotas) {
        heroCuotas.textContent = txtCuotas?.value || '0';
    }

    // Initial state
    onMetodoChange();
    recalcularMontoFinanciado();

})();
