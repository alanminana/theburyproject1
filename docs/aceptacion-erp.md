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
| Usuarios y Roles | Aprobado | Inventario COMPLETO, último SuperAdmin protegido, matriz de permisos con 518 acciones; **D-1 y D-8 resueltos en la base real** (`ventas.discount`, `creditos.configure` y las 10 asignaciones de Precios y Mora, verificados por rol en un clon) | Permisos del catálogo sin uso (~50) sin limpiar. |
| Documentos | Aprobado | Plantillas, versiones, previsualización PDF, contratos | — |
| Reportes | Aprobado con limitaciones | Ventas, Márgenes, Morosidad y Reporte de IVA contrastados con SQL, #205; ticket promedio con la definición aprobada (Hito 11) | Ventas brutas, devoluciones y netas (por fecha de devolución) y ganancia sobre neto sin IVA (H10, contrastado con SQL). Pendiente: costo con IVA, envío/armados y recargo en la ganancia; el Reporte de IVA aún no netea devoluciones. Ver `docs/iva-decisiones-contador.md`. |
| Dashboard | Aprobado | KPIs vs SQL, #177, mobile 390; ticket promedio con la definición aprobada (Hito 11) | — |
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
| ~~Dashboard: ticket promedio bruto mientras «Ventas del mes» es neta~~ | Resuelto (Hito 11) | Ticket promedio unificado en Dashboard y Reportes con la definición aprobada. |

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

## Hito 11 — Etapa D: aceptación final (`main` `745379a2` + PR de la etapa)

Entorno: clon `TheBuryProjectDb_qa_h11d` (copia `COPY_ONLY` de la base real), restaurado de cero para la corrida final y sembrado con los mismos sembradores que el CI; app en :5199. La base real se consultó **solo en lectura**: no se modificó, no se aplicaron migraciones, no se ejecutó ningún job financiero.

### Cambios de la etapa

| Cambio | Motivo | Evidencia |
|---|---|---|
| **Interruptor de arranque del job de borradores** (`BorradoresCredito:VencimientoAutomaticoHabilitado`, apagado por defecto, falla cerrado ante un valor inválido) | Hasta hoy el único modo de apagar el job era poner `NULL` en una tabla de la base real. Ahora no corre salvo habilitación explícita (appsettings o variable de entorno). | 7 tests (ausente, `false`, `flase`, vacío, `1` → no corre ni consulta la base; `true` → corre); verificado en la app real del clon: el log informa «deshabilitado» y la venta 1003 queda intacta pasados los 2 minutos de espera inicial. Revisión adversarial de Codex: 1 hallazgo medio (un valor inválido tumbaba el arranque) corregido. |
| Sembradores E2E sellan `FechaUltimaActualizacionDatos` | La base real tiene la revisión de datos de clientes activa con **1 día**: los clientes sembrados con fecha vieja disparaban el aviso modal que tapa los clics y volvía frágiles 17 specs. | Corrida completa sin esos fallos. |
| 6 specs usan el CTA visible (`CTA_PRIMARIO_VISIBLE`) en vez de `#btn-confirmar` | En móvil `#btn-confirmar` está oculto (rige el CTA fijo inferior): 16 specs de escritorio fallaban a 390 px sin que hubiera un defecto de producto. | Los 16 pasan a 390×844 y a 1366×768. |

### Regresión E2E (clon, suite completa a 1366×768, 223 specs)

**201 pasan, 18 omitidos y 4 fallan.** Los 4 se investigaron uno por uno; ninguno es un defecto de producto comprobado, ninguno se desactivó:

| Spec | Causa verificada | Clase |
|---|---|---|
| `venta-descuento-permiso` — «Edit de una venta con descuentos: totales reales» | Espera $1.600 y la pantalla muestra $1.520: la configuración real tiene **descuento del 5 % por pago en Efectivo** (decisión vigente del propietario); el CI usa una base sin esa configuración. | Configuración de la base real |
| `cotizacion-credito-personal-aptitud-contradiccion` — «Cliente No apto» | Busca un cliente por DNI de QA; en el clon existe un cliente real con ese DNI que está *Apto con cupo $0* (en el CI el DNI no existe y el spec se omite). **Observación a revisar con el propietario:** la tarjeta muestra «Apto» con «Cupo disponible $0» y monto solicitado $12.752. | Datos de la base real |
| `venta-excepcion-documental-reload` | Exige que la configuración documental pida un documento exceptuable; el sistema documental de la base real se configuró a mano y no coincide con el supuesto. | Configuración de la base real |
| `cotizacion-simulador` T9 (móvil) | Depende de los planes de pago configurados en la base real. | Configuración de la base real |

A 1280×720 y 390×844 el resultado de la Etapa C se mantiene (solo fallos de datos); la regresión autorizada sigue siendo el workflow «Playwright Tests» del CI (base sembrada y vacía de configuración propia).

Lección registrada: el dataset sembrado **se consume** (los tests editan y cobran sobre él); una segunda corrida sobre la misma base produce fallos espurios. Cada corrida exige una restauración limpia.

### Controles de integridad en SQL Server

13 controles ejecutados sobre el clon (después de la suite completa, es decir, con todo lo que crearon los E2E) y, en solo lectura, sobre la base real: stock = último movimiento; cadena de stock sin cortes; stock no negativo; cuota = capital + interés + punitorio; pagado ≤ total (el recargo del medio de pago puede ser negativo: es un descuento); cuota pagada sin saldo; cierre esperado = inicial + ingresos − egresos; diferencia = real − esperado; crédito cancelado sin saldo; saldo no negativo; una sola apertura abierta por caja; ventas comerciales con detalle; venta cancelada sin crédito vigente. **0 violaciones en ambas bases.**

### Defectos operativos conocidos: estado

| # | Defecto / pendiente | Estado |
|---|---|---|
| **D-1** | **La base real no tiene los permisos `ventas.discount` ni `creditos.configure`.** Causa: los user-secrets del servidor fijan `Database:InitializeOnStartup=false`, de modo que la app nunca ejecuta los sembradores contra la base real y los permisos añadidos después del 2026-10-04 no existen. Efecto: Administrador, Gerente y Vendedor no pueden aplicar descuentos ni configurar el plan de un crédito; solo SuperAdmin (por bypass). | **Abierto — requiere tu autorización.** Reproducido en el clon: al ejecutar el inicializador se crean exactamente 2 acciones y 16 asignaciones a roles (Administrador 2, Gerente 9, Cajero 1, Contador 1, SuperAdmin 2, Vendedor 1). Ojo: ejecutar el inicializador en Development también crea los usuarios de prueba; hacerlo en el modo normal o con un script acotado. |
| D-2 | Job de borradores | **Mitigado en código** (apagado por defecto). Primera corrida real = decisión del propietario: cancelaría venta 1003 + crédito 3 (−$256.060 de cupo). |
| D-3 | Ticket promedio | Definición propuesta en la Etapa C; **implementación pendiente de tu confirmación** (no se modificó). |
| D-4 | Listas de precios | Circuito operativo = cambio directo del Catálogo (aplicar, historial, revertir, concurrencia en SQL Server). El modo por lotes **no está disponible** (sin listas de precios ni pantalla para crearlas): documentado, fuera del alcance. |
| D-5 | Venta 1006 | Conservada sin cambios (borrador sin crédito; no reserva cupo ni mueve stock). |
| D-6 | Referencias del cliente | Pestaña oculta; no existe en el modelo. |
| D-7 | Cliente «Apto» con cupo $0 en la tarjeta de aptitud del cotizador | **Observación nueva**, sin diagnóstico cerrado: confirmar con el propietario si el cliente con ese DNI tiene límite de crédito 0 y si la tarjeta debe decir «Apto» en ese caso. |

### Conclusión de la etapa

- **Cierre funcional:** los 19 módulos tienen evidencia en la matriz; no hay defectos funcionales de código sin corregir. Hay **un defecto operativo abierto (D-1)** que bloquea el uso de descuentos y la configuración de crédito por roles distintos de SuperAdmin hasta que lo autorices.
- **Pendientes fiscales/contables** (no bloquean el uso local): IVA D1, factura de crédito personal C1–C4, IVA sin netear devoluciones, costo con IVA, envío/armados y recargo en la ganancia.
- **Preparación para producción: no autorizada.** Faltan: copia fuera del servidor, VPN y firewall del servidor local (la seguridad básica del servidor no está resuelta), licencia de AutoMapper, una sola instancia de la app. Mercado Libre queda fuera de alcance.
- **Hito 11:** no se declara finalizado mientras D-1 siga abierto. Con tu autorización de D-1 y las decisiones D-2/D-3 queda cerrado funcionalmente.

## Hito 11 — Cierre (`main` `c1f1ca2f` + PR de cierre)

Decisiones del propietario aplicadas: D-1 autorizado, job de borradores deshabilitado, ticket promedio aprobado, aptitud con cupo $0 a verificar, listas de precios por lotes diferidas, venta 1006 sin cambios, pestaña Referencias oculta. La base real se modificó **solo** en D-1 (descrito abajo); no se aplicaron migraciones ni se ejecutó ningún job financiero; la venta 1003 y el crédito 3 siguen intactos.

### D-1: permisos `ventas.discount` y `creditos.configure` en la base real

- **Procedimiento:** `scripts/sql/h11-d1-permisos-ventas-discount-creditos-configure.sql`. Acotado (solo `INSERT` de 2 acciones y 6 asignaciones), idempotente, transaccional (`XACT_ABORT`, ensayo obligatorio `-v DryRun=1|0`), serializado con un bloqueo de aplicación, aborta ante roles homónimos o acciones dadas de baja, y verifica antes de confirmar. No ejecuta el inicializador ni crea usuarios.
- **Asignaciones (idénticas a las del sembrador, comparadas fila por fila):** `ventas.discount` → Administrador, SuperAdmin; `creditos.configure` → Administrador, SuperAdmin, Gerente, Vendedor.
- **Respaldo:** `COPY_ONLY` con `CHECKSUM` y `RESTORE VERIFYONLY` correcto, antes de modificar (`D:\tmp\h11-d1-previo-TheBuryProjectDb-20261104-205444.bak`).
- **Clon primero:** ensayo (revierte), aplicación, reaplicación (0 inserciones) y dos ejecuciones simultáneas desde cero (una inserta, la otra 0; sin duplicados).
- **Base real, resultado:** +2 acciones y +6 asignaciones; el checksum del resto de los permisos no cambió; usuarios, roles, ventas y créditos con los mismos conteos. Verificación por rol en el clon, antes y después:

| Rol | `creditos.configure` (Credito/ConfigurarVenta) | Descuentos en Cotización |
|---|---|---|
| admin (SuperAdmin) | permitido | sí |
| Administrador | **antes: AccessDenied → ahora permitido** | **antes: no → ahora sí** |
| Gerente | **antes: AccessDenied → ahora permitido** | no (decisión vigente) |
| Vendedor | **antes: AccessDenied → ahora permitido** | no |
| Cajero, Contador | AccessDenied (sin cambios, correcto) | sin acceso |

Los usuarios deben **volver a iniciar sesión** para que sus claims incluyan los permisos nuevos. Tests de contrato del script: `H11D1PermisosScriptContractTests`.

**D-8 (resuelto, ver «Hito 11 — D-8» al final):** a la base real le faltaban 10 asignaciones del sembrador (Precios y Mora para Gerente, Cajero y Contador); autorizadas y aplicadas después de este cierre.

### Ticket promedio (implementado)

(ventas brutas del período − devoluciones de **esas mismas ventas** con fecha de devolución dentro del período) ÷ cantidad de ventas del período; sin ventas, 0. Una única función (`VentasNetas.CalcularTicketPromedio`) usada por Dashboard y Reportes; no cambia ningún criterio fiscal ni registros históricos. Tests (Reportes y Dashboard, mismo escenario): sin devoluciones (150), venta devuelta en el mismo período (100), devolución parcial (125), devolución de una venta de otro período (150: antes habría distorsionado), sin ventas con devolución de otro período (0), devolución no aprobada (no resta), helper con cantidad 0.

### Cliente «Apto» con cupo $0

Verificado sobre un clon fiel a la base real: el cliente con DNI 35996614 sale **No apto** con cupo $0 y faltante de $12.752 (correcto). La anomalía vista en la Etapa D («Apto» con «Cupo disponible $0») ocurre cuando la validación de cupo está desactivada: el cupo no se calcula (queda en 0) y se mostraba como si fuera un dato. Correcciones: el endpoint informa `cupoEvaluado`, `excedeCupo` y el faltante real; la tarjeta dice «Cupo: sin validar» en lugar de $0 (en cualquier estado) y, si el monto supera un cupo evaluado, lo muestra claramente («Supera el cupo disponible», con cupo, solicitado y faltante; en «Requiere autorización» avisa que no se podrá confirmar). **Sin cambios en reglas crediticias ni en datos:** la financiación que excede el límite ya se rechazaba al confirmar la venta (`ValidarCupoDisponibleEnConfirmacionAsync`, con tests) y al crear el crédito; si el propietario desactiva la validación de cupo en la configuración, no se aplica ningún límite (decisión de configuración, no de código). Revisado visualmente en 1366×768 (3 estados) y con tests (`CotizacionAptitudCupoResumenTests`).

### Revisión independiente (Codex)

Dos revisiones adversariales sobre el cierre. 1.ª: 2 hallazgos altos (el script no serializaba ejecuciones concurrentes —aunque la base tiene índices únicos— y resolvía roles por nombre sin detectar homónimos) y 1 medio (la tarjeta mostraba cupo $0 sin evaluar en estados no-Apto y no advertía exceso en «Requiere autorización»): corregidos con prueba. 2.ª, enfocada en el cambio: **aprobada, sin hallazgos**. El estado del worktree fue idéntico antes y después de cada revisión.

### Estado final del Hito 11

| Tema | Estado |
|---|---|
| D-1 permisos | **Resuelto** en la base real (con respaldo verificado). |
| Job de borradores | Deshabilitado por defecto; venta 1003 y crédito 3 intactos. Habilitarlo = decisión futura del propietario. |
| Ticket promedio | Implementado y probado en Dashboard y Reportes. |
| Cliente Apto con cupo $0 | Verificado; presentación corregida; sin cambios de reglas ni datos. |
| Listas de precios por lotes | Diferido; el cambio directo del Catálogo es el circuito operativo. |
| Venta 1006 / Referencias | Sin cambios / pestaña oculta. |
| D-8 (`precios.*`, `mora.*` sin asignar) | **Resuelto** (ver «Hito 11 — D-8»). |

**Cierre funcional:** sin defectos funcionales de código sin resolver; queda el uso de cheques, que debe probarse con un cheque de prueba antes de operar. **Pendientes fiscales/contables:** IVA D1, factura de crédito personal C1–C4, IVA sin netear devoluciones, costo con IVA, envío/armados y recargo en la ganancia. **Preparación para producción: NO autorizada** — siguen pendientes la copia fuera del servidor, la VPN, el firewall y la seguridad básica del servidor local, la licencia de AutoMapper y la instancia única de la app; Mercado Libre fuera de alcance.

## Hito 11 — D-8: permisos de Precios y Mora (cerrado)

**Autorizado por el propietario** exclusivamente para estas 10 asignaciones, que reproducen la política de `Data/Seeds/RolesPermisosSeeder.cs` (Gerente: módulos Precios y Mora salvo `precios.create/update/delete` y `mora.config`; Cajero y Contador: solo `mora.view`):

| Rol | Permisos asignados |
|---|---|
| Gerente | `precios.view`, `precios.simulate`, `precios.approve`, `precios.apply`, `precios.revert`, `precios.history`, `mora.view`, `mora.manage` |
| Cajero | `mora.view` |
| Contador | `mora.view` |

- **Estado previo verificado:** las 8 acciones existían y estaban activas; Gerente, Cajero y Contador no tenían ninguna fila de `precios.*` ni `mora.*` (ni dada de baja: no hubo una remoción deliberada).
- **Procedimiento:** `scripts/sql/h11-d8-permisos-precios-mora.sql`. Solo `INSERT` en `RolPermisos` (no crea acciones), idempotente, transaccional, serializado (`sp_getapplock` + `HOLDLOCK`), con ensayo obligatorio (`-v DryRun=1|0`). Los roles se resuelven una sola vez bajo bloqueo y aborta si falta alguno, si hay homónimos, si falta una acción o si una asignación existe dada de baja; la verificación exige siempre las 10 filas. Test de contrato: `H11D8PermisosScriptContractTests`. Se endureció igual el script de D-1 (mismo patrón de roles).
- **Clon primero:** ensayo (revierte), aplicación, reaplicación (0 inserciones), dos ejecuciones simultáneas (una inserta 10, la otra 0, sin duplicados), rol ausente y rol homónimo (abortan sin insertar nada).
- **Acceso efectivo por rol (clon, GET sin seguir redirecciones), sin y con D-8:**

| Rol | `/CambiosPrecios` y `/Simular` | `/Mora` | `/Mora/RegistrarContacto` (gestionar) |
|---|---|---|---|
| admin, Administrador | permitido (sin cambios) | permitido | permitido |
| Gerente | **antes denegado → ahora permitido** | **antes denegado → ahora permitido** | **antes denegado → ahora permitido** |
| Cajero, Contador | denegado (correcto) | **antes denegado → ahora permitido (solo ver)** | denegado (correcto) |
| Vendedor | denegado (sin cambios) | denegado (sin cambios) | denegado (sin cambios) |

- **Base real — respaldo:** `COPY_ONLY` con `CHECKSUM` y `RESTORE VERIFYONLY` correcto inmediatamente antes de aplicar (`D:\tmp\h11-d8-previo-aplicacion-TheBuryProjectDb-20261104-231304.bak`, 46.884.352 bytes).
- **Base real — resultado:** +10 filas (RolPermisos 615 → 625); el checksum del resto de los permisos es idéntico al previo; acciones, usuarios, roles, ventas, créditos, cuotas, movimientos de caja y movimientos de stock con los mismos conteos; ningún permiso fuera de la lista (0); Administrador y SuperAdmin sin cambios. Las 13 invariantes financieras siguen en 0 violaciones; venta 1003 (PendienteFinanciacion), venta 1006 y crédito 3 (PendienteConfiguracion, saldo 256.060) sin cambios. No se ejecutó el inicializador, no se crearon usuarios, no se aplicaron migraciones ni se ejecutó el job de borradores.
- **Revisión independiente (Codex):** 1.ª con 1 hallazgo alto (la resolución de roles no quedaba protegida frente a homónimos concurrentes) y 1 medio (un rol ausente permitía confirmar una aplicación parcial): corregidos en ambos scripts, con pruebas de rol ausente y duplicado. 2.ª, enfocada en el cambio: **aprobada, sin hallazgos**. El worktree fue idéntico antes y después de cada revisión.
- **Los usuarios Gerente, Cajero y Contador deben volver a iniciar sesión** para que sus permisos nuevos tomen efecto.

**Estado final del Hito 11:** todos los pendientes funcionales identificados están resueltos o diferidos por decisión del propietario. Quedan fuera del cierre funcional: probar cheques con un cheque de prueba antes de operar; los pendientes fiscales/contables; y la preparación para producción (copia fuera del servidor, VPN, firewall y seguridad básica del servidor local, licencia de AutoMapper, instancia única de la app). **No se autoriza la puesta en producción.**
