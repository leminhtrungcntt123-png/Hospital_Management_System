// Editorial content only; booking choices always come from the server catalog.
const doctorIllustrations = {
    'BS. Nguyễn Văn Nam': 'doctor-nam.jpg',
    'BS. Trần Thị Hoa': 'doctor-hoa.jpg',
    'BS. Phạm Văn Hùng': 'doctor-hung.jpg',
    'BS. Đỗ Thị Mai': 'doctor-mai.jpg',
};

const specialtyIntroductions = [
    {
        id: 'tim-mach',
        name: 'Tim mạch',
        icon: 'heart',
        description: 'Tìm hiểu lĩnh vực chăm sóc tim và hệ tuần hoàn.',
        topics: ['Sức khoẻ tim mạch', 'Hệ tuần hoàn', 'Theo dõi chuyên khoa'],
    },
    {
        id: 'nhi-khoa',
        name: 'Nhi khoa',
        icon: 'users',
        description: 'Đồng hành cùng sức khoẻ và sự phát triển của trẻ.',
        topics: ['Sức khoẻ trẻ em', 'Tăng trưởng và phát triển', 'Chăm sóc theo độ tuổi'],
    },
    {
        id: 'san-phu-khoa',
        name: 'Sản phụ khoa',
        icon: 'heart',
        description: 'Chăm sóc sức khoẻ phụ nữ qua từng giai đoạn cuộc sống.',
        topics: ['Sức khoẻ phụ nữ', 'Chăm sóc thai kỳ', 'Theo dõi chuyên khoa'],
    },
    {
        id: 'tai-mui-hong',
        name: 'Tai Mũi Họng',
        icon: 'stethoscope',
        description: 'Giới thiệu lĩnh vực chăm sóc tai, mũi, họng và thính giác.',
        topics: ['Tai và thính giác', 'Mũi xoang', 'Họng và thanh quản'],
    },
    {
        id: 'da-lieu',
        name: 'Da liễu',
        icon: 'shield',
        description: 'Tìm hiểu nhu cầu chăm sóc sức khoẻ da, tóc và móng.',
        topics: ['Sức khoẻ làn da', 'Tóc và da đầu', 'Chăm sóc da chuyên khoa'],
    },
    {
        id: 'mat',
        name: 'Mắt',
        icon: 'eye',
        description: 'Giới thiệu lĩnh vực chăm sóc mắt và sức khoẻ thị giác.',
        topics: ['Thị lực', 'Sức khoẻ đôi mắt', 'Theo dõi chuyên khoa'],
    },
    {
        id: 'rang-ham-mat',
        name: 'Răng Hàm Mặt',
        icon: 'shield',
        description: 'Khám phá lĩnh vực chăm sóc răng miệng và hàm mặt.',
        topics: ['Sức khoẻ răng miệng', 'Nướu và nha chu', 'Chăm sóc hàm mặt'],
    },
    {
        id: 'phuc-hoi-chuc-nang',
        name: 'Phục hồi chức năng',
        icon: 'pulse',
        description: 'Hướng đến vận động và khả năng sinh hoạt trong cuộc sống.',
        topics: ['Vận động', 'Chức năng sinh hoạt', 'Phục hồi theo chuyên khoa'],
    },
];

const doctorIntroductions = [
    {
        id: 'intro-khanh',
        kind: 'illustration',
        name: 'BS. Nguyễn Minh Khánh',
        department: 'Nhi khoa',
        specialty: 'Sức khoẻ và sự phát triển của trẻ',
        specialtyId: 'nhi-khoa',
        illustration: 'doctor-khanh.jpg',
    },
    {
        id: 'intro-linh',
        kind: 'illustration',
        name: 'BS. Lê Phương Linh',
        department: 'Da liễu',
        specialty: 'Chăm sóc sức khoẻ da, tóc và móng',
        specialtyId: 'da-lieu',
        illustration: 'doctor-linh.jpg',
    },
    {
        id: 'intro-an',
        kind: 'illustration',
        name: 'BS. Trần Hoài An',
        department: 'Sản phụ khoa',
        specialty: 'Chăm sóc sức khoẻ phụ nữ',
        specialtyId: 'san-phu-khoa',
        illustration: 'doctor-an.jpg',
    },
    {
        id: 'intro-minh',
        kind: 'illustration',
        name: 'BS. Vũ Quang Minh',
        department: 'Tai Mũi Họng',
        specialty: 'Sức khoẻ tai, mũi, họng và thính giác',
        specialtyId: 'tai-mui-hong',
        illustration: 'doctor-minh.jpg',
    },
    {
        id: 'intro-tuan',
        kind: 'illustration',
        name: 'BS. Đặng Anh Tuấn',
        department: 'Tim mạch',
        specialty: 'Sức khoẻ tim và hệ tuần hoàn',
        specialtyId: 'tim-mach',
        illustration: 'doctor-tuan.jpg',
    },
    {
        id: 'intro-ngoc',
        kind: 'illustration',
        name: 'BS. Phạm Bích Ngọc',
        department: 'Mắt',
        specialty: 'Chăm sóc mắt và sức khoẻ thị giác',
        specialtyId: 'mat',
        illustration: 'doctor-ngoc.jpg',
    },
    {
        id: 'intro-bao',
        kind: 'illustration',
        name: 'BS. Hoàng Gia Bảo',
        department: 'Răng Hàm Mặt',
        specialty: 'Sức khoẻ răng miệng và hàm mặt',
        specialtyId: 'rang-ham-mat',
        illustration: 'doctor-bao.jpg',
    },
    {
        id: 'intro-thao',
        kind: 'illustration',
        name: 'BS. Đỗ Thanh Thảo',
        department: 'Phục hồi chức năng',
        specialty: 'Vận động và chức năng sinh hoạt',
        specialtyId: 'phuc-hoi-chuc-nang',
        illustration: 'doctor-thao.jpg',
    },
    {
        id: 'intro-long',
        kind: 'illustration',
        name: 'BS. Bùi Hải Long',
        department: 'Nhi khoa',
        specialty: 'Chăm sóc sức khoẻ trẻ em',
        specialtyId: 'nhi-khoa',
        illustration: 'doctor-long.jpg',
    },
    {
        id: 'intro-quynh',
        kind: 'illustration',
        name: 'BS. Võ Như Quỳnh',
        department: 'Sản phụ khoa',
        specialty: 'Sức khoẻ phụ nữ và chăm sóc thai kỳ',
        specialtyId: 'san-phu-khoa',
        illustration: 'doctor-quynh.jpg',
    },
    {
        id: 'intro-viet',
        kind: 'illustration',
        name: 'BS. Lâm Quốc Việt',
        department: 'Tai Mũi Họng',
        specialty: 'Chăm sóc tai, mũi, họng và thính giác',
        specialtyId: 'tai-mui-hong',
        illustration: 'doctor-viet.jpg',
    },
    {
        id: 'intro-hanh',
        kind: 'illustration',
        name: 'BS. Đinh Mỹ Hạnh',
        department: 'Da liễu',
        specialty: 'Sức khoẻ làn da, tóc và da đầu',
        specialtyId: 'da-lieu',
        illustration: 'doctor-hanh.jpg',
    },
];

function introductionNotice() {
    return `<div class="introduction-notice">${icon('info')}<span>Nội dung giới thiệu minh hoạ. Các chuyên khoa và hồ sơ bổ sung chưa mở đặt lịch trực tuyến.</span></div>`;
}

function specialtyIntroductionSection() {
    return `<section class="home-section introduction-section" aria-labelledby="intro-specialties-title">
        <div class="section-heading"><div><span class="eyebrow">KHÁM PHÁ LĨNH VỰC CHĂM SÓC</span>
            <h2 id="intro-specialties-title">Hiểu thêm về các chuyên khoa</h2><p>Thông tin ngắn gọn để bạn dễ tìm hiểu từng lĩnh vực.</p>
        </div><span class="catalog-badge illustration">Giới thiệu minh hoạ</span></div>
        ${introductionNotice()}
        <div class="portal-specialties specialty-introductions">${specialtyIntroductions
            .map((specialty) =>
                link(
                    `<span class="specialty-symbol">${icon(specialty.icon)}</span><h3>${esc(specialty.name)}</h3>
                <p>${esc(specialty.description)}</p><span class="text-link">Tìm hiểu chuyên khoa ${icon('arrow')}</span>`,
                    'public/specialty/' + specialty.id,
                    '',
                    'portal-specialty',
                ),
            )
            .join('')}</div>
    </section>`;
}

function doctorIntroductionSection(doctors = doctorIntroductions.slice(0, 4)) {
    if (!doctors.length) return '';
    return `<section class="home-section introduction-section" aria-labelledby="intro-doctors-title">
        <div class="section-heading"><div><span class="eyebrow">GÓC GIỚI THIỆU CHUYÊN MÔN</span>
            <h2 id="intro-doctors-title">Khám phá thêm đội ngũ bác sĩ</h2><p>Các hồ sơ mẫu giúp bạn hình dung thông tin theo từng chuyên khoa.</p>
        </div>${link('Xem tất cả bác sĩ', 'public/doctors', 'arrow', 'text-link')}</div>
        ${introductionNotice()}<div class="doctor-grid">${doctors.map(doctorCard).join('')}</div>
    </section>`;
}

function specialtyIntroductionDetail(id) {
    const specialty = specialtyIntroductions.find((item) => item.id === id);
    if (!specialty) return empty('Không tìm thấy chuyên khoa');
    const doctors = doctorIntroductions.filter((doctor) => doctor.specialtyId === id);
    return `${pageIntro('GIỚI THIỆU CHUYÊN KHOA', specialty.name, specialty.description)}
        ${introductionNotice()}
        <section class="specialty-overview card"><span class="specialty-symbol">${icon(specialty.icon)}</span>
            <div><h2>Lĩnh vực chăm sóc</h2><p>Những chủ đề thường được đề cập trong lĩnh vực ${esc(specialty.name.toLocaleLowerCase('vi'))}.</p>
            <ul class="specialty-topics">${specialty.topics.map((topic) => `<li>${icon('check')}${esc(topic)}</li>`).join('')}</ul></div>
            ${link('Tất cả chuyên khoa', 'public/specialties', 'arrow', 'btn outline')}
        </section>
        ${doctorIntroductionSection(doctors)}
        <section class="specialty-next-step"><div><h2>Bạn muốn tìm lịch khám?</h2><p>Tra cứu đội ngũ đang có trong hệ thống để xem thông tin và lịch khám thực tế.</p></div>
            ${link('Xem bác sĩ trong hệ thống', 'public/doctors', 'search', 'btn primary')}
        </section>`;
}
