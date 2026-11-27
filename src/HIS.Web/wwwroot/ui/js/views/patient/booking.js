const patientAppointmentsState = {
    status: 'idle', // idle | loading | ready | error
    items: [],
    error: '',
    cancellingId: null,
};

const patientNotificationsState = {
    status: 'idle',
    unreadCount: 0,
    items: [],
    error: '',
    marking: false,
};

function parsePortalWallParts(value) {
    if (!value) return null;
    const match = String(value).match(/^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2}))?/);
    if (match) {
        return {
            year: match[1],
            month: match[2],
            day: match[3],
            hour: match[4] ?? null,
            minute: match[5] ?? null,
        };
    }
    const d = new Date(value);
    if (isNaN(d.getTime())) return null;
    return {
        year: String(d.getFullYear()),
        month: String(d.getMonth() + 1).padStart(2, '0'),
        day: String(d.getDate()).padStart(2, '0'),
        hour: String(d.getHours()).padStart(2, '0'),
        minute: String(d.getMinutes()).padStart(2, '0'),
    };
}

function formatPortalTime(value) {
    const p = parsePortalWallParts(value);
    if (!p || p.hour === null) return '';
    return `${p.hour}:${p.minute}`;
}

function formatPortalDate(value) {
    const p = parsePortalWallParts(value);
    if (!p) return value ? String(value) : '—';
    return `${p.day}/${p.month}/${p.year}`;
}

function formatPortalDateTime(value) {
    if (!value) return 'Chưa xếp giờ';
    const p = parsePortalWallParts(value);
    if (!p) return String(value);
    const dateStr = `${p.day}/${p.month}/${p.year}`;
    return p.hour !== null ? `${p.hour}:${p.minute} · ${dateStr}` : dateStr;
}

function formatPortalSlotRange(startTime, endTime) {
    if (!startTime) return '';
    const startStr = formatPortalTime(startTime);
    const endStr = endTime ? formatPortalTime(endTime) : '';
    const dateStr = formatPortalDate(startTime);
    return endStr ? `${startStr} – ${endStr} · ${dateStr}` : `${startStr} · ${dateStr}`;
}

function openAppointmentDetailModal(appointmentId) {
    const item = patientAppointmentsState.items.find((a) => Number(a.id) === Number(appointmentId));
    if (!item) return;

    const timeRange = item.startTime
        ? `${formatPortalTime(item.startTime)}${item.endTime ? ' – ' + formatPortalTime(item.endTime) : ''}`
        : 'Chưa xếp giờ';
    const examDate = item.startTime ? formatPortalDate(item.startTime) : 'Cần đặt lại';
    const dobText = item.patientDateOfBirth ? formatPortalDate(item.patientDateOfBirth) : '—';

    const rows = [
        ['Mã lịch hẹn', `<strong style="color:#007d9f; font-size:16px;">${esc(item.appointmentCode)}</strong>`],
        ['Trạng thái', `<span class="${appointmentBadgeClass(item.statusId)}">${esc(item.statusName)}</span>`],
        ['Ngày khám', `<strong>${esc(examDate)}</strong>`],
        ['Giờ khám', `<strong style="color:#007d9f;">${esc(timeRange)}</strong>`],
        ['Chuyên khoa', esc(item.departmentName || '—')],
        ['Bác sĩ phụ trách', `<strong>${esc(item.doctorName || 'Chưa phân công')}</strong>`],
        ['Phòng khám', esc(item.roomNumber || '—')],
        ['Người khám', `<strong>${esc(item.patientName || '—')}</strong>`],
        ['Ngày sinh', esc(dobText)],
        ['Số điện thoại', esc(item.patientPhone || '—')],
        ['Email nhận thông báo', esc(item.contactEmail || '—')],
    ];

    if (item.queueNumber) {
        rows.push(['Số thứ tự tiếp nhận', `<strong>${esc(item.queueNumber)}</strong>`]);
    }
    rows.push(['Lý do khám / Triệu chứng', esc(item.reason || 'Không có')]);
    rows.push(['Thời gian đặt lịch', esc(formatPortalDateTime(item.createdAt))]);

    const bodyHtml = `
        <div style="background:#f0f8fb; border:1px solid #d5eaf3; border-radius:10px; padding:14px 16px; margin-bottom:12px;">
            <dl style="display:grid; grid-template-columns:160px 1fr; gap:8px 14px; margin:0; font-size:14px;">
                ${rows
                    .map(
                        ([label, val]) =>
                            `<dt style="color:#4b677a; font-weight:600; margin:0;">${label}</dt><dd style="margin:0; color:#15334d;">${val}</dd>`,
                    )
                    .join('')}
            </dl>
        </div>
        <p style="font-size:12px; color:#627d92; margin:0;">Vui lòng đến trước giờ khám 15 phút, mang theo giấy tờ tùy thân / thẻ BHYT (nếu có) và xuất trình mã lịch hẹn tại quầy tiếp nhận.</p>
    `;

    const footerHtml = `
        ${
            item.canCancel
                ? `<button type="button" class="btn outline" onclick="closeModal(); cancelPatientAppointment(${item.id}, '${esc(item.appointmentCode)}')">Hủy lịch</button>`
                : ''
        }
        <button type="button" class="btn primary" onclick="closeModal()">Đóng</button>
    `;

    openModal(`Thông tin lịch hẹn ${item.appointmentCode}`, bodyHtml, footerHtml);
}

function appointmentBadgeClass(statusId) {
    switch (Number(statusId)) {
        case 2:
        case 3:
        case 4:
        case 5:
            return 'badge ok';
        case 6:
            return 'badge';
        case 10:
            return 'badge warn';
        case 7:
        case 8:
        case 9:
            return 'badge danger';
        default:
            return 'badge';
    }
}

function ensurePatientAppointments() {
    if (!config.authenticated || patientAppointmentsState.status !== 'idle') return;
    loadPatientAppointments();
}

async function loadPatientAppointments() {
    patientAppointmentsState.status = 'loading';
    patientAppointmentsState.error = '';
    render();
    try {
        const res = await fetch(config.myAppointmentsUrl || '/Patient/MyAppointments/List', {
            credentials: 'same-origin',
            cache: 'no-store',
            headers: { Accept: 'application/json' },
        });
        const body = await res.json();
        if (!res.ok) throw new Error(body.message || 'Không tải được danh sách lịch hẹn.');
        patientAppointmentsState.items = Array.isArray(body.items) ? body.items : [];
        patientAppointmentsState.status = 'ready';
    } catch (err) {
        patientAppointmentsState.status = 'error';
        patientAppointmentsState.error = err.message;
    }
    render();
}

async function cancelPatientAppointment(appointmentId, code) {
    if (patientAppointmentsState.cancellingId) return;
    if (!confirm(`Bạn có chắc muốn hủy lịch hẹn ${code || ''} không?`)) return;

    patientAppointmentsState.cancellingId = appointmentId;
    patientAppointmentsState.error = '';
    render();
    try {
        const csrf = document.querySelector('[name="__RequestVerificationToken"]')?.value || '';
        const res = await fetch(config.cancelAppointmentUrl || '/Patient/MyAppointments/Cancel', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: csrf,
            },
            body: JSON.stringify({ appointmentId }),
        });
        const body = await res.json();
        if (!res.ok) throw new Error(body.message || 'Không thể hủy lịch hẹn.');
        await loadPatientAppointments();
    } catch (err) {
        patientAppointmentsState.error = err.message;
    } finally {
        patientAppointmentsState.cancellingId = null;
        render();
    }
}

function appointmentsPage() {
    ensurePatientAppointments();
    const s = patientAppointmentsState;
    let content = '';
    if (s.status === 'loading') {
        content = `<div class="card"><p style="padding:20px;">Đang tải danh sách lịch hẹn…</p></div>`;
    } else if (s.status === 'error') {
        content = `<div class="notice error" role="alert">${esc(s.error)} <button type="button" class="btn outline" onclick="loadPatientAppointments()">Thử lại</button></div>`;
    } else if (!s.items.length) {
        content = `<section class="card">${empty('Chưa có lịch hẹn nào', 'Hãy đặt lịch khám mới để theo dõi tại đây.', link('Đặt lịch khám', 'public/booking', 'plus', 'btn primary'))}</section>`;
    } else {
        content = `<section class="card">
            <header class="card-header"><h2>Danh sách lịch hẹn (${s.items.length})</h2>
                <button type="button" class="btn outline" onclick="loadPatientAppointments()">${icon('refresh')}Làm mới</button>
            </header>
            ${s.error ? `<div class="notice error" role="alert">${esc(s.error)}</div>` : ''}
            <div class="table-wrap"><table><thead><tr>
                <th scope="col">Mã lịch</th>
                <th scope="col">Người khám</th>
                <th scope="col">Bác sĩ / Phòng</th>
                <th scope="col">Ngày giờ</th>
                <th scope="col">Trạng thái</th>
                <th scope="col">Thao tác</th>
            </tr></thead><tbody>
                ${s.items
                    .map(
                        (a) => `<tr>
                    <td><strong>${esc(a.appointmentCode)}</strong></td>
                    <td>${esc(a.patientName)}</td>
                    <td>${esc(a.doctorName)}<br /><small class="text-muted">${esc(a.departmentName)} · Phòng ${esc(a.roomNumber)}</small></td>
                    <td>${a.startTime ? esc(formatPortalSlotRange(a.startTime, a.endTime)) : '<em>Cần đặt lại</em>'}</td>
                    <td><span class="${appointmentBadgeClass(a.statusId)}">${esc(a.statusName)}</span></td>
                    <td>
                        <div style="display:flex; gap:6px; flex-wrap:wrap; align-items:center;">
                            <button type="button" class="btn outline" onclick="openAppointmentDetailModal(${a.id})">${icon('eye')}Chi tiết</button>
                            ${a.canCancel ? `<button type="button" class="btn outline" onclick="cancelPatientAppointment(${a.id}, '${esc(a.appointmentCode)}')" ${s.cancellingId === a.id ? 'disabled' : ''}>${s.cancellingId === a.id ? 'Đang hủy…' : 'Hủy lịch'}</button>` : ''}
                            ${Number(a.statusId) === 10 ? link('Đặt lại', 'public/booking', 'calendar', 'btn primary') : ''}
                        </div>
                    </td>
                </tr>`,
                    )
                    .join('')}
            </tbody></table></div>
        </section>`;
    }

    return `${heading('Lịch hẹn của tôi', 'Theo dõi trạng thái lịch khám và thông tin tiếp nhận.', link('Đặt lịch khám', 'public/booking', 'plus', 'btn primary'))}${content}`;
}

function ensurePatientNotifications() {
    if (!config.authenticated || patientNotificationsState.status !== 'idle') return;
    loadPatientNotifications();
}

async function loadPatientNotifications() {
    patientNotificationsState.status = 'loading';
    patientNotificationsState.error = '';
    render();
    try {
        const res = await fetch(config.notificationsUrl || '/Patient/Notifications/List', {
            credentials: 'same-origin',
            cache: 'no-store',
            headers: { Accept: 'application/json' },
        });
        const body = await res.json();
        if (!res.ok) throw new Error(body.message || 'Không tải được thông báo.');
        patientNotificationsState.unreadCount = body.unreadCount || 0;
        patientNotificationsState.items = Array.isArray(body.items) ? body.items : [];
        patientNotificationsState.status = 'ready';
    } catch (err) {
        patientNotificationsState.status = 'error';
        patientNotificationsState.error = err.message;
    }
    render();
}

async function markPatientNotificationsRead(notificationId = null) {
    if (patientNotificationsState.marking) return;
    patientNotificationsState.marking = true;
    render();
    try {
        const csrf = document.querySelector('[name="__RequestVerificationToken"]')?.value || '';
        const res = await fetch(config.markNotificationReadUrl || '/Patient/Notifications/MarkRead', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: csrf,
            },
            body: JSON.stringify({ notificationId }),
        });
        if (res.ok) {
            await loadPatientNotifications();
        }
    } finally {
        patientNotificationsState.marking = false;
        render();
    }
}

function notificationsPage() {
    ensurePatientNotifications();
    const s = patientNotificationsState;
    const markAllBtn = s.unreadCount > 0
        ? `<button type="button" class="btn outline" onclick="markPatientNotificationsRead(null)" ${s.marking ? 'disabled' : ''}>${icon('check')}Đánh dấu tất cả đã đọc (${s.unreadCount})</button>`
        : `<button type="button" class="btn outline" onclick="loadPatientNotifications()">${icon('refresh')}Làm mới</button>`;

    let content = '';
    if (s.status === 'loading') {
        content = `<div class="card"><p style="padding:20px;">Đang tải thông báo…</p></div>`;
    } else if (s.status === 'error') {
        content = `<div class="notice error" role="alert">${esc(s.error)} <button type="button" class="btn outline" onclick="loadPatientNotifications()">Thử lại</button></div>`;
    } else if (!s.items.length) {
        content = `<section class="card">${empty('Chưa có thông báo', 'Các cập nhật về lịch hẹn hoặc thay đổi ca khám sẽ hiển thị tại đây.')}</section>`;
    } else {
        content = `<section class="card">
            <header class="card-header"><h2>Thông báo của bạn (${s.items.length})</h2></header>
            <div class="table-wrap"><table><thead><tr>
                <th scope="col">Thời gian</th>
                <th scope="col">Nội dung</th>
                <th scope="col">Trạng thái</th>
            </tr></thead><tbody>
                ${s.items
                    .map(
                        (n) => `<tr style="${n.isRead ? '' : 'background:#f0f8fb;'}">
                    <td>${esc(formatPortalDateTime(n.createdAt))}</td>
                    <td><strong>${esc(n.title)}</strong><br /><span>${esc(n.content || '')}</span></td>
                    <td>
                        ${n.isRead
                            ? '<span class="badge">Đã đọc</span>'
                            : `<button type="button" class="btn outline" onclick="markPatientNotificationsRead(${n.id})">Chưa đọc · Đánh dấu</button>`}
                    </td>
                </tr>`,
                    )
                    .join('')}
            </tbody></table></div>
        </section>`;
    }

    return `${heading('Thông báo', 'Theo dõi cập nhật về lịch hẹn và hồ sơ của bạn.', markAllBtn)}${content}`;
}
