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
    return `${pageIntro('CHUYÊN KHOA', 'Chuyên khoa', 'Các chuyên khoa đang khám tại bệnh viện.')}
        <section class="listing-section">${specialtyCards()}</section>`;
}

function doctorsPage() {
    const departments = catalog.departments.map((department) => department.name);
    return `${pageIntro('ĐỘI NGŨ CHUYÊN MÔN', 'Bác sĩ của bạn', 'Tra cứu đội ngũ bác sĩ đang khám tại bệnh viện.')}
        <section class="listing-section"><div class="listing-toolbar">
            <label class="search-input">${icon('search')}<input data-search value="${esc(ui.query)}" placeholder="Tìm bác sĩ hoặc chuyên khoa…" aria-label="Tìm bác sĩ hoặc chuyên khoa" /></label>
            <select data-department aria-label="Lọc khoa bác sĩ"><option value="">Tất cả khoa</option>
                ${departments.map((department) => `<option value="${esc(department)}" ${ui.department === department ? 'selected' : ''}>${esc(department)}</option>`).join('')}
            </select>
        </div>${doctorList()}</section>`;
}

function doctorList(route = 'public/doctors') {
    const results = ['idle', 'loading'].includes(catalog.status)
        ? []
        : catalog.doctors.filter(
              (doctor) =>
                  doctor.isPublic &&
                  matched(doctor.name + ' ' + doctor.specialty + ' ' + doctor.department) &&
                  (!ui.department || doctor.department === ui.department),
          );
    const pageCount = Math.max(1, Math.ceil(results.length / 4));
    const page = Math.max(1, Math.min(ui.doctorPage, pageCount));
    const start = (page - 1) * 4;
    const visibleDoctors = results.slice(start, start + 4);
    return `<div class="doctor-list">
        ${catalog.status !== 'ready' ? catalogStatus() : ''}
        ${
            results.length
                ? `<div class="catalog-results-meta"><p>Hiển thị <strong>${start + 1}–${Math.min(start + 4, results.length)}</strong> trên <strong>${results.length}</strong> bác sĩ</p></div>
                <div class="doctor-grid">${visibleDoctors.map(doctorCard).join('')}</div>
                ${doctorPagination(page, pageCount, route)}`
                : catalog.status === 'ready'
                  ? empty('Không tìm thấy bác sĩ', 'Thử đổi khoa hoặc từ khóa tìm kiếm.')
                  : ''
        }
        </div>`;
}

function doctorPagination(page, pageCount, listRoute = 'public/doctors') {
    if (pageCount < 2) return '';
    const pageLink = (number, label, content, classes = '') => {
        const filters = new URLSearchParams({
            q: ui.query,
            department: ui.department,
            page: number,
        });
        const route = listRoute + '?' + filters.toString();
        return `<a class="catalog-page-link ${classes}" href="${esc(routeUrl(route))}" data-route="${esc(route)}"
            aria-label="${esc(label)}" ${number === page ? 'aria-current="page"' : ''}>${content}</a>`;
    };
    return `<nav class="catalog-pagination" aria-label="Phân trang danh sách bác sĩ">
        ${
            page > 1
                ? pageLink(page - 1, 'Trang trước', icon('chevron'), 'previous')
                : `<span class="catalog-page-link previous" role="link" aria-label="Trang trước" aria-disabled="true">${icon('chevron')}</span>`
        }
        ${Array.from({ length: pageCount }, (_, index) => index + 1)
            .map((number) => pageLink(number, 'Trang ' + number, number))
            .join('')}
        ${
            page < pageCount
                ? pageLink(page + 1, 'Trang sau', icon('chevron'))
                : `<span class="catalog-page-link" role="link" aria-label="Trang sau" aria-disabled="true">${icon('chevron')}</span>`
        }
    </nav>`;
}

function doctorDetail(id) {
    if (catalog.status !== 'ready') return catalogStatus();
    const doctor = catalog.doctors.find((item) => String(item.id) === id);
    if (!doctor) return empty('Không tìm thấy thông tin bác sĩ');
    return doctorProfile(doctor);
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
            ['4', 'Chuẩn bị hồ sơ và BHYT', 'Mang theo thẻ BHYT và kết quả khám trước đây nếu có.'],
        ]
            .map(
                ([number, title, detail]) =>
                    `<article class="guide-step"><span>${number}</span><div><h2>${title}</h2><p>${detail}</p></div></article>`,
            )
            .join('')}
        </div><aside class="card guide-aside"><span class="section-icon">${icon('clipboard')}</span><h2>Thông tin lịch hẹn</h2>
            <p>Thời gian khám, điều kiện đổi/hủy và quyền xem hồ sơ được xác định bởi hệ thống bệnh viện.</p>
            ${!config.authenticated || config.role === 'PATIENT' ? link('Lịch hẹn của tôi', 'patient/appointments', 'calendar', 'btn outline full') : `<a class="btn outline full" href="${esc(config.home)}">Về bàn làm việc</a>`}
        </aside></section>`;
}
