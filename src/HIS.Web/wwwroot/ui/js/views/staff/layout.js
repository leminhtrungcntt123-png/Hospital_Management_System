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
                ['staff/cashier/deposits', 'wallet', 'Quản lý tạm ứng'],
                ['staff/cashier/ledger', 'clipboard', 'Sổ thu ngân'],
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
    ],
};

function staffShell() {
    const name = config.displayName || 'Tài khoản';
    const useCustomStaffLogo = ['NURSE', 'ACCOUNTANT', 'CASHIER'].includes((config.role || '').toUpperCase());
    const brandIconHtml = useCustomStaffLogo
        ? `<img src="${esc((config.pathBase || '') + '/images/medicare-staff-logo.jpg')}" alt="HIS MediCare" style="width:36px; height:36px; border:2px solid #107c41; border-radius:10px; box-sizing:border-box; object-fit:cover; flex-shrink:0;" />`
        : `<div class="brand-logo-box"><svg viewBox="0 0 24 24" fill="none"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg></div>`;
    return `<div class="staff-app">
        <aside class="sidebar ${ui.mobileMenu ? 'open' : ''}">
            <div>
                <a class="admin-brand" href="${esc(routeUrl('public/home'))}" title="HIS MediCare - Về trang chủ">
                    ${brandIconHtml}
                    <div class="brand-text"><span class="brand-title">HIS <em>MediCare</em></span></div>
                </a>
                <div class="workspace">${icon('building')}<span><strong>${esc(config.departmentName || 'Không gian nhân viên')}</strong><small>${esc(activeRole().name)}</small></span></div>
            </div>
            <nav class="staff-nav" aria-label="Chức năng theo vai trò">
                ${(staffMenus[config.role] || [])
                    .map(
                        ([group, items]) => `<div class="nav-group"><span>${group}</span>
                    ${items.map(([route, name, title]) => link(`<span>${title}</span>`, route, name, ui.route === route ? 'active' : '')).join('')}
                </div>`,
                    )
                    .join('')}
            </nav>
            <div class="sidebar-bottom">${link('Cổng bệnh nhân', 'public/home', 'external', 'portal-link')}
                <div><span><i></i>HIS MediCare</span></div>
            </div>
        </aside>
        ${ui.mobileMenu ? '<button class="sidebar-scrim" type="button" data-act="menu" aria-label="Đóng điều hướng"></button>' : ''}
        <div class="staff-shell"><header class="staff-topbar">
            ${button('', 'menu', 'menu', 'icon-btn mobile-only', `aria-label="Mở điều hướng" aria-expanded="${ui.mobileMenu}"`)}
            <div class="staff-breadcrumb"><span>${esc(activeRole().name)}</span><i>/</i><strong>${esc(pageTitles[ui.route] || 'Không gian nhân viên')}</strong></div>
            <div class="topbar-right"><button class="staff-profile" type="button" data-act="account-menu" aria-label="Tài khoản của ${esc(name)}">
                ${avatar(name)}<span><strong>${esc(name)}</strong><small>${esc(activeRole().name)}</small></span>${icon('down')}
            </button></div>
        </header><main class="staff-main">${staffContent()}</main></div>
    </div>`;
}

function staffContent() {
    switch (ui.route) {
        case 'staff/dashboard':
            return staffDashboard();
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
        default:
            return empty(
                'Trang chưa sẵn sàng',
                '',
                `<a class="btn outline" href="${esc(config.home)}">Về không gian làm việc</a>`,
            );
    }
}
