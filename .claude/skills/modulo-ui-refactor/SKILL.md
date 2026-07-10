---
name: modulo-ui-refactor
description: Analiza y optimiza el módulo indicado por el usuario en ASP.NET MVC .NET 8 revisando CSHTML, CSS y JavaScript. Úsala cuando el usuario pida limpiar, refactorizar, ordenar, quitar código muerto, fusionar estilos/scripts o mejorar mantenibilidad de un módulo específico del proyecto.
---

# Skill: Módulo UI Refactor

Actuá como desarrollador senior experto en ASP.NET MVC .NET 8, Razor/CSHTML, Tailwind, CSS modular, JavaScript vanilla, UX/UI empresarial, accesibilidad y refactoring seguro.

Esta skill se usa para analizar y optimizar el módulo que indique el usuario, sin romper comportamiento existente.

## Regla crítica de ejecución

Por defecto, trabajar primero en **modo diagnóstico**.

No modificar archivos hasta que el usuario confirme explícitamente algo como:

- “aplicá los cambios”
- “empezá a modificar”
- “hacé el refactor”
- “implementalo”
- “ejecutá el micro-lote”

Si el usuario solo pide:

- “analizá”
- “revisá”
- “diagnosticá”
- “decime qué ves”
- “qué opinás”
- “mirá este módulo”

entonces no editar archivos.

En modo diagnóstico se permite leer archivos y ejecutar comandos seguros de inspección, pero no modificar el working tree.

## Lectura obligatoria del CLAUDE.md

Antes de analizar o modificar cualquier archivo, revisar y respetar el `CLAUDE.md` del proyecto.

Buscarlo en este orden:

1. `CLAUDE.md` en la raíz del repo.
2. `.claude/CLAUDE.md`, si existe.
3. otros `CLAUDE.md` relevantes dentro del árbol del proyecto, si el módulo está en una subcarpeta.

El análisis debe tomar el `CLAUDE.md` como contexto operativo principal del proyecto.

Antes de proponer cambios, resumir brevemente:

- reglas relevantes encontradas en `CLAUDE.md`;
- restricciones que afectan esta tarea;
- comandos de validación esperados;
- límites sobre commits, push, stashes o cambios fuera de alcance;
- cualquier regla de arquitectura o estilo aplicable al módulo.

Si no existe `CLAUDE.md`, aclararlo explícitamente y continuar usando solo esta skill y el pedido del usuario.

Si hay conflicto entre esta skill y `CLAUDE.md`:

1. priorizar reglas de seguridad del `CLAUDE.md`;
2. priorizar esta skill solo cuando sea más específica y no contradiga reglas de seguridad;
3. pedir confirmación antes de cualquier acción dudosa.

## Entrada obligatoria

Antes de empezar, identificar el módulo objetivo.

Ejemplos válidos:

- Cliente
- Venta
- Producto
- Mora
- Caja
- MercadoLibre
- Garante
- Autorización
- Catálogo
- Inventario
- Crédito
- Proveedor
- Reportes
- Configuración

Si el usuario no indica el módulo, preguntar:

```text
¿Qué módulo querés analizar?
```

No empezar el análisis sin módulo definido.

Durante la ejecución, usar:

```text
MODULO = nombre indicado por el usuario
```

Ejemplos:

```text
MODULO = Cliente
MODULO = Venta
MODULO = Mora
MODULO = Producto
```

## Objetivo

Analizar y optimizar completamente el módulo indicado, incluyendo:

- vistas `.cshtml`;
- parciales Razor relacionadas;
- CSS del módulo;
- JavaScript del módulo;
- referencias desde layout, imports, bundles o scripts usados por ese módulo;
- ViewModels usados directamente por esas vistas, solo para entender contratos UI;
- controladores relacionados, solo para entender rutas, actions y contratos;
- tests relacionados, solo para validar impacto.

El objetivo es dejar el módulo más limpio, mantenible, consistente y seguro.

## Prioridad de instrucciones

El `CLAUDE.md` del proyecto debe revisarse antes de actuar y tiene prioridad como guía operativa general.

Orden de prioridad:

1. instrucciones explícitas del usuario en esta conversación;
2. reglas de seguridad del `CLAUDE.md`;
3. esta skill;
4. criterio técnico del agente.

Nunca ignorar el `CLAUDE.md` si contiene restricciones sobre:

- ramas;
- commits;
- push;
- stashes;
- tests;
- procesos;
- arquitectura;
- alcance del módulo;
- comandos permitidos o prohibidos.

## Reglas obligatorias

- No hacer push.
- No tocar stashes.
- No usar `git add -A`.
- No hacer commits salvo pedido explícito.
- No reescribir todo el módulo desde cero.
- No cambiar comportamiento funcional sin evidencia y justificación.
- No modificar backend salvo ajuste mínimo y justificado de ViewModel, mapping o contrato UI.
- No tocar módulos ajenos salvo dependencia directa y demostrable.
- No eliminar código si no hay evidencia clara de que está muerto.
- No dejar procesos colgados.
- No romper tests existentes.
- Trabajar por micro-lotes.
- Revisar el estado del repo antes de modificar.
- No expandir el refactor fuera del módulo objetivo.
- No mezclar cambios de diagnóstico con cambios funcionales.
- No aplicar cambios masivos sin mostrar antes el plan.

## Estado inicial obligatorio

Ejecutar primero:

```bash
git status --short
```

Si hay cambios previos del usuario:

- no pisarlos;
- identificar qué archivos están modificados;
- adaptar el plan para no mezclar cambios propios con cambios existentes;
- si el riesgo de pisar trabajo ajeno es alto, detenerse y pedir confirmación.

También revisar contexto operativo:

```bash
pwd
git branch --show-current
```

Si el proyecto usa solución `.sln`, identificarla antes de validar.

## Descubrimiento del módulo

Antes de editar, identificar los archivos reales del módulo objetivo.

Buscar por el nombre del módulo en:

- `Views/{MODULO}/*.cshtml`;
- `Views/Shared/*{MODULO}*`;
- parciales usadas por vistas del módulo;
- `wwwroot/css/*{modulo}*`;
- `wwwroot/js/*{modulo}*`;
- scripts o estilos referenciados por vistas del módulo;
- referencias relevantes en `_Layout.cshtml`;
- imports, bundles o secciones `Scripts` / `Styles`;
- controladores relacionados, solo para entender rutas y contratos;
- ViewModels usados directamente por esas vistas, solo para entender bindings y validaciones;
- tests relacionados.

También buscar variantes razonables:

- singular y plural;
- mayúsculas y minúsculas;
- nombres compuestos;
- nombres legacy;
- nombres de vistas principales;
- nombres de funcionalidades internas del módulo;
- nombres de archivos CSS/JS asociados.

Ejemplos:

Si `MODULO = Cliente`, buscar también:

- `Clientes`;
- `cliente`;
- `Details`;
- `Details_tw`;
- `Puntaje`;
- `BCRA`;
- `Garante`;
- `Aptitud`;
- `Autorizacion`;
- `Credito`.

Si `MODULO = Venta`, buscar también:

- `Ventas`;
- `venta`;
- `Checkout`;
- `Carrito`;
- `Pago`;
- `Credito`;
- `Cuotas`;
- `MedioPago`.

Si `MODULO = Producto`, buscar también:

- `Productos`;
- `producto`;
- `Catalogo`;
- `Inventario`;
- `Unidad`;
- `Stock`.

Si `MODULO = Mora`, buscar también:

- `Mora`;
- `mora`;
- `ConfiguracionMora`;
- `ConfiguracionExpandida`;
- `Score`;
- `Credito`;
- `Cuotas`.

Adaptar estas variantes al módulo real. No inventar archivos.

## Comandos útiles de inspección

Usar comandos de lectura/inspección.

Ejemplos bash:

```bash
find . -path "*/bin" -prune -o -path "*/obj" -prune -o -type f -iname "*cliente*" -print
rg "NombreDelModulo|nombreDelModulo" .
```

Ejemplos PowerShell:

```powershell
Get-ChildItem -Recurse -File | Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' -and $_.Name -match 'cliente' }
Select-String -Path .\Views\**\*.cshtml,.\wwwroot\css\*.css,.\wwwroot\js\*.js -Pattern "NombreDelModulo"
```

Ajustar los comandos al entorno real del proyecto.

## Diagnóstico obligatorio antes de modificar

Antes de tocar código, entregar un diagnóstico breve con:

1. módulo objetivo confirmado;
2. `CLAUDE.md` revisado y reglas relevantes;
3. estado inicial de Git;
4. archivos detectados;
5. responsabilidades de cada archivo;
6. CSS usado por las vistas;
7. JS usado por las vistas;
8. dependencias entre CSHTML, CSS y JS;
9. rutas/actions/AJAX relevantes;
10. ViewModels o contratos UI relevantes;
11. posibles duplicaciones;
12. posible código muerto;
13. posibles riesgos;
14. cambios sugeridos, separados entre seguros y riesgosos;
15. propuesta de micro-lotes.

No modificar hasta que el usuario confirme explícitamente.

## Clasificación de cambios

Clasificar cada propuesta como:

### Seguro

Cambio local, bajo riesgo, con evidencia clara.

Ejemplos:

- remover `console.log` innecesario;
- corregir label sin cambiar binding;
- consolidar dos reglas CSS idénticas;
- validar existencia de un elemento antes de usarlo en JS.

### Medio

Cambio razonable, pero requiere validación.

Ejemplos:

- fusionar handlers JS similares;
- mover script inline a archivo JS;
- consolidar clases CSS de cards;
- extraer markup repetido a parcial.

### Riesgoso

Cambio que puede alterar comportamiento o contratos.

Ejemplos:

- cambiar nombres de inputs;
- cambiar rutas o actions;
- cambiar endpoints AJAX;
- tocar ViewModels;
- tocar lógica de controller;
- modificar validaciones funcionales.

Los cambios riesgosos no deben aplicarse sin confirmación específica.

## Qué revisar en CSHTML

Buscar y corregir, si es seguro:

- markup duplicado;
- secciones repetidas;
- scripts inline innecesarios;
- estilos inline innecesarios;
- IDs duplicados;
- labels mal vinculados;
- inputs sin validación visual clara;
- botones sin texto claro;
- formularios con bindings frágiles;
- uso inconsistente de Tailwind y clases custom;
- parciales que puedan ordenar mejor el módulo;
- bloques condicionales demasiado complejos;
- mensajes de error poco visibles;
- problemas responsive;
- problemas básicos de accesibilidad;
- HTML inválido o mal anidado;
- formularios sin antiforgery cuando corresponda;
- nombres de secciones poco claros.

No cambiar sin justificación:

- nombres de campos;
- nombres de actions;
- rutas;
- bindings del ViewModel;
- contratos AJAX;
- comportamiento funcional;
- nombres de IDs usados por JS;
- data attributes usados por JS.

## Qué revisar en CSS

Buscar y corregir, si es seguro:

- reglas duplicadas;
- reglas muertas;
- selectores demasiado globales;
- selectores frágiles;
- `!important` innecesarios;
- clases con nombres ambiguos;
- estilos repetidos para cards, badges, botones, alertas o formularios;
- inconsistencias visuales;
- reglas que deberían ser específicas del módulo;
- conflictos con Tailwind;
- media queries redundantes;
- estilos que pisan componentes de otros módulos;
- reglas que dependen de orden accidental.

Criterio:

- consolidar reglas equivalentes;
- mantener CSS modular;
- evitar cambios globales;
- no eliminar una regla si no se puede demostrar que no se usa;
- dejar comentarios solo cuando aclaren una decisión no obvia;
- preferir nombres específicos del módulo cuando haya riesgo de colisión.

## Qué revisar en JavaScript

Buscar y corregir, si es seguro:

- funciones no usadas;
- handlers duplicados;
- listeners registrados más de una vez;
- selectores inexistentes;
- selectores frágiles por texto visible;
- uso innecesario de variables globales;
- fetch/AJAX sin manejo de error;
- código que depende de IDs duplicados;
- lógica repetida para mostrar/ocultar bloques;
- console logs innecesarios;
- código comentado muerto;
- acoplamiento excesivo entre JS y HTML;
- errores silenciosos;
- inicialización que falla si una vista no tiene cierto elemento;
- uso de `innerHTML` cuando no corresponde;
- falta de manejo de estados de carga/error.

Buenas prácticas:

- validar existencia de elementos antes de usarlos;
- preferir `data-*` cuando mejore claridad;
- mantener contratos AJAX existentes;
- no cambiar endpoints sin revisar backend;
- no romper validación client-side;
- mantener comportamiento actual salvo mejora justificada;
- encapsular lógica para evitar contaminación global;
- no introducir dependencias nuevas sin necesidad.

## Código muerto

Solo eliminar código si hay evidencia razonable.

Evidencia aceptable:

- no hay referencias por búsqueda textual;
- no está incluido en vistas ni layout;
- no hay selectores usados por JS;
- no hay clases generadas dinámicamente;
- no hay referencias en tests;
- no hay dependencia desde parciales;
- no hay uso indirecto evidente;
- el archivo no se referencia desde ningún layout, vista o bundle;
- el código es duplicado exacto y hay una versión activa equivalente.

Si hay duda, no eliminar. Marcar como “requiere revisión manual”.

## Duplicación y consolidación

Buscar oportunidades de consolidar:

- cards repetidas;
- badges repetidos;
- alertas repetidas;
- botones con estilos equivalentes;
- bloques de formulario repetidos;
- helpers visuales repetidos;
- funciones JS equivalentes;
- inicializadores JS repetidos;
- CSS duplicado entre módulos.

Solo consolidar si:

- el comportamiento queda igual;
- el alcance es claro;
- la validación es posible;
- no se genera una abstracción más difícil de mantener.

## Micro-lotes sugeridos

Trabajar en este orden, salvo que el diagnóstico indique otra prioridad:

1. inventario y mapa de dependencias;
2. diagnóstico sin cambios;
3. esperar confirmación del usuario;
4. limpieza segura de JS;
5. limpieza segura de CSS;
6. ajustes CSHTML de bajo riesgo;
7. consolidación de duplicados;
8. accesibilidad básica;
9. validación build/tests;
10. resumen final.

Después de cada lote relevante, revisar:

```bash
git diff -- <archivo>
```

No iniciar el siguiente lote si el anterior dejó dudas graves.

## Validación obligatoria

Ejecutar como mínimo:

```bash
dotnet build
dotnet test
```

Si existen tests focalizados del módulo objetivo, ejecutarlos también.

Buscar tests por:

- nombre del módulo;
- plural del módulo;
- nombre del controller;
- nombre de vistas principales;
- funcionalidades internas detectadas en el diagnóstico.

Si hay Playwright o tests UI focalizados disponibles para el módulo, ejecutarlos.

Si no existen tests específicos, aclararlo.

Si un comando falla:

- no ocultar el error;
- resumir la causa probable;
- indicar si el fallo parece relacionado o no con los cambios;
- no seguir aplicando cambios hasta entender el impacto.

## Validación visual esperada

Indicar qué debería observarse visualmente después del cambio:

- formularios sin solapamientos;
- cards alineadas;
- botones consistentes;
- badges consistentes;
- secciones ordenadas;
- mensajes de error visibles;
- estados de carga/error claros;
- responsive igual o mejor;
- sin cambios funcionales inesperados;
- sin parpadeos o estados intermedios rotos;
- sin errores en consola del navegador.

## Resumen de diagnóstico obligatorio

En modo diagnóstico, responder con este formato:

```markdown
### Módulo objetivo

- Módulo: `MODULO`

### CLAUDE.md revisado

- Ubicación:
- Reglas relevantes:
- Restricciones aplicables:

### Estado inicial

Comando:

```bash
git status --short
```

Resultado:

```text
...
```

### Archivos detectados

- `archivo`: responsabilidad

### Dependencias detectadas

- CSHTML:
- CSS:
- JS:
- Parciales:
- ViewModels:
- Controllers/actions:
- AJAX/fetch:

### Hallazgos

#### Posibles cambios seguros

- cambio 1
- cambio 2

#### Cambios de riesgo medio

- cambio 1
- cambio 2

#### Cambios riesgosos o para revisión manual

- cambio 1
- cambio 2

### Propuesta de micro-lotes

1. lote 1
2. lote 2
3. lote 3

### Confirmación requerida

No voy a modificar archivos hasta que confirmes qué micro-lote querés aplicar.
```

## Formato de respuesta final obligatorio

Al terminar una implementación, responder con este formato:

```markdown
### Módulo analizado

- Módulo: `MODULO`

### Resumen de cambios realizados

- `archivo`:
  - Cambio:
  - Motivo:
  - Impacto esperado:

### Código muerto o redundante removido

- `archivo`:
  - Qué se eliminó:
  - Evidencia:
  - Por qué era seguro:

### Código fusionado o simplificado

- `archivo`:
  - Qué se fusionó:
  - Beneficio:

### Validaciones ejecutadas

- `dotnet build`: resultado
- `dotnet test`: resultado
- Tests focalizados: resultado
- Playwright/UI, si aplica: resultado

### Cambios visuales esperados

- cambio visual 1
- cambio visual 2
- cambio visual 3

### Riesgos o puntos a revisar manualmente

- punto 1
- punto 2

### Estado final

Comando:

```bash
git status --short
```

Resultado:

```text
...
```
```

## Si no se puede completar todo

No inventar.

Entregar progreso parcial con:

- módulo analizado;
- archivos revisados;
- cambios aplicados;
- validaciones ejecutadas;
- archivos pendientes;
- riesgos detectados;
- estado actual de Git.


