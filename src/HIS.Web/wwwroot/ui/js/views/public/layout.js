/* views / public / layout */
function publicShell() {
    const r = ui.route,
        privateArea = r.startsWith('patient/'),
        authPage = r === 'account/login' || r === 'account/register';
    const unread = db.notifications.filter(
        (n) => n.accountId === session.accountId && !n.read
    ).length;
    return /* HTML */ `<div class="public-app">
        <div class="utility-bar">
            <div>
                <span>${icon('building')}Bệnh viện An Tâm</span
                ><span class="utility-demo"><i></i>Phiên bản trải nghiệm HIS</span
                ><button data-act="roles">${icon('shield')}Khu vực nhân viên</button>
            </div>
        </div>
        <header class="public-header">
            <div class="header-inner">
                ${link(brand(), 'public/home', '', 'brand-link')}
                <nav
                    class="public-nav ${ui.mobileMenu ? 'open' : ''}"
                    aria-label="Điều hướng chính"
                >
                    ${[
                        ['public/home', 'Trang chủ'],
                        ['public/specialties', 'Chuyên khoa'],
                        ['public/doctors', 'Bác sĩ'],
                        ['public/services', 'Dịch vụ'],
                        ['public/guide', 'Hướng dẫn'],
                    ]
                        .map(([route, label]) =>
                            link(label, route, '', r === route ? 'active' : '')
                        )
                        .join('')}
                </nav>
                <div class="public-header-actions">
                    ${button('', 'search', 'search', 'icon-btn', 'aria-label="Tìm dịch vụ hoặc bác sĩ"')}${session.authenticated ? /* HTML */ `<button class="patient-account" data-act="account-menu" aria-label="Mở tài khoản ${esc(acc()?.name)}">${avatar(acc()?.name || 'Khách')}<span>${esc(acc()?.name?.split(' ').slice(-2).join(' ') || 'Tài khoản')}</span>${icon('down')}</button>` : link('Đăng nhập', 'account/login', '', 'btn ghost')}${link('Đặt lịch khám', 'patient/booking', 'calendar', 'btn primary header-book')}${button('', 'menu', 'menu', 'icon-btn mobile-only', 'aria-label="Mở menu"')}
                </div>
            </div>
        </header>
        ${
            privateArea
                ? /* HTML */ `<nav class="patient-nav" aria-label="Không gian bệnh nhân">
                      <div>
                          ${[
                              ['patient/appointments', 'calendar', 'Lịch hẹn'],
                              ['patient/family', 'users', 'Người khám'],
                              ['patient/records', 'clipboard', 'Hồ sơ & kết quả'],
                              [
                                  'patient/notifications',
                                  'bell',
                                  'Thông báo' + (unread ? ' <b>' + unread + '</b>' : ''),
                              ],
                          ]
                              .map(([route, ic, label]) =>
                                  link(label, route, ic, r === route ? 'active' : '')
                              )
                              .join('')}
                      </div>
                  </nav>`
                : ''
        }
        <main class="${authPage ? 'auth-main' : privateArea ? 'patient-main' : 'public-main'}">
            ${guardRoute() ? publicContent() : accessDenied()}
        </main>
        <footer class="public-footer">
            <div class="footer-top">
                <div>
                    ${brand()}
                    <p>Chủ động đặt khám.<br />Kết nối hành trình chăm sóc sức khỏe.</p>
                    <span class="demo-label"
                        >${window.hisLive ? 'Hình ảnh minh họa · Dữ liệu từ bệnh viện' : 'Dữ liệu, nhân sự và hình ảnh minh họa'}</span
                    >
                </div>
                <div>
                    <h3>Khám phá</h3>
                    ${link('Chuyên khoa', 'public/specialties')}${link('Đội ngũ bác sĩ', 'public/doctors')}${link('Dịch vụ y tế', 'public/services')}
                </div>
                <div>
                    <h3>Dành cho bạn</h3>
                    ${link('Đặt lịch khám', 'patient/booking')}${link('Hồ sơ người thân', 'patient/family')}${link('Hướng dẫn khám', 'public/guide')}
                </div>
                <div>
                    <h3>Giờ tiếp nhận mẫu</h3>
                    <p>Thứ Hai – Thứ Sáu<br />07:30 – 11:30 · 13:00 – 16:30</p>
                    <button class="footer-staff" data-act="roles">
                        Không gian nhân viên ${icon('arrow')}
                    </button>
                </div>
            </div>
            <div class="footer-bottom">
                <span>© 2026 MediCare · Mẫu hệ thống HIS</span>
                <div>
                    <button data-act="privacy">Quyền riêng tư</button
                    ><button data-act="guide-modal">Hướng dẫn trải nghiệm</button>
                </div>
            </div>
        </footer>
        ${
            privateArea
                ? /* HTML */ `<nav
                      class="patient-mobile-nav"
                      aria-label="Điều hướng bệnh nhân trên điện thoại"
                  >
                      ${[
                          ['patient/booking', 'calendar', 'Đặt khám'],
                          ['patient/appointments', 'clock', 'Lịch hẹn'],
                          ['patient/family', 'users', 'Người khám'],
                          ['patient/records', 'clipboard', 'Hồ sơ'],
                      ]
                          .map(([route, ic, label]) =>
                              link(label, route, ic, r === route ? 'active' : '')
                          )
                          .join('')}
                  </nav>`
                : ''
        }
    </div>`;
}

function brand() {
    return /* HTML */ `<span class="brand"
        ><span class="brand-mark">+</span><span>MediCare<sup>+</sup></span></span
    >`;
}

function publicContent() {
    if (ui.route.startsWith('public/service/')) return serviceDetail(ui.route.split('/')[2]);
    if (ui.route.startsWith('public/doctor/')) return doctorDetail(ui.route.split('/')[2]);
    switch (ui.route) {
        case 'public/specialties':
            return specialtiesPage();
        case 'public/doctors':
            return doctorsPage();
        case 'public/services':
            return servicesPage();
        case 'public/guide':
            return guidePage();
        case 'patient/booking':
            return bookingPage();
        case 'patient/appointments':
            return myAppointmentsPage();
        case 'patient/family':
            return familyPage();
        case 'patient/records':
            return myRecordsPage();
        case 'patient/notifications':
            return notificationsPage();
        case 'account/login':
            return loginPage();
        case 'account/register':
            return registerPage();
        case 'account/settings':
            return accountPage();
        default:
            return homePage();
    }
}
