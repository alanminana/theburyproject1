(function () {
    'use strict';

    const root = document.getElementById('venta-create-page') || document.getElementById('venta-edit-page');
    if (!root) return;

    const ventaInicialSeed = document.getElementById('venta-inicial-json');
    if (ventaInicialSeed?.value && !window.ventaInicial) {
        try {
            window.ventaInicial = JSON.parse(ventaInicialSeed.value);
        } catch {
            window.ventaInicial = null;
        }
    }

    const tabButtons = Array.from(root.querySelectorAll('[data-step]'));
    const panels = tabButtons
        .map((button) => {
            const step = button.getAttribute('data-step');
            return step ? document.getElementById(`step-panel-${step}`) : null;
        })
        .filter(Boolean);
    const pagoSelect = document.getElementById('select-tipo-pago');
    const ventaForm = document.getElementById('venta-form');
    const fechaVentaInput = document.getElementById('FechaVenta');
    let creditoValidado = ventaForm?.dataset.creditoConfigurado === 'true';
    // VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01: distingue "ya corrió el SCORE"
    // (creditoValidado sólo pasa a true cuando el PLAN quedó guardado, no cuando la
    // elegibilidad terminó de verificarse) para que el CTA contextual de Crédito pueda
    // dejar de decir "Verificar crédito" apenas existe un resultado, sin inventar un
    // "Reverificar" ni un flag de staleness (no existe hoy). Si el crédito ya viene
    // configurado desde el servidor, el SCORE por definición ya existe también.
    let scoreDisponible = creditoValidado;

    // H4: dd/mm/yyyy a partir del value ISO (yyyy-mm-dd) de <input type="date">, mismo
    // patrón que credito-pagar-cuota.js/credito-adelanto.js — nunca `new Date(...)`, que
    // corre el día por zona horaria.
    function formatearFechaVenta(valorIso) {
        const partes = String(valorIso || '').split('-');
        return partes.length === 3 ? `${partes[2]}/${partes[1]}/${partes[0]}` : 'Fecha sin definir';
    }

    // H1: #btn-confirmar es el submit real y persistente del sidebar; su copy final
    // ("Guardar cambios"/"Guardar Cotización"/"Confirmar Transacción") lo resuelve el
    // servidor y es la fuente de verdad. Se captura una sola vez para reutilizarlo en
    // Revisión y evitar que el wizard invente un texto propio que lo contradiga.
    const btnConfirmarLabel = document.querySelector('#btn-confirmar [data-btn-confirmar-label]');
    const textoConfirmarCanonico = btnConfirmarLabel?.textContent?.trim() || '';
    // Sección "Totales y confirmación" completa del sidebar (recordatorio + submit +
    // nota de qué pasa al confirmar): sólo tiene sentido cerca de confirmar, nunca
    // mientras se está verificando/configurando el crédito (ver actualizarAccionPrincipal).
    const sidebarTotales = document.getElementById('venta-sidebar-totales');

    // El cotizador embebido (paso Cotizar) trae sus propios nodos [data-side-total]:
    // los maneja cotizacion-simulador.js y el resumen de la venta no debe pisarlos.
    const COTIZADOR_SELECTOR = '[data-cotizacion-simulador]';

    function setText(selector, value) {
        root.querySelectorAll(selector).forEach((node) => {
            if (node.closest(COTIZADOR_SELECTOR)) return;
            if (node.textContent !== value) {
                node.textContent = value;
            }
        });
    }

    function getText(id, fallback) {
        const el = document.getElementById(id);
        const text = el?.textContent?.trim();
        return text || fallback;
    }

    function setActiveStep(step) {
        if (!step) return;

        // H7: el banner de "completá los datos requeridos" está atado al paso donde
        // se originó (mostrarRequisitoCredito); al cambiar de paso pierde contexto.
        ocultarFeedback();

        // Hook de layout: con Cotizar activo el CSS oculta [data-venta-workspace]
        // (grilla de la venta + barra móvil) para que el simulador ocupe el ancho.
        root.dataset.pasoActivo = step;

        tabButtons.forEach((button) => {
            const active = button.getAttribute('data-step') === step;
            button.classList.toggle('vm-step-tab--active', active);
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-selected', active ? 'true' : 'false');
            button.tabIndex = active ? 0 : -1;
        });

        panels.forEach((panel) => {
            const active = panel.id === `step-panel-${step}`;
            panel.classList.toggle('hidden', !active);
            panel.hidden = !active;
        });

        actualizarAccionPrincipal(step);

        // BUG reportado: el configurador embebido de Crédito Personal (anticipo/cuotas)
        // sólo se cargaba si el operador clickeaba la pestaña "Crédito" a mano (ver
        // listener en venta-credito-embebido.js sobre #step-btn-credito). La navegación
        // normal del wizard llega a este paso vía avanzar() → setActiveStep(), sin pasar
        // por ese click, así que el panel quedaba vacío. Este evento le avisa al
        // configurador que el paso cambió sin importar cómo se llegó.
        document.dispatchEvent(new CustomEvent('venta:wizard-paso-activo', { detail: { step } }));
    }

    function requiereCredito() {
        return pagoSelect?.value === pagoSelect?.dataset.creditoPersonalValue;
    }

    function pasosVisibles() {
        return tabButtons.filter((button) => !button.hidden);
    }

    function actualizarNumeracionPasos() {
        const visibles = pasosVisibles();
        visibles.forEach((button, index) => {
            button.setAttribute('aria-posinset', String(index + 1));
            button.setAttribute('aria-setsize', String(visibles.length));
            const numero = button.querySelector('.vm-step-tab__num');
            if (numero) numero.textContent = String(index + 1);
        });
    }

    function actualizarAccionPrincipal(step) {
        const esRevision = step === 'revision';
        const esPago = step === 'pago';
        // En Cotizar la acción real no es la venta: el simulador tiene su propio
        // endpoint (/api/cotizacion/simular) y su propio botón. Delegamos en él en
        // vez de conectar el paso al submit de #venta-form.
        const esCotizar = step === 'cotizar';
        const esCredito = step === 'credito';

        // H1 (extendido por VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01): un único
        // texto por paso para toda autoridad visual de avanzar/confirmar. En Crédito la
        // acción real cambia con el estado (verificar → guardar configuración → continuar
        // a revisión) — antes todo el paso compartía "Verificar crédito"/"Revisar
        // operación" con el submit del sidebar, que en realidad nunca verificaba ni
        // guardaba nada (sólo avanzaba). Ver bloque "sidebarTotales" más abajo.
        let texto;
        let accion;
        if (esCotizar) {
            texto = 'Simular cotización';
            accion = 'simular-cotizacion';
        } else if (esRevision) {
            texto = textoConfirmarCanonico;
            accion = 'submit';
        } else if (esPago) {
            texto = requiereCredito() ? 'Continuar a crédito' : 'Revisar operación';
            accion = 'next';
        } else if (esCredito) {
            if (creditoValidado) {
                texto = 'Continuar a revisión';
                accion = 'continuar-revision';
            } else if (scoreDisponible) {
                texto = 'Guardar configuración';
                accion = 'guardar-configuracion';
            } else {
                texto = 'Verificar crédito';
                accion = 'verify-credit';
            }
        } else {
            texto = 'Siguiente';
            accion = 'next';
        }

        // VENTA-CREDITO-REDESIGN-VISUAL-CIERRE-01: una vez que el SCORE ya corrió
        // ("guardar-configuracion"/"continuar-revision"), el CTA contextual tiene un botón
        // real equivalente dentro del configurador embebido (mismos data-hook que ya
        // delega el click más abajo) — mostrar los dos a la vez duplica el mismo CTA
        // primario (regla de cierre visual: un solo CTA primario visible durante
        // Crédito). Sólo "Verificar crédito" (SCORE todavía no corrió) no tiene
        // equivalente dentro del Plan — ese sub-estado sigue mostrando el CTA global
        // normalmente. Mismo patrón classList+atributo que ya usa setActiveStep() para
        // los paneles (necesario porque las utilities de Tailwind como "inline-flex"
        // ganan por Cascade Layers a un solo mecanismo de ocultamiento).
        const ocultarCtaGlobal = esCredito && accion !== 'verify-credit';

        root.querySelectorAll('[data-wizard-primary]').forEach((button) => {
            button.dataset.wizardAction = accion;
            const label = button.querySelector('[data-wizard-primary-label]');
            if (label) {
                label.textContent = texto;
            } else {
                button.textContent = texto;
            }
            button.classList.toggle('hidden', ocultarCtaGlobal);
            button.hidden = ocultarCtaGlobal;
        });

        // VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01: durante Crédito, #btn-confirmar
        // (submit real del sidebar) nunca ejecuta verificar/guardar/continuar — sólo hace
        // avanzar()/submit nativo, que en este paso siempre queda bloqueado (el paso
        // siguiente no se habilita hasta que el plan está configurado). Compartir el mismo
        // copy que el CTA contextual lo hacía pasar por un segundo "Verificar crédito" o
        // "Guardar configuración" que al clickear no hacía ninguna de las dos cosas (bug
        // de origen del audit: 2/3 CTAs "Verificar crédito" simultáneos). Se oculta sólo en
        // este paso; en el resto del wizard su copy sigue sincronizado como siempre, porque
        // ahí avanzar() sí es la acción real que el texto promete.
        if (sidebarTotales) sidebarTotales.hidden = esCredito;

        if (btnConfirmarLabel && !esCredito) {
            btnConfirmarLabel.textContent = texto;
        }
    }

    function actualizarPasoCredito() {
        const tab = document.getElementById('step-btn-credito');
        const panel = document.getElementById('step-panel-credito');
        const activo = requiereCredito();
        if (!tab || !panel) return;

        tab.hidden = !activo;
        panel.hidden = !activo;
        tab.classList.toggle('hidden', !activo);
        if (!activo) creditoValidado = false;
        actualizarNumeracionPasos();
    }

    // Pestañas grisadas hasta completar el paso previo: Cliente siempre habilitado,
    // Productos requiere cliente, y el resto (Pago/Crédito/Revisión) requiere
    // además al menos un producto cargado.
    // Nota: se lee del hidden de cliente y de las filas de la tabla de detalle
    // (no de #hero-cliente/#hero-detalles-count, que no existen en el DOM actual).
    function refreshStepGating() {
        const clienteListo = (parseInt(document.getElementById('hdn-cliente-id')?.value, 10) || 0) > 0;
        const productosListo = (document.getElementById('tbody-detalles')?.children.length || 0) > 0;

        actualizarPasoCredito();
        const requisitos = {
            // Cotizar es el punto de entrada de Create: nunca depende de la venta.
            cotizar: () => true,
            cliente: () => true,
            productos: () => clienteListo,
            pago: () => clienteListo && productosListo,
            credito: () => clienteListo && productosListo,
            revision: () => clienteListo && productosListo && (!requiereCredito() || creditoValidado)
        };

        tabButtons.forEach((button) => {
            const step = button.getAttribute('data-step');
            const habilitado = (requisitos[step] || (() => clienteListo && productosListo))();
            const disponible = habilitado && !button.hidden;
            button.disabled = !disponible;
            button.setAttribute('aria-disabled', String(!disponible));
            if (!disponible) button.tabIndex = -1;
        });

        const activo = tabButtons.find((button) => button.getAttribute('aria-selected') === 'true');
        if (activo?.disabled || activo?.hidden) {
            const disponible = [...tabButtons].reverse().find((button) => !button.hidden && !button.disabled);
            setActiveStep(disponible?.getAttribute('data-step') || tabButtons[0]?.getAttribute('data-step'));
            return;
        }

        actualizarAccionPrincipal(activo?.getAttribute('data-step'));

        // H7: si lo que bloqueaba avanzar ya se resolvió sin cambiar de paso (ej. se
        // completó el dato faltante mientras el banner seguía visible), limpiarlo.
        // Si el paso siguiente sigue deshabilitado, el requisito sigue sin cumplirse
        // y el banner no se toca.
        const visibles = pasosVisibles();
        const siguientePaso = visibles[visibles.indexOf(activo) + 1];
        if (siguientePaso && !siguientePaso.disabled) {
            ocultarFeedback();
        }
    }

    function refreshSummary() {
        const cliente = getText('hero-cliente', 'Sin seleccionar');
        const items = getText('hero-detalles-count', '0 productos');
        const pago = getText('hero-tipo-pago', 'Sin definir');
        const subtotal = getText('total-subtotal', '$0,00');
        const descuento = getText('total-descuento', '-$0,00');
        const iva = getText('total-iva', '$0,00');
        const total = getText('total-final', '$0,00');

        setText('[data-side-cliente], [data-rev-cliente]', cliente);
        setText('[data-side-items], [data-rev-items]', items);
        setText('[data-side-pago], [data-rev-pago], [data-pago-summary]', pago);
        setText('[data-side-subtotal], [data-rev-subtotal]', subtotal);
        setText('[data-side-descuento], [data-rev-descuento]', descuento);
        setText('[data-side-iva], [data-rev-iva]', iva);
        setText('[data-side-total], [data-rev-total], [data-mobile-total]', total);
        setText('[data-conf-cliente]', cliente === 'Sin seleccionar' ? 'Cliente sin seleccionar' : cliente);
        setText('[data-conf-items]', items);
        setText('[data-conf-pago]', pago === 'Sin definir' ? 'Pago sin definir' : pago);
        setText('[data-conf-total]', total);
        // H4: misma autoridad que ya decide si la venta requiere Crédito Personal
        // (requiereCredito) y la fecha real de la operación (#FechaVenta) — antes estos dos
        // nodos quedaban con el texto estático del markup ("Crédito no requerido"/"Fecha sin
        // definir"), sin ningún escritor JS que los actualizara.
        setText('[data-conf-credito]', requiereCredito() ? 'Crédito Personal' : 'Crédito no requerido');
        setText('[data-rev-fecha]', formatearFechaVenta(fechaVentaInput?.value));

        const estado = root.querySelector('#vm-estado-global');
        if (estado) {
            const completo = cliente !== 'Sin seleccionar' && !items.startsWith('0 ') && pago !== 'Sin definir';
            estado.textContent = completo ? 'Lista para revisar' : 'Incompleta';
            estado.classList.toggle('text-emerald-300', completo);
        }

        refreshStepGating();
        refreshRevisionCredito();
    }

    // ── Crédito Personal en Revisión (VENTA-CREDITO-ARQUITECTURA-VISUAL-02) ────────────
    // La sección financiera/evaluación se pinta desde configurar-venta-credito.js (misma
    // autoridad que Crédito, ver actualizarPlanResumen/actualizarSemaforo). Acá sólo se
    // decide si la sección se muestra (mismo requiereCredito() que gobierna el resto del
    // wizard) y se deriva un resumen de "Elegibilidad/Pendientes/Excepción documental" a
    // partir de paneles que venta-create.js ya pinta — se lee su estado visible, nunca se
    // recalcula la elegibilidad ni se duplica el formulario de resolución.
    function contarPendientesCredito() {
        let pendientes = 0;
        const documentacion = document.getElementById('panel-documentacion-faltante');
        if (documentacion && !documentacion.classList.contains('hidden')) pendientes += 1;
        const cupo = document.getElementById('panel-cupo-insuficiente');
        if (cupo && !cupo.classList.contains('hidden')) pendientes += 1;
        const mora = document.getElementById('panel-alerta-mora');
        if (mora && !mora.classList.contains('hidden') && mora.classList.contains('bg-red-500/10')) pendientes += 1;
        document.querySelectorAll('#lista-motivos > div').forEach((item) => {
            if (item.className.includes('border-red-500')) pendientes += 1;
        });
        return pendientes;
    }

    function refreshRevisionCredito() {
        const seccion = document.getElementById('revision-credito-personal');
        if (!seccion) return;

        const activa = requiereCredito();
        seccion.hidden = !activa;
        seccion.classList.toggle('hidden', !activa);
        if (!activa) return;

        const estadoTexto = getText('verificacion-estado', '');
        const elegibilidad = !estadoTexto
            ? 'Pendiente de verificar'
            : (/NO VIABLE/i.test(estadoTexto) ? 'No viable' : 'Viable');
        setText('[data-rev-credito-elegibilidad]', elegibilidad);

        const excepcionResumen = document.getElementById('excepcion-confirmada-resumen');
        const excepcionAplicada = Boolean(excepcionResumen && !excepcionResumen.classList.contains('hidden'));
        setText('[data-rev-credito-excepcion]', excepcionAplicada ? 'Aplicada' : 'No aplica');

        const pendientes = contarPendientesCredito();
        setText('[data-rev-credito-pendientes]', String(pendientes));

        const nota = document.getElementById('rev-credito-pendientes-nota');
        if (nota) nota.classList.toggle('hidden', pendientes === 0);
    }

    tabButtons.forEach((button) => {
        button.addEventListener('click', () => {
            if (button.disabled) return;
            setActiveStep(button.getAttribute('data-step'));
        });
    });

    function avanzar() {
        const activos = pasosVisibles();
        const actualIndex = activos.findIndex((button) => button.getAttribute('aria-selected') === 'true');
        const actual = activos[actualIndex];
        const siguiente = activos[actualIndex + 1];
        if (siguiente && !siguiente.disabled) {
            setActiveStep(siguiente.getAttribute('data-step'));
            siguiente.focus({ preventScroll: true });
            document.getElementById(`step-panel-${siguiente.getAttribute('data-step')}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            return;
        }

        if (!siguiente) return; // ya es el último paso visible, nada que avanzar

        // BUG reportado: "Guardar cambios"/"Siguiente" no hacía nada visible cuando el
        // paso siguiente estaba bloqueado (típicamente Revisión, mientras Crédito Personal
        // no tiene el plan configurado — aplicar la excepción documental no alcanza, sigue
        // haciendo falta anticipo/cuotas/contrato). Avisar en vez de quedar mudo.
        console.debug('[venta-wizard] avanzar() bloqueado', {
            actual: actual?.getAttribute('data-step'),
            siguiente: siguiente.getAttribute('data-step'),
            requiereCredito: requiereCredito(),
            creditoValidado
        });

        if (actual?.getAttribute('data-step') === 'credito' && requiereCredito() && !creditoValidado) {
            mostrarRequisitoCredito(
                'Todavía falta configurar el crédito personal (anticipo, cuotas y contrato) antes de poder guardar. Aplicar la excepción no reemplaza esa configuración.',
                '#panel-configuracion-credito'
            );
            return;
        }
        mostrarRequisitoCredito('Completá los datos requeridos en este paso antes de continuar.', null);
    }

    function mostrarRequisitoCredito(mensaje, selector) {
        const alerta = document.getElementById('wizard-feedback');
        if (alerta) {
            alerta.textContent = mensaje;
            alerta.hidden = false;
            // "hidden" (atributo) no alcanza: el <p> también trae la clase Tailwind
            // "hidden" (display:none) desde el markup inicial, y esa clase gana la
            // cascada. Sin sacarla, el mensaje queda invisible aunque el atributo ya
            // esté en false — el bug reportado ("no pasa nada" al tocar Siguiente).
            alerta.classList.remove('hidden');
        }
        const requisito = selector ? document.querySelector(selector) : null;
        requisito?.focus({ preventScroll: true });
        requisito?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    // H7: limpia el banner de requisito pendiente cuando el contexto que lo originó
    // ya no aplica (cambio de paso vía setActiveStep, o el dato faltante se completó
    // sin cambiar de paso vía refreshStepGating). No se llama mientras el requisito
    // siga sin cumplirse, para no ocultar un error todavía válido en el paso actual.
    function ocultarFeedback() {
        const alerta = document.getElementById('wizard-feedback');
        if (!alerta || alerta.hidden) return;
        alerta.hidden = true;
        alerta.classList.add('hidden');
        alerta.textContent = '';
    }

    function verificarCredito() {
        const clienteId = parseInt(document.getElementById('hdn-cliente-id')?.value, 10) || 0;
        const hayProductos = (document.getElementById('tbody-detalles')?.children.length || 0) > 0;
        if (!clienteId) {
            mostrarRequisitoCredito('Seleccioná un cliente antes de verificar el crédito.', '#input-buscar-cliente');
            return;
        }
        if (!hayProductos) {
            mostrarRequisitoCredito('Agregá al menos un producto antes de verificar el crédito.', '#input-buscar-producto');
            return;
        }

        document.dispatchEvent(new CustomEvent('venta:solicitar-verificacion-credito'));
    }

    ventaForm?.addEventListener('submit', (event) => {
        const activo = pasosVisibles().find((button) => button.getAttribute('aria-selected') === 'true');
        if (activo?.getAttribute('data-step') !== 'revision') {
            event.preventDefault();
            avanzar();
        }
    });

    root.querySelectorAll('[data-wizard-primary]').forEach((button) => {
        button.addEventListener('click', () => {
            if (button.dataset.wizardAction === 'submit') {
                document.getElementById('btn-confirmar')?.click();
                return;
            }
            if (button.dataset.wizardAction === 'verify-credit') {
                verificarCredito();
                return;
            }
            // VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01: el CTA contextual delega en
            // el botón real que ya vive dentro del configurador embebido (mismo id/data-hook/
            // JS de venta-credito-embebido.js) — no duplica su lógica de guardado ni de
            // navegación, sólo simula el click para quien use el CTA de arriba en vez de
            // bajar hasta "Configurar plan".
            if (button.dataset.wizardAction === 'guardar-configuracion') {
                const btnGuardar = document.querySelector('[data-credito-embebido-confirmar]');
                if (btnGuardar) {
                    btnGuardar.click();
                } else {
                    // SCORE ya corrió pero el cliente sigue bloqueado (documentación/cupo/
                    // mora sin resolver): el configurador embebido responde con error y
                    // nunca llega a renderizar "Guardar configuración" — sin este fallback
                    // el CTA quedaba en un no-op silencioso (bug encontrado en vivo con
                    // Playwright, cliente con documentación+mora+cupo insuficiente).
                    mostrarRequisitoCredito(
                        'Resolvé los bloqueantes de crédito (documentación, cupo o mora) antes de guardar la configuración.',
                        '#panel-verificacion-crediticia'
                    );
                }
                return;
            }
            if (button.dataset.wizardAction === 'continuar-revision') {
                const btnContinuar = document.querySelector('[data-credito-embebido-continuar-revision]');
                if (btnContinuar) {
                    btnContinuar.click();
                } else {
                    // El plan ya está configurado (creditoValidado=true) pero el fragmento
                    // embebido todavía no renderizó su propio botón "Continuar a revisión"
                    // (p.ej. justo después de guardar, antes de la recarga del fragmento):
                    // avanzar() ya resuelve lo mismo (el paso Revisión queda habilitado).
                    avanzar();
                }
                return;
            }
            if (button.dataset.wizardAction === 'simular-cotizacion') {
                document.getElementById('cotizacion-simular')?.click();
                return;
            }
            avanzar();
        });

    });

    ventaForm?.addEventListener('keydown', (event) => {
        if (event.key !== 'Enter' || event.isComposing || event.target instanceof HTMLTextAreaElement) return;
        // Dentro del cotizador embebido, Enter pertenece a sus buscadores: no debe
        // avanzar el wizard ni tocar la venta.
        if (event.target instanceof Element && event.target.closest(COTIZADOR_SELECTOR)) return;
        // VENTA-CREDITO-ARQUITECTURA-VISUAL-02 (§16): Enter sobre un <summary> pertenece
        // a su propio <details> nativo (abrir/cerrar) — el handler global de avance no
        // debe interceptarlo. Espacio ya funcionaba porque el navegador lo resuelve antes
        // de que llegue acá; Enter en cambio sí llega a este listener y sin esta exclusión
        // el wizard hacía preventDefault()+avanzar() en vez de dejar que <details> abra.
        if (event.target instanceof Element && event.target.closest('summary')) return;
        const activo = pasosVisibles().find((button) => button.getAttribute('aria-selected') === 'true');
        if (activo?.getAttribute('data-step') === 'revision') return;
        event.preventDefault();
        avanzar();
    });

    tabButtons.forEach((button) => {
        button.addEventListener('keydown', (event) => {
            const visibles = pasosVisibles().filter((tab) => !tab.disabled);
            const index = visibles.indexOf(button);
            if (index < 0) return;

            let nextIndex = null;
            if (event.key === 'ArrowRight') nextIndex = (index + 1) % visibles.length;
            if (event.key === 'ArrowLeft') nextIndex = (index - 1 + visibles.length) % visibles.length;
            if (event.key === 'Home') nextIndex = 0;
            if (event.key === 'End') nextIndex = visibles.length - 1;
            if (nextIndex === null) return;

            event.preventDefault();
            const next = visibles[nextIndex];
            setActiveStep(next.getAttribute('data-step'));
            next.focus({ preventScroll: true });
            next.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        });
    });

    pagoSelect?.addEventListener('change', () => {
        creditoValidado = false;
        scoreDisponible = false;
        refreshStepGating();
    });
    // H4: el resto del resumen se refresca vía MutationObserver sobre los nodos "hero-*"
    // (más abajo); la fecha no tiene un nodo hero equivalente que observar, así que necesita
    // su propio listener directo sobre #FechaVenta.
    fechaVentaInput?.addEventListener('change', refreshSummary);
    document.addEventListener('venta:credito-validado', (event) => {
        // La elegibilidad (o una excepción aplicada) permite abrir el configurador, pero
        // Revisión sólo se habilita cuando el configurador canónico embebido confirmó el
        // plan (configurado=true). "aprobado" sin "configurado" ya no alcanza: antes
        // desbloqueaba Revisión con la sola elegibilidad, sin que existiera todavía
        // ningún plan de cuotas configurado.
        //
        // Ojo: no todo evento habla de "configurado". La verificación automática de
        // elegibilidad (venta-create.js) dispara sólo {aprobado} en cada chequeo —
        // incluido el que corre solo al cargar la página—, sin opinar sobre si el
        // crédito ya está configurado. Tratar esa ausencia como "false" pisaba la
        // semilla real del servidor (Model.CreditoConfigurado) apenas cargaba la
        // página. Sólo los eventos que informan "configurado" explícitamente pueden
        // cambiar este estado; el resto no lo toca.
        if (requiereCredito()) {
            // VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01: venta-create.js dispara este
            // evento con {aprobado} en cada corrida de verificarElegibilidadAuto (incluida
            // la automática al cargar cliente/productos), sin opinar sobre "configurado".
            // La sola presencia de "aprobado" ya significa que el SCORE corrió al menos una
            // vez — es la señal que actualizarAccionPrincipal necesita para dejar de decir
            // "Verificar crédito" una vez que ya no hace falta verificar de nuevo.
            if (typeof event.detail?.aprobado === 'boolean') {
                scoreDisponible = true;
            }
            if (typeof event.detail?.configurado === 'boolean') {
                creditoValidado = event.detail.configurado;
            }
        } else {
            creditoValidado = Boolean(event.detail?.aprobado);
        }
        refreshStepGating();
        refreshRevisionCredito();
    });

    const observer = new MutationObserver(refreshSummary);
    ['hero-cliente', 'hero-detalles-count', 'hero-tipo-pago', 'total-subtotal', 'total-descuento', 'total-iva', 'total-final']
        .map((id) => document.getElementById(id))
        .filter(Boolean)
        .forEach((node) => observer.observe(node, { childList: true, characterData: true, subtree: true }));

    // Estado final/bloqueantes de Crédito en Revisión: los paneles que informan
    // documentación/cupo/mora/motivos/excepción/resultado de verificación los pinta
    // venta-create.js directamente (no pasan por hero-*), así que refreshRevisionCredito
    // necesita su propio observer sobre esos nodos.
    const revisionCreditoObserver = new MutationObserver(refreshRevisionCredito);
    ['panel-documentacion-faltante', 'panel-cupo-insuficiente', 'panel-alerta-mora', 'lista-motivos', 'excepcion-confirmada-resumen', 'verificacion-estado']
        .map((id) => document.getElementById(id))
        .filter(Boolean)
        .forEach((node) => revisionCreditoObserver.observe(node, { attributes: true, attributeFilter: ['class'], childList: true, subtree: true, characterData: true }));

    const initialStep = tabButtons.find((button) => button.getAttribute('aria-selected') === 'true')?.getAttribute('data-step')
        || tabButtons[0]?.getAttribute('data-step');
    setActiveStep(initialStep);
    actualizarNumeracionPasos();
    refreshSummary();

    // API pública para que venta-create.js pueda navegar al paso de un campo con error
    // y refrescar el grisado de pestañas cuando cambian cliente/productos/pago.
    window.VentaWizard = window.VentaWizard || {};
    window.VentaWizard.setActiveStep = setActiveStep;
    window.VentaWizard.refreshStepGating = refreshStepGating;
})();
