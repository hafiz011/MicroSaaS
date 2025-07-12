using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Microservice.AuthService.Entities
{
    public class OrderStatus
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("Order_Id")]
        public string OrderId { get; set; }

        [BsonElement("Session_Id")]
        public string SessionId { get; set; }

        [BsonElement("User_Id")]
        public string UserId { get; set; }

        [BsonElement("Tenant_Id")]
        public string TenantId { get; set; }

        [BsonElement("Order_Placed_At")]
        public DateTime OrderPlacedAt { get; set; }

        [BsonElement("Estimated_Delivery_Date")]
        [BsonIgnoreIfNull]
        public DateTime? EstimatedDeliveryDate { get; set; }

        [BsonElement("Delivery_Status")]
        public string DeliveryStatus { get; set; } = "Pending";  // enum: Pending, Processing, Shipped, Delivered, Cancelled, Failed

        [BsonElement("Payment_Status")]
        public string PaymentStatus { get; set; } = "Pending";   // enum: Pending, Paid, Failed, Refunded

        [BsonElement("Risk_Score")]
        public int RiskScore { get; set; } = 0;  // 0–100

        [BsonElement("Risk_Level")]
        public string RiskLevel { get; set; } = "Safe"; // enum: Safe, Medium, High, Critical

        [BsonElement("Fraud_Tags")]
        public List<string> FraudTags { get; set; } = new();

        [BsonElement("Is_Suspicious")]
        public bool IsSuspicious { get; set; } = false;

        [BsonElement("Meta")]
        public OrderMeta Meta { get; set; } = new();

        [BsonElement("Created_At")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("Updated_At")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class OrderMeta
    {
        [BsonElement("Device_Info")]
        public DeviceInfo Device { get; set; } = new();

        [BsonElement("Geo_Info")]
        public GeoInfo Geo { get; set; } = new();

        [BsonElement("Action_Log_Summary")]
        public ActionSummary ActionSummary { get; set; } = new();
    }

    public class DeviceInfo
    {
        public string Browser { get; set; }
        public string OS { get; set; }
        public string Device { get; set; }
    }

    public class GeoInfo
    {
        public string IP { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public bool IsVPN { get; set; }
    }

    public class ActionSummary
    {
        public int PagesVisited { get; set; }
        public int TimeOnSite { get; set; } // in seconds
        public string FunnelDropStage { get; set; } // e.g., "Checkout", "Cart"
    }
}

