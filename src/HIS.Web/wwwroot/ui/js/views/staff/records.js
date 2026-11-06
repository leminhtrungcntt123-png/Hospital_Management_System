function staffRecordsPage() {
    return `${heading('Bệnh án trong khoa', 'Hồ sơ chuyên môn được cung cấp theo quyền của tài khoản.')}
        ${unavailableNotice()}${pendingTable('Danh sách bệnh án', ['Người khám', 'Ngày khám', 'Bác sĩ', 'Chẩn đoán', 'Trạng thái'])}`;
}
