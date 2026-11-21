import { checkLinkFrame, deleteLink, getLinkPassword, getLinks, saveLink } from './api.js';
import { askConfirm } from './dialogs.js';
import { byId, el, showError, showProgress } from './dom.js';
import { ImportantLink, LINK_GROUPS } from './linkTypes.js';
import { onViewShown } from './navigation.js';
import { showToast, toastCopied } from './toast.js';
import { openInSideWindow, openInWorkTab } from './workWindow.js';

const REVEAL_MILLISECONDS = 10_000;
const ADD_TITLE = 'Add a link to manage your work';

function hueFor(text: string): number {
    let hash = 0;

    for (const character of text.toLowerCase()) {
        hash = (hash * 31 + character.charCodeAt(0)) % 360;
    }

    return hash;
}

function hostOf(url: string): string {
    try {
        return new URL(url).host;
    } catch {
        return url;
    }
}

function isWebUrl(url: string): boolean {
    return /^https?:\/\//i.test(url);
}

async function copyText(text: string): Promise<void> {
    if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        return;
    }

    const scratch = el('textarea');
    scratch.value = text;
    scratch.style.position = 'fixed';
    scratch.style.opacity = '0';
    document.body.appendChild(scratch);
    scratch.select();

    try {
        if (!document.execCommand('copy')) {
            throw new Error('Copy is not available in this browser.');
        }
    } finally {
        scratch.remove();
    }
}

function flash(button: HTMLButtonElement, text: string): void {
    const original = button.dataset.label ?? button.textContent ?? '';
    button.dataset.label = original;
    button.textContent = text;
    window.setTimeout(() => {
        button.textContent = original;
    }, 1500);
}

export function initLinksView(): void {
    const form = byId<HTMLFormElement>('link-form');
    const panel = byId<HTMLDetailsElement>('link-panel');
    const formTitle = byId('link-form-title');
    const formError = byId('link-form-error');
    const titleInput = byId<HTMLInputElement>('link-title');
    const urlInput = byId<HTMLInputElement>('link-url');
    const categoryInput = byId<HTMLSelectElement>('link-category');
    const descriptionInput = byId<HTMLInputElement>('link-description');
    const usernameInput = byId<HTMLInputElement>('link-username');
    const passwordInput = byId<HTMLInputElement>('link-password');
    const passwordToggle = byId<HTMLButtonElement>('link-password-toggle');
    const clearPasswordRow = byId('link-clear-password-row');
    const clearPassword = byId<HTMLInputElement>('link-clear-password');
    const submit = byId<HTMLButtonElement>('link-submit');
    const cancel = byId<HTMLButtonElement>('link-cancel');
    const filter = byId<HTMLInputElement>('link-filter');
    const list = byId('link-list');
    const viewer = byId('link-viewer');
    const viewerName = byId('link-viewer-name');
    const viewerHost = byId('link-viewer-host');
    const viewerBody = byId('link-viewer-body');
    const viewerReload = byId<HTMLButtonElement>('link-viewer-reload');
    const viewerTab = byId<HTMLButtonElement>('link-viewer-tab');
    const viewerSide = byId<HTMLButtonElement>('link-viewer-side');
    const viewerClose = byId<HTMLButtonElement>('link-viewer-close');

    let links: ImportantLink[] = [];
    let editingId: string | undefined;
    let viewing: ImportantLink | undefined;
    let viewToken = 0;

    const closeViewer = (): void => {
        viewToken++;
        viewing = undefined;
        viewer.hidden = true;
        viewerBody.replaceChildren();
    };

    const showFrame = (link: ImportantLink): void => {
        const frame = el('iframe', 'link-viewer-frame');
        frame.title = link.title;
        frame.src = link.url;
        frame.referrerPolicy = 'no-referrer';
        frame.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-forms allow-popups allow-popups-to-escape-sandbox allow-downloads');
        viewerBody.replaceChildren(frame, el('p', 'hint link-viewer-note', 'If the page stays blank, the site refuses to be shown here. Use Work tab instead.'));
    };

    const showBlocked = (link: ImportantLink, reason: string, canTry: boolean): void => {
        const message = el('div', 'link-viewer-blocked');
        message.append(
            el('strong', undefined, `${link.title} can't be shown inside SreHub`),
            el('p', undefined, reason),
            el('p', 'hint', 'Open it in your work tab or the side window instead. The sign-in and your session stay with the site.'));

        const buttons = el('div', 'link-viewer-actions');
        const tab = el('button', undefined, 'Open in work tab');
        tab.type = 'button';
        tab.addEventListener('click', () => openInWorkTab(link.url));
        const side = el('button', 'link-button', 'Open in side window');
        side.type = 'button';
        side.addEventListener('click', () => openInSideWindow(link.url));
        buttons.append(tab, side);

        if (canTry) {
            const attempt = el('button', 'link-button', 'Try anyway');
            attempt.type = 'button';
            attempt.addEventListener('click', () => showFrame(link));
            buttons.append(attempt);
        }

        message.append(buttons);
        viewerBody.replaceChildren(message);
    };

    const openViewer = async (link: ImportantLink): Promise<void> => {
        const token = ++viewToken;
        viewing = link;
        viewer.hidden = false;
        viewerName.textContent = link.title;
        viewerHost.textContent = hostOf(link.url);
        showProgress(viewerBody, `Checking whether ${hostOf(link.url)} allows being shown here...`);
        viewer.scrollIntoView({ behavior: 'smooth', block: 'start' });

        try {
            const check = await checkLinkFrame(link.id);

            if (token !== viewToken) {
                return;
            }

            check.embeddable ? showFrame(link) : showBlocked(link, check.reason, false);
        } catch (failure) {
            if (token === viewToken) {
                showBlocked(link, `It could not be checked: ${(failure as Error).message}`, true);
            }
        }
    };

    viewerReload.addEventListener('click', () => viewing && void openViewer(viewing));
    viewerTab.addEventListener('click', () => viewing && openInWorkTab(viewing.url));
    viewerSide.addEventListener('click', () => viewing && openInSideWindow(viewing.url));
    viewerClose.addEventListener('click', closeViewer);

    const COLLAPSED_KEY = 'srehub.links.collapsed';
    const collapsedGroups = new Set<string>();

    try {
        (JSON.parse(localStorage.getItem(COLLAPSED_KEY) ?? '[]') as string[]).forEach((name) => collapsedGroups.add(name));
    } catch {
        // A blocked or corrupt store just means every group starts expanded.
    }

    const saveCollapsed = (): void => {
        try {
            localStorage.setItem(COLLAPSED_KEY, JSON.stringify([...collapsedGroups]));
        } catch {
            // Remembering the choice is best effort.
        }
    };

    const fillGroupOptions = (selected?: string): void => {
        const legacy = new Set<string>();
        for (const name of [...links.map((link) => link.category), selected ?? '']) {
            if (name && !(LINK_GROUPS as readonly string[]).includes(name)) {
                legacy.add(name);
            }
        }

        const current = selected ?? categoryInput.value;
        categoryInput.replaceChildren(...[...LINK_GROUPS, ...[...legacy].sort()].map((name) => {
            const option = el('option', undefined, name);
            option.value = name;
            return option;
        }));
        categoryInput.value = current || LINK_GROUPS[0];
    };

    const setPasswordVisible = (visible: boolean): void => {
        passwordInput.type = visible ? 'text' : 'password';
        passwordToggle.textContent = visible ? 'Hide' : 'Show';
        passwordToggle.setAttribute('aria-label', visible ? 'Hide password' : 'Show password');
    };

    const resetForm = (group?: string): void => {
        editingId = undefined;
        form.reset();
        fillGroupOptions(group ?? LINK_GROUPS[0]);
        setPasswordVisible(false);
        passwordInput.placeholder = '';
        clearPasswordRow.classList.add('hidden');
        formTitle.textContent = ADD_TITLE;
        submit.textContent = 'Add link';
        cancel.classList.add('hidden');
        formError.replaceChildren();
    };

    const startEdit = (link: ImportantLink): void => {
        editingId = link.id;
        titleInput.value = link.title;
        urlInput.value = link.url;
        fillGroupOptions(link.category);
        descriptionInput.value = link.description;
        usernameInput.value = link.username;
        passwordInput.value = '';
        passwordInput.placeholder = link.hasPassword ? 'Saved - leave blank to keep it' : '';
        clearPassword.checked = false;
        clearPasswordRow.classList.toggle('hidden', !link.hasPassword);
        setPasswordVisible(false);
        formTitle.textContent = 'Edit link';
        submit.textContent = 'Save changes';
        cancel.classList.remove('hidden');
        formError.replaceChildren();
        panel.open = true;
        form.scrollIntoView({ behavior: 'smooth', block: 'center' });
        titleInput.focus();
    };

    const buildCredentials = (link: ImportantLink): HTMLElement | undefined => {
        if (!link.username && !link.hasPassword) {
            return undefined;
        }

        const box = el('div', 'link-credentials');

        if (link.username) {
            const row = el('div', 'link-credential');
            const copy = el('button', 'link-button', 'Copy');
            copy.type = 'button';
            copy.addEventListener('click', async () => {
                try {
                    await copyText(link.username);
                    flash(copy, 'Copied');
                    toastCopied('Username');
                } catch (failure) {
                    showError(list, (failure as Error).message);
                }
            });

            row.append(el('span', 'link-credential-label', 'User'), el('span', 'link-credential-value', link.username), copy);
            box.append(row);
        }

        if (link.hasPassword) {
            const row = el('div', 'link-credential');
            const value = el('span', 'link-credential-value', '\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022');
            const reveal = el('button', 'link-button', 'Show');
            const copy = el('button', 'link-button', 'Copy');
            reveal.type = 'button';
            copy.type = 'button';

            let hideTimer: number | undefined;

            const hide = (): void => {
                window.clearTimeout(hideTimer);
                value.textContent = '\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022';
                reveal.textContent = 'Show';
            };

            reveal.addEventListener('click', async () => {
                if (reveal.textContent === 'Hide') {
                    hide();
                    return;
                }

                try {
                    value.textContent = (await getLinkPassword(link.id)).password;
                    reveal.textContent = 'Hide';
                    hideTimer = window.setTimeout(hide, REVEAL_MILLISECONDS);
                } catch (failure) {
                    showError(list, (failure as Error).message);
                }
            });

            copy.addEventListener('click', async () => {
                try {
                    await copyText((await getLinkPassword(link.id)).password);
                    flash(copy, 'Copied');
                    toastCopied('Password');
                } catch (failure) {
                    showError(list, (failure as Error).message);
                }
            });

            const actions = el('span', 'link-credential-actions');
            actions.append(reveal, copy);
            row.append(el('span', 'link-credential-label', 'Pass'), value, actions);
            box.append(row);
        }

        return box;
    };

    const buildCard = (link: ImportantLink): HTMLElement => {
        const card = el('article', 'link-card');
        card.style.setProperty('--h', String(hueFor(link.category)));

        const open = el('a', 'link-open');
        open.append(el('span', 'link-title', link.title), el('span', 'link-host', hostOf(link.url)));

        if (isWebUrl(link.url)) {
            open.href = link.url;
            open.target = '_blank';
            open.rel = 'noopener noreferrer';
            open.title = 'Open in your work tab (Ctrl+click for a new tab)';
            open.addEventListener('click', (event) => {
                if (event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
                    event.preventDefault();
                    openInWorkTab(link.url);
                }
            });
        }

        const actions = el('div', 'link-actions');

        if (isWebUrl(link.url)) {
            const here = el('button', 'link-button link-here', 'Open here');
            here.type = 'button';
            here.title = 'Show the site inside SreHub, if it allows that';
            here.addEventListener('click', () => void openViewer(link));

            const side = el('button', 'link-button', 'Side');
            side.type = 'button';
            side.title = 'Open in a narrow window next to SreHub';
            side.addEventListener('click', () => openInSideWindow(link.url));
            actions.append(here, side);
        }

        const edit = el('button', 'link-button', 'Edit');
        edit.type = 'button';
        edit.addEventListener('click', () => startEdit(link));

        const remove = el('button', 'link-button danger', 'Delete');
        remove.type = 'button';
        remove.addEventListener('click', async () => {
            if (!await askConfirm('Delete link', `Delete the link '${link.title}'${link.hasPassword ? ' and its saved password' : ''}?`, 'Delete')) {
                return;
            }

            try {
                await deleteLink(link.id);
                showToast(`Deleted '${link.title}'.`, 'success', 'Link deleted');

                if (viewing?.id === link.id) {
                    closeViewer();
                }

                if (editingId === link.id) {
                    resetForm();
                }

                await load();
            } catch (failure) {
                showError(list, (failure as Error).message);
            }
        });

        actions.append(edit, remove);
        card.append(open);

        if (link.description) {
            card.append(el('p', 'link-description', link.description));
        }

        const credentials = buildCredentials(link);
        if (credentials) {
            card.append(credentials);
        }

        card.append(actions);
        return card;
    };

    const buildGroup = (category: string, items: ImportantLink[], searching: boolean): HTMLElement => {
        const group = el('section', 'link-group');
        group.style.setProperty('--h', String(hueFor(category)));

        const isCollapsed = !searching && collapsedGroups.has(category);
        group.classList.toggle('collapsed', isCollapsed);

        const heading = el('h3', 'link-group-title');
        const toggle = el('button', 'link-group-toggle');
        toggle.type = 'button';
        toggle.setAttribute('aria-expanded', String(!isCollapsed));
        toggle.setAttribute('aria-label', `${isCollapsed ? 'Expand' : 'Collapse'} ${category}`);
        toggle.append(el('i', 'link-group-chevron'), el('span', undefined, category));
        toggle.addEventListener('click', () => {
            collapsedGroups.has(category) ? collapsedGroups.delete(category) : collapsedGroups.add(category);
            saveCollapsed();
            render();
        });
        heading.append(toggle, el('span', 'link-count', String(items.length)));

        group.append(heading);

        if (isCollapsed) {
            return group;
        }

        if (items.length === 0) {
            group.append(el('p', 'hint', searching ? 'No matches in this group.' : `No ${category} links yet.`));
            return group;
        }

        const grid = el('div', 'link-grid');
        grid.append(...items.map(buildCard));
        group.append(grid);
        return group;
    };

    const render = (): void => {
        const query = filter.value.trim().toLowerCase();
        const visible = links.filter((link) =>
            !query || [link.title, link.url, link.category, link.description, link.username].some((value) => value.toLowerCase().includes(query)));

        fillGroupOptions(editingId ? categoryInput.value : undefined);

        const byGroup = new Map<string, ImportantLink[]>(LINK_GROUPS.map((name) => [name, []]));
        for (const link of visible) {
            byGroup.set(link.category, [...(byGroup.get(link.category) ?? []), link]);
        }

        list.replaceChildren(...[...byGroup.entries()]
            .filter(([name, items]) => items.length > 0 || (LINK_GROUPS as readonly string[]).includes(name))
            .map(([name, items]) => buildGroup(name, items, query.length > 0)));
    };

    const load = async (): Promise<void> => {
        showProgress(list, 'Loading links...');

        try {
            links = await getLinks();
            render();
        } catch (failure) {
            showError(list, (failure as Error).message);
        }
    };

    form.addEventListener('submit', async (event) => {
        event.preventDefault();
        submit.disabled = true;
        formError.replaceChildren();

        try {
            await saveLink(editingId, {
                title: titleInput.value,
                url: urlInput.value,
                category: categoryInput.value,
                description: descriptionInput.value,
                username: usernameInput.value,
                password: passwordInput.value || undefined,
                clearPassword: clearPassword.checked
            });

            resetForm(categoryInput.value);
            panel.open = false;
            showToast('Your link was saved.', 'success', 'Saved');
            await load();
        } catch (failure) {
            showError(formError, (failure as Error).message);
        } finally {
            submit.disabled = false;
        }
    });

    passwordToggle.addEventListener('click', () => setPasswordVisible(passwordInput.type === 'password'));
    cancel.addEventListener('click', () => {
        resetForm();
        panel.open = false;
    });
    filter.addEventListener('input', render);

    onViewShown('view-links', () => void load());

    resetForm();

    if (location.hash === '#links') {
        void load();
    }
}
