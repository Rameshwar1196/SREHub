import { getCurrentUser, getLearningInfo, signOut } from './api.js';
import { bindCollapse, byId } from './dom.js';
import { initDutyLogView } from './dutyLogView.js';
import { initPersonalFilesView, initSecureFilesView } from './secureFilesView.js';
import { initLearningView } from './learningView.js';
import { initImportantFilesView } from './importantFilesView.js';
import { initLinksView } from './linksView.js';
import { initMathViews } from './mathView.js';
import { initMathsLibraryView } from './mathsLibraryView.js';
import { initMathsBookView } from './mathsBookView.js';
import { wireLicenseViews } from './licenseView.js';
import { showView } from './navigation.js';
import { initRepositoriesView } from './repositoryView.js';
import { initTerminalView } from './terminalView.js';
import { initTrackerView } from './trackerView.js';
import { initWorkLogView } from './workLogView.js';

function wireNavigation(): void {
    for (const button of document.querySelectorAll<HTMLButtonElement>('.nav-button')) {
        button.addEventListener('click', () => showView(button.dataset.target ?? 'view-learning'));
    }

    const requested = `view-${location.hash.replace('#', '').split('/')[0]}`;
    if (document.getElementById(requested)) {
        showView(requested);
    }
}

function wireNavGroups(): void {
    const shell = byId('app-shell');
    const groups = [...document.querySelectorAll<HTMLElement>('.nav-group')];
    const settings = new Map<HTMLElement, { apply: (collapsed: boolean) => void; persist: (collapsed: boolean) => void }>();

    // Opening one group closes the rest, so at most one is ever expanded.
    const expandOnly = (open: HTMLElement | undefined): void => {
        for (const group of groups) {
            const collapsed = group !== open;
            settings.get(group)!.apply(collapsed);
            settings.get(group)!.persist(collapsed);
        }
    };

    for (const group of groups) {
        const head = group.querySelector<HTMLButtonElement>('.nav-group-head')!;
        const key = `srehub.group.${group.dataset.group}`;

        const apply = (collapsed: boolean): void => {
            group.dataset.collapsed = String(collapsed);
            head.setAttribute('aria-expanded', String(!collapsed));
        };

        const persist = (collapsed: boolean): void => {
            try {
                localStorage.setItem(key, String(collapsed));
            } catch {
                // Remembering the choice is best effort.
            }
        };

        settings.set(group, { apply, persist });

        let stored: string | null = null;
        try {
            stored = localStorage.getItem(key);
        } catch {
            // Storage can be blocked; groups start expanded.
        }

        apply(stored === 'true');

        head.addEventListener('click', () => {
            if (shell.dataset.collapsed === 'true') {
                byId<HTMLButtonElement>('rail-toggle').click();
                expandOnly(group);
                return;
            }

            if (group.dataset.collapsed === 'true') {
                expandOnly(group);
            } else {
                apply(true);
                persist(true);
            }
        });
    }

    const startOpen = groups.find((group) => group.dataset.collapsed !== 'true')
        ?? groups.find((group) => group.querySelector('.nav-button.active'));
    expandOnly(startOpen);
}

function initials(name: string): string {
    const parts = name.split(/[\s@._-]+/).filter((part) => part.length > 0);
    const letters = parts.slice(0, 2).map((part) => part[0]).join('');
    return (letters || name.slice(0, 2)).toUpperCase();
}

function wireTheme(): void {
    const swatches = [...document.querySelectorAll<HTMLButtonElement>('.theme-swatch')];

    const apply = (theme: string): void => {
        document.documentElement.dataset.theme = theme;
        swatches.forEach((swatch) => swatch.setAttribute('aria-pressed', String(swatch.dataset.theme === theme)));
    };

    apply(document.documentElement.dataset.theme ?? 'ocean');

    for (const swatch of swatches) {
        swatch.addEventListener('click', () => {
            const theme = swatch.dataset.theme ?? 'ocean';
            apply(theme);

            try {
                localStorage.setItem('srehub.theme', theme);
            } catch {
                // The choice just will not persist.
            }
        });
    }
}

function wireProfileMenu(): void {
    const toggle = byId<HTMLButtonElement>('profile-toggle');
    const menu = byId('profile-menu');
    const dialog = byId<HTMLDialogElement>('about-dialog');

    const setOpen = (open: boolean): void => {
        menu.classList.toggle('hidden', !open);
        toggle.setAttribute('aria-expanded', String(open));
    };

    toggle.addEventListener('click', (event) => {
        event.stopPropagation();
        setOpen(menu.classList.contains('hidden'));
    });

    document.addEventListener('click', (event) => {
        if (!menu.contains(event.target as Node)) {
            setOpen(false);
        }
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            setOpen(false);
        }
    });

    byId<HTMLButtonElement>('about').addEventListener('click', async () => {
        setOpen(false);

        const email = byId('user-email').textContent;
        byId('about-user').textContent = [byId('user-name').textContent, email ? `<${email}>` : ''].join(' ').trim();

        try {
            byId('about-root').textContent = (await getLearningInfo()).contentRoot;
        } catch {
            byId('about-root').textContent = '-';
        }

        dialog.showModal();
    });

    byId<HTMLButtonElement>('about-close').addEventListener('click', () => dialog.close());

    dialog.addEventListener('click', (event) => {
        if (event.target === dialog) {
            dialog.close();
        }
    });
}

async function wireAccount(): Promise<void> {
    byId<HTMLButtonElement>('logout').addEventListener('click', async () => {
        try {
            await signOut();
        } finally {
            location.replace('/login.html');
        }
    });

    try {
        const user = await getCurrentUser();
        byId('user-name').textContent = user.displayName;
        byId('user-email').textContent = user.email;
        byId('user-avatar').textContent = initials(user.displayName || user.email);
        const first = (user.displayName || user.email).split(/[\s@]/)[0];
        byId('home-title').textContent = `${byId('home-greeting').textContent}, ${first}`;
    } catch {
        // unwrap() already redirects to the sign-in page on 401.
    }
}

const quotes: readonly (readonly [string, string])[] = [
    ['Hope is not a strategy.', 'Google SRE'],
    ['Everything fails all the time.', 'Werner Vogels'],
    ['The best way to predict the future is to create it.', 'Peter Drucker'],
    ['Small daily improvements are the key to staggering long-term results.', 'Robin Sharma'],
    ['It always seems impossible until it is done.', 'Nelson Mandela'],
    ['Reliability is the most fundamental feature of any product.', 'Google SRE'],
    ['Discipline is choosing between what you want now and what you want most.', 'Abraham Lincoln'],
    ['You do not rise to the level of your goals, you fall to the level of your systems.', 'James Clear'],
    ['Well done is better than well said.', 'Benjamin Franklin'],
    ['Calm is a superpower during an incident.', 'SRE proverb'],
    ['Learn something new every day, and ship it.', 'SRE proverb'],
    ['The only way to do great work is to love what you do.', 'Steve Jobs']
];

function wireHome(): void {
    const hour = new Date().getHours();
    byId('home-greeting').textContent = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';

    const dayNumber = Math.floor(Date.now() / 86_400_000);
    const [text, author] = quotes[dayNumber % quotes.length];
    byId('home-quote').textContent = `\u201C${text}\u201D`;
    byId('home-author').textContent = author;

    for (const card of document.querySelectorAll<HTMLButtonElement>('.home-card')) {
        card.addEventListener('click', () => showView(card.dataset.go ?? 'view-home'));
    }

    document.querySelector('.brand')?.addEventListener('click', () => showView('view-home'));
}

wireNavigation();
wireHome();
wireNavGroups();
bindCollapse(byId('app-shell'), byId<HTMLButtonElement>('rail-toggle'), 'srehub.rail', 'navigation');
wireProfileMenu();
wireTheme();
wireLicenseViews();
initTerminalView();
initMathViews();
initMathsLibraryView();
initMathsBookView();
initRepositoriesView();
initWorkLogView();
initDutyLogView();
initSecureFilesView();
initLinksView();
initTrackerView();
initImportantFilesView();
initPersonalFilesView();
void wireAccount();
void initLearningView();

byId('app-shell').classList.remove('loading');
