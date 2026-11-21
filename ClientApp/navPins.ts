import { byId, el } from './dom.js';
import { navIcon } from './navIcons.js';
import { onAnyViewShown } from './navigation.js';

const STORAGE_KEY = 'srehub.pinned';
const SVG_NS = 'http://www.w3.org/2000/svg';
const PIN_SHAPE = '<path d="M12 17v5"/><path d="M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z"/>';

interface NavEntry {
    key: string;
    button: HTMLButtonElement;
    row: HTMLElement;
    pin: HTMLButtonElement;
    group: string;
    title: string;
}

function pinIcon(className: string): SVGElement {
    const svg = document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    svg.setAttribute('class', className);
    svg.innerHTML = PIN_SHAPE;
    return svg;
}

function loadPinned(): string[] {
    try {
        const stored: unknown = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]');
        return Array.isArray(stored) ? stored.filter((key): key is string => typeof key === 'string') : [];
    } catch {
        return [];
    }
}

function savePinned(keys: string[]): void {
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(keys));
    } catch {
        // Remembering the pins is best effort.
    }
}

// Items pinned from the Contents tree get an icon button in the slim strip beside it. The Contents panel is docked next to the strip or,
// when unpinned, opens over the page from the strip's list button.
export function initNavPins(): void {
    const shell = byId('app-shell');
    const railToggle = byId<HTMLButtonElement>('rail-toggle');
    const rail = byId('app-rail');
    const dock = byId<HTMLButtonElement>('nav-pin-rail');
    const stripContents = byId<HTMLButtonElement>('strip-contents');
    const strip = byId('pin-strip');
    const stripPins = byId('strip-pins');
    const stripHint = byId('strip-hint');

    const entries = new Map<string, NavEntry>();

    for (const button of document.querySelectorAll<HTMLButtonElement>('.nav-button')) {
        const key = button.dataset.views?.split(' ')[0] ?? button.dataset.target;

        if (!key || entries.has(key)) {
            continue;
        }

        const group = button.closest<HTMLElement>('.nav-group')?.dataset.group ?? '';
        const title = button.querySelector('.nav-title')?.textContent?.trim() ?? button.title;

        const badge = el('span', 'nav-ico');
        badge.appendChild(navIcon(key));
        button.querySelector('.nav-index')?.replaceWith(badge);

        const row = el('div', 'nav-row');
        row.dataset.key = key;
        row.dataset.group = group;
        button.replaceWith(row);

        const pin = el('button', 'nav-pin');
        pin.type = 'button';
        pin.appendChild(pinIcon('icon'));
        row.append(button, pin);

        entries.set(key, { key, button, row, pin, group, title });
    }

    let pinned = loadPinned().filter((key) => entries.has(key));

    const syncActive = (): void => {
        for (const item of stripPins.querySelectorAll<HTMLElement>('.strip-pin')) {
            item.classList.toggle('active', entries.get(item.dataset.key ?? '')?.button.classList.contains('active') ?? false);
        }
    };

    const togglePin = (key: string): void => {
        pinned = pinned.includes(key) ? pinned.filter((existing) => existing !== key) : [...pinned, key];
        savePinned(pinned);
        render();
    };

    const buildStripPin = (entry: NavEntry): HTMLElement => {
        const item = el('button', 'strip-pin');
        item.type = 'button';
        item.dataset.key = entry.key;
        item.dataset.group = entry.group;
        item.title = entry.title;
        item.setAttribute('aria-label', entry.title);
        item.appendChild(navIcon(entry.key));
        item.addEventListener('click', () => entry.button.click());
        return item;
    };

    function render(): void {
        stripPins.replaceChildren(...pinned.map((key) => buildStripPin(entries.get(key)!)));
        stripHint.hidden = pinned.length > 0;

        for (const entry of entries.values()) {
            const on = pinned.includes(entry.key);
            entry.row.dataset.pinned = String(on);
            entry.pin.setAttribute('aria-pressed', String(on));
            entry.pin.setAttribute('aria-label', `${on ? 'Unpin' : 'Pin'} ${entry.title} ${on ? 'from' : 'to'} sidebar`);
            entry.pin.title = on ? 'Unpin from sidebar' : 'Pin to sidebar';
        }

        syncActive();
    }

    for (const entry of entries.values()) {
        entry.pin.addEventListener('click', () => togglePin(entry.key));
    }

    let menu: HTMLElement | undefined;

    const closeMenu = (): void => {
        menu?.remove();
        menu = undefined;
    };

    const openMenu = (x: number, y: number, key: string): void => {
        closeMenu();

        const on = pinned.includes(key);
        const choice = el('button', 'menu-item', on ? 'Unpin from Sidebar' : 'Pin to Sidebar');
        choice.type = 'button';
        choice.setAttribute('role', 'menuitem');
        choice.prepend(pinIcon('icon'));
        choice.addEventListener('click', () => {
            togglePin(key);
            closeMenu();
        });

        menu = el('div', 'nav-context');
        menu.setAttribute('role', 'menu');
        menu.appendChild(choice);
        document.body.appendChild(menu);

        const box = menu.getBoundingClientRect();
        menu.style.left = `${Math.max(8, Math.min(x, window.innerWidth - box.width - 8))}px`;
        menu.style.top = `${Math.max(8, Math.min(y, window.innerHeight - box.height - 8))}px`;
        choice.focus();
    };

    const onContextMenu = (event: Event): void => {
        const row = (event.target as HTMLElement).closest<HTMLElement>('.nav-row, .strip-pin');

        if (row?.dataset.key) {
            event.preventDefault();
            openMenu((event as MouseEvent).clientX, (event as MouseEvent).clientY, row.dataset.key);
        }
    };

    rail.addEventListener('contextmenu', onContextMenu);
    strip.addEventListener('contextmenu', onContextMenu);

    document.addEventListener('click', closeMenu);
    document.addEventListener('scroll', closeMenu, true);
    window.addEventListener('resize', closeMenu);

    // Docked: the Contents panel sits beside the strip. Unpinned: it hides and the strip's list button opens it over the page.
    const isDocked = (): boolean => shell.dataset.collapsed !== 'true';
    const isOpen = (): boolean => isDocked() || shell.dataset.flyout === 'true';

    const reflect = (): void => {
        const docked = isDocked();

        if (docked) {
            delete shell.dataset.flyout;
        }

        const dockLabel = docked ? 'Unpin: hide Contents until you open it' : 'Pin: keep Contents open';
        dock.setAttribute('aria-pressed', String(docked));
        dock.title = dockLabel;
        dock.setAttribute('aria-label', dockLabel);

        stripContents.setAttribute('aria-expanded', String(isOpen()));
        stripContents.title = isOpen() ? 'Hide Contents' : 'Show Contents';
    };

    const closeFlyout = (): void => {
        if (shell.dataset.flyout === 'true') {
            delete shell.dataset.flyout;
            reflect();
        }
    };

    dock.addEventListener('click', () => railToggle.click());

    stripContents.addEventListener('click', (event) => {
        event.stopPropagation();

        if (isDocked()) {
            railToggle.click();
        } else if (shell.dataset.flyout === 'true') {
            closeFlyout();
        } else {
            shell.dataset.flyout = 'true';
            reflect();
        }
    });

    document.addEventListener('click', (event) => {
        const inside = (event.target as HTMLElement).closest('#app-rail, .nav-context');

        if (!inside) {
            closeFlyout();
        }
    });

    // Choosing a page from the open panel closes it again.
    rail.addEventListener('click', (event) => {
        if (!isDocked() && (event.target as HTMLElement).closest('.nav-button, .subject-item, .tree-file')) {
            closeFlyout();
        }
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            closeMenu();
            closeFlyout();
        }
    });

    new MutationObserver(reflect).observe(shell, { attributes: true, attributeFilter: ['data-collapsed'] });
    reflect();

    onAnyViewShown(syncActive);
    render();
}
