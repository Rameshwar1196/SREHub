export interface MathsMaterial {
    name: string;
    title: string;
    kind: string;
    kindOrder: number;
    sizeMb: number;
}

export interface MathsTopic {
    id: string;
    name: string;
    status: string;
    hasNotes: boolean;
    materials: MathsMaterial[];
}

export interface MathsPart {
    name: string;
    topics: MathsTopic[];
}

export interface MathsLibrary {
    root: string;
    exists: boolean;
    parts: MathsPart[];
    totalFiles: number;
}

export interface MathsNote {
    content: string;
    updated?: string;
}

export interface MathsBookTopic {
    id: string;
    name: string;
    status: string;
    notes: string;
    materials: MathsMaterial[];
}

export interface MathsBookPart {
    name: string;
    topics: MathsBookTopic[];
}

export interface MathsBook {
    exists: boolean;
    parts: MathsBookPart[];
}

export const MATHS_STATUSES = ['To prepare', 'Prepared', 'Taught'] as const;
