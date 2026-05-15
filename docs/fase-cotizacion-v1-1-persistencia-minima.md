# Fase Cotizacion V1.1 - Persistencia minima

## A. Objetivo

Guardar cotizaciones como presupuestos recuperables sin convertirlas en ventas y sin afectar stock, caja, factura, credito definitivo ni ProductoUnidad.

## B. Decision: entidad propia, no Venta

Cotizacion V1.1 usa entidad propia:

- `Cotizacion`
- `CotizacionDetalle`
- `CotizacionPagoSimulado`
- `EstadoCotizacion`

No usa `Venta`, `VentaDetalle`, `VentaService`, `EstadoVenta.Cotizacion`, `VentaController`, `VentaApiController` ni `venta-create.js`.

## C. Modelo EF

`Cotizacion` guarda numero, fecha, estado, cliente opcional, cliente libre opcional, observaciones, totales recalculados, opcion seleccionada y vencimiento opcional.

`CotizacionDetalle` guarda snapshot de producto, codigo, nombre, cantidad, precio unitario, descuentos y subtotal.

`CotizacionPagoSimulado` guarda cada opcion/plan simulado con medio, estado, cuotas, porcentajes, total, valor cuota, recomendado, seleccionado y advertencias serializadas.

Las entidades heredan `AuditableEntity`, por lo que usan auditoria automatica (`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`, `IsDeleted`) y `RowVersion`.

## D. Servicio de persistencia

Se agrego `ICotizacionService` / `CotizacionService`.

Responsabilidades:

- recalcular con `ICotizacionPagoCalculator` antes de guardar;
- validar cliente opcional;
- generar numero `COT-yyyyMMdd-0000`;
- persistir snapshot de productos y opciones de pago;
- listar cotizaciones;
- obtener detalle completo.

## E. Flujo guardar cotizacion

La UI arma una simulacion y permite seleccionar una opcion de pago. Al guardar, el frontend envia el request base y la opcion seleccionada a `POST /api/cotizacion/guardar`.

El backend no confia en totales enviados por JS: recalcula y solo persiste el resultado recalculado.

## F. UI agregada

- Boton `Guardar cotizacion`.
- Observaciones opcionales.
- Seleccion de opcion de pago en resultados.
- `GET /Cotizacion/Listado`.
- `GET /Cotizacion/Detalles/{id}`.

## G. Permisos

Se usa el modulo ya existente `cotizaciones`:

- `cotizaciones.view` para pantalla, listado, detalle y simulacion.
- `cotizaciones.create` para guardar.

No se agrego semilla nueva porque `RolesPermisosSeeder` ya contiene el modulo y acciones.

## H. Tests

Se agregaron tests de persistencia con SQLite in-memory:

- persiste cabecera, detalles y opciones;
- cliente nullable;
- numero unico;
- recalculo antes de guardar;
- no crea venta;
- no toca caja;
- no toca stock;
- devuelve detalle completo;
- lista por cliente/estado.

Tambien se ajustaron tests de controller/API para el nuevo servicio y permisos `cotizaciones`.

## I. Que NO se toco

- Venta productivo.
- `VentaService`.
- `VentaController`.
- `VentaApiController`.
- `venta-create.js`.
- Caja.
- Factura.
- Stock.
- ProductoUnidad.
- Devoluciones/Garantia.

## J. Riesgos/deuda

- La generacion de numero usa correlativo por fecha con indice unico; ante concurrencia real puede requerir reintento controlado.
- No hay cancelacion/anulacion compleja en V1.1.
- No hay vencimiento automatico.
- No hay conversion a venta.
- No hay impresion formal ni envio externo.

## K. Checklist actualizado

- Diagnostico Ventas/Cotizacion: cerrado.
- Diseno V1 Cotizacion no persistida: cerrado.
- V1A DTOs/interfaz/tests base: cerrado.
- V1B calculo real read-only basico: cerrado.
- V1C API/controller read-only: cerrado.
- V1D credito personal simulado read-only: cerrado.
- V1E UI cotizacion separada: cerrado.
- V1.1 persistencia minima: cerrado.
- Conversion Cotizacion a Venta: pendiente.
