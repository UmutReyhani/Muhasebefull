using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Muhasebe.Models
{
    public class CariRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        public string Ad { get; set; }
        public string Soyad { get; set; }
        public decimal Bakiye { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    }
}