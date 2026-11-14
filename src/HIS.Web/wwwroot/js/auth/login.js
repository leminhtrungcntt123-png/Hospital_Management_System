const container = document.getElementById('hisSliderContainer');
const btnGoRegister = document.getElementById('btnGoRegister');
const btnGoLogin = document.getElementById('btnGoLogin');

function switchToRegister() {
    container.classList.add('active');
}

function switchToLogin() {
    container.classList.remove('active');
}

if (btnGoRegister) {
    btnGoRegister.addEventListener('click', switchToRegister);
}

if (btnGoLogin) {
    btnGoLogin.addEventListener('click', switchToLogin);
}

// Tự động mở chế độ đăng ký nếu có param hoặc hash
if (
    window.location.hash === '#register' ||
    new URLSearchParams(window.location.search).get('mode') === 'register'
) {
    switchToRegister();
}

// Ẩn / hiện mật khẩu
function togglePasswordVisibility(inputId, iconId) {
    var p = document.getElementById(inputId);
    var icon = document.getElementById(iconId);
    if (!p || !icon) return;

    if (p.type === 'password') {
        p.type = 'text';
        icon.innerHTML =
            '<path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"></path><line x1="1" y1="1" x2="23" y2="23"></line>';
    } else {
        p.type = 'password';
        icon.innerHTML =
            '<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path><circle cx="12" cy="12" r="3"></circle>';
    }
}

function getCsrfToken() {
    var input = document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
}

// ================= Quản lý Lỗi Trực tiếp trên Ô Input (Field Error Highlighting) =================
function clearInputErrors(form) {
    if (!form) return;
    var errorGroups = form.querySelectorAll('.his-input-group.his-input-error');
    errorGroups.forEach(function (grp) {
        grp.classList.remove('his-input-error');
    });
    var errorSpans = form.querySelectorAll('.his-field-error');
    errorSpans.forEach(function (span) {
        span.innerText = '';
    });
}

function markInputError(inputId, message) {
    var inp = document.getElementById(inputId);
    if (!inp) return;
    var grp = inp.closest('.his-input-group');
    if (grp) {
        grp.classList.remove('his-input-error');
        void grp.offsetWidth; // Reflow để kích hoạt hiệu ứng rung nhẹ
        grp.classList.add('his-input-error');
    }
    var errSpan = document.getElementById(inputId + '-error');
    if (errSpan && message) {
        errSpan.innerText = message;
    }
}

function attachInputErrorClear(form) {
    if (!form) return;
    var inputs = form.querySelectorAll('input');
    inputs.forEach(function (inp) {
        inp.addEventListener('input', function () {
            var grp = inp.closest('.his-input-group');
            if (grp) {
                grp.classList.remove('his-input-error');
            }
            var errSpan = document.getElementById(inp.id + '-error');
            if (errSpan) {
                errSpan.innerText = '';
            }
        });
    });
}

// ================= Hệ thống Thông báo Toast =================
function showAuthToast(message, type, duration) {
    type = type || 'info';
    duration = duration || 4500;
    var toastContainer = document.getElementById('hisToastContainer');
    if (!toastContainer) {
        toastContainer = document.createElement('div');
        toastContainer.id = 'hisToastContainer';
        toastContainer.className = 'his-toast-container';
        document.body.appendChild(toastContainer);
    }

    var toast = document.createElement('div');
    toast.className = 'his-toast his-toast-' + type;

    var iconSvg = '';
    if (type === 'success') {
        iconSvg = '<svg class="his-toast-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>';
    } else if (type === 'error') {
        iconSvg = '<svg class="his-toast-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>';
    } else {
        iconSvg = '<svg class="his-toast-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>';
    }

    toast.innerHTML = iconSvg + '<div class="his-toast-msg">' + message + '</div><button type="button" class="his-toast-close" aria-label="Đóng">&times;</button>';
    toastContainer.appendChild(toast);

    var closeBtn = toast.querySelector('.his-toast-close');
    if (closeBtn) {
        closeBtn.addEventListener('click', function () {
            toast.classList.add('his-toast-hiding');
            setTimeout(function () {
                if (toast.parentNode) toast.parentNode.removeChild(toast);
            }, 250);
        });
    }

    setTimeout(function () {
        if (toast.parentNode) {
            toast.classList.add('his-toast-hiding');
            setTimeout(function () {
                if (toast.parentNode) toast.parentNode.removeChild(toast);
            }, 250);
        }
    }, duration);
}

function setInlineAlert(el, message, type) {
    if (!el) return;
    type = type || 'error';
    var iconSvg = type === 'success'
        ? '<svg class="his-alert-svg" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>'
        : '<svg class="his-alert-svg" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>';

    el.className = 'his-alert-' + type;
    el.innerHTML = iconSvg +
        '<div class="his-alert-text">' + message + '</div>' +
        '<button type="button" class="his-alert-close" aria-label="Đóng">&times;</button>';
    el.style.display = 'flex';

    var closeBtn = el.querySelector('.his-alert-close');
    if (closeBtn) {
        closeBtn.addEventListener('click', function () {
            el.style.display = 'none';
        });
    }
}

// ================= Hiệu ứng Loading Spinner =================
function showAuthLoading(message) {
    var overlay = document.getElementById('hisLoadingOverlay');
    if (overlay) {
        if (message) {
            var msgEl = document.getElementById('hisLoadingMessage');
            if (msgEl) msgEl.innerText = message;
        }
        overlay.style.display = 'flex';
        void overlay.offsetWidth;
        overlay.classList.add('active');
    }
}

function hideAuthLoading() {
    var overlay = document.getElementById('hisLoadingOverlay');
    if (overlay) {
        overlay.classList.remove('active');
        setTimeout(function () {
            overlay.style.display = 'none';
        }, 250);
    }
}

// Xử lý gửi form đăng nhập MVC
var loginForm = document.getElementById('loginForm');
if (loginForm) {
    attachInputErrorClear(loginForm);
    loginForm.addEventListener('submit', function (e) {
        clearInputErrors(loginForm);
        var usernameInput = document.getElementById('login-username');
        var passwordInput = document.getElementById('login-password');
        if (!usernameInput || !passwordInput) return;
        var uVal = usernameInput.value.trim();
        var pVal = passwordInput.value;

        var firstInvalid = null;
        if (!uVal) {
            markInputError('login-username', 'Vui lòng nhập tên đăng nhập hoặc email.');
            firstInvalid = firstInvalid || usernameInput;
        }
        if (!pVal) {
            markInputError('login-password', 'Vui lòng nhập mật khẩu.');
            firstInvalid = firstInvalid || passwordInput;
        }

        if (firstInvalid) {
            e.preventDefault();
            firstInvalid.focus();
            return;
        }

        var btn = document.getElementById('btnLoginSubmit') || loginForm.querySelector('button[type="submit"]');
        if (btn) {
            btn.classList.add('his-btn-loading');
            btn.disabled = true;
            btn.innerHTML = '<span class="his-spinner-inline" aria-hidden="true"></span><span>Đang đăng nhập...</span>';
        }
        showAuthLoading('Hệ thống đang xác thực danh tính...');
    });
}

// Xử lý gửi đăng ký qua API (Sai ô nào hiện đỏ ô đó)
async function handleRegisterSubmit(e) {
    e.preventDefault();
    var registerForm = e.target;
    clearInputErrors(registerForm);

    var fullnameInp = document.getElementById('reg-fullname');
    var dobInp = document.getElementById('regDateOfBirth');
    var phoneInp = document.getElementById('reg-phone');
    var emailInp = document.getElementById('reg-email');
    var usernameInp = document.getElementById('reg-username');
    var passwordInp = document.getElementById('reg-password');

    var fullname = fullnameInp.value.trim();
    var dob = dobInp.value;
    var phone = phoneInp.value.trim();
    var email = emailInp.value.trim();
    var username = usernameInp.value.trim();
    var password = passwordInp.value;

    var btn = registerForm.querySelector('button[type="submit"]');
    var originalBtnHtml = btn.innerHTML;

    // Kiểm tra các trường bắt buộc
    var firstInvalid = null;
    if (!fullname) {
        markInputError('reg-fullname', 'Vui lòng nhập họ và tên bệnh nhân.');
        firstInvalid = firstInvalid || fullnameInp;
    }
    if (!dob) {
        markInputError('regDateOfBirth', 'Vui lòng chọn ngày sinh.');
        firstInvalid = firstInvalid || dobInp;
    }
    if (!phone) {
        markInputError('reg-phone', 'Vui lòng nhập số điện thoại liên hệ.');
        firstInvalid = firstInvalid || phoneInp;
    }
    if (!username) {
        markInputError('reg-username', 'Vui lòng nhập tên tài khoản.');
        firstInvalid = firstInvalid || usernameInp;
    }
    if (!password) {
        markInputError('reg-password', 'Vui lòng nhập mật khẩu.');
        firstInvalid = firstInvalid || passwordInp;
    }

    if (firstInvalid) {
        firstInvalid.focus();
        return;
    }

    // Kiểm tra định dạng tên đăng nhập (3-50 ký tự không dấu, không khoảng trắng)
    if (!/^[a-zA-Z0-9_.]{3,50}$/.test(username)) {
        markInputError('reg-username', 'Tên đăng nhập từ 3–50 ký tự chữ, số, dấu chấm/gạch dưới.');
        usernameInp.focus();
        return;
    }

    // Kiểm tra độ dài mật khẩu (tối thiểu 6 ký tự)
    if (password.length < 6) {
        markInputError('reg-password', 'Mật khẩu phải có tối thiểu 6 ký tự.');
        passwordInp.focus();
        return;
    }

    // Kiểm tra định dạng số điện thoại
    if (!/^[0-9+ ()-]{9,15}$/.test(phone)) {
        markInputError('reg-phone', 'Số điện thoại không hợp lệ (9–15 chữ số).');
        phoneInp.focus();
        return;
    }

    // Kiểm tra định dạng email nếu có nhập
    if (email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
        markInputError('reg-email', 'Địa chỉ email không đúng định dạng.');
        emailInp.focus();
        return;
    }

    btn.innerHTML = '<span class="his-spinner-inline" aria-hidden="true"></span><span>Đang đăng ký tài khoản...</span>';
    btn.disabled = true;

    try {
        var response = await fetch(container.dataset.registerUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: getCsrfToken(),
            },
            body: JSON.stringify({
                dateOfBirth: dob,
                fullName: fullname,
                phoneNumber: phone,
                email: email || null,
                username: username,
                password: password,
            }),
        });

        var data = await response.json();
        if (!data.success) {
            var msg = data.message || 'Đăng ký không thành công.';
            var msgLower = msg.toLowerCase();

            // Nhận diện lỗi từ server và làm đỏ chính xác ô đó kèm chữ đỏ bên dưới
            var matched = false;
            if (msgLower.includes('tên đăng nhập') || msgLower.includes('tài khoản')) {
                markInputError('reg-username', msg);
                usernameInp.focus();
                matched = true;
            } else if (msgLower.includes('mật khẩu')) {
                markInputError('reg-password', msg);
                passwordInp.focus();
                matched = true;
            } else if (msgLower.includes('ngày sinh')) {
                markInputError('regDateOfBirth', msg);
                dobInp.focus();
                matched = true;
            } else if (msgLower.includes('số điện thoại') || msgLower.includes('điện thoại')) {
                markInputError('reg-phone', msg);
                phoneInp.focus();
                matched = true;
            } else if (msgLower.includes('email')) {
                markInputError('reg-email', msg);
                emailInp.focus();
                matched = true;
            } else if (msgLower.includes('họ và tên') || msgLower.includes('bệnh nhân')) {
                markInputError('reg-fullname', msg);
                fullnameInp.focus();
                matched = true;
            }

            if (!matched) {
                showAuthToast(msg, 'error');
            }
            btn.innerHTML = originalBtnHtml;
            btn.disabled = false;
            return;
        }

        // Thành công: thông báo Toast và chuyển sang Đăng nhập
        showAuthToast(data.message, 'success');
        switchToLogin();

        var u = document.getElementById('login-username');
        var p = document.getElementById('login-password');
        if (u) u.value = username;
        if (p) {
            p.value = password;
            p.focus();
        }

        registerForm.reset();
        clearInputErrors(registerForm);
    } catch {
        showAuthToast('Không thể kết nối tới máy chủ. Vui lòng thử lại sau.', 'error');
    } finally {
        btn.innerHTML = originalBtnHtml;
        btn.disabled = false;
    }
}

// ================= Quản lý Modal Quên Mật Khẩu =================
const forgotModal = document.getElementById('hisForgotModal');

function openForgotModal() {
    var inputVal = document.getElementById('login-username').value;
    if (inputVal) {
        document.getElementById('forgot-account').value = inputVal;
    }
    backToStep1();
    forgotModal.classList.add('active');
    setTimeout(function () {
        var el = document.getElementById('forgot-account');
        if (el) el.focus();
    }, 120);
}

function closeForgotModal() {
    forgotModal.classList.remove('active');
}

function backToStep1() {
    document.getElementById('forgotStep1').style.display = 'block';
    document.getElementById('forgotStep2').style.display = 'none';
    document.getElementById('forgotStep3').style.display = 'none';
    document.getElementById('step1Alert').style.display = 'none';
}

async function handleForgotSubmit(e) {
    e.preventDefault();
    clearInputErrors(e.target);
    var accountInp = document.getElementById('forgot-account');
    var account = accountInp.value.trim();
    if (!account) {
        markInputError('forgot-account', 'Vui lòng nhập email hoặc tên đăng nhập.');
        accountInp.focus();
        return;
    }

    var alertEl = document.getElementById('step1Alert');
    var btnText = document.getElementById('btnSendOtpText');
    alertEl.style.display = 'none';
    btnText.innerHTML = '<span class="his-spinner-inline" aria-hidden="true"></span>Đang gửi mã OTP...';

    try {
        var response = await fetch(container.dataset.requestResetUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: getCsrfToken(),
            },
            body: JSON.stringify({ emailOrUsername: account }),
        });

        var data = await response.json();
        if (!data.success) {
            markInputError('forgot-account', data.message);
            accountInp.focus();
            btnText.innerText = 'Gửi Mã Xác Thực OTP';
            return;
        }

        // Chuyển sang Bước 2
        document.getElementById('step2AccountHidden').value = account;

        document.getElementById('forgotStep1').style.display = 'none';
        document.getElementById('forgotStep2').style.display = 'block';
        document.getElementById('step2Alert').style.display = 'none';
        document.getElementById('step2Otp').value = '';
        document.getElementById('step2NewPass').value = '';
        document.getElementById('step2ConfirmPass').value = '';
        btnText.innerText = 'Gửi Mã Xác Thực OTP';
        document.getElementById('step2Otp').focus();
    } catch {
        showAuthToast('Lỗi kết nối tới máy chủ. Vui lòng thử lại sau.', 'error');
        btnText.innerText = 'Gửi Mã Xác Thực OTP';
    }
}

async function resendOtp() {
    var account = document.getElementById('step2AccountHidden').value;
    if (!account) return;
    var alertEl = document.getElementById('step2Alert');
    alertEl.style.display = 'none';

    try {
        var response = await fetch(container.dataset.requestResetUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: getCsrfToken(),
            },
            body: JSON.stringify({ emailOrUsername: account }),
        });

        var data = await response.json();
        if (data.success) {
            showAuthToast(data.message, 'info');
        } else {
            showAuthToast(data.message, 'error');
        }
    } catch {
        showAuthToast('Không thể gửi lại mã OTP. Vui lòng kiểm tra kết nối mạng.', 'error');
    }
}

async function handleResetSubmit(e) {
    e.preventDefault();
    clearInputErrors(e.target);
    var account = document.getElementById('step2AccountHidden').value.trim();
    var otpInp = document.getElementById('step2Otp');
    var newPassInp = document.getElementById('step2NewPass');
    var confirmPassInp = document.getElementById('step2ConfirmPass');

    var otp = otpInp.value.trim();
    var newPass = newPassInp.value;
    var confirmPass = confirmPassInp.value;
    var btnText = document.getElementById('btnConfirmResetText');

    if (!otp) {
        markInputError('step2Otp', 'Vui lòng nhập mã OTP.');
        otpInp.focus();
        return;
    }
    if (!newPass) {
        markInputError('step2NewPass', 'Vui lòng nhập mật khẩu mới.');
        newPassInp.focus();
        return;
    }
    if (newPass !== confirmPass) {
        markInputError('step2ConfirmPass', 'Mật khẩu xác nhận không khớp.');
        confirmPassInp.focus();
        return;
    }

    btnText.innerHTML = '<span class="his-spinner-inline" aria-hidden="true"></span>Đang xác nhận...';

    try {
        var response = await fetch(container.dataset.confirmResetUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: getCsrfToken(),
            },
            body: JSON.stringify({
                emailOrUsername: account,
                otp: otp,
                newPassword: newPass,
            }),
        });

        var data = await response.json();
        if (!data.success) {
            var msg = data.message || '';
            var msgLower = msg.toLowerCase();
            if (msgLower.includes('otp')) {
                markInputError('step2Otp', msg);
                otpInp.focus();
            } else if (msgLower.includes('mật khẩu')) {
                markInputError('step2NewPass', msg);
                newPassInp.focus();
            } else {
                showAuthToast(msg, 'error');
            }
            btnText.innerText = 'Xác Nhận Đổi Mật Khẩu';
            return;
        }

        // Thành công: chuyển sang Step 3
        document.getElementById('forgotStep2').style.display = 'none';
        document.getElementById('forgotStep3').style.display = 'block';
        btnText.innerText = 'Xác Nhận Đổi Mật Khẩu';
    } catch {
        showAuthToast('Lỗi kết nối tới máy chủ. Vui lòng thử lại sau.', 'error');
        btnText.innerText = 'Xác Nhận Đổi Mật Khẩu';
    }
}

function finishAndLogin() {
    var account = document.getElementById('step2AccountHidden').value;
    closeForgotModal();
    switchToLogin();
    var u = document.getElementById('login-username');
    var p = document.getElementById('login-password');
    if (u) u.value = account;
    if (p) {
        p.value = '';
        p.focus();
    }
}

// Tự động mở modal quên mật khẩu nếu có param #forgot hoặc mode=forgot
if (
    window.location.hash === '#forgot' ||
    new URLSearchParams(window.location.search).get('mode') === 'forgot'
) {
    openForgotModal();
}

const authActions = {
    switchToRegister,
    switchToLogin,
    openForgotModal,
    closeForgotModal,
    backToStep1,
    resendOtp,
    finishAndLogin,
};
document.addEventListener('click', (event) => {
    const trigger = event.target.closest('[data-auth-action]');
    if (!trigger) return;
    event.preventDefault();
    if (trigger.dataset.authAction === 'password-toggle') {
        togglePasswordVisibility(trigger.dataset.input, trigger.dataset.icon);
    } else {
        authActions[trigger.dataset.authAction]?.();
    }
});

var regFormEl = document.getElementById('registerForm');
if (regFormEl) {
    attachInputErrorClear(regFormEl);
    regFormEl.addEventListener('submit', handleRegisterSubmit);
}

var step1FormEl = document.getElementById('step1Form');
if (step1FormEl) {
    attachInputErrorClear(step1FormEl);
    step1FormEl.addEventListener('submit', handleForgotSubmit);
}

var step2FormEl = document.getElementById('step2Form');
if (step2FormEl) {
    attachInputErrorClear(step2FormEl);
    step2FormEl.addEventListener('submit', handleResetSubmit);
}
