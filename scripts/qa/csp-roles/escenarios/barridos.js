// Barridos de interacciones (clic en cada control distinto, escrituras bloqueadas) sobre las pantallas con modales/AJAX/innerHTML.
'use strict';
const { barrer } = require('./lib');

const RUTAS = [
  '/Catalogo', '/Catalogo?tab=categorias', '/Catalogo?tab=marcas', '/Catalogo?tab=alertas', '/Catalogo?tab=movimientos',
  '/Proveedor', '/Cliente', '/Seguridad', '/Credito', '/Caja', '/Cotizacion', '/Venta', '/OrdenCompra', '/Ticket', '/Mora', '/Devolucion',
  '/Documento', '/MovimientoStock', '/AlertaStock', '/Reporte', '/Dashboard',
];

module.exports = RUTAS.map((ruta) => ({
  nombre: `Barrido de controles: ${ruta}`,
  async fn(E, page) { await barrer(E, page, ruta, { porFirma: 2, max: 45 }); },
}));
