import { getMathsBook } from './api.js';
import { byId, el, showError, showProgress } from './dom.js';
import { renderMarkdown } from './markdown.js';
import { MathsBook, MathsBookTopic } from './mathsTypes.js';

export const BOOK_SCOPE_KEY = 'srehub.maths.bookScope';

export function initMathsBookView(): void {
    const host = byId('book-host');
    const scope = byId<HTMLSelectElement>('book-scope');
    const onlyNotes = byId<HTMLInputElement>('book-only-notes');
    const sheets = byId<HTMLInputElement>('book-show-sheets');
    const printButton = byId<HTMLButtonElement>('book-print');
    const refresh = byId<HTMLButtonElement>('book-refresh');

    let book: MathsBook | undefined;

    const fillScope = (wanted: string): void => {
        const options: Array<[string, string]> = [['all', 'Whole book']];

        for (const part of book?.parts ?? []) {
            options.push([`part:${part.name}`, part.name]);
            for (const topic of part.topics) {
                options.push([`topic:${topic.id}`, `\u00A0\u00A0${topic.name}`]);
            }
        }

        scope.replaceChildren(...options.map(([value, label]) => {
            const option = el('option', undefined, label);
            option.value = value;
            return option;
        }));

        scope.value = options.some(([value]) => value === wanted) ? wanted : 'all';
    };

    const inScope = (partName: string, topic: MathsBookTopic): boolean => {
        const value = scope.value;
        return value === 'all'
            || value === `part:${partName}`
            || value === `topic:${topic.id}`;
    };

    const buildTopic = (topic: MathsBookTopic): HTMLElement => {
        const section = el('section', 'book-topic');
        section.id = `book-${encodeURIComponent(topic.id)}`;

        const head = el('div', 'book-topic-head');
        head.append(el('h3', undefined, topic.name), el('span', `badge ${topic.status === 'Taught' ? 'ok' : topic.status === 'Prepared' ? 'warn' : 'muted'}`, topic.status));
        section.append(head);

        if (sheets.checked && topic.materials.length > 0) {
            const list = el('ul', 'book-sheets');
            for (const material of topic.materials) {
                list.append(el('li', undefined, `${material.kind}: ${material.title}`));
            }
            section.append(el('p', 'book-sheets-label', 'Sheets'), list);
        }

        section.append(topic.notes.trim()
            ? renderMarkdown(topic.notes)
            : el('p', 'hint', 'No notes yet for this topic.'));

        return section;
    };

    const render = (): void => {
        if (!book) {
            return;
        }

        if (!book.exists) {
            showError(host, 'The maths folder was not found.');
            return;
        }

        const parts = book.parts
            .map((part) => ({
                name: part.name,
                topics: part.topics.filter((topic) => inScope(part.name, topic) && (!onlyNotes.checked || topic.notes.trim()))
            }))
            .filter((part) => part.topics.length > 0);

        if (parts.length === 0) {
            host.replaceChildren(el('p', 'hint', onlyNotes.checked
                ? 'No notes written yet. Open the Maths library, pick a topic and add your class notes; they will appear here in book order.'
                : 'Nothing to show.'));
            return;
        }

        const cover = el('header', 'book-cover');
        cover.append(
            el('h2', undefined, 'Mathematics - class notes'),
            el('p', 'muted', `Compiled ${new Date().toLocaleDateString()}`)
        );

        const contents = el('nav', 'book-contents');
        contents.append(el('h3', undefined, 'Contents'));
        const contentsList = el('ol');

        for (const part of parts) {
            const item = el('li');
            item.append(el('strong', undefined, part.name));
            const inner = el('ul');

            for (const topic of part.topics) {
                const link = el('a', undefined, topic.name);
                link.href = `#book-${encodeURIComponent(topic.id)}`;
                link.addEventListener('click', (event) => {
                    event.preventDefault();
                    document.getElementById(`book-${encodeURIComponent(topic.id)}`)?.scrollIntoView({ behavior: 'smooth' });
                });

                const row = el('li');
                row.append(link);
                inner.append(row);
            }

            item.append(inner);
            contentsList.append(item);
        }

        contents.append(contentsList);

        const body = parts.flatMap((part) => {
            const section = el('section', 'book-part');
            section.append(el('h2', 'book-part-title', part.name), ...part.topics.map(buildTopic));
            return [section];
        });

        host.replaceChildren(cover, contents, ...body);
    };

    const load = async (): Promise<void> => {
        showProgress(host, 'Compiling the book...');

        try {
            const wanted = sessionStorage.getItem(BOOK_SCOPE_KEY) || scope.value || 'all';
            sessionStorage.removeItem(BOOK_SCOPE_KEY);
            book = await getMathsBook();
            fillScope(wanted);
            render();
        } catch (failure) {
            showError(host, (failure as Error).message);
        }
    };

    scope.addEventListener('change', render);
    onlyNotes.addEventListener('change', render);
    sheets.addEventListener('change', render);
    refresh.addEventListener('click', () => void load());
    printButton.addEventListener('click', () => window.print());

    document.querySelector<HTMLButtonElement>('.nav-button[data-target="view-math-book"]')
        ?.addEventListener('click', () => void load());

    if (location.hash === '#math-book') {
        void load();
    }
}
