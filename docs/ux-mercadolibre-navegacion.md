# Auditoría UX — Módulo MercadoLibre

> Branch: `ux-simplificacion-navegacion`.
> Scope: **solo el módulo MercadoLibre** (Views/MercadoLibre + MercadoLibreController).
> Objetivo: ordenar navegación, jerarquía y ruido visual. NO cambiar reglas de negocio ni sync/OAuth.

## Cómo se entra al módulo
Sidebar → grupo **Integraciones** → "Mercado Libre" (`_Layout.cshtml:231`) → `MercadoLibre/Dashboard`.
Un solo punto de entrada. Toda la navegación entre las 17 vistas es **interna** al módulo.

## Inventario de vistas (17) + clasificación
`1` landing · `2` listado · `3` detalle · `4` formulario · `5` resultado de acción · `6` config

| Vista | Acción GET | Propósito | Tipo | Líneas |
|---|---|---|---|---|
| Dashboard | /MercadoLibre/Dashboard | Panel operativo (KPIs + recientes) | 1 | 598 |
| Index | /MercadoLibre | Conexión de cuenta (OAuth) + importar | 1 | 193 |
| Configuracion | /MercadoLibre/Configuracion | Config canal (precio/stock/órdenes/caja) | 6 | 291 |
| Listings | /MercadoLibre/Listings | Publicaciones (lista) | 2 | 311 |
| Listing | /MercadoLibre/Listing/{id} | Publicación (detalle) | 3 | 689 |
| ListingCrearProducto | POST flujo | Crear producto ERP desde listing | 4 | 172 |
| SyncPreview | POST result | Preview de sync stock/precio | 5 | 183 |
| Ordenes | /MercadoLibre/Ordenes | Órdenes (lista) | 2 | 215 |
| Orden | /MercadoLibre/Orden/{id} | Orden (detalle: venta, envío, claim, liquidación) | 3 | 687 |
| Preguntas | /MercadoLibre/Preguntas | Preguntas preventa (lista) | 2 | 138 |
| Pregunta | /MercadoLibre/Pregunta/{id} | Pregunta (detalle/responder) | 3 | 136 |
| Mensajes | /MercadoLibre/Mensajes | Mensajes postventa (lista) | 2 | 153 |
| Aumentos | /MercadoLibre/Aumentos | Aumentos masivos (lotes) | 2 | 123 |
| AumentoNuevo | /MercadoLibre/AumentoNuevo | Crear lote de aumento | 4 | 139 |
| Aumento | /MercadoLibre/Aumento/{id} | Lote (detalle/aplicar/revertir) | 3 | 256 |
| Borradores | /MercadoLibre/Borradores | Borradores de publicación (lista) | 2 | 219 |
| Borrador | /MercadoLibre/Borrador/{id} | Borrador (detalle/validar/publicar) | 3 | 244 |

## Áreas funcionales reales (7)
1. **Panel** (Dashboard) · 2. **Publicaciones** (Listings/Listing + Borradores) · 3. **Órdenes** (Ordenes/Orden) ·
4. **Preguntas** · 5. **Mensajes** · 6. **Precios/Aumentos** · 7. **Conexión + Configuración**.

## Problemas encontrados

### 1. No hay navegación interna unificada (causa raíz del "seguimiento difícil")
- **69 cross-links ad-hoc** repartidos en las vistas.
- **13 vistas reinventan "Volver/arrow_back"** con **destinos distintos**:
  - Aumentos → back → **Listings** (raro; debería ser Panel o Aumentos-home).
  - Configuracion → back → **Index**.
  - Dashboard → "Conexión" → Index.
- **No existe ningún partial de navegación** en `Views/MercadoLibre/`. Cada página arma su propia botonera de header.
- El set de destinos del header es **inconsistente**: el Dashboard linkea Configuración/Publicaciones/Órdenes/Preguntas/Mensajes/Conexión pero **omite Aumentos y Borradores**.
- No hay indicación de "dónde estoy" (sin estado activo entre áreas).

### 2. Dashboard sobrecargado de KPIs
- ~11 secciones, ~70 números en una sola pantalla.
- Mezcla KPIs operativos (publicaciones, órdenes, envíos, preguntas, mensajes) con **técnicos/diagnóstico** (Webhooks por topic, Sync logs, contadores "Simuladas/Sin orden/Sin publicación").
- Ruido alto; dificulta decidir qué atender primero (las Alertas operativas ya cubren lo urgente).

### 3. Dos landings con naming solapado
- `Index` (título "Mercado Libre") y `Dashboard` (título "Mercado Libre · Panel operativo") compiten.
- Index = conexión/cuenta; Dashboard = operación. Propósitos distintos → **no fusionar**, pero clarificar títulos y que Conexión sea claramente "ajuste", no landing.

### Lo que está BIEN (no romper)
- Estilo **consistente dentro de ML**: todas usan `erp-page-shell` + `erp-compact` + componentes `erp-*` + acento amber-400. (A diferencia del resto del ERP, acá el sistema visual ya es coherente y canónico.)
- Las **Alertas operativas** del Dashboard con link directo a la acción son un buen patrón.
- Avisos de "Modo simulación" claros en Preguntas/Mensajes/Borradores.

## Propuesta de arquitectura de navegación ML

**Subnav único y persistente** (partial `_MercadoLibreNav.cshtml`) en todas las vistas del módulo, con estado activo:

```
[Panel] [Publicaciones] [Órdenes] [Preguntas] [Mensajes] [Aumentos] [Borradores]        [Conexión] [Configuración]
   Dashboard   Listings     Ordenes  Preguntas  Mensajes   Aumentos   Borradores            Index      Configuracion
```

- Reemplaza la botonera ad-hoc del header por este subnav consistente.
- "Volver" de los detalles (Listing/Orden/Pregunta/Aumento/Borrador) apunta a **su lista** (breadcrumb), y el subnav siempre permite saltar a cualquier área + Panel.
- Acciones propias de cada página (Importar, Nuevo aumento, Generar orden simulada, etc.) quedan como **acción primaria** a la derecha del header, separadas de la navegación.

## Lotes propuestos

- **LOTE ML-A (recomendado primero):** crear `_MercadoLibreNav.cshtml` (subnav con estado activo) e integrarlo en las 17 vistas reemplazando la botonera de header inconsistente. Riesgo bajo: solo navegación/Razor, sin tocar controller/servicios/reglas. Resuelve la causa raíz.
- **LOTE ML-B:** declutter del Dashboard — agrupar KPIs técnicos (Webhooks, Sync logs, contadores "simulado/sin-X") en una sección colapsable "Avanzado / Diagnóstico"; dejar arriba lo operativo + alertas.
- **LOTE ML-C:** normalizar breadcrumbs/back de los detalles y revisar acciones secundarias (mover a dropdown donde haya 3+).

## Gate de QA
CLAUDE.md exige prueba visual real (Playwright, viewports 1440/1280/768/390/360) antes y después de tocar Razor/CSS. **La app no está corriendo** (puertos 5187/7189 sin escuchar). Para ejecutar cualquier lote hay que levantar la app y validar en navegador.

## Riesgos / deuda
- No tocar OAuth, sync, webhooks ni reglas de liquidación.
- El subnav debe respetar permisos (`User.TienePermiso("mercadolibre", ...)`) igual que hoy.
- Las acciones POST (ImportarListings, SyncAplicar, AumentoAplicar, etc.) no se tocan.
