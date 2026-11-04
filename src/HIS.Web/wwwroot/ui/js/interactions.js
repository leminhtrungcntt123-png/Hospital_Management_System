/* interactions */
function beginBooking(did = '', pid = '', parentId = '') {
    if (session.role !== 'PATIENT') {
        switchRole('PATIENT');
    }
    if (!session.authenticated) {
        go('account/login');
        return;
    }
    const d = doctor(did) || db.doctors[0];
    ui.booking = {
        step: pid ? 2 : did ? 2 : 1,
        patientId: pid || selfPatient() || myLinks()[0]?.patientId,
        doctorId: d.id,
        method: 'specialty',
        date: workingDays()[1],
        time: '',
        shift: 'MORNING',
        reason: '',
        holdId: null,
        parentId,
    };
    go('patient/booking');
}

function bindEvents() {
    document.addEventListener('click', handleUiClick);
    document.addEventListener('submit', handleUiSubmit);
    document.addEventListener('input', (e) => {
        const t = e.target;
        if (t.hasAttribute('data-booking-reason')) {
            ui.booking.reason = t.value;
            return;
        }
        if (t.matches('[data-search]')) {
            const q = t.value,
                start = t.selectionStart,
                kind = t.dataset.search;
            ui.query = q;
            render();
            const input = $(`[data-search="${kind}"]`);
            input?.focus();
            input?.setSelectionRange(start, start);
        }
    });
    document.addEventListener('change', (e) => {
        const t = e.target,
            key = t.dataset.change;
        if (!key) return;
        if (key === 'walk-existing') {
            $('#walk-new-fields').hidden = t.value !== 'NEW';
            $$('#walk-new-fields input,#walk-new-fields select').forEach(
                (i) => (i.disabled = t.value !== 'NEW')
            );
            return;
        }
        ui[key] = t.value;
        render();
    });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            closeModal();
            if (ui.mobileMenu) {
                ui.mobileMenu = false;
                render();
            }
        }
        if (e.key === 'Tab' && $('.modal')) {
            const all = [
                    ...$('.modal').querySelectorAll(
                        'button:not([disabled]),a[href],input:not([disabled]),select:not([disabled]),textarea:not([disabled])'
                    ),
                ].filter((e) => e.offsetParent !== null),
                first = all[0],
                last = all[all.length - 1];
            if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last?.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first?.focus();
            }
        }
    });
    document.addEventListener('pointerdown', (e) => {
        if (e.target.classList.contains('modal-backdrop')) closeModal();
    });
    window.addEventListener('hashchange', () => {
        const route = location.hash.replace(/^#\//, '') || 'public/home';
        if (ui.route !== route) {
            persistDraft();
            closeModal();
            ui.route = route;
            ui.query = '';
            ui.filter = 'ALL';
            render();
            window.scrollTo(0, 0);
        }
    });
    window.addEventListener('beforeunload', persistDraft);
}
