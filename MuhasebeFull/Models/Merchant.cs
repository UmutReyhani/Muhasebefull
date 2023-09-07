using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Muhasebe.Models
{
    public class Merchant
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string id { get; set; }

        public string title { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime date { get; set; } = DateTime.UtcNow;

        public string userId { get; set; }
    }
}