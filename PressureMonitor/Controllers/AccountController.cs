using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;
using System.Security.Claims;

namespace PressureMonitor.Controllers;

public class AccountController(ILogger<AccountController> logger, ApplicationDbContext context) : Controller
{
    // REMOVE THIS COMMENT FOR FINAL SUBMISSION
    // I'll need to explain properly to you guys about how cookies work, but basically ASP will handle most of it.
    // The [Authorize] attribute means that only users who have a valid cookie can view this page.
    // If they don't have the page, it redirects to the default page - which you can see in Program.cs is set to /Home/Login
    // When the user logs in successfully, a cookie is created that contains their information (called a claim)
    // Cookies are used because it encrypts the data, if we stored via httpsession then someone could just lie about their username (not that it matters for this project)

    [AllowAnonymous] // Unlike [Authorize], this means that anyone can access this page, even if no cookie
    public IActionResult Login()
    {
        // The user could be already authorised, if they are then just send them to main page
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Dashboard));
        }
        return View();
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

                    return RedirectToAction(nameof(Dashboard));
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
            return RedirectToAction(nameof(Dashboard));
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
                return RedirectToAction("Index", "Admin");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating user by admin.");
                TempData["Error"] = "An error occurred when creating the account.";
                return RedirectToAction("Index", "Admin");
            }
        }

        TempData["Error"] = "You entered an invalid username or password.";
        return RedirectToAction(nameof(Register));
    }


    // Only admins can access the edit user page
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public IActionResult EditUser(Admin admin)        
    {
        // Check if user has been selected
        if (admin.SelectedUserItem == null)
        {
            return RedirectToAction("Index", "Admin");
        }

        var user = context.Users.FirstOrDefault(u => u.Id.ToString() == admin.SelectedUserItem.Value);
        return View(user);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleEditUser(User? user)
    {
        // Only admins can edit users
        if (!User.IsInRole("Admin"))
        {
            return RedirectToAction(nameof(Dashboard));
        }

        

        // Get current user data
        var NewUser = context.Users.FirstOrDefault(u => u.Id == user.Id);

        NewUser.Email = user.Email;
        NewUser.UserType = user.UserType;

        // Hash the password before saving
        //If a new password is entered update the database, otherwise keep the old password
        if (user.Password != null && user.Password != "")
        {
            var hasher = new PasswordHasher<User>();
            var hashed = hasher.HashPassword(user, user.Password);
            NewUser.Password = hashed;
        }

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
        try
        { 
            // Attempt to edit the user (add later)
            context.Update(NewUser);
            await context.SaveChangesAsync();

            //TempData["Success"] = $"User '{user.Username}' updated.";
            return RedirectToAction("Index","Admin");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating user by admin.");
            TempData["Error"] = "An error occurred when creating the account.";
            return RedirectToAction("Index", "Admin");
        }
    }

        //TempData["Error"] = "You entered an invalid username or password.";
        //return RedirectToAction(nameof(EditUser));
    //}


    // Only admins can access the delete user page
    [Authorize(Roles = "Admin")]
    public IActionResult DeleteUserCheck(Admin admin)
    {

        // Check if user has been selected
        if (admin.SelectedUserItem == null)
        {
            return RedirectToAction("Index", "Admin");
        }

        var user = context.Users.FirstOrDefault(u => u.Id.ToString() == admin.SelectedUserItem.Value);
        return View(user);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleDeleteUserCheck(User? user)
    {
        // Only admins can edit users
        if (!User.IsInRole("Admin"))
        {
            return RedirectToAction(nameof(Dashboard));
        }

        // Get current user data
        var DeleteUser = context.Users.FirstOrDefault(u => u.Id == user.Id);

        try
        {
            // Attempt to delete the user (add later)
            context.Remove(DeleteUser);
            await context.SaveChangesAsync();

            //TempData["Success"] = $"User '{user.Username}' created.";
            return RedirectToAction("Index","Admin");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting user by admin.");
            TempData["Error"] = "An error occurred when creating the account.";
            return RedirectToAction("Index", "Admin");
        }
     }
    

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        // Remove the authentication cookie - this means the author won't have [Authorize] access anymore
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    public IActionResult Dashboard()
    {
        var userTypeStr = User.FindFirst("UserType")?.Value;
        
        if (Enum.TryParse<UserType>(userTypeStr, out var userType))
        {
            return userType switch
            {
                UserType.Patient => RedirectToAction("Index", "Patient"),
                UserType.Clinician => RedirectToAction("Index", "Clinician"),
                UserType.Admin => RedirectToAction("Index", "Admin"),
                _ => RedirectToAction("Index", "Home")
            };
        }
        
        return RedirectToAction("Index", "Home");
    }
}
