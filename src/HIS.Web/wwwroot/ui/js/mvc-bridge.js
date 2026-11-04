/* ASP.NET MVC integration. This file never creates authentication cookies or grants roles. */
(() => {
    const config = JSON.parse(document.getElementById('his-ui-config').textContent);
    window.hisUiConfig = config;
    const initial = config.initialRoute || 'public/home';
    const localGo = go,
        localRender = render,
        localSwitchRole = switchRole,
        localBeginBooking = beginBooking;
    const entries = {
        'patient/booking': '/Patient/Booking',
        'patient/appointments': '/Patient/MyAppointments',
        'patient/family': '/Patient/FamilyProfiles',
        'patient/records': '/Patient/MedicalRecords',
        'patient/notifications': '/Patient/Notifications',
        'staff/reception': '/Staff/Reception',
        'staff/cashier': '/Staff/Cashier',
        'staff/consultation': '/Staff/Consultation',
        'admin/accounts': '/Admin/Accounts',
        'admin/catalog': '/Admin/Catalog',
        'account/login': '/Account/Login',
        'account/register': '/Account/Register',
        'account/settings': '/Account/ChangePassword',
        'account/denied': '/Account/AccessDenied',
    };
    const mvcUrl = (route) => config.pathBase + (entries[route] || '/Workspace/' + route);
    const protectedRoute = (route) => /^(patient|staff|admin|account)\//.test(route);
    const storageKey = 'his-mvc-ui-navigation';

    window.configureHisUi = () => {
        if (config.preview) {
            session.authenticated = true;
            return;
        }
        const role = window.hisLive
            ? db.session.role
            : roleInfo[config.role]
              ? config.role
              : 'PATIENT';
        session = window.hisLive
            ? db.session
            : { role, accountId: roleInfo[role].accountId, authenticated: config.authenticated };
        if (window.hisLive) {
            Object.assign(CONFIG, db.options || {});
            ui.booking.patientId = selfPatient() || myLinks()[0]?.patientId || '';
            ui.booking.doctorId = db.doctors.find((d) => d.active)?.id || '';
            ui.booking.date = workingDays()[1] || workingDays()[0] || TODAY;
        }
        if (!window.hisLive && config.displayName) acc().name = config.displayName;
        ui.profileId = selfPatient() || 'BN0001';
        try {
            const pending = JSON.parse(sessionStorage.getItem(storageKey));
            sessionStorage.removeItem(storageKey);
            if (pending?.route === initial && pending.role === role) {
                if (pending.booking) ui.booking = { ...ui.booking, ...pending.booking };
                if (pending.selectedAppointment)
                    ui.selectedAppointment = pending.selectedAppointment;
                ui.filter = pending.filter || 'ALL';
            }
        } catch {
            /* Missing navigation state is harmless. */
        }
    };
    window.getHisInitialRoute = () => {
        const hash = location.hash.replace(/^#\//, '');
        if (config.preview) return hash || initial;
        return initial === 'public/home' && hash.startsWith('public/') ? hash : initial;
    };
    go = (route, options = {}) => {
        if (!config.preview && protectedRoute(route)) {
            persistDraft();
            try {
                sessionStorage.setItem(
                    storageKey,
                    JSON.stringify({
                        route,
                        role: session.role,
                        booking: window.hisLive
                            ? {
                                  step: ui.booking.step,
                                  patientId: ui.booking.patientId,
                                  doctorId: ui.booking.doctorId,
                                  date: ui.booking.date,
                                  shift: ui.booking.shift,
                                  parentId: ui.booking.parentId,
                              }
                            : ui.booking,
                        selectedAppointment: options.id || ui.selectedAppointment,
                        filter: options.filter,
                    })
                );
            } catch {}
            location.assign(mvcUrl(route));
            return;
        }
        localGo(route, options);
    };
    render = () => {
        localRender();
        if (config.preview) return;
        document.querySelectorAll('a[data-route]').forEach((a) => {
            if (protectedRoute(a.dataset.route)) a.href = mvcUrl(a.dataset.route);
        });
        document.querySelectorAll('.sidebar-bottom button[data-act="roles"]').forEach((b) => {
            b.textContent = 'Tài khoản';
        });
        document.querySelectorAll('.utility-demo').forEach((el) => {
            el.textContent = 'MediCare HIS';
        });
    };
    switchRole = (role) => {
        if (config.preview) return localSwitchRole(role);
        location.assign(config.authenticated ? config.home : config.login);
    };
    beginBooking = (...args) => {
        if (!config.preview && (!config.authenticated || config.role !== 'PATIENT')) {
            location.assign(config.authenticated ? config.denied : mvcUrl('patient/booking'));
            return;
        }
        return localBeginBooking(...args);
    };
    const accountMenu = () => {
        if (!config.authenticated) {
            location.assign(config.login);
            return;
        }
        openModal(
            'Tài khoản của bạn',
            config.displayName || 'Tài khoản HIS',
            /* HTML */ `<div class="menu-links">
                <a href="${esc(config.home)}">${icon('grid')}Không gian làm việc</a
                ><a href="${esc(config.changePassword)}">${icon('shield')}Đổi mật khẩu</a>
            </div>`,
            button('Đóng', 'close') + button('Đăng xuất', 'server-logout', 'logout', 'btn outline')
        );
    };
    document.addEventListener(
        'click',
        (event) => {
            if (config.preview) return;
            const target = event.target.closest('[data-act]');
            if (!target) return;
            const action = target.dataset.act;
            if (
                ![
                    'roles',
                    'role',
                    'account-menu',
                    'patient-portal',
                    'logout',
                    'server-logout',
                ].includes(action)
            )
                return;
            event.preventDefault();
            event.stopImmediatePropagation();
            if (action === 'patient-portal') {
                persistDraft();
                location.assign(config.pathBase + '/');
            } else if (action === 'logout' || action === 'server-logout')
                document.getElementById('his-server-logout').requestSubmit();
            else accountMenu();
        },
        true
    );
})();
