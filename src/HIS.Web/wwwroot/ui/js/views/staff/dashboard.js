function staffDashboard() {
    const reception =
        config.role === 'NURSE'
            ? link(
                  'Mở quầy tiếp nhận',
                  'staff/reception',
                  'users',
                  'btn primary',
              )
            : '';
    return `${heading(config.role === 'DEPARTMENT_HEAD' ? 'Tổng quan khoa' : 'Bàn làm việc', 'Theo dõi công việc và các lượt khám trong khoa.', reception)}
        ${unavailableNotice()}${pendingStats(['Lịch khám hôm nay', 'Đã tiếp nhận', 'Đang khám', 'Hoàn thành'])}
        ${pendingTable('Công việc hôm nay', ['Người khám', 'Phòng khám', 'Bác sĩ', 'Trạng thái'])}`;
}

function vitalsPage() {
    return `${heading('Sinh hiệu', 'Ghi nhận sinh hiệu cho người khám đã được tiếp nhận.', unavailableButton('Ghi sinh hiệu', 'pulse'))}
        ${unavailableNotice()}${pendingTable('Danh sách chờ đo sinh hiệu', ['Số thứ tự', 'Người khám', 'Phòng khám', 'Trạng thái'])}`;
}

function patientsPage() {
    return `${heading('Hồ sơ người khám', 'Danh sách được cung cấp theo phạm vi công việc của tài khoản.')}
        ${unavailableNotice()}${pendingTable('Danh sách người khám', ['Mã hồ sơ', 'Họ và tên', 'Ngày sinh', 'Số điện thoại', 'Lượt khám'])}`;
}
