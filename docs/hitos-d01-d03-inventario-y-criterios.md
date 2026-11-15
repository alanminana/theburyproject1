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
| R-08 | Quién recibe los permisos de cuentas por pagar (`cuentasporpagar.view/createinvoice/pay/cancel`) | Administrador/SuperAdmin los cuatro; Contador solo ver; Gerente y el resto ninguno (**implementada**) | Gerente con ver/registrar/pagar (y anular reservado) | B es una decisión de gobierno de pagos: se asigna a mano desde Seguridad > Roles. |
| R-09 | Pagos a cuenta (sin comprobante) | Todo pago se imputa por completo a comprobantes pendientes: suma de imputaciones = monto (**implementada**) | Anticipos a proveedor sin imputar, con saldo a favor | B exige un concepto «saldo a favor del proveedor» y reglas de aplicación posterior. |
| R-10 | Aplicar una nota de crédito a facturas | Exige `cuentasporpagar.pay` (compensa deuda, no mueve dinero); anular la aplicación exige `cancel` (**implementada**) | Permiso propio de «aplicar NC» | B agrega un quinto permiso; hoy no hay evidencia de que haga falta. |
| R-11 | Vínculo con la devolución a proveedor (RMA) | No se vincula: el RMA es por unidad (`ProductoUnidad`) y no tiene una clave natural de proveedor/importe (**implementada: omitido**) | Referencia informativa RMA → nota de crédito | B requiere definir qué RMA respalda qué nota de crédito. |
| R-12 | Estado de cuenta y antigüedad | La nota de crédito reduce el saldo neto al emitirse; el saldo de comprobantes y las NC sin aplicar se muestran separados. Antigüedad por día comercial: vence hoy o sin vencimiento = «Por vencer»; 1-30, 31-60 y +60 días de atraso (**implementada**) | NC reduce deuda solo al aplicarse; tramos contables distintos | B cambia lo que el contador ve como saldo neto. |
| R-13 | Unicidad del comprobante | (proveedor, tipo, número) recortado y en mayúsculas, solo entre no anulados: anular libera el número (**implementada**) | Unicidad aun entre anulados | B impide recargar un comprobante mal cargado con el mismo número. |

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

## 7. Resultado D-03 (cuentas por pagar, pagos y devoluciones a proveedores como libro administrativo)

Implementado en la rama `task/d01-d02-d03-20261114` (3 commits). Registro administrativo: **no** mueve Caja, stock, costo de producto, ni calcula IVA; los importes son los informados por el proveedor.

- Modelo: `ComprobanteProveedor` (factura, nota de débito, nota de crédito; estado y saldo persistidos y recalculados), `PagoProveedor`, `PagoProveedorImputacion` e `ImputacionNotaCredito` (entidad aparte porque la NC no es un pago y no mueve dinero). Todas `AuditableEntity` con `RowVersion`; las anulaciones son contra-registros que conservan motivo, usuario y fecha. Migración única `AddCuentasPorPagar` (4 tablas, 12 índices, FKs `Restrict`, sin arrastres).
- Servicio `ICuentaPorPagarService`: toda escritura corre en una transacción que toma `UPDLOCK, HOLDLOCK` sobre la fila del proveedor y relee los saldos ya bajo el lock; idempotencia por clave de formulario (pagos y aplicaciones de NC).
- Permisos: módulo `cuentasporpagar` con `view`, `createinvoice`, `pay`, `cancel` (ver R-08). Controller `CuentaPorPagarController`, vistas en `Views/CuentaPorPagar`, accesos desde el menú (Logística), `Proveedor/Details` y `OrdenCompra/Details`.

| AC | Estado | Evidencia |
|---|---|---|
| AC-03-1 | VERIFICADO | `CuentaPorPagarServiceTests`: `Registrar_PersisteComprobantePendiente…`, `Registrar_DuplicadoMismoProveedorTipoYNumero_SeRechaza…`, `Registrar_MismoNumeroEnOtroTipoOtroProveedorOTrasAnular_SePermite`, `Registrar_NetoMasIvaContraTotal_RespetaLaTolerancia` (6 casos, bordes ±0,01), `Registrar_VencimientoAnteriorALaEmision…`, validaciones de obligatorios, decimales y orden de compra ajena |
| AC-03-2 | VERIFICADO | `Pago_ParcialYLuegoTotal_RecalculaSaldoYEstado`, `Pago_ImputadoAVariosComprobantes…`, `Pago_ImputacionMayorAlSaldo_SeRechaza…`, `Pago_LaSumaDeImputacionesDebeIgualarElMonto`, rechazo de comprobante ajeno/NC/anulado, cheque solo como referencia sin cambiar su estado, idempotencia; HTTP `Operador_PagoConImporteInvalido…` y `Operador_ElMismoFormularioEnviadoDosVeces…` |
| AC-03-3 | VERIFICADO (SQL Server real) | `Concurrency/CuentaPorPagarSqlServerTests` (6 pruebas, SQL Server 2022 en contenedor descartable): dos pagos de 700 sobre 1000 (5 rondas), ocho pagos de 200, pago + NC simultáneos, mismo formulario ×3, doble anulación, anular + pagar. **La prueba detecta el defecto**: con el lock deshabilitado fallan 4 de las 6; con el lock pasan 6/6. Corren solo con `QA_RELEASE_TURNO=1` y `QA_FIN_CONC_SQL` (catálogo `qa_fin_conc*`), como el resto de los harness de concurrencia (en la corrida por defecto figuran como omitidas) |
| AC-03-4 | VERIFICADO | `AnularPago_RestituyeSaldosYQuedaTrazadoSinBorrarHistoria`, `AnularPago_SinMotivoOYaAnulado…_YPermiteVolverAPagar`, `AnularComprobante_SoloSinImputacionesVigentes…`; E2E: anulación de pago restituye 710 → 1.110 y queda el motivo |
| AC-03-5 | VERIFICADO | `NotaDeCredito_SeAplicaAFacturasDelMismoProveedor…`, `…_NoPuedeSuperarElSaldoDeLaNotaNiElDeLaFactura`, `…_NoSeAplicaAOtroProveedorANotasDeCreditoNiDesdeUnaFactura`, idempotencia, anulación de la aplicación, `…_ElPagoPosteriorSoloPuedeCubrirElSaldoQueQuedaTrasLaNota` |
| AC-03-6 | VERIFICADO | `EstadoDeCuenta_ArmaElLibroConSaldoCorrido…` (saldo corrido; saldo neto = saldo de comprobantes − NC sin aplicar), `Antiguedad_UbicaCadaFacturaEnSuCubetaPorLosBordesDeDias` (8 bordes: +1, 0, −1, −30, −31, −60, −61, −400), `Antiguedad_UsaElSaldoPendiente…`, `Antiguedad_UsaElDiaComercialNoElUtc`, listados con filtros y paginación |
| AC-03-7 | VERIFICADO | `CuentaPorPagarHttpTests` (solo lectura / sin permiso / operador / anulador, GET y POST, antiforgery, base sin cambios), `CuentaPorPagarPermisosSeedTests` (catálogo y grants por rol), `CuentaPorPagarUiContractTests` (permiso por acción) y la matriz general `AutorizacionMatrizPorRolHttpTests` / `AutorizacionMutantesSinEfectosHttpTests` (inventario de acciones mutantes regenerado) |
| AC-03-8 | VERIFICADO | `NoRegresion_TodasLasOperacionesDejanCajaStockCostoEIvaIntactos` compara antes/después movimientos y aperturas de Caja, movimientos de stock, ventas, stock/costo/IVA de productos, estado de cheques y totales de la orden de compra; contrato `Contrato_ElServicioNoReferencia…` (la fuente del servicio no menciona Caja, stock, producto, costo ni IVA de producto) |
| AC-03-9 | VERIFICADO en navegador desde la factura; el tramo «orden de compra → factura» solo por HTTP | `e2e/cuentas-por-pagar-flujo.spec.js` (Playwright, 1366×768 y 390×844, instancia propia sobre SQL Server descartable): factura con vencimiento, rechazo de duplicado y de importes incoherentes, pago parcial con rechazo de sobreimputación, nota de crédito con rechazo de excedente y aplicación, saldo final 710, estado de cuenta, antigüedad (+60), anulación de pago (saldo 1.110), listado «solo vencidos», sin desborde horizontal ni errores de consola/5xx. El dataset E2E no tiene órdenes de compra, por eso el tramo OC → factura (enlace desde el detalle de la orden, alta prellenada, factura vinculada) se prueba en `CuentaPorPagarHttpTests.Recorrido_OrdenDeCompraFacturaPagoParcialNotaDeCreditoYSaldoFinal`. Tablet (768×1024) no se corrió |

Otros contratos y validaciones: `CuentaPorPagarUiContractTests` (27 casos: antiforgery, ARIA, labels, formato es-AR sin `$` fijo, JS sin reglas, migración sin arrastres). Migración validada con `dotnet ef migrations script` (sin conexión) y aplicada solo a bases SQL Server descartables (fixture `MigrateAsync` y arranque de la app, 138 tablas); nunca a `TheBuryProjectDb`. Revisión visual real en 1366, 1024 y 390 px; se corrigieron la columna Acciones recortada en 1366, el importe del KPI partido en mobile y el IVA en 0 que se vaciaba al reabrir el formulario con un error.

Estados NO VALIDADOS: tablet 768×1024 en el spec e2e; lector de pantalla; el spec e2e no se corrió en CI (solo local); la acción «Anular comprobante» se probó por HTTP/servicio pero no en navegador; el pago no exige `cheques.view` para elegir un cheque (el listado solo ofrece cheques del mismo proveedor).

Ambigüedades abiertas (decisión del propietario/contador): R-08 (rol Gerente), R-09 (anticipos), R-11 (vínculo RMA) y si se necesita exportar el estado de cuenta (PDF/Excel), que no se implementó.
