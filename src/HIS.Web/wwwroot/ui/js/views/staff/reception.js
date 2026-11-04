/* views / staff / reception */
function appointmentsTable(apps, compact = false) {
    if (!apps.length)
        return empty(
            'Không có lượt khám trong mục này',
            'Thử đổi trạng thái hoặc tìm theo người khám.'
        );
    return /* HTML */ `<div class="table-wrap">
        <table class="appointments-table ${compact ? 'compact' : ''}">
            <thead>
                <tr>
                    <th>Giờ hẹn</th>
                    <th>Người khám</th>
                    ${compact ? '' : '<th>Bác sĩ / Phòng</th><th>Kênh / Số</th>'}
                    <th>Trạng thái</th>
                    <th class="right">Thao tác</th>
                </tr>
            </thead>
            <tbody>
                ${apps
                    .map((a) => {
                        const p = patient(a.patientId),
                            early =
                                a.date === TODAY &&
                                appointmentTime(a) - demoNow() <= CONFIG.checkinEarly * 60000;
                        return /* HTML */ `<tr>
                            <td>
                                <strong class="time-text">${a.time}</strong
                                >${compact ? '' : /* HTML */ `<small>${fmtDate(a.date)}</small>`}
                            </td>
                            <td>
                                ${personCell(p)}${a.priority ? '<span class="priority-label">Ưu tiên ' + a.priority + '</span>' : ''}
                            </td>
                            ${
                                compact
                                    ? ''
                                    : /* HTML */ `<td>
                                              BS. ${doctor(a.doctorId)?.name || 'Chờ phân'}<small
                                                  >P.${room(a.roomId)?.number || '—'} ·
                                                  ${doctor(a.doctorId)?.specialty || ''}</small
                                              >
                                          </td>
                                          <td>
                                              <strong>${a.queue || 'Chưa cấp số'}</strong
                                              ><small
                                                  >${a.channel === 'WEB' ? 'Trực tuyến' : 'Tại quầy'}</small
                                              >
                                          </td>`
                            }
                            <td>${badge(a.status)}</td>
                            <td>
                                <div class="row-actions">
                                    ${session.role === 'NURSE' && a.status === 'CONFIRMED' ? button(early ? 'Tiếp nhận' : 'Chưa mở', 'checkin', 'check', 'btn primary sm', `data-id="${a.id}" ${early ? '' : 'disabled title="Mở từ 60 phút trước giờ hẹn"'}`) : session.role === 'NURSE' && a.status === 'CHECKED_IN' ? button('Đo sinh hiệu', 'vitals', 'pulse', 'btn primary sm', `data-id="${a.id}"`) : button('Chi tiết', 'appointment-detail', 'chevron', 'btn outline sm', `data-id="${a.id}"`)}
                                </div>
                            </td>
                        </tr>`;
                    })
                    .join('')}
            </tbody>
        </table>
    </div>`;
}

function receptionPage() {
    const apps = todayApps().filter(
        (a) =>
            (ui.filter === 'ALL' || a.status === ui.filter) &&
            matched(
                patient(a.patientId).name +
                    ' ' +
                    a.code +
                    ' ' +
                    (a.queue || '') +
                    ' ' +
                    patient(a.patientId).phone
            )
    );
    return /* HTML */ `${heading('Tiếp nhận & hàng chờ', 'Kiểm tra lịch hẹn, xác minh người đặt hộ và cấp số cho đúng người khám.', button('Khách vãng lai', 'walkin', 'userplus', 'btn primary'))}
        <div class="operation-summary">
            <span>${icon('calendar')}Ngày mẫu ${fmtDate(TODAY)}</span
            ><span>Check-in từ <b>60 phút</b> trước hẹn</span
            ><span><b>O</b> Trực tuyến · <b>W</b> Tại quầy</span>
        </div>
        <div class="reception-layout">
            <section class="card">
                ${cardHeader('Danh sách lượt khám', '', '', 'users')}${tabs(
                    [
                        ['ALL', 'Tất cả'],
                        ['CONFIRMED', 'Chờ tiếp nhận'],
                        ['CHECKED_IN', 'Đã tiếp nhận'],
                        ['READY_FOR_DOCTOR', 'Chờ bác sĩ'],
                        ['IN_PROGRESS', 'Đang khám'],
                    ],
                    ui.filter
                )}
                <div class="toolbar">${searchInput('Tìm tên, mã hẹn hoặc số thứ tự…')}</div>
                ${appointmentsTable(apps)}
            </section>
            ${queuePanel()}
        </div>`;
}

function queuePanel() {
    let apps = todayApps()
            .filter(
                (a) =>
                    ['READY_FOR_DOCTOR', 'IN_PROGRESS'].includes(a.status) &&
                    (ui.roomId === 'ALL' || a.roomId === ui.roomId)
            )
            .sort((a, b) => b.priority - a.priority || a.time.localeCompare(b.time)),
        called =
            apps.find((a) => a.calledAt && a.status === 'READY_FOR_DOCTOR') ||
            apps.find((a) => a.status === 'IN_PROGRESS'),
        waiting = apps.filter((a) => a.id !== called?.id && a.status === 'READY_FOR_DOCTOR');
    return /* HTML */ `<aside class="queue-panel card">
        <div class="queue-panel-head">
            <h2>${icon('volume')}Điều phối hàng chờ</h2>
            <select data-change="roomId" aria-label="Lọc phòng khám">
                <option value="ALL">Tất cả phòng</option>
                ${db.rooms
                    .filter((r) => r.deptId === acc()?.deptId)
                    .map(
                        (r) =>
                            /* HTML */ `<option
                                value="${r.id}"
                                ${ui.roomId === r.id ? 'selected' : ''}
                            >
                                Phòng ${r.number}
                            </option>`
                    )
                    .join('')}
            </select>
        </div>
        <div class="ticket">
            <div class="flex-between">
                <span>${called?.status === 'IN_PROGRESS' ? 'ĐANG KHÁM' : 'ĐANG MỜI VÀO KHÁM'}</span
                ><b>${called ? 'P.' + room(called.roomId).number : 'Sẵn sàng'}</b>
            </div>
            <strong>${called?.queue || '—'}</strong>
            <h3>${called ? patient(called.patientId).name : 'Chưa có số đang gọi'}</h3>
            <p>${called ? doctor(called.doctorId).specialty : 'Chọn gọi số để mời bệnh nhân'}</p>
            ${session.role === 'NURSE' ? (called && called.status === 'READY_FOR_DOCTOR' ? button('Gọi lại số ' + called.queue, 'recall', 'volume', 'btn primary full', `data-id="${called.id}"`) : button('Gọi số tiếp theo', 'call-next', 'volume', 'btn primary full', `${waiting.length ? '' : 'disabled'}`)) : ''}
        </div>
        <div class="queue-next">
            <div class="mini-heading">
                <span>TIẾP THEO</span><span>${waiting.length} đang chờ</span>
            </div>
            ${
                waiting
                    .slice(0, 4)
                    .map(
                        (a) =>
                            /* HTML */ `<div class="queue-row">
                                <b>${a.queue}</b
                                ><span
                                    ><strong>${patient(a.patientId).name}</strong
                                    ><small
                                        >Phòng
                                        ${room(a.roomId).number}${a.priority ? ' · Ưu tiên ' + a.priority : ''}</small
                                    ></span
                                ><small>${a.time}</small>
                            </div>`
                    )
                    .join('') ||
                '<p class="muted small">Không còn bệnh nhân chờ trong phòng này.</p>'
            }
        </div>
    </aside>`;
}

function vitalsPage() {
    const apps = todayApps().filter(
        (a) =>
            ['CHECKED_IN', 'READY_FOR_DOCTOR'].includes(a.status) &&
            matched(patient(a.patientId).name)
    );
    return /* HTML */ `${heading('Sinh hiệu trước khám', 'Ghi đủ sinh hiệu để chuyển lượt tiếp nhận sang hàng chờ bác sĩ.')}
        <section class="card">
            <div class="toolbar">${searchInput('Tìm người khám cần đo sinh hiệu…')}</div>
            ${
                apps.length
                    ? /* HTML */ `<div class="table-wrap">
                          <table>
                              <thead>
                                  <tr>
                                      <th>Người khám</th>
                                      <th>Số khám / Phòng</th>
                                      <th>Mạch</th>
                                      <th>Huyết áp</th>
                                      <th>Nhiệt độ</th>
                                      <th>Trạng thái</th>
                                      <th>Thao tác</th>
                                  </tr>
                              </thead>
                              <tbody>
                                  ${apps
                                      .map((a) => {
                                          const v = db.vitals.find((v) => v.appointmentId === a.id);
                                          return /* HTML */ `<tr>
                                              <td>${personCell(patient(a.patientId))}</td>
                                              <td>
                                                  ${a.queue}<small
                                                      >Phòng ${room(a.roomId).number}</small
                                                  >
                                              </td>
                                              <td>${v?.pulse || '—'}</td>
                                              <td>${v ? v.bpSys + '/' + v.bpDia : '—'}</td>
                                              <td>${v ? v.temp + ' °C' : '—'}</td>
                                              <td>
                                                  ${v ? '<span class="pill success">Đã ghi nhận</span>' : '<span class="pill amber">Cần thực hiện</span>'}
                                              </td>
                                              <td>
                                                  ${button(v ? 'Cập nhật' : 'Ghi sinh hiệu', 'vitals', 'pulse', 'btn primary sm', `data-id="${a.id}"`)}
                                              </td>
                                          </tr>`;
                                      })
                                      .join('')}
                              </tbody>
                          </table>
                      </div>`
                    : empty('Đã hoàn tất danh sách sinh hiệu')
            }
        </section>`;
}

function patientsPage() {
    const ids = new Set(scopedApps().map((a) => a.patientId)),
        ps = db.patients.filter(
            (p) =>
                (ids.has(p.id) || session.role === 'NURSE') &&
                matched(p.name + ' ' + p.id + ' ' + p.phone)
        );
    return /* HTML */ `${heading('Thông tin người khám', 'Thông tin hành chính phục vụ tiếp nhận. Nội dung bệnh án theo quyền chuyên môn.')}
        <section class="card">
            <div class="toolbar">${searchInput()}<span>${ps.length} hồ sơ</span></div>
            <div class="table-wrap">
                <table>
                    <thead>
                        <tr>
                            <th>Người khám</th>
                            <th>Giới tính</th>
                            <th>Điện thoại</th>
                            <th>Ngày sinh</th>
                            <th>Hồ sơ</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${ps
                            .map(
                                (p) =>
                                    /* HTML */ `<tr>
                                        <td>${personCell(p)}</td>
                                        <td>${p.gender}</td>
                                        <td>${p.phone}</td>
                                        <td>${fmtDate(p.dob)}</td>
                                        <td>
                                            ${button('Thông tin', 'patient-detail', 'eye', 'btn outline sm', `data-id="${p.id}"`)}
                                        </td>
                                    </tr>`
                            )
                            .join('')}
                    </tbody>
                </table>
            </div>
        </section>`;
}
