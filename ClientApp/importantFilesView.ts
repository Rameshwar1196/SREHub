import {
    createImportantFile,
    deleteImportantFile,
    getImportantFiles,
    ImportantFile,
    importantFileUrl,
    uploadImportantFiles
} from './api.js';
import { askText } from './dialogs.js';
import { byId, el, formatBytes, showError, showProgress } from './dom.js';
import { menuButton } from './menu.js';
import { officeKindOf, renderOffice } from './officeViewer.js';

const AUTOSAVE_MILLISECONDS = 1200;

const ICON: Record<string, string> = {
    pdf: '[pdf]',
    word: '[doc]',
    excel: '[xls]',
    powerpoint: '[ppt]',
    text: '[txt]',
    legacy: '[old]'
};

const NEW_TYPES: Array<{ label: string; name: string }> = [
    { label: 'Text document', name: 'Untitled.txt' },
    { label: 'Markdown document', name: 'Untitled.md' },
    { label: 'Excel workbook', name: 'Untitled.xlsx' },
    { label: 'Word document', name: 'Untitled.docx' }
];

export function initImportantFilesView(): void {
    const input = byId<HTMLInputElement>('files-input');
    const status = byId('files-status');
    const list = byId('files-list');
    const viewer = byId('files-viewer');
    const filter = byId<HTMLInputElement>('files-filter');

    let files: ImportantFile[] = [];
    let selected: string | undefined;
    let opening = 0;

    const renderTextEditor = async (holder: HTMLElement, file: ImportantFile, token: number): Promise<void> => {
        const response = await fetch(importantFileUrl(file.name));

        if (!response.ok) {
            holder.appendChild(el('p', 'hint', `The file could not be loaded (status ${response.status}).`));
            return;
        }

        const original = await response.text();
        let saved = original;
        let timer: number | undefined;
        let saving = false;
        const editor = el('textarea', 'files-text-editor');
        editor.value = original;
        editor.spellcheck = false;
        editor.setAttribute('aria-label', file.name);

        const note = el('span', 'office-status', 'Changes save automatically.');

        const flush = async (): Promise<void> => {
            window.clearTimeout(timer);

            if (saving || editor.value === saved) {
                return;
            }

            saving = true;
            const text = editor.value;
            note.textContent = 'Saving...';

            try {
                await uploadImportantFiles([new File([text], file.name, { type: 'text/plain' })]);
                saved = text;
                note.textContent = editor.value === saved ? 'All changes saved.' : 'Unsaved changes...';

                if (token === opening) {
                    await load();
                }
            } catch (failure) {
                note.textContent = `Save failed: ${(failure as Error).message}`;
            } finally {
                saving = false;

                if (editor.value !== saved && !note.textContent?.startsWith('Save failed')) {
                    timer = window.setTimeout(() => void flush(), AUTOSAVE_MILLISECONDS);
                }
            }
        };

        editor.addEventListener('input', () => {
            note.textContent = 'Unsaved changes...';
            window.clearTimeout(timer);
            timer = window.setTimeout(() => void flush(), AUTOSAVE_MILLISECONDS);
        });
        editor.addEventListener('blur', () => void flush());

        const actions = el('div', 'office-toolbar');
        actions.append(note);
        holder.append(actions, editor);
    };

    const open = async (file: ImportantFile): Promise<void> => {
        selected = file.name;
        renderList();

        const token = ++opening;
        const url = importantFileUrl(file.name);
        const holder = el('div', 'files-holder');

        const bar = el('div', 'file-bar');
        bar.appendChild(el('span', 'file-path', file.name));

        const download = el('a', 'link-button', 'Download');
        download.href = url;
        download.download = file.name;
        bar.appendChild(download);

        const remove = el('button', 'link-button danger', 'Delete');
        remove.type = 'button';
        remove.addEventListener('click', async () => {
            if (!window.confirm(`Delete '${file.name}'?\n\nThis cannot be undone.`)) {
                return;
            }

            try {
                await deleteImportantFile(file.name);
                selected = undefined;
                viewer.replaceChildren(el('p', 'hint', `Deleted '${file.name}'.`));
                await load();
            } catch (failure) {
                status.textContent = (failure as Error).message;
            }
        });
        bar.appendChild(remove);

        viewer.replaceChildren(bar, holder);

        if (file.kind === 'pdf') {
            const frame = el('iframe', 'file-pdf');
            frame.src = url;
            holder.appendChild(frame);
            return;
        }

        if (file.kind === 'text') {
            await renderTextEditor(holder, file, token);
            return;
        }

        const office = officeKindOf(file.name);

        if (!office) {
            holder.appendChild(el('p', 'hint', `${file.name} (${formatBytes(file.size)}) is an older Office format that cannot be previewed. Use Download.`));
            return;
        }

        if (office !== 'excel') {
            holder.appendChild(el('p', 'hint', office === 'word'
                ? 'Word files open read-only here. Download the file, edit it in Word, then upload it again with the same name to replace it.'
                : 'PowerPoint files open read-only here. Download the file to edit it.'));
        }

        await renderOffice(holder, office, {
            name: file.name,
            url,
            save: office === 'excel'
                ? async (blob) => {
                    await uploadImportantFiles([new File([blob], file.name)]);
                    if (token === opening) {
                        await load();
                    }
                }
                : undefined
        });
    };

    function renderList(): void {
        const term = filter.value.trim().toLowerCase();
        const visible = files.filter((file) => file.name.toLowerCase().includes(term));

        if (visible.length === 0) {
            list.replaceChildren(el('p', 'hint', files.length === 0 ? 'No files yet. Upload one to get started.' : 'No files match your search.'));
            return;
        }

        list.replaceChildren(...visible.map((file) => {
            const item = el('button', 'files-item');
            item.type = 'button';
            item.classList.toggle('active', file.name === selected);
            item.title = file.name;

            const text = el('span', 'files-item-text');
            text.append(
                el('span', 'files-item-name', file.name),
                el('span', 'files-item-meta', `${formatBytes(file.size)} - ${new Date(file.updatedAt).toLocaleDateString()}`)
            );

            item.append(el('span', 'tree-icon', ICON[file.kind] ?? '[?]'), text);
            item.addEventListener('click', () => void open(file));
            return item;
        }));
    }

    async function load(): Promise<void> {
        try {
            files = await getImportantFiles();
            renderList();
        } catch (failure) {
            showError(list, (failure as Error).message);
        }
    }

    input.addEventListener('change', async () => {
        if (!input.files || input.files.length === 0) {
            return;
        }

        status.textContent = 'Uploading...';

        try {
            const saved = await uploadImportantFiles(input.files);
            status.textContent = `Uploaded ${saved.length} file${saved.length === 1 ? '' : 's'}.`;
            input.value = '';
            await load();
        } catch (failure) {
            status.textContent = (failure as Error).message;
        }
    });

    filter.addEventListener('input', renderList);

    const createFile = async (suggested: string): Promise<void> => {
        const entered = await askText('New file', 'File name', suggested, 'Create', 'It is created empty. Text and Excel files can be edited right here.');

        if (!entered) {
            return;
        }

        const extension = suggested.slice(suggested.lastIndexOf('.'));
        const name = entered.toLowerCase().endsWith(extension) ? entered : `${entered}${extension}`;

        try {
            const created = await createImportantFile(name);
            status.textContent = `Created ${created.name}.`;
            await load();
            await open(created);
        } catch (failure) {
            status.textContent = (failure as Error).message;
        }
    };

    byId('files-new-host').appendChild(menuButton('+ New \u25BE', 'files-new', NEW_TYPES.map((type) => ({
        label: type.label,
        onSelect: () => void createFile(type.name)
    }))));

    document.querySelector<HTMLButtonElement>('.nav-button[data-target="view-files"]')
        ?.addEventListener('click', () => {
            showProgress(list, 'Loading files...');
            void load();
        });

    if (location.hash === '#files') {
        void load();
    }
}
