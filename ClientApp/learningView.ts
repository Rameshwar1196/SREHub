import {
    createFolder,
    createSubject,
    deleteLearningFile,
    deleteSubject,
    getFlashcards,
    getLearningFile,
    getSubject,
    getSubjects,
    moveEntry,
    renameEntry,
    renameSubject,
    resolveLink,
    saveNote,
    searchLearning,
    setProgress,
    uploadFiles
} from './api.js';
import { askMove, askText } from './dialogs.js';
import { bindCollapse, byId, el, formatBytes, showError } from './dom.js';
import { EntryLocation, Flashcard, LearningFileContent, LearningNode, SubjectDetail, SubjectSummary } from './learningTypes.js';
import { showView } from './navigation.js';
import { officeKindOf, renderOffice } from './officeViewer.js';

const KIND_ICON: Record<string, string> = {
    folder: '[dir]',
    markdown: '[md]',
    text: '[txt]',
    pdf: '[pdf]',
    image: '[img]',
    word: '[doc]',
    excel: '[xls]',
    powerpoint: '[ppt]',
    binary: '[bin]'
};

const state = {
    subjects: [] as SubjectSummary[],
    subjectId: null as string | null,
    detail: null as SubjectDetail | null,
    file: null as LearningFileContent | null,
    cards: [] as Flashcard[],
    cardIndex: 0
};

export async function initLearningView(): Promise<void> {
    wireSidebar();
    wireSearch();
    wireModes();
    wireAddContent();
    wireFlashcards();

    await reloadSubjects();
}

async function reloadSubjects(selectId?: string): Promise<void> {
    try {
        state.subjects = await getSubjects();
        renderSubjectList();

        const target = selectId ?? state.subjectId ?? state.subjects[0]?.id;
        if (target) {
            await openSubject(target);
        }
    } catch (error) {
        showError(byId('subject-list-wrapper'), (error as Error).message);
    }
}

function renderSubjectList(): void {
    const filter = byId<HTMLInputElement>('subject-filter').value.trim().toLowerCase();
    const list = byId('subject-list');
    list.replaceChildren();

    const visible = state.subjects.filter((subject) =>
        !filter || subject.title.toLowerCase().includes(filter) || subject.id.toLowerCase().includes(filter));

    if (visible.length === 0) {
        list.appendChild(el('li', 'muted-item', 'No subjects match.'));
        return;
    }

    for (const subject of visible) {
        const item = el('li');
        const button = el('button', `subject-item${subject.id === state.subjectId ? ' active' : ''}`);
        button.style.setProperty('--hue', String(hueFor(subject.id)));

        button.append(
            el('span', 'subject-title', subject.title),
            el('span', 'subject-meta', `${subject.completedCount}/${subject.fileCount} done`),
            progressBar(subject.completedCount, subject.fileCount)
        );

        button.addEventListener('click', () => {
            showView('view-learning');
            void openSubject(subject.id);
        });
        item.appendChild(button);
        list.appendChild(item);
    }
}

function hueFor(value: string): number {
    let hash = 0;
    for (let i = 0; i < value.length; i++) {
        hash = (hash * 31 + value.charCodeAt(i)) % 360;
    }

    return hash;
}

function progressBar(done: number, total: number): HTMLElement {
    const track = el('span', 'progress-track');
    const fill = el('span', 'progress-fill');
    fill.style.width = `${total === 0 ? 0 : Math.round((done / total) * 100)}%`;
    track.appendChild(fill);
    return track;
}

async function openSubject(subjectId: string): Promise<void> {
    const header = byId('subject-header');

    try {
        state.subjectId = subjectId;
        state.detail = await getSubject(subjectId);
        renderSubjectList();
        renderSubjectHeader(state.detail);
        renderTree(state.detail);
        byId<HTMLInputElement>('note-subject').value = subjectId;

        if (state.detail.readmePath) {
            await openFile(state.detail.readmePath);
        } else {
            byId('file-view').replaceChildren(el('p', 'hint', 'Select a file from the tree.'));
        }
    } catch (error) {
        showError(header, (error as Error).message);
    }
}

function renderSubjectHeader(detail: SubjectDetail): void {
    const header = byId('subject-header');
    header.replaceChildren();
    header.style.borderLeftColor = `hsl(${hueFor(detail.id)} 75% 62%)`;

    header.appendChild(el('h2', undefined, detail.title));

    if (detail.description) {
        header.appendChild(el('p', 'hint', detail.description));
    }

    const meta = el('div', 'subject-progress');
    meta.append(
        el('span', 'muted', `${detail.fileCount} file(s) · ${detail.completedCount} marked complete`),
        progressBar(detail.completedCount, detail.fileCount)
    );
    header.appendChild(meta);

    const actions = el('div', 'header-actions');
    actions.append(
        actionButton('New folder', () => void createFolderAction(detail.id, '')),
        actionButton('Rename', () => void renameSubjectAction(detail)),
        actionButton('Delete subject', () => void removeSubject(detail), true)
    );
    header.appendChild(actions);
}

function actionButton(label: string, handler: () => void, danger = false): HTMLButtonElement {
    const button = el('button', `link-button${danger ? ' danger' : ''}`, label);
    button.type = 'button';
    button.addEventListener('click', handler);
    return button;
}

async function refreshAfter(location: EntryLocation, isDirectory: boolean): Promise<void> {
    await reloadSubjects(location.subjectId);

    if (!location.path) {
        return;
    }

    if (isDirectory) {
        renderFolder(location.path);
    } else {
        await openFile(location.path);
    }
}

async function createFolderAction(subjectId: string, parentPath: string): Promise<void> {
    const name = await askText(
        'New folder',
        'Folder name',
        '',
        'Create',
        parentPath ? `Created inside ${parentPath}/` : 'Created at the top of the subject.');

    if (!name) {
        return;
    }

    try {
        const created = await createFolder(subjectId, parentPath ? `${parentPath}/${name}` : name);
        await refreshAfter(created, true);
    } catch (error) {
        window.alert((error as Error).message);
    }
}

async function renameSubjectAction(detail: SubjectDetail): Promise<void> {
    const name = await askText(
        'Rename subject',
        'Folder name',
        detail.id,
        'Rename',
        'The folder name sets the order and the number shown in the menu, for example 13-terraform.');

    if (!name || name === detail.id) {
        return;
    }

    try {
        const renamed = await renameSubject(detail.id, name);
        state.subjectId = renamed.subjectId;
        await reloadSubjects(renamed.subjectId);
    } catch (error) {
        window.alert((error as Error).message);
    }
}

async function renameEntryAction(subjectId: string, path: string, isDirectory: boolean): Promise<void> {
    const current = path.split('/').pop() ?? path;
    const name = await askText(isDirectory ? 'Rename folder' : 'Rename file', 'New name', current, 'Rename');

    if (!name || name === current) {
        return;
    }

    try {
        await refreshAfter(await renameEntry(subjectId, path, name), isDirectory);
    } catch (error) {
        window.alert((error as Error).message);
    }
}

async function moveEntryAction(subjectId: string, path: string, isDirectory: boolean): Promise<void> {
    const name = path.split('/').pop() ?? path;
    const parent = path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
    const choice = await askMove(`Move '${name}'`, state.subjects, subjectId, parent);

    if (!choice) {
        return;
    }

    try {
        await refreshAfter(await moveEntry(subjectId, path, choice.subjectId, choice.folder), isDirectory);
    } catch (error) {
        window.alert((error as Error).message);
    }
}

async function removeSubject(detail: SubjectDetail): Promise<void> {
    const confirmed = window.confirm(
        `Delete the subject '${detail.title}' and all ${detail.fileCount} file(s) inside it?\n\nThis cannot be undone.`);

    if (!confirmed) {
        return;
    }

    try {
        await deleteSubject(detail.id);
        state.subjectId = null;
        state.detail = null;
        byId('file-view').replaceChildren();
        byId('file-tree').replaceChildren();
        byId('subject-header').replaceChildren();
        await reloadSubjects();
    } catch (error) {
        showError(byId('subject-header'), (error as Error).message);
    }
}

function renderTree(detail: SubjectDetail): void {
    const tree = byId('file-tree');
    tree.replaceChildren(buildNodeList(detail.files));
}

function buildNodeList(nodes: LearningNode[]): HTMLElement {
    const list = el('ul', 'tree-list');

    for (const node of nodes) {
        const item = el('li');

        if (node.isDirectory) {
            const group = el('details', 'tree-folder');
            const summary = el('summary');
            summary.appendChild(el('span', 'tree-name', `${KIND_ICON.folder} ${node.name}`));

            const manage = el('button', 'tree-manage', '\u22ef');
            manage.type = 'button';
            manage.title = `Open folder '${node.name}' to rename, move or delete it`;
            manage.setAttribute('aria-label', manage.title);
            manage.addEventListener('click', (event) => {
                event.preventDefault();
                event.stopPropagation();
                renderFolder(node.path);
            });
            summary.appendChild(manage);

            group.append(summary, buildNodeList(node.children));
            item.appendChild(group);
        } else {
            const button = el('button', `tree-file${node.completed ? ' done' : ''}`);
            button.append(
                el('span', 'tree-icon', KIND_ICON[node.kind] ?? '[?]'),
                el('span', 'tree-name', node.name),
                el('span', 'tree-size', formatBytes(node.size))
            );
            button.addEventListener('click', () => void openFile(node.path));
            item.appendChild(button);
        }

        list.appendChild(item);
    }

    return list;
}

async function openFile(path: string): Promise<void> {
    const view = byId('file-view');

    if (!state.subjectId) {
        return;
    }

    try {
        state.file = await getLearningFile(state.subjectId, path);
        renderFile(state.file);
    } catch (error) {
        showError(view, (error as Error).message);
    }
}

function renderFile(file: LearningFileContent): void {
    const view = byId('file-view');
    view.replaceChildren();

    const bar = el('div', 'file-bar');
    bar.appendChild(el('span', 'file-path', file.path));

    const done = el('label', 'file-toggle');
    const checkbox = el('input');
    checkbox.type = 'checkbox';
    checkbox.checked = file.completed;
    checkbox.addEventListener('change', async () => {
        try {
            await setProgress({ subjectId: file.subjectId, path: file.path, completed: checkbox.checked });
            await reloadSubjects(file.subjectId);
            await openFile(file.path);
        } catch (error) {
            checkbox.checked = !checkbox.checked;
            window.alert((error as Error).message);
        }
    });
    done.append(checkbox, el('span', undefined, 'Completed'));
    bar.appendChild(done);

    if (file.editable) {
        const edit = el('button', 'link-button', 'Edit');
        edit.addEventListener('click', () => {
            byId<HTMLDetailsElement>('add-panel').open = true;
            byId<HTMLInputElement>('note-path').value = file.path;
            byId<HTMLTextAreaElement>('note-content').value = file.text ?? '';
            byId<HTMLInputElement>('note-overwrite').checked = true;
            byId('note-path').scrollIntoView({ behavior: 'smooth', block: 'center' });
        });
        bar.appendChild(edit);
    }

    if (file.rawUrl) {
        const open = el('a', 'link-button', 'Open raw');
        open.href = file.rawUrl;
        open.target = '_blank';
        open.rel = 'noreferrer';
        bar.appendChild(open);
    }

    bar.append(
        actionButton('Rename', () => void renameEntryAction(file.subjectId, file.path, false)),
        actionButton('Move', () => void moveEntryAction(file.subjectId, file.path, false)),
        actionButton('Delete', () => void removeEntry(file.subjectId, file.path, false), true)
    );

    view.appendChild(bar);

    if (file.kind === 'markdown' && file.html) {
        const body = el('article', 'markdown');
        // Markdown is rendered server-side by Markdig with raw HTML disabled.
        body.innerHTML = file.html;
        view.appendChild(body);
        wireMarkdownLinks(body, file);
        return;
    }

    if (file.kind === 'text') {
        view.appendChild(el('pre', 'code', file.text ?? ''));
        return;
    }

    if (file.kind === 'image' && file.rawUrl) {
        const image = el('img', 'file-image');
        image.src = file.rawUrl;
        image.alt = file.name;
        view.appendChild(image);
        return;
    }

    if (file.kind === 'pdf' && file.rawUrl) {
        const frame = el('iframe', 'file-pdf');
        frame.src = file.rawUrl;
        view.appendChild(frame);
        return;
    }

    const office = officeKindOf(file.name);
    if (office && file.rawUrl) {
        const folder = file.path.includes('/') ? file.path.slice(0, file.path.lastIndexOf('/')) : '';
        void renderOffice(view, office, {
            name: file.name,
            url: file.rawUrl,
            save: async (blob) => {
                await uploadFiles(file.subjectId, folder, [new File([blob], file.name)]);
                await reloadSubjects(file.subjectId);
            }
        });
        return;
    }

    view.appendChild(el('p', 'hint', `${file.name} (${formatBytes(file.size)}) cannot be previewed. Use "Open raw".`));
}

async function removeEntry(subjectId: string, path: string, isDirectory: boolean): Promise<void> {
    const message = isDirectory
        ? `Delete the folder '${path}' and everything inside it?\n\nThis cannot be undone.`
        : `Delete '${path}'?\n\nThis cannot be undone.`;

    if (!window.confirm(message)) {
        return;
    }

    try {
        await deleteLearningFile(subjectId, path);
        state.file = null;
        await reloadSubjects(subjectId);
        byId('file-view').replaceChildren(el('p', 'hint', `Deleted '${path}'.`));
    } catch (error) {
        showError(byId('file-view'), (error as Error).message);
    }
}

function isExternal(href: string): boolean {
    return href.startsWith('//') || /^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(href);
}

function wireMarkdownLinks(body: HTMLElement, file: LearningFileContent): void {
    for (const anchor of Array.from(body.querySelectorAll<HTMLAnchorElement>('a[href]'))) {
        const href = anchor.getAttribute('href') ?? '';

        if (href.startsWith('#')) {
            anchor.addEventListener('click', (event) => {
                event.preventDefault();
                const id = decodeURIComponent(href.slice(1));
                body.querySelector(`#${CSS.escape(id)}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            });
            continue;
        }

        if (isExternal(href) || href.startsWith('/')) {
            anchor.target = '_blank';
            anchor.rel = 'noreferrer noopener';
            continue;
        }

        anchor.addEventListener('click', (event) => {
            event.preventDefault();
            void followLink(file, href);
        });
    }

    for (const image of Array.from(body.querySelectorAll<HTMLImageElement>('img[src]'))) {
        const src = image.getAttribute('src') ?? '';

        if (!src || isExternal(src) || src.startsWith('/')) {
            continue;
        }

        void resolveLink(file.subjectId, file.path, src)
            .then((target) => {
                if (target.rawUrl) {
                    image.src = target.rawUrl;
                }
            })
            .catch(() => undefined);
    }
}

async function followLink(file: LearningFileContent, href: string): Promise<void> {
    try {
        const target = await resolveLink(file.subjectId, file.path, href);

        if (!target.exists) {
            window.alert(`'${href}' does not exist in the learning library yet.`);
            return;
        }

        if (target.subjectId !== state.subjectId || (target.isDirectory && !target.path)) {
            await openSubject(target.subjectId);
        }

        if (target.isDirectory) {
            if (target.path) {
                renderFolder(target.path);
            }
            return;
        }

        await openFile(target.path);
    } catch (error) {
        window.alert((error as Error).message);
    }
}

function findNode(nodes: LearningNode[], path: string): LearningNode | null {
    for (const node of nodes) {
        if (node.path === path) {
            return node;
        }

        if (node.isDirectory) {
            const found = findNode(node.children, path);
            if (found) {
                return found;
            }
        }
    }

    return null;
}

function renderFolder(path: string): void {
    const view = byId('file-view');
    view.replaceChildren();

    const bar = el('div', 'file-bar');
    bar.appendChild(el('span', 'file-path', `${path}/`));
    bar.append(
        actionButton('New folder', () => void createFolderAction(state.subjectId ?? '', path)),
        actionButton('Rename', () => void renameEntryAction(state.subjectId ?? '', path, true)),
        actionButton('Move', () => void moveEntryAction(state.subjectId ?? '', path, true)),
        actionButton('Delete', () => void removeEntry(state.subjectId ?? '', path, true), true)
    );
    view.appendChild(bar);

    const node = state.detail ? findNode(state.detail.files, path) : null;

    if (!node || node.children.length === 0) {
        view.appendChild(el('p', 'hint', node ? 'This folder is empty.' : 'Folder not found.'));
        return;
    }

    const list = el('ul', 'folder-list');

    for (const child of node.children) {
        const item = el('li');
        const button = el('button', 'tree-file');
        button.append(
            el('span', 'tree-icon', KIND_ICON[child.kind] ?? '[?]'),
            el('span', 'tree-name', child.name),
            el('span', 'tree-size', child.isDirectory ? '' : formatBytes(child.size))
        );
        button.addEventListener('click', () => {
            if (child.isDirectory) {
                renderFolder(child.path);
            } else {
                void openFile(child.path);
            }
        });
        item.appendChild(button);
        list.appendChild(item);
    }

    view.appendChild(list);
}

function wireSidebar(): void {
    bindCollapse(
        document.querySelector<HTMLElement>('.learning-split')!,
        byId<HTMLButtonElement>('tree-toggle'),
        'srehub.files',
        'files');

    byId<HTMLInputElement>('subject-filter').addEventListener('input', renderSubjectList);

    const form = byId<HTMLFormElement>('subject-form');
    const toggle = byId<HTMLButtonElement>('subject-new-toggle');

    toggle.addEventListener('click', () => form.classList.toggle('hidden'));
    byId<HTMLButtonElement>('subject-cancel').addEventListener('click', () => form.classList.add('hidden'));

    form.addEventListener('submit', async (event) => {
        event.preventDefault();

        const name = byId<HTMLInputElement>('subject-name');
        const description = byId<HTMLInputElement>('subject-description');

        try {
            const created = await createSubject({ name: name.value.trim(), description: description.value.trim() || undefined });
            name.value = '';
            description.value = '';
            form.classList.add('hidden');
            await reloadSubjects(created.id);
        } catch (error) {
            window.alert((error as Error).message);
        }
    });
}

function wireAddContent(): void {
    byId<HTMLFormElement>('note-form').addEventListener('submit', async (event) => {
        event.preventDefault();

        if (!state.subjectId) {
            window.alert('Select a subject first.');
            return;
        }

        const path = byId<HTMLInputElement>('note-path');
        const content = byId<HTMLTextAreaElement>('note-content');
        const overwrite = byId<HTMLInputElement>('note-overwrite');

        try {
            const saved = await saveNote({
                subjectId: state.subjectId,
                path: path.value.trim(),
                content: content.value,
                overwrite: overwrite.checked
            });

            path.value = '';
            content.value = '';
            overwrite.checked = false;
            await reloadSubjects(state.subjectId);
            await openFile(saved.path);
        } catch (error) {
            window.alert((error as Error).message);
        }
    });

    byId<HTMLFormElement>('upload-form').addEventListener('submit', async (event) => {
        event.preventDefault();

        if (!state.subjectId) {
            window.alert('Select a subject first.');
            return;
        }

        const folder = byId<HTMLInputElement>('upload-folder');
        const files = byId<HTMLInputElement>('upload-files');

        if (!files.files || files.files.length === 0) {
            window.alert('Choose at least one file.');
            return;
        }

        try {
            const saved = await uploadFiles(state.subjectId, folder.value.trim(), files.files);
            files.value = '';
            await reloadSubjects(state.subjectId);
            if (saved[0]) {
                await openFile(saved[0].path);
            }
        } catch (error) {
            window.alert((error as Error).message);
        }
    });
}

function wireSearch(): void {
    const form = byId<HTMLFormElement>('learning-search-form');
    const results = byId('search-results');

    form.addEventListener('submit', async (event) => {
        event.preventDefault();

        const query = byId<HTMLInputElement>('learning-search').value.trim();
        const scoped = byId<HTMLInputElement>('search-current-subject').checked;

        try {
            const hits = await searchLearning(query, scoped && state.subjectId ? state.subjectId : undefined);
            renderSearchResults(results, hits);
            setMode('search');
        } catch (error) {
            showError(results, (error as Error).message);
            setMode('search');
        }
    });
}

function renderSearchResults(container: HTMLElement, hits: Array<{ subjectId: string; subjectTitle: string; path: string; lineNumber: number; line: string }>): void {
    container.replaceChildren();
    container.appendChild(el('h2', undefined, `${hits.length} match(es)`));

    if (hits.length === 0) {
        container.appendChild(el('p', 'hint', 'Nothing found.'));
        return;
    }

    const list = el('ul', 'search-list');

    for (const hit of hits) {
        const item = el('li');
        const button = el('button', 'search-hit');
        button.append(
            el('span', 'search-where', `${hit.subjectTitle} / ${hit.path}:${hit.lineNumber}`),
            el('span', 'search-line', hit.line)
        );
        button.addEventListener('click', async () => {
            setMode('browse');
            await openSubject(hit.subjectId);
            await openFile(hit.path);
        });
        item.appendChild(button);
        list.appendChild(item);
    }

    container.appendChild(list);
}

function wireModes(): void {
    byId<HTMLButtonElement>('mode-browse').addEventListener('click', () => setMode('browse'));
    byId<HTMLButtonElement>('mode-cards').addEventListener('click', () => void startFlashcards());
}

function setMode(mode: 'browse' | 'cards' | 'search'): void {
    byId('learning-browse').classList.toggle('hidden', mode !== 'browse');
    byId('learning-cards').classList.toggle('hidden', mode !== 'cards');
    byId('search-results').classList.toggle('hidden', mode !== 'search');
    byId('mode-browse').classList.toggle('active', mode === 'browse');
    byId('mode-cards').classList.toggle('active', mode === 'cards');
}

async function startFlashcards(): Promise<void> {
    const scoped = byId<HTMLInputElement>('cards-current-subject').checked;

    try {
        state.cards = await getFlashcards(scoped && state.subjectId ? state.subjectId : undefined);
        state.cardIndex = 0;
        setMode('cards');
        renderFlashcard();
    } catch (error) {
        showError(byId('learning-cards'), (error as Error).message);
        setMode('cards');
    }
}

function wireFlashcards(): void {
    byId<HTMLButtonElement>('card-prev').addEventListener('click', () => {
        state.cardIndex = Math.max(0, state.cardIndex - 1);
        renderFlashcard();
    });

    byId<HTMLButtonElement>('card-next').addEventListener('click', () => {
        state.cardIndex = Math.min(state.cards.length - 1, state.cardIndex + 1);
        renderFlashcard();
    });

    byId<HTMLButtonElement>('card-shuffle').addEventListener('click', () => {
        for (let i = state.cards.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [state.cards[i], state.cards[j]] = [state.cards[j], state.cards[i]];
        }
        state.cardIndex = 0;
        renderFlashcard();
    });
}

function renderFlashcard(): void {
    const body = byId('card-body');
    const counter = byId('card-counter');
    body.replaceChildren();

    if (state.cards.length === 0) {
        counter.textContent = '0 / 0';
        body.appendChild(el('p', 'hint', 'No interview questions found. Add a "## Interview questions" section with "**Q: ...**" entries.'));
        return;
    }

    const card = state.cards[state.cardIndex];
    counter.textContent = `${state.cardIndex + 1} / ${state.cards.length}`;

    body.append(
        el('p', 'card-source', `${card.subjectId} / ${card.path}`),
        el('p', 'card-question', card.question)
    );

    const reveal = el('button', 'reveal-button', card.answer ? 'Show answer' : 'No model answer recorded');
    reveal.disabled = !card.answer;

    const answer = el('p', 'card-answer hidden', card.answer);
    reveal.addEventListener('click', () => {
        answer.classList.remove('hidden');
        reveal.classList.add('hidden');
    });

    body.append(reveal, answer);
}
