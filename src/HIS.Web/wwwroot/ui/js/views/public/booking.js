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
        patient: {
            fullName: '',
            dateOfBirth: '',
            gender: '0',
            phoneNumber: '',
            email: config.userEmail || '',
            address: '',
            medicalCode: '',
            reason: '',
            patientId: null,
        },
        step: 'form', // 'form' or 'confirm'
        bookingMethod: 'dept', // 'dept' or 'doctor'
        bookFor: 'self',
        agreePolicy: false,
        otpCode: '',
        otpSending: false,
        otpSecondsLeft: 60,
        otpTimerId: null,
        otpModalOpen: false,
        profilesLoaded: false,
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

function slotKey(slot) {
    return slot.scheduleId + '|' + slot.startTime;
}
function bookingTime(value) {
    return new Date(value).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}
function bookingDate(value) {
    return new Date(value).toLocaleDateString('vi-VN');
}

async function ensureProfilesForBooking() {
    if (guestBooking.profilesLoaded || !config.authenticated) return;
    guestBooking.profilesLoaded = true;
    try {
        const response = await fetch(config.profilesUrl || '/Patient/FamilyProfiles/Profiles', {
            credentials: 'same-origin',
            cache: 'no-store',
            headers: { Accept: 'application/json' },
        });
        if (!response.ok) return;
        const data = await response.json();
        if (data.authenticated) {
            patientProfilesState.selfProfile = data.selfProfile;
            patientProfilesState.relatives = data.relatives || [];
            patientProfilesState.accountEmail = data.accountEmail;
            if (data.selfProfile && !guestBooking.patient.fullName) {
                const p = data.selfProfile;
                guestBooking.patient.fullName = p.fullName || config.displayName || '';
                guestBooking.patient.dateOfBirth = p.dateOfBirth || '';
                guestBooking.patient.gender = String(p.gender ?? '0');
                guestBooking.patient.phoneNumber = p.phoneNumber || '';
                guestBooking.patient.email = p.email || data.accountEmail || config.userEmail || '';
                guestBooking.patient.address = p.address || '';
                guestBooking.patient.medicalCode = p.medicalCode || '';
                guestBooking.patient.patientId = p.patientId || null;
                render();
            }
        }
    } catch {
        // Silently continue for guests
    }
}

function applyProfileToBooking(choice) {
    guestBooking.bookFor = choice;
    if (choice === 'self') {
        const p = patientProfilesState.selfProfile || {};
        guestBooking.patient.fullName = p.fullName || config.displayName || '';
        guestBooking.patient.dateOfBirth = p.dateOfBirth || '';
        guestBooking.patient.gender = String(p.gender ?? '0');
        guestBooking.patient.phoneNumber = p.phoneNumber || '';
        guestBooking.patient.email = p.email || patientProfilesState.accountEmail || config.userEmail || '';
        guestBooking.patient.address = p.address || '';
        guestBooking.patient.medicalCode = p.medicalCode || '';
        guestBooking.patient.patientId = p.patientId || null;
    } else if (choice === 'other') {
        guestBooking.patient.fullName = '';
        guestBooking.patient.dateOfBirth = '';
        guestBooking.patient.gender = '0';
        guestBooking.patient.phoneNumber = '';
        guestBooking.patient.email = patientProfilesState.accountEmail || config.userEmail || '';
        guestBooking.patient.address = '';
        guestBooking.patient.medicalCode = '';
        guestBooking.patient.patientId = null;
    } else {
        const rel = (patientProfilesState.relatives || []).find((r) => String(r.patientId) === choice);
        if (rel) {
            guestBooking.patient.fullName = rel.fullName || '';
            guestBooking.patient.dateOfBirth = rel.dateOfBirth || '';
            guestBooking.patient.gender = String(rel.gender ?? '0');
            guestBooking.patient.phoneNumber = rel.phoneNumber || '';
            guestBooking.patient.email = rel.email || patientProfilesState.accountEmail || config.userEmail || '';
            guestBooking.patient.address = rel.address || '';
            guestBooking.patient.medicalCode = rel.medicalCode || '';
            guestBooking.patient.patientId = rel.patientId || null;
        }
    }
    render();
}

function proceedToConfirmation() {
    guestBooking.error = '';

    // Đồng bộ trực tiếp từ các trường input DOM nếu đang ở trang form
    try {
        const nameEl = document.getElementById('guest-name');
        const birthEl = document.getElementById('guest-birth');
        const phoneEl = document.getElementById('guest-phone');
        const emailEl = document.getElementById('guest-email');
        const codeEl = document.getElementById('guest-code');
        const reasonEl = document.getElementById('guest-reason');
        const genderChecked = document.querySelector('input[name="gender"]:checked');
        if (nameEl) guestBooking.patient.fullName = nameEl.value.trim();
        if (birthEl) guestBooking.patient.dateOfBirth = birthEl.value;
        if (phoneEl) guestBooking.patient.phoneNumber = phoneEl.value.trim();
        if (emailEl) guestBooking.patient.email = emailEl.value.trim();
        if (codeEl) guestBooking.patient.medicalCode = codeEl.value.trim();
        if (reasonEl) guestBooking.patient.reason = reasonEl.value.trim();
        if (genderChecked) guestBooking.patient.gender = genderChecked.value;
    } catch {
        // Môi trường test hoặc element không tồn tại
    }

    const p = guestBooking.patient;
    if (!guestBooking.doctorId) {
        guestBooking.error = 'Vui lòng chọn bác sĩ khám.';
        render();
        return;
    }
    if (!guestBooking.date) {
        guestBooking.error = 'Vui lòng chọn ngày khám.';
        render();
        return;
    }
    if (!guestBooking.selected) {
        guestBooking.error = 'Vui lòng chọn một khung giờ khám còn trống.';
        render();
        return;
    }
    const selectedSlotObj = guestBooking.slots.find((item) => slotKey(item) === guestBooking.selected);
    if (selectedSlotObj) {
        const slotMs = new Date(selectedSlotObj.startTime).getTime();
        if (!isNaN(slotMs) && slotMs <= Date.now()) {
            guestBooking.error = 'Khung giờ khám bạn chọn đã trôi qua thời gian hiện tại. Vui lòng chọn một khung giờ khác.';
            guestBooking.selected = '';
            render();
            return;
        }
    }
    if (!p.fullName || p.fullName.trim().length < 2) {
        guestBooking.error = 'Vui lòng nhập họ và tên người khám (tối thiểu 2 ký tự).';
        render();
        return;
    }
    if (!p.dateOfBirth) {
        guestBooking.error = 'Vui lòng chọn ngày sinh người khám.';
        render();
        return;
    }
    if (!p.phoneNumber || !/^(0[0-9]{9}|\+84[0-9]{9})$/.test(p.phoneNumber.trim())) {
        guestBooking.error = 'Vui lòng nhập số điện thoại liên hệ hợp lệ (10 chữ số).';
        render();
        return;
    }
    if (!p.email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(p.email.trim())) {
        guestBooking.error = 'Vui lòng nhập địa chỉ Email chính xác để nhận mã OTP và phiếu hẹn.';
        render();
        return;
    }
    guestBooking.step = 'confirm';
    render();
    window.scrollTo(0, 0);
}

function backToBookingForm() {
    guestBooking.step = 'form';
    guestBooking.error = '';
    render();
    window.scrollTo(0, 0);
}

async function requestBookingOtp() {
    const p = guestBooking.patient;
    const slot = guestBooking.slots.find((item) => slotKey(item) === guestBooking.selected);
    if (!slot) return;

    const slotMs = new Date(slot.startTime).getTime();
    if (!isNaN(slotMs) && slotMs <= Date.now()) {
        guestBooking.error = 'Khung giờ khám bạn chọn đã trôi qua thời gian hiện tại. Vui lòng quay lại và chọn một khung giờ khác.';
        render();
        return;
    }

    if (!guestBooking.agreePolicy) {
        guestBooking.error = 'Vui lòng tích đồng ý với Chính sách bảo vệ dữ liệu cá nhân của MediCare.';
        render();
        return;
    }

    if (!p.email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(p.email.trim())) {
        guestBooking.error = 'Vui lòng quay lại Bước 1 và nhập địa chỉ Email hợp lệ để nhận mã xác thực OTP.';
        render();
        return;
    }

    guestBooking.otpSending = true;
    guestBooking.error = '';
    render();

    try {
        const csrf = document.querySelector('[name="__RequestVerificationToken"]')?.value || '';
        const response = await fetch(config.bookingSendOtpUrl || '/Booking/SendOtp', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: csrf,
            },
            body: JSON.stringify({
                email: p.email.trim(),
                fullName: p.fullName.trim(),
                scheduleId: slot.scheduleId,
                startTime: slot.startTime,
            }),
        });

        const body = await response.json();
        if (!response.ok) throw new Error(body.message || 'Không thể gửi mã OTP.');

        guestBooking.otpModalOpen = true;
        guestBooking.otpSecondsLeft = 60;
        guestBooking.otpCode = '';
        delete guestBooking.devOtp;
        if (guestBooking.otpTimerId) clearInterval(guestBooking.otpTimerId);
        guestBooking.otpTimerId = setInterval(() => {
            guestBooking.otpSecondsLeft--;
            if (guestBooking.otpSecondsLeft <= 0) {
                clearInterval(guestBooking.otpTimerId);
                guestBooking.otpTimerId = null;
            }
            const timerEl = document.getElementById('otpTimerText');
            if (timerEl) timerEl.textContent = guestBooking.otpSecondsLeft > 0 ? `(${guestBooking.otpSecondsLeft}s)` : '';
        }, 1000);

        openOtpModal();
    } catch (err) {
        guestBooking.error = err.message;
    } finally {
        guestBooking.otpSending = false;
        render();
    }
}

function openOtpModal() {
    const email = guestBooking.patient.email || '';
    openModal(
        'Xác thực mã OTP',
        `
        <div class="otp-modal-content">
            <p class="mb-1">Hệ thống đã gửi mã OTP xác nhận đến hòm thư:</p>
            <p class="fw-bold text-primary mb-1"><strong>${esc(email)}</strong></p>
            <p class="text-muted small mb-3" style="font-size:12px; color:#627d92;"><em>(Vui lòng mở hộp thư Gmail của bạn, kiểm tra cả thư mục Thư rác / Spam nếu không thấy)</em></p>
            <div class="field mb-3">
                <label for="inputOtpCode">Nhập mã OTP 6 chữ số *</label>
                <input id="inputOtpCode" type="text" maxlength="6" inputmode="numeric" pattern="[0-9]*" class="otp-input-control" value="" placeholder="••••••" autocomplete="one-time-code" style="font-size:24px; letter-spacing:6px; text-align:center;" />
            </div>
            <div id="otpModalError" class="account-alert error" style="display:none; margin-bottom:12px;"></div>
            <p class="text-muted small">
                Chưa nhận được mã? <button type="button" class="btn-link" onclick="resendOtp()">Gửi lại mã <span id="otpTimerText">(60s)</span></button>
            </p>
        </div>
        `,
        `
        <button class="btn outline" type="button" onclick="closeModal()">Đóng</button>
        <button id="btnConfirmOtp" class="btn primary" type="button" onclick="confirmOtpAndBook()">Xác nhận đặt lịch</button>
        `,
    );
    setTimeout(() => {
        document.getElementById('inputOtpCode')?.focus();
    }, 100);
}

async function resendOtp() {
    if (guestBooking.otpSecondsLeft > 0) return;
    closeModal();
    requestBookingOtp();
}

async function confirmOtpAndBook() {
    const input = document.getElementById('inputOtpCode');
    const errEl = document.getElementById('otpModalError');
    const btn = document.getElementById('btnConfirmOtp');
    if (!input || !input.value.trim() || input.value.trim().length !== 6 || !/^\d{6}$/.test(input.value.trim())) {
        if (errEl) {
            errEl.textContent = 'Vui lòng nhập đủ 6 chữ số OTP từ email của bạn.';
            errEl.style.display = 'block';
        }
        input?.focus();
        return;
    }
    const code = input.value.trim();
    if (errEl) errEl.style.display = 'none';
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Đang xác thực…';
    }
    const success = await submitGuestBooking(code);
    if (success) {
        closeModal();
    } else {
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Xác nhận đặt lịch';
        }
        if (errEl) {
            errEl.textContent = guestBooking.error || 'Mã OTP không chính xác hoặc đã hết hạn.';
            errEl.style.display = 'block';
        }
    }
}

async function submitGuestBooking(otpOverride) {
    if (guestBooking.submitting) return false;
    const slot = guestBooking.slots.find((item) => slotKey(item) === guestBooking.selected);
    if (!slot) return false;
    guestBooking.submitting = true;
    guestBooking.error = '';
    try {
        const response = await fetch(config.bookingConfirmUrl, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: document.querySelector(
                    '[name="__RequestVerificationToken"]',
                )?.value || '',
            },
            body: JSON.stringify({
                ...guestBooking.patient,
                gender:
                    guestBooking.patient.gender === '' || guestBooking.patient.gender == null
                        ? null
                        : Number(guestBooking.patient.gender),
                scheduleId: slot.scheduleId,
                startTime: slot.startTime,
                medicalCode: guestBooking.patient.medicalCode || null,
                patientId: guestBooking.patient.patientId || null,
                otpCode: otpOverride || guestBooking.otpCode || null,
            }),
        });
        const body = await response.json();
        if (!response.ok || !body.receipt?.appointmentCode)
            throw new Error(body.message || 'Chưa thể xác nhận lịch. Vui lòng thử lại sau.');
        guestBooking.receipt = body.receipt;
        guestBooking.submitting = false;
        render();
        document.querySelector('.booking-receipt h1, .booking-error')?.focus();
        return true;
    } catch (error) {
        guestBooking.error = error.message;
        guestBooking.submitting = false;
        render();
        return false;
    }
}

function guestSlotsMarkup() {
    if (!guestBooking.doctorId) return '<p class="slot-empty">Chọn bác sĩ để xem khung giờ.</p>';
    if (guestBooking.status === 'loading')
        return '<p class="slot-empty" role="status">Đang tải khung giờ…</p>';
    if (guestBooking.status === 'error')
        return `<p class="slot-empty" role="alert">${esc(guestBooking.error)}</p>${button('Tải lại giờ khám', 'retry-guest-slots', 'refresh', 'btn outline')}`;

    // Tự động loại bỏ các khung giờ đã trôi qua thời gian hiện tại
    const now = Date.now();
    const availableSlots = guestBooking.slots.filter((slot) => {
        const t = new Date(slot.startTime).getTime();
        return isNaN(t) || t > now;
    });

    if (!availableSlots.length)
        return '<p class="slot-empty">Chưa có khung giờ còn chỗ trong ngày này (hoặc các khung giờ trong ngày đã kết thúc). Bạn hãy chọn ngày khác hoặc bác sĩ khác.</p>';
    return `<div class="guest-slot-grid">${availableSlots.map((slot, index) => `<label class="guest-slot ${guestBooking.selected === slotKey(slot) ? 'selected' : ''}"><input id="guest-slot-${index}" type="radio" name="booking-slot" data-booking-choice="slot" value="${esc(slotKey(slot))}" ${guestBooking.selected === slotKey(slot) ? 'checked' : ''} required /><strong>${bookingTime(slot.startTime)}</strong><small>Còn ${esc(slot.remaining)} chỗ</small></label>`).join('')}</div>`;
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

function guestBookingPage() {
    ensureProfilesForBooking();
    if (guestBooking.receipt) return guestBookingReceipt();

    if (guestBooking.step === 'confirm') {
        return renderBookingConfirmationStep();
    }

    const selectedDoctor = catalog.doctors.find(
        (doctor) => String(doctor.id) === guestBooking.doctorId,
    );
    const doctors = catalog.doctors.filter(
        (doctor) => !guestBooking.department || doctor.department === guestBooking.department,
    );
    const selectedSlot = guestBooking.slots.find((slot) => slotKey(slot) === guestBooking.selected);
    const p = guestBooking.patient;
    const relatives = patientProfilesState.relatives || [];

    return `
    <div class="medicare-booking-wrapper">
        <div class="medicare-booking-header">
            <h1>Đặt lịch khám</h1>
            <p>Vui lòng điền thông tin để chúng tôi có thể hỗ trợ bạn tốt nhất (Không cần đăng nhập).</p>
        </div>

        <div class="medicare-booking-body">
            ${catalog.status !== 'ready' ? catalogStatus() : ''}
            ${guestBooking.error ? `<div class="booking-error" role="alert">${esc(guestBooking.error)}</div>` : ''}

            <form id="medicareBookingForm" data-guest-booking>
                <!-- PHẦN 1: THÔNG TIN ĐẶT HẸN -->
                <div class="booking-section-box">
                    <div class="section-title">Thông tin đặt hẹn</div>
                    <div class="section-subtitle">Chọn chuyên khoa, bác sĩ và thời gian phù hợp với bạn.</div>

                    <!-- Phương thức khám -->
                    <label class="form-label-title">Phương thức khám *</label>
                    <div class="method-cards-grid">
                        <label class="method-card ${guestBooking.bookingMethod === 'dept' ? 'active' : ''}">
                            <input type="radio" name="bookingMethod" value="dept" ${guestBooking.bookingMethod === 'dept' ? 'checked' : ''} onchange="guestBooking.bookingMethod='dept'; render();" />
                            <div class="method-card-content">
                                <strong>Tôi chưa rõ nên khám bác sĩ nào</strong>
                                <span>Chọn chuyên khoa, hệ thống gợi ý danh sách bác sĩ phù hợp</span>
                            </div>
                        </label>
                        <label class="method-card ${guestBooking.bookingMethod === 'doctor' ? 'active' : ''}">
                            <input type="radio" name="bookingMethod" value="doctor" ${guestBooking.bookingMethod === 'doctor' ? 'checked' : ''} onchange="guestBooking.bookingMethod='doctor'; render();" />
                            <div class="method-card-content">
                                <strong>Tôi đã biết bác sĩ muốn khám</strong>
                                <span>Tìm nhanh và đặt lịch trực tiếp với bác sĩ bạn đã chọn</span>
                            </div>
                        </label>
                    </div>

                    <!-- Chuyên khoa & Bác sĩ -->
                    <div class="form-row-2">
                        <div class="field">
                            <label for="guest-department">Chuyên khoa</label>
                            <select id="guest-department" data-booking-choice="department">
                                <option value="">Tất cả chuyên khoa</option>
                                ${catalog.departments.map((dept) => `<option value="${esc(dept.name)}" ${guestBooking.department === dept.name ? 'selected' : ''}>${esc(dept.name)}</option>`).join('')}
                            </select>
                        </div>
                        <div class="field">
                            <label for="guest-doctor">Bác sĩ *</label>
                            <select id="guest-doctor" data-booking-choice="doctor" required>
                                <option value="">Chọn bác sĩ</option>
                                ${doctors.map((doctor) => `<option value="${esc(doctor.id)}" ${guestBooking.doctorId === String(doctor.id) ? 'selected' : ''}>${esc(doctor.name)} (${esc(doctor.department || '')})</option>`).join('')}
                            </select>
                        </div>
                    </div>

                    ${
                        selectedDoctor
                            ? `<div class="selected-doctor-summary mt-3 p-3 bg-light rounded d-flex align-items-center gap-3">
                                <div>
                                    <h2 style="font-size:16px; margin:0 0 4px 0;">${esc(selectedDoctor.name)}</h2>
                                    <p class="text-muted small mb-0">${esc(selectedDoctor.specialty || '')} · ${esc(selectedDoctor.department || '')}</p>
                                </div>
                            </div>`
                            : ''
                    }

                    <!-- Ngày khám -->
                    <div class="field mt-3">
                        <label for="guest-date">Ngày khám *</label>
                        <input id="guest-date" data-booking-choice="date" type="date" value="${esc(guestBooking.date)}" min="${esc(config.bookingMinDate)}" max="${esc(config.bookingMaxDate)}" required />
                    </div>

                    <!-- Giờ khám (Slots) -->
                    <fieldset class="guest-slot-picker mt-3">
                        <legend>Giờ khám (Ca 30 phút) *</legend>
                        ${guestSlotsMarkup()}
                    </fieldset>

                    <!-- Phí khám dự kiến -->
                    <div class="medicare-fee-bar">
                        <span>Phí khám trong giờ hành chính dự kiến:</span>
                        <strong>300.000 VNĐ</strong>
                    </div>
                </div>

                <!-- PHẦN 2: THÔNG TIN NGƯỜI KHÁM -->
                <div class="booking-section-box">
                    <div class="section-title">Thông tin người khám</div>
                    <div class="section-subtitle">Vui lòng nhập thông tin theo mẫu bên dưới để bệnh viện xuất phiếu khám.</div>

                    ${
                        config.authenticated
                            ? `
                    <div class="field mb-3">
                        <label for="booking-for-select">Đặt lịch cho: *</label>
                        <select id="booking-for-select" onchange="applyProfileToBooking(this.value)">
                            <option value="self" ${guestBooking.bookFor === 'self' ? 'selected' : ''}>Chính tôi (${esc(config.displayName)})</option>
                            ${relatives.map((r) => `<option value="${esc(r.patientId)}" ${guestBooking.bookFor === String(r.patientId) ? 'selected' : ''}>${esc(r.fullName)} (${esc(r.relationshipLabel || 'Người thân')})</option>`).join('')}
                            <option value="other" ${guestBooking.bookFor === 'other' ? 'selected' : ''}>Khác (Nhập thông tin người khác)</option>
                        </select>
                    </div>`
                            : ''
                    }

                    <div class="form-row-2">
                        <div class="field">
                            <label for="guest-name">Họ và tên người khám *</label>
                            <input id="guest-name" name="fullName" data-booking-field value="${esc(p.fullName || '')}" minlength="2" maxlength="100" placeholder="Ví dụ: Nguyễn Văn An" required />
                        </div>
                        <div class="field">
                            <label>Giới tính *</label>
                            <div class="gender-radio-group">
                                <label><input type="radio" name="gender" value="0" ${String(p.gender) === '0' ? 'checked' : ''} onchange="guestBooking.patient.gender='0'" /> Nam</label>
                                <label><input type="radio" name="gender" value="1" ${String(p.gender) === '1' ? 'checked' : ''} onchange="guestBooking.patient.gender='1'" /> Nữ</label>
                                <label><input type="radio" name="gender" value="2" ${String(p.gender) === '2' ? 'checked' : ''} onchange="guestBooking.patient.gender='2'" /> Khác</label>
                            </div>
                        </div>
                    </div>

                    <div class="form-row-2 mt-3">
                        <div class="field">
                            <label for="guest-birth">Ngày sinh *</label>
                            <input id="guest-birth" name="dateOfBirth" data-booking-field value="${esc(p.dateOfBirth || '')}" type="date" max="${esc(config.bookingMinDate)}" required />
                        </div>
                        <div class="field">
                            <label for="guest-phone">Số điện thoại liên hệ *</label>
                            <input id="guest-phone" name="phoneNumber" data-booking-field value="${esc(p.phoneNumber || '')}" type="tel" pattern="(0[0-9]{9}|[+]84[0-9]{9})" placeholder="Ví dụ: 0912345678" required />
                        </div>
                    </div>

                    <div class="form-row-2 mt-3">
                        <div class="field">
                            <label for="guest-email">Địa chỉ Email (để nhận mã OTP và phiếu hẹn) *</label>
                            <input id="guest-email" name="email" data-booking-field type="email" value="${esc(p.email || '')}" placeholder="name@example.com" required />
                        </div>
                        <div class="field">
                            <label for="guest-code">Mã y tế (nếu có)</label>
                            <input id="guest-code" name="medicalCode" data-booking-field value="${esc(p.medicalCode || '')}" placeholder="Nhập mã y tế (không bắt buộc)" />
                        </div>
                    </div>

                    <div class="field mt-3">
                        <label for="guest-reason">Lý do khám / Triệu chứng bệnh</label>
                        <textarea id="guest-reason" name="reason" data-booking-field rows="3" maxlength="200" placeholder="Mô tả triệu chứng hoặc lý do khám...">${esc(p.reason || '')}</textarea>
                    </div>

                    <div class="form-submit-row mt-4">
                        <button class="btn primary btn-next-step" type="submit" ${!guestBooking.selected || guestBooking.submitting ? 'disabled' : ''}>
                            TIẾP THEO →
                        </button>
                    </div>
                </div>
            </form>
        </div>
    </div>`;
}

function renderBookingConfirmationStep() {
    const selectedDoctor = catalog.doctors.find(
        (doctor) => String(doctor.id) === guestBooking.doctorId,
    );
    const selectedSlot = guestBooking.slots.find((slot) => slotKey(slot) === guestBooking.selected);
    const p = guestBooking.patient;

    return `
    <div class="medicare-booking-wrapper">
        <div class="medicare-booking-header">
            <h1>Xác nhận đặt lịch</h1>
            <p>Vui lòng xác nhận thông tin để tiếp tục đặt lịch khám</p>
        </div>

        <div class="medicare-booking-body">
            ${guestBooking.error ? `<div class="booking-error" role="alert">${esc(guestBooking.error)}</div>` : ''}

            <div class="confirm-summary-grid">
                <!-- Cột trái: Thông tin lịch hẹn -->
                <div class="confirm-summary-col">
                    <div class="confirm-col-title">Thông tin lịch hẹn</div>
                    <dl class="confirm-dl">
                        <div><dt>Cơ sở khám</dt><dd>Bệnh viện Đa khoa MediCare</dd></div>
                        <div><dt>Chuyên khoa</dt><dd>${esc(selectedDoctor?.department || guestBooking.department || 'Đa khoa')}</dd></div>
                        <div><dt>Bác sĩ</dt><dd>${esc(selectedDoctor?.name || 'Chưa chọn')}</dd></div>
                        <div><dt>Ngày khám</dt><dd>${guestBooking.date ? bookingDate(guestBooking.date) : ''}</dd></div>
                        <div><dt>Giờ khám</dt><dd class="text-primary font-bold">${selectedSlot ? bookingTime(selectedSlot.startTime) + ' – ' + bookingTime(selectedSlot.endTime) : ''}</dd></div>
                        <div><dt>Phòng khám</dt><dd>${selectedSlot ? esc(selectedSlot.room) : '—'}</dd></div>
                        <div><dt>Phí khám dự kiến</dt><dd class="text-success font-bold">300,000 VNĐ</dd></div>
                        <div><dt>Ghi chú</dt><dd>${esc(p.reason || 'Không có')}</dd></div>
                    </dl>
                </div>

                <!-- Cột phải: Thông tin người khám -->
                <div class="confirm-summary-col">
                    <div class="confirm-col-title">Thông tin người khám</div>
                    <dl class="confirm-dl">
                        <div><dt>Họ và tên</dt><dd><strong>${esc(p.fullName)}</strong></dd></div>
                        <div><dt>Ngày sinh</dt><dd>${esc(p.dateOfBirth)}</dd></div>
                        <div><dt>Giới tính</dt><dd>${Number(p.gender) === 1 ? 'Nữ' : 'Nam'}</dd></div>
                        <div><dt>Điện thoại</dt><dd>${esc(p.phoneNumber)}</dd></div>
                        <div><dt>Email</dt><dd>${esc(p.email || 'Chưa cung cấp')}</dd></div>
                        ${p.medicalCode ? `<div><dt>Mã y tế</dt><dd>${esc(p.medicalCode)}</dd></div>` : ''}
                    </dl>
                </div>
            </div>

            <!-- Checkbox cam kết chính sách -->
            <label class="confirm-policy-check mt-4">
                <input type="checkbox" id="agreePolicyCheck" ${guestBooking.agreePolicy ? 'checked' : ''} onchange="guestBooking.agreePolicy = this.checked; render();" />
                <span>Tôi đồng ý với <a href="#/public/guide" target="_blank">Chính sách bảo vệ dữ liệu cá nhân</a> của MediCare và chấp nhận để MediCare xử lý dữ liệu cá nhân của tôi theo quy định của pháp luật để bảo vệ dữ liệu cá nhân.</span>
            </label>

            <!-- Buttons -->
            <div class="confirm-actions-row mt-4">
                <button class="btn outline" type="button" onclick="backToBookingForm()">
                    ← Quay lại
                </button>
                <button class="btn primary" type="button" onclick="requestBookingOtp()" ${!guestBooking.agreePolicy || guestBooking.otpSending ? 'disabled' : ''}>
                    ${guestBooking.otpSending ? 'Đang gửi mã…' : 'Xác nhận'}
                </button>
            </div>
        </div>
    </div>`;
}
