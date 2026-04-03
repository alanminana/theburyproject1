document.addEventListener('DOMContentLoaded', () => {
    const codigoInput = document.querySelector('[data-caja-form-codigo]');
    const nombreInput = document.querySelector('[data-caja-form-nombre]');
    const sucursalInput = document.querySelector('[data-caja-form-sucursal]');
    const ubicacionInput = document.querySelector('[data-caja-form-ubicacion]');
    const activaInput = document.querySelector('[data-caja-form-activa]');

    const codePreview = document.querySelector('[data-caja-preview-code]');
    const namePreview = document.querySelector('[data-caja-preview-name]');
    const statusPreview = document.querySelector('[data-caja-preview-status]');
    const statusCopyPreview = document.querySelector('[data-caja-preview-status-copy]');
    const statusCard = document.querySelector('[data-caja-preview-status-card]');
    const locationPreview = document.querySelector('[data-caja-preview-location]');

    function readValue(input, fallback) {
        const value = input?.value?.trim();
        return value || fallback;
    }

    function updatePreview() {
        if (codePreview) {
            codePreview.textContent = readValue(codigoInput, 'Pendiente');
        }

        if (namePreview) {
            namePreview.textContent = readValue(nombreInput, 'Nueva terminal');
        }

        if (locationPreview) {
            const parts = [sucursalInput?.value?.trim(), ubicacionInput?.value?.trim()].filter(Boolean);
            locationPreview.textContent = parts.length > 0 ? parts.join(' • ') : 'Sin definir';
        }

        if (statusPreview && statusCopyPreview && activaInput) {
            const activa = activaInput.checked;
            statusPreview.textContent = activa ? 'Activa' : 'Inactiva';
            statusPreview.className = `mt-2 text-2xl font-black ${activa ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400'}`;

            statusCopyPreview.textContent = activa
                ? 'Quedará disponible para aperturas y operaciones.'
                : 'Se mantendrá fuera del flujo operativo hasta volver a activarla.';
            statusCopyPreview.className = `mt-1 text-xs ${activa ? 'text-emerald-600/80 dark:text-emerald-400/80' : 'text-rose-600/80 dark:text-rose-400/80'}`;

            if (statusCard) {
                statusCard.className = `rounded-2xl border p-4 shadow-sm ${activa ? 'border-emerald-500/20 bg-emerald-500/10' : 'border-rose-500/20 bg-rose-500/10'}`;
            }
        }
    }

    [codigoInput, nombreInput, sucursalInput, ubicacionInput].forEach(input => {
        input?.addEventListener('input', updatePreview);
    });

    activaInput?.addEventListener('change', updatePreview);

    updatePreview();
    TheBury.autoDismissToasts();
});
