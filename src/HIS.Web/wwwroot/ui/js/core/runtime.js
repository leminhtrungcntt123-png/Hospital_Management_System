function render() {
    document.title =
        'MediCare — ' + (pageTitles[ui.route] || 'Chăm sóc sức khỏe');
    $('#app').innerHTML =
        ui.route.startsWith('public/') || ui.route.startsWith('patient/')
            ? publicShell()
            : staffShell();
}

function openModal(title, body, footer = '') {
    returnFocus = document.activeElement;
    $('#overlay-root').innerHTML = `<div class="modal-backdrop">
        <section class="modal" role="dialog" aria-modal="true" aria-labelledby="dialog-title">
            <header class="modal-header">
                <h2 id="dialog-title">${esc(title)}</h2>
                ${button('', 'close', 'close', 'icon-btn', 'aria-label="Đóng"')}
            </header>
            <div class="modal-body">${body}</div>
            ${footer ? `<footer class="modal-footer">${footer}</footer>` : ''}
        </section>
    </div>`;
    document.body.style.overflow = 'hidden';
    $('#overlay-root button, #overlay-root a')?.focus();
}

function closeModal() {
    $('#overlay-root').replaceChildren();
    document.body.style.overflow = '';
    if (returnFocus?.isConnected) returnFocus.focus();
}
