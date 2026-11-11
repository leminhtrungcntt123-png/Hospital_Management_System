const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const root = resolve(__dirname, '../..');
const scripts = readFileSync(
    resolve(root, 'src/HIS.Web/Views/Shared/MediCare/_Scripts.cshtml'),
    'utf8',
);
const source = [...scripts.matchAll(/src="~\/([^"?]+)"/g)]
    .map((match) => readFileSync(resolve(root, 'src/HIS.Web/wwwroot', match[1]), 'utf8'))
    .join('\n');

function createClient(fetch, overrides = {}) {
    const app = { innerHTML: '' };
    const listeners = {};
    const navigations = [];
    const windowListeners = {};
    let logoutSubmissions = 0;
    const config = {
        role: 'NURSE',
        authenticated: true,
        displayName: 'Signed-in staff',
        initialRoute: 'staff/cashier',
        pathBase: '/his',
        assetBase: '/his/ui/assets/',
        catalogUrl: '/his/Home/Catalog',
        login: '/his/Account/Login',
        home: '/his/Staff/Reception',
        changePassword: '/his/Account/ChangePassword',
        ...overrides,
    };
    const context = vm.createContext({
        URLSearchParams,
        clearInterval() {},
        setInterval() {},
        document: {
            title: '',
            getElementById(id) {
                if (id === 'his-ui-config') return { textContent: JSON.stringify(config) };
                if (id === 'his-server-logout')
                    return {
                        requestSubmit() {
                            logoutSubmissions++;
                        },
                    };
                throw new Error('Unexpected element: ' + id);
            },
            querySelector(selector) {
                if (selector === '#app') return app;
                if (selector === '#overlay-root') return { replaceChildren() {} };
                if (selector === '[name="__RequestVerificationToken"]')
                    return { value: 'test-token' };
                return null;
            },
            addEventListener(name, handler) {
                listeners[name] = handler;
            },
            body: { style: {} },
        },
        window: {
            addEventListener(name, handler) {
                windowListeners[name] = handler;
            },
            scrollTo() {},
        },
        location: {
            hash: '',
            assign(url) {
                navigations.push(url);
            },
        },
        fetch:
            fetch ||
            (() => {
                throw new Error('Unexpected request');
            }),
    });
    for (const key of ['localStorage', 'sessionStorage']) {
        Object.defineProperty(context, key, {
            get() {
                throw new Error('Browser records must not be used');
            },
        });
    }
    vm.runInContext(source, context);
    return {
        run: (code) => vm.runInContext(code, context),
        app,
        listeners,
        windowListeners,
        navigations,
        logoutSubmissions: () => logoutSubmissions,
    };
}

test('public catalog renders only data returned by its server endpoint', async () => {
    const client = createClient(async (url, options) => {
        assert.equal(url, '/his/Home/Catalog');
        assert.equal(options.credentials, 'same-origin');
        return {
            ok: true,
            json: async () => ({
                doctors: [
                    {
                        id: 17,
                        name: '<Server doctor>',
                        specialty: 'Internal medicine',
                        department: 'Server department',
                    },
                ],
                departments: [{ id: 4, name: 'Server department', description: '' }],
            }),
        };
    });
    await client.run("ui.route = 'public/doctors'; loadCatalog();");
    assert.equal(client.run('catalog.doctors.length'), 1);
    assert.match(client.app.innerHTML, /&lt;Server doctor&gt;/);
    assert.doesNotMatch(client.app.innerHTML, /<Server doctor>/);
});

test('failed requests show an error and never fall back to sample data', async () => {
    const client = createClient(async () => ({ ok: false }));
    await client.run("ui.route = 'public/doctors'; loadCatalog();");
    assert.equal(client.run('catalog.status'), 'error');
    assert.equal(client.run('catalog.doctors.length'), 0);
    assert.match(client.app.innerHTML, /Chưa tải được thông tin/);
});

test('unconnected screens keep their layout without inventing clinical data', () => {
    const client = createClient();
    for (const route of [
        'patient/appointments',
        'patient/family',
        'patient/records',
        'patient/notifications',
        'staff/dashboard',
        'staff/vitals',
        'staff/patients',
        'staff/consultation',
        'staff/labs',
        'staff/records',
        'staff/schedule',
        'staff/exceptions',
        'staff/cashier',
    ]) {
        client.run(`ui.route = ${JSON.stringify(route)}; render();`);
        assert.match(client.app.innerHTML, /Chức năng đang được hoàn thiện/);
        assert.doesNotMatch(client.app.innerHTML, /undefined|NaN/);
    }
});

test('protected navigation goes to MVC and logout keeps native form submission', () => {
    const client = createClient();
    client.run('boot();');
    let prevented = false;
    client.listeners.submit({
        target: { matches: () => false },
        preventDefault() {
            prevented = true;
        },
    });
    assert.equal(prevented, false);
    client.run("go('staff/consultation');");
    assert.deepEqual(client.navigations, ['/his/Staff/Consultation']);
    client.run("go('public/doctors');");
    assert.equal(client.navigations[1], '/his/#/public/doctors');
    client.listeners.click({
        target: {
            closest: () => ({ dataset: { act: 'logout' }, disabled: false }),
        },
    });
    assert.equal(client.logoutSubmissions(), 1);
});

test('home search preserves Vietnamese department and search terms in the doctor route', () => {
    const client = createClient(undefined, { initialRoute: 'public/home', authenticated: false });
    client.run(
        "catalog.status = 'ready'; boot(); ui.query = 'Nam & Hoa'; ui.department = 'Nội tổng quát';",
    );
    let prevented = false;
    client.listeners.submit({
        target: { matches: (selector) => selector === '[data-doctor-search]' },
        preventDefault() {
            prevented = true;
        },
    });
    assert.equal(prevented, true);
    assert.equal(client.run('ui.route'), 'public/doctors');
    assert.equal(client.run('ui.query'), 'Nam & Hoa');
    assert.equal(client.run('ui.department'), 'Nội tổng quát');
    assert.match(client.run('location.hash'), /q=Nam\+%26\+Hoa/);
});

test('a doctor deep link restores filters when opened directly', () => {
    const client = createClient(undefined, { initialRoute: 'public/home' });
    client.run(
        "catalog.status = 'ready'; location.hash = '#/public/doctors?department=Tim%20m%E1%BA%A1ch&q=Lan'; boot();",
    );
    assert.equal(client.run('ui.route'), 'public/doctors');
    assert.equal(client.run('ui.department'), 'Tim mạch');
    assert.equal(client.run('ui.query'), 'Lan');
});

test('the keyboard skip link is not treated as an application route', () => {
    const client = createClient();
    client.run("boot(); location.hash = '#portal-main';");
    client.windowListeners.hashchange();
    assert.equal(client.navigations.length, 0);
});

test('an authenticated staff member has workspace actions instead of patient-only actions', () => {
    const client = createClient(undefined, { initialRoute: 'public/home' });
    client.run("ui.route = 'public/home'; catalog.status = 'ready'; render();");
    assert.match(client.app.innerHTML, /Về bàn làm việc/);
    assert.doesNotMatch(client.app.innerHTML, /href="\/his\/Patient\/Booking"/);
});

test('guest and patient booking actions use the public booking page', () => {
    for (const authenticated of [false, true]) {
        const client = createClient(undefined, {
            role: 'PATIENT',
            authenticated,
            initialRoute: 'public/home',
        });
        client.run(
            "ui.route = 'public/home'; catalog.status = 'ready'; render(); go('patient/booking');",
        );
        assert.match(client.app.innerHTML, /href="\/his\/Booking"/);
        assert.equal(client.navigations[0], '/his/Booking');
    }
});

for (const [role, route, expected] of [
    ['DOCTOR', 'staff/schedule', '/his/Staff/Consultation?tab=schedule'],
    ['DOCTOR', 'staff/labs', '/his/Staff/Consultation?tab=labs'],
    ['DOCTOR', 'staff/records', '/his/Staff/Consultation?tab=records'],
    ['DOCTOR', 'staff/exceptions', '/his/Staff/Consultation?tab=exceptions'],
    ['DEPARTMENT_HEAD', 'staff/dashboard', '/his/Staff/Schedule?tab=overview'],
    ['DEPARTMENT_HEAD', 'staff/patients', '/his/Staff/Schedule?tab=patients'],
    ['DEPARTMENT_HEAD', 'staff/records', '/his/Staff/Schedule?tab=records'],
    ['DEPARTMENT_HEAD', 'staff/exceptions', '/his/Staff/Schedule?tab=exceptions'],
    ['NURSE', 'staff/cashier', '/his/Staff/Cashier'],
    ['NURSE', 'staff/schedule', '/his/Staff/Schedule'],
    ['NURSE', 'public/home', '/his/'],
]) {
    test(`${role} navigation: ${route}`, () => {
        const client = createClient(undefined, { role });
        assert.equal(client.run(`routeUrl(${JSON.stringify(route)})`), expected);
    });
}

test('editing a doctor schedule preserves an online quota of zero', () => {
    const view = readFileSync(
        resolve(root, 'src/HIS.Web/Areas/Staff/Views/Schedule/Index.cshtml'),
        'utf8',
    );
    const editSchedule = view.match(/function openScheduleModalWithData\([\s\S]*?\n    }/)[0];
    const fields = new Map();
    const document = {
        getElementById(id) {
            if (!fields.has(id)) fields.set(id, { value: '', style: {}, classList: { add() {} } });
            return fields.get(id);
        },
    };
    vm.runInNewContext(
        editSchedule +
            "\nopenScheduleModalWithData(1, 1, 1, 0, '07:30', '11:30', 40, 0, 1, '2026-11-10')",
        { document },
    );
    assert.equal(fields.get('scheduleModalOnlineSlots').value, 0);
});

test('choosing a doctor keeps the identifier through the public booking link', () => {
    const client = createClient(undefined, { role: 'PATIENT', authenticated: false });
    assert.equal(client.run("routeUrl('patient/booking?doctorId=17')"), '/his/Booking?doctorId=17');
});

test('the booking summary renders the selected server doctor while waiting for a real time slot', () => {
    const client = createClient(undefined, {
        role: 'PATIENT',
        selectedDoctorId: '17',
        initialRoute: 'public/booking',
    });
    client.run(
        "catalog = {status:'ready',departments:[],doctors:[{id:17,name:'<Server doctor>',specialty:'Server specialty',department:'Server department'}]}; render();",
    );
    assert.match(client.app.innerHTML, /&lt;Server doctor&gt;/);
    assert.match(client.app.innerHTML, /Không cần đăng nhập/);
    assert.equal(client.run('guestBooking.selected'), '');
    assert.match(client.app.innerHTML, /type="submit" disabled/);
});

test('guest booking renders only available times returned by the server', async () => {
    const client = createClient(
        async () => ({
            ok: true,
            json: async () => ({
                slots: [
                    {
                        scheduleId: 7,
                        startTime: '2026-11-12T08:00:00',
                        endTime: '2026-11-12T08:15:00',
                        room: 'SERVER01',
                        remaining: 1,
                    },
                ],
            }),
        }),
        {
            initialRoute: 'public/booking',
            authenticated: false,
            bookingSlotsUrl: '/his/Booking/Slots',
            bookingMinDate: '2026-11-12',
        },
    );
    await client.run("catalog.status='ready'; guestBooking.doctorId='17'; loadGuestSlots();");
    assert.equal(client.run('guestBooking.slots.length'), 1);
    assert.match(client.app.innerHTML, /Không cần đăng nhập/);
    assert.match(client.app.innerHTML, /08:00/);
    assert.doesNotMatch(client.app.innerHTML, /Đặt lịch thành công/);
});

test('guest booking failure never invents time slots or a successful receipt', async () => {
    const client = createClient(
        async () => ({ ok: false, json: async () => ({ message: 'Server unavailable' }) }),
        { initialRoute: 'public/booking', authenticated: false, bookingMinDate: '2026-11-12' },
    );
    await client.run("catalog.status='ready'; guestBooking.doctorId='17'; loadGuestSlots();");
    assert.equal(client.run('guestBooking.slots.length'), 0);
    assert.equal(client.run('guestBooking.receipt'), null);
    assert.match(client.app.innerHTML, /Server unavailable/);
});

test('confirmation sends CSRF and displays the server-issued appointment code', async () => {
    const client = createClient(
        async (url, options) => {
            assert.equal(url, '/his/Booking/Confirm');
            assert.equal(options.method, 'POST');
            assert.equal(options.headers.RequestVerificationToken, 'test-token');
            assert.equal(JSON.parse(options.body).scheduleId, 7);
            return {
                ok: true,
                json: async () => ({
                    receipt: {
                        appointmentCode: 'SERVER-CODE',
                        patientName: '<Server patient>',
                        doctorName: 'Server doctor',
                        department: 'Server department',
                        room: 'SERVER01',
                        startTime: '2026-11-12T08:00:00',
                        endTime: '2026-11-12T08:15:00',
                    },
                }),
            };
        },
        {
            initialRoute: 'public/booking',
            authenticated: false,
            bookingConfirmUrl: '/his/Booking/Confirm',
        },
    );
    await client.run(
        "guestBooking.slots=[{scheduleId:7,startTime:'2026-11-12T08:00:00'}];guestBooking.selected=slotKey(guestBooking.slots[0]);submitGuestBooking();",
    );
    assert.match(client.app.innerHTML, /SERVER-CODE/);
    assert.match(client.app.innerHTML, /&lt;Server patient&gt;/);
    assert.equal(client.run('guestBooking.submitting'), false);
});

test('failed confirmation preserves entered details and shows no receipt', async () => {
    const client = createClient(
        async () => ({ ok: false, json: async () => ({ message: 'Slot filled' }) }),
        { initialRoute: 'public/booking', authenticated: false },
    );
    await client.run(
        "guestBooking.patient.fullName='Entered name';guestBooking.slots=[{scheduleId:7,startTime:'2026-11-12T08:00:00'}];guestBooking.selected=slotKey(guestBooking.slots[0]);submitGuestBooking();",
    );
    assert.equal(client.run('guestBooking.receipt'), null);
    assert.equal(client.run('guestBooking.patient.fullName'), 'Entered name');
    assert.match(client.app.innerHTML, /Slot filled/);
});

test('carousel exposes three images and respects reduced motion', () => {
    const client = createClient(undefined, { initialRoute: 'public/home', authenticated: false });
    client.run("window.matchMedia=()=>({matches:true});catalog.status='ready';render();");
    assert.equal((client.app.innerHTML.match(/data-hero-slide/g) || []).length, 3);
    assert.equal(client.run('ui.heroPaused'), true);
    assert.doesNotMatch(client.app.innerHTML, /data-act="hero-pause"/);
    assert.match(client.run('doctorPhoto()'), /Ảnh minh hoạ/);
});

test('patient profile screen renders personal form and family navigation', () => {
    const client = createClient(undefined, { role: 'PATIENT', authenticated: true, displayName: 'Bùi Gia Phúc' });
    client.run("ui.route = 'patient/profile'; render();");
    assert.match(client.app.innerHTML, /Thông tin cá nhân/);
    assert.match(client.app.innerHTML, /Thông tin người thân/);
    assert.match(client.app.innerHTML, /Thay ảnh/);
});

test('booking confirmation step displays two-column summary and policy checkbox', () => {
    const client = createClient(undefined, { initialRoute: 'public/booking', authenticated: false });
    client.run(`
        Date.now = () => new Date('2026-11-11T00:00:00Z').getTime();
        catalog = { status: 'ready', departments: [], doctors: [{ id: 17, name: 'BS. Lê Minh', department: 'Nội' }] };
        guestBooking.doctorId = '17';
        guestBooking.date = '2026-11-12';
        guestBooking.slots = [{ scheduleId: 1, startTime: '2026-11-12T08:00:00', endTime: '2026-11-12T08:30:00', room: 'P101', remaining: 2 }];
        guestBooking.selected = slotKey(guestBooking.slots[0]);
        guestBooking.patient = { fullName: 'Nguyễn Văn An', dateOfBirth: '1990-01-01', phoneNumber: '0912345678', email: 'an@example.com' };
        proceedToConfirmation();
    `);
    assert.equal(client.run('guestBooking.step'), 'confirm');
    assert.match(client.app.innerHTML, /Xác nhận đặt lịch/);
    assert.match(client.app.innerHTML, /Thông tin lịch hẹn/);
    assert.match(client.app.innerHTML, /Thông tin người khám/);
    assert.match(client.app.innerHTML, /Chính sách bảo vệ dữ liệu cá nhân/);
});

test('booking form requires valid email and blocks progression to confirm step without it', () => {
    const client = createClient(undefined, { initialRoute: 'public/booking', authenticated: false });
    client.run(`
        Date.now = () => new Date('2026-11-11T00:00:00Z').getTime();
        catalog = { status: 'ready', departments: [], doctors: [{ id: 17, name: 'BS. Lê Minh', department: 'Nội' }] };
        guestBooking.status = 'ready';
        guestBooking.doctorId = '17';
        guestBooking.date = '2026-11-12';
        guestBooking.slots = [{ scheduleId: 1, startTime: '2026-11-12T08:00:00', endTime: '2026-11-12T08:30:00', room: 'P101', remaining: 2 }];
        guestBooking.selected = slotKey(guestBooking.slots[0]);
        guestBooking.patient = { fullName: 'Nguyễn Văn An', dateOfBirth: '1990-01-01', phoneNumber: '0912345678', email: '' };
        proceedToConfirmation();
    `);
    assert.equal(client.run('guestBooking.step'), 'form');
    assert.match(client.run('guestBooking.error'), /Email/);
});

test('guest booking submit event navigates to confirmation step instead of immediately calling submitGuestBooking', () => {
    const client = createClient(undefined, { initialRoute: 'public/booking', authenticated: false });
    client.run(`
        Date.now = () => new Date('2026-11-11T00:00:00Z').getTime();
        bindEvents();
        catalog = { status: 'ready', departments: [], doctors: [{ id: 17, name: 'BS. Lê Minh', department: 'Nội' }] };
        guestBooking.status = 'ready';
        guestBooking.doctorId = '17';
        guestBooking.date = '2026-11-12';
        guestBooking.slots = [{ scheduleId: 1, startTime: '2026-11-12T08:00:00', endTime: '2026-11-12T08:30:00', room: 'P101', remaining: 2 }];
        guestBooking.selected = slotKey(guestBooking.slots[0]);
        guestBooking.patient = { fullName: 'Nguyễn Văn An', dateOfBirth: '1990-01-01', phoneNumber: '0912345678', email: 'an@example.com' };
    `);
    let prevented = false;
    client.listeners.submit({
        target: { matches: (sel) => sel === '[data-guest-booking]' },
        preventDefault: () => { prevented = true; },
    });
    assert.equal(prevented, true);
    assert.equal(client.run('guestBooking.step'), 'confirm');
    assert.equal(client.run('guestBooking.receipt'), null);
});
