using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Muhasebe.Attributes;
using Muhasebe.Services;
using MuhasebeFull.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Muhasebe.Models;
using MuhasebeFull.Users;

[Route("api/merchants")]
public class MerchantsController : ControllerBase
{
    private readonly IConnectionService _connectionService;
    

    #region AddMerchant

    public class _addMerchantReq
    {
        [Required]
        public string title { get; set; }
    }

    public class _addMerchantRess
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("addMerchant"), CheckRoleAttribute]
    public ActionResult<_addMerchantRess> AddMerchant([FromBody] _addMerchantReq merchantReq)
    {
        var _merchantCollection = _connectionService.db().GetCollection<Merchant>("MerchantCollection");
        var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);
        if (currentUser == null)
            return Unauthorized(new _addMerchantRess { type = "error", message = "Yetkisiz erişim." });

        Merchant merchant = new Merchant
        {
            title = merchantReq.title,
            userId = currentUser.id,
            date = DateTime.Now,
        };

        _merchantCollection.InsertOne(merchant);

        return Ok(new _addMerchantRess { message = "Cari kaydedildi." });
    }

    #endregion

    #region Update Merchant
    public class _updateMerchantReq
    {
        [Required]
        public string id { get; set; }

        [Required]
        public string title { get; set; }
    }

    public class _updateMerchantRess
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("updateMerchant"), CheckRoleAttribute]
    public async Task<ActionResult<_updateMerchantRess>> UpdateMerchant([FromBody] _updateMerchantReq updateReq)
    {
        var _merchantCollection = _connectionService.db().GetCollection<Merchant>("MerchantCollection");
        var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);

        if (currentUser.role != "Admin")
        {
            return Unauthorized(new _updateMerchantRess { type = "error", message = "Bu işlemi gerçekleştirmek için yetkiniz yok." });
        }

        var existingMerchant = await _merchantCollection.Find<Merchant>(m => m.id == updateReq.id).FirstOrDefaultAsync();

        if (existingMerchant == null)
        {
            return NotFound(new _updateMerchantRess { type = "error", message = "Güncellenmek istenen cari bulunamadı." });
        }

        var update = Builders<Merchant>.Update.Set(m => m.title, updateReq.title);

        await _merchantCollection.UpdateOneAsync(m => m.id == existingMerchant.id, update, new UpdateOptions { IsUpsert = false });

        return Ok(new _updateMerchantRess { message = "Cari bilgisi güncellendi." });
    }

    #endregion

    #region Get Merchant

    public class _getMerchantReq
    {
        [Required]
        public string userId { get; set; }
    }

    public class _getMerchantRes
    {
        public string type { get; set; } // success / error
        public string message { get; set; }
        public List<Merchant> merchants { get; set; } = new List<Merchant>();
    }

    [HttpPost("getMerchant"), CheckRoleAttribute]
    public async Task<ActionResult<_getMerchantRes>> GetMerchant([FromBody] _getMerchantReq request)
    {
        var _merchantCollection = _connectionService.db().GetCollection<Merchant>("MerchantCollection");
        var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);
        _getMerchantRes response = new _getMerchantRes();

        FilterDefinition<Merchant> filter;

        if (currentUser.role == "Admin")
        {
            filter = Builders<Merchant>.Filter.Eq(m => m.userId, request.userId);
        }
        else
        {
            if (request.userId == currentUser.id)
            {
                filter = Builders<Merchant>.Filter.Eq(m => m.userId, currentUser.id);
            }
            else
            {
                response.type = "error";
                response.message = "Bu işlemi gerçekleştirmek için yetkiniz yok.";
                return Unauthorized(response);
            }
        }

        response.merchants = await _merchantCollection.Find(filter).ToListAsync();

        if (response.merchants.Count == 0)
        {
            response.type = "error";
            response.message = "Belirtilen userId ile ilişkilendirilmiş bir cari bulunamadı.";
            return NotFound(response);
        }

        response.type = "success";
        response.message = "İşlem başarılı.";
        return Ok(response);
    }

    #endregion

    #region DeleteMerchant

    public class _deleteMerchantReq
    {
        [Required]
        public string id { get; set; }
    }

    public class _deleteMerchantRess
    {
        [Required]
        public string type { get; set; } = "success";
        public string message { get; set; }
    }

    [HttpPost("deleteMerchant"), CheckRoleAttribute]
    public async Task<ActionResult<_deleteMerchantRess>> DeleteMerchant([FromBody] _deleteMerchantReq deleteReq)
    {
        var _merchantCollection = _connectionService.db().GetCollection<Merchant>("MerchantCollection");
        var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);

        if (currentUser.role != "Admin")
        {
            return Unauthorized(new _deleteMerchantRess { type = "error", message = "Bu işlemi gerçekleştirmek için yetkiniz yok." });
        }

        var deleteResult = await _merchantCollection.DeleteOneAsync(m => m.id == deleteReq.id);

        if (deleteResult.DeletedCount > 0)
        {
            return Ok(new _deleteMerchantRess { message = "Cari kaydı silindi." });
        }

        return NotFound(new _deleteMerchantRess { type = "error", message = "Silmek istenen cari bulunamadı." });
    }

    #endregion

}