# P-07 — Recorridos integrales de negocio en una instalación Docker descartable

Fecha: 2026-11-14. Base: `main` 80b9c64c (PR #314) + los dos arreglos de este PR. No se tocó `TheBuryProjectDb`, LocalDB, datos comerciales
reales, otras ramas ni otros worktrees; ninguna regla comercial ni fiscal se modificó.

**Resultado: 127/127 comprobaciones de negocio y 48/48 pantallas×viewport en verde sobre la pila corregida. Se hallaron y corrigieron 2 defectos
reales (D-01 cultura del contenedor, D-02 modal de devolución). Ningún bloqueo técnico para producción nace de este trabajo; los bloqueos reales
son de configuración inicial y del propietario (§6).**

## 1. Qué ya estaba cubierto y qué faltaba

| Evidencia previa | Qué prueba | Qué **no** prueba |
|---|---|---|
| #310 (host independence) | instalación, persistencia, 24 pantallas, recuperación tras caída | ninguna operación de negocio |
| `docs/auditoria-funcional-matriz.md` (fases 1-8, 95,2 % de acciones) | cada módulo por separado, contra SQL, en **LocalDB / Windows** | que el sistema se comporte igual **en el contenedor Linux** con SQL Server de contenedor; una cadena continua entre módulos desde base vacía |
| Suite xUnit / Playwright en CI | reglas por módulo sobre SQLite o stack de CI con datos sembrados | recorridos completos con conciliación independiente entre módulos |

Recorridos integrales **no cubiertos** antes (los de este trabajo): base vacía → alta de datos maestros por la propia aplicación → compra y recepción →
stock → cotización → venta (contado, transferencia, nota de crédito, crédito personal) → factura → cobros → caja y su cierre → devoluciones →
reportes, todo contrastado con SQL y con **invariantes entre módulos** (stock = Σ movimientos, factura vigente única, capital de cuotas = monto aprobado,
cierre de caja = Σ movimientos, reporte de ventas = SQL), más la matriz por rol sobre instalación nueva y la ejecución en el contenedor (cultura, fuentes de
QuestPDF, health).

## 2. Entorno

Pila **propia y descartable** (`scripts/qa/p07/run-p07.sh`): proyecto Compose `buryp07<aleatorio>`, red/volúmenes/puertos/subred propios (127.0.0.1), secretos
aleatorios en un directorio temporal, SQL Server 2025 real en contenedor + la imagen de la aplicación construida desde este árbol (`docker-compose.yml` +
overlay `docker-compose.ci.yml`: `migrate` en Development siembra los 7 usuarios sintéticos y los roles). Base **vacía**: 0 clientes, productos, proveedores,
ventas; solo catálogo base de migraciones (2 categorías, 3 marcas, 4 alícuotas de IVA). Todo dato se da de alta **por la aplicación**; los datos son 100 %
sintéticos. Al terminar la pila y la imagen se destruyen (verificado: 0 contenedores/volúmenes/redes residuales; las pilas de otras sesiones siguieron intactas).

Cómo repetirlo: `bash scripts/qa/p07/run-p07.sh [--image TAG] [--keep] [--no-ui] [--out DIR]` (Git Bash o WSL; requiere docker, python3, openssl; la smoke de UI usa Chrome
del sistema + `playwright-core`, `P07_NODE_MODULES`).

## 3. Recorridos ejecutados (127 comprobaciones; `scripts/qa/p07/p07_recorridos.py`)

| Recorrido | Qué se hizo (interfaz/HTTP real de formularios + SQL) | Chequeos |
|---|---|---:|
| R0 Salud y presentación | readiness, Dashboard, moneda/fecha es-AR | 3 |
| R1 Alta de cliente | vacío, email inválido y DNI duplicado rechazados; alta válida; edición; Details | 8 |
| R2 Compras | proveedor (CUIT inválido/duplicado), producto (vacío/duplicado; costo +50 % +IVA = 181.500), orden de compra (sin proveedor rechazada; estados solo hacia adelante; IVA 210.000 sobre 1.210.000), recepción parcial 6/10, **sobre-recepción rechazada**, recepción total, Kardex = movimientos | 16 |
| R3 Stock | salida mayor al stock rechazada sin cambios; salida y entrada manuales; Kardex | 4 |
| R4 Caja | alta, no se vende con caja cerrada, apertura 50.000, no se reabre | 4 |
| R5 Cotización → venta | simulación, guardado, preflight, conversión+confirmación+factura en un paso, venta/stock/caja/factura contra SQL, **conversión duplicada rechazada**, una venta por cotización | 11 |
| R6 Venta directa | tope de venta y presupuesto sin stock, confirmación, factura, comprobante, montos es-AR, **cancelar sin motivo no cancela; con motivo: stock, factura anulada y reversión de caja** | 9 |
| R7 Devoluciones | cantidad excedida rechazada, nota de crédito (sin egreso de caja), **canje de la NC como medio de pago y su restitución al cancelar**, reembolso con egreso en caja, stock, venta devuelta por completo | 10 |
| R8 Crédito personal | configuración (solo SuperAdmin), instalación nueva sin crédito disponible, rechazo por documentación, carga y verificación de 3 documentos, conversión, plan 6 cuotas (36.300; total 217.800; capital 181.500; vencimientos mensuales), contrato, confirmación, factura por el total de la venta, **cobro parcial/total de cuota con caja y saldo de capital** (164.833,33 → 151.250) | 23 |
| R9 Cierre de caja | cierre sin justificación rechazado; cierre con el efectivo esperado calculado por SQL independiente (267.800) = el del sistema, diferencia 0 | 5 |
| R10 Reportes y conciliación | reporte de ventas (brutas 544.500, devoluciones 363.000, netas 181.500) = SQL; 7 exportes (Excel/PDF; QuestPDF con las fuentes del contenedor); invariantes entre módulos | 18 |
| R11 Permisos y errores | matriz de 6 roles × 12 pantallas; 5 POST de roles sin permiso sin efecto en SQL; sin sesión; 404/redirección controlada; logs sin excepciones no controladas | 16 |

Interfaz: `p07-ui-smoke.mjs` (Chrome, 1366×768 y 390×844) abre 24 pantallas con los datos de los recorridos: **48/48 sin errores de consola, sin respuestas ≥ 400,
sin desborde horizontal, sin «¤» ni formato de miles invariante**; revisión visual de capturas (venta cancelada con NC, detalle de caja en móvil: «Caja esperada
$ 267.800,00 / Diferencia exacto $ 0,00», coherente con SQL).

## 4. Defectos reales

| Id | Sev. | Defecto | Causa | Corrección | Regresión |
|---|---|---|---|---|---|
| **D-01** | **Alta** | En Docker **todos los importes salían como «¤181,500.00»** (pantallas, JSON, PDF, exportes) y las fechas en formato invariante | La app dependía de la cultura del **SO** (en Windows es es-AR); el contenedor Linux no tiene cultura y .NET usa la invariante. #310 y la auditoría (LocalDB/Windows) no podían verlo | `Program.cs`: cultura **es-AR fija** (`DefaultThreadCurrentCulture` + `UseRequestLocalization` sin proveedores de navegador) | R0.3/R6.5b en el recorrido Docker; 2 pruebas xUnit (importes es-AR y que `Accept-Language: en-US` no cambie el formato; fallan en Linux sin el arreglo) |
| **D-02** | Media | El modal de devolución rápida ofrecía siempre lo vendido: tras devolver todo seguía dejando elegir unidades que el servidor rechaza («Vendido: 2, Ya devuelto: 2, Disponible: 0») | `GetQuickCreateContext` devolvía `detalle.Cantidad` sin descontar lo ya devuelto (hermano de H-31, que solo corrigió el servidor) | `IDevolucionService.ObtenerCantidadesDevueltasPorProductoAsync` (mismo criterio que la validación: cuentan pendiente/aprobada/completada; no rechazada/cancelada); el contexto descuenta y `puedeDevolver` pasa a falso si no queda nada | 2 pruebas xUnit HTTP (verificadas: **fallan sin el arreglo**, pasan con él) y R7.10 |

## 5. Observaciones que **no** son defectos (no se modificó nada)

- **Datos faltantes / configuración inicial** (instalación nueva): crédito personal no disponible hasta cargar tasa, planes y límites por puntaje (`configuracion.update`, hoy solo SuperAdmin; el rol Administrador recibe AccessDenied, por diseño); **sin reglas documentales no se emite recibo ni se genera documento** (`docs/sistema-documental.md`: el motor no se siembra; se configura en Configuración → Documentos); sin documentos verificados y CUIL el cliente no accede a crédito (regla vigente, mensaje claro); MercadoPago sin mapear (advertencia).
- Reglas confirmadas funcionando: tope de venta $20.000.000; `SaldoPendiente` del crédito = capital pendiente (no total a pagar); crédito «Generado» cuenta como vigente; la transferencia entra a caja como pendiente de acreditación; la factura de una venta a crédito es por el total de la venta.
- Rechazos de negocio provocados a propósito (código duplicado, stock insuficiente, tope) se registran con nivel `fail:` y stack trace: ruido para monitoreo por logs (P-08 hoy no lo usa); candidato a bajar a Warning, no urgente.
- Reporte de ventas: el KPI «Costo» es bruto de devoluciones mientras «Ganancia neta» es neta (rótulo ambiguo; las cifras son correctas).
- El rol Gerente no accede a Caja ni a Catálogo; Contador y Gerente ven Ventas/Compras/Crédito/Reportes: confirmar con el propietario que es lo deseado.
- La pantalla de resultado del cobro de cuota con «Generar pendientes» solo existe en la primera carga posterior al pago (flash): comportamiento actual, no probado en otros flujos.

## 6. Bloqueos reales para producción

1. **Desplegar este PR** (D-01): sin él, en Docker todos los importes salen con «¤».
2. **Configuración inicial por el propietario** (no es código): tasa/planes/límites de crédito personal; **sistema documental** (tipos, plantillas, reglas de recibo/contrato/pagaré); datos de empresa; medios de pago.
3. Lo ya listado en P-04..P-08: servidor definitivo, Tailscale/ACL reales, SFTP y custodia, receptor de alertas externo, decisión de CSP Enforce (`docs/p04-plataforma-matriz-automatizacion.md`).
4. **Fuera del alcance de P-07 y siguen diferidos**: facturación fiscal electrónica externa (`docs/facturacion-fiscal-externa.md`), BCRA exitoso con servicio real, Mercado Libre, cheques (medio inactivo), lotes de precios, job de mora y notificaciones.

## 7. Límites de esta evidencia

Un solo operador por vez (la concurrencia ya tiene pruebas SQL Server propias); un cliente, un producto y una caja; no se probaron cambios de precio, envíos/armados, tarjetas con cuotas ni acuerdos de mora; la prueba de roles es de acceso y de POST sin efecto, no de cada acción de cada rol (eso lo cubre la matriz de la auditoría). «Verde» significa lo ejercitado, no que el sistema esté libre de defectos.

## 8. Revisión independiente (Codex, solo lectura, nivel N1)

Un `adversarial-review` sobre el diff completo (foco: cultura global, lógica del contexto de devolución, aislamiento de los scripts QA). Veredicto *needs-attention*, 1 hallazgo **high**:
el recorrido aceptaba cualquier destino (`P07_BASE_URL`/`P07_DB_CONTAINER`) y podía escribir sobre una instalación real. **Confirmado y corregido**: `guardia_descartable()` corre antes de cualquier login o POST y
exige el proyecto Compose efímero `buryp07<hex>` (generado por `run-p07.sh`), contenedores `<proyecto>-db-1/-app-1` con esa etiqueta Compose, base `BuryP07`, URL = puerto publicado por esa app y base **vacía**;
verificado con destinos rechazados (puerto/contenedor ajenos, proyecto distinto) y con la corrida completa (127/127). Sin hallazgos sobre `Program.cs` ni la lógica de devolución.
Límite: el hash del worktree «antes» se tomó antes de un `git add -N` (que altera el estado), por lo que la comparación estricta antes/después no es concluyente; Codex corre en modo solo lectura.
