using Microservice.Session.Models.DTOs;

namespace Microservice.Session.Infrastructure.Interfaces
{
    public interface IRabbitMqPublisher
    {
        void PublishSessionRiskCheck(SessionRiskCheckMessage message);
    }
}
