#!/usr/bin/env node
// A5: render de impresion (no solo que print() se invoque). Para cotizaciones (Cotizacion/Imprimir), facturas (Venta/ComprobanteFactura)
// y recibos/documentos PDF: emula media=print a ancho A4, verifica que los controles (.actions) no se impriman, que no haya desborde
// horizontal, fondo blanco y presencia de datos; en Chromium genera el PDF real con page.pdf() y valida firma %PDF, tamano y paginas.
// Los PDF servidos por la app (Documento/Ver, contratos, recibos) se descargan y se validan (firma, EOF, paginas).
// Solo contra una pila QA local aislada con los usuarios sinteticos. Uso: E2E_BASE_URL=http://127.0.0.1:P node impresion-render.js [--browser b] [--salida DIR]
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium, firefox, webkit } = require('playwright');
const BASE = process.env.E2E_BASE_URL || '';
if (!/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(BASE)) { console.error('Solo stack local aislado (E2E_BASE_URL).'); process.exit(2); }
const arg = (n, d) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : d; };
const browserName = arg('--browser', 'chromium');
const OUT = arg('--salida', '/tmp/print'); fs.mkdirSync(OUT, { recursive: true });
let fallos = 0, ok = 0;
const check = (c, msg) => { if (c) ok++; else { fallos++; console.log('  FALLO:', msg); } };

(async () => {
  const browser = await { chromium, firefox, webkit }[browserName].launch();
  const ctx = await browser.newContext({ viewport: { width: 794, height: 1123 }, locale: 'es-AR' });
  const page = await ctx.newPage();
  const errores = []; page.on('pageerror', (e) => errores.push(String(e.message)));
  await page.goto(`${BASE}/Identity/Account/Login`);
  await page.fill('#Input_UserName, input[name="Input.UserName"]', 'administrador');
  await page.fill('#input-password, input[name="Input.Password"]', 'Admin123!');
  await page.click('button[type="submit"]');
  const nombre = page.locator('input[placeholder="Nombre y apellido"]');
  if (await nombre.isVisible({ timeout: 5000 }).catch(() => false)) {
    await nombre.fill('QA print');
    await page.getByRole('checkbox', { name: /Declaro que le.* y acepto/i }).setChecked(true);
    await page.getByRole('button', { name: 'Aceptar y continuar' }).click();
  }
  await page.waitForURL((u) => !u.toString().toLowerCase().includes('/login'), { timeout: 20000 });

  // descubrir destinos desde las pantallas de detalle (no se inventan IDs)
  const destinos = { cotizacion: new Set(), factura: new Set(), pdf: new Set() };
  const hrefs = async (re) => (await page.$$eval('a[href]', (as) => as.map((a) => a.getAttribute('href')))).filter((h) => h && re.test(h));
  for (let id = 1; id <= 12; id++) {
    let r = await page.goto(`${BASE}/Cotizacion/Detalles/${id}`, { waitUntil: 'domcontentloaded' }).catch(() => null);
    if (r && r.status() < 400) (await hrefs(/Cotizacion\/Imprimir/i)).forEach((h) => destinos.cotizacion.add(h));
    r = await page.goto(`${BASE}/Venta/Details/${id}`, { waitUntil: 'domcontentloaded' }).catch(() => null);
    if (r && r.status() < 400) {
      (await hrefs(/Venta\/ComprobanteFactura/i)).forEach((h) => destinos.factura.add(h));
      (await hrefs(/Documento\/Ver|ContratoVentaCredito|Recibo|\.pdf/i)).forEach((h) => destinos.pdf.add(h));
    }
  }
  console.log(`[${browserName}] destinos: cotizaciones=${destinos.cotizacion.size} facturas=${destinos.factura.size} pdf=${destinos.pdf.size}`);
  check(destinos.cotizacion.size > 0, 'no se encontro ninguna cotizacion imprimible (datos sembrados)');
  check(destinos.factura.size > 0, 'no se encontro ninguna factura imprimible (datos sembrados)');

  let n = 0;
  for (const [tipo, set] of [['cotizacion', destinos.cotizacion], ['factura', destinos.factura]]) {
    for (const h of [...set].slice(0, 3)) {
      n++;
      const r = await page.goto(new URL(h, BASE).toString(), { waitUntil: 'networkidle' });
      check(r.status() === 200, `${tipo} ${h} HTTP ${r.status()}`);
      await page.emulateMedia({ media: 'print' });
      const m = await page.evaluate(() => {
        const vis = (el) => { const s = getComputedStyle(el); return s.display !== 'none' && s.visibility !== 'hidden' && el.getClientRects().length > 0; };
        return {
          controles: [...document.querySelectorAll('.actions, .actions *, nav, aside, header.app-header')].filter(vis).length,
          desborde: document.documentElement.scrollWidth - document.documentElement.clientWidth,
          fondo: getComputedStyle(document.body).backgroundColor,
          texto: document.body.innerText.replace(/\s+/g, ' ').trim(),
        };
      });
      check(m.controles === 0, `${tipo} ${h}: ${m.controles} controles visibles en impresion`);
      check(m.desborde <= 1, `${tipo} ${h}: desborde horizontal ${m.desborde}px a A4`);
      check(/rgb\(255, 255, 255\)|rgba\(0, 0, 0, 0\)/.test(m.fondo), `${tipo} ${h}: fondo no blanco (${m.fondo})`);
      check(/\$\s?\d/.test(m.texto) && m.texto.length > 80, `${tipo} ${h}: sin importes/datos en el render`);
      if (browserName === 'chromium') {
        const pdf = await page.pdf({ format: 'A4', printBackground: true, preferCSSPageSize: true });
        const paginas = (pdf.toString('latin1').match(/\/Type\s*\/Page[^s]/g) || []).length;
        fs.writeFileSync(path.join(OUT, `${tipo}-${n}.pdf`), pdf);
        check(pdf.slice(0, 5).toString() === '%PDF-' && pdf.length > 2000, `${tipo} ${h}: PDF invalido (${pdf.length} bytes)`);
        check(paginas >= 1 && paginas <= 3, `${tipo} ${h}: ${paginas} paginas (esperado 1-3)`);
      }
      await page.screenshot({ path: path.join(OUT, `${tipo}-${n}-${browserName}.png`), fullPage: true });
      await page.emulateMedia({ media: null });
    }
  }
  let pdfValidados = 0;
  for (const h of [...destinos.pdf].slice(0, 4)) {
    const resp = await ctx.request.get(new URL(h, BASE).toString());
    const buf = await resp.body();
    const ct = resp.headers()['content-type'] || '';
    n++;
    check(resp.status() === 200 && /pdf/i.test(ct), `PDF ${h}: respuesta ${resp.status()} ${ct} (se esperaba 200 application/pdf; error, login o HTML no valido)`);
    if (resp.status() !== 200 || !/pdf/i.test(ct)) continue;
    pdfValidados++;
    const t = buf.toString('latin1');
    check(buf.slice(0, 5).toString() === '%PDF-' && /%%EOF\s*$/.test(t) && (t.match(/\/Type\s*\/Page[^s]/g) || []).length >= 1, `PDF ${h}: firma/EOF/paginas invalidos`);
    fs.writeFileSync(path.join(OUT, `pdf-${n}.pdf`), buf);
  }
  check(pdfValidados >= 1, 'no se valido ningun PDF servido por la app (recibos/documentos): la cobertura de PDF desaparecio');
  check(errores.length === 0, `errores de pagina: ${errores.slice(0, 3).join(' | ')}`);
  await browser.close();
  console.log(`[${browserName}] impresion: ${ok} correctas, ${fallos} fallos`);
  process.exit(fallos ? 1 : 0);
})();
