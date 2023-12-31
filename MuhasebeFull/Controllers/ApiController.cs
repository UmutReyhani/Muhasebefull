﻿using Muhasebe.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Muhasebe.Models;
using System.Text.Json;
using Muhasebe.Attributes;
using MongoDB.Driver.Linq;
using MuhasebeFull.Models;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MuhasebeFull.Users;

namespace Muhasebe.Controllers
{
    [Route("api/user")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        public UserController(IConnectionService connectionService)
        {
            _connectionService = connectionService;
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

        [HttpPost("createUser")]
        public ActionResult<_createUserRes> CreateUser([FromBody] _createUserReq data)
        {
            var _userCollection = _connectionService.db().GetCollection<User>("UserCollection");

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

        #region GetUser Details

        public class _getUserDetailsReq
        {
            public int page { get; set; } = 1;
            public int offset { get; set; } = 10; // pageSize olarak da adlandırılabilir
        }

        public class _getUserDetailsRes
        {
            public long? total { get; set; }
            public List<User>? data { get; set; }
            [Required]
            public string type { get; set; }
            public string? message { get; set; }
        }

        [HttpPost("getuserDetail"), CheckRoleAttribute]
        public async Task<ActionResult<_getUserDetailsRes>> GetUserDetails([FromBody] _getUserDetailsReq req)
        {
            var _userCollection = _connectionService.db().GetCollection<User>("UserCollection").AsQueryable();
            var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);
            _getUserDetailsRes response = new _getUserDetailsRes();

            if (currentUser == null)
            {
                response.type = "error";
                response.message = "Oturum bilgisi bulunamadı.";
                return Ok(response);
            }

            if (currentUser.role == "Admin")
            {
                var total = _userCollection.Count();
                var users = _userCollection.Skip((req.page - 1) * req.offset)
                                           .Take(req.offset)
                                           .ToList();

                return Ok(new _getUserDetailsRes
                {
                    total = total,
                    data = users,
                    type = "success",
                    message = "Kullanıcı detayları başarıyla alındı."
                });
            }
            else if (currentUser.role == "User")
            {
                var user = _userCollection.FirstOrDefault(u => u.id == currentUser.id);
                if (user == null)
                {
                    response.type = "error";
                    response.message = "Kullanıcı bulunamadı.";
                    return NotFound(response);
                }

                return Ok(new _getUserDetailsRes
                {
                    data = new List<User> { user },
                    type = "success",
                    message = "Kullanıcı detayı başarıyla alındı."
                });
            }
            else
            {
                response.type = "error";
                response.message = "Bu işlemi gerçekleştirmek için yetkiniz yok.";
                return Unauthorized(response);
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

        [HttpPost("updateUser"), CheckRoleAttribute]
        public async Task<ActionResult<_updateUserRes>> UpdateUser([FromBody] _updateUserReq req)
        {
            var _userCollection = _connectionService.db().GetCollection<User>("UserCollection");
            var _logCollection = _connectionService.db().GetCollection<Log>("LogCollection");
            var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);

            var existingUser = await _userCollection.Find<User>(user => user.id == req.id).FirstOrDefaultAsync();
            if (existingUser == null)
                return NotFound(new _updateUserRes { type = "error", message = "Güncellenmek istenen kullanıcı bulunamadı." });

            if (currentUser.role == "User" && existingUser.id != currentUser.id)
                return Unauthorized(new _updateUserRes { type = "error", message = "Sadece kendi bilgilerinizi güncelleyebilirsiniz." });

            var oldValue = JsonSerializer.Serialize(existingUser);

            User updatedUser = existingUser;
            updatedUser.password = ComputeSha256Hash(req.password);

            var update = Builders<User>.Update.Set(x => x.password, updatedUser.password);
            await _userCollection.UpdateOneAsync(x => x.id == existingUser.id, update, new UpdateOptions { IsUpsert = false });

            var newValue = JsonSerializer.Serialize(updatedUser);

            Log log = new Log
            {
                userId = currentUser.id,
                actionType = "Update",
                target = "User",
                itemId = updatedUser.id,
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

        [HttpPost("deleteUser"), CheckRoleAttribute]
        public ActionResult<_deleteUserRes> DeleteUser([FromBody] _deleteUserReq data)
        {
            var _logCollection = _connectionService.db().GetCollection<Log>("LogCollection");
            var _userCollection = _connectionService.db().GetCollection<User>("UserCollection");
            var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);

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
            [Required]
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
            var _userCollection = _connectionService.db().GetCollection<User>("UserCollection");
            var userInDb = _userCollection.Find<User>(u => u.username == loginData.username && u.password == ComputeSha256Hash(loginData.password)).FirstOrDefault();

            if (userInDb == null)
            {
                return Unauthorized(new _loginRes { type = "error", message = "Kullanıcı adı veya şifre hatalı." });
            }

            userInDb.lastLogin = DateTime.UtcNow;

            _userCollection.ReplaceOne(u => u.id == userInDb.id, userInDb);

            userFunctions.SetCurrentUserToSession(HttpContext, userInDb);

            return Ok(new _loginRes { type = "success", message = "Giriş Başarılı" });
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
            [Required]
            public string type { get; set; } // success / error 
            public string message { get; set; }
        }

        [HttpPost("userStatus")]
        public ActionResult<_userstatusRes> Userstatus([FromBody] _userstatusReq statusData)
        {
            var _userCollection = _connectionService.db().GetCollection<User>("UserCollection");
            var _logCollection = _connectionService.db().GetCollection<Log>("LogCollection");
            var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);

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

        #region addRestriction TESST
        [HttpPost("addRestriction")]
        public ActionResult AddRestriction(string userId, string restriction)
        {
            var _userCollection = _connectionService.db().GetCollection<User>("UserCollection");

            var user = _userCollection.Find(u => u.id == userId).FirstOrDefault();
            if (user == null)
            {
                return NotFound("Kullanıcı bulunamadı.");
            }

            if (user.Restrictions == null)
            {
                user.Restrictions = new List<string>();
            }

            if (!user.Restrictions.Contains(restriction))
            {
                user.Restrictions.Add(restriction);
                var update = Builders<User>.Update.Set(u => u.Restrictions, user.Restrictions);
                _userCollection.UpdateOne(u => u.id == userId, update);
            }

            return Ok("Kısıtlama başarıyla eklendi.");
        }
    
        #endregion
    }
}