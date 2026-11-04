/* core / state */
let db;

if (window.hisLive) {
    db = JSON.parse(document.getElementById('his-db-data').textContent);
} else {
    try {
        db = JSON.parse(localStorage.getItem(STORAGE_KEY));
    } catch {}
    if (!db || db.version !== 3) db = seed();
}

let session = { role: 'PATIENT', accountId: 'ac-p', authenticated: true };

try {
    const saved = JSON.parse(sessionStorage.getItem(STORAGE_KEY + '-session'));
    if (saved && roleInfo[saved.role] && db.accounts.some((a) => a.id === saved.accountId))
        session = saved;
} catch {}

let ui = {
    route: 'public/home',
    query: '',
    filter: 'ALL',
    dept: 'ALL',
    tab: 'general',
    profileId: 'BN0001',
    roomId: 'ALL',
    selectedAppointment: null,
    selectedRecord: null,
    catalog: 'services',
    scheduleOffset: 0,
    booking: {
        step: 1,
        patientId: 'BN0001',
        doctorId: 'd1',
        method: 'specialty',
        date: workingDays()[1],
        time: '',
        shift: 'MORNING',
        reason: '',
        holdId: null,
        parentId: null,
    },
    mobileMenu: false,
};

let toastTimer, returnFocus;

function save() {
    if (window.hisLive) return;
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(db));
    } catch {
        toast('Phiên hiện tại vẫn hoạt động; trình duyệt chưa cho phép lưu dữ liệu.', true);
    }
}

const acc = () => db.accounts.find((a) => a.id === session.accountId);

const patient = (id) => db.patients.find((p) => p.id === id);

const doctor = (id) => db.doctors.find((d) => d.id === id);

const dept = (id) => db.departments.find((d) => d.id === id);

const room = (id) => db.rooms.find((r) => r.id === id);

const appointment = (id) => db.appointments.find((a) => a.id === id);

const selfPatient = () =>
    db.links.find(
        (l) =>
            l.accountId === session.accountId && l.relationship === 'SELF' && l.status === 'ACTIVE'
    )?.patientId;

const myLinks = () =>
    db.links.filter((l) => l.accountId === session.accountId && l.status !== 'REVOKED');

const linkFor = (id) => myLinks().find((l) => l.patientId === id);

const canView = (id) => {
    const l = linkFor(id),
        p = patient(id);
    return (
        session.role === 'PATIENT' &&
        l?.status === 'ACTIVE' &&
        l.canView === true &&
        !(l.relationship === 'CHILD' && age(p.dob) >= 18 && !l.verifiedAdult && !l.verifiedBy)
    );
};

const scopedApps = () =>
    db.appointments.filter(
        (a) =>
            session.role === 'ADMIN' ||
            (session.role === 'PATIENT' && a.bookedBy === session.accountId) ||
            (['NURSE', 'DEPARTMENT_HEAD'].includes(session.role) && a.deptId === acc()?.deptId) ||
            (session.role === 'DOCTOR' && a.doctorId === acc()?.doctorId)
    );

const todayApps = () =>
    scopedApps().filter((a) => a.date === TODAY && !['CANCELLED', 'EXPIRED'].includes(a.status));

const activeApps = () =>
    db.appointments.filter(
        (a) => a.bookedBy === session.accountId && liveStates.includes(a.status)
    );
