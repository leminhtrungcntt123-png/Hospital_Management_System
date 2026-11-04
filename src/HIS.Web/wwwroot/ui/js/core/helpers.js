/* core / helpers */
const $ = (s) => document.querySelector(s),
    $$ = (s) => [...document.querySelectorAll(s)];

const esc = (x) =>
    String(x ?? '').replace(
        /[&<>"']/g,
        (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]
    );

const money = (n) =>
    new Intl.NumberFormat('vi-VN', {
        style: 'currency',
        currency: 'VND',
        maximumFractionDigits: 0,
    }).format(Number(n) || 0);

const fmtDate = (x) => (x ? x.slice(0, 10).split('-').reverse().join('/') : '—');

const initials = (n) =>
    String(n)
        .trim()
        .split(/\s+/)
        .slice(-2)
        .map((v) => v[0])
        .join('')
        .toUpperCase();

const icon = (n, cls = '') =>
    /* HTML */ `<svg class="icon ${cls}" viewBox="0 0 24 24" aria-hidden="true">
        <path d="${paths[n] || paths.file}" />
    </svg>`;

const age = (dob) => {
    const t = new Date(TODAY + 'T12:00:00'),
        d = new Date(dob + 'T12:00:00');
    return (
        t.getFullYear() -
        d.getFullYear() -
        (t.getMonth() < d.getMonth() || (t.getMonth() === d.getMonth() && t.getDate() < d.getDate())
            ? 1
            : 0)
    );
};

const stamp = () => new Date().toISOString();

const uid = (p) => p + '-' + Date.now().toString(36) + Math.random().toString(36).slice(2, 6);

const dateAdd = (d, n) => {
    const x = new Date(d + 'T12:00:00Z');
    x.setUTCDate(x.getUTCDate() + n);
    return x.toISOString().slice(0, 10);
};

const workingDays = (count = 10) => {
    if (window.hisLive && db?.slots)
        return [...new Set(db.slots.map((s) => s.date))].sort().slice(0, count);
    const days = [];
    for (let i = 0; days.length < count; i++) {
        const d = dateAdd(TODAY, i);
        if (![0, 6].includes(new Date(d + 'T12:00:00Z').getUTCDay())) days.push(d);
    }
    return days;
};

const minuteOf = (t) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3, 5));

const demoNow = () =>
    window.hisLive
        ? new Date(Date.parse(db.now) + Date.now() - window.hisSnapshotAt)
        : new Date(TODAY + 'T09:00:00');

const appointmentTime = (a) => new Date(a.date + 'T' + a.time + ':00');

const activeRole = () => roleInfo[session.role];
