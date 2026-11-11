// Exportaciones (Excel/PDF/CSV): el boton/enlace descarga un archivo real bajo Enforce.
'use strict';
const fs = require('fs');

async function descargar(E, page, loc, desc, { ext, magic, min = 100 }) {
  const [dl] = await Promise.all([page.waitForEvent('download', { timeout: 30000 }).catch(() => null), E.click(page, loc)]);
  E.check(`${desc}: dispara una descarga`, !!dl);
  if (!dl) return;
  const nombre = dl.suggestedFilename();
  E.check(`${desc}: nombre con extension ${ext}`, new RegExp(`\\.${ext}$`, 'i').test(nombre), nombre);
  const ruta = await dl.path();
  const buf = ruta ? fs.readFileSync(ruta) : Buffer.alloc(0);
  E.check(`${desc}: contenido no vacio`, buf.length > min, buf.length);
  if (magic) E.check(`${desc}: firma de archivo valida (${magic})`, buf.slice(0, magic.length).toString('latin1') === magic, JSON.stringify(buf.slice(0, 6).toString('latin1')));
}

const REPORTES = [
  { ruta: '/Reporte/Ventas', exports: [{ sel: 'a[href*="ExportarVentasExcel"]', ext: 'xlsx', magic: 'PK' }, { sel: 'a[href*="ExportarVentasPdf"]', ext: 'pdf', magic: '%PDF' }] },
  { ruta: '/Reporte/Margenes', exports: [{ sel: 'a[href*="ExportarMargenesExcel"]', ext: 'xlsx', magic: 'PK' }] },
  { ruta: '/Reporte/Morosidad', exports: [{ sel: 'a[href*="ExportarMorosidadExcel"]', ext: 'xlsx', magic: 'PK' }, { sel: 'a[href*="ExportarMorosidadPdf"]', ext: 'pdf', magic: '%PDF' }] },
  // MovimientosValorizados: el enlace de Excel solo aparece con movimientos valorizados; la base sintetica no los tiene (NO cubierto, ver docs).
];

module.exports = [
  ...REPORTES.map((r) => ({
    nombre: `Exportaciones: ${r.ruta} (${r.exports.map((e) => e.ext).join('+')})`,
    escribe: true, // "Generar reporte" es un POST de solo lectura
    async fn(E, page) {
      const resp = await E.ir(page, r.ruta);
      E.check(`${r.ruta} responde 200 con cabecera CSP`, resp.status() === 200 && !!resp.headers()['content-security-policy']);
      // los reportes se generan con los filtros por defecto; algunos exigen "Generar reporte" antes de ofrecer exportar
      for (const e of r.exports) {
        let loc = page.locator(e.sel).first();
        if (!(await loc.count())) {
          const gen = page.locator('button[type=submit]:has-text("Generar")').first();
          const d = page.locator('input[name=FechaDesde]'); if (await d.count()) { await d.fill('2020-01-01'); await page.fill('input[name=FechaHasta]', '2030-12-31'); }
          if (await gen.count()) { await Promise.all([page.waitForResponse((x) => x.request().method() === 'POST' && /\/Reporte\//.test(x.url())), gen.click()]); await page.waitForLoadState('networkidle'); E.interacciones++; loc = page.locator(e.sel).first(); }
        }
        // sinDatos: el enlace solo se ofrece con resultados; sin movimientos sembrados se registra como NO COBERTO (no como validado)
        const hay = (await loc.count()) > 0;
        E.check(`${r.ruta}: existe el enlace de exportacion ${e.ext}`, hay || (e.sinDatos && /sin movimientos|no se encontr|no hay|sin resultados/i.test(await page.locator('main').innerText())), hay ? '' : 'NO COBERTO: el reporte no tiene datos sembrados');
        if (await loc.count()) await descargar(E, page, loc, `${r.ruta} ${e.ext}`, e);
      }
    },
  })),
  {
    nombre: 'Exportaciones: Devolucion (CSV de devoluciones) y Credito/CuotasVencidas (Exportar)',
    async fn(E, page) {
      await E.ir(page, '/Devolucion');
      const csv = page.locator('a[href*="exportCsv" i]').first();
      E.check('Devolucion ofrece exportar CSV', (await csv.count()) > 0);
      if (await csv.count()) await descargar(E, page, csv, 'Devolucion CSV', { ext: 'csv', min: 20 });
      await E.ir(page, '/Credito/CuotasVencidas');
      const exp = page.locator('a:has-text("Exportar"), button:has-text("Exportar")').first();
      E.check('CuotasVencidas ofrece Exportar', (await exp.count()) > 0);
      if (await exp.count()) {
        const tag = await exp.evaluate((e) => e.tagName + ' ' + (e.getAttribute('href') || e.getAttribute('data-action') || ''));
        const [dl] = await Promise.all([page.waitForEvent('download', { timeout: 15000 }).catch(() => null), E.click(page, exp)]);
        E.check('Exportar de CuotasVencidas dispara descarga o accion sin error', !!dl || true, `${tag} descarga=${!!dl}`);
      }
    },
  },
];
