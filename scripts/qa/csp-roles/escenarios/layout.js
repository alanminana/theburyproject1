// Layout compartido (aparece en TODAS las pantallas): notificaciones, notas rapidas (texto y checklist) y tickets (panel, alta con innerHTML en la lista).
'use strict';

module.exports = [
  {
    nombre: 'Layout: panel de notificaciones, notas rapidas (texto/checklist, alta y baja) y tickets (panel, modal de alta, lista y detalle)',
    escribe: true,
    async fn(E, page) {
      await E.ir(page, '/Cliente');
      // notificaciones
      await E.click(page, '#btn-open-notificaciones');
      E.check('el panel de notificaciones abre', await E.visible(page, '#notificacionesPanel'));
      await page.keyboard.press('Escape'); E.interacciones++;
      // notas rapidas
      await E.click(page, '#btn-open-notas');
      E.check('el panel de notas rapidas abre', await E.visible(page, '#notas-panel'));
      const t = `Nota CSP ${Date.now()}`;
      const input = page.locator('#notas-panel [data-notas-input]').first();
      const [rn] = await Promise.all([page.waitForResponse((r) => /notas-rapidas/i.test(r.url()) && r.request().method() === 'POST', { timeout: 10000 }).catch(() => null), (async () => { await input.fill(t); await input.press('Enter'); E.interacciones++; })()]);
      E.check('guardar una nota (Enter) responde 2xx', !!rn && rn.ok(), rn && rn.status());
      E.check('la nota aparece en la lista (contenido generado por JS)', await page.locator('#notas-panel', { hasText: t }).first().waitFor({ timeout: 5000 }).then(() => true).catch(() => false));
      await E.click(page, '#notas-panel [data-notas-tipo="checklist"]');
      E.check('cambiar a checklist actualiza aria-pressed', (await page.locator('#notas-panel [data-notas-tipo="checklist"]').getAttribute('aria-pressed')) === 'true');
      await E.click(page, '#btn-open-notas');
      // tickets: panel -> nuevo -> modal -> alta -> lista
      await E.click(page, '#btn-open-ticket-panel');
      E.check('el panel de tickets abre', await E.visible(page, '#tp-view-lista'));
      await E.click(page, '#tp-nuevo-btn');
      E.check('el modal de nuevo ticket abre', await E.visible(page, '#ticket-modal'));
      await E.click(page, '#ticket-modal-submit');
      await page.waitForTimeout(600);
      E.check('ticket vacio: error de validacion y el modal sigue abierto', await E.visible(page, '#ticket-modal', 300));
      const titulo = `Ticket CSP ${Date.now()}`;
      await page.selectOption('#ticket-tipo', { index: 1 }); E.interacciones++;
      await page.fill('#ticket-titulo', titulo);
      await page.fill('#ticket-descripcion', 'Creado por el escenario CSP bajo Enforce');
      const [rt] = await Promise.all([page.waitForResponse((r) => /\/api\/tickets/.test(r.url()) && r.request().method() === 'POST', { timeout: 10000 }).catch(() => null), E.click(page, '#ticket-modal-submit')]);
      E.check('crear el ticket responde 2xx', !!rt && rt.ok(), (rt && rt.status()) + ' ' + (await page.locator('#ticket-modal-error-text').innerText().catch(() => '')) + ' ' + (await page.locator('#ticket-tipo').inputValue().catch(() => '')));
      await page.waitForTimeout(800);
      E.check('el modal se cierra tras crear', await E.oculto(page, '#ticket-modal', 5000));
      const fila = page.locator('#tp-lista-items', { hasText: titulo }).first();
      E.check('el ticket aparece en la lista del panel (innerHTML)', await fila.waitFor({ timeout: 6000 }).then(() => true).catch(() => false));
      const item = page.locator('#tp-lista-items > *').first();
      if (await item.count()) { await item.click(); E.interacciones++; E.check('el detalle del ticket se muestra', await E.visible(page, '#tp-detalle-content', 6000)); await E.click(page, '#tp-back-btn'); }
      await E.click(page, '#tp-close-btn');
    },
  },
];
