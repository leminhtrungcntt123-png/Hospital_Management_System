function consultationPage() {
    return `${heading('Khám bệnh & kê đơn', 'Tiếp nhận lượt khám và ghi nhận hồ sơ chuyên môn.')}
        ${unavailableNotice()}<div class="clinical-layout">
            ${pendingTable('Danh sách chờ khám', ['Số thứ tự', 'Người khám', 'Trạng thái'])}
            <section class="card consultation-workspace"><header class="card-header"><h2>Phiếu khám bệnh</h2></header>
                <fieldset disabled class="clinical-inner"><div class="form-grid">
                    <div class="field full"><label for="clinical-symptoms">Triệu chứng / Lý do khám</label><textarea id="clinical-symptoms" rows="3"></textarea></div>
                    <div class="field full"><label for="clinical-examination">Khám lâm sàng</label><textarea id="clinical-examination" rows="3"></textarea></div>
                    <div class="field"><label for="clinical-icd">Mã chẩn đoán (ICD-10)</label><input id="clinical-icd" /></div>
                    <div class="field"><label for="clinical-diagnosis">Chẩn đoán chính</label><input id="clinical-diagnosis" /></div>
                    <div class="field full"><label for="clinical-conclusion">Kết luận / Lời dặn</label><textarea id="clinical-conclusion" rows="3"></textarea></div>
                </div><div class="inline-actions">${unavailableButton('Thêm chỉ định', 'pulse')}${unavailableButton('Thêm thuốc', 'plus')}${unavailableButton('Lưu phiếu khám', 'check')}</div></fieldset>
            </section>
        </div>`;
}

function labsPage() {
    return `${heading('Chỉ định & kết quả', 'Theo dõi dịch vụ được chỉ định và kết quả trả về.')}
        ${unavailableNotice()}${pendingTable('Danh sách chỉ định', ['Người khám', 'Dịch vụ', 'Thời gian', 'Trạng thái', 'Kết quả'])}`;
}
