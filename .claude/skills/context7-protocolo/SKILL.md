---
name: context7-protocolo
description: Reglas de uso de Context7 MCP en TheBuryProject: cuándo consultarlo, uso correcto e incorrecto y formato de evidencia. Usar al consultar documentación externa de ASP.NET Core, EF Core, Playwright, Tailwind o paquetes NuGet/npm.
---

## Reglas de Context7 MCP

### Qué resuelve

Es la herramienta principal para consultar documentación técnica externa, actualizada y específica por versión.

Usarlo para confirmar:

* APIs de ASP.NET Core;
* comportamiento de .NET 8;
* Entity Framework Core;
* Playwright;
* Tailwind;
* bibliotecas NuGet o npm;
* configuraciones;
* sintaxis;
* patrones oficialmente soportados;
* cambios entre versiones;
* APIs obsoletas o reemplazadas.

Context7 no conoce la implementación real de TheBuryProject y no debe usarse para inferir:

* qué servicio usa actualmente el proyecto;
* qué camino es canónico;
* qué versión está instalada sin revisar primero el repositorio;
* qué regla de negocio corresponde;
* qué archivo debe modificarse.

### Usar Context7 cuando

* la respuesta depende de documentación actual;
* hay dudas sobre una API;
* puede existir una diferencia de versión;
* se va a usar una característica poco habitual;
* aparece un warning o error relacionado con una biblioteca;
* se necesita confirmar una práctica recomendada oficial;
* se trabaja con Playwright, Tailwind, EF Core, ASP.NET Core o paquetes externos y el comportamiento no es evidente.

### Uso correcto

Antes de consultar Context7:

1. revisar la versión realmente instalada;
2. identificar la biblioteca o framework exacto;
3. formular una pregunta técnica concreta;
4. consultar únicamente la documentación necesaria;
5. contrastar la respuesta con el código existente.

Ejemplos de consultas válidas:

* comportamiento de model binding en ASP.NET Core 8;
* validación antiforgery en formularios AJAX;
* estrategia recomendada de transacciones en EF Core;
* uso de `Locator` y auto-waiting en Playwright;
* configuración de proyectos Playwright;
* sintaxis canónica actual de Tailwind;
* opciones oficiales de accesibilidad o responsive.

### Uso incorrecto

No usar Context7 para:

* decidir reglas de negocio;
* reemplazar la lectura del repositorio;
* adivinar versiones;
* elegir arbitrariamente una arquitectura nueva;
* generar cambios masivos basados únicamente en ejemplos externos;
* introducir paquetes sin revisar si ya existe una solución interna;
* copiar patrones incompatibles con la arquitectura actual.

### Evidencia de Context7

Cuando Context7 influya de forma relevante en una decisión, registrar brevemente:

* biblioteca consultada;
* versión del proyecto;
* cuestión confirmada;
* impacto sobre la implementación.

Ejemplo:

```text
Context7: Playwright
Versión del proyecto: 1.58.x
Confirmado: usar locator basado en rol y auto-waiting, sin waitForTimeout fijo.
Impacto: se reemplazó espera temporal por expect(locator).toBeVisible().
```

No incluir transcripciones completas de documentación.

### Formato de evidencia Context7

```text
Context7:

- biblioteca:
- versión instalada:
- consulta:
- conclusión relevante:
- aplicación en el código:
```

Incluirlo únicamente cuando haya influido materialmente en la implementación.

---
