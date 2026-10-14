// @ts-check
/**
 * Oraculo de persistencia: consultas SQL directas al contenedor bury-qa-prd-db.
 * Mecanismo autorizado (mismo que qa-stack.ps1 Invoke-Sql): sqlcmd dentro del contenedor, con
 * la credencial que el propio contenedor ya tiene (nunca se lee ni se imprime aca).
 * Cada llamada verifica DB_NAME() = BuryQA_PRD: jamas toca la base de desarrollo.
 */
const { execFileSync } = require('child_process');

const CONTAINER = 'bury-qa-prd-db';
const DB = 'BuryQA_PRD';
const SEP = '\u0001';

function run(sql) {
    const guarded = `SET NOCOUNT ON;\nIF DB_NAME() <> '${DB}' BEGIN RAISERROR('DB equivocada',16,1); RETURN; END\n${sql}\n`;
    const b64 = Buffer.from(guarded, 'utf8').toString('base64');
    const script = `export SQLCMDPASSWORD=$MSSQL_SA_PASSWORD; echo ${b64} | base64 -d | ` +
        `/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d ${DB} -h -1 -W -b -s $'\x01' -w 8000 -i /dev/stdin`;
    const out = execFileSync('docker', ['exec', CONTAINER, 'bash', '-c', script],
        { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024, windowsHide: true });
    return out.split(/\r?\n/).filter(l => l.length > 0);
}

/** Filas como arrays de strings ('NULL' -> null). */
function rows(sql) {
    return run(sql).map(l => l.split(SEP).map(c => (c === 'NULL' ? null : c)));
}
function scalar(sql) { const r = rows(sql); return r.length ? r[0][0] : null; }
function num(sql) { const v = scalar(sql); return v === null ? null : Number(v); }

/** Estado de un producto por codigo (incluye soft-deleted). */
function producto(codigo) {
    const r = rows(`SELECT TOP 1 Id,Codigo,Nombre,PrecioCompra,PrecioVenta,StockActual,StockMinimo,Activo,IsDeleted,CostoEnvio,PorcentajeIVA,ISNULL(UpdatedBy,'') FROM Productos WHERE Codigo='${esc(codigo)}' ORDER BY Id DESC`);
    if (!r.length) return null;
    const c = r[0];
    return {
        id: Number(c[0]), codigo: c[1], nombre: c[2], precioCompra: Number(c[3]), precioVenta: Number(c[4]),
        stock: Number(c[5]), stockMin: Number(c[6]), activo: c[7] === '1', isDeleted: c[8] === '1',
        costoEnvio: Number(c[9]), iva: Number(c[10]), updatedBy: c[11],
    };
}
function countProductos(codigo) { return num(`SELECT COUNT(*) FROM Productos WHERE Codigo='${esc(codigo)}' AND IsDeleted=0`); }

/** Movimientos de stock del producto, orden cronologico. */
function movimientos(productoId) {
    return rows(`SELECT Id,Tipo,Cantidad,StockAnterior,StockNuevo,ISNULL(Referencia,''),ISNULL(Motivo,''),ISNULL(CreatedBy,'') FROM MovimientosStock WHERE ProductoId=${Number(productoId)} ORDER BY Id`)
        .map(c => ({ id: Number(c[0]), tipo: Number(c[1]), cantidad: Number(c[2]), antes: Number(c[3]), despues: Number(c[4]), ref: c[5], motivo: c[6], by: c[7] }));
}

function esc(s) { return String(s).replace(/'/g, "''"); }

module.exports = { run, rows, scalar, num, producto, countProductos, movimientos, esc, DB };
