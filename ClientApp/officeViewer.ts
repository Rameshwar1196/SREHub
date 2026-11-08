import { el } from './dom.js';

export type OfficeKind = 'word' | 'excel' | 'powerpoint';

export type OfficeSave = (blob: Blob) => Promise<void>;

interface MammothLib {
    convertToHtml(input: { arrayBuffer: ArrayBuffer }): Promise<{ value: string }>;
}

interface Cell {
    t?: string;
    v?: unknown;
    w?: string;
    z?: string;
}

interface Range {
    s: { r: number; c: number };
    e: { r: number; c: number };
}

interface Sheet {
    [address: string]: unknown;
}

interface Workbook {
    SheetNames: string[];
    Sheets: Record<string, Sheet>;
}

interface XlsxLib {
    read(data: ArrayBuffer, options: { type: 'array' }): Workbook;
    write(book: Workbook, options: { type: 'array'; bookType: 'xlsx' }): ArrayBuffer;
    utils: {
        decode_range(ref: string): Range;
        encode_cell(cell: { r: number; c: number }): string;
        encode_col(column: number): string;
        decode_cell(address: string): { r: number; c: number };
        encode_range(range: Range): string;
    };
}

interface PptxLib {
    init(host: HTMLElement, options: { width: number; height: number; mode: 'list' | 'slide' }): {
        preview(data: ArrayBuffer): Promise<unknown>;
    };
}

const MAX_ROWS = 5000;
const MAX_COLUMNS = 200;
const BLANK_SHEET_RANGE = 'A1:J30';
const AUTOSAVE_MILLISECONDS = 1200;
const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

const EXTENSIONS: Record<string, OfficeKind> = {
    docx: 'word',
    xlsx: 'excel',
    xlsm: 'excel',
    xls: 'excel',
    pptx: 'powerpoint'
};

const scripts = new Map<string, Promise<void>>();

export function loadScript(src: string): Promise<void> {
    let loading = scripts.get(src);

    if (!loading) {
        loading = new Promise<void>((resolve, reject) => {
            const script = document.createElement('script');
            script.src = src;
            script.onload = () => resolve();
            script.onerror = () => {
                scripts.delete(src);
                reject(new Error(`Could not load ${src}.`));
            };
            document.head.appendChild(script);
        });
        scripts.set(src, loading);
    }

    return loading;
}

export function officeKindOf(name: string): OfficeKind | undefined {
    return EXTENSIONS[name.split('.').pop()?.toLowerCase() ?? ''];
}

async function fetchBuffer(url: string): Promise<ArrayBuffer> {
    const response = await fetch(url);

    if (!response.ok) {
        throw new Error(`The file could not be loaded (status ${response.status}).`);
    }

    return response.arrayBuffer();
}

function sanitizeWordHtml(html: string): string {
    const doc = new DOMParser().parseFromString(html, 'text/html');
    doc.querySelectorAll('script,iframe,object,embed,link,meta,style,base,form').forEach((node) => node.remove());

    doc.querySelectorAll('*').forEach((node) => {
        for (const attribute of Array.from(node.attributes)) {
            if (/^on/i.test(attribute.name)) {
                node.removeAttribute(attribute.name);
            }
        }
    });

    doc.querySelectorAll('a').forEach((anchor) => {
        if (/^(https?:|mailto:)/i.test(anchor.getAttribute('href') ?? '')) {
            anchor.target = '_blank';
            anchor.rel = 'noopener noreferrer';
        } else {
            anchor.removeAttribute('href');
        }
    });

    return doc.body.innerHTML;
}

async function renderWord(host: HTMLElement, buffer: ArrayBuffer): Promise<void> {
    await loadScript('/lib/mammoth.browser.min.js');
    const mammoth = (window as unknown as { mammoth: MammothLib }).mammoth;
    const { value } = await mammoth.convertToHtml({ arrayBuffer: buffer });

    const frame = el('iframe', 'office-frame');
    // No scripts run inside the page; popups are allowed only so links can open in a new tab.
    frame.setAttribute('sandbox', 'allow-popups allow-popups-to-escape-sandbox');
    frame.srcdoc = `<!doctype html><meta charset="utf-8"><style>
        body{font:15px/1.6 Segoe UI,Arial,sans-serif;color:#1d2433;background:#fff;margin:0;padding:32px 44px;}
        table{border-collapse:collapse;margin:12px 0;}td,th{border:1px solid #b9c0d0;padding:6px 10px;vertical-align:top;}
        img{max-width:100%;height:auto;}h1,h2,h3{line-height:1.25;}a{color:#2456c7;}
    </style>${sanitizeWordHtml(value)}`;
    host.appendChild(frame);
}

async function renderPowerPoint(host: HTMLElement, buffer: ArrayBuffer): Promise<void> {
    await loadScript('/lib/pptx-preview.min.js');
    const pptx = (window as unknown as { PptxPreview: PptxLib }).PptxPreview;

    const stage = el('div', 'office-slides');
    host.appendChild(stage);

    const width = Math.max(480, Math.min(stage.clientWidth || 960, 1100));
    await pptx.init(stage, { width, height: Math.round((width * 9) / 16), mode: 'list' }).preview(buffer);
}

async function renderExcel(host: HTMLElement, name: string, buffer: ArrayBuffer, save?: OfficeSave): Promise<void> {
    await loadScript('/lib/xlsx.full.min.js');
    const xlsx = (window as unknown as { XLSX: XlsxLib }).XLSX;
    const book = xlsx.read(buffer, { type: 'array' });

    // Saving rewrites the file as .xlsx without macros, so only plain .xlsx files are editable.
    const canEdit = Boolean(save) && name.toLowerCase().endsWith('.xlsx');
    const pending = new Map<string, string>();
    let active = 0;
    let editing = false;
    let saving = false;
    let saveTimer: number | undefined;

    const toolbar = el('div', 'office-toolbar');
    const tabs = el('div', 'office-tabs');
    const actions = el('div', 'office-actions');
    const status = el('span', 'office-status');
    const grid = el('div', 'office-grid');
    toolbar.append(tabs, actions);
    host.append(toolbar, grid);

    const key = (sheetName: string, address: string): string => `${sheetName}!${address}`;

    const renderSheet = (): void => {
        const sheetName = book.SheetNames[active];
        const sheet = book.Sheets[sheetName];
        const ref = (sheet['!ref'] as string | undefined) ?? (canEdit ? BLANK_SHEET_RANGE : undefined);

        if (!ref) {
            grid.replaceChildren(el('p', 'hint', 'This sheet is empty.'));
            return;
        }

        const range = xlsx.utils.decode_range(ref);
        const lastRow = Math.min(range.e.r, range.s.r + MAX_ROWS - 1);
        const lastColumn = Math.min(range.e.c, range.s.c + MAX_COLUMNS - 1);
        const merges = (sheet['!merges'] as Range[] | undefined) ?? [];
        const origin = new Map<string, Range>();
        const covered = new Set<string>();

        for (const merge of merges) {
            origin.set(`${merge.s.r},${merge.s.c}`, merge);

            for (let r = merge.s.r; r <= merge.e.r; r++) {
                for (let c = merge.s.c; c <= merge.e.c; c++) {
                    if (r !== merge.s.r || c !== merge.s.c) {
                        covered.add(`${r},${c}`);
                    }
                }
            }
        }

        const table = el('table', 'office-sheet');
        const head = table.createTHead().insertRow();
        head.appendChild(el('th', 'corner'));

        for (let c = range.s.c; c <= lastColumn; c++) {
            head.appendChild(el('th', undefined, xlsx.utils.encode_col(c)));
        }

        const body = table.createTBody();

        for (let r = range.s.r; r <= lastRow; r++) {
            const row = body.insertRow();
            row.appendChild(el('th', undefined, String(r + 1)));

            for (let c = range.s.c; c <= lastColumn; c++) {
                if (covered.has(`${r},${c}`)) {
                    continue;
                }

                const address = xlsx.utils.encode_cell({ r, c });
                const cell = sheet[address] as Cell | undefined;
                const original = cell ? (cell.w ?? String(cell.v ?? '')) : '';
                const edited = pending.get(key(sheetName, address));
                const td = row.insertCell();
                td.textContent = edited ?? original;
                td.dataset.address = address;
                td.dataset.original = original;

                if (cell?.t === 'n') {
                    td.classList.add('num');
                }

                if (edited !== undefined) {
                    td.classList.add('dirty');
                }

                const merge = origin.get(`${r},${c}`);
                if (merge) {
                    td.rowSpan = merge.e.r - merge.s.r + 1;
                    td.colSpan = merge.e.c - merge.s.c + 1;
                }

                if (editing) {
                    td.setAttribute('contenteditable', 'plaintext-only');
                }
            }
        }

        const note = lastRow < range.e.r || lastColumn < range.e.c
            ? el('p', 'hint', `Showing the first ${lastRow - range.s.r + 1} rows and ${lastColumn - range.s.c + 1} columns of this sheet.`)
            : undefined;

        grid.replaceChildren(...(note ? [table, note] : [table]));
    };

    grid.addEventListener('input', (event) => {
        const td = (event.target as HTMLElement).closest('td');

        if (!td || !editing) {
            return;
        }

        const id = key(book.SheetNames[active], td.dataset.address ?? '');
        const text = td.textContent ?? '';

        if (text === td.dataset.original) {
            pending.delete(id);
            td.classList.remove('dirty');
        } else {
            pending.set(id, text);
            td.classList.add('dirty');
        }

        updateActions();
        scheduleSave();
    });

    grid.addEventListener('focusout', () => {
        if (pending.size > 0) {
            void commit();
        }
    });

    const renderTabs = (): void => {
        tabs.replaceChildren(...book.SheetNames.map((sheetName, index) => {
            const tab = el('button', 'office-tab', sheetName);
            tab.type = 'button';
            tab.classList.toggle('active', index === active);
            tab.addEventListener('click', () => {
                active = index;
                renderTabs();
                renderSheet();
            });
            return tab;
        }));
    };

    const applyEdits = (edits: Map<string, string>): void => {
        for (const [id, value] of edits) {
            const split = id.lastIndexOf('!');
            const sheet = book.Sheets[id.slice(0, split)];
            const address = id.slice(split + 1);
            const existing = sheet[address] as Cell | undefined;
            const trimmed = value.trim();
            const number = Number(trimmed.replace(/,/g, ''));

            if (trimmed === '') {
                delete sheet[address];
            } else if (Number.isFinite(number) && (!existing || existing.t === 'n')) {
                sheet[address] = existing?.z ? { t: 'n', v: number, z: existing.z } : { t: 'n', v: number };
            } else {
                sheet[address] = { t: 's', v: value };
            }

            if (trimmed !== '') {
                const at = xlsx.utils.decode_cell(address);
                const used = sheet['!ref'] ? xlsx.utils.decode_range(sheet['!ref'] as string) : { s: { r: 0, c: 0 }, e: { r: 0, c: 0 } };
                used.e.r = Math.max(used.e.r, at.r);
                used.e.c = Math.max(used.e.c, at.c);
                sheet['!ref'] = xlsx.utils.encode_range(used);
            }
        }
    };

    const commit = async (): Promise<void> => {
        window.clearTimeout(saveTimer);

        if (!save || saving || pending.size === 0) {
            return;
        }

        saving = true;
        status.textContent = 'Saving...';
        const snapshot = new Map(pending);

        try {
            applyEdits(snapshot);
            const data = xlsx.write(book, { type: 'array', bookType: 'xlsx' });
            await save(new Blob([data], { type: XLSX_MIME }));

            for (const [id, value] of snapshot) {
                if (pending.get(id) === value) {
                    pending.delete(id);
                }
            }

            grid.querySelectorAll<HTMLElement>('td.dirty').forEach((td) => {
                const written = snapshot.get(key(book.SheetNames[active], td.dataset.address ?? ''));

                if (written !== undefined && (td.textContent ?? '') === written) {
                    td.dataset.original = written;
                    td.classList.remove('dirty');
                }
            });

            status.textContent = pending.size === 0 ? 'All changes saved.' : 'Unsaved changes.';
        } catch (failure) {
            status.textContent = `Save failed: ${(failure as Error).message}`;
        } finally {
            saving = false;
            updateActions();

            if (pending.size > 0 && !status.textContent?.startsWith('Save failed')) {
                scheduleSave();
            }
        }
    };

    function scheduleSave(): void {
        window.clearTimeout(saveTimer);
        status.textContent = 'Unsaved changes...';
        saveTimer = window.setTimeout(() => void commit(), AUTOSAVE_MILLISECONDS);
    }

    function updateActions(): void {
        const buttons: HTMLElement[] = [];

        if (canEdit && !editing) {
            const edit = el('button', 'link-button', 'Edit');
            edit.type = 'button';
            edit.addEventListener('click', () => {
                editing = true;
                status.textContent = 'Editing: click a cell and type. Changes save automatically; saving rewrites the workbook, so charts and images may be lost.';
                updateActions();
                renderSheet();
            });
            buttons.push(edit);
        }

        if (editing) {
            const done = el('button', undefined, 'Done');
            done.type = 'button';
            done.addEventListener('click', () => {
                void (async () => {
                    await commit();

                    if (pending.size === 0) {
                        editing = false;
                        status.textContent = '';
                        updateActions();
                        renderSheet();
                    }
                })();
            });
            buttons.push(done);
        }

        actions.replaceChildren(status, ...buttons);
    }

    renderTabs();
    updateActions();
    renderSheet();
}

// Renders a Word, Excel or PowerPoint file into host; Excel is editable when a save callback is given.
export async function renderOffice(
    host: HTMLElement,
    kind: OfficeKind,
    file: { name: string; url: string; save?: OfficeSave }
): Promise<void> {
    const stage = el('div', 'office-viewer');
    stage.appendChild(el('p', 'hint', `Opening ${file.name}...`));
    host.appendChild(stage);

    try {
        const buffer = await fetchBuffer(file.url);
        stage.replaceChildren();

        if (kind === 'word') {
            await renderWord(stage, buffer);
        } else if (kind === 'excel') {
            await renderExcel(stage, file.name, buffer, file.save);
        } else {
            await renderPowerPoint(stage, buffer);
        }
    } catch (failure) {
        stage.replaceChildren(el('p', 'hint', `${file.name} could not be previewed: ${(failure as Error).message} Use the download link instead.`));
    }
}
