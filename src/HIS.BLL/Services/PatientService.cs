using HIS.DAL;
using HIS.Domain;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class PatientService : IPatientService
{
	private readonly AppDbContext _context;

	public PatientService(AppDbContext context)
	{
		_context = context;
	}

	// Lấy danh sách bệnh nhân theo từ khóa tìm kiếm và giới hạn số lượng bản ghi
	public async Task<IReadOnlyList<PatientListItemDto>> GetPatientsAsync(string? search = null, int limit = 100, CancellationToken cancellationToken = default)
	{
		var query = _context.Patients.AsNoTracking().AsQueryable();

		if (!string.IsNullOrWhiteSpace(search))
		{
			var keyword = search.Trim();
			query = query.Where(p =>
				EF.Functions.ILike(p.FullName, $"%{keyword}%") ||
				EF.Functions.ILike(p.PhoneNumber, $"%{keyword}%") ||
				(p.NationalId != null && EF.Functions.ILike(p.NationalId, $"%{keyword}%")));
		}

		// Nhãn giới tính dịch sau khi đã đọc xong dữ liệu: EF không dịch được extension trong Select()
		var rows = await query
			.OrderByDescending(p => p.Id)
			.Take(limit)
			.Select(p => new
			{
				p.Id,
				p.FullName,
				p.DateOfBirth,
				p.Gender,
				p.PhoneNumber,
				p.NationalId,
				p.Address
			})
			.ToListAsync(cancellationToken);

		var patientIds = rows.Select(r => r.Id).ToList();
		var cardByPatient = (await _context.InsuranceCards
			.AsNoTracking()
			.Where(c => c.IsActive && patientIds.Contains(c.PatientId))
			.Select(c => new { c.PatientId, c.CardNumber })
			.ToListAsync(cancellationToken))
			.ToDictionary(c => c.PatientId, c => c.CardNumber);

		return rows
			.Select(r => new PatientListItemDto(
				r.Id,
				r.FullName,
				r.DateOfBirth,
				r.Gender.ToLabel(),
				r.PhoneNumber,
				r.NationalId ?? "",
				r.Address ?? "",
				cardByPatient.GetValueOrDefault(r.Id, "")))
			.ToList();
	}
}