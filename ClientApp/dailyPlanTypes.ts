export interface SubTask {
    id: string;
    title: string;
    done: boolean;
}

export interface IterationTarget {
    id: string;
    title: string;
    priority: string;
    status: string;
    plannedDays?: number;
    url: string;
    outcome: string;
    completedOn: string;
    carriedFrom: string;
    subTasks: SubTask[];
}

export interface IterationPlan {
    start: string;
    end: string;
    targets: IterationTarget[];
}

export interface DailyPlanDay {
    date: string;
    focus: string;
    endOfDay: string;
    blockers: string;
    tomorrow: string;
}

export interface DailyPlanView {
    day: DailyPlanDay;
    iteration: IterationPlan;
    carryOverCount: number;
}

export interface DailyPlanSave {
    day: DailyPlanDay;
    targets: IterationTarget[];
}
