# Guia operativa para rework visual

Documento de apoyo para UI-1 en adelante. No reemplaza el codigo real ni los contratos de vista.

## Roadmap vigente

1. UI-1: Design system documental.
2. UI-2: Login.
3. UI-3: Home/Dashboard.
4. UI-4: Layout global.
5. UI-5: Componentes base.
6. Clientes/Proveedores.
7. Modulos operativos y transaccionales.

## Reglas generales

- Hacer rework progresivo, pantalla por pantalla o modulo por modulo.
- Antes de redisenar, identificar si la vista es canonica, legacy, duplicada o incierta.
- Revisar controller, viewmodel, JS, CSS y tests asociados.
- Confirmar que no haya bug funcional abierto que invalide el rework.
- Proponer cambios visuales minimos, testeables y reversibles.
- Mantener compatibilidad mobile y monitores chicos.
- Validar build y contratos de vista cuando apliquen.

## Criterio visual

Priorizar:

- claridad operativa;
- legibilidad;
- contraste alto;
- baja vision;
- mobile-first;
- accesibilidad;
- jerarquia visual para usuarios reales;
- estados vacios, error, loading y disabled entendibles;
- acciones claras y visibles.

Evitar:

- gradientes genericos;
- violetas/cyan por defecto sin justificacion;
- glassmorphism excesivo;
- transparencias fuertes;
- texto gris de bajo contraste;
- neon;
- animaciones decorativas;
- `transition: all`;
- estetica de landing page SaaS en pantallas operativas.

## Uso de skills visuales

- `ui-ux-pro-max`: auditoria visual Razor, design system dark accesible, contraste, tipografia, espaciado, jerarquia, responsive y componentes.
- `redesign-existing-projects`: mejorar pantallas existentes sin romper funcionalidad.
- `design-taste-frontend`: direccion visual general.
- `minimalist-ui`: interfaz sobria, limpia y empresarial.
- `high-end-visual-design`: elevar calidad visual sin cambiar reglas.
- `emil-design-eng`: microinteracciones, estados hover/focus/active y polish fino.

Regla comun: las skills visuales son apoyo de criterio. La decision final sale del codigo real, los contratos vigentes y las necesidades operativas del ERP.

## No tocar negocio en tareas visuales

En un micro-lote visual no modificar controllers, services, entidades, migraciones ni reglas funcionales salvo que el requerimiento lo pida explicitamente.

Si aparece una regla dudosa durante el rework, documentarla como deuda o riesgo y abrir el siguiente micro-lote tecnico.
