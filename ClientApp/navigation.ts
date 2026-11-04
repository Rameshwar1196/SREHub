export function showView(viewId: string): void {
    document.querySelectorAll<HTMLElement>('.view').forEach((view) => {
        view.classList.toggle('active', view.id === viewId);
    });

    document.querySelectorAll<HTMLButtonElement>('.nav-button').forEach((button) => {
        button.classList.toggle('active', button.dataset.target === viewId);
    });

    const shell = document.getElementById('app-shell');
    if (shell) {
        shell.dataset.view = viewId;
    }

    const target = viewId.replace('view-', '');
    if (location.hash.replace('#', '').split('/')[0] !== target) {
        location.hash = target;
    }
}
