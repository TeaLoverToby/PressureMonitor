using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
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
    public async Task<IActionResult> Index()
    {
        // Get the user ID from the cookie
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            // If there is an isuse with the cookie, send back to login page
            return RedirectToAction(nameof(Login));
        }

        // Load the user from the database and pass it to the view.
        // TODO: Look into AsNoTracking() optimization
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            // If the user no longer exists in the database, log them out
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }
        
        return View(user);
    }

    [Authorize]
    public async Task<IActionResult> Test()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction(nameof(Login));
        }
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }
        return View(user);
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
    
    // Only admins can access the register/create user page
    [Authorize(Roles = "Admin")]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleRegister(User? user)
    {
        // Only admins can create users
        if (!User.IsInRole("Admin"))
        {
            return RedirectToAction(nameof(Index));
        }

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
                // Hash the password before saving
                var hasher = new PasswordHasher<User>();
                var hashed = hasher.HashPassword(user, user.Password);
                user.Password = hashed;

                // REMOVE THIS EXPLANATION WHEN PROJECT IS DONE
                // To explain this, the UserType enum controls what type of user it is.
                // Each user type has a corresponding entity (Admin, Clinician, Patient).
                // We need to create the corresponding entity - think of it like a foreign key.
                // If we don't create this entity, then it will be null and when the user opens their dashboard, it will have a smelly error.
                switch (user.UserType)
                {
                    case UserType.Admin:
                        if (user.Admin != null) break;
                        var admin = new Admin { User = user };
                        user.Admin = admin;
                        break;
                    case UserType.Clinician:
                        if (user.Clinician != null) break;
                        var clinician = new Clinician { User = user };
                        user.Clinician = clinician;
                        break;
                    case UserType.Patient:
                        if (user.Patient != null) break;
                        var patient = new Patient { User = user };
                        user.Patient = patient;
                        break;
                }

                // Attempt to create the user
                await context.Users.AddAsync(user);
                await context.SaveChangesAsync();

                TempData["Success"] = $"User '{user.Username}' created.";
                return RedirectToAction(nameof(Register));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating user by admin.");
                TempData["Error"] = "An error occurred when creating the account.";
                return RedirectToAction(nameof(Register));
            }
        }

        TempData["Error"] = "You entered an invalid username or password.";
        return RedirectToAction(nameof(Register));
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleLogin(User? user)
    {
        if (user != null && !string.IsNullOrEmpty(user.Username) && !string.IsNullOrEmpty(user.Password))
        {
            try
            {
                var existingUser = await context.Users.FirstOrDefaultAsync(u => u.Username == user.Username);
                
                if (existingUser != null)
                {
                    // Verify the hashed password
                    var hasher = new PasswordHasher<User>();
                    var result = hasher.VerifyHashedPassword(existingUser, existingUser.Password ?? string.Empty, user.Password);
                    if (result == PasswordVerificationResult.Failed)
                    {
                        TempData["Error"] = "You entered an invalid username or password.";
                        return RedirectToAction(nameof(Login));
                    }

                    var claims = new List<Claim>
                    {
                        new (ClaimTypes.NameIdentifier, existingUser.Id.ToString()),
                        new (ClaimTypes.Name, existingUser.Username ?? "User"),
                        new (ClaimTypes.Email, existingUser.Email ?? ""),
                        new ("UserType", existingUser.UserType.ToString())
                    };

                    // Set the claim role based on the UserType
                    switch (existingUser.UserType)
                    {
                        case UserType.Clinician:
                            claims.Add(new Claim(ClaimTypes.Role, "Clinician"));
                            break;
                        case UserType.Patient:
                            claims.Add(new Claim(ClaimTypes.Role, "Patient"));
                            break;
                        case UserType.Admin:
                            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                            break;
                    }
                    
                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        claimsPrincipal,
                        new AuthenticationProperties
                        {
                            IsPersistent = true,
                            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
                        });

                    return RedirectToAction("Dashboard", "User");
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