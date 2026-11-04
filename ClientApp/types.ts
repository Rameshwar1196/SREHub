export type LicenseVariant = 'auto' | 'key_value_azure' | 'key_value_build_ci';

export type ImportMode = 'importMissing' | 'reimportAll' | 'validateOnly' | 'dryRun' | 'updateFeatureTree';

export interface ImportRequest {
    licenseFileContent: string;
    innovatorUrls: string[];
    username: string;
    password: string;
    licenseVariant: LicenseVariant;
    database?: string;
    licenseNames?: string[];
    mode: ImportMode;
    runValidation: boolean;
    noPasswordHash: boolean;
    skipCertificateCheck: boolean;
}

export interface LicenseResult {
    keyName: string;
    variant: string;
    importStatus: string;
    validationStatus: string;
    feature?: string;
    expirationDate?: string;
    status: string;
    error?: string;
}

export interface ValidationCheck {
    feature: string;
    status: string;
    message?: string;
}

export interface InstanceResult {
    instanceUrl: string;
    database?: string;
    variant?: string;
    loginStatus: string;
    status: string;
    licenses: LicenseResult[];
    featureTreeUpdate: string;
    validationMethod: string;
    validationCleanup: string;
    validationErrors: number;
    validationResults: ValidationCheck[];
    validationOutput?: string;
    error?: string;
}

export interface ImportSummary {
    variant: string;
    mode: string;
    startedAt: string;
    durationSeconds: number;
    totalInstances: number;
    failedInstances: number;
    totalLicenseTasks: number;
    successfulLicenses: number;
    skippedLicenses: number;
    failedLicenses: number;
    checksPassed: number;
    checksFailed: number;
    executionStatus: string;
    instances: InstanceResult[];
}

export interface KeyVaultRequest {
    licenseFileContent: string;
    keyVaultName: string;
    licenseVariant: LicenseVariant;
    licenseNames?: string[];
    includeFrameworkLicenses: boolean;
    whatIf: boolean;
}

export interface SecretResult {
    keyName: string;
    secretName: string;
    source: string;
    status: string;
    expirationDate?: string;
    error?: string;
}

export interface KeyVaultSummary {
    keyVaultName: string;
    vaultUri: string;
    variant: string;
    whatIf: boolean;
    startedAt: string;
    durationSeconds: number;
    totalSecrets: number;
    updated: number;
    unchanged: number;
    failed: number;
    executionStatus: string;
    secrets: SecretResult[];
}

export interface LicensePreview {
    licenses: Array<{
        keyName: string;
        expirationDate?: string;
        hasAzure: boolean;
        hasBuildCi: boolean;
    }>;
    frameworkLicenses: string[];
}
