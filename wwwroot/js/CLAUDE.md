# JavaScript — TheBuryProject

Estas reglas se cargan solo al trabajar bajo `wwwroot/js/`.

- Identificar qué archivo gobierna realmente el comportamiento y en qué vistas se carga.
- Preservar ids, nombres y atributos `data-*` usados como contratos.
- No mover reglas de negocio al frontend.
- No duplicar listeners ni registrarlos de nuevo al abrir modales o recargar parciales.
- No migrar a TypeScript ni cambiar la arquitectura JavaScript sin pedido explícito.
- Evitar esperas fijas cuando exista un evento o estado observable.
- Para bugs: reproducir, revisar consola y requests, localizar el script real, corregir el menor alcance y validar con Playwright.
- Verificar que los cambios no rompan Razor, endpoints, validación ni accesibilidad.
