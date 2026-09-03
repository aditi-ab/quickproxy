using System.Net.Mail;

namespace QuickProxy.Shared.Auth;

public static class UserInput
{
    public static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    public static string? NormalizeFullName(string? fullName)
    {
        return string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
    }

    public static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email);
            return string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static List<string> ValidateNewUser(string email, string password, string? fullName)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(email))
            errors.Add("email is required.");
        else if (!IsValidEmail(email)) errors.Add("email is invalid.");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            errors.Add("password is required and must be at least 8 characters.");

        if (fullName is not null && fullName.Length > 200) errors.Add("fullName must be 200 characters or fewer.");

        return errors;
    }
}