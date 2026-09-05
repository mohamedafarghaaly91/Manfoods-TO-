using System.Security.Cryptography;

namespace MvcApp.Services;

/// <summary>
/// The one password policy every password-setting flow in the app enforces:
/// at least 12 characters, one uppercase letter, one lowercase letter, one
/// digit, and one symbol. Also generates compliant random temporary
/// passwords for newly created accounts (manual Add User and the bulk
/// "Generate Default Passwords" flow) — login itself is unaffected, this
/// only gates where a password is being set or changed.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;

    public static bool IsValid(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength) return false;

        bool hasUpper = false, hasLower = false, hasDigit = false, hasSymbol = false;
        foreach (var c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else hasSymbol = true;
        }
        return hasUpper && hasLower && hasDigit && hasSymbol;
    }

    // Excludes visually-ambiguous characters (0/O, 1/l/I) since these
    // temporary passwords are read off an Excel sheet or SMS and retyped
    // by hand on a first login.
    private const string UpperChars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string LowerChars = "abcdefghijkmnopqrstuvwxyz";
    private const string DigitChars = "23456789";
    private const string SymbolChars = "!@#$%^&*-_=+?";
    private const string AllChars = UpperChars + LowerChars + DigitChars + SymbolChars;

    /// <summary>Generates a random password that always satisfies IsValid —
    /// used as the initial/temporary password for newly created accounts.</summary>
    public static string GenerateTemporaryPassword(int length = 14)
    {
        var chars = new char[length];
        chars[0] = RandomChar(UpperChars);
        chars[1] = RandomChar(LowerChars);
        chars[2] = RandomChar(DigitChars);
        chars[3] = RandomChar(SymbolChars);
        for (int i = 4; i < length; i++) chars[i] = RandomChar(AllChars);

        // Shuffle so the guaranteed characters aren't always in the first 4 positions.
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    private static char RandomChar(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
