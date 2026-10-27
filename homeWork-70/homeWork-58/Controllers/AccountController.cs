using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using InstagramApp.Models;
using InstagramApp.Services;

namespace InstagramApp.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IWebHostEnvironment _env;
    private readonly EmailService _emailService;

    public AccountController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IWebHostEnvironment env,
        EmailService emailService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _env = env;
        _emailService = emailService;
    }

    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (ModelState.IsValid)
        {
            string? avatarPath = null;
            if (model.AvatarFile != null && model.AvatarFile.Length > 0)
            {
                string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads/avatars");
                Directory.CreateDirectory(uploadsFolder);
                string uniqueFileName = Guid.NewGuid().ToString() + "_" + model.AvatarFile.FileName;
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await model.AvatarFile.CopyToAsync(fileStream);
                }
                avatarPath = "/uploads/avatars/" + uniqueFileName;
            }

            var user = new User
            {
                UserName = model.UserName,
                Email = model.Email,
                Name = string.IsNullOrWhiteSpace(model.Name) ? model.UserName : model.Name,
                Bio = model.Bio,
                PhoneNumber = model.PhoneNumber,
                Gender = model.Gender,
                Avatar = avatarPath ?? "/images/default-avatar.png"
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                await SendWelcomeAndConfirmationAsync(user);
                await _signInManager.SignInAsync(user, isPersistent: false);
                return RedirectToAction("Index", "Home");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
        return View(model);
    }

    [HttpGet]
    public IActionResult Login() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (ModelState.IsValid)
        {
            var user = await _userManager.FindByNameAsync(model.LoginOrEmail)
                       ?? await _userManager.FindByEmailAsync(model.LoginOrEmail);

            if (user != null)
            {
                var result = await _signInManager.PasswordSignInAsync(
                    user.UserName!, model.Password, isPersistent: false, lockoutOnFailure: false);

                if (result.Succeeded)
                    return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError(string.Empty, "Неверный логин/email или пароль.");
        }
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }
    

    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            return BadRequest();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var result = await _userManager.ConfirmEmailAsync(user, token);
        TempData["EmailMessage"] = result.Succeeded
            ? "Email успешно подтверждён."
            : "Ссылка недействительна или устарела.";

        return RedirectToAction("Profile", "Users", new { id = user.Id });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendConfirmation()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        if (!user.EmailConfirmed)
        {
            bool ok = await SendWelcomeAndConfirmationAsync(user);
            TempData["EmailMessage"] = ok
                ? "Письмо с подтверждением отправлено."
                : "Не удалось отправить письмо. Попробуйте позже.";
        }
        return RedirectToAction("Profile", "Users", new { id = user.Id });
    }
    

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> EditProfile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        return View(new EditProfileViewModel
        {
            Name = user.Name,
            Bio = user.Bio,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email!
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProfile(EditProfileViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        var changes = new List<string>();
        void Track(string field, string? oldValue, string? newValue)
        {
            if ((oldValue ?? "") != (newValue ?? ""))
                changes.Add($"{field}: «{WebUtility.HtmlEncode(oldValue ?? "")}» → «{WebUtility.HtmlEncode(newValue ?? "")}»");
        }

        Track("Имя", user.Name, model.Name);
        Track("О себе", user.Bio, model.Bio);
        Track("Телефон", user.PhoneNumber, model.PhoneNumber);

        string oldEmail = user.Email!;
        bool emailChanged = !string.Equals(oldEmail, model.Email, StringComparison.OrdinalIgnoreCase);

        user.Name = string.IsNullOrWhiteSpace(model.Name) ? user.UserName! : model.Name;
        user.Bio = model.Bio;
        user.PhoneNumber = model.PhoneNumber;

        var result = await _userManager.UpdateAsync(user);

        if (result.Succeeded && emailChanged)
        {
            result = await _userManager.SetEmailAsync(user, model.Email);
            if (result.Succeeded) Track("Email", oldEmail, model.Email);
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        if (changes.Count > 0)
        {
            string body = "<h3>Данные вашего профиля были изменены</h3><ul>"
                          + string.Concat(changes.Select(c => $"<li>{c}</li>"))
                          + "</ul><p>Если это были не вы, срочно смените пароль.</p>";

            await _emailService.SendEmailAsync(oldEmail, "Профиль изменён", body);

            if (emailChanged)
            {
                await _emailService.SendEmailAsync(user.Email!, "Профиль изменён", body);
                await SendWelcomeAndConfirmationAsync(user);
            }
        }

        TempData["EmailMessage"] = "Профиль обновлён.";
        return RedirectToAction("Profile", "Users", new { id = user.Id });
    }
    
    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View();

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _signInManager.RefreshSignInAsync(user);

        string body =
            $"<p>Пароль вашего аккаунта <b>{WebUtility.HtmlEncode(user.UserName)}</b> был изменён {DateTime.Now:dd.MM.yyyy HH:mm}.</p>" +
            "<p>Если это были не вы, срочно восстановите доступ к аккаунту.</p>";

        await _emailService.SendEmailAsync(user.Email!, "Пароль изменён", body);

        TempData["EmailMessage"] = "Пароль изменён, уведомление отправлено на почту.";
        return RedirectToAction("Profile", "Users", new { id = user.Id });
    }
    

    private async Task<bool> SendWelcomeAndConfirmationAsync(User user)
    {
        string profileUrl = Url.Action("Profile", "Users", new { id = user.Id }, Request.Scheme)!;

        string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        string confirmUrl = Url.Action("ConfirmEmail", "Account",
            new { userId = user.Id, token }, Request.Scheme)!;

        string body = $@"
            <h2>Добро пожаловать, {WebUtility.HtmlEncode(user.Name)}!</h2>
            <p>Ваш логин: <b>{WebUtility.HtmlEncode(user.UserName)}</b></p>
            <p>Ваш профиль: <a href='{profileUrl}'>{profileUrl}</a></p>
            <p>Чтобы подтвердить email, перейдите по <a href='{confirmUrl}'>этой ссылке</a>.</p>";

        return await _emailService.SendEmailAsync(user.Email!, "Добро пожаловать!", body);
    }
}