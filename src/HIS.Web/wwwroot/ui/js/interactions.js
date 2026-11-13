function accountMenu() {
    if (!config.authenticated) {
        location.assign(config.login);
        return;
    }
    const email = config.userEmail || '';
    openModal(
        'Tài khoản của bạn',
        `<div class="account-menu-popover">
            <div class="account-popover-header">
                <span class="account-popover-avatar">${avatar(config.displayName)}</span>
                <div class="account-popover-meta">
                    <strong>${esc(config.displayName)}</strong>
                    ${email ? `<span>${esc(email)}</span>` : ''}
                </div>
            </div>
            <div class="account-popover-links">
                <a class="account-popover-item" href="${esc(routeUrl('patient/profile'))}" data-route="patient/profile">
                    ${icon('user')}<span>Thông tin cá nhân</span>
                </a>
                <a class="account-popover-item" href="${esc(routeUrl('patient/family'))}" data-route="patient/family">
                    ${icon('users')}<span>Thông tin người thân</span>
                </a>
                <a class="account-popover-item" href="${esc(config.changePassword)}">${icon('shield')}<span>Đổi mật khẩu</span></a>
                ${config.role !== 'PATIENT' ? `<a class="account-popover-item" href="${esc(config.home)}">${icon('grid')}<span>Bàn làm việc</span></a>` : ''}
            </div>
        </div>`,
        button('Đóng', 'close') + button('Đăng xuất', 'logout', 'logout', 'outline'),
    );
}

function bindEvents() {
    document.addEventListener('submit', (event) => {
        if (event.target.matches('[data-guest-booking]')) {
            event.preventDefault();
            proceedToConfirmation();
            return;
        }
        if (!event.target.matches('[data-doctor-search]')) return;
        event.preventDefault();
        const filters = new URLSearchParams({ q: ui.query, department: ui.department });
        go('public/doctors?' + filters.toString());
    });
    document.addEventListener('click', (event) => {
        const target = event.target.closest('[data-act], [data-route]');
        if (!target || target.disabled) return;
        if (target.dataset.route) {
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            go(target.dataset.route);
            return;
        }
        switch (target.dataset.act) {
            case 'hero-slide':
                setHeroSlide(Number(target.dataset.index));
                syncHeroCarousel();
                break;
            case 'new-guest-booking':
                resetGuestBooking();
                render();
                break;
            case 'retry-guest-slots':
                loadGuestSlots();
                break;
            case 'menu':
                ui.mobileMenu = !ui.mobileMenu;
                render();
                $(ui.mobileMenu ? '.sidebar a, .public-nav a' : '[data-act="menu"]')?.focus();
                break;
            case 'account-menu':
                accountMenu();
                break;
            case 'logout':
                document.getElementById('his-server-logout').requestSubmit();
                break;
            case 'close':
                closeModal();
                break;
            case 'reload-catalog':
                loadCatalog();
                break;
        }
    });
    document.addEventListener('input', (event) => {
        if (event.target.matches('[data-booking-field]')) {
            guestBooking.patient[event.target.name] = event.target.value;
            return;
        }
        if (!event.target.matches('[data-search]')) return;
        const cursor = event.target.selectionStart;
        ui.query = event.target.value;
        ui.doctorPage = 1;
        render();
        const input = $('[data-search]');
        input?.focus();
        input?.setSelectionRange(cursor, cursor);
    });
    document.addEventListener('change', (event) => {
        if (event.target.matches('[data-booking-field]')) {
            guestBooking.patient[event.target.name] = event.target.value;
            return;
        }
        if (event.target.matches('[data-booking-choice]')) {
            changeGuestChoice(event.target.dataset.bookingChoice, event.target.value);
            return;
        }
        if (!event.target.matches('[data-department]')) return;
        ui.department = event.target.value;
        ui.doctorPage = 1;
        render();
    });
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            closeModal();
            if (ui.mobileMenu) {
                ui.mobileMenu = false;
                render();
                $('[data-act="menu"]')?.focus();
            }
        }
        const modal = $('#overlay-root .modal');
        if (event.key !== 'Tab' || !modal) return;
        const controls = [...modal.querySelectorAll('button, a[href]')];
        const first = controls[0];
        const last = controls[controls.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last?.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first?.focus();
        }
    });
    document.addEventListener('pointerdown', (event) => {
        if (event.target.classList.contains('modal-backdrop')) closeModal();
    });
    window.addEventListener('hashchange', () => {
        if (location.hash && !location.hash.startsWith('#/')) return;
        const route = location.hash.replace(/^#\//, '') || 'public/home';
        if (ui.address !== route) go(route);
    });
}
