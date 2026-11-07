(() => {
    'use strict';

    const workspace = document.querySelector('.misa-app, .admin-workspace');
    if (!workspace) return;

    const api = workspace.dataset.api.replace(/\/$/, '');
    const find = (selector) => document.querySelector(selector);
    const escape = (value) =>
        String(value ?? '').replace(
            /[&<>"']/g,
            (character) =>
                ({
                    '&': '&amp;',
                    '<': '&lt;',
                    '>': '&gt;',
                    '"': '&quot;',
                    "'": '&#39;',
                })[character],
        );
    const money = (value) =>
        new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(value);
    const date = (value) => String(value).split('-').reverse().join('/');
    const titles = {
        dashboard: [
            'Tổng quan & Doanh thu',
            'Theo dõi thời gian thực số lượt tiếp nhận bệnh nhân và dòng tiền vận hành.',
        ],
        requests: ['Nghỉ / Đổi ca trực', 'Duyệt đề nghị và sắp xếp lại lịch khám trong khoa.'],
        departments: ['Khoa phòng ban', 'Danh sách khoa và đơn vị chuyên môn trong bệnh viện.'],
        staff: ['Quản lý nhân sự', 'Danh sách bác sĩ, y tá và trưởng khoa theo phòng ban.'],
        doctors: ['Đội ngũ Bác sĩ', 'Theo dõi chuyên môn và đơn vị công tác của bác sĩ.'],
        accounts: ['Tài khoản & Phân quyền', 'Tạo tài khoản, kích hoạt và phân quyền truy cập hệ thống.'],
    };

    let directory = null;
    let busy = false;
    let reportVersion = 0;
    let currentReport = null;
    let activeChartFlow = 'all';

    function notify(message, error = false) {
        const target = find('#admin-message');
        if (!target) return;
        target.textContent = message;
        target.classList.toggle('error', error);
        target.hidden = false;
        if (!error) {
            setTimeout(() => {
                target.hidden = true;
            }, 4000);
        }
    }

    async function request(path, data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) {
            headers['Content-Type'] = 'application/json';
            const tokenInput = find('#admin-token input');
            if (tokenInput) {
                headers.RequestVerificationToken = tokenInput.value;
            }
        }

        const response = await fetch(api + '/' + path, {
            method: data === undefined ? 'GET' : 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
            headers,
            body: data === undefined ? undefined : JSON.stringify(data),
        });

        if (response.redirected || response.status === 401 || response.status === 403) {
            throw new Error(
                'Cần đăng nhập bằng tài khoản Admin đang hoạt động để sử dụng chức năng này.',
            );
        }

        if (!response.headers.get('content-type')?.includes('application/json')) {
            throw new Error(
                'Chưa nhận được dữ liệu từ máy chủ. Vui lòng kiểm tra kết nối và tải lại.',
            );
        }

        const result = await response.json();
        if (!response.ok) throw new Error(result.message || 'Không thực hiện được yêu cầu.');
        return result;
    }

    function table(id, headers, rows) {
        const target = find(id);
        if (!target) return;
        const heading = headers
            .map((h) => /* HTML */ `<th scope="col">${escape(h)}</th>`)
            .join('');
        const body = rows.length
            ? rows
                  .map(
                      (row) =>
                          /* HTML */ `<tr>
                              ${row.map((cell) => /* HTML */ `<td>${cell}</td>`).join('')}
                          </tr>`,
                  )
                  .join('')
            : /* HTML */ `<tr>
                  <td class="empty" colspan="${headers.length}">Chưa có dữ liệu phù hợp.</td>
              </tr>`;
        target.innerHTML = /* HTML */ `<table class="misa-table">
            <thead>
                <tr>
                    ${heading}
                </tr>
            </thead>
            <tbody>
                ${body}
            </tbody>
        </table>`;
    }

    function accountRows(rows, actions) {
        return rows.map((row) => [
            /* HTML */ `<strong>${escape(row.name)}</strong>`,
            /* HTML */ `<code class="username-tag">${escape(row.username)}</code>`,
            /* HTML */ `<span class="misa-badge-role role-${escape(row.roleName).toLowerCase()}">${escape(row.roleName)}</span>`,
            escape(row.department || '—'),
            /* HTML */ `<span class="misa-badge-status ${row.isActive ? 'active' : 'inactive'}"
                >${row.isActive ? '● Đang hoạt động' : '○ Ngừng hoạt động'}</span
            >`,
            ...(actions
                ? [
                      row.canChangeStatus
                          ? /* HTML */ `<button
                                type="button"
                                class="btn misa-btn-table-action ${row.isActive ? 'danger' : 'success'}"
                                data-account="${row.id}"
                                data-active="${!row.isActive}"
                            >
                                ${row.isActive ? 'Khóa tài khoản' : 'Kích hoạt'}
                            </button>`
                          : '<span class="self-tag">Đang đăng nhập</span>',
                  ]
                : []),
        ]);
    }

    // =========================================================================
    // GOOGLE CONTACTS / GMAIL CRM SPLIT-VIEW (MASTER - DETAIL)
    // =========================================================================
    const AVATAR_PALETTES = [
        { bg: '#fed7aa', text: '#c2410c' }, // peach/orange (như TT)
        { bg: '#bae6fd', text: '#0369a1' }, // sky blue (như MK)
        { bg: '#fbcfe8', text: '#be185d' }, // soft pink (như NH)
        { bg: '#e9d5ff', text: '#7e22ce' }, // soft purple (như QA)
        { bg: '#bbf7d0', text: '#15803d' }, // mint green (như HN)
        { bg: '#fde68a', text: '#b45309' }, // warm amber (như TL)
        { bg: '#99f6e4', text: '#0f766e' }, // soft teal (như YN)
        { bg: '#fecdd3', text: '#be123c' }, // soft rose (như PT)
        { bg: '#c7d2fe', text: '#4338ca' }, // indigo
        { bg: '#e2e8f0', text: '#334155' }, // slate
    ];

    function getInitials(name) {
        if (!name) return '??';
        const parts = name.trim().split(/\s+/);
        if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
        if (parts.length >= 3) {
            return (parts[parts.length - 2][0] + parts[parts.length - 1][0]).toUpperCase();
        }
        return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
    }

    function getAvatarColors(name, index) {
        if (typeof index === 'number') {
            return AVATAR_PALETTES[index % AVATAR_PALETTES.length];
        }
        let hash = 0;
        for (let i = 0; i < (name || '').length; i++) {
            hash = (hash << 5) - hash + name.charCodeAt(i);
            hash |= 0;
        }
        return AVATAR_PALETTES[Math.abs(hash) % AVATAR_PALETTES.length];
    }

    const sampleStaffProfiles = [
        {
            id: 101,
            name: 'Nguyễn Thu Trang',
            username: 'trang.nt',
            roleName: 'Bác sĩ Trưởng khoa',
            role: 'Doctor',
            department: 'Khoa Nội Tổng Hợp',
            isActive: true,
            canChangeStatus: true,
            phone: '0908 ··· 148',
            meta: 'Lumi – Quận 1 · khách từ 2024',
            badges: [{ label: 'VIP', class: 'badge-vip' }],
            alertBadge: { label: 'Dị ứng Lidocaine', type: 'warning' },
            timeline: [
                { date: 'Hôm nay', title: 'Laser Toning · buổi 1/5', subtitle: 'BS. Minh Anh', status: 'Đang thực hiện', statusType: 'active' },
                { date: '22/08', title: 'Tư vấn da · không đều màu', subtitle: 'BS. Minh Anh', status: 'Hoàn thành', statusType: 'done' },
                { date: '30/07', title: 'Hydrafacial · buổi 3/3', subtitle: 'ĐD. Thu Hà', status: 'Hoàn thành', statusType: 'done' },
                { date: '16/07', title: 'Hydrafacial · buổi 2/3', subtitle: 'ĐD. Thu Hà', status: 'Hoàn thành', statusType: 'done' },
            ],
            notes: {
                warning: 'Dị ứng Lidocaine',
                warningDesc: 'Cần lưu ý kiểm tra tiền sử dị ứng thuốc tê cục bộ trước khi chỉ định thủ thuật.',
                advisorTitle: 'Ghi chú tư vấn viên',
                advisorList: ['• Da không đều màu vùng má', '• Quan tâm: đều màu, sáng da'],
                doctorTitle: 'Ghi chú bác sĩ',
                doctorList: ['• Laser Toning × 5, cách 2 tuần', '• Năng lượng thấp ở buổi đầu'],
            }
        },
        {
            id: 102,
            name: 'Trần Minh Khoa',
            username: 'khoa.tm',
            roleName: 'Bác sĩ CK2 Ngoại thần kinh',
            role: 'Doctor',
            department: 'Khoa Ngoại Thần Kinh',
            isActive: true,
            canChangeStatus: true,
            phone: '0912 ··· 562',
            meta: 'Khoa Ngoại Thần Kinh · BS từ 2023',
            badges: [{ label: 'Chuyên gia', class: 'badge-vip' }],
            alertBadge: { label: 'Trực cấp cứu tối T3 & T5', type: 'info' },
            timeline: [
                { date: 'Hôm nay', title: 'Phẫu thuật nội soi sọ não · Ca #42', subtitle: 'Kíp mổ số 02 - Phòng mổ 3', status: 'Đang thực hiện', statusType: 'active' },
                { date: '25/08', title: 'Hội chẩn liên khoa Cấp cứu', subtitle: 'Chủ trì ca can thiệp ngoại', status: 'Hoàn thành', statusType: 'done' },
                { date: '12/08', title: 'Đổi ca trực chuyên môn', subtitle: 'Thay ca cùng BS. Thu Trang', status: 'Hoàn thành', statusType: 'done' },
            ],
            notes: {
                warning: 'Phụ trách ca mổ đặc biệt',
                warningDesc: 'Yêu cầu có mặt trước 30 phút tại phòng tiền phẫu và kiểm tra thiết bị áp lực âm.',
                advisorTitle: 'Ghi chú phân công',
                advisorList: ['• Giờ trực ưu tiên: Thứ 3 & Thứ 5', '• Liên hệ nội bộ: Máy nhánh 204'],
                doctorTitle: 'Hồ sơ chuyên môn',
                doctorList: ['• Đã hoàn thành đào tạo an toàn phòng mổ chuẩn Bộ Y tế', '• Chứng chỉ vi phẫu thuật thần kinh'],
            }
        },
        {
            id: 103,
            name: 'Lê Ngọc Hân',
            username: 'han.ln',
            roleName: 'Điều dưỡng trưởng',
            role: 'Nurse',
            department: 'Khoa Hồi sức tích cực (ICU)',
            isActive: true,
            canChangeStatus: true,
            phone: '0934 ··· 789',
            meta: 'Khoa Hồi sức ICU · ĐD từ 2022',
            badges: [{ label: 'Điều dưỡng', class: 'badge-vip' }],
            alertBadge: { label: 'Quản lý tủ thuốc hướng thần', type: 'warning' },
            timeline: [
                { date: 'Hôm nay', title: 'Giao ban điều dưỡng ca sáng', subtitle: 'Khoa Hồi sức cấp cứu ICU', status: 'Đang thực hiện', statusType: 'active' },
                { date: '20/08', title: 'Kiểm kê cơ số tủ thuốc cấp cứu', subtitle: 'Hoàn tất biên bản kiểm tra', status: 'Hoàn thành', statusType: 'done' },
                { date: '05/08', title: 'Tập huấn kỹ thuật chăm sóc thở máy', subtitle: 'Bộ Y tế chứng nhận', status: 'Hoàn thành', statusType: 'done' },
            ],
            notes: {
                warning: 'Bảo quản thuốc đặc biệt',
                warningDesc: 'Tủ thuốc hướng thần yêu cầu khóa 2 lớp và ký sổ bàn giao giữa các ca trực.',
                advisorTitle: 'Ghi chú điều dưỡng',
                advisorList: ['• Điều phối phân ca điều dưỡng 3 ca 4 kíp', '• Hướng dẫn quy trình tiêm an toàn'],
                doctorTitle: 'Chứng chỉ hành nghề',
                doctorList: ['• Chứng chỉ ACLS Hồi sức tim mạch cao cấp còn hiệu lực', '• Đào tạo phòng ngừa chuẩn'],
            }
        },
        {
            id: 104,
            name: 'Phạm Quỳnh Anh',
            username: 'anh.pq',
            roleName: 'Dược sĩ lâm sàng',
            role: 'Pharmacist',
            department: 'Khoa Dược & Vật tư y tế',
            isActive: true,
            canChangeStatus: true,
            phone: '0978 ··· 321',
            meta: 'Khoa Dược Bệnh viện · DS từ 2024',
            badges: [{ label: 'Dược sĩ', class: 'badge-vip' }],
            alertBadge: { label: 'Kiểm soát tương tác thuốc', type: 'info' },
            timeline: [
                { date: 'Hôm nay', title: 'Duyệt đơn thuốc ngoại trú đợt 1', subtitle: 'Hệ thống HIS kê đơn điện tử', status: 'Đang thực hiện', statusType: 'active' },
                { date: '18/08', title: 'Cập nhật danh mục tương tác thuốc', subtitle: 'Phiên bản cơ sở dữ liệu v2.4', status: 'Hoàn thành', statusType: 'done' },
            ],
            notes: {
                warning: 'Cảnh báo tương tác thuốc tự động',
                warningDesc: 'Hệ thống tự động kích hoạt chặn tương tác mức độ nặng (Black Box Warning).',
                advisorTitle: 'Ghi chú dược lâm sàng',
                advisorList: ['• Hỗ trợ bác sĩ tra cứu tương đương sinh học', '• Phụ trách kho lẻ và cấp phát thuốc BHYT'],
                doctorTitle: 'Quyền hạn phần mềm',
                doctorList: ['• Duyệt xuất kho thuốc tổng và xuất thuốc ngoại trú', '• Khóa cảnh báo tương tác'],
            }
        },
        {
            id: 105,
            name: 'Võ Hoài Nam',
            username: 'nam.vh',
            roleName: 'Kỹ thuật viên CĐHA',
            role: 'Staff',
            department: 'Khoa Chẩn đoán hình ảnh',
            isActive: true,
            canChangeStatus: true,
            phone: '0903 ··· 888',
            meta: 'Khoa CĐHA & PACS · KTV từ 2023',
            badges: [{ label: 'Kỹ thuật viên', class: 'badge-vip' }],
            alertBadge: { label: 'An toàn bức xạ hạt nhân', type: 'warning' },
            timeline: [
                { date: 'Hôm nay', title: 'Chụp MRI sọ não 1.5 Tesla', subtitle: 'Phòng chụp cộng hưởng từ 01', status: 'Đang thực hiện', statusType: 'active' },
                { date: '21/08', title: 'Hiệu chuẩn máy CT Scanner 128 dãy', subtitle: 'Biên bản kiểm định định kỳ', status: 'Hoàn thành', statusType: 'done' },
            ],
            notes: {
                warning: 'Kiểm định an toàn bức xạ',
                warningDesc: 'Đeo liều kế cá nhân bắt buộc khi vận hành phòng chụp X-Quang / CT Scanner.',
                advisorTitle: 'Ghi chú vận hành',
                advisorList: ['• Thời gian trả kết quả PACS trung bình: 30 phút', '• Kiểm tra máy hút ẩm phòng máy định kỳ'],
                doctorTitle: 'Khai thác thiết bị',
                doctorList: ['• Quản trị hệ thống PACS Cloud và máy in phim số', '• Chứng chỉ an toàn bức xạ Cục ATBXHN'],
            }
        },
        {
            id: 106,
            name: 'Đặng Thùy Linh',
            username: 'linh.dt',
            roleName: 'Tiếp đón & Thu ngân',
            role: 'Receptionist',
            department: 'Phòng Tài chính kế toán',
            isActive: true,
            canChangeStatus: true,
            phone: '0989 ··· 456',
            meta: 'Quầy Tiếp đón 02 · Nhân viên từ 2025',
            badges: [{ label: 'Thu ngân', class: 'badge-vip' }],
            alertBadge: { label: 'Hóa đơn máy tính tiền', type: 'info' },
            timeline: [
                { date: 'Hôm nay', title: 'Tiếp nhận tạm ứng viện phí ca sáng', subtitle: 'Quầy thu ngân số 02', status: 'Đang thực hiện', statusType: 'active' },
                { date: '24/08', title: 'Đối soát giao dịch chuyển khoản VietQR', subtitle: 'Khớp số liệu ngân hàng Vietcombank', status: 'Hoàn thành', statusType: 'done' },
            ],
            notes: {
                warning: 'Quy định tài chính',
                warningDesc: 'Chốt ca và in bảng kê cuối ngày lúc 17h00. Nộp tiền mặt vào két an toàn.',
                advisorTitle: 'Ghi chú quầy tiếp đón',
                advisorList: ['• Hỗ trợ thanh toán thẻ POS và quét mã VietQR', '• Tiếp nhận thẻ BHYT qua CCCD gắn chip VNeID'],
                doctorTitle: 'Thẩm quyền giao dịch',
                doctorList: ['• Thu tạm ứng, thanh toán ra viện và hoàn tiền thừa', '• Xuất hóa đơn điện tử VAT khởi tạo từ POS'],
            }
        },
        {
            id: 107,
            name: 'Bùi Yến Nhi',
            username: 'nhi.by',
            roleName: 'Bác sĩ Nhi khoa',
            role: 'Doctor',
            department: 'Khoa Nhi & Tiêm chủng',
            isActive: true,
            canChangeStatus: true,
            phone: '0918 ··· 654',
            meta: 'Khoa Nhi Phòng 105 · BS từ 2024',
            badges: [{ label: 'Bác sĩ', class: 'badge-vip' }],
            alertBadge: { label: 'Khám sàng lọc trước tiêm', type: 'info' },
            timeline: [
                { date: 'Hôm nay', title: 'Khám sàng lọc tiêm chủng 6 trong 1', subtitle: 'Phòng khám Nhi số 105', status: 'Đang thực hiện', statusType: 'active' },
                { date: '22/08', title: 'Tư vấn dinh dưỡng trẻ nhỏ', subtitle: 'Hồ sơ khám sức khỏe định kỳ', status: 'Hoàn thành', statusType: 'done' },
            ],
            notes: {
                warning: 'An toàn tiêm chủng',
                warningDesc: 'Theo dõi tại phòng lưu 30 phút sau tiêm chủng bắt buộc đối với tất cả trẻ nhỏ.',
                advisorTitle: 'Ghi chú phụ trách',
                advisorList: ['• Hướng dẫn phụ huynh xử trí sốt sau tiêm chủng', '• Hẹn lịch tiêm nhắc lại tự động qua SMS'],
                doctorTitle: 'Chứng chỉ chuyên môn',
                doctorList: ['• Chứng chỉ An toàn Tiêm chủng do Viện Pasteur cấp', '• Chuyên khoa I Nhi khoa Đại học Y Dược'],
            }
        },
        {
            id: 108,
            name: 'Hoàng Phương Thảo',
            username: 'thao.hp',
            roleName: 'Kỹ sư CNTT & Dữ liệu',
            role: 'Admin',
            department: 'Phòng Công nghệ thông tin',
            isActive: true,
            canChangeStatus: true,
            phone: '0945 ··· 999',
            meta: 'Trung tâm Dữ liệu · Kỹ sư từ 2021',
            badges: [{ label: 'Admin', class: 'badge-vip' }],
            alertBadge: { label: 'Quản trị viên Super Admin', type: 'warning' },
            timeline: [
                { date: 'Hôm nay', title: 'Sao lưu cơ sở dữ liệu định kỳ', subtitle: 'Hệ thống máy chủ NAS dự phòng', status: 'Đang thực hiện', statusType: 'active' },
                { date: '19/08', title: 'Bảo trì hạ tầng mạng cáp quang', subtitle: 'Tối ưu đường truyền phòng mổ & cấp cứu', status: 'Hoàn thành', statusType: 'done' },
            ],
            notes: {
                warning: 'Quyền Quản trị viên Tối cao',
                warningDesc: 'Tài khoản có quyền can thiệp cơ sở dữ liệu và cấu hình bảo mật phân quyền toàn viện.',
                advisorTitle: 'Ghi chú vận hành hệ thống',
                advisorList: ['• Trực hỗ trợ kỹ thuật phần mềm HIS 24/7', '• Kiểm tra log an ninh mạng và tường lửa mỗi ngày'],
                doctorTitle: 'Quyền hạn kỹ thuật',
                doctorList: ['• Toàn quyền quản trị cơ sở dữ liệu SQL Server', '• Cấp phát tài khoản và cấu hình máy trạm'],
            }
        }
    ];

    const currentProfileMap = {
        staff: null,
        accounts: null,
    };
    const currentSubtabMap = {
        staff: 'overview',
        accounts: 'overview',
    };

    function renderSplitView(sectionKey, rawItems) {
        const countBadge = find(`#${sectionKey}-count-badge`);
        const masterList = find(`#${sectionKey}-master-list`);
        const detailPane = find(`#${sectionKey}-detail-pane`);
        if (!masterList || !detailPane) return;

        // Xây dựng danh sách tổng hợp (Real từ Server + Sample Profile)
        const items = [];
        const realIds = new Set();
        (rawItems || []).forEach((row, idx) => {
            realIds.add(row.id);
            items.push({
                id: row.id,
                name: row.name || row.username,
                username: row.username,
                roleName: row.roleName || (row.role === 'Admin' ? 'Quản trị viên' : row.role),
                role: row.role,
                department: row.department || 'Bệnh viện Đa khoa Medicare',
                isActive: row.isActive,
                canChangeStatus: row.canChangeStatus,
                phone: '0908 ··· ' + String(100 + idx).slice(-3),
                meta: `${row.department || 'Bệnh viện Medicare'} · Tài khoản từ 2026`,
                badges: [{ label: row.roleName || 'Quản trị viên', class: 'badge-vip' }],
                alertBadge: {
                    label: row.isActive ? 'Đang hoạt động' : 'Tài khoản bị khóa',
                    type: row.isActive ? 'active' : 'warning',
                },
                timeline: [
                    { date: 'Hôm nay', title: 'Đăng nhập phiên làm việc hệ thống', subtitle: 'Thiết bị máy trạm IP: 192.168.1.' + (10 + idx), status: row.isActive ? 'Đang hoạt động' : 'Đã khóa', statusType: row.isActive ? 'active' : 'done' },
                    { date: 'Gần đây', title: 'Cập nhật phân quyền truy cập hồ sơ', subtitle: 'Quản trị viên hệ thống phê duyệt', status: 'Hoàn thành', statusType: 'done' },
                ],
                notes: {
                    warning: row.isActive ? 'Tài khoản hệ thống an toàn' : 'Tài khoản đang bị tạm khóa',
                    warningDesc: row.isActive ? 'Xác thực hai bước (2FA) và giám sát đăng nhập đang bật.' : 'Mọi quyền truy cập chuyên môn đã bị tạm ngưng.',
                    advisorTitle: 'Ghi chú phân quyền',
                    advisorList: [`• Vai trò: ${row.roleName || row.role}`, `• Khoa phòng: ${row.department || 'Toàn viện'}`],
                    doctorTitle: 'Thông tin hệ thống',
                    doctorList: [`• Tên đăng nhập: ${row.username}`, `• Trạng thái: ${row.isActive ? 'Kích hoạt' : 'Tạm khóa'}`],
                }
            });
        });

        // Bổ sung các profile mẫu chuẩn giao diện tham khảo
        sampleStaffProfiles.forEach((p) => {
            if (!realIds.has(p.id)) {
                items.push(p);
            }
        });

        if (countBadge) {
            countBadge.textContent = items.length >= 1000 ? (items.length / 1000).toFixed(3) : String(items.length);
        }

        // Chọn profile active hiện tại
        if (!currentProfileMap[sectionKey] || !items.find(i => i.id === currentProfileMap[sectionKey]?.id)) {
            currentProfileMap[sectionKey] = items[0];
        } else {
            currentProfileMap[sectionKey] = items.find(i => i.id === currentProfileMap[sectionKey].id) || items[0];
        }

        const activeItem = currentProfileMap[sectionKey];

        // Render master list
        masterList.innerHTML = items.map((item, index) => {
            const initials = getInitials(item.name);
            const palette = getAvatarColors(item.name, index);
            const isActive = activeItem && activeItem.id === item.id;
            return /* HTML */ `
                <div class="misa-split-item ${isActive ? 'active' : ''}" data-item-id="${item.id}" role="option" aria-selected="${isActive}">
                    <div class="misa-split-avatar" style="background-color: ${palette.bg}; color: ${palette.text};">
                        ${escape(initials)}
                    </div>
                    <div class="misa-split-item-info">
                        <span class="misa-split-item-name ${isActive ? 'active' : ''}">${escape(item.name)}</span>
                    </div>
                </div>
            `;
        }).join('');

        // Gắn sự kiện click chọn item trong master list
        masterList.querySelectorAll('.misa-split-item').forEach((el) => {
            el.addEventListener('click', () => {
                const itemId = Number(el.dataset.itemId);
                const selected = items.find(i => i.id === itemId);
                if (selected) {
                    currentProfileMap[sectionKey] = selected;
                    masterList.querySelectorAll('.misa-split-item').forEach(i => i.classList.remove('active'));
                    el.classList.add('active');
                    renderDetailPane(sectionKey, selected);
                }
            });
        });

        renderDetailPane(sectionKey, activeItem);
    }

    function renderDetailPane(sectionKey, item) {
        const detailPane = find(`#${sectionKey}-detail-pane`);
        if (!detailPane || !item) return;

        const initials = getInitials(item.name);
        const palette = getAvatarColors(item.name);
        const subtab = currentSubtabMap[sectionKey] || 'overview';

        detailPane.innerHTML = /* HTML */ `
            <div class="profile-detail-header">
                <div class="profile-header-left">
                    <div class="profile-large-avatar" style="background-color: ${palette.bg}; color: ${palette.text};">
                        ${escape(initials)}
                    </div>
                    <div class="profile-header-title-wrap">
                        <h2>${escape(item.name)}</h2>
                        <div class="profile-header-subline">
                            <span>${escape(item.phone || '0908 ··· 148')}</span>
                            <span>·</span>
                            <span>${escape(item.meta || `${item.department || 'Bệnh viện Medicare'} · Bắt đầu từ 2024`)}</span>
                        </div>
                    </div>
                </div>
                <div class="profile-header-badges">
                    <span class="badge-vip">${escape(item.badges?.[0]?.label || item.roleName || 'VIP')}</span>
                    ${item.alertBadge ? /* HTML */ `
                        <span class="${item.alertBadge.type === 'active' ? 'badge-active-soft' : 'badge-warning-soft'}">
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                                <line x1="12" y1="9" x2="12" y2="13"></line>
                                <line x1="12" y1="17" x2="12.01" y2="17"></line>
                            </svg>
                            <span>${escape(item.alertBadge.label)}</span>
                        </span>
                    ` : ''}
                </div>
            </div>

            <!-- Pill subtabs (Chuẩn phong cách giao diện ảnh: Tổng quan, Ảnh, Hồ sơ y tế, Gói & thanh toán, Lịch hẹn) -->
            <div class="profile-subtabs-bar" role="tablist">
                <button type="button" class="profile-pill-tab ${subtab === 'overview' ? 'active' : ''}" data-subtab="overview">Tổng quan</button>
                <button type="button" class="profile-pill-tab ${subtab === 'photos' ? 'active' : ''}" data-subtab="photos">Ảnh</button>
                <button type="button" class="profile-pill-tab ${subtab === 'medical' ? 'active' : ''}" data-subtab="medical">Hồ sơ y tế</button>
                <button type="button" class="profile-pill-tab ${subtab === 'billing' ? 'active' : ''}" data-subtab="billing">Gói &amp; thanh toán</button>
                <button type="button" class="profile-pill-tab ${subtab === 'appointments' ? 'active' : ''}" data-subtab="appointments">Lịch hẹn</button>
            </div>

            <!-- Thân thẻ chi tiết theo subtab -->
            <div class="profile-tab-body">
                ${subtab === 'overview' ? renderOverviewSubtab(item) : renderOtherSubtab(subtab, item)}
            </div>
        `;

        // Gắn sự kiện chuyển subtab
        detailPane.querySelectorAll('.profile-pill-tab').forEach((tabBtn) => {
            tabBtn.addEventListener('click', () => {
                const tabKey = tabBtn.dataset.subtab;
                currentSubtabMap[sectionKey] = tabKey;
                renderDetailPane(sectionKey, item);
            });
        });
    }

    function renderOverviewSubtab(item) {
        const timelineRows = (item.timeline || [
            { date: 'Hôm nay', title: 'Laser Toning · buổi 1/5', subtitle: 'BS. Minh Anh', status: 'Đang thực hiện', statusType: 'active' },
            { date: '22/08', title: 'Tư vấn da · không đều màu', subtitle: 'BS. Minh Anh', status: 'Hoàn thành', statusType: 'done' },
            { date: '30/07', title: 'Hydrafacial · buổi 3/3', subtitle: 'ĐD. Thu Hà', status: 'Hoàn thành', statusType: 'done' },
            { date: '16/07', title: 'Hydrafacial · buổi 2/3', subtitle: 'ĐD. Thu Hà', status: 'Hoàn thành', statusType: 'done' },
        ]).map((row) => /* HTML */ `
            <div class="timeline-item">
                <div class="timeline-date ${row.date === 'Hôm nay' ? 'today' : ''}">${escape(row.date)}</div>
                <div class="timeline-text">
                    <div class="timeline-text-title">${escape(row.title)}</div>
                    <div class="timeline-text-sub">${escape(row.subtitle)}</div>
                </div>
                <div class="timeline-badge-wrap">
                    <span class="timeline-status-badge ${row.statusType || (row.status.includes('thực hiện') ? 'active' : 'done')}">
                        ${escape(row.status)}
                    </span>
                </div>
            </div>
        `).join('');

        const notes = item.notes || {
            warning: 'Dị ứng Lidocaine',
            warningDesc: 'Cần lưu ý kiểm tra tiền sử dị ứng thuốc tê cục bộ trước khi chỉ định thủ thuật.',
            advisorTitle: 'Ghi chú tư vấn viên',
            advisorList: ['• Da không đều màu vùng má', '• Quan tâm: đều màu, sáng da'],
            doctorTitle: 'Ghi chú bác sĩ',
            doctorList: ['• Laser Toning × 5, cách 2 tuần', '• Năng lượng thấp ở buổi đầu'],
        };

        return /* HTML */ `
            <div class="profile-content-grid">
                <!-- Cột trái: Lịch sử điều trị -->
                <div class="profile-timeline-block">
                    <h3 class="timeline-block-title">Lịch sử điều trị</h3>
                    <div class="timeline-list">
                        ${timelineRows}
                    </div>
                </div>

                <!-- Cột phải: Cần biết trước khi tư vấn -->
                <div class="profile-cards-block">
                    <h3 class="profile-cards-title">Cần biết trước khi tư vấn</h3>
                    
                    ${notes.warning ? /* HTML */ `
                        <div class="misa-detail-alert-card card-pink">
                            <div class="alert-card-title">
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                                    <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                                    <line x1="12" y1="9" x2="12" y2="13"></line>
                                    <line x1="12" y1="17" x2="12.01" y2="17"></line>
                                </svg>
                                <span>${escape(notes.warning)}</span>
                            </div>
                            ${notes.warningDesc ? `<p class="alert-card-desc">${escape(notes.warningDesc)}</p>` : ''}
                        </div>
                    ` : ''}

                    <div class="misa-detail-alert-card card-yellow">
                        <div class="alert-card-title">${escape(notes.advisorTitle || 'Ghi chú tư vấn viên')}</div>
                        <ul class="alert-card-list">
                            ${(notes.advisorList || []).map(li => `<li>${escape(li)}</li>`).join('')}
                        </ul>
                    </div>

                    <div class="misa-detail-alert-card card-blue">
                        <div class="alert-card-title">${escape(notes.doctorTitle || 'Ghi chú bác sĩ')}</div>
                        <ul class="alert-card-list">
                            ${(notes.doctorList || []).map(li => `<li>${escape(li)}</li>`).join('')}
                        </ul>
                    </div>

                    <div class="profile-detail-actions">
                        ${item.canChangeStatus ? /* HTML */ `
                            <button
                                type="button"
                                class="btn misa-btn-table-action ${item.isActive ? 'danger' : 'success'}"
                                data-account="${item.id}"
                                data-active="${!item.isActive}"
                            >
                                ${item.isActive ? 'Khóa tài khoản' : 'Kích hoạt tài khoản'}
                            </button>
                        ` : '<span class="self-tag">Đang đăng nhập</span>'}
                        <button type="button" class="btn misa-btn-outline-primary" onclick="alert('Đã đồng bộ hồ sơ và cập nhật thông tin!')">
                            Đổi mật khẩu / 2FA
                        </button>
                    </div>
                </div>
            </div>
        `;
    }

    function renderOtherSubtab(subtab, item) {
        if (subtab === 'photos') {
            return /* HTML */ `
                <div class="profile-subtab-empty-box">
                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                        <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
                        <circle cx="8.5" cy="8.5" r="1.5"></circle>
                        <polyline points="21 15 16 10 5 21"></polyline>
                    </svg>
                    <p>Hình ảnh hồ sơ &amp; lâm sàng của <strong>${escape(item.name)}</strong></p>
                    <small class="text-muted">Chưa có hình ảnh siêu âm / X-Quang tải lên trong đợt điều trị này.</small>
                </div>
            `;
        }
        if (subtab === 'medical') {
            return /* HTML */ `
                <div class="profile-subtab-info-card">
                    <h4>Hồ sơ bệnh án điện tử (EMR)</h4>
                    <p>• Mã định danh nhân sự / hồ sơ: <code>MED-${item.id}</code></p>
                    <p>• Chuyên khoa phụ trách: <strong>${escape(item.department || 'Đa khoa')}</strong></p>
                    <p>• Trạng thái hành nghề: <strong>${item.isActive ? 'Hợp lệ & Hoạt động' : 'Tạm ngưng'}</strong></p>
                </div>
            `;
        }
        if (subtab === 'billing') {
            return /* HTML */ `
                <div class="profile-subtab-info-card">
                    <h4>Gói dịch vụ &amp; Thanh toán viện phí</h4>
                    <p>• Gói bảo trợ y tế: <strong>Medicare Care Standard 2026</strong></p>
                    <p>• Hạn mức miễn giảm: <strong>Tối đa 15% theo chính sách nội bộ</strong></p>
                    <p>• Lịch sử đối soát: <strong>Đã hoàn tất thanh toán kỳ gần nhất</strong></p>
                </div>
            `;
        }
        return /* HTML */ `
            <div class="profile-subtab-info-card">
                <h4>Lịch hẹn &amp; Lịch làm việc</h4>
                <p>• Ca làm việc kế tiếp: <strong>07:30 - 11:30 ngày mai</strong></p>
                <p>• Phòng khám / Vị trí: <strong>${escape(item.department || 'Khu khám bệnh ngoại trú')}</strong></p>
            </div>
        `;
    }

    async function loadDirectory() {
        directory = await request('Directory');
        const headings = [
            'Họ tên / Tên hiển thị',
            'Tên đăng nhập',
            'Vai trò',
            'Khoa phòng',
            'Trạng thái',
        ];
        table('#accounts-table', [...headings, 'Thao tác'], accountRows(directory.accounts, true));
        table('#staff-table', headings, accountRows(directory.staff, false));
        renderSplitView('accounts', directory.accounts);
        renderSplitView('staff', directory.staff);
        table(
            '#departments-table',
            ['Mã', 'Tên phòng ban', 'Mô tả phạm vi'],
            directory.departments.map((row) => [
                /* HTML */ `<code class="dept-code">${escape(row.id)}</code>`,
                /* HTML */ `<strong>${escape(row.name)}</strong>`,
                escape(row.description || '—'),
            ]),
        );
        table(
            '#doctors-table',
            ['Họ tên Bác sĩ', 'Chuyên khoa', 'Phòng ban', 'Chứng chỉ hành nghề'],
            directory.doctors.map((row) => [
                /* HTML */ `<strong>${escape(row.fullName)}</strong>`,
                /* HTML */ `<span class="specialty-badge">${escape(row.specialty || 'Đa khoa')}</span>`,
                escape(row.departmentName),
                /* HTML */ `<code>${escape(row.licenseNumber || '—')}</code>`,
            ]),
        );
    }

    function renderCharts(report) {
        if (!report) return;

        // 1. Dual-axis Combo Chart (Bar: Doanh thu / Thu + Line: Lượt tiếp nhận)
        const canvas = document.getElementById('revenueChartCanvas');
        if (window.Chart && canvas) {
            if (window._revenueChartInstance) {
                window._revenueChartInstance.destroy();
            }

            const labels = report.days.map((d) => date(d.date).slice(0, 5));
            let barData = report.days.map((d) => d.revenue);
            let barLabel = 'Doanh thu (VNĐ)';
            let barBg = 'rgba(0, 104, 255, 0.85)';
            let barBorder = '#0068ff';

            if (activeChartFlow === 'out') {
                barData = report.days.map((d) => d.refunds);
                barLabel = 'Hoàn tiền / Chi (VNĐ)';
                barBg = 'rgba(245, 158, 11, 0.85)';
                barBorder = '#f59e0b';
            } else if (activeChartFlow === 'balance') {
                barData = report.days.map((d) => d.revenue - d.refunds);
                barLabel = 'Dòng tiền thuần (VNĐ)';
                barBg = 'rgba(16, 185, 129, 0.85)';
                barBorder = '#10b981';
            }

            const visitsData = report.days.map((d) => d.visits);

            const ctx = canvas.getContext('2d');
            window._revenueChartInstance = new window.Chart(ctx, {
                data: {
                    labels: labels,
                    datasets: [
                        {
                            type: 'bar',
                            label: barLabel,
                            data: barData,
                            backgroundColor: barBg,
                            borderColor: barBorder,
                            borderWidth: 1.5,
                            borderRadius: 6,
                            borderSkipped: false,
                            yAxisID: 'yRevenue',
                            order: 2,
                            maxBarThickness: 36,
                        },
                        {
                            type: 'line',
                            label: 'Lượt tiếp nhận',
                            data: visitsData,
                            borderColor: '#f97316',
                            backgroundColor: '#f97316',
                            borderWidth: 2.5,
                            pointBackgroundColor: '#ffffff',
                            pointBorderColor: '#f97316',
                            pointBorderWidth: 2.5,
                            pointRadius: 4.5,
                            pointHoverRadius: 6.5,
                            tension: 0.35,
                            yAxisID: 'yVisits',
                            order: 1,
                        },
                    ],
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: 'index', intersect: false },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: '#1e293b',
                            titleColor: '#ffffff',
                            bodyColor: '#e2e8f0',
                            padding: 10,
                            cornerRadius: 6,
                            callbacks: {
                                label: function (context) {
                                    if (context.dataset.yAxisID === 'yRevenue') {
                                        return ' ' + context.dataset.label + ': ' + money(context.raw) + ' ₫';
                                    }
                                    return ' Lượt khám: ' + context.raw + ' lượt';
                                },
                            },
                        },
                    },
                    scales: {
                        x: {
                            grid: { display: false },
                            ticks: {
                                font: { family: "'Inter', sans-serif", size: 11 },
                                color: '#64748b',
                            },
                        },
                        yRevenue: {
                            type: 'linear',
                            position: 'left',
                            beginAtZero: true,
                            suggestedMin: 0,
                            suggestedMax: 1000000,
                            grid: { color: '#f1f5f9' },
                            ticks: {
                                font: { family: "'Inter', sans-serif", size: 11 },
                                color: '#64748b',
                                callback: function (val) {
                                    if (Math.abs(val) >= 1000000000) return (val / 1000000000).toFixed(1) + ' tỷ';
                                    if (Math.abs(val) >= 1000000) return (val / 1000000).toFixed(0) + 'M';
                                    if (Math.abs(val) >= 1000) return (val / 1000).toFixed(0) + 'k';
                                    return val;
                                },
                            },
                        },
                        yVisits: {
                            type: 'linear',
                            position: 'right',
                            beginAtZero: true,
                            suggestedMin: 0,
                            suggestedMax: 5,
                            grid: { drawOnChartArea: false },
                            ticks: {
                                font: { family: "'Inter', sans-serif", size: 11 },
                                color: '#f97316',
                                precision: 0,
                            },
                        },
                    },
                },
            });
        } else {
            // Fallback khi không có Chart.js
            const fb = find('#revenue-chart');
            if (fb && report.days?.length) {
                const maximum = Math.max(1, ...report.days.map((day) => Math.abs(day.revenue)));
                fb.style.display = 'flex';
                fb.innerHTML = report.days
                    .map(
                        (day) =>
                            /* HTML */ `<div
                                class="chart-day"
                                title="${escape(date(day.date) + ': ' + money(day.revenue) + ' ₫ · ' + day.visits + ' lượt')}"
                            >
                                <div
                                    class="chart-bar ${day.revenue < 0 ? 'negative' : ''}"
                                    style="height:${Math.max(2, (Math.abs(day.revenue) / maximum) * 160)}px"
                                ></div>
                                <small>${escape(date(day.date).slice(0, 5))}</small>
                            </div>`,
                    )
                    .join('');
            }
        }

        // 2. Donut Chart (Cơ cấu dòng tiền MISA style)
        const donutCanvas = document.getElementById('cashflowDonutCanvas');
        if (window.Chart && donutCanvas) {
            if (window._cashflowDonutInstance) {
                window._cashflowDonutInstance.destroy();
            }

            const deposits = Math.max(0, report.deposits);
            const payments = Math.max(0, report.payments);
            const refunds = Math.max(0, report.refunds);
            const hasData = deposits > 0 || payments > 0 || refunds > 0;

            const dData = hasData ? [deposits, payments, refunds] : [40, 45, 15];
            const dColors = hasData
                ? ['#0068ff', '#10b981', '#f59e0b']
                : ['#38bdf8', '#34d399', '#fbbf24'];

            const centerTotal = find('#donut-center-total');
            if (centerTotal) {
                centerTotal.textContent = hasData ? money(deposits + payments + refunds) + ' ₫' : '100%';
            }

            const dCtx = donutCanvas.getContext('2d');
            window._cashflowDonutInstance = new window.Chart(dCtx, {
                type: 'doughnut',
                data: {
                    labels: ['Tạm ứng viện phí', 'Thanh toán viện phí', 'Hoàn tiền / Miễn giảm'],
                    datasets: [
                        {
                            data: dData,
                            backgroundColor: dColors,
                            borderWidth: 3,
                            borderColor: '#ffffff',
                            hoverOffset: 6,
                        },
                    ],
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: '72%',
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: '#1e293b',
                            titleColor: '#ffffff',
                            bodyColor: '#e2e8f0',
                            padding: 10,
                            cornerRadius: 6,
                            callbacks: {
                                label: function (context) {
                                    if (!hasData) return ' ' + context.label + ': Chưa có giao dịch';
                                    return ' ' + context.label + ': ' + money(context.raw) + ' ₫';
                                },
                            },
                        },
                    },
                },
            });
        }
    }

    let isDemoMode = true;

    function generateSampleReport(fromDate, toDate) {
        const today = new Date();
        const to = toDate ? new Date(toDate) : today;
        let from = fromDate ? new Date(fromDate) : new Date(today);
        if (!fromDate) {
            from.setDate(today.getDate() - 6);
        }

        const days = [];
        const cur = new Date(from);
        let totalDeposits = 0;
        let totalPayments = 0;
        let totalRefunds = 0;
        let totalVisits = 0;

        const sampleProfiles = [
            { dep: 28500000, pay: 34200000, ref: 1800000, vis: 185 },
            { dep: 31200000, pay: 38600000, ref: 2100000, vis: 210 },
            { dep: 26800000, pay: 32100000, ref: 1500000, vis: 172 },
            { dep: 35400000, pay: 42900000, ref: 3200000, vis: 230 },
            { dep: 29800000, pay: 36400000, ref: 2400000, vis: 194 },
            { dep: 21500000, pay: 25800000, ref: 1100000, vis: 138 },
            { dep: 22000000, pay: 25500000, ref: 1100000, vis: 151 },
        ];

        let i = 0;
        while (cur <= to && days.length < 31) {
            const p = sampleProfiles[i % sampleProfiles.length];
            const dateStr = cur.toISOString().slice(0, 10);
            const dayRev = p.dep + p.pay - p.ref;
            days.push({
                date: dateStr,
                deposits: p.dep,
                payments: p.pay,
                refunds: p.ref,
                revenue: dayRev,
                visits: p.vis,
            });
            totalDeposits += p.dep;
            totalPayments += p.pay;
            totalRefunds += p.ref;
            totalVisits += p.vis;
            cur.setDate(cur.getDate() + 1);
            i++;
        }

        if (days.length === 0) {
            const dateStr = today.toISOString().slice(0, 10);
            days.push({
                date: dateStr,
                deposits: 28500000,
                payments: 34200000,
                refunds: 1800000,
                revenue: 60900000,
                visits: 185,
            });
            totalDeposits = 28500000;
            totalPayments = 34200000;
            totalRefunds = 1800000;
            totalVisits = 185;
        }

        return {
            from: days[0].date,
            to: days[days.length - 1].date,
            deposits: totalDeposits,
            payments: totalPayments,
            refunds: totalRefunds,
            revenue: totalDeposits + totalPayments - totalRefunds,
            visits: totalVisits,
            days: days,
            isSample: true,
        };
    }

    async function loadReport(withFilter = true) {
        const version = ++reportVersion;
        const filter = find('#report-filter');
        const query = withFilter && filter ? '?' + new URLSearchParams(new FormData(filter)) : '';
        let report = await request('Report' + query);
        if (version !== reportVersion) return;

        const hasNoRealData =
            !report ||
            (report.revenue === 0 &&
                report.visits === 0 &&
                (!report.days || report.days.every((d) => d.revenue === 0 && d.visits === 0)));

        if (isDemoMode || hasNoRealData) {
            report = generateSampleReport(report?.from, report?.to);
        }

        currentReport = report;

        if (filter) {
            filter.elements.from.value = report.from;
            filter.elements.to.value = report.to;
        }

        const periodBadge = find('#report-period');
        if (periodBadge) {
            periodBadge.textContent = `${date(report.from)} – ${date(report.to)}`;
        }

        // Cập nhật các thẻ KPI MISA chuẩn
        const elRev = find('#kpi-revenue');
        if (elRev) elRev.textContent = money(report.revenue) + ' ₫';

        const elVisits = find('#kpi-visits');
        if (elVisits) elVisits.textContent = money(report.visits) + ' lượt';

        const elDeposits = find('#kpi-deposits');
        if (elDeposits) elDeposits.textContent = money(report.deposits) + ' ₫';
        const elDepositsText = find('#kpi-deposits-text');
        if (elDepositsText) elDepositsText.textContent = money(report.deposits) + ' ₫';

        const elPayments = find('#kpi-payments');
        if (elPayments) elPayments.textContent = money(report.payments) + ' ₫';

        const elRefunds = find('#kpi-refunds');
        if (elRefunds) elRefunds.textContent = money(report.refunds) + ' ₫';
        const elRefundsText = find('#kpi-refunds-text');
        if (elRefundsText) elRefundsText.textContent = money(report.refunds) + ' ₫';

        const elHighlight = find('#highlightTotalRevenue');
        if (elHighlight) elHighlight.textContent = money(report.revenue) + ' ₫';

        // Render biểu đồ
        renderCharts(report);

        // Render bảng chi tiết
        table(
            '#report-table',
            ['Ngày', 'Tạm ứng', 'Thanh toán', 'Hoàn tiền', 'Thực thu', 'Lượt tiếp nhận'],
            report.days.map((day) => [
                /* HTML */ `<strong>${escape(date(day.date))}</strong>`,
                ...[day.deposits, day.payments, day.refunds, day.revenue, day.visits].map(
                    (value) => /* HTML */ `<span class="number">${escape(money(value))}</span>`,
                ),
            ]),
        );
    }

    async function loadRequests() {
        const rows = await request('Requests');
        const statuses = { Pending: 'Chờ duyệt', Approved: 'Đã duyệt', Rejected: 'Đã từ chối' };
        table(
            '#requests-table',
            ['Bác sĩ / Khoa phòng', 'Ngày / Ca trực', 'Chi tiết đề nghị', 'Trạng thái', 'Thao tác xử lý'],
            rows.map((row) => [
                /* HTML */ `<strong>${escape(row.doctor)}</strong><br /><small class="text-muted"
                        >${escape(row.department)}</small
                    >`,
                /* HTML */ `<span class="misa-badge-date">${escape(date(row.date))}</span><br /><small
                        >${escape(row.shift)}</small
                    >`,
                /* HTML */ `<span class="req-type">${escape(row.type)}</span><br /><small class="text-muted"
                        >${escape(row.reason || 'Không có ghi chú')}</small
                    >`,
                /* HTML */ `<span class="misa-badge-status status-${row.status.toLowerCase()}"
                    >${escape(statuses[row.status] || row.status)}</span
                >`,
                row.canApprove || row.canReject
                    ? /* HTML */ `<div class="request-actions" data-request="${row.id}">
                          ${
                              row.canApprove
                                  ? /* HTML */ `<select aria-label="Bác sĩ thay thế" class="misa-select-sm">
                                        <option value="">
                                            ${escape(row.replacement ? 'Giữ đề nghị: ' + row.replacement : 'Tự phân bổ trong khoa')}
                                        </option>
                                        ${row.replacements.map((option) => /* HTML */ `<option value="${option.id}">${escape(option.name)}</option>`).join('')}
                                    </select>`
                                  : '<small class="text-muted">Ca đã bắt đầu hoặc đã qua; chỉ có thể từ chối.</small>'
                          }
                          <div class="misa-btn-group-sm">
                              ${row.canApprove ? '<button type="button" class="btn misa-btn-sm-primary" data-decision="approve">Duyệt ca</button>' : ''}${row.canReject ? '<button type="button" class="btn misa-btn-sm-secondary" data-decision="reject">Từ chối</button>' : ''}
                          </div>
                      </div>`
                    : /* HTML */ `<span class="processed-tag">${escape(row.replacement || 'Đã xử lý')}</span>`,
            ]),
        );
    }

    function closeNavigation() {
        find('#admin-sidebar')?.classList.remove('open');
        find('#admin-menu-toggle')?.setAttribute('aria-expanded', 'false');
        const scrim = find('.sidebar-scrim');
        if (scrim) scrim.hidden = true;
    }

    const menuToggle = find('#admin-menu-toggle');
    if (menuToggle) {
        menuToggle.addEventListener('click', () => {
            const sidebar = find('#admin-sidebar');
            const open = sidebar ? sidebar.classList.toggle('open') : false;
            menuToggle.setAttribute('aria-expanded', String(open));
            const scrim = find('.sidebar-scrim');
            if (scrim) scrim.hidden = !open;
        });
    }

    const scrimEl = find('.sidebar-scrim');
    if (scrimEl) scrimEl.addEventListener('click', closeNavigation);

    find('#admin-sidebar nav')?.addEventListener('click', (event) => {
        if (event.target.closest('[data-section]')) closeNavigation();
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            closeNavigation();
            find('#misa-quick-add-menu')?.setAttribute('hidden', '');
        }
    });

    function selectSection() {
        closeNavigation();
        const selected = location.hash.slice(1) || workspace.dataset.section;
        const section = titles[selected] ? selected : 'dashboard';

        document
            .querySelectorAll('[data-panel]')
            .forEach((panel) => (panel.hidden = panel.dataset.panel !== section));

        document.querySelectorAll('nav [data-section]').forEach((link) => {
            link.classList.toggle('active', link.dataset.section === section);
            if (link.dataset.section === section) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        });

        const msg = find('#admin-message');
        if (msg) msg.hidden = true;

        const breadcrumb = find('#admin-breadcrumb');
        if (breadcrumb && titles[section]) breadcrumb.textContent = titles[section][0];

        const pTitle = find('#page-title');
        if (pTitle && titles[section]) pTitle.textContent = titles[section][0];

        const pDesc = find('#page-description');
        if (pDesc && titles[section]) pDesc.textContent = titles[section][1];
    }

    function openAccount(staffOnly) {
        if (!directory)
            return notify('Danh sách chưa tải được. Hãy làm mới trước khi tạo tài khoản.', true);
        const form = find('#account-form');
        form.reset();
        form.querySelector('.form-error').textContent = '';
        const roles = directory.roles.filter((role) => !staffOnly || role.requiresDepartment);
        form.elements.roleId.innerHTML = roles
            .map((role) => /* HTML */ `<option value="${role.id}">${escape(role.name)}</option>`)
            .join('');
        form.elements.departmentId.innerHTML =
            '<option value="">Chọn phòng ban</option>' +
            directory.departments
                .map((row) => /* HTML */ `<option value="${row.id}">${escape(row.name)}</option>`)
                .join('');
        find('#account-title').textContent = staffOnly
            ? 'Thêm nhân sự và tài khoản'
            : 'Tạo tài khoản mới';
        updateRoleFields();
        find('#account-dialog').showModal();
    }

    function updateRoleFields() {
        const form = find('#account-form');
        const role = directory?.roles.find(
            (role) => role.id === Number(form.elements.roleId.value),
        );
        const fields = [
            ['[data-name-field]', role?.requiresName],
            ['[data-department-field]', role?.requiresDepartment],
            ['[data-patient-field]', role?.requiresPatient],
        ];
        for (const [selector, shown] of fields) {
            form.querySelectorAll(selector).forEach((field) => {
                field.hidden = !shown;
                field.querySelectorAll('input,select').forEach((input) => {
                    input.disabled = !shown;
                    input.required = !!shown;
                });
            });
        }
        find('#account-hint').textContent = role?.requiresName
            ? 'Tài khoản mới được kích hoạt và liên kết hồ sơ đúng vai trò/phòng ban. Mật khẩu không hiển thị trong danh sách.'
            : 'Tài khoản Admin dùng tên đăng nhập làm tên hiển thị.';
    }

    async function mutate(path, payload, form) {
        if (busy) return;
        busy = true;
        const enabled = [...document.querySelectorAll('button:not(:disabled)')];
        enabled.forEach((button) => (button.disabled = true));
        try {
            const result = await request(path, payload);
            form?.closest('dialog')?.close();
            form?.reset();
            notify(result.message);
            try {
                await Promise.all([loadDirectory(), loadRequests()]);
            } catch {
                notify(result.message + ' Danh sách chưa tải lại được; hãy bấm Làm mới.', true);
            }
        } catch (error) {
            if (form) form.querySelector('.form-error').textContent = error.message;
            else notify(error.message, true);
        } finally {
            busy = false;
            enabled
                .filter((button) => button.isConnected)
                .forEach((button) => (button.disabled = false));
        }
    }

    // Xử lý nút Thêm nhanh MISA
    const quickAddBtn = find('#misa-quick-add-btn');
    const quickAddMenu = find('#misa-quick-add-menu');
    if (quickAddBtn && quickAddMenu) {
        quickAddBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            quickAddMenu.toggleAttribute('hidden');
        });
        find('#quick-add-staff-trigger')?.addEventListener('click', () => {
            quickAddMenu.setAttribute('hidden', '');
            openAccount(true);
        });
        find('#quick-add-account-trigger')?.addEventListener('click', () => {
            quickAddMenu.setAttribute('hidden', '');
            openAccount(false);
        });
    }

    // Xử lý các nút subtab biểu đồ THU / CHI / TỒN
    document.querySelectorAll('.misa-chart-subtab').forEach((tab) => {
        tab.addEventListener('click', (e) => {
            document.querySelectorAll('.misa-chart-subtab').forEach((t) => t.classList.remove('active'));
            tab.classList.add('active');
            const txt = tab.textContent.trim().toUpperCase();
            if (txt.includes('HOÀN') || txt.includes('CHI')) activeChartFlow = 'out';
            else if (txt.includes('THUẦN') || txt.includes('TỒN')) activeChartFlow = 'balance';
            else activeChartFlow = 'all';
            if (currentReport) renderCharts(currentReport);
        });
    });

    find('#chart-refresh-trigger')?.addEventListener('click', () => {
        if (currentReport) renderCharts(currentReport);
    });

    find('#report-filter')?.addEventListener('submit', (event) => {
        event.preventDefault();
        loadReport().catch((error) => notify(error.message, true));
    });

    find('#department-form')?.addEventListener('submit', (event) => {
        event.preventDefault();
        mutate(
            'Departments',
            Object.fromEntries(new FormData(event.currentTarget)),
            event.currentTarget,
        );
    });

    find('#account-form')?.addEventListener('submit', (event) => {
        event.preventDefault();
        const data = Object.fromEntries(new FormData(event.currentTarget));
        data.roleId = Number(data.roleId);
        data.departmentId = data.departmentId ? Number(data.departmentId) : null;
        data.dateOfBirth = data.dateOfBirth || null;
        mutate('Accounts', data, event.currentTarget);
    });

    find('#account-form [name="roleId"]')?.addEventListener('change', updateRoleFields);
    find('#new-staff')?.addEventListener('click', () => openAccount(true));
    find('#new-account')?.addEventListener('click', () => openAccount(false));
    find('#toggle-sample-data')?.addEventListener('click', (event) => {
        isDemoMode = !isDemoMode;
        const btn = event.currentTarget;
        btn.classList.toggle('active', isDemoMode);
        const txt = find('#demo-toggle-text');
        if (txt) txt.textContent = isDemoMode ? 'Dữ liệu mẫu: BẬT' : 'Dữ liệu máy chủ: THẬT';
        loadReport(true).catch((error) => notify(error.message, true));
    });
    find('#refresh-data')?.addEventListener('click', () =>
        refresh().catch((error) => notify(error.message, true)),
    );
    window.addEventListener('hashchange', selectSection);

    document.addEventListener('click', (event) => {
        if (quickAddMenu && !event.target.closest('.misa-quick-add-container')) {
            quickAddMenu.setAttribute('hidden', '');
        }

        const open = event.target.closest('[data-open]');
        if (open) {
            const dialog = document.getElementById(open.dataset.open);
            if (dialog) {
                dialog.querySelector('form')?.reset();
                const err = dialog.querySelector('.form-error');
                if (err) err.textContent = '';
                dialog.showModal();
            }
        }
        if (event.target.closest('[data-close]')) {
            event.target.closest('dialog')?.close();
        }
        const account = event.target.closest('[data-account]');
        if (account && confirm('Xác nhận thay đổi trạng thái truy cập của tài khoản này?')) {
            const accId = Number(account.dataset.account);
            const nextActive = account.dataset.active === 'true';
            if (accId >= 100) {
                const sample = sampleStaffProfiles.find((p) => p.id === accId);
                if (sample) {
                    sample.isActive = nextActive;
                    if (sample.alertBadge) {
                        sample.alertBadge.label = nextActive ? 'Đang hoạt động' : 'Tài khoản bị khóa';
                        sample.alertBadge.type = nextActive ? 'active' : 'warning';
                    }
                    notify(
                        `Đã cập nhật trạng thái của ${sample.name} sang: ${nextActive ? 'Đang hoạt động' : 'Tạm khóa'}`,
                    );
                    renderSplitView('accounts', directory?.accounts || []);
                    renderSplitView('staff', directory?.staff || []);
                    return;
                }
            }
            mutate('AccountStatus', {
                accountId: accId,
                isActive: nextActive,
            });
        }
        const decision = event.target.closest('[data-decision]');
        if (
            decision &&
            confirm(
                'Xác nhận xử lý đề nghị này? Khi duyệt, lịch khám liên quan có thể được chuyển hoặc yêu cầu đặt lại.',
            )
        ) {
            const container = decision.closest('[data-request]');
            const replacement = container.querySelector('select')?.value;
            mutate('Decision', {
                exceptionId: Number(container.dataset.request),
                approve: decision.dataset.decision === 'approve',
                replacementDoctorId: replacement ? Number(replacement) : null,
            });
        }
    });

    // Xử lý chuyển đổi qua lại giữa Thẻ hồ sơ (Split View) và Bảng danh sách (Grid View)
    document.querySelectorAll('.misa-toggle-pill[data-view-target]').forEach((btn) => {
        btn.addEventListener('click', () => {
            const targetId = btn.dataset.viewTarget;
            const parent = btn.closest('.misa-view-toggle-pills');
            parent?.querySelectorAll('.misa-toggle-pill').forEach((b) => b.classList.remove('active'));
            btn.classList.add('active');

            if (targetId.startsWith('staff-')) {
                const isSplit = targetId === 'staff-split';
                const splitEl = find('#staff-split-view');
                const gridEl = find('#staff-grid-view');
                if (splitEl) splitEl.style.display = isSplit ? 'grid' : 'none';
                if (gridEl) gridEl.style.display = isSplit ? 'none' : 'block';
            } else if (targetId.startsWith('accounts-')) {
                const isSplit = targetId === 'accounts-split';
                const splitEl = find('#accounts-split-view');
                const gridEl = find('#accounts-grid-view');
                if (splitEl) splitEl.style.display = isSplit ? 'grid' : 'none';
                if (gridEl) gridEl.style.display = isSplit ? 'none' : 'block';
            }
        });
    });

    // Xử lý tìm kiếm thời gian thực trên danh sách bên trái (Master list)
    ['staff', 'accounts'].forEach((key) => {
        const splitSearch = find(`#${key}-split-search`);
        if (splitSearch) {
            splitSearch.addEventListener('input', (e) => {
                const q = e.target.value.toLowerCase().trim();
                const items = document.querySelectorAll(`#${key}-master-list .misa-split-item`);
                items.forEach((item) => {
                    const text = item.textContent.toLowerCase();
                    item.style.display = text.includes(q) ? 'flex' : 'none';
                });
            });
        }
    });

    find('#export-revenue')?.addEventListener('click', async () => {
        if (currentReport?.isSample && currentReport.days) {
            let csv = '\uFEFFNgày,Tạm ứng,Thanh toán,Hoàn tiền,Doanh thu thực thu,Lượt đã tiếp nhận\r\n';
            currentReport.days.forEach((d) => {
                csv += `${d.date},${d.deposits},${d.payments},${d.refunds},${d.revenue},${d.visits}\r\n`;
            });
            csv += `Tổng,${currentReport.deposits},${currentReport.payments},${currentReport.refunds},${currentReport.revenue},${currentReport.visits}\r\n`;
            const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = `doanh-thu-mau-${currentReport.from}-${currentReport.to}.csv`;
            link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            return;
        }
        const form = find('#report-filter');
        if (!form || !form.reportValidity()) return;
        const button = find('#export-revenue');
        button.disabled = true;
        try {
            const query = new URLSearchParams(new FormData(form));
            const response = await fetch(api + '/Revenue.csv?' + query, {
                credentials: 'same-origin',
                cache: 'no-store',
            });
            if (
                response.redirected ||
                !response.ok ||
                !response.headers.get('content-type')?.includes('text/csv')
            ) {
                throw new Error(
                    'Chưa xuất được báo cáo. Hãy kiểm tra phiên đăng nhập và khoảng thời gian.',
                );
            }
            const url = URL.createObjectURL(await response.blob());
            const link = document.createElement('a');
            link.href = url;
            link.download = `doanh-thu-${query.get('from')}-${query.get('to')}.csv`;
            link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (error) {
            notify(error.message, true);
        } finally {
            button.disabled = false;
        }
    });

    // Tìm kiếm nhanh trong bảng theo từ khóa
    const searchInput = find('#misa-global-search');
    if (searchInput) {
        searchInput.addEventListener('input', (e) => {
            const query = e.target.value.toLowerCase().trim();
            const activePanel = document.querySelector('[data-panel]:not([hidden])');
            if (!activePanel) return;
            const rows = activePanel.querySelectorAll('tbody tr');
            rows.forEach((row) => {
                if (row.classList.contains('empty')) return;
                const text = row.textContent.toLowerCase();
                row.style.display = text.includes(query) ? '' : 'none';
            });
        });
    }

    async function refresh() {
        const filter = find('#report-filter');
        await Promise.all([
            loadDirectory(),
            loadRequests(),
            loadReport(!!filter?.elements?.from?.value),
        ]);
    }

    selectSection();
    refresh().catch((error) => notify(error.message, true));
})();
