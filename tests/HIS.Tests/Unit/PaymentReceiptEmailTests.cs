using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using HIS.Web.Areas.Staff.Controllers;
using HIS.Web.Workers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace HIS.Tests.Unit;

public class PaymentReceiptEmailTests(ITestOutputHelper output)
{
    [Fact]
    public void BuildReceiptEmailHtml_IncludesFinancialTotals_EscapesHtml_AndExcludesClinicalDiagnosis()
    {
        var detail = new InvoiceDetailDto(
            InvoiceId: 42,
            InvoiceNumber: "HD-000042",
            AppointmentId: 10,
            AppointmentCode: "HIS-20261127-00010",
            ExamDate: new DateOnly(2026, 11, 27),
            PatientId: 7,
            PatientCode: "00000007",
            PatientName: "Nguyễn <script>alert(1)</script> An",
            PatientPhone: "0901234567",
            DateOfBirth: new DateOnly(1992, 6, 15),
            Age: 34,
            Gender: "Nam",
            DoctorName: "BS. Trần Văn B",
            DepartmentName: "Khoa Nội tổng quát",
            AppointmentTime: new DateTime(2026, 11, 27, 9, 0, 0),
            HasInsurance: true,
            InsuranceCardNumber: "DN4790011223344",
            CoveragePct: 80m,
            RegisteredFacility: "Bệnh viện Đa khoa MediCare",
            Items:
            [
                new InvoiceItemDto(1, "Công khám", "Khám nội tổng quát", 150000m, 1, 150000m, true, 80m, 120000m, 30000m),
                new InvoiceItemDto(2, "Thuốc kê đơn", "Thuốc Nhạy Cảm SecretMed (Viên)", 50000m, 2, 100000m, true, 80m, 80000m, 20000m)
            ],
            TotalAmount: 250000m,
            InsuranceCoveredAmount: 200000m,
            PatientAmount: 50000m,
            Status: InvoiceStatus.Paid,
            PaidAt: new DateTime(2026, 11, 27, 10, 15, 0),
            PaymentMethod: "Tiền mặt",
            TransactionCode: "PT-00000099",
            CashierName: "ketoan",
            AvailableDepositBalance: 0m,
            PatientDeposits: null,
            DepositDeducted: 20000m,
            AdditionalPaid: 30000m,
            RefundedAmount: 0m,
            NationalId: "079092000111",
            Address: "Quận 1, TP.HCM",
            Diagnosis: "[B20] Chẩn đoán nhạy cảm tuyệt đối không được gửi email",
            PatientEmail: "an@example.com",
            PatientAccountId: 12,
            DepositRefundedAmount: 0m);

        var html = PaymentReceiptEmailQueue.BuildReceiptEmailHtml(detail);

        Assert.Contains("HD-000042", html);
        Assert.Contains("HIS-20261127-00010", html);
        Assert.Contains("Khoa Nội tổng quát", html);
        Assert.Contains("Công khám", html);
        Assert.Contains("Thuốc kê đơn", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("Chẩn đoán nhạy cảm", html);
        Assert.DoesNotContain("SecretMed", html);
    }

    [Fact]
    public void CashierController_RequiresCashierAccessPolicy()
    {
        var authorizeAttr = typeof(CashierController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("CashierAccess", authorizeAttr!.Policy);
    }

    [Fact]
    public async Task SettleDischargeAsync_InvalidReceiptEmail_ReturnsValidationErrorWithoutDatabaseCall()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        await using var db = new AppDbContext(opts, new VietnamTimeProvider());
        var service = new BillingService(db);

        var result = await service.SettleDischargeAsync(new SettleDischargeRequest(
            AppointmentId: 1,
            Method: PaymentMethod.Cash,
            ReferenceCode: null,
            Note: null,
            CashierAccountId: 1,
            IdempotencyKey: "ik-invalid-email",
            ApprovedBy: null,
            ReceiptEmail: "khong-phai-email",
            SendReceiptEmail: true));

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_EMAIL", result.Error.Code);
    }

    [Fact]
    public async Task UpsertPatientInsuranceCardAsync_ValidatesCardNumberAndDateRange()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        await using var db = new AppDbContext(opts, new VietnamTimeProvider());
        var service = new BillingService(db);

        var badCardRes = await service.UpsertPatientInsuranceCardAsync(new UpsertPatientInsuranceCardRequest(
            PatientId: 1,
            CardNumber: "123",
            InsurancePlanId: 1,
            RegisteredFacility: "BV MediCare",
            IsRegisteredHere: true,
            ValidFrom: new DateOnly(2026, 1, 1),
            ValidTo: new DateOnly(2026, 12, 31)));

        Assert.False(badCardRes.IsSuccess);
        Assert.Equal("INVALID_CARD_NUMBER", badCardRes.Error.Code);

        var badDatesRes = await service.UpsertPatientInsuranceCardAsync(new UpsertPatientInsuranceCardRequest(
            PatientId: 1,
            CardNumber: "DN4797938328001",
            InsurancePlanId: 1,
            RegisteredFacility: "BV MediCare",
            IsRegisteredHere: true,
            ValidFrom: new DateOnly(2026, 12, 31),
            ValidTo: new DateOnly(2026, 1, 1)));

        Assert.False(badDatesRes.IsSuccess);
        Assert.Equal("INVALID_DATES", badDatesRes.Error.Code);
    }

    [Fact]
    public void CashierController_UpsertInsuranceCard_RequiresHttpPostAndAntiForgeryToken()
    {
        var method = typeof(CashierController).GetMethod(nameof(CashierController.UpsertInsuranceCard));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task PaymentReceiptEmailQueue_EndToEndWithLocalSmtpServer_DeliversRealMimeEmail()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int smtpPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        var receivedMailTcs = new TaskCompletionSource<(string MailFrom, string RcptTo, string RawData)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var smtpServerTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

            await writer.WriteLineAsync("220 localhost ESMTP MediCareTestServer");
            string mailFrom = string.Empty;
            string rcptTo = string.Empty;
            var dataBuilder = new StringBuilder();

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 localhost");
                }
                else if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase))
                {
                    mailFrom = line;
                    await writer.WriteLineAsync("250 OK");
                }
                else if (line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                {
                    rcptTo = line;
                    await writer.WriteLineAsync("250 OK");
                }
                else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 Start mail input; end with <CRLF>.<CRLF>");
                    string? dataLine;
                    while ((dataLine = await reader.ReadLineAsync()) != null)
                    {
                        if (dataLine == ".")
                        {
                            break;
                        }
                        dataBuilder.AppendLine(dataLine);
                    }
                    await writer.WriteLineAsync("250 OK: queued");
                    receivedMailTcs.TrySetResult((mailFrom, rcptTo, dataBuilder.ToString()));
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 Bye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }
        });

        var sampleDetail = new InvoiceDetailDto(
            InvoiceId: 88,
            InvoiceNumber: "HD-000088",
            AppointmentId: 55,
            AppointmentCode: "HIS-20261127-00055",
            ExamDate: new DateOnly(2026, 11, 27),
            PatientId: 9,
            PatientCode: "00000009",
            PatientName: "Lê Minh Chí",
            PatientPhone: "0890201234",
            DateOfBirth: new DateOnly(1995, 2, 14),
            Age: 31,
            Gender: "Nam",
            DoctorName: "BS. Nguyễn Hoàng Nam",
            DepartmentName: "Khoa Nội tổng quát",
            AppointmentTime: new DateTime(2026, 11, 27, 10, 0, 0),
            HasInsurance: true,
            InsuranceCardNumber: "DN4797938328001",
            CoveragePct: 80m,
            RegisteredFacility: "Bệnh viện Đa khoa MediCare",
            Items:
            [
                new InvoiceItemDto(1, "Công khám", "Khám nội tổng quát", 150000m, 1, 150000m, true, 80m, 120000m, 30000m),
                new InvoiceItemDto(2, "Cận lâm sàng", "Tổng phân tích tế bào máu", 100000m, 1, 100000m, true, 80m, 80000m, 20000m)
            ],
            TotalAmount: 250000m,
            InsuranceCoveredAmount: 200000m,
            PatientAmount: 50000m,
            Status: InvoiceStatus.Paid,
            PaidAt: new DateTime(2026, 11, 27, 11, 45, 0),
            PaymentMethod: "Chuyển khoản (VietQR)",
            TransactionCode: "PT-00000123",
            CashierName: "ketoan",
            AvailableDepositBalance: 0m,
            PatientDeposits: null,
            DepositDeducted: 20000m,
            AdditionalPaid: 30000m,
            RefundedAmount: 0m,
            NationalId: "079085001234",
            Address: "Quận 1, TP.HCM",
            Diagnosis: "[I10] Tăng huyết áp vô căn",
            PatientEmail: "patient.test@medicare.local",
            PatientAccountId: 21,
            DepositRefundedAmount: 0m);

        var billingMock = new Mock<IBillingService>();
        billingMock
            .Setup(b => b.GetInvoiceDetailAsync(55, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(sampleDetail));

        var notificationMock = new Mock<INotificationService>();
        notificationMock
            .Setup(n => n.SendInAppNotificationAsync(21, 55, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        notificationMock
            .Setup(n => n.SendEmailNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string to, string subj, string body, CancellationToken token) =>
            {
                var snapMock = new Mock<IOptionsSnapshot<EmailOptions>>();
                snapMock.Setup(s => s.Value).Returns(new EmailOptions
                {
                    Enabled = true,
                    SmtpHost = "127.0.0.1",
                    SmtpPort = smtpPort,
                    EnableSsl = false,
                    UserName = "billing@medicare.local",
                    Password = "local-smtp-password",
                    FromEmail = "billing@medicare.local",
                    FromDisplayName = "Benh vien MediCare"
                });
                var sender = new SmtpEmailSender(snapMock.Object, NullLogger<SmtpEmailSender>.Instance);
                await sender.SendEmailAsync(to, subj, body, token);
                return Result.Success();
            });

        var services = new ServiceCollection();
        services.Configure<EmailOptions>(o =>
        {
            o.Enabled = true;
            o.SmtpHost = "127.0.0.1";
            o.SmtpPort = smtpPort;
            o.EnableSsl = false;
            o.UserName = "billing@medicare.local";
            o.Password = "local-smtp-password";
        });
        services.AddScoped(_ => billingMock.Object);
        services.AddScoped(_ => notificationMock.Object);

        await using var provider = services.BuildServiceProvider();
        var queue = new PaymentReceiptEmailQueue(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PaymentReceiptEmailQueue>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await queue.StartAsync(cts.Token);

        var enqueued = queue.Enqueue(new PaymentReceiptDispatchJob(
            AppointmentId: 55,
            InvoiceId: 88,
            InvoiceNumber: "HD-000088",
            ToEmail: "patient.test@medicare.local",
            PatientAccountId: 21,
            PatientPayable: 50000m,
            DepositDeducted: 20000m,
            AdditionalPaid: 30000m,
            RefundedToPatient: 0m));
        Assert.True(enqueued);

        var completed = await Task.WhenAny(receivedMailTcs.Task, Task.Delay(TimeSpan.FromSeconds(8), cts.Token));
        Assert.Same(receivedMailTcs.Task, completed);

        var (mailFrom, rcptTo, rawMime) = await receivedMailTcs.Task;
        await queue.StopAsync(CancellationToken.None);
        listener.Stop();
        await smtpServerTask;

        Assert.Contains("billing@medicare.local", mailFrom, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("patient.test@medicare.local", rcptTo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MIME-Version: 1.0", rawMime, StringComparison.OrdinalIgnoreCase);

        // Giải mã phần thân MIME Base64 do SmtpClient đóng gói khi gửi nội dung tiếng Việt UTF-8
        int separatorIdx = rawMime.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (separatorIdx < 0)
        {
            separatorIdx = rawMime.IndexOf("\n\n", StringComparison.Ordinal);
        }
        Assert.True(separatorIdx > 0);
        string rawBodySection = rawMime[(separatorIdx + 2)..].Trim();
        string decodedHtml = rawMime.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(rawBodySection.Replace("\r", "").Replace("\n", "")))
            : rawBodySection;

        Assert.Contains("HD-000088", decodedHtml);
        Assert.Contains("HIS-20261127-00055", decodedHtml);
        Assert.Contains("Lê Minh Chí", decodedHtml);
        Assert.Contains("Khoa Nội tổng quát", decodedHtml);
        Assert.DoesNotContain("Tăng huyết áp vô căn", decodedHtml);

        notificationMock.Verify(
            n => n.SendInAppNotificationAsync(21, 55, It.Is<string>(t => t.Contains("HD-000088")), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PaymentReceiptEmailQueue_WhenEmailDisabledOrSmtpFails_StillSendsInAppNotificationAndDoesNotCrash()
    {
        var handledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var notificationMock = new Mock<INotificationService>();
        notificationMock
            .Setup(n => n.SendInAppNotificationAsync(33, 77, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => handledTcs.TrySetResult(true))
            .ReturnsAsync(Result.Success());

        var services = new ServiceCollection();
        services.Configure<EmailOptions>(o => o.Enabled = false);
        services.AddScoped(_ => Mock.Of<IBillingService>());
        services.AddScoped(_ => notificationMock.Object);

        await using var provider = services.BuildServiceProvider();
        var queue = new PaymentReceiptEmailQueue(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PaymentReceiptEmailQueue>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await queue.StartAsync(cts.Token);

        Assert.True(queue.Enqueue(new PaymentReceiptDispatchJob(
            AppointmentId: 77,
            InvoiceId: 99,
            InvoiceNumber: "HD-000099",
            ToEmail: "guest@example.com",
            PatientAccountId: 33,
            PatientPayable: 150000m,
            DepositDeducted: 0m,
            AdditionalPaid: 150000m,
            RefundedToPatient: 0m)));

        var finished = await Task.WhenAny(handledTcs.Task, Task.Delay(TimeSpan.FromSeconds(4), cts.Token));
        Assert.Same(handledTcs.Task, finished);
        await queue.StopAsync(CancellationToken.None);

        notificationMock.Verify(
            n => n.SendEmailNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LiveUserSecretsSmtpSmokeTest_WhenExplicitlyRequestedViaEnvVar_SendsFullReceiptEmail()
    {
        var recipient = Environment.GetEnvironmentVariable("HIS_SEND_LIVE_TEST_EMAIL");
        output.WriteLine($"[DIAG] HIS_SEND_LIVE_TEST_EMAIL = '{recipient ?? "<null>"}'");
        if (string.IsNullOrWhiteSpace(recipient))
        {
            output.WriteLine("[DIAG] Skipped because env var was empty.");
            return;
        }

        var config = new ConfigurationBuilder()
            .AddUserSecrets("45dd9264-5350-4454-956b-4706b1fa30e1")
            .Build();

        var emailOpts = new EmailOptions();
        config.GetSection(EmailOptions.SectionName).Bind(emailOpts);
        output.WriteLine($"[DIAG] Loaded UserSecrets: Enabled={emailOpts.Enabled}, Host={emailOpts.SmtpHost}:{emailOpts.SmtpPort}, User={emailOpts.UserName}, PassLen={emailOpts.Password?.Length ?? 0}");
        Assert.True(emailOpts.Enabled, "Email:Enabled trong User Secrets phải là true.");
        Assert.False(string.IsNullOrWhiteSpace(emailOpts.UserName));

        var detail = new InvoiceDetailDto(
            InvoiceId: 108,
            InvoiceNumber: "HD-000108",
            AppointmentId: 205,
            AppointmentCode: "HIS-20261127-00205",
            ExamDate: new DateOnly(2026, 11, 27),
            PatientId: 1,
            PatientCode: "00000001",
            PatientName: "Lê Minh Chí",
            PatientPhone: "0890201234",
            DateOfBirth: new DateOnly(1995, 2, 14),
            Age: 31,
            Gender: "Nam",
            DoctorName: "BS. Nguyễn Hoàng Nam",
            DepartmentName: "Khoa Nội tổng quát",
            AppointmentTime: DateTime.Now,
            HasInsurance: true,
            InsuranceCardNumber: "DN4797938328001",
            CoveragePct: 80m,
            RegisteredFacility: "Bệnh viện Đa khoa MediCare",
            Items:
            [
                new InvoiceItemDto(1, "Công khám", "Khám nội tổng quát", 150000m, 1, 150000m, true, 80m, 120000m, 30000m),
                new InvoiceItemDto(2, "Cận lâm sàng", "Tổng phân tích tế bào máu", 100000m, 1, 100000m, true, 80m, 80000m, 20000m),
                new InvoiceItemDto(3, "Thuốc kê đơn", "Thuốc điều trị ngoại trú", 150000m, 1, 150000m, true, 80m, 120000m, 30000m)
            ],
            TotalAmount: 400000m,
            InsuranceCoveredAmount: 320000m,
            PatientAmount: 80000m,
            Status: InvoiceStatus.Paid,
            PaidAt: DateTime.Now,
            PaymentMethod: "Cấn trừ tạm ứng + Thu thêm (Chuyển khoản VietQR)",
            TransactionCode: "PT-00000512",
            CashierName: "ketoan",
            AvailableDepositBalance: 0m,
            PatientDeposits: null,
            DepositDeducted: 50000m,
            AdditionalPaid: 30000m,
            RefundedAmount: 0m,
            NationalId: "079085001234",
            Address: "Quận 1, TP. Hồ Chí Minh",
            Diagnosis: "[I10] Chẩn đoán ẩn bảo mật",
            PatientEmail: recipient.Trim(),
            PatientAccountId: null,
            DepositRefundedAmount: 0m);

        var snap = new Mock<IOptionsSnapshot<EmailOptions>>();
        snap.Setup(s => s.Value).Returns(emailOpts);

        var sender = new SmtpEmailSender(snap.Object, NullLogger<SmtpEmailSender>.Instance);
        var html = PaymentReceiptEmailQueue.BuildReceiptEmailHtml(detail);
        output.WriteLine($"[DIAG] HTML length = {html.Length} chars. Calling SendEmailAsync to {recipient.Trim()}...");
        await sender.SendEmailAsync(recipient.Trim(), $"[MediCare HIS] Biên lai thanh toán viện phí HD-000108 ({DateTime.Now:HH:mm:ss})", html);
        output.WriteLine("[DIAG] SendEmailAsync completed without exception!");
    }
}
