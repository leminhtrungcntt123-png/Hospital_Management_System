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

function formatPortalDateTime(value) {
    if (!value) return 'Chưa xếp giờ';
    const d = new Date(value);
    if (isNaN(d.getTime())) return String(value);
    return d.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' }) + ' · ' + d.toLocaleDateString('vi-VN');
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
                    <td>${a.startTime ? esc(formatPortalDateTime(a.startTime)) : '<em>Cần đặt lại</em>'}</td>
                    <td><span class="${appointmentBadgeClass(a.statusId)}">${esc(a.statusName)}</span></td>
                    <td>
                        ${a.canCancel ? `<button type="button" class="btn outline" onclick="cancelPatientAppointment(${a.id}, '${esc(a.appointmentCode)}')" ${s.cancellingId === a.id ? 'disabled' : ''}>${s.cancellingId === a.id ? 'Đang hủy…' : 'Hủy lịch'}</button>` : ''}
                        ${Number(a.statusId) === 10 ? link('Đặt lại', 'public/booking', 'calendar', 'btn primary') : ''}
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
