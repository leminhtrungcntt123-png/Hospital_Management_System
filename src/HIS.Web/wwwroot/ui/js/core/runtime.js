/* core / runtime */
function audit(action, pid = '', detail = '', module = 'Hệ thống') {
    if (window.hisLive) return;
    db.logs.unshift({
        id: uid('log'),
        actor: session.accountId,
        patientId: pid,
        action,
        module,
        date: stamp(),
        detail,
    });
    save();
}

function notify(a, title, content) {
    if (a.bookedBy)
        db.notifications.unshift({
            id: uid('n'),
            accountId: a.bookedBy,
            appointmentId: a.id,
            title: patient(a.patientId).name + ' · ' + title,
            content,
            read: false,
            date: TODAY,
        });
}

function transition(a, to) {
    if (!transitions[a.status]?.includes(to))
        throw new Error('Lượt khám không thể chuyển sang trạng thái này.');
    const old = a.status;
    a.status = to;
    a.history.push({ from: old, to, at: stamp(), by: acc()?.name || 'Hệ thống' });
    audit('CHANGE_STATUS', a.patientId, old + ' → ' + to, 'Lịch hẹn');
    save();
}

function invoiceTotal(i) {
    const gross = i.items.reduce((n, x) => n + Number(x.amount), 0),
        covered = Math.round((gross * (i.coveragePct || 0)) / 100),
        due = gross - covered,
        paid = db.payments
            .filter((p) => p.invoiceId === i.id)
            .reduce((n, p) => n + (p.type === 'REFUND' ? -1 : 1) * p.amount, 0);
    return {
        gross,
        covered,
        due,
        paid,
        balance: Math.max(0, due - paid),
        overpaid: Math.max(0, paid - due),
        status: paid >= due ? 'PAID' : paid > 0 ? 'PARTIALLY_PAID' : 'UNPAID',
    };
}

function toast(message, error = false) {
    clearTimeout(toastTimer);
    $('#toast-root').innerHTML = /* HTML */ `<div
        class="toast ${error ? 'error' : ''}"
        role="status"
    >
        ${icon(error ? 'info' : 'check')}<span>${esc(message)}</span>
    </div>`;
    toastTimer = setTimeout(() => ($('#toast-root').innerHTML = ''), 4500);
}

function go(route, opts = {}) {
    persistDraft();
    closeModal();
    ui.route = route;
    ui.query = '';
    ui.filter = opts.filter || 'ALL';
    ui.dept = 'ALL';
    ui.mobileMenu = false;
    if (opts.id) ui.selectedAppointment = opts.id;
    location.hash = '#/' + route;
    render();
    window.scrollTo({ top: 0, behavior: 'instant' });
}

function switchRole(role) {
    persistDraft();
    closeModal();
    ui.mobileMenu = false;
    session = { role, accountId: roleInfo[role].accountId, authenticated: true };
    ui.profileId = selfPatient() || 'BN0001';
    ui.selectedAppointment = null;
    ui.route =
        role === 'PATIENT'
            ? 'public/home'
            : role === 'DOCTOR'
              ? 'staff/consultation'
              : role === 'ADMIN'
                ? 'admin/dashboard'
                : role === 'DEPARTMENT_HEAD'
                  ? 'staff/schedule'
                  : 'staff/dashboard';
    location.hash = '#/' + ui.route;
    ui.query = '';
    ui.filter = 'ALL';
    render();
    window.scrollTo(0, 0);
    toast('Đang trải nghiệm vai trò ' + roleInfo[role].name);
}

function openModal(title, sub, body, footer = '', wide = false) {
    returnFocus = document.activeElement;
    $('#overlay-root').innerHTML = /* HTML */ `<div class="modal-backdrop">
        <section
            class="modal ${wide ? 'wide' : ''}"
            role="dialog"
            aria-modal="true"
            aria-labelledby="dialog-title"
        >
            <header class="modal-header">
                <div>
                    <h2 id="dialog-title">${title}</h2>
                    ${sub ? /* HTML */ `<p>${sub}</p>` : ''}
                </div>
                <button type="button" class="icon-btn" data-act="close" aria-label="Đóng">
                    ${icon('close')}
                </button>
            </header>
            <div class="modal-body">${body}</div>
            ${footer ? /* HTML */ `<footer class="modal-footer">${footer}</footer>` : ''}
        </section>
    </div>`;
    document.body.style.overflow = 'hidden';
    setTimeout(
        () =>
            $('.modal input:not([readonly]),.modal select,.modal textarea,.modal button')?.focus(),
        20
    );
}

function closeModal() {
    if ($('#overlay-root')) $('#overlay-root').innerHTML = '';
    document.body.style.overflow = '';
    if (returnFocus?.isConnected) returnFocus.focus();
}

function rolesModal() {
    openModal(
        'Chọn không gian trải nghiệm',
        'Dữ liệu mẫu dùng chung để bạn thử toàn bộ quy trình.',
        /* HTML */ `<div class="role-options">
                ${Object.entries(roleInfo)
                    .map(
                        ([r, x]) =>
                            /* HTML */ `<button
                                type="button"
                                class="role-option ${r === session.role ? 'selected' : ''}"
                                data-act="role"
                                data-role="${r}"
                            >
                                <span class="role-icon">${icon(x.icon)}</span
                                ><span
                                    ><strong>${x.name}</strong><small>${x.description}</small></span
                                >${icon(r === session.role ? 'check' : 'arrow')}
                            </button>`
                    )
                    .join('')}
            </div>
            <p class="fine-print">
                Đây là bộ chuyển vai trò của bản mẫu, không phải cơ chế đăng nhập hay phân quyền máy
                chủ.
            </p>`,
        button('Khôi phục dữ liệu mẫu', 'reset', 'refresh', 'ghost')
    );
}

function guardRoute() {
    const r = ui.route;
    if (r.startsWith('admin/') && session.role !== 'ADMIN') return false;
    if (r === 'staff/reception' || r === 'staff/vitals' || r === 'staff/cashier')
        return session.role === 'NURSE';
    if (r === 'staff/consultation' || r === 'staff/labs') return session.role === 'DOCTOR';
    if (r === 'staff/records') return ['DOCTOR', 'DEPARTMENT_HEAD'].includes(session.role);
    if (r === 'staff/schedule' || r === 'staff/exceptions')
        return ['DOCTOR', 'NURSE', 'DEPARTMENT_HEAD'].includes(session.role);
    if (r === 'staff/patients')
        return ['NURSE', 'DOCTOR', 'DEPARTMENT_HEAD'].includes(session.role);
    if (r.startsWith('staff/')) return session.role !== 'PATIENT' && session.role !== 'ADMIN';
    if (r.startsWith('patient/')) return session.role === 'PATIENT' && session.authenticated;
    return true;
}

function render() {
    try {
        sessionStorage.setItem(STORAGE_KEY + '-session', JSON.stringify(session));
    } catch {}
    expireHolds();
    document.title =
        'MediCare — ' +
        (pageTitles[ui.route] ||
            (ui.route.startsWith('public/service/')
                ? db.services.find((s) => s.id === ui.route.split('/').pop())?.name
                : ui.route.startsWith('public/doctor/')
                  ? 'BS. ' + doctor(ui.route.split('/').pop())?.name
                  : null) ||
            'Chăm sóc sức khỏe');
    $('#app').innerHTML =
        ui.route.startsWith('public/') ||
        ui.route.startsWith('patient/') ||
        ui.route.startsWith('account/')
            ? publicShell()
            : staffShell();
}
