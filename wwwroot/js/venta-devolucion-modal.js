(function () {
    'use strict';

    const modal = document.getElementById('modal-devolucion-venta');
    if (!modal) return;

    const form = document.getElementById('form-devolucion-venta');
    const loading = document.getElementById('devolucion-modal-loading');
    const body = document.getElementById('devolucion-modal-body');
    const errorBox = document.getElementById('devolucion-modal-error');
    const itemsBody = document.getElementById('devolucion-items-body');
    const submitBtn = document.getElementById('btn-submit-devolucion');
    const resolucionSelect = document.getElementById('devolucion-tipo-resolucion');
    const cajaCheckbox = document.getElementById('devolucion-registrar-caja');
    const helperBox = document.getElementById('devolucion-resolucion-helper');
    const descripcionInput = document.getElementById('devolucion-descripcion');
    const totalSeleccionado = document.getElementById('devolucion-total-seleccionado');
    const inputVentaId = document.getElementById('devolucion-venta-id');
    const inputClienteId = document.getElementById('devolucion-cliente-id');

    const summaryNumero = document.getElementById('devolucion-numero-venta');
    const summaryTipoPago = document.getElementById('devolucion-tipo-pago');
    const summaryDias = document.getElementById('devolucion-dias-venta');
    const summaryCliente = document.getElementById('devolucion-cliente-nombre');
    const summaryFecha = document.getElementById('devolucion-fecha-venta');
    const summaryTotal = document.getElementById('devolucion-total-venta');

    const contextUrl = modal.dataset.contextUrl;
    const submitUrl = modal.dataset.submitUrl;
    const estadosProducto = JSON.parse(modal.dataset.estadosProducto || '[]');
    const currency = new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS' });

    let context = null;

    function showError(message) {
        errorBox.textContent = message;
        errorBox.classList.remove('hidden');
    }

    function clearError() {
        errorBox.textContent = '';
        errorBox.classList.add('hidden');
    }

    function openModal() {
        modal.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
    }

    function closeModal() {
        modal.classList.add('hidden');
        document.body.style.overflow = '';
        clearError();
        form.reset();
        itemsBody.innerHTML = '';
        body.classList.add('hidden');
        loading.classList.remove('hidden');
        context = null;
    }

    function setLoadingState(isLoading) {
        loading.classList.toggle('hidden', !isLoading);
        body.classList.toggle('hidden', isLoading);
        submitBtn.disabled = isLoading;
        submitBtn.classList.toggle('opacity-60', isLoading);
        submitBtn.classList.toggle('cursor-not-allowed', isLoading);
    }

    function buildEstadoOptions(selectedValue) {
        return estadosProducto
            .map(option => `<option value="${option.value}" ${String(option.value) === String(selectedValue) ? 'selected' : ''}>${option.label}</option>`)
            .join('');
    }

    function renderItems(items) {
        itemsBody.innerHTML = items.map((item, index) => `
            <tr class="hover:bg-slate-50 dark:hover:bg-slate-900/40 transition-colors" data-index="${index}" data-precio="${item.precioUnitario}">
                <td class="px-4 py-3 align-top">
                    <input type="hidden" name="Items[${index}].ProductoId" value="${item.productoId}" />
                    <input type="hidden" name="Items[${index}].ProductoNombre" value="${escapeHtml(item.productoNombre)}" />
                    <input type="hidden" name="Items[${index}].ProductoCodigo" value="${escapeHtml(item.productoCodigo || '')}" />
                    <input type="hidden" name="Items[${index}].CantidadDisponible" value="${item.cantidadDisponible}" />
                    <input type="hidden" name="Items[${index}].PrecioUnitario" value="${item.precioUnitario}" />
                    <input type="hidden" name="Items[${index}].Seleccionado" value="false" />
                    <label class="inline-flex items-center gap-2 text-sm font-semibold text-slate-700 dark:text-slate-200">
                        <input type="checkbox"
                               class="devolucion-item-check size-4 rounded border-slate-300 text-primary focus:ring-primary"
                               name="Items[${index}].Seleccionado"
                               value="true" />
                        Incluir
                    </label>
                </td>
                <td class="px-4 py-3">
                    <p class="font-bold text-slate-900 dark:text-white">${escapeHtml(item.productoNombre)}</p>
                    <p class="text-xs text-slate-400">${escapeHtml(item.productoCodigo || 'Sin código')}</p>
                </td>
                <td class="px-4 py-3 text-center font-bold text-slate-700 dark:text-slate-200">${item.cantidadDisponible}</td>
                <td class="px-4 py-3 text-right font-semibold text-slate-700 dark:text-slate-200">${item.precioUnitarioDisplay}</td>
                <td class="px-4 py-3">
                    <input type="number"
                           min="0"
                           max="${item.cantidadDisponible}"
                           value="0"
                           name="Items[${index}].CantidadDevolver"
                           class="devolucion-item-cantidad w-24 rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:cursor-not-allowed disabled:bg-slate-100 dark:border-slate-700 dark:bg-slate-950 dark:text-white dark:disabled:bg-slate-800"
                           disabled />
                </td>
                <td class="px-4 py-3">
                    <select name="Items[${index}].EstadoProducto"
                            class="devolucion-item-estado w-full rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:cursor-not-allowed disabled:bg-slate-100 dark:border-slate-700 dark:bg-slate-950 dark:text-white dark:disabled:bg-slate-800"
                            disabled>
                        ${buildEstadoOptions(0)}
                    </select>
                </td>
            </tr>
        `).join('');

        itemsBody.querySelectorAll('.devolucion-item-check').forEach(check => {
            check.addEventListener('change', onItemToggle);
        });

        itemsBody.querySelectorAll('.devolucion-item-cantidad').forEach(input => {
            input.addEventListener('input', updateSelectedTotal);
        });
    }

    function onItemToggle(event) {
        const row = event.target.closest('tr');
        if (!row) return;

        const cantidadInput = row.querySelector('.devolucion-item-cantidad');
        const estadoSelect = row.querySelector('.devolucion-item-estado');
        const max = Number(cantidadInput.max || 0);

        cantidadInput.disabled = !event.target.checked;
        estadoSelect.disabled = !event.target.checked;

        if (event.target.checked) {
            cantidadInput.value = max > 0 ? '1' : '0';
            estadoSelect.value = '0';
        } else {
            cantidadInput.value = '0';
        }

        updateSelectedTotal();
    }

    function updateSelectedTotal() {
        let total = 0;

        itemsBody.querySelectorAll('tr').forEach(row => {
            const checked = row.querySelector('.devolucion-item-check')?.checked;
            const cantidad = Number(row.querySelector('.devolucion-item-cantidad')?.value || 0);
            const precio = Number(row.dataset.precio || 0);

            if (checked && cantidad > 0) {
                total += cantidad * precio;
            }
        });

        totalSeleccionado.textContent = `Total seleccionado: ${currency.format(total)}`;
    }

    function updateResolucionHelper() {
        const tipoResolucion = Number(resolucionSelect.value || 0);
        const permiteImpactoCaja = Boolean(context?.permiteImpactoCaja);

        if (tipoResolucion === 1) {
            helperBox.textContent = permiteImpactoCaja
                ? 'El reembolso puede generar un egreso en caja cuando completes la devolución.'
                : 'El reembolso no impactará caja automáticamente para esta venta.';
            cajaCheckbox.disabled = !permiteImpactoCaja;
            if (!permiteImpactoCaja) {
                cajaCheckbox.checked = false;
            }
            return;
        }

        cajaCheckbox.checked = false;
        cajaCheckbox.disabled = true;

        if (tipoResolucion === 2) {
            helperBox.textContent = 'Cambio por el mismo producto: ingresa el devuelto y la reposición se resuelve por el circuito operativo.';
        } else if (tipoResolucion === 3) {
            helperBox.textContent = 'Cambio por otro producto: la devolución queda registrada, pero la salida del reemplazo debe resolverse manualmente.';
        } else {
            helperBox.textContent = 'Nota de crédito: se genera crédito a favor del cliente al aprobar la devolución.';
        }
    }

    function fillSummary(venta) {
        inputVentaId.value = venta.id;
        inputClienteId.value = venta.clienteId;
        summaryNumero.textContent = venta.numero;
        summaryTipoPago.textContent = venta.tipoPagoDisplay;
        summaryDias.textContent = `${venta.diasDesdeVenta} días`;
        summaryCliente.textContent = venta.clienteNombre;
        summaryFecha.textContent = venta.fechaVenta;
        summaryTotal.textContent = venta.totalDisplay;
    }

    async function loadContext(ventaId) {
        clearError();
        openModal();
        setLoadingState(true);

        try {
            const resp = await fetch(`${contextUrl}?ventaId=${encodeURIComponent(ventaId)}`, {
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            const json = await resp.json();
            if (!resp.ok || !json.success) {
                throw new Error(json.message || 'No se pudo cargar la venta.');
            }

            context = json.venta;
            fillSummary(context);
            renderItems(context.items || []);
            descripcionInput.value = '';
            resolucionSelect.value = '0';
            cajaCheckbox.checked = false;
            cajaCheckbox.disabled = true;
            updateSelectedTotal();
            updateResolucionHelper();
            setLoadingState(false);

            if (!context.puedeDevolver) {
                showError('La venta está fuera del plazo permitido para devoluciones o ya no admite este circuito.');
                submitBtn.disabled = true;
                submitBtn.classList.add('opacity-60', 'cursor-not-allowed');
            }
        } catch (error) {
            showError(error.message || 'No se pudo cargar la devolución.');
            setLoadingState(false);
        }
    }

    async function submitForm(event) {
        event.preventDefault();
        clearError();

        const selectedCount = itemsBody.querySelectorAll('.devolucion-item-check:checked').length;
        if (selectedCount === 0) {
            showError('Seleccioná al menos un producto para devolver.');
            return;
        }

        if ((descripcionInput.value || '').trim().length < 20) {
            showError('La descripción debe tener al menos 20 caracteres.');
            descripcionInput.focus();
            return;
        }

        submitBtn.disabled = true;
        submitBtn.classList.add('opacity-60', 'cursor-not-allowed');

        try {
            const formData = new FormData(form);
            const resp = await fetch(submitUrl, {
                method: 'POST',
                body: formData,
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            const json = await resp.json();
            if (!resp.ok || !json.success) {
                throw new Error(json.message || 'No se pudo crear la devolución.');
            }

            closeModal();
            showToast(json.message || 'Devolución creada correctamente.');
        } catch (error) {
            showError(error.message || 'No se pudo crear la devolución.');
        } finally {
            submitBtn.disabled = false;
            submitBtn.classList.remove('opacity-60', 'cursor-not-allowed');
        }
    }

    function showToast(message) {
        const toast = document.createElement('div');
        toast.className = 'fixed bottom-6 right-6 z-[70] flex items-center gap-3 rounded-xl bg-emerald-600 px-5 py-3 text-sm font-bold text-white shadow-2xl';
        toast.innerHTML = '<span class="material-symbols-outlined text-lg">check_circle</span><span></span>';
        toast.querySelector('span:last-child').textContent = message;
        document.body.appendChild(toast);

        setTimeout(() => {
            toast.style.transition = 'opacity 0.4s ease, transform 0.4s ease';
            toast.style.opacity = '0';
            toast.style.transform = 'translateY(10px)';
            setTimeout(() => toast.remove(), 450);
        }, 3500);
    }

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    document.querySelectorAll('[data-open-devolucion-modal]').forEach(button => {
        button.addEventListener('click', () => {
            const ventaId = button.dataset.ventaId;
            if (!ventaId) return;
            loadContext(ventaId);
        });
    });

    document.querySelectorAll('[data-close-devolucion-modal]').forEach(button => {
        button.addEventListener('click', closeModal);
    });

    resolucionSelect?.addEventListener('change', updateResolucionHelper);
    form?.addEventListener('submit', submitForm);
})();
