// Màn "Tạo hồ sơ" một màn hai nhánh. Panel không được chọn sẽ bị disabled để trình duyệt
// không gửi (và không bắt buộc kiểm tra) trường của nhánh còn lại; server vẫn kiểm tra lại toàn bộ.
(() => {
    const $ = (sel, root = document) => root.querySelector(sel);
    const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

    const form = $('[data-hoso-form]');
    if (!form) return;

    const panels = $$('[data-branch-panel]', form);
    const cards = $$('.branch-choice .branch', form);
    const submit = $('#hoso-submit');
    const dob = $('[data-dob]', form);
    const childNote = $('[data-child-note]', form);
    const OWN_ID_FROM_AGE = 14;   // khớp BookingOptions.OwnIdFromAge, chỉ để gợi ý trên màn hình

    const isValidDob = (value) => {
        const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(value.trim());
        if (!m) return false;
        const d = +m[1], mo = +m[2], y = +m[3];
        const date = new Date(y, mo - 1, d);
        return date.getFullYear() === y && date.getMonth() === mo - 1 && date.getDate() === d && date <= new Date();
    };

    const ageOf = (value) => {
        if (!isValidDob(value)) return null;
        const [d, mo, y] = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(value.trim()).slice(1).map(Number);
        const today = new Date();
        let years = today.getFullYear() - y;
        if (new Date(y + years, mo - 1, d) > today) years--;
        return years;
    };

    const activeBranch = () => ($$('input[name="Branch"]', form).find(r => r.checked)?.value ?? 'new');

    const rules = {
        Keyword: (v) => /^BN\d{1,6}$/i.test(v.replace(/\s+/g, '')),
        FullName: (v) => v.trim().length > 0,
        DateOfBirth: isValidDob,
        Phone: (v) => /^0\d{9,10}$/.test(v.replace(/\s+/g, '')),
        Email: (v) => /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(v.trim())
    };

    const applyBranch = () => {
        const branch = activeBranch();
        panels.forEach((panel) => {
            const on = panel.dataset.branchPanel === branch;
            panel.hidden = !on;
            $$('input, select, textarea', panel).forEach((el) => { el.disabled = !on; });
        });
        cards.forEach((card) => card.classList.toggle('selected', $('input', card)?.checked ?? false));
        if (submit) submit.textContent = branch === 'seen'
            ? submit.dataset.labelSeen ?? 'Gửi'
            : submit.dataset.labelNew ?? 'Lưu';
        if (childNote) childNote.hidden = branch !== 'new' || (ageOf(dob?.value ?? '') ?? 100) >= OWN_ID_FROM_AGE;
    };

    form.addEventListener('change', (e) => {
        if (e.target.name === 'Branch' || e.target === dob) applyBranch();
    });

    applyBranch();

    form.addEventListener('submit', (e) => {
        const branch = activeBranch();
        let ok = true;

        Object.entries(rules).forEach(([name, check]) => {
            const input = form.elements[name];
            if (!input || input.disabled) return;
            if (branch === 'new' && name === 'Keyword') return;
            // Nhánh "đã từng khám" chỉ có ô mã bệnh nhân là bắt buộc; email lấy theo hồ sơ đã lưu nên không nhập ở form.
            if (branch === 'seen' && name !== 'Keyword') return;

            const wrapper = input.closest('label');
            const valid = check(input.value);
            wrapper.classList.toggle('error', !valid);
            const msg = wrapper.querySelector('small');
            if (msg) msg.hidden = valid;
            if (!valid) ok = false;
        });

        if (!ok) { e.preventDefault(); return; }

        if (submit) submit.disabled = true;   // chống gửi nhiều lần
    });
})();
