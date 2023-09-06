using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace MuhasebeFull.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        public string Status { get; set; }

        public DateTime Register { get; set; }

        public DateTime? LastLogin { get; set; }

        public string Role { get; set; }
    }
}
