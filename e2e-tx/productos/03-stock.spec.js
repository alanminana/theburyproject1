// @ts-check
const { test, expect, evidence } = require('./lib/fx');
const db = require('./lib/db');
const { mk, ajuste } = require('./lib/ops');
const { mem } = require('./lib/mem');

const ok = r => r.status() === 302 && /Kardex/i.test(r.headers()['location'] || '');
const sumaMov = movs => movs.reduce((s, m) => s + (m.tipo === 0 ? m.cantidad : m.tipo === 1 ? -m.cantidad : m.cantidad), 0);
/** Cadena de movimientos consistente: cada StockAnterior == StockNuevo del previo y el saldo final == stock. */
function cadenaOk(movs, stockFinal) {
    let prev = 0;
    for (const m of movs) {
        if (m.antes !== prev) return `mov ${m.id}: antes=${m.antes} != previo ${prev}`;
        prev = m.despues;
    }
    return prev === stockFinal ? null : `saldo ${prev} != stock ${stockFinal}`;
}

test.describe('STOCK', () => {
    

    test('S14 producto con stock inicial controlado', async ({ admin, st }) => {
        mem.S = `${st.prefix}-mem.S`;
        const p = await mk(admin, st, mem.S, { StockActual: '10', StockMinimo: '3' });
        mem.idS = p.id;
        const movs = db.movimientos(mem.idS);
        evidence('S14', { producto: p, movs });
        expect(p.stock).toBe(10);
        expect(movs).toHaveLength(1);
        expect(movs[0]).toMatchObject({ tipo: 0, cantidad: 10, antes: 0, despues: 10 });
        expect(movs[0].ref).toBe('Stock inicial');
    });

    test('S15 registrar entrada', async ({ admin }) => {
        const antes = { stock: db.producto(mem.S).stock, movs: db.movimientos(mem.idS).length };
        const r = await ajuste(admin, mem.idS, 0, 5, { Referencia: 'FAC-QA-1', Motivo: 'Compra QA' });
        expect(ok(r), `status ${r.status()} ${r.headers()['location']}`).toBe(true);
        const movs = db.movimientos(mem.idS);
        evidence('S15', { antes, despues: { stock: db.producto(mem.S).stock, ultimo: movs.at(-1) } });
        expect(db.producto(mem.S).stock).toBe(15);
        expect(movs).toHaveLength(antes.movs + 1);
        expect(movs.at(-1)).toMatchObject({ tipo: 0, cantidad: 5, antes: 10, despues: 15, ref: 'FAC-QA-1', motivo: 'Compra QA', by: 'administrador' });
    });

    test('S16 registrar salida', async ({ admin }) => {
        const r = await ajuste(admin, mem.idS, 1, 4, { Motivo: 'Salida QA' });
        expect(ok(r)).toBe(true);
        const movs = db.movimientos(mem.idS);
        evidence('S16', movs.at(-1));
        expect(db.producto(mem.S).stock).toBe(11);
        expect(movs.at(-1)).toMatchObject({ tipo: 1, cantidad: 4, antes: 15, despues: 11 });
    });

    test('S17 movimientos, kardex y saldo final consistentes', async ({ admin }) => {
        const ajuste20 = await ajuste(admin, mem.idS, 2, 20, { Motivo: 'Conteo fisico' }); // ajuste absoluto
        expect(ok(ajuste20)).toBe(true);
        const movs = db.movimientos(mem.idS);
        evidence('S17', { movs, stock: db.producto(mem.S).stock });
        expect(movs.at(-1)).toMatchObject({ tipo: 2, cantidad: 9, antes: 11, despues: 20 }); // delta registrado
        expect(cadenaOk(movs, 20)).toBeNull();
        expect(db.producto(mem.S).stock).toBe(20);
        expect(10 + 5 - 4 + 9).toBe(sumaMov(movs));
        // Kardex (UI server-rendered) y ListJson reflejan los mismos movimientos
        const kardex = await (await admin.get(`/MovimientoStock/Kardex/${mem.idS}`)).text();
        expect(kardex).toContain('FAC-QA-1');
        const lj = await (await admin.get(`/MovimientoStock/ListJson?productoId=${mem.idS}`)).json();
        evidence('S17-listjson', lj);
        const n = Array.isArray(lj) ? lj.length : (lj.items || lj.data || lj.movimientos || []).length;
        expect(n).toBe(movs.length);
    });

    test('S18 retirar mas unidades de las disponibles', async ({ admin }) => {
        const antes = { stock: db.producto(mem.S).stock, n: db.movimientos(mem.idS).length };
        const r = await ajuste(admin, mem.idS, 1, 1000, { Motivo: 'Exceso' });
        const html = r.status() === 200 ? await r.text() : '';
        evidence('S18', { status: r.status(), mensaje: /Stock insuficiente[^<]*/.exec(html)?.[0] });
        expect(ok(r)).toBe(false);
        expect(db.producto(mem.S).stock).toBe(antes.stock);
        expect(db.movimientos(mem.idS)).toHaveLength(antes.n);
        expect(html).toMatch(/Stock insuficiente/i);
    });

    test('S19 stock cero: salida total, nueva salida rechazada, busquedas', async ({ admin }) => {
        expect(ok(await ajuste(admin, mem.idS, 1, 20, { Motivo: 'Vaciar' }))).toBe(true);
        expect(db.producto(mem.S).stock).toBe(0);
        const n = db.movimientos(mem.idS).length;
        const r = await ajuste(admin, mem.idS, 1, 1, { Motivo: 'Sobre cero' });
        expect(ok(r)).toBe(false);
        expect(db.producto(mem.S).stock).toBe(0);
        expect(db.movimientos(mem.idS)).toHaveLength(n);
        // busqueda para Venta: por defecto solo con stock => no aparece; Cotizacion y Ajuste de stock si
        const venta = await (await admin.get(`/api/ventas/BuscarProductos?term=${encodeURIComponent(mem.S)}`)).json();
        const ventaTodos = await (await admin.get(`/api/ventas/BuscarProductos?term=${encodeURIComponent(mem.S)}&soloConStock=false`)).json();
        const cot = await (await admin.get(`/Cotizacion/BuscarProductos?term=${encodeURIComponent(mem.S)}`)).json();
        const mov = await (await admin.get(`/MovimientoStock/BuscarProductos?term=${encodeURIComponent(mem.S)}`)).json();
        evidence('S19', { venta, ventaTodos, cot, mov });
        expect(venta.find(x => x.id === mem.idS)).toBeUndefined();
        expect(ventaTodos.find(x => x.id === mem.idS)?.stockActual).toBe(0);
        expect(cot.find(x => x.id === mem.idS)?.stockActual).toBe(0);
        expect(mov.find(x => x.id === mem.idS)?.stockActual).toBe(0);
        // entrada desde cero vuelve a habilitar
        expect(ok(await ajuste(admin, mem.idS, 0, 7))).toBe(true);
        const v2 = await (await admin.get(`/api/ventas/BuscarProductos?term=${encodeURIComponent(mem.S)}`)).json();
        expect(v2.find(x => x.id === mem.idS)?.stockActual).toBe(7);
    });

    test('S19b entradas invalidas no producen efectos', async ({ admin }) => {
        const casos = [
            ['cantidad 0 entrada', { t: 0, c: '0' }],
            ['cantidad negativa entrada', { t: 0, c: '-5' }],
            ['cantidad negativa salida', { t: 1, c: '-5' }],
            ['cantidad texto', { t: 0, c: 'abc' }],
            ['cantidad > 999999.99', { t: 0, c: '1000000' }],
            ['ajuste negativo', { t: 2, c: '-1' }],
            ['tipo invalido', { t: 9, c: '1' }],
            ['motivo vacio', { t: 0, c: '1', extra: { Motivo: '' } }],
            ['producto inexistente', { t: 0, c: '1', pid: 99999999 }],
        ];
        const stock0 = db.producto(mem.S).stock, n0 = db.movimientos(mem.idS).length;
        const out = [];
        for (const [nombre, c] of casos) {
            const r = await ajuste(admin, c.pid ?? mem.idS, c.t, c.c, c.extra || {});
            out.push({ nombre, status: r.status(), aceptado: ok(r) });
            expect.soft(ok(r), `entrada invalida aceptada: ${nombre}`).toBe(false);
        }
        evidence('S19b', out);
        expect(db.producto(mem.S).stock).toBe(stock0);
        expect(db.movimientos(mem.idS)).toHaveLength(n0);
    });

    test('S19c cantidad decimal (unidad UN): se acepta fraccion?', async ({ admin, st }) => {
        const D = `${st.prefix}-S19C`;
        const p = await mk(admin, st, D, { StockActual: '5' });
        const r = await ajuste(admin, p.id, 0, '2,5');
        evidence('S19c', { aceptado: ok(r), stock: db.producto(D).stock });
        // Observacion: unidades enteras (UN) con fracciones.
        expect.soft(ok(r) && db.producto(D).stock === 7.5, 'fraccion no soportada/aceptada de forma inconsistente').toBe(true);
    });

    test('S20 consistencia Catalogo vs busqueda de Venta vs DB', async ({ admin, st }) => {
        const filas = [];
        const A = await mk(admin, st, `${st.prefix}-S20A`, { StockActual: '12', PrecioVenta: '3000', PrecioCompra: '1000' });
        const B = await mk(admin, st, `${st.prefix}-S20B`, { StockActual: '0', PrecioVenta: '3100', PrecioCompra: '1000' });
        const C = await mk(admin, st, `${st.prefix}-S20C`, { StockActual: '4', PrecioVenta: '3200', PrecioCompra: '1000', Activo: 'false' });
        await ajuste(admin, A.id, 1, 2); // A: 10
        for (const p of [A, B, C]) {
            const dbp = db.producto(p.codigo);
            const gj = await (await admin.get(`/Producto/GetJson/${p.id}`)).json();
            const info = await (await admin.get(`/MovimientoStock/GetProductoInfo/${p.id}`)).json();
            const venta = await (await admin.get(`/api/ventas/BuscarProductos?term=${p.codigo}&soloConStock=false`)).json();
            const v = venta.find(x => x.id === p.id);
            filas.push({ codigo: p.codigo, db: dbp.stock, getJson: gj.stockActual, info: info.stockActual, venta: v?.stockActual ?? null, activo: dbp.activo, precioDb: dbp.precioVenta, precioVenta: v?.precioVenta ?? null });
            expect(gj.stockActual).toBe(dbp.stock);
            expect(info.stockActual).toBe(dbp.stock);
            if (dbp.activo) { expect(v?.stockActual).toBe(dbp.stock); expect(v?.precioVenta).toBe(dbp.precioVenta); }
            else expect(v, 'producto inactivo aparece en busqueda de Venta').toBeUndefined();
        }
        evidence('S20', filas);
        // filtro con stock de la UI de Catalogo coincide con la API
        const html = await (await admin.get(`/Catalogo?searchTerm=${encodeURIComponent(st.prefix + '-S20')}`)).text();
        expect(html).toContain(`${st.prefix}-S20A`);
    });

    test('S21a entradas repetidas legitimas (5x) -> 5 movimientos sin perdidas', async ({ admin, st }) => {
        const R = `${st.prefix}-S21A`;
        const p = await mk(admin, st, R, { StockActual: '0' });
        for (let i = 0; i < 5; i++) expect(ok(await ajuste(admin, p.id, 0, 1, { Referencia: `REP-${i}` }))).toBe(true);
        const movs = db.movimientos(p.id);
        evidence('S21a', movs);
        expect(movs).toHaveLength(5);
        expect(db.producto(R).stock).toBe(5);
        expect(cadenaOk(movs, 5)).toBeNull();
    });

    test('S21b doble envio simultaneo del mismo formulario (idempotencia)', async ({ admin, st }) => {
        const R = `${st.prefix}-S21B`;
        const p = await mk(admin, st, R, { StockActual: '0' });
        const rs = await Promise.all([1, 2, 3].map(() => ajuste(admin, p.id, 0, 5, { Referencia: 'DOBLE-CLICK' })));
        const movs = db.movimientos(p.id);
        evidence('S21b', { statuses: rs.map(r => r.status()), movs, stock: db.producto(R).stock });
        expect(cadenaOk(movs, db.producto(R).stock)).toBeNull(); // integridad pase lo que pase
        expect.soft(movs.length, `3 envios identicos simultaneos generaron ${movs.length} movimientos (sin proteccion contra doble envio)`).toBe(1);
    });

    test('S21c salidas concurrentes no dejan stock negativo ni sobreventa', async ({ admin, st }) => {
        const R = `${st.prefix}-S21C`;
        const p = await mk(admin, st, R, { StockActual: '5' });
        const rs = await Promise.all([1, 2, 3, 4].map(() => ajuste(admin, p.id, 1, 3, { Referencia: 'RACE' })));
        const exitos = rs.filter(ok).length;
        const movs = db.movimientos(p.id);
        const stock = db.producto(R).stock;
        evidence('S21c', { exitos, statuses: rs.map(r => r.status()), movs, stock });
        expect(stock).toBeGreaterThanOrEqual(0);
        expect(cadenaOk(movs, stock)).toBeNull();
        expect(5 - 3 * exitos).toBe(stock);
        expect(exitos).toBeLessThanOrEqual(1);
    });

    test('S22a permisos: lector (movimientos.view) no deberia registrar movimientos', async ({ lector, admin, st }) => {
        const p = await mk(admin, st, `${st.prefix}-S22A`, { StockActual: '10' });
        const r = await ajuste(lector, p.id, 1, 3, { Motivo: 'lector' });
        const stock = db.producto(p.codigo).stock;
        evidence('S22a', { status: r.status(), location: r.headers()['location'], stock, movs: db.movimientos(p.id).length });
        expect.soft(stock, `usuario con SOLO movimientos.view registro una salida (stock ${stock}); la accion de escritura exige solo 'view'`).toBe(10);
    });

    test('S22b permisos: oper (movimientos.create) si registra', async ({ oper, admin, st }) => {
        const p = await mk(admin, st, `${st.prefix}-S22B`, { StockActual: '10' });
        const r = await ajuste(oper, p.id, 1, 3, { Motivo: 'oper' });
        expect(ok(r)).toBe(true);
        expect(db.producto(p.codigo).stock).toBe(7);
        expect(db.movimientos(p.id).at(-1).by).toBe('qaprd_oper');
    });
});
