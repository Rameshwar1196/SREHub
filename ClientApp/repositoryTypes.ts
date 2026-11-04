export interface RepositoryInfo {
    name: string;
    path: string;
    remoteUrl?: string;
    branch?: string;
    detached: boolean;
    hasUpstream: boolean;
    ahead: number;
    behind: number;
    changes: number;
    lastFetchedAt?: string;
    error?: string;
}

export interface RepositoryList {
    root: string;
    repositories: RepositoryInfo[];
}

export interface RepositoryBranches {
    current?: string;
    branches: string[];
}

export interface CloneRepositoryRequest {
    url: string;
    name?: string;
    branch?: string;
}

export type OpenTarget = 'vscode' | 'explorer' | 'terminal';
