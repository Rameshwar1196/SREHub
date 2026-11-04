export interface TerminalSessionInfo {
    id: string;
    shell: string;
    workingDirectory: string;
}

export interface TerminalEvent {
    type: 'out' | 'err' | 'notice' | 'exit';
    text?: string;
    exitCode?: number;
    cwd?: string;
}
