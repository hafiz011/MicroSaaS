using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Microservice.AuthService.Models
{
    public class Users
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public string Tenant_Id { get; set; }
        public string User_Id { get; set; } 
        public string Name { get; set; }
        public string Email { get; set; }
        public DateTime Last_login { get; set; }
        public DateTime Created_at { get; set; }
    }
}
