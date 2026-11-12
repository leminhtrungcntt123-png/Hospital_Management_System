(() => {
    const sidebar = document.getElementById('staff-sidebar');
    const menu = document.getElementById('staff-menu');
    const scrim = document.getElementById('staff-scrim');
    const account = document.querySelector('.reception-account');

    function setMenuOpen(open) {
        sidebar.classList.toggle('open', open);
        menu.setAttribute('aria-expanded', String(open));
        scrim.hidden = !open;
    }

    menu.addEventListener('click', () =>
        setMenuOpen(menu.getAttribute('aria-expanded') !== 'true'),
    );
    scrim.addEventListener('click', () => {
        setMenuOpen(false);
        menu.focus();
    });
    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape') return;
        if (account.open) {
            account.open = false;
            account.querySelector('summary').focus();
        }
        if (menu.getAttribute('aria-expanded') === 'true') {
            setMenuOpen(false);
            menu.focus();
        }
    });
    document.addEventListener('click', (event) => {
        if (!account.contains(event.target)) account.open = false;
    });
    window.matchMedia('(min-width: 851px)').addEventListener('change', () => setMenuOpen(false));
})();
