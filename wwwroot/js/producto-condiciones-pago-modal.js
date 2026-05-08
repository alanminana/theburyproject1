(function () {
    'use strict';

    var modal;
    var currentData = null;
    var selectedTipoPago = 0;
    var lastFocusedElement = null;
    var tiposPago = [
        { value: 0, label: 'Efectivo', mode: 'direct' },
        { value: 1, label: 'Transferencia', mode: 'direct' },
        { value: 2, label: 'Tarjeta Debito', mode: 'card' },
        { value: 3, label: 'Tarjeta Credito', mode: 'card' },
        { value: 4, label: 'Cheque', mode: 'direct' },
        { value: 5, label: 'Credito Personal', mode: 'credit' },
        { value: 6, label: 'Mercado Pago', mode: 'card' },
        { value: 7, label: 'Cuenta Corriente', mode: 'direct' }
    ];

    function el(id) { return document.getElementById(id); }

    function esc(value) {
        var d = document.createElement('div');
        d.textContent = value == null ? '' : String(value);
        return d.innerHTML;
    }

    function getToken() {
        return document.querySelector('#form-condiciones-pago-producto input[name="__RequestVerificationToken"]')?.value || '';
    }

    function findCondicion(tipoPago) {
        return (currentData?.condiciones || []).find(function (c) { return Number(c.tipoPago) === Number(tipoPago); }) || null;
    }

    function findTarjeta(condicion, tarjetaId) {
        return (condicion?.tarjetas || []).find(function (t) {
            return String(t.configuracionTarjetaId || '') === String(tarjetaId || '');
        }) || null;
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
        selectedTipoPago = 0;
        modal.classList.remove('hidden');
        document.body.style.overflow = 'hidden';

        fetch('/Producto/CondicionesPago/' + productoId, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (resp) {
                return resp.json().then(function (json) {
                    if (!resp.ok || json.success === false) throw json;
                    return json;
                });
            })
            .then(function (json) {
                currentData = json.data;
                el('condiciones-pago-producto-label').textContent =
                    [(currentData.productoCodigo || '').trim(), currentData.productoNombre || nombre]
                        .filter(Boolean)
                        .join(' - ');
                render();
                focusInitialControl();
            })
            .catch(function (err) {
                currentData = null;
                renderEmpty();
                showBox('condiciones-pago-validation', extractErrors(err).join(' '));
            })
            .finally(function () {
                setLoading(false);
                if (currentData) focusInitialControl();
            });
    }

    function closeModal() {
        if (!modal) return;
        modal.classList.add('hidden');
        document.body.style.overflow = '';
        currentData = null;
        clearMessages();
        if (lastFocusedElement && typeof lastFocusedElement.focus === 'function') {
            lastFocusedElement.focus();
        }
        lastFocusedElement = null;
    }

    function triStateSelect(name, value) {
        var selected = value === true ? 'true' : value === false ? 'false' : '';
        return '<select name="' + name + '" class="condiciones-input">' +
            '<option value=""' + (selected === '' ? ' selected' : '') + '>Heredar - usa configuracion global</option>' +
            '<option value="true"' + (selected === 'true' ? ' selected' : '') + '>Permitido</option>' +
            '<option value="false"' + (selected === 'false' ? ' selected' : '') + '>Bloqueado</option>' +
            '</select>';
    }

    function numberInput(name, value, placeholder) {
        return '<input name="' + name + '" type="number" min="0" step="1" value="' + esc(value ?? '') + '" placeholder="' + esc(placeholder) + '" class="condiciones-input" />';
    }

    function percentInput(name, value, placeholder) {
        return '<input name="' + name + '" type="number" min="0" max="100" step="0.01" value="' + esc(value ?? '') + '" placeholder="' + esc(placeholder) + '" class="condiciones-input" />';
    }

    function hidden(name, value) {
        return '<input type="hidden" name="' + name + '" value="' + esc(value ?? '') + '" />';
    }

    function render() {
        var medios = el('condiciones-pago-medios');
        var detalle = el('condiciones-pago-detalle');
        if (!medios || !detalle) return;

        medios.innerHTML = renderMedioSelector();
        detalle.innerHTML = tiposPago.map(renderMedio).join('');

        el('condiciones-pago-count').textContent = (currentData.condiciones || []).filter(function (c) { return c.activo; }).length + ' configurados';
        renderTarjetasSummary();
        ensureInputStyles();
    }

    function renderEmpty() {
        el('condiciones-pago-medios').innerHTML = '';
        el('condiciones-pago-detalle').innerHTML = '';
        el('condiciones-pago-count').textContent = '0 configurados';
        renderTarjetasSummary();
    }

    function renderMedioSelector() {
        return tiposPago.map(function (tipo) {
            var condicion = findCondicion(tipo.value);
            var selected = Number(selectedTipoPago) === Number(tipo.value);
            var estado = condicion?.permitido === false ? 'Bloqueado' : condicion?.permitido === true ? 'Permitido' : 'Heredar';
            var activeText = condicion && condicion.activo === false ? 'Inactiva' : 'Activa';
            return '<button type="button" class="condiciones-medio-tab" data-condiciones-medio-tab="' + tipo.value + '" aria-selected="' + selected + '">' +
                '<span class="condiciones-medio-tab__main">' +
                '<span class="condiciones-medio-tab__label">' + esc(tipo.label) + '</span>' +
                '<span class="condiciones-medio-tab__meta">' + esc(activeText) + ' / ' + esc(estado) + '</span>' +
                '</span>' +
                '<span class="material-symbols-outlined condiciones-medio-tab__icon" aria-hidden="true">chevron_right</span>' +
                '</button>';
        }).join('');
    }

    function renderMedio(tipo, index) {
        var condicion = findCondicion(tipo.value);
        var prefix = 'condiciones[' + index + ']';
        var selected = Number(selectedTipoPago) === Number(tipo.value);
        return '<article class="condiciones-panel' + (selected ? '' : ' hidden') + '" data-condicion-card data-condiciones-panel="' + tipo.value + '">' +
            hidden(prefix + '.id', condicion?.id) +
            hidden(prefix + '.rowVersion', condicion?.rowVersion) +
            hidden(prefix + '.tipoPago', tipo.value) +
            '<div class="condiciones-panel__header">' +
            '<div><h4 class="text-lg font-black text-white">' + esc(tipo.label) + '</h4><p class="text-xs text-slate-400">' + esc(getModeHelp(tipo)) + '</p></div>' +
            '<label class="condiciones-check">Activa' +
            '<input type="checkbox" name="' + prefix + '.activo" value="true" class="rounded border-slate-600 text-primary focus:ring-primary" ' + (condicion ? (condicion.activo !== false ? 'checked' : '') : '') + ' />' +
            '<span>La regla participa en venta/diagnostico.</span></label>' +
            '</div>' +
            '<div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">' +
            field('Disponibilidad', triStateSelect(prefix + '.permitido', condicion?.permitido), 'Heredar usa configuracion global. Bloqueado impide usar este medio para el producto.') +
            renderCuotasFields(prefix, condicion, tipo) +
            renderAdjustmentFields(prefix, condicion, tipo) +
            '</div>' +
            '<label class="mt-3 block text-xs font-semibold text-slate-400">Observaciones' +
            '<textarea name="' + prefix + '.observaciones" rows="2" class="condiciones-input mt-1 resize-none" placeholder="Notas internas">' + esc(condicion?.observaciones || '') + '</textarea></label>' +
            renderTarjetasEditables(prefix, condicion, tipo) +
            renderPlanes(prefix, condicion, tipo) +
            '</article>';
    }

    function renderCuotasFields(prefix, condicion, tipo) {
        if (tipo.mode === 'card') {
            return field('Cuotas sin interes', numberInput(prefix + '.maxCuotasSinInteres', condicion?.maxCuotasSinInteres, 'Heredar'), 'Maximo general para este medio. Heredar usa configuracion global.') +
                field('Cuotas con interes', numberInput(prefix + '.maxCuotasConInteres', condicion?.maxCuotasConInteres, 'Heredar'), 'Maximo general para este medio. Heredar usa configuracion global.') +
                hidden(prefix + '.maxCuotasCredito', condicion?.maxCuotasCredito);
        }

        if (tipo.mode === 'credit') {
            return hidden(prefix + '.maxCuotasSinInteres', condicion?.maxCuotasSinInteres) +
                hidden(prefix + '.maxCuotasConInteres', condicion?.maxCuotasConInteres) +
                field('Cuotas credito', numberInput(prefix + '.maxCuotasCredito', condicion?.maxCuotasCredito, 'Heredar'), 'Maximo de cuotas para Credito Personal. Heredar usa configuracion global.');
        }

        return hidden(prefix + '.maxCuotasSinInteres', condicion?.maxCuotasSinInteres) +
            hidden(prefix + '.maxCuotasConInteres', condicion?.maxCuotasConInteres) +
            hidden(prefix + '.maxCuotasCredito', condicion?.maxCuotasCredito);
    }

    function renderAdjustmentFields(prefix, condicion, tipo) {
        if (tipo.mode === 'credit') {
            return hidden(prefix + '.porcentajeRecargo', condicion?.porcentajeRecargo) +
                hidden(prefix + '.porcentajeDescuentoMaximo', condicion?.porcentajeDescuentoMaximo);
        }

        return field('Recargo %', percentInput(prefix + '.porcentajeRecargo', condicion?.porcentajeRecargo, 'Heredar'), 'Informativo por ahora: no modifica totales.') +
            field('Descuento max. %', percentInput(prefix + '.porcentajeDescuentoMaximo', condicion?.porcentajeDescuentoMaximo, 'Heredar'), 'Informativo por ahora: no modifica totales.');
    }

    function getModeHelp(tipo) {
        if (tipo.mode === 'card') return 'Configuracion tipo tarjeta con cuotas sin/con interes y reglas por tarjeta cuando existan.';
        if (tipo.mode === 'credit') return 'Credito Personal se configura separado de tarjetas.';
        return 'Medio directo: no usa campos de cuotas en esta fase.';
    }

    function renderTarjetasEditables(prefix, condicion, tipo) {
        if (tipo.mode !== 'card') return renderHiddenTarjetas(prefix, condicion, tipo);
        var tarjetas = currentData.tarjetasDisponibles || [];
        if (!tarjetas.length) {
            return '<div class="mt-4 rounded-lg border border-slate-800 bg-slate-950/30 px-3 py-2 text-xs text-slate-500">No hay tarjetas disponibles para reglas especificas.</div>';
        }

        var rows = tarjetas.map(function (tarjeta, idx) {
            var regla = findTarjeta(condicion, tarjeta.id);
            var tPrefix = prefix + '.tarjetas[' + idx + ']';
            return '<div class="rounded-lg border border-slate-800 bg-slate-950/40 p-3">' +
                hidden(tPrefix + '.id', regla?.id) +
                hidden(tPrefix + '.rowVersion', regla?.rowVersion) +
                hidden(tPrefix + '.configuracionTarjetaId', tarjeta.id) +
                '<div class="mb-3 flex flex-wrap items-center justify-between gap-2">' +
                '<div><p class="text-sm font-bold text-white">' + esc(tarjeta.nombreTarjeta) + '</p><p class="text-[11px] text-slate-500">' + (tarjeta.activa ? 'Activa' : 'Inactiva') + '</p></div>' +
                '<label class="inline-flex items-center gap-2 text-[11px] font-bold uppercase tracking-[0.12em] text-slate-400">' +
                '<input type="checkbox" name="' + tPrefix + '.activo" value="true" class="rounded border-slate-600 text-primary focus:ring-primary" ' + (regla ? (regla.activo !== false ? 'checked' : '') : '') + ' /> Regla activa</label>' +
                '</div>' +
                '<div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">' +
                field('Disponibilidad', triStateSelect(tPrefix + '.permitido', regla?.permitido), 'Heredar usa la regla del medio y luego la configuracion global.') +
                field('Cuotas sin interes', numberInput(tPrefix + '.maxCuotasSinInteres', regla?.maxCuotasSinInteres, 'Heredar'), 'Maximo especifico de esta tarjeta.') +
                field('Cuotas con interes', numberInput(tPrefix + '.maxCuotasConInteres', regla?.maxCuotasConInteres, 'Heredar'), 'Maximo especifico de esta tarjeta.') +
                field('Recargo %', percentInput(tPrefix + '.porcentajeRecargo', regla?.porcentajeRecargo, 'Heredar'), 'Informativo por ahora: no modifica totales.') +
                field('Descuento max. %', percentInput(tPrefix + '.porcentajeDescuentoMaximo', regla?.porcentajeDescuentoMaximo, 'Heredar'), 'Informativo por ahora: no modifica totales.') +
                '</div>' +
                '<label class="mt-3 block text-xs font-semibold text-slate-400">Observaciones' +
                '<textarea name="' + tPrefix + '.observaciones" rows="2" class="condiciones-input mt-1 resize-none" placeholder="Notas internas">' + esc(regla?.observaciones || '') + '</textarea></label>' +
                '</div>';
        }).join('');

        return '<div class="mt-4 border-t border-slate-800 pt-4">' +
            '<h5 class="mb-3 text-xs font-black uppercase tracking-[0.16em] text-slate-400">Reglas por tarjeta</h5>' +
            '<div class="grid gap-3">' + rows + '</div>' +
            '</div>';
    }

    function renderHiddenTarjetas(prefix, condicion, tipo) {
        if (tipo.mode !== 'card') return '';
        var tarjetas = currentData.tarjetasDisponibles || [];
        return tarjetas.map(function (tarjeta, idx) {
            var regla = findTarjeta(condicion, tarjeta.id);
            var tPrefix = prefix + '.tarjetas[' + idx + ']';
            return hidden(tPrefix + '.id', regla?.id) +
                hidden(tPrefix + '.rowVersion', regla?.rowVersion) +
                hidden(tPrefix + '.configuracionTarjetaId', tarjeta.id) +
                hidden(tPrefix + '.permitido', regla?.permitido) +
                hidden(tPrefix + '.maxCuotasSinInteres', regla?.maxCuotasSinInteres) +
                hidden(tPrefix + '.maxCuotasConInteres', regla?.maxCuotasConInteres) +
                hidden(tPrefix + '.porcentajeRecargo', regla?.porcentajeRecargo) +
                hidden(tPrefix + '.porcentajeDescuentoMaximo', regla?.porcentajeDescuentoMaximo) +
                hidden(tPrefix + '.activo', regla?.activo === false ? 'false' : 'true') +
                hidden(tPrefix + '.observaciones', regla?.observaciones);
        }).join('');
    }

    function renderPlanes(prefix, condicion, tipo) {
        if (tipo.mode === 'direct') return '';

        var planes = condicion?.planes || [];
        var tieneActivos = planes.some(function (p) { return p.activo !== false; });
        var sectionTitle = tipo.mode === 'credit' ? 'Planes de cuotas (Credito Personal)' : 'Planes de cuotas';

        var rows = planes.map(function (plan, idx) {
            return renderPlanRow(prefix + '.planes[' + idx + ']', plan);
        }).join('');

        return '<div class="condiciones-planes-section mt-4 border-t border-slate-800 pt-4" data-planes-section="' + tipo.value + '">' +
            '<div class="mb-2 flex items-center justify-between gap-2">' +
            '<h5 class="text-xs font-black uppercase tracking-[0.16em] text-slate-400">' + esc(sectionTitle) + '</h5>' +
            '<button type="button" class="text-[11px] font-bold text-primary hover:underline focus:outline-none" data-condiciones-add-plan="' + tipo.value + '">' +
            '<span class="material-symbols-outlined text-[14px] align-middle">add</span> Agregar plan' +
            '</button>' +
            '</div>' +
            '<div class="mb-2 rounded-lg bg-slate-900/60 p-2 text-[11px] leading-snug text-slate-400 space-y-0.5">' +
            '<p>Ajuste negativo descuenta precio, cero no cambia, positivo aplica recargo.</p>' +
            '<p>Cuota inactiva no se mostrara al vendedor en venta futura.</p>' +
            '</div>' +
            (!tieneActivos
                ? '<div class="mb-2 rounded-lg border border-slate-700 bg-slate-800/50 px-3 py-2 text-[11px] text-slate-500" data-planes-fallback>' +
                  '<span class="material-symbols-outlined text-[13px] align-middle">info</span>' +
                  ' Sin planes activos: se usan los maximos escalares actuales.' +
                  '</div>'
                : '') +
            '<div class="overflow-x-auto">' +
            '<table class="condiciones-planes-table w-full text-xs border-collapse">' +
            '<thead class="border-b border-slate-800"><tr>' +
            '<th class="condiciones-planes-th text-left">Cuotas</th>' +
            '<th class="condiciones-planes-th text-center">Activa</th>' +
            '<th class="condiciones-planes-th text-right">Ajuste %</th>' +
            '<th class="condiciones-planes-th text-left">Tipo ajuste</th>' +
            '<th class="condiciones-planes-th text-left">Observaciones</th>' +
            '</tr></thead>' +
            '<tbody data-planes-tbody>' + rows + '</tbody>' +
            '</table>' +
            '</div>' +
            '</div>';
    }

    function renderPlanRow(pPrefix, plan) {
        var activo = plan?.activo !== false;
        return '<tr class="condiciones-plan-row border-b border-slate-800/60">' +
            '<td class="px-2 py-1.5">' +
            hidden(pPrefix + '.id', plan?.id) +
            hidden(pPrefix + '.tipoAjuste', 0) +
            '<input name="' + pPrefix + '.cantidadCuotas" type="number" min="1" step="1" value="' + esc(plan?.cantidadCuotas != null ? plan.cantidadCuotas : '') + '" placeholder="ej. 3" class="condiciones-input w-20 text-center" />' +
            '</td>' +
            '<td class="px-2 py-1.5 text-center">' +
            '<input type="checkbox" name="' + pPrefix + '.activo" value="true"' + (activo ? ' checked' : '') + ' class="rounded border-slate-600 text-primary focus:ring-primary" />' +
            '</td>' +
            '<td class="px-2 py-1.5">' +
            '<input name="' + pPrefix + '.ajustePorcentaje" type="number" step="0.01" value="' + esc(plan?.ajustePorcentaje != null ? plan.ajustePorcentaje : 0) + '" placeholder="0.00" class="condiciones-input w-24 text-right" />' +
            '</td>' +
            '<td class="condiciones-planes-td-tipo px-2 py-1.5 text-slate-400">Porcentaje</td>' +
            '<td class="px-2 py-1.5">' +
            '<input name="' + pPrefix + '.observaciones" type="text" value="' + esc(plan?.observaciones || '') + '" placeholder="Notas internas" class="condiciones-input" />' +
            '</td>' +
            '</tr>';
    }

    function renderTarjetasSummary() {
        var count = el('condiciones-pago-tarjetas-count');
        var box = el('condiciones-pago-tarjetas');
        if (!count || !box) return;
        count.textContent = ((currentData?.tarjetasDisponibles || []).length) + ' tarjetas';
        box.innerHTML = renderTarjetas();
    }

    function renderTarjetas() {
        var tarjetas = currentData?.tarjetasDisponibles || [];
        if (!tarjetas.length) {
            return '<div class="rounded-xl border border-slate-800 bg-slate-900/50 p-5 text-sm text-slate-400">No hay tarjetas configuradas.</div>';
        }

        return tarjetas.map(function (t) {
            var estado = t.activa ? 'Activa' : 'Inactiva';
            return '<div class="rounded-xl border border-slate-800 bg-slate-900/50 p-4">' +
                '<div class="flex items-start justify-between gap-3">' +
                '<div><p class="font-bold text-white">' + esc(t.nombreTarjeta) + '</p><p class="text-xs text-slate-500">Disponible para reglas de tarjeta</p></div>' +
                '<span class="rounded-full bg-slate-800 px-2.5 py-1 text-[11px] font-bold text-slate-300">' + esc(estado) + '</span>' +
                '</div>' +
                '</div>';
        }).join('');
    }

    function field(label, html, help) {
        return '<label class="block text-xs font-semibold text-slate-400">' + esc(label) +
            '<span class="mt-1 block">' + html + '</span>' +
            (help ? '<span class="mt-1 block text-[11px] font-medium leading-snug text-slate-500">' + esc(help) + '</span>' : '') +
            '</label>';
    }

    function ensureInputStyles() {
        document.querySelectorAll('.condiciones-input').forEach(function (node) {
            node.className = 'condiciones-input w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-white outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/30 placeholder:text-slate-500';
        });
    }

    function formToJson(form) {
        var data = new FormData(form);
        var productoId = Number(el('condiciones-pago-producto-id').value);
        var condiciones = tiposPago.map(function (_, index) {
            var prefix = 'condiciones[' + index + '].';
            return {
                id: toInt(data.get(prefix + 'id')),
                rowVersion: emptyToNull(data.get(prefix + 'rowVersion')),
                tipoPago: toInt(data.get(prefix + 'tipoPago')),
                permitido: toBoolNullable(data.get(prefix + 'permitido')),
                maxCuotasSinInteres: toInt(data.get(prefix + 'maxCuotasSinInteres')),
                maxCuotasConInteres: toInt(data.get(prefix + 'maxCuotasConInteres')),
                maxCuotasCredito: toInt(data.get(prefix + 'maxCuotasCredito')),
                porcentajeRecargo: toDecimal(data.get(prefix + 'porcentajeRecargo')),
                porcentajeDescuentoMaximo: toDecimal(data.get(prefix + 'porcentajeDescuentoMaximo')),
                activo: data.has(prefix + 'activo'),
                observaciones: emptyToNull(data.get(prefix + 'observaciones')),
                tarjetas: readTarjetas(data, prefix),
                planes: readPlanes(data, prefix)
            };
        }).filter(hasCondicionData);

        return { productoId: productoId, condiciones: condiciones };
    }

    function readTarjetas(data, prefix) {
        var tarjetas = [];
        var available = currentData?.tarjetasDisponibles || [];
        for (var i = 0; i < available.length; i++) {
            var tPrefix = prefix + 'tarjetas[' + i + '].';
            if (!data.has(tPrefix + 'configuracionTarjetaId')) continue;
            tarjetas.push({
                id: toInt(data.get(tPrefix + 'id')),
                rowVersion: emptyToNull(data.get(tPrefix + 'rowVersion')),
                configuracionTarjetaId: toInt(data.get(tPrefix + 'configuracionTarjetaId')),
                permitido: toBoolNullable(data.get(tPrefix + 'permitido')),
                maxCuotasSinInteres: toInt(data.get(tPrefix + 'maxCuotasSinInteres')),
                maxCuotasConInteres: toInt(data.get(tPrefix + 'maxCuotasConInteres')),
                porcentajeRecargo: toDecimal(data.get(tPrefix + 'porcentajeRecargo')),
                porcentajeDescuentoMaximo: toDecimal(data.get(tPrefix + 'porcentajeDescuentoMaximo')),
                activo: data.has(tPrefix + 'activo'),
                observaciones: emptyToNull(data.get(tPrefix + 'observaciones'))
            });
        }
        return tarjetas.filter(hasTarjetaData);
    }

    function readPlanes(data, prefix) {
        var planes = [];
        for (var i = 0; i < 50; i++) {
            var pPrefix = prefix + 'planes[' + i + '].';
            var tipoAjuste = data.get(pPrefix + 'tipoAjuste');
            if (tipoAjuste === null) break;
            planes.push({
                id: toInt(data.get(pPrefix + 'id')),
                cantidadCuotas: toInt(data.get(pPrefix + 'cantidadCuotas')),
                activo: data.has(pPrefix + 'activo'),
                ajustePorcentaje: toDecimal(data.get(pPrefix + 'ajustePorcentaje')) || 0,
                tipoAjuste: toInt(tipoAjuste) || 0,
                observaciones: emptyToNull(data.get(pPrefix + 'observaciones'))
            });
        }
        return planes;
    }

    function hasCondicionData(condicion) {
        return condicion.id != null ||
            condicion.activo ||
            condicion.permitido != null ||
            condicion.maxCuotasSinInteres != null ||
            condicion.maxCuotasConInteres != null ||
            condicion.maxCuotasCredito != null ||
            condicion.porcentajeRecargo != null ||
            condicion.porcentajeDescuentoMaximo != null ||
            condicion.observaciones != null ||
            condicion.tarjetas.length > 0;
    }

    function hasTarjetaData(tarjeta) {
        return tarjeta.id != null ||
            tarjeta.activo ||
            tarjeta.permitido != null ||
            tarjeta.maxCuotasSinInteres != null ||
            tarjeta.maxCuotasConInteres != null ||
            tarjeta.porcentajeRecargo != null ||
            tarjeta.porcentajeDescuentoMaximo != null ||
            tarjeta.observaciones != null;
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

    function extractErrors(err) {
        if (Array.isArray(err?.errors)) return err.errors;
        if (err?.errors && typeof err.errors === 'object') {
            return Object.keys(err.errors).flatMap(function (k) { return err.errors[k]; });
        }
        return ['Error al procesar la solicitud.'];
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
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getToken(),
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: JSON.stringify(formToJson(event.currentTarget))
            });

            var json = await resp.json();
            if (!resp.ok || json.success === false) throw json;

            currentData = json.data;
            render();
            showBox('condiciones-pago-success', json.message || 'Condiciones guardadas.');
        } catch (err) {
            showBox('condiciones-pago-validation', extractErrors(err).join(' '));
        } finally {
            setSubmitLoading(false);
        }
    }

    document.addEventListener('click', function (event) {
        var medioTab = event.target.closest('[data-condiciones-medio-tab]');
        if (medioTab) {
            event.preventDefault();
            selectedTipoPago = Number(medioTab.getAttribute('data-condiciones-medio-tab'));
            updateSelectedMedio();
            return;
        }

        var addPlanBtn = event.target.closest('[data-condiciones-add-plan]');
        if (addPlanBtn) {
            event.preventDefault();
            handleAddPlan(Number(addPlanBtn.getAttribute('data-condiciones-add-plan')));
            return;
        }

        var trigger = event.target.closest('[data-condiciones-pago-producto-id]');
        if (trigger) {
            event.preventDefault();
            openModal(trigger);
            return;
        }

        if (event.target.closest('[data-condiciones-pago-close]')) {
            event.preventDefault();
            closeModal();
        }
    });

    document.addEventListener('keydown', function (event) {
        if (!modal || modal.classList.contains('hidden')) return;

        if (event.key === 'Escape') {
            closeModal();
            return;
        }

        if (event.key === 'Tab') {
            trapFocus(event);
        }
    });

    document.addEventListener('DOMContentLoaded', function () {
        var form = el('form-condiciones-pago-producto');
        if (form) form.addEventListener('submit', handleSubmit);
    });

    function updateSelectedMedio() {
        document.querySelectorAll('[data-condiciones-medio-tab]').forEach(function (node) {
            node.setAttribute('aria-selected', String(Number(node.getAttribute('data-condiciones-medio-tab')) === Number(selectedTipoPago)));
        });

        document.querySelectorAll('[data-condiciones-panel]').forEach(function (node) {
            node.classList.toggle('hidden', Number(node.getAttribute('data-condiciones-panel')) !== Number(selectedTipoPago));
        });

        var panel = document.querySelector('[data-condiciones-panel="' + selectedTipoPago + '"]');
        var firstInput = panel?.querySelector('input:not([type="hidden"]), select, textarea, button');
        if (firstInput) firstInput.focus();
    }

    function focusInitialControl() {
        var firstTab = document.querySelector('[data-condiciones-medio-tab]');
        if (firstTab) firstTab.focus();
    }

    function getFocusableNodes() {
        if (!modal || modal.classList.contains('hidden')) return [];
        return Array.prototype.slice.call(modal.querySelectorAll('a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'))
            .filter(function (node) {
                return node.offsetParent !== null || node === document.activeElement;
            });
    }

    function trapFocus(event) {
        var nodes = getFocusableNodes();
        if (!nodes.length) return;
        var first = nodes[0];
        var last = nodes[nodes.length - 1];

        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    }

    function handleAddPlan(tipoPago) {
        var section = document.querySelector('[data-planes-section="' + tipoPago + '"]');
        if (!section) return;

        var tbody = section.querySelector('[data-planes-tbody]');
        if (!tbody) return;

        var condicionIndex = tiposPago.findIndex(function (t) { return t.value === tipoPago; });
        var rowCount = tbody.querySelectorAll('tr').length;
        var pPrefix = 'condiciones[' + condicionIndex + '].planes[' + rowCount + ']';

        tbody.insertAdjacentHTML('beforeend', renderPlanRow(pPrefix, null));
        ensureInputStyles();

        var fallback = section.querySelector('[data-planes-fallback]');
        if (fallback) fallback.classList.add('hidden');

        var lastRow = tbody.querySelector('tr:last-child');
        var firstInput = lastRow ? lastRow.querySelector('input[type="number"]') : null;
        if (firstInput) firstInput.focus();
    }
}());
