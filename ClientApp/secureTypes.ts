export interface SecureFolder {
    id: string;
    name: string;
    parentId?: string | null;
    deletedAt?: string | null;
}

export interface SecureFileVersion {
    number: number;
    size: number;
    uploadedAt: string;
    uploadedBy: string;
}

export interface SecureFile {
    id: string;
    folderId?: string | null;
    name: string;
    type: string;
    infrastructure: string;
    environment: string;
    expiry: string;
    notes: string;
    size: number;
    updatedAt: string;
    updatedBy: string;
    deletedAt?: string | null;
    versions: SecureFileVersion[];
}

export interface SecureTree {
    folders: SecureFolder[];
    files: SecureFile[];
}

export interface SecureTrash extends SecureTree {
    retentionDays: number;
}

export interface SecureFileMeta {
    name?: string;
    folderId?: string | null;
    type: string;
    infrastructure: string;
    environment: string;
    expiry: string;
    notes: string;
}

export interface SecureAuditEntry {
    at: string;
    user: string;
    action: string;
    target: string;
}
