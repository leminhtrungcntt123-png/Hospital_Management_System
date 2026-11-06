function catalogStatus() {
    if (['idle', 'loading'].includes(catalog.status))
        return '<div class="notice" role="status">Đang tải thông tin từ bệnh viện…</div>';
    return empty(
        'Chưa tải được thông tin',
        'Vui lòng thử lại sau.',
        button('Thử lại', 'reload-catalog', 'refresh', 'outline'),
    );
}

function specialtiesPage() {
    return `${pageIntro('CHUYÊN KHOA', 'Chuyên khoa', 'Thông tin khoa phòng từ bệnh viện.')}
        <section class="listing-section">${
            catalog.status !== 'ready'
                ? catalogStatus()
                : catalog.departments.length
                  ? `<div class="specialty-detail-grid">${catalog.departments
                        .map(
                            (department) => `
                <article class="specialty-detail"><span class="specialty-icon">${icon('building')}</span>
                    <div><h2>${esc(department.name)}</h2><p>${esc(department.description)}</p>
                        ${link('Xem đội ngũ bác sĩ', 'public/doctors', 'arrow', 'text-link')}
                    </div>
                </article>`,
                        )
                        .join('')}</div>`
                  : empty('Chưa có thông tin chuyên khoa')
        }
        </section>`;
}

function doctorsPage() {
    const doctors = catalog.doctors.filter(
        (doctor) =>
            matched(doctor.name + ' ' + doctor.specialty) &&
            (!ui.department || doctor.department === ui.department),
    );
    return `${pageIntro('ĐỘI NGŨ CHUYÊN MÔN', 'Bác sĩ của bạn', 'Thông tin chuyên môn được cung cấp bởi bệnh viện.')}
        <section class="listing-section"><div class="listing-toolbar">
            <label class="search-input">${icon('search')}<input data-search value="${esc(ui.query)}" placeholder="Tìm bác sĩ hoặc chuyên khoa…" aria-label="Tìm bác sĩ hoặc chuyên khoa" /></label>
            <select data-department aria-label="Lọc khoa bác sĩ"><option value="">Tất cả khoa</option>
                ${catalog.departments.map((department) => `<option value="${esc(department.name)}" ${ui.department === department.name ? 'selected' : ''}>${esc(department.name)}</option>`).join('')}
            </select>
        </div>${
            catalog.status !== 'ready'
                ? catalogStatus()
                : doctors.length
                  ? `<div class="doctor-grid">${doctors.map(doctorCard).join('')}</div>`
                  : empty(
                        'Không tìm thấy bác sĩ',
                        'Thử đổi khoa hoặc từ khóa tìm kiếm.',
                    )
        }
        </section>`;
}

function doctorDetail(id) {
    if (catalog.status !== 'ready') return catalogStatus();
    const doctor = catalog.doctors.find((item) => String(item.id) === id);
    if (!doctor) return empty('Không tìm thấy thông tin bác sĩ');
    return `${pageIntro(doctor.specialty, doctor.name, doctor.department)}
        <section class="detail-layout"><article class="card doctor-bio">
            <div class="doctor-bio-top"><div class="doctor-portrait cyan"><span class="doctor-avatar">${esc(initials(doctor.name))}</span>${icon('stethoscope')}</div>
                <div><h2>${esc(doctor.name)}</h2><p>${esc(doctor.specialty)}</p><p>${esc(doctor.department)}</p></div>
            </div>
            <h2>Chuẩn bị trước buổi khám</h2><ul class="content-list"><li>Mang giấy tờ của người khám.</li><li>Chuẩn bị kết quả khám trước đây nếu có.</li></ul>
        </article><aside class="card sticky-card"><h2>Lịch khám</h2><p>Tra cứu lịch trong mục đặt lịch khám.</p>
            ${link('Đến trang đặt lịch', 'patient/booking', 'calendar', 'btn primary full')}
        </aside></section>`;
}

function servicesPage() {
    return `${pageIntro('DỊCH VỤ Y TẾ', 'Dịch vụ y tế', 'Thông tin dịch vụ và chi phí từ bệnh viện.')}
        <section class="listing-section">${unavailableNotice()}${pendingTable('Danh mục dịch vụ', ['Dịch vụ', 'Loại dịch vụ', 'Chi phí'])}</section>`;
}

function guidePage() {
    return `${pageIntro('HƯỚNG DẪN KHÁM', 'Chuẩn bị cho buổi khám', 'Theo dõi thông tin từ bệnh viện và hướng dẫn của nhân viên tại quầy.')}
        <section class="guide-layout"><div class="guide-steps">${[
            [
                '1',
                'Kiểm tra thông tin người khám',
                'Chuẩn bị họ tên, ngày sinh, số điện thoại và giấy tờ tùy thân phù hợp.',
            ],
            [
                '2',
                'Đối chiếu lịch hẹn',
                'Nếu đã đặt lịch, chuẩn bị mã lịch hẹn được hệ thống xác nhận.',
            ],
            [
                '3',
                'Đến quầy tiếp nhận',
                'Nhân viên xác minh thông tin và hướng dẫn phòng khám, số thứ tự.',
            ],
            [
                '4',
                'Chuẩn bị hồ sơ và BHYT',
                'Mang theo thẻ BHYT và kết quả khám trước đây nếu có.',
            ],
        ]
            .map(
                ([number, title, detail]) =>
                    `<article class="guide-step"><span>${number}</span><div><h2>${title}</h2><p>${detail}</p></div></article>`,
            )
            .join('')}
        </div><aside class="card guide-aside"><span class="section-icon">${icon('clipboard')}</span><h2>Thông tin lịch hẹn</h2>
            <p>Thời gian khám, điều kiện đổi/hủy và quyền xem hồ sơ được xác định bởi hệ thống bệnh viện.</p>
            ${link('Lịch hẹn của tôi', 'patient/appointments', 'calendar', 'btn outline full')}
        </aside></section>`;
}
