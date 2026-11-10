import * as api from './api.js';
import type { TrackedItem } from './api.js';
import { askConfirm, askForm, askText } from './dialogs.js';
import { byId, el, showError } from './dom.js';
import { loadScript } from './officeViewer.js';

interface XlsxLib {
    read(data: ArrayBuffer, options: { type: 'array' }): { SheetNames: string[]; Sheets: Record<string, Record<string, { l?: { Target?: string } }>> };
    write(book: unknown, options: { type: 'array'; bookType: 'xlsx' }): ArrayBuffer;
    utils: {
        sheet_to_json(sheet: unknown, options: { header: 1; defval: string; raw: boolean }): unknown[][];
        aoa_to_sheet(rows: unknown[][]): Record<string, unknown>;
        book_new(): unknown;
        book_append_sheet(book: unknown, sheet: unknown, name: string): void;
        encode_cell(address: { r: number; c: number }): string;
    };
}

const QUARTERS = ['Q1', 'Q2', 'Q3', 'Q4'];
const ITERATIONS = ['', 'I1', 'I2', 'I3', 'I4', 'I5', 'I6', 'I7'];
const KIND_LABEL: Record<string, string> = { Ticket: 'Ticket / story', KB: 'KB / note' };
const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const ALL = 'all';

const options = (values: string[], labels?: Record<string, string>): Array<{ value: string; label: string }> =>
    values.map((value) => ({ value, label: labels?.[value] ?? (value || '- (none)') }));

const groupKey = (item: { year: number; quarter: string }): string => `${item.year} ${item.quarter}`;

const cell = (grid: unknown[][], row: number, column: number): string => String(grid[row]?.[column] ?? '').replace(/\s+/g, ' ').trim();

// Reads the sheet layout: Owner | Title | Category | Quarter code (Q4I2) | Comment | [Type | Year | Link].
function parseSheet(sheet: Record<string, { l?: { Target?: string } }>, grid: unknown[][], defaultYear: number, xlsx: XlsxLib): { items: Array<Omit<TrackedItem, 'id'>>; skipped: number } {
    const items: Array<Omit<TrackedItem, 'id'>> = [];
    let skipped = 0;
    let kind: 'Ticket' | 'KB' = 'Ticket';

    for (let row = 0; row < grid.length; row++) {
        const title = cell(grid, row, 1);
        const category = cell(grid, row, 2);
        const code = cell(grid, row, 3);
        const comment = cell(grid, row, 4);

        if (!title) {
            continue;
        }

        const match = /Q([1-4])\s*[-_ ]?\s*(I\d)?/i.exec(code);

        if (!match) {
            if (!category && !code && !comment && /^(title|tickets?|stor|kb|notes?|updated|completed)/i.test(title)) {
                kind = /kb|notes?/i.test(title) ? 'KB' : 'Ticket';
            } else {
                skipped++;
            }

            continue;
        }

        const explicitKind = cell(grid, row, 5);
        const explicitYear = Number(cell(grid, row, 6));
        const link = sheet[xlsx.utils.encode_cell({ r: row, c: 1 })]?.l?.Target ?? cell(grid, row, 7);

        items.push({
            kind: /kb|note/i.test(explicitKind) ? 'KB' : /ticket|stor/i.test(explicitKind) ? 'Ticket' : kind,
            title,
            url: /^https?:\/\//i.test(link) ? link : '',
            category,
            year: explicitYear >= 2000 && explicitYear <= 2100 ? explicitYear : defaultYear,
            quarter: `Q${match[1]}`,
            iteration: (match[2] ?? '').toUpperCase(),
            owner: cell(grid, row, 0),
            comment
        });
    }

    return { items, skipped };
}

export function initTrackerView(): void {
    const host = byId('tracker-list');
    const message = byId('tracker-message');
    const periodFilter = byId<HTMLSelectElement>('tracker-period');
    const kindFilter = byId<HTMLSelectElement>('tracker-kind');
    const search = byId<HTMLInputElement>('tracker-search');
    const importInput = byId<HTMLInputElement>('tracker-import-input');

    let items: TrackedItem[] = [];
    let owner = '';
    const selected = new Set<string>();

    const selectionBar = (): HTMLElement => {
        const bar = el('div', 'tracker-selection');
        bar.hidden = selected.size === 0;

        if (selected.size === 0) {
            return bar;
        }

        const chosen = items.filter((item) => selected.has(item.id));
        const button = (label: string, className: string, handler: () => void): HTMLButtonElement => {
            const control = el('button', className, label);
            control.type = 'button';
            control.addEventListener('click', handler);
            return control;
        };

        const actions = el('div', 'secure-actions');

        if (chosen.length === 1) {
            actions.appendChild(button('Edit', '', () => void form(chosen[0])));
        }

        actions.append(
            button('Delete', 'secondary danger', () => {
                void (async () => {
                    const text = chosen.length === 1 ? `Delete "${chosen[0].title}"?` : `Delete ${chosen.length} entries?`;

                    if (await askConfirm('Delete entries', text, 'Delete')) {
                        const ids = chosen.map((item) => item.id);
                        ids.forEach((id) => selected.delete(id));
                        await perform(async () => {
                            for (const id of ids) {
                                await api.deleteTrackedItem(id);
                            }
                        }, ids.length === 1 ? 'Deleted the entry.' : `Deleted ${ids.length} entries.`);
                    }
                })();
            }),
            button('Clear', 'secondary', () => {
                selected.clear();
                render();
            }));
        bar.append(el('strong', undefined, `${chosen.length} selected`), actions);
        return bar;
    };

    const setMessage = (text: string, tone: 'ok' | 'error' = 'ok'): void => {
        message.textContent = text;
        message.className = `secure-status ${tone}`;
    };

    const visible = (): TrackedItem[] => {
        const query = search.value.trim().toLowerCase();

        return items.filter((item) =>
            (periodFilter.value === ALL || groupKey(item) === periodFilter.value)
            && (kindFilter.value === ALL || item.kind === kindFilter.value)
            && (query === '' || `${item.title} ${item.category} ${item.owner} ${item.comment}`.toLowerCase().includes(query)));
    };

    const fillPeriods = (): void => {
        const keep = periodFilter.value || ALL;
        const keys = [...new Set(items.map(groupKey))];
        periodFilter.replaceChildren(...[ALL, ...keys].map((key) => Object.assign(el('option', undefined, key === ALL ? 'All quarters' : key.replace(' ', ' / ')), { value: key })));
        periodFilter.value = keys.includes(keep) || keep === ALL ? keep : ALL;
    };

    const badge = (kind: string): HTMLElement => el('span', `secure-badge ${kind === 'KB' ? 'env-stg' : 'infra-nprd'}`, kind === 'KB' ? 'KB' : 'Ticket');

    const form = async (existing?: TrackedItem): Promise<void> => {
        const now = new Date();
        const years = new Set<number>([now.getFullYear() - 2, now.getFullYear() - 1, now.getFullYear(), now.getFullYear() + 1, ...items.map((item) => item.year)]);
        const values = await askForm(existing ? 'Edit entry' : 'Add entry', 'Track a ticket, story or KB update for a quarter.', 'Save', [
            { name: 'kind', label: 'Type', kind: 'select', value: existing?.kind ?? 'Ticket', options: options(['Ticket', 'KB'], KIND_LABEL) },
            { name: 'title', label: 'Title', kind: 'text', value: existing?.title ?? '', required: true, maxLength: 200 },
            { name: 'url', label: 'Link (optional)', kind: 'text', value: existing?.url ?? '', maxLength: 2000 },
            { name: 'category', label: 'Category (e.g. SDE provision request, Nprd upgrade)', kind: 'text', value: existing?.category ?? '', maxLength: 100 },
            { name: 'year', label: 'Year', kind: 'select', value: String(existing?.year ?? now.getFullYear()), options: options([...years].sort().map(String)) },
            { name: 'quarter', label: 'Quarter', kind: 'select', value: existing?.quarter ?? `Q${Math.floor(now.getMonth() / 3) + 1}`, options: options(QUARTERS) },
            { name: 'iteration', label: 'Iteration', kind: 'select', value: existing?.iteration ?? '', options: options(ITERATIONS) },
            { name: 'owner', label: 'Owner', kind: 'text', value: existing?.owner ?? owner, maxLength: 100 },
            { name: 'comment', label: 'Comment', kind: 'textarea', value: existing?.comment ?? '', maxLength: 1000 }
        ]);

        if (!values) {
            return;
        }

        await perform(async () => {
            await api.saveTrackedItem(existing?.id, { ...values, year: Number(values.year) } as unknown as Omit<TrackedItem, 'id'>);
        }, existing ? 'Saved the entry.' : 'Added the entry.');
    };

    const perform = async (work: () => Promise<void>, success: string): Promise<void> => {
        try {
            await work();
            items = await api.getTrackedItems();
            for (const id of [...selected]) {
                if (!items.some((item) => item.id === id)) {
                    selected.delete(id);
                }
            }

            fillPeriods();
            setMessage(success);
        } catch (failure) {
            setMessage((failure as Error).message, 'error');
        }

        render();
    };

    const render = (): void => {
        const shown = visible();

        if (shown.length === 0) {
            host.replaceChildren(el('p', 'secure-empty', items.length === 0
                ? 'Nothing tracked yet. Use Add entry, or Import Excel to bring in your existing sheet.'
                : 'No entries match the current filters.'));
            return;
        }

        const groups = new Map<string, TrackedItem[]>();

        for (const item of shown) {
            const key = groupKey(item);
            groups.set(key, [...(groups.get(key) ?? []), item]);
        }

        host.replaceChildren(selectionBar(), ...[...groups].map(([key, group]) => {
            const section = el('section', 'tracker-group');
            const tickets = group.filter((item) => item.kind === 'Ticket').length;
            section.append(el('h3', undefined, key.replace(' ', ' / ')), el('p', 'secure-muted', `${tickets} ticket${tickets === 1 ? '' : 's'}, ${group.length - tickets} KB / note${group.length - tickets === 1 ? '' : 's'}`));

            const table = el('table', 'secure-table');
            table.createTHead().insertRow().append(...['', '#', 'Type', 'Title', 'Category', 'Iteration', 'Owner', 'Comment'].map((name) => el('th', undefined, name)));
            const headCheck = el('input');
            headCheck.type = 'checkbox';
            headCheck.setAttribute('aria-label', `Select all in ${key}`);
            headCheck.checked = group.every((item) => selected.has(item.id));
            headCheck.indeterminate = !headCheck.checked && group.some((item) => selected.has(item.id));
            headCheck.addEventListener('change', () => {
                group.forEach((item) => headCheck.checked ? selected.add(item.id) : selected.delete(item.id));
                render();
            });
            table.tHead!.rows[0].cells[0].appendChild(headCheck);
            const body = table.createTBody();
            let number = 0;

            for (const item of group) {
                const row = body.insertRow();
                row.className = 'secure-row';
                row.classList.toggle('selected', selected.has(item.id));

                const check = el('input');
                check.type = 'checkbox';
                check.checked = selected.has(item.id);
                check.setAttribute('aria-label', `Select ${item.title}`);
                check.addEventListener('change', () => {
                    check.checked ? selected.add(item.id) : selected.delete(item.id);
                    render();
                });
                row.insertCell().appendChild(check);
                row.insertCell().textContent = String(++number);
                row.insertCell().appendChild(badge(item.kind));

                const titleCell = row.insertCell();

                if (item.url) {
                    const link = el('a', 'tracker-link', item.title);
                    link.href = item.url;
                    link.target = '_blank';
                    link.rel = 'noopener noreferrer';
                    titleCell.appendChild(link);
                } else {
                    titleCell.textContent = item.title;
                }

                row.insertCell().textContent = item.category || '-';
                row.insertCell().textContent = item.iteration || '-';
                row.insertCell().textContent = item.owner || '-';
                row.insertCell().textContent = item.comment || '-';
            }

            section.appendChild(el('div', 'secure-list')).appendChild(table);
            return section;
        }));
    };

    const loadXlsx = async (): Promise<XlsxLib> => {
        await loadScript('/lib/xlsx.full.min.js');
        return (window as unknown as { XLSX: XlsxLib }).XLSX;
    };

    const importFile = async (file: File): Promise<void> => {
        try {
            const year = await askText('Import Excel', 'Year', String(new Date().getFullYear()), 'Import', 'Used for rows that have no Year column. Quarter codes like Q4I2 are read from the sheet.');

            if (!year) {
                return;
            }

            const defaultYear = Number(year);

            if (!Number.isInteger(defaultYear) || defaultYear < 2000 || defaultYear > 2100) {
                throw new Error('Enter a year between 2000 and 2100.');
            }

            const xlsx = await loadXlsx();
            const book = xlsx.read(await file.arrayBuffer(), { type: 'array' });
            const found: Array<Omit<TrackedItem, 'id'>> = [];
            let skippedRows = 0;

            for (const name of book.SheetNames) {
                const sheet = book.Sheets[name];
                const parsed = parseSheet(sheet, xlsx.utils.sheet_to_json(sheet, { header: 1, defval: '', raw: false }), defaultYear, xlsx);
                found.push(...parsed.items);
                skippedRows += parsed.skipped;
            }

            if (found.length === 0) {
                throw new Error('No rows with a quarter code such as Q4I2 were found in column D.');
            }

            const result = await api.importTrackedItems(found);
            await perform(async () => undefined, `Imported ${result.added} entr${result.added === 1 ? 'y' : 'ies'}; ${result.skipped} already existed${skippedRows > 0 ? `, ${skippedRows} rows without a quarter were ignored` : ''}.`);
        } catch (failure) {
            setMessage((failure as Error).message, 'error');
        }
    };

    const exportFile = async (): Promise<void> => {
        const shown = visible();

        if (shown.length === 0) {
            setMessage('There is nothing to export for the current filters.', 'error');
            return;
        }

        try {
            const xlsx = await loadXlsx();
            const rows: unknown[][] = [['Owner', 'Title', 'Category', 'Quarter', 'Comment', 'Type', 'Year', 'Link'],
                ...shown.map((item) => [item.owner, item.title, item.category, `${item.quarter}${item.iteration}`, item.comment, item.kind, item.year, item.url])];
            const sheet = xlsx.utils.aoa_to_sheet(rows);
            sheet['!cols'] = [{ wch: 18 }, { wch: 60 }, { wch: 30 }, { wch: 10 }, { wch: 40 }, { wch: 8 }, { wch: 8 }, { wch: 40 }];

            shown.forEach((item, index) => {
                const target = sheet[xlsx.utils.encode_cell({ r: index + 1, c: 1 })] as { l?: { Target: string } } | undefined;

                if (target && item.url) {
                    target.l = { Target: item.url };
                }
            });

            const book = xlsx.utils.book_new();
            xlsx.utils.book_append_sheet(book, sheet, 'Tracked work');
            const blob = new Blob([xlsx.write(book, { type: 'array', bookType: 'xlsx' })], { type: XLSX_MIME });
            const link = el('a');
            link.href = URL.createObjectURL(blob);
            link.download = `tracked-work-${periodFilter.value === ALL ? 'all' : periodFilter.value.replace(' ', '-')}.xlsx`;
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(link.href);
            setMessage(`Exported ${shown.length} entr${shown.length === 1 ? 'y' : 'ies'}.`);
        } catch (failure) {
            setMessage((failure as Error).message, 'error');
        }
    };

    byId('tracker-add').addEventListener('click', () => void form());
    byId('tracker-import').addEventListener('click', () => importInput.click());
    byId('tracker-export').addEventListener('click', () => void exportFile());
    importInput.addEventListener('change', () => {
        const file = importInput.files?.[0];
        importInput.value = '';

        if (file) {
            void importFile(file);
        }
    });
    [periodFilter, kindFilter].forEach((filter) => filter.addEventListener('change', render));
    search.addEventListener('input', render);

    void (async () => {
        try {
            [items, owner] = await Promise.all([api.getTrackedItems(), api.getCurrentUser().then((user) => user.displayName)]);
            fillPeriods();
            render();
        } catch (failure) {
            showError(host, (failure as Error).message);
        }
    })();
}
