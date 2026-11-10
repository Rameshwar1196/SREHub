import { el } from './dom.js';

const INLINE = /(`[^`]+`|\*\*[^*]+\*\*|\*[^*\s][^*]*\*|https?:\/\/[^\s)>\]]+)/g;
const HEADING = /^\s{0,3}#{1,6}(\s|$)/;
const RULE = /^\s*(-{3,}|={3,}|\*{3,}|_{3,})\s*$/;
const MARKER = /^(\s*)((?:[-*+]|\d+[.)])\s|>\s?)/;

const escapeHtml = (text: string): string => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const span = (className: string, text: string): string => `<span class="${className}">${escapeHtml(text)}</span>`;

function inline(text: string): string {
    return text.split(INLINE).map((piece) => {
        if (!piece) {
            return '';
        }

        if (piece.startsWith('`')) {
            return span('md-hl-code', piece);
        }

        if (piece.startsWith('**')) {
            return span('md-hl-bold', piece);
        }

        if (piece.startsWith('*')) {
            return span('md-hl-italic', piece);
        }

        return /^https?:/.test(piece) ? span('md-hl-link', piece) : escapeHtml(piece);
    }).join('');
}

// Returns safe HTML: every piece of the source is escaped before it is wrapped in a span.
export function highlightMarkdown(source: string): string {
    let fenced = false;

    return source.replace(/\r\n/g, '\n').split('\n').map((line) => {
        if (/^\s*```/.test(line)) {
            fenced = !fenced;
            return span('md-hl-code', line);
        }

        if (fenced) {
            return span('md-hl-code', line);
        }

        if (HEADING.test(line)) {
            return span('md-hl-heading', line);
        }

        if (RULE.test(line)) {
            return span('md-hl-rule', line);
        }

        const marker = MARKER.exec(line);
        return marker
            ? `${escapeHtml(marker[1])}${span('md-hl-marker', marker[2])}${inline(line.slice(marker[0].length))}`
            : inline(line);
    }).join('\n');
}

// Colours the text under a transparent textarea so what is typed shows with Markdown highlighting.
export function markdownSurface(editor: HTMLTextAreaElement): { element: HTMLElement; refresh: () => void } {
    const backdrop = el('pre', 'md-backdrop');
    backdrop.setAttribute('aria-hidden', 'true');
    const code = el('code');
    backdrop.appendChild(code);

    const wrapper = el('div', 'md-editor');
    editor.classList.add('md-input');
    wrapper.append(backdrop, editor);

    const refresh = (): void => {
        code.innerHTML = `${highlightMarkdown(editor.value)}\n`;
        backdrop.scrollTop = editor.scrollTop;
        backdrop.scrollLeft = editor.scrollLeft;
    };

    editor.addEventListener('scroll', () => {
        backdrop.scrollTop = editor.scrollTop;
        backdrop.scrollLeft = editor.scrollLeft;
    });
    editor.addEventListener('input', refresh);
    refresh();

    return { element: wrapper, refresh };
}
