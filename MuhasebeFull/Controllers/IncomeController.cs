using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using Muhasebe.Attributes;
using Muhasebe.Services;
using MuhasebeFull.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Muhasebe.Models;

[Route("api/incomes")]
public class IncomesController : ControllerBase
{
    private readonly IConnectionService _connectionService;
    private IMongoCollection<Income> _incomeCollection;
    private IMongoCollection<Accounting> _accountingCollection;

    public IncomesController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
        _incomeCollection = _connectionService.db().GetCollection<Income>("IncomeCollection");
        _accountingCollection = _connectionService.db().GetCollection<Accounting>("AccountingCollection");

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

    public class _addIncomeReq
    {
        [Required]
        public string title { get; set; }
        public string? description { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        [Required]
        public decimal amount { get; set; }
    }

    public class _addIncomeRes
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("addIncome"), CheckRoleAttribute]
    public ActionResult<_addIncomeRes> AddIncome([FromBody] _addIncomeReq incomeReq)
    {
        var currentUser = GetCurrentUserFromSession();

        Income income = new Income
        {
            title = incomeReq.title,
            description = incomeReq.description,
            amount = incomeReq.amount,
            userId = currentUser.id
        };

        _incomeCollection.InsertOne(income);

        return Ok(new _addIncomeRes { message = "Gelir kaydı başarıyla oluşturuldu." });
    }

    #endregion

    #region UpdateIncome

    public class _updateIncomeReq
    {
        [Required]
        public string title { get; set; }
        public string? description { get; set; }
        [Required]
        public decimal amount { get; set; }
    }

    public class _updateIncomeRes
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("updateIncome"), CheckRoleAttribute]
    public async Task<ActionResult<_updateIncomeRes>> UpdateIncome([FromBody] _updateIncomeReq incomeReq)
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser.role != "Admin")
        {
            return Unauthorized(new _updateIncomeRes { type = "error", message = "Bu işlemi gerçekleştirmek için yetkiniz yok." });
        }

        var income = await _incomeCollection.Find<Income>(i => i.id == currentUser.id).FirstOrDefaultAsync();

        if (income == null)
        {
            return NotFound(new _updateIncomeRes { type = "error", message = "Güncellenmek istenen gelir kaydı bulunamadı." });
        }

        income.title = incomeReq.title;
        income.amount = incomeReq.amount;
        income.description = incomeReq.description;

        await _incomeCollection.ReplaceOneAsync(i => i.id == income.id, income);

        return Ok(new _updateIncomeRes { message = "Gelir kaydı başarıyla güncellendi." });
    }

    #endregion

    #region Get Income

    public class _incomeFilterRequest
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    [HttpPost("getIncome"), CheckRoleAttribute]
    public async Task<ActionResult<IEnumerable<Accounting>>> GetIncome([FromBody] _incomeFilterRequest filterRequest)
    {
        var currentUser = GetCurrentUserFromSession();

        var startDate = filterRequest.StartDate ?? DateTime.Now.Date;
        var endDate = filterRequest.EndDate ?? DateTime.Now.Date.AddDays(1);

        FilterDefinition<Accounting> filter = Builders<Accounting>.Filter.Eq(a => a.type, "Gelir")
            & Builders<Accounting>.Filter.Gte(a => a.date, startDate)
            & Builders<Accounting>.Filter.Lt(a => a.date, endDate);

        if (currentUser.role != "Admin")
        {
            filter = filter & Builders<Accounting>.Filter.Eq(a => a.userId, currentUser.id);
        }

        var incomes = await _accountingCollection.Find(filter).ToListAsync();

        return Ok(incomes);
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

    [HttpPost("deleteIncome"), CheckRoleAttribute]
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