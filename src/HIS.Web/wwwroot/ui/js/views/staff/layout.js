/* views / staff / layout */
const staffMenus = {
    NURSE: [
        [
            'CÔNG VIỆC HÔM NAY',
            [
                ['staff/dashboard', 'grid', 'Bàn làm việc'],
                ['staff/reception', 'users', 'Tiếp nhận & gọi số'],
                ['staff/vitals', 'pulse', 'Sinh hiệu'],
                ['staff/cashier', 'wallet', 'Thu phí & BHYT'],
            ],
        ],
        [
            'HỒ SƠ & LỊCH',
            [
                ['staff/patients', 'clipboard', 'Người khám'],
                ['staff/schedule', 'calendar', 'Lịch làm việc'],
            ],
        ],
    ],
    DOCTOR: [
        [
            'CHUYÊN MÔN',
            [
                ['staff/consultation', 'stethoscope', 'Khám & kê đơn'],
                ['staff/labs', 'pulse', 'Chỉ định & kết quả'],
                ['staff/records', 'clipboard', 'Bệnh án trong khoa'],
            ],
        ],
        [
            'CÁ NHÂN',
            [
                ['staff/schedule', 'calendar', 'Lịch khám & trực'],
                ['staff/exceptions', 'clock', 'Nghỉ / đổi ca'],
            ],
        ],
    ],
    DEPARTMENT_HEAD: [
        [
            'ĐIỀU PHỐI KHOA',
            [
                ['staff/schedule', 'calendar', 'Lịch khám & trực'],
                ['staff/exceptions', 'clock', 'Duyệt nghỉ / đổi ca'],
                ['staff/dashboard', 'grid', 'Tổng quan khoa'],
            ],
        ],
        [
            'THEO DÕI',
            [
                ['staff/records', 'clipboard', 'Bệnh án trong khoa'],
                ['staff/patients', 'users', 'Người khám'],
            ],
        ],
    ],
    ADMIN: [
        [
            'QUẢN TRỊ',
            [
                ['admin/dashboard', 'grid', 'Điều hành'],
                ['admin/accounts', 'shield', 'Tài khoản & quyền'],
                ['admin/catalog', 'clipboard', 'Danh mục hệ thống'],
            ],
        ],
        [
            'THEO DÕI',
            [
                ['admin/revenue', 'wallet', 'Doanh thu & thu phí'],
                ['admin/logs', 'clock', 'Nhật ký truy cập'],
            ],
        ],
    ],
};

function staffShell() {
    const unread = db.notifications.filter(
            (n) => n.accountId === session.accountId && !n.read
        ).length,
        counts = {
            'staff/reception': todayApps().filter((a) =>
                ['CONFIRMED', 'CHECKED_IN', 'READY_FOR_DOCTOR'].includes(a.status)
            ).length,
            'staff/vitals': todayApps().filter((a) => a.status === 'CHECKED_IN').length,
            'staff/consultation': todayApps().filter((a) =>
                ['READY_FOR_DOCTOR', 'IN_PROGRESS'].includes(a.status)
            ).length,
            'staff/labs': db.labOrders.filter(
                (l) => l.doctorId === acc()?.doctorId && l.status === 'PENDING'
            ).length,
            'staff/exceptions': db.exceptions.filter(
                (e) => e.status === 'PENDING' && doctor(e.doctorId)?.deptId === acc()?.deptId
            ).length,
        };
    return /* HTML */ `<div class="staff-app">
        <aside class="sidebar ${ui.mobileMenu ? 'open' : ''}">
            <div>
                ${link(brand(), 'public/home', '', 'brand-link')}
                <div class="workspace">
                    ${icon('building')}<span
                        ><strong
                            >${session.role === 'ADMIN' ? 'Bệnh viện An Tâm' : dept(acc()?.deptId)?.name || 'Bệnh viện An Tâm'}</strong
                        ><small>${activeRole().name}</small></span
                    >
                </div>
            </div>
            <nav class="staff-nav" aria-label="Chức năng theo vai trò">
                ${(staffMenus[session.role] || []).map(([group, items]) => /* HTML */ `<div class="nav-group"><span>${group}</span>${items.map(([route, ic, label]) => link(/* HTML */ `<span>${label}</span>${counts[route] ? /* HTML */ `<b>${counts[route]}</b>` : ''}`, route, ic, ui.route === route ? 'active' : '')).join('')}</div>`).join('')}
            </nav>
            <div class="sidebar-bottom">
                ${button('Cổng bệnh nhân', 'patient-portal', 'external', 'portal-link')}
                <div>
                    <span><i></i>Dữ liệu mẫu HIS</span
                    ><button data-act="roles">Đổi vai trò ${icon('down')}</button>
                </div>
            </div>
        </aside>
        ${ui.mobileMenu ? '<button class="sidebar-scrim" data-act="menu" aria-label="Đóng điều hướng"></button>' : ''}
        <div class="staff-shell">
            <header class="staff-topbar">
                ${button('', 'menu', 'menu', 'icon-btn mobile-only', 'aria-label="Mở điều hướng"')}
                <div class="staff-breadcrumb">
                    <span>${activeRole().name}</span><i>/</i
                    ><strong>${pageTitles[ui.route] || 'Không gian nhân viên'}</strong>
                </div>
                <div class="topbar-right">
                    <span class="demo-clock"
                        >${icon('clock')}${fmtDate(TODAY)} ·
                        ${demoNow().toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Ho_Chi_Minh' })}
                        <small>${window.hisLive ? 'giờ VN' : 'giờ mẫu'}</small></span
                    >${button('', 'staff-search', 'search', 'icon-btn', `aria-label="${session.role === 'ADMIN' ? 'Tìm tài khoản' : 'Tìm bệnh nhân'}"`)}${button('', 'staff-notices', 'bell', 'icon-btn', 'aria-label="Thông báo công việc"')}<button
                        class="staff-profile"
                        data-act="roles"
                        aria-label="Tài khoản và đổi vai trò"
                    >
                        ${avatar(acc()?.name || 'Nhân viên')}<span
                            ><strong>${esc(acc()?.name || 'Nhân viên')}</strong
                            ><small>${activeRole().name}</small></span
                        >${icon('down')}
                    </button>
                </div>
            </header>
            <main class="staff-main">${guardRoute() ? staffContent() : accessDenied()}</main>
        </div>
    </div>`;
}

function staffContent() {
    switch (ui.route) {
        case 'staff/dashboard':
            return staffDashboard();
        case 'admin/dashboard':
            return adminDashboard();
        case 'staff/reception':
            return receptionPage();
        case 'staff/vitals':
            return vitalsPage();
        case 'staff/patients':
            return patientsPage();
        case 'staff/consultation':
            return consultationPage();
        case 'staff/labs':
            return labsPage();
        case 'staff/records':
            return staffRecordsPage();
        case 'staff/schedule':
            return schedulePage();
        case 'staff/exceptions':
            return exceptionsPage();
        case 'staff/cashier':
            return cashierPage();
        case 'admin/revenue':
            return revenuePage();
        case 'admin/accounts':
            return accountsPage();
        case 'admin/catalog':
            return catalogPage();
        case 'admin/logs':
            return logsPage();
        default:
            return accessDenied();
    }
}
