using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InstagramApp.Models;
using InstagramApp.Services;

namespace InstagramApp.Controllers;

[Authorize]
public class UsersController : Controller
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly EmailService _emailService;

    public UsersController(
        AppDbContext context,
        UserManager<User> userManager,
        EmailService emailService)
    {
        _context = context;
        _userManager = userManager;
        _emailService = emailService;
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return View(new List<User>());
        }

        string q = query.ToLower();
        var users = await _context.Users
            .Where(u => u.UserName!.ToLower().Contains(q) ||
                        u.Email!.ToLower().Contains(q) ||
                        (u.Name != null && u.Name.ToLower().Contains(q)) ||
                        (u.Bio != null && u.Bio.ToLower().Contains(q)))
            .ToListAsync();

        ViewBag.Query = query;
        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> Profile(string id)
    {
        var user = await _context.Users
            .Include(u => u.Posts.OrderByDescending(p => p.CreatedAt))
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        var currentUserId = _userManager.GetUserId(User);
        bool isFollowing = await _context.Subscriptions
            .AnyAsync(s => s.FollowerId == currentUserId && s.TargetUserId == id);

        ViewBag.IsFollowing = isFollowing;
        ViewBag.IsCurrentUser = currentUserId == id;

        return View(user);
    }

    [HttpPost]
    public async Task<IActionResult> ToggleFollow(string targetUserId)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId == targetUserId) return BadRequest();

        var follower = await _context.Users.FindAsync(currentUserId);
        var targetUser = await _context.Users.FindAsync(targetUserId);

        if (follower == null || targetUser == null) return NotFound();

        var existingSub = await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.FollowerId == currentUserId && s.TargetUserId == targetUserId);

        bool isFollowing;

        if (existingSub != null)
        {
            _context.Subscriptions.Remove(existingSub);
            follower.SubscriptionsCount = Math.Max(0, follower.SubscriptionsCount - 1);
            targetUser.SubscribersCount = Math.Max(0, targetUser.SubscribersCount - 1);
            isFollowing = false;
        }
        else
        {
            _context.Subscriptions.Add(new Subscription
            {
                FollowerId = currentUserId!,
                TargetUserId = targetUserId
            });
            follower.SubscriptionsCount++;
            targetUser.SubscribersCount++;
            isFollowing = true;
        }

        await _context.SaveChangesAsync();

        return Json(new { success = true, isFollowing = isFollowing, subscribersCount = targetUser.SubscribersCount });
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMyData()
    {
        var currentUserId = _userManager.GetUserId(User);
        var user = await _context.Users
            .Include(u => u.Posts)
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        if (user == null) return NotFound();

        int postsCount = user.Posts.Count;
        int likesCount = user.Posts.Sum(p => p.LikesCount);

        static string H(string? s) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s) ? "—" : s);

        string body = $@"
            <h2>Ваши данные в Instagram App</h2>
            <ul>
              <li>Логин: {H(user.UserName)}</li>
              <li>Имя: {H(user.Name)}</li>
              <li>О себе: {H(user.Bio)}</li>
              <li>Email: {H(user.Email)}</li>
              <li>Телефон: {H(user.PhoneNumber)}</li>
              <li>Подписчиков: {user.SubscribersCount}</li>
              <li>Подписок: {user.SubscriptionsCount}</li>
              <li>Публикаций: {postsCount}</li>
              <li>Лайков на ваших публикациях: {likesCount}</li>
            </ul>";

        bool ok = await _emailService.SendEmailAsync(user.Email!, "Ваши данные", body);
        TempData["EmailMessage"] = ok
            ? "Данные отправлены на вашу почту."
            : "Не удалось отправить письмо. Попробуйте позже.";

        return RedirectToAction(nameof(Profile), new { id = user.Id });
    }
}