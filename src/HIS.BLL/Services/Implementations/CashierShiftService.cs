using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HIS.BLL.Services;

public class CashierShiftService : ICashierShiftService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public CashierShiftService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    private DateTime Now => DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);

    public async Task<CashierShiftDto?> GetCurrentShiftAsync(int cashierAccountId, CancellationToken ct = default)
    {
        var shift = await _context.CashierShifts
            .AsNoTracking()
            .Include(s => s.Cashier)
            .Where(s => s.CashierId == cashierAccountId && s.Status == ShiftStatus.Open)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync(ct);

        if (shift == null) return null;

        return MapToDto(shift);
    }

    public async Task<Result<CashierShiftDto>> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default)
    {
        if (request.OpeningCash < 0)
        {
            return Result.Failure<CashierShiftDto>("INVALID_OPENING_CASH", "Số tiền đầu ca không được âm.");
        }

        var existing = await _context.CashierShifts
            .FirstOrDefaultAsync(s => s.CashierId == request.CashierAccountId && s.Status == ShiftStatus.Open, ct);

        if (existing != null)
        {
            return Result.Failure<CashierShiftDto>("SHIFT_ALREADY_OPEN", "Bạn đang có một ca làm việc chưa kết thúc.");
        }

        var shift = new CashierShift
        {
            CashierId = request.CashierAccountId,
            OpenedAt = Now,
            OpeningCash = request.OpeningCash,
            Status = ShiftStatus.Open,
            Note = request.Note?.Trim()
        };

        _context.CashierShifts.Add(shift);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _context.Entry(shift).State = EntityState.Detached;
            return Result.Failure<CashierShiftDto>("SHIFT_ALREADY_OPEN", "Bạn đang có một ca làm việc chưa kết thúc.");
        }

        await _context.Entry(shift).Reference(s => s.Cashier).LoadAsync(ct);

        return Result.Success(MapToDto(shift));
    }

    public async Task<Result<CashierShiftDto>> CloseShiftAsync(CloseShiftRequest request, CancellationToken ct = default)
    {
        if (request.CountedCash < 0)
        {
            return Result.Failure<CashierShiftDto>("INVALID_COUNTED_CASH", "Số tiền kiểm đếm thực tế không được âm.");
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync(ct)
                : null;

            var shift = await _context.CashierShifts
                .FromSqlInterpolated($"SELECT * FROM cashier_shifts WHERE id = {request.ShiftId} FOR UPDATE")
                .Include(s => s.Cashier)
                .FirstOrDefaultAsync(ct);

            if (shift == null)
            {
                return Result.Failure<CashierShiftDto>("NOT_FOUND", "Không tìm thấy thông tin ca làm việc.");
            }

            if (shift.Status == ShiftStatus.Closed)
            {
                return Result.Failure<CashierShiftDto>("ALREADY_CLOSED", "Ca làm việc này đã được đóng trước đó.");
            }

            if (shift.CashierId != request.CashierAccountId)
            {
                return Result.Failure<CashierShiftDto>("FORBIDDEN", "Bạn không có quyền đóng ca làm việc của người khác.");
            }

            var payments = await _context.Payments
                .Where(p => p.ShiftId == shift.Id && p.PaymentMethod == PaymentMethod.Cash)
                .ToListAsync(ct);

            decimal cashIn = payments.Where(p => p.PaymentType == PaymentType.Payment || p.PaymentType == PaymentType.Deposit).Sum(p => p.Amount);
            decimal cashOut = payments.Where(p => p.PaymentType == PaymentType.Refund).Sum(p => p.Amount);

            decimal expectedCash = shift.OpeningCash + cashIn - cashOut;

            shift.ExpectedCash = expectedCash;
            shift.CountedCash = request.CountedCash;
            shift.Difference = request.CountedCash - expectedCash;
            shift.ClosedAt = Now;
            shift.Status = ShiftStatus.Closed;
            if (!string.IsNullOrWhiteSpace(request.Note))
            {
                shift.Note = string.IsNullOrWhiteSpace(shift.Note) ? request.Note.Trim() : $"{shift.Note} | {request.Note.Trim()}";
            }

            await _context.SaveChangesAsync(ct);
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            return Result.Success(MapToDto(shift));
        });
    }

    public async Task<IReadOnlyList<CashierShiftDto>> GetShiftsAsync(DateOnly? date = null, int? cashierAccountId = null, CancellationToken ct = default)
    {
        var query = _context.CashierShifts
            .AsNoTracking()
            .Include(s => s.Cashier)
            .AsQueryable();

        if (cashierAccountId.HasValue)
        {
            query = query.Where(s => s.CashierId == cashierAccountId.Value);
        }

        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = date.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(s => s.OpenedAt >= start && s.OpenedAt < end);
        }

        var list = await query
            .OrderByDescending(s => s.OpenedAt)
            .Take(50)
            .ToListAsync(ct);

        return list.Select(MapToDto).ToList();
    }

    private static CashierShiftDto MapToDto(CashierShift s) => new(
        s.Id,
        s.CashierId,
        s.Cashier?.Username ?? $"User #{s.CashierId}",
        s.OpenedAt,
        s.ClosedAt,
        s.OpeningCash,
        s.ExpectedCash,
        s.CountedCash,
        s.Difference,
        s.Status,
        s.Note);
}

