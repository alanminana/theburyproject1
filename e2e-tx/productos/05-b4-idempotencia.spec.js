// @ts-check
/**
 * QA-PRD-P1 B4 (remediación) en SQL Server real (BuryQA_PRD), con los formularios MVC reales:
 * el RequestId y el token se leen del HTML que renderiza la ficha de inventario, no se inventan.
 * Ejecutar SIEMPRE con qa-run.ps1 prd -- npx playwright test -c e2e-tx/productos/playwright.config.js 05-b4-idempotencia
 */
const { test, expect, evidence } = require('./lib/fx');
const db = require('./lib/db');
const crypto = require('crypto');
const { mk } = require('./lib/ops');

const RE_REQ = /name="RequestId"\s+value="([0-9a-f-]{36})"/gi;
const requestIds = html => [...html.matchAll(RE_REQ)].map(m => m[1].toLowerCase());
const movsDe = (id, req) => db.rows(
    `SELECT Id,Tipo,Cantidad,StockAnterior,StockNuevo,ISNULL(Motivo,''),CONVERT(varchar(36),RequestId) FROM MovimientosStock WHERE ProductoId=${Number(id)}` +
    (req ? ` AND RequestId='${req}'` : '') + ' ORDER BY Id');
const stockDe = id => db.num(`SELECT StockActual FROM Productos WHERE Id=${Number(id)}`);

function campos(productoId, tipo, cantidad, requestId, extra = {}) {
    return {
        ProductoId: String(productoId), Tipo: tipo, Cantidad: String(cantidad), Motivo: 'B4 E2E',
        Referencia: 'B4-E2E', RequestId: requestId, ...extra,
    };
}

test.describe.configure({ mode: 'serial' });

test.describe('B4 idempotencia (SQL Server real, formularios MVC)', () => {
    /** @type {{id:number, codigo:string}} */
    let p;
    let ficha;

    test('B01 los formularios inline de la ficha traen un RequestId y la migración está aplicada', async ({ admin, st }) => {
        const col = db.num("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('MovimientosStock') AND name='RequestId'");
        const idx = db.num("SELECT COUNT(*) FROM sys.indexes WHERE name='IX_MovimientosStock_RequestId' AND is_unique=1 AND filter_definition IS NOT NULL");
        const mig = db.num("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId='20261014175359_QaPrdP1MovimientoStockRequestId'");
        expect({ col, idx, mig }).toEqual({ col: 1, idx: 1, mig: 1 });

        const prod = await mk(admin, st, `${st.prefix}-B4`, { StockActual: '10', StockMinimo: '0' });
        p = { id: prod.id, codigo: prod.codigo };
        const r = await admin.raw.get(`/Producto/Inventario/${p.id}?tab=unidades`);
        expect(r.status()).toBe(200);
        ficha = await r.text();
        const ids = requestIds(ficha);
        evidence('B4-B01', { ids, producto: p });
        expect(ids.length).toBeGreaterThanOrEqual(1);
        expect(new Set(ids).size).toBe(ids.length); // un identificador por formulario
    });

    test('B02 ocho envíos simultáneos con el mismo RequestId -> un solo movimiento y un solo cambio de stock', async ({ admin }) => {
        const req = requestIds(ficha)[0];
        const antes = { movs: movsDe(p.id).length, stock: stockDe(p.id) };
        const rs = await Promise.all(Array.from({ length: 8 }, () =>
            admin.form('/MovimientoStock/Create', campos(p.id, 'Entrada', 5, req))));
        const estados = rs.map(r => r.status());
        const movs = movsDe(p.id, req);
        evidence('B4-B02', { antes, estados, movs, stock: stockDe(p.id) });
        expect(movs).toHaveLength(1);
        expect(stockDe(p.id)).toBe(antes.stock + 5);
        expect(movsDe(p.id)).toHaveLength(antes.movs + 1);
        // Ninguna respuesta es un 500: o son redirect de éxito o un rechazo controlado.
        for (const s of estados) expect([200, 302]).toContain(s);
        expect(estados.filter(s => s === 302).length).toBeGreaterThanOrEqual(1);
    });

    test('B03 respuesta perdida: reenviar el mismo formulario no duplica', async ({ admin }) => {
        const req = requestIds(ficha)[0];
        const antes = { movs: movsDe(p.id).length, stock: stockDe(p.id) };
        for (let i = 0; i < 3; i++) await admin.form('/MovimientoStock/Create', campos(p.id, 'Entrada', 5, req));
        evidence('B4-B03', { antes, despues: { movs: movsDe(p.id).length, stock: stockDe(p.id) } });
        expect(movsDe(p.id).length).toBe(antes.movs);
        expect(stockDe(p.id)).toBe(antes.stock);
    });

    test('B04 mismo RequestId con otra cantidad o motivo se rechaza y no altera nada', async ({ admin }) => {
        const req = requestIds(ficha)[0];
        const antes = { movs: movsDe(p.id).length, stock: stockDe(p.id) };
        const otraCantidad = await admin.form('/MovimientoStock/Create', campos(p.id, 'Entrada', 8, req));
        const otroMotivo = await admin.form('/MovimientoStock/Create', campos(p.id, 'Entrada', 5, req, { Motivo: 'Otro motivo' }));
        const otroTipo = await admin.form('/MovimientoStock/Create', campos(p.id, 'Salida', 5, req));
        for (const r of [otraCantidad, otroMotivo, otroTipo]) {
            expect(r.status(), 'debe re-renderizar el formulario con el error, no redirigir como éxito').toBe(200);
            expect(await r.text()).toMatch(/ya se us(ó|&#xF3;) en otra operaci(ó|&#xF3;)n/);
        }
        evidence('B4-B04', { antes, despues: { movs: movsDe(p.id).length, stock: stockDe(p.id) } });
        expect(movsDe(p.id).length).toBe(antes.movs);
        expect(stockDe(p.id)).toBe(antes.stock);
    });

    test('B05 RequestId distintos con los mismos datos son movimientos legítimos', async ({ admin }) => {
        const antes = { movs: movsDe(p.id).length, stock: stockDe(p.id) };
        const a = crypto.randomUUID(); const b = crypto.randomUUID();
        await admin.form('/MovimientoStock/Create', campos(p.id, 'Entrada', 2, a));
        await admin.form('/MovimientoStock/Create', campos(p.id, 'Entrada', 2, b));
        expect(movsDe(p.id).length).toBe(antes.movs + 2);
        expect(stockDe(p.id)).toBe(antes.stock + 4);
    });

    test('B06 error de negocio en el form inline conserva el RequestId y el reintento corregido se aplica una sola vez', async ({ admin }) => {
        const req = crypto.randomUUID();
        const volver = `/Producto/Inventario/${p.id}?tab=movimientos`;
        const antes = { movs: movsDe(p.id).length, stock: stockDe(p.id) };

        const fallo = await admin.form('/MovimientoStock/Create', campos(p.id, 'Salida', 99999, req, { ReturnUrl: volver }));
        expect(fallo.status()).toBe(302);
        // Sin variación parcial: ni movimiento ni stock cambiaron.
        expect(movsDe(p.id).length).toBe(antes.movs);
        expect(stockDe(p.id)).toBe(antes.stock);

        const pagina = await admin.raw.get(volver);
        const ids = requestIds(await pagina.text());
        evidence('B4-B06', { req, idsEnLaPaginaTrasElError: ids });
        expect(ids.length).toBeGreaterThanOrEqual(1);
        for (const id of ids) expect(id).toBe(req); // el identificador NO se regeneró

        // Reintento corregido con el mismo RequestId, enviado dos veces (doble clic).
        await admin.form('/MovimientoStock/Create', campos(p.id, 'Salida', 3, req, { ReturnUrl: volver }));
        await admin.form('/MovimientoStock/Create', campos(p.id, 'Salida', 3, req, { ReturnUrl: volver }));
        expect(movsDe(p.id, req)).toHaveLength(1);
        expect(stockDe(p.id)).toBe(antes.stock - 3);

        // Tras el éxito, el formulario vuelve a renderizar un identificador nuevo.
        const despues = requestIds(await (await admin.raw.get(volver)).text());
        for (const id of despues) expect(id).not.toBe(req);
    });

    test('B07 CSRF y permisos de /MovimientoStock/Create', async ({ admin, lector, playwright }) => {
        const antes = { movs: movsDe(p.id).length, stock: stockDe(p.id) };
        const req = crypto.randomUUID();

        // Sin token antiforgery: 400.
        const anon = await playwright.request.newContext({ baseURL: 'http://127.0.0.1:18092', storageState: undefined });
        const sinSesion = await anon.post('/MovimientoStock/Create', { form: campos(p.id, 'Entrada', 1, req), maxRedirects: 0 });
        await anon.dispose();
        expect([302, 400, 401, 403]).toContain(sinSesion.status());

        const sinToken = await admin.raw.post('/MovimientoStock/Create', { form: campos(p.id, 'Entrada', 1, req), maxRedirects: 0 });
        expect(sinToken.status()).toBe(400);

        // Lector (sin movimientos.create): denegado, sin efecto.
        const lec = await lector.form('/MovimientoStock/Create', campos(p.id, 'Entrada', 1, req));
        expect([302, 403]).toContain(lec.status());
        if (lec.status() === 302) expect(lec.headers()['location'] || '').toMatch(/AccessDenied|Login|Denegado/i);

        evidence('B4-B07', { sinSesion: sinSesion.status(), sinToken: sinToken.status(), lector: lec.status() });
        expect(movsDe(p.id).length).toBe(antes.movs);
        expect(stockDe(p.id)).toBe(antes.stock);
    });

    test('B08 compatibilidad: sin RequestId cada envío es un movimiento y los históricos siguen legibles', async ({ admin }) => {
        const antes = { movs: movsDe(p.id).length, stock: stockDe(p.id) };
        await admin.form('/MovimientoStock/Create', { ProductoId: String(p.id), Tipo: 'Entrada', Cantidad: '1', Motivo: 'B4 sin id', Referencia: 'B4' });
        await admin.form('/MovimientoStock/Create', { ProductoId: String(p.id), Tipo: 'Entrada', Cantidad: '1', Motivo: 'B4 sin id', Referencia: 'B4' });
        expect(movsDe(p.id).length).toBe(antes.movs + 2);
        expect(stockDe(p.id)).toBe(antes.stock + 2);
        const nulos = db.num(`SELECT COUNT(*) FROM MovimientosStock WHERE ProductoId=${p.id} AND RequestId IS NULL`);
        expect(nulos).toBeGreaterThanOrEqual(3); // stock inicial + los dos sin identificador
        const kardex = await admin.raw.get(`/MovimientoStock/Kardex/${p.id}`);
        expect(kardex.status()).toBe(200);
    });
});
