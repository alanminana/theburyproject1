# Fase 10.19 — DocumentoCliente agrupado por cliente + modal de documentos

**Commit:** `850be58`
**Branch:** main
**Agente:** Juan

---

## A. Objetivo

Reorganizar la vista principal de DocumentoCliente para que el listado primario sea por cliente (no por documento individual). Cada cliente aparece como fila con resumen de estado. Al hacer click se abre un modal con todos sus documentos y las acciones disponibles.

---

## B. Diagnóstico previo

| Pregunta | Respuesta |
|---|---|
| Controller.Index hoy | Llama a `BuscarAsync(filtro)` → lista plana de `DocumentoClienteViewModel` → vista plana. Sin agrupación. |
| ViewModel en Index_tw | `DocumentoClienteFilterViewModel` con `List<DocumentoClienteViewModel> Documentos`. Plano. |
| Agrupación disponible | No. Todo plano. |
| Relación DocumentoCliente ↔ Cliente | `DocumentoCliente.ClienteId` (FK) + navigation `Cliente`. AutoMapper ya mapea `Cliente → ClienteResumenViewModel` (línea 148 AutoMapperProfile). `NumeroDocumento` y `Telefono` disponibles por convención. |
| Acciones por documento | Details, Descargar, Verificar (POST), Reemplazar (modal upload), Delete (POST). Rechazar: solo desde Details. |
| Permisos | `[Authorize]` + `[PermisoRequerido(Modulo = "clientes", Accion = "viewdocs")]`. |
| Filtros existentes | ClienteId, TipoDocumento, Estado, SoloPendientes, SoloVencidos. Paginación PageSize=10. |
| Tests existentes | `DocumentoClienteServiceTests.cs` — ~40 tests de integración del service. Sin tests de controller. |
| Requiere migración | No. |

---

## C. Clasificación de componentes

| Componente | Clasificación | Evidencia | Decisión |
|---|---|---|---|
| `DocumentoClienteController` | canónico | Único controller, integrado en DI, usado activamente | Modificar mínimamente solo `Index` |
| `Index_tw.cshtml` | canónico | Vista activa de producción | Modificar — nueva sección agrupada + existente flat |
| `DocumentoClienteFilterViewModel` | canónico | Modelo de filtrado activo | Extender con `ClientesAgrupados` |
| `DocumentoClienteViewModel` | canónico | ViewModel principal de documento | Solo lectura — sin cambios |
| `DocumentoCliente` entity | canónico | Entidad core | Solo lectura |
| `Cliente` entity | canónico | Entidad core | Solo lectura |
| Acciones Upload/Verificar/Delete/Descargar | canónicas | Usadas activamente, testeadas | Preservadas sin cambios |
| `DocumentoClienteClienteResumenViewModel` | nuevo | Creado en esta fase | Nuevo ViewModel de agrupación |

---

## D. Decisión de ViewModel

Se crea `DocumentoClienteClienteResumenViewModel.cs` (nuevo, no modifica ningún ViewModel existente):

```
int ClienteId
string ClienteNombre
string? DocumentoIdentidad       (de ClienteResumenViewModel.NumeroDocumento)
string? Telefono                 (de ClienteResumenViewModel.Telefono)
int TotalDocumentos
int Pendientes / Verificados / Rechazados / Vencidos
DateTime? UltimaActualizacion
List<DocumentoClienteViewModel> Documentos
string EstadoResumen             (computed: Con vencidos > Con rechazados > Pendiente > Completo > Sin estado)
string EstadoResumenClasses      (computed: clases Tailwind por estado)
static FromDocumentos(IEnumerable<DocumentoClienteViewModel>) → List<...>
```

`DocumentoClienteFilterViewModel` extendido con:
```
List<DocumentoClienteClienteResumenViewModel> ClientesAgrupados { get; set; } = new();
```

---

## E. Agrupación por cliente

La agrupación ocurre en el controller, después de obtener los documentos de `BuscarAsync`:

```csharp
if (!filtro.ClienteId.HasValue)
    filtro.ClientesAgrupados = DocumentoClienteClienteResumenViewModel.FromDocumentos(documentos);
```

`FromDocumentos` usa LINQ `GroupBy(d => d.ClienteId)` y proyecta totales por estado.

**Ordenamiento:** clientes con Vencidos o Pendientes primero, luego por nombre alfabético.

**Decisión sobre documentos sin cliente:** No aplica — la consulta ya filtra `d.Cliente != null && !d.Cliente.IsDeleted`. No existen documentos huérfanos en condiciones normales.

**Decisión sobre paginación:** La paginación sigue operando sobre documentos (no sobre clientes). Un cliente con muchos documentos puede aparecer dividido entre páginas. Esto es un tradeoff documentado. La agrupación se hace sobre los documentos de la página actual. Mejora futura: paginar por cliente.

---

## F. Modos de vista

### Modo agrupado (sin filtro de ClienteId)

Se activa cuando `!Model.ClienteId.HasValue && Model.ClientesAgrupados.Any()`.

- Tabla principal: una fila por cliente con columnas: Cliente, Identificación, Docs, Pendientes, Verificados, Rechazados, Vencidos, Última carga, Estado, Acción.
- Click en fila o botón "Ver documentos" → abre modal del cliente.
- Badge de estado resumen por fila.

### Modo plano (con filtro de ClienteId específico)

Se activa cuando `Model.ClienteId.HasValue` o no hay resultados agrupados.

- Tabla plana existente sin modificaciones funcionales.
- Link "Volver al Cliente" en el header.

---

## G. Modal de documentos por cliente

- Renderizado server-side: un `<div id="modal-cliente-{clienteId}">` por cada grupo.
- Se muestra/oculta via `documentoClienteAbrirModal(id)` / `documentoClienteCerrarModal(id)`.
- JS mínimo inline en `@section Scripts` (solo en modo agrupado): open, close, Escape.
- Z-index: modales de cliente en `z-50`. Modal de upload también en `z-50` pero renderizado después en el DOM → queda encima correctamente.
- Header del modal: nombre del cliente, DNI/CUIT, teléfono, estado resumen, link "Ver todos" (va al modo plano filtrado por ese cliente).
- Footer: total de documentos, pendientes, botón Cerrar.

### Acciones preservadas en el modal

| Acción | Tipo | returnUrl tras acción |
|---|---|---|
| Ver detalle | GET link | `Index?clienteId=X` (modo plano del cliente) |
| Descargar | GET link | — |
| Verificar | POST form + AntiForgeryToken | `Index?clienteId=X` |
| Reemplazar | Botón `data-documento-replace` | Abre modal upload (JS existente) |
| Eliminar | POST form + AntiForgeryToken | `Index?clienteId=X` |

Tras acciones POST, el usuario es llevado a la vista plana del cliente (modo plano). Puede volver a la vista agrupada desde la navegación.

---

## H. Filtros

Los filtros existentes funcionan sin cambios. El resultado visual del filtro se agrupa por cliente.

Ejemplo: filtro Estado = Pendiente → solo aparecen clientes que tienen documentos pendientes en esa página. En el modal, solo los documentos pendientes (porque son los únicos que devolvió BuscarAsync con ese filtro).

---

## I. Tests agregados

**Archivo:** `TheBuryProyect.Tests/Unit/DocumentoClienteIndexAgrupadoTests.cs`

13 tests unitarios sobre `DocumentoClienteClienteResumenViewModel.FromDocumentos`:

1. `FromDocumentos_AgrupaDocumentosMismoCliente` — dos docs del mismo cliente → un grupo
2. `FromDocumentos_ListaVacia_RetornaVacio`
3. `FromDocumentos_UnSoloDocumento_UnGrupoConUnDoc`
4. `FromDocumentos_CalculaTotalesPorEstado` — todos los estados contados correctamente
5. `FromDocumentos_TodosVerificados_CorrectamenteContados`
6. `FromDocumentos_IncluyeDocumentosEnResumenCliente` — los documentos están en el grupo
7. `EstadoResumen_ConVencidos_EsPrioritarioSobrePendiente`
8. `EstadoResumen_ConRechazados_SinVencidos`
9. `EstadoResumen_SoloPendientes_EsPendiente`
10. `EstadoResumen_TodosVerificados_EsCompleto`
11. `FromDocumentos_ClienteConPendientesAparece_AntesDeCompleto`
12. `FromDocumentos_ClienteConVencidosAparece_AntesDeCompleto`
13. `FromDocumentos_MismoOrdenPrioridad_OrdenAlfabetico`
14. `FromDocumentos_UltimaActualizacion_EsLaMasReciente`
15. `FromDocumentos_CopiaNumeroDocumentoDelPrimerDoc`

---

## J. Validaciones técnicas

```
dotnet build --configuration Release → 0 errores / 0 warnings
dotnet test --filter "DocumentoCliente|DocumentoClienteIndex" → 67/67 passing
dotnet test (suite completa) → 2761/2761 passing
git diff --check → limpio (solo warning LF→CRLF esperado en Windows)
git status --short → solo archivos de fase 10.19 en staging
```

---

## K. Qué NO se tocó

- Entidades (`DocumentoCliente`, `Cliente`) — sin cambios
- Migraciones — ninguna
- Servicios (`DocumentoClienteService`, `IDocumentoClienteService`) — sin cambios
- Reglas de verificación/rechazo/eliminación/subida — sin cambios
- Permisos — sin cambios
- Upload modal — sin cambios funcionales
- Rutas — sin cambios
- Tests de servicio existentes — sin modificaciones
- Módulos de Carlos (Cotización) — no tocados
- Worktree de Kira — no tocado
- `AGENTS.md`, `CLAUDE.md` — no incluidos en commit

---

## L. Riesgos y deuda remanente

| Item | Tipo | Prioridad |
|---|---|---|
| Paginación opera sobre documentos, no clientes | Deuda UX | Media — un cliente con muchos docs aparece dividido entre páginas |
| Sin filtro por cliente en modo agrupado, PageSize=10 puede mostrar pocos clientes | UX | Baja — usuario puede aumentar PageSize o filtrar |
| No hay Rechazar inline en modal (solo desde Details) | Funcional conocida | Baja — existía antes de esta fase |
| JS de modales inline (no en archivo externo) | Deuda técnica menor | Baja — es mínimo y view-specific |

---

## M. Checklist actualizado

### Juan

- [x] 10.18 — DocumentoCliente estado visual: cerrado (34832c4)
- [x] 10.19 — DocumentoCliente agrupado por cliente + modal: **cerrado (850be58)**

### Carlos

- [ ] V1.7 — Tests integración seguridad conversión 403: pendiente/en curso

### Kira

- [ ] Fix test HTTPS TestHost: pendiente/en curso

---

## N. Confirmación de no nuevos micro-lotes

Esta es la última fase del loop de micro-lotes visuales, conforme a instrucción del usuario. No se agregan nuevos micro-lotes.
