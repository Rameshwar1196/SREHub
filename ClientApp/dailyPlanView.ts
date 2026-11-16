import { carryOverDailyPlan, getDailyPlan, pushDailyPlanToWorkLog, saveDailyPlan, setDailyPlanIterationStart } from './api.js';
import { DailyPlanDay, DailyPlanView, IterationTarget, SubTask } from './dailyPlanTypes.js';
import { byId, el, showError, showProgress } from './dom.js';
import { enhanceSelects } from './dropdown.js';

const PRIORITIES = ['P1', 'P2', 'P3'];
const STATUSES = ['Todo', 'InProgress', 'Done', 'Blocked', 'Carried'];
const STATUS_LABEL: Record<string, string> = { InProgress: 'In progress' };
const PLANNED_DAYS = [0.5, 1, 2, 3, 4, 5];
const SAVE_DELAY_MS = 600;
const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

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

const isDate = (value: string): boolean => /^\d{4}-\d{2}-\d{2}$/.test(value) && isoDate(parseDay(value)) === value;
const daysBetween = (from: string, to: string): number => Math.round((parseDay(to).getTime() - parseDay(from).getTime()) / 86_400_000);
const shortDay = (value: string): string => `${pad(parseDay(value).getDate())} ${MONTHS[parseDay(value).getMonth()]}`;
const emptyDay = (date: string): DailyPlanDay => ({ date, focus: '', endOfDay: '', blockers: '', tomorrow: '' });
const formatDays = (value: number): string => `${value} ${value === 1 ? 'day' : 'days'}`;

function newId(): string {
    return Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16)).join('');
}

// Same rules as the server: Done target means every sub task is done, and finishing the last sub task finishes the target.
function percentOf(target: IterationTarget): number {
    if (target.status === 'Done') {
        return 100;
    }

    return target.subTasks.length === 0 ? 0 : Math.round(100 * target.subTasks.filter((subTask) => subTask.done).length / target.subTasks.length);
}

function normalize(target: IterationTarget): void {
    const { subTasks } = target;

    if (target.status === 'Done') {
        subTasks.forEach((subTask) => { subTask.done = true; });
    } else if (target.status !== 'Carried' && subTasks.length > 0 && subTasks.every((subTask) => subTask.done)) {
        target.status = 'Done';
    } else if (target.status === 'Todo' && subTasks.some((subTask) => subTask.done)) {
        target.status = 'InProgress';
    }
}

const counted = (targets: IterationTarget[]): IterationTarget[] => targets.filter((target) => target.status !== 'Carried');
const weightOf = (target: IterationTarget): number => target.plannedDays ?? 1;

function progressOf(targets: IterationTarget[]): number {
    const active = counted(targets);
    const weight = active.reduce((sum, target) => sum + weightOf(target), 0);
    return weight === 0 ? 0 : Math.round(active.reduce((sum, target) => sum + percentOf(target) * weightOf(target), 0) / weight);
}

function choice(values: string[], value: string, className: string): HTMLSelectElement {
    const control = el('select', className);

    for (const option of values) {
        const element = el('option', undefined, STATUS_LABEL[option] ?? option);
        element.value = option;
        element.selected = option === value;
        control.appendChild(element);
    }

    return control;
}

function daysSelect(value: number | undefined, className: string): HTMLSelectElement {
    const control = el('select', className);

    for (const option of ['', ...PLANNED_DAYS.map(String)]) {
        const element = el('option', undefined, option ? formatDays(Number(option)) : 'Days');
        element.value = option;
        element.selected = option === (value === undefined ? '' : String(value));
        control.appendChild(element);
    }

    control.setAttribute('aria-label', 'Planned days');
    return control;
}

function textBox(value: string, placeholder: string, maxLength: number, label: string, type = 'text'): HTMLInputElement {
    const control = el('input');
    control.type = type;
    control.value = value;
    control.maxLength = maxLength;
    control.placeholder = placeholder;
    control.setAttribute('aria-label', label);
    return control;
}

interface TargetContext {
    date: () => string;
    onChange: () => void;
    onStatusChanged: (target: IterationTarget, previous: string) => void;
    onRemove: () => void;
}

function buildTarget(target: IterationTarget, context: TargetContext): HTMLElement {
    const card = el('div', 'dp-target');

    const priority = choice(PRIORITIES, target.priority, 'dp-priority');
    priority.dataset.priority = target.priority;
    priority.setAttribute('aria-label', 'Priority');

    const title = textBox(target.title, 'Target', 200, 'Target');
    const status = choice(STATUSES, target.status, 'dp-status');
    status.setAttribute('aria-label', 'Status');
    const days = daysSelect(target.plannedDays, 'dp-days');
    const percent = el('span', 'dp-percent');
    const remove = el('button', 'link-button danger', 'Remove');
    remove.type = 'button';
    remove.addEventListener('click', context.onRemove);

    const titleCell = el('div', 'dp-title-cell');
    titleCell.appendChild(title);

    if (target.carriedFrom) {
        titleCell.appendChild(el('span', 'dp-carried', `Carried from iteration of ${shortDay(target.carriedFrom)}`));
    }

    const head = el('div', 'dp-target-head');
    head.append(priority, titleCell, status, days, percent, remove);

    const fill = el('i');
    const bar = el('div', 'dp-bar');
    bar.appendChild(fill);

    const link = textBox(target.url, 'https:// ticket, PR or page', 500, 'Link', 'url');
    const outcome = textBox(target.outcome, 'Result or evidence', 500, 'Outcome');
    const meta = el('div', 'dp-target-meta');
    meta.append(link, outcome);

    const subTaskList = el('div', 'dp-subtasks');
    const newSubTask = textBox('', 'Add a sub task (for example Build) and press Enter', 200, 'New sub task');

    const body = el('div', 'dp-target-body');
    body.append(meta, subTaskList, newSubTask);
    card.append(head, bar, body);

    const renderSubTasks = (): void => {
        subTaskList.replaceChildren(...target.subTasks.map((subTask, index) => {
            const row = el('div', 'dp-subtask');
            const box = el('input');
            box.type = 'checkbox';
            box.checked = subTask.done;
            box.setAttribute('aria-label', `Done: ${subTask.title}`);
            box.addEventListener('change', () => change(() => {
                subTask.done = box.checked;

                if (!box.checked && target.status === 'Done') {
                    target.status = 'InProgress';
                }
            }));

            const name = textBox(subTask.title, 'Sub task', 200, 'Sub task');
            name.addEventListener('input', () => {
                subTask.title = name.value;
                context.onChange();
            });

            const drop = el('button', 'link-button danger', 'x');
            drop.type = 'button';
            drop.title = 'Remove sub task';
            drop.addEventListener('click', () => change(() => { target.subTasks.splice(index, 1); }));

            row.append(el('span', 'dp-subtask-number', `${index + 1}.`), box, name, drop);
            return row;
        }));
    };

    const refresh = (): void => {
        card.dataset.status = target.status;
        status.value = target.status;
        percent.textContent = `${percentOf(target)}%`;
        fill.style.width = `${percentOf(target)}%`;
        renderSubTasks();
    };

    function change(mutate: () => void): void {
        const previous = target.status;
        mutate();
        normalize(target);

        if (target.status !== previous) {
            target.completedOn = target.status === 'Done' ? context.date() : '';
            context.onStatusChanged(target, previous);
        }

        refresh();
        context.onChange();
    }

    priority.addEventListener('change', () => {
        target.priority = priority.value;
        priority.dataset.priority = priority.value;
        context.onChange();
    });

    status.addEventListener('change', () => change(() => {
        if (target.status === 'Done' && status.value !== 'Done') {
            target.subTasks.forEach((subTask) => { subTask.done = false; });
        }

        target.status = status.value;
    }));

    days.addEventListener('change', () => {
        target.plannedDays = days.value ? Number(days.value) : undefined;
        context.onChange();
    });

    title.addEventListener('input', () => { target.title = title.value; context.onChange(); });
    link.addEventListener('input', () => { target.url = link.value; context.onChange(); });
    outcome.addEventListener('input', () => { target.outcome = outcome.value; context.onChange(); });

    newSubTask.addEventListener('keydown', (event) => {
        const text = newSubTask.value.trim();

        if (event.key !== 'Enter' || !text) {
            return;
        }

        event.preventDefault();
        newSubTask.value = '';

        change(() => {
            const added: SubTask = { id: newId(), title: text, done: false };
            target.subTasks.push(added);

            if (target.status === 'Done') {
                target.status = 'InProgress';
            }
        });
    });

    refresh();
    return card;
}

export function initDailyPlanView(): void {
    const todayIso = (): string => isoDate(new Date());
    let lastToday = todayIso();
    const today = lastToday;
    const dateInput = byId<HTMLInputElement>('dp-date');
    const heading = byId('dp-heading');
    const exportLink = byId<HTMLAnchorElement>('dp-export');
    const iterationLabel = byId('dp-iter-label');
    const iterationHint = byId('dp-iter-hint');
    const iterationStart = byId<HTMLInputElement>('dp-iter-start');
    const progress = byId('dp-progress');
    const carryBar = byId('dp-carry');
    const carryText = byId('dp-carry-text');
    const carryButton = byId<HTMLButtonElement>('dp-carry-button');
    const addForm = byId<HTMLFormElement>('dp-add-form');
    const newTitle = byId<HTMLInputElement>('dp-new-title');
    const newPriority = byId<HTMLSelectElement>('dp-new-priority');
    const newDays = byId<HTMLSelectElement>('dp-new-days');
    const targetsHost = byId('dp-targets-host');
    const focus = byId<HTMLInputElement>('dp-focus');
    const endOfDay = byId<HTMLTextAreaElement>('dp-endofday');
    const blockers = byId<HTMLTextAreaElement>('dp-blockers');
    const tomorrow = byId<HTMLTextAreaElement>('dp-tomorrow');
    const pushButton = byId<HTMLButtonElement>('dp-push');
    const pushStatus = byId('dp-push-status');
    const saveState = byId('dp-save-state');
    const sideRange = byId('dp-side-range');
    const sidePercent = byId('dp-side-percent');
    const sideStats = byId('dp-side-stats');
    const sideList = byId('dp-side-list');

    let date = today;
    let day: DailyPlanDay = emptyDay(today);
    let targets: IterationTarget[] = [];
    let iteration = { start: today, end: today };
    let dirty = false;
    let saveTimer: number | undefined;
    let loadToken = 0;

    newPriority.replaceChildren(...PRIORITIES.map((value) => Object.assign(el('option', undefined, value), { value, selected: value === 'P2' })));
    newDays.replaceChildren(...['', ...PLANNED_DAYS.map(String)].map((value) =>
        Object.assign(el('option', undefined, value ? formatDays(Number(value)) : 'Days'), { value })));

    const setSaveState = (text: string, kind: '' | 'saved' | 'failed', detail = ''): void => {
        saveState.className = `worklog-state ${kind}`.trim();
        saveState.textContent = text;
        saveState.title = detail;
    };

    const renderSummary = (): void => {
        const active = counted(targets);
        const percent = progressOf(targets);
        const done = active.filter((target) => target.status === 'Done').length;
        const blocked = active.filter((target) => target.status === 'Blocked').length;
        const plannedDays = active.reduce((sum, target) => sum + (target.plannedDays ?? 0), 0);
        const doneDays = active.filter((target) => target.status === 'Done').reduce((sum, target) => sum + (target.plannedDays ?? 0), 0);

        if (active.length === 0) {
            progress.replaceChildren(el('span', 'hint', 'No targets for this iteration yet. Add the targets you want to finish in these two weeks.'));
        } else {
            const bar = el('div', 'dp-bar');
            const fill = el('i');
            fill.style.width = `${percent}%`;
            bar.appendChild(fill);
            progress.replaceChildren(
                el('strong', undefined, `${percent}% of the iteration`),
                bar,
                el('span', 'hint', `${done} of ${active.length} targets done${blocked > 0 ? `, ${blocked} blocked` : ''}`));
        }

        sidePercent.textContent = `${percent}%`;
        sideRange.textContent = `${shortDay(iteration.start)} - ${shortDay(iteration.end)}`;
        sideStats.textContent = plannedDays > 0
            ? `${done} of ${active.length} targets done. ${doneDays} of ${plannedDays} planned days finished.`
            : `${done} of ${active.length} targets done.`;

        sideList.replaceChildren(...active.map((target) => {
            const row = el('div', 'dp-side-row');
            row.dataset.status = target.status;
            row.append(el('span', 'dp-side-title', target.title || 'Untitled'), el('span', 'dp-side-percent', `${percentOf(target)}%`));
            return row;
        }));
    };

    const flush = async (): Promise<boolean> => {
        window.clearTimeout(saveTimer);

        if (!dirty) {
            return true;
        }

        dirty = false;
        setSaveState('Saving...', '');

        try {
            await saveDailyPlan(date, structuredClone({ day, targets }));
            setSaveState('Saved', 'saved');
            return true;
        } catch (failure) {
            dirty = true;
            setSaveState(`Failed: ${(failure as Error).message}`, 'failed', (failure as Error).message);
            return false;
        }
    };

    const markDirty = (): void => {
        dirty = true;
        setSaveState('Unsaved', '');
        renderSummary();
        window.clearTimeout(saveTimer);
        saveTimer = window.setTimeout(() => void flush(), SAVE_DELAY_MS);
    };

    // A target that becomes Done is listed under "What I achieved" and one that becomes Blocked under "Blockers and risks"; leaving the status takes the line back out.
    const syncLine = (box: HTMLTextAreaElement, field: 'endOfDay' | 'blockers', title: string, wanted: boolean): void => {
        const line = `- ${title.trim()}`;
        const lines = day[field].split('\n');
        const present = lines.some((existing) => existing.trim() === line);

        if (wanted && !present) {
            day[field] = [...lines.filter((existing) => existing.trim() !== ''), line].join('\n');
        } else if (!wanted && present) {
            day[field] = lines.filter((existing) => existing.trim() !== line).join('\n');
        }

        box.value = day[field];
    };

    const syncStatusLines = (target: IterationTarget, previous: string): void => {
        if ((target.status === 'Done') !== (previous === 'Done')) {
            syncLine(endOfDay, 'endOfDay', target.title, target.status === 'Done');
        }

        if ((target.status === 'Blocked') !== (previous === 'Blocked')) {
            syncLine(blockers, 'blockers', target.title, target.status === 'Blocked');
        }
    };

    const renderTargets = (): void => {
        if (targets.length === 0) {
            targetsHost.replaceChildren(el('p', 'hint', 'Nothing planned for this iteration.'));
            return;
        }

        const context = (target: IterationTarget): TargetContext => ({
            date: () => date,
            onChange: markDirty,
            onStatusChanged: syncStatusLines,
            onRemove: () => {
                targets = targets.filter((existing) => existing !== target);
                renderTargets();
                markDirty();
            }
        });

        targetsHost.replaceChildren(...targets.map((target) => buildTarget(target, context(target))));
    };

    const renderIteration = (): void => {
        const length = daysBetween(iteration.start, iteration.end) + 1;
        const weekendDays = Array.from({ length }, (_, offset) => parseDay(addDays(iteration.start, offset)).getDay()).filter((weekday) => weekday === 0 || weekday === 6).length;
        const weekday = (value: string): string => WEEKDAYS[parseDay(value).getDay()];

        iterationLabel.textContent = `${weekday(iteration.start)} ${shortDay(iteration.start)} - ${weekday(iteration.end)} ${shortDay(iteration.end)} ${parseDay(iteration.end).getFullYear()}`;
        iterationHint.textContent = `Day ${daysBetween(iteration.start, date) + 1} of ${length}: ${length - weekendDays} working days + ${weekendDays} weekend days`;
        iterationStart.value = iteration.start;
    };

    const render = (view: DailyPlanView): void => {
        day = view.day;
        targets = view.iteration.targets;
        iteration = { start: view.iteration.start, end: view.iteration.end };
        dirty = false;
        pushStatus.textContent = '';
        setSaveState('', '');

        dateInput.value = date;
        heading.textContent = date === todayIso() ? `Today - ${shortDay(date)}` : `${WEEKDAYS[parseDay(date).getDay()]} ${shortDay(date)} ${parseDay(date).getFullYear()}`;
        renderIteration();
        exportLink.href = `/api/dailyplan/export?date=${date}`;
        focus.value = day.focus;
        endOfDay.value = day.endOfDay;
        blockers.value = day.blockers;
        tomorrow.value = day.tomorrow;

        carryBar.hidden = view.carryOverCount === 0;
        carryText.textContent = `${view.carryOverCount} open target${view.carryOverCount === 1 ? '' : 's'} from the previous iteration.`;

        renderTargets();
        renderSummary();
    };

    const load = async (): Promise<void> => {
        const token = ++loadToken;
        showProgress(targetsHost, 'Loading daily plan...');

        try {
            const view = await getDailyPlan(date);

            if (token === loadToken) {
                render(view);
            }
        } catch (failure) {
            showError(targetsHost, (failure as Error).message);
        }
    };

    async function goTo(target: string): Promise<void> {
        if (!isDate(target) || target === date) {
            dateInput.value = date;
            return;
        }

        if (!(await flush())) {
            dateInput.value = date;
            return;
        }

        date = target;
        await load();
    }

    byId<HTMLButtonElement>('dp-prev').addEventListener('click', () => void goTo(addDays(date, -1)));
    byId<HTMLButtonElement>('dp-next').addEventListener('click', () => void goTo(addDays(date, 1)));
    byId<HTMLButtonElement>('dp-today').addEventListener('click', () => void goTo(todayIso()));
    dateInput.addEventListener('change', () => void goTo(dateInput.value));

    byId<HTMLButtonElement>('dp-iter-prev').addEventListener('click', () => void goTo(addDays(iteration.start, -1)));
    byId<HTMLButtonElement>('dp-iter-next').addEventListener('click', () => void goTo(addDays(iteration.end, 1)));

    iterationStart.addEventListener('change', async () => {
        const requested = iterationStart.value;

        if (!isDate(requested) || requested === iteration.start) {
            iterationStart.value = iteration.start;
            return;
        }

        try {
            if (await flush()) {
                render(await setDailyPlanIterationStart(date, requested));
            }
        } catch (failure) {
            iterationHint.textContent = (failure as Error).message;
            iterationStart.value = iteration.start;
        }
    });

    focus.addEventListener('input', () => { day.focus = focus.value; markDirty(); });
    endOfDay.addEventListener('input', () => { day.endOfDay = endOfDay.value; markDirty(); });
    blockers.addEventListener('input', () => { day.blockers = blockers.value; markDirty(); });
    tomorrow.addEventListener('input', () => { day.tomorrow = tomorrow.value; markDirty(); });

    addForm.addEventListener('submit', (event) => {
        event.preventDefault();
        const title = newTitle.value.trim();

        if (!title) {
            newTitle.focus();
            return;
        }

        targets.push({
            id: newId(),
            title,
            priority: newPriority.value,
            status: 'Todo',
            plannedDays: newDays.value ? Number(newDays.value) : undefined,
            url: '',
            outcome: '',
            completedOn: '',
            carriedFrom: '',
            subTasks: []
        });

        newTitle.value = '';
        newTitle.focus();
        renderTargets();
        markDirty();
    });

    carryButton.addEventListener('click', async () => {
        carryButton.disabled = true;

        try {
            if (await flush()) {
                render(await carryOverDailyPlan(date));
            }
        } catch (failure) {
            carryText.textContent = (failure as Error).message;
        } finally {
            carryButton.disabled = false;
        }
    });

    pushButton.addEventListener('click', async () => {
        pushButton.disabled = true;
        pushStatus.textContent = 'Updating work log...';

        try {
            if (await flush()) {
                const entry = await pushDailyPlanToWorkLog(date);
                pushStatus.textContent = `Micro-mgmt summary for ${shortDay(date)} set to: ${entry.summary}`;
            } else {
                pushStatus.textContent = 'Fix the save error first.';
            }
        } catch (failure) {
            pushStatus.textContent = (failure as Error).message;
        } finally {
            pushButton.disabled = false;
        }
    });

    window.addEventListener('beforeunload', (event) => {
        if (dirty) {
            event.preventDefault();
        }
    });

    enhanceSelects(byId('view-dailyplan'));

    // A tab left open past midnight moves on to the new day, unless another date was picked on purpose.
    const rollOverToToday = (): boolean => {
        const now = todayIso();

        if (now === lastToday) {
            return false;
        }

        const followed = date === lastToday;
        lastToday = now;

        if (followed) {
            date = now;
        }

        return followed;
    };

    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible' && byId('view-dailyplan').classList.contains('active') && todayIso() !== lastToday) {
            void flush().then((saved) => {
                if (saved && rollOverToToday()) {
                    return load();
                }
            });
        }
    });

    document.querySelector<HTMLButtonElement>('.nav-button[data-target="view-dailyplan"]')
        ?.addEventListener('click', () => {
            void flush().then(() => {
                rollOverToToday();
                return load();
            });
        });

    if (location.hash === '#dailyplan') {
        void load();
    }
}
