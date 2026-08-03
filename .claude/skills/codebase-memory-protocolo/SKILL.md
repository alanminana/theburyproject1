---
name: codebase-memory-protocolo
description: Protocolo de uso de codebase-memory-mcp en TheBuryProject: cuándo consultarlo, qué consultar antes de editar, reglas de validación y contenido sensible excluido del índice. Usar en cambios que involucren varios archivos, dependencias cruzadas, controllers pesados, services, views, JavaScript o componentes legacy.
---

## Protocolo de `codebase-memory-mcp`

### Qué resuelve

Es la herramienta principal para entender la estructura interna del repositorio:

* símbolos;
* referencias;
* llamadas entrantes y salientes;
* dependencias;
* rutas;
* arquitectura;
* impacto probable;
* componentes relacionados;
* posible código muerto;
* conexiones entre backend, frontend y tests.

No reemplaza la lectura directa del código.

### Usar `codebase-memory-mcp` cuando

* intervienen varios archivos;
* existen dependencias entre controllers, services, models, views o JavaScript;
* hay componentes legacy;
* se necesita detectar referencias;
* se investiga código muerto;
* se necesita conocer el impacto probable;
* el controller es pesado;
* el flujo cruza frontend, backend y tests;
* no está claro cuál es el camino canónico.

### Usarlo primero cuando el cambio involucre

* varios archivos;
* dependencias cruzadas;
* controllers pesados;
* services;
* modelos;
* views;
* JavaScript;
* legacy;
* flujo frontend/backend/tests.

### Antes de editar tareas medianas o grandes

Consultar según corresponda:

* arquitectura relacionada;
* símbolos;
* llamadas entrantes;
* llamadas salientes;
* referencias;
* impacto probable;
* dependencias;
* rutas;
* tests relacionados.

Usar sus resultados para reducir exploración innecesaria y leer solo los archivos relevantes.

### Reglas

* El grafo no reemplaza la lectura directa.
* No modificar código únicamente porque dos símbolos aparezcan relacionados.
* Validar referencias reales, DI, rutas, vistas, scripts y tests.
* Si el índice está desactualizado, documentarlo.
* Si falla o no cubre el caso, continuar con lectura directa.
* No indexar ni consultar contenido sensible excluido:

  * keys;
  * `.auth`;
  * uploads;
  * secretos;
  * credenciales;
  * archivos locales sensibles.
