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

    function setTipo(tipoValue) {
        inputTipo.value = tipoValue;

        tipoBtns.forEach(btn => {
            const isActive = btn.dataset.tipo === tipoValue;
            btn.classList.toggle('bg-white', isActive && tipoValue === '0');
            btn.classList.toggle('dark:bg-primary', isActive);
            btn.classList.toggle('text-primary', isActive && tipoValue === '0');
            btn.classList.toggle('bg-rose-500', isActive && tipoValue === '1');
            btn.classList.toggle('dark:text-white', isActive);
            btn.classList.toggle('text-white', isActive && tipoValue === '1');
            btn.classList.toggle('shadow-sm', isActive);
            btn.classList.toggle('font-bold', isActive);
            btn.classList.toggle('text-slate-500', !isActive);
            btn.classList.toggle('dark:text-slate-400', !isActive);
            btn.classList.toggle('font-medium', !isActive);
        });

        populateConceptos(tipoValue);
    }

    function populateConceptos(tipoValue) {
        const conceptos = tipoValue === '0' ? CONCEPTOS_INGRESO : CONCEPTOS_EGRESO;
        const currentVal = selectConcepto.value;

        selectConcepto.innerHTML = '';

        const groupLabel = tipoValue === '0' ? 'Ingresos' : 'Egresos';
        const optgroup = document.createElement('optgroup');
        optgroup.label = groupLabel;
        optgroup.className = 'bg-slate-50 dark:bg-background-dark';

        conceptos.forEach(c => {
            const opt = document.createElement('option');
            opt.value = c.value;
            opt.textContent = c.text;
            if (c.value === currentVal) opt.selected = true;
            optgroup.appendChild(opt);
        });

        selectConcepto.appendChild(optgroup);
    }

    tipoBtns.forEach(btn => {
        btn.addEventListener('click', () => setTipo(btn.dataset.tipo));
    });

    // Initialize with current model value or default to Ingreso
    const initialTipo = inputTipo.value || '0';
    setTipo(initialTipo);

    // If model already has a concepto value, restore it after populating
    const modelConcepto = selectConcepto.dataset.initialValue;
    if (modelConcepto) {
        selectConcepto.value = modelConcepto;
    }

    // Toast auto-dismiss
    const toast = document.getElementById('toast-error');
    if (toast) {
        setTimeout(() => {
            toast.style.transition = 'opacity 0.5s';
            toast.style.opacity = '0';
            setTimeout(() => toast.remove(), 500);
        }, 5000);
    }
});
