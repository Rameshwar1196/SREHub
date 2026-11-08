import { getDutyRoster, getDutyYear, saveDutyEntry, saveDutyRoster } from './api.js';
import { byId, el, showError, showProgress } from './dom.js';
import { enhanceSelects } from './dropdown.js';
import { DutyEntry, DutyRoster, DutyRosterBlock, OneNoteIteration } from './dutyLogTypes.js';
import { askConfirm } from './dialogs.js';
import { loadScript } from './officeViewer.js';
import { select, svg, textInput } from './workLogView.js';

const HANDOVER = ['Given', 'Received', 'Pending', 'N/A'];
const ONENOTE = ['Updated', 'Pending', 'N/A'];

const JAPAN = { label: 'Japan', time: '5:30 AM - 1:30 PM' };
const INDIA = { label: 'India', time: '1:30 PM - 9:30 PM' };

const SHIFT_OPTIONS = [JAPAN.label, INDIA.label, 'Japan + India'];

// The shift decides the time, so the time is never typed.
const timeForShift = (shift: string): string => [JAPAN, INDIA].filter((item) => shift.includes(item.label)).map((item) => item.time).join(' / ');

const DEFAULT_IT_NAMES: Array<[string, string]> = [
    ['IT1', 'Sandip'], ['IT2', 'Shubham'], ['IT3', 'Abhijit'], ['IT4', 'Vaishnavi'],
    ['IT5', 'Navneet'], ['IT6', 'Rishabh'], ['IT7', 'Ramesh'], ['IT8', 'Shreyas']
];

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const MAX_RANGE_DAYS = 400;
const MAX_ROTATION = 60;

// Roster entries closer together than this belong to the same quarter.
const QUARTER_GAP_DAYS = 14;

const pad = (value: number): string => String(value).padStart(2, '0');
const isoDate = (date: Date): string => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;

function parseDay(value: string): Date {
    const [year, month, day] = value.split('-').map(Number);
    return new Date(year, month - 1, day);
}

function addDays(value: string, count: number): string {
    const day = parseDay(value);
    day.setDate(day.getDate() + count);
    return isoDate(day);
}

function eachDay(start: string, end: string): string[] {
    const days: string[] = [];

    for (let day = start; day <= end && days.length < MAX_RANGE_DAYS; day = addDays(day, 1)) {
        days.push(day);
    }

    return days;
}

const isDate = (value: string): boolean => /^\d{4}-\d{2}-\d{2}$/.test(value) && isoDate(parseDay(value)) === value;
const isWeekend = (value: string): boolean => [0, 6].includes(parseDay(value).getDay());

const shortDay = (value: string): string => `${pad(parseDay(value).getDate())} ${MONTHS[parseDay(value).getMonth()].slice(0, 3)}`;
const fullDay = (value: string): string => `${shortDay(value)} ${parseDay(value).getFullYear()}`;
const monthYear = (value: string): string => `${MONTHS[parseDay(value).getMonth()].slice(0, 3)} ${parseDay(value).getFullYear()}`;

// "DO06", "do 6" and "DO6" are the same person, as are "IT07" and "IT7"; other labels such as "Japan DO" are kept as typed.
function normalizeId(value: string): string {
    const match = /^(do|it)\s*0*(\d+)$/i.exec(value.trim());
    return match ? `${match[1].toUpperCase()}${match[2]}` : value.trim().toUpperCase();
}

const isSpecial = (value: string): boolean => /^(japan|eu)\s*do$/i.test(value.trim());

interface Cover {
    label: string;
    time: string;
    japan: boolean;
    india: boolean;
}

interface Slot {
    label: string;
    japan: string;
    india: string;
}

interface Quarter {
    start: string;
    end: string;
    label: string;
}

interface XlsxLib {
    read(data: ArrayBuffer, options: { type: 'array' }): { SheetNames: string[]; Sheets: Record<string, unknown> };
    utils: { sheet_to_json(sheet: unknown, options: { header: 1; defval: string; raw: boolean }): unknown[][] };
}

interface GridResult {
    blocks: DutyRosterBlock[];
    headers: number;
    shiftRows: number;
}

const blankEntry = (date: string): DutyEntry => ({ date, doShift: '', shiftTime: '', handover: '', doNote: '' });

const isBlank = (entry: DutyEntry): boolean => !(entry.doShift || entry.shiftTime || entry.handover || entry.doNote);

function buildQuarters(spans: Array<{ start: string; end: string }>): Quarter[] {
    const quarters: Quarter[] = [];

    for (const span of [...spans].sort((first, second) => first.start.localeCompare(second.start))) {
        const last = quarters[quarters.length - 1];

        if (last && span.start <= addDays(last.end, QUARTER_GAP_DAYS)) {
            last.end = span.end > last.end ? span.end : last.end;
        } else {
            quarters.push({ start: span.start, end: span.end, label: '' });
        }
    }

    quarters.forEach((quarter) => { quarter.label = `${monthYear(quarter.start)} - ${monthYear(quarter.end)}`; });
    return quarters;
}

function monthNumber(name: string): number {
    return MONTHS.findIndex((month) => month.toLowerCase().startsWith(name.slice(0, 3).toLowerCase())) + 1;
}

const RANGE = /(\d{1,2})\s*(?:st|nd|rd|th)?\s*([A-Za-z]{3,9})?\s*[-\u2013\u2014]\s*(\d{1,2})\s*(?:st|nd|rd|th)?\s*([A-Za-z]{3,9})/;

// Reads the quarter grid: a "Week n" header cell, the date range under it, then the Japan and India shift rows.
function parseRosterGrid(grid: unknown[][], firstYear: number): GridResult {
    const text = (row: number, column: number): string => String(grid[row]?.[column] ?? '').replace(/\s+/g, ' ').trim();
    const result: GridResult = { blocks: [], headers: 0, shiftRows: 0 };
    let year = firstYear;
    let previousMonth = 0;

    for (let row = 0; row < grid.length; row++) {
        for (let column = 0; column < (grid[row]?.length ?? 0); column++) {
            const label = text(row, column);

            if (!/^\W*week\s*\d+/i.test(label)) {
                continue;
            }

            result.headers++;

            let range: RegExpExecArray | null = null;

            for (let dateRow = row + 1; dateRow <= row + 3 && !range; dateRow++) {
                range = RANGE.exec(text(dateRow, column));
            }

            const endMonth = range ? monthNumber(range[4]) : 0;

            if (!range || endMonth === 0) {
                continue;
            }

            const startDay = Number(range[1]);
            const endDay = Number(range[3]);
            const startMonth = range[2] ? monthNumber(range[2]) : startDay > endDay ? (endMonth === 1 ? 12 : endMonth - 1) : endMonth;

            if (startMonth < previousMonth) {
                year++;
            }

            const endYear = endMonth < startMonth ? year + 1 : year;
            const block: DutyRosterBlock = {
                start: `${year}-${pad(startMonth)}-${pad(startDay)}`,
                end: `${endYear}-${pad(endMonth)}-${pad(endDay)}`,
                label,
                japan: '',
                india: ''
            };

            for (let below = row + 1; below <= row + 10 && below < grid.length; below++) {
                const rowLabel = `${text(below, 0)} ${text(below, 1)} ${text(below, 2)}`;

                if (/japan\s*shift/i.test(rowLabel)) {
                    block.japan = text(below, column);
                    result.shiftRows++;
                } else if (/india\s*shift/i.test(rowLabel)) {
                    block.india = text(below, column);
                    result.shiftRows++;
                }
            }

            result.blocks.push(block);
            year = endYear;
            previousMonth = endMonth;
        }
    }

    return result;
}

function parseGrids(grids: unknown[][][], firstYear: number): GridResult {
    const total: GridResult = { blocks: [], headers: 0, shiftRows: 0 };

    for (const grid of grids) {
        const found = parseRosterGrid(grid, firstYear);
        total.blocks.push(...found.blocks);
        total.headers += found.headers;
        total.shiftRows += found.shiftRows;
    }

    return total;
}

// The sheet has no year, so pick the one that puts the first week near today.
function parseWithInferredYear(grids: unknown[][][]): GridResult {
    const thisYear = new Date().getFullYear();
    const result = parseGrids(grids, thisYear);
    const first = result.blocks[0]?.start;

    if (!first) {
        return result;
    }

    const daysAway = (parseDay(first).getTime() - Date.now()) / 86400000;

    if (daysAway < -200) {
        return parseGrids(grids, thisYear + 1);
    }

    return daysAway > 250 ? parseGrids(grids, thisYear - 1) : result;
}

async function readWorkbookGrids(file: File): Promise<unknown[][][]> {
    await loadScript('/lib/xlsx.full.min.js');
    const xlsx = (window as unknown as { XLSX: XlsxLib }).XLSX;
    const book = xlsx.read(await file.arrayBuffer(), { type: 'array' });
    return book.SheetNames.map((name) => xlsx.utils.sheet_to_json(book.Sheets[name], { header: 1, defval: '', raw: false }));
}

// Excel puts copied cells on the clipboard as tab-separated text.
const parsePasted = (value: string): unknown[][] => value.split(/\r?\n/).map((line) => line.split('\t'));

function regroup(perDay: Map<string, Slot>): DutyRosterBlock[] {
    const blocks: DutyRosterBlock[] = [];

    for (const day of [...perDay.keys()].sort()) {
        const slot = perDay.get(day)!;
        const last = blocks[blocks.length - 1];

        if (last && last.label === slot.label && last.japan === slot.japan && last.india === slot.india && addDays(last.end, 1) === day) {
            last.end = day;
        } else {
            blocks.push({ start: day, end: day, ...slot });
        }
    }

    return blocks;
}

interface PieSlice {
    label: string;
    value: number;
    colour: string;
}

function pieChart(slices: PieSlice[]): SVGSVGElement {
    const chart = svg('svg', { viewBox: '0 0 200 200', role: 'img', 'aria-label': 'My DO days by type' });
    const total = slices.reduce((sum, slice) => sum + slice.value, 0);

    if (total === 0) {
        chart.append(
            svg('circle', { cx: '100', cy: '100', r: '88', class: 'wl-pie-empty' }),
            Object.assign(svg('text', { x: '100', y: '100', class: 'wl-pie-note' }), { textContent: 'No DO days' }));
        return chart;
    }

    const shown = slices.filter((slice) => slice.value > 0);
    let angle = -Math.PI / 2;

    for (const slice of shown) {
        const sweep = (slice.value / total) * Math.PI * 2;
        const end = angle + sweep;
        const percent = Math.round((slice.value / total) * 100);
        const title = Object.assign(svg('title', {}), { textContent: `${slice.label}: ${slice.value} (${percent}%)` });

        const shape = shown.length === 1
            ? svg('circle', { cx: '100', cy: '100', r: '90', class: 'wl-slice' })
            : svg('path', {
                class: 'wl-slice',
                d: `M100 100 L${100 + 90 * Math.cos(angle)} ${100 + 90 * Math.sin(angle)} `
                    + `A90 90 0 ${sweep > Math.PI ? 1 : 0} 1 ${100 + 90 * Math.cos(end)} ${100 + 90 * Math.sin(end)} Z`
            });

        shape.style.fill = slice.colour;
        shape.appendChild(title);
        chart.appendChild(shape);

        if (percent >= 6) {
            const middle = angle + sweep / 2;
            const label = svg('text', {
                x: String(100 + 58 * Math.cos(middle)),
                y: String(100 + 58 * Math.sin(middle)),
                class: 'wl-pie-label'
            });
            label.textContent = `${percent}%`;
            chart.appendChild(label);
        }

        angle = end;
    }

    return chart;
}

export function initDutyLogView(): void {
    const now = new Date();

    const quarterSelect = byId<HTMLSelectElement>('duty-quarter');
    const range = byId('duty-range');
    const rosterHost = byId('duty-roster-host');
    const doHost = byId('duty-do-host');
    const oneNoteHost = byId('duty-onenote-host');
    const doPanel = byId<HTMLDetailsElement>('duty-do-panel');
    const oneNotePanel = byId<HTMLDetailsElement>('duty-onenote-panel');
    const doCount = byId('duty-do-count');
    const oneNoteCount = byId('duty-onenote-count');
    const summaryBody = byId('duty-summary-body');
    const summaryPeriod = byId('duty-summary-period');
    const nextDuty = byId('duty-next');
    const overview = byId('duty-overview');
    const overviewPie = byId('duty-overview-pie');
    const overviewLegend = byId('duty-overview-legend');
    const overviewPeriod = byId('duty-overview-period');
    const reminder = byId('duty-reminder');
    const reminderText = byId('duty-reminder-text');
    const reminderJump = byId<HTMLButtonElement>('duty-reminder-jump');
    const onlyMineIt = byId<HTMLInputElement>('duty-only-mine-it');
    const onlyMine = byId<HTMLInputElement>('duty-only-mine');
    const exportLink = byId<HTMLAnchorElement>('duty-export');
    const exportOneNoteLink = byId<HTMLAnchorElement>('duty-export-onenote');
    const importButton = byId<HTMLButtonElement>('duty-import');
    const importFile = byId<HTMLInputElement>('duty-import-file');
    const status = byId('duty-status');
    const myIdInput = byId<HTMLInputElement>('duty-my-id');
    const myItInput = byId<HTMLInputElement>('duty-my-it');
    const namesInput = byId<HTMLTextAreaElement>('duty-names');

    const rosterFrom = byId<HTMLInputElement>('duty-roster-from');
    const rosterTo = byId<HTMLInputElement>('duty-roster-to');
    const rosterLabel = byId<HTMLInputElement>('duty-roster-label');
    const rosterJapan = byId<HTMLInputElement>('duty-roster-japan');
    const rosterIndia = byId<HTMLInputElement>('duty-roster-india');
    const rotationStart = byId<HTMLInputElement>('duty-rot-start');
    const rotationDays = byId<HTMLInputElement>('duty-rot-days');
    const rotationCount = byId<HTMLInputElement>('duty-rot-count');
    const rotationFirst = byId<HTMLInputElement>('duty-rot-first');
    const pasteBox = byId<HTMLTextAreaElement>('duty-paste');
    const undoButton = byId<HTMLButtonElement>('duty-undo');

    const roster: DutyRoster = { settings: { myDoId: 'DO3', myItId: '', names: {} }, blocks: [], iterations: [] };
    const entries = new Map<string, DutyEntry>();
    const loadedYears = new Set<number>();
    const myDays = new Map<string, Cover>();
    const flags: Array<{ item: OneNoteIteration; row: HTMLTableRowElement; flag: HTMLElement }> = [];

    let quarters: Quarter[] = [];
    let quarterIndex = 0;
    let generation = 0;
    let queue: Promise<unknown> = Promise.resolve();
    let pending = 0;
    const failures = new Set<string>();
    const history: string[] = [];
    const MAX_HISTORY = 30;

    const todayIso = (): string => isoDate(new Date());
    const currentQuarter = (): Quarter | undefined => quarters[quarterIndex];
    const inQuarter = (date: string): boolean => {
        const quarter = currentQuarter();
        return quarter !== undefined && date >= quarter.start && date <= quarter.end;
    };
    const spansInQuarter = (start: string, end: string): boolean => {
        const quarter = currentQuarter();
        return quarter !== undefined && start >= quarter.start && end <= quarter.end;
    };

    // One request at a time keeps edits of the same day from overwriting each other.
    const enqueue = <T>(work: () => Promise<T>): Promise<T> => {
        pending++;
        const result = queue.then(work);
        queue = result.then(() => undefined, () => undefined).then(() => { pending--; });
        return result;
    };

    const explain = (failure: Error): string =>
        /\b404\b/.test(failure.message) ? `${failure.message} Restart SRE Hub so the server has the latest version.` : failure.message;

    const shiftsFor = (block: DutyRosterBlock): Array<typeof JAPAN> => {
        const mine = normalizeId(roster.settings.myDoId);
        const shifts: Array<typeof JAPAN> = [];

        if (mine && normalizeId(block.japan) === mine) {
            shifts.push(JAPAN);
        }

        if (mine && normalizeId(block.india) === mine) {
            shifts.push(INDIA);
        }

        return shifts;
    };

    const isMyIteration = (item: OneNoteIteration): boolean => {
        const mine = normalizeId(roster.settings.myItId);
        return mine !== '' && normalizeId(item.owner) === mine;
    };

    const nameOf = (id: string): string => {
        const wanted = normalizeId(id);
        const found = Object.entries(roster.settings.names).find(([key]) => normalizeId(key) === wanted);
        return found?.[1] ?? '';
    };

    const rebuildMyDays = (): void => {
        myDays.clear();

        for (const block of roster.blocks) {
            const shifts = shiftsFor(block);

            if (shifts.length > 0) {
                const cover: Cover = {
                    label: shifts.map((item) => item.label).join(' + '),
                    time: shifts.map((item) => item.time).join(' / '),
                    japan: shifts.includes(JAPAN),
                    india: shifts.includes(INDIA)
                };
                eachDay(block.start, block.end).forEach((date) => myDays.set(date, cover));
            }
        }
    };

    const daysInView = (): string[] =>
        [...new Set([...myDays.keys(), ...entries.keys()].filter(inQuarter))].sort();

    const iterationsInQuarter = (): OneNoteIteration[] =>
        roster.iterations.filter((item) => spansInQuarter(item.start, item.end));

    // An update is missing once the iteration is over and nothing was recorded.
    const isMissing = (item: OneNoteIteration): boolean =>
        isMyIteration(item) && item.end < todayIso() && !item.status;

    const saveRosterNow = (): void => {
        const snapshot: DutyRoster = {
            settings: { myDoId: roster.settings.myDoId, myItId: roster.settings.myItId, names: { ...roster.settings.names } },
            blocks: roster.blocks.map((block) => ({ ...block })),
            iterations: roster.iterations.map((item) => ({ ...item }))
        };

        enqueue(() => saveDutyRoster(snapshot))
            .then(() => {
                failures.delete('roster');
                status.textContent = 'Roster saved.';
            })
            .catch((failure: Error) => {
                failures.add('roster');
                status.textContent = `NOT SAVED - the roster change was not stored. ${explain(failure)}`;
            });
    };

    // Snapshot before a change so it can be undone; the server also keeps backups of the files.
    const remember = (): void => {
        history.push(JSON.stringify({ settings: roster.settings, blocks: roster.blocks, iterations: roster.iterations }));

        if (history.length > MAX_HISTORY) {
            history.shift();
        }

        undoButton.disabled = false;
    };

    const undo = (): void => {
        const last = history.pop();
        undoButton.disabled = history.length === 0;

        if (!last) {
            return;
        }

        const restored = JSON.parse(last) as DutyRoster;
        roster.settings = restored.settings;
        roster.blocks = restored.blocks;
        roster.iterations = restored.iterations;
        commitRoster();
        status.textContent = 'Undid the last change.';
    };

    const renderNext = (): void => {
        const today = todayIso();
        const upcoming = roster.blocks.find((block) => block.end >= today && shiftsFor(block).length > 0);
        const nextNote = roster.iterations.find((item) => item.end >= today && isMyIteration(item));
        const lines: string[] = [];

        if (upcoming) {
            const covers = shiftsFor(upcoming).map((item) => `${item.label} ${item.time}`).join(' and ');
            lines.push(`${upcoming.start <= today ? 'On DO now' : 'Next DO'}: ${shortDay(upcoming.start)} - ${shortDay(upcoming.end)} (${covers})`);
        }

        if (nextNote) {
            lines.push(`${nextNote.start <= today ? 'OneNote owner now' : 'Next OneNote'}: ${shortDay(nextNote.start)} - ${shortDay(nextNote.end)} (${nextNote.label || 'iteration'})`);
        }

        nextDuty.textContent = lines.length > 0 ? lines.join('\n')
            : roster.blocks.length === 0 && roster.iterations.length === 0 ? 'Import or enter the roster to see your duties.' : 'No upcoming duty in the roster.';
    };

    // Shown in the space left when both panels are collapsed.
    const renderOverview = (rows: Array<[string, number | undefined, number | undefined, string]>, period: string): void => {
        const [total, ...rest] = rows;
        const categories = rest.slice(0, 4);
        const notes = rest[4];
        const dayTotal = categories.reduce((sum, [, days]) => sum + (days ?? 0), 0);

        overviewPeriod.textContent = period;
        overviewPie.replaceChildren(pieChart(categories.map(([label, days, , colour]) => ({ label, value: days ?? 0, colour }))));

        const line = (label: string, value: string, colour?: string): HTMLElement => {
            const item = el('div', 'duty-overview-item');
            const name = el('span', 'wl-category');

            if (colour) {
                const dot = el('i', 'dot');
                dot.style.background = colour;
                name.appendChild(dot);
            }

            name.appendChild(document.createTextNode(label));
            item.append(name, el('span', 'duty-overview-value', value));
            return item;
        };

        const plural = (count: number, word: string): string => `${count} ${word}${count === 1 ? '' : 's'}`;

        overviewLegend.replaceChildren(
            line('My DO days', `${plural(total[1] ?? 0, 'day')} - ${plural(total[2] ?? 0, 'duty').replace('dutys', 'duties')}`, total[3]),
            ...categories.map(([label, days, duties, colour]) => {
                const percent = dayTotal === 0 ? 0 : Math.round(((days ?? 0) / dayTotal) * 100);
                return line(label, `${plural(days ?? 0, 'day')} - ${(duties ?? 0) === 1 ? '1 duty' : `${duties ?? 0} duties`} - ${percent}%`, colour);
            }),
            line(notes[0], `${plural(notes[1] ?? 0, 'day')} - ${plural(notes[2] ?? 0, 'iteration')}`, notes[3]));
    };

    const renderSummary = (): void => {
        const quarter = currentQuarter();
        const days = [...myDays.entries()].filter(([date]) => inQuarter(date));
        const duties = roster.blocks.filter((block) => inQuarter(block.start) && shiftsFor(block).length > 0);
        const mineNotes = iterationsInQuarter().filter(isMyIteration);

        const dayCount = (weekend: boolean, shift: 'japan' | 'india'): number =>
            days.filter(([date, cover]) => isWeekend(date) === weekend && cover[shift]).length;

        const dutyCount = (weekend: boolean, shift: typeof JAPAN): number =>
            duties.filter((block) => isWeekend(block.start) === weekend && shiftsFor(block).includes(shift)).length;

        const noteDays = mineNotes.reduce((total, item) => total + eachDay(item.start, item.end).length, 0);

        const rows: Array<[string, number | undefined, number | undefined, string]> = [
            ['My DO days', days.length, duties.length, 'var(--indigo)'],
            ['DO weekdays - Japan', dayCount(false, 'japan'), dutyCount(false, JAPAN), 'var(--wl-sat)'],
            ['DO weekdays - India', dayCount(false, 'india'), dutyCount(false, INDIA), 'var(--wl-working)'],
            ['DO weekend - Japan', dayCount(true, 'japan'), dutyCount(true, JAPAN), 'var(--wl-compoff)'],
            ['DO weekend - India', dayCount(true, 'india'), dutyCount(true, INDIA), 'var(--wl-holiday)'],
            ['My OneNote iterations', noteDays, mineNotes.length, 'var(--cyan)']
        ];

        summaryPeriod.textContent = quarter?.label ?? '';
        summaryBody.replaceChildren(...rows.map(([label, dayTotal, dutyTotal, colour]) => {
            const tr = el('tr');
            const name = el('span', 'wl-category');
            const dot = el('i', 'dot');
            dot.style.background = colour;
            name.append(dot, document.createTextNode(label));
            tr.insertCell().appendChild(name);
            tr.insertCell().textContent = dayTotal === undefined ? '' : String(dayTotal);
            tr.insertCell().textContent = dutyTotal === undefined ? '' : String(dutyTotal);
            return tr;
        }));

        renderOverview(rows, quarter?.label ?? '');
    };

    const refreshStatus = (): void => {
        renderSummary();
        renderNext();

        const today = todayIso();

        for (const entry of flags) {
            const missing = isMissing(entry.item);
            const due = isMyIteration(entry.item) && !entry.item.status && entry.item.start <= today && today <= entry.item.end;
            entry.row.classList.toggle('missing', missing);
            entry.row.classList.toggle('mine', isMyIteration(entry.item));
            entry.flag.textContent = missing ? 'Missing' : due ? `Due ${shortDay(entry.item.end)}` : '';
        }

        const label = currentQuarter()?.label ?? 'this quarter';
        const mine = iterationsInQuarter().filter(isMyIteration);
        const missing = mine.filter(isMissing);
        reminder.hidden = mine.length === 0;
        reminder.classList.toggle('ok', missing.length === 0);
        reminderJump.hidden = missing.length === 0;

        reminderText.textContent = missing.length === 0
            ? `Your OneNote updates are up to date for ${label}.`
            : `${missing.length} OneNote iteration${missing.length === 1 ? '' : 's'} in ${label} without an update.`;
    };

    const withDefaults = (entry: DutyEntry): DutyEntry => {
        if (isBlank(entry)) {
            return entry;
        }

        const cover = myDays.get(entry.date);
        const shift = entry.doShift || cover?.label || '';
        return { ...entry, doShift: shift, shiftTime: timeForShift(shift) };
    };

    const save = (patch: Partial<DutyEntry>, date: string, stateLabel: HTMLElement): void => {
        const next = withDefaults({ ...(entries.get(date) ?? blankEntry(date)), ...patch });

        if (isBlank(next)) {
            entries.delete(date);
        } else {
            entries.set(date, next);
        }

        stateLabel.className = 'worklog-state';
        stateLabel.textContent = 'Saving...';
        stateLabel.title = '';

        enqueue(() => saveDutyEntry({ ...next }))
            .then(() => {
                failures.delete(date);
                stateLabel.classList.add('saved');
                stateLabel.textContent = 'Saved';
                refreshStatus();
            })
            .catch((failure: Error) => {
                failures.add(date);
                stateLabel.classList.add('failed');
                stateLabel.textContent = 'NOT SAVED';
                stateLabel.title = explain(failure);
            });
    };

    const dateCell = (date: string, row: HTMLTableRowElement): HTMLElement => {
        const day = parseDay(date);
        const weekday = day.getDay();

        if (weekday === 6) {
            row.classList.add('sat');
        } else if (weekday === 0) {
            row.classList.add('sun');
        }

        if (date === todayIso()) {
            row.classList.add('today');
        }

        const cell = el('div', 'wl-date');
        cell.append(
            el('span', 'wl-day', `${pad(day.getDate())} ${MONTHS[day.getMonth()].slice(0, 3)}`),
            el('span', `wl-wd wd-${weekday}`, WEEKDAYS[weekday]));
        return cell;
    };

    const buildTable = (headers: string[], kind: 'roster' | 'do' | 'onenote'): { table: HTMLTableElement; body: HTMLTableSectionElement } => {
        const table = el('table', `worklog-table duty-table duty-${kind}`);
        const head = table.createTHead().insertRow();

        for (const title of headers) {
            head.appendChild(el('th', undefined, title));
        }

        return { table, body: table.createTBody() };
    };

    const emptyNote = (message: string): HTMLElement => el('p', 'wl-bulk-status', message);

    const dateInput = (value: string): HTMLInputElement => {
        const input = el('input');
        input.type = 'date';
        input.value = value;
        return input;
    };

    const wrapWithName = (input: HTMLInputElement, name: HTMLElement): HTMLElement => {
        const box = el('div', 'duty-cover');
        box.append(input, name);
        return box;
    };

    const renderRoster = (): void => {
        const quarter = currentQuarter();

        if (!quarter) {
            rosterHost.replaceChildren(emptyNote('No roster yet. Import your sheet, paste it, or add weeks with Quick Fill below.'));
            return;
        }

        const { table, body } = buildTable(['Week', 'From', 'To', `Japan Shift\n${JAPAN.time}`, `India Shift\n${INDIA.time}`, ''], 'roster');
        const today = todayIso();

        for (const block of roster.blocks.filter((item) => spansInQuarter(item.start, item.end))) {
            const row = body.insertRow();
            row.classList.toggle('mine', shiftsFor(block).length > 0);
            row.classList.toggle('today', block.start <= today && today <= block.end);
            row.classList.toggle('sat', /end|sa\s*,?\s*su/i.test(block.label));
            row.hidden = onlyMine.checked && shiftsFor(block).length === 0;

            const label = textInput(block.label, 'Week');
            const from = dateInput(block.start);
            const to = dateInput(block.end);
            const japan = textInput(block.japan, 'Unassigned');
            const india = textInput(block.india, 'Unassigned');
            const japanName = el('span', 'duty-name', nameOf(block.japan));
            const indiaName = el('span', 'duty-name', nameOf(block.india));
            const remove = el('button', 'link-button', 'Delete');
            remove.type = 'button';

            const mark = (): void => {
                const me = normalizeId(roster.settings.myDoId);
                japan.classList.toggle('duty-me', me !== '' && normalizeId(japan.value) === me);
                india.classList.toggle('duty-me', me !== '' && normalizeId(india.value) === me);
                japan.classList.toggle('duty-special', isSpecial(japan.value));
                india.classList.toggle('duty-special', isSpecial(india.value));
            };

            mark();

            [label, from, to, wrapWithName(japan, japanName), wrapWithName(india, indiaName), remove]
                .forEach((cell) => row.insertCell().appendChild(cell));

            row.addEventListener('change', (event) => {
                if (event.target === from || event.target === to) {
                    if (!isDate(from.value) || !isDate(to.value) || to.value < from.value) {
                        status.textContent = 'Pick valid dates, with the end on or after the start.';
                        from.value = block.start;
                        to.value = block.end;
                        return;
                    }

                    remember();
                    block.start = from.value;
                    block.end = to.value;
                    commitRoster();
                    return;
                }

                remember();
                block.label = label.value.trim();
                block.japan = japan.value.trim();
                block.india = india.value.trim();
                japanName.textContent = nameOf(block.japan);
                indiaName.textContent = nameOf(block.india);
                row.classList.toggle('mine', shiftsFor(block).length > 0);
                mark();
                rebuildMyDays();
                saveRosterNow();
                renderDays();
            });

            remove.addEventListener('click', async () => {
                if (await askConfirm('Delete week', `Delete "${block.label || 'this block'}" (${shortDay(block.start)} - ${shortDay(block.end)})? You can undo this until you refresh.`, 'Delete')) {
                    remember();
                    roster.blocks.splice(roster.blocks.indexOf(block), 1);
                    commitRoster();
                }
            });
        }

        rosterHost.replaceChildren(table);
    };

    // The owner is picked from the IT ids in the names list and shown as "IT1 - Sandip".
    const ownerSelect = (current: string): HTMLSelectElement => {
        const ids = Object.keys(roster.settings.names).filter((key) => /^it\d+$/i.test(key))
            .sort((first, second) => Number(normalizeId(first).slice(2)) - Number(normalizeId(second).slice(2)));

        if (current && !ids.some((id) => normalizeId(id) === normalizeId(current))) {
            ids.push(current);
        }

        const control = el('select', 'wl-choice');

        for (const id of ['', ...ids]) {
            const name = id ? nameOf(id) : '';
            const option = el('option', undefined, id ? (name ? `${id} - ${name}` : id) : '-');
            option.value = id;
            option.selected = normalizeId(id) === normalizeId(current);
            control.appendChild(option);
        }

        return control;
    };

    const renderIterations = (): void => {
        flags.length = 0;
        const items = iterationsInQuarter();
        oneNoteCount.textContent = items.length === 0 ? '' : `${items.length} iteration${items.length === 1 ? '' : 's'}`;

        if (items.length === 0) {
            oneNoteHost.replaceChildren(emptyNote('No OneNote iterations in this quarter. Add one or generate the rotation with Quick Fill below.'));
            return;
        }

        const { table, body } = buildTable(['Iteration', 'From', 'To', 'Owner', 'Update', 'Page / Link', 'Note', ''], 'onenote');
        const today = todayIso();

        for (const item of items) {
            const row = body.insertRow();
            row.classList.toggle('today', item.start <= today && today <= item.end);
            row.hidden = onlyMineIt.checked && !isMyIteration(item);

            const label = textInput(item.label, 'Iteration');
            const from = dateInput(item.start);
            const to = dateInput(item.end);
            const owner = ownerSelect(item.owner);
            const state = select(ONENOTE, item.status, 'wl-choice');
            const page = textInput(item.page, 'Page title or link');
            const note = textInput(item.note, 'What was updated?');
            const flag = el('span', 'duty-flag');
            const remove = el('button', 'link-button', 'Delete');
            remove.type = 'button';

            // Only the owner records the update; everyone else's rows are for viewing.
            const lockOthers = (): void => {
                const mine = isMyIteration(item);
                [state, page, note].forEach((control) => { control.disabled = !mine; });
            };

            lockOthers();

            const actions = el('div', 'duty-cover');
            actions.append(remove, flag);

            [label, from, to, owner, state, page, note, actions]
                .forEach((cell) => row.insertCell().appendChild(cell));
            flags.push({ item, row, flag });

            row.addEventListener('change', (event) => {
                if (event.target === from || event.target === to) {
                    if (!isDate(from.value) || !isDate(to.value) || to.value < from.value) {
                        status.textContent = 'Pick valid dates, with the end on or after the start.';
                        from.value = item.start;
                        to.value = item.end;
                        return;
                    }

                    remember();
                    item.start = from.value;
                    item.end = to.value;
                    commitRoster();
                    return;
                }

                remember();
                item.label = label.value.trim();
                item.owner = owner.value.trim();
                item.status = state.value;
                item.page = page.value.trim();
                item.note = note.value.trim();
                lockOthers();
                saveRosterNow();
                refreshStatus();
            });

            remove.addEventListener('click', async () => {
                if (await askConfirm('Delete iteration', `Delete "${item.label || 'this iteration'}" (${shortDay(item.start)} - ${shortDay(item.end)})? Its recorded update is deleted too. You can undo this until you refresh.`, 'Delete')) {
                    remember();
                    roster.iterations.splice(roster.iterations.indexOf(item), 1);
                    commitRoster();
                }
            });
        }

        oneNoteHost.replaceChildren(table);
    };

    function renderDays(): void {
        const dates = daysInView();
        doCount.textContent = dates.length === 0 ? '' : `${dates.length} day${dates.length === 1 ? '' : 's'}`;

        if (dates.length === 0) {
            const message = myDays.size === 0
                ? `No DO days found for ${roster.settings.myDoId || 'your DO id'}. Check "My DO ID" and the roster.`
                : `You have no DO days in ${currentQuarter()?.label ?? 'this quarter'}.`;
            doHost.replaceChildren(emptyNote(message));
        } else {
            const doTable = buildTable(['Date', 'DO Shift', 'Shift Time', 'Handover', 'DO Note', ''], 'do');

            for (const date of dates) {
                const entry = entries.get(date);
                const cover = myDays.get(date);

                const shift = select(SHIFT_OPTIONS, entry?.doShift || cover?.label || '', 'wl-choice');
                const time = textInput(timeForShift(shift.value), 'Set by the shift');
                time.readOnly = true;
                const handover = select(HANDOVER, entry?.handover ?? '', 'wl-choice');
                const doNote = el('textarea', 'duty-note');
                doNote.value = entry?.doNote ?? '';
                doNote.rows = 1;
                doNote.maxLength = 2000;
                doNote.placeholder = 'Optional - Enter adds a new line';
                const fit = (): void => {
                    doNote.style.height = 'auto';
                    doNote.style.height = `${doNote.scrollHeight}px`;
                };
                doNote.addEventListener('input', fit);
                window.requestAnimationFrame(fit);
                const doState = el('span', 'worklog-state');

                const doRow = doTable.body.insertRow();
                const doCells: HTMLElement[] = [dateCell(date, doRow), shift, time, handover, doNote, doState];
                doCells.forEach((cell) => doRow.insertCell().appendChild(cell));
                doRow.addEventListener('change', (event) => {
                    const target = event.target;

                    if (target === shift) {
                        time.value = timeForShift(shift.value);
                    }

                    const patch: Partial<DutyEntry> = target === shift ? { doShift: shift.value, shiftTime: timeForShift(shift.value) }
                        : target === handover ? { handover: handover.value }
                            : { doNote: doNote.value };
                    save(patch, date, doState);
                });
            }

            doHost.replaceChildren(doTable.table);
        }

        renderIterations();
        refreshStatus();
    }

    function renderAll(): void {
        const quarter = currentQuarter();
        myIdInput.value = roster.settings.myDoId;
        myItInput.value = roster.settings.myItId;
        namesInput.value = Object.entries(roster.settings.names).map(([id, name]) => `${id} = ${name}`).join('\n');
        range.textContent = quarter ? `(${fullDay(quarter.start)} - ${fullDay(quarter.end)})` : '';
        exportLink.href = quarter ? `/api/dutylog/export?from=${quarter.start}&to=${quarter.end}` : '#';
        exportOneNoteLink.href = quarter ? `/api/dutylog/export-onenote?from=${quarter.start}&to=${quarter.end}` : '#';
        renderRoster();
        renderDays();
    }

    // Entries are stored per day, so load the calendar years the quarter spans.
    const ensureYears = async (): Promise<void> => {
        const quarter = currentQuarter();
        const first = quarter ? Number(quarter.start.slice(0, 4)) : now.getFullYear();
        const last = quarter ? Number(quarter.end.slice(0, 4)) : first;

        for (let year = first; year <= last; year++) {
            if (!loadedYears.has(year)) {
                (await getDutyYear(year)).entries.forEach((entry) => entries.set(entry.date, entry));
                loadedYears.add(year);
            }
        }
    };

    // Keeps the chosen quarter when it still exists; otherwise picks the one around today.
    const refreshQuarters = (keepStart?: string): void => {
        const today = todayIso();
        quarters = buildQuarters([...roster.blocks, ...roster.iterations]);

        const kept = keepStart ? quarters.findIndex((quarter) => quarter.start <= keepStart && keepStart <= quarter.end) : -1;
        const current = quarters.findIndex((quarter) => quarter.start <= today && today <= quarter.end);
        const latestStarted = quarters.map((quarter) => quarter.start <= today).lastIndexOf(true);
        quarterIndex = Math.max(0, kept >= 0 ? kept : current >= 0 ? current : latestStarted);

        quarterSelect.replaceChildren(...(quarters.length === 0
            ? [Object.assign(el('option', undefined, 'No roster'), { value: '0' })]
            : quarters.map((quarter, index) => Object.assign(el('option', undefined, quarter.label), { value: String(index) }))));
        quarterSelect.value = String(quarterIndex);
        quarterSelect.disabled = quarters.length === 0;
    };

    // Called after any roster edit that changes dates or owners.
    function commitRoster(): void {
        roster.blocks.sort((first, second) => first.start.localeCompare(second.start));
        roster.iterations.sort((first, second) => first.start.localeCompare(second.start));
        rebuildMyDays();
        saveRosterNow();
        refreshQuarters(currentQuarter()?.start);
        renderAll();
        void ensureYears().then(renderDays);
    }

    // First load after the OneNote roster was introduced: start from the IT list and the owner's own id.
    const migrateOneNoteSettings = (): boolean => {
        const hasItNames = Object.keys(roster.settings.names).some((key) => /^it\d+$/i.test(key));

        if (hasItNames || roster.settings.myItId) {
            return false;
        }

        roster.settings.myItId = 'IT7';
        DEFAULT_IT_NAMES.forEach(([id, name]) => { roster.settings.names[id] = name; });
        return true;
    };

    const load = async (): Promise<void> => {
        const token = ++generation;
        status.textContent = '';
        showProgress(doHost, 'Loading duty log...');
        oneNoteHost.replaceChildren();

        try {
            const saved = await getDutyRoster();

            if (token !== generation) {
                return;
            }

            roster.settings = { myDoId: saved.settings.myDoId, myItId: saved.settings.myItId ?? '', names: saved.settings.names ?? {} };
            roster.blocks = saved.blocks;
            roster.iterations = saved.iterations ?? [];
            entries.clear();
            loadedYears.clear();

            if (migrateOneNoteSettings()) {
                saveRosterNow();
            }

            rebuildMyDays();
            refreshQuarters();
            await ensureYears();

            if (token === generation) {
                renderAll();
            }
        } catch (failure) {
            showError(doHost, (failure as Error).message);
        }
    };

    quarterSelect.addEventListener('change', () => {
        quarterIndex = Number(quarterSelect.value);
        void ensureYears().then(renderAll).catch((failure: Error) => { status.textContent = failure.message; });
    });

    onlyMine.addEventListener('change', renderRoster);
    onlyMineIt.addEventListener('change', renderIterations);

    reminderJump.addEventListener('click', () => {
        const first = iterationsInQuarter().find(isMissing);
        const row = flags.find((entry) => entry.item === first)?.row;
        onlyMineIt.checked = true;
        oneNotePanel.open = true;
        renderIterations();
        row?.scrollIntoView({ block: 'center' });
    });

    const saveSettings = (): void => {
        const names: Record<string, string> = {};

        for (const line of namesInput.value.split('\n')) {
            const match = /^\s*(.+?)\s*[=:]\s*(.+?)\s*$/.exec(line);

            if (match) {
                names[match[1]] = match[2];
            }
        }

        remember();
        roster.settings = { myDoId: myIdInput.value.trim(), myItId: myItInput.value.trim(), names };
        rebuildMyDays();
        saveRosterNow();
        renderAll();
    };

    myIdInput.addEventListener('change', saveSettings);
    myItInput.addEventListener('change', saveSettings);
    namesInput.addEventListener('change', saveSettings);

    const rangeFrom = (from: string, to: string): string[] | undefined => {
        if (!isDate(from) || !isDate(to) || to < from) {
            status.textContent = 'Pick valid From and To dates, with To on or after From.';
            return undefined;
        }

        const days = eachDay(from, to);

        if (days.length >= MAX_RANGE_DAYS) {
            status.textContent = `A period is limited to ${MAX_RANGE_DAYS} days.`;
            return undefined;
        }

        return days;
    };

    const perDayMap = (): Map<string, Slot> => {
        const perDay = new Map<string, Slot>();

        for (const block of roster.blocks) {
            eachDay(block.start, block.end).forEach((day) => perDay.set(day, { label: block.label, japan: block.japan, india: block.india }));
        }

        return perDay;
    };

    // Fields left empty keep the current value; days outside any block become a new block.
    byId<HTMLButtonElement>('duty-roster-apply').addEventListener('click', async () => {
        const days = rangeFrom(rosterFrom.value, rosterTo.value);

        if (!days) {
            return;
        }

        if (!rosterLabel.value.trim() && !rosterJapan.value.trim() && !rosterIndia.value.trim()) {
            status.textContent = 'Enter a label, a Japan DO or an India DO to apply.';
            return;
        }

        const perDay = perDayMap();
        let changed = 0;

        for (const day of days) {
            const current = perDay.get(day) ?? { label: 'Custom', japan: '', india: '' };
            const next = {
                label: rosterLabel.value.trim() || current.label,
                japan: rosterJapan.value.trim() || current.japan,
                india: rosterIndia.value.trim() || current.india
            };

            if (perDay.has(day) && (next.label !== current.label || next.japan !== current.japan || next.india !== current.india)) {
                changed++;
            }

            perDay.set(day, next);
        }

        if (changed > 0 && !(await askConfirm('Change existing roster', `This changes ${changed} day${changed === 1 ? '' : 's'} that already ${changed === 1 ? 'has' : 'have'} a roster. You can undo it until you refresh.`, 'Apply'))) {
            return;
        }

        remember();
        roster.blocks = regroup(perDay);
        commitRoster();
        status.textContent = `Roster updated for ${shortDay(days[0])} - ${shortDay(days[days.length - 1])}.`;
    });

    byId<HTMLButtonElement>('duty-roster-clear').addEventListener('click', async () => {
        const days = rangeFrom(rosterFrom.value, rosterTo.value);

        if (!days) {
            return;
        }

        const perDay = perDayMap();
        const existing = days.filter((day) => perDay.has(day)).length;

        if (existing === 0) {
            status.textContent = 'There is no roster in that period.';
            return;
        }

        if (!(await askConfirm('Clear roster', `Clear the roster for ${existing} day${existing === 1 ? '' : 's'} (${shortDay(days[0])} - ${shortDay(days[days.length - 1])})? You can undo it until you refresh.`, 'Clear'))) {
            return;
        }

        remember();
        days.forEach((day) => perDay.delete(day));
        roster.blocks = regroup(perDay);
        commitRoster();
        status.textContent = `Roster cleared for ${shortDay(days[0])} - ${shortDay(days[days.length - 1])}.`;
    });

    // An iteration with the same start date is updated in place, keeping its recorded update.
    const upsertIteration = (start: string, end: string, label: string, owner: string): void => {
        const existing = roster.iterations.find((item) => item.start === start);

        if (existing) {
            existing.end = end;
            existing.label = label || existing.label;
            existing.owner = owner || existing.owner;
        } else {
            roster.iterations.push({ start, end, label, owner, status: '', page: '', note: '' });
        }
    };

    // The rotation cycles through the IT ids in the names list, starting from the chosen owner.
    byId<HTMLButtonElement>('duty-rot-apply').addEventListener('click', async () => {
        const length = Number(rotationDays.value);
        const count = Number(rotationCount.value);

        if (!isDate(rotationStart.value) || !Number.isInteger(length) || length < 1 || length > MAX_ROTATION
            || !Number.isInteger(count) || count < 1 || count > MAX_ROTATION) {
            status.textContent = `Enter a start date, an iteration length of 1-${MAX_ROTATION} days and 1-${MAX_ROTATION} iterations.`;
            return;
        }

        const ids = Object.keys(roster.settings.names).filter((key) => /^it\d+$/i.test(key))
            .sort((first, second) => Number(normalizeId(first).slice(2)) - Number(normalizeId(second).slice(2)));
        const owners = ids.length > 0 ? ids : DEFAULT_IT_NAMES.map(([id]) => id);
        const first = Math.max(0, owners.findIndex((id) => normalizeId(id) === normalizeId(rotationFirst.value)));

        const starts = Array.from({ length: count }, (_, index) => addDays(rotationStart.value, index * length));
        const replaced = starts.filter((start) => roster.iterations.some((item) => item.start === start)).length;

        if (replaced > 0 && !(await askConfirm('Replace iterations', `${replaced} existing iteration${replaced === 1 ? '' : 's'} will get new dates and owners (recorded updates are kept). You can undo it until you refresh.`, 'Generate'))) {
            return;
        }

        remember();

        for (let index = 0; index < count; index++) {
            const start = addDays(rotationStart.value, index * length);
            upsertIteration(start, addDays(start, length - 1), `Iteration ${index + 1}`, owners[(first + index) % owners.length]);
        }

        commitRoster();
        status.textContent = `Generated ${count} iteration${count === 1 ? '' : 's'} from ${fullDay(rotationStart.value)}. Edit any row if the real schedule differs.`;
    });

    // A re-import replaces weeks that start on the same day and keeps the rest.
    const mergeImported = async (result: GridResult): Promise<boolean> => {
        const diagnostics = `Found ${result.headers} week header${result.headers === 1 ? '' : 's'}, `
            + `${result.blocks.length} with a readable date range, ${result.shiftRows} Japan/India shift cell${result.shiftRows === 1 ? '' : 's'}.`;

        if (result.blocks.length === 0) {
            status.textContent = `No roster weeks could be read. ${diagnostics} `
                + 'Each week needs a "Week n" header with a range like "12th - 16th Oct" under it. You can also add weeks with Quick Fill.';
            return false;
        }

        const imported = new Set(result.blocks.map((block) => block.start));
        const replaced = roster.blocks.filter((block) => imported.has(block.start)).length;

        if (replaced > 0 && !(await askConfirm('Replace imported weeks', `${replaced} existing week${replaced === 1 ? '' : 's'} will be replaced by the imported data. You can undo it until you refresh.`, 'Replace'))) {
            return false;
        }

        remember();
        roster.blocks = [...roster.blocks.filter((block) => !imported.has(block.start)), ...result.blocks];
        const first = result.blocks[0].start;
        commitRoster();

        // Show the quarter that was just imported.
        const index = quarters.findIndex((quarter) => quarter.start <= first && first <= quarter.end);

        if (index >= 0) {
            quarterIndex = index;
            quarterSelect.value = String(index);
            void ensureYears().then(renderAll);
        }

        status.textContent = `Imported ${result.blocks.length} roster block${result.blocks.length === 1 ? '' : 's'} `
            + `(${fullDay(first)} - ${fullDay(result.blocks[result.blocks.length - 1].end)}). ${diagnostics}`;
        return true;
    };

    importButton.addEventListener('click', () => importFile.click());

    importFile.addEventListener('change', async () => {
        const file = importFile.files?.[0];
        importFile.value = '';

        if (!file) {
            return;
        }

        importButton.disabled = true;
        status.textContent = `Reading ${file.name}...`;

        try {
            await mergeImported(parseWithInferredYear(await readWorkbookGrids(file)));
        } catch (failure) {
            status.textContent = `Import failed: ${(failure as Error).message}. Try "Paste from Excel" in Quick Fill instead.`;
        } finally {
            importButton.disabled = false;
        }
    });

    byId<HTMLButtonElement>('duty-paste-apply').addEventListener('click', async () => {
        if (!pasteBox.value.trim()) {
            status.textContent = 'Paste the copied cells from Excel first.';
            return;
        }

        if (await mergeImported(parseWithInferredYear([parsePasted(pasteBox.value)]))) {
            pasteBox.value = '';
        }
    });

    document.querySelector<HTMLButtonElement>('.nav-button[data-target="view-duty"]')
        ?.addEventListener('click', () => void load());

    undoButton.addEventListener('click', undo);

    // Red for failures, green for completed changes, so a failed save stands out.
    new MutationObserver(() => {
        const text = status.textContent ?? '';
        status.classList.toggle('is-error', /not saved|failed|stopped|could not/i.test(text));
        status.classList.toggle('is-ok', !/not saved|failed|stopped|could not/i.test(text) && /saved|imported|updated|cleared|generated|undid/i.test(text));
    }).observe(status, { childList: true, characterData: true, subtree: true });

    // Both panels start collapsed and only one stays open at a time.
    const syncOverview = (): void => {
        overview.hidden = doPanel.open || oneNotePanel.open;
    };

    syncOverview();

    for (const [panel, other] of [[doPanel, oneNotePanel], [oneNotePanel, doPanel]] as const) {
        panel.addEventListener('toggle', () => {
            if (panel.open) {
                other.open = false;
            }

            syncOverview();
        });
    }

    // Leaving with a save still running, or one that failed, would lose the change.
    window.addEventListener('beforeunload', (event) => {
        if (pending > 0 || failures.size > 0) {
            event.preventDefault();
            event.returnValue = '';
        }
    });

    enhanceSelects(byId('view-duty'));

    if (location.hash === '#duty') {
        void load();
    }
}
