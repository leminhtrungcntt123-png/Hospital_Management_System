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
    await client.run(`
        Date.now = () => new Date('2026-11-11T00:00:00Z').getTime();
        catalog.status='ready'; guestBooking.doctorId='17'; loadGuestSlots();
    `);
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

test('doctor portraits are distinct local assets across system and illustrative profiles', () => {
    const client = createClient();
    const portraits = client.run(`[
        ...Object.keys(doctorIllustrations).map(name => doctorPhoto({ name })),
        ...doctorIntroductions.map(doctorPhoto),
    ].map(markup => markup.match(/src="([^"]+)"/)[1])`);
    assert.equal(portraits.length, 16);
    assert.equal(new Set(portraits).size, 16);
    const imageContents = portraits.map((url) =>
        readFileSync(resolve(root, 'src/HIS.Web/wwwroot/ui/assets', url.split('/').pop())).toString(
            'base64',
        ),
    );
    assert.equal(new Set(imageContents).size, 16);
});

test('doctor pagination shows four profiles per page and clamps the last page', () => {
    const client = createClient(undefined, { authenticated: false });
    client.run(`catalog = { status: 'ready', departments: [], doctors:
        [1, 2, 3, 4].map(id => ({ id, name: 'Server doctor ' + id, department: 'Nội' }))
    };`);
    const first = client.run('doctorsPage()');
    assert.equal((first.match(/class="doctor-card"/g) || []).length, 4);
    assert.match(first, /Server doctor 1/);
    assert.doesNotMatch(first, /Nguyễn Minh Khánh/);
    assert.match(first, /aria-label="Trang trước" aria-disabled="true"/);
    client.run('ui.doctorPage = 2;');
    const second = client.run('doctorsPage()');
    assert.equal((second.match(/class="doctor-card"/g) || []).length, 4);
    assert.match(second, /Nguyễn Minh Khánh/);
    assert.doesNotMatch(second, /Server doctor 1/);
    client.run('ui.doctorPage = 999;');
    const last = client.run('doctorsPage()');
    assert.match(last, /13–16/);
    assert.match(last, /Đinh Mỹ Hạnh/);
    assert.match(last, /aria-label="Trang sau" aria-disabled="true"/);
    client.run("ui.query = 'no-matching-doctor';");
    assert.match(client.run('doctorsPage()'), /Không tìm thấy bác sĩ/);
    assert.doesNotMatch(client.run('doctorsPage()'), /class="catalog-pagination"/);
    client.run("catalog.status = 'loading'; ui.query = '';");
    assert.match(client.run('doctorsPage()'), /Đang tải thông tin/);
    assert.doesNotMatch(
        client.run('doctorsPage()'),
        /class="doctor-card"|class="catalog-pagination"/,
    );
});

test('numbered doctor routes preserve filters and reject invalid page numbers', () => {
    const client = createClient(undefined, { initialRoute: 'public/home', authenticated: false });
    client.run("catalog.status = 'ready'; go('public/doctors?page=3');");
    assert.equal(client.run('ui.doctorPage'), 3);
    client.run("ui.query = 'Nam & Hoa'; ui.department = 'Nội tổng quát';");
    const pagination = client.run('doctorPagination(2, 4)');
    assert.match(pagination, /q=Nam\+%26\+Hoa/);
    assert.match(pagination, /department=N%E1%BB%99i\+t%E1%BB%95ng\+qu%C3%A1t/);
    assert.match(pagination, /aria-label="Trang 2" aria-current="page"/);
    for (const page of ['0', '-1', 'NaN', 'Infinity', '1.5']) {
        client.run(`go('public/doctors?page=${page}');`);
        assert.equal(client.run('ui.doctorPage'), 1);
    }
});

test('doctor search and department changes return to page one', () => {
    const client = createClient(undefined, { initialRoute: 'public/home', authenticated: false });
    client.run(
        "catalog.status = 'ready'; ui.route = 'public/doctors'; bindEvents(); ui.doctorPage = 3;",
    );
    client.listeners.input({
        target: {
            matches: (selector) => selector === '[data-search]',
            value: 'Khánh',
            selectionStart: 5,
        },
    });
    assert.equal(client.run('ui.doctorPage'), 1);
    assert.match(client.app.innerHTML, /Nguyễn Minh Khánh/);
    client.run('ui.doctorPage = 4;');
    client.listeners.change({
        target: {
            matches: (selector) => selector === '[data-department]',
            value: 'Nhi khoa',
        },
    });
    assert.equal(client.run('ui.doctorPage'), 1);
    assert.equal(client.run('ui.department'), 'Nhi khoa');
});

test('home doctor numbers sit below the list and change pages without leaving home', () => {
    const client = createClient(undefined, { initialRoute: 'public/home', authenticated: false });
    client.run(`catalog = { status: 'ready', departments: [], doctors:
        [1, 2, 3, 4].map(id => ({ id, name: 'Server doctor ' + id, department: 'Nội' }))
    };`);
    const first = client.run('homePage()');
    assert.equal((first.match(/class="doctor-card"/g) || []).length, 4);
    assert.ok(
        first.indexOf('class="catalog-pagination"') > first.lastIndexOf('class="doctor-card"'),
    );
    assert.match(first, /data-route="public\/home\?[^\"]*page=2"/);
    client.run("go('public/home?page=3');");
    assert.equal(client.run('ui.route'), 'public/home');
    assert.match(client.app.innerHTML, /Đặng Anh Tuấn/);
    assert.doesNotMatch(client.app.innerHTML, /Server doctor 1/);
    assert.equal(client.navigations.length, 0);
});

test('all existing illustrated doctors display distinct labelled biographies and publications', () => {
    const client = createClient(undefined, { authenticated: false });
    const profiles = client.run(`[
        ...Object.keys(doctorIllustrations).map(name => ({ name })),
        ...doctorIntroductions,
    ].map(doctor => ({
        career: doctorCareer(doctor),
        card: doctorCard(doctor),
        profile: doctorProfile(doctor, doctor.kind === 'illustration'),
    }))`);
    assert.equal(profiles.length, 16);
    assert.equal(new Set(profiles.map(({ career }) => career.introduction)).size, 16);
    assert.equal(new Set(profiles.flatMap(({ career }) => career.publications)).size, 48);
    for (const { career, card, profile } of profiles) {
        assert.ok(career.degree.length > 0);
        assert.ok(career.years > 0);
        assert.ok(career.introduction.length > 0);
        assert.equal(career.publications.length, 3);
        assert.ok(profile.includes(career.introduction));
        for (const title of career.publications) {
            assert.ok(profile.includes(title));
        }
        assert.match(card, /Học vấn/);
        assert.match(card, /Kinh nghiệm/);
        assert.match(card, /\(minh hoạ\)/);
        assert.match(profile, /Quá trình đào tạo/);
        assert.match(profile, /Kinh nghiệm làm việc/);
        assert.equal(
            (profile.match(/<details class="doctor-career-section" open>/g) || []).length,
            4,
        );
        assert.match(profile, /chưa phải thông tin đã xác minh/);
        assert.match(profile, /Nội dung minh hoạ/);
        assert.match(profile, /không phải tài liệu y khoa đã công bố/);
    }
});

test('unmapped server doctors show pending career fields instead of invented qualifications', () => {
    const client = createClient(undefined, { authenticated: false });
    assert.equal(client.run("doctorCareer({ id: 99, name: 'New server doctor' })"), null);
    const profile = client.run("doctorProfile({ id: 99, name: 'New server doctor' }, false)");
    assert.match(profile, /Chưa cập nhật/);
    assert.match(profile, /sẽ hiển thị khi bệnh viện cung cấp thông tin/);
    assert.doesNotMatch(profile, /năm kinh nghiệm chuyên môn|Đào tạo chuyên sâu/);
    assert.match(profile, /Danh sách công bố và đề tài nghiên cứu sẽ được bổ sung/);
    assert.doesNotMatch(profile, /Sổ tay thực hành|Đề tài nghiên cứu:/);
    assert.match(profile, /doctorId=99/);
});

test('doctor profile labels fictional research and leaves awards pending', () => {
    const client = createClient(undefined, { authenticated: false });
    const profile = client.run('doctorProfile(doctorIntroductions[0], true)');
    assert.match(profile, /class="doctor-profile-breadcrumb"/);
    assert.match(profile, /class="doctor-profile-identity"/);
    assert.match(profile, /<h1 tabindex="-1">BS\. Nguyễn Minh Khánh<\/h1>/);
    assert.match(profile, /Chức vụ/);
    assert.match(profile, /Nơi làm việc/);
    assert.match(profile, /Số năm kinh nghiệm/);
    assert.match(profile, /Sách, báo, công trình nghiên cứu/);
    assert.match(profile, /Giải thưởng và ghi nhận/);
    assert.match(profile, /các tên sách, bài viết và đề tài dưới đây là giả định/);
    assert.match(profile, /Thông tin giải thưởng và ghi nhận đang được cập nhật/);
    assert.doesNotMatch(profile, /public\/booking/);
});

test('illustrative profiles never enter booking choices or expose a booking action', () => {
    const client = createClient(undefined, { authenticated: false });
    client.run(`catalog = { status: 'ready', departments: [], doctors: [
        { id: 17, name: 'Server doctor', specialty: 'Nội', department: 'Server department' }
    ] };`);
    const catalogBefore = client.run('JSON.stringify(catalog)');
    const introduction = client.run("doctorDetail('intro-khanh')");
    assert.match(introduction, /Hồ sơ minh hoạ/);
    assert.doesNotMatch(introduction, /public\/booking/);
    assert.match(client.run('doctorCard(catalog.doctors[0])'), /doctorId=17/);
    const booking = client.run('guestBookingPage()');
    assert.match(booking, /Server doctor/);
    assert.doesNotMatch(booking, /intro-khanh|Nguyễn Minh Khánh|Lê Phương Linh/);
    assert.equal(client.run('JSON.stringify(catalog)'), catalogBefore);
});

test('specialty introduction links and illustrative doctor filters work without server data', () => {
    const client = createClient(undefined, { authenticated: false });
    client.run("catalog.status='ready'; ui.department='Nhi khoa'; ui.query='Khánh';");
    const filtered = client.run('doctorsPage()');
    assert.match(filtered, /Nguyễn Minh Khánh/);
    assert.doesNotMatch(filtered, /Lê Phương Linh/);
    assert.doesNotMatch(filtered, /Không tìm thấy bác sĩ/);
    for (const id of client.run('specialtyIntroductions.map(specialty => specialty.id)')) {
        client.run(`ui.route='public/specialty/${id}';`);
        assert.match(client.run('publicContent()'), /Nội dung giới thiệu minh hoạ/);
        assert.doesNotMatch(client.run('publicContent()'), /Không tìm thấy chuyên khoa/);
    }
    assert.match(
        client.run("specialtyIntroductionDetail('unknown')"),
        /Không tìm thấy chuyên khoa/,
    );
});

test('patient profile screen renders personal form and family navigation', () => {
    const client = createClient(undefined, {
        role: 'PATIENT',
        authenticated: true,
        displayName: 'Bùi Gia Phúc',
    });
    client.run("ui.route = 'patient/profile'; render();");
    assert.match(client.app.innerHTML, /Thông tin cá nhân/);
    assert.match(client.app.innerHTML, /Thông tin người thân/);
    assert.match(client.app.innerHTML, /Thay ảnh/);
});

test('booking confirmation step displays two-column summary and policy checkbox', () => {
    const client = createClient(undefined, {
        initialRoute: 'public/booking',
        authenticated: false,
    });
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
    const client = createClient(undefined, {
        initialRoute: 'public/booking',
        authenticated: false,
    });
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
    const client = createClient(undefined, {
        initialRoute: 'public/booking',
        authenticated: false,
    });
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
        preventDefault: () => {
            prevented = true;
        },
    });
    assert.equal(prevented, true);
    assert.equal(client.run('guestBooking.step'), 'confirm');
    assert.equal(client.run('guestBooking.receipt'), null);
});
