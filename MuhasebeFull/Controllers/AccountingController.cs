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

[Route("api/accounting")]
public class AccountingController : ControllerBase
{
    private readonly IConnectionService _connectionService;
    private IMongoCollection<Accounting> _accountingCollection;
    private IMongoCollection<Log> _logCollection;
    private IMongoCollection<FixedExpenses> _fixedExpensesCollection;
    private IMongoCollection<Income> _incomeCollection;

    public AccountingController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
        _accountingCollection = _connectionService.db().GetCollection<Accounting>("AccountingCollection");
        _logCollection = _connectionService.db().GetCollection<Log>("LogCollection");
        _fixedExpensesCollection = _connectionService.db().GetCollection<FixedExpenses>("FixedExpensesCollection");
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

    #region AccountingADD

    public class AddAccountingRecordReq
    {
        public string type { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal amount { get; set; }
        public string currency { get; set; }

    }

    public class AddAccountingRecordRes
    {
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("add-record"), CheckRoleAttribute]
    public IActionResult AddAccountingRecord([FromBody] AddAccountingRecordReq recordReq)
    {
        var currentUser = GetCurrentUserFromSession();

        if (string.IsNullOrEmpty(recordReq.type) || (recordReq.type != "Gelir" && recordReq.type != "Gider"))
            return BadRequest(new AddAccountingRecordRes { type = "error", message = "Type sadece 'Gelir' ya da 'Gider' olabilir." });

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

        return Ok(new AddAccountingRecordRes { message = "Finansal kayıt oluşturuldu." });
    }

    #endregion

    #region AccountingGetRecord
    [HttpGet("get-records"), CheckRoleAttribute]
    public async Task<IActionResult> GetAccountingRecords()
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser.role == "Admin")
        {
            var records = await _accountingCollection.Find(record => true).ToListAsync();
            return Ok(records);
        }
        else if (currentUser.role == "User")
        {
            var userRecords = await _accountingCollection.Find<Accounting>(r => r.userId == currentUser.id).ToListAsync();
            if (userRecords == null || userRecords.Count == 0)
                return NotFound("Kullanıcıya ait finansal kayıt bulunamadı.");

            return Ok(userRecords);
        }
        else
        {
            return Unauthorized("Bu işlemi gerçekleştirmek için yetkiniz yok.");
        }
    }
    #endregion

    #region AccountingUpdateRecord

    public class UpdateAccountingRecordReq
    {
        public string type { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal amount { get; set; }
        public string currency { get; set; }
    }

    public class UpdateAccountingRecordRes
    {
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("update-record"), CheckRoleAttribute]
    public async Task<IActionResult> UpdateRecord([FromBody] UpdateAccountingRecordReq updateReq)
    {
        var currentUser = GetCurrentUserFromSession();
        var existingRecord = await _accountingCollection.Find<Accounting>(r => r.id == currentUser.id).FirstOrDefaultAsync();

        if (existingRecord == null)
            return NotFound(new UpdateAccountingRecordRes { type = "error", message = "Güncellenmek istenen finansal kayıt bulunamadı." });

        if (currentUser.role != "Admin" && existingRecord.userId != currentUser.id)
            return Unauthorized(new UpdateAccountingRecordRes { type = "error", message = "Bu kaydı güncellemek için yetkiniz bulunmamaktadır." });

        string oldValue = JsonSerializer.Serialize(existingRecord);

        existingRecord.amount = updateReq.amount;
        existingRecord.currency = updateReq.currency;
        existingRecord.type = updateReq.type;

        await _accountingCollection.ReplaceOneAsync(r => r.id == currentUser.id, existingRecord);

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

        return Ok(new UpdateAccountingRecordRes { message = "Finansal kayıt başarıyla güncellendi." });
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

    [HttpPost("delete-record"), CheckRoleAttribute]
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
    [HttpGet("accounting-reports"), CheckRoleAttribute]
    public IActionResult GetAccountingSummary([FromQuery] string interval = "1month")
    {
        DateTime now = DateTime.Now;
        DateTime startdate = now;
        DateTime enddate = now;

        switch (interval)
        {
            case "15days":
                startdate = now.Date.AddDays(-15);
                enddate = now.Date.AddDays(1);
                break;
            case "1month":
                startdate = now.Date.AddMonths(-1);
                enddate = now.Date.AddDays(1);
                break;
            default:
                return BadRequest("Geçersiz Tarih Aralığı(15days)-(1month) doğru kullanım olacak.");
        }

        var currentUser = GetCurrentUserFromSession();
        if (currentUser == null)
            return Unauthorized();

        var dateFilter = _accountingCollection.AsQueryable().Where(x => x.date > startdate && x.date < enddate);


        if (currentUser.role.ToString() != "Admin")
        {
            dateFilter = dateFilter.Where(x => x.userId == currentUser.id);
        }

        //////RAPORLAMA
        //fixed expenses all
        var totalFixedExpenses = _fixedExpensesCollection.AsQueryable().Sum(f => f.amount);
        // Diğer giderlerle birlikte toplam gideri hesapla
        var gelirr = dateFilter.Where(x => x.type == "Gelir").Sum(x => x.amount);
        var fixedgider = dateFilter.Where(x => x.type == "Gider").Sum(x => x.amount) + totalFixedExpenses;
        var bakiyee = gelirr - fixedgider;

        // Sabit gelirlerin toplamını al
        var totalIncome = _incomeCollection.AsQueryable().Sum(i => i.amount);
        // Diğer gelirlerle birlikte toplam geliri hesapla
        var gelirrr = dateFilter.Where(x => x.type == "Gelir").Sum(x => x.amount) + totalIncome;


        var gelir = dateFilter.Where(x => x.type == "Gelir").Sum(x => x.amount);
        var gider = dateFilter.Where(x => x.type == "Gider").Sum(x => x.amount);
        var bakiye = gelir - gider;

        return Ok(new
        {
            EskiGelir = gelir,
            EskiGider = gider,
            EskiBakiye = bakiye,
            YeniGelir = gelirrr,
            YeniGider = fixedgider,
            YeniBakiye = bakiyee
        });
    }

    #endregion
}
