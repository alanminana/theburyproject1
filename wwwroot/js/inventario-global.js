/* ============================================================
   Inventario global — experiencia integrada (Unidades + Movimientos)
   Progressive enhancement sobre /Producto/UnidadesGlobal.
   Sin JS: se ve la tabla de unidades + link a /MovimientoStock/Index.
   Con JS y movimientos.view: pestañas; "Movimientos" carga inline desde el endpoint existente
   /MovimientoStock/ListJson (no toca backend). Ruta sin cambios.
   ============================================================ */
(function () {
  "use strict";

  var root = document.querySelector("[data-invg-root]");
  if (!root) return;
  var barra = root.querySelector("[data-invg-tablist]");
  if (barra) barra.hidden = false;
  // Con JS la pestaña "Movimientos" reemplaza al enlace de respaldo del encabezado.
  Array.prototype.forEach.call(root.querySelectorAll("[data-invg-nojs]"), function (el) { el.hidden = Boolean(barra); });

  var tabs = Array.prototype.slice.call(root.querySelectorAll("[data-invg-tab]"));
  var panels = Array.prototype.slice.call(root.querySelectorAll("[data-invg-panel]"));
  var movLoaded = false;

  function show(name) {
    tabs.forEach(function (t) {
      var on = t.getAttribute("data-invg-tab") === name;
      t.setAttribute("aria-selected", String(on));
      t.tabIndex = on ? 0 : -1;
    });
    panels.forEach(function (p) { p.hidden = p.getAttribute("data-invg-panel") !== name; });
    if (name === "movimientos" && !movLoaded) loadMov();
  }
  tabs.forEach(function (t, i) {
    t.addEventListener("click", function () { show(t.getAttribute("data-invg-tab")); });
    t.addEventListener("keydown", function (e) {
      if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") return;
      var next = tabs[(i + (e.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length];
      next.focus();
      show(next.getAttribute("data-invg-tab"));
    });
  });

  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) {
      return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c];
    });
  }

  function aviso(texto, error) {
    return '<tr><td colspan="6" class="inv-empty' + (error ? " inv-text-danger" : "") + '">' + esc(texto) + "</td></tr>";
  }

  function loadMov() {
    movLoaded = true;
    var body = document.getElementById("invg-mov-body");
    var summary = document.getElementById("invg-mov-summary");
    if (!body) return;
    body.innerHTML = aviso("Cargando movimientos…");
    body.setAttribute("aria-busy", "true");

    var sel = document.getElementById("filtro-producto");
    var qs = sel && sel.value ? "?productoId=" + encodeURIComponent(sel.value) : "";

    fetch("/MovimientoStock/ListJson" + qs, { headers: { "X-Requested-With": "XMLHttpRequest" } })
      .then(function (r) {
        if (!r.ok) throw new Error("HTTP " + r.status);
        return r.json();
      })
      .then(function (data) { renderMov(data, body, summary); })
      .catch(function () {
        movLoaded = false;
        body.innerHTML = aviso("No se pudieron cargar los movimientos. Volvé a abrir la pestaña o usá el historial completo.", true);
      })
      .finally(function () { body.removeAttribute("aria-busy"); });
  }

  var MAX_ROWS = 50;

  function renderMov(data, body, summary) {
    var all = (data && data.items) || [];
    var items = all.slice(0, MAX_ROWS);
    if (summary) {
      summary.textContent = (data.total || 0) + " movimientos · " +
        (data.entradas || 0) + " unidades entradas · " + (data.salidas || 0) + " unidades salidas · " + (data.ajustes || 0) + " ajustes" +
        (all.length > MAX_ROWS ? " · mostrando los " + MAX_ROWS + " más recientes" : "");
    }
    if (!items.length) {
      body.innerHTML = aviso("No hay movimientos para el filtro actual.");
      return;
    }
    body.innerHTML = items.map(function (m) {
      var stock = (m.stockAnterior != null && m.stockNuevo != null)
        ? esc(m.stockAnterior) + " → " + esc(m.stockNuevo) : "—";
      var detalle = [m.referenciaLegible, m.motivo].filter(Boolean).join(" · ");
      var signo = m.tipo === "Entrada" ? "+" : m.tipo === "Salida" ? "−" : "";
      var tono = m.tipo === "Entrada" ? "inv-text-ok" : m.tipo === "Salida" ? "inv-text-danger" : "";
      return "<tr>" +
        '<th scope="row">' + esc(m.fecha) + '<p class="inv-ayuda-texto">' + esc(m.hora) + "</p></th>" +
        '<td data-label="Movimiento" class="inv-celda-detalle"><strong>' + esc(m.tipoNombre || m.tipo) + "</strong>" +
          (detalle ? '<p class="inv-ayuda-texto">' + esc(detalle) + "</p>" : "") + "</td>" +
        '<td data-label="Producto">' + esc(m.productoNombre || "—") + '<p class="inv-ayuda-texto">' + esc(m.productoCodigo || "") + "</p></td>" +
        '<td data-label="Cantidad" class="inv-col-num"><span class="' + tono + '">' + signo + esc(Math.abs(m.cantidad)) + "</span></td>" +
        '<td data-label="Stock" class="inv-col-num">' + stock + "</td>" +
        '<td data-label="Usuario">' + esc(m.usuario || "—") + "</td>" +
        "</tr>";
    }).join("");
  }

  // tab inicial: ?tab=movimientos abre movimientos; default unidades
  var params = new URLSearchParams(location.search);
  if (tabs.length) show(params.get("tab") === "movimientos" ? "movimientos" : "unidades");
})();
