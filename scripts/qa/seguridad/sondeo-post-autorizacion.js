#!/usr/bin/env node
// F3.1 (2026-11-11): sondeo EN VIVO de autorizacion de acciones que MODIFICAN datos (POST/PUT/PATCH/DELETE), con verificacion SQL de ausencia de efectos.
//
// Contra un stack QA local en Production (usuarios de prueba sinteticos sembrados por el overlay de CI). Cada rol inicia sesion de verdad (cookie
// real), obtiene un token antiforgery VALIDO de una pagina a la que si accede y ejecuta POST reales contra TODAS las acciones del inventario
// (scripts/qa/seguridad/inventario-acciones-mutantes.json, generado por reflexion y vigilado por un test) que su rol NO puede ejecutar segun los
// permisos efectivos sembrados en SQL, con ids de recursos reales del dataset. Esperado: 403 / redireccion a AccessDenied (nunca 400 de CSRF,
// 2xx ni 5xx) y cero cambios en la base (conteo + CHECKSUM_AGG de todas las tablas antes/despues de cada rol).
// Ademas: anonimo (todas las acciones), y CSRF (sin token y token de otra sesion) del rol Administrador sobre las acciones que SI puede ejecutar,
// con ids inexistentes para no producir efectos aunque la defensa fallara.
//
// Falla (exit != 0) si: inventario vacio/insuficiente (3), no pudo autenticarse un rol (4), no hay datos reales para los ids (5), o hay anomalias (1).
//
// Uso (PowerShell/bash):
//   E2E_BASE_URL=http://127.0.0.1:18230 QA_SQL_CONTAINER=bury-mc-db-1 QA_SQL_PASSWORD=<sa del stack QA> \
//   QA_PASS_ADMINCI=<clave del SuperAdmin sintetico> SONDEO_OUT=evidencia.json node scripts/qa/seguridad/sondeo-post-autorizacion.js
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const L = require('./lib-qa-seguridad');

const USUARIOS = {
  administrador: 'Admin123!', gerente: 'Gerente123!', vendedor: 'Vendedor123!', cajero: 'Cajero123!',
  repositor: 'Repositor123!', tecnico: 'Tecnico123!', contador: 'Contador123!',
};
if (process.env.QA_SUPERADMIN_USER && process.env.QA_PASS_ADMINCI) USUARIOS[process.env.QA_SUPERADMIN_USER] = process.env.QA_PASS_ADMINCI;
const MIN_ACCIONES = 150;

const inventario = JSON.parse(fs.readFileSync(path.join(__dirname, 'inventario-acciones-mutantes.json'), 'utf8')).filter((a) => !a.excluidaPorDiseno);
if (inventario.length < MIN_ACCIONES) { console.error(`Inventario insuficiente (${inventario.length}): el sondeo no es valido.`); process.exit(3); }

// --- datos reales del dataset sintetico ---
const primero = (t, extra = '') => L.sqlEscalar(`SELECT TOP 1 Id FROM ${t} WHERE 1=1 ${extra} ORDER BY Id`, null);
const ids = {
  venta: primero('Ventas', 'AND IsDeleted=0'), credito: primero('Creditos', 'AND IsDeleted=0'), cuota: primero('Cuotas', 'AND IsDeleted=0'),
  apertura: primero('AperturasCaja', 'AND IsDeleted=0'), caja: primero('Cajas', 'AND IsDeleted=0'), cliente: primero('Clientes', 'AND IsDeleted=0'),
  devolucion: primero('Devoluciones', 'AND IsDeleted=0'), producto: primero('Productos', 'AND IsDeleted=0'), movimiento: primero('MovimientosCaja', 'AND IsDeleted=0'),
  proveedor: primero('Proveedores', 'AND IsDeleted=0'),
};
const superId = L.sqlEscalar("SELECT TOP 1 u.Id FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId=u.Id JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='SuperAdmin' ORDER BY u.UserName");
const rolAdminId = L.sqlEscalar("SELECT Id FROM AspNetRoles WHERE Name='Administrador'");
const faltan = ['venta', 'credito', 'cuota', 'apertura', 'caja', 'cliente'].filter((k) => !ids[k]);
if (faltan.length || !superId || !rolAdminId) { console.error(`El dataset no tiene datos reales para: ${[...faltan, !superId && 'superadmin', !rolAdminId && 'rol'].filter(Boolean).join(', ')}.`); process.exit(5); }

function valorDe(a, p, modo = 'real') {
  if (modo === 'inexistente') return /^(Int32|Int64|Int32\?|Int64\?)$/.test(p.tipo) || !['String', 'Boolean', 'Boolean?'].includes(p.tipo) ? '99999999' : p.tipo === 'String' ? 'id-inexistente' : 'true';
  const n = p.nombre.toLowerCase();
  const m = [['venta', ids.venta], ['credito', ids.credito], ['cuota', ids.cuota], ['apertura', ids.apertura], ['caja', ids.caja],
    ['devolucion', ids.devolucion], ['cliente', ids.cliente], ['movimiento', ids.movimiento], ['producto', ids.producto], ['proveedor', ids.proveedor]]
    .find(([k]) => n.includes(k));
  if (m && m[1]) return String(m[1]);
  if (/^(role|rol)id$/.test(n)) return rolAdminId;
  if (/^(user|usuario)id$/.test(n)) return superId;
  if (n === 'id' || n === 'ids') {
    if (a.controller === 'Usuarios') return superId;
    if (a.controller === 'Seguridad') return /Rol/.test(a.accion) && a.accion !== 'EditUsuario' ? rolAdminId : superId;
    const mapa = { Venta: ids.venta, Caja: ids.caja, Credito: ids.credito, Devolucion: ids.devolucion, Cliente: ids.cliente, Producto: ids.producto, Proveedor: ids.proveedor };
    return String(mapa[a.controller] || 1);
  }
  if (p.tipo === 'Boolean' || p.tipo === 'Boolean?') return 'true';
  if (p.tipo === 'String') return 'sondeo';
  return '1';
}
const COMPLEJO = /(ViewModel|Input|Request|Command|Dto|IFormFile|IFormCollection|List<>|Dictionary<>)$/;

async function enviar(request, a, token, modo) {
  let ruta = a.ruta;
  for (const p of a.parametros) ruta = ruta.replace(new RegExp(`\\{${p.nombre}(:[^}]*)?\\??\\}`, 'i'), encodeURIComponent(valorDe(a, p, modo)));
  ruta = ruta.replace(/\{[^}]+\}/g, '1');
  const simples = a.parametros.filter((p) => p.fuente !== 'Body' && !COMPLEJO.test(p.tipo));
  const form = Object.fromEntries(simples.map((p) => [p.nombre, valorDe(a, p, modo)]));
  if (token) form.__RequestVerificationToken = token;
  const cabeceras = token ? { RequestVerificationToken: token } : {};
  const esJson = /json/i.test(a.consumes) || a.parametros.some((p) => p.fuente === 'Body');
  const metodo = a.verbo.toLowerCase();
  const intentos = esJson ? ['json', 'form', 'multipart'] : ['form', 'multipart', 'json'];
  let resp;
  for (const modoCuerpo of intentos) {
    const opts = { headers: { ...cabeceras }, maxRedirects: 0, failOnStatusCode: false, timeout: 60000 };
    if (modoCuerpo === 'form') opts.form = form;
    else if (modoCuerpo === 'multipart') opts.multipart = form;
    else { opts.headers['Content-Type'] = 'application/json'; opts.data = '{}'; }
    resp = await request[metodo](L.BASE + ruta, opts);
    if (resp.status() !== 415) break;
  }
  return resp;
}
const clasificar = (resp, anonimo = false) => {
  const s = resp.status();
  const loc = resp.headers().location || '';
  if (s === 401 || s === 403) return 'DENEGADO';
  if (s >= 300 && s < 400) {
    // Con sesion, solo AccessDenied prueba que el rechazo es por autorizacion; una redireccion a Login seria una sesion perdida (falso verde).
    if (/AccessDenied/i.test(loc) || (anonimo && /\/Login/i.test(loc))) return 'DENEGADO';
    return `REDIRIGE(${loc.slice(0, 60)})`;
  }
  if (s === 404) return 'NO_EXPUESTA';
  if (s === 400) return 'RECHAZO_400';
  if (s >= 500) return 'ERROR_5XX';
  return `ACEPTADO(${s})`;
};

(async () => {
  const informe = { base: L.BASE, inicio: new Date().toISOString(), inventario: inventario.length, roles: {}, anonimo: {}, csrf: {}, anomalias: [] };
  const browser = await chromium.launch();

  // permisos efectivos sembrados en SQL (RolPermisos vigentes) y roles de cada usuario
  const claimsPorRol = {};
  for (const [rol, claim] of L.sql("SELECT r.Name, rp.ClaimValue FROM RolPermisos rp JOIN AspNetRoles r ON r.Id = rp.RoleId WHERE rp.IsDeleted = 0")) (claimsPorRol[rol] ||= []).push(claim);
  const rolesDe = (u) => L.sql(`SELECT r.Name FROM AspNetUsers x JOIN AspNetUserRoles ur ON ur.UserId=x.Id JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE x.UserName='${u}'`).map((f) => f[0]);

  // 1) sesiones reales
  const sesiones = {};
  for (const [user, pass] of Object.entries(USUARIOS)) {
    try {
      const ctx = await browser.newContext();
      await L.login(ctx, user, pass);
      const token = await L.tokenDe(ctx.request);
      if (!token) throw new Error('sin token antiforgery');
      const roles = rolesDe(user);
      if (!roles.length) throw new Error('el usuario no tiene roles en SQL');
      sesiones[user] = { ctx, token, roles, claims: [...new Set(roles.flatMap((r) => claimsPorRol[r] || []))] };
    } catch (e) { console.error(`No se pudo autenticar ${user}: ${e.message}`); await browser.close(); process.exit(4); }
  }

  // 2) cada rol POST a lo que NO puede ejecutar
  for (const [user, s] of Object.entries(sesiones)) {
    const objetivo = inventario.filter((a) => L.debeDenegar(a, s.claims, s.roles));
    const esSuper = s.roles.includes('SuperAdmin');
    const antes = L.huella();
    const conteo = {}; const fallas = [];
    for (const a of objetivo) {
      const r = await enviar(s.ctx.request, a, s.token, 'real');
      const c = clasificar(r);
      conteo[c] = (conteo[c] || 0) + 1;
      if (c !== 'DENEGADO' && c !== 'NO_EXPUESTA') fallas.push(`${a.verbo} ${a.ruta} [${a.id}] -> ${r.status()} ${c}`);
    }
    const cambios = objetivo.length ? L.diferencias(antes, L.huella()) : [];
    informe.roles[user] = { roles: s.roles, permisos: s.claims.length, accionesProhibidas: objetivo.length, respuestas: conteo, fallas, cambiosEnBase: cambios, esSuperAdmin: esSuper };
    console.log(`${user.padEnd(14)} [${s.roles.join(',')}] prohibidas=${objetivo.length} ${JSON.stringify(conteo)} cambiosEnBase=${cambios.length}`);
    fallas.forEach((f) => informe.anomalias.push(`${user}: ${f}`));
    cambios.forEach((c) => informe.anomalias.push(`${user}: un POST denegado cambio la base: ${c}`));
    // un rol (salvo el SuperAdmin) sin nada prohibido seria un sondeo vacio
    if (!esSuper && objetivo.length === 0) informe.anomalias.push(`${user}: no hay acciones prohibidas que probar (inventario o permisos inconsistentes)`);
  }

  // 3) anonimo: todas las acciones
  {
    const anon = await browser.newContext();
    const token = await L.tokenDe(anon.request);
    const antes = L.huella();
    const conteo = {}; const fallas = [];
    for (const a of inventario) {
      const r = await enviar(anon.request, a, token, 'real');
      const c = clasificar(r, true);
      conteo[c] = (conteo[c] || 0) + 1;
      // anonimo: 302 a Login (DENEGADO) esperado; 400 de antiforgery tambien es un rechazo valido sin sesion
      if (!['DENEGADO', 'NO_EXPUESTA', 'RECHAZO_400'].includes(c)) fallas.push(`${a.verbo} ${a.ruta} [${a.id}] -> ${r.status()} ${c}`);
    }
    const cambios = L.diferencias(antes, L.huella());
    informe.anonimo = { acciones: inventario.length, respuestas: conteo, fallas, cambiosEnBase: cambios };
    console.log(`${'anonimo'.padEnd(14)} acciones=${inventario.length} ${JSON.stringify(conteo)} cambiosEnBase=${cambios.length}`);
    fallas.forEach((f) => informe.anomalias.push(`anonimo: ${f}`));
    cambios.forEach((c) => informe.anomalias.push(`anonimo: un POST sin sesion cambio la base: ${c}`));
    await anon.close();
  }

  // 4) CSRF: el Administrador (que SI tiene permiso) sin token y con el token de OTRA sesion; ids inexistentes por seguridad
  {
    const s = sesiones.administrador;
    const otro = sesiones.vendedor;
    const permitidas = inventario.filter((a) => !L.debeDenegar(a, s.claims, s.roles) && !['MercadoLibre', 'Diagnostico'].includes(a.controller));
    const antes = L.huella();
    const fallas = []; const conteo = { sinToken: {}, tokenAjeno: {} };
    for (const a of permitidas) {
      for (const [etiqueta, tok] of [['sinToken', null], ['tokenAjeno', otro.token]]) {
        const r = await enviar(s.ctx.request, a, tok, 'inexistente');
        const c = clasificar(r);
        conteo[etiqueta][c] = (conteo[etiqueta][c] || 0) + 1;
        if (c !== 'RECHAZO_400') fallas.push(`${etiqueta}: ${a.verbo} ${a.ruta} [${a.id}] -> ${r.status()} ${c}`);
      }
    }
    const cambios = L.diferencias(antes, L.huella());
    informe.csrf = { acciones: permitidas.length, respuestas: conteo, fallas, cambiosEnBase: cambios };
    console.log(`${'csrf(admin)'.padEnd(14)} acciones=${permitidas.length}x2 sinToken=${JSON.stringify(conteo.sinToken)} tokenAjeno=${JSON.stringify(conteo.tokenAjeno)} cambiosEnBase=${cambios.length}`);
    fallas.forEach((f) => informe.anomalias.push(`csrf: ${f}`));
    cambios.forEach((c) => informe.anomalias.push(`csrf: un POST sin token valido cambio la base: ${c}`));
  }

  // 5) control positivo / matriz efectiva: cada rol, con token valido e ids inexistentes, NO debe ser denegado en lo que SI puede ejecutar
  //    (una denegacion aqui es una discrepancia entre los permisos sembrados y el comportamiento). Sin aserciones sobre la base: los ids no existen.
  informe.permitidas = {};
  for (const [user, s] of Object.entries(sesiones)) {
    const permitidas = inventario.filter((a) => !L.debeDenegar(a, s.claims, s.roles) && !['MercadoLibre', 'Diagnostico'].includes(a.controller));
    const conteo = {}; const denegadas = []; const cincos = [];
    for (const a of permitidas) {
      const r = await enviar(s.ctx.request, a, s.token, 'inexistente');
      const c = clasificar(r);
      conteo[c] = (conteo[c] || 0) + 1;
      if (c === 'DENEGADO') denegadas.push(`${a.verbo} ${a.ruta} [${a.id}] -> ${r.status()}`);
      if (c === 'ERROR_5XX') cincos.push(`${a.verbo} ${a.ruta} [${a.id}]`);
    }
    // la sesion sigue viva (si no, todo "DENEGADO" anterior podria ser una sesion perdida)
    const viva = (await s.ctx.request.get(L.BASE + '/Dashboard', { maxRedirects: 0 })).status() === 200;
    informe.permitidas[user] = { acciones: permitidas.length, respuestas: conteo, denegadasInesperadamente: denegadas, cincoCientos: cincos, sesionViva: viva };
    console.log(`${user.padEnd(14)} permitidas=${permitidas.length} ${JSON.stringify(conteo)} sesionViva=${viva}`);
    denegadas.forEach((d) => informe.anomalias.push(`${user}: denegada una accion que sus permisos permiten: ${d}`));
    cincos.forEach((d) => informe.anomalias.push(`${user}: 5xx con id inexistente: ${d}`));
    if (!viva) informe.anomalias.push(`${user}: la sesion se perdio durante el sondeo (resultados no validos)`);
  }

  informe.fin = new Date().toISOString();
  informe.totalPostEnviados = Object.values(informe.roles).reduce((n, r) => n + r.accionesProhibidas, 0) + inventario.length + informe.csrf.acciones * 2 + Object.values(informe.permitidas).reduce((n, r) => n + r.acciones, 0);
  console.log(`POST enviados: ${informe.totalPostEnviados}. ${informe.anomalias.length ? `ANOMALIAS (${informe.anomalias.length}):\n  ${informe.anomalias.join('\n  ')}` : 'Sin anomalias.'}`);
  if (process.env.SONDEO_OUT) fs.writeFileSync(process.env.SONDEO_OUT, JSON.stringify(informe, null, 2));
  await browser.close();
  process.exit(informe.anomalias.length ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(6); });
