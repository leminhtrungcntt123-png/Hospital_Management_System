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
    .map((match) =>
        readFileSync(resolve(root, 'src/HIS.Web/wwwroot', match[1]), 'utf8'),
    )
    .join('\n');

function createClient(fetch) {
    const app = { innerHTML: '' };
    const listeners = {};
    const navigations = [];
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
    };
    const context = vm.createContext({
        document: {
            title: '',
            getElementById(id) {
                if (id === 'his-ui-config')
                    return { textContent: JSON.stringify(config) };
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
                if (selector === '#overlay-root')
                    return { replaceChildren() {} };
                return null;
            },
            addEventListener(name, handler) {
                listeners[name] = handler;
            },
            body: { style: {} },
        },
        window: { addEventListener() {}, scrollTo() {} },
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
                departments: [
                    { id: 4, name: 'Server department', description: '' },
                ],
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
        'patient/booking',
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
    assert.equal(client.listeners.submit, undefined);
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
