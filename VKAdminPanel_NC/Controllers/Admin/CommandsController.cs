using Microsoft.AspNetCore.Mvc;
using VKBot_nordciti.Data;

[Route("admin/commands")]
public class CommandsController : Controller
{
    private readonly BotDbContext _db;

    public CommandsController(BotDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var commands = await _db.Commands.ToListAsync();
        return View(commands);
    }

    [HttpGet("create")]
    public IActionResult Create() => View();

    [HttpPost("create")]
    public async Task<IActionResult> Create(Command cmd)
    {
        _db.Commands.Add(cmd);
        await _db.SaveChangesAsync();
        return RedirectToAction("Index");
    }

    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(int id)
    {
        var cmd = await _db.Commands.FindAsync(id);
        return View(cmd);
    }

    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit(Command cmd)
    {
        _db.Commands.Update(cmd);
        await _db.SaveChangesAsync();
        return RedirectToAction("Index");
    }

    [HttpGet("delete/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var cmd = await _db.Commands.FindAsync(id);
        _db.Commands.Remove(cmd);
        await _db.SaveChangesAsync();
        return RedirectToAction("Index");
    }
}
