document.addEventListener('DOMContentLoaded', () => {
    const CONCEPTOS_INGRESO = [
        { value: '0', text: 'Venta efectivo' },
        { value: '1', text: 'Venta tarjeta' },
        { value: '2', text: 'Venta cheque' },
        { value: '3', text: 'Cobro cuota' },
        { value: '4', text: 'Cancelación crédito' },
        { value: '5', text: 'Anticipo crédito' }
    ];

    const CONCEPTOS_EGRESO = [
        { value: '10', text: 'Gasto operativo' },
        { value: '11', text: 'Extracción efectivo' },
        { value: '12', text: 'Depósito efectivo' },
        { value: '20', text: 'Devolución cliente' },
        { value: '30', text: 'Ajuste caja' },
        { value: '99', text: 'Otro' }
    ];

    const inputTipo = document.getElementById('input-tipo');
    const selectConcepto = document.getElementById('select-concepto');
    const tipoBtns = document.querySelectorAll('.tipo-btn');
    const tipoHelpEl = document.getElementById('tipo-help');
    const tipoCardLabelEl = document.getElementById('tipo-card-label');
    const tipoCardCopyEl = document.getElementById('tipo-card-copy');
    const tipoPanelBadgeEl = document.getElementById('tipo-panel-badge');
    const tipoPanelTitleEl = document.getElementById('tipo-panel-title');
    const tipoPanelCopyEl = document.getElementById('tipo-panel-copy');
    const conceptosIngresoSet = new Set(CONCEPTOS_INGRESO.map(concepto => concepto.value));
    const conceptosEgresoSet = new Set(CONCEPTOS_EGRESO.map(concepto => concepto.value));
    let initialConcepto = selectConcepto?.dataset.initialValue || selectConcepto?.value || '';

    function normalizeTipo(rawTipo) {
        const tipo = String(rawTipo || '').trim().toLowerCase();

        if (tipo === '1' || tipo === 'egreso') {
            return '1';
        }

        if (tipo === '0' || tipo === 'ingreso') {
            return '0';
        }

        if (conceptosEgresoSet.has(tipo)) {
            return '1';
        }

        if (conceptosIngresoSet.has(tipo)) {
            return '0';
        }

        return '0';
    }

    function updateTipoUI(tipoValue) {
        const esEgreso = tipoValue === '1';

        tipoBtns.forEach(btn => {
            const isActive = btn.dataset.tipo === tipoValue;
            btn.setAttribute('aria-pressed', isActive ? 'true' : 'false');
        });

        if (tipoHelpEl) {
            tipoHelpEl.textContent = esEgreso
                ? 'Los egresos descuentan saldo de la caja y requieren mayor claridad en la justificacion.'
                : 'Los ingresos suman saldo a la caja y deben quedar correctamente documentados.';
        }

        if (tipoCardLabelEl) {
            tipoCardLabelEl.textContent = esEgreso ? 'Egreso' : 'Ingreso';
            tipoCardLabelEl.className = `mt-2 text-2xl font-black ${esEgreso ? 'text-rose-600 dark:text-rose-400' : 'text-emerald-600 dark:text-emerald-400'}`;
        }

        if (tipoCardCopyEl) {
            tipoCardCopyEl.textContent = esEgreso
                ? 'La operacion reducira el saldo disponible de la caja.'
                : 'La operacion incrementara el saldo disponible de la caja.';
            tipoCardCopyEl.className = `mt-1 text-xs ${esEgreso ? 'text-rose-600/80 dark:text-rose-400/80' : 'text-emerald-600/80 dark:text-emerald-400/80'}`;
        }

        if (tipoPanelBadgeEl) {
            tipoPanelBadgeEl.textContent = esEgreso ? 'Egreso' : 'Ingreso';
            tipoPanelBadgeEl.className = `inline-flex items-center rounded-full border px-3 py-1 text-xs font-bold uppercase tracking-wide ${esEgreso ? 'border-rose-500/20 bg-rose-500/10 text-rose-600 dark:text-rose-400' : 'border-emerald-500/20 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'}`;
        }

        if (tipoPanelTitleEl) {
            tipoPanelTitleEl.textContent = esEgreso ? 'Salida de fondos' : 'Entrada de fondos';
        }

        if (tipoPanelCopyEl) {
            tipoPanelCopyEl.textContent = esEgreso
                ? 'Usalo para gastos, extracciones, depositos o ajustes que reduzcan la disponibilidad.'
                : 'Usalo para ventas cobradas, recuperos o movimientos que incrementen la disponibilidad.';
        }
    }

    function setTipo(tipoValue) {
        const normalizedTipo = normalizeTipo(tipoValue);

        inputTipo.value = normalizedTipo === '1' ? 'Egreso' : 'Ingreso';
        updateTipoUI(normalizedTipo);
        populateConceptos(normalizedTipo);
    }

    function populateConceptos(tipoValue) {
        const conceptos = tipoValue === '0' ? CONCEPTOS_INGRESO : CONCEPTOS_EGRESO;
        const currentVal = selectConcepto.value;
        const preferredValue = initialConcepto || currentVal;

        selectConcepto.innerHTML = '';

        const groupLabel = tipoValue === '0' ? 'Ingresos' : 'Egresos';
        const optgroup = document.createElement('optgroup');
        optgroup.label = groupLabel;
        optgroup.className = 'bg-slate-50 dark:bg-background-dark';

        conceptos.forEach(c => {
            const opt = document.createElement('option');
            opt.value = c.value;
            opt.textContent = c.text;
            if (c.value === preferredValue) {
                opt.selected = true;
            }
            optgroup.appendChild(opt);
        });

        selectConcepto.appendChild(optgroup);

        if (!selectConcepto.value && conceptos.length > 0) {
            selectConcepto.value = conceptos[0].value;
        }

        initialConcepto = '';
    }

    tipoBtns.forEach(btn => {
        btn.addEventListener('click', () => setTipo(btn.dataset.tipo));
    });

    const initialTipo = normalizeTipo(inputTipo.value || initialConcepto || '0');
    setTipo(initialTipo);

    TheBury.autoDismissToasts();
});
