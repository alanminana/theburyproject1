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
| R-14 | Autorización de cambios por lotes y umbral | Aplicar exige siempre un lote aprobado (`precios.approve`); el umbral (`Precios:UmbralAutorizacionPorcentaje`, 10 %) solo marca el lote como «requiere autorización» (informativo). Quien simula puede aprobar si tiene ambos permisos (**existente, no se tocó**) | Obligar aprobador distinto del solicitante, y/o exigir aprobación solo sobre el umbral | B es una política de control interno (separación de funciones); el cambio rápido ya la aplica por permisos. Hoy un usuario con simular + aprobar + aplicar puede hacer todo el circuito solo. |
| R-15 | Margen mínimo de la lista en los lotes y en el cambio rápido | El margen mínimo solo se exige al fijar un precio manual por producto y lista, y solo si la lista lo define (**implementada**); los lotes solo advierten margen negativo | Rechazar (o advertir) en simulación/aplicación los ítems por debajo del mínimo | B puede bloquear liquidaciones legítimas; hay que definir si es bloqueo o aviso y quién puede saltearlo. `ValidarMargenMinimoAsync` (existente) usa 10 % si la lista no define mínimo y no tiene ningún llamador: no se usó. |
| R-16 | Efecto de designar la lista predeterminada | Es inmediato: Venta y Cotización resuelven el precio con la predeterminada y, para productos sin precio en ella, caen al precio base (**implementada**; la pantalla lo advierte y no deja desmarcarla ni darla de baja sin designar otra) | Confirmación reforzada, permiso propio o vigencia programada para cambiar de predeterminada | B protege contra un cambio de precios masivo accidental en ventas; hoy lo cubre el permiso `precios.update` (Administrador; no Gerente). |
| R-17 | Precios manuales con vigencia futura | No se permiten (**implementada**): el precio anterior se cierra al guardar y el producto quedaría sin precio de lista hasta la fecha (el resolvedor cae al precio base) | Permitir «vigente desde» futuro sin cerrar el anterior | B cambia la semántica de `EsVigente`/`VigenciaHasta` y del resolvedor de Venta/Cotización. |
| R-18 | Redondeo por defecto | Sin regla en la lista se redondea a la centena (comportamiento existente de `AplicarRedondeo`); el formulario lo dice (**implementada**) | Que el valor por defecto sea «sin redondeo» | B cambia los precios sugeridos de todas las listas existentes sin regla. |
| R-19 | Costo del precio por lista | El costo del precio manual es el costo actual del producto (`Producto.PrecioCompra`), tomado en el servidor (**implementada**) | Costo editable por lista | Depende de R-03 (método de costeo). |
| R-20 | Exportación del historial de precios | Por producto, todas sus listas, último año, solo con `precios.history` (**implementada**) | Exportación masiva por lista o rango de fechas | B expone costos y márgenes de todo el catálogo: requiere definir permiso y volumen. |
| R-21 | Unicidad del cheque en pagos a proveedor | Un mismo cheque puede referenciarse en más de un pago (no hay restricción de unicidad); el servicio solo rechaza cheques inexistentes, de otro proveedor, anulados o rechazados (**implementada: sin unicidad**) | Un cheque solo puede respaldar un pago (índice único filtrado y control en el servicio) | B evita registrar dos veces el mismo cheque, pero impide repartir un cheque entre pagos o corregir un pago anulado reutilizándolo salvo que anular libere el cheque; requiere definir si el monto del cheque debe coincidir con el pago. Decisión del propietario. |
| R-22 | Costo y margen en la pantalla de precios de una lista | Quien tiene `precios.view` ve costo, margen y sugerido por producto en `ListaPrecio/Precios`, igual que ya ve costo y margen en `CambiosPrecios/Historial` (mismo criterio de visibilidad; **implementada: sin restricción adicional**) | Mostrar costo y margen solo con `precios.history` o `precios.apply` (en ambas pantallas) | B oculta costos al perfil de solo consulta, pero hay que cambiar también `Historial` y definir qué ve ese perfil. La exportación a Excel sí exige `precios.history` (R-20). Decisión del propietario. |

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
- AC-01-1 ABM de listas: alta, edición y baja lógica persisten; el código es único sin distinguir mayúsculas ni espacios (la baja libera el código); un `RowVersion` obsoleto no pisa la lista.
- AC-01-2 Reglas de lista: margen mínimo ≤ margen, rangos de margen/recargo (0 a 999,99, dos decimales), cuotas 1-60, redondeo conocido y JSON válido; editar conserva **todos** los campos (margen mínimo, redondeo y notas se perdían).
- AC-01-3 Lista predeterminada única (servicio e índice único filtrado en la base); no se desmarca por edición, no se desactiva ni se da de baja sin designar otra, y debe estar activa.
- AC-01-4 La baja de una lista conserva sus precios e historial y se rechaza si la usa la configuración de Mercado Libre.
- AC-01-5 Precio manual por producto y lista: vigencia, historial, cierre del anterior, margen mínimo de la lista, tope, decimales, sin vigencia futura ni anterior a la vigente, conflicto de concurrencia con mensaje, y precio sugerido por margen/recargo/redondeo.
- AC-01-6 Permisos por acción (view/create/update/delete/apply), antiforgery y un evento de auditoría por acción exitosa.
- AC-01-7 Exportación del historial de precios a Excel con `precios.history`; sin permiso, producto inexistente o rango invertido no entregan nada.
- AC-01-8 No regresión: crear, editar o dar de baja listas no predeterminadas y fijar precios en ellas no cambia el precio que resuelven Venta y Cotización; sin listas se usa el precio base; el ABM no referencia Venta/Cotización/Cliente.
- AC-01-9 Alcanzabilidad: el menú ofrece «Listas de precios» solo con `precios.view` y enlaza con el circuito de cambios por lotes (y viceversa).
- AC-01-10 E2E en navegador: menú → alta con validaciones → predeterminada → edición → precio manual → historial → baja.
- AC-01-11 El circuito por lotes existente (simular, aprobar, rechazar, cancelar, aplicar, revertir, umbral, cambio rápido, exportación) no regresiona.

### Transversales
- AC-T-1 Instalación completa sobre SQL Server nuevo y descartable con **todas** las migraciones EF y datos sintéticos.
- AC-T-2 Una base nueva queda con **un solo** usuario administrador inicial de prueba y **cero** clientes, productos, ventas, proveedores o créditos importados.
- AC-T-3 Ningún camino de código ni script importa filas de `TheBuryProjectDb`.
- AC-T-4 Build sin errores, tests focalizados y suite de CI verdes sobre el SHA final.

## 5. Verificación de huecos de D-01

Estado verificado en `main` 6326d264 antes de implementar (lectura de código, rutas, menú y tests).

| Capacidad | Estado inicial | Evidencia |
|---|---|---|
| ABM de listas en la UI | **Falta** | No existía `ListaPrecioController` ni vistas; `DiagnosticoController` listaba un «ListasPreciosController» inexistente; los permisos `precios.create/update/delete` estaban sembrados sin ninguna acción que los usara. Las listas solo se podían crear por base de datos |
| Alcanzable desde el menú | **Falta** | `_Layout.cshtml` no enlazaba `/CambiosPrecios` ni listas; el circuito por lotes solo se alcanzaba escribiendo la URL |
| Lista predeterminada única | Parcial | El servicio la mantenía con `ExecuteUpdate` (sin transacción) y sin índice: dos altas simultáneas podían dejar dos predeterminadas; se podía dar de baja la predeterminada y dejar Venta/Cotización en precio base |
| Validaciones de la lista | Parcial | Solo el índice único de `Codigo` (distingue según la collation; sin mensaje: `DbUpdateException`); `CrearListaPrecioViewModel` con rangos estaba sin uso; no se validaba margen mínimo ≤ margen, redondeo ni JSON |
| Edición de la lista | **Defecto** | `UpdateListaAsync` no copiaba `MargenMinimoPorcentaje`, `ReglaRedondeo` ni `Notas`: se perdían en silencio |
| Baja de lista usada | Parcial | Baja lógica (conserva precios por FK Restrict) con una variable `tienePrecios` sin usar; no protegía la predeterminada ni la lista de Mercado Libre |
| Precio por producto y lista | Parcial | `SetPrecioManualAsync` (vigencia, cierre del anterior) sin ningún llamador ni UI; historial: `GetHistorialPreciosAsync` + `CambiosPrecios/Historial` existen. El servicio aceptaba vigencias futuras (producto sin precio de lista) o anteriores al vigente |
| Cálculo automático por margen y redondeo | Existe | `CalcularPrecioAutomaticoAsync`, `AplicarRedondeo` (por defecto centena), con tests; sin uso en la UI |
| Margen mínimo | Parcial | `ValidarMargenMinimoAsync` sin llamadores y con un 10 % implícito si la lista no define mínimo |
| Flujo por lotes | Existe | Simular → aprobar/rechazar/cancelar → aplicar → revertir, cambio rápido y aplicar desde catálogo; `CambiosPreciosController` + 7 vistas; `PrecioServiceBatchTests`, `CambiosPreciosAplicarRapido*Test` |
| Autorización por umbral | Existe (informativo) | `RequiereAutorizacionAsync`: promedio absoluto ≥ 10 % (`Precios:UmbralAutorizacionPorcentaje`); aplicar siempre exige lote aprobado; ver R-14 |
| Exportación de historial | Parcial | `ExportarHistorialPreciosAsync` (con tests) sin endpoint ni botón |
| Permisos | Existe | Módulo `precios` (view, simulate, approve, apply, revert, create, update, delete, history); Gerente sin create/update/delete (`PermisosMoraPreciosPorRolTests`) |
| Auditoría | Parcial | Los lotes guardan solicitante/aprobador/aplicador/revertidor; el ABM de listas solo tenía `CreatedBy/UpdatedBy` (sin evento de seguridad) |
| Concurrencia | Existe | `RowVersion` en listas y lotes (tests de RowVersion); índice único del precio vigente por (producto, lista); el conflicto del precio manual salía como excepción de base |
| Venta/Cotización | Existe | `PrecioVigenteResolver` sin lista explícita: predeterminada activa o precio base; no se asigna lista a clientes (R-07) |

Implementado por ser inequívoco: ABM con permisos, validaciones, predeterminada única con índice, baja protegida, edición sin pérdida de campos, precio manual por producto y lista con historial y margen mínimo, exportación del historial por producto, menú y enlaces cruzados, mensajes de error de concurrencia y auditoría. **No implementado** (decisión del propietario): R-03, R-07, R-14 a R-20.

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

## 8. Resultado D-01 (listas de precios y precios por lotes)

Implementado en la rama `task/d01-d02-d03-20261114` (2 commits). La verificación de huecos de la sección 5 mostró que el motor (servicio, lotes, cambio rápido) ya existía y que faltaba la capa de administración: se agregó el ABM de listas, la fijación manual de precios por producto y lista, la exportación del historial y la navegación, y se corrigieron los defectos del servicio. **No** cambia el cálculo de precio de Venta/Cotización, no asigna listas a clientes (R-07) ni toca el costeo (R-03).

- Servicio (`PrecioService`): validaciones y normalización de listas; `UpdateListaAsync` ya no pierde margen mínimo, redondeo ni notas; predeterminada protegida (no se desmarca, no se desactiva, no se da de baja sin designar otra; debe estar activa); baja protegida si la usa Mercado Libre (conserva precios e historial); `SetPrecioManualAsync` con margen mínimo de la lista, tope, decimales, sin vigencia futura ni anterior a la vigente y conflicto de concurrencia con mensaje; consultas `GetResumenListasAsync`, `GetPreciosDeListaAsync` y `CalcularPrecioSugerido`.
- Migración única `D01_ListaPrecioPredeterminadaUnica`: solo reemplaza el índice `IX_ListasPrecios_EsPredeterminada` por un índice único filtrado (`[IsDeleted] = 0 AND [EsPredeterminada] = 1`). Se validó con `dotnet ef migrations script` (sin conexión) y se aplicó solo a una base LocalDB descartable al arrancar la instancia de prueba (ya eliminada); nunca a `TheBuryProjectDb`. Una base que tuviera dos predeterminadas no autorizaría este índice: el servicio siempre las mantuvo únicas y la base de despliegue es nueva.
- UI: `ListaPrecioController` (`/ListaPrecio`: listado, alta, edición, baja, precios por producto y fijar precio), menú Logística > «Listas de precios», enlaces cruzados con `/CambiosPrecios`, y `CambiosPrecios/ExportarHistorial` con botón en el historial. Permisos sin cambios en el catálogo: `precios.view|create|update|delete` para las listas, `precios.apply` para fijar un precio, `precios.history` para exportar; ninguna acción mutante nueva fuera de las cuatro POST de `ListaPrecioController` (inventario de acciones mutantes regenerado).

| AC | Estado | Evidencia |
|---|---|---|
| AC-01-1 | VERIFICADO | `ListaPrecioAbmServiceTests`: `AC011_Alta_NormalizaCodigoANombreYPersisteConAuditoria`, `AC011_CodigoRepetido_…`, `AC011_EditarConElCodigoDeOtraLista_…`, `AC011_DarDeBajaLiberaElCodigoParaUnaListaNueva`, `AC011_Alta_SinNombreOSinCodigo_SeRechaza`, `AC011_RowVersionObsoleto_AlEditarOBorrar_LanzaConcurrencia`; HTTP `Admin_CreaEditaYDaDeBajaUnaLista_ConEventosDeAuditoria`, `Admin_EdicionConRowVersionObsoleto_…` |
| AC-01-2 | VERIFICADO | `AC012_Editar_PersisteTodosLosCampos_…`, `AC012_MargenMinimoMayorQueElMargen_SeRechaza` (2 casos), `AC012_MargenMinimoIgualAlMargen_SePermite`, `AC012_ValoresFueraDeRango_SeRechazan` (7 casos), `AC012_RedondeoDesconocidoOJsonInvalido_SeRechazan`, `AC012_EdicionInvalida_NoDejaLaListaModificada`; HTTP `Admin_AltaConCodigoRepetidoOReglasInvalidas_…` |
| AC-01-3 | VERIFICADO (SQLite; la concurrencia real sobre SQL Server NO se probó) | `AC013_MarcarOtraComoPredeterminada_DejaSoloUna`, `AC013_ElIndiceDeLaBase_ImpideDosPredeterminadas…` (índice único filtrado), `AC013_LaPredeterminadaNoSeDeSmarcaPorEdicion_NiSeDaDeBaja`, `AC013_UnaListaInactivaNoPuedeSerPredeterminada`, `AC013_DesignarOtraPredeterminada_PermiteDarDeBajaLaVieja`; HTTP `Admin_NoPuedeDarDeBajaLaPredeterminada_…`; migración con `dotnet ef migrations script` |
| AC-01-4 | VERIFICADO | `AC014_BajaDeListaConPrecios_ConservaPreciosEHistorial`, `AC014_ListaUsadaPorMercadoLibre_NoSePuedeDarDeBaja`, `AC014_ResumenDeListas_CuentaSoloPreciosVigentes` |
| AC-01-5 | VERIFICADO | `AC015_PrecioManual_GuardaCostoMargenUsuarioYNotasYCierraElAnterior`, `AC015_PrecioManual_BajoElMargenMinimoDeLaLista_…` (incluye el borde exacto), `AC015_PrecioManual_ListaSinMargenMinimo_NoRestringe`, `AC015_PrecioManual_RechazaVigenciaFuturaAnteriorAlVigenteTopesYDecimales`, `AC015_DosPreciosVigentes…_LosImpideElIndiceUnico`, `AC015_PreciosDeLista_PaginaConPrecioVigenteYSugeridoPorMargen`; HTTP `Admin_FijarPrecio_ValidaYPersiste` (el costo sale del producto, no del formulario) |
| AC-01-6 | VERIFICADO | `ListaPrecioHttpTests`: `SoloVer_VeElListadoYLosPreciosPeroNoRecibeNingunaAccionDeEscritura`, `SoloVer_NoPuedeCrearEditarDarDeBajaNiFijarPrecios_YNoCambiaNada`, `SinPermisoDePrecios_NoPuedeVerElListado`, `Gerente_PuedeVerYFijarPrecios_PeroNoAdministrarLasListas`, `Admin_SinTokenAntiforgery_SeRechazaElAlta`, conteo de eventos `crear-lista`/`editar-lista`/`baja-lista`/`fijar-precio`; `ListaPrecioUiContractTests` (permiso por acción, antiforgery, sin lógica en controller/JS); `AutorizacionMutantesSinEfectosHttpTests` e inventario versionado |
| AC-01-7 | VERIFICADO | `ExportarHistorial_ConPermisoDeHistorial_DevuelveUnExcelConLosPreciosDelProducto` (lee el `.xlsx`), `ExportarHistorial_SinPermisoDeHistorial_OConProductoInexistente_NoEntregaNada` (403/404/rango invertido), `Historial_MuestraElBotonDeExportarSoloConPermisoDeHistorial`; el contenido del Excel ya estaba cubierto por `PrecioServiceExportTests` |
| AC-01-8 | VERIFICADO | `AC018_ABMDeListasNoPredeterminadas_NoCambiaElPrecioQueResuelvenVentaYCotizacion`, `AC018_SinListas_VentaYCotizacionUsanElPrecioBaseDelProducto`, `AC018_FijarPrecioEnUnaListaNoPredeterminada_NoToleraAlPrecioDeVenta`; `ElAbmNoCambiaElCalculoDePrecioNiAsignaListasAClientes` (contrato); más `VentaServicePrecioTests` y `PrecioVigenteResolverTests` existentes |
| AC-01-9 | VERIFICADO | `ElMenuOfreceListasDePreciosSoloAQuienTienePreciosView_YLasPantallasSeEnlazanEntreSi`, `ElMenuYLasPantallasDeLotesSeEnlazan` |
| AC-01-10 | VERIFICADO en navegador (instancia propia sobre LocalDB descartable ya eliminada; spec versionada `e2e/listas-precios-flujo.spec.js`, 8 de 8 pruebas en el proyecto 1366×768 más el login) | Menú → alta con rechazo de margen mínimo > margen y código repetido → predeterminada → mover la predeterminada → edición (los campos se conservan) → precio manual (rechazo bajo el mínimo, guardado, segundo precio con historial de 2 filas) → baja con confirmación; sin errores de consola ni 5xx; sin desborde horizontal en 1366, 1024 y 390 con capturas revisadas (listado, formulario, precios y fijar precio) |
| AC-01-11 | VERIFICADO (suites existentes, sin cambios) | `PrecioServiceBatchTests`, `PrecioServiceCambioDirectoTests`, `CambiosPreciosAplicarRapido*Test`, `PrecioServiceExportTests`, `CatalogoServiceTests`, `PermisosMoraPreciosPorRolTests` pasan con los cambios |

Validación ejecutada: build 0 errores; `dotnet test --filter` focalizado (Autorizacion, Precio, CambiosPrecios, Catalogo, Producto, Permiso, Architecture, Migracion, Layout, HttpIntegrationCollection, ListaPrecio, Seguridad, Cotizacion): 2559 superadas, 0 fallidas, 11 omitidas (las omitidas son los harness de concurrencia SQL Server que exigen variables de entorno, como en el resto de la suite); `git diff --check` sin hallazgos. No se corrió la suite completa.

Revisión visual: se corrigió, tras las capturas, la columna Acciones que quedaba fuera de vista en 1024 (se unió el tipo a la celda de la lista y margen/vigencia a la del precio), los valores `25.00` en inputs numéricos y los textos «— recargo». Estados NO VERIFICADOS: tablet 768×1024; el historial con filas dentro de «Fijar precio» solo se capturó en 1366 por aserciones (no se revisó visualmente en 1024/390); lector de pantalla; el spec e2e no se corrió en CI (solo local, y solo el proyecto 1366×768, que recorre los tres viewports con `setViewportSize`); la concurrencia real de la predeterminada y del precio manual sobre SQL Server (solo SQLite e índices); la baja de una lista con precios se probó por servicio, no en navegador.

Ambigüedades abiertas (decisión del propietario): R-07 (lista por cliente o medio de pago), R-03 (costeo), R-14 (separación de funciones y umbral de autorización de lotes), R-15 (margen mínimo en lotes), R-16 (efecto de cambiar la predeterminada), R-17 (vigencia futura), R-18 (redondeo por defecto), R-19 (costo por lista) y R-20 (exportación masiva).

## 9. Observaciones de la revisión independiente

Hallazgos de la revisión independiente y su tratamiento. Los corregidos tienen prueba; los demás quedan documentados sin cambio de código.

### Corregidos

- **D-01** índice único de la lista predeterminada: la migración normaliza antes de crear el índice (si hubiera más de una predeterminada no eliminada, queda la de menor Orden/Id; idempotente). Verificado aplicando la migración sobre un SQL Server descartable con 2 predeterminadas sembradas.
- **D-01** `ExportarHistorial`: `desde`/`hasta` se acotan a 2000-01-01..2100-12-31 (sin desborde con 9999-12-31).
- **D-02** `ListarPorClienteAsync` excluye referencias de clientes eliminados; `EditarAsync`, verificación y baja exigen el RowVersion leído (la vista y el JS lo envían) y devuelven el mensaje de concurrencia si cambió; la verificación ya no pisa las observaciones generales: agrega la nota con fecha y rechaza (sin perder nada) si el acumulado superaría 1000 caracteres. No se agregó una columna: la migración `AddClienteReferencia` y el snapshot no cambian.
- **D-03** una invariante de saldo violada dentro de la transacción hace rollback, limpia el ChangeTracker, registra Error y devuelve un mensaje genérico; un pago con cheque anulado o rechazado se rechaza en el servicio; `AnularImputacionNotaCredito` redirige a un comprobante de la aplicación (si el `comprobanteId` del POST no pertenece a ella, a la nota de crédito).

### No cambiados (decisión o criterio documentado)

- Costo y margen por producto con solo `precios.view`: ver R-22.
- Unicidad de cheque por pago: ver R-21.
- AC-02-2, teléfono coincidente con el propio cliente: decisión de producto abierta (ya documentada); no se implementó.

### Observaciones menores (sin cambio de código)

Observaciones de severidad baja señaladas por la revisión; se registran para una próxima iteración y no se modificó código:

- Fechas: tratamiento de fechas en algunos filtros y vencimientos (día comercial frente a UTC) cerca del borde de medianoche.
- Idempotencia de una nota de crédito anulada: comportamiento al reenviar la clave de idempotencia de una aplicación ya anulada.
- Estado de cuenta: uso de UTC en la presentación de algunas fechas.

## 10. Criterios transversales (AC-T) y despliegue sobre base nueva

Decisión del propietario (2026-11-14): el despliegue usa una **base nueva**, con un único administrador inicial de pruebas y sin importar usuarios ni datos de `TheBuryProjectDb`, que se conserva intacta. El ERP antiguo sigue en una VM del mismo servidor, administrada por el propietario.

| AC | Resultado | Evidencia |
|---|---|---|
| AC-T-1 instalación completa sobre SQL Server nuevo y descartable con todas las migraciones | VERIFICADO | `scripts/qa/instalacion-limpia.sh`: proyecto Compose propio con secretos sintéticos, `migrate` en Production: «142 migraciones aplicadas, 0 pendientes» y coincide con los 142 ids `[Migration]` del repo; las tablas nuevas de D-01/D-02/D-03 existen |
| AC-T-2 un solo administrador y cero datos de negocio | VERIFICADO | `AspNetUsers = 1` (`admin`, rol SuperAdmin); toda tabla con filas debe figurar en `scripts/qa/instalacion-limpia-tablas-esperadas.txt` (solo catálogos/seed de migraciones EF y configuración); una tabla fuera de la lista falla el script |
| AC-T-3 ningún camino importa datos históricos | VERIFICADO (auditoría de código) | ni `install.ps1`, ni `scripts/install`, `scripts/deploy` ni `docker/` referencian LocalDB, `RESTORE DATABASE` o `.bak`; `TheBuryProjectDb` solo aparece como nombre por defecto de la base dentro del contenedor, por lo que **en el servidor conviene fijar `MSSQL_DATABASE` a un nombre nuevo** en el `.env` para no confundirla con la antigua |
| AC-T-4 build, pruebas focalizadas y CI | build y focalizadas: ver secciones 6-9; CI: se registra en la PR sobre el SHA final | — |

Datos sintéticos: los tests de servicio, HTTP y SQL Server de D-01/D-02/D-03 siembran sus propios datos descartables; el script de instalación limpia verifica el estado **sin** datos. La carga de datos sintéticos sobre la base recién instalada vía UI se cubre con los specs Playwright de cada módulo (secciones 6-8), no con el script.

### Lo que depende del servidor definitivo (no probado por no tener acceso)

- Arranque automático del ERP sin inicio de sesión tras reinicio y corte de energía (BIOS «Restore on AC Power Loss»).
- Convivencia real con la VM del ERP antiguo (RAM, CPU, disco) y rendimiento de SQL Server en el disco real.
- Backups al segundo disco interno con retención por capacidad: la lógica existe (`BACKUP_DIR`, retención por días/mínimo), pero la **retención por capacidad** y el soporte de copia extraíble futura no están implementados; requieren una decisión de diseño aprobada y se prueban en el disco real. Las claves las custodia el propietario.
- Tailscale real y ACL publicada, firewall con cliente LAN real.
