/* views / shared */
const pageTitles = {
    'public/booking': 'Đặt lịch khám',
    'public/home': 'Chăm sóc sức khỏe',
    'public/specialties': 'Chuyên khoa',
    'public/doctors': 'Đội ngũ bác sĩ',
    'public/services': 'Dịch vụ y tế',
    'public/guide': 'Hướng dẫn khám',
    'patient/booking': 'Đặt lịch khám',
    'patient/appointments': 'Lịch hẹn của tôi',
    'patient/family': 'Hồ sơ người khám',
    'patient/records': 'Hồ sơ sức khỏe',
    'patient/notifications': 'Thông báo',
    'staff/dashboard': 'Bàn làm việc',
    'staff/reception': 'Tiếp nhận & hàng chờ',
    'staff/vitals': 'Sinh hiệu',
    'staff/patients': 'Hồ sơ người khám',
    'staff/consultation': 'Khám & kê đơn',
    'staff/labs': 'Chỉ định & kết quả',
    'staff/records': 'Bệnh án trong khoa',
    'staff/schedule': 'Lịch khám & lịch trực',
    'staff/exceptions': 'Nghỉ / đổi ca',
    'staff/cashier': 'Thu phí & BHYT',
    'admin/dashboard': 'Điều hành bệnh viện',
    'admin/accounts': 'Tài khoản & phân quyền',
    'admin/catalog': 'Danh mục hệ thống',
    'admin/revenue': 'Doanh thu & thu phí',
    'admin/logs': 'Nhật ký truy cập',
    'account/login': 'Đăng nhập',
    'account/register': 'Đăng ký',
    'account/settings': 'Tài khoản',
};

function button(label, action, name = '', classes = '', attributes = '') {
    return `<button type="button" class="btn ${classes}" data-act="${action}" ${attributes}>
        ${name ? icon(name) : ''}<span>${esc(label)}</span>
    </button>`;
}

function link(label, route, name = '', classes = '') {
    return `<a href="${esc(routeUrl(route))}" data-route="${esc(route)}" class="${classes}" ${ui.route === route ? 'aria-current="page"' : ''}>
        ${name ? icon(name) : ''}${label}
    </a>`;
}

function avatar(name) {
    return `<span class="avatar" aria-hidden="true">${esc(initials(name))}</span>`;
}

function heading(title, description = '', actions = '') {
    return `<div class="page-heading"><div><h1 tabindex="-1">${esc(title)}</h1>
        <p>${esc(description)}</p></div><div class="heading-actions">${actions}</div></div>`;
}

function empty(title, description = '', action = '') {
    return `<div class="empty">${icon('clipboard')}<h3>${esc(title)}</h3>
        <p>${esc(description)}</p>${action}</div>`;
}

function unavailableNotice() {
    return `<div class="notice" role="status">${icon('info')}
        <span>Chức năng đang được hoàn thiện. Hiện chưa thể tra cứu hoặc gửi yêu cầu tại màn hình này.</span>
    </div>`;
}

function unavailableButton(label, name = 'plus') {
    return `<button type="button" class="btn outline" disabled title="Chức năng chưa sẵn sàng">
        ${icon(name)}${esc(label)}
    </button>`;
}

function pendingTable(title, columns, action = '') {
    return `<section class="card">
        <header class="card-header"><h2>${esc(title)}</h2>${action}</header>
        <div class="table-wrap"><table><thead><tr>
            ${columns.map((column) => `<th scope="col">${esc(column)}</th>`).join('')}
        </tr></thead><tbody><tr><td colspan="${columns.length}">
            ${empty('Chức năng chưa sẵn sàng', 'Dữ liệu sẽ hiển thị sau khi hệ thống hoàn tất tích hợp.')}
        </td></tr></tbody></table></div>
    </section>`;
}

function pendingStats(labels) {
    return `<div class="stats-grid ${labels.length === 3 ? 'three' : ''}">
        ${labels
            .map(
                (label) => `<div class="stat-card"><span class="stat-label">${esc(label)}</span>
            <strong aria-label="Chưa có dữ liệu">—</strong><small>Chưa có dữ liệu</small></div>`,
            )
            .join('')}
    </div>`;
}

function pageIntro(kicker, title, description) {
    return `<section class="page-intro"><div class="breadcrumb">
        ${link('Trang chủ', 'public/home')}${icon('chevron')}<span>${esc(title)}</span>
        </div><span class="eyebrow">${esc(kicker)}</span><h1 tabindex="-1">${esc(title)}</h1><p>${esc(description)}</p>
    </section>`;
}
