/*
 * inventario-productos.js — sección Productos de Inventario.
 * Resultados (búsqueda, filtros, orden y paginado sin recarga; sin JS funcionan como formulario y enlaces),
 * selección que persiste entre páginas, menú de fila, destacado y baja.
 * La fila se renderiza solo en el servidor (_ProductoFila): después de guardar se vuelve a pedir la región.
 */
(function () {
    'use strict';

    var form = document.getElementById('filterForm');
    if (!form) return;

    var buscador = document.getElementById('catalogo-search-input');
    var seleccion = new Set();
    var pedido = null;
    var temporizador = 0;

    function resultados() { return document.getElementById('productos-resultados'); }
    function casillas() { return Array.prototype.slice.call(document.querySelectorAll('#productos-tbody .chk-producto')); }
    function toast(mensaje, tipo) {
        document.dispatchEvent(new CustomEvent('catalogo:toast', { detail: { message: mensaje, type: tipo || 'success' } }));
    }

    // ── Resultados ─────────────────────────────────────────────

    function urlDelFormulario() {
        var url = new URL(form.action, window.location.origin);
        new FormData(form).forEach(function (valor, clave) {
            if (String(valor).trim() !== '') url.searchParams.set(clave, String(valor).trim());
        });
        var tab = new URLSearchParams(window.location.search).get('tab');
        if (tab) url.searchParams.set('tab', tab);
        return url;
    }

    /** Vuelve a pedir la región de resultados de <paramref url>. historial: 'push' | 'replace' | null. */
    function cargar(url, historial) {
        if (pedido) pedido.abort();
        pedido = new AbortController();
        var actual = resultados();
        actual.setAttribute('aria-busy', 'true');

        var parcial = new URL(url, window.location.origin);
        parcial.searchParams.set('parcial', 'true');

        return fetch(parcial, { signal: pedido.signal, headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (respuesta) {
                if (!respuesta.ok || respuesta.redirected) throw new Error('respuesta ' + respuesta.status);
                return respuesta.text();
            })
            .then(function (html) {
                var documento = new DOMParser().parseFromString(html, 'text/html');
                var nuevo = documento.getElementById('productos-resultados');
                if (!nuevo) throw new Error('respuesta sin resultados');
                resultados().replaceWith(nuevo);
                if (historial === 'push') window.history.pushState(null, '', url);
                else if (historial === 'replace') window.history.replaceState(null, '', url);
                pintarSeleccion();
                document.dispatchEvent(new CustomEvent('inventario:resultados'));
            })
            .catch(function (error) {
                if (error.name === 'AbortError') return;
                var region = resultados();
                region.removeAttribute('aria-busy');
                var aviso = region.querySelector('[data-error-carga]');
                if (!aviso) {
                    aviso = document.createElement('p');
                    aviso.className = 'inv-aviso inv-aviso--error';
                    aviso.setAttribute('role', 'alert');
                    aviso.setAttribute('data-error-carga', '');
                    region.prepend(aviso);
                }
                aviso.textContent = 'No se pudo actualizar el listado. Lo que ves puede estar desactualizado. ';
                var reintentar = document.createElement('button');
                reintentar.type = 'button';
                reintentar.className = 'inv-link';
                reintentar.textContent = 'Reintentar';
                reintentar.addEventListener('click', function () { cargar(url, historial); });
                aviso.appendChild(reintentar);
            });
    }

    function refrescar(opciones) {
        var resaltar = opciones && opciones.resaltar;
        return cargar(window.location.href, null).then(function () {
            if (!resaltar) return;
            var fila = document.querySelector('tr[data-catalogo-producto-id="' + resaltar + '"]');
            if (fila) fila.classList.add('inv-row--resaltada');
        });
    }

    function buscar() { cargar(urlDelFormulario().toString(), 'replace'); }

    buscador.addEventListener('input', function () {
        window.clearTimeout(temporizador);
        temporizador = window.setTimeout(buscar, 300);
    });
    form.addEventListener('change', function (e) {
        if (e.target.tagName === 'SELECT') buscar();
    });
    form.addEventListener('submit', function (e) {
        e.preventDefault();
        window.clearTimeout(temporizador);
        buscar();
    });

    document.addEventListener('click', function (e) {
        var enlace = e.target.closest('[data-resultados-link]');
        if (!enlace || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey) return;
        e.preventDefault();
        var url = new URL(enlace.href);
        // El orden forma parte del formulario: la próxima búsqueda lo conserva.
        ['orderBy', 'orderDirection'].forEach(function (nombre) {
            var campo = form.elements.namedItem(nombre);
            campo.value = url.searchParams.get(nombre) || '';
            campo.disabled = !campo.value;
        });
        cargar(url.toString(), 'push').then(function () {
            var region = resultados();
            if (region.getBoundingClientRect().top < 0) region.scrollIntoView({ block: 'start' });
        });
    });

    window.addEventListener('popstate', function () { window.location.reload(); });

    // En pantallas anchas los filtros quedan a la vista; en angostas se pliegan.
    var plegado = form.querySelector('[data-filtros]');
    var ancho = window.matchMedia('(min-width: 900px)');
    function ajustarFiltros() { if (plegado) plegado.open = ancho.matches; }
    ancho.addEventListener('change', ajustarFiltros);
    ajustarFiltros();

    // ── Selección ──────────────────────────────────────────────

    var barra = document.getElementById('selection-bar');

    function ocultosSeleccionados() {
        var enPantalla = new Set(casillas().map(function (c) { return Number(c.value); }));
        var ocultos = 0;
        seleccion.forEach(function (id) { if (!enPantalla.has(id)) ocultos++; });
        return ocultos;
    }

    function pintarSeleccion() {
        var visibles = casillas();
        var marcadas = 0;
        visibles.forEach(function (casilla) {
            casilla.checked = seleccion.has(Number(casilla.value));
            casilla.closest('tr').classList.toggle('inv-row--seleccionada', casilla.checked);
            if (casilla.checked) marcadas++;
        });
        var todas = document.getElementById('chk-select-all');
        if (todas) {
            todas.disabled = visibles.length === 0;
            todas.checked = marcadas > 0 && marcadas === visibles.length;
            todas.indeterminate = marcadas > 0 && marcadas < visibles.length;
        }

        var total = seleccion.size;
        barra.hidden = total === 0;
        document.getElementById('selection-count').textContent = total;
        document.getElementById('selection-product-label').textContent = total === 1 ? 'producto seleccionado' : 'productos seleccionados';
        var enBoton = document.getElementById('selection-count-btn');
        if (enBoton) enBoton.textContent = total;

        var ocultos = ocultosSeleccionados();
        var nota = document.getElementById('selection-hidden-note');
        nota.hidden = ocultos === 0;
        nota.textContent = ocultos === 1
            ? '1 no está en esta página y también está seleccionado'
            : ocultos + ' no están en esta página y también están seleccionados';
    }

    var apiSeleccion = {
        getIds: function () { return Array.from(seleccion); },
        getCount: function () { return seleccion.size; },
        getHiddenCount: ocultosSeleccionados,
        clearAll: function () { seleccion.clear(); pintarSeleccion(); },
        refreshUi: pintarSeleccion
    };
    if (window.CatalogoModule) window.CatalogoModule.registerProductSelectionApi(apiSeleccion);

    document.addEventListener('change', function (e) {
        if (e.target.id === 'chk-select-all') {
            // Solo lo que se ve: un ajuste masivo nunca incluye filas que el usuario no tiene a la vista.
            casillas().forEach(function (casilla) {
                if (e.target.checked) seleccion.add(Number(casilla.value));
                else seleccion.delete(Number(casilla.value));
            });
            pintarSeleccion();
        } else if (e.target.classList.contains('chk-producto')) {
            if (e.target.checked) seleccion.add(Number(e.target.value));
            else seleccion.delete(Number(e.target.value));
            pintarSeleccion();
        }
    });

    document.addEventListener('click', function (e) {
        if (!e.target.closest('[data-seleccionar-resultados]')) return;
        (resultados().dataset.idsFiltrados || '').split(',').filter(Boolean).forEach(function (id) { seleccion.add(Number(id)); });
        pintarSeleccion();
    });

    // ── Menú de fila ───────────────────────────────────────────

    var menu = document.getElementById('catalogo-row-actions-menu');
    var disparador = null;

    function ubicarMenu() {
        if (!disparador || !disparador.isConnected) return;
        var r = disparador.getBoundingClientRect();
        var alto = menu.offsetHeight;
        var ancho = menu.offsetWidth;
        var arriba = r.bottom + 4 + alto > window.innerHeight ? Math.max(8, r.top - alto - 4) : r.bottom + 4;
        menu.style.top = arriba + 'px';
        menu.style.left = Math.min(Math.max(8, r.right - ancho), window.innerWidth - ancho - 8) + 'px';
    }

    function enlace(id, url) {
        var el = document.getElementById(id);
        if (!el) return;
        el.hidden = !url;
        el.setAttribute('href', url || '#');
    }

    function dato(id, atributos) {
        var el = document.getElementById(id);
        if (!el) return;
        Object.keys(atributos).forEach(function (k) { el.setAttribute(k, atributos[k]); });
    }

    // El click corre antes de la activación nativa de popovertarget: el menú se abre ya completo.
    document.addEventListener('click', function (e) {
        var boton = e.target.closest('[data-row-actions-open]');
        if (!boton) return;
        disparador = boton;
        var id = boton.getAttribute('data-producto-id');
        var nombre = boton.getAttribute('data-producto-nombre') || 'este producto';
        document.getElementById('catalogo-row-actions-title').textContent = nombre;
        // Las rutas las genera el servidor (Url.Action): armarlas aquí ya produjo un enlace roto.
        enlace('catalogo-menu-inventario', boton.getAttribute('data-inventario-url'));
        enlace('catalogo-menu-movimientos', boton.getAttribute('data-kardex-url'));
        enlace('catalogo-menu-precio-lista', boton.getAttribute('data-precio-lista-url'));
        enlace('catalogo-menu-compra', boton.getAttribute('data-compra-url'));
        dato('catalogo-menu-historial', { 'data-catalogo-producto-id': id });
        dato('catalogo-menu-costos', { 'data-movimientos-producto-id': id, 'data-movimientos-producto-nombre': nombre });
        dato('catalogo-menu-mercadolibre', { 'data-producto-id': id });
        dato('catalogo-menu-eliminar', { 'data-producto-id': id, 'data-producto-nombre': nombre, 'data-producto-codigo': boton.closest('tr').getAttribute('data-producto-codigo') || '' });
    });

    menu.addEventListener('toggle', function (e) {
        if (e.newState === 'open') ubicarMenu();
    });
    window.addEventListener('resize', function () { if (menu.matches(':popover-open')) ubicarMenu(); });
    window.addEventListener('scroll', function () { if (menu.matches(':popover-open')) ubicarMenu(); }, true);

    // Elegir una acción cierra el menú y devuelve el foco al disparador antes de abrir lo que siga.
    menu.addEventListener('click', function (e) {
        if (!e.target.closest('.inv-menu__item')) return;
        menu.hidePopover();
        if (disparador) disparador.focus({ preventScroll: true });
    }, true);

    var botonMl = document.getElementById('catalogo-menu-mercadolibre');
    if (botonMl) {
        botonMl.addEventListener('click', function () {
            document.getElementById('catalogo-mercadolibre-producto-id').value = botonMl.getAttribute('data-producto-id');
            document.getElementById('catalogo-mercadolibre-form').submit();
        });
    }

    var botonEliminar = document.getElementById('catalogo-menu-eliminar');
    if (botonEliminar) {
        botonEliminar.addEventListener('click', function () {
            var id = botonEliminar.getAttribute('data-producto-id');
            window.InvUI.confirmar({
                titulo: '¿Eliminar este producto?',
                datos: [
                    ['Producto', botonEliminar.getAttribute('data-producto-nombre')],
                    ['Código', botonEliminar.getAttribute('data-producto-codigo') || '—']
                ],
                consecuencias: [
                    'Deja de aparecer en Inventario y no se puede volver a usar en operaciones nuevas.',
                    'No se puede deshacer desde el sistema.'
                ],
                etiquetaConfirmar: 'Eliminar producto'
            }).then(function (r) {
                if (!r.ok) return;
                var formulario = document.getElementById('form-delete-produto');
                var volver = window.location.pathname + window.location.search;
                formulario.action = '/Producto/Delete/' + encodeURIComponent(id) + '?returnUrl=' + encodeURIComponent(volver);
                formulario.submit();
            });
        });
    }

    // ── Destacado ──────────────────────────────────────────────

    function pintarDestacado(boton, activo) {
        boton.setAttribute('data-es-destacado', activo ? 'true' : 'false');
        boton.setAttribute('aria-pressed', activo ? 'true' : 'false');
        boton.title = activo ? 'Quitar destacado' : 'Destacar';
    }

    document.addEventListener('click', function (e) {
        var boton = e.target.closest('.btn-star-destacado');
        if (!boton) return;
        var antes = boton.getAttribute('data-es-destacado') === 'true';
        pintarDestacado(boton, !antes);
        var token = document.querySelector('input[name="__RequestVerificationToken"]');
        fetch('/Catalogo/ToggleDestacado', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': token ? token.value : '' },
            body: 'productoId=' + encodeURIComponent(boton.getAttribute('data-producto-id'))
        })
            .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function (data) {
                if (!data.success) throw new Error();
                pintarDestacado(boton, data.esDestacado);
            })
            .catch(function () {
                pintarDestacado(boton, antes);
                toast('No se pudo cambiar el destacado.', 'error');
            });
    });

    window.InvProductos = { refrescar: refrescar };
    pintarSeleccion();
})();
