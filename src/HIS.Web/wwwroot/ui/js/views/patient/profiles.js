function familyPage() {
    return `${heading('Hồ sơ người khám', 'Quản lý thông tin người khám và các liên kết được hệ thống xác nhận.', unavailableButton('Thêm người khám'))}
        ${unavailableNotice()}${pendingTable('Danh sách hồ sơ', ['Người khám', 'Ngày sinh', 'Quan hệ', 'Trạng thái liên kết'])}`;
}

function recordsPage() {
    return `${heading('Hồ sơ sức khỏe', 'Bệnh án, kết quả và đơn thuốc được cung cấp theo quyền truy cập của tài khoản.')}
        ${unavailableNotice()}${pendingTable('Hồ sơ khám bệnh', ['Ngày khám', 'Người khám', 'Chuyên khoa', 'Bác sĩ', 'Kết quả'])}`;
}

function notificationsPage() {
    return `${heading('Thông báo', 'Theo dõi cập nhật về lịch hẹn và hồ sơ của bạn.', unavailableButton('Đánh dấu đã đọc', 'check'))}
        ${unavailableNotice()}${pendingTable('Thông báo của bạn', ['Thời gian', 'Nội dung', 'Trạng thái'])}`;
}
