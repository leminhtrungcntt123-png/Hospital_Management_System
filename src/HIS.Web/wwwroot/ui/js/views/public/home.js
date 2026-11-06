function homePage() {
    return `<section class="home-hero">
        <div class="hero-copy"><span class="eyebrow">ĐỒNG HÀNH CÙNG SỨC KHỎE CỦA BẠN</span>
            <h1>An tâm đặt khám.<br /><em>Chủ động mỗi ngày.</em></h1>
            <p>Tìm hiểu chuyên khoa, xem thông tin bác sĩ và theo dõi hành trình khám chữa bệnh.</p>
            <div class="hero-actions">${link('Đặt lịch khám', 'patient/booking', 'calendar', 'btn primary large')}${link('Tìm bác sĩ', 'public/doctors', 'stethoscope', 'btn outline large')}</div>
        </div>
        <div class="hero-photo"><img src="${esc(config.assetBase)}healthcare-hero.png" alt="Ảnh minh họa bác sĩ trò chuyện với gia đình tại phòng khám" />
            <div class="photo-note">${icon('heart')}Tận tâm trong từng cuộc hẹn</div>
        </div>
    </section>
    <div class="quick-access">${[
        [
            'patient/booking',
            'calendar',
            'Đặt lịch khám',
            'Chọn thời gian phù hợp',
        ],
        [
            'patient/appointments',
            'clock',
            'Lịch hẹn của tôi',
            'Theo dõi thông tin lịch hẹn',
        ],
        [
            'patient/records',
            'clipboard',
            'Hồ sơ sức khỏe',
            'Kết quả và đơn thuốc',
        ],
    ]
        .map(([route, name, title, detail]) =>
            link(
                `<span class="quick-icon">${icon(name)}</span><span><strong>${title}</strong><small>${detail}</small></span>${icon('arrow')}`,
                route,
                '',
                'quick-item',
            ),
        )
        .join('')}</div>
    <section class="home-section"><div class="section-heading"><div><span class="eyebrow">ĐỘI NGŨ CHUYÊN MÔN</span><h2>Tìm bác sĩ phù hợp</h2></div>
        ${link('Xem tất cả bác sĩ', 'public/doctors', 'arrow', 'text-link')}</div>
        ${
            catalog.status !== 'ready'
                ? catalogStatus()
                : catalog.doctors.length
                  ? `<div class="doctor-grid">${catalog.doctors.slice(0, 4).map(doctorCard).join('')}</div>`
                  : empty('Chưa có thông tin bác sĩ')
        }
    </section>
    <section class="journey-section"><div><span class="eyebrow">CHUẨN BỊ TRƯỚC KHI ĐẾN VIỆN</span><h2>Mỗi bước đều rõ ràng.</h2>
        <p>Chuẩn bị giấy tờ, mã lịch hẹn và các kết quả khám trước đây nếu có.</p>${link('Xem hướng dẫn khám', 'public/guide', 'arrow', 'btn outline')}</div>
        <div class="journey-steps">${[
            [
                '01',
                'Thông tin người khám',
                'Đối chiếu họ tên và giấy tờ của người đến khám.',
            ],
            ['02', 'Lịch hẹn', 'Kiểm tra thông tin lịch hẹn đã được xác nhận.'],
            [
                '03',
                'Tiếp nhận tại quầy',
                'Nhân viên hướng dẫn tiếp nhận và cấp số.',
            ],
            [
                '04',
                'Theo dõi hồ sơ',
                'Xem kết quả theo quyền được hệ thống cấp.',
            ],
        ]
            .map(
                ([number, title, detail]) =>
                    `<article><span>${number}</span><div><h3>${title}</h3><p>${detail}</p></div></article>`,
            )
            .join('')}</div>
    </section>`;
}

function doctorCard(doctor) {
    return `<article class="doctor-card">
        <div class="doctor-portrait cyan"><span class="doctor-avatar">${esc(initials(doctor.name))}</span>${icon('stethoscope')}</div>
        <div class="doctor-card-body"><span class="mini-label">${esc(doctor.specialty || doctor.department)}</span>
            <h3>${esc(doctor.name)}</h3><p>${esc(doctor.department)}</p>
            ${link('Xem hồ sơ bác sĩ', 'public/doctor/' + encodeURIComponent(doctor.id), 'arrow', 'text-link')}
        </div>
    </article>`;
}
