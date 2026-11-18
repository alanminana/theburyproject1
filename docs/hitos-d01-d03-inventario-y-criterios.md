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
| R-03 | Costeo | El costo del producto no cambia con la factura (**implementada**) | Costo = último, promedio ponderado o por lote | Cambia márgenes y precios sugeridos de D-01: requiere método único. **RESUELTA PARCIALMENTE (2026-11-15):** se prepara el promedio ponderado solo para la valoración gerencial de stock (solo lectura, sección 12); el último costo de compra sigue como indicador de reposición y sugerencia; no se modifican valuaciones fiscales, márgenes históricos ni costos persistidos. Método único oficial: pendiente con el contador. |
| R-04 | Nota de crédito del proveedor | Reduce deuda imputándose a facturas (**implementada**) | Además genera ajuste de stock y costo | B acopla con devolución de unidad y costeo (R-03). |
| R-05 | Retenciones/percepciones (IIBB, Ganancias) | Fuera de alcance (**implementada**) | Campos y asientos de retención | Exige criterio fiscal del contador. |
| R-06 | Referencias de clientes y crédito | ~~Informativas~~ (reemplazada) | **RESUELTA con la decisión del propietario (2026-11-15): alternativa B.** Mínimo configurable (0..10, por defecto 1) de referencias verificadas para otorgar crédito personal; si faltan, excepción individual por venta autorizada por un segundo usuario con `ventas.authorize` | Ver sección 6b «Resultado D-02 referencias y crédito». Nunca levanta mora, BCRA, cupo ni documentación. |
| R-07 | Listas de precios por cliente o por medio de pago | Lista predeterminada + precio por lista (lo existente) | Lista asignable a cliente/segmento | B cambia el cálculo de precio en Venta/Cotización. **RESUELTA (2026-11-15): una lista general predeterminada para todos los clientes; no se implementa asignación por cliente (sección 11).** |
| R-08 | Quién recibe los permisos de cuentas por pagar (`cuentasporpagar.view/createinvoice/pay/cancel`) | Administrador/SuperAdmin los cuatro; Contador solo ver; Gerente y el resto ninguno (**implementada**) | Gerente con ver/registrar/pagar (y anular reservado) | B es una decisión de gobierno de pagos: se asigna a mano desde Seguridad > Roles. |
| R-09 | Pagos a cuenta (sin comprobante) | Todo pago se imputa por completo a comprobantes pendientes: suma de imputaciones = monto (**implementada**) | Anticipos a proveedor sin imputar, con saldo a favor | B exige un concepto «saldo a favor del proveedor» y reglas de aplicación posterior. |
| R-10 | Aplicar una nota de crédito a facturas | Exige `cuentasporpagar.pay` (compensa deuda, no mueve dinero); anular la aplicación exige `cancel` (**implementada**) | Permiso propio de «aplicar NC» | B agrega un quinto permiso; hoy no hay evidencia de que haga falta. |
| R-11 | Vínculo con la devolución a proveedor (RMA) | No se vincula: el RMA es por unidad (`ProductoUnidad`) y no tiene una clave natural de proveedor/importe (**implementada: omitido**) | Referencia informativa RMA → nota de crédito | B requiere definir qué RMA respalda qué nota de crédito. |
| R-12 | Estado de cuenta y antigüedad | La nota de crédito reduce el saldo neto al emitirse; el saldo de comprobantes y las NC sin aplicar se muestran separados. Antigüedad por día comercial: vence hoy o sin vencimiento = «Por vencer»; 1-30, 31-60 y +60 días de atraso (**implementada**) | NC reduce deuda solo al aplicarse; tramos contables distintos | B cambia lo que el contador ve como saldo neto. |
| R-13 | Unicidad del comprobante | (proveedor, tipo, número) recortado y en mayúsculas, solo entre no anulados: anular libera el número (**implementada**) | Unicidad aun entre anulados | B impide recargar un comprobante mal cargado con el mismo número. |
| R-14 | Autorización de cambios por lotes y umbral | Aplicar exige siempre un lote aprobado (`precios.approve`); el umbral (`Precios:UmbralAutorizacionPorcentaje`, 10 %) solo marca el lote como «requiere autorización» (informativo). Quien simula puede aprobar si tiene ambos permisos (**existente, no se tocó**) | Obligar aprobador distinto del solicitante, y/o exigir aprobación solo sobre el umbral | B es una política de control interno (separación de funciones); el cambio rápido ya la aplica por permisos. Hoy un usuario con simular + aprobar + aplicar puede hacer todo el circuito solo. **RESUELTA (2026-11-15): quien tiene los permisos correspondientes puede simular, aprobar y aplicar sin exigir otro usuario; la auditoría se mantiene (sección 11).** |
| R-15 | Margen mínimo de la lista en los lotes y en el cambio rápido | El margen mínimo solo se exige al fijar un precio manual por producto y lista, y solo si la lista lo define (**implementada**); los lotes solo advierten margen negativo | Rechazar (o advertir) en simulación/aplicación los ítems por debajo del mínimo | B puede bloquear liquidaciones legítimas; hay que definir si es bloqueo o aviso y quién puede saltearlo. `ValidarMargenMinimoAsync` (existente) usa 10 % si la lista no define mínimo y no tiene ningún llamador: no se usó. |
| R-16 | Efecto de designar la lista predeterminada | Es inmediato: Venta y Cotización resuelven el precio con la predeterminada y, para productos sin precio en ella, caen al precio base (**implementada**; la pantalla lo advierte y no deja desmarcarla ni darla de baja sin designar otra) | Confirmación reforzada, permiso propio o vigencia programada para cambiar de predeterminada | B protege contra un cambio de precios masivo accidental en ventas; hoy lo cubre el permiso `precios.update` (Administrador; no Gerente). **RESUELTA (2026-11-15) junto con R-07: lista general única; el efecto inmediato de designar otra predeterminada queda como está.** |
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
- AC-02-4 ~~La aptitud y el límite de crédito no cambian con o sin referencias~~: **reemplazado por la decisión R-06 (sección 6b)**. El **límite/cupo** sigue sin depender de las referencias (prueba de no regresión `ElCupoDisponible_NoDependeDeLasReferencias`); la **aptitud** sí: con menos referencias verificadas que el mínimo el cliente queda en «Requiere autorización».
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
| AC-02-4 | SUPERADO por R-06 (sección 6b) | (Evidencia histórica; el contrato «los servicios de crédito no referencian `ClienteReferencia`» y la igualdad de aptitud ya no valen: la regla vive en `ClienteAptitudService`. Vigente: el cupo no depende de las referencias.)  `ClienteReferenciaNoAfectaCreditoTests`: aptitud (`ClienteAptitudService.EvaluarAptitudSinGuardarAsync`) y cupo (`CreditoDisponibleService.CalcularDisponibleAsync`) idénticos con y sin referencias en los 4 estados y 3 tipos, con validaciones de crédito activas e inactivas; contrato de que los servicios de crédito/aptitud/scoring/venta no referencian `ClienteReferencia` |
| AC-02-5 | VERIFICADO | `Aislamiento_ListarSoloDevuelveLasReferenciasDelCliente`, `Aislamiento_NoSePuedeEditarVerificarNiDarDeBajaUnaReferenciaDeOtroCliente` (servicio) y `Aislamiento_UnEditorNoPuedeOperarUnaReferenciaDeOtroClientePasandoSuId` (HTTP) |
| AC-02-6 | ~~NO VERIFICADO~~ → ahora VERIFICADO (local), ver sección 6b: `e2e/cliente-referencias-credito.spec.js` crea el cliente por el formulario | (Estado anterior, superado) No se escribió una spec Playwright versionada. Hubo una corrida manual con Playwright MCP sobre una base LocalDB descartable (ya eliminada): alta, errores de validación, edición, verificación, baja, 1440 px y 390 px, sin desborde horizontal y sin errores de consola fuera de los 400 esperados. El cliente se creó por SQL, no por el formulario «Crear cliente», así que el recorrido «crear cliente → referencias» completo no está probado |

Otros contratos: `ClienteReferenciaUiContractTests` (ARIA, labels, dialogos, antiforgery, permiso por acción). La migración se validó con `dotnet ef migrations script` y se aplicó solo a una base LocalDB descartable (no a `TheBuryProjectDb`). Estados «NO VALIDADO»: foco/teclado en modales solo se probó con Escape y foco inicial; no hay prueba de lector de pantalla; viewport tablet no revisado.

## 6b. Resultado D-02 referencias y crédito

Decisión del propietario (R-06, alternativa B), implementada en la rama `task/decisiones-d01-d02-d03-20261115`. Una sola fuente de verdad en el backend: `ClienteAptitudService` cuenta las referencias `Verificada` no dadas de baja contra `ConfiguracionCredito.ReferenciasVerificadasMinimas`; `ValidacionVentaService` la traduce a la razón de autorización `ReferenciasInsuficientes` (nunca a un bloqueo) y `VentaService` aplica el flujo de autorización de ventas que ya existía (`EstadoAutorizacionVenta`: pendiente -> autorizada por otro usuario con `ventas.authorize`, motivo obligatorio, usuario, fecha y auditoría en la propia venta, más el evento de seguridad `Ventas/AutorizarExcepcionReferencias`). **No se creó un mecanismo paralelo** ni se usó `SolicitudAutorizacion`: esa entidad pertenece al circuito de umbrales (`AutorizacionService`) y la venta a crédito nunca la usó; la autorización real de una venta vive en los campos de `Venta`.

- Configuración: `ConfiguracionCredito.ReferenciasVerificadasMinimas` (0 = regla desactivada, 1..10, por defecto 1), editable en Configuración > Crédito personal > Recargos y cuotas (`configuracion.update`, SuperAdmin), validación `[Range(0,10)]` en el modelo y en el servicio, y evento de auditoría `Configuracion/ReferenciasMinimasCredito` (valor anterior -> nuevo) en el mismo `SaveChanges` que el cambio. Migración única `D02_ReferenciasVerificadasMinimasCredito`: una columna `int NOT NULL DEFAULT 1` en `ConfiguracionesCredito` (la fila existente también queda en 1).
- Efecto: faltan referencias => el cliente queda en `RequiereAutorizacion` (detalle «Referencias», no bloqueante). Si además hay mora bloqueante, BCRA no apto, cupo insuficiente o documentación incompleta, el resultado sigue siendo `NoViable` y la razón de referencias solo se agrega como información. La excepción documental/de cupo ya existente (que aplica el propio usuario con `ventas.authorize`) **no** autoriza las referencias: la venta queda pendiente igual.
- Segundo usuario: `AutorizarVentaAsync` exige, si la venta tiene la razón de referencias, que quien autoriza tenga `ventas.authorize` y sea distinto de `CreatedBy` y de `UpdatedBy` (si no se conoce el creador, se rechaza). Para las demás razones el creador con el permiso puede seguir autorizando (regla previa, sin cambios).
- Alcance de la excepción: es de esa venta (campos de la `Venta`). Si cambia el total, o si las referencias dejaron de alcanzar y la autorización vigente no las cubría (`RazonesAutorizacionJson` sin la razón 7), `UpdateAsync` la invalida y vuelve a pedirla (evento `InvalidarExcepcionReferencias`). El cliente no se puede cambiar una vez que existe el crédito (guard existente); en una venta sin crédito la edición reevalúa todo.
- Caminos cubiertos: Venta Create (`CreateAsync`), Edit (`UpdateAsync`, ambas ramas), Configurar el crédito (`CreditoController.ConfigurarVenta` GET/POST -> `IVentaService.AsegurarReferenciasAutorizadasAsync`, 409 con el motivo), Confirmar (`ConfirmarVentaAsync` y `ConfirmarVentaCreditoAsync` revalidan HOY), Cotización: simulador (`/api/cotizacion/aptitud-credito` informa `referencias`), guardado (no evalúa crédito: no otorga nada) y conversión a venta (misma validación unificada), prevalidación del wizard y accesos HTTP directos (misma ruta de servicios; antiforgery y permisos por atributo).
- UI: ficha del cliente (solapa Datos) con el resumen «Referencias verificadas: X de N requeridas» (`role=status`, `aria-live`), actualizado al verificar sin recargar; wizard de Venta (paso Crédito) y Cotizador muestran el motivo con enlace a `/Cliente/Details/{id}#referencias-card`; pantalla de autorización con la nota del segundo usuario; configuración con el campo y su ayuda.
- AC-02-2 (parte teléfono): la referencia tampoco puede tener el teléfono del cónyuge (`ConyugeTelefono`).

| AC | Estado | Evidencia |
|---|---|---|
| R-1 mínimo configurable 0..10, por defecto 1, con auditoría | VERIFICADO | `ClienteReferenciaCreditoReglaTests` (`ElMinimoEsConfigurable`, `ConMinimoCero_*`, `ActualizarElMinimo_ValidaRangoYPersiste`); HTTP `Configuracion_ElMinimoFueraDeRango_SeRechazaSinCambiarNada_YCeroLoDesactivaConAuditoria` (11, -1 y «abc» rechazados; 0 guardado con el evento `ReferenciasMinimasCredito`) y `Configuracion_UnUsuarioSinPermisoDeConfiguracion_*`; migración revisada (`ReferenciasCreditoUiContractTests.LaRegla_VivePorUnLado_*`, `dotnet ef migrations script` sin conexión) |
| R-2 cuentan solo las verificadas no eliminadas del propio cliente | VERIFICADO | `SoloCuentanLasVerificadas_*` (pendiente / no localizada / rechazada), `UnaReferenciaVerificadaDadaDeBaja_NoCuenta`, `LasReferenciasDeOtroCliente_NoCuentan`, contrato `FuenteUnica_*` |
| R-3 falta -> motivo claro y excepción individual desde la venta (segundo usuario, motivo, usuario, fecha, auditoría) | VERIFICADO | `VentaReferenciasCreditoServiceTests` (`Create_SinReferenciasVerificadas_*`, `Autorizar_UnSegundoUsuarioConPermiso_AutorizaConTrazaCompletaYSoloUnaVez`, `Autorizar_SinMotivo_SeRechaza`); HTTP `Autorizar_ElSegundoUsuario_*`; e2e `cliente-referencias-credito.spec.js` (corrida local: el Gerente autoriza por la UI) |
| R-4 sin autoautorización | VERIFICADO | `Autorizar_ElCreadorConPermiso_NoPuedeAutorizarseASiMismo`, `Autorizar_QuienModificoLaVenta_*`, `Autorizar_UnSegundoUsuarioSinPermiso_NoPuede`; HTTP `Autorizar_ElCreadorPorPostDirecto_*` (SuperAdmin); conversión `VentaConvertida_ElCreadorNoPuedeAutorizarla_*`; e2e (el Administrador creador no puede) |
| R-5 sin excepciones globales; se invalida si cambian monto/cliente | VERIFICADO (monto y referencias) | `Autorizar_LaExcepcionNoEsGlobal_*`, `Editar_ConAutorizacionPorReferencias_ElCambioDeMontoLaInvalida*`, `..._SinCambiarElMonto_LaConserva`, `Editar_SiLasReferenciasDejaronDeAlcanzar_*`. El cambio de cliente no es posible con crédito existente (guard previo en `ActualizarDatosVenta`) y sin crédito la edición reevalúa |
| R-6 no evade mora/BCRA/cupo/documentación ni otro bloqueo | VERIFICADO | `ConMoraBloqueante_LaExcepcionNoLevantaNada_*`, `ConMoraBloqueante_YReferenciasInsuficientes_*`, `ConBcraDesfavorable_*`, `FaltanReferencias_PeroHayUnBloqueo_ElClienteSigueNoApto` (documentación), `ConCupoExcedido_YSinReferencias_*`, conversión `Convertir_ConMoraBloqueante_*`; e2e (mora: la excepción solicitada no crea la venta) |
| R-7 todos los caminos con una sola fuente en el backend | VERIFICADO (salvo lo anotado) | Create/Edit: servicio; Configurar: HTTP `ConfigurarVenta_SiLasReferenciasDejaronDeAlcanzar_*` (409); Confirmar: `Confirmar_ConReferenciasInsuficientes*`, `AsegurarReferenciasAutorizadas_*`; conversión: `CotizacionConversionReferenciasTests`; simulador: HTTP `AptitudDelCotizador_*`; ventas que no son crédito personal sin cambios (`Create_LaVentaQueNoEsCreditoPersonal_NoCambia`, `AsegurarReferenciasAutorizadas_NoAplicaAVentasQueNoSonCreditoPersonal`). El guardado de una cotización no evalúa crédito (no hay nada que bloquear ahí; el crédito se decide al convertir) |
| R-8 UI: verificadas vs mínimo, enlace a referencias, estados claros, accesible | VERIFICADO (sin lector de pantalla) | `ReferenciasCreditoUiContractTests`, e2e (ficha, wizard, autorización, configuración; 1366x768, sin desborde ni errores de consola) y capturas revisadas. NO VERIFICADO: 1024 y 390 px, lector de pantalla |
| R-9 doble uso e idempotencia | VERIFICADO | segunda autorización rechazada y sin evento duplicado (`..._SoloUnaVez`, HTTP `Autorizar_ElSegundoUsuario_*`); `AutorizarVentaAsync` solo actúa sobre una venta pendiente |
| R-10 IDOR y permisos | VERIFICADO | HTTP `Autorizar_UnUsuarioSinPermiso_*`, `Autorizar_UnaVentaInexistente_*`, `Autorizar_SinMotivo_OSinTokenAntiforgery_*`; la configuración exige `configuracion.update`; el inventario de acciones mutantes no cambió (no hay acciones nuevas) |
| R-11 concurrencia | NO VERIFICADO sobre SQL Server real | Dos autorizaciones simultáneas están acotadas por el estado pendiente y el `RowVersion` de la venta (el segundo guardado falla por concurrencia); no se ejecutó un harness SQL Server para esto |
| AC-02-2 teléfono del cónyuge | VERIFICADO | `Crear_ConTelefonoDelConyuge_SeRechaza` (3 formas del número), `Editar_ATelefonoDelConyuge_SeRechaza` |
| AC-02-6 E2E Playwright | VERIFICADO (local) / NO VERIFICADO en el job de CI | `e2e/cliente-referencias-credito.spec.js` (7 pruebas, tag `@config-referencias`, instancia propia sobre LocalDB descartable ya eliminada, dos corridas seguidas). Falta el cambio de workflow descripto en el informe de entrega |

Decisiones conservadoras / ambigüedades: (1) ~~con mora no bloqueante y referencias insuficientes se autorizaban ambas razones juntas~~ SUPERADO por la sección 14: cada razón se autoriza de forma expresa y separada. (2) Una venta ya existente de un cliente sin referencias, al activarse la regla, no se invalida retroactivamente: solo se revalida al editar o al confirmar. (3) Con `DEFAULT 1` la regla queda activa también en la fila de configuración ya existente. (4) El cambio de monto invalida la excepción por el criterio conservador del total, aunque la regla de referencias no dependa del monto. (5) El dataset E2E común deja el mínimo en 0; el spec lo activa con `ReferenciasCreditoE2ESeedRunner.Activar` (editar el valor por la UI exige `configuracion.update`, que el usuario E2E no tiene).

Validación ejecutada: build 0 errores (5 advertencias preexistentes); `dotnet test --filter` focalizado (Venta, Credito, Cotizacion, Cliente, Autorizacion, Permiso, Architecture, Migracion, HttpIntegration, ConfiguracionPago, Seguridad, UiContract, Layout, Mora): 5114 superadas, 0 fallidas, 66 omitidas (harness de concurrencia y seeds E2E que exigen variables de entorno); `dotnet ef migrations has-pending-model-changes` sin cambios pendientes; `git diff --check` limpio. Playwright local: `cliente-referencias-credito.spec.js` 7/7 (dos corridas seguidas) y regresión de `cliente-details-tabs`, `cliente-aptitud-punitorio`, `cliente-form-validacion`, `credito-visual-*` y `cotizacion-credito-personal-aptitud-contradiccion` con el mínimo en 0 (dataset común), todos verdes. No se corrió la suite completa del repositorio ni la del job de CI.

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

#### Falla intermitente de `scripts/qa/instalacion-limpia.sh` (2026-11-15): causa raíz y corrección

En una corrida el paso `docker compose up -d --build migrate` falló sin mostrar la causa (el script descartaba toda la salida con `>/dev/null 2>&1`) y la corrida siguiente pasó.
Se investigó con 8 corridas seguidas del script original más diagnóstico (8/8 correctas con caché), 10 corridas del script nuevo (9 correctas y 1 con la causa 2), una corrida sin caché de capas (`--no-cache`, correcta) y, con el script final sin modificar, 10 corridas seguidas (10/10 correctas) más la prueba de reintento por colisión.
Resultado: **dos causas distintas**, ambas identificadas con su mensaje real y ahora tratadas de forma determinista.

| # | Causa | Evidencia | Corrección |
|---|---|---|---|
| 1 | **Colisión de subred** (reproducida de forma determinista). El script elegía `BURY_NET_SUBNET=172.31.<aleatorio 20-219>.0/24` a ciegas. Este host tiene otros stacks `bury-*` (`bury-qa-seg/prd/fin/cfg-net` ocupan `172.31.91-94.0/24`, más `172.29.x` y `172.30.x`): una subred que se solapa hace fallar la creación de la red del proyecto. | Forzando `172.31.91.0/24`: `Network ..._bury-net Error: invalid pool request: Pool overlaps with other one on this address space`, `up` sale con error y el script original no mostraba nada. Probabilidad por corrida ≥ 2 % solo con esas 4 redes (más si otro proceso crea redes a la vez), por eso «a veces falla y la siguiente pasa» (otra subred aleatoria). | El script elige una `/24` de `172.31.0.0/16` **libre**, comparándola (aritmética de CIDR) con todas las subredes de `docker network inspect` y con las rutas IPv4 del host. Si Docker aun así la rechaza por solapamiento (otro proceso creó una red en el mismo instante) limpia el proyecto y reintenta con otra subred, **máximo 3 intentos y solo para esta firma**. `INSTL_SUBNET=<cidr>` fuerza una subred (se valida igual; si se solapa, falla sin tocar Docker). Probado: forzada la colisión, falla de inmediato con «se solapa con 172.31.91.0/24»; con la primera subred forzada a una ocupada, el reintento elige otra y la instalación termina OK (143 migraciones, 1 administrador, tablas esperadas). |
| 2 | **Lectura transitoria del contexto de build** (observada 1 vez en 21 corridas, capturada gracias al nuevo diagnóstico). `failed to solve: failed to compute cache key: failed to calculate checksum of ref ...: "/global.json": not found` en `COPY global.json TheBuryProyect.csproj ./`, aunque `global.json` **existe** en el repo (mtime anterior a la corrida, tracked, sin cambios). | El contexto se lee desde el worktree de Windows (`/mnt/d`, DrvFs) mientras otros procesos trabajan en el mismo árbol (otro agente compilando/editando, otros stacks); la misma corrida repetida pasó. No se pudo reproducir a demanda ni se probó una causa más profunda dentro de BuildKit/DrvFs. | El script clasifica la falla (`transient_cause`): si el archivo que BuildKit dice no encontrar **existe** en el repo, es una lectura transitoria y se reintenta (mismo tope de 3 intentos); si el archivo realmente falta **no** se reintenta. Probado el clasificador con logs de ambas situaciones y con otras fallas (restore de NuGet, daemon caído: sin reintento). |

Diagnóstico ante cualquier falla que no se reintenta: se imprime la subred solicitada, las últimas 60 líneas de `up.log`, `docker compose ps -a`, los logs de `db`, `db-init` y
`migrate` y las subredes en uso del host, con los secretos sintéticos de la corrida redactados (`[REDACTED]`). Se mantienen los tres chequeos (migraciones = ids únicos del repo y 0
pendientes, un único administrador `SuperAdmin`, ninguna tabla con filas fuera de `instalacion-limpia-tablas-esperadas.txt`). `--no-cache` reconstruye la imagen sin caché para descartar fallas
dependientes de caché (probado: correcta). **No se afirma** que la única falla original haya sido la causa 1 o la 2: su salida se descartó; ambas son las únicas halladas en 30 corridas (la corrida 6 del lote intermedio abortó por una edición del script en curso, no cuenta) y
las dos quedan cubiertas.

### Lo que depende del servidor definitivo (no probado por no tener acceso)

- Arranque automático del ERP sin inicio de sesión tras reinicio y corte de energía (BIOS «Restore on AC Power Loss»).
- Convivencia real con la VM del ERP antiguo (RAM, CPU, disco) y rendimiento de SQL Server en el disco real.
- Backups con retención por capacidad (no está confirmado que exista un segundo disco interno; con un solo disco la copia extraíble pasa a ser necesaria, ver `docs/backup-restore.md` §8.2): **implementada y ensayada** (2026-11-15, `docs/backup-restore.md` §8.2: `BACKUP_MAX_TOTAL_GB`/`BACKUP_MIN_FREE_GB`/`BACKUP_CAPACITY_MIN_CHAINS`, borrado por cadenas recuperables, falla ruidosa si el disco es insuficiente, copia verificada al disco extraíble futuro `BACKUP_REMOVABLE_DIR`; probada con cadenas sintéticas y con SQL Server real descartable). Falta confirmar la cantidad de discos del servidor, probarla en el disco definitivo, fijar los límites según su tamaño real y probar un extraíble físico. Las claves las custodia el propietario.
- Tailscale real y ACL publicada, firewall con cliente LAN real.

## 11. Decisiones D-01 aprobadas (2026-11-15): lista general única y circuito de lotes con un mismo usuario

Rama `task/decisiones-d01-d02-d03-20261115`. Decisiones del propietario:

1. **Una lista general predeterminada para todos los clientes** (R-07 y R-16 **resueltas**). No se implementa asignación de listas por cliente, segmento ni medio de pago. Verificado en el código real: `Cliente` no tiene lista de precios; `ProductoService` (Venta) llama `IPrecioVigenteResolver.ResolverAsync(productoId)` sin lista y `CotizacionConversionService` llama `ResolverBatchAsync(productoIds, null, ...)`: ambos resuelven con la **predeterminada activa** (`ResolverListaActivaIdAsync`) y, si el producto no tiene precio en ella, con el precio base. La única otra referencia a una lista es la configuración de Mercado Libre (`MercadoLibreConfiguracion.ListaPrecioId`), que no depende del cliente. El efecto inmediato de designar otra predeterminada (R-16) queda como está: la pantalla lo advierte y el permiso `precios.update` lo protege.
2. **Quien tenga los permisos correspondientes puede simular, aprobar y aplicar cambios, sin exigir otro usuario** (R-14 **resuelta**). Verificado en `PrecioService`/`CambiosPreciosController`: ningún camino compara solicitante, aprobador y aplicador (no había nada que quitar). Cada paso exige su permiso propio (`precios.simulate`, `precios.approve`, `precios.apply`), también por POST directo. **La auditoría se mantiene**: `PriceChangeBatch` guarda `SolicitadoPor/FechaSolicitud`, `AprobadoPor/FechaAprobacion` (con las notas de la aprobación o el motivo del rechazo) y `AplicadoPor/FechaAplicacion`, y cada precio creado guarda `BatchId` y `CreadoPor`; el cambio directo del catálogo deja además su `CambioPrecioEvento` con motivo. Que la misma persona cumpla los tres pasos no borra ninguno de esos registros. El umbral de autorización sigue siendo informativo.

### Resultado de la concurrencia en SQL Server real (contenedor descartable propio)

`TheBuryProyect.Tests/Concurrency/ListaPrecioPrecioManualSqlServerTests.cs` (8 pruebas; 12 rondas por escenario; catálogo propio `qa_fin_conc_lpre` con todas las migraciones). Defectos que las pruebas **revelaron y se corrigieron** en `PrecioService`:

- **`SetPrecioManualAsync`**: dos fijaciones simultáneas sobre el mismo (producto, lista) leían el mismo precio vigente y la segunda cerraba el precio **nuevo** de la primera (`UPDATE ... WHERE EsVigente`), pisando un cambio ya confirmado: quedaba como vigente el precio de menor vigencia («el vigente no es el más reciente») y vigencias invertidas. Corrección: se cierra **exactamente el precio validado (por Id) y solo si sigue vigente**; si otro lo cerró (`filas afectadas != 1`) se rechaza con el mensaje de conflicto («Otro usuario cambió el precio…, recargá»). Además el cierre nunca queda antes del inicio del precio que se cierra (`max(desde − 1 s, desde del anterior)`).
- **`AplicarBatchAsync`** (lote vs precio manual simultáneo): mismo patrón de cierre por Id con rechazo de todo el lote ante conflicto, y la vigencia nueva nunca es anterior o igual a la del precio que reemplaza (se adelanta 1 s si hace falta).

Lo que **no** necesitó corrección (probado): predeterminada única con dos «marcar predeterminada» simultáneos, dos altas predeterminadas simultáneas, baja vs designación simultáneas (índice único filtrado + servicio: siempre exactamente una, sin 500, el perdedor recibe un mensaje controlado); un segundo precio sobre un producto sin precio previo (índice único del vigente).

Mutación («sin la protección»): con el `PrecioService` original (HEAD anterior) las pruebas D01-CONC-05 y D01-CONC-07 **fallan** («el vigente no es el más reciente», «vigencia invertida»); con `QuitarPredeterminadaAOtrasAsync` anulado fallan D01-CONC-01 y D01-CONC-02.

Pruebas del circuito con un mismo usuario: `CambiosPreciosCircuitoMismoUsuarioHttpTests` (5): un usuario con los tres permisos completa simular → aprobar → aplicar con auditoría completa e historial; sin simular, sin aprobar (aprobar/rechazar/cancelar) y sin aplicar se rechaza por POST directo y no cambia el estado ni el precio; con simular+aplicar no puede aplicar un lote sin aprobar.

Cómo corren en CI: el job `sqlserver-concurrencia` de `.github/workflows/fin-dev-sqlserver.yml` (matriz RCSI 0/1) ejecuta clases por filtro; **hay que agregar** `FullyQualifiedName~ListaPrecioPrecioManualSqlServerTests` a un paso (p. ej. el del filtro `OrdenCompraRecepcionSqlServerTests|PrecioRevertirEventoConcurrenciaSqlServerTests`). No se modificaron workflows.

## 12. Costeo gerencial (R-03)

Decisión del propietario (2026-11-15): **preparar el promedio ponderado para la valoración gerencial del stock; mantener el último costo de compra como indicador de reposición y sugerencia de precios; NO modificar valuaciones fiscales, márgenes históricos ni costos persistidos.**

### De dónde sale hoy el costo (verificado)

- `Producto.PrecioCompra`: costo de **ficha**, se edita a mano (`ProductoService`); es el que usan los precios sugeridos y los márgenes (`CalcularPrecioAutomaticoAsync` recibe el costo como parámetro, `ProductoPrecioLista.Costo`, `MovimientoStockService.CompletarCostoMovimiento` como respaldo). **La recepción de una compra no lo actualiza.**
- `OrdenCompraDetalle.PrecioUnitario`: precio pactado. Al recepcionar (`OrdenCompraService.RecepcionarAsync`) se registra una **entrada de Kardex** (`MovimientosStock`) con `OrdenCompraId`, `CostoUnitarioAlMomento = PrecioUnitario` y `FuenteCosto = "OrdenCompraDetalle"`.
- `MovimientoStock` guarda por movimiento `StockAnterior/StockNuevo`, costo unitario al momento y fuente (`OrdenCompraDetalle`, `ProductoActual`, `AjusteManual`, `VentaDetalleSnapshot`, `NoInformado`). Las salidas guardan el costo de ficha del momento (instantánea para reportes), no un costo promedio.
- No existe costo promedio, ni costo por lote, ni costo en `ProductoUnidad` utilizable para esto.

### Qué se implementó (solo lectura)

`IValoracionStockGerencialService` / `ValoracionStockGerencialService`: **no escribe en ninguna tabla** (consultas `AsNoTracking`; no hay `SaveChanges`). Por producto: stock actual, costo promedio ponderado, último costo de compra (y fecha), costo de ficha (informativo), valor gerencial = stock × promedio, valor a último costo = stock × último costo y la diferencia entre ambos. Sin redondeos (decimales de origen; el redondeo es solo de presentación: costos con 4 decimales, totales con 2). Pantalla `/ValoracionStock` (filtros por código/nombre, categoría, marca, estado del cálculo y «incluir sin stock»; paginada) con la leyenda **«Uso gerencial. No es valuación fiscal ni contable; no modifica costos ni márgenes»**, exportación a Excel (`/ValoracionStock/Exportar`, sin paginar) y enlace en el menú Logística. Permiso propio de solo lectura **`valoracionstock.view`** (módulo nuevo «Valoración de stock»): Administrador (por el grant «todo salvo la lista») y Contador lo reciben; Gerente y el resto no. No hay acciones mutantes (el inventario de acciones mutantes no cambia).

### Fórmula y supuestos

Movimientos del producto en orden cronológico (fecha de alta, luego Id), con `d = StockNuevo − StockAnterior`:

1. **Promedio móvil exacto** (`PromedioMovilExacto`): entrada con costo `c` (compra recibida, o entrada ajena a compras con costo informado): `A = (Q·A + d·c) / (Q + d)`, `Q += d`; entrada sin costo informado: entra al promedio vigente sin modificarlo; salida (venta, devolución a proveedor, ajuste negativo): sale al promedio vigente (`A` no cambia). Es exacta solo si el Kardex concilia con el stock actual (`Q = stock`), ninguna unidad quedó sin costo, no hubo salidas mayores al saldo del Kardex y existe al menos una compra recibida.
2. **Composición por compras** (`ComposicionPorCompras`, variante conservadora) cuando no se cumple lo anterior (stock editado fuera del Kardex, unidades sin costo antes de la primera compra, saldos incoherentes): promedio de las **compras recibidas más recientes que componen el stock actual** (supuesto: el stock presente proviene de las últimas compras): `Σ(cantidad·costo)/Σ(cantidad)` hasta cubrir el stock. Si las compras registradas no cubren todo el stock, el promedio vale solo para las unidades cubiertas, se informa la cobertura y **no se calcula valor total**: solo un «valor parcial».
3. **Sin costo calculable**: producto con stock y sin ninguna compra recibida (`SinCompras`); **sin stock** (`SinStock`: se informa el último costo de compra si existe). No se inventan ceros: las celdas sin valor quedan vacías.
4. Las entradas con costo que no son compras (stock inicial valuado a costo de ficha, ajustes con costo, devoluciones de venta) **se incluyen** en el promedio móvil y el producto queda marcado («incluye costos que no provienen de una compra»).

Limitaciones: el cálculo se rehace en cada consulta desde el Kardex (no hay costo promedio persistido: es lo que se pidió); depende de que el Kardex sea fiel (las ediciones directas de stock lo degradan a la variante 2); las compras en moneda extranjera, fletes, descuentos o impuestos no recuperables no forman parte del `PrecioUnitario`; no considera lotes ni vencimientos; la sugerencia de precios y los márgenes **siguen usando** el costo de ficha como hasta ahora (el último costo de compra se muestra como dato de lectura).

### Decisiones contables pendientes con el contador

1. **Valuación fiscal** del stock (inventario a efectos impositivos y balance): este cálculo no la reemplaza ni la alimenta.
2. **Método único oficial** de costeo (último costo, promedio ponderado, PEPS/FIFO) para valuación contable y para costo de ventas; hoy conviven el costo de ficha manual y el último costo.
3. **Ajuste por inflación** del costo de las existencias.
4. **Efecto en márgenes históricos y precios**: si el costo de la venta o de las listas pasara a ser el promedio, cambiarían los márgenes ya registrados y los precios sugeridos de D-01; requiere decidir si se recalcula, desde qué fecha y con qué tratamiento de lo ya vendido.
5. Tratamiento de fletes, impuestos no recuperables, descuentos y moneda extranjera dentro del costo de compra.

## 13. Revisión independiente de Codex (2026-11-18) y validación de cierre del tramo D-01/D-02/costeo/backups

Dos trabajos de Codex en solo lectura (`adversarial-review`; hash del worktree idéntico antes y después). Hallazgos y disposición:

| # | Sev. | Hallazgo | Disposición |
|---|---|---|---|
| 1 | alta | La excepción de referencias sobrevivía a un cambio de monto si en ese momento las referencias alcanzaban; al faltar de nuevo, volvía a valer para un monto nunca autorizado (`VentaService.AplicarAutorizacionDeReferenciasEnEdicion`) | **Confirmado y corregido**: se retira la razón de lo autorizado al cambiar el monto. Regresión `Editar_AutorizarCompletarReferenciasCambiarMontoYRetirarlas_...`; con la corrección anulada la prueba falla (verificado por mutación) |
| 2 | alta | La retención por capacidad podía borrar el único tar de archivos si había dos fulls SQL sueltos posteriores (`lib-capacity.sh`) | **Confirmado y corregido**: la última cadena con conjunto de archivos acreditado nunca se borra |
| 3 | alta | (re-revisión) un tar interrumpido (sin checksum, manifiesto o `.caseal`) podía desplazar esa protección | **Confirmado y corregido**: solo acredita tar + `.sha256` + manifiesto (+ `.caseal` con `BACKUP_CA_KEY=seal`); línea `P\|idx` en `bk_chains` |

Pruebas tras las correcciones: `test-retention-capacity.sh` 89/89 (sintético) y 105/105 con SQL Server real (`--db`); `test-hardening.sh` 81/81; `test-host-portability.sh` 9/9; con la corrección 2 anulada fallan exactamente las 2 pruebas nuevas. Suite .NET completa previa a las correcciones: 8657 correctas, 0 fallos, 238 omitidas; tras las correcciones, 291/291 en referencias, autorización y conversión. Concurrencia en SQL Server real (contenedor descartable): `ListaPrecioPrecioManualSqlServerTests` + `CuentaPorPagarSqlServerTests` 14/14.

La corrección del hallazgo 3 **no** tuvo una tercera revisión de Codex (presupuesto de dos revisiones finales por tanda).


## 14. Excepción de referencias: alcance exacto

Regla del propietario: **la excepción individual solo autoriza omitir REFERENCIAS. No exceptúa mora ni otros bloqueos; se mantiene la distinción entre mora bloqueante (NoViable) y no bloqueante (razón de autorización).**

Implementación (sin migración: el estado por razón vive dentro de `Venta.RazonesAutorizacionJson`):
- Cada razón del JSON lleva `Autorizada`, `AutorizadaPor`, `FechaAutorizacion` y `MotivoAutorizacion`. Las razones exceptuadas en el alta (excepción documental/de cupo, que aplica quien tiene `ventas.authorize`) se sellan como autorizadas en ese acto; las demás nacen sin autorizar.
- `IVentaService.AutorizarVentaAsync(id, usuario, motivo, razonesAutorizadas)` autoriza solo las razones indicadas. La venta pasa a `Autorizada` únicamente cuando TODAS sus razones están autorizadas; si no, sigue `PendienteAutorizacion` y la respuesta/pantalla nombra las que faltan. Sin lista (sobrecarga anterior) se autorizan todas las razones pendientes, como antes, salvo que entre ellas esté la de referencias: en ese caso la lista es obligatoria e incluye expresamente `ReferenciasInsuficientes` (si no, se rechaza). Lista vacía, repetida, con tipos desconocidos o con una razón que la venta no tiene o ya autorizó: se rechaza sin cambiar nada.
- Autorizar referencias sigue exigiendo un segundo usuario con `ventas.authorize`, distinto de quien creó o modificó la venta, con motivo, y registra `Ventas/AutorizarExcepcionReferencias`; cada otra razón autorizada registra `Ventas/AutorizarRazonVenta` (una entrada por razón, en el mismo `SaveChanges`).
- Confirmar, ConfigurarVenta, facturar y conversión de cotización siguen exigiendo `Autorizada` (todas las razones) y el mensaje de Confirmar dice «Falta autorizar: …». La invalidación por cambio de monto retira solo la razón de referencias (con su autorización); las demás conservan la suya, salvo que el flujo existente ya reiniciara toda la autorización (referencias que siguen faltando con otro monto).
- UI: la pantalla de autorización y el modal de Venta/Details muestran una casilla por razón pendiente (parcial `_VentaAutorizarRazones`), la de referencias rotulada «Excepción por referencias insuficientes: solo exime las referencias.»; las razones ya autorizadas se listan con quién las autorizó. Una venta sin razón de referencias conserva el flujo de siempre (sin casillas).

| Razones de la venta | Acto | Resultado |
|---|---|---|
| Mora no bloqueante + referencias | solo referencias | Pendiente (falta mora); Confirmar bloqueado |
| Mora no bloqueante + referencias | solo mora | Pendiente (faltan referencias) |
| Mora no bloqueante + referencias | ambas (un acto, o dos actos con **otro** usuario para referencias) | Autorizada |
| Cupo/documentación exceptuado en el alta + referencias | referencias | Autorizada |
| BCRA «requiere autorización» + referencias | solo referencias | Pendiente; luego el resto -> Autorizada |
| Mora bloqueante (+ referencias, con o sin excepción) | cualquiera | NoViable: no se crea ni se avanza |
| Sin razón de referencias (p. ej. solo mora) | sin lista (o con su tipo) | Autorizada, igual que antes |
| Con referencias | sin lista / lista manipulada | Se rechaza; no cambia nada |

| AC | Estado | Evidencia |
|---|---|---|
| (a) mora no bloqueante + referencias: solo referencias / solo mora / ambas | VERIFICADO | `VentaReferenciasCreditoServiceTests.Razones_MoraNoBloqueanteMasReferencias_*`; HTTP `ReferenciasCreditoHttpTests.Autorizar_SoloReferencias_*`, `Autorizar_OmitiendoLasReferencias_*`, `Autorizar_MoraYReferenciasExpresamente_*` |
| (b) cupo y BCRA con referencias faltantes; documentación | VERIFICADO cupo y BCRA / NO VERIFICADO documentación | `Razones_CupoExceptuadoEnElAlta*`, `Razones_BcraConRequiereAutorizacion*`. La excepción documental usa el mismo sellado por razón que la de cupo, pero no hay un test propio |
| (c) mora bloqueante + excepción -> NoViable | VERIFICADO | `Razones_MoraBloqueanteSigueSiendoNoViable_*` y los previos `ConMoraBloqueante_*` |
| (d) ventas sin razón de referencias: sin regresión | VERIFICADO | `Razones_SinReferenciasFaltantes_*`, `VentaServiceAutorizacionTests` (incl. JSON no estructurado intacto), HTTP `Autorizar_UnaVentaNoCreditoOSinRazonDeReferencias_*` |
| (e) auditoría por razón | VERIFICADO | `Razones_..._AmbasEnUnSoloActo_AutorizaLaVentaYAuditaCadaRazon` |
| (f) segundo usuario / autoautorización para referencias | VERIFICADO | `Razones_ElCreadorNoPuedeAutorizarLasReferencias_*`, tests previos del creador |
| POST directo con lista manipulada (omitir, repetir, tipos inválidos o negativos, razón ajena, sin lista) | VERIFICADO | HTTP `Autorizar_ListaManipulada_NoAutorizaNada` (4 casos), `Autorizar_SinListaDeRazones_*`; servicio `Razones_ListaManipulada_*` |
| «Incluirla sin permiso»/ids ajenos | VERIFICADO (permiso por atributo, venta inexistente) | tests previos `Autorizar_UnUsuarioSinPermiso_*`, `Autorizar_UnaVentaInexistente_*`; no hay ids ajenos que mezclar: el id de la ruta es la única venta |
| Cambio de monto retira solo la autorización de referencias | VERIFICADO | `Razones_ElCambioDeMontoConReferenciasYaSuficientes_*`, `Razones_SoloReferenciasAutorizadas_YCambioDeMonto*` |
| UI: casillas por razón, accesibles | VERIFICADO (HTML, contrato y E2E; sin lector de pantalla ni capturas nuevas) | `ReferenciasCreditoUiContractTests`, HTTP `AutorizarPagina_*`, `VentaDetails_ElModalDeAutorizar*`, E2E `cliente-referencias-credito.spec.js` 7/7 (local, LocalDB descartable ya eliminada, 1366x768) |
| ConfigurarVenta con venta pendiente parcial | NO VERIFICADO por test propio | Usa el mismo estado `Autorizada` (solo se alcanza con todas las razones); sin test dedicado |
| Concurrencia SQL Server de dos autorizaciones parciales | NO VERIFICADO | Acotada por el estado pendiente y el `RowVersion`; sin harness SQL Server |

Decisiones y ambigüedades: (1) autorizar una razón modifica el registro de la venta (`UpdatedBy`), por lo que quien ya autorizó la mora en un acto previo no puede ser, después, el segundo usuario de las referencias; en un único acto con ambas casillas sí puede (el chequeo ocurre antes de guardar). (2) Editar una venta con autorizaciones parciales y referencias que siguen faltando reinicia todas las autorizaciones (criterio conservador previo). (3) El JSON de razones que no es la lista estructurada (ventas anteriores a la trazabilidad) se autoriza como siempre y no se reescribe. (4) Una venta ya `Autorizada` antes de este cambio con la razón de referencias sin el sello por razón ya no cubre las referencias (se exige autorizar de nuevo); no hay datos reales así (base nueva).

### 14b. Revisión independiente de Codex sobre `07b391ea` (H1-H3, todos confirmados contra el código)

| # | Hallazgo | Verificación | Corrección | Regresión (falla sin la corrección) |
|---|---|---|---|---|
| H1 | Autorizada solo por referencias, se configura el crédito y después aparece una mora no bloqueante: `ValidarSinCupoAsync` descartaba esa razón y Confirmar solo comprobaba referencias | Confirmado: la revalidación de confirmación no producía razones no bloqueantes | `ValidarSinCupoAsync` conserva las razones no bloqueantes vigentes (mora, punitorio, revisión del cliente); `ExigirTodasLasRazonesAutorizadas` exige que CADA razón vigente tenga autorización expresa en el JSON (por tipo), en Confirmar, `ConfirmarVentaCreditoAsync` y Configurar (`AsegurarReferenciasAutorizadasAsync`); si falta, se rechaza con «Falta autorizar: …». Mora bloqueante sigue NoViable | `H1_AutorizadaSoloPorReferencias_SiLuegoAparecePorMoraNoBloqueante_ElGateDeConfirmarNoProcede`, `H1_MoraBloqueanteSigueSiendoNoViableEnLaRevalidacion` |
| H2 | Una autorización parcial actualiza `UpdatedBy` y libera al editor de condiciones | Confirmado: el chequeo usaba `UpdatedBy` | Se registra el último editor de condiciones en `Venta.UsuarioSolicita` (campo existente, antes sin uso; sin migración) en Create, Edit (`UpdateAsync`) y Configurar el crédito. El segundo usuario de las referencias debe diferir de `CreatedBy` y de `UsuarioSolicita`; con editor registrado `UpdatedBy` no cuenta (ver H5 para ventas sin editor registrado) | `H2_AEditaCondiciones_BAutorizaLaMora_AUnaVezMas_NoPuedeAutorizarLasReferencias_PeroUnTerceroSi`, `H2_ElEditorQueTambienAutorizoLaMora_*`; el caso «un solo acto con ambas casillas» y el flujo legado (`Autorizar_QuienModificoLaVenta_*`) siguen pasando |
| H3 | La excepción documental/de cupo dejaba Autorizada una venta con otra razón vigente sin autorizar (punitorio) | Confirmado: `RequiereAutorizacion` se calculaba solo por referencias | `RequiereAutorizacion = existe alguna razón sin autorizar` tras sellar las exceptuadas; guarda interna que impide `Autorizada` con razones sin autorizar; además la mora no bloqueante ya no se pierde cuando el cliente está NoApto por otra causa que se exceptúa. En Edit, una venta Autorizada que hoy tiene una razón vigente sin autorizar vuelve a pendiente (`InvalidarAutorizacionRazonNueva`) | `H3_ExcepcionDocumentalConMoraNoBloqueantePendiente_NoDejaLaVentaAutorizada`, `H3_ExcepcionDocumentalSinOtrasRazones_SigueAutorizandoComoAntes`, `Invariante_Edit_*` |

Invariante «Autorizada ⇒ ninguna razón vigente sin autorizar»: se asigna `Autorizada` en dos lugares (excepción documental en el alta, protegida por la guarda, y `AutorizarVentaAsync`, que solo lo hace si no queda razón sin autorizar) y se exige en Confirmar/Configurar y Edit con las mismas funciones (`VentaRazonesAutorizacionJson.VigentesSinAutorizar`). Los caminos de cotizador/conversión usan `AplicarResultadoValidacionAsync` (mismo código que Create).

| AC | Estado |
|---|---|
| H1-H3 con regresión que falla sin la corrección (comprobado anulando cada corrección) | VERIFICADO |
| Invariante en Create, Edit, AutorizarVentaAsync, Confirmar/Configurar (gate) | VERIFICADO por pruebas de servicio |
| Invariante en conversión de cotización y simulador | NO VERIFICADO con prueba propia (comparte `AplicarResultadoValidacionAsync` con Create) |
| H3 con punitorio aplicado pendiente (caso literal de Codex) | NO VERIFICADO con datos de punitorio; se probó con mora no bloqueante, que recorre el mismo código |
| `ConfirmarVentaCreditoAsync`/ConfigurarVenta ejecutados completos con una mora nueva | NO VERIFICADO de punta a punta (se prueba el gate y la revalidación) |

Ambigüedades: (1) si aparece una razón nueva en una venta que nunca requirió autorización (`NoRequiere`), Confirmar la rechaza con «Falta autorizar…» pero nadie puede autorizarla hasta que se guarde de nuevo la venta (Edit la pasa a pendiente); no se cambia el estado dentro de la transacción de Confirmar. (2) `UsuarioSolicita` ahora se muestra como «Solicitante» en la pantalla de autorizar: es el último editor de condiciones. (3) Las ediciones que no pasan por Create/Edit/Configurar no registran editor; para esas se mantiene la comprobación de `UpdatedBy`.

### 14c. Segunda revisión de Codex (H4-H5, confirmados contra el código)

| # | Hallazgo | Corrección | Regresión (falla sin la corrección, comprobado) |
|---|---|---|---|
| H4 | `ValidarSinCupoAsync` omitía las razones no bloqueantes cuando coexistía un bloqueo (documentación vencida + mora nueva): el gate de Confirmar encontraba las referencias cubiertas y no veía la mora | Las razones no bloqueantes vigentes (mora, punitorio, revisión del cliente) se conservan siempre, aunque haya bloqueos; `ExigirTodasLasRazonesAutorizadas` (antes de tocar stock, cuotas o estado en `ConfirmarVentaCreditoAsync`) rechaza con «Falta autorizar: Mora activa» | `H4_DocumentacionVencidaYMoraNuevaTrasAutorizarSoloReferencias_ConfirmarNoProcede` |
| H5 | El fallback «UpdatedBy se ignora si es un autorizador» dejaba que, en una venta sin editor registrado, el último editor autorizara sus propias referencias tras autorizar una mora | Se eliminó el fallback. Con `UsuarioSolicita` registrado se compara con `CreatedBy` y `UsuarioSolicita`; sin editor registrado, con `CreatedBy` y `UpdatedBy` de forma estricta | `H5_VentaLegadaSinEditorRegistrado_QuienEditoYAutorizoLaMora_NoPuedeAutorizarLasReferencias`; siguen pasando A->B edita->C autoriza mora->B rechazado y D autoriza (`H2_*`) y el acto único con ambas casillas (`H5_..._UnSoloActo_...`) |

**Decisión sobre H4(b) (rechazar NoViable/PendienteRequisitos dentro de `ConfirmarVentaCreditoAsync`)**: no se aplicó. En `main` ese servicio nunca revalidaba el crédito (solo `ValidarAutorizacion`); el rechazo de NoViable/PendienteRequisitos vive en el controller (`ConfirmarVentaCreditoPersonalAsync`, con la excepción documental resuelta por `ResolverExcepcionDocumentalAlConfirmarAsync`), y D-02 solo agregó el gate de referencias y de razones en el servicio. El escenario de Codex queda cerrado con (a) más el gate de razones, que corre antes de stock, cuotas y estado; agregar el rechazo de bloqueos en el servicio cambiaría el comportamiento de quien lo invoque sin pasar por el controller (y de ventas con excepción documental registrada), por lo que queda como decisión del propietario.

| AC | Estado |
|---|---|
| H4(a) razón no bloqueante vigente conservada con un bloqueo coexistente; gate rechaza | VERIFICADO (servicio) |
| H4 llamando a `ConfirmarVentaCreditoAsync` completo | NO VERIFICADO: el servicio falla antes por las condiciones de pago/plan (`CondicionesPagoVentaException`), sin llegar al gate; se prueba la revalidación y el gate por separado |
| H4(b) rechazo de bloqueos en el servicio | NO APLICADO (decisión documentada arriba) |
| H5 sin editor registrado: estricto; con editor registrado: CreatedBy + UsuarioSolicita | VERIFICADO |

Ambigüedad nueva: en una venta sin editor registrado, quien autorizó una razón en un acto previo no puede autorizar después las referencias (aceptado por el coordinador; en un solo acto con ambas casillas sí puede si no es creador ni editor).

## 15. Cierre de la tanda D-01 / D-02 / costeo / backups / CI (2026-11-18)

Decisiones del propietario incorporadas: la excepción individual solo exime referencias (no mora ni otros bloqueos; se mantiene la distinción mora bloqueante / no bloqueante), CI con el E2E de referencias y la concurrencia SQL de D-01, y D-03 en una tanda aparte (Tesorería administrativa separada de la Caja de ventas, fuentes de fondos por catálogo configurable, anticipos aplicables a una o varias facturas del mismo proveedor, un cheque físico no reutilizable en varios pagos, cheques recibidos de clientes solo informativos, sin pagos con cheques propios hasta resolver ese punto, costeo gerencial separado de la contabilidad fiscal). **D-03 no forma parte de esta PR.**

### Evidencia final (sobre el SHA de la PR)

| Verificación | Resultado |
|---|---|
| Suite .NET completa | 8698 correctas, 0 fallos, 238 omitidas |
| Concurrencia SQL Server real (D-01, D-03, OC, precios) | 18/18, 0 omitidas |
| Instalación limpia sobre SQL Server descartable | 143 migraciones, 1 administrador, 0 datos de negocio |
| Backups: retención por capacidad | 96/96 sintéticas y 112/112 con SQL Server real; `test-hardening` 81/81; portabilidad 9/9 |
| E2E de referencias y crédito (local, base descartable) | 7/7 (en CI: se valida en esta PR) |
| Valoración de stock en 1366, 1024 y 390 px | validada con capturas reales; 3 defectos corregidos |
| Revisión independiente de Codex | 7 pasadas; 9 hallazgos altos reales, todos corregidos con regresión que falla sin la corrección; última pasada: approve |

### Criterios cerrados

- D-02 referencias exigidas para crédito personal con cantidad mínima configurable; excepción individual por segundo usuario con `ventas.authorize`, motivo y auditoría; sin autoautorización ni excepciones globales; no exime mora ni demás bloqueos; autorización expresa por razón (secciones 14-14c).
- D-01 lista general predeterminada única; circuito de lotes con un mismo usuario con auditoría; concurrencia SQL Server de la lista predeterminada y de los precios manuales.
- Costeo gerencial de solo lectura (promedio ponderado e indicador de último costo), sin modificar costos, márgenes ni valuaciones persistidas.
- Backups: retención por capacidad con cadenas recuperables, copia verificada a disco extraíble futuro, instalación limpia sin falla intermitente por colisión de subred.

### Criterios que siguen pendientes (no marcar como Finalizados)

- **Hardware real (no hay acceso al servidor):** cantidad de discos, prueba en el disco definitivo y en un extraíble físico, arranque sin sesión, convivencia con la VM del ERP antiguo, Tailscale y firewall con cliente real.
- **E2E en CI:** el paso nuevo corre por primera vez en esta PR; hasta ver verde el job `test` no se considera verificado en CI.
- **Sin prueba de punta a punta:** `ConfirmarVentaCreditoAsync` completo con razón nueva (se probó la revalidación y el gate por separado); conversión de cotización y simulador con el invariante (comparten código con Create); concurrencia SQL Server de dos autorizaciones parciales; lector de pantalla; botón «Exportar a Excel» de Valoración.
- **Decisión del propietario:** rechazar `NoViable`/`PendienteRequisitos` dentro del servicio de confirmación (hoy vive en el controller, como en `main`); en una venta sin editor registrado, quien autorizó una razón no puede autorizar después las referencias en otro acto.
- **Contador:** valuación fiscal, método oficial de costeo, ajuste por inflación y efecto en márgenes históricos.
- **D-03 (tanda aparte):** todo lo indicado arriba; el pago con cheques propios queda sin implementar hasta resolverlo expresamente.
