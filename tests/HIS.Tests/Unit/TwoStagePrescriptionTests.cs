using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.Domain;
using Xunit;

namespace HIS.Tests.Unit;

public class TwoStagePrescriptionTests
{
    [Fact]
    public void BuildInitialPrescription_WhenWaitingForLabs_PreservesStageOneMedications()
    {
        var record = new MedicalRecord { Id = 10, AppointmentId = 21, DoctorId = 3 };
        var stageOneItems = new List<CreatePrescriptionItemDto>
        {
            new() { MedicationId = 1, Quantity = 10, Dosage = "2 viên/ngày chia 2 lần" }
        };

        var prescription = DoctorService.BuildInitialPrescription(
            record,
            stageOneItems,
            "Đơn sơ bộ trước cận lâm sàng",
            new DateOnly(2026, 11, 28));

        Assert.NotNull(prescription);
        Assert.Equal("Đơn sơ bộ trước cận lâm sàng", prescription!.Note);
        var item = Assert.Single(prescription.PrescriptionItems);
        Assert.Equal(1, item.MedicationId);
        Assert.Equal(10, item.Quantity);
        Assert.Equal("2 viên/ngày chia 2 lần", item.Dosage);
    }

    [Fact]
    public void SyncPostLabPrescriptions_ReplaceMode_KeepsStageOneAndAddsStageTwoMedications()
    {
        var record = new MedicalRecord { Id = 10, AppointmentId = 21, DoctorId = 3 };
        var initial = DoctorService.BuildInitialPrescription(
            record,
            [new CreatePrescriptionItemDto { MedicationId = 1, Quantity = 10, Dosage = "Uống khi sốt > 38.5 độ" }],
            null,
            new DateOnly(2026, 11, 28))!;
        initial.Id = 100;

        var mutation = DoctorService.SyncPostLabPrescriptions(
            record,
            [
                new CreatePrescriptionItemDto { MedicationId = 1, Quantity = 10, Dosage = "Uống khi sốt > 38.5 độ" },
                new CreatePrescriptionItemDto { MedicationId = 2, Quantity = 35, Dosage = "1 viên/lần x 2 lần/ngày" }
            ],
            "Đơn hoàn tất sau X-quang",
            new DateOnly(2026, 11, 28),
            replaceExisting: true);

        Assert.Empty(mutation.AddedPrescriptions);
        Assert.Empty(mutation.RemovedPrescriptions);
        Assert.Empty(mutation.RemovedItems);
        var addedItem = Assert.Single(mutation.AddedItems);
        Assert.Equal(2, addedItem.MedicationId);
        Assert.Equal(35, addedItem.Quantity);

        var finalPrescription = Assert.Single(record.Prescriptions);
        Assert.Equal("Đơn hoàn tất sau X-quang", finalPrescription.Note);
        Assert.Equal(2, finalPrescription.PrescriptionItems.Count);
        Assert.Contains(finalPrescription.PrescriptionItems, i => i.MedicationId == 1 && i.Quantity == 10);
        Assert.Contains(finalPrescription.PrescriptionItems, i => i.MedicationId == 2 && i.Quantity == 35);
    }

    [Fact]
    public void SyncPostLabPrescriptions_AppendMode_PreservesStageOneWhenOnlyStageTwoSubmitted()
    {
        var record = new MedicalRecord { Id = 10, AppointmentId = 21, DoctorId = 3 };
        var initial = DoctorService.BuildInitialPrescription(
            record,
            [new CreatePrescriptionItemDto { MedicationId = 1, Quantity = 10, Dosage = "Sáng 1, chiều 1" }],
            null,
            new DateOnly(2026, 11, 28))!;
        initial.Id = 100;

        var mutation = DoctorService.SyncPostLabPrescriptions(
            record,
            [new CreatePrescriptionItemDto { MedicationId = 2, Quantity = 35, Dosage = "Sau ăn" }],
            null,
            new DateOnly(2026, 11, 28),
            replaceExisting: false);

        Assert.Empty(mutation.RemovedItems);
        Assert.Single(mutation.AddedItems);

        var finalPrescription = Assert.Single(record.Prescriptions);
        Assert.Equal(2, finalPrescription.PrescriptionItems.Count);
        Assert.Contains(finalPrescription.PrescriptionItems, i => i.MedicationId == 1 && i.Quantity == 10);
        Assert.Contains(finalPrescription.PrescriptionItems, i => i.MedicationId == 2 && i.Quantity == 35);
    }

    [Fact]
    public void SyncPostLabPrescriptions_ReplaceMode_AllowsDoctorToRemoveOrAdjustStageOneMedication()
    {
        var record = new MedicalRecord { Id = 10, AppointmentId = 21, DoctorId = 3 };
        DoctorService.BuildInitialPrescription(
            record,
            [
                new CreatePrescriptionItemDto { MedicationId = 1, Quantity = 10, Dosage = "Trước CLS" },
                new CreatePrescriptionItemDto { MedicationId = 3, Quantity = 5, Dosage = "Bỏ sau CLS" }
            ],
            null,
            new DateOnly(2026, 11, 28));

        var mutation = DoctorService.SyncPostLabPrescriptions(
            record,
            [new CreatePrescriptionItemDto { MedicationId = 1, Quantity = 20, Dosage = "Tăng liều sau CLS" }],
            null,
            new DateOnly(2026, 11, 28),
            replaceExisting: true);

        var removed = Assert.Single(mutation.RemovedItems);
        Assert.Equal(3, removed.MedicationId);

        var finalPrescription = Assert.Single(record.Prescriptions);
        var kept = Assert.Single(finalPrescription.PrescriptionItems);
        Assert.Equal(1, kept.MedicationId);
        Assert.Equal(20, kept.Quantity);
        Assert.Equal("Tăng liều sau CLS", kept.Dosage);
    }

    [Fact]
    public void CashierInvoiceDetailView_DoesNotRenderPostSettlementRefundButton()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var invoiceDetailViewPath = Path.Combine(repoRoot, "src", "HIS.Web", "Areas", "Staff", "Views", "Cashier", "_InvoiceDetail.cshtml");
        var content = File.ReadAllText(invoiceDetailViewPath);

        Assert.DoesNotContain("openRefundInvoiceModal", content);
        Assert.DoesNotContain("modal-refund-invoice", content);
        Assert.DoesNotContain("Hoàn viện phí / Điều chỉnh", content);
    }

    [Fact]
    public void StaffLayout_ExcludesCashierLinksFromNurseMenu()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var staffLayout = File.ReadAllText(Path.Combine(repoRoot, "src", "HIS.Web", "Views", "Shared", "_StaffLayout.cshtml"));
        var nurseBlockStart = staffLayout.IndexOf("else if (isNurse)", StringComparison.Ordinal);
        var doctorBlockStart = staffLayout.IndexOf("else if (isDoctor)", StringComparison.Ordinal);
        Assert.True(nurseBlockStart > 0 && doctorBlockStart > nurseBlockStart);

        var nurseBlock = staffLayout[nurseBlockStart..doctorBlockStart];
        Assert.DoesNotContain("Thu phí & BHYT", nurseBlock);
        Assert.DoesNotContain("Quản lý tạm ứng", nurseBlock);
        Assert.DoesNotContain("Sổ thu ngân", nurseBlock);
    }
}
