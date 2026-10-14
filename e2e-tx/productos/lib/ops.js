// @ts-check
const db = require('./db');
const { productoForm } = require('./http');

/** Crea un producto sintetico y devuelve su fila de DB (falla si no se creo). */
async function mk(admin, st, code, over = {}) {
    const r = await admin.form('/Producto/CreateAjax', productoForm(st, { Codigo: code, Nombre: `${code} QA`, ...over }));
    const j = await r.json();
    if (!j.success) throw new Error(`mk(${code}) fallo: ${JSON.stringify(j)}`);
    const p = db.producto(code);
    if (!p) throw new Error(`mk(${code}) no persistio`);
    return p;
}

/** Cambio directo de precio base (mismo endpoint que el modal "Ajuste masivo"). */
function precio(admin, ids, valor, extra = {}) {
    return admin.json('/Catalogo/AplicarCambioPrecioDirecto', {
        alcance: 'seleccionados', valorPorcentaje: valor, productoIdsText: [].concat(ids).join(','),
        tipoCambio: 'porcentaje', redondeo: 'none', motivo: 'QA-TX', ...extra,
    });
}

/** Ajuste de stock (mismo POST que /MovimientoStock/Create). tipo: 0 Entrada, 1 Salida, 2 Ajuste. */
function ajuste(client, productoId, tipo, cantidad, extra = {}) {
    return client.form('/MovimientoStock/Create', {
        ProductoId: String(productoId), Tipo: String(tipo), Cantidad: String(cantidad),
        Referencia: 'QA-TX', Motivo: 'QA-TX motivo', ...extra,
    });
}

const eventosDe = id => db.rows(`SELECT e.Id,e.Alcance,d.PrecioAnterior,d.PrecioNuevo,ISNULL(CONVERT(varchar,e.RevertidoEn,126),'') FROM CambioPrecioDetalles d JOIN CambioPrecioEventos e ON e.Id=d.EventoId WHERE d.ProductoId=${Number(id)} AND d.IsDeleted=0 ORDER BY e.Id`)
    .map(c => ({ eventoId: Number(c[0]), alcance: c[1], anterior: Number(c[2]), nuevo: Number(c[3]), revertidoEn: c[4] || null }));

module.exports = { mk, precio, ajuste, eventosDe };
