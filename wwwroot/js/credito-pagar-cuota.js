/* credito-pagar-cuota.js — Interacciones para Credito/PagarCuota */

document.addEventListener('DOMContentLoaded', function () {
    var creditoModule = window.TheBury && window.TheBury.CreditoModule;
    var root = document.querySelector('[data-credito-pago]');
    if (!root) return;

    var cuotas = creditoModule && typeof creditoModule.parseJsonScript === 'function'
        ? creditoModule.parseJsonScript('[data-credito-json="cuotas"]', {})
        : {};

    var form = root.querySelector('[data-credito-pago-form]');
    var selectCuota = root.querySelector('[data-credito-cuota-select]');
    var inputMonto = root.querySelector('[data-credito-monto-input]');
    var montoError = root.querySelector('[data-credito-monto-error]');

    var hiddenFields = {
        cuotaId: document.getElementById('hdn-cuota-id'),
        numeroCuota: document.getElementById('hdn-numero-cuota'),
        montoCuota: document.getElementById('hdn-monto-cuota'),
        punitorio: document.getElementById('hdn-punitorio'),
        total: document.getElementById('hdn-total'),
        fechaVencimiento: document.getElementById('hdn-fecha-vto'),
        estaVencida: document.getElementById('hdn-esta-vencida'),
        diasAtraso: document.getElementById('hdn-dias-atraso')
    };

    var ui = {
        numero: Array.from(root.querySelectorAll('[data-credito-cuota-field="numero"], [data-credito-cuota-field="numero-card"]')),
        estado: root.querySelector('[data-credito-cuota-field="estado"]'),
        estadoCard: root.querySelector('[data-credito-cuota-field="estado-card"]'),
        estadoSummary: root.querySelector('[data-credito-cuota-field="estado-summary"]'),
        estadoDetail: root.querySelector('[data-credito-cuota-field="estado-detail"]'),
        estadoBadge: root.querySelector('[data-credito-cuota-field="estado-badge"]'),
        estadoBadgeCard: root.querySelector('[data-credito-cuota-field="estado-badge-card"]'),
        vencimiento: root.querySelector('[data-credito-cuota-field="vencimiento"]'),
        vencimientoMetric: root.querySelector('[data-credito-cuota-field="vencimiento-metric"]'),
        montoCuota: root.querySelector('[data-credito-cuota-field="monto-cuota"]'),
        punitorio: root.querySelector('[data-credito-cuota-field="punitorio"]'),
        total: root.querySelector('[data-credito-cuota-field="total"]'),
        totalMetric: root.querySelector('[data-credito-cuota-field="total-metric"]'),
        montoPreview: root.querySelector('[data-credito-cuota-field="monto-preview"]'),
        totalPreview: root.querySelector('[data-credito-cuota-field="total-preview"]')
    };

    function formatCurrency(value) {
        var number = Number(value || 0);
        return '$ ' + number.toFixed(2).replace('.', ',').replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    }

    function parseDecimal(value) {
        if (!value) return NaN;

        var cleaned = String(value).trim();
        var hasComma = cleaned.indexOf(',') >= 0;
        var hasPoint = cleaned.indexOf('.') >= 0;

        if (hasComma && hasPoint) {
            return parseFloat(cleaned.replace(/\./g, '').replace(',', '.'));
        }

        if (hasComma) {
            return parseFloat(cleaned.replace(',', '.'));
        }

        return parseFloat(cleaned);
    }

    function getCurrentMax() {
        return parseFloat(inputMonto.getAttribute('data-max') || '0');
    }

    function showMontoError(message) {
        if (!montoError) return;
        montoError.textContent = message;
        montoError.classList.remove('hidden');
        inputMonto.classList.add('border-red-500', 'focus:ring-red-500/50');
    }

    function hideMontoError() {
        if (!montoError) return;
        montoError.classList.add('hidden');
        inputMonto.classList.remove('border-red-500', 'focus:ring-red-500/50');
    }

    function validateMonto(silent) {
        var value = parseDecimal(inputMonto.value);
        var max = getCurrentMax();

        if (isNaN(value) || value <= 0) {
            if (!silent) {
                showMontoError('Ingresá un monto válido mayor a 0.');
            }
            return false;
        }

        if (value > max + 0.001) {
            if (!silent) {
                showMontoError('El monto no puede superar el saldo de la cuota ($ ' + max.toFixed(2).replace('.', ',') + ').');
            }
            return false;
        }

        hideMontoError();
        return true;
    }

    function applyBadgeClasses(element, isOverdue) {
        if (!element) return;

        element.className = isOverdue
            ? 'inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-bold uppercase tracking-wide bg-red-500/10 text-red-600 border border-red-500/20'
            : 'inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-bold uppercase tracking-wide bg-emerald-500/10 text-emerald-600 border border-emerald-500/20';
    }

    function applyCuotaData(cuotaId) {
        var cuota = cuotas[cuotaId];
        if (!cuota) return;

        ui.numero.forEach(function (element) {
            element.textContent = cuota.numeroCuota;
        });

        if (ui.vencimiento) {
            ui.vencimiento.textContent = cuota.vencimiento;
        }

        if (ui.vencimientoMetric) {
            var parts = String(cuota.vencimiento || '').split('/');
            ui.vencimientoMetric.textContent = parts.length >= 2 ? parts[0] + '/' + parts[1] : cuota.vencimiento;
        }

        if (ui.montoCuota) {
            ui.montoCuota.textContent = formatCurrency(cuota.montoCuota);
        }

        if (ui.punitorio) {
            ui.punitorio.textContent = formatCurrency(cuota.punitorio);
        }

        if (ui.total) {
            ui.total.textContent = formatCurrency(cuota.saldo);
        }

        if (ui.totalMetric) {
            ui.totalMetric.textContent = formatCurrency(cuota.saldo);
        }

        if (ui.totalPreview) {
            ui.totalPreview.textContent = formatCurrency(cuota.saldo);
        }

        var statusText = cuota.estaVencida ? 'Vencida · ' + cuota.diasAtraso + ' días' : 'Vigente';
        var statusSummary = cuota.estaVencida ? 'Vencida' : 'Vigente';
        var statusDetail = cuota.estaVencida ? cuota.diasAtraso + ' días de atraso' : 'sin mora activa';

        if (ui.estado) {
            ui.estado.textContent = statusText;
        }

        if (ui.estadoCard) {
            ui.estadoCard.textContent = statusText;
        }

        if (ui.estadoSummary) {
            ui.estadoSummary.textContent = statusSummary;
            ui.estadoSummary.className = cuota.estaVencida
                ? 'mt-2 text-2xl font-black text-red-600 dark:text-red-400'
                : 'mt-2 text-2xl font-black text-emerald-600 dark:text-emerald-400';
        }

        if (ui.estadoDetail) {
            ui.estadoDetail.textContent = statusDetail;
        }

        applyBadgeClasses(ui.estadoBadge, cuota.estaVencida);
        applyBadgeClasses(ui.estadoBadgeCard, cuota.estaVencida);

        hiddenFields.cuotaId.value = cuotaId;
        hiddenFields.numeroCuota.value = cuota.numeroCuota;
        hiddenFields.montoCuota.value = cuota.montoCuota;
        hiddenFields.punitorio.value = cuota.punitorio;
        hiddenFields.total.value = cuota.saldo;
        hiddenFields.fechaVencimiento.value = cuota.fechaVencimientoIso || hiddenFields.fechaVencimiento.value;
        hiddenFields.estaVencida.value = cuota.estaVencida;
        hiddenFields.diasAtraso.value = cuota.diasAtraso;

        inputMonto.setAttribute('data-max', Number(cuota.saldo || 0).toFixed(4));
        inputMonto.value = Number(cuota.saldo || 0).toFixed(2).replace('.', ',');

        if (ui.montoPreview) {
            ui.montoPreview.textContent = formatCurrency(cuota.saldo);
        }

        hideMontoError();
    }

    function normalizeInitialMonto() {
        if (!inputMonto.value) return;

        if (inputMonto.value.indexOf(',') >= 0) return;

        var value = parseFloat(inputMonto.value);
        if (!isNaN(value)) {
            inputMonto.value = value.toFixed(2).replace('.', ',');
        }

        if (ui.montoPreview && !isNaN(value)) {
            ui.montoPreview.textContent = formatCurrency(value);
        }
    }

    selectCuota.addEventListener('change', function () {
        applyCuotaData(selectCuota.value);
    });

    inputMonto.addEventListener('blur', function () {
        validateMonto(false);
    });

    inputMonto.addEventListener('input', function () {
        var parsed = parseDecimal(inputMonto.value);

        if (ui.montoPreview && !isNaN(parsed)) {
            ui.montoPreview.textContent = formatCurrency(parsed);
        }

        if (montoError && !montoError.classList.contains('hidden')) {
            validateMonto(true);
        }
    });

    form.addEventListener('submit', function (event) {
        var value = parseDecimal(inputMonto.value);

        if (isNaN(value)) {
            event.preventDefault();
            showMontoError('Ingresá un monto válido (ej: 1,37 o 1.37).');
            return;
        }

        if (!validateMonto(false)) {
            event.preventDefault();
            return;
        }

        inputMonto.value = value.toFixed(2).replace('.', ',');
    });

    if (creditoModule && typeof creditoModule.initSharedUi === 'function') {
        creditoModule.initSharedUi();
    }
    normalizeInitialMonto();
});
