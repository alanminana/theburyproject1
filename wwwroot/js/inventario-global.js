/* ============================================================
   Inventario global — experiencia integrada (Unidades + Movimientos)
   Progressive enhancement sobre /Producto/UnidadesGlobal.
   Sin JS: se ve la tabla de unidades + link a /MovimientoStock/Index.
   Con JS: tabs; "Movimientos" carga inline desde el endpoint existente
   /MovimientoStock/ListJson (no toca backend). Ruta sin cambios.
   ============================================================ */
(function () {
  "use strict";

  var root = document.querySelector("[data-invg-root]");
  if (!root) return;
  root.classList.add("invg-js");

  var tabs = Array.prototype.slice.call(root.querySelectorAll("[data-invg-tab]"));
  var panels = Array.prototype.slice.call(root.querySelectorAll("[data-invg-panel]"));
  var movLoaded = false;

  function show(name) {
    tabs.forEach(function (t) { t.setAttribute("aria-selected", String(t.getAttribute("data-invg-tab") === name)); });
    panels.forEach(function (p) { p.hidden = p.getAttribute("data-invg-panel") !== name; });
    if (name === "movimientos" && !movLoaded) loadMov();
  }
  tabs.forEach(function (t) {
    t.addEventListener("click", function () { show(t.getAttribute("data-invg-tab")); });
  });

  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"]/g, function (c) {
      return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c];
    });
  }

  var chipClass = {
    Entrada: "invg-chip invg-chip-in",
    Salida: "invg-chip invg-chip-out",
    Ajuste: "invg-chip invg-chip-adj"
  };

  function loadMov() {
    movLoaded = true;
    var body = document.getElementById("invg-mov-body");
    var summary = document.getElementById("invg-mov-summary");
    if (!body) return;
    body.innerHTML = '<tr><td colspan="7" class="invg-mov-state">Cargando movimientos…</td></tr>';

    var sel = document.getElementById("filtro-producto");
    var qs = sel && sel.value ? "?productoId=" + encodeURIComponent(sel.value) : "";

    fetch("/MovimientoStock/ListJson" + qs, { headers: { "X-Requested-With": "XMLHttpRequest" } })
      .then(function (r) { return r.json(); })
      .then(function (data) { renderMov(data, body, summary); })
      .catch(function () {
        movLoaded = false;
        body.innerHTML = '<tr><td colspan="7" class="invg-mov-state invg-mov-error">No se pudieron cargar los movimientos. Reintentá o abrí el historial completo.</td></tr>';
      });
  }

  function renderMov(data, body, summary) {
    var items = (data && data.items) || [];
    if (summary) {
      summary.textContent = (data.total || 0) + " movimientos · " +
        (data.entradas || 0) + " entradas · " + (data.salidas || 0) + " salidas · " + (data.ajustes || 0) + " ajustes";
    }
    if (!items.length) {
      body.innerHTML = '<tr><td colspan="7" class="invg-mov-state">No hay movimientos para el filtro actual.</td></tr>';
      return;
    }
    body.innerHTML = items.map(function (m) {
      var cls = chipClass[m.tipo] || "invg-chip invg-chip-neutral";
      var stock = (m.stockAnterior != null && m.stockNuevo != null)
        ? esc(m.stockAnterior) + " → " + esc(m.stockNuevo) : "—";
      var ref = m.referencia || m.motivo || "—";
      return '<tr>' +
        '<td class="invg-td"><div>' + esc(m.fecha) + '</div><div class="invg-sub">' + esc(m.hora) + '</div></td>' +
        '<td class="invg-td"><span class="' + cls + '">' + esc(m.tipoNombre || m.tipo) + '</span></td>' +
        '<td class="invg-td"><div class="invg-strong">' + esc(m.productoNombre || "—") + '</div><div class="invg-sub invg-mono">' + esc(m.productoCodigo || "") + '</div></td>' +
        '<td class="invg-td invg-mono">' + esc(m.cantidad) + '</td>' +
        '<td class="invg-td invg-mono invg-sub">' + stock + '</td>' +
        '<td class="invg-td invg-sub">' + esc(m.usuario || "—") + '</td>' +
        '<td class="invg-td invg-sub">' + esc(ref) + '</td>' +
        '</tr>';
    }).join("");
  }

  // tab inicial: ?tab=movimientos abre movimientos; default unidades
  var params = new URLSearchParams(location.search);
  show(params.get("tab") === "movimientos" ? "movimientos" : "unidades");
})();
