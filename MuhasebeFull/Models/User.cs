using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace MuhasebeFull.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string id { get; set; }

        public string username { get; set; }

        public string password { get; set; }

        public string status { get; set; }

        public DateTime register { get; set; }

        public DateTime? lastLogin { get; set; }

        public List<string> Restrictions { get; set; } = new List<string>();

        public string role { get; set; }
    }
}
