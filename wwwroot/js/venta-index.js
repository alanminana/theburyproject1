/**
 * venta-index.js — Ventas Index page
 * Toast auto-dismiss + Modal Recargos/Descuentos
 */
(() => {
    'use strict';

    // ── Toasts ──
    document.querySelectorAll('[id^="toast-"]').forEach(el => {
        setTimeout(() => {
            el.style.transition = 'opacity 0.5s';
            el.style.opacity = '0';
            setTimeout(() => el.remove(), 500);
        }, 4000);
    });

    // ═══════════════════════════════════════
    //  Modal: Recargos y Descuentos
    // ═══════════════════════════════════════

    const TIPO_PAGO = {
        0: 'Efectivo',
        1: 'Transferencia',
        2: 'Tarjeta Débito',
        3: 'Tarjeta Crédito',
        4: 'Cheque',
        5: 'Crédito Personal',
        6: 'Mercado Pago',
        7: 'Cuenta Corriente',
        8: 'Tarjeta'
    };

    const TIPO_PAGO_ICON = {
        0: 'payments',
        1: 'swap_horiz',
        2: 'credit_card',
        3: 'credit_card',
        4: 'description',
        5: 'account_balance',
        6: 'phone_iphone',
        7: 'account_balance_wallet',
        8: 'credit_card'
    };

    const modal = document.getElementById('modal-recargo');
    const overlay = document.getElementById('modal-recargo-overlay');
    const btnOpen = document.getElementById('btn-configurar-recargo');
    const btnClose = document.getElementById('btn-cerrar-modal-recargo');
    const btnCancel = document.getElementById('btn-cancelar-recargos');
    const btnSave = document.getElementById('btn-guardar-recargos');
    const loadingEl = document.getElementById('recargo-loading');
    const errorEl = document.getElementById('recargo-error');
    const listaEl = document.getElementById('recargo-lista');

    if (!modal || !btnOpen) return;

    let configuraciones = [];

    function openModal() {
        modal.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
        cargarConfiguraciones();
    }

    function closeModal() {
        modal.classList.add('hidden');
        document.body.style.overflow = '';
    }

    btnOpen.addEventListener('click', openModal);
    btnClose.addEventListener('click', closeModal);
    btnCancel.addEventListener('click', closeModal);
    overlay.addEventListener('click', closeModal);

    async function cargarConfiguraciones() {
        loadingEl.classList.remove('hidden');
        errorEl.classList.add('hidden');
        listaEl.classList.add('hidden');

        try {
            const resp = await fetch('/ConfiguracionPago/GetConfiguracionesModal');
            const json = await resp.json();
            if (!json.success) throw new Error(json.message || 'Error desconocido');

            configuraciones = json.data || [];
            renderConfiguraciones();
        } catch (err) {
            errorEl.textContent = err.message;
            errorEl.classList.remove('hidden');
        } finally {
            loadingEl.classList.add('hidden');
        }
    }

    function renderConfiguraciones() {
        listaEl.innerHTML = '';

        if (configuraciones.length === 0) {
            listaEl.innerHTML = `
                <div class="text-center py-8">
                    <span class="material-symbols-outlined text-3xl text-slate-300 dark:text-slate-600">info</span>
                    <p class="text-sm text-slate-400 mt-2">No hay configuraciones de pago registradas.</p>
                    <p class="text-[11px] text-slate-400">Cree configuraciones desde el módulo de Configuración de Pagos.</p>
                </div>`;
            listaEl.classList.remove('hidden');
            return;
        }

        configuraciones.forEach((cfg, idx) => {
            const icon = TIPO_PAGO_ICON[cfg.tipoPago] || 'payment';
            const nombre = cfg.nombre || TIPO_PAGO[cfg.tipoPago] || `Tipo ${cfg.tipoPago}`;
            const esTarjeta = cfg.tipoPago === 2 || cfg.tipoPago === 3 || cfg.tipoPago === 8;
            const tarjetas = cfg.configuracionesTarjeta || [];

            const card = document.createElement('div');
            card.className = 'bg-slate-50 dark:bg-slate-800/50 rounded-xl border border-slate-200 dark:border-slate-700 overflow-hidden';
            card.dataset.index = idx;

            let tarjetaHTML = '';
            if (esTarjeta && tarjetas.length > 0) {
                const rows = tarjetas.map((t, ti) => `
                    <tr class="border-t border-slate-200 dark:border-slate-700 text-xs">
                        <td class="px-3 py-2 font-medium text-slate-700 dark:text-slate-300">${esc(t.nombreTarjeta)}</td>
                        <td class="px-3 py-2 text-center">
                            <span class="px-2 py-0.5 rounded text-[10px] font-black uppercase ${t.tipoTarjeta === 0 ? 'bg-blue-500/10 text-blue-500' : 'bg-purple-500/10 text-purple-500'}">
                                ${t.tipoTarjeta === 0 ? 'Débito' : 'Crédito'}
                            </span>
                        </td>
                        <td class="px-3 py-2 text-center">
                            ${t.permiteCuotas
                                ? `<div class="flex items-center justify-center gap-1">
                                     <span class="text-green-500 font-bold">Sí</span>
                                     <span class="text-slate-400">· hasta</span>
                                     <input type="number" min="1" max="60" value="${t.cantidadMaximaCuotas || 1}"
                                            data-cfg="${idx}" data-tarjeta="${ti}" data-field="cantidadMaximaCuotas"
                                            class="w-14 text-center text-xs bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 input-tarjeta" />
                                     <span class="text-slate-400">cuotas</span>
                                   </div>`
                                : '<span class="text-slate-400">No</span>'}
                        </td>
                        <td class="px-3 py-2 text-center">
                            ${t.permiteCuotas
                                ? `<select data-cfg="${idx}" data-tarjeta="${ti}" data-field="tipoCuota"
                                          class="text-xs bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-0.5 input-tarjeta">
                                       <option value="0" ${(t.tipoCuota === 0 || t.tipoCuota == null) ? 'selected' : ''}>Sin Interés</option>
                                       <option value="1" ${t.tipoCuota === 1 ? 'selected' : ''}>Con Interés</option>
                                   </select>`
                                : '<span class="text-slate-400">—</span>'}
                        </td>
                        <td class="px-3 py-2 text-center">
                            ${t.permiteCuotas && t.tipoCuota === 1
                                ? `<div class="flex items-center justify-center gap-1">
                                     <input type="number" step="0.01" min="0" max="100" value="${t.tasaInteresesMensual || 0}"
                                            data-cfg="${idx}" data-tarjeta="${ti}" data-field="tasaInteresesMensual"
                                            class="w-16 text-center text-xs bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 input-tarjeta" />
                                     <span class="text-slate-400">%</span>
                                   </div>`
                                : t.tipoTarjeta === 0
                                    ? `<div class="flex items-center justify-center gap-1">
                                         <input type="checkbox" ${t.tieneRecargoDebito ? 'checked' : ''}
                                                data-cfg="${idx}" data-tarjeta="${ti}" data-field="tieneRecargoDebito"
                                                class="rounded border-slate-300 text-primary focus:ring-primary input-tarjeta" />
                                         <input type="number" step="0.01" min="0" max="100" value="${t.porcentajeRecargoDebito || 0}"
                                                data-cfg="${idx}" data-tarjeta="${ti}" data-field="porcentajeRecargoDebito"
                                                class="w-14 text-center text-xs bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 input-tarjeta ${t.tieneRecargoDebito ? '' : 'opacity-50'}" 
                                                ${t.tieneRecargoDebito ? '' : 'disabled'} />
                                         <span class="text-slate-400">%</span>
                                       </div>`
                                    : '<span class="text-slate-400">—</span>'}
                        </td>
                    </tr>
                `).join('');

                tarjetaHTML = `
                    <div class="border-t border-slate-200 dark:border-slate-700">
                        <div class="px-4 py-2 bg-slate-100 dark:bg-slate-800">
                            <p class="text-[10px] font-black text-slate-500 uppercase tracking-wider">Tarjetas configuradas</p>
                        </div>
                        <div class="overflow-x-auto">
                            <table class="w-full text-xs">
                                <thead>
                                    <tr class="bg-slate-100 dark:bg-slate-800 text-[10px] font-black text-slate-500 uppercase">
                                        <th class="px-3 py-2 text-left">Tarjeta</th>
                                        <th class="px-3 py-2 text-center">Tipo</th>
                                        <th class="px-3 py-2 text-center">Cuotas</th>
                                        <th class="px-3 py-2 text-center">Interés</th>
                                        <th class="px-3 py-2 text-center">Recargo / Tasa</th>
                                    </tr>
                                </thead>
                                <tbody>${rows}</tbody>
                            </table>
                        </div>
                    </div>`;
            }

            card.innerHTML = `
                <div class="flex items-center gap-4 p-4">
                    <!-- Icon -->
                    <div class="size-10 rounded-lg bg-primary/10 flex items-center justify-center shrink-0">
                        <span class="material-symbols-outlined text-primary text-lg">${icon}</span>
                    </div>

                    <!-- Name + Status -->
                    <div class="flex-1 min-w-0">
                        <div class="flex items-center gap-2">
                            <p class="text-sm font-bold text-slate-900 dark:text-white truncate">${esc(nombre)}</p>
                            <span class="px-1.5 py-0.5 rounded text-[9px] font-black uppercase ${cfg.activo ? 'bg-green-500/10 text-green-500' : 'bg-slate-200 text-slate-400'}">
                                ${cfg.activo ? 'Activo' : 'Inactivo'}
                            </span>
                        </div>
                        ${cfg.descripcion ? `<p class="text-[11px] text-slate-400 truncate">${esc(cfg.descripcion)}</p>` : ''}
                    </div>

                    <!-- Recargo -->
                    <div class="flex flex-col items-center gap-1 px-3">
                        <label class="flex items-center gap-2 cursor-pointer">
                            <input type="checkbox" data-idx="${idx}" data-field="tieneRecargo" ${cfg.tieneRecargo ? 'checked' : ''}
                                   class="rounded border-slate-300 text-primary focus:ring-primary chk-recargo" />
                            <span class="text-[10px] font-bold text-slate-500 uppercase">Recargo</span>
                        </label>
                        <div class="flex items-center gap-1">
                            <input type="number" step="0.01" min="0" max="100"
                                   value="${cfg.porcentajeRecargo || 0}"
                                   data-idx="${idx}" data-field="porcentajeRecargo"
                                   class="w-16 text-center text-xs bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded py-0.5 input-recargo ${cfg.tieneRecargo ? '' : 'opacity-50'}"
                                   ${cfg.tieneRecargo ? '' : 'disabled'} />
                            <span class="text-xs text-slate-400">%</span>
                        </div>
                    </div>

                    <!-- Descuento -->
                    <div class="flex flex-col items-center gap-1 px-3">
                        <label class="flex items-center gap-2 cursor-pointer">
                            <input type="checkbox" data-idx="${idx}" data-field="permiteDescuento" ${cfg.permiteDescuento ? 'checked' : ''}
                                   class="rounded border-slate-300 text-primary focus:ring-primary chk-descuento" />
                            <span class="text-[10px] font-bold text-slate-500 uppercase">Descuento</span>
                        </label>
                        <div class="flex items-center gap-1">
                            <input type="number" step="0.01" min="0" max="100"
                                   value="${cfg.porcentajeDescuentoMaximo || 0}"
                                   data-idx="${idx}" data-field="porcentajeDescuentoMaximo"
                                   class="w-16 text-center text-xs bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded py-0.5 input-descuento ${cfg.permiteDescuento ? '' : 'opacity-50'}"
                                   ${cfg.permiteDescuento ? '' : 'disabled'} />
                            <span class="text-xs text-slate-400">% máx</span>
                        </div>
                    </div>
                </div>
                ${tarjetaHTML}
            `;

            listaEl.appendChild(card);
        });

        listaEl.classList.remove('hidden');
        bindInputEvents();
    }

    function bindInputEvents() {
        // Recargo checkbox toggle
        listaEl.querySelectorAll('.chk-recargo').forEach(chk => {
            chk.addEventListener('change', function () {
                const idx = +this.dataset.idx;
                configuraciones[idx].tieneRecargo = this.checked;
                const input = listaEl.querySelector(`input[data-idx="${idx}"][data-field="porcentajeRecargo"]`);
                if (input) {
                    input.disabled = !this.checked;
                    input.classList.toggle('opacity-50', !this.checked);
                    if (!this.checked) { input.value = '0'; configuraciones[idx].porcentajeRecargo = 0; }
                }
            });
        });

        // Descuento checkbox toggle
        listaEl.querySelectorAll('.chk-descuento').forEach(chk => {
            chk.addEventListener('change', function () {
                const idx = +this.dataset.idx;
                configuraciones[idx].permiteDescuento = this.checked;
                const input = listaEl.querySelector(`input[data-idx="${idx}"][data-field="porcentajeDescuentoMaximo"]`);
                if (input) {
                    input.disabled = !this.checked;
                    input.classList.toggle('opacity-50', !this.checked);
                    if (!this.checked) { input.value = '0'; configuraciones[idx].porcentajeDescuentoMaximo = 0; }
                }
            });
        });

        // Numeric inputs for recargo/descuento
        listaEl.querySelectorAll('.input-recargo').forEach(inp => {
            inp.addEventListener('change', function () {
                const idx = +this.dataset.idx;
                configuraciones[idx].porcentajeRecargo = parseFloat(this.value) || 0;
            });
        });
        listaEl.querySelectorAll('.input-descuento').forEach(inp => {
            inp.addEventListener('change', function () {
                const idx = +this.dataset.idx;
                configuraciones[idx].porcentajeDescuentoMaximo = parseFloat(this.value) || 0;
            });
        });

        // Tarjeta inputs
        listaEl.querySelectorAll('.input-tarjeta').forEach(inp => {
            inp.addEventListener('change', function () {
                const ci = +this.dataset.cfg;
                const ti = +this.dataset.tarjeta;
                const field = this.dataset.field;
                const tarjeta = configuraciones[ci].configuracionesTarjeta[ti];
                if (!tarjeta) return;

                if (field === 'tieneRecargoDebito') {
                    tarjeta.tieneRecargoDebito = this.checked;
                    const pctInput = listaEl.querySelector(`input[data-cfg="${ci}"][data-tarjeta="${ti}"][data-field="porcentajeRecargoDebito"]`);
                    if (pctInput) {
                        pctInput.disabled = !this.checked;
                        pctInput.classList.toggle('opacity-50', !this.checked);
                    }
                } else if (field === 'tipoCuota') {
                    tarjeta.tipoCuota = parseInt(this.value);
                    // Re-render to show/hide tasa input
                    renderConfiguraciones();
                } else if (field === 'cantidadMaximaCuotas') {
                    tarjeta.cantidadMaximaCuotas = parseInt(this.value) || 1;
                } else if (field === 'tasaInteresesMensual') {
                    tarjeta.tasaInteresesMensual = parseFloat(this.value) || 0;
                } else if (field === 'porcentajeRecargoDebito') {
                    tarjeta.porcentajeRecargoDebito = parseFloat(this.value) || 0;
                }
            });
        });
    }

    // ── Save ──
    btnSave.addEventListener('click', async () => {
        btnSave.disabled = true;
        btnSave.innerHTML = '<div class="size-4 border-2 border-white border-t-transparent rounded-full animate-spin"></div> Guardando…';

        try {
            const resp = await fetch('/ConfiguracionPago/GuardarConfiguracionesModal', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(configuraciones)
            });
            const json = await resp.json();
            if (!json.success) throw new Error(json.message || 'Error al guardar');

            closeModal();
            showToast('success', json.message || 'Configuraciones guardadas');
        } catch (err) {
            showToast('error', err.message);
        } finally {
            btnSave.disabled = false;
            btnSave.innerHTML = '<span class="material-symbols-outlined text-sm">save</span> Guardar Cambios';
        }
    });

    // ── Helpers ──
    function esc(str) {
        const d = document.createElement('div');
        d.textContent = str || '';
        return d.innerHTML;
    }

    function showToast(type, msg) {
        const colors = { success: 'bg-emerald-600', error: 'bg-red-600', warning: 'bg-amber-500' };
        const icons = { success: 'check_circle', error: 'error', warning: 'warning' };
        const toast = document.createElement('div');
        toast.className = `fixed bottom-6 right-6 z-[100] flex items-center gap-3 px-5 py-3 rounded-lg ${colors[type] || colors.success} text-white shadow-xl text-sm font-semibold`;
        toast.innerHTML = `<span class="material-symbols-outlined text-lg">${icons[type] || 'info'}</span> ${esc(msg)}`;
        document.body.appendChild(toast);
        setTimeout(() => {
            toast.style.transition = 'opacity 0.5s';
            toast.style.opacity = '0';
            setTimeout(() => toast.remove(), 500);
        }, 4000);
    }

})();
