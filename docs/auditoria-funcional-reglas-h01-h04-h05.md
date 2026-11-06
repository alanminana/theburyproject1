# Reglas de negocio H-01 / H-04 / H-05 — implicaciones y estado

Auditoría funcional integral, tramo 2 (2026-11-05, `main` `6ad544d1`). Reglas propuestas por el propietario; este documento recoge lo verificado en el código antes de implementar.

## H-05 — una apertura activa por usuario y por caja · IMPLEMENTADA (sin migración)

**Estado previo (reproducido con concurrencia real, SQL Server, RCSI 0 y 1):**
- Un mismo usuario podía abrir varias cajas a la vez, en secuencia y en carrera (2 aperturas activas).
- Misma caja, dos aperturas simultáneas: **no** quedaban dos filas, porque ambas operaciones actualizan `Caja` y el `RowVersion` de la caja hacía fallar a la segunda, pero con un `DbUpdateConcurrencyException` crudo en vez de un rechazo de negocio. Es decir, H-11 como duplicado de datos **no se confirmó**; sí se confirmó el error técnico.

**Implementación:** `CajaService.AbrirCajaAsync` abre una transacción y toma dos locks de aplicación (`sp_getapplock`, dueño = transacción) en orden fijo: usuario y luego caja; recién después relee el estado y valida. Rechazos de negocio con mensaje: «La caja ya tiene una apertura activa» y «Ya tenés una caja abierta (…). Cerrala antes de abrir otra.».

**Decisiones de diseño a confirmar:**
- «Activa» = no cerrada **y del día comercial vigente**, igual que `ObtenerAperturaActivaParaUsuarioAsync`. Una apertura vencida (día anterior) no impide abrir la de hoy (el sistema ya admitía ese caso y hay una prueba que lo exige), pero la **caja** vencida sigue sin poder reabrirse hasta cerrarla (comportamiento previo).
- Sin índice único filtrado en la base: dar garantía a nivel de datos exige una migración (índices únicos filtrados por `CajaId` y por `UsuarioApertura` donde `Cerrada = 0 AND IsDeleted = 0`) **y** depurar antes cualquier fila duplicada existente. **No se hizo; requiere tu autorización.** Los locks ya cubren las rutas de la aplicación.
- Impacto en datos: la base real hoy no tiene aperturas abiertas (limpieza del 2026-11-05). Los E2E y sembradores que abrían varias cajas para un mismo usuario se ajustaron (los sembradores insertan directo y usan un usuario por caja).

**Pruebas:** `CajaAperturaUnicaSqlServerTests` (6 escenarios, SQL Server, RCSI 0 y 1, compuerta determinista; con el código anterior fallaban 3) más 3 pruebas de servicio. Paso nuevo en `fin-dev-sqlserver.yml`.

## H-04 — reembolso en efectivo con egreso obligatorio · IMPLEMENTADA (sin migración)

**Ya existía:** la comprobación de efectivo suficiente (`CajaService.RegistrarMovimientoDevolucionAsync`, mide el efectivo físico bajo el lock de la apertura) y el rechazo si no hay caja abierta.

**Cambios:**
- Crear devolución: con resolución «Reembolso de dinero» el egreso es **siempre** verdadero (el servidor ignora lo que mande el navegador). Una venta que no se cobró por caja (crédito personal, cuenta corriente, nota de crédito) no admite reembolso en efectivo: se rechaza con mensaje y se indica resolverla con nota de crédito.
- Completar devolución: el egreso se registra aunque la devolución se haya creado antes con la marca apagada (devoluciones pendientes heredadas).
- Nota de crédito y cambios: nunca generan egreso (prueba nueva).
- Modal: la casilla pasa a informativa («Egreso de caja», marcada y no editable en reembolso) y los textos diferencian claramente reembolso en efectivo vs. nota de crédito.

**Dependencias verificadas:** el contador «reembolsos pendientes de caja» del índice usa la misma marca (coherente para las nuevas). El CSV distingue «Reembolso por caja/sin caja»: las históricas «sin caja» siguen mostrándose así.

**Pendiente de decisión:** si una devolución de reembolso se completa con la caja del usuario sin efectivo suficiente, falla (comportamiento previo, correcto); no hay circuito de «reembolso diferido».

## H-01 — numeración `VTA-…` al confirmar con `COT-…` como referencia histórica · NO IMPLEMENTADA (requiere tu autorización)

**Hallazgos del análisis:**
- Hoy el número se genera una sola vez (`VentaService.cs:267/293`: la venta nace en estado Cotización → prefijo `COT`) y nunca se renumera. `VentaNumberGenerator` ya sabe generar el prefijo `VTA` con contador atómico por prefijo+período; el índice de `Ventas.Numero` es único.
- «Conservar el `COT-…` como referencia histórica» **necesita una columna nueva** (p. ej. `NumeroOrigen`) → **migración**. Sin migración solo cabría guardarlo en la auditoría/observaciones, que no es consultable de forma confiable.
- El número de la venta se copia como texto (sin FK) en: `MovimientoCaja.Referencia/Observaciones`, referencias de `MovimientoStock` (y se **resuelve de vuelta** por texto en `MovimientoStockReferenciaResolver`), notas de crédito (canje), contratos y pagarés (snapshot `{{Venta.Numero}}`), documentos generados, reportes, devoluciones y Mercado Libre. Renumerar **después** de haber emitido esos textos dejaría referencias huérfanas.
- Por eso la renumeración tiene que ocurrir **antes** de descontar stock y de registrar caja, dentro de la misma transacción de confirmación (`ConfirmarVentaAsync` / `ConfirmarVentaCreditoAsync` / confirmación desde cotización), y **antes** de generar el contrato de crédito. **A verificar:** en qué estado de la venta se genera el contrato/pagaré (el servicio toma `venta.Numero` como snapshot, `ContratoVentaCreditoService.cs:708/877`); si puede generarse antes de confirmar, ese documento quedaría con `COT-…` y la venta ya no coincidiría con él («no alterar documentos existentes» lo preserva).
- La conversión de cotización → venta ya genera `VTA-…` directamente (`CotizacionConversionService.cs:529`): el cambio unifica ambos caminos.
- Las facturas tienen su propia numeración (`FA-B-…`), no se tocan.

**Propuesta:** (1) migración con `Ventas.NumeroOrigen` nullable; (2) renumerar en el punto único de confirmación; (3) decidir qué pasa con las ventas confirmadas existentes (base real sin ventas: nada que migrar); (4) definir el orden respecto del contrato de crédito (punto «a verificar»). **Pido autorización para la migración y para el punto (4) antes de implementar (primero verifico el orden real de generación del contrato).**
