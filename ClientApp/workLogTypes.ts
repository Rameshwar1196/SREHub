export interface WorkLogEntry {
    date: string;
    attendance: string;
    emergencyLeave: string;
    networkPower: string;
    summary: string;
    escalation: string;
    note: string;
}

export interface WorkLogSummaryRow {
    category: string;
    days: number;
    percentage: number;
}

export interface WorkLogMonth {
    month: string;
    entries: WorkLogEntry[];
    summary: WorkLogSummaryRow[];
}

export interface WorkLogYear {
    year: number;
    entries: WorkLogEntry[];
    summary: WorkLogSummaryRow[];
    months: WorkLogMonth[];
}
