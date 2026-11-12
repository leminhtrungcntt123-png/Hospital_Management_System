function appointmentsPage() {
    return `${heading('Lịch hẹn của tôi', 'Theo dõi trạng thái lịch khám và thông tin tiếp nhận.', link('Đặt lịch khám', 'public/booking', 'plus', 'btn primary'))}
        ${unavailableNotice()}${pendingTable('Danh sách lịch hẹn', ['Mã lịch', 'Người khám', 'Bác sĩ / phòng', 'Ngày giờ', 'Trạng thái'])}`;
}
