import { byId, el, stats, table } from './dom.js';

type Tokenizer = { text: string; position: number };

const HISTORY_LIMIT = 30;

const CONSTANTS: Record<string, number> = {
    pi: Math.PI,
    e: Math.E,
    tau: Math.PI * 2
};

function factorial(value: number): number {
    if (!Number.isInteger(value) || value < 0 || value > 170) {
        throw new Error('Factorial needs a whole number from 0 to 170.');
    }

    let result = 1;
    for (let step = 2; step <= value; step++) {
        result *= step;
    }

    return result;
}

function functions(degrees: boolean): Record<string, (...args: number[]) => number> {
    const toRadians = (value: number): number => (degrees ? (value * Math.PI) / 180 : value);
    const fromRadians = (value: number): number => (degrees ? (value * 180) / Math.PI : value);

    return {
        sin: (x) => Math.sin(toRadians(x)),
        cos: (x) => Math.cos(toRadians(x)),
        tan: (x) => Math.tan(toRadians(x)),
        asin: (x) => fromRadians(Math.asin(x)),
        acos: (x) => fromRadians(Math.acos(x)),
        atan: (x) => fromRadians(Math.atan(x)),
        sinh: Math.sinh,
        cosh: Math.cosh,
        tanh: Math.tanh,
        sqrt: Math.sqrt,
        cbrt: Math.cbrt,
        abs: Math.abs,
        ln: Math.log,
        log: Math.log10,
        log2: Math.log2,
        exp: Math.exp,
        floor: Math.floor,
        ceil: Math.ceil,
        round: Math.round,
        sign: Math.sign,
        fact: factorial,
        pow: Math.pow,
        min: Math.min,
        max: Math.max,
        hypot: Math.hypot,
        deg: (x) => (x * 180) / Math.PI,
        rad: (x) => (x * Math.PI) / 180
    };
}

export function evaluate(expression: string, degrees: boolean, ans: number): number {
    const source: Tokenizer = { text: expression, position: 0 };
    const known = functions(degrees);

    const skip = (): void => {
        while (source.position < source.text.length && /\s/.test(source.text[source.position])) {
            source.position++;
        }
    };

    const peek = (): string => {
        skip();
        return source.text[source.position] ?? '';
    };

    const take = (character: string): boolean => {
        if (peek() === character) {
            source.position++;
            return true;
        }

        return false;
    };

    const parseNumber = (): number => {
        const match = /^(\d+\.?\d*|\.\d+)(e[+-]?\d+)?/i.exec(source.text.slice(source.position));
        if (!match) {
            throw new Error(`Unexpected '${peek() || 'end of input'}' at position ${source.position + 1}.`);
        }

        source.position += match[0].length;
        return Number(match[0]);
    };

    let parseExpression: () => number = () => 0;

    const parsePrimary = (): number => {
        if (take('(')) {
            const inner = parseExpression();
            if (!take(')')) {
                throw new Error('Missing closing parenthesis.');
            }

            return inner;
        }

        const word = /^[a-z_][a-z0-9_]*/i.exec(source.text.slice(source.position));
        if (word && /[a-z_]/i.test(peek())) {
            const name = word[0].toLowerCase();
            source.position += word[0].length;

            if (peek() === '(') {
                const fn = known[name];
                if (!fn) {
                    throw new Error(`Unknown function '${name}'.`);
                }

                source.position++;
                const args: number[] = [];

                if (!take(')')) {
                    do {
                        args.push(parseExpression());
                    } while (take(','));

                    if (!take(')')) {
                        throw new Error(`Missing closing parenthesis after ${name}(...).`);
                    }
                }

                return fn(...args);
            }

            if (name === 'ans') {
                return ans;
            }

            if (name in CONSTANTS) {
                return CONSTANTS[name];
            }

            throw new Error(`Unknown name '${name}'.`);
        }

        return parseNumber();
    };

    const parsePostfix = (): number => {
        let value = parsePrimary();
        while (take('!')) {
            value = factorial(value);
        }

        return value;
    };

    const parseUnary = (): number => {
        if (take('-')) {
            return -parseUnary();
        }

        if (take('+')) {
            return parseUnary();
        }

        const base = parsePostfix();
        return take('^') ? Math.pow(base, parseUnary()) : base;
    };

    const parseTerm = (): number => {
        let value = parseUnary();

        for (;;) {
            if (take('*')) {
                value *= parseUnary();
            } else if (take('/')) {
                value /= parseUnary();
            } else if (take('%')) {
                value %= parseUnary();
            } else {
                return value;
            }
        }
    };

    parseExpression = (): number => {
        let value = parseTerm();

        for (;;) {
            if (take('+')) {
                value += parseTerm();
            } else if (take('-')) {
                value -= parseTerm();
            } else {
                return value;
            }
        }
    };

    if (!expression.trim()) {
        throw new Error('Enter an expression.');
    }

    const result = parseExpression();

    if (peek() !== '') {
        throw new Error(`Unexpected '${peek()}' at position ${source.position + 1}.`);
    }

    if (!Number.isFinite(result)) {
        throw new Error('The result is undefined or too large.');
    }

    return result;
}

export function formatNumber(value: number): string {
    if (Object.is(value, -0)) {
        return '0';
    }

    const abs = Math.abs(value);
    if (abs !== 0 && (abs >= 1e15 || abs < 1e-9)) {
        return value.toExponential(9).replace(/\.?0+e/, 'e');
    }

    return String(Number(value.toPrecision(14)));
}

export function formatDuration(totalSeconds: number): string {
    if (totalSeconds < 1) {
        return `${Math.round(totalSeconds * 1000)} ms`;
    }

    const rounded = Math.round(totalSeconds);
    const parts: string[] = [];
    const units: Array<[string, number]> = [['d', 86400], ['h', 3600], ['m', 60], ['s', 1]];
    let remaining = rounded;

    for (const [label, size] of units) {
        const amount = Math.floor(remaining / size);
        remaining -= amount * size;

        if (amount > 0) {
            parts.push(`${amount}${label}`);
        }
    }

    return parts.join(' ') || '0s';
}

const DATA_UNITS: Array<[string, number]> = [
    ['B', 1],
    ['KB', 1e3],
    ['MB', 1e6],
    ['GB', 1e9],
    ['TB', 1e12],
    ['KiB', 1024],
    ['MiB', 1024 ** 2],
    ['GiB', 1024 ** 3],
    ['TiB', 1024 ** 4]
];

const TIME_UNITS: Array<[string, number]> = [
    ['ms', 0.001],
    ['s', 1],
    ['min', 60],
    ['h', 3600],
    ['d', 86400],
    ['week', 604800]
];

function initCalculator(): void {
    const form = byId<HTMLFormElement>('calc-form');
    const input = byId<HTMLInputElement>('calc-input');
    const result = byId('calc-result');
    const error = byId('calc-error');
    const degrees = byId<HTMLInputElement>('calc-degrees');
    const historyHost = byId('calc-history');
    const clear = byId<HTMLButtonElement>('calc-clear');

    const history: Array<{ expression: string; value: string }> = [];
    let ans = 0;

    const renderHistory = (): void => {
        if (history.length === 0) {
            historyHost.replaceChildren(el('p', 'hint', 'Your calculations appear here. Click one to reuse it.'));
            return;
        }

        historyHost.replaceChildren(...history.map((entry) => {
            const row = el('button', 'calc-history-row');
            row.type = 'button';
            row.append(el('span', 'calc-history-expression', entry.expression), el('span', 'calc-history-value', `= ${entry.value}`));
            row.addEventListener('click', () => {
                input.value = entry.expression;
                input.focus();
            });
            return row;
        }));
    };

    form.addEventListener('submit', (event) => {
        event.preventDefault();
        error.replaceChildren();

        const expression = input.value.trim();

        try {
            const value = evaluate(expression, degrees.checked, ans);
            const text = formatNumber(value);
            ans = value;
            result.textContent = text;
            history.unshift({ expression, value: text });
            history.splice(HISTORY_LIMIT);
            renderHistory();
            input.select();
        } catch (failure) {
            error.replaceChildren(el('p', 'error', (failure as Error).message));
        }
    });

    clear.addEventListener('click', () => {
        history.length = 0;
        ans = 0;
        result.textContent = '0';
        error.replaceChildren();
        renderHistory();
        input.focus();
    });

    renderHistory();
}

function initAvailability(): void {
    const percent = byId<HTMLInputElement>('sre-availability');
    const host = byId('sre-availability-result');

    const render = (): void => {
        const target = Number(percent.value);

        if (!Number.isFinite(target) || target < 0 || target > 100) {
            host.replaceChildren(el('p', 'hint', 'Enter a percentage between 0 and 100.'));
            return;
        }

        const allowed = 1 - target / 100;
        const periods: Array<[string, number]> = [['Year (365 days)', 365], ['Month (30 days)', 30], ['Week', 7], ['Day', 1]];

        host.replaceChildren(table(
            ['Period', 'Allowed downtime'],
            periods.map(([label, days]) => [label, formatDuration(allowed * days * 86400)])
        ));
    };

    percent.addEventListener('input', render);
    render();
}

function initErrorBudget(): void {
    const slo = byId<HTMLInputElement>('budget-slo');
    const days = byId<HTMLInputElement>('budget-days');
    const used = byId<HTMLInputElement>('budget-used');
    const host = byId('budget-result');

    const render = (): void => {
        const target = Number(slo.value);
        const windowDays = Number(days.value);
        const spentMinutes = Number(used.value || '0');

        if (![target, windowDays, spentMinutes].every(Number.isFinite) || target < 0 || target > 100 || windowDays <= 0 || spentMinutes < 0) {
            host.replaceChildren(el('p', 'hint', 'Enter an SLO from 0 to 100, a window in days and the downtime used so far.'));
            return;
        }

        const budgetMinutes = (1 - target / 100) * windowDays * 1440;
        const remaining = budgetMinutes - spentMinutes;
        const consumed = budgetMinutes === 0 ? (spentMinutes > 0 ? 100 : 0) : (spentMinutes / budgetMinutes) * 100;

        host.replaceChildren(stats([
            ['Total budget', formatDuration(budgetMinutes * 60)],
            ['Used', formatDuration(spentMinutes * 60)],
            ['Remaining', remaining >= 0 ? formatDuration(remaining * 60) : `Exhausted by ${formatDuration(-remaining * 60)}`],
            ['Budget consumed', `${formatNumber(Math.round(consumed * 100) / 100)}%`]
        ]));
    };

    for (const field of [slo, days, used]) {
        field.addEventListener('input', render);
    }

    render();
}

function initConverter(): void {
    const category = byId<HTMLSelectElement>('convert-category');
    const value = byId<HTMLInputElement>('convert-value');
    const from = byId<HTMLSelectElement>('convert-from');
    const to = byId<HTMLSelectElement>('convert-to');
    const host = byId('convert-result');

    const unitsFor = (): Array<[string, number]> => (category.value === 'time' ? TIME_UNITS : DATA_UNITS);

    const fillUnits = (): void => {
        const units = unitsFor();
        const options = (): HTMLOptionElement[] => units.map(([name]) => {
            const option = el('option', undefined, name);
            option.value = name;
            return option;
        });

        from.replaceChildren(...options());
        to.replaceChildren(...options());
        from.selectedIndex = units.length > 2 ? 3 : 1;
        to.selectedIndex = 1;
    };

    const render = (): void => {
        const amount = Number(value.value);
        const units = new Map(unitsFor());
        const source = units.get(from.value);
        const target = units.get(to.value);

        if (!value.value || !Number.isFinite(amount) || !source || !target) {
            host.replaceChildren(el('p', 'hint', 'Enter a value to convert.'));
            return;
        }

        host.replaceChildren(el('p', 'convert-answer', `${formatNumber(amount)} ${from.value} = ${formatNumber((amount * source) / target)} ${to.value}`));
    };

    category.addEventListener('change', () => {
        fillUnits();
        render();
    });

    for (const field of [value, from, to]) {
        field.addEventListener('input', render);
    }

    fillUnits();
    render();
}

export function initMathViews(): void {
    initCalculator();
    initAvailability();
    initErrorBudget();
    initConverter();
}
