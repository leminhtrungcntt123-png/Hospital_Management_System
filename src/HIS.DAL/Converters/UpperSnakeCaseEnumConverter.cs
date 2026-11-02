using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HIS.DAL.Converters;

public class UpperSnakeCaseEnumConverter<TEnum> : ValueConverter<TEnum, string> where TEnum : struct, Enum
{
    public UpperSnakeCaseEnumConverter() : base(
        v => ToUpperSnakeCase(v.ToString()),
        v => FromUpperSnakeCase(v))
    {
    }

    private static string ToUpperSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(input[i - 1]))
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    private static TEnum FromUpperSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Enum.Parse<TEnum>(input, true);
        }

        // Convert UPPER_SNAKE to PascalCase
        var parts = input.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    sb.Append(part.Substring(1).ToLowerInvariant());
                }
            }
        }

        return Enum.Parse<TEnum>(sb.ToString(), true);
    }
}

