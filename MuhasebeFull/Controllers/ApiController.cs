using Muhasebe.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Muhasebe.Models;
using System.Text.Json;
using Muhasebe.Attributes;
using MongoDB.Driver.Linq;
using MuhasebeFull.Models;

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
        [HttpPost("createuser")]
        public IActionResult CreateUser([FromBody] User user)
        {
            if (user.Role != "Admin" && user.Role != "User")
                return BadRequest("Role can only be 'Admin' or 'User'.");

            if (user.Status != "Active" && user.Status != "Passive")
                return BadRequest("Status can only be 'Active' or 'Passive'.");

            var existingUser = _userCollection.Find<User>(u => u.Username == user.Username).FirstOrDefault();


            if (existingUser != null)
            {
                return BadRequest("Bu isimde bir kullanıcı mevcut. Başka bir kullanıcı adı seçin.");
            }

            user.Password = ComputeSha256Hash(user.Password);
            user.Register = DateTime.Now;
            user.LastLogin = null;
            _userCollection.InsertOne(user);

            return Ok(new
            {
                Message = "Kullanıcı başarıyla oluşturuldu.",
            });
        }
        #endregion

        #region GetUser
        [HttpGet("getuserdetail"), CheckRoleAttribute]
        public async Task<IActionResult> GetUser()
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser.Role == "Admin")
            {
                var users = await _userCollection.Find(user => true).ToListAsync();
                return Ok(users);
            }
            else if (currentUser.Role == "User")
            {
                var user = await _userCollection.Find<User>(u => u.Id == currentUser.Id).FirstOrDefaultAsync();
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
        [HttpPost("updateuser/{id:length(24)}"), CheckRoleAttribute]
        public IActionResult UpdateUser(string id, [FromBody] User updatedUser)
        {
            var currentUser = GetCurrentUserFromSession();

            var userToBeUpdated = _userCollection.Find<User>(u => u.Id == id).FirstOrDefault();

            if (currentUser.Role == "User")
            {
                if (currentUser.Id != userToBeUpdated.Id)
                {
                    return Unauthorized("Sadece kendi şifrenizi güncelleyebilirsiniz.");
                }
                userToBeUpdated.Password = ComputeSha256Hash(updatedUser.Password);
            }
            else if (currentUser.Role == "Admin")
            {
                userToBeUpdated.Password = ComputeSha256Hash(updatedUser.Password);
            }

            _userCollection.ReplaceOne(u => u.Id == id, userToBeUpdated);

            return Ok(new { Message = "Kullanıcı başarıyla güncellendi." });
        }
        #endregion

        #region DeleteUser
        [HttpDelete("deleteuser/{id:length(24)}"), CheckRoleAttribute]
        public IActionResult DeleteUser(string id)
        {
            var currentUser = GetCurrentUserFromSession();

            var userToDelete = _userCollection.Find<User>(u => u.Id == id).FirstOrDefault();
            if (userToDelete == null)
            {
                return NotFound("Kullanıcı bulunamadı.");
            }

            if (currentUser.Role == "User" && currentUser.Id != userToDelete.Id)
            {
                return Unauthorized("Sadece kendi hesabınızı silebilirsiniz.");
            }

            _userCollection.DeleteOne(u => u.Id == id);

            return Ok(new { Message = "Kullanıcı başarıyla silindi." });
        }
        #endregion

        #region UserLogin
        [HttpPost("login")]
        public IActionResult Login([FromBody] User userModel)
        {
            if (userModel == null || string.IsNullOrEmpty(userModel.Username) || string.IsNullOrEmpty(userModel.Password))
            {
                return BadRequest("Kullanıcı adı veya şifre eksik.");
            }

            var userInDb = _userCollection.Find<User>(u => u.Username == userModel.Username && u.Password == ComputeSha256Hash(userModel.Password)).FirstOrDefault();

            if (userInDb == null)
            {
                return Unauthorized("Kullanıcı adı veya şifre hatalı.");
            }

            SetCurrentUserToSession(userInDb);

            return Ok("Giriş Başarılı");
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

        #region UserStatus
        [HttpPost("userstatus/{id:length(24)}")]
        public IActionResult UserStatus(string id)
        {
            var currentUser = GetCurrentUserFromSession();
            if (currentUser == null)
            {
                return Unauthorized("Giriş yapmanız gerekmektedir.");
            }

            if (currentUser.Role != "Admin")
            {
                return Unauthorized("Bu işlemi gerçekleştirmek için yetkiniz yok.");
            }

            var userToBeUpdated = _userCollection.Find<User>(u => u.Id == id).FirstOrDefault();
            if (userToBeUpdated == null)
            {
                return NotFound("Durumu değiştirilmek istenen kullanıcı bulunamadı.");
            }

            userToBeUpdated.Status = userToBeUpdated.Status == "Active" ? "Passive" : "Active";
            _userCollection.ReplaceOne(u => u.Id == id, userToBeUpdated);

            return Ok(new { Message = $"Kullanıcının durumu {userToBeUpdated.Status} olarak güncellendi." });
        }
        #endregion

        #region Accounting ADD
        [HttpPost("add-record"), CheckRoleAttribute]
        public IActionResult AddAccountingRecord([FromBody] Accounting record)
        {
            var currentUser = GetCurrentUserFromSession();
            if (currentUser == null)
                return Unauthorized();

            if (string.IsNullOrEmpty(record.Type) || (record.Type != "Gelir" && record.Type != "Gider"))
                return BadRequest(new { Message = "Type sadece 'Gelir' ya da 'Gider' olabilir." });

            record.UserId = currentUser.Id;
            record.Date = DateTime.Now;

            // Eğer boş string gelirse null yap
            if (string.IsNullOrEmpty(record.IncomeId))
                record.IncomeId = null;
            if (string.IsNullOrEmpty(record.MerchantId))
                record.MerchantId = null;
            if (string.IsNullOrEmpty(record.FixedExpensesId))
                record.FixedExpensesId = null;

            _accountingCollection.InsertOne(record);
            return Ok(new { Message = "Finansal kayıt oluşturuldu." });
        }

        #endregion


        #region Accounting Get Record
        [HttpGet("get-records"), CheckRoleAttribute]
        public async Task<IActionResult> GetAccountingRecords()
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser == null)
                return Unauthorized("Kullanıcı oturumu bulunamadı.");

            if (currentUser.Role == "Admin")
            {
                var records = await _accountingCollection.Find(record => true).ToListAsync();
                return Ok(records);
            }
            else if (currentUser.Role == "User")
            {
                var userRecords = await _accountingCollection.Find<Accounting>(r => r.UserId == currentUser.Id).ToListAsync();
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

            if (currentUser == null)
                return Unauthorized("Kullanıcı oturumu bulunamadı.");

            var existingRecord = await _accountingCollection.Find<Accounting>(r => r.Id == updatedRecord.Id).FirstOrDefaultAsync();

            if (existingRecord == null)
                return NotFound("Güncellenmek istenen finansal kayıt bulunamadı.");

            if (currentUser.Role != "Admin" && existingRecord.UserId != currentUser.Id)
                return Unauthorized("Bu kaydı güncellemek için yetkiniz bulunmamaktadır.");

            existingRecord.Amount = updatedRecord.Amount;
            existingRecord.Currency = updatedRecord.Currency;
            existingRecord.Type = updatedRecord.Type;

            await _accountingCollection.ReplaceOneAsync(r => r.Id == updatedRecord.Id, existingRecord);

            return Ok(new { Message = "Finansal kayıt başarıyla güncellendi." });
        }

        #endregion

        #region Accounting Delete Record
        [HttpDelete("delete-record/{id}"), CheckRoleAttribute]
        public async Task<IActionResult> DeleteRecord(string id)
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser == null)
                return Unauthorized("Kullanıcı oturumu bulunamadı.");

            var existingRecord = await _accountingCollection.Find<Accounting>(r => r.Id == id).FirstOrDefaultAsync();

            if (existingRecord == null)
                return NotFound("Silmek istenen finansal kayıt bulunamadı.");

            if (currentUser.Role != "Admin" && existingRecord.UserId != currentUser.Id)
                return Unauthorized("Bu kaydı silmek için yetkiniz bulunmamaktadır.");

            await _accountingCollection.DeleteOneAsync(r => r.Id == id);

            return Ok(new { Message = "Finansal kayıt başarıyla silindi." });
        }
        #endregion

        //#region Accounting Reports
        //[HttpGet("accounting-reports"), CheckRoleAttribute]
        //public IActionResult GetAccountingSummary([FromQuery] string interval = "1month")
        //{
        //    DateTime now = DateTime.Now;
        //    DateTime startDate = now;
        //    DateTime endDate = now;

        //    switch (interval)
        //    {
        //        case "15days":
        //            startDate = now.Date.AddDays(-15);
        //            endDate = now.Date.AddDays(1);
        //            break;
        //        case "1month":
        //            startDate = now.Date.AddMonths(-1);
        //            endDate = now.Date.AddDays(1);
        //            break;
        //        default:
        //            return BadRequest("Geçersiz Tarih Aralığı(15days)-(1month) doğru kullanım olacak.");
        //    }

        //    var currentUser = GetCurrentUserFromSession();
        //    if (currentUser == null)
        //        return Unauthorized();

        //    var dateFilter = _Accounting.AsQueryable().Where(x => x.Date > startDate && x.Date < endDate);


        //    if (currentUser.Role.ToString() != "Admin")
        //    {
        //        dateFilter = dateFilter.Where(x => x.UserId == currentUser.Id);
        //    }

        //    var gelir = dateFilter.Where(x => x.Type == AccountingType.Gelir).Sum(x => x.Amount);
        //    var gider = dateFilter.Where(x => x.Type == AccountingType.Gider).Sum(x => x.Amount);
        //    var bakiye = gelir - gider;

        //    return Ok(new { Gelir = gelir, Gider = gider, Kar = bakiye });
        //}

        //#endregion

        #region Create Fixed Expenses
        [HttpPost("add-fixed-expenses"), CheckRoleAttribute]
        public IActionResult AddFixedExpenses([FromBody] FixedExpenses expenses)
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser == null)
                return Unauthorized();

            expenses.UserId = currentUser.Id;

            _fixedExpensesCollection.InsertOne(expenses);

            return Ok(new { Message = "Sabit gider kaydı başarıyla oluşturuldu." });
        }
        #endregion

        #region Get Fixed Expenses
        [HttpGet("get-fixed-expenses"), CheckRoleAttribute]
        public async Task<IActionResult> GetFixedExpenses()
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser.Role == "Admin")
            {
                var expenses = await _fixedExpensesCollection.Find(expense => true).ToListAsync();
                return Ok(expenses);
            }
            else if (currentUser.Role == "User")
            {
                var userExpenses = await _fixedExpensesCollection.Find<FixedExpenses>(expense => expense.UserId == currentUser.Id).ToListAsync();
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

            if (currentUser == null)
                return Unauthorized();

            var existingExpense = await _fixedExpensesCollection.Find<FixedExpenses>(expense => expense.Id == expenses.Id).FirstOrDefaultAsync();
            if (existingExpense == null)
                return NotFound("Gider bulunamadı.");

            if (currentUser.Role != "Admin" && existingExpense.UserId != currentUser.Id)
                return Unauthorized("Bu gideri güncelleme yetkiniz yok.");

            _fixedExpensesCollection.ReplaceOne(expense => expense.Id == expenses.Id, expenses);

            return Ok(new { Message = "Sabit gider kaydı başarıyla güncellendi." });
        }
        #endregion

        #region Delete Fixed Expenses
        [HttpDelete("delete-fixed-expenses/{id}"), CheckRoleAttribute]
        public async Task<IActionResult> DeleteFixedExpenses(string id)
        {
            var currentUser = GetCurrentUserFromSession();

            if (currentUser == null)
                return Unauthorized();

            var existingExpense = await _fixedExpensesCollection.Find<FixedExpenses>(expense => expense.Id == id).FirstOrDefaultAsync();
            if (existingExpense == null)
                return NotFound("Gider bulunamadı.");

            if (currentUser.Role != "Admin" && existingExpense.UserId != currentUser.Id)
                return Unauthorized("Bu gideri silme yetkiniz yok.");

            _fixedExpensesCollection.DeleteOne(expense => expense.Id == id);

            return Ok(new { Message = "Sabit gider kaydı başarıyla silindi." });
        }
        #endregion
    }

}

