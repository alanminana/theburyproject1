# Matriz de aceptación del ERP — cierre del Hito 9

Fecha: 2026-10-30 · `main` `36d57726` · Base real `TheBuryProjectDb` en `20261029012048_H4E2NotaCreditoCanje`.

Estados: **Aprobado** (completo y probado) · **Aprobado con limitaciones** (funciona dentro de un alcance explícito y seguro) · **Bloqueado** (defecto o decisión indispensable pendiente) · **Diferido** (fuera del alcance inicial por decisión del propietario).

Evidencia = lo realmente ejecutado: tests con CI 14/14 sobre el HEAD de cada PR, recorridos en clones de la base real con contraste SQL, barridos dinámicos con roles. Nada se probó sobre datos reales.

| Módulo | Estado | Evidencia principal | Limitaciones / pendiente |
|---|---|---|---|
| Ventas y Cotizaciones | Aprobado con limitaciones | Recorrido cotización → venta → confirmar → facturar → caja con SQL; #165, #182, #203, #205 | Interés de financiación y recargo: tratamiento fiscal sin validar (D1). «Siguiente: Cliente» del cotizador con productos cargados ya no abre el wizard vacío: guarda la cotización, la convierte sin confirmar y abre Venta/Edit con los importes intactos (Hito 11, validado en clon con SQL); sin cliente avisa y no navega. Factura de venta a crédito personal: recorrido validado en clon (contrato con PDF → facturar cierra neto + IVA = total); el interés queda fuera de la factura y el tratamiento fiscal espera decisión (`docs/factura-credito-personal.md`). |
| Caja | Aprobado con limitaciones | QA Caja/cierre/arqueo, concurrencia en SQL Server, #197, #211 | Acreditar y rechazar cheques recibidos en la interfaz (#211). Un solo medio de pago por venta. |
| Crédito | Aprobado con limitaciones | Crédito personal en la pantalla única, #161, #166 | El job de vencimiento de borradores no corrió contra la base real: probado en un clon, cancela **solo la venta 1003** (la 1006 no es elegible: sin crédito asociado); plazo de 7 días provisorio. Ver «Hito 11 — Bloque 2». El interés del crédito queda fuera de la factura de venta (D1). |
| Mora y Acuerdos | Aprobado con limitaciones | Smoke en clon (crear, activar, cobrar, cumplir, incumplir, auditoría), acuerdos con pagos no efectivos y punitorio > 0, #169 | `DiasParaCumplirPromesa`: lectura conservadora (días de espera tras la fecha prometida); hoy NULL. Una sola instancia de la app. |
| Cheques | **Aprobado con limitaciones** (migración `H10ChequesRecibidos` aplicada a la base real el 2026-10-30, #211) | `docs/cheques-circuito-propuesta.md` §6, `docs/cheques-migracion-autorizacion.md`; D1-A y D2-A autorizadas, D3-D6 aplicadas; tests de servicio, autorización/IDOR/antiforgery y concurrencia en SQL Server (RCSI 0 y 1) con 3 revisiones independientes (Codex); QA visual 1280 y 390 sobre clon; migración probada Up/Down/Up en clon y aplicada con backup verificado, CHECKDB limpio y conteos idénticos | D5 resuelta: las cuotas de crédito **no aceptan cheques** (rechazo en el servidor en todos los caminos de cobro; 0 cobros de cuota con cheque en la base real). Cancelar una venta con cheque de caja ya cerrada ya está resuelto (B13: contrapartida auditada en la caja vigente, `docs/cheques-circuito-propuesta.md` §6.4; QA en SQL Server RCSI 0 y 1); con otros medios de pago y caja cerrada sigue rechazándose. Sin uso real todavía: probar con un cheque de prueba antes de operar. |
| Compras | Aprobado con limitaciones | QA OC completa, recepción parcial, concurrencia, #171, #202 | Costo del producto manual (no se actualiza al recibir). Cancelar una OC parcial conserva el stock (sin devolución al proveedor). Sin cuentas ni pagos a proveedor. |
| Stock | Aprobado | Stock = Σ movimientos para los 16 productos, Kardex sin cortes, recepción idempotente | — |
| Devoluciones | Aprobado con limitaciones | Flujo del modal real de punta a punta, #172, #176, #206 | Venta con crédito vigente: bloqueada. Devolución de venta pagada con cheque: D4 implementada en `hito10/cheques` (cheque Pendiente o Rechazado bloquea el reembolso en efectivo). |
| Notas de crédito | Aprobado con limitaciones | Canje como medio de pago, cancelación con restitución e idempotencia, #178, #179 | Canje solo total (parcial exige pagos múltiples por venta). |
| Clientes | Aprobado con limitaciones | Recorrido de pantallas, #162, #202; Etapa C: edición con valores de crédito históricos | Consulta BCRA con tope de 6 s (provisorio aceptado). La solapa Referencias no existe en el modelo: se oculta en la interfaz (sin entidad ni migración; decisión del propietario si se construye). |
| Usuarios y Roles | Aprobado | Inventario COMPLETO, último SuperAdmin protegido, matriz de permisos con 518 acciones | Permisos del catálogo sin uso (~50) sin limpiar. |
| Documentos | Aprobado | Plantillas, versiones, previsualización PDF, contratos | — |
| Reportes | Aprobado con limitaciones | Ventas, Márgenes, Morosidad y Reporte de IVA contrastados con SQL, #205 | Ventas brutas, devoluciones y netas (por fecha de devolución) y ganancia sobre neto sin IVA (H10, contrastado con SQL). Pendiente: costo con IVA, envío/armados y recargo en la ganancia; el Reporte de IVA aún no netea devoluciones. Ver `docs/iva-decisiones-contador.md`. |
| Dashboard | Aprobado con limitaciones | KPIs vs SQL, #177, mobile 390 | «Ventas del mes» neta de devoluciones (H10); ticket promedio sigue bruto. |
| Notificaciones | Aprobado con limitaciones | Campana conectada, #180 | Llegada en vivo por SignalR sin validar. |
| Tickets | Aprobado con limitaciones | Alta, estados, adjuntos, checklist | Acciones masivas por UI sin recorrer con clics. |
| Configuración | Aprobado | Empresa, prefijos, mora, crédito, medios de pago persisten | — |
| Seguridad | Aprobado | Barrido dinámico de 506 acciones con 10 identidades, antiforgery, IDOR, XSS, auditoría ampliada, #162, #184–#186, #202 | Webhook de Mercado Libre anónimo (firma diferida). Hallazgos bajos documentados. |
| Infraestructura y recuperación | Aprobado con limitaciones | Pila Docker desde cero con 133 migraciones, backup y restauración con conteos y checksum idénticos, recuperación desde la copia externa en 44 s, #181, #200, #201 | **Copia fuera del servidor sin configurar** (falta el proveedor y sus credenciales). IP de la VPN y firewall del host sin definir. Una sola instancia de la app. Licencia de AutoMapper diferida. |

## Veredicto

**Listo para uso local con limitaciones.** El circuito principal (cotizar, vender, cobrar, facturar, devolver, comprar, controlar stock, crédito y mora) funciona, está integrado en `main` y fue probado de punta a punta.

Condiciones: probar el circuito de cheques con un cheque de prueba antes de operar (D1-A y D2-A implementadas, #211); verificar el job de borradores en la primera ejecución real; configurar la copia externa antes de operar con datos de producción.

## Decisiones pendientes del propietario

1. **IVA D1** — tratamiento del interés de financiación y del recargo del medio de pago (vigente: a la alícuota de los ítems, provisional; validar con el contador, ver `docs/iva-decisiones-contador.md`).
2. ~~**Cheques D5**~~ — resuelta: prohibido cobrar cuotas con cheque.
3. **Factura de crédito personal C1–C4** (`docs/factura-credito-personal.md`).
4. **Efectivo con descuento del plan en venta directa** (#210): se desactiva en la configuración del plan si no se quiere.

## Clasificación de limitaciones de Compras y Reportes (Hito 11)

| Limitación | Clase | Detalle |
|---|---|---|
| Compras: costo del producto manual (no se actualiza al recibir) | Regla de negocio (propietario) | Hay que elegir el método de costeo (manual, último costo, promedio). No es fiscal ni defecto: cambia el margen informado. |
| Compras: cancelar una OC parcial conserva el stock | Comportamiento correcto | La mercadería recibida existe físicamente; devolverla al proveedor es un circuito aparte (RMA de unidades). |
| Compras: sin cuentas ni pagos a proveedor | Funcionalidad nueva, fuera de alcance | Sin cambio hasta que el propietario la declare indispensable. |
| Reportes: Reporte de IVA no netea devoluciones | Defecto funcional parcial; el período es fiscal | El informe avisa en su nota. Restar la devolución requiere definir en qué período se computa la nota de crédito (contador, `docs/iva-decisiones-contador.md` #4). |
| Reportes: costo con IVA, envío/armados y recargo en la ganancia | Contador | `docs/iva-decisiones-contador.md` #1, #5 y #6. |
| Dashboard: ticket promedio bruto mientras «Ventas del mes» es neta | Inconsistencia técnica menor | Definición de la métrica; candidata a unificar sin criterio fiscal. |

## Hito 11 — Bloque 2: evidencia (clon de la base real, `main` `6d9d4065` + PR del bloque)

Todo se probó en `TheBuryProjectDb_qa_h11` (copia con `COPY_ONLY`); la base real no se modificó.

| Área | Resultado | Evidencia |
|---|---|---|
| Crédito: job de borradores | **Verificado en clon.** Elegibles hoy: solo la venta **1003** (PendienteFinanciacion, crédito 3 PendienteConfiguracion, 38 días). La **1006** está en PendienteFinanciacion pero **sin crédito**: el job la ignora y no reserva cupo. | El servicio de fondo corrió solo a los 2 min de arrancar: venta 1003 → Cancelada y desvinculada; crédito 3: estado 6 → 5 (Cancelado), saldo $256.060 → 0; cupo comprometido del cliente 1: $1.377.451,32 → $1.121.391,32 (−$256.060 exactos); sin movimientos de caja ni de stock. |
| Ventas: edición | Guardado desde Revisión persiste y conserva los importes. «Guardar sin confirmar» solo aparece en Revisión (antes se veía en todos los pasos y fuera de Revisión solo avanzaba). E2E «Cliente como paso inicial» corregido: el aviso modal «Actualizar datos del cliente» (depende de la fecha de datos del cliente) tapaba los botones; `irAlPaso` lo cierra. | SQL: total $12.114,40 estable, `UpdatedAt` y observación guardados. E2E `venta-cotizar-step` 35/35 a 1366 y 390. |
| Cambios de Precios | Camino operativo = modal del Catálogo (`AplicarCambioPrecioDirecto` + historial + revertir). El módulo por lotes (Simular/Aprobar/Aplicar) **exige una lista de precios y la base no tiene ninguna**; su servicio está cubierto por tests (123/123). Permisos y antiforgery cubiertos por tests y por `[AutoValidateAntiforgeryToken]` de clase. | Clon: +10 % sobre $12.752 → $14.027,20 con evento e historial; **dos reversiones simultáneas: una gana, la otra se rechaza sin efectos parciales**, precio vuelve a $12.752. Test SQL Server nuevo `PrecioRevertirEventoConcurrenciaSqlServerTests` (agregado al CI). |
| Clientes: Cónyuge y Laboral | Guardan y se releen correctamente. **Defecto corregido:** con la solapa Crediticio visible no se podía guardar (jQuery rechazaba `10,00`/`5000,00`, el formato con que el servidor precarga) y la máscara de dinero convertía `250.000` en `250.00` (monto mil veces menor, sin aviso). | SQL: `250.000,00` → `250000.00`; cónyuge `150000,50` → `150000.50`. Contrato único cliente/servidor (el último separador es el decimal, miles repetidos), E2E `input-rules-monto` y tests de `DecimalParsingHelper`. |
| Clientes: Referencias | **No existe en el modelo**: la pestaña es un marcador («SIN CAMPOS EN EL MODELO ACTUAL»). Es funcionalidad nueva, no un defecto; decisión del propietario (ver abajo). | `Views/Cliente/_ClienteFormCampos.cshtml`. |
| Tickets: acciones masivas | Cambiar estado (2 tickets → En Curso) y Eliminar (con confirmación, borrado lógico) funcionan desde la interfaz. | SQL: `Estado=1`, `IsDeleted=1`. Tests de Tickets 85/85 (permisos, adjuntos, bulk). |
| SignalR | **Verificado en vivo**: con dos pestañas del mismo usuario, marcar todo como leído en una actualiza el contador de la otra en ~270 ms, sin recargar. | Playwright, hub `/hubs/notificaciones`. |
| Seguridad (HTTP 500) | Resuelto; verificado con la app real. | `/Seguridad` y pestañas 200; página enorme/negativa 200; rango invertido 302. |

**Revisión independiente (Codex, solo lectura):** 2 revisiones adversariales sobre montos/validación/concurrencia. Hallazgos corregidos: (high) `250,000.00` se guardaba como 250 por el normalizador del servidor, que asumía el orden es-AR; (medium) `1.234.567` bloqueado por validador y binder; (medium) `min/max/range` sin normalizar miles con coma. El test que «caracterizaba» `1,234.56 = 1,23456` pasó a exigir `1234,56`.

### Ticket promedio vs. «Ventas del mes» (propuesta, sin implementar)

Hoy «Ventas del mes» es neta de devoluciones (H10) pero el ticket promedio (Dashboard y Reporte) es bruto: `Σ Total ÷ cantidad`. Un operador espera que `Ventas ÷ cantidad = ticket`. **Criterio propuesto:** ticket promedio = ventas netas del período ÷ cantidad de ventas del período (mismo numerador que el KPI de ventas), en Dashboard y Reportes, mostrando también el bruto en el Reporte. No es un criterio fiscal. Variante a decidir: una venta devuelta al 100 % sigue contando en la cantidad (propuesto) o se excluye. Cambio localizado: `DashboardService.CalcularTicketPromedioAsync` y `ReporteService` (líneas del `TicketPromedio`).

### Decisiones nuevas para el propietario

1. **Job de borradores en la app real:** el servicio corre solo (2 min tras arrancar y una vez por día comercial). La venta 1003 y su crédito se cancelarán automáticamente la primera vez que corra una versión con este job sobre la base real. Autorizá esa primera corrida o pedí desactivarla (`DiasVencimientoBorradorCredito` en la configuración de crédito).
2. **Referencias del cliente:** construir la sección (entidad + migración, requiere tu autorización) u ocultar la pestaña en Edit.
3. **Ticket promedio:** confirmar el criterio propuesto.
4. **Venta 1006** (PendienteFinanciacion sin crédito): decidir si se cancela manualmente.
5. **Cliente 1** tiene `MontoMaximoPersonalizado` = 100.000.000, por encima del tope de 20.000.000 de la validación: no se puede guardar hasta corregir ese dato.
6. **Lista de precios:** si querés usar el módulo por lotes, hay que crear al menos una lista (no hay pantalla de listas).

## Hito 11 — Etapa C: validación integral (`main` `124e30ea` + PR de la etapa)

Entorno: clon `TheBuryProjectDb_qa_h11c` (copia `COPY_ONLY`; el job de borradores desactivado **solo en el clon**), app en :5199. La base real no se modificó (se consultó en solo lectura); no se aplicaron migraciones ni se ejecutaron jobs financieros reales.

### Regresión

| Capa | Resultado |
|---|---|
| CI sobre `main` `124e30ea` (merge de #221) | 14/14 checks, **más el workflow «Playwright Tests»: suite completa a 1366×768 con SQL Server en Docker y los sembradores**, verde (permisos por rol, antiforgery, IDOR y concurrencia incluidos en los tests .NET). Es la evidencia autorizada de la regresión E2E. |
| E2E en el clon, 1366×768 (204 specs) | 90 pasan en la primera corrida; con la caja del clon abierta y la fecha de actualización de datos de los clientes controlada, solo quedan fallos de specs que exigen usuarios y datos sembrados (`PUN-ML9` con usuario vendedor, `configuracion-punitorio`, `credito-pago-cuota-*`) y tres ya documentados como dependientes de datos (`venta-functional-audit`, `venta-excepcion-documental-reload`, `cotizacion-simulador` T9). |
| E2E en el clon, 1280×720 | 112 pasan; los 3 mismos fallos de datos. |
| E2E en el clon, 390×844 | 99 pasan; 16 fallan por specs de escritorio que usan `#btn-confirmar` (en móvil rige el CTA fijo inferior); el resto pasa. |
| Invariantes financieras contra SQL Server (base real, solo lectura) | Stock = último movimiento (0 violaciones) y sin cortes de cadena (0); cuota total = capital + interés + punitorio (0); pagado ≤ total (0); cierres de caja: esperado = inicial + ingresos − egresos (0) y diferencia = contado − esperado (0); capital pendiente del crédito = Σ capital de las cuotas (0). `SaldoPendiente` **es capital pendiente por diseño** (alimenta el cupo), no la suma de cuotas. |

Defectos corregidos en esta etapa (con tests discriminantes y revisión de Codex, 2 revisiones adversariales, 2 hallazgos altos y 1 medio corregidos):
- **Clientes con valores históricos de crédito** (p. ej. monto máximo 100.000.000 contra el tope de 20.000.000): ya se pueden editar los demás datos. Solo se exceptúa la validación individual del valor guardado e igual al enviado; un valor nuevo fuera de tope, uno no interpretable o la relación mínimo ≤ máximo se rechazan igual, y se vuelve a ejecutar la validación de objeto (fecha de nacimiento, CUIL). El valor real no se modificó.
- **Referencias**: solapa oculta también en edición; «Crediticio» pasa a ser la 5.ª sección.

### Pendientes resueltos o con definición

| # | Tema | Resultado |
|---|---|---|
| 1 | Job de borradores | No se ejecutó nada contra la base real ni se arrancó la app sobre ella. La venta 1003 espera tu autorización. |
| 2 | Referencias | Pestaña oculta (solo interfaz). |
| 3 | Ticket promedio | Ver definición abajo; **no se modificó la métrica**. |
| 4 | Venta 1006 | Ver abajo. No se tocó. |
| 5 | Cliente 1 | Resuelto con compatibilidad histórica (ver arriba); el valor real sigue en 100.000.000. |
| 6 | Listas de precios | Ver abajo. |

**Venta 1006.** Se creó el 2026-09-26 07:45:45 al convertir la cotización COT-20260926-0020 (estado Convertida), y quedó en PendienteFinanciacion sin crédito porque el crédito solo se crea cuando el operador entra al paso «Configurar plan» (la 1003, convertida 20 minutos antes, sí lo hizo: crédito 3 a los 9 segundos). Es un borrador legítimo de conversión que nadie continuó; no es corrupción. Efectos: no reserva cupo, no mueve stock ni caja. Brecha funcional: el job exige crédito en PendienteConfiguracion, por lo que un borrador sin crédito **no vence nunca**. Opciones: (a) dejarla (inocua), (b) cancelarla manualmente con tu autorización, (c) extender el job para vencer también PendienteFinanciacion sin crédito (cambio de regla de negocio: necesita tu decisión).

**Ticket promedio — definición final propuesta (validada con SQL Server sobre escenarios; la base real no tiene devoluciones aprobadas/completadas).** Ticket del período = (ventas brutas del período − devoluciones **de ventas del mismo período, por fecha de devolución dentro del período**) ÷ cantidad de ventas del período; cantidad 0 → 0.

| Caso (período) | Hoy (bruto) | KPI ÷ cant. | **Propuesta** |
|---|---|---|---|
| Venta devuelta al 100 % en el mismo mes (100 y 200, devuelta la de 100) | 150 | 100 | **100** |
| Devolución de una venta de un período anterior (300 en sep., devuelta en oct.; oct. = 100 y 200) | oct. 150 | oct. **0** (distorsiona) | oct. **150**; sep. sigue 300 |
| Cantidad 0 con una devolución de una venta anterior | 0 | −300 → 0 | **0** |
| Devolución parcial (50 sobre 300 en el mes) | 150 | 125 | **125** |

Es igual a «Ventas del mes ÷ cantidad» salvo cuando entran devoluciones de períodos anteriores, que sí restan de «Ventas del mes» (criterio H10, por fecha de devolución) y no deben deformar el ticket de las ventas del período. No cambia el pasado (a diferencia de netear por venta) y no es un criterio fiscal. Cambio localizado: `DashboardService.CalcularTicketPromedioAsync` y `ReporteService` (`TicketPromedio`), reutilizando `VentasNetas.ObtenerDevolucionesAsync(..., ventaIds)`. **Falta tu confirmación** antes de modificarlo.

**Listas de precios — qué falta exactamente para usar el módulo por lotes.**
- *Datos de configuración faltantes:* no existe ninguna lista (`ListasPrecios` vacía en la base real y en el clon). Hace falta al menos una lista activa y una predeterminada; sin ella `Simular` responde «Debe seleccionar al menos una lista» y `AplicarRapido` no encuentra la predeterminada.
- *Funcionalidad sin desarrollar:* nada en la aplicación crea ni edita listas (no hay controlador, vista ni semilla; `CreateListaAsync` y similares no tienen llamadores) y el módulo `CambiosPrecios` no está enlazado en ningún menú. Además `AplicarBatch` escribe `ProductoPrecioLista` pero no `Producto.PrecioVenta`: el precio resuelto pasaría a depender de dos fuentes.
- *Alternativas:* **A (recomendada)** mantener el cambio directo del Catálogo como circuito oficial (ya probado: aplicar, historial, revertir, concurrencia) y declarar el módulo por lotes fuera de alcance; **B** crear una lista «Contado» predeterminada por semilla (requiere tu aprobación de datos y define la regla de precedencia entre lista y precio base); **C** desarrollar un ABM de listas (funcionalidad nueva).

### Etapa D — qué falta para declarar finalizado funcionalmente el Hito 11

1. **Tus decisiones:** autorizar (o desactivar) la primera corrida del job de borradores sobre la base real (cancelaría 1003 y su crédito 3); confirmar la definición del ticket promedio; elegir la alternativa de listas de precios (A/B/C); decidir qué hacer con la venta 1006 y si se construye Referencias.
2. **Cheques:** el circuito está aprobado con limitaciones; falta probarlo con un cheque de prueba en el uso real antes de operar.
3. **Contador (no bloquea el uso local):** IVA D1, factura de crédito personal C1–C4, IVA sin netear devoluciones, costo con IVA, envío/armados y recargo en la ganancia.
4. **Dato de desarrollo:** el cliente 1 conserva un monto máximo histórico fuera de tope (ya no impide editarlo).
5. **Infraestructura diferida:** copia fuera del servidor, VPN y firewall, licencia de AutoMapper, una sola instancia de la app; Mercado Libre fuera de alcance.
6. **Cierre documental:** pasar esta matriz a «Aprobado» solo donde la evidencia sea suficiente; el veredicto final de la Etapa D depende de los puntos 1 y 2. No se autoriza la puesta en producción.
