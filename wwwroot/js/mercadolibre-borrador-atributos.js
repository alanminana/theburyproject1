// Formulario dinámico de atributos obligatorios de Mercado Libre para el borrador.
// Renderiza los controles a partir del catálogo LOCAL (isla JSON inicial + AJAX al
// cambiar la categoría o la condición). Cada control bindea a Atributos[i].Id /
// .ValueId / .ValueName para que el POST del borrador los persista.
(function () {
    "use strict";

    var root = document.getElementById("ml-atributos");
    if (!root) return;

    var contenedor = root.querySelector("[data-rol='atributos-contenedor']");
    var estado = root.querySelector("[data-rol='atributos-estado']");
    var urlAtributos = root.dataset.urlAtributos;
    var catalogoImportado = root.dataset.catalogoImportado === "true";
    var atributosError = (root.dataset.atributosError || "")
        .split(",").map(function (s) { return s.trim().toLowerCase(); })
        .filter(function (s) { return s.length > 0; });

    var hiddenCat = document.getElementById("CategoryIdMl");
    var condicionSel = document.getElementById("Condicion");
    var listingSel = document.querySelector("[data-ml-listing-type]") || document.getElementById("ListingTypeId");

    function escapeHtml(s) {
        return String(s == null ? "" : s)
            .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
    }

    function setEstado(txt) { if (estado) estado.textContent = txt || ""; }

    function construirControl(a, i) {
        var box = document.createElement("div");
        box.className = "mt-1";
        var nameVId = "Atributos[" + i + "].ValueId";
        var nameVName = "Atributos[" + i + "].ValueName";
        var vt = (a.valueType || "").toLowerCase();
        var tieneValores = a.values && a.values.length;

        // Select para list / boolean / cualquier atributo con valores predefinidos.
        if (tieneValores || vt === "list" || vt === "boolean") {
            var sel = document.createElement("select");
            sel.className = "select-erp w-full";
            var opt0 = document.createElement("option");
            opt0.value = ""; opt0.textContent = "— Elegí —";
            sel.appendChild(opt0);

            var usaId = tieneValores && a.values[0].id;
            (a.values || []).forEach(function (v) {
                var o = document.createElement("option");
                o.value = v.id || v.name;
                o.setAttribute("data-name", v.name);
                o.textContent = v.name;
                if ((a.valueId && v.id === a.valueId) ||
                    (!a.valueId && a.valueName && v.name === a.valueName)) {
                    o.selected = true;
                }
                sel.appendChild(o);
            });

            if (usaId) {
                sel.name = nameVId;
                var hidName = document.createElement("input");
                hidName.type = "hidden";
                hidName.name = nameVName;
                hidName.value = a.valueName || "";
                sel.addEventListener("change", function () {
                    var o = sel.options[sel.selectedIndex];
                    hidName.value = o ? (o.getAttribute("data-name") || "") : "";
                });
                box.appendChild(sel);
                box.appendChild(hidName);
            } else {
                // Sin ids: el texto elegido ES el value_name.
                sel.name = nameVName;
                box.appendChild(sel);
            }
            return box;
        }

        // number_unit: número + unidad → value_name "valor unidad".
        if (vt === "number_unit") {
            var num = document.createElement("input");
            num.type = "number"; num.step = "any";
            num.className = "input-erp";
            num.style.width = "62%"; num.style.marginRight = "4%";

            var unitSel = document.createElement("select");
            unitSel.className = "select-erp";
            unitSel.style.width = "34%";
            var unidades = a.allowedUnits && a.allowedUnits.length
                ? a.allowedUnits
                : (a.defaultUnit ? [{ id: a.defaultUnit, name: a.defaultUnit }] : []);
            unidades.forEach(function (u) {
                var o = document.createElement("option");
                o.value = u.name || u.id;
                o.textContent = u.name || u.id;
                if (a.defaultUnit && (u.id === a.defaultUnit || u.name === a.defaultUnit)) o.selected = true;
                unitSel.appendChild(o);
            });

            var hid = document.createElement("input");
            hid.type = "hidden"; hid.name = nameVName;

            if (a.valueName) {
                var m = String(a.valueName).trim().match(/^(.*\S)\s+(\S+)$/);
                if (m) {
                    num.value = m[1];
                    for (var k = 0; k < unitSel.options.length; k++) {
                        if (unitSel.options[k].value === m[2]) { unitSel.selectedIndex = k; break; }
                    }
                } else {
                    num.value = a.valueName;
                }
            }

            function sync() {
                var v = (num.value || "").trim();
                hid.value = v ? (v + (unitSel.value ? " " + unitSel.value : "")) : "";
            }
            num.addEventListener("input", sync);
            unitSel.addEventListener("change", sync);
            sync();

            box.appendChild(num);
            box.appendChild(unitSel);
            box.appendChild(hid);
            return box;
        }

        // number / string / multivalued / default → input bindeado a value_name.
        var inp = document.createElement("input");
        inp.type = vt === "number" ? "number" : "text";
        if (vt === "number") inp.step = "any";
        inp.className = "input-erp w-full";
        inp.name = nameVName;
        inp.value = a.valueName || "";
        if (a.valueMaxLength) inp.maxLength = a.valueMaxLength;
        if (a.multivalued) inp.placeholder = "Valores separados por coma";
        box.appendChild(inp);
        return box;
    }

    function fila(a, i) {
        var idAttr = a.attributeId || "";
        var bloqueante = !!a.esBloqueante;
        var enError = atributosError.indexOf(idAttr.toLowerCase()) >= 0;

        var wrap = document.createElement("div");
        wrap.className = "ml-attr rounded border px-3 py-2 " +
            (enError ? "border-rose-500 bg-rose-950/30" : "border-slate-700 bg-slate-950/40");

        var cab = document.createElement("div");
        cab.className = "flex items-center justify-between gap-2";
        cab.innerHTML =
            '<span class="text-xs font-semibold text-slate-200">' + escapeHtml(a.name) +
            (bloqueante
                ? ' <span class="text-rose-400" title="Obligatorio">*</span>'
                : (a.esRecomendado ? ' <span class="text-amber-300 text-[10px]">(recomendado)</span>' : '')) +
            '</span>' +
            '<span class="font-mono text-[10px] text-slate-500">' + escapeHtml(idAttr) + '</span>';
        wrap.appendChild(cab);

        var hid = document.createElement("input");
        hid.type = "hidden";
        hid.name = "Atributos[" + i + "].Id";
        hid.value = idAttr;
        wrap.appendChild(hid);

        wrap.appendChild(construirControl(a, i));

        var ayuda = a.hint || a.tooltip;
        if (ayuda) {
            var p = document.createElement("p");
            p.className = "text-[11px] text-slate-500 mt-1";
            p.textContent = ayuda;
            wrap.appendChild(p);
        }
        return wrap;
    }

    function render(atributos) {
        contenedor.innerHTML = "";

        if (!catalogoImportado) {
            setEstado("Catálogo ML no importado. Importalo desde Configuración o consultá los atributos en vivo en Mercado Libre.");
            return;
        }
        var hayCategoria = hiddenCat && hiddenCat.value;
        if (!atributos || !atributos.length) {
            setEstado(hayCategoria
                ? "Esta categoría no declara atributos obligatorios en el catálogo local."
                : "Elegí una categoría hoja para ver sus atributos obligatorios.");
            return;
        }
        var bloqueantes = atributos.filter(function (a) { return a.esBloqueante; }).length;
        setEstado(atributos.length + " atributo(s) de categoría — " + bloqueantes + " obligatorio(s) (marcados con *).");
        atributos.forEach(function (a, i) { contenedor.appendChild(fila(a, i)); });
    }

    function cargarDesdeIsla() {
        var isla = root.querySelector("[data-ml-attrs-initial]");
        var data = [];
        if (isla) {
            try { data = JSON.parse(isla.textContent || "[]"); } catch (e) { data = []; }
        }
        render(data);
    }

    async function recargar() {
        if (!catalogoImportado) { render([]); return; }
        var categoryId = hiddenCat ? (hiddenCat.value || "") : "";
        if (!categoryId) { render([]); return; }

        var condition = condicionSel ? (condicionSel.value || "new") : "new";
        var listingTypeId = listingSel ? (listingSel.value || "") : "";

        setEstado("Cargando atributos de la categoría…");
        try {
            var url = urlAtributos +
                "?categoryId=" + encodeURIComponent(categoryId) +
                "&condition=" + encodeURIComponent(condition) +
                "&listingTypeId=" + encodeURIComponent(listingTypeId);
            var resp = await fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" } });
            if (!resp.ok) throw new Error("HTTP " + resp.status);
            var json = await resp.json();
            if (!json.ok) { setEstado(json.error || "No se pudieron cargar los atributos."); return; }
            catalogoImportado = json.importado !== false;
            render(json.atributos || []);
        } catch (e) {
            setEstado("No se pudieron cargar los atributos de la categoría.");
        }
    }

    // Render inicial desde la isla (incluye valores ya guardados).
    cargarDesdeIsla();

    // La categoría cambió en el picker, o cambió la condición/tipo: recargar.
    document.addEventListener("ml:categoria-cambiada", recargar);
    if (condicionSel) condicionSel.addEventListener("change", recargar);
    if (listingSel) listingSel.addEventListener("change", recargar);
})();
