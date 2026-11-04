export interface SubjectSummary {
    id: string;
    title: string;
    description?: string;
    fileCount: number;
    completedCount: number;
    updatedAt: string;
}

export interface LearningNode {
    name: string;
    path: string;
    isDirectory: boolean;
    kind: string;
    size: number;
    updatedAt: string;
    completed: boolean;
    children: LearningNode[];
}

export interface SubjectDetail {
    id: string;
    title: string;
    description?: string;
    readmePath?: string;
    readmeHtml?: string;
    fileCount: number;
    completedCount: number;
    files: LearningNode[];
}

export interface LearningFileContent {
    subjectId: string;
    path: string;
    name: string;
    kind: string;
    text?: string;
    html?: string;
    rawUrl?: string;
    size: number;
    updatedAt: string;
    completed: boolean;
    editable: boolean;
}

export interface SearchHit {
    subjectId: string;
    subjectTitle: string;
    path: string;
    lineNumber: number;
    line: string;
}

export interface LinkTarget {
    subjectId: string;
    path: string;
    isDirectory: boolean;
    exists: boolean;
    rawUrl?: string;
}

export interface Flashcard {
    subjectId: string;
    path: string;
    question: string;
    answer: string;
}

export interface CreateSubjectRequest {
    name: string;
    description?: string;
}

export interface SaveNoteRequest {
    subjectId: string;
    path: string;
    content: string;
    overwrite: boolean;
}

export interface ProgressRequest {
    subjectId: string;
    path: string;
    completed: boolean;
}

export interface EntryLocation {
    subjectId: string;
    path: string;
}
