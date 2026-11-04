/* views / patient / booking */
function bookingPage() {
    const b = ui.booking,
        hold = appointment(b.holdId),
        p = patient(b.patientId),
        d = doctor(b.doctorId),
        links = myLinks();
    return /* HTML */ `${heading('Đặt lịch khám', 'Chọn đúng người khám và thời gian phù hợp cho cuộc hẹn của bạn.', '', 'CHỦ ĐỘNG TỪNG BƯỚC')}
        <ol class="booking-steps">
            ${['Khám cho ai', 'Chọn bác sĩ', 'Ngày & giờ', 'Xác nhận'].map((s, i) => /* HTML */ `<li class="${b.step === i + 1 ? 'active' : b.step > i + 1 ? 'done' : ''}"><span>${b.step > i + 1 ? icon('check') : i + 1}</span><strong>${s}</strong></li>`).join('')}
        </ol>
        <div class="booking-layout">
            <section class="card booking-panel">
                ${
                    b.step === 1
                        ? /* HTML */ `<div class="booking-section-head">
                                  <span class="section-icon">${icon('users')}</span>
                                  <div>
                                      <h2>Ai sẽ đến khám?</h2>
                                      <p>
                                          Thông tin được gắn với người được khám, không phải người
                                          đặt hộ.
                                      </p>
                                  </div>
                              </div>
                              <div class="profile-options">
                                  ${links
                                      .map((l) => {
                                          const q = patient(l.patientId);
                                          return /* HTML */ `<button
                                              class="profile-option ${b.patientId === q.id ? 'selected' : ''}"
                                              data-act="booking-profile"
                                              data-id="${q.id}"
                                          >
                                              ${avatar(q.name)}
                                              <div>
                                                  <strong>${q.name}</strong
                                                  ><span
                                                      >${relationNames[l.relationship]} ·
                                                      ${age(q.dob)} tuổi</span
                                                  >${l.status === 'PENDING' ? badge('PENDING_LINK') : ''}
                                              </div>
                                              <span class="radio-mark"
                                                  >${b.patientId === q.id ? icon('check') : ''}</span
                                              >
                                          </button>`;
                                      })
                                      .join('')}
                              </div>
                              <div class="inline-actions">
                                  ${button('Thêm người khám', 'new-family', 'plus', 'btn outline')}${button('Liên kết hồ sơ đã có', 'link-family', 'users', 'text-link')}
                              </div>
                              ${notice('Hồ sơ chờ xác minh được đặt lịch nhưng chưa được xem bệnh án. Việc xác minh diễn ra tại quầy tiếp nhận.')}
                              <div class="booking-actions">
                                  ${button('Tiếp tục chọn bác sĩ', 'booking-next', 'arrow', 'btn primary')}
                              </div>`
                        : b.step === 2
                          ? /* HTML */ `<div class="booking-section-head">
                                    <span class="section-icon">${icon('stethoscope')}</span>
                                    <div>
                                        <h2>Chọn bác sĩ phù hợp</h2>
                                        <p>
                                            Khám cho ${esc(p.name)} ·
                                            ${relationNames[linkFor(p.id)?.relationship]}
                                        </p>
                                    </div>
                                </div>
                                <div class="method-toggle">
                                    ${[
                                        ['specialty', 'Tôi chọn theo chuyên khoa'],
                                        ['doctor', 'Tôi đã biết bác sĩ muốn khám'],
                                    ]
                                        .map(
                                            ([m, label]) =>
                                                /* HTML */ `<button
                                                    class="${b.method === m ? 'active' : ''}"
                                                    data-act="booking-method"
                                                    data-value="${m}"
                                                >
                                                    ${icon(m === 'doctor' ? 'search' : 'grid')}${label}
                                                </button>`
                                        )
                                        .join('')}
                                </div>
                                ${b.method === 'doctor' ? /* HTML */ `<div class="field"><label for="doctor-search">Tìm tên bác sĩ</label><input id="doctor-search" data-search="booking-doctor" value="${esc(ui.query)}" placeholder="Nhập tên bác sĩ hoặc chuyên khoa" /></div>` : ''}
                                <div class="booking-doctors">
                                    ${db.doctors
                                        .filter(
                                            (d) =>
                                                d.active &&
                                                (b.method !== 'doctor' ||
                                                    matched(d.name + ' ' + d.specialty))
                                        )
                                        .map(
                                            (x) =>
                                                /* HTML */ `<button
                                                    class="booking-doctor ${b.doctorId === x.id ? 'selected' : ''}"
                                                    data-act="booking-doctor"
                                                    data-id="${x.id}"
                                                >
                                                    ${avatar(x.name, x.color)}
                                                    <div>
                                                        <span>${x.specialty}</span
                                                        ><strong>BS. ${x.name}</strong
                                                        ><small
                                                            >${dept(x.deptId).name} · Phòng
                                                            ${room(x.roomId).number}</small
                                                        >
                                                    </div>
                                                    <strong class="doctor-fee"
                                                        >${money(x.fee)}</strong
                                                    ><span class="radio-mark"
                                                        >${b.doctorId === x.id ? icon('check') : ''}</span
                                                    >
                                                </button>`
                                        )
                                        .join('')}
                                </div>
                                <div class="booking-actions">
                                    ${button('Quay lại', 'booking-prev', 'chevron', 'btn ghost prev')}${button('Chọn ngày & giờ', 'booking-next', 'arrow', 'btn primary')}
                                </div>`
                          : b.step === 3
                            ? /* HTML */ `<div class="booking-section-head">
                                      <span class="section-icon">${icon('calendar')}</span>
                                      <div>
                                          <h2>Chọn ngày & khung giờ</h2>
                                          <p>
                                              BS. ${d.name} · ${d.specialty} · Phòng
                                              ${room(d.roomId).number}
                                          </p>
                                      </div>
                                  </div>
                                  <div class="date-options">
                                      ${workingDays(10)
                                          .map(
                                              (date) =>
                                                  /* HTML */ `<button
                                                      class="date-option ${b.date === date ? 'selected' : ''}"
                                                      data-act="booking-date"
                                                      data-date="${date}"
                                                  >
                                                      <small
                                                          >${date === TODAY ? 'Hôm nay' : new Intl.DateTimeFormat('vi-VN', { weekday: 'short' }).format(new Date(date + 'T12:00:00'))}</small
                                                      ><strong
                                                          >${date.slice(8)}/${date.slice(5, 7)}</strong
                                                      >
                                                  </button>`
                                          )
                                          .join('')}
                                  </div>
                                  <div class="shift-toggle">
                                      ${[
                                          ['MORNING', 'Buổi sáng'],
                                          ['AFTERNOON', 'Buổi chiều'],
                                      ]
                                          .map(
                                              ([shift, label]) =>
                                                  /* HTML */ `<button
                                                      class="${b.shift === shift ? 'active' : ''}"
                                                      data-act="booking-shift"
                                                      data-value="${shift}"
                                                  >
                                                      ${icon('clock')}${label}<small
                                                          >${availableTimes(d.id, b.date, shift).length}
                                                          giờ còn trống</small
                                                      >
                                                  </button>`
                                          )
                                          .join('')}
                                  </div>
                                  <div class="time-grid">
                                      ${
                                          availableTimes(d.id, b.date, b.shift)
                                              .map(
                                                  (time) =>
                                                      /* HTML */ `<button
                                                          class="time-option ${b.time === time ? 'selected' : ''}"
                                                          data-act="booking-time"
                                                          data-value="${time}"
                                                      >
                                                          ${time}${b.time === time ? icon('check') : ''}
                                                      </button>`
                                              )
                                              .join('') ||
                                          empty(
                                              'Chưa có giờ khám khả dụng',
                                              'Vui lòng chọn buổi hoặc ngày khác.'
                                          )
                                      }
                                  </div>
                                  <div class="field">
                                      <label for="booking-reason">Lý do khám *</label
                                      ><textarea
                                          id="booking-reason"
                                          data-booking-reason
                                          maxlength="255"
                                          placeholder="Mô tả ngắn nhu cầu thăm khám…"
                                      >
${esc(b.reason)}</textarea>
                                  </div>
                                  <div class="booking-actions">
                                      ${button('Chọn lại bác sĩ', 'booking-prev', 'chevron', 'btn ghost prev')}${button('Giữ chỗ & kiểm tra', 'hold-booking', 'arrow', 'btn primary')}
                                  </div>`
                            : /* HTML */ `<div class="booking-section-head">
                                      <span class="section-icon">${icon('check')}</span>
                                      <div>
                                          <h2>Kiểm tra lịch hẹn</h2>
                                          <p>
                                              Xác nhận trong thời gian giữ chỗ để hoàn tất đặt khám.
                                          </p>
                                      </div>
                                  </div>
                                  ${
                                      hold
                                          ? /* HTML */ `<div
                                                    class="hold-banner ${hold.status === 'EXPIRED' ? 'expired' : ''}"
                                                >
                                                    ${icon('clock')}
                                                    <div>
                                                        <strong
                                                            >Khung giờ đang được giữ cho bạn</strong
                                                        ><span
                                                            >Thời gian còn lại
                                                            <b data-countdown="${hold.id}"
                                                                >${Math.max(0, Math.ceil(((hold.holdUntil || 0) - Date.now()) / 60000))}:00</b
                                                            ></span
                                                        >
                                                    </div>
                                                </div>
                                                <div class="review-grid">
                                                    <div>
                                                        <small>Người khám</small
                                                        ><strong>${p.name}</strong
                                                        ><span
                                                            >${p.id} ·
                                                            ${relationNames[linkFor(p.id)?.relationship]}</span
                                                        >
                                                    </div>
                                                    <div>
                                                        <small>Bác sĩ</small
                                                        ><strong>BS. ${d.name}</strong
                                                        ><span>${d.specialty}</span>
                                                    </div>
                                                    <div>
                                                        <small>Ngày & giờ</small
                                                        ><strong
                                                            >${b.time} · ${fmtDate(b.date)}</strong
                                                        ><span
                                                            >${b.shift === 'MORNING' ? 'Buổi sáng' : 'Buổi chiều'}
                                                            · Phòng ${room(d.roomId).number}</span
                                                        >
                                                    </div>
                                                    <div>
                                                        <small>Phí khám tham khảo</small
                                                        ><strong>${money(d.fee)}</strong
                                                        ><span
                                                            >BHYT được đối chiếu tại quầy thu
                                                            phí</span
                                                        >
                                                    </div>
                                                    <div class="full">
                                                        <small>Lý do khám</small
                                                        ><strong>${esc(b.reason)}</strong>
                                                    </div>
                                                </div>
                                                ${notice('Check-in mở từ 60 phút trước giờ khám. Vui lòng mang giấy tờ của người khám và mã lịch hẹn.')}
                                                <div class="booking-actions">
                                                    ${button('Chọn lại khung giờ', 'release-hold', 'chevron', 'btn ghost prev')}${hold.status === 'PENDING' ? button('Xác nhận đặt lịch', 'confirm-booking', 'check', 'btn primary') : button('Chọn lịch mới', 'release-hold', 'refresh', 'btn primary')}
                                                </div>`
                                          : empty(
                                                'Chưa có lịch giữ chỗ',
                                                'Vui lòng chọn lại lịch khám.',
                                                button(
                                                    'Chọn lại',
                                                    'release-hold',
                                                    'refresh',
                                                    'btn primary'
                                                )
                                            )
                                  }`
                }
            </section>
            <aside class="booking-summary card">
                <span class="mini-label">CUỘC HẸN CỦA BẠN</span>
                <div class="summary-person">
                    ${avatar(p?.name || 'Người khám')}
                    <div>
                        <strong>${esc(p?.name || 'Chọn người khám')}</strong
                        ><span
                            >${p ? relationNames[linkFor(p.id)?.relationship] || 'Người khám' : ''}</span
                        >
                    </div>
                </div>
                <div class="summary-row">
                    ${icon('stethoscope')}
                    <div>
                        <small>Bác sĩ / Chuyên khoa</small><strong>BS. ${d?.name}</strong
                        ><span>${d?.specialty}</span>
                    </div>
                </div>
                <div class="summary-row">
                    ${icon('calendar')}
                    <div>
                        <small>Ngày & giờ</small><strong>${fmtDate(b.date)}</strong
                        ><span>${b.time || 'Chưa chọn giờ khám'}</span>
                    </div>
                </div>
                <div class="summary-price">
                    <span>Phí khám dự kiến</span><strong>${money(d?.fee)}</strong>
                </div>
                <p class="fine-print">
                    Chi phí bổ sung phát sinh theo chỉ định và được thể hiện trên hóa đơn.
                </p>
                <div class="summary-help">
                    ${icon('shield')}<span
                        >Quyền xem bệnh án được xác minh riêng với quyền đặt lịch.</span
                    >
                </div>
            </aside>
        </div>`;
}
