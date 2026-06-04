/* ============================================================
   Plantillas de contrato · Crear/Editar — formulario
   Prototipo visual. En producción:
     · EsNueva decide POST a Create vs Edit/{Id}
     · AntiForgeryToken + hidden Id ya están en el form
     · ModelState pinta el validation summary del lado server
   ============================================================ */
(function () {
  const SAMPLE = JSON.parse(document.getElementById('sample-data').textContent);
  const SEED = JSON.parse(document.getElementById('edit-seed').textContent);

  /* mismo catálogo que el Index */
  const VAR_GROUPS = [
    { title: 'Vendedor', icon: 'storefront', vars: [
      ['VENDEDOR_NOMBRE','Nombre del vendedor'],['VENDEDOR_DOMICILIO','Domicilio del vendedor'],
      ['VENDEDOR_DNI','DNI del vendedor'],['VENDEDOR_CUIT','CUIT del vendedor'] ]},
    { title: 'Comprador', icon: 'person', vars: [
      ['COMPRADOR_NOMBRE','Nombre completo del comprador'],['COMPRADOR_DNI','DNI del comprador'],
      ['COMPRADOR_CUIL','CUIL del comprador'],['COMPRADOR_DOMICILIO','Domicilio del comprador'],
      ['COMPRADOR_LOCALIDAD','Localidad del comprador'],['COMPRADOR_TELEFONO','Teléfono del comprador'] ]},
    { title: 'Garante', icon: 'handshake', vars: [
      ['GARANTE_NOMBRE','Nombre completo del garante'],['GARANTE_DNI','DNI del garante'],
      ['GARANTE_DOMICILIO','Domicilio del garante'],['GARANTE_RELACION','Relación con el comprador'] ]},
    { title: 'Operación', icon: 'receipt_long', vars: [
      ['NUMERO_CONTRATO','Número del contrato'],['NUMERO_PAGARE','Número del pagaré'],
      ['FECHA_OPERACION','Fecha de la venta'],['FECHA_HORA_EMISION','Fecha y hora de emisión'],
      ['PRODUCTOS_DETALLE','Lista de productos de la venta'] ]},
    { title: 'Montos y cuotas', icon: 'payments', vars: [
      ['PRECIO_TOTAL','Total de la venta'],['SALDO_FINANCIADO','Monto financiado / total a pagar'],
      ['CANTIDAD_CUOTAS','Cantidad de cuotas'],['VALOR_CUOTA','Valor de cada cuota'],
      ['PLAN_CUOTAS','Plan completo de cuotas'],['INTERES_MORA','Interés diario por mora (%)'] ]},
    { title: 'Sistema', icon: 'settings', vars: [
      ['USUARIO_GENERACION','Usuario que generó el contrato'],['SUCURSAL','Sucursal'],['CAJA','Caja'] ]},
  ];

  /* ---------- estado ---------- */
  const params = new URLSearchParams(location.search);
  const editId = params.get('id');
  const esNueva = !editId;
  let activeField = document.getElementById('f-TextoContrato');
  let previewTab = 'contrato';
  let activa = true;

  const $ = id => document.getElementById(id);
  const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;' }[c]));

  /* ---------- solapas del formulario ---------- */
  const FIELD_TAB = { Nombre:'general', VigenteDesde:'general',
    NombreVendedor:'vendedor', CiudadFirma:'vendedor', InteresMoraDiarioPorcentaje:'vendedor',
    TextoContrato:'contrato' };
  function tab(name) {
    document.querySelectorAll('#form-tabs .tab').forEach(t => t.setAttribute('aria-selected', t.dataset.ftab === name));
    document.querySelectorAll('[data-ftab-panel]').forEach(p => p.classList.toggle('is-active', p.dataset.ftabPanel === name));
    if (name === 'contrato') { activeField = $('f-TextoContrato'); previewTabSet('contrato'); }
    if (name === 'pagare')   { activeField = $('f-TextoPagare');   previewTabSet('pagare'); }
  }

  /* ---------- bloques de firma ---------- */
  const FIRMA_CONTRATO =
    "\n\n\nFIRMAS\n\n" +
    "______________________________\nAclaración: {{COMPRADOR_NOMBRE}}\nDocumento: {{COMPRADOR_DNI}}\nEL COMPRADOR\n\n" +
    "______________________________\nAclaración: {{GARANTE_NOMBRE}}\nDocumento: {{GARANTE_DNI}}\nEL GARANTE\n\n" +
    "______________________________\nAclaración: {{VENDEDOR_NOMBRE}}\nEL VENDEDOR";
  const FIRMA_PAGARE =
    "\n\n______________________________\nFirma: {{COMPRADOR_NOMBRE}}\nDocumento: {{COMPRADOR_DNI}}\nFecha: {{FECHA_OPERACION}}";
  function appendTo(ta, block) {
    ta.value = ta.value.replace(/\s+$/, '') + block;
    touch(ta.id.replace('f-', ''));
    renderPreview();
    toast('Bloque de firmas agregado.', 'draw');
  }
  function insertFirmas() { tab('contrato'); appendTo($('f-TextoContrato'), FIRMA_CONTRATO); $('f-TextoContrato').focus(); }
  function insertFirmaPagare() { tab('pagare'); appendTo($('f-TextoPagare'), FIRMA_PAGARE); $('f-TextoPagare').focus(); }

  function initMode() {
    if (esNueva) {
      $('page-title').textContent = 'Nueva plantilla de contrato';
      $('crumb-leaf').textContent = 'Nueva plantilla';
      $('submit-label').textContent = 'Crear plantilla';
      $('tpl-form').setAttribute('action', '/PlantillaContratoCredito/Create');
      return;
    }
    const d = SEED[editId];
    const nombre = d ? d.Nombre : 'Plantilla';
    $('page-title').textContent = 'Editar plantilla — ' + nombre;
    $('page-sub').textContent = 'Modificá la vigencia, los datos del vendedor y los textos. Los cambios se aplican a los contratos generados a partir de ahora.';
    $('crumb-leaf').textContent = 'Editar plantilla';
    $('submit-label').textContent = 'Guardar cambios';
    $('submit-btn').querySelector('.material-symbols-outlined').textContent = 'save';
    $('tpl-form').setAttribute('action', '/PlantillaContratoCredito/Edit/' + editId);
    $('f-Id').value = editId;
    const chip = $('estado-chip'); chip.hidden = false; chip.textContent = '#' + editId;
    $('delete-btn').style.display = '';
    $('delete-form').setAttribute('action', '/PlantillaContratoCredito/Delete/' + editId);
    if (d) fill(d);
  }

  function fill(d) {
    $('f-Nombre').value = d.Nombre || '';
    $('f-VigenteDesde').value = d.VigenteDesde || '';
    $('f-VigenteHasta').value = d.VigenteHasta || '';
    $('f-NombreVendedor').value = d.NombreVendedor || '';
    $('f-DomicilioVendedor').value = d.DomicilioVendedor || '';
    $('f-DniVendedor').value = d.DniVendedor || '';
    $('f-CuitVendedor').value = d.CuitVendedor || '';
    $('f-CiudadFirma').value = d.CiudadFirma || '';
    $('f-Jurisdiccion').value = d.Jurisdiccion || '';
    $('f-InteresMoraDiarioPorcentaje').value = d.InteresMoraDiarioPorcentaje || '';
    $('f-TextoContrato').value = d.TextoContrato || '';
    $('f-TextoPagare').value = d.TextoPagare || '';
    setActiva(d.Activa !== false);
  }

  /* ---------- switch activa ---------- */
  function setActiva(v) {
    activa = v;
    $('sw-activa').setAttribute('aria-checked', v ? 'true' : 'false');
    $('f-Activa').value = v ? 'true' : 'false';
  }
  function toggleActiva() { setActiva(!activa); }

  /* ---------- textarea activo + inserción ---------- */
  function setActive(ta) { activeField = ta; }
  function insert(token) {
    const ta = activeField || $('f-TextoContrato');
    ta.focus();
    const s = ta.selectionStart ?? ta.value.length;
    const e = ta.selectionEnd ?? ta.value.length;
    const before = ta.value.slice(0, s), after = ta.value.slice(e);
    const needSpace = before && !/\s$/.test(before) ? ' ' : '';
    ta.value = before + needSpace + token + after;
    const pos = s + needSpace.length + token.length;
    ta.setSelectionRange(pos, pos);
    ta.focus();
    touch(ta.id.replace('f-', ''));
    renderPreview();
  }

  /* ---------- paleta de variables ---------- */
  function renderPalette() {
    $('palette').innerHTML = VAR_GROUPS.map(g => `
      <div class="var-group" data-group>
        <div class="vg-title"><span class="material-symbols-outlined">${g.icon}</span>${g.title}</div>
        <div class="var-list">
          ${g.vars.map(([tok, desc]) => `
            <button class="var-item" type="button" data-tok="{{${tok}}}" data-text="${(tok + ' ' + desc).toLowerCase()}" onclick="PlantillaForm.insert('{{${tok}}}')" title="Insertar {{${tok}}}">
              <span class="var-token">{{${tok}}}</span>
              <span class="var-desc">${desc}</span>
              <span class="material-symbols-outlined vi-copy">add</span>
            </button>`).join('')}
        </div>
      </div>`).join('');
  }
  function filterPalette(q) {
    q = (q || '').trim().toLowerCase();
    let shown = 0;
    document.querySelectorAll('#palette .var-group').forEach(group => {
      let g = 0;
      group.querySelectorAll('.var-item').forEach(it => {
        const hit = !q || it.dataset.text.includes(q);
        it.style.display = hit ? '' : 'none';
        if (hit) { g++; shown++; }
      });
      group.style.display = g ? '' : 'none';
    });
    $('pal-count').textContent = shown;
  }

  /* ---------- vista previa tipo documento ---------- */
  function previewTabSet(name) {
    previewTab = name;
    document.querySelectorAll('.paper-tab').forEach(t => t.setAttribute('aria-selected', t.dataset.ptab === name));
    renderPreview();
  }
  function highlight(text, fill) {
    // escapar y resaltar tokens {{TOKEN}}
    let html = esc(text);
    html = html.replace(/\{\{([A-Z_]+)\}\}/g, (m, key) => {
      if (fill && SAMPLE[key] != null) return `<span class="tok-fill">${esc(SAMPLE[key])}</span>`;
      if (fill) return `<span class="tok-miss">${esc(m)}</span>`;
      return `<span class="tok-fill">${esc(m)}</span>`;
    });
    // líneas de firma: runs de guiones bajos -> línea para firmar
    html = html.replace(/_{4,}/g, '<span class="sig-line"></span>');
    return html;
  }
  function renderPreview() {
    const fill = $('prev-fill').checked;
    const txt = previewTab === 'contrato' ? $('f-TextoContrato').value : $('f-TextoPagare').value;
    const body = $('paper-body');
    if (!txt.trim()) {
      body.className = 'paper empty';
      body.textContent = previewTab === 'contrato'
        ? 'Escribí el texto del contrato para ver la vista previa.'
        : 'Escribí el texto del pagaré para ver la vista previa.';
    } else {
      body.className = 'paper';
      body.innerHTML = highlight(txt, fill);
    }
    $('prev-note').textContent = fill
      ? 'Vista previa con datos de ejemplo — los valores reales se completan al generar la operación.'
      : 'Vista previa con variables sin reemplazar.';
    // contadores
    $('count-contrato').textContent = $('f-TextoContrato').value.length + ' caracteres';
    $('count-pagare').textContent = $('f-TextoPagare').value.length + ' caracteres';
  }

  /* ---------- validación ---------- */
  const REQ = [
    ['Nombre', v => v.trim() !== ''],
    ['VigenteDesde', v => v.trim() !== ''],
    ['NombreVendedor', v => v.trim() !== ''],
    ['CiudadFirma', v => v.trim() !== ''],
    ['InteresMoraDiarioPorcentaje', v => { const n = parseFloat(v); return !isNaN(n) && n >= 0.0001 && n <= 100; }],
    ['TextoContrato', v => v.trim() !== ''],
  ];
  function touch(name) {
    const f = $('f-' + name); if (!f) return;
    const rule = REQ.find(r => r[0] === name); if (!rule) return;
    const ok = rule[1](f.value);
    const err = document.querySelector(`[data-err="${name}"]`);
    if (err) err.style.display = ok ? 'none' : 'flex';
    f.style.borderColor = ok ? '' : 'var(--bad)';
  }
  function validate() {
    const errors = [];
    const tabsWithError = new Set();
    document.querySelectorAll('#form-tabs .tab').forEach(t => t.classList.remove('has-error'));
    REQ.forEach(([name, fn]) => {
      const f = $('f-' + name);
      const ok = fn(f.value);
      const err = document.querySelector(`[data-err="${name}"]`);
      if (err) err.style.display = ok ? 'none' : 'flex';
      f.style.borderColor = ok ? '' : 'var(--bad)';
      if (!ok) {
        const label = ({ Nombre:'Nombre', VigenteDesde:'Vigente desde', NombreVendedor:'Nombre del vendedor',
          CiudadFirma:'Ciudad de firma', InteresMoraDiarioPorcentaje:'Interés por mora (0.0001–100)',
          TextoContrato:'Texto del contrato' })[name];
        errors.push(label);
        const tn = FIELD_TAB[name];
        if (tn) tabsWithError.add(tn);
      }
    });
    // marcar solapas con error y saltar a la primera
    tabsWithError.forEach(tn => {
      const tb = document.querySelector(`#form-tabs .tab[data-ftab="${tn}"]`);
      if (tb) tb.classList.add('has-error');
    });
    const sum = $('validation-summary');
    if (errors.length) {
      const first = ['general','vendedor','contrato'].find(tn => tabsWithError.has(tn));
      if (first) tab(first);
      $('validation-list').innerHTML = errors.map(e => `<li>${e}</li>`).join('');
      sum.hidden = false;
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } else { sum.hidden = true; }
    return errors.length === 0;
  }

  function confirmDelete(ev) {
    if (ev) ev.preventDefault();
    const nombre = $('f-Nombre').value || 'esta plantilla';
    if (!window.confirm(`¿Eliminar definitivamente la plantilla «${nombre}»?\n\nEsta acción no se puede deshacer.`)) return false;
    // En producción: $('delete-form').submit() postea a Delete/{Id} con AntiForgeryToken.
    toast('Plantilla eliminada.', 'delete');
    setTimeout(() => location.href = 'Index.html', 1100);
    return false;
  }

  function submit(ev) {
    ev.preventDefault();
    if (!validate()) return false;
    // En producción el form postea a Create o Edit/{Id}. Acá simulamos.
    toast(esNueva ? 'Plantilla creada correctamente.' : 'Cambios guardados.', 'check_circle');
    setTimeout(() => location.href = 'Index.html', 1300);
    return false;
  }

  /* ---------- toast ---------- */
  let tt;
  function toast(msg, icon) {
    $('toast-msg').textContent = msg;
    if (icon) $('toast-ic').textContent = icon;
    const t = $('toast'); t.classList.add('open');
    clearTimeout(tt); tt = setTimeout(() => t.classList.remove('open'), 2200);
  }

  /* ---------- init ---------- */
  if (esNueva) { $('f-VigenteDesde').value = new Date().toISOString().slice(0, 10); }
  initMode();
  renderPalette();
  renderPreview();

  window.PlantillaForm = {
    submit, toggleActiva, setActive, insert, touch, confirmDelete,
    renderPreview, filterPalette, previewTab: previewTabSet,
    tab, insertFirmas, insertFirmaPagare,
  };
})();
