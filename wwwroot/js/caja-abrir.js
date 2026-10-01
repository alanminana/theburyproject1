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

    // El fondo del último cierre es solo un valor por defecto: si el usuario ya cargó
    // un monto (tipeado o con los atajos), elegir/cambiar la caja no debe pisarlo.
    let montoEditadoPorUsuario = false;
    let aplicandoDefault = false;

    const ultimoCierrePanel = document.querySelector('[data-caja-ultimo-cierre-panel]');

    function mostrarUltimoCierre(cierre) {
        if (!ultimoCierrePanel) {
            return;
        }

        if (!cierre) {
            ultimoCierrePanel.classList.add('hidden');
            return;
        }

        const dif = Number(cierre.diferencia) || 0;
        const difTexto = dif === 0 ? 'sin diferencia' : `diferencia ${TheBury.formatCurrency(dif)}`;
        ultimoCierrePanel.querySelector('[data-ultimo-cierre-resumen]').textContent =
            `${cierre.fecha} · cerró ${cierre.usuario} · esperado ${TheBury.formatCurrency(Number(cierre.esperado) || 0)}, ` +
            `contado ${TheBury.formatCurrency(Number(cierre.contado) || 0)} (${difTexto})`;
        ultimoCierrePanel.querySelector('[data-ultimo-cierre-link]').href = cierre.detalleUrl || '#';
        ultimoCierrePanel.classList.remove('hidden');
    }

    async function aplicarUltimoCierreComoFondo(soloPanel = false) {
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
            mostrarUltimoCierre(data?.ultimoCierre);
            const monto = Number(data?.monto);
            if (!soloPanel && Number.isFinite(monto) && !montoEditadoPorUsuario) {
                aplicandoDefault = true;
                montoInput.value = monto;
                montoInput.dispatchEvent(new Event('input'));
                aplicandoDefault = false;
            }
        } catch {
            // Si falla la consulta, el usuario carga el fondo manualmente.
        }
    }

    selectCaja?.addEventListener('change', () => {
        updateCajaPreview();
        aplicarUltimoCierreComoFondo();
    });
    montoInput?.addEventListener('input', () => {
        if (!aplicandoDefault) {
            montoEditadoPorUsuario = true;
        }
        updateMontoPreview();
    });

    updateCajaPreview();
    updateMontoPreview();
    TheBury.autoDismissToasts();
});
