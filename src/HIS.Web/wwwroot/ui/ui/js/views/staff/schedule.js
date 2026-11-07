function schedulePage() {
    return `${heading('Lịch khám & lịch trực', 'Theo dõi lịch được phân công và phòng khám phụ trách.')}
        ${unavailableNotice()}${pendingTable('Lịch khám', ['Ngày', 'Bác sĩ', 'Phòng', 'Ca khám', 'Giờ bắt đầu', 'Giờ kết thúc'])}
        <div class="space-top">${pendingTable('Ca trực nhân sự', ['Nhân sự', 'Bắt đầu', 'Kết thúc', 'Ghi chú'])}</div>`;
}

function exceptionsPage() {
    return `${heading('Nghỉ / đổi ca', 'Theo dõi đề nghị và kết quả xử lý lịch làm việc.', unavailableButton('Tạo đề nghị', 'calendar'))}
        ${unavailableNotice()}${pendingTable('Danh sách đề nghị', ['Nhân sự', 'Ngày / ca', 'Loại đề nghị', 'Lý do', 'Trạng thái'])}`;
}
