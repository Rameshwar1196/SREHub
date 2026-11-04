export interface ImportantLink {
    id: string;
    title: string;
    url: string;
    category: string;
    description: string;
    username: string;
    hasPassword: boolean;
}

export interface ImportantLinkInput {
    title: string;
    url: string;
    category: string;
    description: string;
    username: string;
    password?: string;
    clearPassword?: boolean;
}

export const LINK_GROUPS = ['Prorigo', 'Aras', 'Personal', 'Other'] as const;
