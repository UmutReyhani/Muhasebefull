using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Muhasebe.Models;
using System.Text.Json;

namespace Muhasebe.Attributes
{
    public class CheckRoleAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var userJson = context.HttpContext.Session.GetString("CurrentUser");
            if (string.IsNullOrEmpty(userJson))
            {
                context.Result = new StatusCodeResult(401);
                return;
            }
        }
    }
}
