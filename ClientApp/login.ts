const form = document.getElementById('login-form') as HTMLFormElement;
const email = document.getElementById('login-email') as HTMLInputElement;
const password = document.getElementById('login-password') as HTMLInputElement;
const submit = document.getElementById('login-submit') as HTMLButtonElement;
const errorBox = document.getElementById('login-error') as HTMLElement;

function showError(message: string): void {
    errorBox.textContent = message;
    errorBox.className = 'error';
}

function clearError(): void {
    errorBox.textContent = '';
    errorBox.className = 'hidden';
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
            throw new Error(body.error ?? `Sign-in failed (${response.status}).`);
        }

        location.replace('/');
    } catch (error) {
        password.value = '';
        password.focus();
        showError((error as Error).message);
    } finally {
        submit.disabled = false;
        submit.textContent = 'Sign in';
    }
});

void alreadySignedIn();
