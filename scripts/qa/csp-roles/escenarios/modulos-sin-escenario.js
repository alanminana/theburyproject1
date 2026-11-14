// D-05 (2026-11-14): pantallas con JS dinámico (innerHTML / modales / parciales) que hasta ahora solo cubría la suite E2E o el análisis
// estático (docs/csp-enforce-pendientes.md, F2.4): crédito, caja, detalle de cliente (garante), venta (devolución/facturar/crédito
// embebido), contrato, plantillas de contrato, orden de compra, inventario global. Solo lectura: las escrituras las aborta el arnés.
// Cada detalle se descubre desde su listado (id real del stack QA); sin dato, el escenario FALLA (no se omite en silencio).
'use strict';
const { barrer } = require('./lib');

// Primer enlace del listado que cumpla `re` (ruta relativa con query). Devuelve null si no hay.
async function primerEnlace(E, page, listado, re) {
  await E.ir(page, listado);
  const hrefs = await page.evaluate(() => [...document.querySelectorAll('a[href]')].map((a) => a.getAttribute('href')));
  const h = hrefs.find((x) => x && re.test(x));
  return h ? h.replace(/^https?:\/\/[^/]+/, '') : null;
}

const detalle = (nombre, listado, re, opts = {}) => ({
  nombre: `Sin escenario previo: ${nombre}`,
  async fn(E, page) {
    const ruta = await primerEnlace(E, page, listado, re);
    E.check(`${listado}: hay al menos un enlace de detalle (${re}) en los datos del stack`, !!ruta, ruta || 'ninguno');
    if (!ruta) return;
    const r = await E.ir(page, ruta);
    E.check(`${ruta}: responde 200`, r && r.status() === 200, r && r.status());
    const res = await barrer(E, page, ruta, { porFirma: 2, max: opts.max || 40 });
    E.check(`${ruta}: hay controles dinámicos para accionar`, res.firmas > 0, res.firmas);
  },
});

const fija = (nombre, ruta, opts = {}) => ({
  nombre: `Sin escenario previo: ${nombre}`,
  async fn(E, page) {
    const r = await E.ir(page, ruta);
    E.check(`${ruta}: responde 200`, r && r.status() === 200, r && r.status());
    const res = await barrer(E, page, ruta, { porFirma: 2, max: opts.max || 40 });
    if (!opts.sinControles) E.check(`${ruta}: hay controles dinámicos para accionar`, res.firmas > 0, res.firmas);
  },
});

module.exports = [
  {
    // El listado /Credito agrupa por cliente y no expone el enlace de detalle: se busca en el detalle de los primeros clientes.
    nombre: 'Sin escenario previo: Crédito: detalle (credito-details, pago de cuota, adelanto)',
    async fn(E, page) {
      await E.ir(page, '/Cliente');
      const clientes = [...new Set(await page.evaluate(() => [...document.querySelectorAll('a[href]')].map((a) => a.getAttribute('href')).filter((h) => /\/Cliente\/Details[/?]/i.test(h || ''))))].slice(0, 15);
      let ruta = null;
      for (const c of clientes) { ruta = await primerEnlace(E, page, c, /\/Credito\/Details[/?]\d+|\/Credito\/Details\?id=\d+/i); if (ruta) break; }
      E.check('algún cliente de los datos del stack tiene un crédito con detalle', !!ruta, ruta || `ninguno en ${clientes.length} clientes`);
      if (!ruta) return;
      const r = await E.ir(page, ruta);
      E.check(`${ruta}: responde 200`, r && r.status() === 200, r && r.status());
      const res = await barrer(E, page, ruta, { porFirma: 2, max: 40 });
      E.check(`${ruta}: hay controles dinámicos para accionar`, res.firmas > 0, res.firmas);
    },
  },
  detalle('Cliente: detalle (cliente-details, garante)', '/Cliente', /\/Cliente\/Details[/?]\d+|\/Cliente\/Details\?id=\d+/i),
  detalle('Venta: detalle (modal de devolución)', '/Venta', /\/Venta\/Details[/?]\d+|\/Venta\/Details\?id=\d+/i),
  detalle('Caja: detalle de apertura (caja-detalles-apertura)', '/Caja', /\/Caja\/(DetallesApertura|Detalles|Details)[/?]\d+|\/Caja\/(DetallesApertura|Detalles|Details)\?id=\d+/i),
  detalle('Caja: historial (caja-historial)', '/Caja', /\/Caja\/Historial/i),
  fija('Caja: abrir (caja-abrir)', '/Caja/Abrir', { sinControles: true }),
  fija('OrdenCompra: alta (ordencompra-form)', '/OrdenCompra/Create'),
  fija('Producto: inventario global de unidades (inventario-global)', '/Producto/UnidadesGlobal'),
  fija('Plantillas de contrato: listado', '/PlantillaContratoCredito', { sinControles: true }),
  // NO VALIDADO: /PlantillaContratoCredito/Create exige configuracion.update, que el rol Administrador no tiene (solo SuperAdmin);
  // el arnés no inicia sesion como SuperAdmin. Con administrador responde AccessDenied: no es evidencia del formulario.
  fija('Venta: alta con crédito embebido (venta-credito-embebido, configurar-venta-credito)', '/Venta/Create'),
  fija('Catálogo: movimientos de inventario (movimientos-inventario-modal)', '/Catalogo?tab=movimientos'),
];
