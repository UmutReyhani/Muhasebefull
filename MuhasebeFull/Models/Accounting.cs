using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Muhasebe.Models
{
    public class Accounting
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string id { get; set; }

        public string userId    { get; set; }

        public string type { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal amount { get; set; }
        public string currency { get; set; }
        public DateTime date { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? incomeId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? merchantId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? fixedExpensesId { get; set; }
    }
}
