const VIEW_SHOWN = 'srehub:viewshown';

export function onViewShown(viewId: string, handler: () => void): void {
    document.addEventListener(VIEW_SHOWN, (event) => {
        if ((event as CustomEvent<string>).detail === viewId) {
            handler();
        }
    });
}

export function onAnyViewShown(handler: (viewId: string) => void): void {
    document.addEventListener(VIEW_SHOWN, (event) => handler((event as CustomEvent<string>).detail));
}

export function showView(viewId: string): void {
    document.querySelectorAll<HTMLElement>('.view').forEach((view) => {
        view.classList.toggle('active', view.id === viewId);
    });

    // A grouped nav button stays active for every tab in its group and reopens the tab last used.
    document.querySelectorAll<HTMLButtonElement>('.nav-button').forEach((button) => {
        const views = button.dataset.views?.split(' ') ?? [button.dataset.target];
        const member = views.includes(viewId);
        button.classList.toggle('active', member);

        if (member && button.dataset.views) {
            button.dataset.target = viewId;
        }
    });

    document.querySelectorAll<HTMLButtonElement>('.view-tab').forEach((tab) => {
        const active = tab.dataset.view === viewId;
        tab.classList.toggle('active', active);
        tab.setAttribute('aria-selected', String(active));
    });

    const shell = document.getElementById('app-shell');
    if (shell) {
        shell.dataset.view = viewId;
    }

    const target = viewId.replace('view-', '');
    if (location.hash.replace('#', '').split('/')[0] !== target) {
        location.hash = target;
    }

    document.dispatchEvent(new CustomEvent(VIEW_SHOWN, { detail: viewId }));
}
