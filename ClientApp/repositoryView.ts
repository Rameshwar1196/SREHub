import {
    cloneRepository,
    fetchRepository,
    getRepositories,
    getRepositoryBranches,
    openRepository,
    pullRepository,
    removeRepository,
    switchRepositoryBranch
} from './api.js';
import { byId, el, showError, showProgress } from './dom.js';
import { OpenTarget, RepositoryInfo } from './repositoryTypes.js';

function pill(text: string, kind: string): HTMLElement {
    return el('span', `badge ${kind}`, text);
}

function describeAge(value: string): string {
    const minutes = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 60000));

    if (minutes < 1) {
        return 'just now';
    }

    if (minutes < 60) {
        return `${minutes} min ago`;
    }

    const hours = Math.round(minutes / 60);
    return hours < 48 ? `${hours} h ago` : `${Math.round(hours / 24)} days ago`;
}

function badgesFor(info: RepositoryInfo): HTMLElement[] {
    if (info.error) {
        return [pill('Error', 'fail')];
    }

    const badges = [
        pill(info.detached ? 'Detached HEAD' : info.branch ?? 'No branch', 'muted'),
        info.changes > 0 ? pill(`${info.changes} uncommitted`, 'warn') : pill('Clean', 'ok')
    ];

    if (!info.hasUpstream && !info.detached) {
        badges.push(pill('No upstream', 'muted'));
    }

    if (info.ahead > 0) {
        badges.push(pill(`${info.ahead} ahead`, 'warn'));
    }

    if (info.behind > 0) {
        badges.push(pill(`${info.behind} behind`, 'warn'));
    }

    return badges;
}

function buildCard(info: RepositoryInfo, onChange: (updated: RepositoryInfo | null) => void): HTMLElement {
    const card = el('article', 'card repo-card');
    const status = el('div', 'repo-status');
    const branchRow = el('div', 'repo-branch-row hidden');
    const actions = el('div', 'repo-actions');

    const head = el('div', 'repo-head');
    head.append(el('h3', undefined, info.name), el('div', 'repo-badges'));
    head.lastElementChild!.append(...badgesFor(info));

    const meta = el('dl', 'repo-meta');
    const addMeta = (label: string, value: string): void => {
        meta.append(el('dt', undefined, label), el('dd', undefined, value));
    };

    if (info.remoteUrl) {
        addMeta('Remote', info.remoteUrl);
    }

    addMeta('Folder', info.path);

    if (info.lastFetchedAt) {
        addMeta('Last fetch', describeAge(info.lastFetchedAt));
    }

    const busy = async (message: string, work: () => Promise<RepositoryInfo | void>): Promise<void> => {
        const buttons = card.querySelectorAll<HTMLButtonElement>('button');
        buttons.forEach((button) => { button.disabled = true; });
        showProgress(status, message);

        try {
            const updated = await work();
            if (updated) {
                onChange(updated);
                return;
            }

            status.replaceChildren();
        } catch (failure) {
            showError(status, (failure as Error).message);
        }

        buttons.forEach((button) => { button.disabled = false; });
    };

    const button = (label: string, handler: () => void, danger = false): HTMLButtonElement => {
        const element = el('button', danger ? 'link-button danger' : 'link-button', label);
        element.type = 'button';
        element.addEventListener('click', handler);
        return element;
    };

    const open = (label: string, target: OpenTarget): HTMLButtonElement =>
        button(label, () => void busy(`Opening ${info.name}...`, async () => {
            await openRepository(info.name, target);
        }));

    const toggleBranches = async (): Promise<void> => {
        if (!branchRow.classList.contains('hidden')) {
            branchRow.classList.add('hidden');
            return;
        }

        await busy('Loading branches...', async () => {
            const { current, branches } = await getRepositoryBranches(info.name);
            const select = el('select');

            for (const branch of branches) {
                const option = el('option', undefined, branch);
                option.value = branch;
                option.selected = branch === current;
                select.appendChild(option);
            }

            const apply = button('Switch', () => void busy(`Switching to ${select.value}...`, () =>
                switchRepositoryBranch(info.name, select.value)));

            branchRow.replaceChildren(select, apply);
            branchRow.classList.remove('hidden');
        });
    };

    actions.append(
        open('VS Code', 'vscode'),
        open('Folder', 'explorer'),
        open('Terminal', 'terminal'),
        button('Fetch', () => void busy(`Fetching ${info.name}...`, () => fetchRepository(info.name))),
        button('Pull', () => void busy(`Pulling ${info.name}...`, () => pullRepository(info.name))),
        button('Branch', () => void toggleBranches()),
        button('Remove', () => void remove(), true)
    );

    const remove = async (): Promise<void> => {
        const risky = info.changes > 0 || info.ahead > 0;
        const warning = risky
            ? `'${info.name}' has uncommitted changes or unpushed commits that will be lost.\n\n`
            : '';

        if (!window.confirm(`${warning}Delete the local folder '${info.path}'? This cannot be undone.`)) {
            return;
        }

        await busy(`Removing ${info.name}...`, async () => {
            await removeRepository(info.name, risky);
            onChange(null);
        });
    };

    card.append(head, meta);

    if (info.error) {
        card.append(el('p', 'error', info.error));
    }

    card.append(actions, branchRow, status);
    return card;
}

export function initRepositoriesView(): void {
    const list = byId('repo-list');
    const root = byId('repo-root');
    const form = byId<HTMLFormElement>('repo-form');
    const submit = byId<HTMLButtonElement>('repo-submit');
    const result = byId('repo-clone-result');
    const refreshButton = byId<HTMLButtonElement>('repo-refresh');

    const render = (repositories: RepositoryInfo[]): void => {
        if (repositories.length === 0) {
            list.replaceChildren(el('p', 'hint', 'No repositories yet. Clone one above to get started.'));
            return;
        }

        list.replaceChildren(...repositories.map((info) => {
            const holder = el('div');
            const show = (current: RepositoryInfo): void => {
                holder.replaceChildren(buildCard(current, (updated) => {
                    if (updated) {
                        show(updated);
                    } else {
                        holder.remove();
                        if (list.children.length === 0) {
                            render([]);
                        }
                    }
                }));
            };

            show(info);
            return holder;
        }));
    };

    const refresh = async (): Promise<void> => {
        refreshButton.disabled = true;
        showProgress(list, 'Reading repositories...');

        try {
            const response = await getRepositories();
            root.textContent = response.root;
            render(response.repositories);
        } catch (failure) {
            showError(list, (failure as Error).message);
        } finally {
            refreshButton.disabled = false;
        }
    };

    form.addEventListener('submit', async (event) => {
        event.preventDefault();

        const value = (id: string): string | undefined => byId<HTMLInputElement>(id).value.trim() || undefined;

        submit.disabled = true;
        showProgress(result, 'Cloning. Large repositories can take a few minutes...');

        try {
            const cloned = await cloneRepository({
                url: byId<HTMLInputElement>('repo-url').value.trim(),
                name: value('repo-name'),
                branch: value('repo-branch')
            });

            form.reset();
            result.replaceChildren(el('p', 'hint', `Cloned ${cloned.name} into ${cloned.path}.`));
            await refresh();
        } catch (failure) {
            showError(result, (failure as Error).message);
        } finally {
            submit.disabled = false;
        }
    });

    refreshButton.addEventListener('click', () => void refresh());

    document.querySelector<HTMLButtonElement>('.nav-button[data-target="view-repositories"]')
        ?.addEventListener('click', () => void refresh());

    if (location.hash === '#repositories') {
        void refresh();
    }
}
