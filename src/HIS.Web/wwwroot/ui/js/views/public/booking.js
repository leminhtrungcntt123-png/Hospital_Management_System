let guestBooking;
function resetGuestBooking() {
    const presetDoctorId = config.selectedDoctorId || '';
    guestBooking = {
        doctorId: presetDoctorId,
        department: '',
        date: '',
        availableDates: [],
        datesStatus: 'idle',
        datesError: '',
        showCustomDate: false,
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
        bookingMethod: presetDoctorId ? 'doctor' : 'dept', // 'dept' or 'doctor'
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
    if (catalog.status !== 'ready') return;
    if (guestBooking.doctorId && !guestBooking.department) {
        const doc = catalog.doctors.find((d) => String(d.id) === guestBooking.doctorId);
        if (doc?.department) {
            guestBooking.department = doc.department;
            render();
            return;
        }
    }
    if (guestBooking.doctorId && guestBooking.datesStatus === 'idle') {
        loadGuestAvailableDates();
    }
    if (guestBooking.doctorId && guestBooking.date && guestBooking.status === 'idle') {
        loadGuestSlots();
    }
}

async function loadGuestAvailableDates() {
    const key = guestBooking.doctorId;
    if (!key) return;
    guestBooking.datesStatus = 'loading';
    guestBooking.datesError = '';
    guestBooking.availableDates = [];
    render();
    try {
        const query = new URLSearchParams({
            doctorId: guestBooking.doctorId,
            days: '7',
        });
        const response = await fetch(
            (config.bookingAvailableDatesUrl || '/Booking/AvailableDates') + '?' + query,
            {
                credentials: 'same-origin',
                cache: 'no-store',
            },
        );
        const body = await response.json();
        if (!response.ok || !Array.isArray(body.dates)) {
            throw new Error(body.message || 'Chưa tải được ngày khám khả dụng. Vui lòng thử lại.');
        }
        if (key !== guestBooking.doctorId) return;
        guestBooking.availableDates = body.dates;
        guestBooking.datesStatus = 'ready';
        if (body.dates.length === 0) {
            guestBooking.showCustomDate = true;
        }
    } catch (error) {
        if (key !== guestBooking.doctorId) return;
        guestBooking.datesStatus = 'error';
        guestBooking.datesError = error.message;
    }
    render();
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

function switchBookingMethod(method) {
    if (guestBooking.submitting || guestBooking.bookingMethod === method) return;
    guestBooking.bookingMethod = method;
    guestBooking.department = '';
    guestBooking.doctorId = '';
    guestBooking.date = '';
    guestBooking.availableDates = [];
    guestBooking.datesStatus = 'idle';
    guestBooking.datesError = '';
    guestBooking.showCustomDate = false;
    guestBooking.slots = [];
    guestBooking.selected = '';
    guestBooking.status = 'idle';
    guestBooking.error = '';
    render();
}

function selectBookingDate(isoDate) {
    if (guestBooking.submitting || !isoDate) return;
    guestBooking.showCustomDate = false;
    changeGuestChoice('date', isoDate);
}

function openCustomDatePicker() {
    if (guestBooking.submitting) return;
    guestBooking.showCustomDate = true;
    render();
    setTimeout(() => {
        const input = document.getElementById('guest-custom-date');
        if (input) {
            input.focus();
            if (typeof input.showPicker === 'function') {
                try {
                    input.showPicker();
                } catch {
                    // Trình duyệt chặn showPicker ngoài cử chỉ trực tiếp
                }
            }
        }
    }, 30);
}

function formatBookingDayCard(isoDate) {
    const parts = String(isoDate).split('-').map(Number);
    if (parts.length !== 3 || parts.some(isNaN)) {
        return { weekday: '', dayMonth: isoDate };
    }
    const d = new Date(parts[0], parts[1] - 1, parts[2]);
    const weekdays = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7'];
    const dd = String(parts[2]).padStart(2, '0');
    const mm = String(parts[1]).padStart(2, '0');
    return { weekday: weekdays[d.getDay()], dayMonth: `${dd}/${mm}` };
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
        guestBooking.date = '';
        guestBooking.availableDates = [];
        guestBooking.datesStatus = 'idle';
        guestBooking.datesError = '';
        guestBooking.showCustomDate = false;
    } else if (field === 'doctor') {
        guestBooking.doctorId = value;
        if (guestBooking.bookingMethod === 'doctor') {
            const doc = catalog.doctors.find((d) => String(d.id) === value);
            guestBooking.department = doc?.department || '';
        }
        guestBooking.date = '';
        guestBooking.availableDates = [];
        guestBooking.datesStatus = 'idle';
        guestBooking.datesError = '';
        guestBooking.showCustomDate = false;
    } else if (field === 'date') {
        guestBooking.date = value;
        if (value && guestBooking.availableDates.includes(value)) {
            guestBooking.showCustomDate = false;
        }
    }
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
    if (!value) return '';
    const parts = String(value).split('T')[0].split('-');
    if (parts.length === 3) {
        return `${parts[2]}/${parts[1]}/${parts[0]}`;
    }
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
    if (!config.authenticated && (!p.email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(p.email.trim()))) {
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

    if (config.authenticated) {
        guestBooking.otpSending = true;
        guestBooking.error = '';
        render();
        await submitGuestBooking(null);
        guestBooking.otpSending = false;
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
        if (/hết chỗ|trôi qua|không còn khả dụng|giữ chỗ/i.test(err.message)) {
            await loadGuestSlots();
        }
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
        if (typeof patientAppointmentsState !== 'undefined') {
            patientAppointmentsState.status = 'idle';
        }
        render();
        document.querySelector('.booking-receipt h1, .booking-error')?.focus();
        return true;
    } catch (error) {
        guestBooking.submitting = false;
        if (/hết chỗ|trôi qua|không còn khả dụng|thay đổi/i.test(error.message)) {
            await loadGuestSlots();
        }
        guestBooking.error = error.message;
        render();
        return false;
    }
}

function guestDatesMarkup() {
    if (!guestBooking.doctorId) return '';

    if (guestBooking.datesStatus === 'loading') {
        return '<p class="slot-empty" role="status">Đang tải các ngày khám khả dụng trong 7 ngày gần nhất…</p>';
    }
    if (guestBooking.datesStatus === 'error') {
        return `<p class="slot-empty" role="alert">${esc(guestBooking.datesError || 'Chưa tải được danh sách ngày khám.')}</p><button type="button" class="btn outline mt-2" onclick="loadGuestAvailableDates()">Tải lại ngày khám</button>`;
    }

    const dates = guestBooking.availableDates || [];
    const isCustomSelected = Boolean(guestBooking.date && !dates.includes(guestBooking.date));
    const customLabel = isCustomSelected ? formatBookingDayCard(guestBooking.date) : null;
    const calendarSvg = `<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>`;

    return `
        ${
            dates.length === 0
                ? '<p class="slot-empty mb-2">Bác sĩ hiện chưa có lịch trống trong 7 ngày gần nhất. Vui lòng chọn <strong>Ngày khác</strong> để kiểm tra thêm lịch trống.</p>'
                : ''
        }
        <div class="booking-date-grid" role="group" aria-label="Chọn ngày khám">
            ${dates
                .map((iso) => {
                    const { weekday, dayMonth } = formatBookingDayCard(iso);
                    const active = guestBooking.date === iso;
                    return `<button type="button" class="booking-date-card ${active ? 'active' : ''}" onclick="selectBookingDate('${esc(iso)}')" aria-pressed="${active ? 'true' : 'false'}">
                        <span class="date-weekday">${esc(weekday)}</span>
                        <strong class="date-daymonth">${esc(dayMonth)}</strong>
                    </button>`;
                })
                .join('')}
            <button type="button" class="booking-date-card booking-date-card-other ${isCustomSelected || guestBooking.showCustomDate ? 'active' : ''}" onclick="openCustomDatePicker()">
                <span class="date-weekday">${isCustomSelected ? esc(customLabel.weekday) : 'Ngày khác'}</span>
                ${
                    isCustomSelected
                        ? `<strong class="date-daymonth">${esc(customLabel.dayMonth)}</strong>`
                        : `<span class="date-other-icon">${calendarSvg}</span>`
                }
            </button>
        </div>
        ${
            guestBooking.showCustomDate
                ? `<div class="custom-date-picker-row">
                    <label for="guest-custom-date">Chọn ngày khám khác</label>
                    <input id="guest-custom-date" data-booking-choice="date" type="date" value="${esc(guestBooking.date)}" min="${esc(config.bookingMinDate)}" max="${esc(config.bookingMaxDate)}" />
                </div>`
                : ''
        }
    `;
}

function guestSlotsMarkup() {
    if (!guestBooking.doctorId || !guestBooking.date) return '';
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
            <p>${config.authenticated ? 'Vui lòng kiểm tra và xác nhận thông tin đặt lịch khám của bạn.' : 'Vui lòng điền thông tin để chúng tôi có thể hỗ trợ bạn tốt nhất (Không cần đăng nhập).'}</p>
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
                    <label class="form-label-title">Phương thức khám <span class="req-star">*</span></label>
                    <div class="method-cards-grid">
                        <label class="method-card ${guestBooking.bookingMethod === 'dept' ? 'active' : ''}">
                            <input type="radio" name="bookingMethod" value="dept" ${guestBooking.bookingMethod === 'dept' ? 'checked' : ''} onchange="switchBookingMethod('dept')" />
                            <div class="method-card-content">
                                <strong>Tôi chưa rõ nên khám bác sĩ nào</strong>
                                <span>Chọn chuyên khoa, hệ thống gợi ý danh sách bác sĩ phù hợp</span>
                            </div>
                        </label>
                        <label class="method-card ${guestBooking.bookingMethod === 'doctor' ? 'active' : ''}">
                            <input type="radio" name="bookingMethod" value="doctor" ${guestBooking.bookingMethod === 'doctor' ? 'checked' : ''} onchange="switchBookingMethod('doctor')" />
                            <div class="method-card-content">
                                <strong>Tôi đã biết bác sĩ muốn khám</strong>
                                <span>Tìm nhanh và đặt lịch trực tiếp với bác sĩ bạn đã chọn</span>
                            </div>
                        </label>
                    </div>

                    <!-- Chuyên khoa & Bác sĩ -->
                    ${
                        guestBooking.bookingMethod === 'dept'
                            ? `
                    <div class="form-row-2">
                        <div class="field">
                            <label for="guest-department">Chuyên khoa <span class="req-star">*</span></label>
                            <select id="guest-department" data-booking-choice="department" required>
                                <option value="">Chọn chuyên khoa</option>
                                ${catalog.departments.map((dept) => `<option value="${esc(dept.name)}" ${guestBooking.department === dept.name ? 'selected' : ''}>${esc(dept.name)}</option>`).join('')}
                            </select>
                        </div>
                        <div class="field">
                            <label for="guest-doctor">Bác sĩ</label>
                            <select id="guest-doctor" data-booking-choice="doctor" class="${!guestBooking.department ? 'booking-control-muted' : ''}" ${!guestBooking.department ? 'disabled' : ''} required>
                                ${
                                    !guestBooking.department
                                        ? '<option value="">Chọn chuyên khoa trước</option>'
                                        : `<option value="">Chọn bác sĩ</option>${doctors.map((doctor) => `<option value="${esc(doctor.id)}" ${guestBooking.doctorId === String(doctor.id) ? 'selected' : ''}>${esc(doctor.name)}</option>`).join('')}`
                                }
                            </select>
                        </div>
                    </div>`
                            : `
                    <div class="form-row-2">
                        <div class="field">
                            <label for="guest-doctor">Bác sĩ <span class="req-star">*</span></label>
                            <select id="guest-doctor" data-booking-choice="doctor" required>
                                <option value="">Chọn bác sĩ</option>
                                ${catalog.doctors.map((doctor) => `<option value="${esc(doctor.id)}" ${guestBooking.doctorId === String(doctor.id) ? 'selected' : ''}>${esc(doctor.name)}</option>`).join('')}
                            </select>
                        </div>
                        <div class="field">
                            <label for="guest-department-readonly">Chuyên khoa</label>
                            <input id="guest-department-readonly" type="text" class="booking-control-muted" value="${esc(selectedDoctor?.department || guestBooking.department || '')}" placeholder="Chuyên khoa" disabled readonly />
                        </div>
                    </div>`
                    }

                    <!-- Ngày khám (hiện sau khi đã chọn bác sĩ) -->
                    ${
                        guestBooking.doctorId
                            ? `
                    <div class="booking-step-block mt-3">
                        <label class="form-label-title">Ngày khám <span class="req-star">*</span></label>
                        <p class="booking-helper-text">Dưới đây là các ngày khám khả dụng trong 7 ngày gần nhất. Nếu không có lịch phù hợp với bạn, vui lòng chọn Ngày khác để kiểm tra thêm lịch trống.</p>
                        ${guestDatesMarkup()}
                    </div>`
                            : ''
                    }

                    <!-- Giờ khám (hiện sau khi đã chọn ngày khám) -->
                    ${
                        guestBooking.doctorId && guestBooking.date
                            ? `
                    <fieldset class="guest-slot-picker mt-3">
                        <legend>Giờ khám <span class="req-star">*</span></legend>
                        <p class="booking-helper-text">Tổng đài viên MediCare sẽ liên hệ xác nhận thông tin thời gian dựa theo đăng ký và điều chỉnh nếu cần thiết.</p>
                        ${guestSlotsMarkup()}
                    </fieldset>`
                            : ''
                    }

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
                            <label for="guest-email">${config.authenticated ? 'Địa chỉ Email (để nhận phiếu hẹn)' : 'Địa chỉ Email (để nhận mã OTP và phiếu hẹn) *'}</label>
                            <input id="guest-email" name="email" data-booking-field type="email" value="${esc(p.email || '')}" placeholder="name@example.com" ${config.authenticated ? '' : 'required'} />
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
                        <div><dt>Giới tính</dt><dd>${Number(p.gender) === 1 ? 'Nữ' : Number(p.gender) === 2 ? 'Khác' : 'Nam'}</dd></div>
                        <div><dt>Điện thoại</dt><dd>${esc(p.phoneNumber)}</dd></div>
                        <div><dt>Email</dt><dd>${esc(p.email || 'Chưa cung cấp')}</dd></div>
                        ${p.medicalCode ? `<div><dt>Mã y tế</dt><dd>${esc(p.medicalCode)}</dd></div>` : ''}
                    </dl>
                </div>
            </div>

            <!-- Checkbox cam kết chính sách -->
            <label class="confirm-policy-check mt-4">
                <input type="checkbox" id="agreePolicyCheck" ${guestBooking.agreePolicy ? 'checked' : ''} onchange="guestBooking.agreePolicy = this.checked; render();" />
                <span>Tôi đồng ý với <a href="${esc(routeUrl('public/guide'))}" data-route="public/guide">Chính sách bảo vệ dữ liệu cá nhân</a> của MediCare và chấp nhận để MediCare xử lý dữ liệu cá nhân của tôi theo quy định của pháp luật để bảo vệ dữ liệu cá nhân.</span>
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
