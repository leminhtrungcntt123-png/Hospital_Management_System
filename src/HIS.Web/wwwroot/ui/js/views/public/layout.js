function brand() {
    return '<span class="brand"><span class="brand-mark">+</span><span>MediCare<sup>+</sup></span></span>';
}

function headerUserActions() {
    const booking = link(
        'Đặt lịch khám',
        'patient/booking',
        'calendar',
        'btn primary header-book',
    );
    if (!config.authenticated)
        return link('Đăng nhập', 'account/login', '', 'btn ghost') + booking;
    const workspace =
        config.role === 'PATIENT'
            ? booking
            : `<a class="btn primary header-book" href="${esc(config.home)}">${icon('grid')}Không gian làm việc</a>`;
    return (
        workspace +
        `<button class="patient-account" data-act="account-menu" aria-label="Mở tài khoản ${esc(config.displayName)}">
        ${avatar(config.displayName)}<span>${esc(config.displayName)}</span>${icon('down')}
    </button>`
    );
}

function publicShell() {
    const patientArea = ui.route.startsWith('patient/');
    const menu = [
        ['public/home', 'Trang chủ'],
        ['public/specialties', 'Chuyên khoa'],
        ['public/doctors', 'Bác sĩ'],
        ['public/services', 'Dịch vụ'],
        ['public/guide', 'Hướng dẫn'],
    ];
    const patientMenu = [
        ['patient/appointments', 'Lịch hẹn', 'calendar'],
        ['patient/family', 'Người khám', 'users'],
        ['patient/records', 'Hồ sơ & kết quả', 'clipboard'],
        ['patient/notifications', 'Thông báo', 'bell'],
    ];
    return `<div class="public-app">
        <div class="utility-bar"><div><span>${icon('building')}MediCare</span>
            <span>Cổng thông tin bệnh viện</span><a href="${esc(config.authenticated ? config.home : config.login)}">${config.authenticated ? 'Tài khoản của tôi' : 'Khu vực nhân viên'}</a>
        </div></div>
        <header class="public-header"><div class="header-inner">
            ${link(brand(), 'public/home', '', 'brand-link')}
            <nav class="public-nav ${ui.mobileMenu ? 'open' : ''}" aria-label="Điều hướng chính">
                ${menu.map(([route, title]) => link(title, route, '', ui.route === route ? 'active' : '')).join('')}
            </nav>
            <div class="public-header-actions">${headerUserActions()}
                ${button('', 'menu', 'menu', 'icon-btn mobile-only', `aria-label="Mở menu" aria-expanded="${ui.mobileMenu}"`)}
            </div>
        </div></header>
        ${
            patientArea
                ? `<nav class="patient-nav" aria-label="Không gian bệnh nhân"><div>
            ${patientMenu.map(([route, title, name]) => link(title, route, name, ui.route === route ? 'active' : '')).join('')}
        </div></nav>`
                : ''
        }
        <main class="${patientArea ? 'patient-main' : 'public-main'}">${publicContent()}</main>
        <footer class="public-footer"><div class="footer-top">
            <div>${brand()}<p>Chủ động đặt khám.<br />Kết nối hành trình chăm sóc sức khỏe.</p></div>
            <div><h3>Khám phá</h3>${link('Chuyên khoa', 'public/specialties')}${link('Đội ngũ bác sĩ', 'public/doctors')}${link('Dịch vụ y tế', 'public/services')}</div>
            <div><h3>Dành cho bạn</h3>${link('Đặt lịch khám', 'patient/booking')}${link('Hồ sơ người khám', 'patient/family')}${link('Hướng dẫn khám', 'public/guide')}</div>
            <div><h3>Tài khoản</h3><a href="${esc(config.authenticated ? config.home : config.login)}">Không gian làm việc</a></div>
        </div><div class="footer-bottom"><span>© ${new Date().getFullYear()} MediCare · Hệ thống thông tin bệnh viện</span></div></footer>
        ${
            patientArea
                ? `<nav class="patient-mobile-nav" aria-label="Điều hướng bệnh nhân trên điện thoại">
            ${[
                ['patient/booking', 'Đặt khám', 'calendar'],
                ...patientMenu.slice(0, 3),
            ]
                .map(([route, title, name]) =>
                    link(
                        title,
                        route,
                        name,
                        ui.route === route ? 'active' : '',
                    ),
                )
                .join('')}
        </nav>`
                : ''
        }
    </div>`;
}

function publicContent() {
    if (ui.route.startsWith('public/doctor/'))
        return doctorDetail(ui.route.split('/')[2]);
    switch (ui.route) {
        case 'public/home':
            return homePage();
        case 'public/doctors':
            return doctorsPage();
        case 'public/specialties':
            return specialtiesPage();
        case 'public/services':
            return servicesPage();
        case 'public/guide':
            return guidePage();
        case 'patient/booking':
            return bookingPage();
        case 'patient/appointments':
            return appointmentsPage();
        case 'patient/family':
            return familyPage();
        case 'patient/records':
            return recordsPage();
        case 'patient/notifications':
            return notificationsPage();
        default:
            return empty(
                'Không tìm thấy trang',
                '',
                link('Về trang chủ', 'public/home', 'arrow', 'btn outline'),
            );
    }
}
