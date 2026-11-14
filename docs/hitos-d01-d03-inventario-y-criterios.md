# D-01 / D-02 / D-03: inventario verificable y criterios de aceptación

Fecha: 2026-11-14 · base: `main` 6326d264 · rama `task/d01-d02-d03-20261114`.
Decisión del propietario: los tres módulos son obligatorios para considerar completo el ERP. El despliegue usa una **base nueva**; no se importan datos ni usuarios de `TheBuryProjectDb`.

Nota de trazabilidad: el repositorio no contiene la definición original de D-01/D-02/D-03 (tablero fuera del repo, no conectado). Este documento fija el alcance **derivado del código real y del pedido** y deja como decisión del propietario todo lo que sea regla de precios, crédito, IVA, costeo o notas de crédito.

## 1. Inventario (estado verificado en `main`)

### D-01 Listas de precios y precios por lotes

| Capacidad | Estado | Evidencia |
|---|---|---|
| Entidades `ListaPrecio`, `ProductoPrecioLista` (vigencia, costo, margen), `PriceChangeBatch/Item`, `CambioPrecioEvento/Detalle`, `PrecioHistorico` | Existe | `Models/Entities` |
| Servicio de listas (CRUD, predeterminada), precio vigente, batch, margen mínimo | Existe | `Services/PrecioService.cs`, `IPrecioService` |
| Flujo por lotes: simular → autorizar → aprobar/rechazar/cancelar → aplicar → revertir; aplicar rápido desde catálogo; historial | Existe | `Controllers/CambiosPreciosController.cs`, `Views/CambiosPrecios/*` |
| ABM de listas de precios en la UI | **Se verifica abajo** (no hay `ListaPrecioController`) | — |
| Precio por lista editable por producto desde UI | **Se verifica abajo** | — |

La tarea de D-01 comienza con una verificación de huecos contra el código y termina implementando solo lo inequívoco.

### D-02 Referencias de clientes

| Capacidad | Estado |
|---|---|
| Entidad/tabla de referencias (personales, laborales, comerciales) | **No existe** (solo `Cliente.Empleador`/`TelefonoLaboral`) |
| ABM, verificación, permisos, auditoría, tests | **No existe** |

### D-03 Cuentas por pagar, pagos y devoluciones a proveedores

| Capacidad | Estado |
|---|---|
| `OrdenCompra` (estados Borrador..Recibida/Cancelada, totales) | Existe (sin concepto de factura del proveedor ni de deuda) |
| `Cheque` con relación a `Proveedor` | Existe |
| Devolución a proveedor a nivel unidad (RMA, `ProductoUnidad`) | Existe (sin impacto en deuda) |
| Comprobantes del proveedor, saldo, vencimientos, pagos, imputaciones, notas de crédito, estado de cuenta | **No existe** |

## 2. Alcance implementable sin decisión (registro administrativo, sin asientos)

- **D-02:** `ClienteReferencia` informativa (tipo, nombre, relación, teléfono, estado de verificación, observaciones), ABM desde el detalle del cliente, validaciones (obligatorios, no duplicada, no coincide con el propio cliente), baja lógica, permisos, auditoría. **No** altera aptitud ni límite de crédito.
- **D-03:** libro de cuentas por pagar como **registro**: comprobantes (factura, nota de débito, nota de crédito) con importes tal como los informa el proveedor; pagos con imputación a comprobantes (sin sobreimputar, concurrencia segura); anulación de pagos por contra-registro; saldo y vencimientos; estado de cuenta y antigüedad de deuda; vínculo opcional con orden de compra y con la devolución a proveedor. **No** calcula IVA crédito fiscal, **no** recalcula costo de producto, **no** genera movimientos de Caja, **no** emite comprobantes fiscales.
- **D-01:** lo que la verificación de huecos muestre sin ambigüedad (p. ej. ABM de listas con validaciones y permisos).

## 3. Decisiones del propietario / contador (alternativas y consecuencias)

| # | Tema | Alternativa A | Alternativa B | Consecuencia / recomendación |
|---|---|---|---|---|
| R-01 | ¿El pago a proveedor en efectivo descuenta de la Caja? | No: registro administrativo (**implementada**) | Sí: egreso de la caja abierta del usuario | B obliga a definir caja, arqueo y qué pasa si no hay caja abierta. Hasta decidir, la Caja no se toca. |
| R-02 | IVA crédito fiscal de las facturas de compra | Solo se guardan neto/IVA/total informados (**implementada**) | El ERP calcula y acumula IVA crédito por alícuota | B depende de P-02 (respuestas del contador, `iva-decisiones-contador.md`). |
| R-03 | Costeo | El costo del producto no cambia con la factura (**implementada**) | Costo = último, promedio ponderado o por lote | Cambia márgenes y precios sugeridos de D-01: requiere método único. |
| R-04 | Nota de crédito del proveedor | Reduce deuda imputándose a facturas (**implementada**) | Además genera ajuste de stock y costo | B acopla con devolución de unidad y costeo (R-03). |
| R-05 | Retenciones/percepciones (IIBB, Ganancias) | Fuera de alcance (**implementada**) | Campos y asientos de retención | Exige criterio fiscal del contador. |
| R-06 | Referencias de clientes y crédito | Informativas (**implementada**) | Mínimo de referencias verificadas para otorgar crédito personal | B cambia el evaluador de crédito: regla del propietario. |
| R-07 | Listas de precios por cliente o por medio de pago | Lista predeterminada + precio por lista (lo existente) | Lista asignable a cliente/segmento | B cambia el cálculo de precio en Venta/Cotización. |

## 4. Criterios de aceptación (cada uno debe tener prueba; si no la tiene, el módulo no se marca completo)

### D-02
- AC-02-1 Alta, edición y baja lógica de referencia desde el cliente; persisten con auditoría.
- AC-02-2 Rechaza referencia sin nombre/teléfono, duplicada (mismo cliente, tipo y teléfono) o con el teléfono/documento del propio cliente.
- AC-02-3 Permisos: sin permiso de edición de clientes no hay alta/edición/baja (403/redirección); con solo lectura se ven pero no se editan.
- AC-02-4 La aptitud y el límite de crédito no cambian con o sin referencias (prueba de no regresión).
- AC-02-5 Aislamiento entre clientes (no se ven ni editan referencias de otro cliente).
- AC-02-6 E2E: crear cliente → agregar referencias → verificar → ver en detalle.

### D-03
- AC-03-1 Alta de comprobante con unicidad (proveedor, tipo, número); neto + IVA = total con tolerancia definida; vencimiento ≥ emisión.
- AC-03-2 Pago imputado a uno o varios comprobantes; no se puede imputar más que el saldo; el saldo y el estado se recalculan.
- AC-03-3 Concurrencia: dos pagos simultáneos sobre el mismo saldo no lo sobreimputan (prueba SQL Server).
- AC-03-4 Anulación de pago restituye saldo y queda trazada; no se borra historia.
- AC-03-5 Nota de crédito imputable a facturas del mismo proveedor, nunca por más que el saldo.
- AC-03-6 Estado de cuenta y antigüedad de deuda (por vencer / vencido 1-30 / 31-60 / +60) coinciden con los datos.
- AC-03-7 Permisos separados (ver / registrar / pagar / anular) y un usuario sin permiso no opera.
- AC-03-8 Caja, stock, costo e IVA de ventas no cambian (prueba de no regresión).
- AC-03-9 E2E: orden de compra → factura → pago parcial → NC → saldo final.

### D-01
- AC-01-x se completan al terminar la verificación de huecos (sección 5).

### Transversales
- AC-T-1 Instalación completa sobre SQL Server nuevo y descartable con **todas** las migraciones EF y datos sintéticos.
- AC-T-2 Una base nueva queda con **un solo** usuario administrador inicial de prueba y **cero** clientes, productos, ventas, proveedores o créditos importados.
- AC-T-3 Ningún camino de código ni script importa filas de `TheBuryProjectDb`.
- AC-T-4 Build sin errores, tests focalizados y suite de CI verdes sobre el SHA final.

## 5. Verificación de huecos de D-01

(se completa durante la implementación)

## 6. Resultado D-02 (referencias de clientes)

Implementado en la rama `task/d01-d02-d03-20261114`: entidad `ClienteReferencia` + migración `AddClienteReferencia` (solo la tabla nueva y 2 índices), `IClienteReferenciaService`, `ClienteReferenciaController` (`/ClienteReferencia/Lista|Crear|Editar|Verificar|Baja`), sección «Referencias» dentro de la solapa Datos de `Cliente/Details` (la ficha conserva sus 4 solapas por contrato de UI existente) y script `cliente-referencias.js`. Permisos reutilizados, sin cambios: lectura `clientes.view`; alta, edición, verificación y baja `clientes.edit`.

| AC | Estado | Evidencia |
|---|---|---|
| AC-02-1 | VERIFICADO | `ClienteReferenciaServiceTests`: `Crear_PersisteReferenciaPendienteConAuditoria`, `Editar_ActualizaCamposYSellaUpdatedAt`, `DarDeBaja_EsLogica_ConservaElRegistroYSaleDelListado`, `CambiarVerificacion_*`; HTTP `Editor_RecibeLasAccionesYPuedeCrearEditarVerificarYDarDeBaja` |
| AC-02-2 | VERIFICADO | `Crear_SinNombreOSinTelefono_SeRechaza`, `Crear_ConTelefonoInvalido_SeRechaza`, `Crear_Duplicada_MismoClienteTipoYTelefonoNormalizado_SeRechaza`, `Crear_ConTelefonoDelPropioCliente_SeRechaza` (teléfono, alternativo y laboral), `Editar_*`. **Parcial en «documento»**: la referencia no tiene campo de documento, por lo que no existe comparación contra el documento del cliente (ver ambigüedades) |
| AC-02-3 | VERIFICADO | `ClienteReferenciaPermisoHttpTests`: `SoloLectura_VeLasReferenciasPeroNoRecibeNingunaAccionDeEdicion`, `SoloLectura_NoPuedeCrearEditarVerificarNiDarDeBaja_YNoCambiaNada`, `SinPermisoDeClientes_NoPuedeVerElListado`, `Editor_SinTokenAntiforgery_SeRechaza` |
| AC-02-4 | VERIFICADO | `ClienteReferenciaNoAfectaCreditoTests`: aptitud (`ClienteAptitudService.EvaluarAptitudSinGuardarAsync`) y cupo (`CreditoDisponibleService.CalcularDisponibleAsync`) idénticos con y sin referencias en los 4 estados y 3 tipos, con validaciones de crédito activas e inactivas; contrato de que los servicios de crédito/aptitud/scoring/venta no referencian `ClienteReferencia` |
| AC-02-5 | VERIFICADO | `Aislamiento_ListarSoloDevuelveLasReferenciasDelCliente`, `Aislamiento_NoSePuedeEditarVerificarNiDarDeBajaUnaReferenciaDeOtroCliente` (servicio) y `Aislamiento_UnEditorNoPuedeOperarUnaReferenciaDeOtroClientePasandoSuId` (HTTP) |
| AC-02-6 | NO VERIFICADO (E2E automatizado) | No se escribió una spec Playwright versionada. Hubo una corrida manual con Playwright MCP sobre una base LocalDB descartable (ya eliminada): alta, errores de validación, edición, verificación, baja, 1440 px y 390 px, sin desborde horizontal y sin errores de consola fuera de los 400 esperados. El cliente se creó por SQL, no por el formulario «Crear cliente», así que el recorrido «crear cliente → referencias» completo no está probado |

Otros contratos: `ClienteReferenciaUiContractTests` (ARIA, labels, dialogos, antiforgery, permiso por acción). La migración se validó con `dotnet ef migrations script` y se aplicó solo a una base LocalDB descartable (no a `TheBuryProjectDb`). Estados «NO VALIDADO»: foco/teclado en modales solo se probó con Escape y foco inicial; no hay prueba de lector de pantalla; viewport tablet no revisado.
