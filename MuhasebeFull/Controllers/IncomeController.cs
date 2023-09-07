using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using Muhasebe.Attributes;
using Muhasebe.Services;
using MuhasebeFull.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using static Muhasebe.Controllers.UserController;

[Route("api/incomes")]
public class IncomesController : ControllerBase
{
    private readonly IConnectionService _connectionService;
    private IMongoCollection<Income> _incomeCollection;

    public IncomesController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
        _incomeCollection = _connectionService.db().GetCollection<Income>("IncomeCollection");
    }

    #region UserSession
    private void SetCurrentUserToSession(User user)
    {
        var userJson = JsonSerializer.Serialize(user);
        HttpContext.Session.SetString("CurrentUser", userJson);
    }

    private User? GetCurrentUserFromSession()
    {
        var userJson = HttpContext.Session.GetString("CurrentUser");
        if (string.IsNullOrEmpty(userJson))
            return null;

        return JsonSerializer.Deserialize<User>(userJson);
    }
    #endregion

    #region IncomeAdd

    public class AddIncomeReq
    {
        public string title { get; set; }
        public string? description { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal amount { get; set; }
    }

    public class AddIncomeRes
    {
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("add-income"), CheckRoleAttribute]
    public IActionResult AddIncome([FromBody] AddIncomeReq incomeReq)
    {
        var currentUser = GetCurrentUserFromSession();

        Income income = new Income
        {
            title = incomeReq.title,
            description = incomeReq.description,
            amount = incomeReq.amount,
            userId = currentUser.id
            // Diğer gerekli özellikler...
        };

        _incomeCollection.InsertOne(income);

        return Ok(new AddIncomeRes { message = "Gelir kaydı başarıyla oluşturuldu." });
    }

    #endregion

    #region UpdateIncome

    public class UpdateIncomeReq
    {
        public string title { get; set; }
        public string? description { get; set; }
        public decimal amount { get; set; }
    }

    public class UpdateIncomeRes
    {
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("update-income"), CheckRoleAttribute]
    public async Task<IActionResult> UpdateIncome([FromBody] UpdateIncomeReq incomeReq)
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser.role != "Admin")
        {
            return Unauthorized(new UpdateIncomeRes { type = "error", message = "Bu işlemi gerçekleştirmek için yetkiniz yok." });
        }

        var income = await _incomeCollection.Find<Income>(i => i.id == currentUser.id).FirstOrDefaultAsync();

        if (income == null)
        {
            return NotFound(new UpdateIncomeRes { type = "error", message = "Güncellenmek istenen gelir kaydı bulunamadı." });
        }

        income.title = incomeReq.title;
        income.amount = incomeReq.amount;
        income.description = incomeReq.description;

        await _incomeCollection.ReplaceOneAsync(i => i.id == income.id, income);

        return Ok(new UpdateIncomeRes { message = "Gelir kaydı başarıyla güncellendi." });
    }

    #endregion

    #region Get Income
    [HttpGet("get-income"), CheckRoleAttribute]
    public async Task<IActionResult> GetIncome()
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser.role == "Admin")
        {
            var incomes = await _incomeCollection.Find(income => true).ToListAsync();
            return Ok(incomes);
        }
        else
        {
            var income = await _incomeCollection.Find<Income>(i => i.id == currentUser.id).ToListAsync();
            return Ok(income);
        }
    }

    #endregion

    #region DeleteIncome

    public class _deleteIncomeReq
    {
        [Required]
        public string incomeId { get; set; }
    }

    public class _deleteIncomeRes
    {
        [Required]
        public string type { get; set; } // success / error 
        public string message { get; set; }
    }

    [HttpPost("delete-income"), CheckRoleAttribute]
    public async Task<ActionResult<_deleteIncomeRes>> DeleteIncome([FromBody] _deleteIncomeReq data)
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser.role != "Admin")
        {
            return Unauthorized(new _deleteIncomeRes { type = "error", message = "Bu işlemi gerçekleştirmek için yetkiniz yok." });
        }

        var deleteResult = await _incomeCollection.DeleteOneAsync(i => i.id == data.incomeId);

        if (deleteResult.DeletedCount > 0)
        {
            return Ok(new _deleteIncomeRes { type = "success", message = "Gelir kaydı silindi." });
        }

        return NotFound(new _deleteIncomeRes { type = "error", message = "Silmek istenen gelir kaydı bulunamadı." });
    }

    #endregion
}