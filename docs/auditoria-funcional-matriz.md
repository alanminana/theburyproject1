# Auditoría funcional integral — matriz de aceptación con evidencia (fase 5, 2026-11-06)

Base: `main` (PR #236 a #238 y #240 a #243 fusionadas) + PR de cierre de la fase 5. H-01 vive en el PR #239 (borrador). Complementa `docs/auditoria-funcional-2026-11-05.md` (tramo 1) y `docs/auditoria-funcional-reglas-h01-h04-h05.md`.
**La auditoría NO está cerrada.** Hay **dos métricas distintas** que no deben confundirse:

- **Cobertura de acciones HTTP ejercitadas con evidencia: 24,2 %** (75 de 310 funcionalidades de usuario; la base del tramo 2 recalculada con los mismos logs da 43, 13,9 %). Mide *qué se probó*, no cuánto está desarrollado. «Ejercitada» en una acción de solo lectura (GET) significa que cargó con 200: **no** prueba que su contenido sea correcto. Contenido contrastado con SQL: Reporte de ventas, Morosidad (deuda vencida total 2.296.750 = SQL) y Márgenes (muestra); los 6 exportes (Excel/PDF) se obtuvieron con 200 y tamaño > 0 sin abrir su contenido. Los POST rechazados por autorización (302 a *AccessDenied*) se excluyen del conteo: los logs de la sonda de permisos no se usan como evidencia de funcionalidad.
- **Desarrollo funcional completado**: tabla cualitativa de la §2-bis. No es un porcentaje porque no hay un denominador de «alcance aprobado» cerrado; se informa por módulo con su estado y lo que falta.

Todo lo no ejercitado es *no verificado*, no aprobado.

## 1. Cómo se mide (para que el porcentaje sea auditable)

- **Funcionalidad** = par (controlador, acción) de un controlador de usuario (se excluyen los `*Api` y utilitarios JSON, que se informan aparte). Si la acción tiene POST, solo cuenta si se ejercitó **ese POST** con respuesta < 400; si es solo GET, el GET. Denominador: **310** funcionalidades de **18** módulos (el 19.º, *Infraestructura y recuperación*, no tiene acciones HTTP y se informa en §4-F; Mercado Libre y Diagnóstico quedan fuera de alcance).
- **Evidencia = ejercicio real contra la app**, registrado por el log del servidor (`Executing endpoint` + `Request finished` con verbo y estado), no por tener pantalla ni tests unitarios:
  - **A (auditoría)**: circuitos operados desde la interfaz con contraste SQL, repetidos de punta a punta en un clon restaurado desde un estado conocido (resultados en §3).
  - **E (E2E)**: la suite Playwright existente (1366×768) en un clon limpio: **209 correctos · 10 omitidos · 4 fallidos** (3 dependientes de la configuración de la base real y 1 por una variable de entorno de seeds desactualizada en mi corrida, no del producto).
- Los tests unitarios y de integración **no suman** a este porcentaje (hay 8.195: 7.978 pasan y 200 se omiten por entorno). El generador es `scripts/qa/inventario-funcional.py` y el detalle por acción está en `docs/auditoria-funcional-inventario.csv`.
- Limitación explícita: «ejercitada» no equivale a «correcta en todos los casos». Una acción verificada cuenta una vez aunque tenga varias ramas (negativos, permisos, concurrencia); las ramas probadas se informan en §3.

## 2. Matriz por módulo

| Módulo | Funcionalidades | Verificadas (auditoría) | Verificadas (E2E) | Fallidas | Incompletas | No verificadas | Cobertura |
|---|---:|---:|---:|---:|---:|---:|---:|
| Ventas y Cotizaciones | 27 | 9 | 1 | 0 | 0 | 17 | 37 % |
| Caja | 11 | 6 | 0 | 0 | 0 | 5 | 55 % |
| Crédito | 25 | 5 | 6 | 0 | 0 | 14 | 44 % |
| Mora y Acuerdos | 21 | 9 | 0 | 0 | 0 | 12 | 43 % |
| Cheques | 5 | 4 | 0 | 0 | 0 | 1 | 80 % |
| Compras | 16 | 6 | 0 | 0 | 0 | 10 | 38 % |
| Stock | 62 | 9 | 0 | 0 | 12 | 41 | 15 % |
| Devoluciones | 12 | 5 | 0 | 0 | 0 | 7 | 42 % |
| Notas de crédito | 1 | 1 | 0 | 0 | 0 | 0 | 100 % |
| Clientes | 27 | 3 | 4 | 0 | 0 | 20 | 26 % |
| Usuarios y Roles | 21 | 14 | 0 | 0 | 0 | 7 | 67 % |
| Documentos | 29 | 17 | 0 | 0 | 0 | 12 | 59 % |
| Reportes | 14 | 13 | 0 | 0 | 0 | 1 | 93 % |
| Dashboard | 5 | 2 | 0 | 0 | 0 | 3 | 40 % |
| Notificaciones | 3 | 2 | 0 | 0 | 0 | 1 | 67 % |
| Tickets | 5 | 4 | 0 | 0 | 0 | 1 | 80 % |
| Configuración | 25 | 11 | 0 | 0 | 0 | 14 | 44 % |
| Seguridad | 1 | 1 | 0 | 0 | 0 | 0 | 100 % |
| **Total** | **310** | **121** | **11** | **0** | **12** | **166** | **42.6 %** |

Notas: *Stock* incluye Catálogo, Productos, Categorías, Marcas, Precios y Alertas. *Incompletas* = las 12 funcionalidades del módulo de cambios de precios por lotes (sin listas ni menú; diferido). *Notas de crédito* solo tiene 1 acción propia: su función real (generar al aprobar la devolución, canjear como medio de pago, restituir al cancelar) vive en Devoluciones y Ventas y se prueba en §3. *Cheques* y *Mora y Acuerdos* tienen evidencia **previa** documentada (`docs/cheques-circuito-propuesta.md` §7; Hitos 9 y 10) que **no se re-ejecutó** en esta auditoría, por eso figuran en 0.

## 2-bis. Estado de desarrollo funcional por módulo (métrica distinta de la cobertura)

«Desarrollado» = la funcionalidad existe y sus circuitos principales funcionan en lo ejercitado. No implica que todas sus acciones estén probadas (ver cobertura). Nada se declara «terminado» mientras queden acciones sin ejercitar.

| Módulo | Desarrollo | Evidencia de la fase 3 | Pendiente / abierto |
|---|---|---|---|
| Ventas y Cotizaciones | Desarrollado; **H-01 aprobada, sin implementar** | Cotización → conversión (manual y «Confirmar Mi Venta») → venta → stock → caja, con negativos (conversión duplicada, fecha de primera cuota pasada) y SQL | H-01 (migración solo en clon QA), descuentos/permiso |
| Caja | Desarrollado | Apertura única (H-05/H-11), cobro de cuotas, acuerdos y cheque contra caja con saldo SQL | Índices únicos filtrados (esperan análisis de aperturas vencidas) |
| Crédito | Desarrollado | Plan 6 cuotas: capital 6.000 + interés 1.440 = 7.440; 6 cuotas × 1.240 con vencimientos mensuales (SQL); pago parcial 500 + 740 con caja | Punitorios (E2E), refinanciación |
| Mora y Acuerdos | Desarrollado | Ficha → contacto → promesa → acuerdo (21.300 + 4 × 25.000 = 121.300) → activación → pago de entrega y cuotas → acuerdo *Cumplido*, créditos *Pagados* y caja repartida por cuota original | KPI de la bandeja sin conciliar (H-18); incumplido/cancelación |
| Cheques | Desarrollado, **medio no habilitable desde la UI** (H-15) | Venta con cheque → pendiente → acreditar (confirmación) → rechazar acreditado con motivo obligatorio (egreso reversor) | Habilitación del medio; cuota con cheque rechazado (decisión A/B/C) |
| Compras | Desarrollado | Tramo 2 (OC, recepción parcial/total, sobre-recepción rechazada) | Resto de acciones |
| Stock | Desarrollado salvo **lotes de precios (Incompleto, diferido)** | Ajuste masivo del catálogo: simular → aplicar (+10 % solo a los seleccionados) → historial → revertir el evento completo | H-16 (mensaje de reversión); listas de precios (§4-C) |
| Devoluciones / Notas de crédito | Desarrollado | Tramo 2; (fase 5) **circuito por interfaz contra SQL**: «Crear devolución» desde la venta (sin ítems y con descripción corta no se crea), aprobar → completar: devolución DEV-…002 por 1.900 (1 de 2 unidades de 3.800), **nota de crédito NC-202611-000001 por 1.900 al cliente, vigente a un año, sin egreso de caja**, stock 81 → 82, y el listado de notas de crédito del cliente lo muestra | Rechazar/cancelar, uso de la nota de crédito en una venta (cubierto por la suite SQL Server), reembolso en otros medios |
| Clientes | Parcial verificado | E2E | Pestaña Referencias (oculta, sin modelo) |
| Usuarios y Roles | Desarrollado | Tramo 2 (matriz 8×34 por GET) | Permisos por acción POST |
| Documentos | Desarrollado, poca evidencia | Contrato de crédito generado y verificado en el circuito de crédito | Configuración documental (3 %) |
| Reportes / Dashboard | Desarrollado | Los 8 reportes y 6 exportes responden (200); contenido contrastado con SQL en Ventas, Morosidad (total) y Márgenes (muestra: Motor Eléctrico 2.000/1.000 → 39,5 %; ganancia potencial 100 × 652,89); IVA coherente con la regla «solo facturadas/entregadas» | Contenido de los exportes sin abrir; Comisiones, Agrupadas e IVA con datos facturados sin contrastar; **H-22** (morosidad vs bandeja) |
| Notificaciones | Desarrollado, mínimo | Marcar leída por usuario (SQL) | Eliminación; solo se observó la notificación «Caja abierta» |
| Tickets | Desarrollado | Alta con validación, Marcar en curso, Cancelar, Eliminar (baja lógica) | Adjuntos, checklist |
| Configuración | Desarrollado | Rentabilidad (negativos y válidos), prefijos de numeración (negativos y válido), costos de envío/armado | Medios de pago, plantillas de contrato, configuración de cliente |
| Seguridad | Desarrollado | Pantalla de auditoría accesible | Filtros y eventos |
| Infraestructura | Fuera del alcance funcional | — | Diferido por el propietario |

## 3. Resultados de los circuitos (clon limpio, SQL Server LocalDB, interfaz + SQL)

| Circuito | Resultado verificado |
|---|---|
| Proveedor → OC → recepción → stock | Alta y validaciones (obligatorios, CUIT inválido, duplicado); OC sin ítems rechazada; recepción parcial y total; sobre-recepción y cantidad negativa rechazadas por el **servidor**; stock = Σ movimientos (0→4→10→15); IVA incluido = 21/121 |
| Venta contado → factura → caja | Crear no descuenta stock; confirmar y facturar descuenta 1 y cobra en caja; neto + IVA = total; factura B coherente; el `COT-…` se conserva en la venta facturada (H-01) |
| Devolución | Sin ítem rechazada; reembolso con **egreso obligatorio** (aun con la casilla bloqueada), stock +1, referencia `DEV-…` sin duplicar (H-02); la NC se genera al aprobar y no genera egreso |
| Nota de crédito → canje | NC insuficiente no se concreta; NC suficiente se consume por el total de la venta y queda saldo; el canje es un movimiento **no físico** que no entra al efectivo del arqueo |
| Caja | Apertura única por usuario y por caja (H-05, UI y concurrencia); montos inválidos rechazados en el servidor; cierre con efectivo esperado calculado de forma independiente en SQL, diferencia sin justificar rechazada, doble cierre rechazado |
| Usuarios | Reset de contraseña: política, confirmación, rotación de SecurityStamp, ingreso con la nueva |
| Reportes / Dashboard | Ventas brutas $695.496,20 · devoluciones −$693.496,20 · netas $2.000,00 · ganancia sobre neto sin IVA, idénticas en SQL, Reporte y Dashboard |
| Permisos | Matriz de 8 roles × 34 rutas (tramo 1); sin escalada. **No** se probó el permiso por acción (POST) |
| Concurrencia (SQL Server, RCSI 0 y 1) | `CajaAperturaUnicaSqlServerTests` 6/6 (4 de 6 **fallan con el código anterior**); `FinResidual` + `FinCaja` + nuevas: 90/90 en RCSI 0 y 90/90 en RCSI 1 |

### 3-bis. Circuitos de la fase 3 (clon `f3` restaurado desde el respaldo limpio; interfaz con Playwright + SQL)

| Circuito | Casos positivos | Negativos / excepcionales | Contraste SQL | Resultado |
|---|---|---|---|---|
| Cotización → venta | Simular (13 opciones; Efectivo −5 %), «Confirmar Mi Venta» → `VTA-202611-000001`, conversión manual desde Detalles → Edit → confirmar | Conversión duplicada rechazada (400, «ya fue convertida»); fecha de primera cuota pasada rechazada con mensaje | Stock 100→98→96→94; ingreso de caja por el total con la referencia de la venta | OK; **H-13** (cotización duplicada) corregido |
| Crédito → cuotas → cobro | Crédito personal 6 cuotas, contrato generado y verificado, venta confirmada | Primera cuota < hoy (400; la venta queda pendiente y se reutiliza); pago 0 y pago > saldo deshabilitan confirmar | 6 cuotas × 1.240 = 7.440; pago 500 + 740 → cuota Pagada; caja 12.640 = 3 × 3.800 + 500 + 740 | OK |
| Mora → acuerdo | Contacto, promesa, acuerdo con entrega, activar, pagar entrega + 4 cuotas | Promesa con fecha pasada bloqueada por el navegador; acuerdo con 0 cuotas sin efecto | 21.300 + 4 × 25.000 = 121.300; caja repartida 60.650 + 60.650 por cuota original; acuerdo Cumplido | OK; contacto sin observaciones se acepta (H-19) |
| Cheque | Venta con cheque → pendiente → acreditar → rechazar acreditado | Rechazo sin motivo bloqueado; el medio no existe hasta crear su configuración (H-15) | `EstadoAcreditacion` 1 → 2; egreso reversor de 2.000; venta sigue confirmada | OK |
| Envío / armado → ganancia | Costos 20.000 / 10.000 cargados desde Configuración; cotización con armado N.º 1 + envío Ciudad | Costo negativo no se persiste | Total 62.000 × 0,95 = 58.900; `CostoEstandarServicio` 20.000 y `ArmadoCostoUnitarioEstandar` 10.000 congelados | OK; **H-14** (costo del producto no congelado en la conversión) corregido |
| Cambio de precios directo + reversión | Ajuste masivo +10 % a 2 productos: vista previa 2.000 → 2.200; historial; revertir | Valor vacío no avanza; el resto del catálogo no cambia | Precios 2.200 y luego 2.000; evento *reversion* creado; se revierte el evento completo (H-16) | OK |
| Tickets / Notificaciones / Configuración | Alta, estados, eliminar; leída por usuario; umbrales y prefijos | Ticket vacío; umbral bajo > alto; negativo; prefijo vacío / inválido | Filas y estados | OK; H-17 y H-20 abiertos |

## 4. Hallazgos (defectos de software separados del resto)

**A. Defectos de software**

| ID | Módulo | Crit. | Resultado | Estado |
|---|---|---|---|---|
| H-03 | Configuración (punitorios) | Media | La vigencia retroactiva era inválida para el cliente (`data-fecha="hoy"` fija `min=hoy`) y el modal de confirmación se abría con el formulario inválido; `form.submit()` salteaba la validación | **Corregido** (regla `hoy-libre`, validar antes del modal, `requestSubmit`); prueba de contrato y spec E2E |
| H-05 | Caja | Media | Un usuario podía abrir varias cajas (carrera y secuencia) | **Corregido** (locks de aplicación + transacción); pruebas SQL Server RCSI 0/1 |
| H-11 | Caja | Baja | Misma caja con aperturas simultáneas: **no** había duplicado de datos (lo evitaba el `RowVersion` de Caja por azar), pero la perdedora fallaba con un `DbUpdateConcurrencyException` crudo | **Corregido** (rechazo de negocio con mensaje); la prueba previa R-05 documentaba la defensa por RowVersion y se actualizó |
| H-04 | Devoluciones | Media | Reembolso en efectivo sin egreso de caja por defecto; reembolso «completado» de venta no cobrada por caja sin egreso ni NC | **Corregido** (egreso obligatorio; rechazo de reembolso en efectivo de ventas sin caja); regla aprobada por el propietario |
| H-02 | Devoluciones/Stock | Baja | Referencia `DEV-DEV-…` | **Corregido** |
| H-06 | Compras (UI) | Media | A 1280–1535 px la columna «A recepcionar» quedaba oculta (tabla de 735 px en un contenedor de 623–680) | **Corregido** y verificado a 1366×768 |
| H-13 | Cotización | Media | «Confirmar Mi Venta» guardaba la cotización dos veces; además, al cambiar la opción de pago con el modal abierto se podía confirmar con datos viejos | **Corregido** (huella del contenido; al reemplazar se cancela la previa; PR #238 y #240). Verificado: sin cambios → 1 cotización; con cambio de opción → 2, la previa queda *Cancelada*, la venta usa la opción visible |
| H-14 | Ventas / Reportes | Media | Las ventas creadas por conversión de cotización guardaban costo 0 en la línea; el reporte caía al `PrecioCompra` vigente y la ganancia de ventas ya hechas cambiaba al modificar el costo del producto | **Corregido** (se congela el costo al convertir) + prueba que falla con el código anterior |
| H-15 | Configuración / Cheques | Media | La venta con cheque no era seleccionable porque no existe la configuración del medio. **Corrección del análisis:** «Agregar método» ya permitía crearlo; el problema era que nacía activo y lo podía crear cualquiera con `configuracion.update` | **Corregido** (sin migración): solo SuperAdmin crea/edita/elimina el medio Cheque (y sus planes/tarjetas), nace **inactivo**, no se ofrece a otros roles. Dependencia abierta: al activarlo aplica la decisión pendiente «cuota con cheque rechazado» (A/B/C) |
| H-16 | Stock (precios) | Baja | El modal de reversión no avisaba que revierte el ajuste completo | **Corregido** (mensaje) |
| H-17 | Configuración | Baja | Validaciones del navegador en inglés | **Corregido** (`jquery-validate-messages-es.js`); verificado en la pantalla de rentabilidad |
| H-18 | Mora | Media | KPI de la bandeja de mora distinto de SQL. **Conciliado cliente por cliente:** (1) el KPI sale de las alertas, que aplican los **10 días de gracia**; con esa regla SQL y alertas coinciden (31 clientes, 2.289.400) salvo los clientes sembrados con alertas que incluyen cuotas dentro de la gracia; (2) **defecto real:** el reproceso solo refrescaba `DiasAtraso`, así que tras un pago parcial o al vencer más cuotas la alerta mostraba la deuda de su creación (reproducido: pago de 250 → alerta 1.000, deuda real 750) | **Corregido** (`ProcesarMoraAsync` refresca `MontoVencido`/`CuotasVencidas` sin tocar `UpdatedAt`) + prueba que falla con el código anterior. Queda **H-22** |
| H-19 | Mora | Baja | Se acepta registrar un contacto de cobranza sin observaciones | **Abierto — evaluación:** hoy no hay regla que lo prohíba y el resultado/tipo sí son obligatorios. Exigir observaciones sería una regla nueva (mejora el rastro de cobranza pero frena contactos rápidos). Propuesta: observaciones obligatorias solo para resultados «Promesa»/«No contesta» con próximo contacto; **no implementado hasta decidir** |
| H-20 | Cotización | Baja | El endpoint de cancelar cotización no tenía botón | **Corregido** (fase 5): panel «Cancelar cotización» en *Detalles* solo para *Emitida* con permiso `cotizaciones.cancel`, motivo obligatorio y confirmación; el servicio ya validaba estados. Verificado por interfaz (sin motivo no cancela; con motivo → *Cancelada* y motivo guardado en SQL; convertida sin panel; móvil 390 px) |
| H-21 | Seguridad (login) | Baja | La pantalla «Lockout» decía que la cuenta se bloqueó por intentos fallidos aun en un bloqueo administrativo | **Corregido** (fase 5): texto neutro que cubre ambos casos |
| H-23 | Documentos | Baja | Tras **cancelar un recibo** de una venta, «Generar pendientes» del detalle de la venta no lo vuelve a emitir (cantidad de documentos igual); el texto de ayuda sugiere que sí. Los recibos se reintentan por pago (`pagoCuotaId`) | **Abierto — a confirmar**: probar el reintento desde la cuota/pago y ajustar el texto o el alcance |
| H-24 | Clientes / Reportes | Media | Se podía dar de baja a un cliente con crédito vigente y, al hacerlo, su deuda y sus ventas desaparecían de mora, alertas, reportes y listados (filtros `Cliente.IsDeleted`) | **Corregido** (regla aprobada por el propietario): (1) la baja se rechaza en el **servidor** con créditos no cerrados o cuotas impagas, con mensaje claro; la pantalla lo anticipa (aviso, controles deshabilitados) y muestra el mensaje del servidor; (2) baja y alta de crédito se serializan con el lock de la fila del cliente (SQL Server, RCSI 0 y 1: gana la baja y el crédito se rechaza, o gana el crédito y la baja se rechaza); (3) se quitaron los filtros de cliente eliminado en ventas, créditos, mora, devoluciones, notas de crédito, Dashboard y reportes: el histórico y sus importes se conservan sin duplicar; (4) nada reactiva al cliente. Pruebas: 14 de regla, 3 de concurrencia SQL Server, mora con deuda heredada, reportes/Dashboard antes y después de la baja |
| H-25 | Reportes / Dashboard | Media | **«Ventas agrupadas» no coincidía con el Reporte de ventas**: sumaba 12 ventas / 98.140 contra 11 / 94.340 porque no excluía las de clientes dados de baja (el resto de Reportes y el Dashboard sí, con `VentasNetas.Comerciales`); lo mismo en el top de productos del Dashboard | **Corregido** (fase 5): ambas consultas usan el criterio único; prueba que falla con el código anterior; verificado por interfaz (94.340 / 11 / ganancia 28.966,96 = Reporte de ventas y SQL). Sigue pendiente la decisión H-24 sobre qué hacer con esas ventas |
| H-26 | Notificaciones | Baja | La acción `EliminarNotificacion` (permiso `notificaciones.delete`) **no tiene interfaz**: el panel solo ofrece «Marcar todas como leídas» | **Abierto (B, función solo backend):** decisión: exponer «Eliminar» en el panel o retirar el endpoint. Verificado: marcar todas afecta solo a las del usuario y las de otros usuarios no se pueden marcar ni eliminar (404) |
| H-22 | Mora / Reportes | Media | **Tres superficies, tres reglas:** la bandeja y las alertas aplican los días de gracia (10); la **ficha del cliente** y el **Reporte de morosidad** cuentan vencida desde el día siguiente (deuda 2.296.750 en el reporte vs 2.289.400 en la bandeja). Un cliente con una cuota en gracia aparece con deuda en la ficha y no en la bandeja | **Decisión de negocio (D):** ¿la gracia debe afectar a la ficha y al reporte? No se cambió |
| H-12 | Reportes | Baja | El «ticket» por cliente era bruto y no coincidía con el global neto | **Corregido** (mismo cálculo que el global) + prueba |
| H-10 | Usuarios | Baja | El mensaje decía «entre 6 y 100» y la política real exige más | **Corregido** (mensaje coherente con la política vigente) |

**B. Funcionalidad incompleta**: módulo de cambios de precios por lotes (sin listas de precios, sin ABM de listas, sin menú; además `AplicarBatch` no escribe `Producto.PrecioVenta`); pestaña *Referencias* del cliente (oculta, sin modelo).

**C. No desarrollada (fuera del alcance confirmado)**: ABM de listas de precios; cuentas y pagos a proveedor; devolución a proveedor desde una OC parcial; reembolso diferido cuando la caja no tiene efectivo.

**D. Decisión de negocio**: **H-01** (criterio aprobado; la migración se genera y prueba **solo en clones QA**, no se aplica a `TheBuryProjectDb`; ver `docs/auditoria-funcional-reglas-h01-h04-h05.md`); H-07 (el alta de OC ofrece «Cancelada» como estado inicial: permitido por diseño, COM-S-006, y evita el permiso de cancelar); índices únicos filtrados de apertura de caja (migración); método de costeo de compras; primera corrida del job de borradores; venta 1006; cliente 1 con monto máximo histórico.

**E. Pendiente fiscal (contador)**: tratamiento del interés y recargo (IVA D1), factura de crédito personal C1–C4, IVA sin netear devoluciones, costo con IVA, envío/armados/recargo en la ganancia.

**F. Infraestructura y pruebas**: copia externa, VPN/firewall, licencia de AutoMapper e instancia única de la app (todo diferido por el propietario); la suite E2E **consume su dataset** y exige clon restaurado, caja abierta y contraseñas de desarrollo (H-09); sin Docker en esta máquina.

## 5. Respuestas a las 10 preguntas

1. **¿Los 19 módulos funcionan?** Lo verificado funciona y no quedan defectos abiertos graves en lo probado, pero solo se verificó el 42,6 % de las funcionalidades: **no se puede afirmar que los 19 funcionen**.
2. **¿Completamente terminados?** Ninguno puede declararse así con esta evidencia. Los más avanzados: Caja (55 %), Devoluciones y Compras (~30 %, con sus circuitos principales completos).
3. **¿Incompletos?** Cambios de precios por lotes y Referencias del cliente (§4-B).
4. **¿Errores encontrados y corregidos?** H-02, H-03, H-04, H-05, H-06 y H-11 (PR #237) H-13 y H-14 (fase 3) y H-10, H-12, H-15, H-16, H-17 y H-18 (fase 4), todos sin migraciones.
5. **¿Qué falta desarrollar?** §4-C (fuera del alcance confirmado). Nada de eso se implementó.
6. **¿Decisiones comerciales?** §4-D; la más urgente es **H-01** (requiere autorización por migración).
7. **¿Pruebas que faltan?** Unas 268 funcionalidades sin ejercitar. Circuitos pendientes: cotización → conversión → venta (hoy solo E2E), cliente → crédito → cuotas → cobro, mora → acuerdo → regularización, cheques (re-ejecutar), envío/armado → ganancia, cambio de precios directo y reversión, Tickets, Notificaciones, Documentos, Configuración, permisos por acción, móvil y concurrencia fuera de caja/OC.
8. **¿Porcentaje funcional verificable?** **42,6 %** de cobertura de acciones (132 de 310; denominador y criterio en §1). Es cobertura de *prueba*, no porcentaje de *desarrollo completado*: ese estado va por módulo en §2-bis.
9. **¿Cuánto desarrollo queda?** Desarrollo nuevo: ninguno confirmado dentro del alcance. Lo que queda es **verificación** (unas 268 funcionalidades) y las decisiones de §4-D/E.
10. **¿Se puede declarar finalizado el desarrollo funcional?** **No**, con la evidencia disponible.

## 6. Checklist de pendientes por prioridad

1. **H-01 (PR #239, borrador):** código y pruebas listos; **migración no aplicada**. Propuesta de aplicación segura (respaldo, ensayo, recuperación) en `docs/h01-aplicacion-segura.md`. Espera tu autorización explícita.
2. **Decisiones de negocio:** H-22 (¿la gracia de mora afecta a ficha y reporte?), H-19 (¿observaciones obligatorias en contactos?), cuota con cheque rechazado (A/B/C) antes de activar el medio Cheque, Gerente (borrar clientes / cancelar ventas).
3. **Verificación pendiente:** Notas de crédito, Stock (categorías, marcas, unidades físicas, resolver/ignorar alertas), Clientes (alta, edición, documentos), Reportes (Comisiones, Agrupadas, IVA con datos facturados), Documentos (activar/restaurar plantilla, reglas, paquetes, H-23).
4. Permisos POST: matriz por rol en los anexos; faltan los permisos parciales por acción.
5. **Móvil (390 px):** 15 pantallas inspeccionadas a mano en fase 5 (Cajas, Inventario, Cheques pendientes, Clientes, Documentos, Mora, Reportes, Seguridad, Detalle de venta, Créditos, Compras, Devoluciones, Tickets, Ventas y Cotización) sin desborde ni elementos superpuestos; la pestaña «Auditoría» de Seguridad queda recortada (desplazable). Faltan modales, wizard de venta y estados de error.
6. **Concurrencia:** las suites SQL Server de la CI (RCSI 0 y 1, verdes en #241) cubren recepción doble de órdenes, devoluciones simultáneas (R-06, R-15), devolución vs cancelación, cobros vs cancelación, cheques, aperturas de caja, último SuperAdmin y reversión de precios; faltan cambios de permisos concurrentes.
7. Índices únicos filtrados de apertura de caja: analizar apertura vencida y cierres entre días antes de decidir (migración).
8. Menores abiertos: H-23. Fiscales (§4-E) e infraestructura (§4-F): fuera del cierre funcional.

### Anexo — sondeo de permisos por acción (rol Vendedor, clon f3)

POST con token antifalsificación válido hechos por un usuario Vendedor: `PrefijosNumeracion/Guardar`, `ConfiguracionRentabilidad`, `ServiciosVenta/Guardar`, `Caja/AcreditarCheque`, `Caja/RechazarCheque`, `Mora/EjecutarJob`, `Mora/CrearAcuerdo`, `Mora/CambiarEstadoAcuerdo`, `Ticket/Eliminar`, `Usuarios/Create`, `DocumentosConfig/PlantillaActivar`, `ConfiguracionCliente`, `ConfiguracionPago/CrearPlanGlobal`, `PlantillaContratoCredito/ToggleActiva`, `Catalogo/AplicarCambioPrecioDirecto`, `Venta/Cancelar`, `Cliente/Delete`: **los 17 devolvieron 302 a `/Identity/Account/AccessDenied`** y no cambiaron datos (prefijos y umbrales de rentabilidad intactos en SQL). Limitación: solo se probó un rol; los demás roles y el POST con permiso parcial quedan sin verificar.

### Anexo — matriz de POST por rol (fase 4, clon f3, app :5193)

POST con token válido ejecutados por cada rol sobre 17 acciones sensibles. `DENEGADO` = 302 a *AccessDenied* (sin efecto); `302/200` = la acción se **ejecutó o devolvió su flujo normal** (permitida por el rol).

| Acción | Administrador | Gerente | Cajero | Contador | Repositor | Técnico |
|---|---|---|---|---|---|---|
| Rentabilidad / Prefijos / Servicios (config) | DENEGADO | DENEGADO | DENEGADO | DENEGADO | DENEGADO | DENEGADO |
| Config. de pagos (planes, eliminar medio), plantilla de contrato, DeleteRol | DENEGADO | DENEGADO | DENEGADO | DENEGADO | DENEGADO | DENEGADO |
| Acreditar / rechazar cheque | permitido | DENEGADO | DENEGADO | DENEGADO | DENEGADO | DENEGADO |
| Mora: ejecutar proceso, cambiar estado de acuerdo | permitido | permitido | DENEGADO | DENEGADO | DENEGADO | DENEGADO |
| Eliminar ticket, crear usuario, ajuste de precios (catálogo) | permitido | DENEGADO | DENEGADO | DENEGADO | DENEGADO | DENEGADO |
| Activar plantilla documental, cancelar venta, eliminar cliente | permitido | permitido | DENEGADO | DENEGADO | DENEGADO | DENEGADO |

Vendedor: ver el anexo anterior (17/17 denegadas). **Observación (decisión de negocio, no defecto probado):** Gerente puede eliminar clientes y cancelar ventas, y Administrador puede crear usuarios; coincide con la descripción de los roles («todos los permisos excepto configuración crítica»), pero conviene confirmar que es lo querido. No se probó el permiso parcial dentro de un mismo rol ni los GET.
