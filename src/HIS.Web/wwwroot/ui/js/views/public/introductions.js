// Biểu tượng + chủ đề theo tên khoa; danh sách khoa và bác sĩ luôn lấy từ server.
const specialtyInfo = {
    'Nội tổng quát': ['stethoscope', ['Bệnh mạn tính', 'Tăng huyết áp, đái tháo đường', 'Khám sức khoẻ định kỳ']],
    'Ngoại tổng quát': ['shield', ['Chấn thương', 'Tư vấn phẫu thuật', 'Theo dõi sau điều trị']],
    'Tim mạch': ['heart', ['Sức khoẻ tim mạch', 'Hệ tuần hoàn', 'Theo dõi huyết áp']],
    'Nhi khoa': ['users', ['Sức khoẻ trẻ em', 'Tăng trưởng và phát triển', 'Chăm sóc theo độ tuổi']],
    'Sản phụ khoa': ['heart', ['Sức khoẻ phụ nữ', 'Chăm sóc thai kỳ', 'Khám phụ khoa định kỳ']],
    'Tai Mũi Họng': ['stethoscope', ['Tai và thính giác', 'Mũi xoang', 'Họng và thanh quản']],
    'Da liễu': ['shield', ['Sức khoẻ làn da', 'Tóc và da đầu', 'Chăm sóc da chuyên khoa']],
    Mắt: ['eye', ['Thị lực', 'Sức khoẻ đôi mắt', 'Đo khúc xạ']],
    'Răng Hàm Mặt': ['shield', ['Sức khoẻ răng miệng', 'Nướu và nha chu', 'Chỉnh nha']],
    'Phục hồi chức năng': ['pulse', ['Vận động', 'Chức năng sinh hoạt', 'Vật lý trị liệu']],
    'Thần kinh': ['pulse', ['Đau đầu, chóng mặt', 'Rối loạn giấc ngủ', 'Thần kinh ngoại biên']],
    'Tiêu hoá': ['clipboard', ['Dạ dày', 'Đại tràng', 'Gan mật']],
    'Cơ xương khớp': ['users', ['Thoái hoá khớp', 'Loãng xương', 'Cột sống']],
    'Hô hấp': ['pulse', ['Ho kéo dài', 'Hen phế quản', 'Bệnh phổi mạn tính']],
};

const specialtyIcon = (name) => (specialtyInfo[name] || ['building'])[0];

function specialtyCards() {
    if (catalog.status !== 'ready') return catalogStatus();
    if (!catalog.departments.length)
        return empty('Chưa có thông tin chuyên khoa', 'Danh mục sẽ hiển thị khi bệnh viện cập nhật.');
    return `<div class="portal-specialties specialty-introductions">${catalog.departments
        .map((department) =>
            link(
                `<span class="specialty-symbol">${icon(specialtyIcon(department.name))}</span><h3>${esc(department.name)}</h3>
                <p>${esc(department.description || 'Xem thông tin khoa và đội ngũ bác sĩ.')}</p><span class="text-link">Tìm hiểu chuyên khoa ${icon('arrow')}</span>`,
                'public/specialty/' + department.id,
                '',
                'portal-specialty',
            ),
        )
        .join('')}</div>`;
}

function specialtyIntroductionDetail(id) {
    if (catalog.status !== 'ready') return catalogStatus();
    const specialty = catalog.departments.find((item) => String(item.id) === id);
    if (!specialty) return empty('Không tìm thấy chuyên khoa');
    const topics = (specialtyInfo[specialty.name] || [, []])[1];
    const inDept = catalog.doctors.filter((doctor) => doctor.department === specialty.name);
    const doctors = inDept.some((doctor) => doctor.isPublic)
        ? inDept.filter((doctor) => doctor.isPublic)
        : inDept;
    const canBook = !config.authenticated || config.role === 'PATIENT';
    return `${pageIntro('CHUYÊN KHOA', specialty.name, specialty.description || '')}
        <section class="specialty-overview card"><span class="specialty-symbol">${icon(specialtyIcon(specialty.name))}</span>
            <div><h2>Lĩnh vực chăm sóc</h2><p>Khoa có ${inDept.length} bác sĩ đang khám tại bệnh viện.</p>
            ${topics.length ? `<ul class="specialty-topics">${topics.map((topic) => `<li>${icon('check')}${esc(topic)}</li>`).join('')}</ul>` : ''}</div>
            ${link('Tất cả chuyên khoa', 'public/specialties', 'arrow', 'btn outline')}
        </section>
        ${
            doctors.length
                ? `<section class="home-section" aria-labelledby="specialty-doctors-title">
            <div class="section-heading"><div><span class="eyebrow">ĐỘI NGŨ CHUYÊN MÔN</span>
                <h2 id="specialty-doctors-title">Bác sĩ khoa ${esc(specialty.name)}</h2></div>
                ${link('Xem tất cả bác sĩ', 'public/doctors?department=' + encodeURIComponent(specialty.name), 'arrow', 'text-link')}</div>
            <div class="doctor-grid">${doctors.map(doctorCard).join('')}</div></section>`
                : ''
        }
        ${
            canBook
                ? `<section class="specialty-next-step"><div><h2>Bạn muốn đặt lịch khám?</h2><p>Chọn khoa ${esc(specialty.name)} và khung giờ còn trống.</p></div>
            ${link('Đặt lịch khám', 'public/booking', 'calendar', 'btn primary')}</section>`
                : ''
        }`;
}
