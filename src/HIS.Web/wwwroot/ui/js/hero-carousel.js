let heroTimer;
function syncHeroCarousel() {
    clearInterval(heroTimer);
    const carousel = document.querySelector('[data-hero-carousel]');
    if (!carousel) return;
    ui.heroPaused ??= window.matchMedia?.('(prefers-reduced-motion: reduce)').matches || false;
    if (ui.heroPaused) return;
    heroTimer = setInterval(() => {
        if (
            document.hidden ||
            carousel.matches(':hover') ||
            carousel.contains(document.activeElement)
        )
            return;
        setHeroSlide(((ui.heroIndex || 0) + 1) % 3);
    }, 5000);
}

function setHeroSlide(index) {
    ui.heroIndex = ((index % 3) + 3) % 3;
    document.querySelectorAll('[data-hero-slide]').forEach((slide, position) => {
        const active = position === ui.heroIndex;
        slide.classList.toggle('active', active);
        slide.setAttribute('aria-hidden', String(!active));
    });
    document
        .querySelectorAll('[data-act="hero-slide"]')
        .forEach((dot, position) =>
            dot.setAttribute('aria-pressed', String(position === ui.heroIndex)),
        );
}
