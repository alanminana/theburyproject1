(function () {
    'use strict';

    var modal;
    var currentData = null;
    var tiposPago = [
        { value: 0, label: 'Efectivo', cardRules: false },
        { value: 1, label: 'Transferencia', cardRules: false },
        { value: 2, label: 'Tarjeta Debito', cardRules: true },
        { value: 3, label: 'Tarjeta Credito', cardRules: true },
        { value: 4, label: 'Cheque', cardRules: false },
        { value: 5, label: 'Credito Personal', cardRules: false },
        { value: 6, label: 'Mercado Pago', cardRules: false },
        { value: 7, label: 'Cuenta Corriente', cardRules: false }
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
            })
            .catch(function (err) {
                currentData = null;
                renderEmpty();
                showBox('condiciones-pago-validation', extractErrors(err).join(' '));
            })
            .finally(function () {
                setLoading(false);
            });
    }

    function closeModal() {
        if (!modal) return;
        modal.classList.add('hidden');
        document.body.style.overflow = '';
        currentData = null;
        clearMessages();
    }

    function triStateSelect(name, value) {
        var selected = value === true ? 'true' : value === false ? 'false' : '';
        return '<select name="' + name + '" class="condiciones-input">' +
            '<option value=""' + (selected === '' ? ' selected' : '') + '>Heredar</option>' +
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
        var tarjetas = el('condiciones-pago-tarjetas');
        if (!medios || !tarjetas) return;

        medios.innerHTML = tiposPago.map(renderMedio).join('');
        tarjetas.innerHTML = renderTarjetas();

        el('condiciones-pago-count').textContent = (currentData.condiciones || []).filter(function (c) { return c.activo; }).length + ' configurados';
        el('condiciones-pago-tarjetas-count').textContent = (currentData.tarjetasDisponibles || []).length + ' tarjetas';
        ensureInputStyles();
    }

    function renderEmpty() {
        el('condiciones-pago-medios').innerHTML = '';
        el('condiciones-pago-tarjetas').innerHTML = '';
        el('condiciones-pago-count').textContent = '0 configurados';
        el('condiciones-pago-tarjetas-count').textContent = '0 tarjetas';
    }

    function renderMedio(tipo, index) {
        var condicion = findCondicion(tipo.value);
        var prefix = 'condiciones[' + index + ']';
        return '<article class="rounded-xl border border-slate-800 bg-slate-900/50 p-4" data-condicion-card>' +
            hidden(prefix + '.id', condicion?.id) +
            hidden(prefix + '.rowVersion', condicion?.rowVersion) +
            hidden(prefix + '.tipoPago', tipo.value) +
            '<div class="mb-4 flex flex-wrap items-center justify-between gap-3">' +
            '<div><h4 class="text-base font-bold text-white">' + esc(tipo.label) + '</h4><p class="text-xs text-slate-500">Regla por producto</p></div>' +
            '<label class="inline-flex items-center gap-2 text-xs font-bold uppercase tracking-[0.12em] text-slate-400">' +
            '<input type="checkbox" name="' + prefix + '.activo" value="true" class="rounded border-slate-600 text-primary focus:ring-primary" ' + (condicion ? (condicion.activo !== false ? 'checked' : '') : '') + ' /> Activa</label>' +
            '</div>' +
            '<div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">' +
            field('Estado', triStateSelect(prefix + '.permitido', condicion?.permitido)) +
            field('Cuotas sin interes', numberInput(prefix + '.maxCuotasSinInteres', condicion?.maxCuotasSinInteres, 'Heredar')) +
            field('Cuotas con interes', numberInput(prefix + '.maxCuotasConInteres', condicion?.maxCuotasConInteres, 'Heredar')) +
            field('Cuotas credito', numberInput(prefix + '.maxCuotasCredito', condicion?.maxCuotasCredito, 'Heredar')) +
            field('Recargo %', percentInput(prefix + '.porcentajeRecargo', condicion?.porcentajeRecargo, 'Heredar')) +
            field('Descuento max. %', percentInput(prefix + '.porcentajeDescuentoMaximo', condicion?.porcentajeDescuentoMaximo, 'Heredar')) +
            '</div>' +
            '<label class="mt-3 block text-xs font-semibold text-slate-400">Observaciones' +
            '<textarea name="' + prefix + '.observaciones" rows="2" class="condiciones-input mt-1 resize-none" placeholder="Notas internas">' + esc(condicion?.observaciones || '') + '</textarea></label>' +
            renderTarjetasEditables(prefix, condicion, tipo) +
            '</article>';
    }

    function renderTarjetasEditables(prefix, condicion, tipo) {
        if (!tipo.cardRules) return '';
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
                field('Estado', triStateSelect(tPrefix + '.permitido', regla?.permitido)) +
                field('Cuotas sin interes', numberInput(tPrefix + '.maxCuotasSinInteres', regla?.maxCuotasSinInteres, 'Heredar')) +
                field('Cuotas con interes', numberInput(tPrefix + '.maxCuotasConInteres', regla?.maxCuotasConInteres, 'Heredar')) +
                field('Recargo %', percentInput(tPrefix + '.porcentajeRecargo', regla?.porcentajeRecargo, 'Heredar')) +
                field('Descuento max. %', percentInput(tPrefix + '.porcentajeDescuentoMaximo', regla?.porcentajeDescuentoMaximo, 'Heredar')) +
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
        if (!tipo.cardRules) return '';
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

    function renderTarjetas() {
        var tarjetas = currentData.tarjetasDisponibles || [];
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

    function field(label, html) {
        return '<label class="block text-xs font-semibold text-slate-400">' + esc(label) + '<span class="mt-1 block">' + html + '</span></label>';
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
                tarjetas: readTarjetas(data, prefix)
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
        if (event.key === 'Escape' && modal && !modal.classList.contains('hidden')) {
            closeModal();
        }
    });

    document.addEventListener('DOMContentLoaded', function () {
        var form = el('form-condiciones-pago-producto');
        if (form) form.addEventListener('submit', handleSubmit);
    });
}());
