# UserType Pattern - Usage Examples

## 1. Creating Users with Different Types

### Creating a Patient
```csharp
var patientUser = new User
{
    Username = "john_doe",
    Email = "john@example.com",
    Password = hashedPassword,
    UserType = UserType.Patient
};
await context.Users.AddAsync(patientUser);
await context.SaveChangesAsync();

// Add patient-specific data
var patient = new Patient
{
    UserId = patientUser.Id,
    DateOfBirth = new DateTime(1990, 5, 15),
    MedicalRecordNumber = "MRN12345",
    BloodType = "O+",
    Allergies = "Penicillin"
};
await context.Patients.AddAsync(patient);
await context.SaveChangesAsync();
```

### Creating a Clinician
```csharp
var clinicianUser = new User
{
    Username = "dr_smith",
    Email = "smith@hospital.com",
    Password = hashedPassword,
    UserType = UserType.Clinician
};
await context.Users.AddAsync(clinicianUser);
await context.SaveChangesAsync();

// Add clinician-specific data
var clinician = new Clinician
{
    UserId = clinicianUser.Id,
    LicenseNumber = "MD123456",
    Specialization = "Cardiology",
    Department = "Emergency",
    HireDate = DateTime.Now
};
await context.Clinicians.AddAsync(clinician);
await context.SaveChangesAsync();
```

### Creating an Admin
```csharp
var adminUser = new User
{
    Username = "admin",
    Email = "admin@hospital.com",
    Password = hashedPassword,
    UserType = UserType.Admin,
    IsAdmin = true  // Can also set this flag
};
await context.Users.AddAsync(adminUser);
await context.SaveChangesAsync();
```

---

## 2. Querying Users by Type

### Get all patients
```csharp
var patients = await context.Users
    .Where(u => u.UserType == UserType.Patient)
    .Include(u => u.Patient)  // Eager load patient data
    .ToListAsync();

foreach (var patient in patients)
{
    var mrn = patient.Patient?.MedicalRecordNumber;
    var dob = patient.Patient?.DateOfBirth;
}
```

### Get all clinicians
```csharp
var clinicians = await context.Users
    .Where(u => u.UserType == UserType.Clinician)
    .Include(u => u.Clinician)
    .ToListAsync();

foreach (var clinician in clinicians)
{
    var license = clinician.Clinician?.LicenseNumber;
    var dept = clinician.Clinician?.Department;
}
```

### Get specific user with their role data
```csharp
var user = await context.Users
    .Include(u => u.Patient)
    .Include(u => u.Clinician)
    .FirstOrDefaultAsync(u => u.Id == userId);

if (user?.UserType == UserType.Patient && user.Patient != null)
{
    var bloodType = user.Patient.BloodType;
}
else if (user?.UserType == UserType.Clinician && user.Clinician != null)
{
    var specialization = user.Clinician.Specialization;
}
```

---

## 3. Authorization with Claims

### In HandleLogin - Add UserType claim
```csharp
var claims = new List<Claim>
{
    new Claim(ClaimTypes.NameIdentifier, existingUser.Id.ToString()),
    new Claim(ClaimTypes.Name, existingUser.Username ?? "User"),
    new Claim(ClaimTypes.Email, existingUser.Email ?? ""),
    new Claim("UserType", existingUser.UserType.ToString())  // Add UserType claim
};

if (existingUser.IsAdmin)
{
    claims.Add(new Claim(ClaimTypes.Role, "Admin"));
}

// Also add role claim based on UserType
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
```

### Checking UserType in Controllers
```csharp
[Authorize]
public async Task<IActionResult> Dashboard()
{
    // Get UserType from claims
    var userTypeStr = User.FindFirst("UserType")?.Value;
    if (Enum.TryParse<UserType>(userTypeStr, out var userType))
    {
        switch (userType)
        {
            case UserType.Patient:
                return RedirectToAction("PatientDashboard");
            case UserType.Clinician:
                return RedirectToAction("ClinicianDashboard");
            case UserType.Admin:
                return RedirectToAction("AdminDashboard");
        }
    }
    
    return View();
}

[Authorize(Roles = "Clinician")]
public IActionResult ClinicianDashboard()
{
    // Only clinicians can access
    return View();
}

[Authorize(Roles = "Patient")]
public IActionResult PatientDashboard()
{
    // Only patients can access
    return View();
}
```

### Checking in Razor Views
```cshtml
@if (User.IsInRole("Clinician"))
{
    <li class="nav-item">
        <a class="nav-link" asp-action="Patients">My Patients</a>
    </li>
}

@if (User.IsInRole("Patient"))
{
    <li class="nav-item">
        <a class="nav-link" asp-action="MyRecords">Medical Records</a>
    </li>
}

@if (User.HasClaim("UserType", "Clinician"))
{
    <div class="alert alert-info">
        Welcome, Dr. @User.Identity.Name
    </div>
}
```

---

## 4. Advanced Queries

### Find all clinicians in a specific department
```csharp
var emergencyClinicians = await context.Clinicians
    .Include(c => c.User)
    .Where(c => c.Department == "Emergency")
    .ToListAsync();
```

### Find patients with specific conditions
```csharp
var diabeticPatients = await context.Patients
    .Include(p => p.User)
    .Where(p => p.MedicalConditions.Contains("Diabetes"))
    .ToListAsync();
```

### Get user with full profile
```csharp
var user = await context.Users
    .Include(u => u.Patient)
    .Include(u => u.Clinician)
    .FirstOrDefaultAsync(u => u.Id == userId);

if (user != null)
{
    Console.WriteLine($"User: {user.Username}");
    Console.WriteLine($"Type: {user.UserType}");
    
    if (user.Patient != null)
    {
        Console.WriteLine($"MRN: {user.Patient.MedicalRecordNumber}");
    }
    
    if (user.Clinician != null)
    {
        Console.WriteLine($"License: {user.Clinician.LicenseNumber}");
    }
}
```

---

## 5. Registration Flow Example

### Controller action for registering different user types
```csharp
[HttpPost]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> CreateUser(User user, UserType userType, 
    Clinician? clinician, Patient? patient)
{
    // Hash password
    var hasher = new PasswordHasher<User>();
    user.Password = hasher.HashPassword(user, user.Password);
    user.UserType = userType;
    
    await context.Users.AddAsync(user);
    await context.SaveChangesAsync();
    
    // Add role-specific data
    if (userType == UserType.Clinician && clinician != null)
    {
        clinician.UserId = user.Id;
        await context.Clinicians.AddAsync(clinician);
    }
    else if (userType == UserType.Patient && patient != null)
    {
        patient.UserId = user.Id;
        await context.Patients.AddAsync(patient);
    }
    
    await context.SaveChangesAsync();
    return RedirectToAction("Index");
}
```

---

## Benefits of This Approach

✅ **Single authentication system** - All users login the same way
✅ **Type-safe** - Enum ensures valid user types
✅ **Flexible queries** - Easy to query by type or include related data
✅ **Clean separation** - Role-specific data in separate tables
✅ **Scalable** - Easy to add more properties to Clinician/Patient
✅ **Claims-based auth** - Works perfectly with ASP.NET Core authorization
✅ **No null columns** - Patient/Clinician tables only exist when needed

