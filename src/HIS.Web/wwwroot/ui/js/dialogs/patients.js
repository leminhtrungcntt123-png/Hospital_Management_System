/* dialogs / patients */
function requireRole(...roles) {
    if (!roles.includes(session.role))
        throw new Error('Vai trò hiện tại không được thực hiện thao tác này.');
}

function patientFields(p = {}, prefix = 'p') {
    return /* HTML */ `<div class="form-grid">
        <div class="field full">
            <label for="${prefix}-name">Họ và tên *</label
            ><input
                id="${prefix}-name"
                name="name"
                value="${esc(p.name || '')}"
                required
                minlength="3"
                maxlength="100"
            />
        </div>
        <div class="field">
            <label for="${prefix}-dob">Ngày sinh *</label
            ><input
                id="${prefix}-dob"
                name="dob"
                type="date"
                max="${TODAY}"
                value="${esc(p.dob || '')}"
                required
            />
        </div>
        <div class="field">
            <label for="${prefix}-gender">Giới tính</label
            ><select id="${prefix}-gender" name="gender">
                ${['Nữ', 'Nam', 'Khác'].map((g) => /* HTML */ `<option ${p.gender === g ? 'selected' : ''}>${g}</option>`).join('')}
            </select>
        </div>
        <div class="field">
            <label for="${prefix}-phone">Điện thoại *</label
            ><input
                id="${prefix}-phone"
                name="phone"
                type="tel"
                pattern="[0-9+ ()-]{9,16}"
                value="${esc(p.phone || '')}"
                required
            />
        </div>
        <div class="field">
            <label for="${prefix}-national">CCCD / Giấy tờ</label
            ><input
                id="${prefix}-national"
                name="nationalId"
                value="${esc(p.nationalId || '')}"
                maxlength="20"
                placeholder="Có thể để trống với trẻ nhỏ"
            />
        </div>
        <div class="field full">
            <label for="${prefix}-address">Địa chỉ</label
            ><input
                id="${prefix}-address"
                name="address"
                value="${esc(p.address || '')}"
                maxlength="255"
            />
        </div>
    </div>`;
}

function newPatient(f) {
    return {
        id:
            'BN' +
            String(
                Math.max(0, ...db.patients.map((p) => Number(p.id.replace('BN', '')) || 0)) + 1
            ).padStart(4, '0'),
        name: plainName(f.get('name')),
        dob: f.get('dob'),
        gender: f.get('gender'),
        phone: String(f.get('phone')).trim(),
        nationalId: String(f.get('nationalId') || '').trim(),
        address: String(f.get('address') || '').trim(),
        allergy: 'Chưa ghi nhận',
    };
}

function familyModal() {
    requireRole('PATIENT');
    if (myLinks().length >= CONFIG.maxProfiles) throw new Error('Bạn đã sử dụng đủ 5 hồ sơ.');
    openModal(
        'Thêm người khám',
        'Tạo hồ sơ mới và liên kết với tài khoản của bạn.',
        /* HTML */ `<form id="family-form">
            ${patientFields()}
            <div class="field space-top">
                <label for="family-relationship">Mối quan hệ với bạn *</label
                ><select id="family-relationship" name="relationship">
                    ${Object.entries(relationNames)
                        .filter(([k]) => k !== 'SELF')
                        .map(([k, n]) => /* HTML */ `<option value="${k}">${n}</option>`)
                        .join('')}
                </select>
            </div>
            ${notice('Hồ sơ trẻ dưới 18 tuổi được liên kết là Con có thể xem bệnh án. Các hồ sơ người lớn cần xác minh quyền xem riêng.')}
        </form>`,
        button('Quay lại', 'close') +
            '<button class="btn primary" type="submit" form="family-form">' +
            icon('check') +
            'Tạo hồ sơ</button>',
        true
    );
}

function linkFamilyModal() {
    requireRole('PATIENT');
    if (myLinks().length >= CONFIG.maxProfiles) throw new Error('Bạn đã sử dụng đủ 5 hồ sơ.');
    openModal(
        'Liên kết hồ sơ đã có',
        'Điền thông tin người bệnh để gửi yêu cầu xác minh.',
        /* HTML */ `<form id="link-family-form">
            <div class="form-grid">
                <div class="field full">
                    <label for="link-name">Họ tên người bệnh *</label
                    ><input id="link-name" name="name" required />
                </div>
                <div class="field">
                    <label for="link-dob">Ngày sinh *</label
                    ><input id="link-dob" name="dob" type="date" max="${TODAY}" required />
                </div>
                <div class="field">
                    <label for="link-relation">Mối quan hệ *</label
                    ><select id="link-relation" name="relationship">
                        ${Object.entries(relationNames)
                            .filter(([k]) => k !== 'SELF')
                            .map(([k, n]) => /* HTML */ `<option value="${k}">${n}</option>`)
                            .join('')}
                    </select>
                </div>
                <div class="field">
                    <label for="link-national">CCCD / Giấy tờ</label
                    ><input id="link-national" name="nationalId" />
                </div>
                <div class="field">
                    <label for="link-insurance">Mã thẻ BHYT</label
                    ><input id="link-insurance" name="cardNumber" />
                </div>
                <div class="field full">
                    <label for="link-phone">Điện thoại người bệnh (trẻ dưới 18 tuổi)</label
                    ><input id="link-phone" name="phone" type="tel" />
                </div>
            </div>
            ${notice('Người lớn cần khớp họ tên, ngày sinh và CCCD hoặc thẻ BHYT. Yêu cầu khớp thông tin chuyển sang chờ xác minh, chưa mở quyền xem bệnh án.')}
        </form>`,
        button('Quay lại', 'close') +
            '<button class="btn primary" form="link-family-form" type="submit">Gửi yêu cầu liên kết</button>',
        true
    );
}

function walkinModal() {
    requireRole('NURSE');
    openModal(
        'Tiếp nhận khách vãng lai',
        'Tạo hoặc chọn hồ sơ, phân phòng và cấp số tại quầy.',
        /* HTML */ `<form id="walkin-form">
            <div class="field">
                <label for="walk-profile">Hồ sơ người khám</label
                ><select id="walk-profile" name="existing" data-change="walk-existing">
                    <option value="NEW">Người khám mới — tạo hồ sơ</option>
                    ${db.patients.map((p) => /* HTML */ `<option value="${p.id}">${p.name} · ${p.id} · ${p.phone}</option>`).join('')}
                </select>
            </div>
            <div id="walk-new-fields" class="space-top">${patientFields({}, 'walk')}</div>
            <div class="form-grid space-top">
                <div class="field full">
                    <label for="walk-doctor">Bác sĩ / Phòng khám *</label
                    ><select id="walk-doctor" name="doctorId">
                        ${db.doctors
                            .filter((d) => d.active && d.deptId === acc().deptId)
                            .map(
                                (d) =>
                                    /* HTML */ `<option value="${d.id}">
                                        ${d.specialty} · BS. ${d.name} · P.${room(d.roomId).number}
                                    </option>`
                            )
                            .join('')}
                    </select>
                </div>
                <div class="field">
                    <label for="walk-time">Giờ tiếp nhận *</label
                    ><input
                        id="walk-time"
                        name="time"
                        type="time"
                        value="${window.hisLive ? demoNow().toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Ho_Chi_Minh' }) : '09:00'}"
                        required
                    />
                </div>
                <div class="field">
                    <label for="walk-priority">Mức ưu tiên</label
                    ><select id="walk-priority" name="priority">
                        <option value="0">Thông thường</option>
                        <option value="1">Ưu tiên 1</option>
                        <option value="2">Ưu tiên 2</option>
                        <option value="3">Ưu tiên 3</option>
                    </select>
                </div>
                <div class="field full">
                    <label for="walk-reason">Lý do khám *</label
                    ><input id="walk-reason" name="reason" required maxlength="255" />
                </div>
            </div>
        </form>`,
        button('Quay lại', 'close') +
            '<button class="btn primary" form="walkin-form" type="submit">' +
            icon('userplus') +
            'Tiếp nhận & cấp số W</button>',
        true
    );
}

function appointmentDetail(id) {
    const a = appointment(id);
    if (!a || !scopedApps().some((x) => x.id === id))
        throw new Error('Bạn không có quyền xem lịch hẹn này.');
    const p = patient(a.patientId),
        d = doctor(a.doctorId),
        mins = (appointmentTime(a) - demoNow()) / 60000,
        canCancel =
            ['PENDING', 'RESCHEDULE_REQUIRED'].includes(a.status) ||
            (a.status === 'CONFIRMED' && mins >= CONFIG.cancelCutoff);
    openModal(
        'Chi tiết lịch hẹn',
        a.code,
        /* HTML */ `<div class="patient-strip">
                ${avatar(p.name)}
                <div>
                    <h3>${p.name}</h3>
                    <p>${p.id} · ${p.phone}</p>
                </div>
                ${badge(a.status)}
            </div>
            <div class="review-grid">
                <div><small>Ngày & giờ</small><strong>${a.time} · ${fmtDate(a.date)}</strong></div>
                <div><small>Bác sĩ</small><strong>BS. ${d?.name || 'Chờ cập nhật'}</strong></div>
                <div>
                    <small>Chuyên khoa / Phòng</small
                    ><strong>${d?.specialty || ''} · P.${room(a.roomId)?.number || '—'}</strong>
                </div>
                <div>
                    <small>Kênh / Số thứ tự</small
                    ><strong
                        >${a.channel === 'WEB' ? 'Trực tuyến' : 'Tại quầy'} ·
                        ${a.queue || 'Chưa cấp số'}</strong
                    >
                </div>
                <div class="full">
                    <small>Lý do khám</small><strong>${esc(a.reason || '')}</strong>
                </div>
            </div>
            ${a.status === 'CONFIRMED' && mins < CONFIG.cancelCutoff && session.role === 'PATIENT' ? notice('Lịch này nằm trong thời gian 120 phút trước giờ khám. Vui lòng liên hệ quầy để được hỗ trợ thay đổi.') : ''}
            <details class="history-details">
                <summary>Lịch sử trạng thái</summary>
                ${(a.history || []).map((h) => /* HTML */ `<div><span>${statusNames[h.to] || h.to}</span><small>${esc(h.by)} · ${fmtDate(h.at)}</small></div>`).join('')}
            </details>`,
        button('Đóng', 'close') +
            (session.role === 'PATIENT' && canCancel
                ? button(
                      'Hủy lịch hẹn',
                      'cancel-appointment',
                      'close',
                      'btn danger',
                      `data-id="${a.id}"`
                  )
                : '') +
            (session.role === 'PATIENT' && a.status === 'RESCHEDULE_REQUIRED'
                ? button(
                      'Chọn lịch mới',
                      'reschedule',
                      'calendar',
                      'btn primary',
                      `data-id="${a.id}"`
                  )
                : '') +
            (session.role === 'NURSE' && a.status === 'CONFIRMED'
                ? button('Tiếp nhận', 'checkin', 'check', 'btn primary', `data-id="${a.id}"`)
                : ''),
        true
    );
}

function patientDetail(id) {
    const p = patient(id);
    if (!p) throw new Error('Không tìm thấy hồ sơ.');
    if (session.role === 'PATIENT' && !linkFor(id)) throw new Error('Hồ sơ chưa được liên kết.');
    const rs = db.records.filter((r) => r.patientId === id && r.final && recordAllowed(r));
    openModal(
        'Thông tin người khám',
        p.id,
        /* HTML */ `<div class="patient-strip">
                ${avatar(p.name, 'cyan', true)}
                <div>
                    <h2>${p.name}</h2>
                    <p>${p.gender} · ${age(p.dob)} tuổi</p>
                </div>
            </div>
            <div class="info-grid">
                <div><small>Ngày sinh</small><strong>${fmtDate(p.dob)}</strong></div>
                <div><small>Điện thoại</small><strong>${p.phone}</strong></div>
                <div>
                    <small>Giấy tờ</small><strong>${esc(p.nationalId || 'Chưa cập nhật')}</strong>
                </div>
                <div>
                    <small>Địa chỉ</small><strong>${esc(p.address || 'Chưa cập nhật')}</strong>
                </div>
            </div>
            ${
                rs.length
                    ? /* HTML */ `<h3 class="modal-section-title">Phiếu khám được phép xem</h3>
                          ${rs
                              .map(
                                  (r) =>
                                      /* HTML */ `<div class="history-record">
                                          <div>
                                              <strong>${esc(r.diagnosis)}</strong
                                              ><small
                                                  >${fmtDate(r.date)} · BS.
                                                  ${doctor(r.doctorId).name}</small
                                              >
                                          </div>
                                          ${button('Xem phiếu', 'record-detail', 'eye', 'text-link', `data-id="${r.id}"`)}
                                      </div>`
                              )
                              .join('')}`
                    : ['NURSE', 'ADMIN'].includes(session.role)
                      ? notice(
                            'Vai trò này chỉ xem thông tin hành chính và sinh hiệu được cấp quyền.'
                        )
                      : empty('Chưa có phiếu khám được phép xem')
            }`,
        button('Đóng', 'close'),
        true
    );
}
