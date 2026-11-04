/* views / shared */
const pageTitles = {
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

const relationNames = {
    SELF: 'Bản thân',
    SPOUSE: 'Vợ / Chồng',
    CHILD: 'Con',
    PARENT: 'Bố / Mẹ',
    SIBLING: 'Anh / Chị / Em',
    OTHER: 'Người thân khác',
};

const typeNames = {
    EXAMINATION: 'Khám bệnh',
    LAB_TEST: 'Xét nghiệm',
    IMAGING: 'Chẩn đoán hình ảnh',
};

function button(label, act, ic = '', cls = '', extra = '') {
    return /* HTML */ `<button type="button" class="btn ${cls}" data-act="${act}" ${extra}>
        ${ic ? icon(ic) : ''}<span>${label}</span>
    </button>`;
}

function link(label, route, ic = '', cls = '') {
    return /* HTML */ `<a href="#/${route}" data-route="${route}" class="${cls}"
        >${ic ? icon(ic) : ''}${label}</a
    >`;
}

function avatar(name, color = 'cyan', large = false) {
    return /* HTML */ `<span class="avatar ${color} ${large ? 'large' : ''}" aria-hidden="true"
        >${esc(initials(name))}</span
    >`;
}

function badge(status, label) {
    const names = {
        ...statusNames,
        ACTIVE: 'Đã xác minh',
        PENDING_LINK: 'Chờ xác minh',
        REVOKED: 'Đã thu hồi',
        UNPAID: 'Chưa thanh toán',
        PARTIALLY_PAID: 'Đã thu một phần',
        PAID: 'Đã thanh toán',
        APPROVED: 'Đã duyệt',
        REJECTED: 'Từ chối',
    };
    return /* HTML */ `<span class="badge s-${status.toLowerCase()}"
        ><i></i>${label || names[status] || esc(status)}</span
    >`;
}

function empty(title, description = '', action = '') {
    return /* HTML */ `<div class="empty">
        ${icon('clipboard')}
        <h3>${title}</h3>
        ${description ? /* HTML */ `<p>${description}</p>` : ''}${action}
    </div>`;
}

function heading(title, description = '', actions = '', eyebrow = '') {
    return /* HTML */ `<div class="page-heading">
        <div>
            ${eyebrow ? /* HTML */ `<span class="eyebrow">${eyebrow}</span>` : ''}
            <h1>${title}</h1>
            ${description ? /* HTML */ `<p>${description}</p>` : ''}
        </div>
        ${actions ? /* HTML */ `<div class="heading-actions">${actions}</div>` : ''}
    </div>`;
}

function cardHeader(title, sub = '', action = '', ic = '') {
    return /* HTML */ `<header class="card-header">
        <div class="section-title">
            ${ic ? /* HTML */ `<span class="section-icon">${icon(ic)}</span>` : ''}
            <div>
                <h2>${title}</h2>
                ${sub ? /* HTML */ `<p>${sub}</p>` : ''}
            </div>
        </div>
        ${action}
    </header>`;
}

function stat(label, value, ic, sub = '', color = 'cyan', act = '', extra = '') {
    return /* HTML */ `<${act ? 'button' : 'div'} ${act ? `type="button" data-act="${act}" ${extra}` : ''} class="stat-card ${color}"><div class="stat-label"><span class="stat-icon">${icon(ic)}</span><span>${label}</span>${act ? icon('arrow', 'stat-arrow') : ''}</div><strong>${value}</strong>${sub ? /* HTML */ `<small>${sub}</small>` : ''}</${act ? 'button' : 'div'}>`;
}

function tabs(options, current, kind = 'filter') {
    return /* HTML */ `<div class="tabs" role="tablist">
        ${options.map(([value, label, count]) => /* HTML */ `<button type="button" role="tab" aria-selected="${current === value}" class="tab ${current === value ? 'active' : ''}" data-act="tab" data-kind="${kind}" data-value="${value}">${label}${count !== undefined ? /* HTML */ `<span>${count}</span>` : ''}</button>`).join('')}
    </div>`;
}

function searchInput(label = 'Tìm tên, mã hoặc số điện thoại…') {
    return /* HTML */ `<label class="search-input"
        >${icon('search')}<input
            data-search="page"
            aria-label="${esc(label)}"
            placeholder="${esc(label)}"
            value="${esc(ui.query)}"
    /></label>`;
}

function notice(text, type = 'info') {
    return /* HTML */ `<div class="notice ${type}">${icon('info')}<span>${text}</span></div>`;
}

function personCell(p, sub = '') {
    return /* HTML */ `<div class="person-cell">
        ${avatar(p.name)}
        <div>
            <strong>${esc(p.name)}</strong
            ><small>${sub || p.id + (p.dob ? ' · ' + age(p.dob) + ' tuổi' : '')}</small>
        </div>
    </div>`;
}

function dateChip(date) {
    return /* HTML */ `<span class="date-chip"
        ><small>THÁNG ${Number(date.slice(5, 7))}</small><strong>${date.slice(8)}</strong></span
    >`;
}

function moneyText(t) {
    return /* HTML */ `<span class="money">${money(t)}</span>`;
}

function matched(text) {
    return (
        !ui.query || String(text).toLocaleLowerCase('vi').includes(ui.query.toLocaleLowerCase('vi'))
    );
}
