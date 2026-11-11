#!/usr/bin/env node
// Auto-prueba del GATE de CSP (F2.3): demuestra que resumen.js / preload.js / crawl.js / impresion.js NO dan verde ante una medicion
// vacia, truncada, corrupta o incompleta, y que SI pasan con datos sanos. Sale con 1 si algun caso no se comporta como se espera.
// Uso:
//   node scripts/qa/csp-medicion/selftest.js                       # casos sobre archivos sinteticos (sin navegador, <1 s)
//   node scripts/qa/csp-medicion/selftest.js --con-navegador       # ademas: preload.js real + servidor local con CSP + crawl.js/impresion.js
//   node scripts/qa/csp-medicion/selftest.js --resumen <ruta.js>   # prueba otra version de resumen.js (p. ej. la anterior, para mostrar el defecto)
// Como se detectan "contextos que debian medirse y no se midieron": (a) preload.js registra un evento `contexto-creado` por cada contexto y otro
// `contexto` cuando la instrumentacion quedo instalada; resumen.js falla si creados > instrumentados; (b) con --playwright-json compara los
// contextos medidos contra los tests ejecutados (minimo --min-ratio, 0.5 por defecto): un spec que deja de medirse baja el cociente.
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const http = require('http');
const { spawnSync, spawn } = require('child_process');
// El servidor local vive en ESTE proceso: los hijos que lo consultan deben lanzarse en asincrono (spawnSync bloquearia el servidor).
const spawnAsync = (cmd, args, opts) => new Promise((res) => { const h = spawn(cmd, args, opts); let stdout = ''; let stderr = ''; h.stdout.on('data', (d) => { stdout += d; }); h.stderr.on('data', (d) => { stderr += d; }); const t = setTimeout(() => h.kill(), 120000); h.on('close', (status) => { clearTimeout(t); res({ status, stdout, stderr }); }); });

const arg = (n) => { const i = process.argv.indexOf(n); return i > -1 ? process.argv[i + 1] : null; };
const RESUMEN = path.resolve(arg('--resumen') || path.join(__dirname, 'resumen.js'));
const conNavegador = process.argv.includes('--con-navegador');
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'csp-selftest-'));

const L = (...o) => o.map((x) => JSON.stringify(x)).join('\n') + '\n';
const ctx = (n) => Array.from({ length: n }, (_, i) => [{ t: 'contexto-creado', pid: 1 + i }, { t: 'contexto', pid: 1 + i }]).flat();
const sano = L(...ctx(3));
const viol = { t: 'violacion', directiva: 'script-src-elem', bloqueado: 'inline', ruta: '/x', disposicion: 'report' };
const escribir = (nombre, contenido) => { const f = path.join(tmp, nombre); fs.writeFileSync(f, contenido); return f; };

const casos = [];
const caso = (nombre, esperado, args, setup) => casos.push({ nombre, esperado, args, setup });

caso('datos sanos (3 contextos, 0 violaciones)', 0, ['--max-violaciones', '0'], () => escribir('a.jsonl', sano));
caso('la instrumentacion no se instala: no existe el archivo', 3, ['--max-violaciones', '0'], () => path.join(tmp, 'no-existe.jsonl'));
caso('resumen vacio (archivo de 0 bytes)', 3, ['--max-violaciones', '0'], () => escribir('b.jsonl', ''));
caso('resumen sin contextos (solo ruido/pageerror)', 3, ['--max-violaciones', '0'], () => escribir('c.jsonl', L({ t: 'pageerror', mensaje: 'x', pagina: '/' })));
caso('se interrumpe: ultima linea truncada', 3, ['--max-violaciones', '0'], () => escribir('d.jsonl', sano + '{"t":"violacion","direct'));
caso('linea corrupta en el medio (podria esconder una violacion)', 3, ['--max-violaciones', '0'], () => escribir('e.jsonl', L(...ctx(1)) + 'basura{{\n' + L(...ctx(2))));
caso('fallo de instalacion de la instrumentacion', 3, ['--max-violaciones', '0'], () => escribir('f.jsonl', L(...ctx(2), { t: 'fallo-instrumentacion', mensaje: 'closed', pid: 9 })));
caso('contextos creados sin instrumentar (3 creados, 2 medidos)', 3, ['--max-violaciones', '0'], () => escribir('g.jsonl', L(...ctx(2), { t: 'contexto-creado', pid: 7 })));
caso('1 violacion CSP', 1, ['--max-violaciones', '0'], () => escribir('h.jsonl', L(...ctx(3), viol)));
caso('bloqueo CSP que solo dejo rastro en la consola', 1, ['--max-violaciones', '0'], () => escribir('i.jsonl', L(...ctx(3), { t: 'consola-csp', mensaje: 'Refused to ...', pagina: '/' })));
caso('muy pocos contextos para los tests ejecutados (2 de 100)', 3, ['--max-violaciones', '0', '--playwright-json', 'PW100'], () => { escribir('pw100.json', JSON.stringify({ stats: { expected: 100, unexpected: 0, flaky: 0, skipped: 0 } })); return escribir('j.jsonl', L(...ctx(2))); });
caso('Playwright reporta 0 tests ejecutados', 3, ['--max-violaciones', '0', '--playwright-json', 'PW0'], () => { escribir('pw0.json', JSON.stringify({ stats: { expected: 0, unexpected: 0, flaky: 0, skipped: 5 } })); return escribir('k.jsonl', L(...ctx(3))); });
caso('reporte de Playwright ilegible', 3, ['--max-violaciones', '0', '--playwright-json', path.join(tmp, 'no-hay.json')], () => escribir('l.jsonl', sano));
caso('contextos suficientes para los tests ejecutados', 0, ['--max-violaciones', '0', '--playwright-json', 'PW6'], () => { escribir('pw6.json', JSON.stringify({ stats: { expected: 6, unexpected: 0, flaky: 0, skipped: 0 } })); return escribir('m.jsonl', sano); });
caso('errores de pagina con el gate opt-in --max-pageerrors 0', 1, ['--max-violaciones', '0', '--max-pageerrors', '0'], () => escribir('n.jsonl', L(...ctx(3), { t: 'pageerror', mensaje: 'boom', pagina: '/' })));
caso('umbral invalido (NaN) es error de uso, no verde', 2, ['--max-violaciones', 'abc'], () => escribir('o.jsonl', sano));
caso('sin umbral, 1 violacion solo informa (comportamiento documentado)', 0, [], () => escribir('p.jsonl', L(...ctx(3), viol)));

const corre = (cmd, args, env = {}) => spawnSync(process.execPath, [cmd, ...args], { encoding: 'utf8', env: { ...process.env, ...env }, timeout: 120000 });
const resultados = [];
const registrar = (nombre, esperado, real, detalle = '') => { const ok = real === esperado; resultados.push(ok); console.log(`${ok ? 'OK   ' : 'FALLA'} ${nombre} | esperado exit ${esperado}, real ${real}${detalle}`); };

for (const c of casos) {
  const archivo = c.setup();
  const args = c.args.map((a) => (/^PW/.test(a) ? path.join(tmp, a.toLowerCase() + '.json') : a));
  const r = corre(RESUMEN, [archivo, ...args]);
  registrar(c.nombre, c.esperado, r.status);
}

async function conNav() {
  const { chromium } = require('playwright');
  // Servidor local con CSP (Enforce) y una pagina con script inline SIN nonce (violacion segura y controlada) + paginas para crawl/impresion.
  const srv = http.createServer((q, r) => {
    const csp = { 'content-security-policy': "default-src 'self'; script-src 'self' 'nonce-abc'" };
    if (q.url === '/viol') { r.writeHead(200, { 'content-type': 'text/html', ...csp }); return r.end('<script>window.x=1</script><p>hola</p>'); }
    if (q.url === '/limpia') { r.writeHead(200, { 'content-type': 'text/html', ...csp }); return r.end('<p>sin scripts</p>'); }
    if (q.url.startsWith('/Identity/Account/Login') && q.method === 'GET') { r.writeHead(200, { 'content-type': 'text/html' }); return r.end('<form method=post><input id=Input_UserName name="Input.UserName"><input id=input-password name="Input.Password"><button type=submit>ok</button></form>'); }
    if (q.url.startsWith('/Identity/Account/Login')) { r.writeHead(302, { location: '/' }); return r.end(); }
    if (q.url === '/sin-botones') { r.writeHead(200, { 'content-type': 'text/html' }); return r.end('<p>sin imprimir ni volver</p>'); }
    r.writeHead(200, { 'content-type': 'text/html' }); r.end('<p>inicio sin enlaces ni menu</p>'); // inventario vacio
  });
  await new Promise((res) => srv.listen(0, '127.0.0.1', res));
  const base = `http://127.0.0.1:${srv.address().port}`;
  const preload = path.join(__dirname, 'preload.js');
  const nodeCon = (script, env) => spawnAsync(process.execPath, ['--require', preload, '-e', script], { env: { ...process.env, ...env, NODE_PATH: path.join(__dirname, '..', '..', '..', 'node_modules') } });
  const visitar = (ruta) => `(async()=>{const {chromium}=require('playwright-core');const b=await chromium.launch();const c=await b.newContext();const p=await c.newPage();await p.goto('${base}${ruta}');await p.waitForTimeout(500);await b.close();})()`;

  // preload real: instalacion + violacion detectada + gate
  const outV = path.join(tmp, 'real-viol.jsonl');
  let r = await nodeCon(visitar('/viol'), { CSP_MEDIR_OUT: outV });
  const evs = fs.existsSync(outV) ? fs.readFileSync(outV, 'utf8') : '';
  registrar('preload real: instala la medicion (contexto-creado y contexto registrados)', 1, /"t":"contexto-creado"/.test(evs) && /"t":"contexto"/.test(evs) ? 1 : 0, ` (salida ${r.status})`);
  registrar('preload real: una pagina con script inline sin nonce produce una violacion en el archivo', 1, /"t":"violacion"/.test(evs) ? 1 : 0);
  registrar('gate real: esa violacion hace fallar resumen.js', 1, corre(RESUMEN, [outV, '--max-violaciones', '0']).status);
  const outL = path.join(tmp, 'real-limpia.jsonl');
  await nodeCon(visitar('/limpia'), { CSP_MEDIR_OUT: outL });
  registrar('gate real: una pagina limpia pasa', 0, corre(RESUMEN, [outL, '--max-violaciones', '0']).status);
  // sin CSP_MEDIR_OUT el preload no hace nada: el gate debe fallar por archivo ausente
  const outN = path.join(tmp, 'real-no-engancha.jsonl');
  await nodeCon(visitar('/limpia'), { CSP_MEDIR_OUT: '' });
  registrar('gate real: preload sin CSP_MEDIR_OUT (no se engancha) deja el gate en rojo', 3, corre(RESUMEN, [outN, '--max-violaciones', '0']).status);

  // crawl.js: inventario vacio (login ok, sin enlaces) no debe dar verde
  const crawl = path.join(__dirname, '..', 'csp-roles', 'crawl.js');
  r = await spawnAsync(process.execPath, [crawl, '--roles', 'administrador'], { env: { ...process.env, E2E_BASE_URL: base } });
  registrar('crawl.js: inventario vacio / 0 paginas visitadas NO es verde', 1, r.status, ` | ${(r.stdout || '').split('\n').filter((l) => /administrador|TOTAL/.test(l)).join(' / ').slice(0, 140)}`);
  // impresion.js: pagina sin botones Imprimir/Volver, y sin --rutas
  const imp = path.join(__dirname, '..', 'csp-roles', 'impresion.js');
  r = await spawnAsync(process.execPath, [imp, '--rutas', '/sin-botones'], { env: { ...process.env, E2E_BASE_URL: base } });
  registrar('impresion.js: botones ausentes NO es verde', 1, r.status);
  r = await spawnAsync(process.execPath, [imp], { env: { ...process.env, E2E_BASE_URL: base } });
  registrar('impresion.js: sin --rutas (inventario vacio) NO es verde', 2, r.status);
  srv.close();
}

(async () => {
  if (conNavegador) await conNav();
  const mal = resultados.filter((x) => !x).length;
  console.log(`SELFTEST gate CSP: ${resultados.length} casos, ${resultados.length - mal} correctos, ${mal} con falsos verdes/rojos (resumen=${path.relative(process.cwd(), RESUMEN)}${conNavegador ? ', con navegador' : ''})`);
  fs.rmSync(tmp, { recursive: true, force: true });
  process.exit(mal ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(1); });
