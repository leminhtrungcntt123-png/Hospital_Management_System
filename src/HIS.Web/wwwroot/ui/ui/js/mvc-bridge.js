const mvcEntries = {
    'patient/booking': '/Patient/Booking',
    'patient/appointments': '/Patient/MyAppointments',
    'patient/family': '/Patient/FamilyProfiles',
    'patient/records': '/Patient/MedicalRecords',
    'patient/notifications': '/Patient/Notifications',
    'staff/reception': '/Staff/Reception',
    'staff/cashier': '/Staff/Cashier',
    'staff/consultation': '/Staff/Consultation',
    'staff/schedule': '/Staff/Schedule',
    'admin/dashboard': '/Admin/Management',
    'admin/accounts': '/Admin/Accounts',
    'admin/catalog': '/Admin/Catalog',
    'account/login': '/Account/Login',
    'account/register': '/Account/Register',
    'account/settings': '/Account/ChangePassword',
};

function routeUrl(route) {
    return route.startsWith('public/')
        ? config.pathBase + '/#/' + route
        : config.pathBase + (mvcEntries[route] || '/Workspace/' + route);
}

function go(route) {
    if (!route.startsWith('public/') || config.initialRoute !== 'public/home') {
        location.assign(routeUrl(route));
        return;
    }
    closeModal();
    ui.route = route;
    ui.query = '';
    ui.department = '';
    ui.mobileMenu = false;
    location.hash = '#/' + route;
    render();
    window.scrollTo(0, 0);
    if (catalog.status === 'idle') loadCatalog();
}

async function loadCatalog() {
    catalog = { status: 'loading', doctors: [], departments: [] };
    render();
    try {
        const response = await fetch(config.catalogUrl, {
            credentials: 'same-origin',
            cache: 'no-store',
            headers: { Accept: 'application/json' },
        });
        if (!response.ok) throw new Error('Catalog request failed');
        const data = await response.json();
        if (!Array.isArray(data.doctors) || !Array.isArray(data.departments)) {
            throw new Error('Invalid catalog response');
        }
        catalog = {
            status: 'ready',
            doctors: data.doctors,
            departments: data.departments,
        };
    } catch {
        catalog = { status: 'error', doctors: [], departments: [] };
    }
    render();
}
