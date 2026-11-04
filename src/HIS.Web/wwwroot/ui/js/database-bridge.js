/* Production UI writes go through authenticated, anti-forgery protected server commands. */
(() => {
    if (!window.hisLive) return;
    const config = window.hisUiConfig;
    const forms = new Set([
        'family-form',
        'link-family-form',
        'contact-form',
        'walkin-form',
        'checkin-form',
        'vitals-form',
        'consult-form',
        'lab-order-form',
        'lab-result-form',
        'insurance-form',
        'payment-form',
        'exception-form',
        'schedule-form',
        'duty-form',
        'account-form',
        'catalog-form',
        'password-form',
    ]);
    const actions = new Set([
        'hold-booking',
        'confirm-booking',
        'release-hold',
        'confirm-cancel',
        'confirm-revoke',
        'call-next',
        'begin-consult',
        'save-draft',
        'confirm-exception',
        'reject-exception',
        'toggle-account',
        'read-all',
        'read-notice',
    ]);
    let busy = false;
    let draft = null;

    function formData(form) {
        const result = Object.fromEntries(new FormData(form).entries());
        for (const field of form.querySelectorAll('select:disabled'))
            if (field.name) result[field.name] = field.value;
        return result;
    }
    async function refresh() {
        const response = await fetch(
            config.apiBase + 'snapshot?screen=' + encodeURIComponent(ui.route),
            {
                credentials: 'same-origin',
                cache: 'no-store',
                headers: { Accept: 'application/json' },
            }
        );
        if (!response.ok) throw new Error('Không tải được dữ liệu mới. Hãy tải lại trang.');
        db = await response.json();
        window.hisSnapshotAt = Date.now();
        session = db.session;
    }
    async function send(action, id, data = {}) {
        if (busy) return null;
        busy = true;
        const buttons = [
            ...document.querySelectorAll(
                '.modal-footer button,#consult-form button,[data-act="hold-booking"],[data-act="confirm-booking"]'
            ),
        ];
        const enabled = buttons.filter((b) => !b.disabled);
        enabled.forEach((b) => (b.disabled = true));
        try {
            const token = document.querySelector(
                '#his-server-logout input[name="__RequestVerificationToken"]'
            )?.value;
            if (!token) throw new Error('Phiên làm việc đã hết. Vui lòng đăng nhập lại.');
            const response = await fetch(config.apiBase + 'command', {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    Accept: 'application/json',
                    RequestVerificationToken: token,
                },
                body: JSON.stringify({ action, id: id || null, data, screen: ui.route }),
            });
            if (response.status === 401 || response.redirected) {
                location.assign(config.login);
                return null;
            }
            const result = await response.json();
            if (!response.ok) {
                if (response.status === 409) {
                    await refresh();
                    render();
                }
                throw new Error(result.message || 'Không thể lưu thao tác.');
            }
            await refresh();
            return result;
        } catch (error) {
            toast(error.message || 'Không kết nối được máy chủ.', true);
            return null;
        } finally {
            busy = false;
            enabled.filter((b) => b.isConnected).forEach((b) => (b.disabled = false));
        }
    }
    persistDraft = () => {
        const form = document.getElementById('consult-form');
        if (form && !form.querySelector('fieldset')?.disabled)
            draft = { id: form.dataset.id, ...readConsultForm(form) };
    };
    document.addEventListener(
        'submit',
        async (event) => {
            const form = event.target;
            if (!forms.has(form.id)) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            let data = formData(form);
            if (form.id === 'consult-form') data = readConsultForm(form);
            if (form.id === 'catalog-form') data.key = form.dataset.key;
            if (form.id === 'lab-order-form') {
                const clinical = document.getElementById('consult-form');
                data.draft = clinical ? readConsultForm(clinical) : draft;
            }
            if (form.id === 'password-form') {
                data.newPassword = data.password;
                data.confirmPassword = data.confirm;
            }
            const invoiceId = form.dataset.id;
            const result = await send(form.id, invoiceId, data);
            if (!result) return;
            closeModal();
            if (form.id === 'family-form' && ui.route === 'patient/booking')
                ui.booking.patientId = result.id;
            if (form.id === 'consult-form') {
                draft = null;
                ui.selectedAppointment = null;
            }
            if (form.id === 'walkin-form') {
                ui.selectedAppointment = result.id;
                go('staff/vitals');
            } else {
                render();
                if (form.id === 'insurance-form' || form.id === 'payment-form')
                    invoiceDetail(invoiceId);
            }
            toast(result.message);
        },
        true
    );
    document.addEventListener(
        'click',
        async (event) => {
            const element = event.target.closest('[data-act]');
            if (!element) return;
            const action = element.dataset.act;
            if (action === 'new-account' || action === 'edit-account') {
                queueMicrotask(enhanceAccount);
                return;
            }
            if (['reset', 'confirm-reset', 'login-form', 'register-form'].includes(action)) {
                event.preventDefault();
                event.stopImmediatePropagation();
                toast('Chức năng này chỉ có ở bản xem mẫu.', true);
                return;
            }
            if (!actions.has(action)) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            let id = element.dataset.id;
            let data = {};
            if (action === 'hold-booking') data = { ...ui.booking };
            if (action === 'confirm-booking' || action === 'release-hold') id = ui.booking.holdId;
            if (action === 'call-next') data.roomId = ui.roomId;
            if (action === 'save-draft') {
                const form = document.getElementById('consult-form');
                if (!form) return;
                id = form.dataset.id;
                data = readConsultForm(form);
            }
            const notice =
                action === 'read-notice' ? db.notifications.find((n) => n.id === id) : null;
            const result = await send(action, id, data);
            if (!result) return;
            closeModal();
            if (action === 'hold-booking') {
                ui.booking.holdId = result.id;
                ui.booking.step = 4;
                render();
                window.scrollTo(0, 0);
            } else if (action === 'confirm-booking') {
                ui.booking.holdId = null;
                ui.booking.step = 1;
                go('patient/appointments');
            } else if (action === 'release-hold') {
                ui.booking.holdId = null;
                ui.booking.step = 3;
                ui.booking.time = '';
                render();
            } else {
                if (action === 'begin-consult') ui.selectedAppointment = id;
                if (action === 'save-draft') draft = null;
                render();
                if (
                    notice?.appointmentId &&
                    db.appointments.some((a) => a.id === notice.appointmentId)
                )
                    appointmentDetail(notice.appointmentId);
            }
            toast(result.message);
        },
        true
    );
    function enhanceAccount() {
        const form = document.getElementById('account-form');
        if (!form) return;
        const creating = !form.dataset.id;
        const role = form.querySelector('[name="role"]');
        if (!creating) {
            role.disabled = true;
            const dept = form.querySelector('[name="deptId"]');
            if (dept) dept.disabled = true;
            return;
        }
        if (!form.querySelector('[name="password"]'))
            form.querySelector('.form-grid').insertAdjacentHTML(
                'beforeend',
                '<div class="field full"><label for="ac-initial-password">Mật khẩu ban đầu *</label><input id="ac-initial-password" type="password" name="password" autocomplete="new-password" minlength="8" required></div><div class="field full" data-patient-fields><div class="form-grid"><div class="field"><label for="ac-dob">Ngày sinh người khám *</label><input id="ac-dob" type="date" name="dob" max="' +
                    TODAY +
                    '"></div><div class="field"><label for="ac-phone">Điện thoại người khám *</label><input id="ac-phone" name="phone" type="tel" pattern="[0-9+ ()-]{9,16}"></div></div></div>'
            );
        const fields = form.querySelector('[data-patient-fields]');
        const patient = role.value === 'PATIENT';
        fields.hidden = !patient;
        fields.querySelectorAll('input').forEach((input) => {
            input.disabled = !patient;
            input.required = patient;
        });
    }
    document.addEventListener('change', (event) => {
        if (event.target.id === 'ac-role') queueMicrotask(enhanceAccount);
    });
    const originalRender = render;
    render = () => {
        originalRender();
        document
            .querySelectorAll('.sidebar-bottom>div>span')
            .forEach((el) => (el.textContent = 'PostgreSQL · Dữ liệu thật'));
        document
            .querySelectorAll('.utility-demo')
            .forEach((el) => (el.textContent = 'Cổng thông tin bệnh viện'));
        document
            .querySelectorAll('.ui-preview-shortcut')
            .forEach((el) =>
                el.setAttribute(
                    'title',
                    'Trang xem mẫu sử dụng bộ dữ liệu riêng, không ghi database'
                )
            );
    };
    const originalAvailableTimes = availableTimes;
    availableTimes = (doctorId, date, shift) =>
        db.slots
            .filter(
                (s) =>
                    s.doctorId === doctorId &&
                    s.date === date &&
                    s.shift === shift &&
                    s.available &&
                    s.remaining > 0
            )
            .map((s) => s.time);
    window.addEventListener('online', () =>
        toast('Đã có kết nối mạng. Bạn có thể tải lại dữ liệu.')
    );
    window.addEventListener('offline', () =>
        toast('Đang mất kết nối. Các thay đổi chưa được lưu lên máy chủ.', true)
    );
})();
