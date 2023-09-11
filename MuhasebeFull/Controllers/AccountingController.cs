using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using Muhasebe.Attributes;
using Muhasebe.Models;
using Muhasebe.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MuhasebeFull.Models;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Identity;

[Route("api/accounting")]
public class AccountingController : ControllerBase
{
    private readonly IConnectionService _connectionService;
    private IMongoCollection<Accounting> _accountingCollection;
    private IMongoCollection<Log> _logCollection;
    private IMongoCollection<FixedExpenses> _fixedExpensesCollection;
    private IMongoCollection<Income> _incomeCollection;
    private IMongoCollection<Merchant> _merchantCollection;

    public AccountingController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
        _accountingCollection = _connectionService.db().GetCollection<Accounting>("AccountingCollection");
        _logCollection = _connectionService.db().GetCollection<Log>("LogCollection");
        _fixedExpensesCollection = _connectionService.db().GetCollection<FixedExpenses>("FixedExpensesCollection");
        _incomeCollection = _connectionService.db().GetCollection<Income>("IncomeCollection");
        _merchantCollection = _connectionService.db().GetCollection<Merchant>("MerchantCollection");
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

    #region AccountingADD

    public class _addAccountingRecordReq
    {
        [Required]
        public string type { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        [Required]
        public decimal amount { get; set; }
        [Required]
        public string currency { get; set; }

    }

    public class _addAccountingRecordRes
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("addRecord"), CheckRoleAttribute]
    public ActionResult<_addAccountingRecordRes> AddAccountingRecord([FromBody] _addAccountingRecordReq recordReq)
    {
        var currentUser = GetCurrentUserFromSession();

        if (string.IsNullOrEmpty(recordReq.type) || (recordReq.type != "Gelir" && recordReq.type != "Gider"))
            return BadRequest(new _addAccountingRecordRes { type = "error", message = "Type sadece 'Gelir' ya da 'Gider' olabilir." });

        Accounting record = new Accounting
        {
            type = recordReq.type,
            userId = currentUser.id,
            date = DateTime.Now,
            amount = recordReq.amount,
            currency = recordReq.currency,
        };

        _accountingCollection.InsertOne(record);

        Log log = new Log
        {
            userId = currentUser.id,
            actionType = "Add",
            target = "Accounting",
            itemId = record.id,
            newValue = JsonSerializer.Serialize(record),
            date = DateTime.UtcNow
        };
        _logCollection.InsertOne(log);

        return Ok(new _addAccountingRecordRes { message = "Finansal kayıt oluşturuldu." });
    }

    #endregion

    #region AccountingGetRecord

    public class _accountingRecordReq
    {
        public string? userId { get; set; }
        public string? type { get; set; }
        public int page { get; set; } = 1;
        public int offset { get; set; } = 20;
    }

    public class _getAccountingRecordRes
    {
        public long? total { get; set; }
        public List<Accounting>? data { get; set; }
        [Required]
        public string type { get; set; } = "success";
        public string? message { get; set; }
    }

    [HttpPost("getRecords"), CheckRoleAttribute]
    public ActionResult<_getAccountingRecordRes> GetAccountingRecords([FromBody] _accountingRecordReq req)
    {
        var currentUser = GetCurrentUserFromSession();


        if (!string.IsNullOrEmpty(req.type) && (req.type != "Gelir" | req.type != "Gider"))
        {
            return Ok(new _getAccountingRecordRes { type = "error", message = "" });
        }

        var query = _accountingCollection.AsQueryable()
            .Where(x =>
                (string.IsNullOrEmpty(req.userId) | (!string.IsNullOrEmpty(req.userId) && x.userId == req.userId))
                & (string.IsNullOrEmpty(req.type) | (!string.IsNullOrEmpty(req.type) && x.type == req.type))
                & (currentUser.role == "Admin" | (currentUser.role != "Admin" && req.userId == currentUser.id))
            );


        var total = query.Count();
        var records = query
                            .Skip((req.page - 1) * req.offset)
                            .Take(req.offset)
                            .ToList();
        return Ok(new _getAccountingRecordRes
        {
            total = total,
            data = records,
            message = records.Count > 0 ? "Kayıtlar başarıyla getirildi." : "Kayıt bulunamadı."
        });
    }

    #endregion

    #region AccountingUpdateRecord

    public class _updateAccountingRecordReq
    {
        [Required]
        public string id { get; set; }
        [Required]
        public string type { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        [Required]
        public decimal amount { get; set; }
        [Required]
        public string currency { get; set; }
    }

    public class _updateAccountingRecordRes
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("updateRecord"), CheckRoleAttribute]
    public async Task<ActionResult<_updateAccountingRecordRes>> UpdateAccountingRecord([FromBody] _updateAccountingRecordReq updateReq)

    {
        var currentUser = GetCurrentUserFromSession();
        var existingRecord = await _accountingCollection.Find<Accounting>(r => r.id == updateReq.id).FirstOrDefaultAsync();

        if (existingRecord == null)
            return NotFound(new _updateAccountingRecordRes { type = "error", message = "Güncellenmek istenen finansal kayıt bulunamadı." });

        if (currentUser.role != "Admin" && existingRecord.userId != currentUser.id)
            return Unauthorized(new _updateAccountingRecordRes { type = "error", message = "Bu kaydı güncellemek için yetkiniz bulunmamaktadır." });

        string oldValue = JsonSerializer.Serialize(existingRecord);

        existingRecord.amount = updateReq.amount;
        existingRecord.currency = updateReq.currency;
        existingRecord.type = updateReq.type;

        await _accountingCollection.UpdateOneAsync(x => x.id == existingRecord.id, Builders<Accounting>.Update.Set(x => x.amount, updateReq.amount).Set(x => x.currency, updateReq.currency).Set(x => x.type, updateReq.type), new UpdateOptions { IsUpsert = false });

        string newValue = JsonSerializer.Serialize(existingRecord);

        Log log = new Log
        {
            userId = currentUser.id,
            actionType = "Update",
            target = "Accounting",
            itemId = existingRecord.id,
            oldValue = oldValue,
            newValue = newValue,
            date = DateTime.UtcNow
        };
        _logCollection.InsertOne(log);

        return Ok(new _updateAccountingRecordRes { message = "Finansal kayıt başarıyla güncellendi." });
    }

    #endregion

    #region Accounting DeleteRecord

    public class _deleteRecordReq
    {
        [Required]
        public string id { get; set; }
    }

    public class _deleteRecordRes
    {
        [Required]
        public string type { get; set; } // success / error 
        public string message { get; set; }
    }

    [HttpPost("deleteRecord"), CheckRoleAttribute]
    public async Task<ActionResult<_deleteRecordRes>> DeleteRecord([FromBody] _deleteRecordReq data)
    {
        var currentUser = GetCurrentUserFromSession();

        var existingRecord = await _accountingCollection.Find<Accounting>(r => r.id == data.id).FirstOrDefaultAsync();

        if (existingRecord == null)
            return NotFound(new _deleteRecordRes { type = "error", message = "Silmek istenen finansal kayıt bulunamadı." });

        if (currentUser.role != "Admin" && existingRecord.userId != currentUser.id)
            return Unauthorized(new _deleteRecordRes { type = "error", message = "Bu kaydı silmek için yetkiniz bulunmamaktadır." });

        string oldValue = JsonSerializer.Serialize(existingRecord);

        await _accountingCollection.DeleteOneAsync(r => r.id == data.id);

        Log log = new Log
        {
            userId = currentUser.id,
            actionType = "Delete",
            target = "Accounting",
            itemId = data.id,
            oldValue = oldValue,
            newValue = null,
            date = DateTime.UtcNow
        };
        _logCollection.InsertOne(log);

        return Ok(new _deleteRecordRes { type = "success", message = "Finansal kayıt başarıyla silindi." });
    }

    #endregion

    #region Accounting Reports
    public class _accReportsReq
    {
        public string? type { get; set; }
        public string? userId { get; set; }
        public string? incomeId { get; set; }
        public string? merchantId { get; set; }
        public string? fixedExpensesId { get; set; }
    }

    public class _accReportsRes
    {
        [Required]
        public string type { get; set; }
        public string? message { get; set; }
        public decimal? totalIncome { get; set; }
        public decimal? totalExpenses { get; set; }
        public string? merchantTitle { get; set; }
        public decimal? balance { get; set; }
    }

    [HttpPost("accountingReports"), CheckRoleAttribute]
    public ActionResult<_accReportsRes> GetAccountingSummary([FromBody] _accReportsReq data)
    {
        var filterBuilder = Builders<Accounting>.Filter;
        var filter = filterBuilder.Empty;
        var currentUser = GetCurrentUserFromSession();

        if (!string.IsNullOrEmpty(data.userId))
        {
            filter = filter & filterBuilder.Eq(a => a.userId, data.userId);
        }

        if (!string.IsNullOrEmpty(data.merchantId))
        {
            filter = filter & filterBuilder.Eq(a => a.merchantId, data.merchantId);
        }
        if (!string.IsNullOrEmpty(data.incomeId))
        {
            filter = filter & filterBuilder.Eq(a => a.incomeId, data.incomeId);
        }

        if (!string.IsNullOrEmpty(data.fixedExpensesId))
        {
            filter = filter & filterBuilder.Eq(a => a.fixedExpensesId, data.fixedExpensesId);
        }

        var dateFilter = _accountingCollection.Find(filter).ToList();

        var totalFixedExpenses = _fixedExpensesCollection.AsQueryable().Sum(f => f.amount);
        var gelir = dateFilter.Where(x => x.type == "Gelir").Sum(x => x.amount);
        var gider = dateFilter.Where(x => x.type == "Gider").Sum(x => x.amount);
        var bakiye = gelir - gider;

        return Ok(new _accReportsRes
        {
            totalIncome = gelir,
            totalExpenses = gider,
            balance = bakiye,
            message = "Rapor başarıyla oluşturuldu."
        });
    }

    #endregion

}
