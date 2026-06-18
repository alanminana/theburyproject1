// Picker de categorías de Mercado Libre para el borrador de publicación.
// Consume online (sin árbol persistido): predictor por título, navegado del árbol
// y resolución de la categoría elegida (hoja + listing_allowed) contra el backend.
(function () {
    "use strict";

    var root = document.getElementById("ml-categoria-picker");
    if (!root) return;

    var urls = {
        sugerencias: root.dataset.urlSugerencias,
        hijos: root.dataset.urlHijos,
        resolver: root.dataset.urlResolver
    };

    var tituloInput = document.getElementById("Titulo");

    var hidden = {
        id: document.getElementById("CategoryIdMl"),
        nombre: document.getElementById("CategoryNombre"),
        path: document.getElementById("CategoryPathFromRoot"),
        hoja: document.getElementById("CategoryEsHoja")
    };

    var el = {
        seleccion: root.querySelector("[data-rol='seleccion']"),
        estado: root.querySelector("[data-rol='estado']"),
        resultados: root.querySelector("[data-rol='resultados']"),
        buscar: root.querySelector("[data-rol='buscar']"),
        btnSugerir: root.querySelector("[data-rol='sugerir']"),
        btnBuscar: root.querySelector("[data-rol='buscar-btn']"),
        btnArbol: root.querySelector("[data-rol='arbol']")
    };

    function setEstado(texto, tipo) {
        if (!el.estado) return;
        el.estado.textContent = texto || "";
        el.estado.className = "text-xs mt-1 " + (
            tipo === "error" ? "text-rose-400" :
            tipo === "ok" ? "text-emerald-400" :
            "text-slate-400");
    }

    function pintarSeleccion() {
        if (!el.seleccion) return;
        var id = hidden.id.value;
        if (!id) {
            el.seleccion.innerHTML = '<span class="text-slate-500">Sin categoría seleccionada.</span>';
            return;
        }
        var nombre = hidden.nombre.value || id;
        var path = hidden.path.value || "";
        el.seleccion.innerHTML =
            '<span class="font-semibold text-white">' + escapeHtml(nombre) + '</span> ' +
            '<span class="font-mono text-xs text-slate-400">(' + escapeHtml(id) + ')</span>' +
            (path ? '<div class="text-xs text-slate-400 mt-0.5">' + escapeHtml(path) + '</div>' : '');
    }

    function escapeHtml(s) {
        return String(s == null ? "" : s)
            .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
    }

    function limpiarResultados() {
        if (el.resultados) el.resultados.innerHTML = "";
    }

    async function getJson(url) {
        var resp = await fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" } });
        if (!resp.ok) throw new Error("HTTP " + resp.status);
        return resp.json();
    }

    // Resuelve una categoría y, si es hoja publicable, la fija en los hidden.
    async function seleccionarCategoria(categoryId) {
        setEstado("Resolviendo categoría…");
        try {
            var data = await getJson(urls.resolver + "?categoryId=" + encodeURIComponent(categoryId));
            if (!data.ok) { setEstado(data.error, "error"); return; }

            var c = data.categoria;
            if (!c.esHoja) {
                setEstado("« " + c.nombre + " » no es categoría hoja: elegí una más específica.", "error");
                // Permitir profundizar desde acá.
                cargarNivel(c.categoryId);
                return;
            }
            if (!c.listingAllowed) {
                setEstado("« " + c.nombre + " » no permite publicar (listing_allowed = false).", "error");
                return;
            }

            hidden.id.value = c.categoryId;
            hidden.nombre.value = c.nombre;
            hidden.path.value = c.path;
            hidden.hoja.value = "true";
            pintarSeleccion();
            limpiarResultados();
            setEstado("Categoría hoja seleccionada. Guardá los cambios para conservarla.", "ok");

            // Avisar al formulario dinámico de atributos que la categoría cambió.
            document.dispatchEvent(new CustomEvent("ml:categoria-cambiada", {
                detail: { categoryId: c.categoryId }
            }));
        } catch (e) {
            setEstado("No se pudo resolver la categoría.", "error");
        }
    }

    function filaResultado(item, opciones) {
        var li = document.createElement("li");
        li.className = "flex items-center justify-between gap-2 py-1.5 border-b border-slate-800";

        var info = document.createElement("div");
        info.className = "min-w-0";
        info.innerHTML =
            '<span class="text-sm text-slate-200">' + escapeHtml(item.nombre) + '</span> ' +
            '<span class="font-mono text-xs text-slate-500">' + escapeHtml(item.categoryId) + '</span>' +
            (item.dominio ? '<div class="text-xs text-slate-500">' + escapeHtml(item.dominio) + '</div>' : '');
        li.appendChild(info);

        var acciones = document.createElement("div");
        acciones.className = "flex gap-1 shrink-0";

        if (opciones.navegable) {
            var btnVer = document.createElement("button");
            btnVer.type = "button";
            btnVer.className = "btn-erp-ghost btn-sm text-xs";
            btnVer.textContent = "Abrir";
            btnVer.addEventListener("click", function () { cargarNivel(item.categoryId); });
            acciones.appendChild(btnVer);
        }

        var btnElegir = document.createElement("button");
        btnElegir.type = "button";
        btnElegir.className = "btn-erp-secondary btn-sm text-xs";
        btnElegir.textContent = "Elegir";
        btnElegir.addEventListener("click", function () { seleccionarCategoria(item.categoryId); });
        acciones.appendChild(btnElegir);

        li.appendChild(acciones);
        return li;
    }

    function pintarResultados(items, opciones) {
        limpiarResultados();
        if (!el.resultados) return;
        if (!items || !items.length) {
            el.resultados.innerHTML = '<li class="py-2 text-xs text-slate-500">Sin resultados.</li>';
            return;
        }
        items.forEach(function (it) { el.resultados.appendChild(filaResultado(it, opciones)); });
    }

    async function sugerirPorTitulo() {
        var q = (tituloInput && tituloInput.value || "").trim();
        if (q.length < 2) { setEstado("Escribí un título para sugerir categorías.", "error"); return; }
        setEstado("Buscando sugerencias para « " + q + " »…");
        try {
            var data = await getJson(urls.sugerencias + "?titulo=" + encodeURIComponent(q));
            if (!data.ok) { setEstado(data.error, "error"); return; }
            pintarResultados(data.sugerencias, { navegable: false });
            setEstado(data.sugerencias.length + " sugerencia(s). « Elegir » para fijar la categoría hoja.", "ok");
        } catch (e) {
            setEstado("No se pudieron obtener sugerencias.", "error");
        }
    }

    async function buscarTexto() {
        var q = (el.buscar && el.buscar.value || "").trim();
        if (q.length < 2) { setEstado("Escribí al menos 2 caracteres para buscar.", "error"); return; }
        setEstado("Buscando « " + q + " »…");
        try {
            var data = await getJson(urls.sugerencias + "?titulo=" + encodeURIComponent(q));
            if (!data.ok) { setEstado(data.error, "error"); return; }
            pintarResultados(data.sugerencias, { navegable: false });
            setEstado(data.sugerencias.length + " resultado(s).", "ok");
        } catch (e) {
            setEstado("No se pudo buscar.", "error");
        }
    }

    // Navega el árbol (raíz si categoryId es null). Requiere cuenta ML conectada.
    async function cargarNivel(categoryId) {
        setEstado(categoryId ? "Abriendo categoría…" : "Cargando categorías raíz…");
        try {
            var url = urls.hijos + (categoryId ? "?categoryId=" + encodeURIComponent(categoryId) : "");
            var data = await getJson(url);
            if (!data.ok) { setEstado(data.error, "error"); return; }

            var nivel = data.nivel;
            if (nivel.esHoja) {
                setEstado("« " + (nivel.nombre || categoryId) + " » es hoja. « Elegir » para fijarla.", "ok");
                pintarResultados([{ categoryId: nivel.categoryId, nombre: nivel.nombre, dominio: nivel.path }], { navegable: false });
                return;
            }
            pintarResultados(nivel.hijos, { navegable: true });
            setEstado((nivel.path ? nivel.path + " — " : "") + nivel.hijos.length + " subcategoría(s).", "ok");
        } catch (e) {
            setEstado("No se pudo navegar el árbol (¿hay una cuenta ML conectada?).", "error");
        }
    }

    if (el.btnSugerir) el.btnSugerir.addEventListener("click", sugerirPorTitulo);
    if (el.btnBuscar) el.btnBuscar.addEventListener("click", buscarTexto);
    if (el.buscar) el.buscar.addEventListener("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); buscarTexto(); } });
    if (el.btnArbol) el.btnArbol.addEventListener("click", function () { cargarNivel(null); });

    pintarSeleccion();
})();
