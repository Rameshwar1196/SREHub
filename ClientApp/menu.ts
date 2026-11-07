import { el } from './dom.js';

export interface MenuItem {
    label: string;
    onSelect: () => void;
    danger?: boolean;
    separatorBefore?: boolean;
}

let closeOpenMenu: (() => void) | undefined;

export function openMenu(x: number, y: number, items: MenuItem[], onClose?: () => void): void {
    closeOpenMenu?.();

    const menu = el('div', 'popup-menu');
    menu.setAttribute('role', 'menu');
    const buttons: HTMLButtonElement[] = [];

    for (const item of items) {
        if (item.separatorBefore) {
            menu.appendChild(el('div', 'popup-menu-separator'));
        }

        const button = el('button', item.danger ? 'popup-menu-item danger' : 'popup-menu-item', item.label);
        button.type = 'button';
        button.setAttribute('role', 'menuitem');
        button.addEventListener('click', () => {
            close();
            item.onSelect();
        });
        buttons.push(button);
        menu.appendChild(button);
    }

    const close = (): void => {
        menu.remove();
        document.removeEventListener('mousedown', onOutside, true);
        document.removeEventListener('keydown', onKey, true);
        window.removeEventListener('resize', close);
        closeOpenMenu = undefined;
        onClose?.();
    };

    const onOutside = (event: MouseEvent): void => {
        if (!menu.contains(event.target as Node)) {
            close();
        }
    };

    const onKey = (event: KeyboardEvent): void => {
        const index = buttons.indexOf(document.activeElement as HTMLButtonElement);

        if (event.key === 'Escape') {
            event.preventDefault();
            close();
        } else if (event.key === 'ArrowDown') {
            event.preventDefault();
            buttons[(index + 1) % buttons.length].focus();
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            buttons[(index - 1 + buttons.length) % buttons.length].focus();
        }
    };

    document.body.appendChild(menu);
    const left = Math.max(8, Math.min(x, window.innerWidth - menu.offsetWidth - 8));
    const top = Math.max(8, Math.min(y, window.innerHeight - menu.offsetHeight - 8));
    menu.style.left = `${left}px`;
    menu.style.top = `${top}px`;

    document.addEventListener('mousedown', onOutside, true);
    document.addEventListener('keydown', onKey, true);
    window.addEventListener('resize', close);
    closeOpenMenu = close;
    buttons[0]?.focus();
}

// A button that opens the menu below itself; right-aligns the menu to the button when `alignRight` is set.
export function menuButton(label: string, className: string, items: MenuItem[], alignRight = false): HTMLButtonElement {
    const button = el('button', className, label);
    button.type = 'button';
    button.setAttribute('aria-haspopup', 'menu');
    button.addEventListener('click', () => {
        const box = button.getBoundingClientRect();
        button.classList.add('open');
        openMenu(alignRight ? box.right - 200 : box.left, box.bottom + 6, items, () => button.classList.remove('open'));
    });

    return button;
}
