# Auditoría funcional integral — matriz de aceptación con evidencia (tramo 2, 2026-11-05)

Base: `main` `6ad544d1` + PR #237. Complementa `docs/auditoria-funcional-2026-11-05.md` (tramo 1) y `docs/auditoria-funcional-reglas-h01-h04-h05.md`.
**La auditoría NO está cerrada**: la cobertura medida con evidencia es del **13,5 %** de las funcionalidades de usuario. Todo lo demás es *no verificado*, no aprobado.

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
| Ventas y Cotizaciones | 27 | 3 | 4 | 0 | 0 | 20 | 26 % |
| Caja | 11 | 5 | 1 | 0 | 0 | 5 | 55 % |
| Crédito | 25 | 0 | 9 | 0 | 0 | 16 | 36 % |
| Mora y Acuerdos | 21 | 0 | 0 | 0 | 0 | 21 | 0 % |
| Cheques | 5 | 0 | 0 | 0 | 0 | 5 | 0 % |
| Compras | 16 | 5 | 0 | 0 | 0 | 11 | 31 % |
| Stock | 62 | 0 | 1 | 0 | 12 | 49 | 2 % |
| Devoluciones | 12 | 4 | 0 | 0 | 0 | 8 | 33 % |
| Notas de crédito | 1 | 0 | 0 | 0 | 0 | 1 | 0 % |
| Clientes | 27 | 0 | 5 | 0 | 0 | 22 | 19 % |
| Usuarios y Roles | 21 | 1 | 0 | 0 | 0 | 20 | 5 % |
| Documentos | 29 | 0 | 1 | 0 | 0 | 28 | 3 % |
| Reportes | 14 | 1 | 0 | 0 | 0 | 13 | 7 % |
| Dashboard | 5 | 2 | 0 | 0 | 0 | 3 | 40 % |
| Notificaciones | 3 | 0 | 0 | 0 | 0 | 3 | 0 % |
| Tickets | 5 | 0 | 0 | 0 | 0 | 5 | 0 % |
| Configuración | 25 | 0 | 0 | 0 | 0 | 25 | 0 % |
| Seguridad | 1 | 0 | 0 | 0 | 0 | 1 | 0 % |
| **Total** | **310** | **21** | **21** | **0** | **12** | **256** | **13.5 %** |

Notas: *Stock* incluye Catálogo, Productos, Categorías, Marcas, Precios y Alertas. *Incompletas* = las 12 funcionalidades del módulo de cambios de precios por lotes (sin listas ni menú; diferido). *Notas de crédito* solo tiene 1 acción propia: su función real (generar al aprobar la devolución, canjear como medio de pago, restituir al cancelar) vive en Devoluciones y Ventas y se prueba en §3. *Cheques* y *Mora y Acuerdos* tienen evidencia **previa** documentada (`docs/cheques-circuito-propuesta.md` §7; Hitos 9 y 10) que **no se re-ejecutó** en esta auditoría, por eso figuran en 0.

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
| H-12 | Reportes | Baja | El «ticket» por cliente del Reporte de Ventas es bruto (`ReporteService.cs:1124`) mientras el ticket promedio global es neto de devoluciones | **Abierto** |
| H-10 | Usuarios | Baja | El mensaje del VM dice «entre 6 y 100»; en Producción el mínimo real es 10 y el rechazo llega con el texto por defecto de Identity (en inglés) | **Abierto** (la política sí se cumple) |

**B. Funcionalidad incompleta**: módulo de cambios de precios por lotes (sin listas de precios, sin ABM de listas, sin menú; además `AplicarBatch` no escribe `Producto.PrecioVenta`); pestaña *Referencias* del cliente (oculta, sin modelo).

**C. No desarrollada (fuera del alcance confirmado)**: ABM de listas de precios; cuentas y pagos a proveedor; devolución a proveedor desde una OC parcial; reembolso diferido cuando la caja no tiene efectivo.

**D. Decisión de negocio**: **H-01** (renumerar `COT-…` → `VTA-…` al confirmar conservando `COT` como referencia: requiere migración y definir el orden con el contrato de crédito; **no implementado, espera autorización**); H-07 (el alta de OC ofrece «Cancelada» como estado inicial: permitido por diseño, COM-S-006, y evita el permiso de cancelar); índices únicos filtrados de apertura de caja (migración); método de costeo de compras; primera corrida del job de borradores; venta 1006; cliente 1 con monto máximo histórico.

**E. Pendiente fiscal (contador)**: tratamiento del interés y recargo (IVA D1), factura de crédito personal C1–C4, IVA sin netear devoluciones, costo con IVA, envío/armados/recargo en la ganancia.

**F. Infraestructura y pruebas**: copia externa, VPN/firewall, licencia de AutoMapper e instancia única de la app (todo diferido por el propietario); la suite E2E **consume su dataset** y exige clon restaurado, caja abierta y contraseñas de desarrollo (H-09); sin Docker en esta máquina.

## 5. Respuestas a las 10 preguntas

1. **¿Los 19 módulos funcionan?** Lo verificado funciona y no quedan defectos abiertos graves en lo probado, pero solo se verificó el 13,5 % de las funcionalidades: **no se puede afirmar que los 19 funcionen**.
2. **¿Completamente terminados?** Ninguno puede declararse así con esta evidencia. Los más avanzados: Caja (55 %), Devoluciones y Compras (~30 %, con sus circuitos principales completos).
3. **¿Incompletos?** Cambios de precios por lotes y Referencias del cliente (§4-B).
4. **¿Errores encontrados y corregidos?** H-02, H-03, H-04, H-05, H-06 y H-11 (PR #237, sin migraciones).
5. **¿Qué falta desarrollar?** §4-C (fuera del alcance confirmado). Nada de eso se implementó.
6. **¿Decisiones comerciales?** §4-D; la más urgente es **H-01** (requiere autorización por migración).
7. **¿Pruebas que faltan?** Unas 268 funcionalidades sin ejercitar. Circuitos pendientes: cotización → conversión → venta (hoy solo E2E), cliente → crédito → cuotas → cobro, mora → acuerdo → regularización, cheques (re-ejecutar), envío/armado → ganancia, cambio de precios directo y reversión, Tickets, Notificaciones, Documentos, Configuración, permisos por acción, móvil y concurrencia fuera de caja/OC.
8. **¿Porcentaje funcional verificable?** **13,5 %** (42 de 310). Denominador y criterio en §1.
9. **¿Cuánto desarrollo queda?** Desarrollo nuevo: ninguno confirmado dentro del alcance. Lo que queda es **verificación** (unas 268 funcionalidades) y las decisiones de §4-D/E.
10. **¿Se puede declarar finalizado el desarrollo funcional?** **No**, con la evidencia disponible.

## 6. Checklist de pendientes por prioridad

1. Autorizar o rechazar la migración de **H-01** y definir el orden con el contrato de crédito.
2. Fusionar #237 con CI verde y decidir los índices únicos filtrados de apertura de caja (migración).
3. Re-ejecutar y registrar con log: cotización → venta, crédito/cuotas/mora/acuerdos, cheques.
4. Probar permisos por acción (POST) por rol y los módulos con 0 %: Mora, Cheques, Notificaciones, Tickets, Configuración, Seguridad.
5. Móvil (390×844) de los circuitos principales y concurrencia de recepción, devolución y venta.
6. Inventario de botones y validaciones por pantalla (esta matriz es por acción).
7. Fiscales (§4-E) e infraestructura (§4-F): fuera del cierre funcional.
