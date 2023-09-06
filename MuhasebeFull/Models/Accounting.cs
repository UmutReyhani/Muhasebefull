using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Muhasebe.Models
{
    public class Accounting
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; }

        public string Type { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public DateTime Date { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? IncomeId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? MerchantId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? FixedExpensesId { get; set; }
    }
}
