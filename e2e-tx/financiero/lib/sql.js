// @ts-check
/**
 * Acceso SQL ACOTADO a BuryQA_FIN vía `docker exec bury-qa-fin-db` (sqlcmd dentro del contenedor, sin exponer
 * credenciales). Antes de cualquier uso se verifica contenedor + DB_NAME() = BuryQA_FIN.
 * Uso permitido: LECTURA de verificación y la retrodatación documentada de FechaVencimiento (simulación de
 * paso del tiempo sin tocar el reloj del SO). Jamás sobre TheBuryProjectDb ni otro stack.
 */
const { execFileSync } = require('child_process');

const CONTENEDOR = 'bury-qa-fin-db';
const DB = 'BuryQA_FIN';

function crudo(query) {
    const b64 = Buffer.from(query, 'utf8').toString('base64');
    const script = `export SQLCMDPASSWORD=$MSSQL_SA_PASSWORD; echo ${b64} | base64 -d | /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d ${DB} -s '|' -W -b -i /dev/stdin`;
    return execFileSync('docker', ['exec', CONTENEDOR, 'bash', '-c', script], { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
}

/** @returns {Record<string, string|null>[]} filas del PRIMER result set */
function filas(query) {
    const out = crudo(query).split(/\r?\n/);
    const h = out.findIndex(l => l.trim() !== '');
    if (h < 0) return [];
    const cols = out[h].split('|').map(c => c.trim());
    const res = [];
    for (let i = h + 2; i < out.length; i++) {
        const l = out[i];
        if (!l.trim() || l.startsWith('(')) break;
        const v = l.split('|').map(x => x.trim());
        res.push(Object.fromEntries(cols.map((c, k) => [c, v[k] === 'NULL' ? null : v[k]])));
    }
    return res;
}

const uno = q => filas(q)[0] ?? null;
const num = (q, col) => { const r = uno(q); const k = col ?? Object.keys(r ?? {})[0]; return r && r[k] != null ? Number(r[k]) : NaN; };

function verificarIdentidad() {
    const r = uno('SELECT DB_NAME() AS db, @@SERVERNAME AS srv');
    if (!r || r.db !== DB) throw new Error(`Identidad SQL inesperada: ${JSON.stringify(r)} (se esperaba ${DB})`);
    return r;
}

function ejecutar(query) { return crudo(query); }

module.exports = { filas, uno, num, ejecutar, verificarIdentidad, DB, CONTENEDOR };
