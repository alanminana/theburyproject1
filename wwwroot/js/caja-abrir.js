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

    selectCaja?.addEventListener('change', updateCajaPreview);
    montoInput?.addEventListener('input', updateMontoPreview);

    updateCajaPreview();
    updateMontoPreview();
    TheBury.autoDismissToasts();
});
