import { carryOverDailyPlan, getDailyPlan, getDailyPlanSyncStatus, pushDailyPlanToWorkLog, saveDailyPlan, setDailyPlanIterationStart, syncDailyPlan } from './api.js';
import { DailyPlanDay, DailyPlanView, IterationTarget, SubTask, WorkItemSyncResult } from './dailyPlanTypes.js';
import { byId, el, showError, showProgress } from './dom.js';
import { askConfirm } from './dialogs.js';
import { enhanceSelects } from './dropdown.js';

const PRIORITIES = ['P1', 'P2', 'P3'];
const STATUSES = ['Todo', 'InProgress', 'Done', 'Blocked', 'Carried', 'Cancelled', 'ACF'];
const STATUS_LABEL: Record<string, string> = { InProgress: 'In progress' };
const PLANNED_DAYS = [0.5, 1, 2, 3, 4, 5];
const SAVE_DELAY_MS = 600;
const UPDATE_COLLAPSED_KEY = 'srehub.dailyplan.updateCollapsed';
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

type TargetSource = 'ado' | 'sre' | 'manual';

const sourceOf = (target: IterationTarget): TargetSource =>
    target.externalId?.startsWith('sre:') ? 'sre' : target.externalId?.startsWith('ado:') ? 'ado' : 'manual';

function sourceLabel(externalId: string): string {
    if (externalId.startsWith('sre:')) {
        return `SRE Portal ${externalId.slice(4)}`;
    }

    return externalId ? `Azure DevOps ${externalId.replace('ado:', '#')}` : 'Manual';
}

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
    } else if (target.status !== 'Carried' && target.status !== 'Cancelled' && target.status !== 'ACF' && subTasks.length > 0 && subTasks.every((subTask) => subTask.done)) {
        target.status = 'Done';
    } else if (target.status === 'Todo' && subTasks.some((subTask) => subTask.done)) {
        target.status = 'InProgress';
    }
}

// A target counts once it has days planned; Carried, Cancelled and ACF items never count.
const isTarget = (target: IterationTarget): boolean =>
    (target.plannedDays ?? 0) > 0 && target.status !== 'Carried' && target.status !== 'Cancelled' && target.status !== 'ACF';
const counted = (targets: IterationTarget[]): IterationTarget[] => targets.filter(isTarget);
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
    onRegroup: () => void;
    onRemove: () => void;
}

type TargetGroup = 'planned' | 'unplanned' | 'acf' | 'carried' | 'cancelled' | 'done';

const GROUPS: Array<{ key: Exclude<TargetGroup, 'done'>; label: string }> = [
    { key: 'planned', label: 'Targets' },
    { key: 'unplanned', label: 'Not planned - choose the days to make it a target' },
    { key: 'acf', label: 'ACF - waiting, not a target' },
    { key: 'carried', label: 'Carried to a later iteration' },
    { key: 'cancelled', label: 'Cancelled' }
];

function groupOf(target: IterationTarget): TargetGroup {
    switch (target.status) {
        case 'Done': return 'done';
        case 'Cancelled': return 'cancelled';
        case 'ACF': return 'acf';
        case 'Carried': return 'carried';
        default: return (target.plannedDays ?? 0) > 0 ? 'planned' : 'unplanned';
    }
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

    const tags = el('div', 'dp-target-tags');
    const tag = (text: string, className = '', href = ''): void => {
        const chip = href ? Object.assign(el('a', `dp-tag ${className}`.trim(), text), { href, target: '_blank', rel: 'noopener noreferrer' }) : el('span', `dp-tag ${className}`.trim(), text);
        tags.appendChild(chip);
    };

    if (target.externalId) {
        tag(sourceLabel(target.externalId), `source ${sourceOf(target)}`, /^https?:\/\//i.test(target.url) ? target.url : '');

        if (target.externalState) {
            tag(target.externalState);
        }

        if (target.externalIteration) {
            tag(target.externalIteration.split('\\').slice(1).join(' / ') || target.externalIteration);
        }
    } else {
        tag('Manual', 'source manual');
    }

    card.dataset.source = sourceOf(target);

    if (target.carriedFrom) {
        tag(`Carried from iteration of ${shortDay(target.carriedFrom)}`, 'carried');
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
    const newSubTask = textBox('', 'Add a sub task if you have for this workitem and press Enter', 200, 'New sub task');

    const body = el('div', 'dp-target-body');
    body.append(meta, subTaskList, newSubTask);
    card.append(head);

    if (tags.childElementCount > 0) {
        card.appendChild(tags);
    }

    card.append(bar, body);

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
        card.dataset.planned = String(isTarget(target));
        status.value = target.status;
        days.value = target.plannedDays === undefined ? '' : String(target.plannedDays);
        days.disabled = target.status === 'ACF';
        days.title = target.status === 'ACF' ? 'ACF items are not targets, so they have no days' : '';
        percent.textContent = `${percentOf(target)}%`;
        fill.style.width = `${percentOf(target)}%`;
        renderSubTasks();
    };

    function change(mutate: () => void): void {
        const previous = target.status;
        const group = groupOf(target);
        mutate();
        normalize(target);

        if (target.status !== previous) {
            target.completedOn = target.status === 'Done' ? context.date() : '';
            context.onStatusChanged(target, previous);
        }

        refresh();
        context.onChange();

        if (groupOf(target) !== group) {
            context.onRegroup();
        }
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

        if (status.value === 'ACF') {
            target.plannedDays = undefined;
        }
    }));

    days.addEventListener('change', () => change(() => {
        target.plannedDays = days.value ? Number(days.value) : undefined;
    }));

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
    const updateSummary = byId('dp-update-summary');
    const endOfDay = byId<HTMLTextAreaElement>('dp-endofday');
    const blockers = byId<HTMLTextAreaElement>('dp-blockers');
    const tomorrow = byId<HTMLTextAreaElement>('dp-tomorrow');
    const pushButton = byId<HTMLButtonElement>('dp-push');
    const syncButton = byId<HTMLButtonElement>('dp-sync');
    const syncStatus = byId('dp-sync-status');
    const syncDot = byId('dp-sync-dot');
    const achievedHost = byId('dp-achieved-host');
    const achievedCount = byId('dp-achieved-count');
    const updateToggle = byId<HTMLButtonElement>('dp-update-toggle');
    const updateBody = byId('dp-update-body');
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
    let carryCount = 0;
    let removedExternalIds: string[] = [];
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
            progress.replaceChildren(el('span', 'hint', targets.some((target) => groupOf(target) !== 'done' && groupOf(target) !== 'cancelled')
                ? 'No planned targets yet. Choose the days on a target to count it toward this iteration.'
                : 'No targets for this iteration yet. Add the targets you want to finish in these two weeks.'));
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
            const removed = [...removedExternalIds];
            await saveDailyPlan(date, structuredClone({ day, targets, removedExternalIds: removed }));
            removedExternalIds = removedExternalIds.filter((id) => !removed.includes(id));
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

    // A target that becomes Blocked is listed under "Blockers and risks"; leaving the status takes the line back out.
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
        if ((target.status === 'Blocked') !== (previous === 'Blocked')) {
            syncLine(blockers, 'blockers', target.title, target.status === 'Blocked');
            renderUpdateSummary();
        }
    };

    const renderAchieved = (): void => {
        const finished = targets
            .filter((target) => target.status === 'Done')
            .sort((first, second) => (first.completedOn || '9999-99-99').localeCompare(second.completedOn || '9999-99-99'));

        achievedCount.textContent = finished.length === 0 ? '' : `${finished.length} completed`;

        if (finished.length === 0) {
            achievedHost.replaceChildren(el('p', 'hint', 'Nothing completed yet in this iteration.'));
            return;
        }

        achievedHost.replaceChildren(...finished.map((target, index) => {
            const item = el('div', 'dp-achieved-item');
            item.dataset.source = sourceOf(target);
            const title = target.url && /^https?:\/\//i.test(target.url)
                ? Object.assign(el('a', 'dp-achieved-title', target.title), { href: target.url, target: '_blank', rel: 'noopener noreferrer' })
                : el('span', 'dp-achieved-title', target.title);

            const chips = el('div', 'dp-achieved-chips');
            const chip = (text: string, className = ''): void => { chips.appendChild(el('span', `dp-achieved-chip ${className}`.trim(), text)); };
            const doneSubTasks = target.subTasks.filter((subTask) => subTask.done).length;

            chip(target.completedOn && isDate(target.completedOn) ? `Completed ${WEEKDAYS[parseDay(target.completedOn).getDay()]} ${shortDay(target.completedOn)} ${parseDay(target.completedOn).getFullYear()}` : 'Completed', 'done');
            chip(target.priority);

            if (target.plannedDays !== undefined) {
                chip(formatDays(target.plannedDays));
            }

            if (target.subTasks.length > 0) {
                chip(`${doneSubTasks}/${target.subTasks.length} sub tasks`);
            }

            if (target.carriedFrom) {
                chip(`Carried from ${shortDay(target.carriedFrom)}`);
            }

            chip(sourceLabel(target.externalId ?? ''), `source ${sourceOf(target)}`);

            const outcome = textBox(target.outcome, 'Result or evidence', 500, 'Outcome');
            outcome.addEventListener('input', () => { target.outcome = outcome.value; markDirty(); });

            const reopen = el('button', 'link-button', 'Reopen');
            reopen.type = 'button';
            reopen.title = 'Move this target back to the iteration targets';
            reopen.addEventListener('click', () => {
                target.status = 'InProgress';
                target.completedOn = '';
                target.subTasks.forEach((subTask) => { subTask.done = false; });
                renderTargets();
                renderAchieved();
                markDirty();
            });

            const main = el('div', 'dp-achieved-main');
            main.append(title, chips, outcome);
            item.append(el('span', 'dp-achieved-number', `${index + 1}.`), main, reopen);
            return item;
        }));
    };

    const renderTargets = (): void => {
        const groups = GROUPS
            .map((group) => ({ ...group, items: targets.filter((target) => groupOf(target) === group.key) }))
            .filter((group) => group.items.length > 0);

        if (groups.length === 0) {
            targetsHost.replaceChildren(el('p', 'hint', targets.length === 0 ? 'Nothing planned for this iteration.' : 'Every target in this iteration is done.'));
            return;
        }

        const context = (target: IterationTarget): TargetContext => ({
            date: () => date,
            onChange: markDirty,
            onStatusChanged: syncStatusLines,
            onRegroup: () => {
                renderTargets();
                renderAchieved();
            },
            onRemove: () => {
                if (target.externalId) {
                    removedExternalIds.push(target.externalId);
                }

                targets = targets.filter((existing) => existing !== target);
                renderTargets();
                markDirty();
            }
        });

        const showLabels = groups.length > 1 || groups[0].key !== 'planned';

        targetsHost.replaceChildren(...groups.flatMap((group) => [
            ...(showLabels ? [el('div', `dp-group-label ${group.key}`, `${group.label} (${group.items.length})`)] : []),
            ...group.items.map((target) => buildTarget(target, context(target)))
        ]));
    };

    const setUpdateCollapsed = (collapsed: boolean): void => {
        updateToggle.setAttribute('aria-expanded', String(!collapsed));
        updateToggle.title = collapsed ? 'Expand the iteration update' : 'Collapse the iteration update';
        updateBody.hidden = collapsed;
    };

    const renderUpdateSummary = (): void => {
        const filled = [day.endOfDay, day.blockers, day.tomorrow].filter((text) => text.trim() !== '').length;
        updateSummary.textContent = filled === 0 ? 'Nothing added yet' : `${filled} of 3 notes added`;
        updateSummary.dataset.filled = filled === 0 ? 'false' : 'true';
    };

    // Collapsed until the user opens it once; the choice is then remembered.
    try {
        setUpdateCollapsed(localStorage.getItem(UPDATE_COLLAPSED_KEY) !== '0');
    } catch {
        setUpdateCollapsed(true);
    }

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
        endOfDay.value = day.endOfDay;
        blockers.value = day.blockers;
        tomorrow.value = day.tomorrow;
        renderUpdateSummary();

        carryBar.hidden = view.carryOverCount === 0;
        carryCount = view.carryOverCount;
        carryText.textContent = `${view.carryOverCount} In progress, Blocked or ACF target${view.carryOverCount === 1 ? '' : 's'} from the previous iteration can be carried over.`;

        renderTargets();
        renderAchieved();
        renderSummary();
    };

    const describeSync = (result: WorkItemSyncResult): string => {
        if (result.error) {
            return result.error;
        }

        if (!result.ranAt) {
            return result.configured ? 'Work item sync has not run yet.' : '';
        }

        const parts = [
            result.added > 0 ? `${result.added} new` : '',
            result.updated > 0 ? `${result.updated} updated` : '',
            result.removed > 0 ? `${result.removed} removed` : '',
            result.skipped > 0 ? `${result.skipped} skipped (iteration is full)` : ''
        ].filter(Boolean);

        return `Synced ${result.ranAt.slice(11)}${parts.length > 0 ? `: ${parts.join(', ')}` : ': up to date'}`;
    };

    const showSyncStatus = (): void => {
        getDailyPlanSyncStatus()
            .then((result) => { syncStatus.textContent = describeSync(result); syncStatus.title = result.error; })
            .catch(() => { syncStatus.textContent = ''; });
    };

    const load = async (): Promise<void> => {
        const token = ++loadToken;
        showProgress(targetsHost, 'Loading daily plan...');

        try {
            const view = await getDailyPlan(date);

            if (token === loadToken) {
                render(view);
                showSyncStatus();
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

    endOfDay.addEventListener('input', () => { day.endOfDay = endOfDay.value; renderUpdateSummary(); markDirty(); });
    blockers.addEventListener('input', () => { day.blockers = blockers.value; renderUpdateSummary(); markDirty(); });
    tomorrow.addEventListener('input', () => { day.tomorrow = tomorrow.value; renderUpdateSummary(); markDirty(); });

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
        const confirmed = await askConfirm(
            'Carry over to this iteration',
            `Carry ${carryCount} In progress, Blocked or ACF target${carryCount === 1 ? '' : 's'} from the previous iteration into this one? `
            + 'Completed, not started and cancelled work stays where it is.',
            'Carry over');

        if (!confirmed) {
            return;
        }

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

    const syncMenu = byId('dp-sync-menu');
    const sourceNames = { ado: 'Azure DevOps', sre: 'Support portal' } as const;

    const closeSyncMenu = (): void => {
        syncMenu.hidden = true;
        syncButton.setAttribute('aria-expanded', 'false');
    };

    syncButton.addEventListener('click', (event) => {
        event.stopPropagation();
        syncMenu.hidden = !syncMenu.hidden;
        syncButton.setAttribute('aria-expanded', String(!syncMenu.hidden));
    });

    document.addEventListener('click', closeSyncMenu);
    syncMenu.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            closeSyncMenu();
            syncButton.focus();
        }
    });

    syncMenu.addEventListener('click', async (event) => {
        const choice = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-source]');

        if (!choice) {
            return;
        }

        const source = choice.dataset.source as keyof typeof sourceNames;
        closeSyncMenu();
        syncButton.disabled = true;
        syncStatus.textContent = `Syncing ${sourceNames[source]}...`;
        syncStatus.title = '';
        syncDot.dataset.source = source;
        syncDot.dataset.state = 'busy';
        syncDot.hidden = false;

        try {
            if (await flush()) {
                const outcome = await syncDailyPlan(date, source);
                render(outcome.view);
                syncStatus.textContent = describeSync(outcome.sync);
                syncStatus.title = outcome.sync.error;
                syncDot.dataset.state = outcome.sync.error ? 'error' : 'ok';
            } else {
                syncStatus.textContent = 'Fix the save error first.';
                syncDot.dataset.state = 'error';
            }
        } catch (failure) {
            syncStatus.textContent = (failure as Error).message;
            syncDot.dataset.state = 'error';
        } finally {
            syncButton.disabled = false;
        }
    });

    updateToggle.addEventListener('click', () => {
        const collapsed = updateToggle.getAttribute('aria-expanded') === 'true';
        setUpdateCollapsed(collapsed);

        try {
            localStorage.setItem(UPDATE_COLLAPSED_KEY, collapsed ? '1' : '0');
        } catch {
            // The choice just is not remembered when storage is blocked.
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
