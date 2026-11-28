/** producto-comision-modal.js — comisión del vendedor por producto, desde la tabla del catálogo. */
(function () {
    'use strict';

    var dialogo = document.getElementById('modal-comision-vendedor');
    if (!dialogo) return;

    var form = document.getElementById('form-comision-vendedor');
    var campo = document.getElementById('comision-modal-porcentaje');
    var error = document.getElementById('comision-modal-error');
    var guardar = document.getElementById('btn-guardar-comision');
    var panel = window.InvUI.panelFormulario(dialogo, { cierres: '[data-comision-modal-close]' });
    var DOS_DECIMALES = { minimumFractionDigits: 2, maximumFractionDigits: 2 };

    function mostrarError(mensaje) {
        error.textContent = mensaje || '';
        error.hidden = !mensaje;
        if (mensaje) {
            campo.setAttribute('aria-invalid', 'true');
            campo.setAttribute('aria-describedby', 'comision-modal-ayuda comision-modal-error');
            campo.focus();
        } else {
            campo.removeAttribute('aria-invalid');
            campo.setAttribute('aria-describedby', 'comision-modal-ayuda');
        }
    }

    function actualizarFila(productoId, valor) {
        var fila = document.querySelector('tr[data-catalogo-producto-id="' + productoId + '"]');
        if (!fila) return;
        var texto = valor.toLocaleString('es-AR', DOS_DECIMALES) + '%';
        var celda = fila.querySelector('[data-producto-comision]');
        if (celda) celda.textContent = texto;
        var boton = fila.querySelector('[data-comision-producto-id]');
        if (boton) {
            boton.setAttribute('data-comision-porcentaje', valor);
            boton.setAttribute('aria-label', 'Editar comisión de ' + boton.getAttribute('data-comision-producto-nombre') +
                '. Comisión actual: ' + valor.toLocaleString('es-AR', DOS_DECIMALES) + ' por ciento');
        }
    }

    // Delegado: las filas se reemplazan al buscar, ordenar o paginar sin recargar.
    document.addEventListener('click', function (e) {
        var boton = e.target.closest('[data-comision-producto-id]');
        if (!boton) return;
        form.reset();
        mostrarError('');
        guardar.disabled = false;
        document.getElementById('comision-modal-nombre').textContent = boton.getAttribute('data-comision-producto-nombre');
        document.getElementById('comision-modal-productoId').value = boton.getAttribute('data-comision-producto-id');
        campo.value = boton.getAttribute('data-comision-porcentaje');
        panel.abrir();
        campo.select();
    });

    campo.addEventListener('input', function () { mostrarError(''); });

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        mostrarError('');
        if (!campo.value || !campo.validity.valid) {
            mostrarError('El porcentaje debe estar entre 0 y 100, con hasta dos decimales.');
            return;
        }

        var productoId = document.getElementById('comision-modal-productoId').value;
        var etiqueta = guardar.textContent;
        guardar.disabled = true;
        guardar.textContent = 'Guardando…';
        try {
            var respuesta = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!respuesta.ok) {
                mostrarError('Error de servidor (' + respuesta.status + '). Intentá nuevamente.');
            } else {
                var datos = await respuesta.json();
                if (datos.success) {
                    actualizarFila(productoId, datos.comisionPorcentaje);
                    panel.cerrar();
                    document.dispatchEvent(new CustomEvent('catalogo:toast', { detail: { message: datos.message, type: 'success' } }));
                } else {
                    mostrarError(datos.message || 'No se pudo guardar la comisión.');
                }
            }
        } catch (_) {
            mostrarError('Error de comunicación. Intentá nuevamente.');
        }
        guardar.disabled = false;
        guardar.textContent = etiqueta;
    });
}());
