import { previewLicenseFile, runImport, runKeyVaultUpdate } from './api.js';
import { byId, el, showError, showProgress } from './dom.js';
import { renderImportSummary, renderKeyVaultSummary, renderPreview } from './licenseRender.js';
import { LicenseVariant, ImportMode } from './types.js';

const state = {
    importFileContent: '',
    vaultFileContent: ''
};

function splitLines(value: string): string[] {
    return value
        .split(/[\r\n,;]+/)
        .map((item) => item.trim())
        .filter((item) => item.length > 0);
}

async function readFile(input: HTMLInputElement): Promise<string> {
    const file = input.files?.[0];
    if (!file) {
        throw new Error('Select a license JSON file first.');
    }

    return file.text();
}

function setBusy(button: HTMLButtonElement, busy: boolean, label: string): void {
    button.disabled = busy;

    if (busy) {
        button.replaceChildren(el('span', 'spinner'), el('span', undefined, 'Working...'));
    } else {
        button.textContent = label;
    }
}

export function wireLicenseViews(): void {
    wireImport();
    wireKeyVault();
}

const MODE_NOTES: Record<ImportMode, string> = {
    importMissing: 'Licenses already present with a matching expiration date are left untouched.',
    reimportAll: 'Every license in the file is written again, even when it already matches.',
    validateOnly: 'No file needed. Reads every Feature License already on the instance, consumes each one, then removes the temporary server Method.',
    dryRun: 'Nothing is written. Licenses that would be imported are reported as WouldUpdate.',
    updateFeatureTree: 'No file needed. Runs the instance\'s Update Feature Tree method, then validates every Feature License.'
};

const MODE_ACTIONS: Record<ImportMode, string> = {
    importMissing: 'Run import',
    reimportAll: 'Re-import all',
    validateOnly: 'Run validation',
    dryRun: 'Preview changes',
    updateFeatureTree: 'Update feature tree'
};

function selectedImportMode(): ImportMode {
    const checked = document.querySelector<HTMLInputElement>('input[name="import-mode"]:checked');
    return (checked?.value as ImportMode) ?? 'importMissing';
}

function wireImportMode(): void {
    const validation = byId<HTMLInputElement>('import-validate');
    const fileInput = byId<HTMLInputElement>('import-file');

    const apply = (): void => {
        const mode = selectedImportMode();
        const needsFile = mode !== 'validateOnly' && mode !== 'updateFeatureTree';

        validation.disabled = !needsFile || mode === 'dryRun';
        if (mode === 'validateOnly' || mode === 'updateFeatureTree') {
            validation.checked = true;
        } else if (mode === 'dryRun') {
            validation.checked = false;
        }

        fileInput.required = needsFile;
        byId('import-source').classList.toggle('hidden', !needsFile);
        byId('import-names-field').classList.toggle('hidden', !needsFile);
        byId('import-mode-note').textContent = MODE_NOTES[mode];
        byId<HTMLButtonElement>('import-submit').textContent = MODE_ACTIONS[mode];
    };

    for (const radio of document.querySelectorAll<HTMLInputElement>('input[name="import-mode"]')) {
        radio.addEventListener('change', apply);
    }

    apply();
}

function wireImport(): void {
    const fileInput = byId<HTMLInputElement>('import-file');
    const results = byId('import-results');
    const submit = byId<HTMLButtonElement>('import-submit');

    wireImportMode();

    fileInput.addEventListener('change', async () => {
        try {
            state.importFileContent = await readFile(fileInput);
            renderPreview(results, await previewLicenseFile(state.importFileContent));
        } catch (error) {
            state.importFileContent = '';
            showError(results, (error as Error).message);
        }
    });

    byId<HTMLFormElement>('import-form').addEventListener('submit', async (event) => {
        event.preventDefault();

        const mode = selectedImportMode();
        const label = MODE_ACTIONS[mode];

        try {
            if (!state.importFileContent && mode !== 'validateOnly' && mode !== 'updateFeatureTree') {
                state.importFileContent = await readFile(fileInput);
            }

            setBusy(submit, true, label);
            showProgress(results, `${label} on ${splitLines(byId<HTMLTextAreaElement>('import-urls').value).length} instance(s). This can take a few minutes.`);

            renderImportSummary(results, await runImport({
                licenseFileContent: state.importFileContent,
                innovatorUrls: splitLines(byId<HTMLTextAreaElement>('import-urls').value),
                username: byId<HTMLInputElement>('import-username').value,
                password: byId<HTMLInputElement>('import-password').value,
                licenseVariant: byId<HTMLSelectElement>('import-variant').value as LicenseVariant,
                database: byId<HTMLInputElement>('import-database').value.trim() || undefined,
                licenseNames: splitLines(byId<HTMLInputElement>('import-names').value),
                mode,
                runValidation: byId<HTMLInputElement>('import-validate').checked,
                noPasswordHash: byId<HTMLInputElement>('import-no-hash').checked,
                skipCertificateCheck: byId<HTMLInputElement>('import-skip-cert').checked
            }));
        } catch (error) {
            showError(results, (error as Error).message);
        } finally {
            setBusy(submit, false, label);
        }
    });
}

function wireKeyVault(): void {
    const fileInput = byId<HTMLInputElement>('vault-file');
    const results = byId('vault-results');
    const submit = byId<HTMLButtonElement>('vault-submit');

    fileInput.addEventListener('change', async () => {
        try {
            state.vaultFileContent = await readFile(fileInput);
            renderPreview(results, await previewLicenseFile(state.vaultFileContent));
        } catch (error) {
            state.vaultFileContent = '';
            showError(results, (error as Error).message);
        }
    });

    byId<HTMLFormElement>('vault-form').addEventListener('submit', async (event) => {
        event.preventDefault();

        try {
            if (!state.vaultFileContent) {
                state.vaultFileContent = await readFile(fileInput);
            }

            setBusy(submit, true, 'Update Key Vault');
            showProgress(results, 'Contacting Azure Key Vault...');

            renderKeyVaultSummary(results, await runKeyVaultUpdate({
                licenseFileContent: state.vaultFileContent,
                keyVaultName: byId<HTMLInputElement>('vault-name').value.trim(),
                licenseVariant: byId<HTMLSelectElement>('vault-variant').value as LicenseVariant,
                licenseNames: splitLines(byId<HTMLInputElement>('vault-names').value),
                includeFrameworkLicenses: byId<HTMLInputElement>('vault-framework').checked,
                whatIf: byId<HTMLInputElement>('vault-whatif').checked
            }));
        } catch (error) {
            showError(results, (error as Error).message);
        } finally {
            setBusy(submit, false, 'Update Key Vault');
        }
    });
}
