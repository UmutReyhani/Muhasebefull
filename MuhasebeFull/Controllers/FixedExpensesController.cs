using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using Muhasebe.Attributes;
using Muhasebe.Services;
using MuhasebeFull.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

[Route("api/fixedexpenses")]
public class FixedExpensesController : ControllerBase
{
    private readonly IConnectionService _connectionService;
    private IMongoCollection<FixedExpenses> _fixedExpensesCollection;

    public FixedExpensesController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
        _fixedExpensesCollection = _connectionService.db().GetCollection<FixedExpenses>("FixedExpensesCollection");
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

    public class AddFixedExpensesReq
    {
        public string title { get; set; }
        public string? description { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal amount { get; set; }
    }

    public class AddFixedExpensesRes
    {
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("add-fixed-expenses"), CheckRoleAttribute]
    public IActionResult AddFixedExpenses([FromBody] AddFixedExpensesReq expensesReq)
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

        return Ok(new AddFixedExpensesRes { message = "Sabit gider kaydı başarıyla oluşturuldu." });
    }

    #endregion

    #region Get Fixed Expenses
    [HttpGet("get-fixed-expenses"), CheckRoleAttribute]
    public async Task<IActionResult> GetFixedExpenses()
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser.role == "Admin")
        {
            var expenses = await _fixedExpensesCollection.Find(expense => true).ToListAsync();
            return Ok(expenses);
        }
        else if (currentUser.role == "User")
        {
            var userExpenses = await _fixedExpensesCollection.Find<FixedExpenses>(expense => expense.userId == currentUser.id).ToListAsync();
            return Ok(userExpenses);
        }
        else
        {
            return Unauthorized("Bu işlemi gerçekleştirmek için yetkiniz yok.");
        }
    }
    #endregion

    #region UpdateFixedExpenses

    public class UpdateFixedExpensesReq
    {
        public string title { get; set; }
        public string? description { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal amount { get; set; }
    }

    public class UpdateFixedExpensesRes
    {
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("update-fixed-expenses"), CheckRoleAttribute]
    public async Task<IActionResult> UpdateFixedExpenses([FromBody] UpdateFixedExpensesReq expensesReq)
    {
        var currentUser = GetCurrentUserFromSession();

        var existingExpense = await _fixedExpensesCollection.Find<FixedExpenses>(expense => expense.id == currentUser.id).FirstOrDefaultAsync();
        if (existingExpense == null)
            return NotFound(new UpdateFixedExpensesRes { type = "error", message = "Gider bulunamadı." });

        if (currentUser.role != "Admin" && existingExpense.userId != currentUser.id)
            return Unauthorized(new UpdateFixedExpensesRes { type = "error", message = "Bu gideri güncelleme yetkiniz yok." });

        FixedExpenses updatedExpense = existingExpense;
        updatedExpense.title = expensesReq.title;
        updatedExpense.description = expensesReq.description;
        updatedExpense.amount = expensesReq.amount;

        await _fixedExpensesCollection.ReplaceOneAsync(expense => expense.id == updatedExpense.id, updatedExpense);

        return Ok(new UpdateFixedExpensesRes { message = "Sabit gider kaydı başarıyla güncellendi." });
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

    [HttpPost("delete-fixed-expenses"), CheckRoleAttribute]
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
   