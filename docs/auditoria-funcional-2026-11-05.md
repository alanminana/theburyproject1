# Auditoría funcional integral — avance 1 (2026-11-05)

Base: `main` `eb9c20dc` (PR #235). Worktree limpio `theburyproject1-audit-func`, rama `audit/funcional-integral-20261105`.

**Esto NO es el cierre de la auditoría.** Es el primer tramo ejecutado: entorno QA, inventario dinámico, circuitos de Compras → Stock → Venta → Devolución, Caja, Usuarios/permisos y una pasada completa de la suite E2E existente. Todo lo que no figura como *verificado* aquí queda **NO VERIFICADO**, no aprobado.

## 1. Entorno

| Elemento | Detalle |
|---|---|
| Base | Clon `COPY_ONLY` de la base real (ya limpia de datos operativos: 0 ventas/clientes/créditos, 16 productos, 11 usuarios, 135 migraciones) restaurado como `TheBuryProjectDb_qa_audit` (circuitos, :5199) y `TheBuryProjectDb_qa_audit_e2e` (suite E2E, :5198). La base real solo se leyó para el respaldo. |
| Datos | Sembradores E2E del CI (clientes, productos genéricos, escenarios de punitorio, descuento-permiso) + datos creados desde la interfaz. |
| Herramientas | Playwright (Chrome) operando la interfaz, contraste con SQL Server LocalDB. Sin Docker disponible en esta máquina. |
| Aislamiento | Ninguna escritura sobre `TheBuryProjectDb`. Job de borradores de crédito deshabilitado en los clones. |

## 2. Inventario dinámico

- **Rastreo como SuperAdmin**: 155 pantallas alcanzables por enlaces desde el menú; **0 errores HTTP ≥ 400, 0 páginas de error, 0 errores de consola** (se excluyen Google Fonts).
- **Código**: 521 acciones de controlador (444 sin Mercado Libre/Diagnóstico) en 46 controladores. El contraste con lo alcanzado dejó, como no alcanzadas por navegación, casi solo modales, endpoints JSON y detalles que exigen registros. Lo único con pantalla propia sin enlace en el menú es `CambiosPrecios` (ya documentado como diferido, D-4 del Hito 11).
- **Alcance del inventario**: es un inventario de *rutas*, no de funcionalidades. El inventario funcional por módulo (botones, formularios, masivas, automáticas, reportes) sigue pendiente (ver §6).

## 3. Funcionalidades verificadas en esta auditoría

Cada fila: operación hecha desde la interfaz (o POST forjado con el token real cuando el cliente clampa el valor) y verificada en SQL.

| Módulo | Prueba | Resultado |
|---|---|---|
| Proveedores | Obligatorios; CUIT con dígito verificador inválido; alta válida; CUIT duplicado | 4/4 correctos (persisten solo los válidos) |
| Compras | OC sin ítems rechazada; alta con 1 ítem (subtotal 1.000.000, IVA incluido 173.553,72 = ×21/121) | correcto |
| Compras → Stock | Recepción parcial 4/10 (stock 0→4, OC «Parcial»); resto 6 (4→10, OC «Recibida»); cliente clampa 7→6; **servidor**: POST con 99 sobre 5 pendientes y con −3 rechazados sin efectos; recepción válida 5/5 (10→15); el formulario desaparece en OC ya recibida | correcto; stock = Σ movimientos (0→4→10→15) con referencia a la OC |
| Caja | Apertura válida; **servidor**: monto −100, −0,01, `abc`, 99.999.999.999.999 rechazados con mensaje | correcto |
| Venta contado | Wizard (cliente, producto, Efectivo, vendedor) → crear (stock sin cambio) → confirmar y facturar: stock 15→14, cobro en caja 173.374,05 (182.499 − 5 % efectivo), factura B neto 143.284,34 + IVA 30.089,71 = total | correcto (×2 ventas) |
| Venta sin caja | «Nueva venta» sin caja abierta redirige a Ventas con aviso «Caja requerida» | correcto |
| Devoluciones | Sin ítem seleccionado: no se crea; reembolso sin caja: aprobar → completar (stock +1, sin egreso, por diseño); reembolso **con caja**: egreso 173.374,05 y stock +1 | correcto |
| Usuarios | Reset de contraseña: corta/confirmación distinta rechazadas; válida rota `SecurityStamp` y el usuario ingresa con la nueva | 4/4 |
| Permisos | Matriz 8 roles × 34 rutas (272 comprobaciones, GET sin seguir redirecciones) | sin escalada: cajero/contador/repositor/técnico acotados; Gerente sin Caja/Seguridad/Configuración; Vendedor sin Compras/Mora/Reportes. Resultado en §7 |
| Config. punitorios | Versionado append-only: rechaza una versión anterior a una existente | correcto (comportamiento deseado) |

## 4. Regresión E2E existente (1366×768, clon aislado, 223 specs)

| Corrida | Resultado | Causa de lo que falló |
|---|---|---|
| 1.ª (sin caja abierta para `admin`) | 148 ok · 12 omitidos · 63 fallos | Los 36 fallos `venta-*` son por prerrequisito: «Caja requerida» (admin sin caja abierta) |
| 2.ª (`--last-failed`, con caja abierta) | 55 ok · 9 fallos | 5 por contraseña real del usuario `vendedor` (no la de desarrollo): tras resetearla pasan |
| Detalle de los restantes | 1 defecto real (§5, H-03, corregido) · 5 de `configuracion-punitorio` + 2 de `credito-pago-cuota` + 2 de `credito-punitorio-detalle` por **dataset consumido** (versionado append-only; las corridas son no idempotentes) · 3 coinciden con los ya documentados como dependientes de la configuración de la base real (`venta-descuento-permiso` 5 % efectivo, `venta-excepcion-documental-reload`, `cotizacion-simulador` T9) | |

No se omitió ni se desactivó ningún spec. Lo no re-ejecutado en limpio (specs consumibles) queda **NO VERIFICADO** hasta restaurar el clon y correrlos de a uno.

## 5. Hallazgos

Clases: A defecto · B incompleta · C no desarrollada · D decisión de negocio · E fiscal · F infraestructura/pruebas.

| ID | Módulo | Clase | Crit. | Esperado → Observado | Evidencia | Estado |
|---|---|---|---|---|---|---|
| H-03 | Configuración / Crédito personal (punitorios) | A | Baja | Al abrir el modal de vigencia retroactiva, el foco queda atrapado dentro → jQuery Validate (`focusInvalid`) devuelve el foco a `#pun-vigente-desde` justo después; el Tab recorre la página bajo el modal | Traza de `focus()` mostró la llamada del modal y luego la de jQuery; spec `configuracion-punitorio.spec.js:118` fallaba 3/3 y pasa con la corrección | **Corregido** en esta rama (`CreditoPersonal_tw.cshtml`: reafirma el foco tras el ciclo de eventos). Cubierto por el spec existente |
| H-01 | Ventas | A/D | Media | Una venta confirmada y facturada debería llevar numeración de venta (`VTA-…`) → conserva `COT-202611-000001` porque el número se asigna al crearse como cotización y nunca se renumera. Aparece así en el movimiento de caja, la devolución y la factura | Ventas 2048 y 2049 | **Abierto**. Requiere decisión: renumerar al confirmar o aceptar que el prefijo es del borrador |
| H-02 | Devoluciones/Stock | A | Baja | Referencia del movimiento de stock `DEV-202611-000002` → `DEV-DEV-202611-000002` (prefijo duplicado) | `MovimientosStock` ref. de las devoluciones 2 y 3 | **Abierto** (cosmético, fix trivial; no aplicado aún) |
| H-04 | Devoluciones | D | Info | «Reembolso de dinero» con «Reflejar en caja» **desmarcado por defecto**: la devolución se completa sin egreso y la caja no refleja la salida del dinero | Dev. 2: `RegistrarEgresoCaja=0`, sin movimiento | Abierto: confirmar si el default debe ser marcado |
| H-05 | Caja | D | Info | Un mismo usuario puede tener **dos cajas abiertas a la vez** (admin: aperturas 1025 y 1026); las ventas cobran en la apertura más reciente | `AperturasCaja` | Abierto: confirmar si es política deseada |
| H-06 | Compras (UI) | A | Baja | En Details de OC a 1366×768 la primera columna de la tabla de recepción aparece recortada («…CTO», «…ZAFIRO80GN») | Capturas `oc3`/`oc4` | **Sin confirmar** (puede ser un scroll horizontal por foco en el input); a revisar con captura controlada |
| H-07 | Compras | A/D | Baja | El alta de OC ofrece estado inicial «Cancelada» y «Confirmada»; crear una OC ya cancelada no tiene sentido operativo | Selector `Estado` de `/OrdenCompra/Create` | **No verificado** (solo se creó «Confirmada») |
| H-08 | Cambios de precios por lotes | C | — | Módulo sin enlace en el menú, sin listas de precios ni ABM de listas | Rastreo + `docs/aceptacion-erp.md` Hito 11 D-4 | Diferido por el propietario |
| H-09 | Pruebas E2E | F | — | Specs consumen el dataset y exigen caja abierta y contraseñas de desarrollo; una segunda corrida sobre la misma base falla | §4 | Registrado; el CI corre en base efímera |
| H-10 | Usuarios | A | Info | El mensaje de política del reset dice «entre 6 y 100 caracteres»; en Producción el mínimo es 10 (#233) | Mensaje en Development | **No verificado** en Producción |

## 6. No verificado (pendiente de este plan)

Circuitos completos no ejecutados todavía en esta auditoría: Cotización → conversión → venta; Cliente → evaluación → crédito → cuotas → cobro; Crédito → mora → acuerdo → regularización; devolución → **nota de crédito** → canje; cheque (recepción/acreditación/rechazo); envío/armado → costo real → ganancia → reporte; arqueo y cierre de caja; cambio de precios directo del Catálogo + reversión; reportes contrastados con SQL; Tickets, Notificaciones, Documentos; concurrencia; viewport móvil; permisos por acción (no solo por pantalla); operaciones masivas; ABM de Productos/Categorías/Marcas. El inventario por funcionalidad (no por ruta) de los 19 módulos tampoco está hecho.

## 7. Matriz de acceso por rol (GET, sin seguir redirecciones)

`OK` acceso · `DENY` AccessDenied · `R:` redirección funcional · `?` sin dato.

| Ruta | admin | administr. | gerente | vendedor | cajero | contador | repositor | técnico |
|---|---|---|---|---|---|---|---|---|
| /Venta | OK | OK | OK | OK | OK | OK | DENY | DENY |
| /Venta/Create | OK | OK | OK | R:/Venta¹ | DENY | DENY | DENY | DENY |
| /Cotizacion | OK | OK | OK | OK | DENY | DENY | DENY | DENY |
| /Cliente | OK | OK | OK | OK | OK | OK | DENY | DENY |
| /Cliente/Create | OK | OK | OK | OK | DENY | DENY | DENY | DENY |
| /Catalogo | OK | OK | DENY | OK | DENY | DENY | OK | OK |
| /Proveedor | OK | OK | OK | DENY | DENY | OK | DENY | DENY |
| /OrdenCompra | OK | OK | OK | DENY | DENY | OK | DENY | DENY |
| /OrdenCompra/Create | OK | OK | OK | DENY | DENY | DENY | DENY | DENY |
| /MovimientoStock | OK | OK | OK | DENY | DENY | DENY | OK | DENY |
| /Caja | OK | OK | DENY | OK | OK | DENY | DENY | DENY |
| /Reporte | OK | OK | OK | DENY | DENY | OK | DENY | DENY |
| /Credito | OK | OK | OK | OK | OK | OK | DENY | DENY |
| /Mora | OK | OK | OK | DENY | OK | OK | DENY | DENY |
| /Devolucion | OK | OK | OK | DENY | DENY | DENY | OK | OK |
| /Seguridad | OK | OK | DENY | DENY | DENY | DENY | DENY | DENY |
| /ConfiguracionPago | R:ok | R:ok | DENY | DENY | DENY | DENY | DENY | DENY |
| /CambiosPrecios | OK | OK | OK | DENY | DENY | DENY | DENY | DENY |
| /AlertaStock | OK | OK | OK | DENY | DENY | DENY | OK | DENY |
| /MercadoLibre/Dashboard | OK | OK | DENY | DENY | DENY | DENY | DENY | DENY |
| /Diagnostico | OK | DENY | DENY | DENY | DENY | DENY | DENY | DENY |

¹ Redirige a Ventas porque `vendedor` no tiene caja abierta (el wizard exige caja). Matriz completa de 34 rutas y 8 roles en el log de la corrida; no se verificó el permiso por **acción** (POST) en esta entrega.

## 8. Cambio de código de esta entrega

Una sola modificación de producto: `Views/ConfiguracionPago/CreditoPersonal_tw.cshtml` (H-03). Verificación: spec `configuracion-punitorio.spec.js:118` pasa (falla sin el cambio); `dotnet test` filtrado `CreditoPersonal|Punitorio`: 685 correctas, 4 omitidas (SQL Server por variable de entorno), 0 fallos.
