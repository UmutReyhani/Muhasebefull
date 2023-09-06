using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace MuhasebeFull.Models
{
    public class FixedExpenses
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        public string Title { get; set; }

        public decimal Amount { get; set; }

        public string Description { get; set; }

        public string UserId { get; set; }
    }
}
