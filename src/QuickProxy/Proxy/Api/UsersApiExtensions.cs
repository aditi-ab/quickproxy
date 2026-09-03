using QuickProxy.Shared.Auth;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Api;

public static class UsersApiExtensions
{
    public static IEndpointRouteBuilder MapUsersApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{InternalApiPaths.AdminRoot}/users").RequireAuthorization();

        group.MapGet("/", (IUserStore store) => { return Results.Ok(store.List().Select(ToResponse)); });

        group.MapPost("/", (CreateUserRequest request, IUserStore store, IPasswordHashingService hasher) =>
        {
            var errors = UserInput.ValidateNewUser(request.Email, request.Password, request.FullName);
            if (errors.Count > 0) return Validation(errors);

            var email = NormalizeEmail(request.Email);
            if (store.Exists(email))
                return Results.Conflict(new
                {
                    code = "duplicate_email",
                    message = $"User '{email}' already exists."
                });

            store.Upsert(new AdminUserRecord
            {
                Email = email,
                FullName = UserInput.NormalizeFullName(request.FullName),
                Enabled = request.Enabled,
                PasswordHash = hasher.HashPassword(request.Password)
            });

            var saved = store.GetByEmail(email)!;
            return Results.Created($"{InternalApiPaths.AdminRoot}/users/{Uri.EscapeDataString(email)}",
                ToResponse(saved));
        });

        group.MapPut("/{email}", (string email, UpdateUserRequest request, IUserStore store) =>
        {
            var normalizedEmail = NormalizeEmail(email);
            var existing = store.GetByEmail(normalizedEmail);
            if (existing is null) return NotFound(normalizedEmail);

            if (request.FullName is not null && request.FullName.Length > 200)
                return Validation(["fullName must be 200 characters or fewer."]);

            existing.FullName = UserInput.NormalizeFullName(request.FullName);
            existing.Enabled = request.Enabled;
            store.Upsert(existing);
            return Results.Ok(ToResponse(existing));
        });

        group.MapPut("/{email}/password",
            (string email, UpdatePasswordRequest request, IUserStore store, IPasswordHashingService hasher) =>
            {
                var normalizedEmail = NormalizeEmail(email);
                var existing = store.GetByEmail(normalizedEmail);
                if (existing is null) return NotFound(normalizedEmail);

                if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                    return Validation(["password is required and must be at least 8 characters."]);

                existing.PasswordHash = hasher.HashPassword(request.Password);
                store.Upsert(existing);
                return Results.NoContent();
            });

        group.MapDelete("/{email}", (string email, IUserStore store) =>
        {
            var normalizedEmail = NormalizeEmail(email);
            return store.Delete(normalizedEmail) ? Results.NoContent() : NotFound(normalizedEmail);
        });

        return app;
    }

    private static string NormalizeEmail(string value)
    {
        return UserInput.NormalizeEmail(value);
    }

    private static UserResponse ToResponse(AdminUserRecord user)
    {
        return new UserResponse(user.Email, user.FullName, user.Enabled, !string.IsNullOrWhiteSpace(user.PasswordHash),
            user.ExternalIdentities.Count);
    }

    private static IResult Validation(List<string> details)
    {
        return Results.BadRequest(new
        {
            code = "validation_error",
            message = "Validation failed.",
            details
        });
    }

    private static IResult NotFound(string email)
    {
        return Results.NotFound(new
        {
            code = "not_found",
            message = $"User '{email}' was not found."
        });
    }

    public sealed record CreateUserRequest(string Email, string Password, string? FullName, bool Enabled);

    public sealed record UpdateUserRequest(string? FullName, bool Enabled);

    public sealed record UpdatePasswordRequest(string Password);

    public sealed record UserResponse(
        string Email,
        string? FullName,
        bool Enabled,
        bool HasPassword,
        int ExternalIdentityCount);
}