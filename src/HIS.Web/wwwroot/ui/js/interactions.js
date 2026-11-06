function accountMenu() {
    if (!config.authenticated) {
        location.assign(config.login);
        return;
    }
    openModal(
        'Tài khoản của bạn',
        `<div class="menu-links">
        <p>${esc(config.displayName)}</p>
        <a href="${esc(config.home)}">${icon('grid')}Không gian làm việc</a>
        <a href="${esc(config.changePassword)}">${icon('shield')}Đổi mật khẩu</a>
    </div>`,
        button('Đóng', 'close') +
            button('Đăng xuất', 'logout', 'logout', 'outline'),
    );
}

function bindEvents() {
    document.addEventListener('click', (event) => {
        const target = event.target.closest('[data-act], [data-route]');
        if (!target || target.disabled) return;
        if (target.dataset.route) {
            if (
                event.ctrlKey ||
                event.metaKey ||
                event.shiftKey ||
                event.altKey
            )
                return;
            event.preventDefault();
            go(target.dataset.route);
            return;
        }
        switch (target.dataset.act) {
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
        if (!event.target.matches('[data-search]')) return;
        const cursor = event.target.selectionStart;
        ui.query = event.target.value;
        render();
        const input = $('[data-search]');
        input?.focus();
        input?.setSelectionRange(cursor, cursor);
    });
    document.addEventListener('change', (event) => {
        if (!event.target.matches('[data-department]')) return;
        ui.department = event.target.value;
        render();
    });
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            closeModal();
            if (ui.mobileMenu) {
                ui.mobileMenu = false;
                render();
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
        const route = location.hash.replace(/^#\//, '') || 'public/home';
        if (ui.route !== route) go(route);
    });
}
