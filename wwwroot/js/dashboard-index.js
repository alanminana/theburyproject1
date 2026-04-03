(() => {
    window.TheBury = window.TheBury || {};

    const NOTAS_KEY = 'dashboard_notas';
    const DASHBOARD_ACTIONS = {
        SAVE_NOTE: 'save-note',
        DELETE_NOTE: 'delete-note'
    };
    const BASE_TAB_CLASSES = ['min-h-11', 'px-3', 'py-1.5', 'text-xs', 'font-semibold', 'rounded-xl'];
    const INACTIVE_TAB_CLASSES = ['bg-slate-100', 'dark:bg-slate-800', 'text-slate-600', 'dark:text-slate-300'];
    const ACTIVE_TAB_CLASSES = {
        danger: ['bg-red-500/10', 'text-red-500', 'border', 'border-red-500/20'],
        info: ['bg-blue-500/10', 'text-blue-500', 'border', 'border-blue-500/20']
    };

    let noteStatusTimeoutId = null;

    const getNotesState = () => ({
        input: document.querySelector('[data-dashboard-note-input]'),
        list: document.querySelector('[data-dashboard-note-list]'),
        status: document.querySelector('[data-dashboard-note-status]')
    });

    const getStoredNotes = () => {
        try {
            const parsed = JSON.parse(window.localStorage.getItem(NOTAS_KEY));
            return Array.isArray(parsed) ? parsed : [];
        } catch (_) {
            return [];
        }
    };

    const saveStoredNotes = (notes) => {
        window.localStorage.setItem(NOTAS_KEY, JSON.stringify(notes));
    };

    const clearStatusTimeout = () => {
        if (noteStatusTimeoutId) {
            window.clearTimeout(noteStatusTimeoutId);
            noteStatusTimeoutId = null;
        }
    };

    const setNoteStatus = (message, autoClear = false) => {
        const { status } = getNotesState();
        if (!status) return;

        status.textContent = message || '';
        clearStatusTimeout();

        if (autoClear) {
            noteStatusTimeoutId = window.setTimeout(() => {
                status.textContent = '';
                noteStatusTimeoutId = null;
            }, 2000);
        }
    };

    const createMaterialIcon = (name, className) => {
        const icon = document.createElement('span');
        icon.className = `material-symbols-outlined ${className || ''}`.trim();
        icon.textContent = name;
        return icon;
    };

    const renderNotes = () => {
        const { list } = getNotesState();
        if (!list) return;

        const notes = getStoredNotes();
        list.innerHTML = '';

        notes.forEach((note, index) => {
            const item = document.createElement('div');
            item.className = 'flex gap-3 items-start rounded-xl border border-slate-200 bg-white/90 p-3 shadow-sm dark:border-slate-800 dark:bg-slate-950/40';

            const content = document.createElement('div');
            content.className = 'flex-1 min-w-0';

            const text = document.createElement('p');
            text.className = 'text-xs break-words';
            text.textContent = note.texto;

            const timestamp = document.createElement('p');
            timestamp.className = 'text-[10px] text-slate-500 mt-1';
            const date = new Date(note.fecha);
            timestamp.textContent = date.toLocaleDateString('es-AR', {
                day: '2-digit',
                month: 'short',
                hour: '2-digit',
                minute: '2-digit'
            });

            const deleteButton = document.createElement('button');
            deleteButton.type = 'button';
            deleteButton.className = 'inline-flex size-8 shrink-0 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-red-500/10 hover:text-red-500 sm:size-7';
            deleteButton.title = 'Eliminar';
            deleteButton.dataset.dashboardAction = DASHBOARD_ACTIONS.DELETE_NOTE;
            deleteButton.dataset.dashboardNoteIndex = String(index);
            deleteButton.setAttribute('aria-label', 'Eliminar nota');
            deleteButton.appendChild(createMaterialIcon('close', 'text-sm'));

            content.append(text, timestamp);
            item.append(content, deleteButton);
            list.appendChild(item);
        });
    };

    const setInitialNoteStatus = () => {
        const notes = getStoredNotes();
        if (!notes.length) return;

        const suffix = notes.length > 1 ? 's' : '';
        setNoteStatus(`${notes.length} nota${suffix} guardada${suffix}`);
    };

    const addNote = () => {
        const { input } = getNotesState();
        if (!input) return;

        const text = input.value.trim();
        if (!text) return;

        const notes = getStoredNotes();
        notes.unshift({ texto: text, fecha: new Date().toISOString() });
        if (notes.length > 20) {
            notes.pop();
        }

        saveStoredNotes(notes);
        input.value = '';
        setNoteStatus('Guardada', true);
        renderNotes();
    };

    const deleteNote = (index) => {
        const notes = getStoredNotes();
        if (Number.isNaN(index) || index < 0 || index >= notes.length) return;

        notes.splice(index, 1);
        saveStoredNotes(notes);
        renderNotes();
    };

    const applyTabClasses = (button, isActive) => {
        const tone = button.dataset.dashboardTabTone === 'info' ? 'info' : 'danger';
        const activeClasses = ACTIVE_TAB_CLASSES[tone];
        const allVariantClasses = [...INACTIVE_TAB_CLASSES, ...ACTIVE_TAB_CLASSES.danger, ...ACTIVE_TAB_CLASSES.info];

        button.classList.add(...BASE_TAB_CLASSES);
        button.classList.remove(...allVariantClasses);
        button.classList.add(...(isActive ? activeClasses : INACTIVE_TAB_CLASSES));
        button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
    };

    const setActiveTab = (tabName) => {
        const buttons = Array.from(document.querySelectorAll('[data-dashboard-tab]'));
        const panels = Array.from(document.querySelectorAll('[data-dashboard-tab-panel]'));

        buttons.forEach((button) => {
            applyTabClasses(button, button.dataset.dashboardTab === tabName);
        });

        panels.forEach((panel) => {
            panel.classList.toggle('hidden', panel.dataset.dashboardTabPanel !== tabName);
        });
    };

    const initTabs = () => {
        const defaultButton = document.querySelector('[data-dashboard-tab][aria-pressed="true"]') || document.querySelector('[data-dashboard-tab="vencidas"]');
        if (defaultButton) {
            setActiveTab(defaultButton.dataset.dashboardTab);
        }
    };

    const initScrollAffordances = () => {
        const roots = Array.from(document.querySelectorAll('[data-oc-scroll]'));
        const affordances = roots
            .map((root) => window.TheBury.initHorizontalScrollAffordance?.(root))
            .filter(Boolean);

        const refresh = () => {
            affordances.forEach((affordance) => {
                affordance.update?.();
            });
        };

        refresh();
        window.requestAnimationFrame(() => window.requestAnimationFrame(refresh));
        window.setTimeout(refresh, 150);
    };

    const handleClick = (event) => {
        const tabButton = event.target.closest('[data-dashboard-tab]');
        if (tabButton) {
            event.preventDefault();
            setActiveTab(tabButton.dataset.dashboardTab);
            return;
        }

        const actionTarget = event.target.closest('[data-dashboard-action]');
        if (!actionTarget) {
            return;
        }

        if (actionTarget.dataset.dashboardAction === DASHBOARD_ACTIONS.SAVE_NOTE) {
            event.preventDefault();
            addNote();
            return;
        }

        if (actionTarget.dataset.dashboardAction === DASHBOARD_ACTIONS.DELETE_NOTE) {
            event.preventDefault();
            deleteNote(Number.parseInt(actionTarget.dataset.dashboardNoteIndex || '', 10));
        }
    };

    const init = () => {
        window.TheBury.autoDismissToasts?.();
        initTabs();
        renderNotes();
        setInitialNoteStatus();
        initScrollAffordances();
        document.addEventListener('click', handleClick);
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
