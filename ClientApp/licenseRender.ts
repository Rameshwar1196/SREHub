import { ImportSummary, KeyVaultSummary, LicensePreview } from './types.js';
import { badge, el, section, showError, stats, table } from './dom.js';

export { showError as renderError };

export function renderPreview(container: HTMLElement, preview: LicensePreview): void {
    const rows = preview.licenses.map((license) => [
        license.keyName,
        license.expirationDate?.substring(0, 10) ?? '-',
        license.hasAzure ? 'yes' : 'no',
        license.hasBuildCi ? 'yes' : 'no'
    ]);

    container.replaceChildren(section(
        `License file: ${preview.licenses.length} license(s), ${preview.frameworkLicenses.length} framework entry(ies)`,
        table(['Key name', 'Expires', 'key_value_azure', 'key_value_build_ci'], rows)
    ));
}

const MODE_LABELS: Record<string, string> = {
    importMissing: 'Import missing only',
    reimportAll: 'Re-import all',
    validateOnly: 'Validate only',
    dryRun: 'Dry run (no writes)',
    updateFeatureTree: 'Update feature tree + validate'
};

export function renderImportSummary(container: HTMLElement, summary: ImportSummary): void {
    container.replaceChildren();

    const header = el('div', 'summary-header');
    header.append(badge(summary.executionStatus), stats([
        ['Mode', MODE_LABELS[summary.mode] ?? summary.mode],
        ['Variant', summary.variant],
        ['Instances', `${summary.totalInstances} total, ${summary.totalInstances - summary.failedInstances} succeeded, ${summary.failedInstances} failed`],
        ['Licenses', `${summary.totalLicenseTasks} total, ${summary.successfulLicenses} imported, ${summary.skippedLicenses} already present, ${summary.failedLicenses} failed`],
        ['Validation', `${summary.checksPassed} passed, ${summary.checksFailed} failed`],
        ['Duration', `${summary.durationSeconds}s`]
    ]));
    container.appendChild(header);

    for (const instance of summary.instances) {
        const children: HTMLElement[] = [
            stats([
                ['Status', instance.status],
                ['Database', instance.database ?? '-'],
                ['Login', instance.loginStatus],
                ['Variant', instance.variant ?? '-'],
                ['Feature tree', instance.featureTreeUpdate],
                ['Validation method', instance.validationMethod],
                ['Method cleanup', instance.validationCleanup]
            ])
        ];

        if (instance.error) {
            children.push(el('p', 'error', instance.error));
        }

        if (instance.licenses.length > 0) {
            children.push(table(
                ['Key name', 'Feature', 'Expires', 'Import', 'Status', 'Message'],
                instance.licenses.map((license) => [
                    license.keyName,
                    license.feature ?? '-',
                    license.expirationDate?.substring(0, 10) ?? '-',
                    license.importStatus,
                    badge(license.status),
                    license.error ?? ''
                ])
            ));
        }

        if (instance.validationResults.length > 0) {
            children.push(table(
                ['Feature', 'Consume check', 'Message'],
                instance.validationResults.map((check) => [check.feature, badge(check.status), check.message ?? ''])
            ));
        } else if (instance.validationMethod === 'Failed' && instance.validationOutput) {
            children.push(el('pre', undefined, instance.validationOutput));
        }

        container.appendChild(section(instance.instanceUrl, ...children));
    }
}

export function renderKeyVaultSummary(container: HTMLElement, summary: KeyVaultSummary): void {
    container.replaceChildren();

    const header = el('div', 'summary-header');
    header.append(badge(summary.executionStatus), stats([
        ['Key Vault', summary.keyVaultName],
        ['Vault URI', summary.vaultUri],
        ['Variant', summary.variant],
        ['Mode', summary.whatIf ? 'Preview (no writes)' : 'Apply'],
        ['Secrets', `${summary.totalSecrets} total, ${summary.updated} written, ${summary.unchanged} unchanged, ${summary.failed} failed`],
        ['Duration', `${summary.durationSeconds}s`]
    ]));
    container.appendChild(header);

    container.appendChild(section(
        'Secrets',
        table(
            ['Key name', 'Secret name', 'Source', 'Expires', 'Status', 'Message'],
            summary.secrets.map((secret) => [
                secret.keyName,
                secret.secretName,
                secret.source,
                secret.expirationDate?.substring(0, 10) ?? '-',
                badge(secret.status),
                secret.error ?? ''
            ])
        )
    ));
}
