# Fase Cotización V1.12 — Impresión / descarga de cotización

**Rama:** `carlos/cotizacion-v1-contratos`
**Agente:** Carlos
**Fecha:** 2026-05-16

---

## A. Objetivo

Implementar una salida imprimible de la cotización que permita entregarla al cliente.

---

## B. Diagnóstico previo

| Ítem | Resultado |
|---|---|
| Vistas imprimibles existentes | SÍ — `Views/Venta/ComprobanteFactura_tw.cshtml` con `Layout = null` + `window.print()` |
| PDF real | NO — sin librería instalada |
| SMTP/email | NO — sin servicio real |
| Descarga real | NO — patrón del proyecto es HTML + navegador |
| `CotizacionResultado` | Completo: número, fecha, vencimiento, cliente, detalles, opciones pago |
| Datos faltantes | Ninguno — `ObtenerAsync` carga todo vía `Include` |
| Permiso requerido | `cotizaciones.view` (heredado a nivel de clase) |

### Clasificación de componentes

| Componente | Clasificación | Evidencia | Decisión |
|---|---|---|---|
| `CotizacionController` | canónico | único controller de cotizaciones, registrado en DI | agregar acción `Imprimir` |
| `CotizacionService` / `ICotizacionService` | canónico | `ObtenerAsync` ya carga detalles y opciones de pago | reutilizar sin cambios |
| `Detalles_tw.cshtml` | canónico | vista activa del detalle de cotización | agregar botón |
| `Imprimir_tw.cshtml` | nuevo canónico | nueva vista sin Layout, siguiendo patrón de `ComprobanteFactura_tw.cshtml` | crear |
| `ComprobanteFactura_tw.cshtml` | canónico existente | patrón de referencia para vistas imprimibles | referenciar, no modificar |
| PDF/email infra | inexistente | no hay librería PDF ni servicio SMTP real en el proyecto | documentar como backlog |

---

## C. Decisión técnica

**Opción A — HTML imprimible con `window.print()`**

- Sin librerías nuevas.
- Sin riesgo.
- Suficiente para entregar al cliente.
- El PDF se obtiene desde el diálogo de impresión del navegador: **Guardar como PDF**.
- Sigue el patrón canónico ya establecido por `ComprobanteFactura_tw.cshtml`.

---

## D. Ruta / vista imprimible

- **Acción:** `GET /Cotizacion/Imprimir/{id}`
- **Vista:** `Views/Cotizacion/Imprimir_tw.cshtml`
- **Permiso:** `cotizaciones.view` (heredado del atributo de clase)
- **Comportamiento:** read-only, no modifica estado, no llama conversión, no llama venta.

---

## E. Datos incluidos en la vista

- Número de cotización
- Fecha de emisión
- Fecha de vencimiento (si existe)
- Estado (badge visual)
- Nombre del cliente (vinculado o libre)
- Teléfono del cliente libre (si existe)
- Pago seleccionado: medio, plan, cuotas, valor cuota
- Tabla de productos: nombre, código, cantidad, precio unitario, descuento (si aplica), subtotal
- Ajuste por plan de pago (si la opción seleccionada tiene recargo/descuento/interés)
- Totales: subtotal, descuento total, total base, total con plan (si difiere)
- Observaciones (si existen)
- Disclaimer: "Cotización sujeta a disponibilidad y vigencia de precios."

---

## F. PDF / descarga vía navegador

No se implementó generación de PDF con librería (no existe infraestructura en el proyecto).

El usuario puede obtener el PDF desde el diálogo de impresión del navegador seleccionando **"Guardar como PDF"** como destino.

El disclaimer en la vista lo indica explícitamente.

### Backlog sugerido

Si en el futuro se requiere PDF real o envío por email/WhatsApp, evaluar:
- PDF: `DinkToPdf`, `QuestPDF` u otra librería compatible con .NET 8.
- Email: implementar servicio `IEmailService` + SMTP configurado.
- WhatsApp: evaluar integración con API de terceros (Twilio, etc.).

---

## G. Tests agregados

Archivo: `TheBuryProyect.Tests/Unit/CotizacionControllerUiTests.cs`

| Test | Cobertura |
|---|---|
| `Imprimir_CotizacionExistente_DevuelveVistaImprimir_tw` | Acción devuelve vista correcta con modelo |
| `Imprimir_CotizacionInexistente_DevuelveNotFound` | Acción devuelve 404 si no existe |
| `ImprimirView_ContieneBotonImprimirYWindowPrint` | Vista tiene `window.print()` y botón Imprimir |
| `ImprimirView_TieneLayoutNullYNoDependeDeLayout` | Vista independiente del layout del ERP |
| `ImprimirView_NoContieneBotonesOperativos` | Vista no tiene botones de conversión/cancelación |
| `ImprimirView_ContieneDisclaimerDeCotizacion` | Vista incluye advertencia de vigencia + instrucción PDF |
| `ImprimirView_UsaModeloCotizacionResultado` | Vista usa modelo correcto con datos esperados |
| `DetallesView_ContieneEnlaceImprimir` | Botón en detalle apunta a la acción correcta con `target="_blank"` |

---

## H. Validaciones

- Build Release: **0 errores, 0 advertencias**
- Tests Cotizacion: **153/153** (+ 8 nuevos respecto a V1.11)
- Suite completa: en ejecución (base: 2860 en V1.11)
- `git diff --check`: OK
- Sin migración
- Sin cambio en entidades, servicios ni módulos ajenos

---

## I. Qué NO se tocó

- `VentaService`, `VentaController`, `VentaApiController`
- `Caja`, `Factura`, `Stock`, `ProductoUnidad`
- `CotizacionConversionService`
- Background service de vencimiento
- `Program.cs`
- Migraciones
- Módulos Juan / Kira

La impresión de cotización **no crea venta, no descuenta stock, no registra caja, no factura, no convierte cotización, no modifica estado**.

---

## J. Riesgos / deuda

| Ítem | Severidad | Nota |
|---|---|---|
| PDF real no implementado | Bajo | El navegador permite guardar como PDF; es suficiente para V1.12 |
| Email/WhatsApp no implementado | Bajo | No existe infraestructura; documentado como backlog |
| Vista no cubre multimoneda | Bajo | El proyecto no tiene multimoneda actualmente |

---

## K. Checklist

### Carlos — Bloque Cotización V1.x

- [x] V1.6 — Permiso granular `cotizaciones.convert`
- [x] V1.7 — Tests integración seguridad conversión
- [x] V1.8 — Preview tabla cambios unitarios
- [x] V1.9 — Numeración robusta
- [x] V1.10 — Cancelación compleja
- [x] V1.11 — Vencimiento automático
- [x] **V1.12 — Impresión / descarga** ← esta fase

**Bloque Cotización V1.x: CERRADO.**

### Juan — Sin pendientes activos.
### Kira — Sin pendientes activos.
