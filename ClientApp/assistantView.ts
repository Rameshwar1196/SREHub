import { AssistantPending, AssistantReply, AssistantStep, getAssistantStatus, resetAssistant, resolveAssistantAction, sendAssistantMessage } from './api.js';
import { el } from './dom.js';
import { renderMarkdown } from './markdown.js';
import { showToast, toastCopied } from './toast.js';

const SVG_NS = 'http://www.w3.org/2000/svg';
const SUGGESTIONS = [
    'What did I complete this quarter?',
    'Who is DO next week?',
    'Summarise my attendance this month',
    'Find KB articles about regenerating the SPN'
];

function icon(markup: string): SVGSVGElement {
    const svg = document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    svg.innerHTML = markup;
    return svg;
}

const SPARK = '<path d="M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z"/><path d="M19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z"/>';

export function initAssistant(): void {
    let conversationId: string | null = null;
    let busy = false;
    let configured: boolean | undefined;

    const fab = el('button', 'assistant-fab');
    fab.type = 'button';
    fab.title = 'Ask the assistant';
    fab.setAttribute('aria-label', 'Open the assistant');
    fab.appendChild(icon(SPARK));

    const panel = el('section', 'assistant-panel');
    panel.hidden = true;
    panel.setAttribute('aria-label', 'SRE assistant');

    const title = el('div', 'assistant-title');
    title.append(el('strong', undefined, 'SRE Assistant'), el('span', undefined, 'Ask, look up, or ask it to do something'));
    const fresh = el('button', 'assistant-icon-button', 'New chat');
    fresh.type = 'button';
    const close = el('button', 'assistant-icon-button', '\u00D7');
    close.type = 'button';
    close.setAttribute('aria-label', 'Close the assistant');
    const head = el('header', 'assistant-head');
    const mark = el('span', 'assistant-mark');
    mark.appendChild(icon(SPARK));
    head.append(mark, title, fresh, close);

    const log = el('div', 'assistant-log');
    log.setAttribute('role', 'log');
    log.setAttribute('aria-live', 'polite');

    const form = el('form', 'assistant-form');
    const input = el('textarea', 'assistant-input');
    input.rows = 1;
    input.maxLength = 4000;
    input.placeholder = 'Message the assistant (Enter to send, Shift+Enter for a new line)';
    const send = el('button', 'assistant-send', 'Send');
    send.type = 'submit';
    form.append(input, send);

    panel.append(head, log, form);
    document.body.append(fab, panel);

    const scroll = (): void => {
        log.scrollTop = log.scrollHeight;
    };

    const setBusy = (value: boolean): void => {
        busy = value;
        send.disabled = value;
        input.disabled = value;
        panel.classList.toggle('busy', value);
    };

    const bubble = (role: 'user' | 'assistant' | 'error', content: HTMLElement | string): HTMLElement => {
        const row = el('div', `assistant-msg ${role}`);
        const body = el('div', 'assistant-bubble');

        if (typeof content === 'string') {
            body.textContent = content;
        } else {
            body.appendChild(content);
        }

        row.appendChild(body);
        log.appendChild(row);
        scroll();
        return row;
    };

    const steps = (items: AssistantStep[]): void => {
        if (items.length === 0) {
            return;
        }

        const row = el('div', 'assistant-steps');
        items.forEach((step) => row.appendChild(el('span', `assistant-step${/^(Approved|Declined)/.test(step.summary) ? ' decision' : ''}`, step.summary)));
        log.appendChild(row);
    };

    const typing = (): HTMLElement => {
        const row = el('div', 'assistant-msg assistant');
        const body = el('div', 'assistant-bubble assistant-typing');
        body.append(el('i'), el('i'), el('i'));
        row.appendChild(body);
        log.appendChild(row);
        scroll();
        return row;
    };

    const answer = (text: string): void => {
        const row = bubble('assistant', renderMarkdown(text));
        const copy = el('button', 'assistant-copy', 'Copy');
        copy.type = 'button';
        copy.addEventListener('click', () => {
            void navigator.clipboard.writeText(text).then(() => toastCopied('The answer'));
        });
        row.querySelector('.assistant-bubble')?.appendChild(copy);
    };

    const approval = (pending: AssistantPending): void => {
        const card = el('div', 'assistant-approval');
        card.append(el('strong', undefined, 'Approve this change?'), el('p', undefined, pending.summary));

        const details = el('details');
        details.append(el('summary', undefined, 'Details'), el('pre', undefined, pending.details));
        card.appendChild(details);

        const approve = el('button', 'assistant-approve', 'Approve');
        const decline = el('button', 'assistant-decline', 'Decline');
        approve.type = 'button';
        decline.type = 'button';

        const decide = async (value: boolean): Promise<void> => {
            approve.disabled = true;
            decline.disabled = true;
            card.classList.add('resolved');
            setBusy(true);
            const wait = typing();

            try {
                show(await resolveAssistantAction(conversationId!, value));
                showToast(value ? 'The change was made.' : 'The change was not made.', value ? 'success' : 'info', value ? 'Approved' : 'Declined');
            } catch (failure) {
                bubble('error', (failure as Error).message);
            } finally {
                wait.remove();
                setBusy(false);
                input.focus();
            }
        };

        approve.addEventListener('click', () => void decide(true));
        decline.addEventListener('click', () => void decide(false));
        card.append(el('div', 'assistant-approval-actions'));
        card.lastElementChild?.append(approve, decline);
        log.appendChild(card);
        scroll();
    };

    const show = (reply: AssistantReply): void => {
        conversationId = reply.conversationId;
        steps(reply.steps);

        if (reply.reply) {
            answer(reply.reply);
        }

        if (reply.pending) {
            approval(reply.pending);
        }

        scroll();
    };

    const welcome = (): void => {
        log.replaceChildren();
        const intro = el('div', 'assistant-welcome');
        intro.append(el('strong', undefined, 'How can I help?'), el('p', undefined, 'I can search your learning notes and KB, read your tracker, work log and DO roster, and add entries when you approve them.'));
        const chips = el('div', 'assistant-chips');

        for (const text of SUGGESTIONS) {
            const chip = el('button', 'assistant-chip', text);
            chip.type = 'button';
            chip.addEventListener('click', () => void submit(text));
            chips.appendChild(chip);
        }

        intro.appendChild(chips);
        log.appendChild(intro);
    };

    const notConfigured = (): void => {
        log.replaceChildren();
        const card = el('div', 'assistant-welcome');
        card.append(
            el('strong', undefined, 'The assistant needs an AI service'),
            el('p', undefined, 'Create appsettings.Local.json next to appsettings.json, then restart SRE Hub. The file is excluded from git and the key is never sent to the page.'));
        card.appendChild(el('pre', 'assistant-setup', [
            '{',
            '  "Ai": {',
            '    "Provider": "AzureOpenAI",',
            '    "Endpoint": "https://<resource>.openai.azure.com",',
            '    "Model": "<deployment name>",',
            '    "ApiKey": "<key>"',
            '  }',
            '}'
        ].join('\n')));
        card.appendChild(el('p', 'assistant-hint', 'For OpenAI or a local Ollama model, also set Ai:Provider to OpenAI and Ai:Endpoint to the /v1 address.'));
        log.appendChild(card);
    };

    async function submit(text: string): Promise<void> {
        const message = text.trim();

        if (!message || busy) {
            return;
        }

        if (configured === false) {
            input.value = '';
            notConfigured();
            return;
        }

        if (!log.querySelector('.assistant-msg')) {
            log.replaceChildren();
        }

        bubble('user', message);
        input.value = '';
        input.style.height = 'auto';
        setBusy(true);
        const wait = typing();

        try {
            show(await sendAssistantMessage(conversationId, message));
        } catch (failure) {
            bubble('error', (failure as Error).message);
        } finally {
            wait.remove();
            setBusy(false);
            input.focus();
        }
    }

    const open = async (): Promise<void> => {
        panel.hidden = false;
        fab.classList.add('hidden');

        if (configured === undefined) {
            try {
                configured = (await getAssistantStatus()).configured;
            } catch {
                configured = false;
            }

            configured ? welcome() : notConfigured();
        }

        input.focus();
    };

    const shut = (): void => {
        panel.hidden = true;
        fab.classList.remove('hidden');
        fab.focus();
    };

    fab.addEventListener('click', () => void open());
    close.addEventListener('click', shut);
    panel.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            shut();
        }
    });

    fresh.addEventListener('click', () => {
        void resetAssistant(conversationId).catch(() => undefined);
        conversationId = null;
        configured ? welcome() : notConfigured();
        input.focus();
    });

    form.addEventListener('submit', (event) => {
        event.preventDefault();
        void submit(input.value);
    });

    input.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
            event.preventDefault();
            void submit(input.value);
        }
    });

    input.addEventListener('input', () => {
        input.style.height = 'auto';
        input.style.height = `${Math.min(input.scrollHeight, 140)}px`;
    });
}
