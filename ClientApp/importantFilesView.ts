import {
    deleteImportantFile,
    getImportantFiles,
    ImportantFile,
    importantFileUrl,
    uploadImportantFiles
} from './api.js';
import { byId, el, formatBytes, showError, showProgress } from './dom.js';
import { officeKindOf, renderOffice } from './officeViewer.js';

const ICON: Record<string, string> = {
    pdf: '[pdf]',
    word: '[doc]',
    excel: '[xls]',
    powerpoint: '[ppt]',
    legacy: '[old]'
};

export function initImportantFilesView(): void {
    const input = byId<HTMLInputElement>('files-input');
    const status = byId('files-status');
    const list = byId('files-list');
    const viewer = byId('files-viewer');
    const filter = byId<HTMLInputElement>('files-filter');

    let files: ImportantFile[] = [];
    let selected: string | undefined;
    let opening = 0;

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

        const office = officeKindOf(file.name);

        if (!office) {
            holder.appendChild(el('p', 'hint', `${file.name} (${formatBytes(file.size)}) is an older Office format that cannot be previewed. Use Download.`));
            return;
        }

        if (office !== 'excel') {
            holder.appendChild(el('p', 'hint', 'Word and PowerPoint files open read-only here. Download the file to edit it.'));
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

    document.querySelector<HTMLButtonElement>('.nav-button[data-target="view-files"]')
        ?.addEventListener('click', () => {
            showProgress(list, 'Loading files...');
            void load();
        });

    if (location.hash === '#files') {
        void load();
    }
}
