/**
 * catalogo-entidad-modal.js — alta, edición y baja de Categorías y Marcas (diálogo de _EntidadDialogo.cshtml).
 * Tras guardar o eliminar se recarga la sección: listado, contador y combos salen del servidor.
 */
(function () {
    'use strict';

    var ENTIDADES = {
        categoria: {
            nombre: 'categoría',
            controlador: '/Categoria',
            listado: '/Catalogo?tab=categorias',
            editar: 'data-cat-edit-id',
            eliminar: 'data-cat-delete-id',
            eliminarNombre: 'data-cat-delete-nombre',
            consecuencias: ['No se puede eliminar si tiene productos o subcategorías con stock.', 'No se puede deshacer desde el sistema.']
        },
        marca: {
            nombre: 'marca',
            controlador: '/Marca',
            listado: '/Catalogo?tab=marcas',
            editar: 'data-marca-edit-id',
            eliminar: 'data-marca-delete-id',
            eliminarNombre: 'data-marca-delete-nombre',
            consecuencias: ['Deja de poder asignarse a productos.', 'No se puede deshacer desde el sistema.']
        }
    };

    function avisar(mensaje) {
        document.dispatchEvent(new CustomEvent('catalogo:toast', { detail: { message: mensaje, type: 'error' } }));
    }

    function iniciar(dialogo) {
        var clave = dialogo.getAttribute('data-entidad');
        var cfg = ENTIDADES[clave];
        var form = dialogo.querySelector('form');
        var resumen = dialogo.querySelector('[data-entidad-resumen]');
        var titulo = dialogo.querySelector('[data-entidad-titulo]');
        var guardar = dialogo.querySelector('[data-entidad-guardar]');
        var padre = form.elements.namedItem('ParentId');
        var editando = false;

        var panel = window.InvUI.panelFormulario(dialogo, {
            cierres: '[data-entidad-cerrar]',
            alCerrar: limpiarErrores
        });

        /** Marca o limpia un campo. La ayuda queda siempre descrita; el error, solo mientras se muestra. */
        function marcar(campo, mensaje) {
            var error = form.querySelector('[data-valmsg-for="' + campo.name + '"]');
            if (!error) return;
            error.textContent = mensaje || '';
            error.hidden = !mensaje;
            var descritos = (campo.getAttribute('aria-describedby') || '').split(' ')
                .filter(function (id) { return id && id !== error.id; });
            if (mensaje) {
                descritos.push(error.id);
                campo.setAttribute('aria-invalid', 'true');
            } else {
                campo.removeAttribute('aria-invalid');
            }
            if (descritos.length) campo.setAttribute('aria-describedby', descritos.join(' '));
            else campo.removeAttribute('aria-describedby');
        }

        function limpiarErrores() {
            resumen.hidden = true;
            form.querySelectorAll('[aria-invalid]').forEach(function (campo) { marcar(campo, ''); });
        }

        /** errores: { Campo: [mensajes] }, igual que la respuesta del servidor. La clave vacía es general. */
        function mostrarErrores(errores) {
            var mensajes = [];
            var primero = null;
            Object.keys(errores).forEach(function (nombre) {
                mensajes = mensajes.concat(errores[nombre]);
                var campo = nombre && form.elements.namedItem(nombre);
                if (campo && campo.tagName) {
                    marcar(campo, errores[nombre][0]);
                    primero = primero || campo;
                }
            });
            if (!mensajes.length) return;
            resumen.textContent = mensajes.map(function (m) { return String(m).replace(/[.\s]+$/, ''); }).join('. ') + '.';
            resumen.hidden = false;
            if (primero) primero.focus();
        }

        function obligatoriosVacios() {
            var errores = {};
            form.querySelectorAll('[data-requerido]').forEach(function (campo) {
                if (!campo.value.trim()) errores[campo.name] = [campo.getAttribute('data-requerido')];
            });
            return errores;
        }

        /** datos: respuesta de GetJson para editar; null para un alta. */
        function preparar(datos) {
            form.reset();
            limpiarErrores();
            editando = !!datos;

            // En un alta, Id y RowVersion no se envían: un valor vacío haría fallar el enlace del modelo.
            ['Id', 'RowVersion'].forEach(function (nombre) {
                var oculto = form.elements.namedItem(nombre);
                oculto.disabled = !datos;
                oculto.value = '';
            });

            // Un registro no puede ser su propio padre. El servidor además rechaza cualquier ciclo.
            Array.prototype.forEach.call(padre.options, function (opcion) {
                opcion.disabled = opcion.hidden = editando && opcion.value === String(datos.id);
            });

            if (datos) {
                Array.prototype.forEach.call(form.elements, function (campo) {
                    if (!campo.name || (campo.type === 'hidden' && campo.name === 'Activo')) return;
                    var llave = campo.name.charAt(0).toLowerCase() + campo.name.slice(1);
                    if (!(llave in datos)) return;
                    if (campo.type === 'checkbox') campo.checked = !!datos[llave];
                    else campo.value = datos[llave] == null ? '' : datos[llave];
                });
            }

            titulo.textContent = (editando ? 'Editar ' : 'Nueva ') + cfg.nombre;
            guardar.textContent = editando ? 'Guardar cambios' : 'Guardar ' + cfg.nombre;
            guardar.disabled = false;
            form.action = cfg.controlador + (editando ? '/EditAjax/' + datos.id : '/CreateAjax');
        }

        function abrir(datos) {
            preparar(datos);
            panel.abrir();
        }

        form.addEventListener('input', function (e) {
            if (e.target.hasAttribute('aria-invalid')) marcar(e.target, '');
        });

        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            limpiarErrores();

            var vacios = obligatoriosVacios();
            if (Object.keys(vacios).length) { mostrarErrores(vacios); return; }

            var etiqueta = guardar.textContent;
            guardar.disabled = true;
            guardar.textContent = 'Guardando…';
            try {
                var respuesta = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });
                // Sin permiso o con la sesión vencida el servidor responde con una redirección o HTML, no JSON.
                if (!respuesta.ok || (respuesta.headers.get('content-type') || '').indexOf('json') === -1) {
                    mostrarErrores({ '': ['No se pudo guardar: no tenés permiso para ' + (editando ? 'editar' : 'crear') +
                        ' ' + cfg.nombre + 's o tu sesión venció. Recargá la página e intentá de nuevo'] });
                } else {
                    var resultado = await respuesta.json();
                    if (resultado.success) {
                        // El botón queda en "Guardando…" hasta la recarga: evita un doble envío.
                        window.location.assign(cfg.listado);
                        return;
                    }
                    mostrarErrores(resultado.errors || { '': ['No se pudo guardar. Intentá nuevamente'] });
                }
            } catch (_) {
                mostrarErrores({ '': ['Error de conexión. Intentá nuevamente'] });
            }
            guardar.disabled = false;
            guardar.textContent = etiqueta;
        });

        async function editar(id) {
            try {
                var respuesta = await fetch(cfg.controlador + '/GetJson/' + id, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (!respuesta.ok) throw new Error(String(respuesta.status));
                abrir(await respuesta.json());
            } catch (_) {
                avisar('No se pudo cargar la ' + cfg.nombre + '. Intentá nuevamente.');
            }
        }

        function eliminar(id, nombre) {
            window.InvUI.confirmar({
                titulo: '¿Eliminar esta ' + cfg.nombre + '?',
                datos: [[cfg.nombre.charAt(0).toUpperCase() + cfg.nombre.slice(1), nombre]],
                consecuencias: cfg.consecuencias,
                etiquetaConfirmar: 'Eliminar ' + cfg.nombre
            }).then(function (r) {
                if (!r.ok) return;
                var baja = document.getElementById('form-delete-' + clave);
                baja.action = cfg.controlador + '/Delete/' + id + '?returnUrl=' + encodeURIComponent(cfg.listado);
                baja.submit();
            });
        }

        document.addEventListener('click', function (e) {
            var boton = e.target.closest('[' + cfg.editar + '], [' + cfg.eliminar + ']');
            if (!boton) return;
            if (boton.hasAttribute(cfg.editar)) editar(boton.getAttribute(cfg.editar));
            else eliminar(boton.getAttribute(cfg.eliminar), boton.getAttribute(cfg.eliminarNombre) || '—');
        });

        // catalogo-index.js abre el alta desde [data-catalogo-modal-open].
        window.CatalogoModule.registerModalApi(clave, { open: function () { abrir(null); }, close: panel.cerrar });
    }

    document.querySelectorAll('dialog[data-entidad]').forEach(iniciar);
})();
