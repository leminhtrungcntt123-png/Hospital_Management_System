/* views / staff / dashboard */
function staffDashboard() {
    const apps = todayApps(),
        waiting = apps.filter((a) => ['READY_FOR_DOCTOR', 'IN_PROGRESS'].includes(a.status)),
        confirm = apps.filter((a) => a.status === 'CONFIRMED'),
        vital = apps.filter((a) => a.status === 'CHECKED_IN'),
        invoices = db.invoices.filter(
            (i) =>
                appointment(i.appointmentId)?.deptId === acc().deptId && invoiceTotal(i).balance > 0
        );
    return /* HTML */ `${heading(session.role === 'DEPARTMENT_HEAD' ? 'Tổng quan khoa' : 'Bàn làm việc hôm nay', session.role === 'NURSE' ? 'Những lượt khám cần tiếp nhận và xử lý tại khoa của bạn.' : 'Theo dõi tiến độ khám và điều phối nhân sự trong khoa.', session.role === 'NURSE' ? button('Khách có lịch hẹn', 'open-work', 'calendar', 'btn outline', 'data-value="CONFIRMED"') + button('Tiếp nhận vãng lai', 'walkin', 'plus', 'btn primary') : link('Xem lịch khoa', 'staff/schedule', 'calendar', 'btn primary'), 'VẬN HÀNH TẠI KHOA')}
        <section class="stats-grid">
            ${stat('Chờ tiếp nhận', confirm.length, 'calendar', 'Xác nhận thông tin & cấp số', 'cyan', 'open-work', 'data-value="CONFIRMED"')}${stat('Cần đo sinh hiệu', vital.length, 'pulse', 'Chuẩn bị trước khi vào khám', 'blue', 'open-work', 'data-value="CHECKED_IN"')}${stat('Chờ / đang khám', waiting.length, 'stethoscope', 'Theo dõi phòng và hàng chờ', 'neutral', 'open-work', 'data-value="READY_FOR_DOCTOR"')}${stat('Chưa thu đủ phí', invoices.length, 'wallet', money(invoices.reduce((n, i) => n + invoiceTotal(i).balance, 0)) + ' còn lại', 'amber', session.role === 'NURSE' ? 'open-cashier' : '', '')}
        </section>
        <div class="staff-dashboard-grid">
            <div class="dashboard-main">
                <section class="card">
                    ${cardHeader('Cần xử lý trước', apps.length + ' lượt trong ngày', link('Mở danh sách', session.role === 'NURSE' ? 'staff/reception' : 'staff/records', 'arrow', 'text-link'), 'calendar')}${tabs(
                        [
                            ['ALL', 'Tất cả'],
                            ['CONFIRMED', 'Tiếp nhận', confirm.length],
                            ['CHECKED_IN', 'Sinh hiệu', vital.length],
                            [
                                'READY_FOR_DOCTOR',
                                'Chờ bác sĩ',
                                apps.filter((a) => a.status === 'READY_FOR_DOCTOR').length,
                            ],
                        ],
                        ui.filter
                    )}${appointmentsTable(apps.filter((a) => ui.filter === 'ALL' || a.status === ui.filter).slice(0, 5), true)}
                </section>
                <section class="card">
                    ${cardHeader('Bác sĩ tại khoa', '', link('Lịch làm việc', 'staff/schedule', 'arrow', 'text-link'), 'stethoscope')}
                    <div class="doctor-strip">
                        ${db.doctors
                            .filter((d) => d.deptId === acc().deptId)
                            .map(
                                (d) =>
                                    /* HTML */ `<div>
                                        ${avatar(d.name, d.color)}<span
                                            ><strong>BS. ${d.name}</strong
                                            ><small>${d.specialty}</small></span
                                        ><b>P.${room(d.roomId).number}</b>
                                    </div>`
                            )
                            .join('')}
                    </div>
                </section>
            </div>
            ${queuePanel()}
        </div>`;
}

function adminDashboard() {
    const ts = db.invoices.map(invoiceTotal),
        gross = ts.reduce((n, t) => n + t.gross, 0),
        due = ts.reduce((n, t) => n + t.balance, 0),
        todayPayments = db.payments.filter((p) => p.date === TODAY),
        collected = todayPayments.reduce(
            (n, p) => n + (p.type === 'REFUND' ? -1 : 1) * p.amount,
            0
        );
    return /* HTML */ `${heading('Điều hành bệnh viện', 'Tổng hợp vận hành, tài chính và tài khoản. Không hiển thị nội dung bệnh án.', button('Xuất sổ thu phí', 'export', 'download', 'btn outline') + button('Tạo tài khoản', 'new-account', 'userplus', 'btn primary'), 'QUẢN TRỊ HỆ THỐNG')}
        <section class="stats-grid">
            ${stat('Đã thu hôm nay', money(collected), 'wallet', todayPayments.length + ' giao dịch', 'cyan', 'open-revenue')}${stat('Còn phải thu', money(due), 'clock', db.invoices.filter((i) => invoiceTotal(i).balance > 0).length + ' hóa đơn chưa đủ phí', 'amber', 'open-revenue')}${stat('Lượt khám hôm nay', db.appointments.filter((a) => a.date === TODAY && !['CANCELLED', 'EXPIRED'].includes(a.status)).length, 'users', 'Tổng hợp toàn hệ thống', 'blue')}${stat('Tài khoản hoạt động', db.accounts.filter((a) => a.active).length, 'shield', 'Quản lý vai trò & trạng thái', 'neutral', 'open-accounts')}
        </section>
        <div class="admin-grid">
            <section class="card">
                ${cardHeader('Vận hành theo khoa', 'Thông tin hành chính, không bao gồm nội dung khám', '', 'building')}
                <div class="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>Khoa</th>
                                <th>Bác sĩ</th>
                                <th>Lượt hôm nay</th>
                                <th>Chờ khám</th>
                                <th>Hoàn tất</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${db.departments
                                .map((d) => {
                                    const as = db.appointments.filter(
                                        (a) => a.deptId === d.id && a.date === TODAY
                                    );
                                    return /* HTML */ `<tr>
                                        <td>
                                            <strong>${d.name}</strong
                                            ><small
                                                >${db.rooms.filter((r) => r.deptId === d.id).length}
                                                phòng khám</small
                                            >
                                        </td>
                                        <td>
                                            ${db.doctors.filter((x) => x.deptId === d.id).length}
                                        </td>
                                        <td>${as.length}</td>
                                        <td>
                                            ${as.filter((a) => ['CHECKED_IN', 'READY_FOR_DOCTOR'].includes(a.status)).length}
                                        </td>
                                        <td>
                                            <span class="pill cyan"
                                                >${as.filter((a) => a.status === 'COMPLETED').length}</span
                                            >
                                        </td>
                                    </tr>`;
                                })
                                .join('')}
                        </tbody>
                    </table>
                </div>
                <div class="admin-shortcuts">
                    ${link('Danh mục khoa, phòng, thuốc & dịch vụ', 'admin/catalog', 'clipboard', 'shortcut-row')}${link('Tài khoản và phân quyền nhân viên', 'admin/accounts', 'shield', 'shortcut-row')}${link('Nhật ký truy cập & thao tác', 'admin/logs', 'clock', 'shortcut-row')}
                </div>
            </section>
            ${collectionCard()}
        </div>
        <section class="card space-top">
            ${cardHeader('Giao dịch gần đây', 'Dữ liệu thu phí từ các hóa đơn trong bản mẫu', link('Xem sổ thu phí', 'admin/revenue', 'arrow', 'text-link'), 'wallet')}${paymentsTable(db.payments.slice(0, 5), true)}
        </section>`;
}

function collectionCard() {
    const totals = db.invoices.map(invoiceTotal),
        due = totals.reduce((n, t) => n + t.due, 0),
        paid = totals.reduce((n, t) => n + t.paid, 0),
        rate = due ? Math.min(100, Math.round((paid / due) * 100)) : 0;
    return /* HTML */ `<section class="card collection-card">
        ${cardHeader('Tình hình thu phí', '', '', 'wallet')}
        <div class="collection-rate">
            <strong>${rate}<small>%</small></strong
            ><span>đã thu trên phần<br />người bệnh phải trả</span>
        </div>
        <div class="progress-track"><span style="width:${rate}%"></span></div>
        <div class="collection-lines">
            <div><span>Đã ghi nhận</span><strong>${money(paid)}</strong></div>
            <div><span>Còn phải thu</span><strong>${money(Math.max(0, due - paid))}</strong></div>
            <div>
                <span>BHYT đã áp dụng</span
                ><strong>${money(totals.reduce((n, t) => n + t.covered, 0))}</strong>
            </div>
        </div>
        ${link('Mở chi tiết thu phí', 'admin/revenue', 'arrow', 'collection-link')}
    </section>`;
}
