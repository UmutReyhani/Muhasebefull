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

        if (currentUser == null)
        {
            return Unauthorized(new _addIncomeRes { type = "error", message = "Oturum bilgisi bulunamadı." });
        }

        Income income = new Income
        {
            title = incomeReq.title,
            description = incomeReq.description,
            amount = incomeReq.amount,
            date = DateTime.Now,
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
        [BsonRepresentation(BsonType.Decimal128)]
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
    public async Task<ActionResult<_updateIncomeRes>> UpdateIncome([FromBody] _updateIncomeReq req)
    {
        var currentUser = GetCurrentUserFromSession();

        var existingIncome = await _incomeCollection.Find<Income>(income => income.id == currentUser.id).FirstOrDefaultAsync();

        if (existingIncome == null)
        {
            return NotFound(new _updateIncomeRes { type = "error", message = "Gelir kaydı bulunamadı." });
        }

        if (currentUser.role != "Admin" && existingIncome.userId != currentUser.id)
        {
            return Unauthorized(new _updateIncomeRes { type = "error", message = "Bu gelir kaydını güncelleme yetkiniz yok." });
        }

        var update = Builders<Income>.Update
            .Set(x => x.title, req.title)
            .Set(x => x.amount, req.amount);


        if (!string.IsNullOrEmpty(req.description ?? ""))
        {
            update = update.Set(x => x.description, req.description);
        }

        await _incomeCollection.UpdateOneAsync(x => x.id == existingIncome.id, update, new UpdateOptions { IsUpsert = false });

        return Ok(new _updateIncomeRes { message = "Gelir kaydı başarıyla güncellendi." });
    }

    #endregion

    #region Get Income
    public class _incomeFilterRequest
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int page { get; set; } = 1;
        public int offset { get; set; } = 20;
    }


    public class _getIncomeRes
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; } = "Gelir kayıtları başarıyla alındı.";
        public List<Income> incomes { get; set; }
    }

    [HttpPost("getIncome"), CheckRoleAttribute]
    public async Task<ActionResult<_getIncomeRes>> GetIncome([FromBody] _incomeFilterRequest req)
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser == null)
        {
            return Unauthorized(new _getIncomeRes { type = "error", message = "Oturum bilgisi bulunamadı." });
        }

        var startDate = req.StartDate ?? DateTime.Now.Date;
        var endDate = req.EndDate ?? DateTime.Now.Date.AddDays(1);

        if ((endDate - startDate).Days > 31)
        {
            return BadRequest(new _getIncomeRes { type = "error", message = "Tarih aralığı maksimum 1 ay olabilir." });
        }

        IQueryable<Income> query = _incomeCollection.AsQueryable();

        query = query.Where(a => a.date >= startDate && a.date < endDate);

        if (currentUser.role != "Admin")
        {
            query = query.Where(a => a.userId == currentUser.id);
        }
        query = query.Skip((req.page - 1) * req.offset).Take(req.offset);

        var incomes = query.ToList();

        return Ok(new _getIncomeRes { incomes = incomes });
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