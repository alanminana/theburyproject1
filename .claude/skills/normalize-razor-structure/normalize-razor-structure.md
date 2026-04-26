# normalize-razor-structure

Propósito:
Normalizar vistas Razor de un frontend ERP modular en ASP.NET MVC únicamente a nivel estructural.
Esta skill NO debe rediseñar la interfaz, NO debe crear el design system final y NO debe reescribir clases visuales existentes.
El objetivo es dejar HTML, Razor y jerarquía consistentes entre módulos y tipos de vista, preparando una base limpia para una fase visual posterior.

Tarea:
Refactorizar una vista Razor del módulo `{{MODULO}}` y del tipo `{{VIEW_TYPE}}` respetando una estructura canónica por tipo de vista.

Instrucción clave:
Esta tarea es de normalización estructural pura, no de mejora visual final.

Reglas obligatorias:
- NO rediseñar
- NO cambiar lógica Razor
- NO cambiar nombres de variables, modelos, bindings, helpers, handlers, rutas ni condiciones
- NO agregar comportamiento nuevo
- NO introducir una nueva capa visual final
- NO reemplazar clases visuales existentes por un design system nuevo
- NO asumir que existen componentes finales como btn, badge, metric-card, data-table, page-title, page-subtitle, filter-input, filter-select, etc. salvo que ya existan realmente en el proyecto
- NO inventar bloques estructurales fuera de la estructura canónica del tipo de vista
- SÍ eliminar wrappers redundantes
- SÍ reducir nesting innecesario
- SÍ limpiar clases duplicadas, inconsistentes o ruidosas solo si pertenecen a wrappers estructurales y no afectan CSS o JS existente
- SÍ normalizar patrones repetidos a nivel estructural
- SÍ mantener una estructura predecible, plana y reusable
- SÍ preservar el significado funcional de cada bloque existente
- SÍ mantener clases actuales cuando estén ligadas al CSS existente, a scripts o a comportamiento
- SÍ introducir clases canónicas solo para estructura cuando haga falta
- SÍ dejar explícita la deuda pendiente para una futura fase visual

Reglas críticas adicionales:
- NO reescribir clases visuales existentes en elementos ya presentes
- NO introducir nuevas utilidades Tailwind en elementos existentes, salvo que sean wrappers estructurales nuevos
- NO cambiar el atributo `class=""` de un elemento existente, excepto para agregar una clase estructural canónica adicional en wrappers estructurales
- Si un elemento ya tiene clases visuales, conservarlas exactamente como están
- Solo se permite:
  1. agregar wrappers estructurales nuevos
  2. agregar clases canónicas estructurales a wrappers existentes
  3. eliminar wrappers redundantes sin función
  4. mover bloques completos sin alterar sus clases internas
- En el `Refactored Razor code`, preservar literalmente las clases visuales internas del archivo original salvo en wrappers estructurales
- Si para cumplir la estructura canónica hace falta tocar demasiado la capa visual, detenerse y reportarlo en “Riesgos de refactorización” en vez de reescribirlo

Tipos de vista soportados:
- index
- create
- edit
- details
- delete
- dashboard
- form-partial
- table-partial
- modal

Estructuras canónicas por tipo de vista:

[index]
page-shell
  page-hero
    page-hero__content
    page-hero__actions
  page-metrics
  page-filters
  page-feedback
  page-table
  page-summary

[create]
page-shell
  page-hero
    page-hero__content
    page-hero__actions
  page-feedback
  page-form
  page-summary

[edit]
page-shell
  page-hero
    page-hero__content
    page-hero__actions
  page-feedback
  page-form
  page-summary

[details]
page-shell
  page-hero
    page-hero__content
    page-hero__actions
  page-feedback
  page-summary
  page-related

[delete]
page-shell
  page-hero
    page-hero__content
    page-hero__actions
  page-feedback
  page-summary
  page-actions

[dashboard]
page-shell
  page-hero
    page-hero__content
    page-hero__actions
  page-metrics
  page-feedback
  page-sections
  page-summary

[form-partial]
form-shell
  form-section
  form-section
  form-actions

[table-partial]
table-shell
  table-toolbar
  table-feedback
  table-content
  table-summary

[modal]
modal-shell
  modal-header
  modal-body
  modal-actions

Detección del tipo de vista:
- Usa `{{VIEW_TYPE}}` si fue proporcionado explícitamente
- Si no fue proporcionado, infiérelo por convención del archivo, contenido y propósito de la vista
- Si hay ambigüedad entre dos tipos, elige el más conservador y explica la decisión en “Diagnóstico estructural”

Criterios de normalización:
- El contenedor raíz debe responder a la estructura canónica del tipo de vista
- Elimina divs o wrappers sin función clara de estructura, semántica o lógica
- Reduce nesting innecesario
- Agrupa contenido relacionado en un único bloque canónico
- No crees bloques vacíos
- Si una sección canónica no existe en la vista original, no la inventes salvo que sea necesaria para ordenar elementos ya presentes
- Conserva bloques condicionales Razor exactamente como están, cambiando solo su ubicación estructural si hace falta
- Mantén formularios, tablas, mensajes y acciones en zonas consistentes
- No mezcles acciones principales con acciones de fila o acciones secundarias
- No disperses feedback, alerts o validation summaries en múltiples lugares si pueden consolidarse en un único bloque canónico
- No conviertas todavía la vista al sistema visual final
- Separa estructura de estética
- Si existe un wrapper de módulo útil, mantenelo y agregale la clase canónica en vez de reemplazarlo
- Si un bloque interno ya tiene clases visuales válidas, no las reescribas; solo rodealo o nombralo estructuralmente

Qué hacer con las clases:
- Mantén clases existentes si son necesarias para no romper CSS, JS o contratos actuales
- Elimina clases ruidosas, duplicadas o contradictorias solo cuando pertenezcan a wrappers estructurales y sea seguro hacerlo
- Agrega clases canónicas estructurales solo cuando ayuden a unificar la base
- No reemplaces masivamente clases existentes por nombres de componentes finales que todavía no estén implementados
- No conviertas clases visuales del proyecto a utilidades Tailwind nuevas
- No reemplaces clases de módulo por clases canónicas; ambas pueden coexistir si la de módulo sigue siendo necesaria

Qué hacer con responsive:
- No rediseñar la pantalla
- Sí detectar problemas estructurales que luego afectarán desktop, tablet o mobile
- Sí dejar la estructura preparada para una futura fase responsive
- Sí señalar tablas anchas, grupos de filtros extensos, acciones densas y bloques difíciles de adaptar
- No resolver responsive reestilando visualmente en esta fase

Qué NO hacer:
- No cambiar texto de negocio sin necesidad
- No cambiar nombres de propiedades o llamadas Razor
- No convertir la vista a otro patrón arquitectónico
- No introducir componentes nuevos que no existían
- No separar archivos
- No optimizar visualmente
- No reordenar contenido de forma que altere el flujo funcional de la pantalla
- No escribir el archivo automáticamente salvo que el usuario lo pida explícitamente

Entrada admitida:
La skill debe aceptar cualquiera de estas formas:

Opción A:
- Módulo: {{MODULO}}
- Tipo de vista: {{VIEW_TYPE}}
- Archivo: {{FILE_PATH}}

Opción B:
- Módulo: {{MODULO}}
- Tipo de vista: {{VIEW_TYPE}}
- Código Razor embebido

Si el usuario pasa una ruta de archivo:
- leer el archivo indicado y trabajar sobre ese contenido

Si el usuario pega el contenido Razor:
- trabajar directamente sobre ese contenido

Si falta el tipo de vista:
- inferirlo si es posible y reportarlo

Formato de salida obligatorio:
1. Diagnóstico estructural
   - Lista concreta de problemas detectados

2. Riesgos de refactorización
   - Qué se podría romper o afectar si se toca mal

3. Estrategia recomendada
   - Qué se normaliza ahora y qué se difiere a la fase visual

4. Refactored Razor code
   - Devuelve el archivo Razor completo refactorizado
   - Debe respetar estrictamente las reglas críticas adicionales
   - No debe reescribir clases visuales existentes salvo para agregar clases estructurales canónicas a wrappers

5. Deuda pendiente para la fase visual
   - Qué queda pendiente para la futura aplicación de Tailwind o sistema visual

6. Components to extract later
   - Lista de piezas repetibles que conviene extraer después

7. Checklist responsive
   - Riesgos o consideraciones para desktop, tablet y mobile

Modo de trabajo:
- Primero entender la vista y mapearla al canon
- Después detectar wrappers redundantes, nesting innecesario y bloques fuera de lugar
- Luego proponer un refactor conservador
- Mantener intactas las clases visuales internas existentes
- Solo introducir cambios estructurales permitidos
- Si una transformación implica rediseño visual, no hacerla; reportarla como deuda o riesgo