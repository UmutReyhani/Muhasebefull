using Muhasebe.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Muhasebe.Models;
using System.Text.Json;
using Muhasebe.Attributes;
using MongoDB.Driver.Linq;
using MuhasebeFull.Models;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;

namespace Muhasebe.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        private IMongoCollection<User> _userCollection;
        private IMongoCollection<Accounting> _accountingCollection;
        private IMongoCollection<Merchant> _merchantCollection;
        private IMongoCollection<FixedExpenses> _fixedExpensesCollection;
        private IMongoCollection<Income> _incomeCollection;
        private IMongoCollection<Log> _logCollection;


        private string ComputeSha256Hash(string rawData)
        {
            using (System.Security.Cryptography.SHA256 sha256Hash = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawData));

                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public UserController(IConnectionService connectionService)
        {
            _connectionService = connectionService;
            _userCollection = _connectionService.db().GetCollection<User>("UserCollection");
            _accountingCollection = _connectionService.db().GetCollection<Accounting>("AccountingCollection");
            _merchantCollection = _connectionService.db().GetCollection<Merchant>("MerchantCollection");
            _fixedExpensesCollection = _connectionService.db().GetCollection<FixedExpenses>("FixedExpensesCollection");
            _incomeCollection = _connectionService.db().GetCollection<Income>("IncomeCollection");
            _logCollection = _connectionService.db().GetCollection<Log>("LogCollection");
        }

        #region CreateUser

        public class _createUserReq
        {
            [Required]
            public string username { get; set; }
            [Required]
            public string password { get; set; }
            [Required]
            public string role { get; set; }
        }

        public class _createUserRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("createuser")]
        public ActionResult<_createUserRes> CreateUser([FromBody] _createUserReq data)
        {
            if (data.role != "Admin" && data.role != "User")
                return BadRequest(new _createUserRes { type = "error", message = "role can only be 'Admin' or 'User'." });

            var existingUser = _userCollection.Find<User>(u => u.username == data.username).FirstOrDefault();

            if (existingUser != null)
            {
                return BadRequest(new _createUserRes { type = "error", message = "Bu isimde bir kullanıcı mevcut. Başka bir kullanıcı adı seçin." });
            }

            User newUser = new User
            {
                username = data.username,
                password = ComputeSha256Hash(data.password),
                role = data.role,
                status = "Active",
                register = DateTime.Now,
                lastLogin = null
            };

            _userCollection.InsertOne(newUser);

            return Ok(new _createUserRes { type = "success", message = "Kullanıcı başarıyla oluşturuldu." });
        }

        #endregion

        #region GetUser
        [HttpGet("getuserdetail"), CheckRoleAttribute]
        public async Task<IActionResult> GetUser()
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser.role == "Admin")
            {
                var users = await _userCollection.Find(user => true).ToListAsync();
                return Ok(users);
            }
            else if (currentUser.role == "User")
            {
                var user = await _userCollection.Find<User>(u => u.id == currentUser.id).FirstOrDefaultAsync();
                if (user == null)
                    return NotFound("Kullanıcı bulunamadı.");

                return Ok(user);
            }
            else
            {
                return Unauthorized("Bu işlemi gerçekleştirmek için yetkiniz yok.");
            }
        }
        #endregion

        #region UpdateUser

        public class _updateUserReq
        {
            [Required]
            public string id { get; set; }
            [Required]
            public string password { get; set; }
        }
        public class _updateUserRes
        {
            [Required]
            public string type { get; set; } // success / error 
            public string message { get; set; }
        }

        [HttpPost("updateuser"), CheckRoleAttribute]
        public ActionResult<_updateUserRes> UpdateUser([FromBody] _updateUserReq data)
        {
            var currentUser = GetCurrentUserFromSession();

            var userToBeUpdated = _userCollection.Find<User>(u => u.id == data.id).FirstOrDefault();

            if (userToBeUpdated == null)
            {
                return NotFound("Güncellenmek istenen kullanıcı bulunamadı.");
            }

            var oldValue = JsonSerializer.Serialize(userToBeUpdated);

            if (currentUser.role == "User")
            {
                if (currentUser.id != userToBeUpdated.id)
                {
                    return Unauthorized("Sadece kendi şifrenizi güncelleyebilirsiniz.");
                }
                userToBeUpdated.password = ComputeSha256Hash(data.password);
            }
            else if (currentUser.role == "Admin")
            {
                userToBeUpdated.password = ComputeSha256Hash(data.password);
            }

            _userCollection.ReplaceOne(u => u.id == data.id, userToBeUpdated);

            var newValue = JsonSerializer.Serialize(userToBeUpdated);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Update",
                targetModel = "User",
                itemId = userToBeUpdated.id,
                oldValue = oldValue,
                newValue = newValue,
                date = DateTime.UtcNow
            };
            _logCollection.InsertOne(log);

            return Ok(new _updateUserRes { type = "success", message = "Kullanıcı başarıyla güncellendi." });
        }

        #endregion

        #region DeleteUser
        [HttpDelete("deleteuser/{id:length(24)}"), CheckRoleAttribute]
        public IActionResult DeleteUser(string id)
        {
            var currentUser = GetCurrentUserFromSession();

            var userToDelete = _userCollection.Find<User>(u => u.id == id).FirstOrDefault();
            if (userToDelete == null)
            {
                return NotFound("Kullanıcı bulunamadı.");
            }

            if (currentUser.role == "User" && currentUser.id != userToDelete.id)
            {
                return Unauthorized("Sadece kendi hesabınızı silebilirsiniz.");
            }

            var oldValue = JsonSerializer.Serialize(userToDelete);

            _userCollection.DeleteOne(u => u.id == id);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Delete",
                targetModel = "User",
                itemId = userToDelete.id,
                oldValue = oldValue,
                newValue = null,
                date = DateTime.UtcNow
            };
            _logCollection.InsertOne(log);

            return Ok(new { Message = "Kullanıcı başarıyla silindi." });
        }

        #endregion

        #region UserLogin

        public class _loginReq
        {
            [Required]
            public string username { get; set; }
            [Required]
            public string password { get; set; }
        }

        public class _loginRes
        {
            public string type { get; set; } // success / error 
            public string message { get; set; }
        }

        [HttpPost("login")]
        public ActionResult<_loginRes> Login([FromBody] _loginReq loginData)
        {
            if (loginData == null || string.IsNullOrEmpty(loginData.username) || string.IsNullOrEmpty(loginData.password))
            {
                return BadRequest(new _loginRes { type = "error", message = "Kullanıcı adı veya şifre eksik." });
            }

            var userInDb = _userCollection.Find<User>(u => u.username == loginData.username && u.password == ComputeSha256Hash(loginData.password)).FirstOrDefault();

            if (userInDb == null)
            {
                return Unauthorized(new _loginRes { type = "error", message = "Kullanıcı adı veya şifre hatalı." });
            }

            SetCurrentUserToSession(userInDb);

            return Ok(new _loginRes { type = "success", message = "Giriş Başarılı" });
        }

        #endregion

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

        #region Userstatus

        public class _userstatusReq
        {
            [Required]
            public string Id { get; set; }
        }

        public class _userstatusRes
        {
            public string type { get; set; } // success / error 
            public string message { get; set; }
        }

        [HttpPost("userstatus")]
        public ActionResult<_userstatusRes> Userstatus([FromBody] _userstatusReq statusData)
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser.role != "Admin")
            {
                return Unauthorized(new _userstatusRes { type = "error", message = "Bu işlemi gerçekleştirmek için yetkiniz yok." });
            }

            var userToBeUpdated = _userCollection.Find<User>(u => u.id == statusData.Id).FirstOrDefault();
            if (userToBeUpdated == null)
            {
                return NotFound(new _userstatusRes { type = "error", message = "Durumu değiştirilmek istenen kullanıcı bulunamadı." });
            }

            var oldValue = userToBeUpdated.status;

            userToBeUpdated.status = userToBeUpdated.status == "Active" ? "Passive" : "Active";
            _userCollection.ReplaceOne(u => u.id == statusData.Id, userToBeUpdated);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Update",
                targetModel = "User",
                itemId = userToBeUpdated.id,
                oldValue = oldValue,
                newValue = userToBeUpdated.status,
                date = DateTime.UtcNow
            };
            _logCollection.InsertOne(log);

            return Ok(new _userstatusRes { type = "success", message = $"Kullanıcının durumu {userToBeUpdated.status} olarak güncellendi." });
        }

        #endregion


        #region Accounting ADD
        [HttpPost("add-record"), CheckRoleAttribute]
        public IActionResult AddAccountingRecord([FromBody] Accounting record)
        {
            var currentUser = GetCurrentUserFromSession();

            if (string.IsNullOrEmpty(record.type) || (record.type != "Gelir" && record.type != "Gider"))
                return BadRequest(new { Message = "Type sadece 'Gelir' ya da 'Gider' olabilir." });

            record.userId = currentUser.id;
            record.date = DateTime.Now;

            //boş olabilirler
            if (string.IsNullOrEmpty(record.incomeId))
                record.incomeId = null;
            if (string.IsNullOrEmpty(record.merchantId))
                record.merchantId = null;
            if (string.IsNullOrEmpty(record.fixedExpensesId))
                record.fixedExpensesId = null;

            _accountingCollection.InsertOne(record);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Add",
                targetModel = "Accounting",
                itemId = record.id,
                newValue = JsonSerializer.Serialize(record),
                date = DateTime.UtcNow
            };
            _logCollection.InsertOne(log);

            return Ok(new { Message = "Finansal kayıt oluşturuldu." });
        }


        #endregion

        #region Accounting Get Record
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

        #region Accounting Update Record
        [HttpPost("update-record"), CheckRoleAttribute]
        public async Task<IActionResult> UpdateRecord([FromBody] Accounting updatedRecord)
        {
            var currentUser = GetCurrentUserFromSession();

            var existingRecord = await _accountingCollection.Find<Accounting>(r => r.id == updatedRecord.id).FirstOrDefaultAsync();

            if (existingRecord == null)
                return NotFound("Güncellenmek istenen finansal kayıt bulunamadı.");

            if (currentUser.role != "Admin" && existingRecord.userId != currentUser.id)
                return Unauthorized("Bu kaydı güncellemek için yetkiniz bulunmamaktadır.");

            string oldValue = JsonSerializer.Serialize(existingRecord);

            existingRecord.amount = updatedRecord.amount;
            existingRecord.currency = updatedRecord.currency;
            existingRecord.type = updatedRecord.type;

            await _accountingCollection.ReplaceOneAsync(r => r.id == updatedRecord.id, existingRecord);

            string newValue = JsonSerializer.Serialize(existingRecord);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Update",
                targetModel = "Accounting",
                itemId = existingRecord.id,
                oldValue = oldValue,
                newValue = newValue,
                date = DateTime.UtcNow
            };
            _logCollection.InsertOne(log);

            return Ok(new { Message = "Finansal kayıt başarıyla güncellendi." });
        }


        #endregion

        #region Accounting Delete Record
        [HttpDelete("delete-record/{id}"), CheckRoleAttribute]
        public async Task<IActionResult> DeleteRecord(string id)
        {
            var currentUser = GetCurrentUserFromSession();

            var existingRecord = await _accountingCollection.Find<Accounting>(r => r.id == id).FirstOrDefaultAsync();

            if (existingRecord == null)
                return NotFound("Silmek istenen finansal kayıt bulunamadı.");

            if (currentUser.role != "Admin" && existingRecord.userId != currentUser.id)
                return Unauthorized("Bu kaydı silmek için yetkiniz bulunmamaktadır.");

            string oldValue = JsonSerializer.Serialize(existingRecord);

            await _accountingCollection.DeleteOneAsync(r => r.id == id);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Delete Accounting Record",
                targetModel = "Accounting",
                itemId = id,
                oldValue = oldValue,
                newValue = null,
                date = DateTime.UtcNow
            };
            _logCollection.InsertOne(log);

            return Ok(new { Message = "Finansal kayıt başarıyla silindi." });
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

        #region Create Fixed Expenses
        [HttpPost("add-fixed-expenses"), CheckRoleAttribute]
        public IActionResult AddFixedExpenses([FromBody] FixedExpenses expenses)
        {
            var currentUser = GetCurrentUserFromSession();

            expenses.userId = currentUser.id;

            _fixedExpensesCollection.InsertOne(expenses);

            return Ok(new { Message = "Sabit gider kaydı başarıyla oluşturuldu." });
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

        #region Update Fixed Expenses
        [HttpPost("update-fixed-expenses"), CheckRoleAttribute]
        public async Task<IActionResult> UpdateFixedExpenses([FromBody] FixedExpenses expenses)
        {
            var currentUser = GetCurrentUserFromSession();

            var existingExpense = await _fixedExpensesCollection.Find<FixedExpenses>(expense => expense.id == expenses.id).FirstOrDefaultAsync();
            if (existingExpense == null)
                return NotFound("Gider bulunamadı.");

            if (currentUser.role != "Admin" && existingExpense.userId != currentUser.id)
                return Unauthorized("Bu gideri güncelleme yetkiniz yok.");

            _fixedExpensesCollection.ReplaceOne(expense => expense.id == expenses.id, expenses);

            return Ok(new { Message = "Sabit gider kaydı başarıyla güncellendi." });
        }
        #endregion

        #region Delete Fixed Expenses
        [HttpDelete("delete-fixed-expenses/{id}"), CheckRoleAttribute]
        public async Task<IActionResult> DeleteFixedExpenses(string id)
        {
            var currentUser = GetCurrentUserFromSession();

            var existingExpense = await _fixedExpensesCollection.Find<FixedExpenses>(expense => expense.id == id).FirstOrDefaultAsync();
            if (existingExpense == null)
                return NotFound("Gider bulunamadı.");

            if (currentUser.role != "Admin" && existingExpense.userId != currentUser.id)
                return Unauthorized("Bu gideri silme yetkiniz yok.");

            _fixedExpensesCollection.DeleteOne(expense => expense.id == id);

            return Ok(new { Message = "Sabit gider kaydı başarıyla silindi." });
        }
        #endregion

        #region Add Income
        [HttpPost("add-income"), CheckRoleAttribute]
        public IActionResult AddIncome([FromBody] Income income)
        {
            var currentUser = GetCurrentUserFromSession();

            income.userId = currentUser.id;
            _incomeCollection.InsertOne(income);
            return Ok(new { Message = "Gelir kaydı oluşturuldu." });
        }

        #endregion

        #region Update Income
        [HttpPost("update-income"), CheckRoleAttribute]
        public async Task<IActionResult> UpdateIncome([FromBody] Income incomeToUpdate)
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser.role != "Admin")
            {
                return Unauthorized("Bu işlemi gerçekleştirmek için yetkiniz yok.");
            }

            var income = await _incomeCollection.Find<Income>(i => i.id == incomeToUpdate.id).FirstOrDefaultAsync();

            if (income == null)
            {
                return NotFound("Güncellenmek istenen gelir kaydı bulunamadı.");
            }

            income.title = incomeToUpdate.title;
            income.amount = incomeToUpdate.amount;
            income.description = incomeToUpdate.description;

            await _incomeCollection.ReplaceOneAsync(i => i.id == income.id, income);

            return Ok(new { Message = "Gelir kaydı güncellendi." });
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

        #region Delete Income 
        [HttpPost("delete-income"), CheckRoleAttribute]
        public async Task<IActionResult> DeleteIncome([FromBody] string incomeId)
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser.role != "Admin")
            {
                return Unauthorized("Bu işlemi gerçekleştirmek için yetkiniz yok.");
            }

            var deleteResult = await _incomeCollection.DeleteOneAsync(i => i.id == incomeId);

            if (deleteResult.DeletedCount > 0)
            {
                return Ok(new { Message = "Gelir kaydı silindi." });
            }

            return NotFound("Silmek istenen gelir kaydı bulunamadı.");
        }
        #endregion

        #region Add Merchant
        [HttpPost("add-merchant"), CheckRoleAttribute]
        public IActionResult AddMerchant([FromBody] Merchant merchant)
        {
            var currentUser = GetCurrentUserFromSession();
            if (currentUser == null)
                return Unauthorized();

            merchant.userId = currentUser.id;
            merchant.date = DateTime.Now;

            _merchantCollection.InsertOne(merchant);
            return Ok(new { Message = "cari kaydedildi." });
        }
        #endregion

        #region Update Merchant
        [HttpPost("update-merchant"), CheckRoleAttribute]
        public async Task<IActionResult> UpdateMerchant([FromBody] Merchant merchantToUpdate)
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser.role != "Admin")
            {
                return Unauthorized("Bu işlemi gerçekleştirmek için yetkiniz yok.");
            }

            var merchant = await _merchantCollection.Find<Merchant>(m => m.id == merchantToUpdate.id).FirstOrDefaultAsync();

            if (merchant == null)
            {
                return NotFound("Güncellenmek istenen cari bulunamadı.");
            }

            merchant.title = merchantToUpdate.title;

            await _merchantCollection.ReplaceOneAsync(m => m.id == merchant.id, merchant);

            return Ok(new { Message = "cari bilgisi güncellendi." });
        }

        #endregion

        #region Get Merchant
        [HttpGet("get-merchant"), CheckRoleAttribute]
        public async Task<IActionResult> GetMerchant()
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser.role == "Admin")
            {
                var merchants = await _merchantCollection.Find(merchant => true).ToListAsync();
                return Ok(merchants);
            }
            else
            {
                var merchant = await _merchantCollection.Find<Merchant>(m => m.userId == currentUser.id).ToListAsync();
                return Ok(merchant);
            }
        }

        #endregion

        #region Delete Merhant
        [HttpPost("delete-merchant"), CheckRoleAttribute]
        public async Task<IActionResult> DeleteMerchant([FromBody] string merchantId)
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser.role != "Admin")
            {
                return Unauthorized("Bu işlemi gerçekleştirmek için yetkiniz yok.");
            }

            var deleteResult = await _merchantCollection.DeleteOneAsync(m => m.id == merchantId);

            if (deleteResult.DeletedCount > 0)
            {
                return Ok(new { Message = "Car kaydı silindi." });
            }

            return NotFound("Silmek istenen cari bulunamadı.");
        }

        #endregion
    }

}

