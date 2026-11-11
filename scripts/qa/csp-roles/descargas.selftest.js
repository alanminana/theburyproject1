#!/usr/bin/env node
// Auto-prueba de scripts/qa/csp-roles/descargas.js: una descarga solo es valida con 200 + adjunto + archivo no vacio + firma correcta.
// Sin navegador ni stack: usa un servidor HTTP local efimero y el cliente de peticiones de Playwright. Sale con codigo != 0 si algo falla.
'use strict';
const http = require('http');
const { request } = require('playwright');
const { esInicioDeDescarga, validarDescarga } = require('./descargas');

const pdf = Buffer.concat([Buffer.from('%PDF-1.4\n'), Buffer.alloc(64, 0x20)]);
const zip = Buffer.concat([Buffer.from([0x50, 0x4b, 0x03, 0x04]), Buffer.alloc(32)]);
const adj = (nombre) => `attachment; filename="${nombre}"`;
const rutas = {
  '/pdf-ok': (res) => { res.writeHead(200, { 'Content-Type': 'application/pdf', 'Content-Disposition': adj('contrato.pdf') }); res.end(pdf); },
  '/xlsx-ok': (res) => { res.writeHead(200, { 'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'Content-Disposition': adj('r.xlsx') }); res.end(zip); },
  '/pdf-vacio': (res) => { res.writeHead(200, { 'Content-Type': 'application/pdf', 'Content-Disposition': adj('x.pdf') }); res.end(); },
  '/pdf-roto': (res) => { res.writeHead(200, { 'Content-Type': 'application/pdf', 'Content-Disposition': adj('x.pdf') }); res.end('<html>Error</html>'); },
  '/html-no-adjunto': (res) => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<html>hola</html>'); },
  '/sin-permiso': (res) => { res.writeHead(403); res.end('no'); },
  '/a-login': (res) => { res.writeHead(302, { Location: '/Identity/Account/Login' }); res.end(); },
  '/error': (res) => { res.writeHead(500); res.end('boom'); },
};

(async () => {
  const srv = http.createServer((req, res) => (rutas[req.url.split('?')[0]] || ((r) => { r.writeHead(404); r.end(); }))(res));
  await new Promise((r) => srv.listen(0, '127.0.0.1', r));
  const base = `http://127.0.0.1:${srv.address().port}`;
  const ctx = await request.newContext();
  const casos = [
    ['/pdf-ok', true], ['/xlsx-ok', true],
    ['/pdf-vacio', false], ['/pdf-roto', false], ['/html-no-adjunto', false],
    ['/sin-permiso', false], ['/a-login', false], ['/error', false], ['/inexistente', false],
  ];
  let mal = 0;
  for (const [ruta, esperado] of casos) {
    const d = await validarDescarga(ctx, base + ruta);
    const bien = d.ok === esperado;
    if (!bien) mal++;
    console.log(`${bien ? 'OK ' : 'MAL'} ${ruta.padEnd(18)} esperado=${esperado ? 'valida' : 'invalida'} obtenido=${d.ok ? 'valida' : 'invalida'} (${d.detalle})`);
  }
  for (const [txt, esperado] of [['page.goto: Download is starting', true], ['net::ERR_CONNECTION_REFUSED', false], ['Timeout 30000ms exceeded', false]]) {
    const bien = esInicioDeDescarga(new Error(txt)) === esperado;
    if (!bien) mal++;
    console.log(`${bien ? 'OK ' : 'MAL'} esInicioDeDescarga("${txt}") = ${esperado}`);
  }
  await ctx.dispose();
  srv.close();
  console.log(mal ? `FALLA: ${mal} casos` : `descargas.selftest: ${casos.length + 3} casos correctos`);
  process.exit(mal ? 1 : 0);
})();
