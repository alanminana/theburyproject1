/* Ayuda contextual «¿Cómo funciona?». Una pantalla la ofrece incluyendo un <dialog data-erp-ayuda>; el botón del
   encabezado aparece solo en ese caso. Acá: abrir/cerrar, pestañas y ejemplos paso a paso. No lee ni guarda datos. */
(function () {
    'use strict';

    var dialogo = document.querySelector('dialog[data-erp-ayuda]');
    if (!dialogo) return;

    var pestanas = Array.prototype.slice.call(dialogo.querySelectorAll('[role="tab"]'));
    var cuerpo = dialogo.querySelector('.lp-ayuda__cuerpo');
    var abridor = null;

    var boton = document.getElementById('btn-ayuda');
    if (boton) {
        boton.setAttribute('aria-controls', dialogo.id);
        boton.closest('[data-ayuda-encabezado]').hidden = false;
    }

    function activar(pestana, enfocar) {
        pestanas.forEach(function (p) {
            var activa = p === pestana;
            p.setAttribute('aria-selected', activa ? 'true' : 'false');
            p.tabIndex = activa ? 0 : -1;
            document.getElementById(p.getAttribute('aria-controls')).hidden = !activa;
        });
        cuerpo.scrollTop = 0;
        if (enfocar) pestana.focus();
        pestana.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }

    function abrir(origen) {
        abridor = origen;
        if (!dialogo.open) dialogo.showModal();
        document.documentElement.classList.add('lp-sin-scroll');
        if (pestanas.length) activar(pestanas[0], true);
        // Cada módulo puede llevar la ayuda a un punto concreto (p. ej. un campo del formulario).
        dialogo.dispatchEvent(new CustomEvent('erp-ayuda:abierta', { detail: { origen: origen } }));
    }

    document.addEventListener('click', function (e) {
        var disparador = e.target.closest('[data-erp-ayuda-abrir]');
        if (disparador) abrir(disparador);
    });

    dialogo.addEventListener('click', function (e) {
        // Clic en el fondo oscuro (el propio <dialog>) o en el botón de cerrar.
        if (e.target === dialogo || e.target.closest('[data-erp-ayuda-cerrar]')) dialogo.close();
    });
    dialogo.addEventListener('close', function () {
        document.documentElement.classList.remove('lp-sin-scroll');
        if (abridor && document.contains(abridor)) abridor.focus();
    });

    pestanas.forEach(function (pestana, i) {
        pestana.addEventListener('click', function () { activar(pestana, false); });
        pestana.addEventListener('keydown', function (e) {
            var destino = { ArrowRight: i + 1, ArrowLeft: i - 1, Home: 0, End: pestanas.length - 1 }[e.key];
            if (destino === undefined) return;
            e.preventDefault();
            activar(pestanas[(destino + pestanas.length) % pestanas.length], true);
        });
    });

    // ── Ejemplo paso a paso (importes fijos escritos en la vista) ──
    dialogo.querySelectorAll('[data-ayuda-pasos]').forEach(function (bloque) {
        var pasos = Array.prototype.slice.call(bloque.querySelectorAll('[data-ayuda-paso]'));
        var anterior = bloque.querySelector('[data-ayuda-anterior]');
        var siguiente = bloque.querySelector('[data-ayuda-siguiente]');
        var contador = bloque.querySelector('[data-ayuda-contador]');
        var actual = 0;

        function mostrar(indice) {
            actual = Math.max(0, Math.min(pasos.length - 1, indice));
            pasos.forEach(function (paso, i) { paso.hidden = i !== actual; });
            contador.textContent = 'Paso ' + (actual + 1) + ' de ' + pasos.length;
            // aria-disabled (no disabled) para no perder el foco del teclado al llegar a un extremo.
            anterior.setAttribute('aria-disabled', actual === 0 ? 'true' : 'false');
            siguiente.setAttribute('aria-disabled', actual === pasos.length - 1 ? 'true' : 'false');
        }

        anterior.addEventListener('click', function () { mostrar(actual - 1); });
        siguiente.addEventListener('click', function () { mostrar(actual + 1); });
        mostrar(0);
    });
})();
