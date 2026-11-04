import { el } from './dom.js';

const INLINE = /(`[^`]+`|\*\*[^*]+\*\*|\*[^*\s][^*]*\*)/g;

function inline(parent: HTMLElement, text: string): void {
    for (const piece of text.split(INLINE)) {
        if (!piece) {
            continue;
        }

        if (piece.startsWith('`') && piece.endsWith('`') && piece.length > 2) {
            parent.append(el('code', undefined, piece.slice(1, -1)));
        } else if (piece.startsWith('**') && piece.endsWith('**') && piece.length > 4) {
            parent.append(el('strong', undefined, piece.slice(2, -2)));
        } else if (piece.startsWith('*') && piece.endsWith('*') && piece.length > 2) {
            parent.append(el('em', undefined, piece.slice(1, -1)));
        } else {
            parent.append(document.createTextNode(piece));
        }
    }
}

// Small, DOM-based renderer: no HTML from the note is ever interpreted.
export function renderMarkdown(source: string): HTMLElement {
    const root = el('div', 'md');
    const lines = source.replace(/\r\n/g, '\n').split('\n');

    let paragraph: string[] = [];
    let list: HTMLElement | undefined;
    let listKind = '';
    let code: string[] | undefined;

    const flushParagraph = (): void => {
        if (paragraph.length > 0) {
            const block = el('p');
            inline(block, paragraph.join(' '));
            root.append(block);
            paragraph = [];
        }
    };

    const flushList = (): void => {
        list = undefined;
        listKind = '';
    };

    for (const raw of lines) {
        const line = raw.trimEnd();

        if (code) {
            if (line.trim().startsWith('```')) {
                root.append(el('pre', undefined, code.join('\n')));
                code = undefined;
            } else {
                code.push(raw);
            }
            continue;
        }

        if (line.trim().startsWith('```')) {
            flushParagraph();
            flushList();
            code = [];
            continue;
        }

        const heading = /^(#{1,4})\s+(.*)$/.exec(line);
        const bullet = /^\s*[-*+]\s+(.*)$/.exec(line);
        const numbered = /^\s*\d+[.)]\s+(.*)$/.exec(line);
        const quote = /^>\s?(.*)$/.exec(line);

        if (!line.trim()) {
            flushParagraph();
            flushList();
        } else if (/^(-{3,}|\*{3,})$/.test(line.trim())) {
            flushParagraph();
            flushList();
            root.append(el('hr'));
        } else if (heading) {
            flushParagraph();
            flushList();
            const block = el(`h${Math.min(heading[1].length + 1, 5)}` as 'h2' | 'h3' | 'h4' | 'h5');
            inline(block, heading[2]);
            root.append(block);
        } else if (bullet || numbered) {
            flushParagraph();
            const kind = bullet ? 'ul' : 'ol';

            if (!list || listKind !== kind) {
                list = el(kind);
                listKind = kind;
                root.append(list);
            }

            const item = el('li');
            const text = (bullet ?? numbered)![1];
            const task = /^\[( |x|X)\]\s+(.*)$/.exec(text);

            if (task) {
                const box = el('input');
                box.type = 'checkbox';
                box.disabled = true;
                box.checked = task[1].toLowerCase() === 'x';
                item.className = 'md-task';
                item.append(box, document.createTextNode(' '));
                inline(item, task[2]);
            } else {
                inline(item, text);
            }

            list.append(item);
        } else if (quote) {
            flushParagraph();
            flushList();
            const block = el('blockquote');
            inline(block, quote[1]);
            root.append(block);
        } else {
            flushList();
            paragraph.push(line.trim());
        }
    }

    if (code) {
        root.append(el('pre', undefined, code.join('\n')));
    }

    flushParagraph();
    return root;
}
