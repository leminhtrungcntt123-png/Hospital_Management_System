const receptionMenu = document.getElementById('reception-menu');
const receptionSidebar = document.getElementById('reception-sidebar');
const receptionScrim = document.getElementById('reception-scrim');

function setReceptionMenu(open) {
    receptionSidebar.classList.toggle('open', open);
    receptionMenu.setAttribute('aria-expanded', String(open));
    receptionScrim.hidden = !open;
    if (open) receptionSidebar.querySelector('a').focus();
    else receptionMenu.focus();
}

receptionMenu.addEventListener('click', () => {
    setReceptionMenu(receptionMenu.getAttribute('aria-expanded') !== 'true');
});
receptionScrim.addEventListener('click', () => setReceptionMenu(false));
document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && receptionMenu.getAttribute('aria-expanded') === 'true') {
        setReceptionMenu(false);
    }
});
