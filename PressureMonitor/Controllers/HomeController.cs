using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using PressureMonitor.Models;

namespace PressureMonitor.Controllers;

public class HomeController(ILogger<HomeController> logger, ApplicationDbContext context) : Controller
{
    // REMOVE THIS COMMENT FOR FINAL SUBMISSION
    // I'll need to explain properly to you guys about how cookies work, but basically ASP will handle most of it.
    // The [Authorize] attribute means that only users who have a valid cookie can view this page.
    // If they don't have the page, it redirects to the default page - which you can see in Program.cs is set to /Home/Login
    // When the user logs in successfully, a cookie is created that contains their information (called a claim)
    // Cookies are used because it encrypts the data, if we stored via httpsession then someone could just lie about their username (not that it matters for this project)
    
    [Authorize]
    public IActionResult Index()
    {
        var username = User.Identity?.Name ?? "User";
        ViewData["Username"] = username;
        return View();
    }

    [AllowAnonymous] // Unlike [Authorize], this means that anyone can access this page, even if no cookie
    public IActionResult Login()
    {
        // The user could be already authorised, if they are then just send them to main page
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Index));
        }
        return View();
    }
    
    [AllowAnonymous]
    public IActionResult Register()
    {
        // If already logged in, redirect to Index
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Index));
        }
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> HandleLogin(User? user)
    {
        if (user != null && !string.IsNullOrEmpty(user.Username) && !string.IsNullOrEmpty(user.Password))
        {
            try
            {
                // Check to see if there is a user with the given username and password
                var existingUser = await context.Users.FirstOrDefaultAsync(u => u.Username == user.Username && u.Password == user.Password);
                
                if (existingUser != null)
                {
                    // Store the user information in the cookie
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, existingUser.Id.ToString()),
                        new Claim(ClaimTypes.Name, existingUser.Username ?? "User"),
                        new Claim(ClaimTypes.Email, existingUser.Email ?? "")
                    };
                    
                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

                    // Create the authentication cookie and store it in the user's browser
                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        claimsPrincipal,
                        new AuthenticationProperties
                        {
                            IsPersistent = true,
                            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
                        });

                    return RedirectToAction(nameof(Index)); // Go to main page
                }

                TempData["Error"] = "You entered an invalid username or password.";
            } catch (Exception ex)
            {
                logger.LogError(ex, "Error during login");
                return RedirectToAction(nameof(Login));
            }
        }

        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> HandleRegister(User? user)
    {
        // Check if the username / password was entered
        if (user != null && !string.IsNullOrEmpty(user.Username) && !string.IsNullOrEmpty(user.Password))
        {
            // We now need to check if the username is already taken
            var existingUser = await context.Users.FirstOrDefaultAsync(u => u.Username == user.Username);
            if (existingUser != null)
            {
                TempData["Error"] = "Username is already taken.";
                return RedirectToAction(nameof(Register));
            }

            try
            {
                // Attempt to create the user
                await context.Users.AddAsync(user);
                await context.SaveChangesAsync();
                
                // Store the user information in the cookie
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username ?? "User"),
                    new Claim(ClaimTypes.Email, user.Email ?? "")
                };
                
                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

                // Create the authentication cookie and store it in the user's browser
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    claimsPrincipal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
                    });

                return RedirectToAction(nameof(Index)); // Go to main page
            }
            catch (DbUpdateException ex)
            {
                TempData["Error"] = "An error occurred when creating your account. Please try again.";
                logger.LogError(ex, "Database update error during registration.");
                return RedirectToAction(nameof(Register));
            }
            catch (Exception ex) // This catches unexpected exceptions
            {
                TempData["Error"] = "An error occurred when creating your account. Please try again.";
                logger.LogError(ex, "Unexpected error during registration.");
                return RedirectToAction(nameof(Register));
            }
        }
        // If this is reached, then the user forgot to enter a piece of data.
        TempData["Error"] = "You entered an invalid username or password.";
        return RedirectToAction(nameof(Register));
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        // Remove the authentication cookie - this means the author won't have [Authorize] access anymore
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
    

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}