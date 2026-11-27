/*
 * inventario-ui.js — comportamiento compartido de las pantallas de Inventario.
 *
 * InvUI.confirmar(opciones) -> Promise<{ ok: boolean, motivo: string }>
 *   titulo, descripcion?, datos?: [[etiqueta, valor]], elementos?: string[] (lo afectado; se muestran hasta 5),
 *   consecuencias?: string[], avisos?: string[], motivo?: { etiqueta, requerido?, valor?, ayuda? },
 *   etiquetaConfirmar, tono?: 'peligro' | 'primario'
 *
 * Formularios declarativos: <form data-inv-confirm='{ ...mismas opciones... }'>
 *   - datos admite { "etiqueta": "Vigencia", "campo": "fechaVigencia", "vacio": "Al confirmar" }: toma el valor al enviar.
 *   - motivo.nombre: el texto escrito en la confirmación se envía con ese nombre de campo.
 * Corre después de la validación HTML (el evento submit no se dispara si el formulario es inválido).
 */
(function () {
    'use strict';

    var MAX_ELEMENTOS = 5;
    var secuencia = 0;

    function crear(tag, clase, texto) {
        var el = document.createElement(tag);
        if (clase) el.className = clase;
        if (texto !== undefined && texto !== null) el.textContent = String(texto);
        return el;
    }

    function lista(clase, items) {
        var ul = crear('ul', clase);
        items.forEach(function (item) { ul.appendChild(crear('li', '', item)); });
        return ul;
    }

    function confirmar(opciones) {
        var o = opciones || {};
        var id = 'inv-confirm-' + (++secuencia);
        // El HTML invalida la devolución de foco si el disparador se quitó del DOM: se guarda como respaldo.
        var disparador = document.activeElement;

        var dialogo = crear('dialog', 'inv-dialog inv-dialog--confirm');
        dialogo.setAttribute('aria-labelledby', id + '-titulo');
        var formulario = crear('form', 'inv-dialog__panel');
        formulario.method = 'dialog';
        formulario.noValidate = true;

        var titulo = crear('h1', 'inv-dialog__title', o.titulo || 'Confirmar');
        titulo.id = id + '-titulo';
        var cabecera = crear('header', 'inv-dialog__header');
        cabecera.appendChild(titulo);
        formulario.appendChild(cabecera);

        var cuerpo = crear('div', 'inv-dialog__body');
        var descripciones = [];
        if (o.descripcion) {
            var p = crear('p', 'inv-dialog__lead', o.descripcion);
            p.id = id + '-desc';
            descripciones.push(p.id);
            cuerpo.appendChild(p);
        }

        if (o.datos && o.datos.length) {
            var dl = crear('dl', 'inv-datos');
            o.datos.forEach(function (par) {
                var fila = crear('div', 'inv-datos__fila');
                fila.appendChild(crear('dt', '', par[0]));
                fila.appendChild(crear('dd', '', par[1]));
                dl.appendChild(fila);
            });
            cuerpo.appendChild(dl);
        }

        if (o.elementos && o.elementos.length) {
            var visibles = o.elementos.slice(0, MAX_ELEMENTOS);
            if (o.elementos.length > MAX_ELEMENTOS) visibles.push('… y ' + (o.elementos.length - MAX_ELEMENTOS) + ' más');
            cuerpo.appendChild(lista('inv-dialog__elementos', visibles));
        }

        (o.avisos || []).filter(Boolean).forEach(function (aviso) {
            cuerpo.appendChild(crear('p', 'inv-aviso inv-aviso--atencion', aviso));
        });

        var consecuencias = (o.consecuencias || []).filter(Boolean);
        if (consecuencias.length) {
            var bloque = lista('inv-dialog__consecuencias', consecuencias);
            bloque.id = id + '-consecuencias';
            descripciones.push(bloque.id);
            cuerpo.appendChild(bloque);
        }

        var campoMotivo = null;
        var errorMotivo = null;
        if (o.motivo) {
            var envoltorio = crear('div', 'inv-campo');
            var etiqueta = crear('label', 'inv-campo__etiqueta', o.motivo.etiqueta || 'Motivo');
            etiqueta.htmlFor = id + '-motivo';
            campoMotivo = crear('textarea', 'inv-control');
            campoMotivo.id = id + '-motivo';
            campoMotivo.rows = 2;
            campoMotivo.maxLength = o.motivo.maximo || 500;
            campoMotivo.value = o.motivo.valor || '';
            if (o.motivo.requerido) {
                campoMotivo.required = true;
                etiqueta.appendChild(crear('span', 'inv-campo__requerido', ' (obligatorio)'));
            }
            errorMotivo = crear('p', 'inv-campo__error');
            errorMotivo.id = id + '-motivo-error';
            errorMotivo.hidden = true;
            envoltorio.appendChild(etiqueta);
            envoltorio.appendChild(campoMotivo);
            envoltorio.appendChild(errorMotivo);
            cuerpo.appendChild(envoltorio);
            campoMotivo.addEventListener('input', function () {
                if (campoMotivo.value.trim()) {
                    errorMotivo.hidden = true;
                    campoMotivo.removeAttribute('aria-invalid');
                    campoMotivo.removeAttribute('aria-describedby');
                }
            });
        }
        formulario.appendChild(cuerpo);

        var pie = crear('footer', 'inv-dialog__footer');
        var cancelar = crear('button', 'inv-btn inv-btn--ghost', o.etiquetaCancelar || 'Cancelar');
        cancelar.type = 'submit';
        cancelar.value = 'cancelar';
        cancelar.formNoValidate = true;
        // La opción segura recibe el foco: Enter repetido no confirma una operación sensible.
        cancelar.autofocus = true;
        var aceptar = crear('button', 'inv-btn ' + (o.tono === 'primario' ? 'inv-btn--primary' : 'inv-btn--danger'),
            o.etiquetaConfirmar || 'Confirmar');
        aceptar.type = 'submit';
        aceptar.value = 'confirmar';
        pie.appendChild(cancelar);
        pie.appendChild(aceptar);
        formulario.appendChild(pie);
        if (descripciones.length) dialogo.setAttribute('aria-describedby', descripciones.join(' '));
        dialogo.appendChild(formulario);
        document.body.appendChild(dialogo);

        formulario.addEventListener('submit', function (e) {
            if (!e.submitter || e.submitter.value !== 'confirmar') return;
            if (campoMotivo && o.motivo.requerido && !campoMotivo.value.trim()) {
                e.preventDefault();
                errorMotivo.textContent = 'Escribí el motivo para continuar.';
                errorMotivo.hidden = false;
                campoMotivo.setAttribute('aria-invalid', 'true');
                campoMotivo.setAttribute('aria-describedby', errorMotivo.id);
                campoMotivo.focus();
            }
        });

        return new Promise(function (resolver) {
            dialogo.addEventListener('close', function () {
                var ok = dialogo.returnValue === 'confirmar';
                var motivo = campoMotivo ? campoMotivo.value.trim() : '';
                dialogo.remove();
                if (!ok && disparador && disparador.isConnected && document.activeElement === document.body) disparador.focus();
                resolver({ ok: ok, motivo: motivo });
            });
            dialogo.showModal();
        });
    }

    function valorDeCampo(formulario, nombre) {
        var campo = formulario.elements.namedItem(nombre);
        if (!campo) return '';
        if (campo.tagName === 'SELECT') {
            var opcion = campo.options[campo.selectedIndex];
            return campo.value && opcion ? opcion.text.trim() : '';
        }
        if (campo.type === 'datetime-local' && campo.value) return campo.value.replace(/^(\d{4})-(\d\d)-(\d\d)T/, '$3/$2/$1 ');
        return (campo.value || '').trim();
    }

    function resolverDatos(formulario, datos) {
        return (datos || []).map(function (d) {
            if (Array.isArray(d)) return d;
            return [d.etiqueta, valorDeCampo(formulario, d.campo) || d.vacio || '—'];
        });
    }

    document.addEventListener('submit', function (e) {
        var formulario = e.target;
        if (!(formulario instanceof HTMLFormElement) || !formulario.hasAttribute('data-inv-confirm')) return;
        if (formulario.dataset.invConfirmado === 'true') { delete formulario.dataset.invConfirmado; return; }
        // Si la validación no intrusiva va a frenar el envío, no tiene sentido pedir confirmación todavía.
        if (window.jQuery && window.jQuery.data(formulario, 'validator') && !window.jQuery(formulario).valid()) return;

        var opciones;
        try { opciones = JSON.parse(formulario.getAttribute('data-inv-confirm')); } catch (err) { opciones = {}; }
        e.preventDefault();
        e.stopImmediatePropagation();
        var enviador = e.submitter || null;
        var copia = Object.assign({}, opciones, { datos: resolverDatos(formulario, opciones.datos) });
        confirmar(copia).then(function (r) {
            if (!r.ok) return;
            if (opciones.motivo && opciones.motivo.nombre) {
                var oculto = formulario.querySelector('input[data-inv-motivo]');
                if (!oculto) {
                    oculto = crear('input');
                    oculto.type = 'hidden';
                    oculto.setAttribute('data-inv-motivo', '');
                    formulario.appendChild(oculto);
                }
                oculto.name = opciones.motivo.nombre;
                oculto.value = r.motivo;
            }
            formulario.dataset.invConfirmado = 'true';
            formulario.requestSubmit(enviador || undefined);
        });
    }, true);

    /* Las capas propias anteriores escuchan Escape/Tab en document: con un <dialog> nativo encima no deben reaccionar. */
    function hayDialogoNativoAbierto() {
        return !!document.querySelector('dialog:modal');
    }

    window.InvUI = Object.assign(window.InvUI || {}, {
        confirmar: confirmar,
        hayDialogoNativoAbierto: hayDialogoNativoAbierto
    });
})();
