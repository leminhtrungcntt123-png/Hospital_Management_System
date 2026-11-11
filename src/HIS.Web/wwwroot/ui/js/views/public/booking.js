let guestBooking;
function resetGuestBooking() {
    guestBooking = {
        doctorId: config.selectedDoctorId || '',
        department: '',
        date: config.bookingMinDate || '',
        slots: [],
        selected: '',
        status: 'idle',
        error: '',
        submitting: false,
        receipt: null,
        patient: {},
    };
}
resetGuestBooking();

function ensureGuestSlots() {
    if (
        catalog.status === 'ready' &&
        guestBooking.doctorId &&
        guestBooking.date &&
        guestBooking.status === 'idle'
    )
        loadGuestSlots();
}

async function loadGuestSlots() {
    const key = guestBooking.doctorId + '/' + guestBooking.date;
    guestBooking.status = 'loading';
    guestBooking.error = '';
    guestBooking.slots = [];
    guestBooking.selected = '';
    render();
    try {
        const query = new URLSearchParams({
            doctorId: guestBooking.doctorId,
            date: guestBooking.date,
        });
        const response = await fetch(config.bookingSlotsUrl + '?' + query, {
            credentials: 'same-origin',
            cache: 'no-store',
        });
        const body = await response.json();
        if (!response.ok || !Array.isArray(body.slots))
            throw new Error(body.message || 'Chưa tải được lịch khám. Vui lòng thử lại.');
        if (key !== guestBooking.doctorId + '/' + guestBooking.date) return;
        guestBooking.slots = body.slots;
        guestBooking.status = 'ready';
    } catch (error) {
        if (key !== guestBooking.doctorId + '/' + guestBooking.date) return;
        guestBooking.status = 'error';
        guestBooking.error = error.message;
    }
    render();
}

function changeGuestChoice(field, value) {
    if (guestBooking.submitting) return;
    if (field === 'slot') {
        guestBooking.selected = value;
        guestBooking.error = '';
        render();
        return;
    }
    if (field === 'department') {
        guestBooking.department = value;
        guestBooking.doctorId = '';
    } else if (field === 'doctor') guestBooking.doctorId = value;
    else if (field === 'date') guestBooking.date = value;
    guestBooking.slots = [];
    guestBooking.selected = '';
    guestBooking.error = '';
    guestBooking.status = 'idle';
    render();
}

async function submitGuestBooking() {
    if (guestBooking.submitting) return;
    const slot = guestBooking.slots.find((item) => slotKey(item) === guestBooking.selected);
    if (!slot) return;
    guestBooking.submitting = true;
    guestBooking.error = '';
    render();
    try {
        const response = await fetch(config.bookingConfirmUrl, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: document.querySelector(
                    '[name="__RequestVerificationToken"]',
                ).value,
            },
            body: JSON.stringify({
                ...guestBooking.patient,
                gender:
                    guestBooking.patient.gender === '' || guestBooking.patient.gender == null
                        ? null
                        : Number(guestBooking.patient.gender),
                scheduleId: slot.scheduleId,
                startTime: slot.startTime,
            }),
        });
        const body = await response.json();
        if (!response.ok || !body.receipt?.appointmentCode)
            throw new Error(body.message || 'Chưa thể xác nhận lịch. Vui lòng thử lại sau.');
        guestBooking.receipt = body.receipt;
    } catch (error) {
        guestBooking.error = error.message;
    }
    guestBooking.submitting = false;
    render();
    document.querySelector('.booking-receipt h1, .booking-error')?.focus();
}

function slotKey(slot) {
    return slot.scheduleId + '|' + slot.startTime;
}
function bookingTime(value) {
    return new Date(value).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}
function bookingDate(value) {
    return new Date(value).toLocaleDateString('vi-VN');
}

function guestBookingPage() {
    if (guestBooking.receipt) return guestBookingReceipt();
    const selectedDoctor = catalog.doctors.find(
        (doctor) => String(doctor.id) === guestBooking.doctorId,
    );
    const doctors = catalog.doctors.filter(
        (doctor) => !guestBooking.department || doctor.department === guestBooking.department,
    );
    const selectedSlot = guestBooking.slots.find((slot) => slotKey(slot) === guestBooking.selected);
    const patient = guestBooking.patient;
    return `${heading('Đặt lịch khám', 'Chọn bác sĩ, giờ khám và điền thông tin người khám. Không cần đăng nhập.')}
        <ol class="guest-booking-steps" aria-label="Các bước đặt khám"><li class="active"><span>1</span>Bác sĩ & lịch khám</li><li class="${selectedSlot ? 'active' : ''}"><span>2</span>Thông tin người khám</li><li><span>3</span>Nhận mã lịch hẹn</li></ol>
        ${catalog.status !== 'ready' ? catalogStatus() : ''}
        <form class="guest-booking-layout" data-guest-booking><fieldset class="guest-booking-panels" aria-label="Thông tin đặt khám" ${guestBooking.submitting ? 'disabled' : ''}><section class="card guest-booking-panel"><div class="booking-section-title"><span>01</span><div><h2>Chọn lịch phù hợp</h2><p>Khung giờ được cập nhật theo lịch đang nhận của bác sĩ.</p></div></div>
            <div class="form-grid"><div class="field"><label for="guest-department">Chuyên khoa</label><select id="guest-department" data-booking-choice="department"><option value="">Tất cả chuyên khoa</option>${catalog.departments.map((dept) => `<option value="${esc(dept.name)}" ${guestBooking.department === dept.name ? 'selected' : ''}>${esc(dept.name)}</option>`).join('')}</select></div>
            <div class="field"><label for="guest-doctor">Bác sĩ <span aria-hidden="true">*</span></label><select id="guest-doctor" data-booking-choice="doctor" required><option value="">Chọn bác sĩ</option>${doctors.map((doctor) => `<option value="${esc(doctor.id)}" ${guestBooking.doctorId === String(doctor.id) ? 'selected' : ''}>${esc(doctor.name)}</option>`).join('')}</select></div>
            <div class="field full"><label for="guest-date">Ngày khám <span aria-hidden="true">*</span></label><input id="guest-date" data-booking-choice="date" type="date" value="${esc(guestBooking.date)}" min="${esc(config.bookingMinDate)}" max="${esc(config.bookingMaxDate)}" required /></div></div>
            <fieldset class="guest-slot-picker"><legend>Giờ khám <span aria-hidden="true">*</span></legend>${guestSlotsMarkup()}</fieldset>
        </section><section class="card guest-booking-panel"><div class="booking-section-title"><span>02</span><div><h2>Thông tin người khám</h2><p>Dùng thông tin của người sẽ trực tiếp đến khám.</p></div></div><div class="form-grid">
            <div class="field full"><label for="guest-name">Họ và tên <span aria-hidden="true">*</span></label><input id="guest-name" name="fullName" data-booking-field value="${esc(patient.fullName || '')}" minlength="2" maxlength="100" autocomplete="name" placeholder="Nhập họ và tên đầy đủ" required /></div>
            <div class="field"><label for="guest-birth">Ngày sinh <span aria-hidden="true">*</span></label><input id="guest-birth" name="dateOfBirth" data-booking-field value="${esc(patient.dateOfBirth || '')}" type="date" max="${esc(config.bookingMinDate)}" required /></div>
            <div class="field"><label for="guest-gender">Giới tính</label><select id="guest-gender" name="gender" data-booking-field><option value="">Chưa cung cấp</option>${[
                ['0', 'Nam'],
                ['1', 'Nữ'],
                ['2', 'Khác'],
            ]
                .map(
                    ([value, title]) =>
                        `<option value="${value}" ${patient.gender === value ? 'selected' : ''}>${title}</option>`,
                )
                .join('')}</select></div>
            <div class="field full"><label for="guest-phone">Số điện thoại liên hệ <span aria-hidden="true">*</span></label><input id="guest-phone" name="phoneNumber" data-booking-field value="${esc(patient.phoneNumber || '')}" type="tel" autocomplete="tel" pattern="(0[0-9]{9}|[+]84[0-9]{9})" maxlength="12" title="Nhập 10 chữ số bắt đầu bằng 0 hoặc số +84 tương ứng" placeholder="Ví dụ: 0901234567" required /></div>
            <div class="field full"><label for="guest-address">Địa chỉ</label><input id="guest-address" name="address" data-booking-field value="${esc(patient.address || '')}" maxlength="255" autocomplete="street-address" /></div>
            <div class="field full"><label for="guest-reason">Lý do khám</label><textarea id="guest-reason" name="reason" data-booking-field rows="3" maxlength="255" placeholder="Mô tả ngắn nhu cầu khám của bạn">${esc(patient.reason || '')}</textarea></div></div></section></fieldset>
        <aside class="card guest-booking-summary"><span class="eyebrow">LỊCH KHÁM CỦA BẠN</span>${selectedDoctor ? `${doctorPhoto()}<h2>${esc(selectedDoctor.name)}</h2><p>${esc(selectedDoctor.specialty)}</p><p>${esc(selectedDoctor.department)}</p>` : '<h2>Chưa chọn bác sĩ</h2><p>Chọn chuyên khoa và bác sĩ ở bên cạnh để xem giờ khám.</p>'}
            <dl><div><dt>Ngày khám</dt><dd>${guestBooking.date ? bookingDate(guestBooking.date) : 'Chưa chọn'}</dd></div><div><dt>Giờ khám</dt><dd>${selectedSlot ? bookingTime(selectedSlot.startTime) + ' – ' + bookingTime(selectedSlot.endTime) : 'Chưa chọn'}</dd></div><div><dt>Phòng khám</dt><dd>${selectedSlot ? esc(selectedSlot.room) : '—'}</dd></div></dl>
            <p class="booking-hint">${icon('info')}Số thứ tự được cấp sau khi nhân viên xác minh tại quầy. Vui lòng lưu mã lịch hẹn sau khi đặt thành công.</p>
            <label class="booking-confirm-check"><input type="checkbox" required ${guestBooking.submitting ? 'checked' : ''} />Tôi đã kiểm tra thông tin người khám và lịch khám.</label>
            ${guestBooking.error ? `<p class="booking-error" role="alert" tabindex="-1">${esc(guestBooking.error)}</p>${button('Tải lại giờ khám', 'retry-guest-slots', 'refresh', 'btn outline full')}` : ''}
            <button class="btn primary full" type="submit" ${!selectedSlot || guestBooking.submitting ? 'disabled' : ''}>${icon('calendar')}${guestBooking.submitting ? 'Đang xác nhận…' : 'Xác nhận đặt lịch'}</button><small>Chỉ nhận mã lịch khi bệnh viện xác nhận lưu thành công.</small>
        </aside></form>`;
}

function guestSlotsMarkup() {
    if (!guestBooking.doctorId) return '<p class="slot-empty">Chọn bác sĩ để xem khung giờ.</p>';
    if (guestBooking.status === 'loading')
        return '<p class="slot-empty" role="status">Đang tải khung giờ…</p>';
    if (guestBooking.status === 'error')
        return `<p class="slot-empty" role="alert">${esc(guestBooking.error)}</p>${button('Tải lại giờ khám', 'retry-guest-slots', 'refresh', 'btn outline')}`;
    if (!guestBooking.slots.length)
        return '<p class="slot-empty">Chưa có khung giờ còn chỗ trong ngày này. Bạn hãy chọn ngày khác hoặc bác sĩ khác.</p>';
    return `<div class="guest-slot-grid">${guestBooking.slots.map((slot, index) => `<label class="guest-slot ${guestBooking.selected === slotKey(slot) ? 'selected' : ''}"><input id="guest-slot-${index}" type="radio" name="booking-slot" data-booking-choice="slot" value="${esc(slotKey(slot))}" ${guestBooking.selected === slotKey(slot) ? 'checked' : ''} required /><strong>${bookingTime(slot.startTime)}</strong><small>Còn ${esc(slot.remaining)} chỗ</small></label>`).join('')}</div>`;
}

function guestBookingReceipt() {
    const receipt = guestBooking.receipt;
    return `<section class="card booking-receipt"><span class="receipt-check">${icon('check')}</span><span class="eyebrow">LỊCH KHÁM ĐÃ ĐƯỢC GHI NHẬN</span><h1 tabindex="-1">Đặt lịch thành công</h1><p>Lưu lại mã dưới đây và cung cấp tại quầy tiếp nhận khi đến khám.</p><strong class="receipt-code">${esc(receipt.appointmentCode)}</strong><dl>${[
        ['Người khám', receipt.patientName],
        ['Bác sĩ', receipt.doctorName],
        ['Chuyên khoa', receipt.department],
        ['Ngày khám', bookingDate(receipt.startTime)],
        ['Giờ khám', bookingTime(receipt.startTime) + ' – ' + bookingTime(receipt.endTime)],
        ['Phòng khám', receipt.room],
    ]
        .map(([title, value]) => `<div><dt>${title}</dt><dd>${esc(value)}</dd></div>`)
        .join(
            '',
        )}</dl><p class="booking-hint">Mang theo giấy tờ của người khám, thẻ BHYT và kết quả khám trước đây nếu có. Nhân viên sẽ kiểm tra thông tin và cấp số tại quầy.</p><div class="receipt-actions">${link('Về trang chủ', 'public/home', 'arrow', 'btn outline')}${button('Đặt lịch khác', 'new-guest-booking', 'calendar', 'btn primary')}</div></section>`;
}
