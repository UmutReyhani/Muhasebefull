using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace MuhasebeFull.Models
{
    public class FixedExpenses
    {
        internal string currency;

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string id { get; set; }

        public string title { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal amount { get; set; }

        public string? description { get; set; }

        public string userId { get; set; }
    }
}
