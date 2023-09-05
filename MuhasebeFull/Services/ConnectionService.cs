using Muhasebe.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Muhasebe.Services
{
    public class ConnectionService : IConnectionService
    {
        private readonly IMongoDatabase _db;
        public ConnectionService(IOptions<DatabaseSettings> dbSettings)
        {
            var mongoClient = new MongoClient(dbSettings.Value.ConnectionString);
            _db = mongoClient.GetDatabase(dbSettings.Value.DatabaseName);
        }
        public IMongoDatabase db() => _db;
    }
}
