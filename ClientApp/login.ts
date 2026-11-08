const form = document.getElementById('login-form') as HTMLFormElement;
const email = document.getElementById('login-email') as HTMLInputElement;
const password = document.getElementById('login-password') as HTMLInputElement;
const submit = document.getElementById('login-submit') as HTMLButtonElement;
const errorBox = document.getElementById('login-error') as HTMLElement;

const SVG_NS = 'http://www.w3.org/2000/svg';
const WELCOME_MILLISECONDS = 2600;
const CONFETTI_COLOURS = ['#f472b6', '#a78bfa', '#38bdf8', '#34d399', '#fbbf24'];

function icon(markup: string): SVGSVGElement {
    const svg = document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    svg.innerHTML = markup;
    return svg;
}

function showError(message: string, friendly = false): void {
    errorBox.replaceChildren();
    errorBox.className = friendly ? 'error login-oops' : 'error';

    if (!friendly) {
        errorBox.textContent = message;
        return;
    }

    const art = document.createElement('span');
    art.className = 'login-oops-art';
    art.append(icon('<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/><path d="M12 14.5v2"/>'));

    const title = document.createElement('strong');
    title.textContent = 'Oops, you missed it!';
    const text = document.createElement('span');
    text.textContent = message;

    const copy = document.createElement('span');
    copy.className = 'login-oops-copy';
    copy.append(title, text);
    errorBox.append(art, copy);

    // Restart the shake each time, even for repeated failures.
    form.classList.remove('shake');
    void form.offsetWidth;
    form.classList.add('shake');
}

function clearError(): void {
    errorBox.textContent = '';
    errorBox.className = 'hidden';
    form.classList.remove('shake');
}

async function showWelcome(name: string): Promise<void> {
    const overlay = document.createElement('div');
    overlay.className = 'welcome-overlay';
    overlay.setAttribute('role', 'status');

    for (let index = 0; index < 28; index++) {
        const piece = document.createElement('i');
        piece.className = 'confetti';
        piece.style.left = `${Math.round(Math.random() * 100)}%`;
        piece.style.background = CONFETTI_COLOURS[index % CONFETTI_COLOURS.length];
        piece.style.animationDelay = `${(Math.random() * 0.8).toFixed(2)}s`;
        piece.style.animationDuration = `${(1.6 + Math.random() * 1.2).toFixed(2)}s`;
        overlay.appendChild(piece);
    }

    const badge = document.createElement('span');
    badge.className = 'welcome-badge';
    badge.append(icon('<path d="M12 3 4 7v5.5c0 4.4 3.3 7.8 8 8.5 4.7-.7 8-4.1 8-8.5V7z"/><path class="welcome-tick" d="m9 12 2.2 2.2L15.5 10"/>'));

    const heading = document.createElement('h2');
    heading.textContent = name ? `Welcome back, ${name.split(/\s+/)[0]}!` : 'Welcome back!';
    const thanks = document.createElement('p');
    thanks.textContent = 'Thank you for logging in to SRE Hub.';
    const sub = document.createElement('p');
    sub.className = 'welcome-sub';
    sub.textContent = 'Getting your workspace ready...';

    const card = document.createElement('div');
    card.className = 'welcome-card';
    card.append(badge, heading, thanks, sub);
    overlay.appendChild(card);
    document.body.appendChild(overlay);

    await new Promise((resolve) => window.setTimeout(resolve, WELCOME_MILLISECONDS));
}

async function alreadySignedIn(): Promise<void> {
    const response = await fetch('/api/auth/me');
    if (response.ok) {
        location.replace('/');
    }
}

form.addEventListener('submit', async (event) => {
    event.preventDefault();
    clearError();

    submit.disabled = true;
    submit.textContent = 'Signing in...';

    try {
        const response = await fetch('/api/auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email: email.value.trim(), password: password.value })
        });

        const text = await response.text();
        const body = text ? JSON.parse(text) : {};

        if (!response.ok) {
            const failure = new Error(body.error ?? `Sign-in failed (${response.status}).`);
            failure.name = response.status === 401 ? 'Mismatch' : 'Error';
            throw failure;
        }

        submit.textContent = 'Welcome!';
        await showWelcome(body.displayName ?? '');
        location.replace('/');
    } catch (error) {
        password.value = '';
        password.focus();
        showError((error as Error).message, (error as Error).name === 'Mismatch');
    } finally {
        submit.disabled = false;
        submit.textContent = 'Sign in';
    }
});

void alreadySignedIn();
