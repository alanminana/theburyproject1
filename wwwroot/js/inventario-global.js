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
  // Con JS la pestaña "Movimientos" reemplaza al link de fallback del header.
  Array.prototype.forEach.call(root.querySelectorAll("[data-invg-nojs]"), function (el) { el.style.display = "none"; });

  // Estado (dropdown) y "Solo …" (atajos por estado) se excluyen entre sí: combinarlos da 0 resultados sin explicación.
  (function () {
    var estado = document.getElementById("filtro-estado");
    var atajos = Array.prototype.slice.call(root.querySelectorAll(
      'input[name="soloDisponibles"],input[name="soloVendidas"],input[name="soloFaltantes"],input[name="soloBaja"],input[name="soloDevueltas"]'));
    if (!estado || !atajos.length) return;
    estado.addEventListener("change", function () {
      if (estado.value) atajos.forEach(function (a) { a.checked = false; });
    });
    atajos.forEach(function (a) {
      a.addEventListener("change", function () { if (a.checked) estado.value = ""; });
    });
  })();

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
        '<td class="invg-td invg-mono">' + (m.tipo === "Entrada" ? "+" : m.tipo === "Salida" ? "−" : "") + esc(Math.abs(m.cantidad)) + '</td>' +
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
