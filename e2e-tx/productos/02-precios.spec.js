// @ts-check
const { test, expect, evidence } = require('./lib/fx');
const db = require('./lib/db');
const { mk, precio, eventosDe } = require('./lib/ops');
const { mem } = require('./lib/mem');

const fmt = n => String(n).replace('.', ',');
async function editForm(admin, id, over = {}) {
    const j = await (await admin.get(`/Producto/GetJson/${id}`)).json();
    return {
        Id: String(j.id), RowVersion: j.rowVersion, Codigo: j.codigo, Nombre: j.nombre, Descripcion: j.descripcion ?? '',
        CategoriaId: String(j.categoriaId), MarcaId: String(j.marcaId),
        PrecioCompra: fmt(j.precioCompra), PrecioVenta: fmt(j.precioVenta), PorcentajeIVA: fmt(j.porcentajeIVA),
        CostoEnvio: fmt(j.costoEnvio), PercepcionesCompra: fmt(j.percepcionesCompra), OtrosCostosCompra: fmt(j.otrosCostosCompra),
        ComisionPorcentaje: fmt(j.comisionPorcentaje), StockActual: fmt(j.stockActual), StockMinimo: fmt(j.stockMinimo),
        Activo: String(j.activo), ...over,
    };
}

test.describe('PRECIOS', () => {
    

    test('PR07 cambiar precio de venta (+10%)', async ({ admin, st }) => {
        mem.B = `${st.prefix}-PR`;
        const p0 = await mk(admin, st, mem.B, { PrecioCompra: '1000', PrecioVenta: '2000' });
        mem.idB = p0.id;
        const antes = { prod: db.producto(mem.B), eventos: eventosDe(mem.idB) };
        const r = await precio(admin, mem.idB, 10);
        const j = await r.json();
        expect(r.status(), JSON.stringify(j)).toBe(200);
        expect(j.exitoso).toBe(true);
        const despues = { prod: db.producto(mem.B), eventos: eventosDe(mem.idB) };
        evidence('PR07', { antes, resp: j, despues });
        expect(despues.prod.precioVenta).toBe(2200);
        expect(despues.prod.precioCompra).toBe(1000); // el costo no se toca
        expect(despues.eventos).toHaveLength(1);
        expect(despues.eventos[0]).toMatchObject({ anterior: 2000, nuevo: 2200, alcance: 'individual' }); // 1 producto => el sistema lo clasifica como individual
        mem.evento1 = despues.eventos[0].eventoId;
    });

    test('PR08 precio actualizado visible en catalogo', async ({ admin }) => {
        // PR11 revierte despues; aqui el precio vigente es 2200 (la reversion corre mas tarde).
        const html = await (await admin.get(`/Catalogo?searchTerm=${encodeURIComponent(mem.B)}`)).text();
        const m = html.match(new RegExp(`data-producto-codigo="${mem.B}"[^>]*?data-sort-precio="([^"]+)"`));
        evidence('PR08', { filaPrecio: m && m[1] });
        expect(m, 'fila del producto en el catalogo').not.toBeNull();
        expect(Number(m[1])).toBe(2200);
        const j = await (await admin.get(`/Producto/GetJson/${mem.idB}`)).json();
        expect(j.precioVenta).toBe(2200);
        expect(j.precioActual).toBe(2200);
    });

    test('PR09 precio nuevo aparece en cotizacion (y en busqueda de Venta)', async ({ admin }) => {
        const cot = await (await admin.get(`/Cotizacion/BuscarProductos?term=${encodeURIComponent(mem.B)}`)).json();
        const ven = await (await admin.get(`/api/ventas/BuscarProductos?term=${encodeURIComponent(mem.B)}&soloConStock=false`)).json();
        evidence('PR09', { cot, ven });
        expect(cot.find(x => x.id === mem.idB)?.precioVenta).toBe(2200);
        expect(ven.find(x => x.id === mem.idB)?.precioVenta).toBe(2200);
    });

    test('PR10 historial de cambios (evento de ajuste directo)', async ({ admin }) => {
        const h = await (await admin.get(`/Catalogo/HistorialPrecioProductoApi?productoId=${mem.idB}`)).json();
        evidence('PR10', h);
        const it = h.items.find(i => i.eventoId === mem.evento1);
        expect(it).toBeTruthy();
        expect(it).toMatchObject({ precioAnterior: 2000, precioNuevo: 2200, puedeRevertir: true });
        expect(it.usuario).toBeTruthy();
        // y tambien existe en PreciosHistoricos
        expect(db.num(`SELECT COUNT(*) FROM PreciosHistoricos WHERE ProductoId=${mem.idB} AND PrecioVentaAnterior=2000 AND PrecioVentaNuevo=2200`)).toBe(1);
    });

    test('PR10b cambio de precio hecho por Edit tambien figura en el historial del catalogo', async ({ admin, st }) => {
        const C = `${st.prefix}-PR10B`;
        const p = await mk(admin, st, C, { PrecioCompra: '1000', PrecioVenta: '1500' });
        const f = await editForm(admin, p.id, { PrecioVenta: '1700' });
        expect((await (await admin.form(`/Producto/EditAjax/${p.id}`, f)).json()).success).toBe(true);
        const enPrecHist = db.num(`SELECT COUNT(*) FROM PreciosHistoricos WHERE ProductoId=${p.id}`);
        const h = await (await admin.get(`/Catalogo/HistorialPrecioProductoApi?productoId=${p.id}`)).json();
        evidence('PR10b', { preciosHistoricosRows: enPrecHist, catalogoHistorialItems: h.items });
        expect(enPrecHist).toBe(1);
        expect.soft(h.items.some(i => i.precioAnterior === 1500 && i.precioNuevo === 1700),
            'el cambio hecho desde Edit NO aparece en el historial del Catalogo (solo lee CambioPrecioDetalles)').toBe(true);
    });

    test('PR11 revertir un cambio y no permitir doble reversion', async ({ admin }) => {
        const antes = { prod: db.producto(mem.B), ev: eventosDe(mem.idB) };
        const t = await admin.token();
        const r = await admin.raw.post('/Catalogo/RevertirCambioPrecioApi', { form: { eventoId: String(mem.evento1), __RequestVerificationToken: t }, headers: { 'X-Requested-With': 'XMLHttpRequest' }, maxRedirects: 0 });
        const j = await r.json();
        const despues = { prod: db.producto(mem.B), ev: eventosDe(mem.idB) };
        evidence('PR11', { antes, resp: j, despues });
        expect(j.success, JSON.stringify(j)).toBe(true);
        expect(despues.prod.precioVenta).toBe(2000);
        expect(despues.ev.find(e => e.eventoId === mem.evento1).revertidoEn).toBeTruthy();
        expect(despues.ev.some(e => e.alcance === 'reversion')).toBe(true);
        // segunda reversion del mismo evento
        const r2 = await (await admin.raw.post('/Catalogo/RevertirCambioPrecioApi', { form: { eventoId: String(mem.evento1), __RequestVerificationToken: t }, headers: { 'X-Requested-With': 'XMLHttpRequest' }, maxRedirects: 0 })).json();
        expect(r2.success).toBe(false);
        expect(db.producto(mem.B).precioVenta).toBe(2000);
        expect(eventosDe(mem.idB).filter(e => e.alcance === 'reversion')).toHaveLength(1);
    });

    test('PR11b revertir un evento viejo tras cambios posteriores (integridad)', async ({ admin, st }) => {
        const D = `${st.prefix}-PR11B`;
        const p = await mk(admin, st, D, { PrecioCompra: '1000', PrecioVenta: '2000' });
        await precio(admin, p.id, 10);            // 2000 -> 2200 (E1)
        await precio(admin, p.id, 10);            // 2200 -> 2420 (E2)
        const evs = eventosDe(p.id);
        const e1 = evs[0].eventoId;
        const t = await admin.token();
        const j = await (await admin.raw.post('/Catalogo/RevertirCambioPrecioApi', { form: { eventoId: String(e1), __RequestVerificationToken: t }, headers: { 'X-Requested-With': 'XMLHttpRequest' }, maxRedirects: 0 })).json();
        const final = db.producto(D).precioVenta;
        evidence('PR11b', { eventos: evs, resp: j, precioFinal: final });
        // Una reversion consistente deberia o bien rechazarse (hay un cambio posterior) o bien dar 2200 (deshacer solo E1)
        expect.soft([2420, 2200].includes(final) || j.success === false,
            `revertir E1 con E2 posterior dejo el precio en ${final} (se pierde el cambio E2 sin aviso)`).toBe(true);
    });

    test('PR12 margen y rentabilidad coherentes', async ({ admin, st }) => {
        const M = `${st.prefix}-PR12`;
        const p = await mk(admin, st, M, { PrecioCompra: '1000', PrecioVenta: '1500' });
        const f = await editForm(admin, p.id, { PrecioVenta: '1600' });
        const j = await (await admin.form(`/Producto/EditAjax/${p.id}`, f)).json();
        expect(j.success).toBe(true);
        const markup = (1600 - 1000) / 1000 * 100;      // 60
        const margenSobreVenta = (1600 - 1000) / 1600 * 100; // 37.5
        evidence('PR12', { filaMargen: j.entity.margenPorcentaje, markup, margenSobreVenta });
        expect([markup, margenSobreVenta].some(v => Math.abs(Number(j.entity.margenPorcentaje) - v) < 0.01),
            `margen del catalogo=${j.entity.margenPorcentaje} no coincide con markup(${markup}) ni margen s/venta(${margenSobreVenta})`).toBe(true);
        // venta bajo costo: el sistema solo advierte en log (no bloquea)
        const bajo = await (await admin.form(`/Producto/EditAjax/${p.id}`, await editForm(admin, p.id, { PrecioVenta: '900' }))).json();
        evidence('PR12-bajo-costo', { resp: bajo, db: db.producto(M) });
        expect.soft(bajo.success, 'precio de venta menor al costo aceptado sin bloqueo/confirmacion').toBe(false);
    });

    test('PR13a sin permiso: lector no aplica ni revierte precios', async ({ lector, admin, st }) => {
        const antes = db.producto(mem.B);
        const r1 = await precio(lector, mem.idB, 50);
        const t = await lector.token();
        const evId = eventosDe(mem.idB)[0].eventoId;
        const r2 = await lector.raw.post('/Catalogo/RevertirCambioPrecioApi', { form: { eventoId: String(evId), __RequestVerificationToken: t }, maxRedirects: 0 });
        const r3 = await lector.json('/CambiosPrecios/AplicarRapido', { modo: 'seleccionados', porcentaje: 50, productoIds: [mem.idB], listasPrecioIds: [] });
        evidence('PR13a', { aplicar: r1.status(), revertir: r2.status(), rapido: r3.status(), antes, despues: db.producto(mem.B) });
        for (const r of [r1, r2, r3]) expect([302, 401, 403]).toContain(r.status());
        expect(db.producto(mem.B)).toEqual(antes);
    });

    test('PR13b oper (precios.simulate, sin apply) no aplica cambio masivo', async ({ oper }) => {
        const antes = db.producto(mem.B);
        const r = await precio(oper, mem.idB, 50);
        evidence('PR13b', { status: r.status(), antes, despues: db.producto(mem.B) });
        expect([302, 401, 403]).toContain(r.status());
        expect(db.producto(mem.B)).toEqual(antes);
    });

    test('PR13c oper sin precios.apply NO deberia poder cambiar precio/costo via Edit de producto', async ({ oper, admin, st }) => {
        const E = `${st.prefix}-PR13C`;
        const p = await mk(admin, st, E, { PrecioCompra: '1000', PrecioVenta: '1500' });
        const f = await editForm(admin, p.id, { PrecioCompra: '10', PrecioVenta: '11' });
        const j = await (await oper.form(`/Producto/EditAjax/${p.id}`, f)).json();
        const despues = db.producto(E);
        evidence('PR13c', { resp: j, despues });
        expect.soft(despues.precioVenta === 1500 && despues.precioCompra === 1000,
            `usuario con productos.update (sin precios.apply) cambio costo/precio via EditAjax: compra=${despues.precioCompra} venta=${despues.precioVenta}`).toBe(true);
    });

    test('PR14 validaciones del ajuste de precio', async ({ admin, st }) => {
        const V = `${st.prefix}-PR14`;
        const p = await mk(admin, st, V, { PrecioCompra: '1000', PrecioVenta: '2000' });
        const casos = [];
        const intento = async (nombre, fn, esperaOk) => {
            const r = await fn();
            const j = await r.json().catch(() => ({}));
            const precioDb = db.producto(V).precioVenta;
            casos.push({ nombre, status: r.status(), exitoso: j.exitoso, precioDb });
            return { r, j, precioDb };
        };
        const c0 = await intento('0%', () => precio(admin, p.id, 0));
        expect(c0.r.status()).toBe(400); expect(c0.precioDb).toBe(2000);
        const c1 = await intento('-101% (negativo)', () => precio(admin, p.id, -101));
        expect(c1.r.status()).toBe(400); expect(c1.precioDb).toBe(2000);
        const c2 = await intento('alcance invalido', () => precio(admin, p.id, 5, { alcance: 'todos' }));
        expect(c2.r.status()).toBe(400);
        const c3 = await intento('ids vacios', () => precio(admin, '', 5));
        expect(c3.r.status()).toBe(400);
        const c4 = await intento('id inexistente', () => precio(admin, 99999999, 5));
        expect(c4.r.status()).toBe(400);
        const c5 = await intento('monto fijo +150', () => precio(admin, p.id, 150, { tipoCambio: 'montofijo' }));
        expect(c5.precioDb).toBe(2150);
        const c6 = await intento('-50% redondeo entero', () => precio(admin, p.id, -50, { redondeo: 'entero' }));
        expect(c6.precioDb).toBe(1075);
        const c7 = await intento('+7,3% redondeo .99', () => precio(admin, p.id, 7.3, { redondeo: '99' }));
        evidence('PR14', casos);
        expect(String(c7.precioDb)).toMatch(/\.99$/);
        // consistencia: cada evento exitoso tiene detalle con precios reales
        const evs = eventosDe(p.id);
        expect(evs.length).toBe(3);
        expect(evs.map(e => e.nuevo)).toEqual([2150, 1075, c7.precioDb]);
    });
    test('PR15 alcance "filtrados" con filtros vacios afecta a TODO el catalogo (se revierte al final)', async ({ admin }) => {
        const totalActivos = db.num('SELECT COUNT(*) FROM Productos WHERE IsDeleted=0');
        const antes = db.rows('SELECT Id,PrecioVenta FROM Productos WHERE IsDeleted=0 ORDER BY Id').map(r => `${r[0]}:${Number(r[1])}`).join('|');
        const r = await admin.json('/Catalogo/AplicarCambioPrecioDirecto', { alcance: 'filtrados', valorPorcentaje: 1, filtrosJson: '{}', tipoCambio: 'porcentaje', redondeo: 'none', motivo: 'QA-TX PR15' });
        const j = await r.json();
        const cambiados = db.rows('SELECT Id,PrecioVenta FROM Productos WHERE IsDeleted=0 ORDER BY Id').map(x => `${x[0]}:${Number(x[1])}`).join('|') !== antes;
        let revertido = null;
        if (j.cambioPrecioEventoId) {
            const t = await admin.token();
            revertido = await (await admin.raw.post('/Catalogo/RevertirCambioPrecioApi', { form: { eventoId: String(j.cambioPrecioEventoId), __RequestVerificationToken: t }, headers: { 'X-Requested-With': 'XMLHttpRequest' }, maxRedirects: 0 })).json();
        }
        const despues = db.rows('SELECT Id,PrecioVenta FROM Productos WHERE IsDeleted=0 ORDER BY Id').map(x => `${x[0]}:${Number(x[1])}`).join('|');
        evidence('PR15', { totalActivos, resp: j, afectoElCatalogo: cambiados, revertido, restauradoExacto: despues === antes });
        expect.soft(r.status() === 400 || !cambiados, `filtros vacios aplicaron +1% a ${j.productosActualizados}/${totalActivos} productos sin pedir confirmacion de alcance`).toBe(true);
        expect(despues).toBe(antes); // la reversion deja el catalogo como estaba
    });
});
