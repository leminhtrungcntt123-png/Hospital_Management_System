/* views / public / home */
function homePage() {
    const next =
        session.authenticated && session.role === 'PATIENT'
            ? db.appointments
                  .filter((a) => a.bookedBy === session.accountId && a.status === 'CONFIRMED')
                  .sort((a, b) => a.date.localeCompare(b.date) || a.time.localeCompare(b.time))[0]
            : null;
    return /* HTML */ `<section class="home-hero">
            <div class="hero-copy">
                <span class="eyebrow">MỘT ĐIỂM CHẠM. TRỌN HÀNH TRÌNH CHĂM SÓC.</span>
                <h1>An tâm đặt khám.<br /><em>Chủ động mỗi ngày.</em></h1>
                <p>
                    Tìm bác sĩ phù hợp, chọn lịch khám thuận tiện và theo dõi hồ sơ sức khỏe cho bạn
                    cùng người thân.
                </p>
                <div class="hero-actions">
                    ${link('Đặt lịch khám', 'patient/booking', 'calendar', 'btn primary large')}${link('Tìm bác sĩ', 'public/doctors', 'stethoscope', 'btn outline large')}
                </div>
                <div class="hero-trust">
                    <span>${icon('check')}Chọn bác sĩ & giờ khám</span
                    ><span>${icon('check')}Hồ sơ cho cả gia đình</span>
                </div>
            </div>
            <div class="hero-photo">
                <img
                    src="${window.hisUiConfig.assetBase}healthcare-hero.png"
                    alt="Ảnh minh họa bác sĩ trò chuyện với một gia đình tại phòng khám"
                />
                <div class="photo-note">${icon('heart')}Tận tâm trong từng cuộc hẹn</div>
            </div>
        </section>
        <div class="quick-access">
            ${[
                ['patient/booking', 'calendar', 'Đặt lịch khám', 'Chọn lịch phù hợp'],
                ['patient/appointments', 'clock', 'Lịch hẹn của tôi', 'Mã hẹn & trạng thái'],
                ['patient/records', 'clipboard', 'Hồ sơ sức khỏe', 'Kết quả & đơn thuốc'],
            ]
                .map(([route, ic, label, sub]) =>
                    link(
                        /* HTML */ `<span class="quick-icon">${icon(ic)}</span
                            ><span><strong>${label}</strong><small>${sub}</small></span
                            >${icon('arrow')}`,
                        route,
                        '',
                        'quick-item'
                    )
                )
                .join('')}
        </div>
        ${
            next
                ? /* HTML */ `<section class="upcoming-ribbon">
                      <div class="ribbon-icon">${icon('bell')}</div>
                      <div>
                          <strong>Bạn có lịch khám sắp tới</strong
                          ><span
                              >${esc(patient(next.patientId).name)} ·
                              ${doctor(next.doctorId).specialty} · ${next.time},
                              ${fmtDate(next.date)}</span
                          >
                      </div>
                      ${button('Xem lịch hẹn', 'appointment-detail', 'arrow', 'link-button', `data-id="${next.id}"`)}
                  </section>`
                : ''
        }
        <section class="home-section">
            <div class="section-heading">
                <div>
                    <span class="eyebrow">BẮT ĐẦU TỪ NHU CẦU CỦA BẠN</span>
                    <h2>Chăm sóc theo chuyên khoa</h2>
                    <p>Chọn lĩnh vực cần thăm khám để tìm bác sĩ và lịch còn trống.</p>
                </div>
                ${link('Tất cả chuyên khoa', 'public/specialties', 'arrow', 'text-link')}
            </div>
            <div class="specialty-grid">
                ${db.doctors.map((d) => /* HTML */ `<button class="specialty-card" data-act="specialty" data-id="${d.id}"><span class="specialty-icon ${d.color}">${icon(d.id === 'd1' ? 'stethoscope' : d.id === 'd2' ? 'heart' : d.id === 'd3' ? 'shield' : 'users')}</span><strong>${d.specialty}</strong><small>${dept(d.deptId).name}</small>${icon('arrow')}</button>`).join('')}
            </div>
        </section>
        <section class="home-section section-tint">
            <div class="section-heading">
                <div>
                    <span class="eyebrow">KẾT NỐI ĐÚNG CHUYÊN MÔN</span>
                    <h2>Chọn bác sĩ cho cuộc hẹn của bạn</h2>
                    <p>
                        Thông tin chuyên khoa, phòng khám và thời gian tiếp nhận được trình bày rõ
                        ràng.
                    </p>
                </div>
                ${link('Xem đội ngũ bác sĩ', 'public/doctors', 'arrow', 'text-link')}
            </div>
            <div class="doctor-grid">
                ${db.doctors
                    .filter((d) => d.active)
                    .map(doctorCard)
                    .join('')}
            </div>
        </section>
        <section class="home-section">
            <div class="section-heading">
                <div>
                    <span class="eyebrow">THÔNG TIN RÕ RÀNG, LỰA CHỌN DỄ DÀNG</span>
                    <h2>Dịch vụ khám dành cho bạn</h2>
                    <p>Tham khảo nội dung, chi phí mẫu và quy trình trước khi đặt lịch.</p>
                </div>
                ${link('Xem tất cả dịch vụ', 'public/services', 'arrow', 'text-link')}
            </div>
            <div class="service-grid">
                ${db.services
                    .filter((s) => s.type === 'EXAMINATION' && s.active)
                    .slice(0, 3)
                    .map(serviceCard)
                    .join('')}
            </div>
        </section>
        <section class="journey-section">
            <div>
                <span class="eyebrow">TỪ ĐẶT LỊCH ĐẾN KẾT QUẢ</span>
                <h2>Mỗi bước đều rõ ràng.</h2>
                <p>Bạn luôn biết cần chuẩn bị gì và thực hiện bước nào tiếp theo.</p>
                ${link('Xem hướng dẫn khám', 'public/guide', 'arrow', 'text-link')}
            </div>
            <div class="journey-steps">
                ${[
                    [
                        '01',
                        'Chọn người khám',
                        'Đặt cho bản thân hoặc hồ sơ người thân đã liên kết.',
                    ],
                    [
                        '02',
                        'Giữ chỗ & xác nhận',
                        'Chọn bác sĩ, khung giờ và kiểm tra lại thông tin.',
                    ],
                    ['03', 'Đến quầy tiếp nhận', 'Đối chiếu thông tin, cấp số và đo sinh hiệu.'],
                    ['04', 'Theo dõi hồ sơ', 'Xem kết quả và đơn thuốc theo quyền được cấp.'],
                ]
                    .map(
                        ([n, t, d]) =>
                            /* HTML */ `<article>
                                <span>${n}</span>
                                <div>
                                    <h3>${t}</h3>
                                    <p>${d}</p>
                                </div>
                            </article>`
                    )
                    .join('')}
            </div>
        </section>
        <section class="family-banner">
            <div>
                ${icon('users')}
                <h2>Một tài khoản, chăm sóc cho cả nhà.</h2>
                <p>
                    Quản lý tối đa 5 hồ sơ người khám. Quyền đặt lịch và quyền xem bệnh án được kiểm
                    soát riêng.
                </p>
            </div>
            ${link('Quản lý hồ sơ gia đình', 'patient/family', 'arrow', 'btn primary')}
        </section>`;
}

function doctorCard(d) {
    return /* HTML */ `<article class="doctor-card">
        <div class="doctor-portrait ${d.color}">
            <span class="doctor-avatar">${initials(d.name)}</span
            ><span class="portrait-icon">${icon('stethoscope')}</span
            ><span class="portrait-label"
                >${window.hisLive ? 'HỒ SƠ BÁC SĨ' : 'HỒ SƠ BÁC SĨ MẪU'}</span
            >
        </div>
        <div class="doctor-card-body">
            <span class="mini-label">${d.specialty}</span>
            <h3>BS. ${esc(d.name)}</h3>
            <p>${dept(d.deptId).name}</p>
            <div class="doctor-meta">
                <span>${icon('location')}Phòng ${room(d.roomId).number}</span
                ><span>${icon('calendar')}Thứ Hai – Thứ Sáu</span>
            </div>
            <div class="doctor-card-actions">
                ${link('Hồ sơ bác sĩ', 'public/doctor/' + d.id, '', 'btn ghost sm')}${button('Đặt khám', 'book-doctor', 'arrow', 'btn primary sm', `data-id="${d.id}"`)}
            </div>
        </div>
    </article>`;
}

function serviceCard(s) {
    return /* HTML */ `<article class="service-card">
        <div class="service-visual ${s.type.toLowerCase()}">
            <span>${icon(s.icon || 'file')}</span
            ><span class="service-type">${typeNames[s.type]}</span>
            <div class="service-visual-pattern"></div>
        </div>
        <div class="service-card-body">
            <span class="mini-label">${s.specialty || typeNames[s.type]}</span>
            <h3>${esc(s.name)}</h3>
            <p>${esc(s.description)}</p>
            <div class="service-price">
                <small>Chi phí tham khảo</small><strong>${money(s.price)}</strong>
            </div>
            <div class="service-card-actions">
                ${link('Xem chi tiết', 'public/service/' + s.id, '', 'btn outline')}${s.type === 'EXAMINATION' ? button('Đặt lịch', 'book-service', 'arrow', 'btn primary', `data-id="${s.id}"`) : link('Tìm hiểu quy trình', 'public/guide', 'arrow', 'text-link')}
            </div>
        </div>
    </article>`;
}

function pageIntro(kicker, title, desc) {
    return /* HTML */ `<section class="page-intro">
        <div class="breadcrumb">
            ${link('Trang chủ', 'public/home')} ${icon('chevron')}<span>${title}</span>
        </div>
        <span class="eyebrow">${kicker}</span>
        <h1>${title}</h1>
        <p>${desc}</p>
    </section>`;
}
