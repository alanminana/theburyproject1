# Matriz de aceptación del ERP — cierre del Hito 9

Fecha: 2026-10-30 · `main` `36d57726` · Base real `TheBuryProjectDb` en `20261029012048_H4E2NotaCreditoCanje`.

Estados: **Aprobado** (completo y probado) · **Aprobado con limitaciones** (funciona dentro de un alcance explícito y seguro) · **Bloqueado** (defecto o decisión indispensable pendiente) · **Diferido** (fuera del alcance inicial por decisión del propietario).

Evidencia = lo realmente ejecutado: tests con CI 14/14 sobre el HEAD de cada PR, recorridos en clones de la base real con contraste SQL, barridos dinámicos con roles. Nada se probó sobre datos reales.

| Módulo | Estado | Evidencia principal | Limitaciones / pendiente |
|---|---|---|---|
| Ventas y Cotizaciones | Aprobado con limitaciones | Recorrido cotización → venta → confirmar → facturar → caja con SQL; #165, #182, #203, #205 | Interés de financiación y recargo: tratamiento fiscal sin validar (D1). «Siguiente: Cliente» del cotizador abre el wizard vacío (decisión de flujo). Factura de venta a crédito personal: recorrido validado en clon (contrato con PDF → facturar cierra neto + IVA = total); el interés queda fuera de la factura y el tratamiento fiscal espera decisión (`docs/factura-credito-personal.md`). |
| Caja | Aprobado con limitaciones | QA Caja/cierre/arqueo, concurrencia en SQL Server, #197, #211 | Acreditar y rechazar cheques recibidos en la interfaz (#211). Un solo medio de pago por venta. |
| Crédito | Aprobado con limitaciones | Crédito personal en la pantalla única, #161, #166 | El job de vencimiento de borradores no corrió aún contra la base real (debe cancelar solo las ventas 1003 y 1006; plazo de 7 días provisorio). El interés del crédito queda fuera de la factura de venta (D1). |
| Mora y Acuerdos | Aprobado con limitaciones | Smoke en clon (crear, activar, cobrar, cumplir, incumplir, auditoría), acuerdos con pagos no efectivos y punitorio > 0, #169 | `DiasParaCumplirPromesa`: lectura conservadora (días de espera tras la fecha prometida); hoy NULL. Una sola instancia de la app. |
| Cheques | **Aprobado con limitaciones** (migración `H10ChequesRecibidos` aplicada a la base real el 2026-10-30, #211) | `docs/cheques-circuito-propuesta.md` §6, `docs/cheques-migracion-autorizacion.md`; D1-A y D2-A autorizadas, D3-D6 aplicadas; tests de servicio, autorización/IDOR/antiforgery y concurrencia en SQL Server (RCSI 0 y 1) con 3 revisiones independientes (Codex); QA visual 1280 y 390 sobre clon; migración probada Up/Down/Up en clon y aplicada con backup verificado, CHECKDB limpio y conteos idénticos | Cuota con cheque solo desde «Pagar cuota» individual; si el cheque de una cuota rebota, la cuota no se revierte (decisión D5 pendiente: revertir automático / prohibir cheques en cuotas). Cancelar una venta con cheque de caja ya cerrada ya está resuelto (B13: contrapartida auditada en la caja vigente, `docs/cheques-circuito-propuesta.md` §6.4; QA en SQL Server RCSI 0 y 1); con otros medios de pago y caja cerrada sigue rechazándose. Sin uso real todavía: probar con un cheque de prueba antes de operar. |
| Compras | Aprobado con limitaciones | QA OC completa, recepción parcial, concurrencia, #171, #202 | Costo del producto manual (no se actualiza al recibir). Cancelar una OC parcial conserva el stock (sin devolución al proveedor). Sin cuentas ni pagos a proveedor. |
| Stock | Aprobado | Stock = Σ movimientos para los 16 productos, Kardex sin cortes, recepción idempotente | — |
| Devoluciones | Aprobado con limitaciones | Flujo del modal real de punta a punta, #172, #176, #206 | Venta con crédito vigente: bloqueada. Devolución de venta pagada con cheque: D4 implementada en `hito10/cheques` (cheque Pendiente o Rechazado bloquea el reembolso en efectivo). |
| Notas de crédito | Aprobado con limitaciones | Canje como medio de pago, cancelación con restitución e idempotencia, #178, #179 | Canje solo total (parcial exige pagos múltiples por venta). |
| Clientes | Aprobado con limitaciones | Recorrido de pantallas, #162, #202 | Consulta BCRA con tope de 6 s (provisorio aceptado). |
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
2. **Cheques D5** — qué pasa con la cuota si su cheque rebota (hoy: no se revierte, queda marcada en caja).
3. **Factura de crédito personal C1–C4** (`docs/factura-credito-personal.md`).
4. **Efectivo con descuento del plan en venta directa** (#210): se desactiva en la configuración del plan si no se quiere.
