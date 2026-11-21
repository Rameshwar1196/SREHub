const SVG_NS = 'http://www.w3.org/2000/svg';

// One small outline icon per page, drawn on a 24x24 grid.
const ICONS: Record<string, string> = {
    'view-import': '<path d="m21 2-9.6 9.6"/><circle cx="7.5" cy="15.5" r="5.5"/><path d="m15.5 7.5 3 3L22 7l-3-3"/>',
    'view-vault': '<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
    'view-terminal': '<path d="m4 17 6-6-6-6"/><path d="M12 19h8"/>',
    'view-secure': '<path d="M12 3 4 6v6c0 4.4 3.3 7.8 8 9 4.7-1.2 8-4.6 8-9V6z"/><path d="m9 12 2 2 4-4"/>',
    'view-tracker': '<rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="m9 14 2 2 4-4"/>',
    'view-repositories': '<circle cx="6" cy="6" r="2.5"/><circle cx="6" cy="18" r="2.5"/><circle cx="18" cy="8" r="2.5"/><path d="M6 8.5v7"/><path d="M18 10.5c0 4-12 2-12 5"/>',
    'view-worklog': '<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/><path d="m9 16 2 2 4-4"/>',
    'view-dailyplan': '<path d="m3 6 1.5 1.5L7.5 4.5"/><path d="m3 13 1.5 1.5 3-3"/><path d="M11 6h10M11 13h10M11 20h10"/>',
    'view-duty': '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    'view-links': '<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>',
    'view-personal': '<rect x="3" y="7" width="18" height="13" rx="2"/><path d="M16 7V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2"/><circle cx="12" cy="13.5" r="1.5"/>',
    'view-math-library': '<path d="M12 7v14"/><path d="M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z"/>',
    'view-math-book': '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6M9 17h6"/>',
    'view-math-calc': '<rect x="5" y="2" width="14" height="20" rx="2"/><path d="M8 6h8"/><path d="M8 11h.01M12 11h.01M16 11h.01M8 15h.01M12 15h.01M16 15h.01M8 18.5h.01M12 18.5h.01M16 18.5h.01"/>',
    'view-math-sre': '<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>'
};

const FALLBACK = '<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="2.5"/>';

export function navIcon(key: string, className = ''): SVGElement {
    const svg = document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');

    if (className) {
        svg.setAttribute('class', className);
    }

    svg.innerHTML = ICONS[key] ?? FALLBACK;
    return svg;
}
