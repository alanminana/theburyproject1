# Cleanup: Migración AddCotizacionMotivoCancelacion

**Fecha:** 2026-05-16  
**Rama:** carlos/cleanup-cotizacion-migracion-motivo-cancelacion  
**Agente:** Carlos

---

## A. Diagnóstico

### Situación inicial

Existía el archivo `Migrations/20260516000000_AddCotizacionMotivoCancelacion.cs` sin su correspondiente `.Designer.cs`. La suposición inicial era que el campo ya estaba en la DB, pero el diagnóstico reveló una inconsistencia real.

### Hallazgos concretos

| Aspecto | Estado inicial |
|---|---|
| Entidad `Cotizacion.MotivoCancelacion` | ✓ Presente |
| `AppDbContextModelSnapshot` para Cotizacion | ✗ **Ausente** |
| `AddCotizacionOrigenToVenta.Designer.cs` snapshot de Cotizacion | ✗ Ausente (correctamente) |
| `20260516000000_AddCotizacionMotivoCancelacion.cs` | ✓ Presente pero sin Designer.cs |
| `__EFMigrationsHistory` | ✗ **Migración NO registrada** |
| Columna en DB (`Cotizaciones.MotivoCancelacion`) | ✗ **Columna NO existía** |
| `dotnet ef migrations list` mostraba la migración | ✗ **NO aparecía** |

**Causa raíz**: la ausencia del Designer.cs impedía que EF reconociera la migración. Sin el atributo `[Migration("...")]` que provee el Designer.cs, EF no incluye la clase en su cadena de migraciones. La migración nunca fue aplicada a la DB y la columna no existía.

**Nota sobre el snapshot**: las 3 ocurrencias de `MotivoCancelacion` en `AddCotizacionOrigenToVenta.Designer.cs` pertenecen a las entidades `AcuerdoPago`, `PriceChangeBatch` y `Venta` (no a `Cotizacion`). El snapshot de `Cotizacion` en ese Designer.cs estaba correctamente sin la columna.

---

## B. Estado en `migrations list`

```
20260515194116_AddCotizaciones
20260515234236_AddCotizacionOrigenToVenta
```

La migración `20260516000000_AddCotizacionMotivoCancelacion` **no aparecía** antes del fix.

---

## C. Estado en snapshot

Antes del fix: `AppDbContextModelSnapshot` no tenía `MotivoCancelacion` en la entidad `Cotizacion` (lines 2519-2620).  
Después del fix: snapshot actualizado automáticamente por `dotnet ef migrations add`.

---

## D. Estado en DB

Antes del fix:
```sql
SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Cotizaciones' AND COLUMN_NAME = 'MotivoCancelacion'
-- 0 rows
```

Después del fix:
```
MotivoCancelacion | nvarchar | 500 | YES
```

`__EFMigrationsHistory` después del fix:
```
20260515194116_AddCotizaciones
20260515234236_AddCotizacionOrigenToVenta
20260516174350_AddCotizacionMotivoCancelacion  ← nueva
```

---

## E. Decisión tomada

**Opción ejecutada: Eliminar archivo huérfano y regenerar con `dotnet ef migrations add`**

Justificación:
- El archivo original nunca fue aplicado (no estaba en `__EFMigrationsHistory`)
- No existía Designer.cs; EF no reconocía la migración
- El snapshot tampoco tenía la columna en Cotizacion
- Eliminar y regenerar no rompe historial de migraciones aplicadas
- El nuevo timestamp `20260516174350` mantiene la secuencia cronológica correcta después de `20260515234236_AddCotizacionOrigenToVenta`
- El `Up()`/`Down()` generado es idéntico al del archivo original

**Nota de build**: `dotnet build` (Debug) falla con error en `CotizacionPdfService.cs:355` (`IContainer.BorderStyle` — API incompatible de QuestPDF). Este error es pre-existente, parte de WIP de integración PDF, y no está relacionado con esta tarea. Se usan flags `--configuration Release` para los comandos EF.

---

## F. Cambios aplicados

Archivos modificados:
- **Eliminado**: `Migrations/20260516000000_AddCotizacionMotivoCancelacion.cs` (archivo huérfano sin Designer.cs)
- **Creado**: `Migrations/20260516174350_AddCotizacionMotivoCancelacion.cs` (migración regenerada con EF)
- **Creado**: `Migrations/20260516174350_AddCotizacionMotivoCancelacion.Designer.cs` (Designer.cs generado correctamente)
- **Modificado**: `Migrations/AppDbContextModelSnapshot.cs` (snapshot actualizado con `MotivoCancelacion` en `Cotizacion`)

Migración generada:
```csharp
// Up()
migrationBuilder.AddColumn<string>(
    name: "MotivoCancelacion",
    table: "Cotizaciones",
    type: "nvarchar(500)",
    maxLength: 500,
    nullable: true);

// Down()
migrationBuilder.DropColumn(name: "MotivoCancelacion", table: "Cotizaciones");
```

---

## G. Validaciones

| Validación | Resultado |
|---|---|
| `dotnet build --configuration Release` | ✓ 0 errores, 0 advertencias |
| `dotnet ef migrations list --configuration Release` | ✓ `20260516174350_AddCotizacionMotivoCancelacion` aparece |
| `dotnet ef database update --configuration Release` | ✓ `Applying migration '20260516174350_AddCotizacionMotivoCancelacion'. Done.` |
| Columna en DB (`INFORMATION_SCHEMA.COLUMNS`) | ✓ `nvarchar(500)`, nullable |
| `__EFMigrationsHistory` | ✓ Migración registrada |
| `dotnet test --filter "Cotizacion"` | 161/162 ✓, 1 pre-existente fuera de scope |
| `git diff --check` | sin conflictos de espacios |

**Test fallido pre-existente**: `ImprimirView_ContieneDisclaimerDeCotizacion` — busca "Guardar como PDF" en la vista Imprimir. Corresponde al WIP de integración PDF (rama anterior, archivos: `CotizacionPdfService.cs`, `Imprimir_tw.cshtml`, `CotizacionControllerUiTests.cs`).

---

## H. Riesgo / Deuda final

**Riesgo**: Bajo.
- La migración está correctamente registrada y aplicada
- El timestamp cambió de `20260516000000` a `20260516174350` (no había ninguna referencia al ID antiguo en la DB)
- La cadena de migraciones está íntegra

**Deuda remanente identificada**:

1. **Debug build roto** (`CotizacionPdfService.cs` — `IContainer.BorderStyle` no existe en la versión actual de QuestPDF): WIP de integración PDF pendiente de completar. Bloquea `dotnet ef` sin `--configuration Release`. Separar en tarea específica.

2. **Test fallido pre-existente** (`ImprimirView_ContieneDisclaimerDeCotizacion`): el test valida texto "Guardar como PDF" que aún no está en la vista Imprimir. Parte del WIP de PDF.

3. **Archivos WIP sin commitear** en working tree:
   - `Controllers/CotizacionController.cs`
   - `Program.cs`
   - `Services/CotizacionPdfService.cs` (untracked)
   - `Services/Interfaces/ICotizacionPdfService.cs` (untracked)
   - `Views/Cotizacion/Detalles_tw.cshtml`
   - `Views/Cotizacion/Imprimir_tw.cshtml`
   - `TheBuryProyect.Tests/Unit/CotizacionControllerPdfTests.cs` (untracked)
   - `TheBuryProyect.Tests/Unit/CotizacionControllerUiTests.cs`

   Estos pertenecen al WIP de integración PDF de Cotizacion. Deben ser revisados y commiteados o descartados en la tarea correspondiente antes de mergear a main.

**Próximo paso recomendado**: cerrar el WIP de integración PDF de Cotizacion (CotizacionPdfService + QuestPDF + tests + vistas) en una tarea dedicada, resolviendo el error de `IContainer.BorderStyle`.
