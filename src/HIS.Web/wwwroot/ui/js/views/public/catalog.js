/* views / public / catalog */
function specialtiesPage() {
    return /* HTML */ `${pageIntro('CHUYÊN MÔN PHÙ HỢP VỚI NHU CẦU', 'Chuyên khoa', 'Tìm hiểu chuyên khoa và chọn lịch khám với bác sĩ phụ trách.')}
        <section class="listing-section">
            <div class="specialty-detail-grid">
                ${db.doctors
                    .map(
                        (d) =>
                            /* HTML */ `<article class="specialty-detail">
                                <span class="specialty-icon ${d.color}"
                                    >${icon(d.id === 'd2' ? 'heart' : d.id === 'd4' ? 'users' : 'stethoscope')}</span
                                >
                                <div>
                                    <span class="mini-label">${dept(d.deptId).name}</span>
                                    <h2>${d.specialty}</h2>
                                    <p>${d.focus}</p>
                                    <div class="flex">
                                        ${link('BS. ' + d.name, 'public/doctor/' + d.id, '', 'text-link')}${button('Chọn lịch khám', 'book-doctor', 'arrow', 'btn primary sm', `data-id="${d.id}"`)}
                                    </div>
                                </div>
                            </article>`
                    )
                    .join('')}
            </div>
            <div class="help-callout">
                ${icon('info')}
                <div>
                    <h3>Chưa biết nên chọn chuyên khoa nào?</h3>
                    <p>
                        Bạn có thể bắt đầu với Nội tổng quát hoặc xem thông tin bác sĩ trước khi
                        chọn lịch.
                    </p>
                </div>
                ${button('Xem lịch Nội tổng quát', 'book-doctor', 'arrow', 'btn outline', `data-id="d1"`)}
            </div>
        </section>`;
}

function doctorsPage() {
    const ds = db.doctors.filter(
        (d) =>
            d.active &&
            (ui.dept === 'ALL' || d.deptId === ui.dept) &&
            matched(d.name + ' ' + d.specialty)
    );
    return /* HTML */ `${pageIntro('ĐỘI NGŨ CHUYÊN MÔN', 'Bác sĩ của bạn', 'Xem chuyên khoa, phòng khám và chủ động chọn thời gian phù hợp.')}
        <section class="listing-section">
            <div class="listing-toolbar">
                ${searchInput('Tìm tên bác sĩ hoặc chuyên khoa…')}<select
                    data-change="dept"
                    aria-label="Lọc khoa bác sĩ"
                >
                    <option value="ALL">Tất cả khoa</option>
                    ${db.departments.map((d) => /* HTML */ `<option value="${d.id}" ${ui.dept === d.id ? 'selected' : ''}>${d.name}</option>`).join('')}</select
                ><span>${ds.length} bác sĩ</span>
            </div>
            <div class="doctor-grid">${ds.map(doctorCard).join('')}</div>
            ${!ds.length ? empty('Không tìm thấy bác sĩ', 'Thử đổi chuyên khoa hoặc từ khóa tìm kiếm.') : ''}
            <p class="fine-print">
                Danh tính và thông tin bác sĩ trong website này là dữ liệu minh họa cho đồ án.
            </p>
        </section>`;
}

function doctorDetail(id) {
    const d = doctor(id);
    if (!d) return accessDenied();
    return /* HTML */ `${pageIntro(d.specialty, 'Hồ sơ bác sĩ', 'Thông tin chuyên môn và lịch khám minh họa.')}
        <section class="detail-layout">
            <article class="card doctor-bio">
                <div class="doctor-bio-top">
                    <div class="doctor-portrait ${d.color}">
                        <span class="doctor-avatar">${initials(d.name)}</span>${icon('stethoscope')}
                    </div>
                    <div>
                        <span class="mini-label">${d.specialty}</span>
                        <h1>BS. ${d.name}</h1>
                        <p>${dept(d.deptId).name}</p>
                        <span class="pill">${icon('location')}Phòng ${room(d.roomId).number}</span>
                    </div>
                </div>
                <h2>Phạm vi thăm khám</h2>
                <p>${d.focus}</p>
                <h2>Lịch tiếp nhận</h2>
                <div class="schedule-info">
                    <span>${icon('calendar')}Thứ Hai đến Thứ Sáu</span
                    ><span>${icon('clock')}Sáng 07:30–11:30 · Chiều 13:00–16:30</span>
                </div>
                <h2>Chuẩn bị trước buổi khám</h2>
                <ul class="content-list">
                    <li>Mang giấy tờ tùy thân và mã lịch hẹn.</li>
                    <li>Chuẩn bị hồ sơ hoặc kết quả khám trước đây nếu có.</li>
                    <li>Kiểm tra quyền liên kết nếu đặt khám cho người thân.</li>
                </ul>
                <p class="fine-print">
                    Hồ sơ bác sĩ minh họa; không đại diện cho cá nhân hành nghề thực tế.
                </p>
            </article>
            <aside class="sticky-card card">
                <span class="mini-label">ĐẶT LỊCH VỚI BÁC SĨ</span>
                <h2>BS. ${d.name}</h2>
                <p>${d.specialty} · Phòng ${room(d.roomId).number}</p>
                <div class="detail-price">
                    <small>Phí khám tham khảo</small><strong>${money(d.fee)}</strong>
                </div>
                ${button('Chọn ngày & giờ khám', 'book-doctor', 'calendar', 'btn primary full', `data-id="${d.id}"`)}
                <div class="mini-checks">
                    <span>${icon('check')}Chọn người khám trước khi đặt</span
                    ><span>${icon('check')}Giữ chỗ 10 phút để xác nhận</span
                    ><span>${icon('check')}Thông báo khi lịch thay đổi</span>
                </div>
            </aside>
        </section>`;
}

function servicesPage() {
    const services = db.services.filter(
        (s) => s.active && (ui.filter === 'ALL' || s.type === ui.filter) && matched(s.name)
    );
    return /* HTML */ `${pageIntro('TÌM HIỂU TRƯỚC KHI ĐẶT KHÁM', 'Dịch vụ y tế', 'Nội dung rõ ràng, quy trình dễ theo dõi và chi phí tham khảo ngay trên từng dịch vụ.')}
        <section class="listing-section">
            <div class="catalog-layout">
                <aside class="filter-panel card">
                    <h3>${icon('more')}Lọc dịch vụ</h3>
                    ${[['ALL', 'Tất cả dịch vụ'], ...Object.entries(typeNames)].map(([v, l]) => /* HTML */ `<button class="filter-option ${ui.filter === v ? 'active' : ''}" data-act="tab" data-kind="filter" data-value="${v}">${icon(v === 'LAB_TEST' ? 'pulse' : v === 'IMAGING' ? 'file' : 'stethoscope')}<span>${l}</span><b>${db.services.filter((s) => s.active && (v === 'ALL' || s.type === v)).length}</b></button>`).join('')}
                    <div class="filter-note">
                        ${icon('info')}Xét nghiệm và chẩn đoán hình ảnh được thực hiện theo chỉ định
                        của bác sĩ.
                    </div>
                </aside>
                <div>
                    <div class="listing-toolbar">
                        ${searchInput('Tìm tên dịch vụ…')}<span>${services.length} dịch vụ</span>
                    </div>
                    <div class="service-grid two">${services.map(serviceCard).join('')}</div>
                    ${!services.length ? empty('Không tìm thấy dịch vụ') : ''}
                </div>
            </div>
        </section>`;
}

function serviceDetail(id) {
    const s = db.services.find((x) => x.id === id);
    if (!s) return accessDenied();
    return /* HTML */ `${pageIntro(s.specialty || typeNames[s.type], s.name, 'Thông tin dịch vụ và hành trình thăm khám tại MediCare.')}
        <section class="detail-layout">
            <article class="service-content">
                <div class="detail-banner">
                    ${icon(s.icon || 'stethoscope')}
                    <div>
                        <span class="eyebrow">CHỦ ĐỘNG CHĂM SÓC SỨC KHỎE</span>
                        <h2>${s.name}</h2>
                        <p>${esc(s.description)}</p>
                    </div>
                </div>
                <nav class="anchor-tabs" aria-label="Nội dung dịch vụ">
                    ${[
                        ['overview', 'Tổng quan'],
                        ['items', 'Hạng mục'],
                        ['process', 'Quy trình'],
                        ['notes', 'Lưu ý'],
                    ]
                        .map(
                            ([id, label]) =>
                                /* HTML */ `<button data-act="anchor" data-id="${id}">
                                    ${label}
                                </button>`
                        )
                        .join('')}
                </nav>
                <section id="overview" class="content-section card">
                    <span class="mini-label">TỔNG QUAN</span>
                    <h2>Hiểu rõ dịch vụ trước buổi khám</h2>
                    <p>${s.description}</p>
                    <p>
                        Bạn có thể xem thông tin bác sĩ, lựa chọn người khám và khung giờ phù hợp.
                        Các bước được liên kết với lịch hẹn và hồ sơ của đúng người được khám.
                    </p>
                </section>
                <section id="items" class="content-section card">
                    <span class="mini-label">HẠNG MỤC THỰC HIỆN</span>
                    <h2>Nội dung của dịch vụ</h2>
                    <div class="item-list">
                        ${s.items.map((item, i) => /* HTML */ `<div><span>${String(i + 1).padStart(2, '0')}</span><strong>${esc(item)}</strong>${icon('check')}</div>`).join('')}
                    </div>
                </section>
                <section id="process" class="content-section card">
                    <span class="mini-label">QUY TRÌNH</span>
                    <h2>Từng bước trong hành trình chăm sóc</h2>
                    <div class="process-list">
                        ${[
                            [
                                'Đặt lịch trực tuyến',
                                'Chọn người khám, bác sĩ và giờ còn trống. Xác nhận trong thời gian giữ chỗ.',
                            ],
                            [
                                'Tiếp nhận tại quầy',
                                'Đối chiếu thông tin, xác minh liên kết người thân và nhận số khám.',
                            ],
                            [
                                'Thăm khám & chỉ định',
                                'Nhân viên ghi sinh hiệu. Bác sĩ thăm khám và chỉ định dịch vụ nếu cần.',
                            ],
                            [
                                'Thanh toán & theo dõi',
                                'Kiểm tra chi phí, BHYT tại quầy và xem hồ sơ theo quyền được cấp.',
                            ],
                        ]
                            .map(
                                ([t, d], i) =>
                                    /* HTML */ `<article>
                                        <span>${i + 1}</span>
                                        <div>
                                            <h3>${t}</h3>
                                            <p>${d}</p>
                                        </div>
                                    </article>`
                            )
                            .join('')}
                    </div>
                </section>
                <section id="notes" class="content-section card">
                    <span class="mini-label">LƯU Ý</span>
                    <h2>Chuẩn bị cho buổi hẹn</h2>
                    <ul class="content-list">
                        <li>Check-in mở từ 60 phút trước giờ khám.</li>
                        <li>Hủy lịch trực tuyến trước giờ khám ít nhất 120 phút.</li>
                        <li>
                            Thẻ BHYT được đối chiếu theo người khám và ngày khám tại bước thu phí.
                        </li>
                        <li>
                            Chi phí trên trang là dữ liệu minh họa; chỉ định bổ sung được thể hiện
                            trong hóa đơn.
                        </li>
                    </ul>
                </section>
            </article>
            <aside class="sticky-card card">
                <span class="mini-label">CHI PHÍ MINH HỌA</span>
                <div class="detail-price"><strong>${money(s.price)}</strong></div>
                <h3>${s.name}</h3>
                <p>${typeNames[s.type]}</p>
                ${s.type === 'EXAMINATION' ? button('Chọn lịch khám', 'book-service', 'calendar', 'btn primary full', `data-id="${s.id}"`) : link('Tìm bác sĩ để được chỉ định', 'public/doctors', 'stethoscope', 'btn primary full')}
                <div class="mini-checks">
                    <span>${icon('check')}Quy trình tiếp nhận rõ ràng</span
                    ><span>${icon('check')}Chi phí gắn với từng lượt khám</span
                    ><span>${icon('check')}Hồ sơ và kết quả được liên kết</span>
                </div>
            </aside>
        </section>`;
}

function guidePage() {
    return /* HTML */ `${pageIntro('CHUẨN BỊ TỐT CHO CUỘC HẸN', 'Hướng dẫn khám bệnh', 'Từ chọn hồ sơ người khám đến theo dõi kết quả, mọi bước đều có hướng dẫn.')}
        <section class="guide-layout">
            <div class="card guide-main">
                ${[
                    [
                        '1',
                        'Đặt khám cho đúng người',
                        'Chọn bản thân hoặc hồ sơ người thân trong tài khoản. Hồ sơ chờ xác minh vẫn có thể được đặt lịch; quyền xem bệnh án chỉ được cấp sau xác minh và đồng ý phù hợp.',
                    ],
                    [
                        '2',
                        'Chọn bác sĩ và xác nhận lịch',
                        'Lịch trống hiển thị trong 14 ngày. Khung giờ được giữ 10 phút, sau đó tự hết hạn nếu chưa xác nhận. Mỗi tài khoản có tối đa 5 lịch hiệu lực.',
                    ],
                    [
                        '3',
                        'Đến quầy và nhận số',
                        'Check-in mở từ 60 phút trước hẹn. Mang giấy tờ của người khám, thẻ BHYT nếu có và mã lịch hẹn. Khách vãng lai có thể tạo lượt khám trực tiếp tại quầy.',
                    ],
                    [
                        '4',
                        'Đo sinh hiệu và vào phòng khám',
                        'Nhân viên tiếp nhận ghi nhận sinh hiệu. Theo dõi số O cho lịch trực tuyến hoặc W cho lượt tại quầy. Hàng chờ có mức ưu tiên.',
                    ],
                    [
                        '5',
                        'Thanh toán và nhận biên lai',
                        'BHYT được kiểm tra ở bước thu phí. Có thể ghi nhận tạm ứng, thanh toán một phần hoặc hoàn tiền. Biên lai điện tử chỉ thể hiện mã hẹn, ngày và số tiền.',
                    ],
                    [
                        '6',
                        'Xem hồ sơ và thay đổi lịch',
                        'Hồ sơ người lớn cần sự đồng ý để người đặt hộ được xem. Nếu bác sĩ đổi ca, tài khoản đặt lịch nhận thông báo bác sĩ thay thế hoặc yêu cầu đặt lại.',
                    ],
                ]
                    .map(
                        ([n, t, d]) =>
                            /* HTML */ `<article class="guide-step">
                                <span>${n}</span>
                                <div>
                                    <h2>${t}</h2>
                                    <p>${d}</p>
                                </div>
                            </article>`
                    )
                    .join('')}
            </div>
            <aside class="card guide-aside">
                <span class="section-icon">${icon('clipboard')}</span>
                <h2>Trước khi đến viện</h2>
                <ul class="check-list">
                    <li>Mã lịch hẹn đã xác nhận</li>
                    <li>Giấy tờ của người khám</li>
                    <li>Thẻ BHYT còn hiệu lực</li>
                    <li>Kết quả khám trước đây nếu có</li>
                </ul>
                ${link('Đặt lịch khám', 'patient/booking', 'calendar', 'btn primary full')}
                <details>
                    <summary>Có thể hủy lịch vào lúc nào?</summary>
                    <p>
                        Lịch đã xác nhận được hủy trực tuyến khi còn ít nhất 120 phút trước giờ khám
                        trong bản mẫu.
                    </p>
                </details>
                <details>
                    <summary>Tôi đặt cho người thân được không?</summary>
                    <p>
                        Có. Một tài khoản liên kết tối đa 5 hồ sơ. Quyền đặt và quyền xem bệnh án
                        được kiểm soát riêng.
                    </p>
                </details>
                <details>
                    <summary>Ca trực đêm có đặt khám không?</summary>
                    <p>
                        Ca trực đêm là lịch nhân sự. Lịch đặt khám chỉ hiển thị ca sáng và ca chiều.
                    </p>
                </details>
            </aside>
        </section>`;
}
