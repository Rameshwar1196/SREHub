import {
    TerminalSessionGoneError,
    closeTerminalSession,
    createTerminalSession,
    runTerminalCommand,
    stopTerminalCommand
} from './api.js';
import { askConfirm } from './dialogs.js';
import { byId, el } from './dom.js';
import { TerminalEvent, TerminalSessionInfo } from './terminalTypes.js';

const MAX_LINES = 5000;
const MAX_HISTORY = 200;
const HISTORY_KEY = 'srehub.terminal.history';

function loadHistory(): string[] {
    try {
        return JSON.parse(sessionStorage.getItem(HISTORY_KEY) ?? '[]') as string[];
    } catch {
        return [];
    }
}

function saveHistory(history: string[]): void {
    try {
        sessionStorage.setItem(HISTORY_KEY, JSON.stringify(history.slice(-MAX_HISTORY)));
    } catch {
        // History is a convenience only.
    }
}

export function initTerminalView(): void {
    const view = byId('view-terminal');
    const output = byId('terminal-output');
    const form = byId<HTMLFormElement>('terminal-form');
    const input = byId<HTMLTextAreaElement>('terminal-input');
    const prompt = byId('terminal-prompt');
    const status = byId('terminal-status');
    const shellLabel = byId('terminal-shell');
    const runButton = byId<HTMLButtonElement>('terminal-run');
    const stopButton = byId<HTMLButtonElement>('terminal-stop');
    const clearButton = byId<HTMLButtonElement>('terminal-clear');
    const restartButton = byId<HTMLButtonElement>('terminal-restart');
    const errorBox = byId('terminal-error');

    const history = loadHistory();
    let historyIndex = history.length;
    let draft = '';
    let session: TerminalSessionInfo | undefined;
    let starting: Promise<boolean> | undefined;
    let cwd = '';
    let running = false;

    const appendLine = (kind: string, text: string): void => {
        const nearBottom = output.scrollHeight - output.scrollTop - output.clientHeight < 40;
        output.appendChild(el('div', `term-line ${kind}`, text.length > 0 ? text : ' '));

        while (output.childElementCount > MAX_LINES) {
            output.firstElementChild?.remove();
        }

        if (nearBottom) {
            output.scrollTop = output.scrollHeight;
        }
    };

    const setCwd = (value: string): void => {
        cwd = value;
        prompt.textContent = `PS ${cwd}>`;
    };

    const setRunning = (value: boolean): void => {
        running = value;
        input.disabled = value;
        runButton.disabled = value;
        stopButton.disabled = !value;
        status.textContent = value ? 'Running' : 'Ready';
        status.className = `badge ${value ? 'warn' : 'ok'}`;

        if (!value) {
            input.focus();
        }
    };

    const startSession = (): Promise<boolean> => {
        starting ??= (async () => {
            errorBox.replaceChildren();
            status.textContent = 'Starting';
            status.className = 'badge muted';

            try {
                session = await createTerminalSession();
                shellLabel.textContent = session.shell;
                setCwd(session.workingDirectory);
                setRunning(false);
                return true;
            } catch (error) {
                session = undefined;
                status.textContent = 'Unavailable';
                status.className = 'badge fail';

                const message = error instanceof Error ? error.message : String(error);
                const text = message.includes('status 404')
                    ? 'The running SRE Hub does not include the terminal yet. Stop it and start it again (dotnet run -c Release), then press New session.'
                    : message;

                errorBox.replaceChildren(el('p', 'error', text));
                appendLine('err', text);
                input.disabled = false;
                runButton.disabled = false;
                return false;
            } finally {
                starting = undefined;
            }
        })();

        return starting;
    };

    const handleEvent = (event: TerminalEvent): void => {
        switch (event.type) {
            case 'out':
                appendLine('out', event.text ?? '');
                break;
            case 'err':
                appendLine('err', event.text ?? '');
                break;
            case 'notice':
                appendLine('notice', event.text ?? '');
                break;
            case 'exit':
                if (event.cwd) {
                    setCwd(event.cwd);
                }

                if (event.exitCode && event.exitCode !== 0) {
                    appendLine('exit-fail', `exit code ${event.exitCode}`);
                }
                break;
        }
    };

    const execute = async (command: string): Promise<void> => {
        if (!session && !(await startSession())) {
            input.value = command;
            resize();
            return;
        }

        appendLine('cmd', `${prompt.textContent} ${command}`);
        setRunning(true);

        try {
            await runTerminalCommand(session!.id, command, handleEvent);
        } catch (error) {
            if (error instanceof TerminalSessionGoneError) {
                appendLine('notice', 'The shell session expired. A new session was started; run the command again.');
                session = undefined;
                await startSession();
            } else {
                appendLine('err', error instanceof Error ? error.message : String(error));
            }
        } finally {
            if (session) {
                setRunning(false);
            }
        }
    };

    const submit = (): void => {
        const command = input.value.trim();
        if (!command || running) {
            return;
        }

        if (history[history.length - 1] !== command) {
            history.push(command);
            saveHistory(history);
        }

        historyIndex = history.length;
        draft = '';
        input.value = '';
        resize();
        void execute(command);
    };

    const resize = (): void => {
        input.style.height = 'auto';
        input.style.height = `${Math.min(input.scrollHeight, 220)}px`;
    };

    const recall = (index: number): void => {
        if (historyIndex === history.length) {
            draft = input.value;
        }

        historyIndex = Math.min(Math.max(index, 0), history.length);
        input.value = historyIndex === history.length ? draft : history[historyIndex];
        input.setSelectionRange(input.value.length, input.value.length);
        resize();
    };

    const restart = async (): Promise<void> => {
        const previous = session;
        session = undefined;

        if (previous) {
            await closeTerminalSession(previous.id);
        }

        output.replaceChildren();
        appendLine('notice', 'New shell session.');
        await startSession();
    };

    form.addEventListener('submit', (event) => {
        event.preventDefault();
        submit();
    });

    input.addEventListener('input', resize);

    input.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            submit();
            return;
        }

        const singleLine = !input.value.includes('\n');

        if (event.key === 'ArrowUp' && singleLine) {
            event.preventDefault();
            recall(historyIndex - 1);
        } else if (event.key === 'ArrowDown' && singleLine) {
            event.preventDefault();
            recall(historyIndex + 1);
        } else if (event.key.toLowerCase() === 'l' && event.ctrlKey) {
            event.preventDefault();
            output.replaceChildren();
        }
    });

    document.addEventListener('keydown', (event) => {
        if (event.key.toLowerCase() === 'c' && event.ctrlKey && running && !window.getSelection()?.toString()) {
            event.preventDefault();
            stopButton.click();
        }
    });

    stopButton.addEventListener('click', () => {
        if (session && running) {
            void stopTerminalCommand(session.id);
        }
    });

    clearButton.addEventListener('click', () => {
        output.replaceChildren();
        input.focus();
    });

    restartButton.addEventListener('click', () => {
        void (async () => {
            if (!running || await askConfirm('New session', 'Stop the running command and start a new shell?', 'Restart')) {
                void restart();
            }
        })();
    });

    output.addEventListener('click', () => {
        if (!window.getSelection()?.toString()) {
            input.focus();
        }
    });

    const open = (): void => {
        if (!session && !starting) {
            void startSession();
        }

        input.focus();
    };

    for (const button of document.querySelectorAll<HTMLButtonElement>('.nav-button[data-target="view-terminal"]')) {
        button.addEventListener('click', open);
    }

    if (view.classList.contains('active')) {
        open();
    }

    window.addEventListener('pagehide', () => {
        if (session) {
            void closeTerminalSession(session.id);
        }
    });

    setRunning(false);
    stopButton.disabled = true;
    status.textContent = 'Idle';
    status.className = 'badge muted';
}
