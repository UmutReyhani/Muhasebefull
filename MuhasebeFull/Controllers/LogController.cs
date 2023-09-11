using Muhasebe.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Muhasebe.Models;
using System.Text.Json;
using Muhasebe.Attributes;
using MongoDB.Driver.Linq;
using MuhasebeFull.Models;
using System.ComponentModel.DataAnnotations;
using MuhasebeFull.Users;

namespace MuhasebeFull.Controllers
{
    [Route("log")]
    [ApiController]
    public class LogController : ControllerBase
    {
        private readonly IConnectionService _connectionService;

        

        #region Log Record all
        public class _getLogsReq
        {

            public int page { get; set; } = 1;
            public int offset { get; set; } = 20;
        }
        public class _getLogsRes
        {
            public int? total { get; set; }
            public List<Log>? data { get; set; }
            [Required]
            public string type { get; set; }
            public string? message { get; set; }
        }

        [HttpPost("[action]"), CheckRoleAttribute]
        public ActionResult<_getLogsRes> getLogs([FromBody] _getLogsReq req)
        {
            var _logCollection = _connectionService.db().GetCollection<Log>("LogCollection");
            var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);
            if (currentUser == null)
                return Ok(new _getLogsRes { type = "error", message = "Oturum bilgisi bulunamadı." });

            if (currentUser.role.ToString() != "Admin")
            {
                return Ok(new _getLogsRes { type = "error", message = "yetkisiz işlem." });
            }

            var query = _logCollection.AsQueryable()/*.Where(x=> x.date > asdasdasd && asdasd)*/;
            var total = query.Count();
            var logs = query
                            .OrderByDescending(log => log.date)
                            .Skip((req.page - 1) * req.offset)
                            .Take(req.page)
                            .ToList();

            return Ok(new _getLogsRes
            {
                total = total,
                data = logs,
                type = "success",
                message = "Log kayıtları başarıyla alındı."
            });
        }

        #endregion

        #region Log FILTER

        public class _logsRes
        {
            [Required]
            public int totalCount { get; set; }
            [Required]
            public List<Log> logs { get; set; }
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        public class _logRequest
        {
            [Required]
            public string? target { get; set; }
            public string? itemId { get; set; }
        }

        [HttpPost("logrecordfilter"), CheckRoleAttribute]
        public ActionResult<_logsRes> GetLogs([FromBody] _logRequest request, int pageSize = 20, int pageNumber = 1)
        {
            var _logCollection = _connectionService.db().GetCollection<Log>("LogCollection");
            var currentUser = userFunctions.GetCurrentUserFromSession(HttpContext);
            if (currentUser == null)
                return Unauthorized("Oturum bilgisi bulunamadı.");

            if (currentUser.role.ToString() != "Admin")
            {
                return Unauthorized("Sadece yöneticiler bu bilgilere erişebilir.");
            }

            var filter = Builders<Log>.Filter.Empty;

            if (!string.IsNullOrEmpty(request.itemId))
            {
                filter = filter & Builders<Log>.Filter.Eq(l => l.itemId, request.itemId);
            }

            if (!string.IsNullOrEmpty(request.target))
            {
                filter = filter & Builders<Log>.Filter.Eq(l => l.target, request.target);
            }

            var totalCount = (int)_logCollection.CountDocuments(filter);

            var logs = _logCollection.AsQueryable()
                .Where(log => log.itemId == request.itemId && (string.IsNullOrEmpty(request.target) || log.target == request.target))
                .OrderByDescending(log => log.date)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new _logsRes
            {
                totalCount = totalCount,
                logs = logs,
                type = "success",
                message = "Log kayıtları başarıyla alındı."
            });
        }

#endregion

    }
}
