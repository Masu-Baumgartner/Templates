using Microsoft.AspNetCore.Mvc.Controllers;
using MoonCore.Attributes;
using MoonCore.Authentication;
using MoonCore.Extensions;

namespace WebAppTemplate.ApiServer.Http.Middleware;

public class AuthorizationMiddleware
{
    private readonly RequestDelegate Next;

    public AuthorizationMiddleware(RequestDelegate next)
    {
        Next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (await Authorize(context))
        {
            await Next(context);
        }
    }

    private async Task<bool> Authorize(HttpContext context)
    {
        var requiredPermissions = ResolveRequiredPermissions(context);

        if (requiredPermissions.Length == 0)
            return true;
        
        if (context.User is not PermClaimsPrinciple permClaimsPrinciple)
        {
            await Results.Problem(
                title: "An unauthenticated request is not allowed to use this endpoint",
                statusCode: 401
            ).ExecuteAsync(context);
            
            return false;
        }

        // Check if one of the required permissions is to be logged in
        if (requiredPermissions.Any(x => x == "meta.authenticated") && permClaimsPrinciple.IdentityModel == null)
        {
            await Results.Problem(
                title: "This endpoint requires a user authenticated token",
                statusCode: 401
            ).ExecuteAsync(context);
            
            return false;
        }

        foreach (var permission in requiredPermissions)
        {
            if(permission == "meta.authenticated") // We already verified that
                continue;

            if (!permClaimsPrinciple.HasPermission(permission))
            {
                await Results.Problem(
                    title: "You dont have the required permission",
                    detail: permission,
                    statusCode: 403
                ).ExecuteAsync(context);

                return false;
            }
        }

        return true;
    }
    
    private string[] ResolveRequiredPermissions(HttpContext context)
    {
        // Basic handling
        var endpoint = context.GetEndpoint();
        
        if (endpoint == null)
            return [];
        
        var metadata = endpoint
            .Metadata
            .GetMetadata<ControllerActionDescriptor>();

        if (metadata == null)
            return [];

        // Retrieve attribute infos
        var controllerAttrInfo = metadata
            .ControllerTypeInfo
            .CustomAttributes
            .FirstOrDefault(x => x.AttributeType == typeof(RequirePermissionAttribute));

        var methodAttrInfo = metadata
            .MethodInfo
            .CustomAttributes
            .FirstOrDefault(x => x.AttributeType == typeof(RequirePermissionAttribute));

        // Retrieve permissions from attribute infos
        var controllerPermission = controllerAttrInfo != null
            ? controllerAttrInfo.ConstructorArguments.First().Value as string
            : null;
        
        var methodPermission = methodAttrInfo != null
            ? methodAttrInfo.ConstructorArguments.First().Value as string
            : null;

        // If both have a permission flag, return both
        if (controllerPermission != null && methodPermission != null)
            return [controllerPermission, methodPermission];
        
        // If either of them have a permission set, return it
        if (controllerPermission != null)
            return [controllerPermission];
        
        if (methodPermission != null)
            return [methodPermission];
        
        // If both have no permission set, allow everyone to access it
        return [];
    }
}