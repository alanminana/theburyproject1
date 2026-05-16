# Fase Cotización V1.13 — PDF real descargable

**Agente:** Carlos
**Fecha:** 2026-05-16
**Rama:** main (integrado)

---

## A. Diagnóstico

| Punto | Resultado |
|-------|-----------|
| Librería PDF instalada | **QuestPDF v2026.2.3** — presente en `TheBuryProyect.csproj` línea 58 |
| Servicio PDF existente | `ContratoVentaCreditoService` y `ReporteService` usan QuestPDF con `LicenseType.Community` |
| Patrón de descarga | `ReporteController.ExportarPdf()` → `File(bytes, "application/pdf", fileName)` |
| Vista Imprimir_tw.cshtml | Reutilizable como referencia de layout; no renderizable por QuestPDF directamente (QuestPDF usa fluent builder, no Razor) |
| Licenciamiento | Community Edition confirmada — sin costo para proyectos con ingresos < USD 1M/año |

---

## B. Decisión PDF

- **Implementar PDF real** con QuestPDF Community, misma infraestructura ya presente en el proyecto.
- Generación on-the-fly (sin persistencia a disco) — coherente con la naturaleza read-only de la cotización.
- **No se implementa** email ni WhatsApp en esta fase.

---

## C. Librería e infraestructura usada

- **QuestPDF v2026.2.3** (ya instalada, sin nueva dependencia)
- `QuestPDF.Settings.License = LicenseType.Community` — mismo patrón que `ContratoVentaCreditoService` y `ReporteService`
- PDF generado en memoria → `byte[]` → `FileContentResult`

---

## D. Cambios aplicados

### Archivos nuevos

| Archivo | Descripción |
|---------|-------------|
| `Services/Interfaces/ICotizacionPdfService.cs` | Interfaz: `byte[] Generar(CotizacionResultado cotizacion)` |
| `Services/CotizacionPdfService.cs` | Implementación QuestPDF — layout A4 portrait, espeja estructura de `Imprimir_tw.cshtml` |
| `TheBuryProyect.Tests/Unit/CotizacionControllerPdfTests.cs` | 8 tests unitarios de controller y vista |

### Archivos modificados

| Archivo | Cambio |
|---------|--------|
| `Controllers/CotizacionController.cs` | + parámetro `ICotizacionPdfService pdfService` en constructor; + acción `DescargarPdf(int id)` |
| `Program.cs` | + `AddSingleton<ICotizacionPdfService, CotizacionPdfService>()` |
| `Views/Cotizacion/Detalles_tw.cshtml` | + botón "Descargar PDF" con ícono `picture_as_pdf` junto al botón "Imprimir" |
| `Views/Cotizacion/Imprimir_tw.cshtml` | Disclaimer actualizado: reemplaza "Guardar como PDF" por referencia al botón real |
| `TheBuryProyect.Tests/Unit/CotizacionControllerUiTests.cs` | Constructor actualizado + `StubNullPdfService` + actualización de assert del disclaimer |

### Endpoint nuevo

```
GET /Cotizacion/DescargarPdf/{id}
Permiso: cotizaciones.view (heredado del controller)
Respuesta: application/pdf; filename="Cotizacion-{Numero}.pdf"
```

**Regla crítica cumplida:** el endpoint solo llama `_cotizacionService.ObtenerAsync()` y `_pdfService.Generar()`. No crea venta, no convierte, no modifica estado, no toca stock ni caja.

---

## E. Layout del PDF

Espeja `Imprimir_tw.cshtml`:

1. **Header** — "The Bury Project", título, estado con color, cuadro Cotización (Nro/Fecha/Vencimiento)
2. **Sección cliente / pago seleccionado** — dos columnas border
3. **Tabla de productos** — Producto, Código, Cantidad, Precio unit., Descuento (condicional), Subtotal
4. **Tabla ajuste de pago** — aparece solo si existe opción seleccionada con recargo/descuento/interés
5. **Totales** — Subtotal, Descuento total (si aplica), Total base, Total c/ plan (si difiere), **Total** (fondo negro)
6. **Observaciones** — aparece solo si existen
7. **Disclaimer** — texto centrado border
8. **Footer** — Página N de M

---

## F. Tests

Archivo: `TheBuryProyect.Tests/Unit/CotizacionControllerPdfTests.cs`

| Test | Tipo | Resultado |
|------|------|-----------|
| `DescargarPdf_RequierePermisosCotizacionesView` | Seguridad | ✅ |
| `DescargarPdf_CotizacionExistente_RetornaFilePdf` | Happy path | ✅ |
| `DescargarPdf_CotizacionExistente_NombreArchivoContieneNumero` | Happy path | ✅ |
| `DescargarPdf_CotizacionInexistente_RetornaNotFound` | Edge case | ✅ |
| `DescargarPdf_NoInvocaConversionNiCancelacion` | Read-only | ✅ |
| `DescargarPdf_NoModificaEstado_SoloLlamaObtener` | Read-only spy | ✅ |
| `Controller_ConPdfService_NoDependeDeVentaCajaStock` | Arquitectura | ✅ |
| `DetallesView_ContieneEnlaceDescargarPdf` | Vista | ✅ |
| `ImprimirView_DisclaimerMencionaDescargarPdf` | Vista | ✅ |

**Totales fase:** 162 tests cotización / 421 tests filtro amplio — 0 errores.

---

## G. Riesgos y deuda

| Ítem | Nivel | Notas |
|------|-------|-------|
| Moneda sin localización explícita | Bajo | `.ToString("C2")` usa la cultura del proceso. En producción usar `CultureInfo` específica si es necesario. |
| Sin caché de PDF generado | Bajo | On-the-fly es correcto para read-only. Si se requiere cache futura, agregar `ICotizacionPdfCache`. |
| Fuente Arial no garantizada en Linux | Bajo | QuestPDF usa fuentes del sistema. En Docker/Linux evaluar embeber fuente o usar `Fonts.NotoSans`. |
| `CotizacionPdfService` registrado como `Singleton` | — | Correcto: sin estado, thread-safe, instancia única. |

---

## H. Próximos pasos

- **Email:** requiere evaluación de servicio SMTP/SendGrid. No implementar sin infraestructura decidida.
- **WhatsApp:** requiere API externa (Twilio, Meta Business API). No implementar sin decisión de integración.
- **Mejora potencial:** agregar logo de empresa al header del PDF si se dispone de imagen en `wwwroot`.
- **Test de integración del servicio PDF:** se puede agregar un test que genere un PDF real con QuestPDF y verifique que el resultado comienza con `%PDF`.
