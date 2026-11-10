import { el } from './dom.js';

export type ToastKind = 'success' | 'error' | 'info';

const SVG_NS = 'http://www.w3.org/2000/svg';
const DURATION: Record<ToastKind, number> = { success: 2800, info: 3600, error: 6000 };
const TITLE: Record<ToastKind, string> = { success: 'Done', info: 'Heads up', error: 'Something went wrong' };
const ICON: Record<ToastKind, string> = {
    success: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
    info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
    error: '<path d="M12 4 3 20h18z"/><path d="M12 10v4M12 17h.01"/>'
};

function stack(): HTMLElement {
    let host = document.getElementById('toast-stack');

    if (!host) {
        host = el('div', 'toast-stack');
        host.id = 'toast-stack';
        host.setAttribute('role', 'region');
        host.setAttribute('aria-label', 'Notifications');
        document.body.appendChild(host);
    }

    return host;
}

export function showToast(message: string, kind: ToastKind = 'info', title?: string): void {
    const toast = el('div', `toast toast-${kind}`);
    toast.setAttribute('role', kind === 'error' ? 'alert' : 'status');

    const icon = document.createElementNS(SVG_NS, 'svg');
    icon.setAttribute('viewBox', '0 0 24 24');
    icon.setAttribute('aria-hidden', 'true');
    icon.innerHTML = ICON[kind];
    const badge = el('span', 'toast-icon');
    badge.appendChild(icon);

    const text = el('div', 'toast-text');
    text.append(el('strong', undefined, title ?? TITLE[kind]), el('span', undefined, message));

    const close = el('button', 'toast-close', '\u00D7');
    close.type = 'button';
    close.setAttribute('aria-label', 'Dismiss');

    const bar = el('i', 'toast-bar');
    const duration = DURATION[kind];
    bar.style.animationDuration = `${duration}ms`;

    toast.append(badge, text, close, bar);
    stack().appendChild(toast);

    let timer = 0;
    const dismiss = (): void => {
        window.clearTimeout(timer);
        toast.classList.add('leaving');
        window.setTimeout(() => toast.remove(), 220);
    };

    timer = window.setTimeout(dismiss, duration);
    close.addEventListener('click', dismiss);
    toast.addEventListener('mouseenter', () => {
        window.clearTimeout(timer);
        bar.style.animationPlayState = 'paused';
    });
    toast.addEventListener('mouseleave', () => {
        bar.style.animationPlayState = 'running';
        timer = window.setTimeout(dismiss, 1500);
    });
}

export const toastCopied = (what: string): void => showToast(`${what} copied to the clipboard.`, 'success', 'Copied');

// Routes the browser's alert() boxes through the in-app notifications.
export function useInAppAlerts(): void {
    window.alert = (message?: unknown): void => showToast(String(message ?? ''), 'error');
}
