using Microsoft.AspNetCore.Http;

namespace QuickProxy.Shared.Web;

public static class InternalApiPaths
{
    public const string Root = "/api";
    public const string AdminRoot = "/api/admin";

    public static bool IsInternalApi(PathString path)
    {
        return path.StartsWithSegments(Root, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInternalAdminApi(PathString path)
    {
        return path.StartsWithSegments(AdminRoot, StringComparison.OrdinalIgnoreCase);
    }
}