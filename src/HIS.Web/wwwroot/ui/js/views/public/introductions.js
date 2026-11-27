// Biểu tượng + chủ đề theo tên khoa; danh mục và bác sĩ kết hợp dữ liệu hệ thống và hồ sơ minh hoạ.
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

// Nội dung giới thiệu và ảnh minh hoạ theo tên khoa trong danh mục hệ thống.
const specialtyArticles = {
    'noi tong quat': {
        image: 'noi',
        alt: 'Bác sĩ nội khoa đo huyết áp cho người bệnh',
        overview:
            'Nội tổng quát là nơi bắt đầu khi người lớn cần khám sức khỏe định kỳ hoặc có triệu chứng chưa rõ nguyên nhân. Bác sĩ xem xét bệnh sử, các thuốc đang dùng và kết quả khám trước đây để xác định bước theo dõi phù hợp.',
        topics: [
            'Khám sức khỏe tổng quát cho người lớn',
            'Theo dõi bệnh mạn tính',
            'Đánh giá triệu chứng chưa xác định chuyên khoa',
        ],
        preparation:
            'Mang danh sách thuốc đang dùng, kết quả khám gần đây và ghi lại những thay đổi sức khỏe bạn muốn trao đổi.',
    },
    'ngoai tong quat': {
        image: 'ngoai',
        alt: 'Bác sĩ ngoại khoa trao đổi với người bệnh về hướng điều trị',
        overview:
            'Ngoại tổng quát đánh giá các vấn đề có thể cần can thiệp ngoại khoa. Buổi khám giúp người bệnh hiểu hướng xử trí, những thông tin cần chuẩn bị nếu có chỉ định phẫu thuật và cách theo dõi sau điều trị.',
        topics: [
            'Tư vấn và đánh giá bệnh lý ngoại khoa',
            'Đánh giá tổn thương và chấn thương',
            'Theo dõi sau phẫu thuật',
        ],
        preparation:
            'Nếu đã điều trị hoặc phẫu thuật, hãy mang giấy ra viện, kết quả chụp và ghi chú về các thuốc đang dùng.',
    },
    'tim mach': {
        image: 'tim',
        alt: 'Bác sĩ tim mạch cùng người bệnh xem bản ghi điện tâm đồ',
        overview:
            'Khoa Tim mạch tập trung vào sức khỏe của tim và hệ tuần hoàn. Bác sĩ xem xét triệu chứng, tiền sử và kết quả thăm dò trước đây để trao đổi về các yếu tố nguy cơ và kế hoạch theo dõi.',
        topics: [
            'Đánh giá sức khỏe tim và hệ tuần hoàn',
            'Theo dõi tăng huyết áp',
            'Khám và theo dõi rối loạn nhịp tim',
        ],
        preparation:
            'Nếu có, hãy mang nhật ký huyết áp, điện tâm đồ hoặc siêu âm tim trước đây và đơn thuốc hiện tại.',
    },
    'nhi khoa': {
        image: 'nhi',
        alt: 'Bác sĩ nhi khoa nghe tim phổi cho trẻ cùng với phụ huynh',
        overview:
            'Nhi khoa chăm sóc sức khỏe của trẻ theo độ tuổi, từ thăm khám khi trẻ có triệu chứng đến theo dõi tăng trưởng và phát triển. Phụ huynh có thể trao đổi về việc ăn uống, giấc ngủ và những thay đổi trong sinh hoạt của trẻ.',
        topics: [
            'Khám bệnh ở trẻ em',
            'Theo dõi tăng trưởng và phát triển',
            'Trao đổi về dinh dưỡng và lịch tiêm chủng',
        ],
        preparation:
            'Mang sổ theo dõi sức khỏe, sổ tiêm chủng và ghi lại thời điểm xuất hiện triệu chứng hoặc thuốc trẻ đã dùng.',
    },
    'san phu khoa': {
        image: 'san',
        alt: 'Nhân viên y tế thực hiện siêu âm trong một buổi khám thai',
        overview:
            'Sản phụ khoa đồng hành với phụ nữ trong chăm sóc sức khỏe sinh sản, khám phụ khoa và theo dõi thai kỳ. Buổi khám là dịp để trao đổi các thắc mắc về sức khỏe, các mốc theo dõi và lần tái khám tiếp theo.',
        topics: [
            'Khám và tư vấn sức khỏe phụ khoa',
            'Khám thai và theo dõi thai kỳ',
            'Chăm sóc sức khỏe sinh sản',
        ],
        preparation:
            'Nếu đã khám trước đó, hãy mang sổ khám thai, kết quả siêu âm hoặc các xét nghiệm liên quan.',
    },
    'tai mui hong': {
        image: 'tmh',
        alt: 'Bác sĩ Tai Mũi Họng thăm khám bằng dụng cụ soi tai',
        overview:
            'Khoa Tai Mũi Họng đánh giá các vấn đề liên quan đến tai, thính giác, mũi xoang, họng và giọng nói. Bác sĩ tìm hiểu tần suất và hoàn cảnh xuất hiện triệu chứng để định hướng thăm khám.',
        topics: [
            'Khám tai và đánh giá thính giác',
            'Khám các vấn đề về mũi xoang',
            'Khám họng, thanh quản và giọng nói',
        ],
        preparation:
            'Mang kết quả đo thính lực hoặc nội soi nếu đã thực hiện, cùng tên các thuốc đang dùng.',
    },
    'da lieu': {
        image: 'da',
        alt: 'Bác sĩ da liễu kiểm tra vùng da cẳng tay bằng dụng cụ soi da',
        overview:
            'Da liễu tiếp nhận các vấn đề về da, tóc và móng. Buổi khám tập trung vào diễn biến của triệu chứng, các sản phẩm đã sử dụng và những yếu tố ảnh hưởng đến sinh hoạt hằng ngày.',
        topics: [
            'Khám các vấn đề về da',
            'Khám tóc, da đầu và móng',
            'Tư vấn chăm sóc da theo tình trạng',
        ],
        preparation:
            'Nếu triệu chứng thay đổi theo thời điểm, bạn có thể mang ảnh đã chụp và danh sách thuốc hoặc sản phẩm bôi đã dùng.',
    },
    mat: {
        image: 'mat',
        alt: 'Bác sĩ Mắt kiểm tra mắt người bệnh bằng đèn khe',
        overview:
            'Khoa Mắt thăm khám thị lực và các vấn đề ảnh hưởng đến sức khỏe đôi mắt. Bác sĩ xem xét sự thay đổi về khả năng nhìn, nhu cầu sử dụng kính và kết quả khám trước đây để tư vấn hướng theo dõi.',
        topics: ['Kiểm tra thị lực', 'Đánh giá tật khúc xạ', 'Theo dõi sức khỏe mắt'],
        preparation: 'Mang kính đang dùng và đơn kính hoặc kết quả khám mắt gần nhất nếu có.',
    },
    'rang ham mat': {
        image: 'rang',
        alt: 'Nha sĩ chuẩn bị đèn khám trên ghế nha khoa',
        overview:
            'Răng Hàm Mặt chăm sóc sức khỏe răng, nướu và vùng hàm mặt. Người bệnh có thể đến khám khi gặp khó chịu lúc ăn nhai hoặc muốn đánh giá sức khỏe răng miệng.',
        topics: [
            'Khám răng và nướu',
            'Đánh giá vấn đề ăn nhai và vùng hàm mặt',
            'Tư vấn chỉnh nha khi phù hợp',
        ],
        preparation:
            'Nếu đã điều trị nha khoa, hãy mang phim chụp hoặc thông tin về lần điều trị gần nhất.',
    },
    'phuc hoi chuc nang': {
        image: 'phcn',
        alt: 'Kỹ thuật viên hướng dẫn người bệnh tập thăng bằng với thanh song song',
        overview:
            'Phục hồi chức năng giúp người bệnh cải thiện khả năng vận động và sinh hoạt sau chấn thương, phẫu thuật hoặc bệnh lý. Buổi đánh giá chú trọng các hoạt động người bệnh muốn thực hiện tốt hơn trong đời sống hằng ngày.',
        topics: [
            'Đánh giá khả năng vận động và thăng bằng',
            'Phục hồi sau chấn thương hoặc phẫu thuật',
            'Theo dõi mục tiêu vận động trong sinh hoạt',
        ],
        preparation:
            'Mang giấy ra viện hoặc kết quả điều trị liên quan, mặc trang phục dễ vận động và mang theo dụng cụ hỗ trợ đang dùng.',
    },
    'than kinh': {
        image: 'thankinh',
        alt: 'Bác sĩ thần kinh trao đổi về hình ảnh chụp não trên màn hình',
        overview:
            'Khoa Thần kinh đánh giá các triệu chứng như đau đầu, chóng mặt, tê bì hoặc thay đổi vận động. Thông tin về thời điểm xuất hiện và mức độ ảnh hưởng giúp bác sĩ chọn hướng thăm khám phù hợp.',
        topics: [
            'Đánh giá đau đầu và chóng mặt',
            'Khám tê bì và thay đổi vận động',
            'Theo dõi các bệnh lý thần kinh',
        ],
        preparation:
            'Ghi lại những lần xuất hiện triệu chứng. Mang kết quả chụp, đơn thuốc và hồ sơ khám trước đây nếu có.',
    },
    'tieu hoa': {
        image: 'tieuhoa',
        alt: 'Bác sĩ tiêu hoá dùng mô hình dạ dày và ruột để trao đổi với người bệnh',
        overview:
            'Khoa Tiêu hóa thăm khám các vấn đề liên quan đến dạ dày, đường ruột và gan mật. Bác sĩ tìm hiểu triệu chứng, thói quen ăn uống, bệnh sử và kết quả xét nghiệm để xác định nội dung cần theo dõi.',
        topics: [
            'Khám bệnh lý dạ dày và đường ruột',
            'Đánh giá các vấn đề về gan mật',
            'Theo dõi rối loạn tiêu hóa',
        ],
        preparation:
            'Mang kết quả nội soi, siêu âm hoặc xét nghiệm nếu đã thực hiện. Nếu có lịch làm thủ thuật, làm theo hướng dẫn của cơ sở khám.',
    },
    'co xuong khop': {
        image: 'xuongkhop',
        alt: 'Bác sĩ dùng mô hình khớp gối để giải thích cấu trúc khớp',
        overview:
            'Cơ xương khớp tập trung vào các vấn đề của xương, khớp, cơ và cột sống. Buổi khám xem xét triệu chứng khi vận động, mức độ ảnh hưởng đến sinh hoạt và tiền sử chấn thương hoặc điều trị.',
        topics: [
            'Khám đau khớp và hạn chế vận động',
            'Đánh giá sức khỏe xương',
            'Khám các vấn đề về cơ và cột sống',
        ],
        preparation:
            'Mang phim chụp hoặc kết quả đo mật độ xương nếu có và ghi lại những động tác khiến bạn khó chịu.',
    },
    'ho hap': {
        image: 'hohap',
        alt: 'Nhân viên y tế hướng dẫn người bệnh đo chức năng hô hấp',
        overview:
            'Khoa Hô hấp thăm khám các vấn đề ở đường thở và phổi. Bác sĩ xem xét diễn biến ho, khó thở hoặc các triệu chứng khác, đồng thời trao đổi về kết quả thăm dò và kế hoạch theo dõi.',
        topics: [
            'Đánh giá ho kéo dài hoặc khó thở',
            'Theo dõi bệnh đường thở mạn tính',
            'Đánh giá chức năng hô hấp khi có chỉ định',
        ],
        preparation:
            'Nếu có, hãy mang phim chụp phổi, kết quả đo hô hấp và tên hoặc vỏ thuốc hít đang sử dụng.',
    },
    'cap cuu': {
        image: 'capcuu',
        alt: 'Nhân viên cấp cứu theo dõi sinh hiệu người bệnh trên cáng',
        overview:
            'Khoa Cấp cứu tiếp nhận người bệnh có tình trạng cần đánh giá kịp thời. Đội ngũ tiếp nhận phân loại mức độ ưu tiên theo tình trạng và phối hợp với chuyên khoa liên quan khi cần.',
        topics: [
            'Tiếp nhận và phân loại theo tình trạng',
            'Đánh giá ban đầu và theo dõi sinh hiệu',
            'Phối hợp với các chuyên khoa liên quan',
        ],
        preparation:
            'Với tình huống khẩn cấp, hãy gọi cấp cứu hoặc đến cơ sở cấp cứu gần nhất. Có thể mang thông tin thuốc đang dùng nếu sẵn có.',
    },
};

const specialtyAliases = {
    'noi tong hop': 'noi tong quat',
    nhi: 'nhi khoa',
    'chan thuong chinh hinh': 'co xuong khop',
    'hoi suc cap cuu': 'cap cuu',
};

function specialtyArticle(name) {
    const key = name
        .trim()
        .toLowerCase()
        .replace(/^(khoa|phòng)\s+/, '')
        .normalize('NFD')
        .replace(/\p{M}/gu, '')
        .replace(/[\s–—-]+/g, ' ')
        .trim();
    const articleKey = Object.hasOwn(specialtyAliases, key) ? specialtyAliases[key] : key;
    return Object.hasOwn(specialtyArticles, articleKey) ? specialtyArticles[articleKey] : null;
}

function specialtyImage(article, isCover = false) {
    if (!article)
        return `<span class="specialty-media specialty-media-empty" aria-hidden="true">${icon('building')}</span>`;
    const src = esc(config.assetBase + 'specialties/' + article.image + '.jpg');
    const alt = isCover ? esc(article.alt) : '';
    return `<img class="specialty-media" src="${src}" alt="${alt}" width="960" height="640" ${isCover ? 'fetchpriority="high"' : 'loading="lazy"'} decoding="async" />`;
}

function introductionNotice() {
    return '';
}

function specialtyIntroductionSection() {
    return `<section class="home-section introduction-section" aria-labelledby="intro-specialties-title">
        <div class="section-heading"><div><span class="eyebrow">KHÁM PHÁ LĨNH VỰC CHĂM SÓC</span>
            <h2 id="intro-specialties-title">Hiểu thêm về các chuyên khoa</h2><p>Thông tin ngắn gọn để bạn dễ tìm hiểu từng lĩnh vực.</p>
        </div></div>
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
            <h2 id="intro-doctors-title">Khám phá thêm đội ngũ bác sĩ</h2><p>Thông tin đội ngũ bác sĩ theo từng chuyên khoa.</p>
        </div>${link('Xem tất cả bác sĩ', 'public/doctors', 'arrow', 'text-link')}</div>
        ${introductionNotice()}<div class="doctor-grid">${doctors.map(doctorCard).join('')}</div>
    </section>`;
}

function specialtyCards(limit = catalog.departments.length) {
    if (catalog.status !== 'ready') return catalogStatus();
    if (!catalog.departments.length)
        return empty(
            'Chưa có thông tin chuyên khoa',
            'Danh mục sẽ hiển thị khi bệnh viện cập nhật.',
        );
    return `<div class="portal-specialties specialty-introductions">${catalog.departments
        .slice(0, limit)
        .map((department) =>
            link(
                `${specialtyImage(specialtyArticle(department.name))}
                <div class="specialty-card-body"><h3>${esc(department.name)}</h3>
                ${department.description ? `<p>${esc(department.description)}</p>` : ''}
                <span class="text-link">Xem giới thiệu ${icon('arrow')}</span></div>`,
                'public/specialty/' + encodeURIComponent(department.id),
                '',
                'portal-specialty',
            ),
        )
        .join('')}</div>`;
}

function specialtyIntroductionDetail(id) {
    const introSpecialty = specialtyIntroductions.find((item) => item.id === id);
    if (introSpecialty) {
        const doctors = doctorIntroductions.filter((doctor) => doctor.specialtyId === id);
        return `${pageIntro('GIỚI THIỆU CHUYÊN KHOA', introSpecialty.name, introSpecialty.description)}
            ${introductionNotice()}
            <section class="specialty-overview card"><span class="specialty-symbol">${icon(introSpecialty.icon)}</span>
                <div><h2>Lĩnh vực chăm sóc</h2><p>Những chủ đề thường được đề cập trong lĩnh vực ${esc(introSpecialty.name.toLocaleLowerCase('vi'))}.</p>
                <ul class="specialty-topics">${introSpecialty.topics.map((topic) => `<li>${icon('check')}${esc(topic)}</li>`).join('')}</ul></div>
                ${link('Tất cả chuyên khoa', 'public/specialties', 'arrow', 'btn outline')}
            </section>
            ${doctorIntroductionSection(doctors)}
            <section class="specialty-next-step"><div><h2>Bạn muốn tìm lịch khám?</h2><p>Tra cứu đội ngũ đang có trong hệ thống để xem thông tin và lịch khám thực tế.</p></div>
                ${link('Xem bác sĩ trong hệ thống', 'public/doctors', 'search', 'btn primary')}
            </section>`;
    }
    if (catalog.status !== 'ready') return catalogStatus();
    const specialty = catalog.departments.find((item) => String(item.id) === String(id) || item.name === id);
    if (!specialty) return empty('Không tìm thấy chuyên khoa');

    const route = 'public/specialty/' + encodeURIComponent(id);
    const article = specialtyArticle(specialty.name);
    const sections = [
        ['overview', 'Tổng quan'],
        ...(article
            ? [
                  ['scope', 'Phạm vi khám'],
                  ['prepare', 'Chuẩn bị trước khi khám'],
              ]
            : []),
        ['doctors', 'Danh sách bác sĩ'],
    ];
    const requested = new URLSearchParams(ui.address.split('?')[1]).get('tab');
    const current = sections.find(([key]) => key === requested)?.[0] || 'overview';
    const doctors = catalog.doctors.filter(
        (doctor) => doctor.isPublic && doctor.department === specialty.name,
    );

    let content;
    switch (current) {
        case 'scope':
            content = `<h2>Phạm vi khám</h2><ul class="specialty-topics">${article.topics
                .map((topic) => `<li>${icon('check')}<span>${esc(topic)}</span></li>`)
                .join('')}</ul>`;
            break;
        case 'prepare':
            content = `<h2>Chuẩn bị trước khi khám</h2><p>${esc(article.preparation)}</p>
                ${link('Xem hướng dẫn khám', 'public/guide', 'arrow', 'text-link')}`;
            break;
        case 'doctors': {
            const booking = !config.authenticated || config.role === 'PATIENT'
                ? link('Đặt lịch khám', 'public/booking', 'calendar', 'btn primary')
                : '';
            content = `<h2>Danh sách bác sĩ</h2>${
                doctors.length
                    ? `<div class="doctor-grid">${doctors.map(doctorCard).join('')}</div>`
                    : empty('Chưa có hồ sơ bác sĩ công khai cho khoa này.', '', booking)
            }`;
            break;
        }
        default:
            content = `<h2>Giới thiệu ${esc(specialty.name)}</h2>
                <p>${esc(article?.overview || specialty.description || 'Thông tin giới thiệu đang được cập nhật.')}</p>
                ${
                    article
                        ? `<div class="specialty-overview-links">
                    ${link('Xem phạm vi khám', route + '?tab=scope', 'arrow', 'text-link')}
                    ${link('Chuẩn bị trước khi khám', route + '?tab=prepare', 'arrow', 'text-link')}
                </div>`
                        : ''
                }`;
    }

    return `<div class="specialty-page">
        <nav class="breadcrumb" aria-label="Đường dẫn chuyên khoa">
            ${link('Trang chủ', 'public/home')}${icon('chevron')}
            ${link('Chuyên khoa', 'public/specialties')}${icon('chevron')}
            <span aria-current="page">${esc(specialty.name)}</span>
        </nav>
        <section class="specialty-hero" aria-labelledby="specialty-title">
            <div class="specialty-hero-copy">
                <h1 id="specialty-title" tabindex="-1">${esc(specialty.name)}</h1>
                ${article && specialty.description ? `<p>${esc(specialty.description)}</p>` : ''}
                ${link('Xem bác sĩ của khoa', route + '?tab=doctors', 'users', 'btn primary')}
            </div>
            <figure class="specialty-cover">
                ${specialtyImage(article, true)}
            </figure>
        </section>
        <nav class="specialty-sections" aria-label="Nội dung chuyên khoa">
            ${sections
                .map(([key, label]) => {
                    const target = key === 'overview' ? route : route + '?tab=' + key;
                    return `<a href="${esc(routeUrl(target))}" data-route="${esc(target)}" ${key === current ? 'aria-current="page"' : ''}>${esc(label)}</a>`;
                })
                .join('')}
        </nav>
        <article class="specialty-article" tabindex="-1">${content}</article>
    </div>`;
}
