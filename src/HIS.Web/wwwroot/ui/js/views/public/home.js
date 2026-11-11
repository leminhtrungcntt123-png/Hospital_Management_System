function homePage() {
    ui.heroPaused ??= window.matchMedia?.('(prefers-reduced-motion: reduce)').matches || false;
    const canBook = !config.authenticated || config.role === 'PATIENT';
    const primary = canBook
        ? link('Đặt lịch khám', 'public/booking', 'calendar', 'btn primary')
        : `<a class="btn primary" href="${esc(config.home)}">${icon('grid')}Về bàn làm việc</a>`;
    const shortcuts = canBook
        ? [
              ['public/booking', 'calendar', 'Đặt lịch khám', 'Chuẩn bị cho cuộc hẹn sắp tới'],
              [
                  'patient/appointments',
                  'clock',
                  'Lịch hẹn của tôi',
                  'Theo dõi lịch khám và tiếp nhận',
              ],
              [
                  'patient/records',
                  'clipboard',
                  'Hồ sơ sức khỏe',
                  'Thông tin khám và kết quả của bạn',
              ],
          ]
        : [
              ['public/specialties', 'building', 'Chuyên khoa', 'Tra cứu các khoa trong bệnh viện'],
              ['public/doctors', 'stethoscope', 'Đội ngũ bác sĩ', 'Tìm thông tin chuyên môn'],
              ['public/guide', 'clipboard', 'Hướng dẫn khám', 'Quy trình dành cho người khám'],
          ];
    return `<section class="home-hero">
        <div class="hero-copy"><span class="eyebrow">MEDICARE · CỔNG KHÁM BỆNH TRỰC TUYẾN</span>
            <h1 tabindex="-1">An tâm đặt khám.<br /><em>Chủ động mỗi ngày.</em></h1>
            <p>Tìm bác sĩ phù hợp, chuẩn bị lịch khám và theo dõi hồ sơ sức khỏe cho bạn cùng người thân — trong cùng một nơi.</p>
            <div class="hero-actions">${primary}${link('Tìm bác sĩ phù hợp', 'public/doctors', 'search', 'btn outline')}</div>
            <div class="hero-trust"><span>${icon('shield')}Thông tin theo quyền truy cập</span><span>${icon('users')}Cho bạn và người thân</span></div>
        </div>
        <div class="hero-photo hero-carousel" data-hero-carousel role="region" aria-roledescription="trình chiếu" aria-label="Chăm sóc tại MediCare">
            ${[
                [
                    'healthcare-hero.png',
                    'Bác sĩ trò chuyện cùng mẹ và bé tại phòng khám',
                    'Tận tâm trong từng cuộc hẹn',
                ],
                [
                    'healthcare-hero-2.jpg',
                    'Ảnh minh họa bác sĩ tư vấn cho hai người cao tuổi',
                    'Chăm sóc bạn và gia đình',
                ],
                [
                    'healthcare-hero-3.jpg',
                    'Ảnh minh họa điều dưỡng hướng dẫn người đến khám',
                    'Đồng hành từ bước tiếp đón',
                ],
            ]
                .map(
                    ([file, alt, caption], index) =>
                        `<figure data-hero-slide class="hero-slide ${(ui.heroIndex || 0) === index ? 'active' : ''}" aria-hidden="${(ui.heroIndex || 0) !== index}"><img src="${esc(config.assetBase + file)}" alt="${alt}" ${index ? 'loading="lazy"' : 'fetchpriority="high"'} /><figcaption class="photo-note">${icon('heart')}${caption}</figcaption></figure>`,
                )
                .join('')}
            <div class="hero-carousel-controls"><div class="hero-dots" aria-label="Chọn ảnh">${[0, 1, 2].map((index) => button('', 'hero-slide', '', 'hero-dot', `data-index="${index}" aria-label="Xem ảnh ${index + 1}" aria-pressed="${(ui.heroIndex || 0) === index}"`)).join('')}</div></div>
        </div>
    </section>
    <div class="quick-access" aria-label="Truy cập nhanh">${shortcuts.map(([route, name, title, detail]) => link(`<span class="quick-icon">${icon(name)}</span><span><strong>${title}</strong><small>${detail}</small></span>${icon('arrow')}`, route, '', 'quick-item')).join('')}</div>
    <section class="doctor-search-section" aria-labelledby="home-doctor-title">
        <div><span class="eyebrow">BẮT ĐẦU TỪ NHU CẦU CỦA BẠN</span><h2 id="home-doctor-title">Tìm đúng bác sĩ, đúng chuyên khoa.</h2><p>Tra cứu đội ngũ chuyên môn đang có trong hệ thống bệnh viện.</p></div>
        <form class="home-doctor-search" data-doctor-search>
            <label><span>Tên bác sĩ hoặc chuyên môn</span><span class="search-input">${icon('search')}<input data-search name="q" value="${esc(ui.query)}" placeholder="Bạn muốn tìm bác sĩ nào?" autocomplete="off" /></span></label>
            <label><span>Chuyên khoa</span><select data-department name="department"><option value="">Tất cả chuyên khoa</option>${catalog.departments.map((department) => `<option value="${esc(department.name)}" ${ui.department === department.name ? 'selected' : ''}>${esc(department.name)}</option>`).join('')}</select></label>
            <button class="btn primary" type="submit">${icon('search')}Tìm bác sĩ</button>
        </form>
    </section>
    <section class="home-section" aria-labelledby="home-specialties-title"><div class="section-heading"><div><span class="eyebrow">KHÁM PHÁ THEO CHUYÊN KHOA</span><h2 id="home-specialties-title">Chăm sóc phù hợp với bạn</h2><p>Tìm thông tin khoa phòng và đội ngũ bác sĩ phụ trách.</p></div>${link('Tất cả chuyên khoa', 'public/specialties', 'arrow', 'text-link')}</div>
        ${catalog.status !== 'ready' ? catalogStatus() : catalog.departments.length ? `<div class="portal-specialties">${catalog.departments.map((department) => link(`<span class="specialty-symbol">${icon('building')}</span><h3>${esc(department.name)}</h3><p>${esc(department.description || 'Xem thông tin khoa và đội ngũ bác sĩ.')}</p><span class="text-link">Xem bác sĩ ${icon('arrow')}</span>`, 'public/doctors?department=' + encodeURIComponent(department.name), '', 'portal-specialty')).join('')}</div>` : empty('Chưa có thông tin chuyên khoa', 'Danh mục sẽ hiển thị khi bệnh viện cập nhật.')}
    </section>
    <section class="home-section" aria-labelledby="home-doctors-title"><div class="section-heading"><div><span class="eyebrow">ĐỘI NGŨ CHUYÊN MÔN</span><h2 id="home-doctors-title">Gặp bác sĩ của bạn</h2><p>Tra cứu thông tin chuyên môn trước khi đến khám.</p></div>${link('Xem tất cả bác sĩ', 'public/doctors', 'arrow', 'text-link')}</div>
        ${catalog.status !== 'ready' ? catalogStatus() : catalog.doctors.length ? `<div class="doctor-grid">${catalog.doctors.slice(0, 3).map(doctorCard).join('')}</div>` : empty('Chưa có thông tin bác sĩ')}
    </section>
    <section class="portal-journey"><div><span class="eyebrow">TRƯỚC, TRONG VÀ SAU BUỔI KHÁM</span><h2>Mỗi bước đều rõ ràng.</h2><p>Từ lựa chọn chuyên khoa đến theo dõi hồ sơ, luôn có đường dẫn đến tác vụ bạn cần.</p>${link('Xem hướng dẫn khám', 'public/guide', 'arrow', 'btn outline')}</div>
        <ol>${[
            [
                '01',
                'Tìm chuyên khoa và bác sĩ',
                'Chọn theo nhu cầu khám hoặc thông tin chuyên môn.',
            ],
            [
                '02',
                'Chuẩn bị thông tin người khám',
                'Kiểm tra hồ sơ của bạn hoặc người thân trước cuộc hẹn.',
            ],
            [
                '03',
                'Theo dõi lịch hẹn và hồ sơ',
                'Đối chiếu lịch đã xác nhận, hướng dẫn tiếp nhận và kết quả được cấp quyền.',
            ],
        ]
            .map(
                ([number, title, detail]) =>
                    `<li><span>${number}</span><div><h3>${title}</h3><p>${detail}</p></div></li>`,
            )
            .join('')}</ol>
    </section>`;
}

function doctorCard(doctor) {
    return `<article class="doctor-card">${doctorPhoto()}
        <div class="doctor-card-body"><span class="mini-label">${esc(doctor.department)}</span><h3>${esc(doctor.name)}</h3><p>${esc(doctor.specialty || 'Thông tin chuyên môn đang được cập nhật.')}</p>
            ${link('Xem thông tin bác sĩ', 'public/doctor/' + encodeURIComponent(doctor.id), 'arrow', 'text-link')}
        </div></article>`;
}

function doctorPhoto() {
    return `<figure class="doctor-portrait doctor-photo"><img src="${esc(config.assetBase)}doctors-illustration.jpg" alt="Ảnh minh họa đội ngũ bác sĩ" loading="lazy" /><figcaption>Ảnh minh hoạ</figcaption></figure>`;
}
