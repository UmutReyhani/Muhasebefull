using MuhasebeFull.Models;

namespace MuhasebeFull.Users
{
    public class userFunctions
    {
        #region UserSession
        public static void SetCurrentUserToSession(HttpContext context, User user)
        {
            context.Session.SetString("id", user.id);
            context.Session.SetString("username", user.username);
            context.Session.SetString("role", user.role);
        }

        public static User? GetCurrentUserFromSession(HttpContext context)
        {
            var userid = context.Session.GetString("id");
            if (string.IsNullOrEmpty(userid))
                return null;
            var username = context.Session.GetString("username");
            var role = context.Session.GetString("role");
            return new User { id = userid, username = username, role = role };
        }
        #endregion
    }
}
