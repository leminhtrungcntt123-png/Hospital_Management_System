using HIS.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HIS.DAL;

/// <summary>
/// Lưu enum C# (PascalCase) vào cột VARCHAR theo khuôn UPPER_SNAKE_CASE của CSDL:
/// PartiallyPaid -> PARTIALLY_PAID, WalkIn -> WALK_IN, OutOfNetwork -> OUT_OF_NETWORK.
/// Áp dụng một lần cho mọi enum trong AppDbContext.ConfigureConventions.
/// </summary>
public class UpperSnakeCaseEnumConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ByDatabaseValue =
        Enum.GetValues<TEnum>().ToDictionary(value => value.ToDatabaseCode());

    public UpperSnakeCaseEnumConverter()
        : base(value => value.ToDatabaseCode(), value => ReadFromDatabase(value))
    {
    }

    private static TEnum ReadFromDatabase(string value) =>
        ByDatabaseValue.TryGetValue(value, out var parsed)
            ? parsed
            : throw new InvalidCastException($"Giá trị '{value}' không thuộc enum {typeof(TEnum).Name}.");
}
