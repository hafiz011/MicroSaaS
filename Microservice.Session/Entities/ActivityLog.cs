using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace Microservice.Session.Entities
{
    public class ActivityLog
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public string Tenant_Id { get; set; }
        public string Session_Id { get; set; }
        public string User_Id { get; set; }

        public string Action_Type { get; set; }        // view_product, add_to_cart, purchase, login_attempt
        public string Product_Id { get; set; }
        public string Category_Id { get; set; }
        public int Quantity { get; set; }
        public double Price { get; set; }            // Product price

        public string Url { get; set; }
        public string ReferrerUrl { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
        public string Request_Method { get; set; }     // GET, POST, PUT, DELETE
        public int Response_Code { get; set; }
        public DateTime Time_Stamp { get; set; }

        public double Session_Elapsed_Time { get; set; } // Seconds since session start
        public int Event_Sequence { get; set; }          // Sequence per session
        public bool Success_Flag { get; set; }           // Action success/failure
        public double Response_Time { get; set; }        // ms
    }
}
