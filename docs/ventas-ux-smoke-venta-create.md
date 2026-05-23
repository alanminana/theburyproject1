# Smoke Manual — Venta/Create (VENTAS-UX-SMOKE-1)

## A. Objetivo

Validar manualmente que el flujo completo de Venta/Create funciona correctamente
tras el cierre del rework visual/UX (VENTAS-UX-1A → VENTAS-UX-MAINT-1).

Verificar que lo visual y lo funcional básico conviven sin regresiones visibles.

Esta fase es QA / smoke / documentación únicamente.
No se implementaron cambios de código.

---

## B. Base y contexto

- HEAD al iniciar: `eb9b169` — Agregar labels accesibles en venta create (VENTAS-UX-MAINT-1)
- Rama de trabajo: `kira/ventas-ux-smoke-venta-create`
- Fases previas integradas: VENTAS-UX-1A, 1B, 1C, 1D, 1E-A, 1E-B, 1F, 1G, QA, MAINT-1

---

## C. Ambiente usado

- Entorno: `ASPNETCORE_ENVIRONMENT=Development`
- PID proceso app: 36732 (`dotnet run --project TheBuryProyect.csproj --configuration Release`)
- La app fue iniciada por esta tarea.

---

## D. Usuario usado

- Usuario: `Admin`
- Contraseña: `Admin123!`
- Rol: SuperAdmin

---

## E. URL usada

- Base: `http://localhost:5187`
- Login: `http://localhost:5187/Identity/Account/Login`
- Venta Create: `http://localhost:5187/Venta/Create`
- Venta Index (modal): `http://localhost:5187/Venta`
- Resultado confirmación: `http://localhost:5187/Venta/Details/6137`

---

## F. Datos de prueba usados

- Cliente: `doe, jhon — DNI: 54242156`
- Producto: `televisor samsung 40 pulgadas` (código: `tele-smart-sam`, $121,00 con IVA)
- Tipo de pago: `QA 8.8F Efectivo`
- Vendedor: `vendedor`
- Fecha: `2026-05-22` (pre-cargada)
- Venta creada: `VTA-202605-000021` (ID 6137)

---

## G. Resultado desktop

**OK — sin errores críticos.**

- Pantalla carga correctamente con título "Nueva Venta".
- Layout limpio, sin overflow visible.
- Sidebar colapsado funciona.
- Hero cards muestran estado inicial: Sin seleccionar / 0 productos / $0,00 / QA 8.8F Efectivo.
- Build previo: 0 errores, 0 advertencias.
- DevTools sin errores críticos JS en carga inicial.

---

## H. Resultado mobile

**OK — layout mobile funcional.**

- Viewport 390×844 sin overflow horizontal.
- Sidebar colapsa correctamente.
- Textos legibles.
- Sticky footer (`sticky-action-footer`) visible con total espejado y botón Confirmar compacto.
- Espaciador `h-20 md:hidden` presente (línea 891 Create_tw.cshtml) — evita que el sticky tape el contenido.
- `vm-mobile-summary-bar` en modal desde Index visible: muestra total `$0,00` y botón Confirmar.
- No se detectó overflow horizontal.

---

## I. Resultado cliente

**OK.**

- Campo `input-buscar-cliente` responde al tipeo.
- Dropdown aparece con resultados en ~1-2 seg.
- Resultado: `doe, jhon - DNI: 54242156 · 345252341`
- Selección actualiza el panel `info-cliente` correctamente.
- Nombre: `jhon doe`, DNI: `54242156`.
- Botón "Cambiar" visible y funcional.
- Hero card Cliente actualiza a datos del cliente seleccionado.
- No se rompió el layout al seleccionar.

---

## J. Resultado fecha

**OK.**

- Label: `Fecha de Operación` asociado semánticamente a `#FechaVenta`.
- Valor pre-cargado: `2026-05-22`.
- Campo editable y funcional.
- Label visible y legible.

---

## K. Resultado tipo de pago principal

**OK.**

- Label dice exactamente: `"Tipo de pago principal"` — correcto (VENTAS-UX-1A).
- Opciones disponibles: QA 8.8F Efectivo, QA 8.8F Transferencia, QA Mikasa Tarjeta Debito, QA 8.8F Tarjeta Credito, CreditoPersonal.
- Pre-seleccionado: `QA 8.8F Efectivo`.
- Texto de ayuda presente: `"Configuracion global activa cargada."`.
- No se confunde visualmente con `select-tipo-pago-item` (pago por producto).
- Para tipo Efectivo: no aparece sub-panel de cobro adicional — correcto por diseño (solo Tarjeta, Cheque y CreditoPersonal tienen sub-paneles).

---

## L. Resultado producto

**OK — flujo de dos pasos confirmado.**

- Campo `#input-buscar-producto` responde al tipeo.
- Dropdown aparece con resultados filtrados por texto.
- Formato de resultado: código, nombre, precio, stock (identificadas/sin identificar).
- Al seleccionar: el panel `#panel-agregar-producto` se completa con nombre y precio.
- Campos disponibles: Producto (readonly), Cantidad (default 1), Desc. % (default 0).
- Botón `add Añadir` agrega el producto a `#tbody-detalles`.
- Fila en tabla muestra: `tele-smart-sam | televisor samsung 40 pulgadas | 1 | $121,00 | 0% | $121,00`.
- Botón eliminar con nombre accesible correcto: `"Eliminar televisor samsung 40 pulgadas"` (VENTAS-UX-1E-A).
- Hero card Detalle actualiza a `1 productos`.

---

## M. Resultado totales

**OK.**

- `#total-subtotal`: `$100,00`
- `#total-iva`: `$21,00`
- `#total-final`: `$121,00` — prominente, texto 3xl font-black.
- Hidden inputs `#hdn-subtotal` y `#hdn-iva` con valores correctos.
- Sticky mobile total espejado: `$121,00` en `#sticky-create-total`.
- Hero card Total Estimado: `$121,00`.
- Cálculo: 100 neto + 21 IVA = 121 total. Correcto para producto con IVA estándar.

---

## N. Resultado alertas

**OK — correctamente ocultas para cliente sin alertas.**

- `#panel-alerta-mora`: oculto. Atributo `hidden` presente. No genera ruido.
- `#panel-cupo-insuficiente`: oculto. `role="alert"` presente pero inactivo.
- `#panel-documentacion-faltante`: oculto. `role="alert"` presente pero inactivo.
- Para cliente `doe, jhon` sin mora, cupo insuficiente ni documentación faltante, los paneles permanecen correctamente ocultos.
- No se probaron escenarios con alertas activas (fuera del alcance de este smoke).

---

## O. Resultado pre-confirmación

**OK.**

- Bloque `role="note"` con `aria-label="Revisá antes de confirmar"` presente (VENTAS-UX-1G).
- Ícono: `checklist`.
- Texto: `"Verificá cliente, tipo de pago y total. Si hay alertas activas de mora, cupo o documentación, revisalas antes de continuar."`.
- Visible antes del botón Confirmar.
- No genera ruido visual excesivo.
- No empuja demasiado el botón.
- Legible en desktop y mobile.

---

## P. Resultado confirmación

**OK — con observación (ver sección Q).**

**Primer intento (sin vendedor):**
- Se hizo clic en `#btn-confirmar` sin seleccionar vendedor.
- El backend devolvió error de validación: `"Debe seleccionar un vendedor."`.
- El formulario se re-renderizó completamente vacío — datos perdidos (ver observación).
- Error mostrado en alert rojo al tope del form: `"Corrige los errores antes de continuar / Debe seleccionar un vendedor."`.

**Segundo intento (con vendedor):**
- Se completó el flujo completo con vendedor seleccionado.
- `#btn-confirmar` procesó el submit.
- Redirección exitosa a: `http://localhost:5187/Venta/Details/6137`.
- Título resultante: `Venta VTA-202605-000021`.
- Estado de la venta: `PRESUPUESTO`.
- Datos visibles en details: Cliente `doe, jhon - DNI: 54242156`, Forma de pago: `Efectivo`, Vendedor: `vendedor`, Fecha: `22/05/2026`, Total: `$121,00`, 1 ítem, 1 unidad registrada.
- Sin factura emitida (estado PRESUPUESTO requiere segunda confirmación desde la pantalla de detalles).
- Acciones disponibles en details: Confirmar Venta, Editar Venta, Cancelar Venta.

---

## Q. Errores encontrados

### Q1 — Vendedor requerido sin indicación visual de obligatoriedad

**Tipo:** Observación UX — no bloqueante para flujo feliz.

**Descripción:**
El campo Vendedor es obligatorio a nivel server-side (validación de backend), pero no tiene:
- Asterisco `*` ni indicación visual de campo requerido.
- `data-val-required` en el HTML del select.
- Hint o placeholder que indique la obligatoriedad.

El usuario solo descubre que es obligatorio al intentar confirmar y recibir error del servidor.

**Archivo probable:** [Views/Venta/Create_tw.cshtml](../Views/Venta/Create_tw.cshtml) línea ~749, y ViewModel/Controller correspondiente.

**Riesgo:** Bajo-moderado. No bloquea el flujo si el usuario sabe que debe seleccionar vendedor.

**Propuesta:** Microfase VENTAS-UX-2 o VENTAS-MAINT-2 — agregar indicador visual de campo requerido al selector de vendedor.

---

### Q2 — Pérdida de datos al fallar validación server-side

**Tipo:** Observación UX — moderada.

**Descripción:**
Al fallar la validación del formulario (POST a /Venta/Create con error server-side), el servidor devuelve el formulario re-renderizado desde el modelo con estado inicial.

Se pierden:
- Cliente seleccionado (vuelve a "Sin seleccionar").
- Productos agregados (tabla vacía).
- Tipo de pago (vuelve al default).
- Fecha (se mantiene por ser input date con default).
- Observaciones.

**Causa probable:** El modelo de retorno no preserva los datos del detalle dinámico (detalles de producto se manejan via JS/hidden inputs, y si el binding falla parcialmente, el estado JS se pierde al recargar).

**Riesgo:** Moderado. Puede ser frustrante para operadores que cargaron ventas largas con muchos productos.

**Propuesta:** Microfase funcional específica (no visual) — evaluar si el Controller puede devolver el modelo con los detalles preservados, o implementar guardado local (localStorage) del estado del formulario antes del submit.

**Nota:** Esta es deuda funcional preexistente al rework UX, no introducida por él.

---

### Q3 — Estado post-creación es PRESUPUESTO, no CONFIRMADA

**Tipo:** Observación de flujo — no es un error, sino una característica del ambiente.

**Descripción:**
La venta creada con tipo de pago "QA 8.8F Efectivo" queda en estado `PRESUPUESTO`, requiriendo una segunda acción "Confirmar Venta" desde la pantalla de detalles.

Puede ser el flujo normal para este medio de pago en el ambiente QA, o una configuración específica.

**Riesgo:** Ninguno para el smoke. A documentar para el usuario final.

---

## R. Observaciones

- El flujo de búsqueda de cliente y producto funciona correctamente con el autocomplete AJAX.
- El dropdown de productos muestra precio, stock y categoría — suficientemente descriptivo.
- El panel `#panel-agregar-producto` actúa como paso intermedio de configuración antes de agregar a la tabla. Correcto.
- La tabla de detalle con `aria-label` en botón eliminar es correcta y accesible (VENTAS-UX-1E-A).
- El recordatorio pre-confirmación (VENTAS-UX-1G) está bien posicionado y no es intrusivo.
- El sticky footer mobile (VENTAS-UX-1F) funciona correctamente y espeja el total.
- La barra `vm-mobile-summary-bar` del modal también está correctamente presente en mobile.
- No se detectaron errores JS críticos en consola durante el flujo normal.
- El build está limpio: 0 errores, 0 advertencias.

---

## S. Decisión final

**Opción B — Aprobado con observaciones.**

El flujo Venta/Create funciona correctamente. El rework visual/UX convive sin regresiones funcionales con la lógica existente. Las observaciones encontradas son deuda funcional preexistente o UX no-bloqueante, no introducidas por el rework.

El flujo está listo para revisión funcional/manual del usuario, con las observaciones documentadas.

---

## T. Riesgos

- **Q1** (vendedor requerido sin indicador): Bajo-moderado. Operadores nuevos pueden frustrarse.
- **Q2** (pérdida de datos en validación): Moderado para ventas largas. Preexistente al rework.
- **Q3** (estado PRESUPUESTO): Informativo. Sin riesgo operativo en dev.
- No se probaron escenarios con alertas activas (mora, cupo, documentación).
- No se probó el path de Crédito Personal ni Tarjeta (solo Efectivo).
- No se probó la eliminación de producto desde la tabla.
- No se probó la edición de cantidad o descuento por item.

---

## U. Próximo paso recomendado

**Opción 1 (recomendada):** Cerrar Ventas UX como completada. Retomar Misa/Inventario pausada.

**Opción 2:** Abrir microfase VENTAS-UX-MAINT-2 para resolver Q1 (indicador visual de campo requerido en Vendedor). Cambio mínimo, bajo riesgo.

**Opción 3:** Abrir microfase funcional para Q2 (preservación de datos en validación server-side). Mayor alcance, involucra backend.

---

## V. Procesos iniciados/cerrados

- **Proceso iniciado por esta tarea:** `dotnet run` — PID 36732.
- Estado al cierre: proceso activo (app en ejecución).
- **Acción:** Cerrar al finalizar la tarea.

Procesos externos preexistentes (no tocar):
- VS Code (`Code.exe`)
- C# DevKit (PID 20980, 30060, 2868, 15300)
- Roslyn Language Server (PID 2972)
- MSBuild Language Server (PID 5484, 32732)

---

## Checklist de cierre

- [x] VENTAS-UX-SMOKE-1 creada desde main eb9b169.
- [x] No se modificó código productivo.
- [x] No se modificó Razor.
- [x] No se modificó CSS.
- [x] No se modificó JS.
- [x] No se modificó backend.
- [x] Se probó desktop.
- [x] Se probó mobile (390px).
- [x] Se probó cliente (búsqueda y selección).
- [x] Se probó fecha.
- [x] Se probó tipo de pago principal.
- [x] Se probó producto (búsqueda, selección, añadir, tabla).
- [x] Se revisaron totales.
- [x] Se revisaron alertas (ocultas, sin cliente con alertas activas).
- [x] Se revisó pre-confirmación.
- [x] Se documentó confirmación (éxito en segundo intento con vendedor).
- [x] Se clasificó resultado B.
- [x] Se creó documento de smoke.
- [x] No se ejecutaron tests innecesarios.
- [x] No se commiteó AGENTS.md.
- [x] No se commiteó CLAUDE.md.
- [x] No se commiteó .claude/settings.local.json.
- [x] No se commiteó skills-lock.json.
