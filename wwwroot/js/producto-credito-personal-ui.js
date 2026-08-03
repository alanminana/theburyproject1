/**
 * producto-credito-personal-ui.js
 * Lógica compartida de la sección "Crédito personal" de Producto (Crear y Editar):
 * render de las cards de plan por cantidad de cuotas, wiring del selector tri-estado
 * (Hereda global / Planes propios / No disponible) y preview server-side del recargo.
 * No calcula nada: solo pinta lo que devuelve el backend.
 */
(function () {
    'use strict';

    function formatMoney(n) {
        return Number(n).toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    // El vector de cuotas viene resuelto del servidor (mismo cálculo canónico que persiste
    // el guardado real): solo se lee: nunca se recalcula ni se reconstruye por multiplicación
    // (eso podría no cerrar contra totalFinanciado cuando el residuo cae en la última cuota).
    function textoVector(data) {
        var vector = data.cuotas;
        var regular = vector[0].total;
        var ultima = vector[vector.length - 1].total;
        var n = vector.length;
        return (regular === ultima)
            ? (n + ' cuota' + (n !== 1 ? 's' : '') + ' de $ ' + formatMoney(regular))
            : ((n - 1) + ' cuotas de $ ' + formatMoney(regular) + ' · última $ ' + formatMoney(ultima));
    }

    function renderCards(cont, cuotas, fieldPrefix, previewUrl) {
        if (!cont) return;
        cont.innerHTML = '';
        cuotas = cuotas || [];
        if (!cuotas.length) {
            var vacio = document.createElement('p');
            vacio.className = 'text-xs text-slate-500 sm:col-span-2 lg:col-span-3';
            vacio.textContent = 'No hay cantidades de cuota configuradas globalmente todavía.';
            cont.appendChild(vacio);
            return;
        }

        cuotas.forEach(function (c, i) {
            var n = Number(c.cantidadCuotas) || 0;
            // null = heredar el recargo global -> input vacío (placeholder "Hereda global").
            // 0 = sin recargo explícito, válido y distinto de "hereda".
            var recargo = (c.tasaMensual != null) ? c.tasaMensual : '';
            var orden = (c.orden != null) ? c.orden : n;
            var card = document.createElement('div');
            card.className = 'rounded-xl border border-slate-800 bg-slate-950/40 p-3 space-y-2';
            card.setAttribute('data-cp-card', '');
            card.innerHTML =
                '<input type="hidden" name="' + fieldPrefix + '[' + i + '].Id" value="' + (c.id || 0) + '" />' +
                '<input type="hidden" name="' + fieldPrefix + '[' + i + '].CantidadCuotas" value="' + n + '" />' +
                '<input type="hidden" name="' + fieldPrefix + '[' + i + '].Orden" value="' + orden + '" />' +
                '<div class="flex items-center justify-between gap-2">' +
                    '<span class="text-sm font-semibold text-white">' + n + ' cuota' + (n !== 1 ? 's' : '') + '</span>' +
                    '<label class="inline-flex items-center gap-1.5 text-[11px] font-semibold text-slate-300">' +
                        '<input type="checkbox" data-cp-activo name="' + fieldPrefix + '[' + i + '].Activo" value="true" class="rounded border-slate-600 bg-slate-900 text-primary focus:ring-primary"' + (c.activo ? ' checked' : '') + ' />' +
                        '<input type="hidden" name="' + fieldPrefix + '[' + i + '].Activo" value="false" />' +
                        'Activo' +
                    '</label>' +
                '</div>' +
                '<div class="space-y-1">' +
                    '<label class="text-xs text-slate-400">Recargo total (%)</label>' +
                    '<input data-cp-tasa name="' + fieldPrefix + '[' + i + '].TasaMensual" type="number" step="0.01" min="0" max="100" value="' + recargo + '" placeholder="Hereda global" class="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 font-mono text-white focus:ring-2 focus:ring-primary outline-none" />' +
                '</div>' +
                '<p data-cp-preview class="text-[11px] text-slate-400"></p>';
            cont.appendChild(card);
            wirePreview(card, n, previewUrl);
        });
    }

    function wirePreview(card, cantidadCuotas, previewUrl) {
        if (!previewUrl) return;
        var tasaInput = card.querySelector('[data-cp-tasa]');
        var previewEl = card.querySelector('[data-cp-preview]');
        if (!tasaInput || !previewEl) return;
        var timer = null;

        function actualizar() {
            if (tasaInput.value === '') {
                previewEl.textContent = 'Hereda el recargo global para esta cantidad.';
                return;
            }
            var pct = Number(tasaInput.value);
            if (isNaN(pct) || pct < 0) { previewEl.textContent = ''; return; }
            var url = previewUrl + '?cuotas=' + encodeURIComponent(cantidadCuotas) + '&porcentajeRecargoTotal=' + encodeURIComponent(pct);
            fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    previewEl.textContent = data
                        ? ('Sobre $ 100.000: recargo $ ' + formatMoney(data.recargoTotal) + ' · total $ ' + formatMoney(data.totalFinanciado) + ' · ' + textoVector(data))
                        : '';
                })
                .catch(function () { previewEl.textContent = ''; });
        }

        tasaInput.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(actualizar, 350);
        });
        actualizar();
    }

    // ── Selector tri-estado (Hereda global / Planes propios / No disponible) ──
    // El radio es la señal explícita que el backend valida contra AdmiteCreditoPersonal y
    // los planes activos (ver ProductoCreditoPersonalConfigService.Validar). Al salir del modo
    // "Planes propios" se desmarcan todas las cuotas activas para que lo que se envía sea
    // honesto con lo que se ve: nada queda activo "oculto" en un modo que no lo admite.
    function aplicarModo(modo, els) {
        var esPropia = modo === 'ConfiguracionPropia';
        var esBloqueado = modo === 'NoDisponible';

        if (els.admiteHidden) els.admiteHidden.value = esBloqueado ? 'false' : 'true';
        if (els.cardsWrap) els.cardsWrap.classList.toggle('hidden', !esPropia);
        if (els.maxCuotasWrap) els.maxCuotasWrap.classList.toggle('hidden', esBloqueado);

        if (!esPropia && els.cardsCont) {
            els.cardsCont.querySelectorAll('[data-cp-activo]').forEach(function (chk) {
                chk.checked = false;
            });
        }
    }

    function wireModo(radios, els) {
        if (!radios || !radios.length) return;
        radios.forEach(function (r) {
            r.addEventListener('change', function () {
                if (r.checked) aplicarModo(r.value, els);
            });
        });
        var checked = Array.prototype.filter.call(radios, function (r) { return r.checked; })[0];
        aplicarModo(checked ? checked.value : 'HeredaGlobal', els);
    }

    window.ProductoCreditoPersonalUI = {
        renderCards: renderCards,
        wireModo: wireModo,
        aplicarModo: aplicarModo,
        wirePreview: wirePreview,
        formatMoney: formatMoney
    };
})();
