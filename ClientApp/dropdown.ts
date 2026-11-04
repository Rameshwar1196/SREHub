const MENU_MAX_HEIGHT = 280;
const MENU_GAP = 6;

interface MenuSource {
    anchor: HTMLElement;
    labels: string[];
    selected: number;
    typing: boolean;
    choose: (index: number) => void;
}

let activeMenu: { close: () => void } | undefined;

function openMenu(source: MenuSource): void {
    activeMenu?.close();

    if (source.labels.length === 0) {
        return;
    }

    const { anchor, labels, typing } = source;
    const menu = document.createElement('div');
    menu.className = 'wl-menu';
    menu.setAttribute('role', 'listbox');
    menu.addEventListener('mousedown', (event) => event.preventDefault());

    const items = labels.map((label, index) => {
        const item = document.createElement('div');
        item.className = 'wl-menu-item';
        item.setAttribute('role', 'option');
        item.textContent = label;
        item.classList.toggle('selected', index === source.selected);
        item.setAttribute('aria-selected', String(index === source.selected));
        item.addEventListener('mousemove', () => highlight(index));
        item.addEventListener('click', () => choose(index));
        menu.appendChild(item);
        return item;
    });

    // A text box keeps typing as its default; only a deliberate arrow key arms Enter.
    let active = typing ? -1 : Math.max(source.selected, 0);

    function highlight(index: number): void {
        active = index;
        items.forEach((item, position) => item.classList.toggle('active', position === index));
        items[index]?.scrollIntoView({ block: 'nearest' });
    }

    function close(): void {
        menu.remove();
        document.removeEventListener('mousedown', onOutside, true);
        document.removeEventListener('keydown', onKey, true);
        window.removeEventListener('resize', close);
        window.removeEventListener('scroll', onScroll, true);
        activeMenu = undefined;
    }

    function choose(index: number): void {
        close();
        anchor.focus();
        source.choose(index);
    }

    function onOutside(event: MouseEvent): void {
        if (!menu.contains(event.target as Node) && event.target !== anchor) {
            close();
        }
    }

    function onScroll(event: Event): void {
        if (!menu.contains(event.target as Node)) {
            close();
        }
    }

    function onKey(event: KeyboardEvent): void {
        const last = items.length - 1;

        switch (event.key) {
            case 'ArrowDown':
                highlight(Math.min(active + 1, last));
                break;
            case 'ArrowUp':
                highlight(Math.max(active - 1, 0));
                break;
            case 'Home':
                if (typing) {
                    return;
                }
                highlight(0);
                break;
            case 'End':
                if (typing) {
                    return;
                }
                highlight(last);
                break;
            case 'Enter':
                if (active < 0) {
                    close();
                    return;
                }
                choose(active);
                break;
            case ' ':
                if (typing) {
                    return;
                }
                choose(active);
                break;
            case 'Escape':
                close();
                anchor.focus();
                break;
            case 'Tab':
                close();
                return;
            default:
                return;
        }

        event.preventDefault();
        event.stopPropagation();
    }

    document.body.appendChild(menu);

    const box = anchor.getBoundingClientRect();
    menu.style.minWidth = `${box.width}px`;
    menu.style.maxHeight = `${MENU_MAX_HEIGHT}px`;

    const height = Math.min(menu.scrollHeight, MENU_MAX_HEIGHT);
    const fitsBelow = box.bottom + MENU_GAP + height <= window.innerHeight;
    menu.style.left = `${Math.max(8, Math.min(box.left, window.innerWidth - menu.offsetWidth - 8))}px`;
    menu.style.top = `${fitsBelow ? box.bottom + MENU_GAP : Math.max(8, box.top - MENU_GAP - height)}px`;

    if (active >= 0) {
        highlight(active);
    }

    document.addEventListener('mousedown', onOutside, true);
    document.addEventListener('keydown', onKey, true);
    window.addEventListener('resize', close);
    window.addEventListener('scroll', onScroll, true);
    activeMenu = { close };
}

function openSelect(select: HTMLSelectElement): void {
    openMenu({
        anchor: select,
        labels: Array.from(select.options).map((option) => option.textContent ?? ''),
        selected: select.selectedIndex,
        typing: false,
        choose: (index) => {
            const changed = select.selectedIndex !== index;
            select.selectedIndex = index;

            if (changed) {
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }
    });
}

// Suggestions come from the datalist named in data-list; typing narrows them.
function openSuggestions(input: HTMLInputElement, narrow: boolean): void {
    const list = document.getElementById(input.dataset.list ?? '');
    const all = Array.from(list?.querySelectorAll('option') ?? []).map((option) => option.value);
    const typed = input.value.trim().toLowerCase();
    const labels = narrow && typed ? all.filter((value) => value.toLowerCase().includes(typed)) : all;

    if (labels.length === 0 || (narrow && labels.length === 1 && labels[0].toLowerCase() === typed)) {
        activeMenu?.close();
        return;
    }

    openMenu({
        anchor: input,
        labels,
        selected: labels.findIndex((value) => value === input.value),
        typing: true,
        choose: (index) => {
            input.value = labels[index];
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }
    });
}

// Replaces the browser's native lists with themed ones for every <select> and data-list input inside root.
export function enhanceSelects(root: HTMLElement): void {
    const selectFrom = (event: Event): HTMLSelectElement | undefined => {
        const select = (event.target as HTMLElement).closest('select');
        return select && !select.disabled ? select : undefined;
    };

    const suggestFrom = (event: Event): HTMLInputElement | undefined => {
        const input = event.target as HTMLElement;
        return input instanceof HTMLInputElement && input.dataset.list && !input.disabled ? input : undefined;
    };

    root.addEventListener('mousedown', (event) => {
        const select = selectFrom(event);

        if (!select || event.button !== 0) {
            return;
        }

        event.preventDefault();
        select.focus();
        openSelect(select);
    });

    root.addEventListener('click', (event) => {
        const input = suggestFrom(event);

        if (input) {
            openSuggestions(input, false);
        }
    });

    root.addEventListener('input', (event) => {
        const input = suggestFrom(event);

        if (input && event.isTrusted) {
            openSuggestions(input, true);
        }
    });

    root.addEventListener('keydown', (event) => {
        const select = selectFrom(event);

        if (select && (event.key === 'Enter' || event.key === ' ' || (event.altKey && event.key === 'ArrowDown'))) {
            event.preventDefault();
            openSelect(select);
            return;
        }

        const input = suggestFrom(event);

        if (input && event.key === 'ArrowDown') {
            event.preventDefault();
            openSuggestions(input, false);
        }
    });
}
