using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Muhasebe.Models
{
    public class Log
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string userId { get; set; }

        public string actionType { get; set; }

        public string targetModel { get; set; }

        public string? itemId { get; set; }

        public string? oldValue { get; set; }

        public string? newValue { get; set; }

        public DateTime date { get; set; } = DateTime.UtcNow;
    }
}
