// Dispara el remarcado animado (ver validation-motion.css) cuando un envio falla la
// validacion cliente o cuando el servidor devolvio la pagina con campos invalidos.
// Solo anima y enfoca: no valida ni cambia reglas.
(function () {
    function nudge(el) {
        // Un input de archivo suele estar invisible sobre su zona de arrastre: se anima la zona.
        if (el.type === 'file' && el.parentElement) { el = el.parentElement; }
        el.classList.remove('is-nudge');
        void el.offsetWidth; // reflow: permite repetir la animacion en cada intento
        el.classList.add('is-nudge');
    }

    function invalidos(root) {
        return Array.prototype.filter.call(
            root.querySelectorAll('.input-validation-error, [aria-invalid="true"]'),
            function (el) { return (el.offsetParent !== null || el.type === 'file') && !el.disabled && el.type !== 'hidden'; });
    }

    // jquery-validate ya enfoca el primer campo invalido; aca solo se anima.
    function remarcar(root) {
        invalidos(root).forEach(nudge);
    }

    // "invalid-form" de jquery-validate se emite con triggerHandler (no burbujea), asi que se
    // observa el submit en fase de burbuja: corre despues de los handlers del form, y si
    // alguno lo cancelo (validacion cliente) se remarcan los campos invalidos.
    document.addEventListener('submit', function (e) {
        if (!e.defaultPrevented) { return; }
        var form = e.target;
        requestAnimationFrame(function () { remarcar(form); });
    });

    // Pagina devuelta por el servidor con errores de modelo.
    document.addEventListener('DOMContentLoaded', function () {
        invalidos(document)
            .filter(function (el) { return el.classList.contains('input-validation-error'); })
            .forEach(nudge);
    });
})();
