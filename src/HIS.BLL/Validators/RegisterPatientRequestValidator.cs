using FluentValidation;
using HIS.BLL.DTOs;

namespace HIS.BLL.Validators;

public class RegisterPatientRequestValidator : AbstractValidator<RegisterPatientRequest>
{
    public RegisterPatientRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Tên đăng nhập không được để trống")
            .MinimumLength(3).WithMessage("Tên đăng nhập phải có ít nhất 3 ký tự")
            .MaximumLength(50).WithMessage("Tên đăng nhập tối đa 50 ký tự");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống")
            .MinimumLength(6).WithMessage("Mật khẩu phải có ít nhất 6 ký tự");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ và tên không được để trống")
            .MaximumLength(100).WithMessage("Họ và tên tối đa 100 ký tự");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Số điện thoại không được để trống")
            .Matches(@"^[0-9]{10,11}$").WithMessage("Số điện thoại không hợp lệ (10-11 số)");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email không hợp lệ")
            .When(x => !string.IsNullOrEmpty(x.Email));
    }
}

