import * as api from './api.js';
import { askConfirm, askForm, askText } from './dialogs.js';
import { byId, el, formatBytes, showError, showProgress } from './dom.js';
import { MenuItem, menuButton, openMenu } from './menu.js';
import { SecureFile, SecureFolder, SecureTree } from './secureTypes.js';

const INFRASTRUCTURES = ['NPRD', 'PROD', 'TEST'];
const ENVIRONMENTS = ['DEV', 'SIT', 'TEST', 'STG', 'PROD', 'N/A'];

// Which environments each infrastructure offers.
const ENVIRONMENTS_BY_INFRASTRUCTURE: Record<string, string[]> = {
    NPRD: ['DEV', 'SIT', 'N/A'],
    PROD: ['STG', 'PROD', 'N/A'],
    TEST: ['TEST', 'N/A']
};

const environmentsFor = (infrastructure: string): Array<{ value: string; label: string }> =>
    (ENVIRONMENTS_BY_INFRASTRUCTURE[infrastructure] ?? ENVIRONMENTS).map((value) => ({ value, label: value }));
const EXPIRY_WARNING_DAYS = 30;

type Pane = 'files' | 'trash' | 'activity';

const asOptions = (values: string[]): Array<{ value: string; label: string }> => values.map((value) => ({ value, label: value }));
const formatTime = (iso: string): string => new Date(iso).toLocaleString();

// Whole days until the end of the expiry date; negative once it has passed.
function daysLeft(expiry: string): number {
    return Math.floor((new Date(`${expiry}T23:59:59`).getTime() - Date.now()) / 86400000);
}

interface VaultConfig {
    base: string;
    prefix: string;
    view: string;
    types: string[];
    withPlacement: boolean;
    rootLabel: string;
}

const OPERATIONS_VAULT: VaultConfig = {
    base: api.SECURE_BASE,
    prefix: 'secure',
    view: 'secure',
    types: ['Kubeconfig', 'Credentials', 'Certificate', 'Config', 'Other'],
    withPlacement: true,
    rootLabel: 'All projects'
};

const PERSONAL_VAULT: VaultConfig = {
    base: api.PERSONAL_BASE,
    prefix: 'personal',
    view: 'personal',
    types: ['Identity', 'Finance', 'Insurance', 'Medical', 'Fitness', 'Education', 'Credentials', 'Other'],
    withPlacement: false,
    rootLabel: 'All files'
};

export const initSecureFilesView = (): void => initVaultView(OPERATIONS_VAULT);
export const initPersonalFilesView = (): void => initVaultView(PERSONAL_VAULT);

function initVaultView(config: VaultConfig): void {
    const { base, prefix, types: TYPES, withPlacement, rootLabel } = config;
    const tabs = byId(`${prefix}-tabs`);
    const alert = byId(`${prefix}-alert`);
    const status = byId(`${prefix}-status`);
    const treeHost = byId(`${prefix}-tree`);
    const content = byId(`${prefix}-content`);
    const uploadInput = byId<HTMLInputElement>(`${prefix}-upload-input`);
    const versionInput = byId<HTMLInputElement>(`${prefix}-version-input`);

    const addSecureVersion = (id: string, file: File) => api.addSecureVersion(id, file, base);
    const createSecureFolder = (name: string, parentId: string | null) => api.createSecureFolder(name, parentId, base);
    const deleteSecureFile = (id: string) => api.deleteSecureFile(id, base);
    const deleteSecureFolder = (id: string) => api.deleteSecureFolder(id, base);
    const getSecureAudit = (limit: number) => api.getSecureAudit(limit, base);
    const getSecureText = (id: string) => api.getSecureText(id, base);
    const getSecureTrash = () => api.getSecureTrash(base);
    const getSecureTree = () => api.getSecureTree(base);
    const purgeSecure = (kind: 'file' | 'folder', id: string) => api.purgeSecure(kind, id, base);
    const restoreSecure = (kind: 'file' | 'folder', id: string) => api.restoreSecure(kind, id, base);
    const secureDownloadUrl = (id: string, version?: number) => api.secureDownloadUrl(id, version, base);
    const updateSecureFile = (id: string, meta: Parameters<typeof api.updateSecureFile>[1]) => api.updateSecureFile(id, meta, base);
    const updateSecureFolder = (id: string, name: string, parentId: string | null) => api.updateSecureFolder(id, name, parentId, base);
    const uploadSecureFiles = (folderId: string | null, files: File[], meta: Parameters<typeof api.uploadSecureFiles>[2]) => api.uploadSecureFiles(folderId, files, meta, base);

    let tree: SecureTree = { folders: [], files: [] };
    let pane: Pane = 'files';
    let selectedFolder: string | null = null;
    let selectedFile: string | undefined;
    let versionTarget: string | undefined;

    const collapsed = new Set<string>();

    const setStatus = (message: string, kind: 'ok' | 'error' | 'info' = 'info'): void => {
        status.textContent = message;
        status.classList.toggle('is-ok', kind === 'ok');
        status.classList.toggle('is-error', kind === 'error');
    };

    const folderById = (id: string | null | undefined): SecureFolder | undefined => tree.folders.find((folder) => folder.id === id);

    const folderPath = (id: string | null | undefined): string => {
        const parts: string[] = [];

        for (let current = folderById(id); current && parts.length < 50; current = folderById(current.parentId)) {
            parts.unshift(current.name);
        }

        return `/${parts.join('/')}`;
    };

    const childrenOf = (parentId: string | null): SecureFolder[] =>
        tree.folders.filter((folder) => (folder.parentId ?? null) === parentId);

    const filesIn = (folderId: string | null): SecureFile[] =>
        tree.files.filter((file) => (file.folderId ?? null) === folderId);

    const folderOptions = (): Array<{ value: string; label: string }> => [
        { value: '', label: '/ (top level)' },
        ...tree.folders.map((folder) => ({ value: folder.id, label: folderPath(folder.id) })).sort((first, second) => first.label.localeCompare(second.label))
    ];

    const badge = (text: string, className: string): HTMLElement => el('span', `secure-badge ${className}`, text);

    const expiryCell = (expiry: string): HTMLElement => {
        if (!expiry) {
            return el('span', 'secure-muted', '-');
        }

        const days = daysLeft(expiry);

        if (days < 0) {
            return badge(`Expired ${expiry}`, 'bad');
        }

        return days <= EXPIRY_WARNING_DAYS ? badge(`${days}d left (${expiry})`, 'warn') : el('span', undefined, expiry);
    };

    const infrastructureBadge = (infrastructure: string): HTMLElement =>
        infrastructure ? badge(infrastructure, `infra-${infrastructure.toLowerCase()}`) : el('span', 'secure-muted', '-');

    const environmentBadge = (environment: string): HTMLElement =>
        badge(environment, `env-${environment.toLowerCase().replace(/[^a-z]/g, '')}`);

    const reloadTree = async (): Promise<void> => {
        tree = await getSecureTree();

        if (selectedFolder && !folderById(selectedFolder)) {
            selectedFolder = null;
        }

        if (selectedFile && !tree.files.some((file) => file.id === selectedFile)) {
            selectedFile = undefined;
        }
    };

    const renderAlert = (): void => {
        const problems = tree.files
            .filter((file) => file.expiry && daysLeft(file.expiry) <= EXPIRY_WARNING_DAYS)
            .sort((first, second) => first.expiry.localeCompare(second.expiry));

        alert.hidden = problems.length === 0;

        if (problems.length === 0) {
            return;
        }

        const expired = problems.filter((file) => daysLeft(file.expiry) < 0).length;
        const names = problems.slice(0, 3).map((file) => `${file.name} (${daysLeft(file.expiry) < 0 ? 'expired' : `${daysLeft(file.expiry)}d`})`).join(', ');
        alert.textContent = `${problems.length} file${problems.length === 1 ? '' : 's'} expired or expiring within ${EXPIRY_WARNING_DAYS} days`
            + `${expired > 0 ? ` (${expired} already expired)` : ''}: ${names}${problems.length > 3 ? ', ...' : ''}`;
    };

    const renderTabs = (): void => {
        tabs.replaceChildren(...([['files', 'Files'], ['trash', 'Recycle bin'], ['activity', 'Activity']] as const).map(([key, label]) => {
            const button = el('button', 'link-button', label);
            button.type = 'button';
            button.setAttribute('aria-pressed', String(pane === key));
            button.classList.toggle('active', pane === key);
            button.addEventListener('click', () => {
                pane = key;
                void render();
            });
            return button;
        }));
    };

    const renderTree = (): void => {
        const list = el('div', 'secure-tree-list');

        const node = (id: string | null, label: string, depth: number): void => {
            const kids = id === null ? [] : childrenOf(id);
            const row = el('div', 'secure-node');
            row.classList.toggle('active', pane === 'files' && selectedFolder === id);
            row.style.paddingLeft = `${10 + depth * 16}px`;

            const caret = el('button', 'secure-caret', kids.length > 0 ? '\u203A' : '');
            caret.type = 'button';
            caret.classList.toggle('open', kids.length > 0 && !collapsed.has(id ?? ''));
            caret.tabIndex = -1;
            caret.addEventListener('click', (event) => {
                event.stopPropagation();

                if (id !== null && kids.length > 0) {
                    collapsed.has(id) ? collapsed.delete(id) : collapsed.add(id);
                    renderTree();
                }
            });

            const name = el('span', 'secure-node-name', label);
            const count = el('span', 'secure-count', String(filesIn(id).length));
            row.append(caret, el('i', 'secure-folder-icon'), name, count);
            row.addEventListener('click', () => {
                pane = 'files';
                selectedFolder = id;
                selectedFile = undefined;
                void render();
            });
            list.appendChild(row);

            if (id !== null && !collapsed.has(id)) {
                kids.forEach((child) => node(child.id, child.name, depth + 1));
            }
        };

        const rootRow = el('div', 'secure-node');
        rootRow.classList.toggle('active', pane === 'files' && selectedFolder === null);
        rootRow.style.paddingLeft = '10px';
        rootRow.append(el('span', 'secure-caret'), el('i', 'secure-folder-icon'), el('span', 'secure-node-name', rootLabel), el('span', 'secure-count', String(filesIn(null).length)));
        rootRow.addEventListener('click', () => {
            pane = 'files';
            selectedFolder = null;
            selectedFile = undefined;
            void render();
        });
        list.appendChild(rootRow);
        childrenOf(null).forEach((folder) => node(folder.id, folder.name, 1));

        treeHost.replaceChildren(list);
    };

    const run = async (work: () => Promise<void>, success: string): Promise<void> => {
        try {
            await work();
            await reloadTree();
            setStatus(success, 'ok');
        } catch (failure) {
            setStatus((failure as Error).message, 'error');
        }

        await render();
    };

    const startUpload = async (files: File[]): Promise<void> => {
        if (files.length === 0) {
            return;
        }

        const values = await askForm(
            'Upload files',
            `${files.length} file${files.length === 1 ? '' : 's'} will be encrypted and saved in ${folderPath(selectedFolder)}.`,
            'Upload',
            [
                { name: 'type', label: 'Type', kind: 'select', value: 'Other', options: asOptions(TYPES) },
                ...(withPlacement ? [
                    { name: 'infrastructure', label: 'Infrastructure', kind: 'select' as const, value: 'NPRD', options: asOptions(INFRASTRUCTURES) },
                    { name: 'environment', label: 'Environment', kind: 'select' as const, value: 'N/A', dependsOn: 'infrastructure', optionsFor: environmentsFor }
                ] : []),
                { name: 'expiry', label: 'Expiry date (optional)', kind: 'date', value: '' },
                { name: 'notes', label: 'Notes (optional)', kind: 'textarea', value: '', maxLength: 2000 }
            ]);

        if (!values) {
            return;
        }

        setStatus('Encrypting and uploading...');
        await run(async () => {
            await uploadSecureFiles(selectedFolder, files, { type: values.type, infrastructure: values.infrastructure ?? '', environment: values.environment ?? 'N/A', expiry: values.expiry, notes: values.notes });
        }, `Saved ${files.length} file${files.length === 1 ? '' : 's'} to ${folderPath(selectedFolder)}.`);
    };

    const editFile = async (file: SecureFile): Promise<void> => {
        const values = await askForm('Edit details', 'Changes are recorded in the activity log.', 'Save', [
            { name: 'name', label: 'Name', kind: 'text', value: file.name, required: true, maxLength: 200 },
            { name: 'folderId', label: 'Folder', kind: 'select', value: file.folderId ?? '', options: folderOptions() },
            { name: 'type', label: 'Type', kind: 'select', value: file.type, options: asOptions(TYPES) },
            ...(withPlacement ? [
                { name: 'infrastructure', label: 'Infrastructure', kind: 'select' as const, value: file.infrastructure ?? '', options: [{ value: '', label: '- (not set)' }, ...asOptions(INFRASTRUCTURES)] },
                { name: 'environment', label: 'Environment', kind: 'select' as const, value: file.environment, dependsOn: 'infrastructure', optionsFor: environmentsFor }
            ] : []),
            { name: 'expiry', label: 'Expiry date (optional)', kind: 'date', value: file.expiry },
            { name: 'notes', label: 'Notes', kind: 'textarea', value: file.notes, maxLength: 2000 }
        ]);

        if (!values) {
            return;
        }

        await run(async () => {
            await updateSecureFile(file.id, {
                name: values.name,
                folderId: values.folderId || null,
                type: values.type,
                infrastructure: values.infrastructure ?? '',
                environment: values.environment ?? 'N/A',
                expiry: values.expiry,
                notes: values.notes
            });
        }, `Saved ${values.name}.`);
    };

    const copyText = async (file: SecureFile): Promise<void> => {
        try {
            const { text } = await getSecureText(file.id);
            await navigator.clipboard.writeText(text);
            setStatus(`Copied the text of ${file.name}. The clipboard is not cleared automatically, so paste it where you need it and copy something else afterwards.`, 'ok');
        } catch (failure) {
            setStatus((failure as Error).message, 'error');
        }
    };

    const editContent = async (file: SecureFile): Promise<void> => {
        let current: string;

        try {
            current = (await getSecureText(file.id)).text;
        } catch (failure) {
            setStatus((failure as Error).message, 'error');
            return;
        }

        const values = await askForm('Edit content', 'Saving creates a new version; earlier versions stay available.', 'Save', [
            { name: 'text', label: file.name, kind: 'textarea', value: current, rows: 18, raw: true, required: true }
        ]);

        if (!values || values.text === current) {
            return;
        }

        const updated = new File([values.text], file.name, { type: 'text/plain' });
        await run(() => addSecureVersion(file.id, updated).then(() => undefined), `Saved a new version of ${file.name}.`);
    };

    const fileMenuItems = (file: SecureFile): MenuItem[] => [
        { label: 'Copy text', onSelect: () => void copyText(file) },
        { label: 'Edit details', onSelect: () => void editFile(file) },
        {
            label: 'New version',
            onSelect: () => {
                versionTarget = file.id;
                versionInput.click();
            }
        },
        {
            label: 'Delete',
            danger: true,
            separatorBefore: true,
            onSelect: () => {
                void (async () => {
                    if (await askConfirm('Delete file', `Delete "${file.name}"? It moves to the recycle bin for 30 days and can be restored from there.`, 'Delete')) {
                        selectedFile = undefined;
                        await run(() => deleteSecureFile(file.id).then(() => undefined), `Moved ${file.name} to the recycle bin.`);
                    }
                })();
            }
        }
    ];

    const download = (file: SecureFile, version?: number): void => {
        const link = el('a');
        link.href = secureDownloadUrl(file.id, version);
        link.download = file.name;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setStatus(`Downloading ${file.name}. The download is recorded in the activity log.`);
    };

    const renderDetails = (host: HTMLElement): void => {
        const file = tree.files.find((item) => item.id === selectedFile);

        if (!file) {
            host.replaceChildren(el('p', 'secure-muted', 'Select a file to see its details, notes and versions.'));
            return;
        }

        const facts: Array<[string, HTMLElement | string]> = [
            ['Location', folderPath(file.folderId)],
            ['Type', file.type],
            ...(withPlacement ? [
                ['Infrastructure', infrastructureBadge(file.infrastructure ?? '')] as [string, HTMLElement],
                ['Environment', environmentBadge(file.environment)] as [string, HTMLElement]
            ] : []),
            ['Expiry', expiryCell(file.expiry)],
            ['Size', formatBytes(file.size)]
        ];

        const grid = el('dl', 'secure-facts');

        for (const [label, value] of facts) {
            const term = el('dt', undefined, label);
            const detail = el('dd');
            detail.append(value);
            grid.append(term, detail);
        }

        const actions = el('div', 'secure-file-actions');
        const download_ = el('button', undefined, 'Download');
        download_.type = 'button';
        download_.addEventListener('click', () => download(file));
        const edit = el('button', 'secondary', 'Edit content');
        edit.type = 'button';
        edit.addEventListener('click', () => void editContent(file));
        actions.append(download_, edit, menuButton('More \u25BE', 'secondary', fileMenuItems(file)));

        const versions = el('table', 'secure-versions');
        versions.createTHead().insertRow().append(...['Version', 'Size', 'Uploaded', 'By', ''].map((title) => el('th', undefined, title)));
        const body = versions.createTBody();

        file.versions.forEach((version, index) => {
            const row = body.insertRow();
            row.insertCell().textContent = index === 0 ? `v${version.number} (current)` : `v${version.number}`;
            row.insertCell().textContent = formatBytes(version.size);
            row.insertCell().textContent = formatTime(version.uploadedAt);
            row.insertCell().textContent = version.uploadedBy;
            const open = el('button', 'link-button', 'Download');
            open.type = 'button';
            open.addEventListener('click', () => download(file, version.number));
            row.insertCell().appendChild(open);
        });

        const notes = el('div', 'secure-notes');
        notes.append(el('h4', undefined, 'Notes'), el('p', file.notes ? undefined : 'secure-muted', file.notes || 'No notes.'));

        const title = el('div', 'secure-file-title');
        title.append(el('h3', undefined, file.name), el('span', 'secure-muted', `Updated ${formatTime(file.updatedAt)} by ${file.updatedBy}`));

        host.replaceChildren(title, actions, grid, notes, el('h4', undefined, 'Versions'), versions);
    };

    const renderFiles = (): void => {
        const toolbar = el('div', 'secure-toolbar');
        const crumbs = el('nav', 'secure-crumbs');
        const path = [{ id: null as string | null, name: rootLabel }];

        for (let current = folderById(selectedFolder); current && path.length < 50; current = folderById(current.parentId)) {
            path.splice(1, 0, { id: current.id, name: current.name });
        }

        path.forEach((part, index) => {
            const crumb = el('button', 'secure-crumb', part.name);
            crumb.type = 'button';
            crumb.disabled = index === path.length - 1;
            crumb.addEventListener('click', () => {
                selectedFolder = part.id;
                selectedFile = undefined;
                void render();
            });
            crumbs.appendChild(crumb);
        });

        const buttons = el('div', 'secure-actions');
        const newFolder = async (): Promise<void> => {
            const name = await askText('New folder', 'Folder name', '', 'Create', `It will be created in ${folderPath(selectedFolder)}.`);

            if (name) {
                await run(async () => {
                    const created = await createSecureFolder(name, selectedFolder);
                    selectedFolder = created.id;
                }, `Created ${name}.`);
            }
        };
        const newTextDocument = async (): Promise<void> => {
            const entered = await askText('New text document', 'File name', 'notes.txt', 'Create', `It will be created in ${folderPath(selectedFolder)}.`);

            if (entered) {
                const name = /\.[A-Za-z0-9]+$/.test(entered) ? entered : `${entered}.txt`;
                await run(async () => {
                    const [created] = await uploadSecureFiles(selectedFolder, [new File([''], name, { type: 'text/plain' })], { type: 'Other', infrastructure: '', environment: 'N/A', expiry: '', notes: '' });
                    selectedFile = created?.id;
                }, `Created ${name}.`);
            }
        };

        buttons.appendChild(menuButton('+ Add \u25BE', '', [
            { label: 'Upload files', onSelect: () => uploadInput.click() },
            { label: 'New folder', onSelect: () => void newFolder() },
            { label: 'New text document', onSelect: () => void newTextDocument() }
        ]));

        const current = folderById(selectedFolder);

        if (current) {
            buttons.appendChild(menuButton('\u22EF', 'secondary icon-button', [
                {
                    label: 'Rename folder',
                    onSelect: () => {
                        void (async () => {
                            const name = await askText('Rename folder', 'Folder name', current.name, 'Rename');

                            if (name && name !== current.name) {
                                await run(() => updateSecureFolder(current.id, name, current.parentId ?? null).then(() => undefined), `Renamed to ${name}.`);
                            }
                        })();
                    }
                },
                {
                    label: 'Delete folder',
                    danger: true,
                    separatorBefore: true,
                    onSelect: () => {
                        void (async () => {
                            const inside = tree.files.filter((file) => file.folderId === current.id).length;

                            if (await askConfirm('Delete folder', `Delete "${folderPath(current.id)}" with everything in it${inside > 0 ? ` (${inside} file${inside === 1 ? '' : 's'} at this level)` : ''}? It moves to the recycle bin for 30 days.`, 'Delete')) {
                                const parent = current.parentId ?? null;
                                await run(async () => {
                                    await deleteSecureFolder(current.id);
                                    selectedFolder = parent;
                                    selectedFile = undefined;
                                }, `Moved ${current.name} to the recycle bin.`);
                            }
                        })();
                    }
                }
            ], true));
        }

        toolbar.append(crumbs, buttons);

        const subfolders = childrenOf(selectedFolder);
        const files = filesIn(selectedFolder);
        const table = el('table', 'secure-table');
        const showFileColumns = files.length > 0;
        table.createTHead().insertRow().append(...(!showFileColumns ? ['Name', 'Type'] : withPlacement ? ['Name', 'Type', 'Infrastructure', 'Environment', 'Expiry', 'Size', 'Updated'] : ['Name', 'Type', 'Expiry', 'Size', 'Updated']).map((title) => el('th', undefined, title)));
        const body = table.createTBody();

        for (const folder of subfolders) {
            const row = body.insertRow();
            row.className = 'secure-row secure-row-folder';
            const name = row.insertCell();
            name.append(el('i', 'secure-folder-icon'), document.createTextNode(` ${folder.name}`));
            row.insertCell().textContent = 'Folder';
            for (let blank = !showFileColumns ? 0 : withPlacement ? 5 : 3; blank > 0; blank--) {
                row.insertCell();
            }

            row.addEventListener('click', () => {
                selectedFolder = folder.id;
                selectedFile = undefined;
                void render();
            });
        }

        for (const file of files) {
            const row = body.insertRow();
            row.className = 'secure-row';
            row.classList.toggle('selected', file.id === selectedFile);
            row.insertCell().textContent = file.name;
            row.insertCell().textContent = file.type;
            if (withPlacement) {
                row.insertCell().appendChild(infrastructureBadge(file.infrastructure ?? ''));
                row.insertCell().appendChild(environmentBadge(file.environment));
            }

            row.insertCell().appendChild(expiryCell(file.expiry));
            row.insertCell().textContent = formatBytes(file.size);
            row.insertCell().textContent = formatTime(file.updatedAt);
            row.addEventListener('click', () => {
                selectedFile = file.id;
                void render();
            });
            row.addEventListener('contextmenu', (event) => {
                event.preventDefault();
                selectedFile = file.id;
                void render();
                openMenu(event.clientX, event.clientY, [{ label: 'Download', onSelect: () => download(file) }, { label: 'Edit content', onSelect: () => void editContent(file) }, ...fileMenuItems(file)]);
            });
        }

        const list = el('div', 'secure-list');
        list.appendChild(subfolders.length + files.length === 0
            ? el('p', 'secure-empty', 'This folder is empty. Drop files here or use Upload files.')
            : table);

        const details = el('div', 'secure-details');
        renderDetails(details);

        // Dropping files anywhere on the pane starts an upload into the current folder.
        content.ondragover = (event) => {
            event.preventDefault();
            content.classList.add('dragging');
        };
        content.ondragleave = () => content.classList.remove('dragging');
        content.ondrop = (event) => {
            event.preventDefault();
            content.classList.remove('dragging');
            void startUpload(Array.from(event.dataTransfer?.files ?? []));
        };

        content.replaceChildren(toolbar, list, details);
    };

    const renderTrash = async (): Promise<void> => {
        showProgress(content, 'Loading the recycle bin...');

        try {
            const trash = await getSecureTrash();
            const wrapper = el('div', 'secure-list');
            wrapper.appendChild(el('p', 'secure-muted', `Deleted items are kept for ${trash.retentionDays} days and then removed for good.`));

            const table = el('table', 'secure-table');
            table.createTHead().insertRow().append(...['Name', 'Kind', 'Deleted', 'Removed for good', ''].map((title) => el('th', undefined, title)));
            const body = table.createTBody();

            const addRow = (kind: 'file' | 'folder', id: string, name: string, deletedAt: string): void => {
                const row = body.insertRow();
                const purgeOn = new Date(new Date(deletedAt).getTime() + trash.retentionDays * 86400000);
                row.insertCell().textContent = name;
                row.insertCell().textContent = kind === 'file' ? 'File' : 'Folder';
                row.insertCell().textContent = formatTime(deletedAt);
                row.insertCell().textContent = purgeOn.toLocaleDateString();

                const actions = row.insertCell();
                const restore = el('button', 'secondary', 'Restore');
                restore.type = 'button';
                restore.addEventListener('click', () => {
                    void run(() => restoreSecure(kind, id).then(() => undefined), `Restored ${name}.`);
                });

                const purge = el('button', 'secondary danger', 'Delete forever');
                purge.type = 'button';
                purge.addEventListener('click', () => {
                    void (async () => {
                        if (await askConfirm('Delete forever', `Permanently delete "${name}"${kind === 'folder' ? ' and everything deleted with it' : ''}? This cannot be undone.`, 'Delete forever')) {
                            await run(() => purgeSecure(kind, id).then(() => undefined), `Deleted ${name} permanently.`);
                        }
                    })();
                });

                actions.append(restore, purge);
            };

            trash.folders.forEach((folder) => addRow('folder', folder.id, folder.name, folder.deletedAt ?? ''));
            trash.files.forEach((file) => addRow('file', file.id, file.name, file.deletedAt ?? ''));

            wrapper.appendChild(trash.folders.length + trash.files.length === 0 ? el('p', 'secure-empty', 'The recycle bin is empty.') : table);
            content.replaceChildren(wrapper);
        } catch (failure) {
            showError(content, (failure as Error).message);
        }
    };

    const renderActivity = async (): Promise<void> => {
        showProgress(content, 'Loading the activity log...');

        try {
            const entries = await getSecureAudit(200);
            const wrapper = el('div', 'secure-list');
            wrapper.appendChild(el('p', 'secure-muted', 'Every upload, download, copy, edit and delete is recorded here (latest 200). File contents are never logged.'));

            const table = el('table', 'secure-table');
            table.createTHead().insertRow().append(...['When', 'Who', 'Action', 'Item'].map((title) => el('th', undefined, title)));
            const body = table.createTBody();

            for (const entry of entries) {
                const row = body.insertRow();
                row.insertCell().textContent = formatTime(entry.at);
                row.insertCell().textContent = entry.user;
                row.insertCell().textContent = entry.action;
                row.insertCell().textContent = entry.target;
            }

            wrapper.appendChild(entries.length === 0 ? el('p', 'secure-empty', 'Nothing has been recorded yet.') : table);
            content.replaceChildren(wrapper);
        } catch (failure) {
            showError(content, (failure as Error).message);
        }
    };

    async function render(): Promise<void> {
        renderTabs();
        renderTree();
        renderAlert();

        if (pane === 'trash') {
            await renderTrash();
        } else if (pane === 'activity') {
            await renderActivity();
        } else {
            renderFiles();
        }
    }

    const load = async (): Promise<void> => {
        showProgress(content, 'Loading secure files...');
        setStatus('');

        try {
            await reloadTree();
            await render();
        } catch (failure) {
            showError(content, (failure as Error).message);
        }
    };

    uploadInput.addEventListener('change', () => {
        const files = Array.from(uploadInput.files ?? []);
        uploadInput.value = '';
        void startUpload(files);
    });

    versionInput.addEventListener('change', () => {
        const file = versionInput.files?.[0];
        const target = tree.files.find((item) => item.id === versionTarget);
        versionInput.value = '';

        if (!file || !target) {
            return;
        }

        void (async () => {
            if (await askConfirm('New version', `Add "${file.name}" as a new version of "${target.name}"? Earlier versions are kept (the latest 5).`, 'Add version')) {
                await run(() => addSecureVersion(target.id, file).then(() => undefined), `Added a new version of ${target.name}.`);
            }
        })();
    });

    document.querySelector<HTMLButtonElement>(`.nav-button[data-target="view-${config.view}"]`)
        ?.addEventListener('click', () => void load());

    if (location.hash === `#${config.view}`) {
        void load();
    }
}
