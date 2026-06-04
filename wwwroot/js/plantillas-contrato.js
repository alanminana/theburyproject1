/* ============================================================
   Plantillas de contrato — Crear/Editar (progressive enhancement)
   El formulario funciona sin este JS: secciones visibles, validación
   server + jQuery unobtrusive, paleta como referencia de variables.
   Acá se agregan: tabs, insertar variable en cursor, preview en vivo,
   buscar y copiar variables.
   ============================================================ */
(function () {
  "use strict";

  var module = document.querySelector(".pl-module");
  var form = document.getElementById("pl-form");
  if (!module || !form) return; // sólo en CreateEdit

  module.classList.add("pl-has-js");
  var left = document.getElementById("pl-form-left");
  if (left) left.classList.add("pl-has-tabs");

  /* ---------- editores ---------- */
  var editors = {};
  document.querySelectorAll("[data-editor]").forEach(function (ta) {
    editors[ta.getAttribute("data-editor")] = ta;
  });
  var activeName = "TextoContrato";
  var activeEditor = editors.TextoContrato || null;

  Object.keys(editors).forEach(function (name) {
    var ta = editors[name];
    ta.addEventListener("focus", function () { activeName = name; activeEditor = ta; });
    ta.addEventListener("input", function () { updateCount(name); renderPreview(); });
  });

  /* ---------- sample data ---------- */
  var SAMPLE = {};
  try {
    var raw = document.getElementById("pl-sample-data");
    if (raw) SAMPLE = JSON.parse(raw.textContent);
  } catch (e) { SAMPLE = {}; }

  /* ---------- tabs de sección ---------- */
  var tabs = Array.prototype.slice.call(document.querySelectorAll(".pl-tab"));
  var sections = Array.prototype.slice.call(document.querySelectorAll(".pl-section"));

  function showTab(name) {
    tabs.forEach(function (t) { t.setAttribute("aria-selected", String(t.getAttribute("data-tab") === name)); });
    sections.forEach(function (s) { s.classList.toggle("is-active", s.getAttribute("data-tab") === name); });
    if (name === "contrato") setPreviewTab("contrato");
    if (name === "pagare") setPreviewTab("pagare");
  }
  tabs.forEach(function (t) {
    t.addEventListener("click", function () { showTab(t.getAttribute("data-tab")); });
  });

  /* ---------- preview tabs ---------- */
  var previewTab = "contrato";
  var pTabs = Array.prototype.slice.call(document.querySelectorAll(".pl-paper-tab"));
  function setPreviewTab(name) {
    previewTab = name;
    pTabs.forEach(function (t) { t.setAttribute("aria-selected", String(t.getAttribute("data-ptab") === name)); });
    renderPreview();
  }
  pTabs.forEach(function (t) {
    t.addEventListener("click", function () { setPreviewTab(t.getAttribute("data-ptab")); });
  });
  var fillChk = document.getElementById("pl-prev-fill");
  if (fillChk) fillChk.addEventListener("change", renderPreview);

  /* ---------- contador de caracteres ---------- */
  function updateCount(name) {
    var el = document.querySelector('[data-count="' + name + '"]');
    var ta = editors[name];
    if (el && ta) el.textContent = ta.value.length + " caracteres";
  }

  /* ---------- insertar token en cursor ---------- */
  function insertToken(token, targetName) {
    var ta = targetName ? editors[targetName] : activeEditor;
    if (!ta) ta = editors.TextoContrato;
    if (!ta) return;
    var start = typeof ta.selectionStart === "number" ? ta.selectionStart : ta.value.length;
    var end = typeof ta.selectionEnd === "number" ? ta.selectionEnd : ta.value.length;
    ta.value = ta.value.slice(0, start) + token + ta.value.slice(end);
    var pos = start + token.length;
    ta.focus();
    try { ta.setSelectionRange(pos, pos); } catch (e) { /* noop */ }
    var nm = targetName || activeName;
    updateCount(nm);
    renderPreview();
  }

  // botones "Insertar:" de cada editor
  document.querySelectorAll(".pl-ins-btn[data-insert]").forEach(function (b) {
    b.addEventListener("click", function () {
      insertToken(b.getAttribute("data-insert"), b.getAttribute("data-target"));
    });
  });

  /* ---------- paleta: insertar + copiar ---------- */
  document.querySelectorAll(".pl-var-item[data-token]").forEach(function (item) {
    item.addEventListener("click", function () {
      var token = item.getAttribute("data-token");
      insertToken(token, null);
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(token).catch(function () {});
      }
      toast("Insertada y copiada: " + token);
    });
  });

  /* ---------- buscar variables ---------- */
  var search = document.getElementById("pl-var-search");
  var varCount = document.getElementById("pl-var-count");
  if (search) {
    search.addEventListener("input", function () {
      var q = (search.value || "").trim().toLowerCase();
      var shown = 0;
      document.querySelectorAll(".pl-var-group").forEach(function (group) {
        var gShown = 0;
        group.querySelectorAll(".pl-var-item").forEach(function (it) {
          var hay = (it.getAttribute("data-search") || "").toLowerCase();
          var hit = !q || hay.indexOf(q) !== -1;
          it.style.display = hit ? "" : "none";
          it.classList.toggle("is-hit", !!q && hit);
          if (hit) { gShown++; shown++; }
        });
        group.style.display = gShown ? "" : "none";
      });
      if (varCount) varCount.textContent = shown;
    });
  }

  /* ---------- preview en vivo ----------
     Seguridad: todo el texto del editor se escapa con esc() antes de
     inyectarse; sólo se envuelven en <span> los tokens {{[A-Z0-9_]+}}.
     No hay paso de HTML crudo del usuario al DOM. */
  var paperBody = document.getElementById("pl-paper-body");
  var TOKEN_RE = /\{\{([A-Z0-9_]+)\}\}/g;

  function esc(s) {
    return String(s).replace(/[&<>"]/g, function (c) {
      return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c];
    });
  }

  function renderPreview() {
    if (!paperBody) return;
    var ta = editors[previewTab === "pagare" ? "TextoPagare" : "TextoContrato"];
    var text = ta ? ta.value : "";
    if (!text.trim()) {
      paperBody.className = "pl-paper is-empty";
      paperBody.textContent = "La vista previa del documento aparece acá a medida que escribís, con las variables resaltadas.";
      return;
    }
    var fill = !fillChk || fillChk.checked;
    var html = esc(text).replace(TOKEN_RE, function (m, key) {
      if (Object.prototype.hasOwnProperty.call(SAMPLE, key)) {
        return '<span class="tok">' + (fill ? esc(SAMPLE[key]) : esc(m)) + "</span>";
      }
      return '<span class="tok-miss">' + esc(m) + "</span>";
    });
    paperBody.className = "pl-paper";
    paperBody.innerHTML = html;
  }

  /* ---------- toast ---------- */
  var toastEl = document.getElementById("pl-toast");
  var toastMsg = document.getElementById("pl-toast-msg");
  var toastTimer;
  function toast(msg) {
    if (!toastEl) return;
    if (toastMsg) toastMsg.textContent = msg;
    toastEl.classList.add("is-open");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { toastEl.classList.remove("is-open"); }, 2000);
  }

  /* ---------- revelar tab con error de validación ---------- */
  function revealErrorTab() {
    var bad = document.querySelector(".pl-section .input-validation-error, .pl-section .field-validation-error");
    if (!bad) return;
    var sec = bad.closest(".pl-section");
    if (sec) showTab(sec.getAttribute("data-tab"));
  }
  // tras submit (validación cliente) revelar la sección con el primer error
  form.addEventListener("submit", function () { setTimeout(revealErrorTab, 0); });

  /* ---------- init ---------- */
  Object.keys(editors).forEach(updateCount);
  // si el server re-renderizó con errores, mostrar esa sección
  if (document.querySelector(".pl-section .input-validation-error, .pl-section .field-validation-error")) {
    revealErrorTab();
  } else {
    showTab("general");
  }
  renderPreview();
})();
