const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

test('AdminWorkspace has matching data-section and data-panel for every nav item', () => {
    const html = readFileSync(resolve(__dirname, '../../src/HIS.Web/Views/Shared/AdminWorkspace.cshtml'), 'utf8');
    const sections = [...html.matchAll(/<a\s+[^>]*data-section="([^"]+)"/g)].map(m => m[1]);
    const panels = [...html.matchAll(/data-panel="([^"]+)"/g)].map(m => m[1]);

    for (const section of sections) {
        assert.ok(panels.includes(section), `Section "${section}" has no matching data-panel in AdminWorkspace.cshtml!`);
    }
});

test('admin-management.js titles object covers all sections', () => {
    const js = readFileSync(resolve(__dirname, '../../src/HIS.Web/wwwroot/js/admin-management.js'), 'utf8');
    const html = readFileSync(resolve(__dirname, '../../src/HIS.Web/Views/Shared/AdminWorkspace.cshtml'), 'utf8');
    const sections = [...html.matchAll(/<a\s+[^>]*data-section="([^"]+)"/g)].map(m => m[1]);

    for (const section of sections) {
        assert.ok(js.includes(`${section}:`), `titles in admin-management.js missing key: "${section}"`);
    }
});

test('admin-management.js execution does not throw syntax or runtime errors', async () => {
    const js = readFileSync(resolve(__dirname, '../../src/HIS.Web/wwwroot/js/admin-management.js'), 'utf8');

    // Create a mock DOM environment
    const elements = new Map();
    function createMockElement(id, tag = 'div', dataset = {}) {
        return {
            id,
            tagName: tag.toUpperCase(),
            dataset,
            classList: {
                toggle: () => false,
                add: () => {},
                remove: () => {},
            },
            setAttribute: () => {},
            removeAttribute: () => {},
            addEventListener: () => {},
            querySelector: (sel) => {
                if (sel === 'input') return { value: 'test-token' };
                if (sel === 'form') return createMockElement('form', 'form');
                if (sel === '.form-error') return { textContent: '' };
                return null;
            },
            querySelectorAll: () => [],
            elements: {
                from: { value: '2026-11-01' },
                to: { value: '2026-11-06' },
                roleId: { value: '1' },
                departmentId: { value: '' },
            },
            innerHTML: '',
            textContent: '',
            hidden: false,
        };
    }

    const workspaceEl = createMockElement('workspace', 'div', {
        api: '/Admin/Management',
        section: 'dashboard'
    });
    workspaceEl.classList.add('admin-workspace');

    const domContext = {
        document: {
            querySelector: (sel) => {
                if (sel === '.admin-workspace') return workspaceEl;
                if (sel === '#admin-token input') return { value: 'token-123' };
                if (sel === '#report-filter') return createMockElement('report-filter', 'form');
                if (sel.startsWith('#')) return createMockElement(sel.slice(1));
                return createMockElement(sel.replace(/[^a-zA-Z0-9_-]/g, ''));
            },
            querySelectorAll: (sel) => {
                if (sel === '[data-panel]') {
                    return ['dashboard', 'requests', 'departments', 'staff', 'doctors', 'accounts', 'rooms', 'services', 'medications', 'audit']
                        .map(p => ({ dataset: { panel: p }, hidden: p !== 'dashboard' }));
                }
                if (sel === 'nav [data-section]') {
                    return ['dashboard', 'requests', 'departments', 'staff', 'doctors', 'accounts', 'rooms', 'services', 'medications', 'audit']
                        .map(s => ({
                            dataset: { section: s },
                            classList: { toggle: () => {} },
                            setAttribute: () => {},
                            removeAttribute: () => {}
                        }));
                }
                return [];
            },
            getElementById: (id) => createMockElement(id),
            addEventListener: () => {},
        },
        window: {
            addEventListener: () => {},
        },
        location: {
            hash: '#rooms',
        },
        fetch: async (url) => {
            return {
                ok: true,
                redirected: false,
                headers: {
                    get: (h) => h === 'content-type' ? 'application/json' : null,
                },
                json: async () => {
                    if (url.includes('CatalogDirectory')) return { rooms: [], services: [], serviceTypes: [], medications: [], departments: [] };
                    if (url.includes('Directory')) return { accounts: [], staff: [], roles: [], doctors: [], departments: [] };
                    if (url.includes('Report')) return { from: '2026-11-01', to: '2026-11-06', revenue: 0, visits: 0, deposits: 0, refunds: 0, days: [] };
                    if (url.includes('Requests')) return [];
                    if (url.includes('AuditLogs')) return [];
                    if (url.includes('DepartmentStats')) return [];
                    return {};
                }
            };
        },
        Intl,
        Date,
        Math,
        String,
        Number,
        Boolean,
        Array,
        Object,
        Promise,
        console,
        FormData: class { entries() { return []; } },
        URLSearchParams: class { toString() { return ''; } }
    };

    const script = new vm.Script(js);
    const context = vm.createContext(domContext);
    assert.doesNotThrow(() => {
        script.runInContext(context);
    });
    await new Promise((resolve) => setTimeout(resolve, 50));
});

test('AdminWorkspace has department filter dropdowns for rooms and departments', () => {
    const html = readFileSync(resolve(__dirname, '../../src/HIS.Web/Views/Shared/AdminWorkspace.cshtml'), 'utf8');
    assert.match(html, /id="filter-room-department"/, 'AdminWorkspace must have filter dropdown for rooms by department');
    assert.match(html, /id="filter-department"/, 'AdminWorkspace must have filter dropdown for departments');
    assert.match(html, /id="audit-filter"/, 'AdminWorkspace must have filter bar for audit log');
    assert.match(html, /id="audit-count"/, 'AdminWorkspace must have count element for audit events');
});
