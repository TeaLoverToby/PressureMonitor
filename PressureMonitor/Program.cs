using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<ApplicationDbContext>(options => 
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Login"; // If the user is not authenticated, then they are redirected to the login page
        options.LogoutPath = "/Home/Logout";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=User}/{action=Dashboard}/{id?}")
    .WithStaticAssets();

// This makes sure that the database is created and also creates the admin user
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.EnsureCreated();

    if (!await dbContext.Users.AnyAsync())
    {
        var adminUser = new User
        {
            Username = "admin",
            Email = "admin@example.com",
            UserType = UserType.Admin
        };
        var hasher = new PasswordHasher<User>();
        adminUser.Password = hasher.HashPassword(adminUser, "admin");

        dbContext.Users.Add(adminUser);
        await dbContext.SaveChangesAsync();
        
        var adminEntity = new Admin { UserId = adminUser.Id };
        dbContext.Admins.Add(adminEntity);
        await dbContext.SaveChangesAsync();
    }
}

app.Run();
