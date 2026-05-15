# Fase 10.14 — Diagnóstico Badges RMA y NotaCredito

**Fecha:** 2026-05-15  
**Agente:** Juan  
**Estado:** Cerrada — Sin cambios de código  

---

## Objetivo

Revisar vistas de RMA y NotaCredito para detectar divergencias visuales en badges respecto al patrón canónico normalizado en fases 10.12 y 10.13.

---

## Hallazgos

### A. ¿Existen Views/RMA/?

**No.** No existe ninguna carpeta `Views/RMA/` ni ningún archivo `.cshtml` dedicado al módulo RMA.

### B. ¿Existen Views/NotaCredito/?

**No.** No existe ninguna carpeta `Views/NotaCredito/` ni ningún archivo `.cshtml` dedicado al módulo NotaCredito.

### C. ¿Dónde se renderizan los badges de RMA y NotaCredito?

En `Views/Devolucion/Detalles.cshtml`, normalizado en **Fase 10.12** (commit `989faec`).

### D. ¿Usan badges o texto plano?

Badges con el patrón canónico. Helpers locales Razor en `@{ ... }` al inicio de la vista.

### E. Estado de los helpers

| Helper | Archivo | Patrón | Cobertura enum | Fallback |
|--------|---------|--------|----------------|---------|
| `EstadoNCClass` | `Detalles.cshtml:53–61` | `border-X-500/30 bg-X-500/10 text-X-300` | 5/5 ✓ | `_` ✓ |
| `EstadoRMAClass` | `Detalles.cshtml:63–73` | `border-X-500/30 bg-X-500/10 text-X-300` | 7/7 ✓ | `_` ✓ |

#### EstadoNotaCredito — valores del enum vs. helper

| Valor | Clase aplicada |
|-------|---------------|
| Vigente | `border-emerald-500/30 bg-emerald-500/10 text-emerald-300` |
| UtilizadaParcialmente | `border-amber-500/30 bg-amber-500/10 text-amber-300` |
| UtilizadaTotalmente | `border-slate-600/30 bg-slate-600/10 text-slate-400` |
| Vencida | `border-rose-500/30 bg-rose-500/10 text-rose-300` |
| Cancelada | `border-slate-600/30 bg-slate-600/10 text-slate-400` |
| `_` (fallback) | `border-slate-600/30 bg-slate-600/10 text-slate-400` |

#### EstadoRMA — valores del enum vs. helper

| Valor | Clase aplicada |
|-------|---------------|
| Pendiente | `border-amber-500/30 bg-amber-500/10 text-amber-300` |
| AprobadoProveedor | `border-sky-500/30 bg-sky-500/10 text-sky-300` |
| EnTransito | `border-violet-500/30 bg-violet-500/10 text-violet-300` |
| RecibidoProveedor | `border-indigo-500/30 bg-indigo-500/10 text-indigo-300` |
| EnEvaluacion | `border-sky-500/30 bg-sky-500/10 text-sky-300` |
| Resuelto | `border-emerald-500/30 bg-emerald-500/10 text-emerald-300` |
| Rechazado | `border-rose-500/30 bg-rose-500/10 text-rose-300` |
| `_` (fallback) | `border-slate-600/30 bg-slate-600/10 text-slate-400` |

### F. ¿Coinciden con el patrón canónico de Detalles/Index?

Sí. Ambos helpers fueron normalizados en Fase 10.12 y siguen el mismo patrón que el resto de helpers en la vista (`DevState`, `ResClass`, `AccionClass`, `EstadoProdClass`).

### G. ¿Se puede resolver sin tocar controllers?

No aplica. No hay divergencias que corregir.

---

## Clasificación de componentes

| Componente | Clasificación | Evidencia | Decisión |
|---|---|---|---|
| `Views/Devolucion/Detalles.cshtml` | canónico | normalizado en 10.12, badges completos y correctos | no tocar |
| `Views/RMA/*` | no existe | búsqueda exhaustiva en repo | no aplica |
| `Views/NotaCredito/*` | no existe | búsqueda exhaustiva en repo | no aplica |
| `EstadoRMA` (enum) | canónico | definido en `Models/Entities/Devolucion.cs:385` | solo lectura |
| `EstadoNotaCredito` (enum) | canónico | definido en `Models/Entities/Devolucion.cs:427` | solo lectura |

---

## Decisión

**Sin cambios de código.** Los badges de RMA y NotaCredito ya están normalizados en la única vista que los renderiza (`Views/Devolucion/Detalles.cshtml`), con cobertura completa de enums y patrón canónico aplicado correctamente desde Fase 10.12.

---

## Estado de validaciones

| Check | Resultado |
|-------|----------|
| `git status --short` | `M AGENTS.md`, `M CLAUDE.md`, `?? docs/fase-cotizacion-...` (ajenos) |
| `git log -1` | `8a433bc` — Fase 10.13 |
| `dotnet build --configuration Release` | 0 errores, 0 advertencias |
| Cambios en código | Ninguno |
| Commit | No aplica |
| Tests | Sin regresiones (sin cambios de código) |

---

## Siguiente micro-lote recomendado

Opciones ordenadas por valor/riesgo:

1. **Fase 10.15** — Revisar badges en módulo Ticket/Garantía (`Views/Ticket/`) si usan estados con badges sin normalizar.
2. **Fase 10.15** — Revisar badges en módulo OrdenCompra (`Views/OrdenCompra/`) para detectar divergencias similares.
3. Cerrar línea 10.x y avanzar a siguiente frente funcional.
