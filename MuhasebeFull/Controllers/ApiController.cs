using Muhasebe.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Muhasebe.Models;
using System.Text.Json;
using Muhasebe.Attributes;
using MongoDB.Driver.Linq;

namespace Muhasebe.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        private IMongoCollection<User> _userCollection;
        private IMongoCollection<FinancialRecord> _financialCollection;
        private IMongoCollection<CariRecord> _cariCollection;

        public UserController(IConnectionService connectionService)
        {
            _connectionService = connectionService;
            _userCollection = _connectionService.db().GetCollection<User>("UserCollection");
            _financialCollection = _connectionService.db().GetCollection<FinancialRecord>("FinancialCollection");
            _cariCollection = _connectionService.db().GetCollection<CariRecord>("CariCollection");
        }

        #region GetUser
        [HttpGet, CheckRoleAttribute]
        public async Task<IActionResult> GetUser()
        {
            var users = await _userCollection.AsQueryable().ToListAsync();
            return Ok(users);
        }
        #endregion

        #region CreateUser
        [HttpPost("createuser"), CheckRoleAttribute]
        public IActionResult CreateUser([FromBody] User user)
        {
            if (user.Role != "Admin" && user.Role != "User")
                return BadRequest("Role can only be 'Admin' or 'User'.");

            var existingUser = _userCollection.Find<User>(u => u.Username == user.Username).FirstOrDefault();

            if (existingUser != null)
            {
                return BadRequest("Bu isimde bir kullanıcı mevcut başka bir kullanıcı adı seçin.");
            }

            user.Password = ComputeSha256Hash(user.Password);
            _userCollection.InsertOne(user);

            return Ok(new { Message = "Kullanıcı başarıyla oluşturuldu.", Id = user.Id, Username = user.Username, Role = user.Role });
        }

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
        #endregion

        #region USER SESSION
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

        #region User Login
        [HttpPost("login")]
        public IActionResult Login([FromBody] UserLogin loginModel)
        {
            var user = _userCollection.Find<User>(u => u.Username == loginModel.Username && u.Password == ComputeSha256Hash(loginModel.Password)).FirstOrDefault();

            if (user == null)
            {
                return Unauthorized();
            }

            SetCurrentUserToSession(user);

            return Ok("Giriş Başarılı");
        }
        #endregion

        #region DeleteUser
        [HttpDelete("{id:length(24)}")]
        public IActionResult DeleteUser(string id)
        {
            var user = _userCollection.Find<User>(u => u.Id == id).FirstOrDefault();

            if (user == null)
            {
                return NotFound();
            }

            _userCollection.DeleteOne(u => u.Id == id);

            return NoContent();
        }
        #endregion

        #region FINANCE ADD RECORD
        [HttpPost("add-record"), CheckRoleAttribute]
        public IActionResult AddFinancialRecord([FromBody] FinancialRecord record)
        {
            var currentUser = GetCurrentUserFromSession();
            if (currentUser == null)
                return Unauthorized();

            record.UserId = currentUser.Id;

            record.Date = DateTime.Now;

            _financialCollection.InsertOne(record);
            return Ok(new { Message = "Finansal kayıt oluşturuldu." });
        }

        #endregion

        #region Finance Get Record
        [HttpGet("get-records"), CheckRoleAttribute]
        public async Task<IActionResult> GetFinancialRecords()
        {
            var currentUser = GetCurrentUserFromSession();
            if (currentUser == null)
                return Unauthorized();

            FilterDefinition<FinancialRecord> filter;

            if (currentUser.Role == "Admin")
            {
                filter = Builders<FinancialRecord>.Filter.Empty;
            }
            else
            {
                filter = Builders<FinancialRecord>.Filter.Eq(r => r.UserId, currentUser.Id);
            }

            var records = await _financialCollection.Find(filter).ToListAsync();

            return Ok(records);
        }
        #endregion

        #region Finance Update Record
        [HttpPut("edit-record/{id:length(24)}"), CheckRoleAttribute]
        public IActionResult EditFinancialRecord(string id, [FromBody] FinancialRecord record)
        {
            var currentUser = GetCurrentUserFromSession();
            if (currentUser == null)
                return Unauthorized();

            var existingRecord = _financialCollection.Find<FinancialRecord>(r => r.Id == id).FirstOrDefault();
            if (existingRecord == null)
            {
                return NotFound("Kayıt Bulunamadı");
            }

            if (currentUser.Role != "Admin" && existingRecord.UserId != currentUser.Id)
            {
                return Unauthorized("Sadece kendi kayıtlarınızı güncelleyebilirsiniz.");
            }

            existingRecord.Amount = record.Amount;
            existingRecord.Type = record.Type;

            _financialCollection.ReplaceOne(r => r.Id == id, existingRecord);
            return Ok(new { Message = "Finansal kayıt güncellemesi tamamlandı." });
        }

        #endregion

        #region Finance Delete Record
        [HttpDelete("delete-record/{id:length(24)}"), CheckRoleAttribute]
        public IActionResult DeleteFinancialRecord(string id)
        {
            var currentUser = GetCurrentUserFromSession();
            if (currentUser == null)
                return Unauthorized();

            var recordToDelete = _financialCollection.Find<FinancialRecord>(r => r.Id == id).FirstOrDefault();
            if (recordToDelete == null)
            {
                return NotFound("Kayıt bulunamadı.");
            }

            if (currentUser.Role != "Admin" && recordToDelete.UserId != currentUser.Id)
            {
                return Unauthorized("Sadece kendi kayıtlarınızı silebilirsiniz.");
            }

            _financialCollection.DeleteOne(r => r.Id == id);
            return Ok(new { Message = "Finansal kayıt silindi." });
        }


        #endregion

        #region Financial Reports
        [HttpGet("financialreports"), CheckRoleAttribute]
        public IActionResult GetFinancialSummary([FromQuery] string interval = "1month")
        {
            DateTime now = DateTime.Now;
            DateTime startDate = now;
            DateTime endDate = now;

            switch (interval)
            {
                case "15days":
                    startDate = now.Date.AddDays(-15);
                    endDate = now.Date.AddDays(1);
                    break;
                case "1month":
                    startDate = now.Date.AddMonths(-1);
                    endDate = now.Date.AddDays(1);
                    break;
                default:
                    return BadRequest("Geçersiz Tarih Aralığı(15days)-(1month) doğru kullanım olacak.");
            }

            var currentUser = GetCurrentUserFromSession();
            if (currentUser == null)
                return Unauthorized();

            var dateFilter = _financialCollection.AsQueryable().Where(x => x.Date > startDate && x.Date < endDate);


            if (currentUser.Role != "Admin")
            {
                dateFilter = dateFilter.Where(x => x.UserId == currentUser.Id);
            }

            var gelir = dateFilter.Where(x => x.Type == "Gelir" ).Sum(x => x.Amount);
            var gider = dateFilter.Where(x => x.Type == "Gider" ).Sum(x => x.Amount);
            var bakiye = gelir - gider;

            return Ok(new { Gelir = gelir, Gider = gider, Kar = bakiye });
        }

        #endregion

        #region CreateCari
        [HttpPost("create-cari"), CheckRoleAttribute]
        public IActionResult CreateCariRecord([FromBody] CariRecord cariRecord)
        {
            _cariCollection.InsertOne(cariRecord);
            return Ok(new { Message = "Cari kayıt eklendi" });
        }
        #endregion

        #region GetCari
        [HttpGet("get-cari/{id:length(24)}"), CheckRoleAttribute]
        public IActionResult GetCari(string id)
        {
            var cari = _cariCollection.Find<CariRecord>(c => c.Id == id).FirstOrDefault();

            if (cari == null)
            {
                return NotFound();
            }

            return Ok(cari);
        }
        #endregion

        #region GetAllCaris
        [HttpGet("get-all-caris"), CheckRoleAttribute]
        public async Task<IActionResult> GetAllCaris()
        {
            var caris = await _cariCollection.AsQueryable().ToListAsync();
            return Ok(caris);
        }
        #endregion

        #region UpdateCari
        [HttpPut("update-cari/{id:length(24)}"), CheckRoleAttribute]
        public IActionResult UpdateCari(string id, [FromBody] CariRecord updatedCari)
        {
            var cari = _cariCollection.Find<CariRecord>(c => c.Id == id).FirstOrDefault();

            if (cari == null)
            {
                return NotFound("Cari not found.");
            }

            cari.Ad = updatedCari.Ad;
            cari.Soyad = updatedCari.Soyad;
            cari.Bakiye = updatedCari.Bakiye;

            _cariCollection.ReplaceOne(c => c.Id == id, cari);

            return Ok(new { Message = "Cari kayıt güncellendi." });
        }
        #endregion

        #region DeleteCari
        [HttpDelete("delete-cari/{id:length(24)}"), CheckRoleAttribute]
        public IActionResult DeleteCari(string id)
        {
            var cari = _cariCollection.Find<CariRecord>(c => c.Id == id).FirstOrDefault();

            if (cari == null)
            {
                return NotFound("Cari not found.");
            }

            _cariCollection.DeleteOne(c => c.Id == id);
            return Ok(new { Message = "Cari kayıt silindi." });
        }
        #endregion
    }

}

