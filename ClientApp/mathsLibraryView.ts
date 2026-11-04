import { getMathsLibrary, getMathsNote, mathsFileUrl, saveMathsNote, saveMathsStatus } from './api.js';
import { byId, el, showError, showProgress } from './dom.js';
import { BOOK_SCOPE_KEY } from './mathsBookView.js';
import { renderMarkdown } from './markdown.js';
import { MATHS_STATUSES, MathsLibrary, MathsMaterial, MathsTopic } from './mathsTypes.js';

const SAVE_DELAY_MILLISECONDS = 900;
const LAST_TOPIC_KEY = 'srehub.maths.lastTopic';

const NOTE_TEMPLATE = (topic: string): string => `# ${topic}

## Aim of the class

## Key formulas and shortcuts

## Examples to solve on the board

## Common mistakes

## Homework
`;

const STATUS_CLASS: Record<string, string> = {
    'To prepare': 'todo',
    Prepared: 'prepared',
    Taught: 'taught'
};

export function initMathsLibraryView(): void {
    const toc = byId('maths-toc');
    const summary = byId('maths-summary');
    const filter = byId<HTMLInputElement>('maths-filter');
    const empty = byId('maths-empty');
    const topicPanel = byId('maths-topic');
    const title = byId('maths-topic-title');
    const coverage = byId('maths-topic-coverage');
    const statusSelect = byId<HTMLSelectElement>('maths-status');
    const previous = byId<HTMLButtonElement>('maths-prev');
    const next = byId<HTMLButtonElement>('maths-next');
    const materials = byId('maths-materials');
    const notesPanel = byId('maths-notes-panel');
    const notesHome = byId('maths-notes-home');
    const notes = byId<HTMLTextAreaElement>('maths-notes');
    const saveState = byId('maths-save-state');
    const templateButton = byId<HTMLButtonElement>('maths-template');
    const previewButton = byId<HTMLButtonElement>('maths-preview-toggle');
    const preview = byId('maths-preview');
    const bookButton = byId<HTMLButtonElement>('maths-open-book');
    const classOverlay = byId('maths-class');
    const classTitle = byId('maths-class-title');
    const classFile = byId<HTMLSelectElement>('maths-class-file');
    const classFrame = byId<HTMLIFrameElement>('maths-class-frame');
    const classNewTab = byId<HTMLAnchorElement>('maths-class-newtab');
    const classNotesToggle = byId<HTMLButtonElement>('maths-class-notes-toggle');
    const classNotesSlot = byId('maths-class-notes-slot');
    const classClose = byId<HTMLButtonElement>('maths-class-close');

    let library: MathsLibrary | undefined;
    let current: MathsTopic | undefined;
    let loadedNoteTopic: string | undefined;
    let saveTimer: number | undefined;
    let dirty = false;
    let selectionToken = 0;

    const flatTopics = (): MathsTopic[] => (library?.parts ?? []).flatMap((part) => part.topics);

    const setPreview = (on: boolean): void => {
        preview.replaceChildren();

        if (on) {
            preview.append(notes.value.trim() ? renderMarkdown(notes.value) : el('p', 'hint', 'Nothing to preview yet.'));
        }

        preview.classList.toggle('hidden', !on);
        notes.classList.toggle('hidden', on);
        previewButton.textContent = on ? 'Edit' : 'Preview';
    };

    const persistNotes = async (keepalive = false): Promise<void> => {
        window.clearTimeout(saveTimer);

        if (!dirty || !loadedNoteTopic) {
            return;
        }

        const topic = loadedNoteTopic;
        const content = notes.value;
        dirty = false;
        saveState.textContent = 'Saving...';

        try {
            await saveMathsNote(topic, content, keepalive && content.length < 30000);
            saveState.textContent = `Saved ${new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;

            const known = flatTopics().find((item) => item.id === topic);
            if (known) {
                known.hasNotes = content.trim().length > 0;
                renderToc();
            }
        } catch (failure) {
            dirty = true;
            saveState.textContent = `Not saved: ${(failure as Error).message}`;
        }
    };

    const scheduleSave = (): void => {
        dirty = true;
        saveState.textContent = 'Editing...';
        window.clearTimeout(saveTimer);
        saveTimer = window.setTimeout(() => void persistNotes(), SAVE_DELAY_MILLISECONDS);
    };

    const matches = (topic: MathsTopic, query: string): boolean =>
        !query || topic.name.toLowerCase().includes(query)
        || topic.materials.some((material) => material.title.toLowerCase().includes(query));

    function renderToc(): void {
        if (!library) {
            return;
        }

        if (!library.exists) {
            showError(toc, `The maths folder was not found: ${library.root}`);
            return;
        }

        const query = filter.value.trim().toLowerCase();
        const groups = library.parts
            .map((part) => ({ part, topics: part.topics.filter((topic) => matches(topic, query)) }))
            .filter((group) => group.topics.length > 0);

        if (groups.length === 0) {
            toc.replaceChildren(el('p', 'hint', 'No topics match your search.'));
            return;
        }

        toc.replaceChildren(...groups.map(({ part, topics }) => {
            const section = el('section', 'maths-part');
            section.append(el('h4', 'maths-part-title', part.name));

            for (const topic of topics) {
                const button = el('button', `maths-topic-button${current?.id === topic.id ? ' active' : ''}`);
                button.type = 'button';
                button.append(
                    el('span', `maths-dot ${STATUS_CLASS[topic.status] ?? 'todo'}`),
                    el('span', 'maths-topic-name', topic.name),
                    el('span', 'maths-topic-count', topic.hasNotes ? `${topic.materials.length} \u270E` : String(topic.materials.length))
                );
                button.title = `${topic.name} - ${topic.status}`;
                button.addEventListener('click', () => void selectTopic(topic.id));
                section.append(button);
            }

            return section;
        }));
    }

    const renderSummary = (): void => {
        if (!library?.exists) {
            summary.textContent = '';
            return;
        }

        const topics = flatTopics();
        const taught = topics.filter((topic) => topic.status === 'Taught').length;
        const prepared = topics.filter((topic) => topic.status === 'Prepared').length;
        summary.textContent = `${topics.length} topics, ${library.totalFiles} files. ${taught} taught, ${prepared} prepared.`;
    };

    const buildCoverage = (topic: MathsTopic): HTMLElement[] => {
        const kinds = new Set(topic.materials.map((material) => material.kind));
        const expected: Array<[string, string]> = [
            ['Class notes', 'Class notes'],
            ['Classroom sheet', 'Classroom sheet'],
            ['Pro level sheet', 'Pro level sheet']
        ];

        return expected.map(([kind, label]) => el('span', `badge ${kinds.has(kind) ? 'ok' : 'muted'}`, kinds.has(kind) ? label : `No ${label.toLowerCase()}`));
    };

    const openClassMode = (topic: MathsTopic, material: MathsMaterial): void => {
        classTitle.textContent = topic.name;
        classFile.replaceChildren(...topic.materials.map((item) => {
            const option = el('option', undefined, `${item.kind}: ${item.title}`);
            option.value = item.name;
            return option;
        }));
        classFile.value = material.name;

        const show = (name: string): void => {
            const url = mathsFileUrl(topic.id, name);
            classFrame.src = `${url}#view=FitH`;
            classNewTab.href = url;
        };

        classFile.onchange = () => show(classFile.value);
        show(material.name);

        classNotesSlot.appendChild(notesPanel);
        classOverlay.classList.remove('hidden');
        document.body.classList.add('maths-class-open');
        classClose.focus();
    };

    const closeClassMode = (): void => {
        if (classOverlay.classList.contains('hidden')) {
            return;
        }

        void persistNotes();
        classOverlay.classList.add('hidden');
        classFrame.src = 'about:blank';
        document.body.classList.remove('maths-class-open');
        notesHome.appendChild(notesPanel);
    };

    const renderMaterials = (topic: MathsTopic): void => {
        if (topic.materials.length === 0) {
            materials.replaceChildren(el('p', 'hint', 'No PDF files in this topic yet.'));
            return;
        }

        const byKind = new Map<string, MathsMaterial[]>();
        for (const material of topic.materials) {
            byKind.set(material.kind, [...(byKind.get(material.kind) ?? []), material]);
        }

        materials.replaceChildren(...[...byKind.entries()].map(([kind, items]) => {
            const group = el('section', 'maths-material-group');
            group.append(el('h4', 'maths-material-kind', kind));

            for (const material of items) {
                const row = el('div', 'maths-material');
                const open = el('button', 'maths-material-open', material.title);
                open.type = 'button';
                open.title = 'Open in class view';
                open.addEventListener('click', () => openClassMode(topic, material));

                const link = el('a', 'link-button', 'New tab');
                link.href = mathsFileUrl(topic.id, material.name);
                link.target = '_blank';
                link.rel = 'noopener noreferrer';

                row.append(open, el('span', 'maths-material-size', `${material.sizeMb} MB`), link);
                group.append(row);
            }

            return group;
        }));
    };

    async function selectTopic(id: string): Promise<void> {
        const topic = flatTopics().find((item) => item.id === id);
        if (!topic) {
            return;
        }

        await persistNotes();

        const token = ++selectionToken;
        current = topic;
        loadedNoteTopic = undefined;
        dirty = false;
        location.hash = `math-library/${encodeURIComponent(id)}`;

        try {
            localStorage.setItem(LAST_TOPIC_KEY, id);
        } catch {
            // Remembering the last topic is a convenience only.
        }

        setPreview(false);

        empty.classList.add('hidden');
        topicPanel.classList.remove('hidden');
        title.textContent = topic.name;
        coverage.replaceChildren(...buildCoverage(topic));
        statusSelect.value = topic.status;
        renderMaterials(topic);

        const list = flatTopics();
        const index = list.indexOf(topic);
        previous.disabled = index <= 0;
        next.disabled = index >= list.length - 1;
        renderToc();

        notes.disabled = true;
        notes.value = '';
        saveState.textContent = 'Loading notes...';

        try {
            const note = await getMathsNote(id);

            if (token !== selectionToken) {
                return;
            }

            notes.value = note.content;
            loadedNoteTopic = id;
            saveState.textContent = note.updated ? `Saved ${new Date(note.updated).toLocaleString()}` : 'No notes yet';
        } catch (failure) {
            saveState.textContent = (failure as Error).message;
        } finally {
            if (token === selectionToken) {
                notes.disabled = !loadedNoteTopic;
            }
        }
    }

    const load = async (): Promise<void> => {
        showProgress(toc, 'Loading the maths library...');

        try {
            library = await getMathsLibrary();
            renderSummary();
            renderToc();

            const requested = decodeURIComponent(location.hash.replace(/^#math-library\/?/, ''))
                || localStorage.getItem(LAST_TOPIC_KEY)
                || '';
            if (requested && flatTopics().some((topic) => topic.id === requested)) {
                await selectTopic(requested);
            }
        } catch (failure) {
            showError(toc, (failure as Error).message);
        }
    };

    statusSelect.replaceChildren(...MATHS_STATUSES.map((status) => {
        const option = el('option', undefined, status);
        option.value = status;
        return option;
    }));

    statusSelect.addEventListener('change', async () => {
        if (!current) {
            return;
        }

        const topic = current;
        try {
            topic.status = (await saveMathsStatus(topic.id, statusSelect.value)).status;
            renderToc();
            renderSummary();
        } catch (failure) {
            statusSelect.value = topic.status;
            saveState.textContent = (failure as Error).message;
        }
    });

    const step = (offset: number): void => {
        const list = flatTopics();
        const target = current ? list[list.indexOf(current) + offset] : undefined;
        if (target) {
            void selectTopic(target.id);
        }
    };

    previous.addEventListener('click', () => step(-1));
    next.addEventListener('click', () => step(1));
    filter.addEventListener('input', renderToc);
    notes.addEventListener('input', scheduleSave);

    templateButton.addEventListener('click', () => {
        if (!current || notes.disabled) {
            return;
        }

        const insert = NOTE_TEMPLATE(current.name);
        notes.value = notes.value.trim() ? `${notes.value.replace(/\s+$/, '')}\n\n${insert}` : insert;
        scheduleSave();
        notes.focus();
    });

    classNotesToggle.addEventListener('click', () => {
        const hidden = classNotesSlot.classList.toggle('hidden');
        classNotesToggle.textContent = hidden ? 'Show notes' : 'Hide notes';
    });

    previewButton.addEventListener('click', () => setPreview(preview.classList.contains('hidden')));

    bookButton.addEventListener('click', async () => {
        if (!current) {
            return;
        }

        await persistNotes();
        sessionStorage.setItem(BOOK_SCOPE_KEY, `topic:${current.id}`);
        document.querySelector<HTMLButtonElement>('.nav-button[data-target="view-math-book"]')?.click();
    });

    classClose.addEventListener('click', closeClassMode);

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            closeClassMode();
        }
    });

    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') {
            void persistNotes(true);
        }
    });

    window.addEventListener('pagehide', () => void persistNotes(true));

    document.querySelector<HTMLButtonElement>('.nav-button[data-target="view-math-library"]')
        ?.addEventListener('click', () => void load());

    if (location.hash.startsWith('#math-library')) {
        void load();
    }
}
