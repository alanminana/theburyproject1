document.addEventListener('DOMContentLoaded', () => {
    const inputTipo = document.getElementById('input-tipo');
    const selectConcepto = document.getElementById('select-concepto');
    const tipoBtns = document.querySelectorAll('.tipo-btn');
    const tipoHelpEl = document.getElementById('tipo-help');
    const montoEl = document.getElementById('Monto');
    const impactBaseEl = document.getElementById('impact-base');
    const impactDeltaEl = document.getElementById('impact-delta');
    const impactTotalEl = document.getElementById('impact-total');
    const impactLabelEl = document.getElementById('impact-label');
    const impactIconEl = document.getElementById('impact-icon');
    const submitBtnEl = document.getElementById('submit-btn');

    // Grupos de Concepto ya vienen server-renderizados en el <select> (data-concepto-group
    // "0"=Ingreso/"1"=Egreso en cada <optgroup>): se leen del DOM en vez de duplicar la lista
    // en JS, así el <select> sigue teniendo todas las opciones aunque este script no cargue.
    const conceptoGroups = selectConcepto
        ? Array.prototype.slice.call(selectConcepto.querySelectorAll('optgroup[data-concepto-group]'))
        : [];
    const conceptoSets = { '0': new Set(), '1': new Set() };
    conceptoGroups.forEach(group => {
        const key = group.dataset.conceptoGroup;
        if (!conceptoSets[key]) return;
        group.querySelectorAll('option').forEach(opt => conceptoSets[key].add(opt.value));
    });

    let initialConcepto = selectConcepto?.dataset.initialValue || selectConcepto?.value || '';
    let currentTipo = '0';

    function normalizeTipo(rawTipo) {
        const tipo = String(rawTipo || '').trim().toLowerCase();

        if (tipo === '1' || tipo === 'egreso') {
            return '1';
        }

        if (tipo === '0' || tipo === 'ingreso') {
            return '0';
        }

        if (conceptoSets['1'].has(tipo)) {
            return '1';
        }

        if (conceptoSets['0'].has(tipo)) {
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

        if (impactLabelEl) {
            impactLabelEl.textContent = esEgreso ? 'Este egreso' : 'Este ingreso';
        }

        if (impactIconEl) {
            impactIconEl.textContent = esEgreso ? 'trending_down' : 'trending_up';
            impactIconEl.style.color = esEgreso ? '#fb7185' : '#34d399';
        }

        if (submitBtnEl) {
            submitBtnEl.innerHTML = `<span class="material-symbols-outlined">check</span>Registrar ${esEgreso ? 'egreso' : 'ingreso'}`;
        }

        recalcImpacto();
    }

    function setTipo(tipoValue) {
        const normalizedTipo = normalizeTipo(tipoValue);

        currentTipo = normalizedTipo;
        inputTipo.value = normalizedTipo === '1' ? 'Egreso' : 'Ingreso';
        updateTipoUI(normalizedTipo);
        populateConceptos(normalizedTipo);
    }

    function formatCurrency(value) {
        if (window.TheBury && typeof TheBury.formatCurrency === 'function') {
            return TheBury.formatCurrency(value);
        }

        return '$ ' + value.toLocaleString('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function recalcImpacto() {
        if (!montoEl || !impactBaseEl || !impactDeltaEl || !impactTotalEl) {
            return;
        }

        const base = parseFloat(impactBaseEl.dataset.value) || 0;
        const monto = parseFloat(montoEl.value) || 0;
        const esEgreso = currentTipo === '1';
        const delta = esEgreso ? -monto : monto;
        const nuevoTotal = base + delta;

        impactDeltaEl.textContent = `${delta < 0 ? '-' : '+'} ${formatCurrency(Math.abs(delta))}`;
        impactDeltaEl.style.color = esEgreso ? '#fb7185' : '#34d399';
        impactTotalEl.textContent = formatCurrency(nuevoTotal);
    }

    function populateConceptos(tipoValue) {
        if (!selectConcepto) return;

        let activeGroup = null;
        conceptoGroups.forEach(group => {
            const isActive = group.dataset.conceptoGroup === tipoValue;
            group.hidden = !isActive;
            group.disabled = !isActive;
            if (isActive) activeGroup = group;
        });

        if (!activeGroup) return;

        const options = Array.prototype.slice.call(activeGroup.querySelectorAll('option'));
        const preferredValue = initialConcepto || selectConcepto.value;
        const hasPreferred = options.some(opt => opt.value === preferredValue);

        selectConcepto.value = hasPreferred ? preferredValue : (options[0] ? options[0].value : '');
        initialConcepto = '';
    }

    tipoBtns.forEach(btn => {
        btn.addEventListener('click', () => setTipo(btn.dataset.tipo));
    });

    if (montoEl) {
        montoEl.addEventListener('input', recalcImpacto);
    }

    const initialTipo = normalizeTipo(inputTipo.value || initialConcepto || '0');
    setTipo(initialTipo);

    TheBury.autoDismissToasts();
});
