/* ===== Módulo Mercado Libre — interacción (rework visual MercadoLibre1) =====
   Portado del standalone. Se quitó el router cliente (lo reemplaza el ruteo
   server-side de ASP.NET) y los renders de datos demo (los reemplaza Razor).
   La navegación es siempre por TABS dentro del shell global del sistema; el
   selector Tabs/Lateral se eliminó. Se conserva: sub-tabs, paneles
   colapsables, selección de modo (sim/real) y selección de filas. */
(function () {
  const $ = (s, c = document) => c.querySelector(s);
  const $$ = (s, c = document) => [...c.querySelectorAll(s)];

  /* ---- sub-tabs (atención / configuración / listing) ---- */
  function subtabs(tabsSel, attr) {
    $$(tabsSel + ' .subtab').forEach(t => t.onclick = () => {
      $$(tabsSel + ' .subtab').forEach(x => x.classList.toggle('active', x === t));
      const key = t.dataset[attr];
      $$(`[data-${attr}-panel]`).forEach(p => p.classList.toggle('hide', p.dataset[attr + 'Panel'] !== key));
    });
  }
  subtabs('#attTabs', 'att');
  subtabs('#cfgTabs', 'cfg');
  subtabs('#liTabs', 'li');

  /* ---- modo simulación/real (crear) ---- */
  $$('.modebox .opt').forEach(opt => opt.addEventListener('click', () => {
    $$('.modebox .opt').forEach(o => o.classList.toggle('sel', o === opt));
  }));

  /* ---- paneles colapsables (acoplables) ---- */
  const chevSvg = '<svg class="col-chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 9l6 6 6-6"/></svg>';
  $$('.card .subsec').forEach(h => {
    h.insertAdjacentHTML('beforeend', chevSvg);
    h.addEventListener('click', () => h.closest('.card').classList.toggle('collapsed'));
  });
  $$('.railcard .rh').forEach(h => {
    h.insertAdjacentHTML('beforeend', chevSvg);
    h.addEventListener('click', e => { if (e.target.closest('.chip')) return; h.closest('.railcard').classList.toggle('collapsed'); });
  });

  /* ---- selección de filas (publicaciones / sync) ---- */
  const selCnt = $('#selCnt'), selCnt2 = $('#selCnt2'), selbar = $('#selbar'), syncPrev = $('#syncPrev'), ckAll = $('#ckAll');
  function refreshSel() {
    const cks = $$('.rowck'), on = cks.filter(c => c.checked).length;
    if (selCnt) selCnt.textContent = on;
    if (selCnt2) selCnt2.textContent = on;
    if (syncPrev) syncPrev.disabled = on === 0;
    if (selbar) selbar.classList.toggle('hide', on === 0);
    if (ckAll) ckAll.checked = on === cks.length && on > 0;
  }
  document.addEventListener('change', e => {
    if (e.target.classList && e.target.classList.contains('rowck')) refreshSel();
    if (e.target === ckAll) { $$('.rowck').forEach(c => c.checked = ckAll.checked); refreshSel(); }
  });

  refreshSel();
})();
