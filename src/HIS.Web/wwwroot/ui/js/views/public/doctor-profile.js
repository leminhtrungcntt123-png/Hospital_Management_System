// Illustrative career content, kept outside the server catalog until verified data is available.
const doctorCareerIntroductions = {
    'doctor-nam.jpg': {
        degree: 'Bác sĩ, Chuyên khoa I Nội khoa',
        years: 14,
        focus: 'Nội tổng quát',
        introduction:
            'Chú trọng tiếp cận người bệnh một cách toàn diện, kết hợp khai thác bệnh sử với theo dõi diễn biến sức khoẻ. Phong cách tư vấn rõ ràng, dành thời gian giải thích kế hoạch chăm sóc và trao đổi cùng người bệnh.',
        publications: [
            'Sổ tay thực hành: Tiếp cận người bệnh nội khoa tại phòng khám ngoại trú',
            'Bài viết chuyên môn: Kết nối thông tin trong theo dõi người bệnh đa bệnh lý',
            'Đề tài nghiên cứu: Khảo sát trải nghiệm tư vấn của người đến khám nội tổng quát',
        ],
    },
    'doctor-hoa.jpg': {
        degree: 'Thạc sĩ, Bác sĩ Nội khoa',
        years: 11,
        focus: 'Nội tim mạch',
        introduction:
            'Quan tâm đến việc đồng hành lâu dài cùng người bệnh trong theo dõi sức khoẻ tim mạch. Các buổi tư vấn hướng đến cách trình bày thông tin dễ hiểu, lắng nghe thói quen sinh hoạt và những băn khoăn của từng người.',
        publications: [
            'Chuyên đề: Giao tiếp và tư vấn trong theo dõi sức khoẻ tim mạch',
            'Bài viết chuyên môn: Hồ sơ theo dõi liên tục tại phòng khám nội tim mạch',
            'Đề tài nghiên cứu: Mức độ hài lòng của người bệnh về thông tin sau buổi khám',
        ],
    },
    'doctor-hung.jpg': {
        degree: 'Bác sĩ, Chuyên khoa II Ngoại khoa',
        years: 22,
        focus: 'Ngoại chấn thương',
        introduction:
            'Định hướng chuyên môn vào chăm sóc người bệnh ngoại chấn thương và phối hợp theo dõi sau điều trị. Đề cao việc giải thích từng bước tiếp nhận, trao đổi với gia đình và duy trì liên hệ giữa các bộ phận chăm sóc.',
        publications: [
            'Chương sách: Phối hợp chăm sóc người bệnh trong chuyên khoa ngoại chấn thương',
            'Bài viết chuyên môn: Tổ chức thông tin tái khám sau điều trị chấn thương',
            'Đề tài nghiên cứu: Cải thiện quy trình bàn giao hồ sơ tại phòng khám ngoại khoa',
        ],
    },
    'doctor-mai.jpg': {
        degree: 'Thạc sĩ, Bác sĩ Ngoại khoa',
        years: 18,
        focus: 'Ngoại tiêu hoá',
        introduction:
            'Tập trung vào lĩnh vực ngoại tiêu hoá với cách tiếp cận thận trọng và coi trọng trao đổi trước buổi khám. Quan tâm việc phối hợp giữa khám chuyên khoa, theo dõi diễn biến và hướng dẫn chuẩn bị hồ sơ cho những lần hẹn tiếp theo.',
        publications: [
            'Chuyên đề: Chuẩn bị thông tin người bệnh trong khám ngoại tiêu hoá',
            'Bài viết chuyên môn: Trao đổi giữa bác sĩ và người bệnh trước buổi khám ngoại khoa',
            'Đề tài nghiên cứu: Chuẩn hoá biểu mẫu theo dõi người bệnh ngoại tiêu hoá',
        ],
    },
    'doctor-khanh.jpg': {
        degree: 'Bác sĩ, Chuyên khoa I Nhi khoa',
        years: 9,
        focus: 'Sức khoẻ trẻ em',
        introduction:
            'Ưu tiên tạo không gian thăm khám thân thiện để trẻ và phụ huynh cảm thấy thoải mái khi trao đổi. Quan tâm việc theo dõi sự phát triển theo độ tuổi và xây dựng mối liên hệ rõ ràng giữa gia đình với đội ngũ chăm sóc.',
        publications: [
            'Sổ tay tham khảo: Giao tiếp thân thiện trong phòng khám nhi',
            'Bài viết chuyên môn: Vai trò của phụ huynh trong ghi nhận thông tin sức khoẻ của trẻ',
            'Đề tài nghiên cứu: Trải nghiệm của gia đình khi sử dụng hồ sơ sức khoẻ trẻ em',
        ],
    },
    'doctor-linh.jpg': {
        degree: 'Thạc sĩ, Bác sĩ Da liễu',
        years: 8,
        focus: 'Da liễu',
        introduction:
            'Quan tâm đến sức khoẻ làn da và trải nghiệm tư vấn riêng tư, nhẹ nhàng cho người đến khám. Chú trọng lắng nghe những thay đổi trong sinh hoạt, nhu cầu chăm sóc cá nhân và mong muốn của từng người bệnh.',
        publications: [
            'Chuyên đề: Lắng nghe nhu cầu chăm sóc cá nhân trong khám da liễu',
            'Bài viết chuyên môn: Ghi nhận diễn biến sức khoẻ làn da qua các lần tái khám',
            'Đề tài nghiên cứu: Cải thiện cách trình bày thông tin trong hồ sơ da liễu',
        ],
    },
    'doctor-an.jpg': {
        degree: 'Bác sĩ, Chuyên khoa II Sản phụ khoa',
        years: 21,
        focus: 'Sức khoẻ phụ nữ',
        introduction:
            'Đồng hành cùng sức khoẻ phụ nữ qua nhiều giai đoạn của cuộc sống, với phong cách tư vấn kín đáo và tôn trọng sự riêng tư. Đề cao việc giải thích thông tin rõ ràng để người đến khám chủ động trao đổi những điều còn băn khoăn.',
        publications: [
            'Chương sách: Giao tiếp tôn trọng và riêng tư trong chăm sóc sức khoẻ phụ nữ',
            'Bài viết chuyên môn: Xây dựng hành trình theo dõi sức khoẻ phụ khoa liên tục',
            'Đề tài nghiên cứu: Nhu cầu thông tin của phụ nữ trước buổi khám chuyên khoa',
        ],
    },
    'doctor-minh.jpg': {
        degree: 'Thạc sĩ, Bác sĩ Tai Mũi Họng',
        years: 15,
        focus: 'Tai Mũi Họng',
        introduction:
            'Tập trung vào lĩnh vực tai, mũi, họng và thính giác, kết hợp thăm khám với trao đổi về nhu cầu sinh hoạt hằng ngày. Chú trọng giải thích trình tự buổi khám và giúp người bệnh chuẩn bị đầy đủ thông tin trước các lần hẹn.',
        publications: [
            'Chuyên đề: Tổ chức buổi khám Tai Mũi Họng theo nhu cầu người bệnh',
            'Bài viết chuyên môn: Hồ sơ theo dõi thính giác trong thực hành ngoại trú',
            'Đề tài nghiên cứu: Tối ưu phiếu ghi nhận thông tin trước khám Tai Mũi Họng',
        ],
    },
    'doctor-tuan.jpg': {
        degree: 'Tiến sĩ, Bác sĩ Nội tim mạch',
        years: 28,
        focus: 'Tim mạch',
        introduction:
            'Quan tâm xây dựng quá trình chăm sóc tim mạch có sự phối hợp giữa người bệnh, gia đình và các chuyên khoa liên quan. Định hướng chuyên môn gắn với việc theo dõi liên tục, trao đổi rõ ràng và chuẩn hoá thông tin trong hồ sơ khám.',
        publications: [
            'Chương sách: Phối hợp chuyên khoa trong chăm sóc người bệnh tim mạch',
            'Bài tổng quan: Tính liên tục của thông tin trong hồ sơ khám tim mạch',
            'Đề tài nghiên cứu: Mô hình trao đổi thông tin giữa phòng khám và người bệnh',
        ],
    },
    'doctor-ngoc.jpg': {
        degree: 'Bác sĩ, Chuyên khoa I Nhãn khoa',
        years: 12,
        focus: 'Mắt và thị giác',
        introduction:
            'Chú trọng nhu cầu thị giác trong học tập, công việc và sinh hoạt của từng người đến khám. Phong cách tư vấn cẩn thận, dành thời gian giải thích nội dung thăm khám và trao đổi về quá trình theo dõi sức khoẻ đôi mắt.',
        publications: [
            'Sổ tay tham khảo: Chuẩn bị thông tin cho buổi khám mắt',
            'Bài viết chuyên môn: Giao tiếp bằng hình ảnh trong tư vấn sức khoẻ thị giác',
            'Đề tài nghiên cứu: Trải nghiệm tra cứu kết quả khám mắt của người bệnh',
        ],
    },
    'doctor-bao.jpg': {
        degree: 'Bác sĩ Răng Hàm Mặt',
        years: 6,
        focus: 'Răng Hàm Mặt',
        introduction:
            'Hướng đến trải nghiệm thăm khám răng miệng nhẹ nhàng và dễ trao đổi, đặc biệt với người còn lo lắng khi đến phòng khám. Quan tâm việc trình bày rõ từng bước tiếp nhận và ghi nhận nhu cầu chăm sóc của người bệnh.',
        publications: [
            'Chuyên đề: Xây dựng trải nghiệm thân thiện tại phòng khám Răng Hàm Mặt',
            'Bài viết chuyên môn: Ghi nhận nhu cầu chăm sóc răng miệng theo từng độ tuổi',
            'Đề tài nghiên cứu: Cải thiện biểu mẫu trao đổi trước buổi khám nha khoa',
        ],
    },
    'doctor-thao.jpg': {
        degree: 'Thạc sĩ, Bác sĩ Phục hồi chức năng',
        years: 10,
        focus: 'Phục hồi chức năng',
        introduction:
            'Quan tâm đến khả năng vận động và mức độ độc lập trong sinh hoạt của người bệnh. Đề cao sự phối hợp với gia đình và đội ngũ chăm sóc để ghi nhận những thay đổi nhỏ trong suốt quá trình theo dõi phục hồi.',
        publications: [
            'Chương sách: Ghi nhận mục tiêu sinh hoạt trong hồ sơ phục hồi chức năng',
            'Bài viết chuyên môn: Phối hợp giữa gia đình và đội ngũ chăm sóc phục hồi',
            'Đề tài nghiên cứu: Trình bày tiến trình vận động bằng nhật ký theo dõi',
        ],
    },
    'doctor-long.jpg': {
        degree: 'Bác sĩ, Chuyên khoa II Nhi khoa',
        years: 20,
        focus: 'Chăm sóc sức khoẻ trẻ em',
        introduction:
            'Dành sự quan tâm cho quá trình theo dõi sức khoẻ trẻ em và những thay đổi qua từng giai đoạn phát triển. Coi trọng việc lắng nghe phụ huynh, ghi nhận thông tin có hệ thống và tạo sự gần gũi trong mỗi lần thăm khám.',
        publications: [
            'Chuyên đề: Đồng hành cùng phụ huynh trong theo dõi sức khoẻ trẻ nhỏ',
            'Bài tổng quan: Tổ chức thông tin phát triển của trẻ qua các lần khám',
            'Đề tài nghiên cứu: Hành trình tiếp nhận của gia đình tại phòng khám nhi',
        ],
    },
    'doctor-quynh.jpg': {
        degree: 'Thạc sĩ, Bác sĩ Sản phụ khoa',
        years: 16,
        focus: 'Sản phụ khoa',
        introduction:
            'Quan tâm xây dựng mối quan hệ tin cậy để phụ nữ cảm thấy an tâm khi chia sẻ thông tin sức khoẻ. Phong cách tư vấn chú trọng sự riêng tư, giải thích dễ hiểu và phối hợp theo dõi qua các lần hẹn chuyên khoa.',
        publications: [
            'Sổ tay tham khảo: Chuẩn bị hồ sơ theo dõi sức khoẻ phụ nữ',
            'Bài viết chuyên môn: Trải nghiệm tư vấn riêng tư tại phòng khám Sản phụ khoa',
            'Đề tài nghiên cứu: Nhu cầu trao đổi của người đến khám trong từng giai đoạn cuộc sống',
        ],
    },
    'doctor-viet.jpg': {
        degree: 'Bác sĩ, Chuyên khoa I Tai Mũi Họng',
        years: 11,
        focus: 'Tai Mũi Họng',
        introduction:
            'Chú trọng cách tiếp cận gần gũi, giúp người bệnh mô tả rõ những khó chịu ảnh hưởng đến công việc và sinh hoạt. Quan tâm việc lưu trữ thông tin thăm khám dễ theo dõi và duy trì trao đổi nhất quán giữa các lần tái khám.',
        publications: [
            'Chuyên đề: Khai thác thông tin sinh hoạt trong khám Tai Mũi Họng',
            'Bài viết chuyên môn: Tính nhất quán của hồ sơ tái khám chuyên khoa',
            'Đề tài nghiên cứu: Thiết kế bảng hỏi trước khám phù hợp với người trưởng thành',
        ],
    },
    'doctor-hanh.jpg': {
        degree: 'Bác sĩ, Chuyên khoa II Da liễu',
        years: 25,
        focus: 'Da liễu',
        introduction:
            'Quan tâm đến việc đồng hành lâu dài cùng người bệnh trong theo dõi sức khoẻ da, tóc và da đầu. Phong cách tư vấn điềm tĩnh, lắng nghe trải nghiệm cá nhân và giúp người bệnh hiểu rõ nội dung hồ sơ chuyên khoa của mình.',
        publications: [
            'Chương sách: Theo dõi liên tục trong chăm sóc người bệnh da liễu',
            'Bài tổng quan: Giao tiếp và kỳ vọng cá nhân tại phòng khám da liễu',
            'Đề tài nghiên cứu: Chuẩn hoá nhật ký ghi nhận thay đổi về da và da đầu',
        ],
    },
};

function doctorCareer(doctor) {
    return (
        doctorCareerIntroductions[doctor.illustration || doctorIllustrations[doctor.name]] || null
    );
}

function doctorCareerSummary(doctor) {
    const career = doctorCareer(doctor);
    return `<li>${icon('file')}<span><span class="doctor-meta-label">Học vấn ${career ? '<small>(minh hoạ)</small>' : ''}</span>${esc(career?.degree || 'Chưa cập nhật')}</span></li>
        <li>${icon('clock')}<span><span class="doctor-meta-label">Kinh nghiệm ${career ? '<small>(minh hoạ)</small>' : ''}</span>${career ? esc(career.years) + ' năm · ' + esc(career.focus) : 'Chưa cập nhật'}</span></li>`;
}

function doctorProfile(doctor, illustrative) {
    const career = doctorCareer(doctor);
    const canBook = !illustrative && (!config.authenticated || config.role === 'PATIENT');
    const pendingCareer =
        '<p class="doctor-profile-note">Quá trình đào tạo và công tác sẽ hiển thị khi bệnh viện cung cấp thông tin.</p>';
    const training = career
        ? `<ol class="doctor-career-list"><li><h3>Bác sĩ Y khoa</h3><p>Đào tạo nền tảng y khoa và thực hành chăm sóc người bệnh.</p></li>
        <li><h3>${esc(career.degree)}</h3><p>Đào tạo chuyên sâu trong lĩnh vực ${esc(career.focus)}.</p></li></ol>`
        : pendingCareer;
    const experience = career
        ? `<ol class="doctor-career-list"><li><h3>${esc(career.years)} năm kinh nghiệm chuyên môn</h3><p>Lĩnh vực ${esc(career.focus)}.</p></li>
        <li><h3>Hoạt động chuyên môn</h3><p>Khám, theo dõi người bệnh và phối hợp chăm sóc trong phạm vi chuyên khoa ${esc(doctor.department || career.focus)}.</p></li></ol>`
        : pendingCareer;
    const publications = career
        ? `<p class="doctor-profile-note">Nội dung minh hoạ: các tên sách, bài viết và đề tài dưới đây là giả định, không phải tài liệu y khoa đã công bố.</p>
            <ul class="doctor-career-list">${career.publications.map((title) => `<li><h3>${esc(title)}</h3></li>`).join('')}</ul>`
        : '<p class="doctor-profile-note">Danh sách công bố và đề tài nghiên cứu sẽ được bổ sung khi có thông tin đã xác minh.</p>';
    return `<section class="doctor-profile-page">
        <nav class="doctor-profile-breadcrumb" aria-label="Đường dẫn hồ sơ bác sĩ">${link('Trang chủ', 'public/home')}${icon('chevron')}${link('Tìm bác sĩ', 'public/doctors')}${icon('chevron')}<span aria-current="page">${esc(doctor.name)}</span></nav>
        <section class="doctor-profile-layout">
            <aside class="doctor-profile-summary">
                <header class="doctor-profile-identity">${doctorPhoto(doctor)}<div>
                    <p class="doctor-profile-degree">${esc(career?.degree || 'Bác sĩ')} ${career ? '<span class="career-sample-label">Minh hoạ</span>' : ''}</p>
                    <h1 tabindex="-1">${esc(doctor.name)}</h1>
                    ${canBook ? link('Đăng ký khám', 'public/booking?doctorId=' + encodeURIComponent(doctor.id), 'calendar', 'btn doctor-profile-booking') : ''}
                    ${illustrative ? '<button class="btn doctor-profile-booking" type="button" disabled title="Hồ sơ minh hoạ chưa mở đặt lịch">Chưa có lịch khám</button>' : ''}
                    ${!illustrative && config.authenticated && config.role !== 'PATIENT' ? `<a class="btn outline" href="${esc(config.home)}">Về bàn làm việc</a>` : ''}
                </div></header>
                <h2>Giới thiệu ${career ? '<span class="career-sample-label">Nội dung minh hoạ</span>' : ''}</h2>
                <p>Hồ sơ chuyên môn của ${esc(doctor.name)}, lĩnh vực ${esc(doctor.specialty || doctor.department || 'chuyên môn')}.</p>
                ${career ? `<p>${esc(career.introduction)}</p><p>Hồ sơ mẫu thể hiện trình độ ${esc(career.degree)} và ${esc(career.years)} năm kinh nghiệm trong lĩnh vực ${esc(career.focus)}.</p>` : ''}
                <p class="doctor-profile-note">Ảnh, tiểu sử, học vấn, kinh nghiệm và các tên công trình đang dùng để minh hoạ, chưa phải thông tin đã xác minh.</p>
                ${illustrative ? `<span class="catalog-badge illustration">Hồ sơ minh hoạ</span><p class="doctor-profile-note">Bác sĩ này chưa có lịch khám trong hệ thống.</p>${link('Tìm hiểu chuyên khoa', 'public/specialty/' + doctor.specialtyId, 'arrow', 'text-link')}` : ''}
            </aside>
            <article class="doctor-profile-content" aria-label="Thông tin chuyên môn">
                <dl class="doctor-profile-facts">
                    <div><dt>Chức vụ</dt><dd>Bác sĩ chuyên khoa ${illustrative ? '<span class="career-sample-label">Minh hoạ</span>' : ''}</dd></div>
                    <div><dt>Chuyên khoa</dt><dd>${esc(doctor.specialty || 'Chưa cập nhật')}</dd></div>
                    <div><dt>Nơi làm việc ${illustrative ? '<span class="career-sample-label">Minh hoạ</span>' : ''}</dt><dd>Bệnh viện MediCare<br />${esc(doctor.department || 'Đơn vị đang cập nhật')}</dd></div>
                    <div><dt>Số năm kinh nghiệm ${career ? '<span class="career-sample-label">Minh hoạ</span>' : ''}</dt><dd>${career ? esc(career.years) + ' năm' : 'Chưa cập nhật'}</dd></div>
                </dl>
                ${doctorProfileSection('Quá trình đào tạo', training, Boolean(career))}
                ${doctorProfileSection('Kinh nghiệm làm việc', experience, Boolean(career))}
                ${doctorProfileSection('Sách, báo, công trình nghiên cứu', publications, Boolean(career))}
                ${doctorProfileSection('Giải thưởng và ghi nhận', '<p class="doctor-profile-note">Thông tin giải thưởng và ghi nhận đang được cập nhật.</p>')}
            </article>
        </section></section>`;
}

function doctorProfileSection(title, content, illustrative = false) {
    return `<details class="doctor-career-section" open><summary>${esc(title)}${illustrative ? '<span class="career-sample-label">Minh hoạ</span>' : ''}${icon('down')}</summary><div class="doctor-career-body">${content}</div></details>`;
}
