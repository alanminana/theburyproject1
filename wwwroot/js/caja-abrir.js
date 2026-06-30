document.addEventListener('DOMContentLoaded', () => {
    const selectCaja = document.querySelector('[data-caja-abrir-select]');
    const montoInput = document.querySelector('[data-caja-abrir-monto]');
    const terminalLabel = document.querySelector('[data-caja-abrir-terminal-label]');
    const terminalCopy = document.querySelector('[data-caja-abrir-terminal-copy]');
    const fondoLabel = document.querySelector('[data-caja-abrir-fondo-label]');

    function updateCajaPreview() {
        if (!selectCaja || !terminalLabel || !terminalCopy) {
            return;
        }

        const selectedOption = selectCaja.options[selectCaja.selectedIndex];
        const selectedText = selectedOption?.text?.trim() || '';
        const isPlaceholder = !selectCaja.value;

        terminalLabel.textContent = isPlaceholder ? 'Seleccionar terminal' : selectedText;
        terminalCopy.textContent = isPlaceholder
            ? 'Sin código cargado'
            : 'Terminal lista para iniciar un nuevo turno operativo.';
    }

    function updateMontoPreview() {
        if (!montoInput || !fondoLabel) {
            return;
        }

        const value = parseFloat(montoInput.value);
        fondoLabel.textContent = TheBury.formatCurrency(Number.isFinite(value) ? value : 0);
    }

    const ultimoCierreUrl = selectCaja?.dataset.cajaUltimoCierreUrl;

    async function aplicarUltimoCierreComoFondo() {
        if (!selectCaja || !montoInput || !ultimoCierreUrl || !selectCaja.value) {
            return;
        }

        try {
            const resp = await fetch(`${ultimoCierreUrl}?cajaId=${encodeURIComponent(selectCaja.value)}`, {
                headers: { 'Accept': 'application/json' }
            });
            if (!resp.ok) {
                return;
            }

            const data = await resp.json();
            const monto = Number(data?.monto);
            if (Number.isFinite(monto)) {
                montoInput.value = monto;
                montoInput.dispatchEvent(new Event('input'));
            }
        } catch {
            // Si falla la consulta, el usuario carga el fondo manualmente.
        }
    }

    selectCaja?.addEventListener('change', () => {
        updateCajaPreview();
        aplicarUltimoCierreComoFondo();
    });
    montoInput?.addEventListener('input', updateMontoPreview);

    updateCajaPreview();
    updateMontoPreview();
    TheBury.autoDismissToasts();
});
