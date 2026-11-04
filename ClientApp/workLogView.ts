import { getWorkLogYear, saveWorkLogEntry } from './api.js';
import { byId, el, showError, showProgress } from './dom.js';
import { enhanceSelects } from './dropdown.js';
import { WorkLogEntry, WorkLogSummaryRow, WorkLogYear } from './workLogTypes.js';

const ATTENDANCE = ['Yes', 'Late', 'No', 'Leave', 'Comp-off', 'Holiday', 'Weekend', 'DO-wkd', 'DO-wk'];
const EMERGENCY_LEAVE = ['No', 'Yes', 'Weekend', 'N/A'];
const NETWORK_POWER = ['No', 'Network issue', 'Power outage', 'Both', 'N/A'];
const WORKING = ['Yes', 'Late', 'DO-wkd', 'DO-wk'];

const DEFAULT_SUMMARY: Record<string, string> = {
    Yes: 'All good.',
    Late: 'All good.',
    'DO-wkd': 'All good.',
    'DO-wk': 'All good.',
    Weekend: 'Weekend off',
    Holiday: 'Holiday',
    Leave: 'On planned leave',
    'Comp-off': 'On comp-off leave'
};

const CATEGORY_COLOUR: Record<string, string> = {
    Working: 'var(--wl-working)',
    Leave: 'var(--wl-leave)',
    'Comp-off': 'var(--wl-compoff)',
    Holiday: 'var(--wl-holiday)',
    Weekend: 'var(--wl-weekend)'
};

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

const MIN_YEAR = 2000;
const MAX_YEAR = 2100;
const SVG_NS = 'http://www.w3.org/2000/svg';

const pad = (value: number): string => String(value).padStart(2, '0');

interface RowHandle {
    weekday: number;
    markWeekend: () => Promise<void>;
}

export function select(options: string[], value: string, className: string): HTMLSelectElement {
    const control = el('select', className);
    const values = ['', ...options];

    if (value && !options.includes(value)) {
        values.push(value);
    }

    for (const option of values) {
        const element = el('option', undefined, option || '-');
        element.value = option;
        element.selected = option === value;
        control.appendChild(element);
    }

    control.dataset.value = value;
    return control;
}

export function textInput(value: string, placeholder: string, listId?: string): HTMLInputElement {
    const control = el('input');
    control.type = 'text';
    control.value = value;
    control.maxLength = 500;
    control.placeholder = placeholder;

    if (listId) {
        control.dataset.list = listId;
    }

    return control;
}

function buildRow(date: string, entry: WorkLogEntry | undefined, onSaved: () => void): { element: HTMLTableRowElement; handle: RowHandle } {
    const [year, month, day] = date.split('-').map(Number);
    const weekday = new Date(year, month - 1, day).getDay();
    const row = el('tr');
    const today = new Date();

    if (weekday === 6) {
        row.classList.add('sat');
    } else if (weekday === 0) {
        row.classList.add('sun');
    }

    if (today.getFullYear() === year && today.getMonth() === month - 1 && today.getDate() === day) {
        row.classList.add('today');
    }

    const attendance = select(ATTENDANCE, entry?.attendance ?? '', 'wl-att');
    const emergency = select(EMERGENCY_LEAVE, entry?.emergencyLeave ?? '', 'wl-choice');
    const network = select(NETWORK_POWER, entry?.networkPower ?? '', 'wl-choice');
    const summary = textInput(entry?.summary ?? '', 'What did you work on?');
    const escalation = textInput(entry?.escalation ?? '', 'No', 'worklog-escalation-options');
    const note = textInput(entry?.note ?? '', 'Optional');
    const state = el('span', 'worklog-state');

    const dateCell = el('div', 'wl-date');
    dateCell.append(
        el('span', 'wl-day', `${pad(day)} ${MONTHS[month - 1].slice(0, 3)}`),
        el('span', `wl-wd wd-${weekday}`, WEEKDAYS[weekday]));

    for (const control of [dateCell, attendance, emergency, network, summary, escalation, note, state]) {
        row.insertCell().appendChild(control);
    }

    const save = async (): Promise<void> => {
        state.className = 'worklog-state';
        state.textContent = 'Saving...';
        state.title = '';

        try {
            await saveWorkLogEntry({
                date,
                attendance: attendance.value,
                emergencyLeave: emergency.value,
                networkPower: network.value,
                summary: summary.value,
                escalation: escalation.value,
                note: note.value
            });

            state.classList.add('saved');
            state.textContent = 'Saved';
            onSaved();
        } catch (failure) {
            state.classList.add('failed');
            state.textContent = 'Failed';
            state.title = (failure as Error).message;
        }
    };

    // Fill only blanks so a typed value is never overwritten.
    const applyDefaults = (): void => {
        const chosen = attendance.value;
        attendance.dataset.value = chosen;

        if (!(chosen in DEFAULT_SUMMARY)) {
            return;
        }

        const working = WORKING.includes(chosen);

        if (!emergency.value) {
            emergency.value = working ? 'No' : 'N/A';
        }

        if (!network.value) {
            network.value = working ? 'No' : 'N/A';
        }

        if (!summary.value) {
            summary.value = DEFAULT_SUMMARY[chosen];
        }
    };

    row.addEventListener('change', (event) => {
        if (event.target === attendance) {
            applyDefaults();
        }

        void save();
    });

    const markWeekend = async (): Promise<void> => {
        if ((weekday !== 0 && weekday !== 6) || attendance.value) {
            return;
        }

        attendance.value = 'Weekend';
        applyDefaults();
        await save();
    };

    return { element: row, handle: { weekday, markWeekend } };
}

export function svg<K extends keyof SVGElementTagNameMap>(tag: K, attributes: Record<string, string>): SVGElementTagNameMap[K] {
    const element = document.createElementNS(SVG_NS, tag);

    for (const [name, value] of Object.entries(attributes)) {
        element.setAttribute(name, value);
    }

    return element;
}

function pieChart(rows: WorkLogSummaryRow[]): SVGSVGElement {
    const chart = svg('svg', { viewBox: '0 0 200 200', role: 'img', 'aria-label': 'Monthly attendance distribution' });
    const total = rows.reduce((sum, row) => sum + row.days, 0);

    if (total === 0) {
        chart.append(
            svg('circle', { cx: '100', cy: '100', r: '88', class: 'wl-pie-empty' }),
            Object.assign(svg('text', { x: '100', y: '100', class: 'wl-pie-note' }), { textContent: 'No data yet' }));
        return chart;
    }

    const slices = rows.filter((row) => row.days > 0);
    let angle = -Math.PI / 2;

    for (const row of slices) {
        const sweep = (row.days / total) * Math.PI * 2;
        const end = angle + sweep;
        const colour = CATEGORY_COLOUR[row.category] ?? 'var(--text-faint)';
        const title = Object.assign(svg('title', {}), { textContent: `${row.category}: ${row.days} (${Math.round(row.percentage * 100)}%)` });

        const shape = slices.length === 1
            ? svg('circle', { cx: '100', cy: '100', r: '90', class: 'wl-slice' })
            : svg('path', {
                class: 'wl-slice',
                d: `M100 100 L${100 + 90 * Math.cos(angle)} ${100 + 90 * Math.sin(angle)} `
                    + `A90 90 0 ${sweep > Math.PI ? 1 : 0} 1 ${100 + 90 * Math.cos(end)} ${100 + 90 * Math.sin(end)} Z`
            });

        shape.style.fill = colour;
        shape.appendChild(title);
        chart.appendChild(shape);

        if (row.percentage >= 0.06) {
            const middle = angle + sweep / 2;
            const label = svg('text', {
                x: String(100 + 58 * Math.cos(middle)),
                y: String(100 + 58 * Math.sin(middle)),
                class: 'wl-pie-label'
            });
            label.textContent = `${Math.round(row.percentage * 100)}%`;
            chart.appendChild(label);
        }

        angle = end;
    }

    return chart;
}

function renderSummary(body: HTMLElement, rows: WorkLogSummaryRow[]): void {
    body.replaceChildren(...rows.map((row) => {
        const tr = el('tr');
        const name = el('span', 'wl-category');
        const dot = el('i', 'dot');
        dot.style.background = CATEGORY_COLOUR[row.category] ?? 'var(--text-faint)';
        name.append(dot, document.createTextNode(row.category));

        tr.insertCell().appendChild(name);
        tr.insertCell().textContent = String(row.days);
        tr.insertCell().textContent = `${Math.round(row.percentage * 100)}%`;
        return tr;
    }));
}

export function initWorkLogView(): void {
    const now = new Date();
    const state = { year: now.getFullYear(), month: now.getMonth() + 1 };

    const yearInput = byId<HTMLInputElement>('worklog-year');
    const range = byId('worklog-range');
    const tableHost = byId('worklog-table-host');
    const monthSelect = byId<HTMLSelectElement>('worklog-month-select');
    const monthTabs = byId('worklog-month-tabs');
    const summaryBody = byId('worklog-summary-body');
    const pieHost = byId('worklog-pie');
    const markButton = byId<HTMLButtonElement>('worklog-mark-weekends');
    const exportLink = byId<HTMLAnchorElement>('worklog-export-year');

    let rows: RowHandle[] = [];
    let monthBodies: HTMLTableSectionElement[] = [];
    let yearData: WorkLogYear | undefined;
    let generation = 0;
    let sideGeneration = 0;
    let refreshTimer: number | undefined;

    const tabs: HTMLButtonElement[] = [];

    for (const [index, name] of MONTHS.entries()) {
        const option = el('option', undefined, name);
        option.value = String(index + 1);
        monthSelect.appendChild(option);

        const tab = el('button', 'wl-month-tab', name.slice(0, 3));
        tab.type = 'button';
        tab.style.setProperty('--h', String(index * 30));
        tab.title = name;
        tab.addEventListener('click', () => selectMonth(index + 1));
        tabs.push(tab);
    }

    monthTabs.replaceChildren(...tabs);

    const renderSide = (): void => {
        const rowsForMonth = yearData?.months[state.month - 1]?.summary ?? [];
        monthSelect.value = String(state.month);
        renderSummary(summaryBody, rowsForMonth);
        pieHost.replaceChildren(pieChart(rowsForMonth));
    };

    // Only the chosen month is visible; the other months stay in the page but hidden.
    function selectMonth(month: number): void {
        state.month = month;
        monthBodies.forEach((body, index) => { body.hidden = index + 1 !== month; });

        tabs.forEach((tab, index) => {
            tab.classList.toggle('active', index + 1 === month);
            tab.setAttribute('aria-pressed', String(index + 1 === month));
        });

        renderSide();
    }

    const refreshSide = async (): Promise<void> => {
        const token = ++sideGeneration;

        try {
            const data = await getWorkLogYear(state.year);

            if (token === sideGeneration) {
                yearData = data;
                renderSide();
            }
        } catch {
            // Row state already reports save failures; stale totals are acceptable.
        }
    };

    const scheduleRefresh = (): void => {
        window.clearTimeout(refreshTimer);
        refreshTimer = window.setTimeout(() => void refreshSide(), 300);
    };

    const renderTable = (data: WorkLogYear): void => {
        const byDate = new Map(data.entries.map((entry) => [entry.date, entry]));
        const table = el('table', 'worklog-table');
        const head = table.createTHead().insertRow();

        for (const title of ['Date', 'Attendance Status', 'Emergency Leave', 'Network / Power Status', 'Daily Work Summary', 'Escalation Status / Details', 'Note', '']) {
            head.appendChild(el('th', undefined, title));
        }

        rows = [];
        monthBodies = [];

        for (let month = 1; month <= 12; month++) {
            const days = new Date(state.year, month, 0).getDate();
            const short = MONTHS[month - 1].slice(0, 3).toUpperCase();

            const body = table.createTBody();
            monthBodies.push(body);

            const banner = body.insertRow();
            banner.className = 'wl-month-row';
            banner.style.setProperty('--h', String((month - 1) * 30));
            const cell = banner.insertCell();
            cell.colSpan = 8;
            cell.textContent = `${MONTHS[month - 1].toUpperCase()} ${state.year} - 1 ${short} TO ${days} ${short}`;

            for (let day = 1; day <= days; day++) {
                const date = `${state.year}-${pad(month)}-${pad(day)}`;
                const built = buildRow(date, byDate.get(date), scheduleRefresh);
                rows.push(built.handle);
                body.appendChild(built.element);
            }
        }

        tableHost.replaceChildren(table);
    };

    const load = async (): Promise<void> => {
        const token = ++generation;
        yearInput.value = String(state.year);
        range.textContent = `(1 Jan - 31 Dec ${state.year})`;
        exportLink.href = `/api/worklog/export?period=${state.year}`;
        showProgress(tableHost, 'Loading work log...');

        try {
            const data = await getWorkLogYear(state.year);

            if (token !== generation) {
                return;
            }

            yearData = data;
            renderTable(data);
            selectMonth(state.month);
        } catch (failure) {
            showError(tableHost, (failure as Error).message);
        }
    };

    const setYear = (year: number): void => {
        if (!Number.isInteger(year) || year < MIN_YEAR || year > MAX_YEAR) {
            yearInput.value = String(state.year);
            return;
        }

        state.year = year;
        state.month = year === now.getFullYear() ? now.getMonth() + 1 : 1;
        void load();
    };

    byId<HTMLButtonElement>('worklog-prev-year').addEventListener('click', () => setYear(state.year - 1));
    byId<HTMLButtonElement>('worklog-next-year').addEventListener('click', () => setYear(state.year + 1));
    yearInput.addEventListener('change', () => setYear(Number(yearInput.value)));

    monthSelect.addEventListener('change', () => selectMonth(Number(monthSelect.value)));

    markButton.addEventListener('click', async () => {
        markButton.disabled = true;

        try {
            await Promise.all(rows.map((row) => row.markWeekend()));
        } finally {
            markButton.disabled = false;
        }
    });

    document.querySelector<HTMLButtonElement>('.nav-button[data-target="view-worklog"]')
        ?.addEventListener('click', () => void load());

    enhanceSelects(byId('view-worklog'));

    const bulkForm = byId<HTMLFormElement>('worklog-bulk-form');
    const bulkFrom = byId<HTMLInputElement>('bulk-from');
    const bulkTo = byId<HTMLInputElement>('bulk-to');
    const bulkAttendance = byId<HTMLSelectElement>('bulk-attendance');
    const bulkEmergency = byId<HTMLSelectElement>('bulk-emergency');
    const bulkNetwork = byId<HTMLSelectElement>('bulk-network');
    const bulkSummary = byId<HTMLInputElement>('bulk-summary');
    const bulkEscalation = byId<HTMLInputElement>('bulk-escalation');
    const bulkNote = byId<HTMLInputElement>('bulk-note');
    const bulkSkipWeekends = byId<HTMLInputElement>('bulk-skip-weekends');
    const bulkOverwrite = byId<HTMLInputElement>('bulk-overwrite');
    const bulkApply = byId<HTMLButtonElement>('bulk-apply');
    const bulkStatus = byId('bulk-status');

    for (const [target, options] of [[bulkAttendance, ATTENDANCE], [bulkEmergency, EMERGENCY_LEAVE], [bulkNetwork, NETWORK_POWER]] as const) {
        target.replaceChildren(...['', ...options].map((option) => Object.assign(el('option', undefined, option || '-'), { value: option })));
    }

    bulkForm.addEventListener('submit', async (event) => {
        event.preventDefault();

        const [fromYear, fromMonth, fromDay] = bulkFrom.value.split('-').map(Number);
        const [toYear, toMonth, toDay] = bulkTo.value.split('-').map(Number);
        const from = new Date(fromYear, fromMonth - 1, fromDay);
        const to = new Date(toYear, toMonth - 1, toDay);

        if (from > to) {
            bulkStatus.textContent = 'From must not be after To.';
            return;
        }

        if (from.getFullYear() !== state.year || to.getFullYear() !== state.year) {
            bulkStatus.textContent = `Pick dates inside ${state.year}, or change the year first.`;
            return;
        }

        const existing = new Map((yearData?.entries ?? []).map((entry) => [entry.date, entry]));
        const overwrite = bulkOverwrite.checked;
        const pick = (current: string | undefined, incoming: string): string => (current && !overwrite ? current : incoming || current || '');

        const days: string[] = [];
        for (const day = new Date(from); day <= to; day.setDate(day.getDate() + 1)) {
            if (bulkSkipWeekends.checked && (day.getDay() === 0 || day.getDay() === 6)) {
                continue;
            }

            days.push(`${day.getFullYear()}-${pad(day.getMonth() + 1)}-${pad(day.getDate())}`);
        }

        bulkApply.disabled = true;
        let saved = 0;

        try {
            for (const date of days) {
                const current = existing.get(date);
                const attendance = pick(current?.attendance, bulkAttendance.value);
                const defaultStatus = WORKING.includes(attendance) ? 'No' : 'N/A';

                await saveWorkLogEntry({
                    date,
                    attendance,
                    emergencyLeave: pick(current?.emergencyLeave, bulkEmergency.value) || defaultStatus,
                    networkPower: pick(current?.networkPower, bulkNetwork.value) || defaultStatus,
                    summary: pick(current?.summary, bulkSummary.value) || DEFAULT_SUMMARY[attendance] || '',
                    escalation: pick(current?.escalation, bulkEscalation.value),
                    note: pick(current?.note, bulkNote.value)
                });

                saved++;
                bulkStatus.textContent = `Saving ${saved} of ${days.length}...`;
            }

            bulkStatus.textContent = `Updated ${saved} day${saved === 1 ? '' : 's'}.`;
            state.month = from.getMonth() + 1;
            await load();
        } catch (failure) {
            bulkStatus.textContent = `Stopped after ${saved} of ${days.length}: ${(failure as Error).message}`;
        } finally {
            bulkApply.disabled = false;
        }
    });

    if (location.hash === '#worklog') {
        void load();
    }
}
