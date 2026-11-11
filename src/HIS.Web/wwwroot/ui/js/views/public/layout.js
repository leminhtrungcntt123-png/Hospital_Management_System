function brand() {
    return '<span class="brand"><span class="brand-mark">+</span><span>MediCare<sup>+</sup></span></span>';
}

function headerUserActions() {
    const booking = link('Đặt lịch khám', 'public/booking', 'calendar', 'btn primary header-book');
    if (!config.authenticated) return link('Đăng nhập', 'account/login', '', 'btn ghost') + booking;
    const action =
        config.role === 'PATIENT'
            ? booking
            : `<a class="btn primary header-book" href="${esc(config.home)}">${icon('grid')}Bàn làm việc</a>`;
    return `${action}<button class="patient-account" type="button" data-act="account-menu" aria-label="Mở tài khoản ${esc(config.displayName)}">
        ${avatar(config.displayName)}<span>${esc(config.displayName)}</span>${icon('down')}</button>`;
}

function publicShell() {
    const patientArea = ui.route.startsWith('patient/');
    const canBook = !config.authenticated || config.role === 'PATIENT';
    const menu = [
        ['public/home', 'Trang chủ'],
        ['public/specialties', 'Chuyên khoa'],
        ['public/doctors', 'Tìm bác sĩ'],
        ['public/guide', 'Hướng dẫn khám'],
    ];
    const patientMenu = [
        ['public/booking', 'Đặt lịch khám', 'calendar'],
        ['patient/appointments', 'Lịch hẹn', 'clock'],
        ['patient/family', 'Người khám', 'users'],
        ['patient/records', 'Hồ sơ sức khỏe', 'clipboard'],
        ['patient/notifications', 'Thông báo', 'bell'],
    ];
    return `<div class="public-app hospital-portal">
        <a class="skip-link" href="#portal-main">Đến nội dung chính</a>
        <div class="utility-bar"><div><span>${icon('building')}Bệnh viện MediCare</span><span>Chăm sóc bạn và gia đình</span>
            <a href="${esc(config.authenticated ? config.home : config.login)}">${icon('shield')}Khu vực nhân viên</a></div></div>
        <header class="public-header"><div class="header-inner">
            ${link(brand(), 'public/home', '', 'brand-link')}
            <nav id="portal-navigation" class="public-nav ${ui.mobileMenu ? 'open' : ''}" aria-label="Điều hướng chính">
                ${menu.map(([route, title]) => link(title, route, '', ui.route === route ? 'active' : '')).join('')}
                ${canBook ? link('Đặt lịch khám', 'public/booking', 'calendar', 'nav-mobile menu-book') : ''}
                ${!config.authenticated ? link('Đăng nhập', 'account/login', '', 'nav-mobile') : ''}
            </nav>
            <div class="public-header-actions">${headerUserActions()}
                ${button('', 'menu', 'menu', 'icon-btn mobile-only', `aria-label="Mở menu" aria-controls="portal-navigation" aria-expanded="${ui.mobileMenu}"`)}
            </div></div></header>
        ${patientArea ? `<nav class="patient-nav" aria-label="Không gian bệnh nhân"><div>${patientMenu.map(([route, title, name]) => link(title, route, name, ui.route === route ? 'active' : '')).join('')}</div></nav>` : ''}
        <main id="portal-main" tabindex="-1" class="${patientArea ? 'patient-main' : 'public-main'}">${publicContent()}</main>
        <footer class="public-footer"><div class="footer-top">
            <div>${brand()}<p>Một nơi kết nối lịch hẹn, người khám<br />và hành trình chăm sóc sức khỏe.</p></div>
            <div><h3>Khám tại MediCare</h3>${link('Chuyên khoa', 'public/specialties')}${link('Đội ngũ bác sĩ', 'public/doctors')}${link('Hướng dẫn khám', 'public/guide')}</div>
            <div><h3>${canBook ? 'Dành cho người khám' : 'Không gian nhân viên'}</h3>${canBook ? link('Đặt lịch khám', 'public/booking') + link('Lịch hẹn của tôi', 'patient/appointments') + link('Hồ sơ sức khỏe', 'patient/records') : `<a href="${esc(config.home)}">Về bàn làm việc</a><a href="${esc(config.changePassword)}">Đổi mật khẩu</a>`}</div>
            <div><h3>Tài khoản</h3>${config.authenticated ? button('Quản lý tài khoản', 'account-menu', 'shield', 'footer-account') : link('Đăng nhập', 'account/login') + link('Đăng ký người khám', 'account/register')}<p>Thông tin cá nhân được hiển thị<br />theo quyền của tài khoản.</p></div>
        </div><div class="footer-bottom"><span>© ${new Date().getFullYear()} MediCare · Hệ thống thông tin bệnh viện</span><span>Đồng hành trong từng cuộc hẹn</span></div></footer>
        ${
            patientArea
                ? `<nav class="patient-mobile-nav" aria-label="Điều hướng bệnh nhân trên điện thoại">${patientMenu
                      .slice(0, 4)
                      .map(([route, title, name]) =>
                          link(title, route, name, ui.route === route ? 'active' : ''),
                      )
                      .join('')}</nav>`
                : ''
        }
    </div>`;
}

function publicContent() {
    if (ui.route.startsWith('public/doctor/')) return doctorDetail(ui.route.split('/')[2]);
    switch (ui.route) {
        case 'public/booking':
        case 'patient/booking':
            return guestBookingPage();
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
        case 'patient/appointments':
            return appointmentsPage();
        case 'patient/profile':
            return personalProfilePage();
        case 'patient/family':
            return familyPage();
        case 'patient/records':
            return recordsPage();
        case 'patient/notifications':
            return notificationsPage();
        default:
            return empty(
                'Không tìm thấy trang',
                'Liên kết này không còn khả dụng.',
                link('Về trang chủ', 'public/home', 'arrow', 'btn outline'),
            );
    }
}
