export function byId<T extends HTMLElement>(id: string): T {
    const found = document.getElementById(id);
    if (!found) {
        throw new Error(`Element '${id}' is missing from the page.`);
    }

    return found as T;
}

export function el<K extends keyof HTMLElementTagNameMap>(
    tag: K,
    className?: string,
    text?: string
): HTMLElementTagNameMap[K] {
    const element = document.createElement(tag);
    if (className) {
        element.className = className;
    }
    if (text !== undefined) {
        element.textContent = text;
    }

    return element;
}

const STATUS_CLASS: Record<string, string> = {
    Success: 'ok',
    Created: 'ok',
    Updated: 'ok',
    PASS: 'ok',
    AlreadyExists: 'warn',
    Unchanged: 'warn',
    WouldUpdate: 'warn',
    Skipped: 'warn',
    PartiallyFailed: 'warn',
    Failed: 'fail',
    FAIL: 'fail'
};

export function badge(status: string): HTMLElement {
    return el('span', `badge ${STATUS_CLASS[status] ?? 'muted'}`, status);
}

export function table(headers: string[], rows: Array<Array<string | HTMLElement>>): HTMLElement {
    const element = el('table');

    const head = element.createTHead().insertRow();
    for (const header of headers) {
        head.appendChild(el('th', undefined, header));
    }

    const body = element.createTBody();
    for (const row of rows) {
        const tr = body.insertRow();
        for (const value of row) {
            const cell = tr.insertCell();
            if (typeof value === 'string') {
                cell.textContent = value;
            } else {
                cell.appendChild(value);
            }
        }
    }

    return element;
}

export function section(title: string, ...children: HTMLElement[]): HTMLElement {
    const element = el('section', 'result-block');
    element.appendChild(el('h3', undefined, title));
    children.forEach((child) => element.appendChild(child));
    return element;
}

export function stats(items: Array<[string, string]>): HTMLElement {
    const element = el('dl', 'stats');

    for (const [label, value] of items) {
        element.append(el('dt', undefined, label), el('dd', undefined, value));
    }

    return element;
}

export function showError(container: HTMLElement, message: string): void {
    container.replaceChildren(el('p', 'error', message));
}

export function showProgress(container: HTMLElement, message: string): void {
    const note = el('div', 'progress-note');
    note.append(el('span', 'spinner'), el('span', undefined, message));
    container.replaceChildren(note);
}

export function bindCollapse(
    target: HTMLElement,
    button: HTMLButtonElement,
    storageKey: string,
    noun: string
): void {
    const apply = (collapsed: boolean): void => {
        target.dataset.collapsed = String(collapsed);
        button.setAttribute('aria-expanded', String(!collapsed));

        const label = `${collapsed ? 'Expand' : 'Collapse'} ${noun}`;
        button.title = label;
        button.setAttribute('aria-label', label);
    };

    let stored: string | null = null;
    try {
        stored = localStorage.getItem(storageKey);
    } catch {
        // Storage can be blocked; the panel simply starts expanded.
    }

    apply(stored === 'true');

    button.addEventListener('click', () => {
        const collapsed = target.dataset.collapsed !== 'true';
        apply(collapsed);

        try {
            localStorage.setItem(storageKey, String(collapsed));
        } catch {
            // Persisting the choice is best effort.
        }
    });
}

export function formatBytes(bytes: number): string {
    if (bytes < 1024) {
        return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
        return `${Math.round(bytes / 1024)} KB`;
    }

    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
