import { getDailyPlan, getLinks, getSubjects, searchLearning } from './api.js';
import { byId, el } from './dom.js';
import { openLearningLocation } from './learningView.js';
import { showView } from './navigation.js';
import { openInWorkTab } from './workWindow.js';

interface Result {
    group: string;
    title: string;
    detail: string;
    open: () => void;
}

interface Source {
    title: string;
    detail: string;
    open: () => void;
}

const GROUPS = ['Pages', 'Daily plan', 'Links', 'Learning subjects', 'Learning notes'] as const;
const MIN_LENGTH = 2;
const MAX_PER_GROUP = 6;
const REFRESH_MS = 60_000;
const NOTE_DELAY_MS = 250;

const today = (): string => {
    const now = new Date();
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
};

const matches = (text: string, terms: string[]): boolean => {
    const lower = text.toLowerCase();
    return terms.every((term) => lower.includes(term));
};

const hostOf = (url: string): string => {
    try {
        return new URL(url).hostname;
    } catch {
        return url;
    }
};

export function initSearchBox(): void {
    const root = byId('global-search');
    const input = byId<HTMLInputElement>('global-search-input');
    const panel = byId('global-search-panel');

    let links: Source[] = [];
    let plan: Source[] = [];
    let subjects: Source[] = [];
    let loadedAt = 0;
    let results: Result[] = [];
    let active = -1;
    let noteTimer = 0;
    let noteRequest = 0;

    const pages = (): Source[] =>
        [...document.querySelectorAll<HTMLButtonElement>('.nav-button')].map((button) => {
            const group = button.closest<HTMLElement>('.nav-group')?.querySelector('.nav-group-title')?.textContent?.trim() ?? '';
            const sub = button.querySelector('.nav-sub')?.textContent?.trim() ?? '';

            return {
                title: button.querySelector('.nav-title')?.textContent?.trim() ?? button.title,
                detail: [group, sub].filter(Boolean).join(' · '),
                open: () => button.click()
            };
        });

    const loadData = async (): Promise<void> => {
        if (Date.now() - loadedAt < REFRESH_MS) {
            return;
        }

        loadedAt = Date.now();

        const [linkList, planView, subjectList] = await Promise.allSettled([getLinks(), getDailyPlan(today()), getSubjects()]);

        if (linkList.status === 'fulfilled') {
            links = linkList.value.map((link) => ({
                title: link.title,
                detail: [link.category, hostOf(link.url), link.description].filter(Boolean).join(' · '),
                open: () => openInWorkTab(link.url)
            }));
        }

        if (planView.status === 'fulfilled') {
            plan = planView.value.iteration.targets.map((target) => ({
                title: target.title,
                detail: [target.status, target.priority].filter(Boolean).join(' · '),
                open: () => showView('view-dailyplan')
            }));
        }

        if (subjectList.status === 'fulfilled') {
            subjects = subjectList.value.map((subject) => ({
                title: subject.title,
                detail: subject.description ?? `${subject.fileCount} notes`,
                open: () => void openLearningLocation(subject.id)
            }));
        }

        if (!panel.hidden) {
            refresh();
        }
    };

    const close = (): void => {
        panel.hidden = true;
        input.setAttribute('aria-expanded', 'false');
        active = -1;
    };

    const choose = (index: number): void => {
        const result = results[index];

        if (!result) {
            return;
        }

        close();
        input.value = '';
        input.blur();
        result.open();
    };

    const highlight = (index: number): void => {
        active = index;
        const items = panel.querySelectorAll<HTMLElement>('.gsearch-item');
        items.forEach((item, position) => item.classList.toggle('active', position === index));
        items[index]?.scrollIntoView({ block: 'nearest' });
    };

    const render = (): void => {
        panel.replaceChildren();
        const query = input.value.trim();

        if (query.length < MIN_LENGTH) {
            close();
            return;
        }

        const ordered = GROUPS.flatMap((group) => results.filter((result) => result.group === group));
        results = ordered;

        if (ordered.length === 0) {
            panel.appendChild(el('p', 'gsearch-empty', `Nothing found for "${query}".`));
        }

        for (const group of GROUPS) {
            const members = ordered.filter((result) => result.group === group);

            if (members.length === 0) {
                continue;
            }

            panel.appendChild(el('div', 'gsearch-group', group));

            for (const result of members) {
                const index = ordered.indexOf(result);
                const item = el('button', 'gsearch-item');
                item.type = 'button';
                item.dataset.kind = group;
                item.setAttribute('role', 'option');
                item.append(el('span', 'gsearch-title', result.title), el('span', 'gsearch-detail', result.detail));
                item.addEventListener('mousemove', () => active !== index && highlight(index));
                item.addEventListener('click', () => choose(index));
                panel.appendChild(item);
            }
        }

        panel.hidden = false;
        input.setAttribute('aria-expanded', 'true');
        highlight(ordered.length > 0 ? Math.min(Math.max(active, 0), ordered.length - 1) : -1);
    };

    const local = (terms: string[]): Result[] => {
        const pick = (group: string, source: Source[]): Result[] =>
            source
                .filter((item) => matches(`${item.title} ${item.detail}`, terms))
                .slice(0, MAX_PER_GROUP)
                .map((item) => ({ group, ...item }));

        return [...pick('Pages', pages()), ...pick('Daily plan', plan), ...pick('Links', links), ...pick('Learning subjects', subjects)];
    };

    const refresh = (): void => {
        const terms = input.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
        window.clearTimeout(noteTimer);
        noteRequest++;

        if (input.value.trim().length < MIN_LENGTH) {
            results = [];
            render();
            return;
        }

        results = local(terms);
        render();

        const request = noteRequest;
        const query = input.value.trim();

        noteTimer = window.setTimeout(async () => {
            try {
                const hits = await searchLearning(query);

                if (request !== noteRequest) {
                    return;
                }

                const notes: Result[] = hits.slice(0, MAX_PER_GROUP).map((hit) => ({
                    group: 'Learning notes',
                    title: hit.line.trim() || hit.path,
                    detail: `${hit.subjectTitle} / ${hit.path}:${hit.lineNumber}`,
                    open: () => void openLearningLocation(hit.subjectId, hit.path)
                }));

                results = [...results.filter((result) => result.group !== 'Learning notes'), ...notes];
                render();
            } catch {
                // The other groups are still useful when the notes search fails.
            }
        }, NOTE_DELAY_MS);
    };

    input.addEventListener('focus', () => {
        void loadData();

        if (input.value.trim().length >= MIN_LENGTH) {
            refresh();
        }
    });

    input.addEventListener('input', () => {
        active = 0;
        refresh();
    });

    input.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            if (panel.hidden) {
                input.blur();
            } else {
                close();
            }

            event.preventDefault();
        } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            if (results.length > 0) {
                const step = event.key === 'ArrowDown' ? 1 : -1;
                highlight((active + step + results.length) % results.length);
            }

            event.preventDefault();
        } else if (event.key === 'Enter') {
            choose(active);
            event.preventDefault();
        }
    });

    document.addEventListener('pointerdown', (event) => {
        if (!root.contains(event.target as Node)) {
            close();
        }
    });

    document.addEventListener('keydown', (event) => {
        const typing = (event.target as HTMLElement | null)?.closest('input, textarea, select, [contenteditable="true"]');
        const shortcut = (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k';

        if (shortcut || (event.key === '/' && !typing && !event.ctrlKey && !event.metaKey && !event.altKey)) {
            event.preventDefault();
            input.focus();
            input.select();
        }
    });
}
