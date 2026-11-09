function cashierPage() {
    return `${heading('Thu phí & BHYT', 'Đối chiếu hóa đơn, quyền lợi bảo hiểm và các khoản thanh toán.', unavailableButton('Xuất sổ thu phí', 'download'))}
        ${unavailableNotice()}${pendingStats(['Tổng người bệnh trả', 'Đã ghi nhận thu', 'Còn phải thu'])}
        ${pendingTable('Danh sách hóa đơn', ['Hóa đơn / Ngày khám', 'Người khám', 'Tổng chi phí', 'BHYT', 'Đã thu', 'Còn lại', 'Trạng thái'])}`;
}
