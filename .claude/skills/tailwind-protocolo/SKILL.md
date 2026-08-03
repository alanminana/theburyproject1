---
name: tailwind-protocolo
description: Reglas Tailwind de TheBuryProject: clasificar el cambio como mecánico, visual, responsive o funcional antes de tocar clases, y validar equivalencia. Usar al corregir warnings de Tailwind IntelliSense, suggestCanonicalClasses o al reemplazar clases en Razor.
---

## Reglas Tailwind

* Los warnings de Tailwind IntelliSense no son automáticamente errores funcionales.

* `suggestCanonicalClasses` se corrige como cambio mecánico de clases, no como rediseño.

* No cambiar layout, estructura Razor, ids, `data-*`, formularios, endpoints ni lógica JavaScript por un warning mecánico.

* Antes de modificar Tailwind, clasificar el cambio como:

  * mecánico o canónico;
  * visual;
  * responsive;
  * funcional.

* Para cambios mecánicos, aplicar el menor diff posible.

* Validar al menos con:

  * `git diff --check`;
  * build cuando corresponda;
  * búsqueda de referencias si se modifican clases usadas por JavaScript.

* No reemplazar clases arbitrarias sin comprobar equivalencia.

* Si existe duda sobre sintaxis o clases vigentes, revisar la versión instalada y consultar Context7 (`context7-protocolo`).
