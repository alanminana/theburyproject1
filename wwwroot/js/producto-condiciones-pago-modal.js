(function () {
    'use strict';

    var modal = null;
    var detailModal = null;
    var currentData = null;
    var stagingData = {};
    var currentDetailTipoPago = -1;
    var lastFocusedElement = null;

    var tiposPago = [
        { value: 0, label: 'Efectivo',         mode: 'direct', icon: 'payments',        color: 'emerald' },
        { value: 1, label: 'Transferencia',    mode: 'direct', icon: 'sync_alt',        color: 'blue'    },
        { value: 2, label: 'Tarjeta Debito',   mode: 'card',   icon: 'credit_card',     color: 'violet'  },
        { value: 3, label: 'Tarjeta Credito',  mode: 'card',   icon: 'credit_score',    color: 'purple'  },
        { value: 4, label: 'Cheque',           mode: 'direct', icon: 'receipt_long',    color: 'orange'  },
        { value: 5, label: 'Credito Personal', mode: 'credit', icon: 'person',          color: 'teal'    },
        { value: 6, label: 'Mercado Pago',     mode: 'card',   icon: 'store',           color: 'cyan'    },
        { value: 7, label: 'Cuenta Corriente', mode: 'direct', icon: 'account_balance', color: 'indigo'  }
    ];

    var colorMap = {
        emerald: { bg: 'bg-emerald-500/15', text: 'text-emerald-400', border: 'border-emerald-500/20' },
        blue:    { bg: 'bg-blue-500/15',    text: 'text-blue-400',    border: 'border-blue-500/20'    },
        violet:  { bg: 'bg-violet-500/15',  text: 'text-violet-400',  border: 'border-violet-500/20'  },
        purple:  { bg: 'bg-purple-500/15',  text: 'text-purple-400',  border: 'border-purple-500/20'  },
        orange:  { bg: 'bg-orange-500/15',  text: 'text-orange-400',  border: 'border-orange-500/20'  },
        teal:    { bg: 'bg-teal-500/15',    text: 'text-teal-400',    border: 'border-teal-500/20'    },
        cyan:    { bg: 'bg-cyan-500/15',    text: 'text-cyan-400',    border: 'border-cyan-500/20'    },
        indigo:  { bg: 'bg-indigo-500/15',  text: 'text-indigo-400',  border: 'border-indigo-500/20'  }
    };

    function el(id) { return document.getElementById(id); }

    function esc(value) {
        var d = document.createElement('div');
        d.textContent = value == null ? '' : String(value);
        return d.innerHTML;
    }

    function getToken() {
        return document.querySelector('#form-condiciones-pago-producto input[name="__RequestVerificationToken"]')?.value || '';
    }

    function showBox(id, message) {
        var box = el(id);
        if (!box) return;
        box.textContent = message || '';
        box.classList.toggle('hidden', !message);
    }

    function clearMessages() {
        showBox('condiciones-pago-validation', '');
        showBox('condiciones-pago-success', '');
    }

    function setLoading(loading) {
        var loadingEl = el('condiciones-pago-loading');
        var form = el('form-condiciones-pago-producto');
        if (loadingEl) loadingEl.classList.toggle('hidden', !loading);
        if (form) form.classList.toggle('hidden', loading);
    }

    function setSubmitLoading(loading) {
        var btn = el('btn-guardar-condiciones-pago');
        if (!btn) return;
        btn.disabled = loading;
        btn.innerHTML = loading
            ? '<span class="material-symbols-outlined text-[18px] animate-spin">progress_activity</span> Guardando...'
            : '<span class="material-symbols-outlined text-[18px]">save</span> Guardar condiciones';
    }

    function extractErrors(err) {
        if (Array.isArray(err?.errors)) return err.errors;
        if (err?.errors && typeof err.errors === 'object') {
            return Object.keys(err.errors).flatMap(function (k) { return err.errors[k]; });
        }
        return ['Error al procesar la solicitud.'];
    }

    function toInt(value) {
        if (value == null || value === '') return null;
        var parsed = parseInt(value, 10);
        return isNaN(parsed) ? null : parsed;
    }

    function toDecimal(value) {
        if (value == null || value === '') return null;
        var parsed = parseFloat(String(value).replace(',', '.'));
        return isNaN(parsed) ? null : parsed;
    }

    function toBoolNullable(value) {
        if (value == null || value === '') return null;
        return String(value).toLowerCase() === 'true';
    }

    function emptyToNull(value) {
        return value == null || value === '' ? null : value;
    }

    function deepClone(obj) { return JSON.parse(JSON.stringify(obj)); }

    function initStagingData() {
        stagingData = {};
        (currentData?.condiciones || []).forEach(function (c) {
            stagingData[Number(c.tipoPago)] = deepClone(c);
        });
    }

    function getStagingCondicion(tipoPago) {
        return stagingData[Number(tipoPago)] || null;
    }

    function findTarjeta(condicion, tarjetaId) {
        return (condicion?.tarjetas || []).find(function (t) {
            return String(t.configuracionTarjetaId || '') === String(tarjetaId || '');
        }) || null;
    }

    function openModal(trigger) {
        modal = modal || el('modal-condiciones-pago-producto');
        if (!modal) return;
        var productoId = trigger.getAttribute('data-condiciones-pago-producto-id');
        var nombre = trigger.getAttribute('data-condiciones-pago-producto-nombre') || 'Producto seleccionado';
        el('condiciones-pago-producto-id').value = productoId;
        el('condiciones-pago-producto-label').textContent = nombre;
        clearMessages();
        setLoading(true);
        lastFocusedElement = document.activeElement;
        modal.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
        fetch('/Producto/CondicionesPago/' + productoId, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (resp) { return resp.json().then(function (json) { if (!resp.ok || json.success === false) throw json; return json; }); })
            .then(function (json) {
                currentData = json.data;
                el('condiciones-pago-producto-label').textContent =
                    [(currentData.productoCodigo || '').trim(), currentData.productoNombre || nombre].filter(Boolean).join(' - ');
                initStagingData();
                render();
            })
            .catch(function (err) { currentData = null; renderEmpty(); showBox('condiciones-pago-validation', extractErrors(err).join(' ')); })
            .finally(function () { setLoading(false); });
    }

    function closeModal() {
        if (!modal) return;
        closeMedioModal();
        modal.classList.add('hidden');
        document.body.style.overflow = '';
        currentData = null; stagingData = {};
        clearMessages();
        if (lastFocusedElement && typeof lastFocusedElement.focus === 'function') lastFocusedElement.focus();
        lastFocusedElement = null;
    }

    function render() {
        var medios = el('condiciones-pago-medios');
        if (!medios) return;
        medios.innerHTML = tiposPago.map(renderMedioCard).join('');
        updateConfiguredCount();
    }

    function renderEmpty() {
        var medios = el('condiciones-pago-medios');
        if (medios) medios.innerHTML = '';
        updateConfiguredCount();
    }

    function updateConfiguredCount() {
        var count = Object.values(stagingData).filter(function (c) { return c && (c.activo || c.permitido != null); }).length;
        var countEl = el('condiciones-pago-count');
        if (countEl) countEl.textContent = count + ' configurados';
    }

    function renderMedioCard(tipo) {
        var condicion = getStagingCondicion(tipo.value);
        var activo = condicion ? (condicion.activo !== false) : false;
        var permitido = condicion ? condicion.permitido : null;
        var colors = colorMap[tipo.color] || colorMap.blue;
        var permitidoLabel = permitido === false ? 'Bloqueado' : permitido === true ? 'Permitido' : 'Heredar';
        var permitidoBadge = permitido === false ? 'condiciones-badge condiciones-badge--blocked'
            : permitido === true ? 'condiciones-badge condiciones-badge--allowed'
            : 'condiciones-badge condiciones-badge--inherit';
        var activoBadge = activo ? 'condiciones-badge condiciones-badge--active' : 'condiciones-badge condiciones-badge--inactive';
        var activoLabel = activo ? 'Activa' : 'Inactiva';
        var extraInfo = '';
        if (condicion && tipo.mode === 'card') {
            var parts = [];
            if (condicion.maxCuotasSinInteres != null) parts.push(condicion.maxCuotasSinInteres + ' ctas s/int');
            if (condicion.maxCuotasConInteres != null) parts.push(condicion.maxCuotasConInteres + ' ctas c/int');
            if (parts.length) extraInfo = parts.join(' - ');
        }
        if (condicion && tipo.mode === 'credit' && condicion.maxCuotasCredito != null) extraInfo = condicion.maxCuotasCredito + ' cuotas';
        var metaText = extraInfo || getModeLabel(tipo);
        return '<button type="button" class="condiciones-card" data-condiciones-medio-open="' + tipo.value + '">' +
            '<span class="condiciones-card__icon ' + colors.bg + ' ' + colors.text + ' ' + colors.border + '">' +
            '<span class="material-symbols-outlined" style="font-size:20px">' + tipo.icon + '</span></span>' +
            '<span class="condiciones-card__body">' +
            '<span class="condiciones-card__name">' + esc(tipo.label) + '</span>' +
            '<span class="condiciones-card__meta">' + esc(metaText) + '</span></span>' +
            '<span class="condiciones-card__badges">' +
            '<span class="' + activoBadge + '">' + esc(activoLabel) + '</span>' +
            '<span class="' + permitidoBadge + '">' + esc(permitidoLabel) + '</span></span>' +
            '<span class="condiciones-card__arrow"><span class="material-symbols-outlined" style="font-size:20px">chevron_right</span></span>' +
            '</button>';
    }

    function getModeLabel(tipo) {
        if (tipo.mode === 'card') return 'Tarjeta - cuotas y reglas';
        if (tipo.mode === 'credit') return 'Credito Personal';
        return 'Medio directo';
    }

    function openMedioModal(tipoPago) {
        detailModal = detailModal || el('modal-condiciones-medio');
        if (!detailModal || !currentData) return;
        currentDetailTipoPago = Number(tipoPago);
        var tipo = tiposPago.find(function (t) { return t.value === currentDetailTipoPago; });
        if (!tipo) return;
        var titleEl = el('condiciones-medio-title');
        var subtitleEl = el('condiciones-medio-subtitle');
        var bodyEl = el('condiciones-medio-body');
        if (titleEl) titleEl.textContent = tipo.label;
        if (subtitleEl) subtitleEl.textContent = getModeLabel(tipo);
        if (bodyEl) { bodyEl.innerHTML = renderMedioDetail(tipo); ensureDetailInputStyles(); }
        detailModal.classList.remove('hidden');
        var firstInput = bodyEl?.querySelector('input:not([type="hidden"]), select, textarea');
        if (firstInput) setTimeout(function () { firstInput.focus(); }, 40);
    }

    function closeMedioModal() {
        detailModal = detailModal || el('modal-condiciones-medio');
        if (!detailModal) return;
        detailModal.classList.add('hidden');
        currentDetailTipoPago = -1;
    }

    function renderMedioDetail(tipo) {
        var condicion = getStagingCondicion(tipo.value);
        var activo = condicion ? (condicion.activo !== false) : false;
        var html = '<div class="condiciones-panel__header">' +
            '<div><h4 class="text-base font-black text-white">' + esc(tipo.label) + '</h4>' +
            '<p class="text-xs text-slate-500 mt-0.5">' + esc(getModeHelp(tipo)) + '</p></div>' +
            '<label class="condiciones-check cursor-pointer"><span>Activa</span>' +
            '<input type="checkbox" id="cond-activo" value="true" class="rounded border-slate-600 text-primary focus:ring-primary"' + (activo ? ' checked' : '') + ' />' +
            '<span>Participa en venta y diagnostico.</span></label></div>';
        html += '<div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">';
        html += detailField('Disponibilidad', triStateSelectId('cond-permitido', condicion?.permitido), 'Heredar usa la configuracion global.');
        html += renderCuotasDetailFields(condicion, tipo);
        html += renderAdjustmentDetailFields(condicion, tipo);
        html += '</div>';
        html += '<label class="mt-4 block text-xs font-semibold text-slate-400">Observaciones' +
            '<textarea id="cond-observaciones" rows="2" class="condiciones-input mt-1 resize-none" placeholder="Notas internas">' +
            esc(condicion?.observaciones || '') + '</textarea></label>';
        if (tipo.mode !== 'direct') html += renderPlanesDetail(condicion, tipo);
        return html;
    }

    function getModeHelp(tipo) {
        if (tipo.mode === 'card') return 'Tarjeta con cuotas sin/con interes y reglas especificas por tarjeta.';
        if (tipo.mode === 'credit') return 'Credito Personal se configura con sus propias cuotas.';
        return 'Medio directo: no usa campos de cuotas en esta fase.';
    }

    function renderCuotasDetailFields(condicion, tipo) {
        if (tipo.mode === 'card') {
            return detailField('Cuotas sin interes', numberInputId('cond-maxCuotasSinInteres', condicion?.maxCuotasSinInteres, 'Heredar'), 'Maximo general.') +
                detailField('Cuotas con interes', numberInputId('cond-maxCuotasConInteres', condicion?.maxCuotasConInteres, 'Heredar'), 'Maximo general.');
        }
        if (tipo.mode === 'credit') {
            return detailField('Cuotas credito', numberInputId('cond-maxCuotasCredito', condicion?.maxCuotasCredito, 'Heredar'), 'Maximo de cuotas para Credito Personal.');
        }
        return '';
    }

    function renderAdjustmentDetailFields(condicion, tipo) {
        if (tipo.mode === 'credit') return '';
        return detailField('Recargo %', percentInputId('cond-porcentajeRecargo', condicion?.porcentajeRecargo, 'Heredar'), 'Informativo.') +
            detailField('Descuento max. %', percentInputId('cond-porcentajeDescuentoMaximo', condicion?.porcentajeDescuentoMaximo, 'Heredar'), 'Informativo.');
    }

    function renderTarjetasDetail(condicion) {
        var tarjetas = currentData?.tarjetasDisponibles || [];
        if (!tarjetas.length) return '<div class="mt-4 rounded-lg border border-slate-800 bg-slate-950/30 px-3 py-2 text-xs text-slate-500">No hay tarjetas configuradas.</div>';
        var rows = tarjetas.map(function (tarjeta, idx) {
            var regla = findTarjeta(condicion, tarjeta.id);
            var activaRegla = regla ? (regla.activo !== false) : false;
            return '<div class="rounded-xl border border-slate-800 bg-slate-950/40 p-4" data-tarjeta-row="' + idx + '">' +
                '<div class="mb-3 flex flex-wrap items-center justify-between gap-2">' +
                '<div><p class="text-sm font-bold text-white">' + esc(tarjeta.nombreTarjeta) + '</p>' +
                '<p class="text-[11px] text-slate-500">' + (tarjeta.activa ? 'Activa' : 'Inactiva') + '</p></div>' +
                '<label class="inline-flex cursor-pointer items-center gap-2 text-[11px] font-bold uppercase tracking-[0.12em] text-slate-400">' +
                '<input type="checkbox" data-t-activo="' + idx + '" value="true" class="rounded border-slate-600 text-primary focus:ring-primary"' + (activaRegla ? ' checked' : '') + ' />' +
                'Regla activa</label></div>' +
                '<div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">' +
                detailField('Disponibilidad', triStateSelectData('t-permitido', idx, regla?.permitido), 'Heredar usa la regla del medio.') +
                detailField('Cuotas sin interes', numberInputData('t-sin', idx, regla?.maxCuotasSinInteres, 'Heredar'), '') +
                detailField('Cuotas con interes', numberInputData('t-con', idx, regla?.maxCuotasConInteres, 'Heredar'), '') +
                detailField('Recargo %', percentInputData('t-recargo', idx, regla?.porcentajeRecargo, 'Heredar'), '') +
                detailField('Descuento max. %', percentInputData('t-desc', idx, regla?.porcentajeDescuentoMaximo, 'Heredar'), '') +
                '</div>' +
                '<label class="mt-3 block text-xs font-semibold text-slate-400">Observaciones' +
                '<input type="text" data-t-obs="' + idx + '" value="' + esc(regla?.observaciones || '') + '" placeholder="Notas internas" class="condiciones-input mt-1 w-full" /></label>' +
                '</div>';
        }).join('');
        return '<div class="mt-5 border-t border-slate-800 pt-5">' +
            '<h5 class="mb-3 text-xs font-black uppercase tracking-[0.16em] text-slate-400">Reglas por tarjeta</h5>' +
            '<div class="grid gap-3">' + rows + '</div></div>';
    }

    function renderPlanesDetail(condicion, tipo) {
        var planes = condicion?.planes || [];
        var sectionTitle = tipo.mode === 'credit' ? 'Planes de cuotas (Credito Personal)' : 'Planes de cuotas';
        var rows = planes.map(function (plan, idx) { return renderPlanDetailRow(idx, plan); }).join('');
        return '<div class="condiciones-planes-section" id="cond-planes-section">' +
            '<div class="mb-2 flex items-center justify-between gap-2">' +
            '<h5 class="text-xs font-black uppercase tracking-[0.16em] text-slate-400">' + esc(sectionTitle) + '</h5>' +
            '<button type="button" id="btn-cond-add-plan" class="text-[11px] font-bold text-primary hover:underline focus:outline-none">' +
            '<span class="material-symbols-outlined" style="font-size:14px;vertical-align:middle">add</span> Agregar plan</button></div>' +
            '<div class="mb-2 rounded-lg bg-slate-900/60 p-2 text-[11px] leading-snug text-slate-400">' +
            '<p>Ajuste negativo = descuento, cero = sin cambio, positivo = recargo. Cuota inactiva no se mostrara.</p></div>' +
            '<div class="overflow-x-auto"><table class="condiciones-planes-table w-full text-xs">' +
            '<thead class="border-b border-slate-800"><tr>' +
            '<th class="condiciones-planes-th text-left">Cuotas</th><th class="condiciones-planes-th text-center">Activa</th>' +
            '<th class="condiciones-planes-th text-right">Ajuste %</th><th class="condiciones-planes-th text-left">Observaciones</th>' +
            '</tr></thead><tbody id="cond-planes-tbody">' + rows + '</tbody></table></div></div>';
    }

    function renderPlanDetailRow(idx, plan) {
        var activo = plan ? (plan.activo !== false) : true;
        return '<tr class="condiciones-plan-row border-b border-slate-800/60" data-plan-row="' + idx + '">' +
            '<td class="px-2 py-1.5">' + (plan?.id != null ? '<input type="hidden" data-plan-id="' + idx + '" value="' + esc(plan.id) + '" />' : '') +
            '<input data-plan-cuotas="' + idx + '" type="number" min="1" step="1" value="' + esc(plan?.cantidadCuotas != null ? plan.cantidadCuotas : '') + '" placeholder="ej. 3" class="condiciones-input w-20 text-center" /></td>' +
            '<td class="px-2 py-1.5 text-center"><input type="checkbox" data-plan-activo="' + idx + '" value="true"' + (activo ? ' checked' : '') + ' class="rounded border-slate-600 text-primary focus:ring-primary" /></td>' +
            '<td class="px-2 py-1.5"><input data-plan-ajuste="' + idx + '" type="number" step="0.01" value="' + esc(plan?.ajustePorcentaje != null ? plan.ajustePorcentaje : 0) + '" placeholder="0.00" class="condiciones-input w-24 text-right" /></td>' +
            '<td class="px-2 py-1.5"><input data-plan-obs="' + idx + '" type="text" value="' + esc(plan?.observaciones || '') + '" placeholder="Notas" class="condiciones-input" /></td></tr>';
    }

    function confirmMedioChanges() {
        if (currentDetailTipoPago < 0 || !currentData) return;
        var body = el('condiciones-medio-body');
        if (!body) return;
        var tipo = tiposPago.find(function (t) { return t.value === currentDetailTipoPago; });
        if (!tipo) return;
        var existing = getStagingCondicion(currentDetailTipoPago);
        var activo = body.querySelector('#cond-activo')?.checked || false;
        var permitido = toBoolNullable(body.querySelector('#cond-permitido')?.value ?? '');
        var maxCuotasSinInteres = tipo.mode === 'card' ? toInt(body.querySelector('#cond-maxCuotasSinInteres')?.value) : (existing?.maxCuotasSinInteres ?? null);
        var maxCuotasConInteres = tipo.mode === 'card' ? toInt(body.querySelector('#cond-maxCuotasConInteres')?.value) : (existing?.maxCuotasConInteres ?? null);
        var maxCuotasCredito = tipo.mode === 'credit' ? toInt(body.querySelector('#cond-maxCuotasCredito')?.value) : (existing?.maxCuotasCredito ?? null);
        var porcentajeRecargo = tipo.mode !== 'credit' ? toDecimal(body.querySelector('#cond-porcentajeRecargo')?.value) : (existing?.porcentajeRecargo ?? null);
        var porcentajeDescuentoMaximo = tipo.mode !== 'credit' ? toDecimal(body.querySelector('#cond-porcentajeDescuentoMaximo')?.value) : (existing?.porcentajeDescuentoMaximo ?? null);
        var observaciones = emptyToNull(body.querySelector('#cond-observaciones')?.value);
        var tarjetas = existing?.tarjetas || [];
        var planes = [];
        body.querySelectorAll('[data-plan-row]').forEach(function (row) {
            var idx = row.getAttribute('data-plan-row');
            planes.push({
                id: toInt(row.querySelector('[data-plan-id="' + idx + '"]')?.value),
                cantidadCuotas: toInt(row.querySelector('[data-plan-cuotas="' + idx + '"]')?.value),
                activo: row.querySelector('[data-plan-activo="' + idx + '"]')?.checked || false,
                ajustePorcentaje: toDecimal(row.querySelector('[data-plan-ajuste="' + idx + '"]')?.value) || 0,
                tipoAjuste: 0,
                observaciones: emptyToNull(row.querySelector('[data-plan-obs="' + idx + '"]')?.value)
            });
        });
        stagingData[currentDetailTipoPago] = {
            id: existing?.id || null, rowVersion: existing?.rowVersion || null,
            tipoPago: currentDetailTipoPago, activo: activo, permitido: permitido,
            maxCuotasSinInteres: maxCuotasSinInteres, maxCuotasConInteres: maxCuotasConInteres,
            maxCuotasCredito: maxCuotasCredito, porcentajeRecargo: porcentajeRecargo,
            porcentajeDescuentoMaximo: porcentajeDescuentoMaximo, observaciones: observaciones,
            tarjetas: tarjetas, planes: planes
        };
        closeMedioModal();
        render();
    }

    function triStateSelectId(id, value) {
        var sel = value === true ? 'true' : value === false ? 'false' : '';
        return '<select id="' + id + '" class="condiciones-input">' +
            '<option value=""' + (sel === '' ? ' selected' : '') + '>Heredar - usa configuracion global</option>' +
            '<option value="true"' + (sel === 'true' ? ' selected' : '') + '>Permitido</option>' +
            '<option value="false"' + (sel === 'false' ? ' selected' : '') + '>Bloqueado</option></select>';
    }

    function triStateSelectData(attr, idx, value) {
        var sel = value === true ? 'true' : value === false ? 'false' : '';
        return '<select data-' + attr + '="' + idx + '" class="condiciones-input">' +
            '<option value=""' + (sel === '' ? ' selected' : '') + '>Heredar</option>' +
            '<option value="true"' + (sel === 'true' ? ' selected' : '') + '>Permitido</option>' +
            '<option value="false"' + (sel === 'false' ? ' selected' : '') + '>Bloqueado</option></select>';
    }

    function numberInputId(id, value, placeholder) {
        return '<input id="' + id + '" type="number" min="0" step="1" value="' + esc(value ?? '') + '" placeholder="' + esc(placeholder) + '" class="condiciones-input" />';
    }

    function numberInputData(attr, idx, value, placeholder) {
        return '<input data-' + attr + '="' + idx + '" type="number" min="0" step="1" value="' + esc(value ?? '') + '" placeholder="' + esc(placeholder) + '" class="condiciones-input" />';
    }

    function percentInputId(id, value, placeholder) {
        return '<input id="' + id + '" type="number" min="0" max="100" step="0.01" value="' + esc(value ?? '') + '" placeholder="' + esc(placeholder) + '" class="condiciones-input" />';
    }

    function percentInputData(attr, idx, value, placeholder) {
        return '<input data-' + attr + '="' + idx + '" type="number" min="0" max="100" step="0.01" value="' + esc(value ?? '') + '" placeholder="' + esc(placeholder) + '" class="condiciones-input" />';
    }

    function detailField(label, html, help) {
        return '<label class="block text-xs font-semibold text-slate-400">' + esc(label) +
            '<span class="mt-1 block">' + html + '</span>' +
            (help ? '<span class="mt-1 block text-[11px] font-medium leading-snug text-slate-500">' + esc(help) + '</span>' : '') +
            '</label>';
    }

    function ensureDetailInputStyles() {
        var body = el('condiciones-medio-body');
        if (!body) return;
        body.querySelectorAll('.condiciones-input').forEach(function (node) {
            node.classList.add('w-full', 'rounded-lg', 'border', 'border-slate-700', 'bg-slate-800',
                'px-3', 'py-2', 'text-sm', 'text-white', 'outline-none', 'transition');
        });
    }

    async function handleSubmit(event) {
        event.preventDefault();
        if (!currentData) return;
        clearMessages();
        setSubmitLoading(true);
        try {
            var productoId = el('condiciones-pago-producto-id').value;
            var resp = await fetch('/Producto/CondicionesPago/' + productoId, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getToken(), 'X-Requested-With': 'XMLHttpRequest' },
                body: JSON.stringify(buildPayload(Number(productoId)))
            });
            var json = await resp.json();
            if (!resp.ok || json.success === false) throw json;
            currentData = json.data;
            initStagingData();
            render();
            showBox('condiciones-pago-success', json.message || 'Condiciones guardadas correctamente.');
        } catch (err) {
            showBox('condiciones-pago-validation', extractErrors(err).join(' '));
        } finally {
            setSubmitLoading(false);
        }
    }

    function buildPayload(productoId) {
        var condiciones = tiposPago.map(function (tipo) {
            var c = stagingData[tipo.value];
            return c && hasCondicionData(c) ? c : null;
        }).filter(Boolean);
        return { productoId: productoId, condiciones: condiciones };
    }

    function hasCondicionData(c) {
        return c.id != null || c.activo || c.permitido != null ||
            c.maxCuotasSinInteres != null || c.maxCuotasConInteres != null || c.maxCuotasCredito != null ||
            c.porcentajeRecargo != null || c.porcentajeDescuentoMaximo != null || c.observaciones != null ||
            (c.tarjetas && c.tarjetas.some(hasTarjetaData)) || (c.planes && c.planes.length > 0);
    }

    function hasTarjetaData(t) {
        return t.id != null || t.activo || t.permitido != null ||
            t.maxCuotasSinInteres != null || t.maxCuotasConInteres != null ||
            t.porcentajeRecargo != null || t.porcentajeDescuentoMaximo != null || t.observaciones != null;
    }

    document.addEventListener('click', function (event) {
        var medioOpen = event.target.closest('[data-condiciones-medio-open]');
        if (medioOpen) { event.preventDefault(); openMedioModal(Number(medioOpen.getAttribute('data-condiciones-medio-open'))); return; }
        if (event.target.closest('#btn-confirmar-medio')) { event.preventDefault(); confirmMedioChanges(); return; }
        if (event.target.closest('#btn-cond-add-plan')) { event.preventDefault(); addDetailPlanRow(); return; }
        var trigger = event.target.closest('[data-condiciones-pago-producto-id]');
        if (trigger) { event.preventDefault(); openModal(trigger); return; }
        if (event.target.closest('[data-condiciones-pago-close]')) { event.preventDefault(); closeModal(); return; }
        if (event.target.closest('[data-condiciones-medio-close]')) { event.preventDefault(); closeMedioModal(); return; }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') return;
        detailModal = detailModal || el('modal-condiciones-medio');
        if (detailModal && !detailModal.classList.contains('hidden')) { closeMedioModal(); return; }
        if (modal && !modal.classList.contains('hidden')) closeModal();
    });

    document.addEventListener('DOMContentLoaded', function () {
        var form = el('form-condiciones-pago-producto');
        if (form) form.addEventListener('submit', handleSubmit);
    });

    function addDetailPlanRow() {
        var tbody = el('cond-planes-tbody');
        if (!tbody) return;
        var rowCount = tbody.querySelectorAll('[data-plan-row]').length;
        tbody.insertAdjacentHTML('beforeend', renderPlanDetailRow(rowCount, null));
        ensureDetailInputStyles();
        tbody.querySelector('[data-plan-row="' + rowCount + '"]')?.querySelector('input[type="number"]')?.focus();
    }
}());
