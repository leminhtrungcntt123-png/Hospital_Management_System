/* views / patient / profiles */
function myAppointmentsPage() {
    const as = scopedApps()
        .filter(
            (a) =>
                (ui.filter === 'ALL' ||
                    (ui.filter === 'ACTIVE' && liveStates.includes(a.status)) ||
                    a.status === ui.filter) &&
                matched(patient(a.patientId).name + ' ' + a.code)
        )
        .sort((a, b) => b.date.localeCompare(a.date) || a.time.localeCompare(b.time));
    return /* HTML */ `${heading('Lịch hẹn của tôi', 'Theo dõi cuộc hẹn của bạn và người thân trong cùng một không gian.', link('Đặt lịch mới', 'patient/booking', 'plus', 'btn primary'))}
        <div class="card">
            ${tabs(
                [
                    ['ALL', 'Tất cả'],
                    ['ACTIVE', 'Sắp khám'],
                    ['COMPLETED', 'Đã hoàn tất'],
                    ['RESCHEDULE_REQUIRED', 'Cần đặt lại'],
                ],
                ui.filter
            )}
            <div class="toolbar">${searchInput('Tìm người khám hoặc mã lịch hẹn…')}</div>
            <div class="appointment-cards">
                ${
                    as
                        .map(
                            (a) =>
                                /* HTML */ `<article
                                    class="appointment-card ${a.status === 'RESCHEDULE_REQUIRED' ? 'attention' : ''}"
                                >
                                    ${dateChip(a.date)}
                                    <div class="appointment-card-main">
                                        <div class="flex">
                                            <span class="mini-label"
                                                >${esc(patient(a.patientId).name)}</span
                                            >${badge(a.status)}
                                        </div>
                                        <h3>
                                            ${doctor(a.doctorId)?.specialty || 'Chờ phân bác sĩ'}
                                        </h3>
                                        <p>
                                            BS. ${doctor(a.doctorId)?.name || 'Đang cập nhật'} ·
                                            ${a.time} · Phòng ${room(a.roomId)?.number || '—'}
                                        </p>
                                        <small>${a.code}</small>
                                    </div>
                                    <div class="appointment-card-actions">
                                        ${a.status === 'RESCHEDULE_REQUIRED' ? button('Chọn lịch mới', 'reschedule', 'calendar', 'btn primary sm', `data-id="${a.id}"`) : button('Chi tiết', 'appointment-detail', 'chevron', 'btn outline sm', `data-id="${a.id}"`)}
                                    </div>
                                </article>`
                        )
                        .join('') ||
                    empty(
                        'Chưa có lịch hẹn phù hợp',
                        'Bạn có thể đặt cuộc hẹn mới hoặc đổi mục đang xem.'
                    )
                }
            </div>
        </div>`;
}

function familyPage() {
    return /* HTML */ `${heading('Hồ sơ người khám', 'Liên kết tối đa 5 hồ sơ. Mỗi người khám có thông tin và quyền xem riêng.', button('Liên kết hồ sơ đã có', 'link-family', 'users', 'btn outline') + button('Thêm người khám', 'new-family', 'plus', 'btn primary'))}
        <div class="family-summary">
            <span>${icon('users')}Đang sử dụng <strong>${myLinks().length}/5 hồ sơ</strong></span
            ><span>${icon('shield')}Đặt lịch và xem bệnh án là hai quyền riêng biệt</span>
        </div>
        <div class="family-grid">
            ${myLinks()
                .map((l) => {
                    const p = patient(l.patientId);
                    return /* HTML */ `<article class="card family-card">
                        <div class="family-card-top">
                            ${avatar(p.name, 'cyan', true)}
                            <div>
                                <span class="mini-label">${relationNames[l.relationship]}</span>
                                <h2>${p.name}</h2>
                                <p>${p.id} · ${p.gender} · ${age(p.dob)} tuổi</p>
                            </div>
                            ${badge(l.status === 'PENDING' ? 'PENDING_LINK' : l.status)}
                        </div>
                        <div class="info-grid">
                            <div><small>Ngày sinh</small><strong>${fmtDate(p.dob)}</strong></div>
                            <div><small>Điện thoại</small><strong>${p.phone}</strong></div>
                        </div>
                        <div
                            class="permission-line ${l.canView && l.status === 'ACTIVE' ? 'allowed' : ''}"
                        >
                            ${icon(l.canView && l.status === 'ACTIVE' ? 'check' : 'shield')}<span
                                >${l.canView && l.status === 'ACTIVE' ? 'Được xem hồ sơ khám & kết quả' : 'Chưa được cấp quyền xem bệnh án'}</span
                            >
                        </div>
                        <div class="family-actions">
                            ${button('Đặt lịch', 'book-profile', 'calendar', 'btn primary sm', `data-id="${p.id}"`)}${canView(p.id) ? button('Xem hồ sơ', 'open-profile-records', 'clipboard', 'btn outline sm', `data-id="${p.id}"`) : button('Thông tin quyền xem', 'access-help', 'info', 'btn ghost sm', `data-id="${p.id}"`)}${l.relationship !== 'SELF' ? button('Thu hồi liên kết', 'revoke-link', 'close', 'text-link danger-text', `data-id="${l.id}"`) : button('Cập nhật liên hệ', 'edit-self', 'edit', 'text-link', `data-id="${p.id}"`)}
                        </div>
                    </article>`;
                })
                .join('')}
        </div>
        ${notice('Hồ sơ người lớn chỉ mở quyền xem khi người bệnh đồng ý và nhân viên xác minh. Liên kết đang chờ vẫn đặt được lịch do chính tài khoản này tạo.')}`;
}

function myRecordsPage() {
    const links = myLinks(),
        p = patient(ui.profileId) || patient(selfPatient()),
        allowed = p && canView(p.id),
        records = db.records
            .filter((r) => r.patientId === p?.id && r.final)
            .sort((a, b) => b.date.localeCompare(a.date));
    return /* HTML */ `${heading('Hồ sơ sức khỏe', 'Phiếu khám, đơn thuốc và kết quả được hiển thị theo quyền của từng hồ sơ.')}
        <div class="profile-selector">
            ${links
                .map((l) => {
                    const q = patient(l.patientId);
                    return /* HTML */ `<button
                        class="${q.id === p?.id ? 'active' : ''}"
                        data-act="record-profile"
                        data-id="${q.id}"
                    >
                        ${avatar(q.name)}<span
                            >${q.name}<small>${relationNames[l.relationship]}</small></span
                        >${!canView(q.id) ? icon('shield') : ''}
                    </button>`;
                })
                .join('')}
        </div>
        ${
            !allowed
                ? /* HTML */ `<div class="card access-card">
                      ${icon('shield')}
                      <h2>Hồ sơ này chưa được cấp quyền xem bệnh án</h2>
                      <p>
                          Bạn vẫn có thể đặt và theo dõi các lịch do tài khoản của mình tạo. Quyền
                          xem nội dung khám cần được xác minh riêng tại quầy.
                      </p>
                      ${link('Xem lịch hẹn', 'patient/appointments', 'calendar', 'btn primary')}${button('Tìm hiểu quyền truy cập', 'access-help', 'info', 'btn outline', `data-id="${p?.id}"`)}
                  </div>`
                : /* HTML */ `<div class="patient-record-layout">
                      <aside class="card record-identity">
                          ${avatar(p.name, 'cyan', true)}
                          <h2>${p.name}</h2>
                          <p>${p.id} · ${relationNames[linkFor(p.id)?.relationship]}</p>
                          <dl>
                              <dt>Ngày sinh</dt>
                              <dd>${fmtDate(p.dob)}</dd>
                              <dt>Giới tính</dt>
                              <dd>${p.gender}</dd>
                              <dt>Điện thoại</dt>
                              <dd>${p.phone}</dd>
                          </dl>
                          <div class="permission-line allowed">
                              ${icon('shield')}Quyền xem đã được xác minh
                          </div>
                      </aside>
                      <div class="card">
                          ${cardHeader('Lịch sử thăm khám', records.length + ' phiếu khám đã hoàn tất', '', 'clipboard')}${
                              records.length
                                  ? records
                                        .map(
                                            (r) =>
                                                /* HTML */ `<article class="record-preview">
                                                    <div class="record-date">
                                                        ${icon('calendar')}${fmtDate(r.date)}
                                                    </div>
                                                    <h3>${esc(r.diagnosis)}</h3>
                                                    <p>
                                                        BS. ${doctor(r.doctorId).name} ·
                                                        ${doctor(r.doctorId).specialty}
                                                    </p>
                                                    <div class="record-tags">
                                                        <span>${icon('file')}Phiếu khám</span
                                                        ><span
                                                            >${icon('clipboard')}${r.meds.length}
                                                            thuốc</span
                                                        ><span
                                                            >${icon('pulse')}${db.labOrders.filter((l) => l.recordId === r.id && l.status === 'COMPLETED').length}
                                                            kết quả</span
                                                        >
                                                    </div>
                                                    ${button('Xem phiếu khám & kết quả', 'record-detail', 'arrow', 'text-link', `data-id="${r.id}"`)}
                                                </article>`
                                        )
                                        .join('')
                                  : empty(
                                        'Chưa có phiếu khám hoàn tất',
                                        'Kết quả sẽ xuất hiện sau khi bác sĩ hoàn tất thăm khám.'
                                    )
                          }
                      </div>
                  </div>`
        }`;
}

function notificationsPage() {
    const list = db.notifications.filter((n) => n.accountId === session.accountId);
    return /* HTML */ `${heading('Thông báo', 'Các cập nhật gửi đến tài khoản đã đặt lịch, ghi rõ tên người khám.', button('Đánh dấu tất cả đã đọc', 'read-all', 'check', 'btn outline'))}
        <section class="card notifications-list">
            ${
                list
                    .map(
                        (n) =>
                            /* HTML */ `<article class="notification-row ${n.read ? '' : 'unread'}">
                                <span class="notification-icon">${icon('bell')}</span>
                                <div>
                                    <div class="flex">
                                        <h3>${esc(n.title)}</h3>
                                        ${!n.read ? '<i class="unread-dot"></i>' : ''}
                                    </div>
                                    <p>${esc(n.content)}</p>
                                    <small>${fmtDate(n.date)}</small>
                                </div>
                                <div>
                                    ${button(n.read ? 'Xem lịch hẹn' : 'Đọc thông báo', 'read-notice', 'arrow', 'text-link', `data-id="${n.id}"`)}
                                </div>
                            </article>`
                    )
                    .join('') || empty('Chưa có thông báo')
            }
        </section>`;
}
