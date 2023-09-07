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
using MongoDB.Bson.Serialization.Attributes;

namespace Muhasebe.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        private IMongoCollection<User> _userCollection;
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
        public async Task<IActionResult> GetUser(int page = 1)
        {
            const int pageSize = 10;
            var skip = (page - 1) * pageSize;

            var currentUser = GetCurrentUserFromSession();

            if (currentUser.role == "Admin")
            {
                var users = await _userCollection.Find(user => true).Skip(skip).Limit(pageSize).ToListAsync();
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
                if (currentUser.id != data.id)
                {
                    return Unauthorized("Sadece kendi şifrenizi güncelleyebilirsiniz.");
                }
                userToBeUpdated.password = ComputeSha256Hash(data.password);
            }
            else if (currentUser.role == "Admin")
            {
                userToBeUpdated.password = ComputeSha256Hash(data.password);
            }

            _userCollection.ReplaceOne(u => u.id == currentUser.id, userToBeUpdated);

            var newValue = JsonSerializer.Serialize(userToBeUpdated);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Update",
                target = "User",
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

        public class _deleteUserReq
        {
            [Required]
            public string id { get; set; }
        }

        public class _deleteUserRes
        {
            [Required]
            public string type { get; set; } // success / error 
            public string message { get; set; }
        }

        [HttpPost("deleteuser"), CheckRoleAttribute]
        public ActionResult<_deleteUserRes> DeleteUser([FromBody] _deleteUserReq data)
        {
            var currentUser = GetCurrentUserFromSession();

            var userToDelete = _userCollection.Find<User>(u => u.id == data.id).FirstOrDefault();
            if (userToDelete == null)
            {
                return NotFound(new _deleteUserRes { type = "error", message = "Kullanıcı bulunamadı." });
            }

            if (currentUser.role == "User" && currentUser.id != userToDelete.id)
            {
                return Unauthorized(new _deleteUserRes { type = "error", message = "Sadece kendi hesabınızı silebilirsiniz." });
            }

            var oldValue = JsonSerializer.Serialize(userToDelete);

            _userCollection.DeleteOne(u => u.id == data.id);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Delete",
                target = "User",
                itemId = userToDelete.id,
                oldValue = oldValue,
                newValue = null,
                date = DateTime.UtcNow
            };
            _logCollection.InsertOne(log);

            return Ok(new _deleteUserRes { type = "success", message = "Kullanıcı başarıyla silindi." });
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

            userInDb.lastLogin = DateTime.UtcNow;

            _userCollection.ReplaceOne(u => u.id == userInDb.id, userInDb);

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

        #region Userstatus Update

        public class _userstatusReq
        {
            [Required]
            public string id { get; set; }
            public string status { get; set; }
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

            var userToBeUpdated = _userCollection.Find<User>(u => u.id == statusData.id).FirstOrDefault();
            if (userToBeUpdated == null)
            {
                return NotFound(new _userstatusRes { type = "error", message = "Durumu değiştirilmek istenen kullanıcı bulunamadı." });
            }

            if (statusData.status != userToBeUpdated.status)
            {
                return BadRequest(new _userstatusRes { type = "error", message = "Girilen statü, kullanıcının mevcut statüsü ile eşleşmiyor." });
            }

            var oldValue = userToBeUpdated.status;

            userToBeUpdated.status = userToBeUpdated.status == "Active" ? "Passive" : "Active";
            _userCollection.ReplaceOne(u => u.id == statusData.id, userToBeUpdated);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Update",
                target = "User",
                itemId = userToBeUpdated.id,
                oldValue = oldValue,
                newValue = userToBeUpdated.status,
                date = DateTime.UtcNow
            };
            _logCollection.InsertOne(log);

            return Ok(new _userstatusRes { type = "success", message = $"Kullanıcının durumu {userToBeUpdated.status} olarak güncellendi." });
        }

        #endregion

    }


}