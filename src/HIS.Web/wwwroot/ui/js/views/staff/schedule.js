/* views / staff / schedule */
function schedulePage() {
    const head = session.role === 'DEPARTMENT_HEAD',
        ds = db.doctors.filter((d) =>
            head
                ? d.deptId === acc().deptId
                : session.role === 'DOCTOR'
                  ? d.id === acc().doctorId
                  : d.deptId === acc().deptId
        ),
        dow = new Date(TODAY + 'T12:00:00Z').getUTCDay(),
        monday = dateAdd(TODAY, 1 - dow + ui.scheduleOffset * 7),
        days = [0, 1, 2, 3, 4].map((n) => dateAdd(monday, n)),
        duty = db.dutyShifts.filter((s) =>
            head ? s.deptId === acc().deptId : s.accountId === session.accountId
        );
    return /* HTML */ `${heading(head ? 'Lịch khám & điều phối khoa' : 'Lịch làm việc của tôi', 'Lịch khám sáng / chiều và lịch trực nhân sự được quản lý riêng.', head ? button('Phân ca trực', 'new-duty', 'plus', 'btn outline') + button('Thiết lập lịch khám', 'new-schedule', 'calendar', 'btn primary') : button('Đề nghị nghỉ / đổi ca', 'new-exception', 'clock', 'btn outline'))}
        <div class="schedule-week-nav">
            <div>
                ${button('', 'prev-week', 'chevron', 'icon-btn prev', 'aria-label="Tuần trước"')}<strong
                    >${fmtDate(days[0])} – ${fmtDate(days[4])}</strong
                >${button('', 'next-week', 'chevron', 'icon-btn', 'aria-label="Tuần sau"')}
            </div>
            ${button('Tuần này', 'this-week', 'calendar', 'btn ghost sm')}
        </div>
        <section class="card">
            ${cardHeader('Lịch khám trong tuần', 'Sáng 07:30–11:30 · Chiều 13:00–16:30', '', 'calendar')}
            <div class="calendar-scroll">
                <div class="calendar-grid">
                    <div class="calendar-col label-col">
                        <header>Bác sĩ / Phòng</header>
                        ${ds.map((d) => /* HTML */ `<div class="calendar-doctor">${avatar(d.name, d.color)}<strong>BS. ${d.name}</strong><small>${d.specialty} · P.${room(d.roomId).number}</small></div>`).join('')}
                    </div>
                    ${days
                        .map(
                            (date, index) =>
                                /* HTML */ `<div
                                    class="calendar-col ${date === TODAY ? 'today' : ''}"
                                >
                                    <header>
                                        <small
                                            >Thứ ${['Hai', 'Ba', 'Tư', 'Năm', 'Sáu'][index]}</small
                                        ><strong>${date.slice(8)}/${date.slice(5, 7)}</strong
                                        >${date === TODAY ? '<b>Hôm nay</b>' : ''}
                                    </header>
                                    ${ds
                                        .map((d) => {
                                            const exceptions = db.exceptions.filter(
                                                    (e) =>
                                                        e.doctorId === d.id &&
                                                        e.date === date &&
                                                        e.status === 'APPROVED'
                                                ),
                                                active = db.schedules.some(
                                                    (s) => s.doctorId === d.id && s.active
                                                );
                                            return /* HTML */ `<div class="calendar-cell">
                                                ${
                                                    active
                                                        ? ['MORNING', 'AFTERNOON']
                                                              .map((shift) => {
                                                                  const ex = exceptions.find(
                                                                      (e) =>
                                                                          e.shift === shift ||
                                                                          e.shift === 'ALL'
                                                                  );
                                                                  return /* HTML */ `<div
                                                                      class="shift-chip ${ex ? 'blocked' : shift === 'AFTERNOON' ? 'blue' : ''}"
                                                                  >
                                                                      <strong
                                                                          >${shift === 'MORNING' ? 'Ca sáng' : 'Ca chiều'}</strong
                                                                      ><small
                                                                          >${ex ? 'Đã duyệt thay đổi' : shift === 'MORNING' ? '07:30–11:30' : '13:00–16:30'}</small
                                                                      >
                                                                  </div>`;
                                                              })
                                                              .join('')
                                                        : '<span class="muted">Chưa có lịch</span>'
                                                }
                                            </div>`;
                                        })
                                        .join('')}
                                </div>`
                        )
                        .join('')}
                </div>
            </div>
        </section>
        <section class="card space-top">
            ${cardHeader('Ca trực nhân sự', 'Ca trực đêm không tạo lịch đặt khám', '', 'clock')}${
                duty.length
                    ? /* HTML */ `<div class="table-wrap">
                          <table>
                              <thead>
                                  <tr>
                                      <th>Nhân sự</th>
                                      <th>Bắt đầu</th>
                                      <th>Kết thúc</th>
                                      <th>Ghi chú</th>
                                  </tr>
                              </thead>
                              <tbody>
                                  ${duty
                                      .map(
                                          (s) =>
                                              /* HTML */ `<tr>
                                                  <td>
                                                      <strong
                                                          >${db.accounts.find((a) => a.id === s.accountId)?.name}</strong
                                                      >
                                                  </td>
                                                  <td>
                                                      ${fmtDate(s.start)} · ${s.start.slice(11)}
                                                  </td>
                                                  <td>${fmtDate(s.end)} · ${s.end.slice(11)}</td>
                                                  <td>${esc(s.note)}</td>
                                              </tr>`
                                      )
                                      .join('')}
                              </tbody>
                          </table>
                      </div>`
                    : empty('Chưa có ca trực trong danh sách')
            }
        </section>
        ${
            head
                ? /* HTML */ `<section class="card space-top">
                      ${cardHeader('Hạn mức tiếp nhận', 'Giới hạn áp dụng cho mỗi ca khám', '', 'users')}
                      <div class="quota-grid">
                          ${ds
                              .map((d) => {
                                  const s = db.schedules.find((s) => s.doctorId === d.id);
                                  return /* HTML */ `<article>
                                      <h3>BS. ${d.name}</h3>
                                      <div>
                                          <span>Tổng / ca</span
                                          ><strong>${s?.maxPatients || 40}</strong>
                                      </div>
                                      <div>
                                          <span>Trực tuyến</span
                                          ><strong>${s?.onlineSlots || 28}</strong>
                                      </div>
                                      <div>
                                          <span>Tại quầy</span
                                          ><strong
                                              >${(s?.maxPatients || 40) - (s?.onlineSlots || 28)}</strong
                                          >
                                      </div>
                                      ${button('Chỉnh hạn mức', 'edit-schedule', 'edit', 'text-link', `data-id="${s?.id || ''}"`)}
                                  </article>`;
                              })
                              .join('')}
                      </div>
                  </section>`
                : ''
        }`;
}

function exceptionsPage() {
    const head = session.role === 'DEPARTMENT_HEAD',
        list = db.exceptions.filter(
            (e) =>
                (head
                    ? doctor(e.doctorId).deptId === acc().deptId
                    : e.doctorId === acc().doctorId) &&
                (ui.filter === 'ALL' || e.status === ui.filter)
        );
    return /* HTML */ `${heading(head ? 'Duyệt nghỉ / thay đổi ca' : 'Đề nghị nghỉ / đổi ca', head ? 'Kiểm tra bác sĩ thay thế và cập nhật lịch hẹn bị ảnh hưởng.' : 'Gửi yêu cầu để trưởng khoa xác nhận trước khi thay đổi lịch khám.', session.role === 'DOCTOR' ? button('Tạo đề nghị', 'new-exception', 'plus', 'btn primary') : '')}
        <section class="card">
            ${tabs(
                [
                    ['ALL', 'Tất cả'],
                    ['PENDING', 'Chờ duyệt'],
                    ['APPROVED', 'Đã duyệt'],
                    ['REJECTED', 'Từ chối'],
                ],
                ui.filter
            )}${
                list.length
                    ? /* HTML */ `<div class="table-wrap">
                          <table>
                              <thead>
                                  <tr>
                                      <th>Bác sĩ</th>
                                      <th>Ngày / Ca</th>
                                      <th>Loại / Lý do</th>
                                      <th>Đề xuất thay thế</th>
                                      <th>Trạng thái</th>
                                      <th>Thao tác</th>
                                  </tr>
                              </thead>
                              <tbody>
                                  ${list
                                      .map(
                                          (e) =>
                                              /* HTML */ `<tr>
                                                  <td>BS. ${doctor(e.doctorId).name}</td>
                                                  <td>
                                                      ${fmtDate(e.date)}<small
                                                          >${e.shift === 'MORNING' ? 'Ca sáng' : e.shift === 'AFTERNOON' ? 'Ca chiều' : 'Cả ngày'}</small
                                                      >
                                                  </td>
                                                  <td>
                                                      ${{ LEAVE: 'Nghỉ phép', TRAINING: 'Đào tạo', SURGERY: 'Phẫu thuật', SHIFT_SWAP: 'Đổi ca' }[e.type]}<small
                                                          >${esc(e.reason)}</small
                                                      >
                                                  </td>
                                                  <td>
                                                      ${e.replacementId ? 'BS. ' + doctor(e.replacementId).name : 'Tự tìm cùng khoa'}
                                                  </td>
                                                  <td>
                                                      ${badge(e.status, e.status === 'PENDING' ? 'Chờ duyệt' : undefined)}
                                                  </td>
                                                  <td>
                                                      <div class="row-actions">
                                                          ${head && e.status === 'PENDING' ? button('Duyệt', 'approve-exception', 'check', 'btn primary sm', `data-id="${e.id}"`) + button('Từ chối', 'reject-exception', 'close', 'btn ghost sm', `data-id="${e.id}"`) : '<span class="muted small">' + (e.approvedAt ? fmtDate(e.approvedAt) : 'Đang chờ trưởng khoa') + '</span>'}
                                                      </div>
                                                  </td>
                                              </tr>`
                                      )
                                      .join('')}
                              </tbody>
                          </table>
                      </div>`
                    : empty('Chưa có đề nghị trong mục này')
            }
        </section>
        ${notice('Có người thay: giữ lịch hẹn và cập nhật bác sĩ. Không còn chỗ thay thế: thông báo để người đặt chọn lại lịch. Lịch giữ chỗ bị ảnh hưởng được hủy.')}`;
}
