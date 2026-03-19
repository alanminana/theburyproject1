/* cliente-form.js — Lógica para Edit_tw / Create_tw de Cliente */

/** Expande o colapsa una sección del formulario */
function toggleSection(name) {
    var content = document.getElementById('content-' + name);
    var chevron = document.getElementById('chevron-' + name);
    if (!content || !chevron) return;

    var isHidden = content.classList.contains('hidden');
    content.classList.toggle('hidden', !isHidden);
    chevron.classList.toggle('rotate-180', isHidden);
}

/** Validación cruzada: monto mínimo < monto máximo */
function validarMontos() {
    var minInput = document.getElementById('montoMinimo');
    var maxInput = document.getElementById('montoMaximo');
    var errorEl = document.getElementById('montoError');
    if (!minInput || !maxInput || !errorEl) return true;

    var min = parseFloat(minInput.value);
    var max = parseFloat(maxInput.value);

    if (!isNaN(min) && !isNaN(max) && min > max) {
        errorEl.classList.remove('hidden');
        maxInput.classList.add('border-red-500');
        return false;
    }

    errorEl.classList.add('hidden');
    maxInput.classList.remove('border-red-500');
    return true;
}

document.addEventListener('DOMContentLoaded', function () {
    var minInput = document.getElementById('montoMinimo');
    var maxInput = document.getElementById('montoMaximo');

    if (minInput) minInput.addEventListener('input', validarMontos);
    if (maxInput) maxInput.addEventListener('input', validarMontos);

    var form = document.getElementById('clienteForm');
    if (form) {
        form.addEventListener('submit', function (e) {
            if (!validarMontos()) {
                e.preventDefault();
                // Abrir la sección de crédito para mostrar el error
                var content = document.getElementById('content-credito');
                var chevron = document.getElementById('chevron-credito');
                if (content && content.classList.contains('hidden')) {
                    content.classList.remove('hidden');
                    if (chevron) chevron.classList.remove('rotate-180');
                }
            }
        });
    }
});
