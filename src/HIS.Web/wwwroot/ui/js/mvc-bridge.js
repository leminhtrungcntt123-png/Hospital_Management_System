const mvcEntries = {
    'patient/booking': '/Booking',
    'patient/appointments': '/Patient/MyAppointments',
    'patient/profile': '/Patient/FamilyProfiles?tab=personal',
    'patient/family': '/Patient/FamilyProfiles?tab=family',
    'patient/records': '/Patient/MedicalRecords',
    'patient/notifications': '/Patient/Notifications',
    'staff/reception': '/Staff/Reception',
    'staff/cashier': '/Staff/Cashier',
    'staff/consultation': '/Staff/Consultation',
    'staff/schedule':
        config.role === 'DOCTOR' ? '/Staff/Consultation?tab=schedule' : '/Staff/Schedule',
    'staff/labs': '/Staff/Consultation?tab=labs',
    'staff/records':
        config.role === 'DOCTOR'
            ? '/Staff/Consultation?tab=records'
            : '/Staff/Schedule?tab=records',
    'staff/exceptions':
        config.role === 'DOCTOR'
            ? '/Staff/Consultation?tab=exceptions'
            : '/Staff/Schedule?tab=exceptions',
    'admin/dashboard': '/Admin/Management',
    'admin/accounts': '/Admin/Accounts',
    'admin/catalog': '/Admin/Catalog',
    'account/login': '/Account/Login',
    'account/register': '/Account/Register',
    'account/settings': '/Account/ChangePassword',
};

function routeUrl(route) {
    const publicHome = config.publicHome || config.pathBase + '/';
    if (route === 'public/booking' || route.startsWith('public/booking?'))
        return (
            (config.bookingUrl || config.pathBase + '/Booking') +
            (route.includes('?') ? '?' + route.split('?')[1] : '')
        );
    if (route === 'public/home') return publicHome;
    if (config.role === 'DEPARTMENT_HEAD' && route === 'staff/dashboard')
        return config.pathBase + '/Staff/Schedule?tab=overview';
    if (config.role === 'DEPARTMENT_HEAD' && route === 'staff/patients')
        return config.pathBase + '/Staff/Schedule?tab=patients';
    if (route.startsWith('public/')) return publicHome + '#/' + route;
    const [page, query = ''] = route.split('?', 2);
    const entry = mvcEntries[page] || '/Workspace/' + page;
    return config.pathBase + entry + (query ? (entry.includes('?') ? '&' : '?') + query : '');
}

function go(route) {
    if (
        route.startsWith('public/booking') ||
        !route.startsWith('public/') ||
        config.initialRoute !== 'public/home'
    ) {
        location.assign(routeUrl(route));
        return;
    }
    closeModal();
    const [page, query = ''] = route.split('?', 2);
    const filters = new URLSearchParams(query);
    ui.route = page;
    ui.address = route;
    ui.query = filters.get('q') || '';
    ui.department = filters.get('department') || '';
    ui.mobileMenu = false;
    location.hash = '#/' + route;
    render();
    window.scrollTo(0, 0);
    document.querySelector('main h1')?.focus({ preventScroll: true });
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
