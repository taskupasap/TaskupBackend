using FirebaseAdmin.Auth;
using System.Security.Claims;

namespace taskup_backend.Middleware
{
    public class FirebaseAuthMiddleware
    {
        private readonly RequestDelegate _next;

        public FirebaseAuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();

            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring(7);
                try
                {
                    FirebaseToken decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(token);

                    // Add User ID to the context so controllers can use it
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, decodedToken.Uid),
                        new Claim(ClaimTypes.Email, decodedToken.Claims.ContainsKey("email") ? decodedToken.Claims["email"].ToString()! : "")
                    };

                    var identity = new ClaimsIdentity(claims, "Firebase");
                    context.User = new ClaimsPrincipal(identity);
                }
                catch (Exception)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Invalid Firebase Token");
                    return;
                }
            }

            await _next(context);
        }
    }
}
