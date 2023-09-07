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
using static Muhasebe.Controllers.UserController;

[Route("api/merchants")]
public class MerchantsController : ControllerBase
{
    private readonly IConnectionService _connectionService;
    private IMongoCollection<Merchant> _merchantCollection;

    public MerchantsController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
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

    #region AddMerchant

    public class AddMerchantReq
    {
        public string title { get; set; }
    }

    public class AddMerchantRes
    {
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("add-merchant"), CheckRoleAttribute]
    public IActionResult AddMerchant([FromBody] AddMerchantReq merchantReq)
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser == null)
            return Unauthorized(new AddMerchantRes { type = "error", message = "Yetkisiz erişim." });

        Merchant merchant = new Merchant
        {
            title = merchantReq.title,
            userId = currentUser.id,
            date = DateTime.Now,
        };

        _merchantCollection.InsertOne(merchant);

        return Ok(new AddMerchantRes { message = "Cari kaydedildi." });
    }

    #endregion

    #region UpdateMerchant

    public class UpdateMerchantReq
    {
        public string title { get; set; }
    }

    public class UpdateMerchantRes
    {
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("update-merchant"), CheckRoleAttribute]
    public async Task<IActionResult> UpdateMerchant([FromBody] UpdateMerchantReq updateReq)
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser.role != "Admin")
        {
            return Unauthorized(new UpdateMerchantRes { type = "error", message = "Bu işlemi gerçekleştirmek için yetkiniz yok." });
        }

        var merchant = await _merchantCollection.Find<Merchant>(m => m.id == currentUser.id).FirstOrDefaultAsync();

        if (merchant == null)
        {
            return NotFound(new UpdateMerchantRes { type = "error", message = "Güncellenmek istenen cari bulunamadı." });
        }

        merchant.title = updateReq.title;

        await _merchantCollection.ReplaceOneAsync(m => m.id == merchant.id, merchant);

        return Ok(new UpdateMerchantRes { message = "Cari bilgisi güncellendi." });
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

    #region DeleteMerchant

    public class DeleteMerchantReq
    {
        public string merchantId { get; set; }
    }

    public class DeleteMerchantRes
    {
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("delete-merchant"), CheckRoleAttribute]
    public async Task<IActionResult> DeleteMerchant([FromBody] DeleteMerchantReq deleteReq)
    {
        var currentUser = GetCurrentUserFromSession();

        if (currentUser.role != "Admin")
        {
            return Unauthorized(new DeleteMerchantRes { type = "error", message = "Bu işlemi gerçekleştirmek için yetkiniz yok." });
        }

        var deleteResult = await _merchantCollection.DeleteOneAsync(m => m.id == deleteReq.merchantId);

        if (deleteResult.DeletedCount > 0)
        {
            return Ok(new DeleteMerchantRes { message = "Cari kaydı silindi." });
        }

        return NotFound(new DeleteMerchantRes { type = "error", message = "Silmek istenen cari bulunamadı." });
    }

    #endregion

}