// @ts-check
/**
 * Registro de verificaciones con clasificación PASS / FAIL / BLOCKED y reporte final (JSON + Markdown).
 * Las verificaciones NO lanzan: cada una queda registrada y la suite continúa, para que un defecto no
 * oculte el resto del recorrido financiero.
 */
const fs = require('fs');
const path = require('path');

const DIR = path.join(__dirname, '..', 'evidencia');
fs.mkdirSync(DIR, { recursive: true });

/** @type {{paso:string,desc:string,esperado:any,real:any,estado:'PASS'|'FAIL'|'BLOCKED',diferencia?:number,nota?:string,evidencia?:string[]}[]} */
const filas = [];
/** @type {{ts:string,op:string,detalle?:any}[]} */
const operaciones = [];
/** @type {Record<string, any>} */
const datos = {};

const f = v => (typeof v === 'number' ? v.toFixed(2) : JSON.stringify(v));

function op(operacion, detalle) { operaciones.push({ ts: new Date().toISOString(), op: operacion, detalle }); }

/**
 * @param {string} paso  id del paso de la consigna (p. ej. "10")
 * @param {string} desc
 * @param {any} esperado
 * @param {any} real
 * @param {{tol?:number,nota?:string,evidencia?:string[],severidad?:string}} [o]
 */
function chk(paso, desc, esperado, real, o = {}) {
    let ok;
    let dif;
    if (typeof esperado === 'number' && typeof real === 'number') {
        dif = Math.round((real - esperado) * 100) / 100;
        ok = Math.abs(real - esperado) <= (o.tol ?? 0.005);
    } else {
        ok = JSON.stringify(esperado) === JSON.stringify(real);
    }
    filas.push({ paso, desc, esperado, real, estado: ok ? 'PASS' : 'FAIL', diferencia: dif, nota: o.nota, evidencia: o.evidencia });
    return ok;
}

function bloqueado(paso, desc, motivo, evidencia) {
    filas.push({ paso, desc, esperado: 'verificable', real: 'no verificable', estado: 'BLOCKED', nota: motivo, evidencia });
}

function falla(paso, desc, esperado, real, nota, evidencia) {
    filas.push({ paso, desc, esperado, real, estado: 'FAIL', nota, evidencia });
}

function escribir() {
    const resumen = { PASS: 0, FAIL: 0, BLOCKED: 0 };
    for (const r of filas) resumen[r.estado]++;
    fs.writeFileSync(path.join(DIR, 'reporte-financiero.json'), JSON.stringify({ generado: new Date().toISOString(), resumen, datos, filas, operaciones }, null, 2));
    const L = [];
    L.push('# Reporte QA financiero — TheBuryProject (stack bury-qa-fin, http://127.0.0.1:18093)');
    L.push('');
    L.push(`Generado: ${new Date().toISOString()}  |  PASS ${resumen.PASS} · FAIL ${resumen.FAIL} · BLOCKED ${resumen.BLOCKED}`);
    L.push('');
    L.push('## Datos afectados');
    L.push('```json'); L.push(JSON.stringify(datos, null, 2)); L.push('```');
    L.push('');
    L.push('## Verificaciones');
    L.push('| Paso | Verificación | Esperado | Real | Dif. | Estado | Nota |');
    L.push('|---|---|---|---|---|---|---|');
    const esc = s => String(s ?? '').replace(/\|/g, '\\|').replace(/\n/g, ' ');
    for (const r of filas) {
        L.push(`| ${r.paso} | ${esc(r.desc)} | ${esc(f(r.esperado))} | ${esc(f(r.real))} | ${r.diferencia ?? ''} | **${r.estado}** | ${esc(r.nota ?? '')}${r.evidencia?.length ? ' · ev: ' + r.evidencia.join(', ') : ''} |`);
    }
    L.push('');
    L.push('## Operaciones realizadas');
    for (const o of operaciones) L.push(`- ${o.ts} — ${o.op}${o.detalle !== undefined ? ' — `' + esc(JSON.stringify(o.detalle)).slice(0, 220) + '`' : ''}`);
    fs.writeFileSync(path.join(DIR, 'reporte-financiero.md'), L.join('\n'));
    return resumen;
}

module.exports = { chk, bloqueado, falla, op, datos, filas, escribir, DIR };
