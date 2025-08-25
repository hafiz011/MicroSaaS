using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Microservice.Session.Entities
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

        // Derived / AI features
        public int Total_Sessions { get; set; }       // Total sessions count
        public int Total_Actions { get; set; }        // Aggregate actions across sessions
        public int Total_Products_Viewed { get; set; }
        public int Total_Products_Added { get; set; }
        public int Total_Products_Purchased { get; set; }
    }
}
