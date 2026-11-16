import { el } from './dom.js';
import { showView } from './navigation.js';

const LIBRARY_TABS = [
    { view: 'view-links', label: 'Important links' },
    { view: 'view-files', label: 'Important files' }
];

export function initViewTabs(): void {
    for (const member of LIBRARY_TABS) {
        const page = document.querySelector<HTMLElement>(`#${member.view} > .page`);

        if (!page) {
            continue;
        }

        const bar = el('div', 'view-tabs');
        bar.setAttribute('role', 'tablist');
        bar.setAttribute('aria-label', 'Library');

        for (const tab of LIBRARY_TABS) {
            const button = el('button', 'view-tab', tab.label);
            button.type = 'button';
            button.dataset.view = tab.view;
            button.setAttribute('role', 'tab');
            button.addEventListener('click', () => showView(tab.view));
            bar.appendChild(button);
        }

        page.prepend(bar);
    }
}
