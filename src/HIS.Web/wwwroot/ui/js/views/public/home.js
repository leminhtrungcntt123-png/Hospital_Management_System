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
    return `<section class="campaign-banner hero-carousel" data-hero-carousel role="region" aria-roledescription="trình chiếu" aria-label="Chăm sóc tại MediCare">
            ${[
                [
                    'home-banner-family.jpg',
                    'healthcare-hero.png',
                    'Bác sĩ trò chuyện cùng mẹ và bé tại phòng khám',
                    'AN TÂM TỪ CUỘC HẸN ĐẦU TIÊN',
                    'An tâm đặt khám.<br /><em>Chủ động mỗi ngày.</em>',
                    'Chọn chuyên khoa, tìm bác sĩ và chuẩn bị lịch khám ngay trên MediCare.',
                ],
                [
                    'home-banner-senior.jpg',
                    'healthcare-hero-2.jpg',
                    'Bác sĩ tư vấn cho hai người cao tuổi',
                    'QUAN TÂM ĐẾN NHỮNG NGƯỜI BẠN YÊU',
                    'Chăm sóc sức khỏe.<br /><em>Kết nối cả gia đình.</em>',
                    'Quản lý hồ sơ người khám của bạn và người thân, thuận tiện theo dõi mỗi cuộc hẹn.',
                ],
                [
                    'home-banner-welcome.jpg',
                    'healthcare-hero-3.jpg',
                    'Điều dưỡng hướng dẫn người đến khám',
                    'ĐỒNG HÀNH TỪ BƯỚC TIẾP ĐÓN',
                    'Đúng chuyên khoa.<br /><em>Thuận tiện từng bước.</em>',
                    'Tìm hiểu thông tin trước khi khám, lựa chọn bác sĩ phù hợp và chủ động sắp xếp thời gian.',
                ],
            ]
                .map(
                    ([file, mobileFile, alt, eyebrow, title, detail], index) =>
                        `<figure data-hero-slide class="hero-slide ${(ui.heroIndex || 0) === index ? 'active' : ''}" aria-hidden="${(ui.heroIndex || 0) !== index}" ${(ui.heroIndex || 0) !== index ? 'inert' : ''}>
                            <picture><source media="(max-width: 600px)" srcset="${esc(config.assetBase + mobileFile)}" /><img src="${esc(config.assetBase + file)}" alt="${alt}" ${index ? 'loading="lazy"' : 'fetchpriority="high"'} width="1920" height="640" /></picture>
                            <figcaption class="campaign-copy"><span class="eyebrow">${eyebrow}</span>
                                <h1 tabindex="-1">${title}</h1><p>${detail}</p>
                                <div class="campaign-actions">${primary}${link('Tìm bác sĩ', 'public/doctors', 'search', 'btn outline')}</div>
                                <span class="campaign-signature">${icon('heart')}MediCare · Đồng hành cùng bạn và gia đình</span>
                            </figcaption>
                        </figure>`,
                )
                .join('')}
            <div class="hero-carousel-controls"><div class="hero-dots" aria-label="Chọn ảnh">${[0, 1, 2].map((index) => button('', 'hero-slide', '', 'hero-dot', `data-index="${index}" aria-label="Xem ảnh ${index + 1}" aria-pressed="${(ui.heroIndex || 0) === index}"`)).join('')}</div></div>
    </section>
    <div class="home-content">
    <div class="quick-access" aria-label="Truy cập nhanh">${shortcuts.map(([route, name, title, detail]) => link(`<span class="quick-icon">${icon(name)}</span><span><strong>${title}</strong><small>${detail}</small></span>${icon('arrow')}`, route, '', 'quick-item')).join('')}</div>
    <section class="why-medicare" aria-labelledby="why-medicare-title">
        <div class="why-layout">
            <figure class="why-portrait"><img src="${esc(config.assetBase + 'home-care-nurse.png')}" alt="Nhân viên y tế MediCare" width="800" height="1200" loading="lazy" /><figcaption>Đồng hành trong từng cuộc hẹn</figcaption></figure>
            <div class="why-content">
            <div class="why-heading"><span class="eyebrow">THUẬN TIỆN HƠN, AN TÂM HƠN</span><h2 id="why-medicare-title">Tại sao chọn <em>MediCare?</em></h2><p>Một nơi kết nối bạn với hành trình chăm sóc sức khỏe.</p></div>
            <div class="why-features">${[
                [
                    'calendar',
                    'Đặt khám chủ động',
                    'Chọn bác sĩ và khung giờ phù hợp, điền thông tin người khám ngay trên website.',
                    'public/booking',
                    'Đặt lịch khám',
                ],
                [
                    'stethoscope',
                    'Tìm đúng chuyên khoa',
                    'Khám phá các chuyên khoa, tìm hiểu hồ sơ và thông tin chuyên môn của bác sĩ trước cuộc hẹn.',
                    'public/specialties',
                    'Khám phá chuyên khoa',
                ],
                [
                    'users',
                    'Chăm sóc cho gia đình',
                    'Lưu và quản lý hồ sơ của bạn cùng người thân trong không gian dành cho người khám.',
                    'patient/family',
                    'Quản lý người khám',
                ],
                [
                    'clipboard',
                    'Theo dõi thuận tiện',
                    'Tra cứu lịch hẹn và hồ sơ sức khỏe được cấp quyền, tiếp nối thông tin sau mỗi lần khám.',
                    'patient/records',
                    'Xem hồ sơ sức khỏe',
                ],
            ]
                .map(
                    ([name, title, detail, route, label]) =>
                        `<article class="why-feature"><span class="why-icon">${icon(name)}</span><h3>${title}</h3><p>${detail}</p>${link(label, route, 'arrow', 'text-link')}</article>`,
                )
                .join('')}</div></div>
        </div>
    </section>
    <section class="doctor-search-section" aria-labelledby="home-doctor-title">
        <div><span class="eyebrow">BẮT ĐẦU TỪ NHU CẦU CỦA BẠN</span><h2 id="home-doctor-title">Tìm đúng bác sĩ, đúng chuyên khoa.</h2><p>Tra cứu đội ngũ chuyên môn đang có trong hệ thống bệnh viện.</p></div>
        <form class="home-doctor-search" data-doctor-search>
            <label><span>Tên bác sĩ hoặc chuyên môn</span><span class="search-input">${icon('search')}<input data-search name="q" value="${esc(ui.query)}" placeholder="Bạn muốn tìm bác sĩ nào?" autocomplete="off" /></span></label>
            <label><span>Chuyên khoa</span><select data-department name="department"><option value="">Tất cả chuyên khoa</option>${catalog.departments.map((department) => `<option value="${esc(department.name)}" ${ui.department === department.name ? 'selected' : ''}>${esc(department.name)}</option>`).join('')}</select></label>
            <button class="btn primary" type="submit">${icon('search')}Tìm bác sĩ</button>
        </form>
    </section>
    <section class="home-section" aria-labelledby="home-specialties-title"><div class="section-heading"><div><span class="eyebrow">KHÁM PHÁ THEO CHUYÊN KHOA</span><h2 id="home-specialties-title">Chăm sóc phù hợp với bạn</h2><p>Tìm thông tin khoa phòng và đội ngũ bác sĩ phụ trách.</p></div>${link('Tất cả chuyên khoa', 'public/specialties', 'arrow', 'text-link')}</div>
        ${specialtyCards(4)}
    </section>
    <section class="home-section" aria-labelledby="home-doctors-title"><div class="section-heading"><div><span class="eyebrow">ĐỘI NGŨ CHUYÊN MÔN</span><h2 id="home-doctors-title" tabindex="-1">Danh sách Bác sĩ - Chuyên gia</h2><p>Chọn số trang bên dưới để xem thêm bác sĩ, hoặc mở hồ sơ để tìm hiểu chuyên môn.</p></div>${link('Tìm và lọc bác sĩ', 'public/doctors', 'search', 'text-link')}</div>
        ${doctorList('public/home')}
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
    </section></div>`;
}

function doctorCard(doctor) {
    const illustrative = doctor.kind === 'illustration';
    const canBook = !illustrative && (!config.authenticated || config.role === 'PATIENT');
    return `<article class="doctor-card">${doctorPhoto(doctor)}
        <div class="doctor-card-body"><span class="catalog-badge">${esc(doctor.department || 'Bác sĩ chuyên khoa')}</span><h3>${esc(doctor.name)}</h3>
            <ul class="doctor-card-meta"><li>${icon('stethoscope')}<span>${esc(doctor.specialty || 'Thông tin chuyên môn đang được cập nhật.')}</span></li><li>${icon('building')}<span>${esc(doctor.department)}</span></li>${doctorCareerSummary(doctor)}</ul>
            <div class="doctor-card-actions">${canBook ? link('Xem lịch khám', 'public/booking?doctorId=' + encodeURIComponent(doctor.id), 'calendar', 'btn primary') : ''}
                ${link('Xem hồ sơ', 'public/doctor/' + encodeURIComponent(doctor.id), 'arrow', 'text-link')}
            </div>
        </div></article>`;
}

function doctorPhoto(doctor = {}) {
    const portrait = doctor.avatar || doctor.illustration || (typeof doctorIllustrations !== 'undefined' ? doctorIllustrations[doctor.name] : null);
    return `<figure class="doctor-portrait doctor-photo">${
        portrait
            ? `<img src="${esc(config.assetBase + portrait)}" alt="Ảnh chân dung ${esc(doctor.name)}" width="640" height="800" loading="lazy" decoding="async" />`
            : `<span class="doctor-initials" aria-hidden="true">${esc(initials(doctor.name)) || icon('stethoscope')}</span>`
    }</figure>`;
}
