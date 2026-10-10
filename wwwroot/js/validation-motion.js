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

    // Convenciones de "campo invalido" del sistema (ver validation-motion.css). has-error marca
    // la pestaña de un wizard con errores; border-red-500 tambien lo usan las zonas de arrastre.
    var SEL = '.input-validation-error, [aria-invalid="true"], .prov-invalid, .border-red-500, .has-error';

    function invalidos(root) {
        return Array.prototype.filter.call(
            root.querySelectorAll(SEL),
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

    function esInvalido(el) {
        return el.matches(SEL);
    }

    // Registro de campos ya marcados: el nudge se dispara solo en la transicion a invalido,
    // y el propio cambio de clase is-nudge no vuelve a disparar nada.
    var marcados = new WeakSet();

    function revisar(el) {
        if (el.nodeType !== 1) { return; }
        if (!esInvalido(el)) { marcados.delete(el); return; }
        if (marcados.has(el)) { return; }
        marcados.add(el);
        if ((el.offsetParent !== null || el.type === 'file') && !el.disabled && el.type !== 'hidden') {
            nudge(el);
        }
    }

    // Wizards y validaciones con JS propio (Cliente, modales, etc.) marcan el campo sin enviar
    // el formulario: se observa el cambio de clase / aria-invalid en todo el documento.
    document.addEventListener('DOMContentLoaded', function () {
        // Validacion nativa del navegador (required / pattern sin novalidate): el evento "invalid"
    // no burbujea, se captura en la fase de captura del documento.
    document.addEventListener('invalid', function (e) { nudge(e.target); }, true);

    // Pagina devuelta por el servidor con errores de modelo.
        invalidos(document).forEach(function (el) { marcados.add(el); });
        invalidos(document).forEach(nudge);

        new MutationObserver(function (muts) {
            muts.forEach(function (m) {
                revisar(m.target);
                if (m.type === 'childList') {
                    m.addedNodes.forEach(function (n) {
                        if (n.nodeType === 1) { revisar(n); n.querySelectorAll && n.querySelectorAll(SEL).forEach(revisar); }
                    });
                }
            });
        }).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'aria-invalid'] });
    });
})();
