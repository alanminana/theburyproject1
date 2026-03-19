// details-venta.js — Venta Details page interactions
(function () {
    'use strict';

    // ── Toast auto-dismiss ──
    document.querySelectorAll('.toast-msg').forEach(el => {
        setTimeout(() => {
            el.style.transition = 'opacity 0.5s ease, transform 0.5s ease';
            el.style.opacity = '0';
            el.style.transform = 'translateY(-8px)';
            setTimeout(() => el.remove(), 500);
        }, 5000);
    });

    // ── Confirm Venta form ──
    const formConfirmar = document.getElementById('form-confirmar');
    if (formConfirmar) {
        formConfirmar.addEventListener('submit', function (e) {
            if (!confirm('¿Está seguro de confirmar esta venta? Esta acción no se puede deshacer.')) {
                e.preventDefault();
            }
        });
    }

    // ── Anular Factura button ──
    const btnAnular = document.getElementById('btn-anular-factura');
    if (btnAnular) {
        btnAnular.addEventListener('click', function () {
            const facturaId = this.dataset.facturaId;
            const motivo = prompt('Ingrese el motivo de anulación:');
            if (motivo && motivo.trim()) {
                const form = document.createElement('form');
                form.method = 'POST';
                form.action = `/Venta/AnularFactura`;

                const tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');

                const fields = {
                    facturaId: facturaId,
                    motivo: motivo.trim()
                };

                if (tokenEl) {
                    fields['__RequestVerificationToken'] = tokenEl.value;
                }

                for (const [key, value] of Object.entries(fields)) {
                    const input = document.createElement('input');
                    input.type = 'hidden';
                    input.name = key;
                    input.value = value;
                    form.appendChild(input);
                }

                document.body.appendChild(form);
                form.submit();
            }
        });
    }

})();
