using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using Muhasebe.Attributes;
using Muhasebe.Services;
using MuhasebeFull.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using static AccountingController;
using Muhasebe.Models;

[Route("api/fixedexpenses")]
public class FixedExpensesController : ControllerBase
{
    private readonly IConnectionService _connectionService;
    private IMongoCollection<FixedExpenses> _fixedExpensesCollection;
    private IMongoCollection<Accounting> _accountingCollection;

    public FixedExpensesController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
        _fixedExpensesCollection = _connectionService.db().GetCollection<FixedExpenses>("FixedExpensesCollection");
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

    #region FixedExpensesAdd

    public class _addFixedExpensesReq
    {
        [Required]
        public string title { get; set; }
        public string? description { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        [Required]
        public decimal amount { get; set; }
    }

    public class _addFixedExpensesRes
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("addFixedExpenses"), CheckRoleAttribute]
    public ActionResult<_addFixedExpensesRes> AddFixedExpenses([FromBody] _addFixedExpensesReq expensesReq)
    {
        var currentUser = GetCurrentUserFromSession();

        FixedExpenses expenses = new FixedExpenses
        {
            title = expensesReq.title,
            description = expensesReq.description,
            amount = expensesReq.amount,
            userId = currentUser.id
        };

        _fixedExpensesCollection.InsertOne(expenses);

        return Ok(new _addFixedExpensesRes { message = "Sabit gider kaydı başarıyla oluşturuldu." });
    }

    #endregion

    #region Get Fixed Expenses

    public class _getFixedExpensesReq
    {
        public int page { get; set; } = 1;
        public int offset { get; set; } = 20;

        public string userId { get; set; }
    }

    public class _getFixedExpensesRes
    {
        public long? total { get; set; }
        public List<FixedExpenses>? data { get; set; }
        [Required]
        public string type { get; set; }
        public string? message { get; set; }
    }

    [HttpPost("getFixedExpenses"), CheckRoleAttribute]
    public async Task<ActionResult<_getFixedExpensesRes>> GetFixedExpenses([FromBody] _getFixedExpensesReq req)
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser == null)
            return Ok(new _getFixedExpensesRes { type = "error", message = "Oturum bilgisi bulunamadı." });

        var filterBuilder = Builders<FixedExpenses>.Filter;
        FilterDefinition<FixedExpenses> filter;

        if (currentUser.role.ToString() == "Admin" && !string.IsNullOrEmpty(req.userId))
        {
            filter = filterBuilder.Eq(e => e.userId, req.userId);
        }
        else if (currentUser.role.ToString() == "User")
        {
            filter = filterBuilder.Eq(e => e.userId, currentUser.id);
        }
        else
            filter = filterBuilder.Empty;

        var total = await _fixedExpensesCollection.Find(filter).CountDocumentsAsync();
        var expenses = await _fixedExpensesCollection.Find(filter)
                        .Skip((req.page - 1) * req.offset)
                        .Limit(req.offset)
                        .ToListAsync();

        return Ok(new _getFixedExpensesRes
        {
            total = total,
            data = expenses,
            type = "success",
            message = "Sabit gider kayıtları başarıyla alındı."
        });
    }
    #endregion






    #region UpdateFixedExpenses

    public class _updateFixedExpensesReq
    {
        [Required]
        public string title { get; set; }
        [Required]
        public string currency { get; set; }
        public string? description { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        [Required]
        public decimal amount { get; set; }
    }

    public class _updateFixedExpensesRes
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("updateFixedExpenses"), CheckRoleAttribute]
    public async Task<ActionResult<_updateFixedExpensesRes>> UpdateFixedExpenses([FromBody] _updateFixedExpensesReq req)
    {
        var currentUser = GetCurrentUserFromSession();

        var existingExpense = await _fixedExpensesCollection.Find<FixedExpenses>(expense => expense.id == currentUser.id).FirstOrDefaultAsync();
        if (existingExpense == null)
            return NotFound(new _updateFixedExpensesRes { type = "error", message = "Gider bulunamadı." });

        if (currentUser.role != "Admin" && existingExpense.userId != currentUser.id)
            return Unauthorized(new _updateFixedExpensesRes { type = "error", message = "Bu gideri güncelleme yetkiniz yok." });

        FixedExpenses updatedExpense = existingExpense;
        updatedExpense.currency = req.currency;
        updatedExpense.title = req.title;
        updatedExpense.description = req.description;
        updatedExpense.amount = req.amount;
        var update = Builders<FixedExpenses>.Update.Set(x => x.title, req.title).Set(x => x.amount, req.amount);
        if (!string.IsNullOrEmpty(req.description ?? ""))
        {
            update = update.Set(x => x.description, req.description);
        }
        await _fixedExpensesCollection.UpdateOneAsync(x => x.id == existingExpense.id, update, new UpdateOptions { IsUpsert = false });

        return Ok(new _updateFixedExpensesRes { message = "Sabit gider kaydı başarıyla güncellendi." });
    }

    #endregion

    #region DeleteFixedExpenses

    public class _deleteFixedExpenseReq
    {
        [Required]
        public string id { get; set; }
    }

    public class _deleteFixedExpenseRes
    {
        [Required]
        public string type { get; set; } // success / error 
        public string message { get; set; }
    }

    [HttpPost("deleteFixedExpenses"), CheckRoleAttribute]
    public async Task<ActionResult<_deleteFixedExpenseRes>> DeleteFixedExpenses([FromBody] _deleteFixedExpenseReq data)
    {
        var currentUser = GetCurrentUserFromSession();

        var existingExpense = await _fixedExpensesCollection.Find<FixedExpenses>(expense => expense.id == data.id).FirstOrDefaultAsync();
        if (existingExpense == null)
            return NotFound(new _deleteFixedExpenseRes { type = "error", message = "Gider bulunamadı." });

        if (currentUser.role != "Admin" && existingExpense.userId != currentUser.id)
            return Unauthorized(new _deleteFixedExpenseRes { type = "error", message = "Bu gideri silme yetkiniz yok." });

        _fixedExpensesCollection.DeleteOne(expense => expense.id == data.id);

        return Ok(new _deleteFixedExpenseRes { type = "success", message = "Sabit gider kaydı başarıyla silindi." });
    }

    #endregion
}
