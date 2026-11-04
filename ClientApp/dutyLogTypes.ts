export interface DutyEntry {
    date: string;
    doShift: string;
    shiftTime: string;
    handover: string;
    doNote: string;
}

export interface DutyYear {
    year: number;
    entries: DutyEntry[];
}

export interface DutyRosterBlock {
    start: string;
    end: string;
    label: string;
    japan: string;
    india: string;
}

export interface OneNoteIteration {
    start: string;
    end: string;
    label: string;
    owner: string;
    status: string;
    page: string;
    note: string;
}

export interface DutySettings {
    myDoId: string;
    myItId: string;
    names: Record<string, string>;
}

export interface DutyRoster {
    settings: DutySettings;
    blocks: DutyRosterBlock[];
    iterations: OneNoteIteration[];
}
