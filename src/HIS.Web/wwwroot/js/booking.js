/* =========================================================
   booking.js - JavaScript cho quy trình đặt lịch khám (ba luồng)
   Mỗi khối chỉ chạy khi trang hiện tại có phần tử tương ứng:
     Trang chủ   (không form)        ba lối vào, không cần script
     Chọn ngày   #ngay-form          lịch toàn viện của luồng "theo ngày"
     Chọn khoa   #khoa-form          chọn chuyên khoa + tìm kiếm
     Chọn bác sĩ #bacsi-form         thẻ bác sĩ, hoặc bác sĩ kèm khung giờ (luồng theo ngày)
     Ngày & giờ  #ngaygio-form       lịch của khoa/bác sĩ + chip khung giờ (fetch KhungGio)
     Người khám  #nguoi-form         chọn hồ sơ đã liên kết
     Xác nhận    #xacnhan-form       khai báo bảo hiểm + đồng ý + chống gửi nhiều lần
     In phiếu    [data-print]        lưu / in phiếu hẹn
     Chung       modal thông tin bác sĩ
   ========================================================= */
(function () {
    'use strict';

    const $ = (sel, root = document) => root.querySelector(sel);
    const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));
    const refreshIcons = () => { if (window.lucide) window.lucide.createIcons(); };

    // Bỏ dấu tiếng Việt để tìm kiếm không phân biệt dấu: "tim mach" khớp "Tim mạch".
    const normalize = (s) => s.toLowerCase().normalize('NFD')
        .replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd');

    // Lọc thẻ theo từ khóa trong data-tags; trả về số thẻ còn hiển thị để biết lúc nào hiện empty-state.
    const filterCards = (cards, keyword, tagAttr = 'tags') => {
        const q = normalize(keyword);
        let visible = 0;
        cards.forEach((card) => {
            const match = normalize(card.dataset[tagAttr] || '').includes(q);
            card.hidden = !match;
            if (match) visible++;
        });
        return visible;
    };

    // ------------------------------------------------------------------ Modal chung

    const showModal = (name) => {
        const modal = $('#' + name + '-modal');
        const scrim = $('#' + name + '-scrim');
        if (modal) modal.hidden = false;
        if (scrim) scrim.hidden = false;
    };

    const closeModals = () => {
        $$('.modal, .modal-scrim').forEach((el) => { el.hidden = true; });
    };

    const setText = (id, text) => { const el = $('#' + id); if (el) el.textContent = text; };

    // Dòng thông tin chỉ hiện khi hệ thống thật sự có dữ liệu (khung giờ không mang theo ngày trực/chuyên khoa).
    const setFact = (rowId, textId, text) => {
        setText(textId, text || '');
        const row = $('#' + rowId);
        if (row) row.hidden = !text;
    };

    const openDoctorModal = (d) => {
        setText('dm-avatar', d.initials);
        setText('dm-name', d.name);
        setText('dm-sub', d.time ? `Giờ phụ trách: ${d.time}` : '');
        setText('dm-specialty', d.specialty || 'Chưa cập nhật');
        setFact('dm-department-row', 'dm-department', d.department);
        setFact('dm-duty-row', 'dm-duty', d.duty);
        setText('dm-room', d.room);
        setText('dm-date', d.date);
        // Màn danh sách bác sĩ chưa có ngày khám cụ thể -> bỏ cụm "ngày ...".
        const dateWrap = $('#dm-date-wrap');
        if (dateWrap) dateWrap.hidden = !d.date;
        showModal('doctor');
    };

    document.addEventListener('click', (e) => {
        const doctorBtn = e.target.closest('[data-doctor-open]');
        if (doctorBtn) {
            try { openDoctorModal(JSON.parse(doctorBtn.dataset.doctorOpen)); } catch (err) { /* bỏ qua dữ liệu lỗi */ }
            return;
        }
        if (e.target.closest('[data-modal-close]')) closeModals();

        if (e.target.closest('[data-print]')) window.print();
    });

    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeModals(); });

    // ------------------------------------------------------------------ Chọn chuyên khoa

    const khoaForm = $('#khoa-form');
    if (khoaForm) {
        const hidden = $('#field-department');
        const next = $('#khoa-next');
        const label = $('#khoa-selected-name');
        const grid = $('#khoa-grid');
        const empty = $('#khoa-empty');
        const search = $('#khoa-search');

        grid.addEventListener('click', (e) => {
            const card = e.target.closest('.specialty-card');
            if (!card || card.disabled) return;
            $$('.specialty-card', grid).forEach((c) => c.classList.toggle('selected', c === card));
            hidden.value = card.dataset.id;
            label.textContent = card.dataset.name;
            next.disabled = false;
        });

        search.addEventListener('input', () => { empty.hidden = filterCards($$('.specialty-card', grid), search.value) > 0; });
        search.addEventListener('keydown', (e) => { if (e.key === 'Enter') e.preventDefault(); });
    }

    // ------------------------------------------------------------------ Chọn ngày khám (bước 1 luồng theo ngày)

    const ngayForm = $('#ngay-form');
    if (ngayForm) {
        const hidden = $('#field-date');
        const label = $('#ngay-selected-label');
        const strip = $('#date-strip');

        strip.addEventListener('click', (e) => {
            const btn = e.target.closest('button[data-date]');
            if (!btn || btn.disabled) return;
            $$('button', strip).forEach((b) => b.classList.toggle('selected', b === btn));
            hidden.value = btn.dataset.date;
            label.textContent = btn.dataset.label;
        });
    }

    // ------------------------------------------------------------------ Chọn bác sĩ (+ khung giờ nếu đã chốt ngày)

    const bacsiForm = $('#bacsi-form');
    if (bacsiForm) {
        const hiddenDoctor = $('#field-doctor');
        const hiddenSlot = $('#field-slot');
        const next = $('#bacsi-next');
        const label = $('#bacsi-selected-label');
        const search = $('#bacsi-search');
        const empty = $('#bacsi-empty');

        if (search) {
            const cards = $$('.doctor-card', bacsiForm);
            search.addEventListener('input', () => { empty.hidden = filterCards(cards, search.value) > 0; });
            search.addEventListener('keydown', (e) => { if (e.key === 'Enter') e.preventDefault(); });
        }

        bacsiForm.addEventListener('click', (e) => {
            const card = e.target.closest('.doctor-card');
            if (card && !card.disabled) {
                $$('.doctor-card', bacsiForm).forEach((c) => c.classList.toggle('selected', c === card));
                hiddenDoctor.value = card.dataset.id;
                hiddenSlot.value = '';
                label.textContent = card.dataset.name;
                next.disabled = false;
                return;
            }

            // Dạng "bác sĩ & khung giờ": một cú bấm chốt cả hai vì ngày đã cố định từ bước trước.
            const chip = e.target.closest('.slot-chip');
            if (chip && !chip.disabled) {
                const item = chip.closest('.slot-item');
                $$('.slot-item', bacsiForm).forEach((c) => c.classList.toggle('selected', c === item));
                $$('.doctor-block', bacsiForm).forEach((b) =>
                    b.classList.toggle('selected', b.contains(item)));
                hiddenDoctor.value = item.dataset.doctor;
                hiddenSlot.value = item.dataset.slot;
                label.textContent = `${item.dataset.doctorName} · ${item.dataset.label}`;
                next.disabled = false;
            }
        });

        bacsiForm.addEventListener('submit', (e) => {
            if (!hiddenDoctor.value) { e.preventDefault(); return; }
            if (hiddenSlot && $$('.slot-chip', bacsiForm).length && !hiddenSlot.value) e.preventDefault();
        });
    }

    // ------------------------------------------------------------------ Ngày & khung giờ (luồng theo khoa / theo bác sĩ)

    const ngayGioForm = $('#ngaygio-form');
    if (ngayGioForm) {
        const hiddenDate = $('#field-date');
        const hiddenSlot = $('#field-slot');
        const next = $('#ngaygio-next');
        const label = $('#ngaygio-selected-label');
        const strip = $('#date-strip');
        const box = $('#slot-container');
        let requestId = 0;

        const pickChip = (item) => {
            $$('.slot-item', box).forEach((c) => c.classList.toggle('selected', c === item));
            hiddenSlot.value = item.dataset.slot;
            label.textContent = `${item.dataset.label} · ${item.dataset.doctorName}`;
            setText('sum-slot', item.dataset.label);
            setText('sum-doctor', item.dataset.doctorName);
            setText('sum-room', item.dataset.room);
            next.disabled = false;
        };

        const loadSlots = async (date) => {
            const myRequest = ++requestId;      // bỏ qua kết quả cũ nếu người dùng bấm ngày khác rất nhanh
            hiddenDate.value = date;
            hiddenSlot.value = '';
            label.textContent = 'chưa chọn khung giờ';
            next.disabled = true;
            box.innerHTML = '<div class="empty-state"><p>Đang tải khung giờ...</p></div>';

            try {
                const res = await fetch(box.dataset.url + '?date=' + encodeURIComponent(date),
                    { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (!res.ok) throw new Error('HTTP ' + res.status);
                const html = await res.text();
                if (myRequest !== requestId) return;
                box.innerHTML = html;
            } catch (err) {
                if (myRequest !== requestId) return;
                box.innerHTML =
                    '<div class="alert error" role="alert"><i data-lucide="circle-alert"></i>' +
                    '<div><strong>Không tải được khung giờ</strong>' +
                    '<p>Vui lòng kiểm tra kết nối và thử lại. Các lựa chọn của bạn vẫn được giữ.</p></div>' +
                    '<button type="button" class="btn btn-outline" data-retry>Thử lại</button></div>';
            }
            refreshIcons();
        };

        strip.addEventListener('click', (e) => {
            const btn = e.target.closest('button[data-date]');
            if (!btn || btn.disabled || btn.classList.contains('selected')) return;
            $$('button', strip).forEach((b) => b.classList.toggle('selected', b === btn));
            loadSlots(btn.dataset.date);
        });

        box.addEventListener('click', (e) => {
            if (e.target.closest('[data-retry]')) { loadSlots(hiddenDate.value); return; }
            if (e.target.closest('[data-doctor-open]')) return;   // mở modal, không đổi lựa chọn

            const chip = e.target.closest('.slot-chip');
            if (chip && !chip.disabled) pickChip(chip.closest('.slot-item'));
        });

        ngayGioForm.addEventListener('submit', (e) => { if (!hiddenSlot.value) e.preventDefault(); });
    }

    // ------------------------------------------------------------------ Chọn người khám

    const nguoiForm = $('#nguoi-form');
    if (nguoiForm) {
        const hidden = $('#field-patient');
        const next = $('#nguoi-next');
        const label = $('#nguoi-selected-name');
        const warn = $('#unverified-alert');

        nguoiForm.addEventListener('click', (e) => {
            const card = e.target.closest('.profile-select');
            if (!card) return;
            $$('.profile-select', nguoiForm).forEach((c) => c.classList.toggle('selected', c === card));
            hidden.value = card.dataset.id;
            label.textContent = card.dataset.name;
            const bookable = card.dataset.bookable === 'true';
            warn.hidden = bookable;          // hồ sơ bị thu hồi quyền: hiện cảnh báo và khóa nút Tiếp tục
            next.disabled = !bookable;
        });
    }

    // ------------------------------------------------------------------ Xác nhận

    const xacNhanForm = $('#xacnhan-form');
    if (xacNhanForm) {
        const agree = $('#agree');
        const submit = $('#xacnhan-submit');
        let sending = false;

        agree.addEventListener('change', () => { submit.disabled = !agree.checked; });

        // Ô khai báo bảo hiểm: checkbox được bọc trong label nên phải tự tô trạng thái chọn.
        xacNhanForm.addEventListener('change', (e) => {
            const box = e.target.closest('.declare input[type="checkbox"]');
            if (box) box.closest('.declare').classList.toggle('selected', box.checked);
        });

        xacNhanForm.addEventListener('submit', (e) => {
            if (sending || !agree.checked) { e.preventDefault(); return; }   // chặn gửi nhiều lần
            sending = true;
            submit.disabled = true;
            submit.innerHTML = '<span class="spinner"></span> Đang gửi yêu cầu...';
        });
    }
})();
