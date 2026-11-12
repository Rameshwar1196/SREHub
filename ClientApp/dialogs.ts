import { getSubject } from './api.js';
import { byId, el } from './dom.js';
import { markdownSurface } from './mdHighlight.js';
import { LearningNode, SubjectSummary } from './learningTypes.js';

type Reader<T> = () => T;

function openDialog<T>(
    title: string,
    hint: string,
    confirmLabel: string,
    build: (body: HTMLElement) => Reader<T>
): Promise<T | null> {
    const dialog = byId<HTMLDialogElement>('action-dialog');
    const form = byId<HTMLFormElement>('action-form');
    const fields = byId('action-fields');
    const error = byId('action-error');
    const confirm = byId<HTMLButtonElement>('action-confirm');

    byId('action-title').textContent = title;
    byId('action-hint').textContent = hint;
    byId('action-hint').classList.toggle('hidden', hint.length === 0);
    confirm.textContent = confirmLabel;
    dialog.classList.remove('wide');
    error.classList.add('hidden');
    fields.replaceChildren();

    const read = build(fields);

    return new Promise<T | null>((resolve) => {
        const cleanup = (): void => {
            form.removeEventListener('submit', onSubmit);
            byId('action-cancel').removeEventListener('click', onCancel);
            dialog.removeEventListener('cancel', onCancel);
        };

        const onSubmit = (event: Event): void => {
            event.preventDefault();

            try {
                const value = read();
                cleanup();
                dialog.close();
                resolve(value);
            } catch (failure) {
                error.textContent = (failure as Error).message;
                error.classList.remove('hidden');
            }
        };

        const onCancel = (): void => {
            cleanup();
            dialog.close();
            resolve(null);
        };

        form.addEventListener('submit', onSubmit);
        byId('action-cancel').addEventListener('click', onCancel);
        dialog.addEventListener('cancel', onCancel);

        dialog.showModal();
        fields.querySelector<HTMLElement>('input, select')?.focus();
    });
}

function field(label: string, control: HTMLElement, action?: HTMLElement): HTMLElement {
    const wrapper = el('div', 'field');

    if (action) {
        const head = el('div', 'field-head');
        head.append(el('label', undefined, label), action);
        wrapper.append(head, control);
    } else {
        wrapper.append(el('label', undefined, label), control);
    }

    return wrapper;
}

function copyAction(source: HTMLTextAreaElement): HTMLButtonElement {
    const button = el('button', 'secure-copy-button');
    button.type = 'button';
    button.title = 'Copy text';
    button.setAttribute('aria-label', 'Copy text');
    button.append(el('i', 'secure-copy-icon'));
    let timer = 0;
    button.addEventListener('click', () => {
        void navigator.clipboard.writeText(source.value).then(() => {
            button.classList.add('copied');
            button.title = 'Copied';
            window.clearTimeout(timer);
            timer = window.setTimeout(() => {
                button.classList.remove('copied');
                button.title = 'Copy text';
            }, 1500);
        });
    });
    return button;
}

export interface FormField {
    name: string;
    label: string;
    kind: 'text' | 'date' | 'select' | 'textarea';
    value: string;
    options?: Array<{ value: string; label: string }>;
    // Rebuilds this select whenever the field it depends on changes.
    dependsOn?: string;
    optionsFor?: (parentValue: string) => Array<{ value: string; label: string }>;
    required?: boolean;
    maxLength?: number;
    rows?: number;
    highlight?: 'markdown';
    // Shows a small copy button beside the label of a textarea.
    copy?: boolean;
    // Keeps the value exactly as typed instead of trimming it.
    raw?: boolean;
}

// Several labelled fields in one dialog; resolves to the trimmed values by field name, or null when cancelled.
export function askForm(
    title: string,
    hint: string,
    confirmLabel: string,
    fields: FormField[]
): Promise<Record<string, string> | null> {
    return openDialog(title, hint, confirmLabel, (body) => {
        const controls = new Map<string, HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>();
        byId('action-dialog').classList.toggle('wide', fields.some((item) => item.kind === 'textarea' && (item.rows ?? 0) > 6));

        for (const item of fields) {
            let control: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;

            if (item.kind === 'select') {
                const select = el('select');

                for (const choice of item.options ?? []) {
                    const option = el('option', undefined, choice.label);
                    option.value = choice.value;
                    option.selected = choice.value === item.value;
                    select.appendChild(option);
                }

                control = select;
            } else if (item.kind === 'textarea') {
                const area = el('textarea');
                area.rows = item.rows ?? 4;
                area.value = item.value;
                control = area;
            } else {
                const input = el('input');
                input.type = item.kind;
                input.value = item.value;
                control = input;
            }

            if (item.maxLength && !(control instanceof HTMLSelectElement)) {
                control.maxLength = item.maxLength;
            }

            controls.set(item.name, control);
            const shown = item.highlight === 'markdown' && control instanceof HTMLTextAreaElement
                ? markdownSurface(control).element
                : control;
            body.appendChild(field(item.label, shown, item.copy && control instanceof HTMLTextAreaElement ? copyAction(control) : undefined));
        }

        for (const item of fields) {
            const parent = item.dependsOn ? controls.get(item.dependsOn) : undefined;
            const child = controls.get(item.name);

            if (parent && item.optionsFor && child instanceof HTMLSelectElement) {
                const rebuild = (): void => {
                    const keep = child.value;
                    const choices = item.optionsFor!(parent.value);
                    child.replaceChildren(...choices.map((choice) => Object.assign(el('option', undefined, choice.label), { value: choice.value })));
                    child.value = choices.some((choice) => choice.value === keep) ? keep : (choices[0]?.value ?? '');
                };

                parent.addEventListener('change', rebuild);
                rebuild();
            }
        }

        return () => {
            const values: Record<string, string> = {};

            for (const item of fields) {
                const rawValue = controls.get(item.name)!.value;
                const value = item.raw ? rawValue : rawValue.trim();

                if (item.required && value.length === 0) {
                    throw new Error(`${item.label} is required.`);
                }

                values[item.name] = value;
            }

            return values;
        };
    });
}

// Cancel is the first button, so it has the focus when the dialog opens.
export async function askConfirm(title: string, message: string, confirmLabel: string): Promise<boolean> {
    return (await openDialog(title, message, confirmLabel, () => () => true)) === true;
}

export function askText(
    title: string,
    label: string,
    value: string,
    confirmLabel: string,
    hint = ''
): Promise<string | null> {
    return openDialog(title, hint, confirmLabel, (body) => {
        const input = el('input');
        input.type = 'text';
        input.value = value;
        body.appendChild(field(label, input));

        return () => {
            const trimmed = input.value.trim();
            if (trimmed.length === 0) {
                throw new Error('A value is required.');
            }

            return trimmed;
        };
    });
}

export interface MoveChoice {
    subjectId: string;
    folder: string;
}

function collectFolders(nodes: LearningNode[], into: string[] = []): string[] {
    for (const node of nodes) {
        if (node.isDirectory) {
            into.push(node.path);
            collectFolders(node.children, into);
        }
    }

    return into;
}

export function askMove(
    title: string,
    subjects: SubjectSummary[],
    currentSubjectId: string,
    currentFolder: string
): Promise<MoveChoice | null> {
    return openDialog(
        title,
        'Pick the destination subject and folder. Leave the folder empty to place it at the top of the subject.',
        'Move',
        (body) => {
            const select = el('select');
            for (const subject of subjects) {
                const option = el('option', undefined, subject.title);
                option.value = subject.id;
                option.selected = subject.id === currentSubjectId;
                select.appendChild(option);
            }

            const folder = el('input');
            folder.type = 'text';
            folder.value = currentFolder;
            folder.placeholder = 'e.g. notes/archive';
            folder.setAttribute('list', 'move-folders');

            const suggestions = el('datalist');
            suggestions.id = 'move-folders';

            const loadSuggestions = async (): Promise<void> => {
                try {
                    const detail = await getSubject(select.value);
                    suggestions.replaceChildren(...collectFolders(detail.files).map((path) => {
                        const option = el('option');
                        option.value = path;
                        return option;
                    }));
                } catch {
                    suggestions.replaceChildren();
                }
            };

            select.addEventListener('change', () => {
                folder.value = '';
                void loadSuggestions();
            });

            body.append(field('Destination subject', select), field('Destination folder', folder), suggestions);
            void loadSuggestions();

            return () => ({ subjectId: select.value, folder: folder.value.trim() });
        });
}
