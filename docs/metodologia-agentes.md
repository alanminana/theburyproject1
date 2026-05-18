# Metodologia de agentes

Este documento conserva la guia ampliada para trabajar con agentes sin convertir `AGENTS.md` y `CLAUDE.md` en archivos gigantes.

## Objetivo

Trabajar sobre el ERP ASP.NET MVC .NET 8 priorizando estabilidad, mantenibilidad, reduccion de deuda tecnica y mejoras incrementales seguras.

La prioridad habitual no es agregar funcionalidad nueva ni redisenar todo el sistema. La prioridad es entender el contexto, identificar el camino canonico real, evitar duplicacion y validar cada cambio.

## Clasificacion de componentes

Antes de modificar, extender, eliminar o tomar como referencia un archivo, clasificarlo con evidencia:

- `canonico`: integrado al flujo actual, registrado si corresponde, usado por controllers/services/vistas/scripts, cubierto por tests o alineado con la arquitectura vigente.
- `legacy`: poco referenciado, reemplazado por otro flujo, con nombres historicos o ramas muertas, o con logica vieja en controllers/Razor/JS.
- `duplicado/paralelo`: convive con otra implementacion mas nueva o completa.
- `incierto`: su rol no esta claro.

Regla operativa:

- canonico: consolidar, mejorar y proteger;
- legacy: no expandir, aislar o preparar retiro;
- duplicado/paralelo: no reforzar, buscar el camino canonico;
- incierto: verificar referencias, DI, rutas, vistas, scripts, tests y navegacion real antes de tocar.

## Micro-lotes

Cada intervencion debe tener un foco principal:

- extraer una responsabilidad puntual;
- consolidar una implementacion canonica;
- corregir una duplicacion especifica;
- mover logica de negocio fuera de un controller;
- corregir un bug real;
- preparar una remediacion futura de menor riesgo.

Evitar mezclar refactor tecnico grande, cambio funcional, rework visual, migraciones, limpieza global y cambios de multiples dominios.

## Backend

- Aplicar SOLID, DRY y separacion de responsabilidades con criterio incremental.
- Revisar servicios, fachadas, helpers y AutoMapper antes de crear piezas nuevas.
- No duplicar logica entre controllers y services.
- Extraer primero funciones puras, builders de viewmodel, consultas read-only, orquestaciones simples o validaciones repetidas.
- Evitar empezar por state machines complejas o operaciones multi-entidad sin cobertura minima.

## Reglas de negocio y calculos

- El backend es la autoridad para reglas sensibles.
- El frontend puede mostrar previews, pero no decidir calculos finales.
- No duplicar reglas financieras criticas en JavaScript si ya existen en backend.
- No cambiar calculos sin tests o validacion explicita.
- Si frontend y backend calculan algo parecido, documentar cual es preview y cual es autoridad.

## Testing

- Crear tests cuando aporten valor real.
- Cubrir reglas sensibles, calculos, bugs corregidos y regresiones viables.
- Si se mueve logica de controller a service, evaluar tests del service.
- No actualizar snapshots, strings o contratos UI sin confirmar que el comportamiento nuevo es correcto.
- Si hay fallas preexistentes fuera de alcance, reportarlas sin corregirlas salvo pedido explicito.

## Cierre

El cierre debe indicar:

- componentes afectados y clasificacion;
- cambios aplicados;
- pruebas o validaciones;
- riesgos y deuda remanente;
- decision sobre caminos canonicos/legacy;
- siguiente micro-lote recomendado.
