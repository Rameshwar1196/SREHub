const WORK_TAB = 'srehub-work';
const SIDE_WINDOW = 'srehub-side';
const SIDE_WIDTH = 560;

// Opening an empty named window first lets us cut its link back to SreHub before the other site loads, while later clicks still reuse the same window.
function openNamed(url: string, name: string, features?: string): void {
    const target = window.open('', name, features);

    if (!target) {
        window.open(url, '_blank', 'noopener,noreferrer');
        return;
    }

    try {
        target.opener = null;
    } catch {
        // An already open window belongs to another site and was cut off when it was first opened.
    }

    target.location.href = url;
    target.focus();
}

export function openInWorkTab(url: string): void {
    openNamed(url, WORK_TAB);
}

// A narrow window docked to the right edge of the screen, to keep next to SreHub.
export function openInSideWindow(url: string): void {
    const height = Math.max(window.screen.availHeight - 40, 480);
    const left = Math.max(window.screen.availWidth - SIDE_WIDTH, 0);
    openNamed(url, SIDE_WINDOW, `popup=yes,width=${SIDE_WIDTH},height=${height},left=${left},top=0`);
}
