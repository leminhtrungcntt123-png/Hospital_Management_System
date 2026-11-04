function boot() {
    window.configureHisUi?.();
    ui.route = window.getHisInitialRoute
        ? window.getHisInitialRoute()
        : location.hash.replace(/^#\//, '') || 'public/home';
    bindEvents();
    render();
    setInterval(() => {
        expireHolds();
        const el = $('[data-countdown]');
        if (el) {
            const a = appointment(el.dataset.countdown),
                remaining = Math.max(0, Math.floor(((a?.holdUntil || 0) - Date.now()) / 1000));
            el.textContent =
                String(Math.floor(remaining / 60)).padStart(2, '0') +
                ':' +
                String(remaining % 60).padStart(2, '0');
            if (!remaining && a?.status === 'EXPIRED')
                el.closest('.hold-banner')?.classList.add('expired');
        }
    }, 1000);
}

window.addEventListener('DOMContentLoaded', boot);

function plainName(value) {
    const text = String(value || '').trim();
    if (/[<>]/.test(text)) throw new Error('Tên không được chứa dấu < hoặc >.');
    return text;
}
