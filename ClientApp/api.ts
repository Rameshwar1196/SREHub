import { ImportRequest, ImportSummary, KeyVaultRequest, KeyVaultSummary, LicensePreview } from './types.js';
import {
    CloneRepositoryRequest,
    OpenTarget,
    RepositoryBranches,
    RepositoryInfo,
    RepositoryList
} from './repositoryTypes.js';
import { ImportantLink, ImportantLinkInput } from './linkTypes.js';
import { TerminalEvent, TerminalSessionInfo } from './terminalTypes.js';
import { MathsBook, MathsLibrary, MathsNote } from './mathsTypes.js';
import { WorkLogEntry, WorkLogYear } from './workLogTypes.js';
import { DutyEntry, DutyRoster, DutyYear } from './dutyLogTypes.js';
import { SecureAuditEntry, SecureFile, SecureFileMeta, SecureFolder, SecureTrash, SecureTree } from './secureTypes.js';
import {
    CreateSubjectRequest,
    EntryLocation,
    Flashcard,
    LearningFileContent,
    LinkTarget,
    ProgressRequest,
    SaveNoteRequest,
    SearchHit,
    SubjectDetail,
    SubjectSummary
} from './learningTypes.js';

async function unwrap<T>(response: Response): Promise<T> {
    if (response.status === 401 && !location.pathname.startsWith('/login')) {
        location.replace('/login.html');
        throw new Error('Your session has expired.');
    }

    const text = await response.text();
    const body = text ? JSON.parse(text) : {};

    if (!response.ok) {
        throw new Error(body.error ?? `Request failed with status ${response.status}.`);
    }

    return body as T;
}

async function post<TRequest, TResponse>(url: string, payload: TRequest): Promise<TResponse> {
    return unwrap<TResponse>(await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    }));
}

async function get<T>(url: string): Promise<T> {
    return unwrap<T>(await fetch(url));
}

export function runImport(request: ImportRequest): Promise<ImportSummary> {
    return post<ImportRequest, ImportSummary>('/api/import', request);
}

export interface CurrentUser {
    email: string;
    displayName: string;
}

export function getCurrentUser(): Promise<CurrentUser> {
    return get('/api/auth/me');
}

export function signOut(): Promise<{ ok: boolean }> {
    return post<Record<string, never>, { ok: boolean }>('/api/auth/logout', {});
}

export class TerminalSessionGoneError extends Error {}

export function createTerminalSession(): Promise<TerminalSessionInfo> {
    return post<Record<string, never>, TerminalSessionInfo>('/api/terminal/sessions', {});
}

export function stopTerminalCommand(sessionId: string): Promise<{ ok: boolean }> {
    return post<Record<string, never>, { ok: boolean }>(`/api/terminal/sessions/${sessionId}/stop`, {});
}

export async function closeTerminalSession(sessionId: string): Promise<void> {
    await fetch(`/api/terminal/sessions/${sessionId}`, { method: 'DELETE', keepalive: true });
}

export async function runTerminalCommand(
    sessionId: string,
    command: string,
    onEvent: (event: TerminalEvent) => void
): Promise<void> {
    const response = await fetch(`/api/terminal/sessions/${sessionId}/run`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ command })
    });

    if (response.status === 404) {
        throw new TerminalSessionGoneError('This shell session no longer exists.');
    }

    if (!response.ok || !response.body) {
        await unwrap<unknown>(response);
        return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let pending = '';

    for (;;) {
        const { done, value } = await reader.read();
        pending += decoder.decode(value, { stream: !done });

        let newline = pending.indexOf('\n');
        while (newline >= 0) {
            const line = pending.slice(0, newline).trim();
            pending = pending.slice(newline + 1);

            if (line) {
                onEvent(JSON.parse(line) as TerminalEvent);
            }

            newline = pending.indexOf('\n');
        }

        if (done) {
            break;
        }
    }
}

export function runKeyVaultUpdate(request: KeyVaultRequest): Promise<KeyVaultSummary> {
    return post<KeyVaultRequest, KeyVaultSummary>('/api/keyvault', request);
}

export function previewLicenseFile(licenseFileContent: string): Promise<LicensePreview> {
    return post<{ licenseFileContent: string }, LicensePreview>('/api/licenses/preview', { licenseFileContent });
}

export function getLearningInfo(): Promise<{ contentRoot: string }> {
    return get('/api/learning');
}

export function getSubjects(): Promise<SubjectSummary[]> {
    return get('/api/learning/subjects');
}

export function getSubject(subjectId: string): Promise<SubjectDetail> {
    return get(`/api/learning/subjects/${encodeURIComponent(subjectId)}`);
}

export function createSubject(request: CreateSubjectRequest): Promise<SubjectSummary> {
    return post<CreateSubjectRequest, SubjectSummary>('/api/learning/subjects', request);
}

export function getLearningFile(subjectId: string, path: string): Promise<LearningFileContent> {
    return get(`/api/learning/file?subject=${encodeURIComponent(subjectId)}&path=${encodeURIComponent(path)}`);
}

export function resolveLink(subjectId: string, fromPath: string, href: string): Promise<LinkTarget> {
    return get(`/api/learning/resolve?subject=${encodeURIComponent(subjectId)}`
        + `&from=${encodeURIComponent(fromPath)}&href=${encodeURIComponent(href)}`);
}

export function saveNote(request: SaveNoteRequest): Promise<LearningFileContent> {
    return post<SaveNoteRequest, LearningFileContent>('/api/learning/notes', request);
}

export async function uploadFiles(subjectId: string, folder: string, files: ArrayLike<File>): Promise<LearningFileContent[]> {
    const form = new FormData();
    form.append('subjectId', subjectId);
    form.append('folder', folder);
    Array.from(files).forEach((file) => form.append('files', file, file.name));

    return unwrap<LearningFileContent[]>(await fetch('/api/learning/upload', { method: 'POST', body: form }));
}

export function searchLearning(query: string, subjectId?: string): Promise<SearchHit[]> {
    const subject = subjectId ? `&subject=${encodeURIComponent(subjectId)}` : '';
    return get(`/api/learning/search?q=${encodeURIComponent(query)}${subject}`);
}

export function getFlashcards(subjectId?: string): Promise<Flashcard[]> {
    const subject = subjectId ? `?subject=${encodeURIComponent(subjectId)}` : '';
    return get(`/api/learning/flashcards${subject}`);
}

export function setProgress(request: ProgressRequest): Promise<{ ok: boolean }> {
    return post<ProgressRequest, { ok: boolean }>('/api/learning/progress', request);
}

async function remove<T>(url: string): Promise<T> {
    return unwrap<T>(await fetch(url, { method: 'DELETE' }));
}

export function deleteLearningFile(subjectId: string, path: string): Promise<{ ok: boolean }> {
    return remove(`/api/learning/file?subject=${encodeURIComponent(subjectId)}&path=${encodeURIComponent(path)}`);
}

export function deleteSubject(subjectId: string): Promise<{ ok: boolean }> {
    return remove(`/api/learning/subjects/${encodeURIComponent(subjectId)}`);
}

export function createFolder(subjectId: string, path: string): Promise<EntryLocation> {
    return post<{ subjectId: string; path: string }, EntryLocation>('/api/learning/folders', { subjectId, path });
}

export function renameEntry(subjectId: string, path: string, newName: string): Promise<EntryLocation> {
    return post<{ subjectId: string; path: string; newName: string }, EntryLocation>(
        '/api/learning/rename', { subjectId, path, newName });
}

export function moveEntry(
    subjectId: string,
    path: string,
    targetSubjectId: string,
    targetFolder: string
): Promise<EntryLocation> {
    return post<{ subjectId: string; path: string; targetSubjectId: string; targetFolder: string }, EntryLocation>(
        '/api/learning/move', { subjectId, path, targetSubjectId, targetFolder });
}

export function renameSubject(subjectId: string, newName: string): Promise<EntryLocation> {
    return post<{ newName: string }, EntryLocation>(
        `/api/learning/subjects/${encodeURIComponent(subjectId)}/rename`, { newName });
}

export function getRepositories(): Promise<RepositoryList> {
    return get('/api/repositories');
}

export function cloneRepository(request: CloneRepositoryRequest): Promise<RepositoryInfo> {
    return post<CloneRepositoryRequest, RepositoryInfo>('/api/repositories', request);
}

export function fetchRepository(name: string): Promise<RepositoryInfo> {
    return post<Record<string, never>, RepositoryInfo>(`/api/repositories/${encodeURIComponent(name)}/fetch`, {});
}

export function pullRepository(name: string): Promise<RepositoryInfo> {
    return post<Record<string, never>, RepositoryInfo>(`/api/repositories/${encodeURIComponent(name)}/pull`, {});
}

export function getRepositoryBranches(name: string): Promise<RepositoryBranches> {
    return get(`/api/repositories/${encodeURIComponent(name)}/branches`);
}

export function switchRepositoryBranch(name: string, branch: string): Promise<RepositoryInfo> {
    return post<{ branch: string }, RepositoryInfo>(`/api/repositories/${encodeURIComponent(name)}/switch`, { branch });
}

export function openRepository(name: string, target: OpenTarget): Promise<{ ok: boolean }> {
    return post<{ target: OpenTarget }, { ok: boolean }>(`/api/repositories/${encodeURIComponent(name)}/open`, { target });
}

export function removeRepository(name: string, force: boolean): Promise<{ ok: boolean }> {
    return remove(`/api/repositories/${encodeURIComponent(name)}?force=${force}`);
}

export function getWorkLogYear(year: number): Promise<WorkLogYear> {
    return get(`/api/worklog/year?year=${year}`);
}

export async function saveWorkLogEntry(entry: WorkLogEntry): Promise<WorkLogEntry> {
    return unwrap<WorkLogEntry>(await fetch(`/api/worklog/${encodeURIComponent(entry.date)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(entry)
    }));
}

export function getDutyYear(year: number): Promise<DutyYear> {
    return get(`/api/dutylog/year?year=${year}`);
}

export async function saveDutyEntry(entry: DutyEntry): Promise<DutyEntry> {
    return unwrap<DutyEntry>(await fetch(`/api/dutylog/${encodeURIComponent(entry.date)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(entry)
    }));
}

export function getDutyRoster(): Promise<DutyRoster> {
    return get('/api/dutylog/roster');
}

export async function saveDutyRoster(roster: DutyRoster): Promise<DutyRoster> {
    return unwrap<DutyRoster>(await fetch('/api/dutylog/roster', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(roster)
    }));
}

export interface TrackedItem {
    id: string;
    kind: 'Ticket' | 'KB';
    title: string;
    url: string;
    category: string;
    year: number;
    quarter: string;
    iteration: string;
    owner: string;
    comment: string;
}

export function getTrackedItems(): Promise<TrackedItem[]> {
    return get('/api/tracker');
}

export async function saveTrackedItem(id: string | undefined, item: Omit<TrackedItem, 'id'>): Promise<TrackedItem> {
    if (!id) {
        return post<Omit<TrackedItem, 'id'>, TrackedItem>('/api/tracker', item);
    }

    return unwrap<TrackedItem>(await fetch(`/api/tracker/${encodeURIComponent(id)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(item)
    }));
}

export function deleteTrackedItem(id: string): Promise<{ ok: boolean }> {
    return remove(`/api/tracker/${encodeURIComponent(id)}`);
}

export function importTrackedItems(items: Array<Omit<TrackedItem, 'id'>>): Promise<{ added: number; skipped: number }> {
    return post<{ items: Array<Omit<TrackedItem, 'id'>> }, { added: number; skipped: number }>('/api/tracker/import', { items });
}

export function getLinks(): Promise<ImportantLink[]> {
    return get('/api/links');
}

export async function saveLink(id: string | undefined, link: ImportantLinkInput): Promise<ImportantLink> {
    if (!id) {
        return post<ImportantLinkInput, ImportantLink>('/api/links', link);
    }

    return unwrap<ImportantLink>(await fetch(`/api/links/${encodeURIComponent(id)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(link)
    }));
}

export function deleteLink(id: string): Promise<{ ok: boolean }> {
    return remove(`/api/links/${encodeURIComponent(id)}`);
}

export function getLinkPassword(id: string): Promise<{ password: string }> {
    return get(`/api/links/${encodeURIComponent(id)}/password`);
}

export interface ImportantFile {
    name: string;
    kind: string;
    size: number;
    updatedAt: string;
}

export function importantFileUrl(name: string): string {
    return `/api/files/raw?name=${encodeURIComponent(name)}`;
}

export function getImportantFiles(): Promise<ImportantFile[]> {
    return get('/api/files');
}

export async function uploadImportantFiles(files: ArrayLike<File>): Promise<ImportantFile[]> {
    const form = new FormData();
    Array.from(files).forEach((file) => form.append('files', file, file.name));

    return unwrap<ImportantFile[]>(await fetch('/api/files/upload', { method: 'POST', body: form }));
}

export function deleteImportantFile(name: string): Promise<{ ok: boolean }> {
    return remove(`/api/files?name=${encodeURIComponent(name)}`);
}

export function getMathsLibrary(): Promise<MathsLibrary> {
    return get('/api/maths');
}

export function getMathsBook(): Promise<MathsBook> {
    return get('/api/maths/book');
}

export function mathsFileUrl(topic: string, name: string): string {
    return `/api/maths/file?topic=${encodeURIComponent(topic)}&name=${encodeURIComponent(name)}`;
}

export function getMathsNote(topic: string): Promise<MathsNote> {
    return get(`/api/maths/notes?topic=${encodeURIComponent(topic)}`);
}

export async function saveMathsNote(topic: string, content: string, keepalive = false): Promise<MathsNote> {
    return unwrap<MathsNote>(await fetch(`/api/maths/notes?topic=${encodeURIComponent(topic)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content }),
        keepalive
    }));
}

export async function saveMathsStatus(topic: string, status: string): Promise<{ status: string }> {
    return unwrap<{ status: string }>(await fetch(`/api/maths/status?topic=${encodeURIComponent(topic)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status })
    }));
}
export const SECURE_BASE = '/api/secure';
export const PERSONAL_BASE = '/api/personal';

export function getSecureTree(base = SECURE_BASE): Promise<SecureTree> {
    return get(`${base}/tree`);
}

export function createSecureFolder(name: string, parentId: string | null, base = SECURE_BASE): Promise<SecureFolder> {
    return post<{ name: string; parentId: string | null }, SecureFolder>(`${base}/folders`, { name, parentId });
}

export async function updateSecureFolder(id: string, name: string, parentId: string | null, base = SECURE_BASE): Promise<SecureFolder> {
    return unwrap<SecureFolder>(await fetch(`${base}/folders/${encodeURIComponent(id)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, parentId })
    }));
}

export function deleteSecureFolder(id: string, base = SECURE_BASE): Promise<{ ok: boolean }> {
    return remove(`${base}/folders/${encodeURIComponent(id)}`);
}

export async function uploadSecureFiles(folderId: string | null, files: File[], meta: SecureFileMeta, base = SECURE_BASE): Promise<SecureFile[]> {
    const form = new FormData();
    form.append('folderId', folderId ?? '');
    form.append('type', meta.type);
    form.append('infrastructure', meta.infrastructure);
    form.append('environment', meta.environment);
    form.append('expiry', meta.expiry);
    form.append('notes', meta.notes);
    files.forEach((file) => form.append('files', file, file.name));

    return unwrap<SecureFile[]>(await fetch(`${base}/files`, { method: 'POST', body: form }));
}

export async function updateSecureFile(id: string, meta: SecureFileMeta, base = SECURE_BASE): Promise<SecureFile> {
    return unwrap<SecureFile>(await fetch(`${base}/files/${encodeURIComponent(id)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(meta)
    }));
}

export async function addSecureVersion(id: string, file: File, base = SECURE_BASE): Promise<SecureFile> {
    const form = new FormData();
    form.append('file', file, file.name);
    return unwrap<SecureFile>(await fetch(`${base}/files/${encodeURIComponent(id)}/version`, { method: 'POST', body: form }));
}

export function getSecureText(id: string, base = SECURE_BASE): Promise<{ text: string }> {
    return get(`${base}/files/${encodeURIComponent(id)}/text`);
}

export function deleteSecureFile(id: string, base = SECURE_BASE): Promise<{ ok: boolean }> {
    return remove(`${base}/files/${encodeURIComponent(id)}`);
}

export function getSecureTrash(base = SECURE_BASE): Promise<SecureTrash> {
    return get(`${base}/trash`);
}

export function restoreSecure(kind: 'file' | 'folder', id: string, base = SECURE_BASE): Promise<{ ok: boolean }> {
    return post<Record<string, never>, { ok: boolean }>(`${base}/trash/${kind}/${encodeURIComponent(id)}/restore`, {});
}

export function purgeSecure(kind: 'file' | 'folder', id: string, base = SECURE_BASE): Promise<{ ok: boolean }> {
    return remove(`${base}/trash/${kind}/${encodeURIComponent(id)}`);
}

export function getSecureAudit(limit: number, base = SECURE_BASE): Promise<SecureAuditEntry[]> {
    return get(`${base}/audit?limit=${limit}`);
}

export function secureDownloadUrl(id: string, version?: number, base = SECURE_BASE): string {
    return `${base}/files/${encodeURIComponent(id)}/download${version === undefined ? '' : `?version=${version}`}`;
}